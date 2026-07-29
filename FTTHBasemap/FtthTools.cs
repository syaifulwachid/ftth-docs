using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FTTHBasemap.Model;

namespace FTTHBasemap
{
    public static class FtthTools
    {
        [System.Runtime.InteropServices.DllImport("accore.dll", EntryPoint = "acedEvaluateLisp", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int acedEvaluateLisp(string lispLine, out IntPtr result);

        [System.Runtime.InteropServices.DllImport("accore.dll", EntryPoint = "acutRelRb", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int acutRelRb(IntPtr rb);

        private static readonly short[] ShiftingColors = new short[] { 1, 2, 3, 4, 5, 6, 30 };
        private const string GroupDataAppName = "FTTH-GROUP_DATA";
        private static int _colorIndex = 0;

        private static void FlattenPolylineCurves(Polyline pl, Database db)
        {
            if (pl == null) return;
            
            bool hasCurves = false;
            int numVerts = pl.NumberOfVertices;
            for (int i = 0; i < numVerts; i++)
            {
                if (Math.Abs(pl.GetBulgeAt(i)) > 1e-6)
                {
                    hasCurves = true;
                    break;
                }
            }

            if (hasCurves)
            {
                if (!pl.IsWriteEnabled) pl.UpgradeOpen();
                for (int i = 0; i < numVerts; i++)
                {
                    if (Math.Abs(pl.GetBulgeAt(i)) > 1e-6)
                    {
                        pl.SetBulgeAt(i, 0.0);
                    }
                }
            }
        }

        /// <summary>
        /// Command: FTTH-GROUP. Interactive grouping of up to 16 Text/MText elements.
        /// </summary>
        public static void CreateTextGroups()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n--- FTTH TEXT GROUPING (Max 16 Texts per group) ---");
            ed.WriteMessage("\nSelect text entities one-by-one or multiple. Press ENTER to finish/group remaining.");

            // Selection filter for TEXT/MTEXT
            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            SelectionFilter sf = new SelectionFilter(filter);

            List<ObjectId> masterList = new List<ObjectId>();
            bool done = false;
            int groupIndex = GetNextGroupIndex(db);

            using (DocumentLock docLock = doc.LockDocument())
            {
                while (!done)
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions
                    {
                        MessageForAdding = $"\n[\t{masterList.Count} / 16\t] Select text to add to group (ENTER to finish): ",
                        SingleOnly = true,
                        SinglePickInSpace = true
                    };

                    PromptSelectionResult psr = ed.GetSelection(pso, sf);
                    if (psr.Status == PromptStatus.Cancel)
                    {
                        // User canceled/escaped
                        HighlightEntities(db, masterList, false);
                        done = true;
                        ed.WriteMessage("\nGrouping canceled.");
                        return;
                    }
                    else if (psr.Status == PromptStatus.OK)
                    {
                        SelectionSet ss = psr.Value;
                        foreach (SelectedObject so in ss)
                        {
                            if (!masterList.Contains(so.ObjectId))
                            {
                                masterList.Add(so.ObjectId);
                                HighlightEntity(db, so.ObjectId, true);
                            }
                        }

                        // Process groups in chunks of 16
                        while (masterList.Count >= 16)
                        {
                            List<ObjectId> chunk = masterList.GetRange(0, 16);
                            masterList.RemoveRange(0, 16);

                            ProcessSingleGroup(db, chunk, groupIndex, ShiftingColors[_colorIndex]);
                            groupIndex++;
                            _colorIndex = (_colorIndex + 1) % ShiftingColors.Length;
                        }
                    }
                    else
                    {
                        // ENTER pressed (Null response) -> group the remaining items
                        done = true;
                    }
                }

                // Process remaining items in masterList
                if (masterList.Count > 0)
                {
                    ProcessSingleGroup(db, masterList, groupIndex, ShiftingColors[_colorIndex]);
                    _colorIndex = (_colorIndex + 1) % ShiftingColors.Length;
                }
            }
            ed.Regen();
        }

        [System.Runtime.InteropServices.DllImport("accore.dll", EntryPoint = "acutEvaluateLisp", CharSet = System.Runtime.InteropServices.CharSet.Unicode, CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int acutEvaluateLisp(string lispLine, out IntPtr result);

        private static Polyline SilentBPoly(Point3d pt, Transaction tr)
        {
            // Format coordinates to LISP (bpoly (list x y z))
            string lispCmd = string.Format(System.Globalization.CultureInfo.InvariantCulture, "(bpoly (list {0} {1} {2}))", pt.X, pt.Y, pt.Z);
            IntPtr rbPtr = IntPtr.Zero;
            try
            {
                int status = acutEvaluateLisp(lispCmd, out rbPtr);
                if (status == 5100 && rbPtr != IntPtr.Zero) // 5100 is RTNORM
                {
                    using (ResultBuffer rb = DisposableWrapper.Create(typeof(ResultBuffer), rbPtr, true) as ResultBuffer)
                    {
                        if (rb != null)
                        {
                            TypedValue[] vals = rb.AsArray();
                            if (vals.Length > 0 && vals[0].TypeCode == 5006) // 5006 is RTENAME
                            {
                                ObjectId id = (ObjectId)vals[0].Value;
                                if (id.IsValid && !id.IsErased)
                                {
                                    return tr.GetObject(id, OpenMode.ForWrite) as Polyline;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to null
            }
            return null;
        }

        private static List<Curve> DilatePolyline(Polyline ib, double offsetVal, BlockTableRecord btr, Transaction tr)
        {
            double origArea = 0;
            try { origArea = ib.Area; } catch { }

            DBObjectCollection offPos = null;
            DBObjectCollection offNeg = null;

            try { offPos = ib.GetOffsetCurves(offsetVal); } catch { }
            try { offNeg = ib.GetOffsetCurves(-offsetVal); } catch { }

            double areaPos = 0;
            if (offPos != null)
            {
                foreach (DBObject obj in offPos)
                {
                    if (obj is Curve c) { try { areaPos += c.Area; } catch { } }
                }
            }

            double areaNeg = 0;
            if (offNeg != null)
            {
                foreach (DBObject obj in offNeg)
                {
                    if (obj is Curve c) { try { areaNeg += c.Area; } catch { } }
                }
            }

            DBObjectCollection chosen = null;
            DBObjectCollection discarded = null;

            if (areaPos > origArea && areaPos >= areaNeg)
            {
                chosen = offPos;
                discarded = offNeg;
            }
            else if (areaNeg > origArea)
            {
                chosen = offNeg;
                discarded = offPos;
            }
            else
            {
                // Fallback: both failed to dilate or went inward. Clean up and return clone of original.
                if (offPos != null) { foreach (DBObject obj in offPos) obj.Dispose(); }
                if (offNeg != null) { foreach (DBObject obj in offNeg) obj.Dispose(); }

                Polyline clone = ib.Clone() as Polyline;
                btr.AppendEntity(clone);
                tr.AddNewlyCreatedDBObject(clone, true);
                return new List<Curve> { clone };
            }

            // Clean up discarded
            if (discarded != null)
            {
                foreach (DBObject obj in discarded) obj.Dispose();
            }

            List<Curve> result = new List<Curve>();
            if (chosen != null)
            {
                foreach (DBObject obj in chosen)
                {
                    if (obj is Curve c)
                    {
                        btr.AppendEntity(c);
                        tr.AddNewlyCreatedDBObject(c, true);
                        result.Add(c);
                    }
                    else
                    {
                        obj.Dispose();
                    }
                }
            }
            return result;
        }

        private static void ZoomWindow(Editor ed, Point3d minPt, Point3d maxPt)
        {
            try
            {
                // Call standard ZOOM command synchronously
                ed.Command("_.ZOOM", "_W", minPt, maxPt);
            }
            catch
            {
                // Fallback to SetCurrentView if ed.Command throws or is unavailable
                using (ViewTableRecord view = ed.GetCurrentView())
                {
                    double width = Math.Abs(maxPt.X - minPt.X);
                    double height = Math.Abs(maxPt.Y - minPt.Y);
                    Point2d center = new Point2d((minPt.X + maxPt.X) / 2.0, (minPt.Y + maxPt.Y) / 2.0);

                    view.Width = width;
                    view.Height = height;
                    view.CenterPoint = center;

                    ed.SetCurrentView(view);
                }
                ed.UpdateScreen();
            }
        }

        /// <summary>
        /// Command: FTTH-BOUNDARY. Smart ODP Group boundary tool with erosion & dilation.
        /// </summary>
        public static void GenerateSmartBoundaries()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db  = doc.Database;
            Editor   ed  = doc.Editor;

            ed.WriteMessage("\nSelect texts or groups to build boundary lines for: ");
            
            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start,    "TEXT"),
                new TypedValue((int)DxfCode.Start,    "MTEXT"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            SelectionFilter sf = new SelectionFilter(filter);

            PromptSelectionResult psr = ed.GetSelection(sf);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo text selected. Canceled.");
                return;
            }

            List<ObjectId> selectedIds = new List<ObjectId>();
            foreach (SelectedObject so in psr.Value) selectedIds.Add(so.ObjectId);

            List<List<ObjectId>> batches     = new List<List<ObjectId>>();

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var allGroups = GetAllGroups(db, tr);

                    HashSet<ObjectId> processedIds = new HashSet<ObjectId>();
                    foreach (var grp in allGroups)
                    {
                        bool hit = grp.Any(id => selectedIds.Contains(id));
                        if (!hit) continue;

                        List<ObjectId> currentBatch = new List<ObjectId>();
                        foreach (ObjectId id in grp)
                        {
                            DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                            if (obj is MText || obj is DBText) { currentBatch.Add(id); processedIds.Add(id); }
                        }
                        if (currentBatch.Count > 0)
                        {
                            batches.Add(currentBatch);
                        }
                    }

                    List<ObjectId> ungroupedBatch = selectedIds.Where(id => !processedIds.Contains(id)).ToList();
                    if (ungroupedBatch.Count > 0)
                    {
                        batches.Add(ungroupedBatch);
                    }

                    batches = MergeIntersectingBatches(batches);
                    tr.Commit();
                }
            }

            GenerateSmartBoundariesCore(db, batches);
        }

        public static void GenerateBoundariesForClusters(Database db, List<FTTHBasemap.Export.RouteCableGenerator.HomeCluster> clusters)
        {
            var panel = PaletteManager.BasemapPanel;
            if (panel != null)
            {
                panel.IsolatePlacerLayers();
            }

            try
            {
                List<List<ObjectId>> batches = new List<List<ObjectId>>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead) as DBDictionary;
                    if (groupDict != null)
                    {
                        foreach (DBDictionaryEntry entry in groupDict)
                        {
                            if (entry.Key.StartsWith("FTTH-GRP_") || entry.Key.StartsWith("FTTH-MAN-GRP_") || entry.Key.StartsWith("FTTH-AUTO-GRP_"))
                            {
                                Group grp = tr.GetObject(entry.Value, OpenMode.ForRead) as Group;
                                if (grp != null && grp.NumEntities > 0)
                                {
                                    List<ObjectId> currentBatch = new List<ObjectId>();
                                    foreach (ObjectId id in grp.GetAllEntityIds())
                                    {
                                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                                        if (obj is MText || obj is DBText)
                                        {
                                            currentBatch.Add(id);
                                        }
                                    }
                                    if (currentBatch.Count > 0)
                                    {
                                        batches.Add(currentBatch);
                                    }
                                }
                            }
                        }
                    }
                    tr.Commit();
                }

                batches = MergeIntersectingBatches(batches);

                if (batches.Count > 0)
                {
                    GenerateSmartBoundariesCore(db, batches);
                }
            }
            finally
            {
                if (panel != null)
                {
                    panel.UnisolatePlacerLayers();
                }
            }
        }

        public static void GenerateSmartBoundariesCore(Database db, List<List<ObjectId>> batches)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor   ed  = doc.Editor;
            var settings = BasemapSettings.Instance;

            HashSet<ObjectId> flatAllGrouped = new HashSet<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var allGroups = GetAllGroups(db, tr);
                foreach (var grp in allGroups) foreach (ObjectId id in grp) flatAllGrouped.Add(id);
                tr.Commit();
            }

            // Temporarily hide the road label layer (Nama Jalan)
            bool wasRoadLabelOff = false;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (lt.Has(settings.LayerRoadLabel))
                {
                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[settings.LayerRoadLabel], OpenMode.ForWrite);
                    wasRoadLabelOff = ltr.IsOff;
                    ltr.IsOff = true; // Hide road labels
                }
                tr.Commit();
            }
            ed.Regen(); // Force regen so road labels are hidden before TraceBoundary is called

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (ViewTableRecord originalView = ed.GetCurrentView())
                {
                    object oldHg = Application.GetSystemVariable("HPGAPTOL");
                    object oldHpid = null;
                    try
                    {
                        oldHpid = Application.GetSystemVariable("HPISLANDDETECTION");
                        Application.SetSystemVariable("HPISLANDDETECTION", (short)2);
                    }
                    catch { }

                    Application.SetSystemVariable("HPGAPTOL", 0.5);
                    try
                    {
                        // Ensure FAT AREA layer exists
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            GetOrCreateLayer(db, tr, settings.LayerFatArea, settings.ColorFatArea);
                            tr.Commit();
                        }

                        HashSet<ObjectId> globalProcessed = new HashSet<ObjectId>();
                        int batchNum = 0;

                        foreach (var batch in batches)
                        {
                            batchNum++;
                            List<ObjectId> cleanBatch = batch.Where(id => !globalProcessed.Contains(id)).ToList();
                            if (cleanBatch.Count == 0) { continue; }

                            var batchItems = new List<(ObjectId Id, Point3d InsPt, Point3d MinPt, Point3d MaxPt)>();
                            List<Point2d> hullPts = null;

                            // ── TR-1: Read coordinates for Convex Hull ─────────────────────────
                            using (Transaction tr1 = db.TransactionManager.StartTransaction())
                            {
                                // Bounding-box corners for Convex Hull
                                List<Point3d> allCorners = new List<Point3d>();
                                foreach (ObjectId id in cleanBatch)
                                {
                                    Entity ent = tr1.GetObject(id, OpenMode.ForRead) as Entity;
                                    if (ent == null) continue;
                                    Extents3d? ext = ent.Bounds;
                                    if (!ext.HasValue)
                                    {
                                        continue;
                                    }
                                    Point3d mn = ext.Value.MinPoint, mx = ext.Value.MaxPoint;
                                    allCorners.Add(mn); allCorners.Add(mx);
                                    allCorners.Add(new Point3d(mn.X, mx.Y, 0));
                                    allCorners.Add(new Point3d(mx.X, mn.Y, 0));
                                }

                                if (allCorners.Count > 0)
                                {
                                    hullPts = ConvexHull(allCorners.Select(p => new Point2d(p.X, p.Y)).ToList());
                                }
                                tr1.Commit();
                            }

                            // ── Zoom & Crossing Polygon Selection (Outside active transaction) ──
                            if (hullPts != null && hullPts.Count > 0)
                            {
                                double minX = hullPts.Min(p => p.X), minY = hullPts.Min(p => p.Y);
                                double maxX = hullPts.Max(p => p.X), maxY = hullPts.Max(p => p.Y);

                                ZoomWindow(ed, new Point3d(minX - 20, minY - 20, 0), new Point3d(maxX + 20, maxY + 20, 0));

                                if (hullPts.Count >= 3)
                                {
                                    Point3dCollection ptsCol = new Point3dCollection();
                                    foreach (Point2d p in hullPts) ptsCol.Add(new Point3d(p.X, p.Y, 0));
                                    TypedValue[] cpF = new TypedValue[]
                                    {
                                        new TypedValue((int)DxfCode.Operator, "<OR"),
                                        new TypedValue((int)DxfCode.Start, "TEXT"),
                                        new TypedValue((int)DxfCode.Start, "MTEXT"),
                                        new TypedValue((int)DxfCode.Operator, "OR>")
                                    };
                                    var cpPsr = ed.SelectCrossingPolygon(ptsCol, new SelectionFilter(cpF));
                                    if (cpPsr.Status == PromptStatus.OK && cpPsr.Value != null)
                                    {
                                        foreach (SelectedObject so in cpPsr.Value)
                                        {
                                            ObjectId id = so.ObjectId;
                                            if (!cleanBatch.Contains(id) && !flatAllGrouped.Contains(id))
                                            { cleanBatch.Add(id); }
                                        }
                                    }
                                }
                            }

                            // ── TR-1.5: Cache insertion points and bounds for all batch items (including recruited ones) ──
                            using (Transaction tr15 = db.TransactionManager.StartTransaction())
                            {
                                double minX = double.MaxValue, minY = double.MaxValue;
                                double maxX = double.MinValue, maxY = double.MinValue;
                                bool hasBounds = false;

                                foreach (ObjectId id in cleanBatch)
                                {
                                    Entity ent = tr15.GetObject(id, OpenMode.ForRead) as Entity;
                                    if (ent == null) continue;
                                    Extents3d? ext = ent.Bounds;
                                    Point3d minP = Point3d.Origin, maxP = Point3d.Origin;
                                    if (ext.HasValue) 
                                    { 
                                        minP = ext.Value.MinPoint; 
                                        maxP = ext.Value.MaxPoint; 
                                        minX = Math.Min(minX, minP.X);
                                        minY = Math.Min(minY, minP.Y);
                                        maxX = Math.Max(maxX, maxP.X);
                                        maxY = Math.Max(maxY, maxP.Y);
                                        hasBounds = true;
                                    }
                                    Point3d ins = ent is MText mt ? mt.Location : (ent is DBText txt ? txt.Position : (ext.HasValue ? new Point3d((minP.X + maxP.X)/2.0, (minP.Y + maxP.Y)/2.0, 0) : Point3d.Origin));
                                    batchItems.Add((id, ins, minP, maxP));
                                }
                                tr15.Commit();

                                if (hasBounds)
                                {
                                    ZoomWindow(ed, new Point3d(minX - 20, minY - 20, 0), new Point3d(maxX + 20, maxY + 20, 0));
                                }
                            }

                            foreach (ObjectId id in cleanBatch) globalProcessed.Add(id);
                            if (batchItems.Count == 0) { continue; }

                            bool hidden = false;
                            List<ObjectId> bpolyIds = new List<ObjectId>();
                            try
                            {
                                // ── TR-2: Hide texts ─────────────────────────────────────────────
                                using (Transaction tr2 = db.TransactionManager.StartTransaction())
                                {
                                    foreach (var item in batchItems)
                                    {
                                        Entity ent = tr2.GetObject(item.Id, OpenMode.ForWrite) as Entity;
                                        if (ent != null) { ent.Visible = false; ent.RecordGraphicsModified(true); }
                                    }
                                    tr2.Commit();
                                }
                                hidden = true;
                                ed.UpdateScreen();

                                // Call TraceBoundary
                                foreach (var item in batchItems)
                                {
                                    Point3d centre = new Point3d(
                                        (item.MinPt.X + item.MaxPt.X) / 2.0,
                                        (item.MinPt.Y + item.MaxPt.Y) / 2.0, 0);
                                    
                                    ObjectId bpId = ObjectId.Null;
                                    try
                                    {
                                        DBObjectCollection boundaries = ed.TraceBoundary(centre, true);
                                        if (boundaries != null && boundaries.Count > 0)
                                        {
                                            using (Transaction trB = db.TransactionManager.StartTransaction())
                                            {
                                                BlockTable bt = (BlockTable)trB.GetObject(db.BlockTableId, OpenMode.ForRead);
                                                BlockTableRecord btr = (BlockTableRecord)trB.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                                                
                                                foreach (DBObject obj in boundaries)
                                                {
                                                    if (bpId == ObjectId.Null && obj is Polyline pl)
                                                    {
                                                        btr.AppendEntity(pl);
                                                        trB.AddNewlyCreatedDBObject(pl, true);
                                                        bpId = pl.ObjectId;
                                                    }
                                                    else
                                                    {
                                                        obj.Dispose();
                                                    }
                                                }
                                                trB.Commit();
                                            }
                                        }
                                    }
                                    catch (System.Exception) { }
                                    
                                    bpolyIds.Add(bpId);
                                }

                                // ── TR-3: Process (bpoly / dilate / union / join / erode) ────────
                                using (Transaction tr3 = db.TransactionManager.StartTransaction())
                                {
                                    BlockTable       bt  = (BlockTable)      tr3.GetObject(db.BlockTableId, OpenMode.ForRead);
                                    BlockTableRecord btr = (BlockTableRecord) tr3.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                                    List<Polyline> indBounds = new List<Polyline>();

                                    for (int idx = 0; idx < batchItems.Count; idx++)
                                    {
                                        var item = batchItems[idx];
                                        ObjectId bpId = bpolyIds[idx];

                                        if (bpId != ObjectId.Null)
                                        {
                                            Polyline bpPl = tr3.GetObject(bpId, OpenMode.ForWrite) as Polyline;
                                            indBounds.Add(bpPl);
                                        }
                                        else
                                        {
                                            Polyline rect = new Polyline();
                                            rect.AddVertexAt(0, new Point2d(item.MinPt.X-1, item.MinPt.Y-1), 0, 0, 0);
                                            rect.AddVertexAt(1, new Point2d(item.MaxPt.X+1, item.MinPt.Y-1), 0, 0, 0);
                                            rect.AddVertexAt(2, new Point2d(item.MaxPt.X+1, item.MaxPt.Y+1), 0, 0, 0);
                                            rect.AddVertexAt(3, new Point2d(item.MinPt.X-1, item.MaxPt.Y+1), 0, 0, 0);
                                            rect.Closed = true;
                                            btr.AppendEntity(rect); tr3.AddNewlyCreatedDBObject(rect, true);
                                            indBounds.Add(rect);
                                        }
                                    }

                                    // Dilate +8 m
                                    DBObjectCollection dilatedCurves = new DBObjectCollection();
                                    for (int di = 0; di < indBounds.Count; di++)
                                    {
                                        Polyline ib = indBounds[di];
                                        List<Curve> dils = DilatePolyline(ib, 8.0, btr, tr3);
                                        foreach (Curve dil in dils)
                                        {
                                            dilatedCurves.Add(dil);
                                        }
                                        if (!ib.IsWriteEnabled) ib.UpgradeOpen(); ib.Erase();
                                    }

                                    if (dilatedCurves.Count > 0)
                                    {
                                        DBObjectCollection regions = new DBObjectCollection();
                                        foreach (DBObject dbObj in dilatedCurves)
                                        {
                                            if (dbObj is Curve curve)
                                            {
                                                try
                                                {
                                                    var col = Region.CreateFromCurves(new DBObjectCollection { curve });
                                                    if (col != null && col.Count > 0) { foreach (DBObject ro in col) regions.Add(ro); }
                                                }
                                                catch { }
                                            }
                                        }

                                        foreach (DBObject dc in dilatedCurves)
                                        {
                                            Entity ent = dc as Entity;
                                            if (ent != null && !ent.IsErased) { if (!ent.IsWriteEnabled) ent.UpgradeOpen(); ent.Erase(); }
                                        }

                                        Region unionRegion = null;
                                        if (regions.Count > 0)
                                        {
                                            unionRegion = regions[0] as Region;
                                            for (int k = 1; k < regions.Count; k++)
                                            {
                                                if (unionRegion != null && regions[k] is Region other)
                                                {
                                                    try { unionRegion.BooleanOperation(Autodesk.AutoCAD.DatabaseServices.BooleanOperationType.BoolUnite, other); other.Dispose(); }
                                                    catch { other.Dispose(); }
                                                }
                                                else regions[k]?.Dispose();
                                            }
                                        }

                                        if (unionRegion != null)
                                        {
                                            DBObjectCollection edges = new DBObjectCollection();
                                            try { unionRegion.Explode(edges); }
                                            catch { }
                                            unionRegion.Dispose();

                                            List<Polyline> joined = JoinExplodedLines(edges, btr, tr3);
                                            Polyline outer = null; double maxA = -1;
                                            foreach (Polyline jp in joined) { if (jp.Area > maxA) { maxA = jp.Area; outer = jp; } }

                                            foreach (Polyline jp in joined)
                                                if (jp != outer) { if (!jp.IsWriteEnabled) jp.UpgradeOpen(); jp.Erase(); }

                                            if (outer != null)
                                            {
                                                Polyline eroded = ApplyAdaptiveErosion(outer, 9.0, btr, tr3, ed);

                                                if (eroded != null)
                                                {
                                                    if (!eroded.IsWriteEnabled) eroded.UpgradeOpen();
                                                    FlattenPolylineCurves(eroded, db);
                                                    eroded.Layer      = settings.LayerFatArea;
                                                    eroded.ColorIndex = 256;
                                                    WriteXData(db, tr3, eroded, cleanBatch.Count);
                                                    if (!outer.IsWriteEnabled) outer.UpgradeOpen(); outer.Erase();
                                                }
                                                else
                                                {
                                                    if (!outer.IsWriteEnabled) outer.UpgradeOpen();
                                                    FlattenPolylineCurves(outer, db);
                                                    outer.Layer = settings.LayerFatArea; outer.ColorIndex = 256;
                                                    WriteXData(db, tr3, outer, cleanBatch.Count);
                                                }
                                            }
                                        }
                                    }

                                    tr3.Commit();
                                    bpolyIds.Clear();
                                }
                            }
                            catch (System.Exception)
                            {
                                if (bpolyIds.Count > 0)
                                {
                                    using (Transaction cleanTr = db.TransactionManager.StartTransaction())
                                    {
                                        foreach (ObjectId bpId in bpolyIds)
                                        {
                                            if (bpId == ObjectId.Null) continue;
                                            try
                                            {
                                                Entity ent = cleanTr.GetObject(bpId, OpenMode.ForWrite) as Entity;
                                                if (ent != null && !ent.IsErased) ent.Erase();
                                            }
                                            catch { }
                                        }
                                        cleanTr.Commit();
                                    }
                                }
                                throw;
                            }
                            finally
                            {
                                if (hidden)
                                {
                                    using (Transaction tr4 = db.TransactionManager.StartTransaction())
                                    {
                                        foreach (var item in batchItems)
                                        {
                                            Entity ent = tr4.GetObject(item.Id, OpenMode.ForWrite) as Entity;
                                            if (ent != null) { ent.Visible = true; ent.RecordGraphicsModified(true); }
                                        }
                                        tr4.Commit();
                                    }
                                    ed.UpdateScreen();
                                }
                            }
                        }
                    }
                    finally
                    {
                        Application.SetSystemVariable("HPGAPTOL", oldHg);
                        if (oldHpid != null)
                        {
                            try { Application.SetSystemVariable("HPISLANDDETECTION", oldHpid); } catch { }
                        }
                        // Turn On road label layer (Nama Jalan)
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                            if (lt.Has(settings.LayerRoadLabel))
                            {
                                LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[settings.LayerRoadLabel], OpenMode.ForWrite);
                                ltr.IsOff = wasRoadLabelOff;
                            }
                            tr.Commit();
                        }
                        ed.SetCurrentView(originalView);
                    }
                }
            }
            ed.Regen();
        }

        /// <summary>
        /// Tessellates a Curve (Line or Arc) into a list of 2D points.
        /// Arc segments from Region.Explode must be sampled at equal intervals.
        /// </summary>
        private static List<Point2d> TessellateCurve(Curve c, double chordTol = 0.15)
        {
            List<Point2d> pts = new List<Point2d>();
            try
            {
                double startDist = c.GetDistanceAtParameter(c.StartParam);
                double endDist = c.GetDistanceAtParameter(c.EndParam);
                double length = Math.Abs(endDist - startDist);

                if (length < 0.001)
                {
                    pts.Add(new Point2d(c.StartPoint.X, c.StartPoint.Y));
                    pts.Add(new Point2d(c.EndPoint.X,   c.EndPoint.Y));
                    return pts;
                }

                int segments = Math.Max(4, (int)Math.Ceiling(length / chordTol));
                double step = length / segments;
                for (int i = 0; i <= segments; i++)
                {
                    Point3d p = c.GetPointAtDist(startDist + i * step);
                    pts.Add(new Point2d(p.X, p.Y));
                }
            }
            catch
            {
                pts.Add(new Point2d(c.StartPoint.X, c.StartPoint.Y));
                pts.Add(new Point2d(c.EndPoint.X,   c.EndPoint.Y));
            }
            return pts;
        }

        private struct PolylineVertex
        {
            public Point2d Position;
            public double Bulge;
            public PolylineVertex(Point2d pos, double bulge = 0.0)
            {
                Position = pos;
                Bulge = bulge;
            }
        }

        private static List<PolylineVertex> GetCurveVertices(Curve c, bool reverse = false)
        {
            List<PolylineVertex> verts = new List<PolylineVertex>();
            if (c is Line ln)
            {
                if (!reverse)
                {
                    verts.Add(new PolylineVertex(new Point2d(ln.StartPoint.X, ln.StartPoint.Y), 0.0));
                    verts.Add(new PolylineVertex(new Point2d(ln.EndPoint.X,   ln.EndPoint.Y), 0.0));
                }
                else
                {
                    verts.Add(new PolylineVertex(new Point2d(ln.EndPoint.X,   ln.EndPoint.Y), 0.0));
                    verts.Add(new PolylineVertex(new Point2d(ln.StartPoint.X, ln.StartPoint.Y), 0.0));
                }
            }
            else if (c is Arc arc)
            {
                double totalAngle = arc.TotalAngle;
                double bulge = Math.Tan(totalAngle / 4.0);
                if (!reverse)
                {
                    verts.Add(new PolylineVertex(new Point2d(arc.StartPoint.X, arc.StartPoint.Y), bulge));
                    verts.Add(new PolylineVertex(new Point2d(arc.EndPoint.X,   arc.EndPoint.Y), 0.0));
                }
                else
                {
                    verts.Add(new PolylineVertex(new Point2d(arc.EndPoint.X,   arc.EndPoint.Y), -bulge));
                    verts.Add(new PolylineVertex(new Point2d(arc.StartPoint.X, arc.StartPoint.Y), 0.0));
                }
            }
            else
            {
                List<Point2d> pts = TessellateCurve(c);
                if (reverse) pts.Reverse();
                foreach (Point2d p in pts)
                {
                    verts.Add(new PolylineVertex(p, 0.0));
                }
            }
            return verts;
        }

        private static List<Polyline> JoinExplodedLines(DBObjectCollection curves, BlockTableRecord btr, Transaction tr)
        {
            List<Polyline> joined = new List<Polyline>();
            List<Curve>    lines  = new List<Curve>();
            foreach (DBObject obj in curves)
            {
                if (obj is Curve c)
                {
                    lines.Add(c);
                }
                else obj.Dispose();
            }

            double tolerance = 0.1;
            while (lines.Count > 0)
            {
                Curve startLine = lines[0]; lines.RemoveAt(0);
                List<PolylineVertex> verts = GetCurveVertices(startLine, false);
                startLine.Dispose();

                bool added = true;
                while (added)
                {
                    added = false;
                    Point2d cs = verts[0].Position;
                    Point2d ce = verts[verts.Count - 1].Position;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Curve   l  = lines[i];
                        Point2d sp = new Point2d(l.StartPoint.X, l.StartPoint.Y);
                        Point2d ep = new Point2d(l.EndPoint.X,   l.EndPoint.Y);

                        List<PolylineVertex> seg = null;
                        bool appendToEnd = false;
                        bool prependToStart = false;

                        if      (sp.GetDistanceTo(ce) < tolerance) { seg = GetCurveVertices(l, false); appendToEnd    = true; }
                        else if (ep.GetDistanceTo(ce) < tolerance) { seg = GetCurveVertices(l, true);  appendToEnd    = true; }
                        else if (ep.GetDistanceTo(cs) < tolerance) { seg = GetCurveVertices(l, false); prependToStart = true; }
                        else if (sp.GetDistanceTo(cs) < tolerance) { seg = GetCurveVertices(l, true);  prependToStart = true; }

                        if (seg != null)
                        {
                            lines.RemoveAt(i); l.Dispose(); added = true;
                            if (appendToEnd)
                            {
                                PolylineVertex last = verts[verts.Count - 1];
                                last.Bulge = seg[0].Bulge;
                                verts[verts.Count - 1] = last;

                                for (int si = 1; si < seg.Count; si++) verts.Add(seg[si]);
                            }
                            else if (prependToStart)
                            {
                                PolylineVertex segLast = seg[seg.Count - 1];
                                segLast.Bulge = verts[0].Bulge;
                                seg[seg.Count - 1] = segLast;

                                for (int si = seg.Count - 2; si >= 0; si--) verts.Insert(0, seg[si]);
                            }
                            break;
                        }
                    }
                }

                if (verts.Count >= 3)
                {
                    if (verts[0].Position.GetDistanceTo(verts[verts.Count - 1].Position) < tolerance)
                    {
                        PolylineVertex last = verts[verts.Count - 2];
                        last.Bulge = verts[verts.Count - 1].Bulge;
                        verts[verts.Count - 2] = last;

                        verts.RemoveAt(verts.Count - 1);
                    }
                    Polyline pl = new Polyline();
                    for (int idx = 0; idx < verts.Count; idx++)
                    {
                        pl.AddVertexAt(idx, verts[idx].Position, verts[idx].Bulge, 0, 0);
                    }
                    pl.Closed = true;
                    btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                    joined.Add(pl);
                }
            }
            return joined;
        }

        private static Polyline ApplyAdaptiveErosion(Polyline original, double targetDist, BlockTableRecord btr, Transaction tr, Editor ed = null)
        {
            double tryDist = targetDist;
            double originalArea = original.Area;

            while (tryDist >= 1.0)
            {
                List<Polyline> erodedPosList = StepOffset(original, tryDist, 18, btr, tr);
                List<Polyline> erodedNegList = StepOffset(original, -tryDist, 18, btr, tr);

                // Filter each list to keep ONLY the single largest area polyline, and erase the others
                Polyline erodedPos = null;
                if (erodedPosList != null && erodedPosList.Count > 0)
                {
                    erodedPos = erodedPosList.OrderByDescending(p => p.Area).First();
                    foreach (Polyline p in erodedPosList)
                    {
                        if (p != erodedPos) { if (!p.IsWriteEnabled) p.UpgradeOpen(); p.Erase(); }
                    }
                }

                Polyline erodedNeg = null;
                if (erodedNegList != null && erodedNegList.Count > 0)
                {
                    erodedNeg = erodedNegList.OrderByDescending(p => p.Area).First();
                    foreach (Polyline p in erodedNegList)
                    {
                        if (p != erodedNeg) { if (!p.IsWriteEnabled) p.UpgradeOpen(); p.Erase(); }
                    }
                }

                double areaPos = (erodedPos != null && !erodedPos.IsErased) ? erodedPos.Area : 0;
                double areaNeg = (erodedNeg != null && !erodedNeg.IsErased) ? erodedNeg.Area : 0;

                Polyline bestStepEroded = null;

                if (areaPos > 0 && areaPos < originalArea && (areaNeg == 0 || areaPos <= areaNeg))
                {
                    bestStepEroded = erodedPos;
                    if (erodedNeg != null) { if (!erodedNeg.IsWriteEnabled) erodedNeg.UpgradeOpen(); erodedNeg.Erase(); }
                }
                else if (areaNeg > 0 && areaNeg < originalArea)
                {
                    bestStepEroded = erodedNeg;
                    if (erodedPos != null) { if (!erodedPos.IsWriteEnabled) erodedPos.UpgradeOpen(); erodedPos.Erase(); }
                }
                else
                {
                    if (erodedPos != null) { if (!erodedPos.IsWriteEnabled) erodedPos.UpgradeOpen(); erodedPos.Erase(); }
                    if (erodedNeg != null) { if (!erodedNeg.IsWriteEnabled) erodedNeg.UpgradeOpen(); erodedNeg.Erase(); }
                }

                if (bestStepEroded != null)
                {
                    return bestStepEroded;
                }

                // If step offset fails, try ForceHackOffset both positive and negative
                Polyline hackPos = ForceHackOffset(original, tryDist, btr, tr);
                Polyline hackNeg = ForceHackOffset(original, -tryDist, btr, tr);

                double hAreaPos = (hackPos != null && !hackPos.IsErased) ? hackPos.Area : 0;
                double hAreaNeg = (hackNeg != null && !hackNeg.IsErased) ? hackNeg.Area : 0;

                Polyline bestHackEroded = null;

                if (hAreaPos > 0 && hAreaPos < originalArea && (hAreaNeg == 0 || hAreaPos <= hAreaNeg))
                {
                    bestHackEroded = hackPos;
                    if (hackNeg != null) { if (!hackNeg.IsWriteEnabled) hackNeg.UpgradeOpen(); hackNeg.Erase(); }
                }
                else if (hAreaNeg > 0 && hAreaNeg < originalArea)
                {
                    bestHackEroded = hackNeg;
                    if (hackPos != null) { if (!hackPos.IsWriteEnabled) hackPos.UpgradeOpen(); hackPos.Erase(); }
                }
                else
                {
                    if (hackPos != null) { if (!hackPos.IsWriteEnabled) hackPos.UpgradeOpen(); hackPos.Erase(); }
                    if (hackNeg != null) { if (!hackNeg.IsWriteEnabled) hackNeg.UpgradeOpen(); hackNeg.Erase(); }
                }

                if (bestHackEroded != null)
                {
                    return bestHackEroded;
                }

                // If both fail, reduce distance and retry
                tryDist -= 1.5;
            }

            return null;
        }

        private static List<Polyline> StepOffset(Polyline pl, double dist, int steps, BlockTableRecord btr, Transaction tr)
        {
            double stepVal = dist / steps;
            List<Polyline> activePolys = new List<Polyline> { pl };
            bool activeIsClone = false;

            for (int s = 0; s < steps; s++)
            {
                List<Polyline> nextPolys = new List<Polyline>();
                try
                {
                    foreach (Polyline activePoly in activePolys)
                    {
                        DBObjectCollection offCurves = activePoly.GetOffsetCurves(stepVal);
                        if (offCurves != null)
                        {
                            foreach (DBObject dbObj in offCurves)
                            {
                                if (dbObj is Polyline p)
                                {
                                    if (p.Area > 1.0)
                                    {
                                        nextPolys.Add(p);
                                    }
                                    else
                                    {
                                        dbObj.Dispose();
                                    }
                                }
                                else
                                {
                                    dbObj.Dispose();
                                }
                            }
                        }
                    }

                    if (nextPolys.Count == 0)
                    {
                        // Clean up active clones
                        if (activeIsClone)
                        {
                            foreach (Polyline ap in activePolys)
                            {
                                if (!ap.IsWriteEnabled) ap.UpgradeOpen();
                                ap.Erase();
                            }
                        }
                        return null;
                    }

                    // Append to database
                    foreach (Polyline np in nextPolys)
                    {
                        btr.AppendEntity(np);
                        tr.AddNewlyCreatedDBObject(np, true);
                    }

                    // Erase intermediate step
                    if (activeIsClone)
                    {
                        foreach (Polyline ap in activePolys)
                        {
                            if (!ap.IsWriteEnabled) ap.UpgradeOpen();
                            ap.Erase();
                        }
                    }

                    activePolys = nextPolys;
                    activeIsClone = true;
                }
                catch
                {
                    foreach (Polyline np in nextPolys) np.Dispose();
                    if (activeIsClone)
                    {
                        foreach (Polyline ap in activePolys)
                        {
                            if (!ap.IsWriteEnabled) ap.UpgradeOpen();
                            ap.Erase();
                        }
                    }
                    return null;
                }
            }

            return activePolys;
        }

        private static Polyline ForceHackOffset(Polyline pl, double dist, BlockTableRecord btr, Transaction tr)
        {
            try
            {
                int numVerts = pl.NumberOfVertices;
                if (numVerts < 3) return null;

                // Find longest segment to cut the 10cm gap
                double maxL = -1;
                int longestI = -1;

                for (int i = 0; i < numVerts; i++)
                {
                    Point2d pa = pl.GetPoint2dAt(i);
                    Point2d pb = pl.GetPoint2dAt((i + 1) % numVerts);
                    double len = pa.GetDistanceTo(pb);
                    if (len > maxL)
                    {
                        maxL = len;
                        longestI = i;
                    }
                }

                if (maxL <= 0.5) return null;

                Point2d a = pl.GetPoint2dAt(longestI);
                Point2d b = pl.GetPoint2dAt((longestI + 1) % numVerts);

                double dx = (b.X - a.X) / maxL;
                double dy = (b.Y - a.Y) / maxL;

                // 10cm gap at the midpoint of segment (longestI -> longestI + 1)
                Point2d mid = new Point2d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                Point2d p1 = new Point2d(mid.X - dx * 0.05, mid.Y - dy * 0.05);
                Point2d p2 = new Point2d(mid.X + dx * 0.05, mid.Y + dy * 0.05);

                // Build open vertex list starting at p2, going around, ending at p1
                List<Point2d> openVerts = new List<Point2d> { p2 };
                for (int s = 1; s <= numVerts; s++)
                {
                    openVerts.Add(pl.GetPoint2dAt((longestI + s) % numVerts));
                }
                openVerts.Add(p1);

                // Create temporary open polyline
                using (Polyline openPl = new Polyline())
                {
                    openPl.Closed = false;
                    for (int i = 0; i < openVerts.Count; i++)
                    {
                        openPl.AddVertexAt(i, openVerts[i], 0, 0, 0);
                    }

                    // Offset the open polyline
                    DBObjectCollection offCurves = openPl.GetOffsetCurves(dist);
                    if (offCurves != null && offCurves.Count > 0)
                    {
                        Polyline bestOffsetPoly = null;
                        double maxArea = -1;

                        foreach (DBObject obj in offCurves)
                        {
                            if (obj is Polyline offPl)
                            {
                                // Stitch/Close it!
                                offPl.Closed = true;
                                if (offPl.Area > 1.0 && offPl.Area > maxArea)
                                {
                                    maxArea = offPl.Area;
                                    bestOffsetPoly = offPl;
                                }
                            }
                        }

                        if (bestOffsetPoly != null)
                        {
                            btr.AppendEntity(bestOffsetPoly);
                            tr.AddNewlyCreatedDBObject(bestOffsetPoly, true);

                            // Dispose others
                            foreach (DBObject obj in offCurves)
                            {
                                if (obj != bestOffsetPoly) obj.Dispose();
                            }

                            return bestOffsetPoly;
                        }

                        foreach (DBObject obj in offCurves) obj.Dispose();
                    }
                }
            }
            catch { }

            return null;
        }

        private static List<List<ObjectId>> MergeIntersectingBatches(List<List<ObjectId>> batches)
        {
            List<List<ObjectId>> merged = new List<List<ObjectId>>();
            List<List<ObjectId>> remaining = new List<List<ObjectId>>(batches);

            while (remaining.Count > 0)
            {
                List<ObjectId> b1 = remaining[0];
                remaining.RemoveAt(0);

                bool didMerge = true;
                while (didMerge)
                {
                    didMerge = false;
                    List<List<ObjectId>> nextRemaining = new List<List<ObjectId>>();

                    foreach (var b2 in remaining)
                    {
                        bool overlaps = b1.Any(id => b2.Contains(id));
                        if (overlaps)
                        {
                            // Union b2 into b1
                            foreach (ObjectId id in b2)
                            {
                                if (!b1.Contains(id)) b1.Add(id);
                            }
                            didMerge = true;
                        }
                        else
                        {
                            nextRemaining.Add(b2);
                        }
                    }

                    remaining = nextRemaining;
                }

                merged.Add(b1);
            }

            return merged;
        }

        private static List<List<ObjectId>> GetAllGroups(Database db, Transaction tr)
        {
            List<List<ObjectId>> result = new List<List<ObjectId>>();
            DBDictionary groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead) as DBDictionary;
            if (groupDict != null)
            {
                foreach (DBDictionaryEntry entry in groupDict)
                {
                    Group grp = tr.GetObject(entry.Value, OpenMode.ForRead) as Group;
                    if (grp != null && grp.NumEntities > 0)
                    {
                        result.Add(grp.GetAllEntityIds().ToList());
                    }
                }
            }
            return result;
        }

        private static int GetNextGroupIndex(Database db)
        {
            int maxIdx = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead) as DBDictionary;
                if (groupDict != null)
                {
                    foreach (DBDictionaryEntry entry in groupDict)
                    {
                        string key = entry.Key;
                        int idx = 0;
                        bool parsed = false;
                        if (key.StartsWith("FTTH-GRP_"))
                        {
                            parsed = int.TryParse(key.Substring(9), out idx);
                        }
                        else if (key.StartsWith("FTTH-MAN-GRP_"))
                        {
                            parsed = int.TryParse(key.Substring(13), out idx);
                        }
                        else if (key.StartsWith("FTTH-AUTO-GRP_"))
                        {
                            parsed = int.TryParse(key.Substring(14), out idx);
                        }

                        if (parsed && idx > maxIdx)
                        {
                            maxIdx = idx;
                        }
                    }
                }
                tr.Commit();
            }
            return maxIdx + 1;
        }

        private static void ProcessSingleGroup(Database db, List<ObjectId> ids, int groupIdx, short colorIndex)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Calculate max distance in group to print warnings if > 50m
                List<Point3d> pts = new List<Point3d>();
                foreach (ObjectId id in ids)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent != null)
                    {
                        // Set color
                        ent.ColorIndex = colorIndex;

                        if (ent is MText mt) pts.Add(mt.Location);
                        else if (ent is DBText dt) pts.Add(dt.Position);
                    }
                }

                double maxDist = 0.0;
                for (int i = 0; i < pts.Count; i++)
                {
                    for (int j = i + 1; j < pts.Count; j++)
                    {
                        double d = pts[i].DistanceTo(pts[j]);
                        if (d > maxDist) maxDist = d;
                    }
                }

                // Create the native AutoCAD group
                DBDictionary groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite) as DBDictionary;
                string groupName = $"FTTH-MAN-GRP_{groupIdx}";

                if (groupDict.Contains(groupName))
                {
                    // Erase existing
                    ObjectId existingId = groupDict.GetAt(groupName);
                    Group existingGrp = tr.GetObject(existingId, OpenMode.ForWrite) as Group;
                    existingGrp.Erase();
                }

                using (Group newGrp = new Group("FTTH Group", true))
                {
                    groupDict.SetAt(groupName, newGrp);
                    tr.AddNewlyCreatedDBObject(newGrp, true);

                    ObjectIdCollection idCol = new ObjectIdCollection(ids.ToArray());
                    newGrp.Append(idCol);
                }

                tr.Commit();

                Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage($"\n>>> Group successfully created: {groupName} ({ids.Count} ODP). Max distance: {maxDist:F2}m.");
                if (maxDist > 50.0)
                {
                    ed.WriteMessage("\n*** PERINGATAN: JARAK GRUP MELEBIHI 50 METER! ***");
                }
            }
        }

        private static void HighlightEntities(Database db, List<ObjectId> ids, bool highlight)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        if (highlight) ent.Highlight();
                        else ent.Unhighlight();
                    }
                }
                tr.Commit();
            }
        }

        private static void HighlightEntity(Database db, ObjectId id, bool highlight)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null)
                {
                    if (highlight) ent.Highlight();
                    else ent.Unhighlight();
                }
                tr.Commit();
            }
        }

        private static List<Point2d> ConvexHull(List<Point2d> pts)
        {
            if (pts.Count <= 3) return pts;

            // Sort points lexicographically
            var sorted = pts.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

            List<Point2d> lower = new List<Point2d>();
            foreach (var p in sorted)
            {
                while (lower.Count >= 2 && CrossProduct(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0.0)
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(p);
            }

            List<Point2d> upper = new List<Point2d>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                Point2d p = sorted[i];
                while (upper.Count >= 2 && CrossProduct(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0.0)
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);

            lower.AddRange(upper);
            return lower;
        }

        private static double CrossProduct(Point2d o, Point2d a, Point2d b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        private static bool IsPointInPolygon(Point2d pt, List<Point2d> poly)
        {
            int numVerts = poly.Count;
            if (numVerts < 3) return false;

            bool inside = false;
            for (int i = 0, j = numVerts - 1; i < numVerts; j = i++)
            {
                Point2d pi = poly[i];
                Point2d pj = poly[j];

                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool IsPointInPolygon(Point2d pt, Polyline pl)
        {
            int numVerts = pl.NumberOfVertices;
            if (numVerts < 3) return false;

            bool inside = false;
            for (int i = 0, j = numVerts - 1; i < numVerts; j = i++)
            {
                Point2d pi = pl.GetPoint2dAt(i);
                Point2d pj = pl.GetPoint2dAt(j);

                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Mimics LISP (bpoly pt): finds the TIGHTEST closed LWPOLYLINE in model space
        /// whose interior contains the given 2-D seed point. Returns null if none found.
        /// </summary>
        private static Polyline FindEnclosingPolyline(BlockTableRecord btr, Transaction tr, Point2d seed)
        {
            Polyline bestPoly = null;
            double   bestArea = double.MaxValue;

            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                DBObject dbObj = tr.GetObject(id, OpenMode.ForRead);
                if (dbObj is Polyline pl && pl.Closed && pl.NumberOfVertices >= 3 && !pl.IsErased)
                {
                    double area = 0;
                    try { area = pl.Area; } catch { continue; }
                    if (area <= 0) continue;

                    // Quick extents check first (fast)
                    Extents3d? ext = pl.Bounds;
                    if (ext.HasValue)
                    {
                        if (seed.X < ext.Value.MinPoint.X || seed.X > ext.Value.MaxPoint.X ||
                            seed.Y < ext.Value.MinPoint.Y || seed.Y > ext.Value.MaxPoint.Y)
                            continue;
                    }

                    if (IsPointInPolygon(seed, pl))
                    {
                        if (area < bestArea) { bestArea = area; bestPoly = pl; }
                    }
                }
            }

            return bestPoly;
        }

        private static void WriteXData(Database db, Transaction tr, Polyline pl, int count)
        {
            // Register APPID
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(GroupDataAppName))
            {
                if (!rat.IsWriteEnabled) rat.UpgradeOpen();
                using (RegAppTableRecord ratr = new RegAppTableRecord())
                {
                    ratr.Name = GroupDataAppName;
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }
            }

            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, GroupDataAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ObjectCount"),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)count)
            ))
            {
                pl.XData = rb;
            }
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                ObjectId id = lt[name];
                using (LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite))
                {
                    ltr.IsFrozen = false;
                    ltr.IsOff = false;
                }
                return id;
            }

            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
            using (LayerTableRecord ltr = new LayerTableRecord())
            {
                ltr.Name = name;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                ltr.IsFrozen = false;
                ltr.IsOff = false;
                ObjectId id = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return id;
            }
        }
    }
}
