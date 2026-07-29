using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using FTTHBasemap.Model;
using FTTHBasemap.UI;

namespace FTTHBasemap
{
    public class ExtensionApp : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage("\n[FTTH] ===============================================");
                    doc.Editor.WriteMessage("\n[FTTH] FTTH Design Planner v3.6.9 Loaded.");
                    doc.Editor.WriteMessage("\n[FTTH] Type FTTH_SHOWPANEL to display the UI Panel.");
                    doc.Editor.WriteMessage("\n[FTTH] ===============================================");
                }
            }
            catch
            {
                // Silently handle load message errors (e.g., when no document is active on startup)
            }
        }

        public void Terminate()
        {
        }
    }

    public static class Commands
    {
        private static bool CheckLicense()
        {
            if (!FTTHBasemap.Licensing.LicensingService.Instance.IsLicenseActive())
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage("\n[FTTH] ================================================================");
                    doc.Editor.WriteMessage("\n[FTTH] ERROR: Lisensi tidak aktif atau kadaluarsa!");
                    doc.Editor.WriteMessage("\n[FTTH] Silahkan ketik FTTH_SHOWPANEL dan aktifkan software via tombol 🔑.");
                    doc.Editor.WriteMessage("\n[FTTH] ================================================================");
                }
                return false;
            }
            return true;
        }

        private static void RecordTelemetryProject()
        {
            try
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await FTTHBasemap.Licensing.LicensingService.Instance.SendTelemetryAsync(1);
                });
            }
            catch { }
        }

        [CommandMethod("FTTH_SHOWPANEL")]
        public static void ShowPanel()
        {
            try
            {
                PaletteManager.ShowPanel();
                PaletteManager.Log("Panel visible. Setup parameters and select a command.");
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[FTTH] Error showing panel: {ex.Message}");
            }
        }

        [CommandMethod("FTTH_SETWIDTH")]
        public static void SetWidth()
        {
            if (!CheckLicense()) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;
            double width = settings.RoadWidth;

            ed.WriteMessage($"\n[FTTH] Target width: {width:F1} m. Please select centerline polylines...");

            // Selection filter for LWPOLYLINE
            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") };
            SelectionFilter sf = new SelectionFilter(filter);

            PromptSelectionResult psr = ed.GetSelection(sf);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[FTTH] Command canceled.");
                PaletteManager.Log("Width assignment canceled.");
                return;
            }

            SelectionSet ss = psr.Value;
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Setup layers first
                    Generator.SetupLayers(db, tr);

                    int count = 0;
                    foreach (SelectedObject so in ss)
                    {
                        Curve cl = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Curve;
                        if (cl != null)
                        {
                            cl.Layer = settings.LayerCenterline;
                            cl.ColorIndex = Generator.GetColorForWidth(width);

                            XDataManager.WriteMetadata(cl, new FTTHMetadata
                            {
                                SourceHandle = cl.Handle.ToString(),
                                Side = "CENTER",
                                RoadWidth = width,
                                OffsetDistance = 0.0,
                                ObjectType = "CENTERLINE_PENDING"
                            });
                            count++;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Assigned {width:F1}m width to {count} centerlines.");
                    PaletteManager.Log($"Assigned {width:F1}m width to {count} centerlines.");
                }
            }
        }

        [CommandMethod("FTTH_GENERATE")]
        public static void Generate()
        {
            if (!CheckLicense()) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage("\n[FTTH] Generating basemap components...");
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Setup layers
                    Generator.SetupLayers(db, tr);

                    // Scan ModelSpace for CENTERLINE_PENDING centerlines
                    List<ObjectId> pendingCenterlines = new List<ObjectId>();
                    foreach (ObjectId id in btr)
                    {
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c == null) continue;

                        if (c.Layer == settings.LayerCenterline)
                        {
                            var meta = XDataManager.ReadMetadata(c);
                            if (meta != null && meta.ObjectType == "CENTERLINE_PENDING")
                            {
                                pendingCenterlines.Add(id);
                            }
                        }
                    }

                    int count = 0;
                    if (pendingCenterlines.Count == 0)
                    {
                        ed.WriteMessage("\n[FTTH] No pending centerlines found. Running incremental offset generation from existing objects...");
                        count = Generator.ProcessIncremental(db, tr, btr, settings);
                        if (count == 0)
                        {
                            ed.WriteMessage("\n[FTTH] No new layers could be incrementally generated (no source objects found or target layers already exist).");
                            PaletteManager.Log("No pending centerlines or source curves.");
                            tr.Commit();
                            return;
                        }
                        tr.Commit();
                        RecordTelemetryProject();
                        ed.WriteMessage($"\n[FTTH] Generated {count} basemap components incrementally.");
                        PaletteManager.Log($"Incrementally generated {count} components.");
                    }
                    else
                    {
                        foreach (ObjectId clId in pendingCenterlines)
                        {
                            count += Generator.ProcessCenterline(db, tr, btr, clId, settings);
                        }
                        tr.Commit();
                        RecordTelemetryProject();
                        ed.WriteMessage($"\n[FTTH] Generated basemap for {count} centerlines.");
                        PaletteManager.Log($"Successfully generated {count} centerlines.");
                    }
                }
            }
        }

        [CommandMethod("FTTH_INTERSECT")]
        public static void IntersectClean()
        {
            if (!CheckLicense()) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage("\n[FTTH] Running intersection cleanup...");
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Intersection.CleanIntersections(db, tr, settings);
                    tr.Commit();
                }
            }
            ed.WriteMessage("\n[FTTH] Clean process complete.");
        }

        [CommandMethod("FTTH_RESET")]
        public static void Reset()
        {
            if (!CheckLicense()) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage("\n[FTTH] Resetting basemap drawing...");
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Generator.ResetDrawing(db, tr, settings);
                    tr.Commit();
                }
            }
            ed.WriteMessage("\n[FTTH] Reset complete.");
            PaletteManager.Log("Basemap reset complete.");
        }

        [CommandMethod("FTTH_CLEARPREVIEWS")]
        public static void ClearPreviews()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            var settings = BasemapSettings.Instance;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Intersection.ClearPreviews(db, tr, settings);
                    tr.Commit();
                }
            }
            PaletteManager.Log("Previews cleared.");
        }

        [CommandMethod("FTTH_FREEZE_CL")]
        public static void FreezeCenterline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            var settings = BasemapSettings.Instance;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(settings.LayerCenterline))
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[settings.LayerCenterline], OpenMode.ForWrite);
                        
                        // Set CLAYER to "0" if we are freezing the active layer
                        if (db.Clayer == ltr.ObjectId)
                        {
                            if (lt.Has("0"))
                            {
                                db.Clayer = lt["0"];
                            }
                        }

                        ltr.IsFrozen = true;
                        doc.Editor.WriteMessage($"\n[FTTH] Layer {settings.LayerCenterline} is frozen.");
                        PaletteManager.Log("Centerlines frozen.");
                    }
                    tr.Commit();
                }
            }
            doc.Editor.Regen();
        }

        [CommandMethod("FTTH_THAW_CL")]
        public static void ThawCenterline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            var settings = BasemapSettings.Instance;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(settings.LayerCenterline))
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[settings.LayerCenterline], OpenMode.ForWrite);
                        ltr.IsFrozen = false;
                        doc.Editor.WriteMessage($"\n[FTTH] Layer {settings.LayerCenterline} is thawed.");
                        PaletteManager.Log("Centerlines thawed.");
                    }
                    tr.Commit();
                }
            }
            doc.Editor.Regen();
        }

        [CommandMethod("FTTH_CREATE_POINT")]
        public static void CreatePoint()
        {
            if (!CheckLicense()) return;
            ParcelGenerator.CreatePointsInteractive();
        }

        [CommandMethod("FTTH_IMPORT_POINT")]
        public static void ImportPoint()
        {
            if (!CheckLicense()) return;
            ParcelGenerator.ImportPointsFromFile();
        }

        [CommandMethod("FTTH_IMPORT_KML")]
        public static void ImportKml()
        {
            if (!CheckLicense()) return;
            BoundaryImporter.ImportKmlBoundary();
        }

        [CommandMethod("FTTH_IMPORT_PDF")]
        public static void ImportPdf()
        {
            if (!CheckLicense()) return;
            BoundaryImporter.ImportPdfBoundary();
        }

        [CommandMethod("FTTH_GENERATE_PARCELS")]
        public static void GenerateParcels()
        {
            if (!CheckLicense()) return;
            ParcelGenerator.GenerateParcels();
            RecordTelemetryProject();
        }

        [CommandMethod("FTTH_DRAW_SMART_PLINE")]
        public static void DrawSmartPolyline()
        {
            if (!CheckLicense()) return;
            SmartSmoothPolyline.DrawSmartPolyline();
        }

        [CommandMethod("FTTH_SMOOTH_PLINE")]
        public static void SmoothPolyline()
        {
            if (!CheckLicense()) return;
            SmartSmoothPolyline.SmoothSelectedPolyline();
        }

        [CommandMethod("FTTH_RUN_PLACER")]
        public static void RunPlacer()
        {
            if (!CheckLicense()) return;
            MasterPlacer.RunPlacerBoundary();
        }

        [CommandMethod("FTTH_SELECTTEMPLATE")]
        public static void SelectTemplate()
        {
            MasterPlacer.SelectTemplate();
        }

        [CommandMethod("FTTH_LABEL_PARCELS")]
        public static void LabelParcels()
        {
            if (!CheckLicense()) return;
            MasterPlacer.LabelGeneratedParcels();
        }

        [CommandMethod("FTTH_ROADLABEL")]
        public static void RoadLabel()
        {
            if (!CheckLicense()) return;
            RoadLabelGenerator.GenerateRoadLabels();
        }

        [CommandMethod("FTTH_GROUP")]
        public static void CreateGroups()
        {
            FtthTools.CreateTextGroups();
        }

        [CommandMethod("FTTH_BOUNDARY")]
        public static void CreateBoundary()
        {
            if (!CheckLicense()) return;
            FtthTools.GenerateSmartBoundaries();
        }

        [CommandMethod("FTTH_SCALEINPLACE")]
        public static void ScaleInPlace()
        {
            ScaleTools.StartScaleInPlace();
        }

        [CommandMethod("FTTH_SCALEBASE")]
        public static void ScaleBase()
        {
            ScaleTools.RunScaleBase();
        }

        [CommandMethod("FTTH_SHOWSTREETVIEW")]
        public static void ShowStreetView()
        {
            try
            {
                PaletteManager.ShowStreetView();
                PaletteManager.Log("StreetView viewer window visible.");
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[FTTH] Error showing StreetView: {ex.Message}");
            }
        }

        [CommandMethod("FTTH_LOCATE_SV")]
        public static void LocateStreetView()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            var wpfViewer = PaletteManager.StreetViewViewer;
            if (wpfViewer == null)
            {
                ed.WriteMessage("\n[FTTH] Error: StreetView window is not open. Please open it first using FTTH_SHOWSTREETVIEW.");
                return;
            }

            while (true)
            {
                PromptPointOptions ppo = new PromptPointOptions("\nPick point in CAD model to locate in StreetView (Press Enter/ESC to exit): ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) break;

                Point3d pt = ppr.Value;
                double lat = 0.0;
                double lon = 0.0;

                try
                {
                    Point3d geoPt = TileMapManager.ProjectWcsToLonLat(db, pt);
                    lon = geoPt.X;
                    lat = geoPt.Y;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[FTTH] Projection failed: {ex.Message}");
                    break;
                }

                wpfViewer.LoadCoordinates(lat, lon);
            }
        }
        public static string ReplaceTargetPole = "NP 7 4\"";
        public static int ReplaceScope = 0; // 0 = Individual, 1 = Global, 2 = Centerline
        public static string ReplaceSourceFilter = "ALL";
        public static double ReplaceSearchRadius = 10.0;
        public static string PubSourceLayout = "";
        public static bool PubUsePrefix = true;
        public static string PubPrefix = "ABD";
        public static bool PubUseSuffix = false;
        public static string PubSuffix = "FDT";
        public static bool PubUseAlpha = false;
        public static int PubCopyCount = 5;
        public static string AlignMethod = "BLOCK";
        public static string AlignBlockOrLayer = "";
        public static string AlignIdSource = "ATTRIBUTE";
        public static string AlignIdTag = "SHEET_NO";
        public static bool CpyValEraseSource = true;

        [CommandMethod("FTTH_CPYVAL")]
        public static void RunCpyVal()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n=== COPY TEXT VALUE (CPYVAL) ===");
            ed.WriteMessage($"\nErase source mode: {(CpyValEraseSource ? "ACTIVE" : "INACTIVE")}");

            while (true)
            {
                // 1. Prompt for source text
                PromptEntityOptions peoSrc = new PromptEntityOptions("\nSelect SOURCE Text/MText (or ESC/Enter to exit): ");
                peoSrc.SetRejectMessage("\nObject must be Text or MText.");
                peoSrc.AddAllowedClass(typeof(DBText), true);
                peoSrc.AddAllowedClass(typeof(MText), true);

                PromptEntityResult perSrc = ed.GetEntity(peoSrc);
                if (perSrc.Status != PromptStatus.OK)
                    break;

                ObjectId srcId = perSrc.ObjectId;
                string srcTextValue = "";

                // Highlight source
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity srcEnt = tr.GetObject(srcId, OpenMode.ForRead) as Entity;
                    if (srcEnt is DBText t)
                        srcTextValue = t.TextString;
                    else if (srcEnt is MText mt)
                        srcTextValue = mt.Contents;

                    srcEnt.Highlight();
                    tr.Commit();
                }

                ed.WriteMessage($"\nSelected text value: \"{srcTextValue}\"");

                // 2. Loop for destination text selection
                bool completed = false;
                while (!completed)
                {
                    PromptEntityOptions peoDst = new PromptEntityOptions("\nSelect DESTINATION Text/MText (or ESC/Enter to cancel/change source): ");
                    peoDst.SetRejectMessage("\nObject must be Text or MText.");
                    peoDst.AddAllowedClass(typeof(DBText), true);
                    peoDst.AddAllowedClass(typeof(MText), true);

                    PromptEntityResult perDst = ed.GetEntity(peoDst);
                    if (perDst.Status != PromptStatus.OK)
                    {
                        // Cancel/ESC: Unhighlight source and go back to source selection
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            Entity srcEnt = tr.GetObject(srcId, OpenMode.ForRead) as Entity;
                            srcEnt.Unhighlight();
                            tr.Commit();
                        }
                        break;
                    }

                    ObjectId dstId = perDst.ObjectId;
                    if (dstId == srcId)
                    {
                        ed.WriteMessage("\nSource and destination cannot be the same object.");
                        continue;
                    }

                    using (DocumentLock docLock = doc.LockDocument())
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            Entity dstEnt = tr.GetObject(dstId, OpenMode.ForWrite) as Entity;
                            if (dstEnt is DBText t)
                            {
                                t.TextString = srcTextValue;
                            }
                            else if (dstEnt is MText mt)
                            {
                                mt.Contents = srcTextValue;
                            }

                            Entity srcEnt = tr.GetObject(srcId, OpenMode.ForWrite) as Entity;
                            srcEnt.Unhighlight();

                            if (CpyValEraseSource)
                            {
                                srcEnt.Erase();
                                ed.WriteMessage("\nSuccessfully copied and erased source.");
                            }
                            else
                            {
                                ed.WriteMessage("\nSuccessfully copied.");
                            }

                            tr.Commit();
                            completed = true;
                        }
                    }
                }
            }
        }
        [CommandMethod("FTTH_REPLACE_POLES")]
        public static void ReplacePoles()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] Error: Basemap panel is not active.");
                return;
            }

            string jsonFilename = panel.GetPoleJsonFilename(ReplaceTargetPole);
            string assetJsonPath = panel.GetAssetPath(jsonFilename);
            if (string.IsNullOrEmpty(assetJsonPath) || !File.Exists(assetJsonPath))
            {
                ed.WriteMessage($"\n[FTTH] Error: Pole asset JSON not found: {jsonFilename}");
                return;
            }

            string targetLayer = ReplaceTargetPole.StartsWith("EXT") ? "FTTH-POLE-EXISTING" : panel.GetNewPoleLayer(ReplaceTargetPole);
            short colorIndex = ReplaceTargetPole.StartsWith("EXT") ? (short)8 : panel.GetNewPoleColor(ReplaceTargetPole);

            List<ObjectId> polesToReplace = new List<ObjectId>();

            if (ReplaceScope == 0) // Individual / Window Selection
            {
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect poles to replace (or Press Enter to finish): ";
                
                // Selection filter to pick block references
                SelectionFilter filter = new SelectionFilter(new TypedValue[] {
                    new TypedValue((int)DxfCode.Start, "INSERT")
                });

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        BlockReference br = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (IsPoleBlockReference(br, tr))
                        {
                            polesToReplace.Add(so.ObjectId);
                        }
                    }
                    tr.Commit();
                }
            }
            else if (ReplaceScope == 1) // Global / Select Similar on Layer
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (IsPoleBlockReference(br, tr))
                        {
                            if (ReplaceSourceFilter == "ALL")
                            {
                                polesToReplace.Add(id);
                            }
                            else
                            {
                                // Filter by source type in XData
                                string poleType = "";
                                var rb = br.GetXDataForApplication("FTTH_POLE_DATA");
                                if (rb != null)
                                {
                                    foreach (TypedValue tv in rb)
                                    {
                                        if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                        {
                                            poleType = tv.Value?.ToString() ?? "";
                                            break;
                                        }
                                    }
                                }

                                if (poleType.Equals(ReplaceSourceFilter, StringComparison.OrdinalIgnoreCase))
                                {
                                    polesToReplace.Add(id);
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            else if (ReplaceScope == 2) // Centerline Distance Range
            {
                PromptPointOptions ppo1 = new PromptPointOptions("\nPick FIRST point on centerline: ");
                PromptPointResult ppr1 = ed.GetPoint(ppo1);
                if (ppr1.Status != PromptStatus.OK) return;

                PromptPointOptions ppo2 = new PromptPointOptions("\nPick SECOND point on centerline: ");
                PromptPointResult ppr2 = ed.GetPoint(ppo2);
                if (ppr2.Status != PromptStatus.OK) return;

                Point3d pt1 = ppr1.Value;
                Point3d pt2 = ppr2.Value;

                BasemapSettings settings = BasemapSettings.Instance;
                string centerlineLayer = settings.LayerCenterline;

                ObjectId centerlineId = ObjectId.Null;
                double bestDistanceSum = double.MaxValue;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c != null && c.Layer.Equals(centerlineLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                Point3d cp1 = c.GetClosestPointTo(pt1, false);
                                Point3d cp2 = c.GetClosestPointTo(pt2, false);
                                double d1 = pt1.DistanceTo(cp1);
                                double d2 = pt2.DistanceTo(cp2);

                                if (d1 < 5.0 && d2 < 5.0)
                                {
                                    double distSum = d1 + d2;
                                    if (distSum < bestDistanceSum)
                                    {
                                        bestDistanceSum = distSum;
                                        centerlineId = id;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (centerlineId == ObjectId.Null)
                    {
                        ed.WriteMessage("\n[FTTH] Error: No centerline found near the picked points (threshold < 5m).");
                        return;
                    }

                    Curve cl = tr.GetObject(centerlineId, OpenMode.ForRead) as Curve;
                    double t1 = cl.GetParameterAtPoint(cl.GetClosestPointTo(pt1, false));
                    double t2 = cl.GetParameterAtPoint(cl.GetClosestPointTo(pt2, false));
                    double tMin = Math.Min(t1, t2);
                    double tMax = Math.Max(t1, t2);

                    // Scan poles along centerline stretch
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (IsPoleBlockReference(br, tr))
                        {
                            try
                            {
                                Point3d closestPt = cl.GetClosestPointTo(br.Position, false);
                                double tPole = cl.GetParameterAtPoint(closestPt);

                                if (tPole >= tMin && tPole <= tMax)
                                {
                                    double dist = br.Position.DistanceTo(closestPt);
                                    if (dist <= ReplaceSearchRadius)
                                    {
                                        polesToReplace.Add(id);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    tr.Commit();
                }
            }

            if (polesToReplace.Count == 0)
            {
                ed.WriteMessage("\n[FTTH] No poles found matching selection criteria.");
                return;
            }

            ed.WriteMessage($"\n[FTTH] Replacing {polesToReplace.Count} poles with {ReplaceTargetPole} on layer {targetLayer}...");

            int replaceCount = 0;
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!rat.Has("FTTH_POLE_DATA"))
                    {
                        rat.UpgradeOpen();
                        using (var ratr = new RegAppTableRecord())
                        {
                            ratr.Name = "FTTH_POLE_DATA";
                            rat.Add(ratr);
                            tr.AddNewlyCreatedDBObject(ratr, true);
                        }
                    }

                    // Get or create target layer
                    BasemapPanel.GetOrCreateLayer(db, tr, targetLayer, colorIndex);

                    foreach (ObjectId poleId in polesToReplace)
                    {
                        BlockReference oldBr = tr.GetObject(poleId, OpenMode.ForWrite) as BlockReference;
                        if (oldBr != null)
                        {
                            Point3d pos = oldBr.Position;
                            double rot = oldBr.Rotation;

                            // Erase old pole
                            oldBr.Erase();

                            // Draw new asset
                            try
                            {
                                List<ObjectId> newIds = AssetDrawer.DrawAsset(db, tr, ms, assetJsonPath, pos, rot, 1.0, targetLayer);
                                foreach (ObjectId newId in newIds)
                                {
                                    Entity newEnt = tr.GetObject(newId, OpenMode.ForWrite) as Entity;
                                    if (newEnt is BlockReference newBr)
                                    {
                                        using (var rb = new ResultBuffer(
                                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, "FTTH_POLE_DATA"),
                                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, ReplaceTargetPole)
                                        ))
                                        {
                                            newBr.XData = rb;
                                        }
                                    }
                                }
                                replaceCount++;
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage($"\n[FTTH] Failed to draw new pole asset: {ex.Message}");
                            }
                        }
                    }

                    tr.Commit();
                }
            }

            // Sync attribute block if needed
            string syncBlockName = "";
            if (ReplaceTargetPole.Contains("NP") || ReplaceTargetPole.Contains("EP") || ReplaceTargetPole.Contains("EXT")) syncBlockName = "POLE";
            if (!string.IsNullOrEmpty(syncBlockName))
            {
                doc.SendStringToExecute($"_.ATTSYNC _Name {syncBlockName}\n", true, false, false);
            }

            ed.WriteMessage($"\n[FTTH] Successfully replaced {replaceCount} poles.");
            panel.LogMessage($"Replaced {replaceCount} poles with {ReplaceTargetPole}.");
        }

        private static bool IsPoleBlock(string blockName, string layerName)
        {
            string upperBlock = blockName.ToUpper();
            string upperLayer = layerName.ToUpper();

            if (upperBlock == "NP725" || upperBlock == "NP73" || upperBlock == "NP74" || upperBlock == "NP94" ||
                upperBlock == "EP725" || upperBlock == "EP73" || upperBlock == "EP74" || upperBlock == "EP94" ||
                upperBlock == "EXT_POLE" || upperBlock == "POLE73IN" || upperBlock == "POLE73EX" || upperBlock == "POLETEL" ||
                upperBlock == "EXT TEL" || upperBlock == "POLE")
            {
                return true;
            }

            if (upperLayer.StartsWith("FTTH-POLE-") || upperLayer == "EXT POLE" || upperLayer == "NEW POLE 7M 2.5INCH")
            {
                return true;
            }

            return false;
        }

        private static bool IsPoleBlockReference(BlockReference br, Transaction tr)
        {
            if (br == null) return false;

            var rb = br.GetXDataForApplication("FTTH_POLE_DATA");
            if (rb != null) return true;

            string blockName = br.Name;
            try
            {
                if (br.IsDynamicBlock)
                {
                    BlockTableRecord btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (btr != null) blockName = btr.Name;
                }
            }
            catch { }

            return IsPoleBlock(blockName, br.Layer);
        }

        [CommandMethod("FTTH_REGEN_HPNUM")]
        public static void RegenHpNumber()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var panel = PaletteManager.BasemapPanel;
            string labelLayer = "FTTH-NOMOR-RUMAH";
            if (!MasterPlacer.SelectedTemplateId.IsNull && !MasterPlacer.SelectedTemplateId.IsErased)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBObject obj = tr.GetObject(MasterPlacer.SelectedTemplateId, OpenMode.ForRead);
                    if (obj is Entity ent)
                    {
                        labelLayer = ent.Layer;
                    }
                    tr.Commit();
                }
            }

            // Gather all texts on the target layer
            List<ObjectId> textIds = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && (ent is MText || ent is DBText) && ent.Layer.Equals(labelLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        textIds.Add(id);
                    }
                }
                tr.Commit();
            }

            if (textIds.Count == 0)
            {
                ed.WriteMessage($"\n[FTTH] No Homepass texts found on layer: {labelLayer}");
                return;
            }

            // Fetch positions and sort them spatially (Left-to-Right, then Top-to-Bottom)
            var textData = new List<Tuple<ObjectId, Point3d>>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in textIds)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    Point3d pos = Point3d.Origin;
                    if (ent is MText mt) pos = mt.Location;
                    else if (ent is DBText dt) pos = dt.Position;

                    textData.Add(new Tuple<ObjectId, Point3d>(id, pos));
                }
                tr.Commit();
            }

            // Sort left-to-right (ascending X), then top-to-bottom (descending Y)
            var sortedTextData = textData
                .OrderBy(t => t.Item2.X)
                .ThenByDescending(t => t.Item2.Y)
                .ToList();

            // Load prefix and start index from MasterPlacer
            string prefix = MasterPlacer.TextPrefix ?? "NN-";
            int currentIndex = MasterPlacer.StartIndex;
            if (currentIndex <= 0) currentIndex = 1;

            int updatedCount = 0;
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var pair in sortedTextData)
                    {
                        Entity ent = tr.GetObject(pair.Item1, OpenMode.ForWrite) as Entity;
                        string textVal = $"{prefix}{currentIndex:D3}";

                        if (ent is MText mt)
                        {
                            mt.Contents = textVal;
                        }
                        else if (ent is DBText dt)
                        {
                            dt.TextString = textVal;
                        }

                        currentIndex++;
                        updatedCount++;
                    }
                    tr.Commit();
                }
            }

            ed.WriteMessage($"\n[FTTH] Successfully regenerated {updatedCount} Homepass numbers on layer {labelLayer}.");
            if (panel != null)
            {
                panel.LogMessage($"Regenerated {updatedCount} HP numbers starting with index {MasterPlacer.StartIndex}.");
            }
        }

        [CommandMethod("FTTH_PUB_QUICKCOPY")]
        public static void PubQuickCopy()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null) return;

            if (string.IsNullOrEmpty(PubSourceLayout))
            {
                ed.WriteMessage("\n[FTTH] Error: No source layout selected.");
                return;
            }

            // Presets
            string[] suffixPresets = { "SCHEMATIC", "MANCORE", "FDT INFO" };
            int successCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                LayoutManager lm = LayoutManager.Current;

                int totalLayouts = 0;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    totalLayouts = layoutDict.Count;
                    tr.Commit();
                }

                foreach (string suffix in suffixPresets)
                {
                    string targetName = PubUsePrefix ? $"{PubPrefix}_{suffix}" : suffix;

                    try
                    {
                        // Check if layout already exists
                        ObjectId existingId;
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                            existingId = layoutDict.Contains(targetName) ? layoutDict.GetAt(targetName) : ObjectId.Null;
                            tr.Commit();
                        }

                        if (existingId != ObjectId.Null)
                        {
                            ed.WriteMessage($"\n[FTTH] Layout '{targetName}' already exists. Skipping.");
                            continue;
                        }

                        lm.CloneLayout(PubSourceLayout, targetName, totalLayouts++);
                        successCount++;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FTTH] Error cloning layout to '{targetName}': {ex.Message}");
                    }
                }
            }

            ed.WriteMessage($"\n[FTTH] Quick Copy done: Created {successCount} layouts.");
            
            // Refresh layout list in UI panel
            try { panel.RefreshLayoutList(); } catch { }
        }

        [CommandMethod("FTTH_PUB_DUPLICATE")]
        public static void PubDuplicate()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null) return;

            if (string.IsNullOrEmpty(PubSourceLayout))
            {
                ed.WriteMessage("\n[FTTH] Error: No source layout selected.");
                return;
            }

            int successCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                LayoutManager lm = LayoutManager.Current;

                int totalLayouts = 0;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    totalLayouts = layoutDict.Count;
                    tr.Commit();
                }

                for (int i = 1; i <= PubCopyCount; i++)
                {
                    string numStr;
                    if (PubUseAlpha)
                    {
                        int baseVal = i - 1;
                        if (baseVal < 26)
                        {
                            numStr = ((char)('A' + baseVal)).ToString();
                        }
                        else
                        {
                            numStr = $"A{(char)('A' + (baseVal % 26))}";
                        }
                    }
                    else
                    {
                        numStr = i.ToString();
                    }
                    
                    // Generate new name: [Prefix] + Number + [Suffix]
                    string prefixPart = PubUsePrefix ? PubPrefix : "";
                    string suffixPart = PubUseSuffix ? PubSuffix : "";
                    string targetName = $"{prefixPart}{numStr}{suffixPart}";

                    try
                    {
                        // Check if layout already exists
                        ObjectId existingId;
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                            existingId = layoutDict.Contains(targetName) ? layoutDict.GetAt(targetName) : ObjectId.Null;
                            tr.Commit();
                        }

                        if (existingId != ObjectId.Null)
                        {
                            ed.WriteMessage($"\n[FTTH] Layout '{targetName}' already exists. Skipping.");
                            continue;
                        }

                        lm.CloneLayout(PubSourceLayout, targetName, totalLayouts++);
                        successCount++;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FTTH] Error cloning layout to '{targetName}': {ex.Message}");
                    }
                }
            }

            ed.WriteMessage($"\n[FTTH] Duplication done: Created {successCount} layouts.");

            // Refresh layout list in UI panel
            try { panel.RefreshLayoutList(); } catch { }
        }

        [CommandMethod("FTTH_PUB_PICKFRAME")]
        public static void PubPickFrame()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null) return;

            // Prompt user to select a template block or polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect a reference layout frame template in Model Space: ");
            peo.SetRejectMessage("\nMust be a block reference or polyline.");
            
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                if (ent is BlockReference br)
                {
                    string blockName = br.Name;
                    if (br.IsDynamicBlock)
                    {
                        BlockTableRecord btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        if (btr != null) blockName = btr.Name;
                    }
                    panel.SetSelectedAlignBlockOrLayer(blockName);
                    ed.WriteMessage($"\n[FTTH] Selected Frame Block: {blockName}");
                }
                else if (ent is Polyline poly)
                {
                    string layerName = poly.Layer;
                    panel.SetSelectedAlignBlockOrLayer(layerName);
                    ed.WriteMessage($"\n[FTTH] Selected Frame Layer: {layerName}");
                }
                else
                {
                    ed.WriteMessage("\n[FTTH] Entity must be a block reference or a polyline.");
                }
                tr.Commit();
            }
        }

        [CommandMethod("FTTH_PUB_ALIGNVIEWPORTS")]
        public static void PubAlignViewports()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null) return;

            if (string.IsNullOrEmpty(AlignBlockOrLayer))
            {
                ed.WriteMessage("\n[FTTH] Error: Frame identifier (Block/Layer name) not specified.");
                return;
            }

            ed.WriteMessage($"\n[FTTH] Scanning Model Space for keymap frames (Method={AlignMethod}, Filter={AlignBlockOrLayer})...");

            // Get all layout names to filter text markers
            List<string> allLayoutNames = new List<string>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layoutDict)
                {
                    if (!entry.Key.Equals("Model", StringComparison.OrdinalIgnoreCase))
                    {
                        allLayoutNames.Add(entry.Key);
                    }
                }
                tr.Commit();
            }

            // Step 1: Scan Model Space for frames
            var frames = new List<LayoutFrameInfo>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                if (AlignMethod == "BLOCK")
                {
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br != null)
                        {
                            string bName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                BlockTableRecord dynBtr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                                if (dynBtr != null) bName = dynBtr.Name;
                            }

                            if (bName.Equals(AlignBlockOrLayer, StringComparison.OrdinalIgnoreCase))
                            {
                                // Extract label from Attribute
                                string label = "";
                                if (AlignIdSource == "ATTRIBUTE")
                                {
                                    foreach (ObjectId attId in br.AttributeCollection)
                                    {
                                        AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                        if (attRef != null && attRef.Tag.Equals(AlignIdTag, StringComparison.OrdinalIgnoreCase))
                                        {
                                            label = attRef.TextString.Trim();
                                            break;
                                        }
                                    }
                                }
                                else // TEXT inside boundary
                                {
                                    Extents3d ext = br.GeometricExtents;
                                    label = FindTextLabelInsideBounds(tr, ms, ext, allLayoutNames);
                                }

                                if (!string.IsNullOrEmpty(label))
                                {
                                    Extents3d ext = br.GeometricExtents;
                                    double w = ext.MaxPoint.X - ext.MinPoint.X;
                                    double h = ext.MaxPoint.Y - ext.MinPoint.Y;
                                    Point3d center = new Point3d(ext.MinPoint.X + w / 2, ext.MinPoint.Y + h / 2, 0);

                                    frames.Add(new LayoutFrameInfo
                                    {
                                        Label = label,
                                        Center = center,
                                        Width = w,
                                        Height = h,
                                        Rotation = br.Rotation
                                    });
                                }
                            }
                        }
                    }
                }
                else // POLYLINE
                {
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        Polyline poly = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (poly != null && poly.Layer.Equals(AlignBlockOrLayer, StringComparison.OrdinalIgnoreCase) && poly.Closed)
                        {
                            Extents3d ext = poly.GeometricExtents;
                            string label = FindTextLabelInsideBounds(tr, ms, ext, allLayoutNames);

                            if (!string.IsNullOrEmpty(label))
                            {
                                double w = ext.MaxPoint.X - ext.MinPoint.X;
                                double h = ext.MaxPoint.Y - ext.MinPoint.Y;
                                Point3d center = new Point3d(ext.MinPoint.X + w / 2, ext.MinPoint.Y + h / 2, 0);

                                frames.Add(new LayoutFrameInfo
                                {
                                    Label = label,
                                    Center = center,
                                    Width = w,
                                    Height = h,
                                    Rotation = 0.0
                                });
                            }
                        }
                    }
                }

                tr.Commit();
            }

            if (frames.Count == 0)
            {
                ed.WriteMessage("\n[FTTH] No keymap frames found in Model Space.");
                return;
            }

            ed.WriteMessage($"\n[FTTH] Found {frames.Count} valid frames with labels. Matching with layout tabs...");

            // Step 2: Match frames with layouts & align viewports
            int alignedCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

                    foreach (var frame in frames)
                    {

                        // Look for layout exactly matching the frame label (case-insensitive)
                        string matchedLayoutName = "";
                        foreach (DBDictionaryEntry entry in layoutDict)
                        {
                            if (entry.Key.Equals(frame.Label, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedLayoutName = entry.Key;
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(matchedLayoutName))
                        {
                            Layout lay = tr.GetObject(layoutDict.GetAt(matchedLayoutName), OpenMode.ForRead) as Layout;
                            if (lay != null)
                            {
                                BlockTableRecord psBtr = tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead) as BlockTableRecord;
                                if (psBtr != null)
                                {
                                    Viewport customVp = null;
                                    ObjectId firstVpId = ObjectId.Null;
                                    foreach (ObjectId entId in psBtr)
                                    {
                                        Viewport vp = tr.GetObject(entId, OpenMode.ForRead) as Viewport;
                                        if (vp != null)
                                        {
                                            if (firstVpId == ObjectId.Null)
                                            {
                                                firstVpId = entId; // Skip the default Paper Space background viewport
                                            }
                                            else
                                            {
                                                customVp = vp; // Custom viewport
                                            }
                                        }
                                    }

                                    if (customVp != null)
                                    {
                                        customVp.UpgradeOpen();
                                         
                                        try
                                        {
                                            customVp.On = true;
                                        }
                                        catch { }

                                        // Unlock viewport if locked
                                        bool wasLocked = customVp.Locked;
                                        if (wasLocked)
                                        {
                                            customVp.Locked = false;
                                        }

                                        // 1. Set twist angle and view direction first
                                        customVp.ViewDirection = Vector3d.ZAxis;
                                        customVp.TwistAngle = -frame.Rotation;

                                        // 2. Set scale by matching ViewHeight
                                        customVp.ViewHeight = frame.Height;
                                        // 3. Transform WCS center to DCS using the mathematical matrix calculation method
                                        Matrix3d wcsToDcs = Matrix3d.Rotation(-customVp.TwistAngle, Vector3d.ZAxis, Point3d.Origin) *
                                                            Matrix3d.Displacement(-customVp.ViewTarget.GetAsVector()) *
                                                            Matrix3d.PlaneToWorld(customVp.ViewDirection).Inverse();
                                        Point3d dcsPt = frame.Center.TransformBy(wcsToDcs);
                                        customVp.ViewCenter = new Point2d(dcsPt.X, dcsPt.Y);
                                        
                                        // Relock if it was originally locked
                                        if (wasLocked)
                                        {
                                            customVp.Locked = true;
                                        }

                                        try
                                        {
                                            customVp.UpdateDisplay();
                                        }
                                        catch { }

                                        ed.WriteMessage($"\n[FTTH] Aligned layout '{matchedLayoutName}' viewport to Frame '{frame.Label}'.");
                                        alignedCount++;
                                    }
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }

            ed.WriteMessage($"\n[FTTH] Viewport alignment completed successfully. Aligned {alignedCount} viewports.");
            panel.LogMessage($"Aligned {alignedCount} viewports to Model Space keymaps.");
            
            // Redraw screen
            ed.UpdateScreen();
        }

        private static string FindTextLabelInsideBounds(Transaction tr, BlockTableRecord ms, Extents3d ext, List<string> allLayoutNames)
        {
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                string textVal = "";
                if (ent is DBText dt)
                {
                    if (IsPointInsideExtents(dt.Position, ext))
                    {
                        textVal = dt.TextString.Trim();
                    }
                }
                else if (ent is MText mt)
                {
                    if (IsPointInsideExtents(mt.Location, ext))
                    {
                        textVal = mt.Text.Trim();
                    }
                }

                if (!string.IsNullOrEmpty(textVal))
                {
                    foreach (string layName in allLayoutNames)
                    {
                        if (layName.Equals(textVal, StringComparison.OrdinalIgnoreCase))
                        {
                            return textVal;
                        }
                    }
                }
            }
            return "";
        }

        private static bool IsPointInsideExtents(Point3d pt, Extents3d ext)
        {
            return pt.X >= ext.MinPoint.X && pt.X <= ext.MaxPoint.X &&
                   pt.Y >= ext.MinPoint.Y && pt.Y <= ext.MaxPoint.Y;
        }

        private class LayoutFrameInfo
        {
            public string Label { get; set; }
            public Point3d Center { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Rotation { get; set; }
        }

        [CommandMethod("FTTH_DOWNLOAD_BY_POLYGON")]
        public static void DownloadByPolygon()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // Prompt user to select a closed polyline
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect closed Polyline boundary for download: ");
            peo.SetRejectMessage("\nEntity must be a Polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline poly = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;
                    if (poly == null)
                    {
                        ed.WriteMessage("\n[FTTH] Selected entity is not a Polyline.");
                        return;
                    }

                    // Extract all vertices of the polyline in WCS (Model space)
                    Point3dCollection polyPoints = new Point3dCollection();
                    for (int i = 0; i < poly.NumberOfVertices; i++)
                    {
                        polyPoints.Add(poly.GetPoint3dAt(i));
                    }

                    // Calculate Lat/Lon boundaries by projecting every vertex
                    double latMin = double.MaxValue;
                    double latMax = double.MinValue;
                    double lonMin = double.MaxValue;
                    double lonMax = double.MinValue;

                    for (int i = 0; i < polyPoints.Count; i++)
                    {
                        Point3d wcsPt = polyPoints[i];
                        try
                        {
                            Point3d gp = TileMapManager.ProjectWcsToLonLat(db, wcsPt);
                            if (gp.X < lonMin) lonMin = gp.X;
                            if (gp.X > lonMax) lonMax = gp.X;
                            if (gp.Y < latMin) latMin = gp.Y;
                            if (gp.Y > latMax) latMax = gp.Y;
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\n[FTTH] Vertex projection failed at index {i}: {ex.Message}");
                        }
                    }

                    if (lonMin == double.MaxValue)
                    {
                        ed.WriteMessage("\n[FTTH] Failed to project any polyline vertices.");
                        return;
                    }

                    // Get settings from Panel
                    string provider = "google";
                    string mapType = "satellite";
                    string resolution = "manual";
                    string colorMode = "color";
                    int manualZoom = 20;

                    var panel = PaletteManager.BasemapPanel;
                    if (panel != null)
                    {
                        panel.Dispatcher.Invoke(() =>
                        {
                            if (panel.CboMapProvider.SelectedItem is System.Windows.Controls.ComboBoxItem provItem)
                                provider = provItem.Tag?.ToString() ?? "google";
                            
                            if (panel.CboMapType.SelectedItem is System.Windows.Controls.ComboBoxItem typeItem)
                                mapType = typeItem.Tag?.ToString() ?? "satellite";
                            
                            if (panel.CboMapResolution.SelectedItem is System.Windows.Controls.ComboBoxItem resItem)
                                resolution = resItem.Tag?.ToString() ?? "manual";
                            
                            if (panel.CboColorMode.SelectedItem is System.Windows.Controls.ComboBoxItem colItem)
                                colorMode = colItem.Tag?.ToString() ?? "color";

                            if (panel.CboZoomLevel.SelectedItem is System.Windows.Controls.ComboBoxItem zoomItem &&
                                int.TryParse(zoomItem.Tag?.ToString(), out int parsedZoom))
                            {
                                manualZoom = parsedZoom;
                            }
                        });
                    }

                    int zoom = manualZoom;
                    if (resolution != "manual" && int.TryParse(resolution, out int maxTiles))
                    {
                        zoom = TileMapManager.CalculateZoomLevel(latMin, latMax, lonMin, lonMax, maxTiles);
                        // Update calculated zoom on the panel
                        if (panel != null)
                        {
                            panel.Dispatcher.Invoke(() =>
                            {
                                panel.TxtCalculatedZoom.Text = $"Auto (Zoom {zoom})";
                            });
                        }
                    }

                    // Calculate Tile bounds
                    int tileXMin = TileMapManager.LonToTileX(lonMin, zoom);
                    int tileXMax = TileMapManager.LonToTileX(lonMax, zoom);
                    int tileYMin = TileMapManager.LatToTileY(latMax, zoom);
                    int tileYMax = TileMapManager.LatToTileY(latMin, zoom);

                    int numTilesX = tileXMax - tileXMin + 1;
                    int numTilesY = tileYMax - tileYMin + 1;
                    int totalTiles = numTilesX * numTilesY;

                    ed.WriteMessage($"\n[FTTH] Selected Polyline vertices count: {polyPoints.Count}");
                    ed.WriteMessage($"\n[FTTH] Projected Bounds: Lon({lonMin:F6} to {lonMax:F6}), Lat({latMin:F6} to {latMax:F6})");
                    ed.WriteMessage($"\n[FTTH] Tiles Bounds: X({tileXMin} to {tileXMax}), Y({tileYMin} to {tileYMax})");
                    ed.WriteMessage($"\n[FTTH] Total Tiles: {numTilesX} x {numTilesY} = {totalTiles} tiles at zoom {zoom} ({provider} - {mapType})...");

                    // Find/create path to save
                    string docDir = Path.GetDirectoryName(doc.Name);
                    if (string.IsNullOrEmpty(docDir) || !Directory.Exists(docDir))
                    {
                        docDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FTTHBasemap");
                        Directory.CreateDirectory(docDir);
                    }

                    string imgName = $"{provider}_{mapType}_{zoom}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                    string imgPath = Path.Combine(docDir, imgName);

                    // Execute download using ProgressMeter and DoEvents to keep AutoCAD and WPF UI responsive
                    using (Autodesk.AutoCAD.Runtime.ProgressMeter pm = new Autodesk.AutoCAD.Runtime.ProgressMeter())
                    {
                        pm.Start($"Downloading {totalTiles} tiles");
                        pm.SetLimit(totalTiles);

                        TileMapManager.DownloadAndStitchTilesSync(
                            tileXMin, tileXMax, tileYMin, tileYMax, zoom, imgPath,
                            provider, mapType, colorMode,
                            (curr, total) =>
                            {
                                pm.MeterProgress();
                                if (panel != null)
                                {
                                    panel.Dispatcher.Invoke(() =>
                                    {
                                        panel.TxtMapStatus.Text = $"Status: Downloading {curr}/{total}...";
                                    });
                                }
                                System.Windows.Forms.Application.DoEvents();
                            }
                        );
                        
                        pm.Stop();
                    }

                    // Reset status in panel
                    if (panel != null)
                    {
                        panel.Dispatcher.Invoke(() =>
                        {
                            panel.TxtMapStatus.Text = "Status: Idle";
                        });
                    }

                    // Calculate CAD coordinates for the stitched image bounding box
                    double stitchedLonMin = TileMapManager.TileXToLon(tileXMin, zoom);
                    double stitchedLonMax = TileMapManager.TileXToLon(tileXMax + 1, zoom);
                    double stitchedLatMin = TileMapManager.TileYToLat(tileYMax + 1, zoom);
                    double stitchedLatMax = TileMapManager.TileYToLat(tileYMin, zoom);

                    Point3d ptSW, ptSE, ptNE, ptNW;

                    try
                    {
                        ptSW = TileMapManager.ProjectLonLatToWcs(db, stitchedLonMin, stitchedLatMin);
                        ptSE = TileMapManager.ProjectLonLatToWcs(db, stitchedLonMax, stitchedLatMin);
                        ptNE = TileMapManager.ProjectLonLatToWcs(db, stitchedLonMax, stitchedLatMax);
                        ptNW = TileMapManager.ProjectLonLatToWcs(db, stitchedLonMin, stitchedLatMax);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FTTH] Image positioning failed: {ex.Message}");
                        return;
                    }

                    // Insert image into model space, clipped to selected polyline
                    TileMapManager.InsertRasterImage(db, imgPath, ptSW, ptSE, ptNE, ptNW, polyPoints, numTilesX * 256, numTilesY * 256);
                    ed.WriteMessage("\n[FTTH] Map loaded and clipped to polygon successfully!");

                    tr.Commit();
                }
            }
        }

        [CommandMethod("FTTH_PLACE_DIRECT")]
        public static void PlaceDirect()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active. Please open it using FTTH_SHOWPANEL.");
                return;
            }

            PaletteManager.LogToFile("PlaceDirect command started in looping mode.");

            while (true)
            {
                PromptPointOptions ppo = new PromptPointOptions("\nPick location in CAD to place pole marker (or ESC to cancel): ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK)
                {
                    PaletteManager.LogToFile("PlaceDirect loop finished/canceled by user.");
                    break;
                }

                string poleType = "";
                List<string> checkedAccs = new List<string>();

                panel.Dispatcher.Invoke(() =>
                {
                    poleType = panel.GetSelectedPoleType();
                    if (panel.ChkSlackCable.IsChecked == true) checkedAccs.Add("SLACK CABLE");
                    if (panel.ChkFatOdp.IsChecked == true) checkedAccs.Add("FAT/ODP");
                    if (panel.ChkFdtOdc48.IsChecked == true) checkedAccs.Add("FDT/ODC 48C");
                    if (panel.ChkFdtOdc72.IsChecked == true) checkedAccs.Add("FDT/ODC 72C");
                    if (panel.ChkClosure.IsChecked == true) checkedAccs.Add("CLOSURE");
                });

                Point3d pickedPt = ppr.Value;
                Point3d targetPt = pickedPt;
                double roadAngle = 0.0;

                PaletteManager.LogToFile($"PlaceDirect picked point: X={pickedPt.X:F4}, Y={pickedPt.Y:F4}");

                double searchRadius = 20.0;
                double minDistance;
                ObjectId nearestCenterlineId = panel.FindNearestCenterlineId(db, pickedPt, searchRadius, out minDistance);
                PaletteManager.LogToFile($"Nearest centerline lookup result: ID={nearestCenterlineId.ToString()}, Distance={minDistance:F2}m");

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        if (nearestCenterlineId != ObjectId.Null)
                        {
                            Curve centerline = tr.GetObject(nearestCenterlineId, OpenMode.ForRead) as Curve;
                            if (centerline != null)
                            {
                                var meta = XDataManager.ReadMetadata(centerline);
                                double roadWidth = meta != null ? meta.RoadWidth : BasemapSettings.Instance.RoadWidth;
                                double sidewalkWidth = BasemapSettings.Instance.SidewalkWidth;
                                double D_middle = roadWidth / 2.0 + sidewalkWidth / 2.0;

                                DBObjectCollection offsetsLeft = null;
                                DBObjectCollection offsetsRight = null;
                                try { offsetsLeft = centerline.GetOffsetCurves(D_middle); } catch { }
                                try { offsetsRight = centerline.GetOffsetCurves(-D_middle); } catch { }

                                List<Curve> offsetCurves = new List<Curve>();
                                if (offsetsLeft != null)
                                {
                                    foreach (DBObject obj in offsetsLeft)
                                    {
                                        if (obj is Curve c) offsetCurves.Add(c);
                                        else obj.Dispose();
                                    }
                                }
                                if (offsetsRight != null)
                                {
                                    foreach (DBObject obj in offsetsRight)
                                    {
                                        if (obj is Curve c) offsetCurves.Add(c);
                                        else obj.Dispose();
                                    }
                                }

                                if (offsetCurves.Count > 0)
                                {
                                    Point3d bestPt = pickedPt;
                                    double bestDist = double.MaxValue;

                                    foreach (Curve offsetCurve in offsetCurves)
                                    {
                                        Point3d pt = offsetCurve.GetClosestPointTo(pickedPt, false);
                                        double d = pickedPt.DistanceTo(pt);
                                        if (d < bestDist)
                                        {
                                            bestDist = d;
                                            bestPt = pt;
                                        }
                                        offsetCurve.Dispose();
                                    }
                                    targetPt = bestPt;
                                }

                                // Calculate road angle based on tangent of centerline closest to targetPt
                                Point3d closestCenterPt = centerline.GetClosestPointTo(targetPt, false);
                                double param = centerline.GetParameterAtPoint(closestCenterPt);
                                Vector3d tangent = centerline.GetFirstDerivative(param).GetNormal();
                                roadAngle = tangent.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));
                            }
                        }

                        PaletteManager.LogToFile($"PlaceDirect snapped target point: X={targetPt.X:F4}, Y={targetPt.Y:F4}, RoadAngle={roadAngle:F4}rad");

                        string targetLayer = poleType.StartsWith("EXT") ? "FTTH-POLE-EXISTING" : panel.GetNewPoleLayer(poleType);
                        short colorIndex = poleType.StartsWith("EXT") ? (short)8 : panel.GetNewPoleColor(poleType);

                        BasemapPanel.GetOrCreateLayer(db, tr, targetLayer, colorIndex);

                        List<ObjectId> createdIds = new List<ObjectId>();
                        string assetJsonFilename = panel.GetPoleJsonFilename(poleType);
                        string assetJsonPath = panel.GetAssetPath(assetJsonFilename);
                        PaletteManager.LogToFile($"Drawing pole asset JSON: {assetJsonFilename} at {assetJsonPath}");

                        if (File.Exists(assetJsonPath))
                        {
                            var ids = AssetDrawer.DrawAsset(db, tr, modelSpace, assetJsonPath, targetPt, 0.0, 1.0, targetLayer);
                            createdIds.AddRange(ids);
                            PaletteManager.LogToFile($"Pole drawn successfully. Created {ids.Count} entities.");
                        }
                        else
                        {
                            PaletteManager.LogToFile($"Pole JSON file not found. Fallback to drawing a simple circle.");
                            using (Circle circle = new Circle())
                            {
                                circle.Center = targetPt;
                                circle.Radius = 0.5;
                                circle.Layer = targetLayer;
                                circle.ColorIndex = colorIndex;
                                modelSpace.AppendEntity(circle);
                                tr.AddNewlyCreatedDBObject(circle, true);
                                createdIds.Add(circle.ObjectId);
                            }
                        }

                        // Place accessories exactly at targetPt (overlap)
                        Vector3d zeroOffset = new Vector3d(0, 0, 0);

                        foreach (string acc in checkedAccs)
                        {
                            if (acc == "SLACK CABLE") panel.DrawAccessory(db, tr, modelSpace, "Slack Cable.JSON", targetPt, 0.0, zeroOffset, ref createdIds);
                            if (acc == "FAT/ODP") panel.DrawAccessory(db, tr, modelSpace, "FAT Symbol n Label on Pole.JSON", targetPt, 0.0, zeroOffset, ref createdIds);
                            if (acc == "FDT/ODC 48C") panel.DrawAccessory(db, tr, modelSpace, "FDT48.JSON", targetPt, 0.0, zeroOffset, ref createdIds);
                            if (acc == "FDT/ODC 72C") panel.DrawAccessory(db, tr, modelSpace, "FDT72.JSON", targetPt, 0.0, zeroOffset, ref createdIds);
                            if (acc == "CLOSURE") panel.DrawAccessory(db, tr, modelSpace, "Closure144.JSON", targetPt, 0.0, zeroOffset, ref createdIds);
                        }

                        PaletteManager.LogToFile("Accessories process done. Checked accessories count: " + checkedAccs.Count);

                        tr.Commit();

                        if (createdIds.Count > 0)
                        {
                            panel.Dispatcher.Invoke(() =>
                            {
                                panel.AddPlacedStep(createdIds);
                                panel.TxtLastProjected.Text = $"{targetPt.X:F2}, {targetPt.Y:F2}";
                                panel.LogMessage($"Placed {poleType} (Direct CAD) at {targetPt.X:F2}, {targetPt.Y:F2}");
                                PaletteManager.LogToFile($"Placement step recorded. Pole & accessories added successfully.");
                            });
                        }
                    }
                }

                // If FAT or FDT is placed, call ATTSYNC synchronously
                List<string> syncBlocks = new List<string>();
                if (checkedAccs.Contains("FAT/ODP")) syncBlocks.Add("FAT");
                if (checkedAccs.Contains("FDT/ODC 48C")) syncBlocks.Add("FDT48");
                if (checkedAccs.Contains("FDT/ODC 72C")) syncBlocks.Add("FDT72");

                foreach (string bName in syncBlocks)
                {
                    try
                    {
                        ed.Command("_.ATTSYNC", "_Name", bName);
                    }
                    catch (System.Exception ex)
                    {
                        PaletteManager.LogToFile($"Error running ATTSYNC for {bName}: {ex.Message}");
                    }
                }

                doc.Editor.UpdateScreen();
            }
        }

        [CommandMethod("FTTH_TAG_HOMPASS")]
        public static void TagHompassCommand()
        {
            HompassTypeManager.ProcessSelection(
                HompassTypeManager.ActiveLabel,
                HompassTypeManager.ActiveColor,
                HompassTypeManager.ActiveLayer
            );
        }

        [CommandMethod("FTTH_EXTRACT_TK")]
        public static void ExtractTkCommand()
        {
            HompassTypeManager.ProcessExtractTK();
        }

        [CommandMethod("RESTORENAMAHP")]
        public static void RestoreNamaHpCommand()
        {
            HompassTypeManager.RestoreNamaHp();
        }

        [CommandMethod("FTTH_GEN_POLE_LABEL")]
        public static void GenPoleLabelCommand()
        {
            LabelingManager.GeneratePoleLabels();
        }

        [CommandMethod("FTTH_GEN_FAT_LABEL")]
        public static void GenFatLabelCommand()
        {
            LabelingManager.GenerateFatLabels();
            LabelingManager.GenerateFdtLabels();
        }

        [CommandMethod("FTTH_GEN_FAT_TABLE")]
        public static void GenFatTableCommand()
        {
            LabelingManager.GenerateFatTables();
        }

        [CommandMethod("FTTH_GROUP_FAT_TABLES")]
        public static void GroupFatTablesCommand()
        {
            LabelingManager.GroupAllFatTables();
        }

        [CommandMethod("FTTH_UNGROUP_FAT_TABLES")]
        public static void UngroupFatTablesCommand()
        {
            LabelingManager.UngroupAllFatTables();
        }

        [CommandMethod("FTTH_GEN_FDT_LABEL")]
        public static void GenFdtLabelCommand()
        {
            LabelingManager.GenerateFdtLabels();
            LabelingManager.GenerateFatLabels();
        }

        [CommandMethod("FTTH_GEN_FDT_TABLE")]
        public static void GenFdtTableCommand()
        {
            LabelingManager.GenerateFdtTables();
        }

        [CommandMethod("FTTH_GEN_CABLE_LABEL")]
        public static void GenCableLabelCommand()
        {
            LabelingManager.GenerateCableLabels();
        }

        [CommandMethod("FTTH_GEN_SPAN_LABELS")]
        public static void GenSpanLabelsCommand()
        {
            LabelingManager.GenerateSpanLabels();
        }

        [CommandMethod("FTTH_GEN_SUMMARY")]
        public static void GenSummaryCommand()
        {
            SummaryManager.GenerateSummaryTable();
        }

        [CommandMethod("FTTH_AUTO_GENERATE_POLES")]
        public static void AutoGeneratePolesCommand()
        {
            if (!CheckLicense()) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage("\n[FTTH] Auto-Generate Poles: Interval={0}m, AvoidRadius={1}m, StartOffset={2}m, Side={3}",
                settings.AutoPoleInterval, settings.AutoPoleAvoidDist, settings.AutoPoleStartOffset, settings.AutoPoleSide);

            // Selection filter for centerlines (LINE, LWPOLYLINE, POLYLINE) on LayerCenterline
            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<AND"),
                new TypedValue((int)DxfCode.LayerName, settings.LayerCenterline),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>"),
                new TypedValue((int)DxfCode.Operator, "AND>")
            };
            SelectionFilter sf = new SelectionFilter(filter);

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect road centerline lines/polylines to place poles (or press ENTER to process all centerlines): ";
            pso.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult psr = ed.GetSelection(pso, sf);
            List<ObjectId> targetCurves = new List<ObjectId>();

            if (psr.Status == PromptStatus.OK)
            {
                SelectionSet ss = psr.Value;
                foreach (SelectedObject so in ss)
                {
                    targetCurves.Add(so.ObjectId);
                }
            }
            else if (psr.Status == PromptStatus.Error) // User pressed Enter without selection
            {
                // Retrieve all curves on LayerCenterline automatically
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c != null && c.Layer.Equals(settings.LayerCenterline, StringComparison.OrdinalIgnoreCase))
                        {
                            targetCurves.Add(id);
                        }
                    }
                    tr.Commit();
                }
            }
            else
            {
                ed.WriteMessage("\nSelection canceled.");
                return;
            }

            if (targetCurves.Count == 0)
            {
                ed.WriteMessage("\nNo road centerline polylines found or selected.");
                return;
            }

            // Run generation logic
            AssetPlacer.GeneratePoles(db, targetCurves, settings);
        }

        [CommandMethod("FTTH_AUTO_BOUNDARY")]
        public static void AutoBoundaryCommand()
        {
            if (!CheckLicense()) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            FtthTools.GenerateBoundariesForClusters(doc.Database, null);
        }

        [CommandMethod("FTTH_DRAW_SLINGWIRE")]
        public static void DrawSlingWire()
        {
            if (!CheckLicense()) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    string layerName = "WIRE CABLE";
                    
                    LayerTableRecord ltr;
                    if (!lt.Has(layerName))
                    {
                        lt.UpgradeOpen();
                        ltr = new LayerTableRecord();
                        ltr.Name = layerName;
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByColor, 4); // 4 = Cyan
                        ltr.LineWeight = LineWeight.LineWeight030; // 0.30 mm
                        
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                    else
                    {
                        ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByColor, 4);
                        ltr.LineWeight = LineWeight.LineWeight030;
                    }

                    db.Clayer = ltr.ObjectId;
                    tr.Commit();
                }

                ed.WriteMessage("\nMulai menggambar Sling Wire di layer WIRE CABLE...");
                ed.WriteMessage("\nKetik/klik koordinat untuk polyline...");

                try
                {
                    Application.SetSystemVariable("PLINEWIDTH", 0.30);
                }
                catch (System.Exception exVar)
                {
                    ed.WriteMessage("\nWarning setting PLINEWIDTH: " + exVar.Message);
                }

                try
                {
                    Application.SetSystemVariable("PLINEGEN", (short)1);
                }
                catch (System.Exception exVar)
                {
                    ed.WriteMessage("\nWarning setting PLINEGEN: " + exVar.Message);
                }

                doc.SendStringToExecute("._PLINE ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nError drawing sling wire: " + ex.Message);
            }
        }

        [CommandMethod("FTTH_PICK_FDT_ORDER")]
        public static void PickFdtOrder()
        {
            if (!CheckLicense()) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            ed.WriteMessage("\n--- ATUR URUTAN FDT SECARA MANUAL ---");
            var handles = new List<string>();
            var names = new List<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = 1;
                while (true)
                {
                    PromptEntityOptions peo = new PromptEntityOptions($"\nSilakan klik FDT ke-{count} (atau tekan ENTER untuk selesai): ");
                    peo.SetRejectMessage("\nObjek yang dipilih harus berupa block FDT.");
                    peo.AddAllowedClass(typeof(BlockReference), true);

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                    {
                        break;
                    }

                    ObjectId fdtId = per.ObjectId;
                    BlockReference br = tr.GetObject(fdtId, OpenMode.ForRead) as BlockReference;
                    if (br != null)
                    {
                        string blockName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            try
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                blockName = btr.Name;
                            }
                            catch { }
                        }

                        // Check if it is an FDT block
                        if (blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string fdtName = "FDT";
                            // Read name attribute
                            if (br.AttributeCollection.Count > 0)
                            {
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    if (attRef != null && (attRef.Tag.Equals("NAME", StringComparison.OrdinalIgnoreCase) || 
                                                           attRef.Tag.Equals("FDT_NAME", StringComparison.OrdinalIgnoreCase) || 
                                                           attRef.Tag.Equals("FDT NAME", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        fdtName = attRef.TextString;
                                        break;
                                    }
                                }
                            }

                            string handleStr = br.Handle.ToString();
                            if (handles.Contains(handleStr))
                            {
                                ed.WriteMessage($"\n[FTTH] Warning: FDT {fdtName} sudah dimasukkan dalam urutan.");
                            }
                            else
                            {
                                handles.Add(handleStr);
                                names.Add(fdtName);
                                ed.WriteMessage($"\n[FTTH] FDT {count}: {fdtName} ditambahkan ke urutan.");
                                count++;
                            }
                        }
                        else
                        {
                            ed.WriteMessage("\n[FTTH] Objek yang dipilih bukan merupakan block FDT (nama block atau layer harus mengandung kata 'FDT').");
                        }
                    }
                }
                tr.Commit();
            }

            if (handles.Count > 0)
            {
                LabelingManager.ManualFdtOrderHandles = handles;
                ed.WriteMessage($"\n[FTTH] Urutan FDT manual berhasil disimpan: {string.Join(" -> ", names)}");
                
                // Update UI state label
                if (PaletteManager.BasemapPanel != null)
                {
                    PaletteManager.BasemapPanel.Dispatcher.Invoke(() =>
                    {
                        PaletteManager.BasemapPanel.UpdateFdtOrderLabel($"Manual ({handles.Count} FDTs)");
                    });
                }
            }
            else
            {
                ed.WriteMessage("\n[FTTH] Pengaturan urutan FDT manual dibatalkan.");
            }
        }

        [CommandMethod("FTTH_DRAW_HELPER_ARROW")]
        public static void DrawHelperArrow()
        {
            if (!CheckLicense()) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptPointOptions ppo1 = new PromptPointOptions("\nPilih titik awal panah (dekat Blok FAT): ");
                PromptPointResult ppr1 = ed.GetPoint(ppo1);
                if (ppr1.Status != PromptStatus.OK) return;

                PromptPointOptions ppo2 = new PromptPointOptions("\nPilih titik akhir panah (di dalam Area/Boundary): ");
                ppo2.UseBasePoint = true;
                ppo2.BasePoint = ppr1.Value;
                PromptPointResult ppr2 = ed.GetPoint(ppo2);
                if (ppr2.Status != PromptStatus.OK) return;

                Point3d startPt = ppr1.Value;
                Point3d endPt = ppr2.Value;

                double dist = startPt.DistanceTo(endPt);
                if (dist < 0.1)
                {
                    ed.WriteMessage("\nJarak terlalu dekat!");
                    return;
                }

                // 1. Calculate parameters from reference ArrowDot.lsp
                double ang = Math.Atan2(endPt.Y - startPt.Y, endPt.X - startPt.X);

                double dotSize = 1.0;
                double arrowSize = 2.5;
                double arrowLen = 4.0;

                // Scale down arrow parts dynamically for short lengths
                if (dist < 5.0)
                {
                    arrowLen = dist * 0.5;
                    arrowSize = dist * 0.3;
                    dotSize = dist * 0.15;
                }

                // Calculate p1b (polar p1 ang 0.01)
                Point3d p1b = new Point3d(
                    startPt.X + 0.01 * Math.Cos(ang),
                    startPt.Y + 0.01 * Math.Sin(ang),
                    0.0
                );

                // Calculate p2b (polar p2 (ang + pi) arrowLen)
                Point3d p2b = new Point3d(
                    endPt.X + arrowLen * Math.Cos(ang + Math.PI),
                    endPt.Y + arrowLen * Math.Sin(ang + Math.PI),
                    0.0
                );

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    string layerName = "FTTH-HELPER-ARROW-FAT";
                    
                    LayerTableRecord ltr;
                    if (!lt.Has(layerName))
                    {
                        lt.UpgradeOpen();
                        ltr = new LayerTableRecord();
                        ltr.Name = layerName;
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByColor, 7); // 7 = White
                        ltr.LineWeight = LineWeight.LineWeight030; // 0.30 mm
                        
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                    else
                    {
                        ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                        ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByColor, 7);
                        ltr.LineWeight = LineWeight.LineWeight030;
                    }

                    db.Clayer = ltr.ObjectId;

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Polyline poly = new Polyline();
                    poly.SetDatabaseDefaults();
                    poly.Layer = layerName;

                    // Add vertices matching LISP logic
                    poly.AddVertexAt(0, new Point2d(startPt.X, startPt.Y), 0.0, dotSize, dotSize);
                    poly.AddVertexAt(1, new Point2d(p1b.X, p1b.Y), 0.0, 0.0, 0.0);
                    poly.AddVertexAt(2, new Point2d(p2b.X, p2b.Y), 0.0, arrowSize, 0.0);
                    poly.AddVertexAt(3, new Point2d(endPt.X, endPt.Y), 0.0, 0.0, 0.0);

                    btr.AppendEntity(poly);
                    tr.AddNewlyCreatedDBObject(poly, true);

                    tr.Commit();
                }

                ed.WriteMessage("\nSukses membuat Panah Bantu FAT pada layer FTTH-HELPER-ARROW-FAT (Putih).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nError drawing helper arrow: " + ex.Message);
            }
        }
    }
}
