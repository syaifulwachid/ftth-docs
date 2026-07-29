using System;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using FTTHBasemap.UI;
using FTTHBasemap.Model;

namespace FTTHBasemap
{
    public static class AssetPlacer
    {
        public static string ActiveAssetFile { get; set; }
        public static string ActiveAssetLayer { get; set; }
        public static short ActiveAssetColor { get; set; } = 256; // ByLayer
        public static string ActiveAssetName { get; set; }

        [CommandMethod("FTTH_PLACE_MANUAL_ASSET")]
        public static void PlaceManualAsset()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (string.IsNullOrEmpty(ActiveAssetFile))
            {
                ed.WriteMessage("\n[FTTH] Error: No active asset selected for manual placement.");
                return;
            }

            var panel = PaletteManager.BasemapPanel;
            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] Error: FTTH basemap panel is not open.");
                return;
            }

            string assetPath = panel.GetAssetPath(ActiveAssetFile);
            if (!File.Exists(assetPath))
            {
                ed.WriteMessage($"\n[FTTH] Error: Asset file not found: {assetPath}");
                return;
            }

            ed.WriteMessage($"\n[FTTH] Manual Placement: placing '{ActiveAssetName}'...");
            ed.WriteMessage("\n[FTTH] Pick point in drawing to place. Press ESC or Enter when done.");

            while (true)
            {
                PromptPointOptions ppo = new PromptPointOptions($"\nPick insertion point for {ActiveAssetName}: ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK)
                {
                    break;
                }

                Point3d targetPt = ppr.Value;

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        string targetLayer = ActiveAssetLayer;
                        short colorIndex = ActiveAssetColor;

                        // Create layer if targetLayer is specified and doesn't exist
                        if (!string.IsNullOrEmpty(targetLayer))
                        {
                            BasemapPanel.GetOrCreateLayer(db, tr, targetLayer, colorIndex);
                        }

                        // Place asset using AssetDrawer
                        List<ObjectId> createdIds = null;
                        try
                        {
                            createdIds = AssetDrawer.DrawAsset(db, tr, modelSpace, assetPath, targetPt, 0.0, 1.0, targetLayer);
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\n[FTTH] Error placing asset: {ex.Message}");
                            tr.Abort();
                            continue;
                        }

                        tr.Commit();

                        if (createdIds != null && createdIds.Count > 0)
                        {
                            panel.Dispatcher.Invoke(() =>
                            {
                                panel.AddPlacedStep(createdIds);
                                panel.LogMessage($"Placed {ActiveAssetName} manually at {targetPt.X:F2}, {targetPt.Y:F2}");
                            });
                        }
                    }
                }

                ed.UpdateScreen();
            }

            // Sync attributes if placing a block with attribute definitions (like FAT or FDT)
            if (ActiveAssetName.Contains("FAT") || ActiveAssetName.Contains("FDT"))
            {
                string syncBlockName = "";
                if (ActiveAssetName.Contains("FAT Symbol") || ActiveAssetName.Contains("FAT")) syncBlockName = "FAT";
                else if (ActiveAssetName.Contains("FDT48")) syncBlockName = "FDT48";
                else if (ActiveAssetName.Contains("FDT72")) syncBlockName = "FDT72";

                if (!string.IsNullOrEmpty(syncBlockName))
                {
                    doc.SendStringToExecute($"_.ATTSYNC _Name {syncBlockName}\n", true, false, false);
                }
            }

            ed.WriteMessage("\n[FTTH] Manual placement finished.");
        }

        public static void GeneratePoles(Database db, List<ObjectId> centerlineIds, BasemapSettings settings)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            int placedCount = 0;
            int skippedCount = 0;

            // 1. Gather coordinates of existing poles
            List<Point3d> existingPolePts = new List<Point3d>();

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        bool isPole = false;
                        if (ent.Layer != null && ent.Layer.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isPole = true;
                        }
                        else
                        {
                            try
                            {
                                var rb = ent.GetXDataForApplication("FTTH_POLE_DATA");
                                if (rb != null)
                                {
                                    isPole = true;
                                    rb.Dispose();
                                }
                            }
                            catch { }
                        }

                        if (isPole)
                        {
                            if (ent is BlockReference br)
                            {
                                existingPolePts.Add(br.Position);
                            }
                            else if (ent is Circle c)
                            {
                                existingPolePts.Add(c.Center);
                            }
                            else
                            {
                                try
                                {
                                    Extents3d ext = ent.Bounds.Value;
                                    Point3d center = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5;
                                    existingPolePts.Add(center);
                                }
                                catch { }
                            }
                        }
                    }

                    tr.Commit();
                }
            }

            ed.WriteMessage($"\n[FTTH] Scanned {existingPolePts.Count} existing poles in drawing.");

            // 2. Prepare parameters
            string poleType = settings.AutoPoleType;
            double interval = settings.AutoPoleInterval;
            double avoidDist = settings.AutoPoleAvoidDist;
            double startOffset = settings.AutoPoleStartOffset;
            string side = settings.AutoPoleSide; // "LEFT" or "RIGHT"

            var panel = PaletteManager.BasemapPanel;
            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] Error: Basemap panel not active.");
                return;
            }

            string assetJsonFilename = panel.GetPoleJsonFilename(poleType);
            string assetJsonPath = panel.GetAssetPath(assetJsonFilename);
            string targetLayer = poleType.StartsWith("EXT") ? "FTTH-POLE-EXISTING" : panel.GetNewPoleLayer(poleType);
            short colorIndex = poleType.StartsWith("EXT") ? (short)8 : panel.GetNewPoleColor(poleType);

            bool jsonExists = File.Exists(assetJsonPath);
            if (!jsonExists)
            {
                ed.WriteMessage($"\n[FTTH] Warning: Pole asset JSON not found at: {assetJsonPath}. Simple circles will be generated.");
            }

            List<ObjectId> allCreatedIds = new List<ObjectId>();

            // 3. Generate poles along each centerline
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!rat.Has("FTTH_POLE_DATA"))
                    {
                        if (!rat.IsWriteEnabled) rat.UpgradeOpen();
                        using (RegAppTableRecord ratr = new RegAppTableRecord())
                        {
                            ratr.Name = "FTTH_POLE_DATA";
                            rat.Add(ratr);
                            tr.AddNewlyCreatedDBObject(ratr, true);
                        }
                    }

                    // Create the target layer if it doesn't exist
                    BasemapPanel.GetOrCreateLayer(db, tr, targetLayer, colorIndex);

                    foreach (ObjectId id in centerlineIds)
                    {
                        Curve centerline = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (centerline == null) continue;

                        double len = centerline.GetDistanceAtParameter(centerline.EndParam);
                        if (len <= startOffset) continue;

                        // Get metadata for road width
                        var meta = XDataManager.ReadMetadata(centerline);
                        double roadWidth = meta != null ? meta.RoadWidth : settings.RoadWidth;
                        double sidewalkWidth = settings.SidewalkWidth;

                        // Compute side offset: middle of sidewalk
                        double dMiddle = roadWidth / 2.0 + sidewalkWidth / 2.0;

                        double currentDist = startOffset;
                        while (currentDist <= len)
                        {
                            Point3d centerlinePt = centerline.GetPointAtDist(currentDist);
                            double param = centerline.GetParameterAtPoint(centerlinePt);
                            Vector3d tangent = centerline.GetFirstDerivative(param).GetNormal();

                            // Perpendicular offset vector depending on Left/Right side
                            Vector3d perp;
                            if (side.Equals("RIGHT", StringComparison.OrdinalIgnoreCase))
                            {
                                perp = new Vector3d(tangent.Y, -tangent.X, 0).GetNormal();
                            }
                            else
                            {
                                perp = new Vector3d(-tangent.Y, tangent.X, 0).GetNormal();
                            }

                            Point3d targetPt = centerlinePt + perp * dMiddle;

                            // Check collision with existing poles
                            bool tooClose = false;
                            foreach (Point3d extPt in existingPolePts)
                            {
                                if (targetPt.DistanceTo(extPt) < avoidDist)
                                {
                                    tooClose = true;
                                    break;
                                }
                            }

                            if (tooClose)
                            {
                                skippedCount++;
                                currentDist += interval;
                                continue;
                            }

                            double roadAngle = tangent.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));

                            List<ObjectId> createdIds = null;
                            if (jsonExists)
                            {
                                try
                                {
                                    createdIds = AssetDrawer.DrawAsset(db, tr, modelSpace, assetJsonPath, targetPt, 0.0, 1.0, targetLayer);
                                }
                                catch (System.Exception ex)
                                {
                                    ed.WriteMessage($"\n[FTTH] Error drawing JSON block: {ex.Message}");
                                }
                            }

                            if (createdIds == null || createdIds.Count == 0)
                            {
                                using (Circle circle = new Circle())
                                {
                                    circle.Center = targetPt;
                                    circle.Radius = 0.5;
                                    circle.Layer = targetLayer;
                                    circle.ColorIndex = colorIndex;
                                    modelSpace.AppendEntity(circle);
                                    tr.AddNewlyCreatedDBObject(circle, true);
                                    createdIds = new List<ObjectId> { circle.ObjectId };
                                }
                            }

                            if (createdIds != null && createdIds.Count > 0)
                            {
                                allCreatedIds.AddRange(createdIds);

                                Entity mainEnt = tr.GetObject(createdIds[0], OpenMode.ForWrite) as Entity;
                                if (mainEnt != null)
                                {
                                    using (ResultBuffer rb = new ResultBuffer(
                                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, "FTTH_POLE_DATA"),
                                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, poleType),
                                        new TypedValue((int)DxfCode.ExtendedDataReal, 0.0), // camLat
                                        new TypedValue((int)DxfCode.ExtendedDataReal, 0.0), // camLon
                                        new TypedValue((int)DxfCode.ExtendedDataReal, roadAngle), // heading
                                        new TypedValue((int)DxfCode.ExtendedDataReal, dMiddle), // side offset
                                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, "") // accessories
                                    ))
                                    {
                                        mainEnt.XData = rb;
                                    }
                                }

                                placedCount++;
                                existingPolePts.Add(targetPt);
                            }

                            currentDist += interval;
                        }
                    }

                    tr.Commit();
                }
            }

            if (placedCount > 0)
            {
                string syncBlockName = "";
                if (poleType.Contains("NP") || poleType.Contains("EP") || poleType.Contains("EXT")) syncBlockName = "POLE";
                if (!string.IsNullOrEmpty(syncBlockName))
                {
                    doc.SendStringToExecute($"_.ATTSYNC _Name {syncBlockName}\n", true, false, false);
                }
            }

            if (panel != null && allCreatedIds.Count > 0)
            {
                panel.Dispatcher.Invoke(() =>
                {
                    panel.AddPlacedStep(allCreatedIds);
                    panel.LogMessage($"Auto-Generated {placedCount} poles. Skipped {skippedCount} locations.");
                });
            }

            ed.WriteMessage($"\n[FTTH] Auto-placement complete: Placed={placedCount}, Skipped={skippedCount}.");
            ed.UpdateScreen();
        }
    }
}
