using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FTTHBasemap.Model;

namespace FTTHBasemap
{
    public static class MasterPlacer
    {
        public static ObjectId SelectedTemplateId { get; set; } = ObjectId.Null;
        public static string TextPrefix { get; set; } = "NN-";
        public static int StartIndex { get; set; } = 1;

        /// <summary>
        /// Select an MText or Text to act as a styling template.
        /// </summary>
        public static void SelectTemplate()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect Text or MText as Template: ");
            peo.SetRejectMessage("\nMust be a Text or MText entity.");
            peo.AddAllowedClass(typeof(MText), true);
            peo.AddAllowedClass(typeof(DBText), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.OK)
            {
                SelectedTemplateId = per.ObjectId;
                ed.WriteMessage($"\nTemplate selected successfully (ID: {SelectedTemplateId.Handle}).");
                PaletteManager.Log("Template text selected.");
            }
            else
            {
                ed.WriteMessage("\nTemplate selection canceled.");
            }
        }

        /// <summary>
        /// Mode A: Interactive boundary placement using selected lines.
        /// </summary>
        public static void RunPlacerBoundary()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage($"\n[FTTH] Master Placer: Prefix='{TextPrefix}', StartIndex={StartIndex}");
            ed.WriteMessage("\nSelect boundary lines to detect regions...");

            // Selection filter for LINE and LWPOLYLINE
            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "POLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            SelectionFilter sf = new SelectionFilter(filter);

            PromptSelectionResult psr = ed.GetSelection(sf);
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo lines selected. Canceled.");
                return;
            }

            int currentCounter = StartIndex;
            int placedCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Gather target layer for labels
                    string labelLayer = "0";
                    MText templateMText = null;
                    DBText templateDBText = null;

                    if (!SelectedTemplateId.IsNull && !SelectedTemplateId.IsErased)
                    {
                        DBObject obj = tr.GetObject(SelectedTemplateId, OpenMode.ForRead);
                        if (obj is MText mt)
                        {
                            templateMText = mt;
                            labelLayer = mt.Layer;
                        }
                        else if (obj is DBText dt)
                        {
                            templateDBText = dt;
                            labelLayer = dt.Layer;
                        }
                    }
                    else
                    {
                        labelLayer = "FTTH-NOMOR-RUMAH";
                        // Ensure layer exists with pink color (Magenta = 6)
                        GetOrCreateLayer(db, tr, labelLayer, 6);
                    }

                    // Gather existing placed text centroids to prevent duplicates
                    List<Point3d> placedCentroids = new List<Point3d>();
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if ((ent is MText || ent is DBText) && ent.Layer.Equals(labelLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            if (ent is MText mt) placedCentroids.Add(mt.Location);
                            else if (ent is DBText dt) placedCentroids.Add(dt.Position);
                        }
                    }

                    SelectionSet ss = psr.Value;
                    foreach (SelectedObject so in ss)
                    {
                        Curve c = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                        if (c == null) continue;

                        try
                        {
                            Point3d p1 = c.StartPoint;
                            Point3d p2 = c.EndPoint;
                            double len = c.StartPoint.DistanceTo(c.EndPoint);
                            if (len < 0.05) continue;

                            Vector3d dir = (p2 - p1).GetNormal();
                            Vector3d perp = new Vector3d(-dir.Y, dir.X, 0); // Perpendicular vector

                            Point3d midPt = p1 + (p2 - p1) * 0.5;

                            // 2 test points offset left and right
                            Point3d[] testPoints = new Point3d[]
                            {
                                midPt + perp * 0.7,
                                midPt - perp * 0.7
                            };

                            bool successOnThisLine = false;

                            foreach (Point3d ptTest in testPoints)
                            {
                                if (successOnThisLine) break;

                                // Check proximity to existing texts
                                bool tooNear = false;
                                foreach (Point3d pc in placedCentroids)
                                {
                                    if (ptTest.DistanceTo(pc) < 1.0)
                                    {
                                        tooNear = true;
                                        break;
                                    }
                                }
                                if (tooNear) continue;

                                // OPTIMIZATION: Check if there's already a parcel on LayerParcel enclosing ptTest.
                                // If so, use it instead of TraceBoundary.
                                Polyline targetParcel = null;
                                foreach (ObjectId id in btr)
                                {
                                    if (id.IsErased) continue;
                                    Polyline parcelPoly = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                                    if (parcelPoly != null && parcelPoly.Layer.Equals(settings.LayerParcel, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (IsPointInPolygon(new Point2d(ptTest.X, ptTest.Y), parcelPoly))
                                        {
                                            targetParcel = parcelPoly;
                                            break;
                                        }
                                    }
                                }

                                Polyline boundaryPoly = null;
                                bool deleteBoundary = false;

                                if (targetParcel != null)
                                {
                                    boundaryPoly = targetParcel;
                                    deleteBoundary = false; // Do not delete the database parcel!
                                }
                                else
                                {
                                    // Fallback to TraceBoundary
                                    DBObjectCollection boundaryCol = ed.TraceBoundary(ptTest, false);
                                    if (boundaryCol != null && boundaryCol.Count > 0)
                                    {
                                        boundaryPoly = boundaryCol[0] as Polyline;
                                        deleteBoundary = true; // Delete temporary boundary
                                        // Clean up others
                                        for (int j = 1; j < boundaryCol.Count; j++)
                                        {
                                            boundaryCol[j].Dispose();
                                        }
                                    }
                                }

                                if (boundaryPoly != null)
                                {
                                    double area = boundaryPoly.Area;
                                    if (area > 0.8)
                                    {
                                        double rotation = GetLongestSegmentAngle(boundaryPoly);
                                        Point3d centroid = CalculateCentroid(boundaryPoly);

                                        // Double check centroid duplication
                                        bool isCentroidDup = false;
                                        foreach (Point3d pc in placedCentroids)
                                        {
                                            if (centroid.DistanceTo(pc) < 1.0)
                                            {
                                                isCentroidDup = true;
                                                break;
                                            }
                                        }

                                        if (!isCentroidDup)
                                        {
                                            placedCentroids.Add(centroid);
                                            string textVal = $"{TextPrefix}{currentCounter:D3}";

                                            if (templateMText != null)
                                            {
                                                MText mtCopy = templateMText.Clone() as MText;
                                                mtCopy.Attachment = AttachmentPoint.MiddleCenter;
                                                mtCopy.Contents = textVal;
                                                mtCopy.Location = centroid;
                                                mtCopy.Rotation = rotation;

                                                btr.AppendEntity(mtCopy);
                                                tr.AddNewlyCreatedDBObject(mtCopy, true);
                                            }
                                            else if (templateDBText != null)
                                            {
                                                DBText dtCopy = templateDBText.Clone() as DBText;
                                                dtCopy.HorizontalMode = TextHorizontalMode.TextCenter;
                                                dtCopy.VerticalMode = TextVerticalMode.TextVerticalMid;
                                                dtCopy.TextString = textVal;
                                                dtCopy.Position = centroid;
                                                dtCopy.AlignmentPoint = centroid;
                                                dtCopy.Rotation = rotation;

                                                btr.AppendEntity(dtCopy);
                                                tr.AddNewlyCreatedDBObject(dtCopy, true);
                                            }
                                            else
                                            {
                                                // Create a fresh MText
                                                using (MText mt = new MText())
                                                {
                                                    mt.Layer = labelLayer;
                                                    mt.Attachment = AttachmentPoint.MiddleCenter;
                                                    mt.Contents = textVal;
                                                    mt.Location = centroid;
                                                    mt.Height = 2.5;
                                                    mt.Rotation = rotation;

                                                    btr.AppendEntity(mt);
                                                    tr.AddNewlyCreatedDBObject(mt, true);
                                                }
                                            }

                                            currentCounter++;
                                            placedCount++;
                                            successOnThisLine = true;
                                        }
                                    }

                                    if (deleteBoundary && boundaryPoly != null)
                                    {
                                        boundaryPoly.Dispose();
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nPlaced {placedCount} sequential labels successfully.");
                    PaletteManager.Log($"Placed {placedCount} labels.");
                }
            }
            ed.Regen();
        }

        /// <summary>
        /// Mode B: Label generated parcels directly using reference points as anchors.
        /// </summary>
        public static void LabelGeneratedParcels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage($"\n[FTTH] Converting point markers to sequential labels...");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Gather target layer
                    string labelLayer = settings.LayerRoadLabel; // e.g. Nama Jalan
                    MText templateMText = null;
                    DBText templateDBText = null;

                    if (!SelectedTemplateId.IsNull && !SelectedTemplateId.IsErased)
                    {
                        DBObject obj = tr.GetObject(SelectedTemplateId, OpenMode.ForRead);
                        if (obj is MText mt)
                        {
                            templateMText = mt;
                            labelLayer = mt.Layer;
                        }
                        else if (obj is DBText dt)
                        {
                            templateDBText = dt;
                            labelLayer = dt.Layer;
                        }
                    }
                    else
                    {
                        GetOrCreateLayer(db, tr, labelLayer, settings.ColorRoadLabel);
                    }

                    // Gather all parcel polylines on LayerParcel
                    List<Polyline> parcels = new List<Polyline>();
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        Polyline pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (pl != null && pl.Layer.Equals(settings.LayerParcel, StringComparison.OrdinalIgnoreCase))
                        {
                            parcels.Add(pl);
                        }
                    }

                    // Gather all points on LayerParcelPoint
                    List<DBPoint> points = new List<DBPoint>();
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        DBPoint pt = tr.GetObject(id, OpenMode.ForRead) as DBPoint;
                        if (pt != null && pt.Layer.Equals(settings.LayerParcelPoint, StringComparison.OrdinalIgnoreCase))
                        {
                            points.Add(pt);
                        }
                    }

                    if (points.Count == 0)
                    {
                        ed.WriteMessage("\nError: No reference points found on layer " + settings.LayerParcelPoint);
                        tr.Commit();
                        return;
                    }

                    int currentCounter = StartIndex;
                    int placedCount = 0;

                    foreach (DBPoint pt in points)
                    {
                        Point3d pos = pt.Position;
                        Point2d pos2d = new Point2d(pos.X, pos.Y);

                        // Find enclosing parcel
                        Polyline matchedParcel = null;
                        foreach (Polyline pl in parcels)
                        {
                            if (IsPointInPolygon(pos2d, pl))
                            {
                                matchedParcel = pl;
                                break;
                            }
                        }

                        double rotation = 0.0;
                        Point3d insertPt = pos;

                        if (matchedParcel != null)
                        {
                            rotation = GetLongestSegmentAngle(matchedParcel);
                            insertPt = CalculateCentroid(matchedParcel);
                        }

                        // Create label
                        string textVal = $"{TextPrefix}{currentCounter:D3}";

                        if (templateMText != null)
                        {
                            MText mtCopy = templateMText.Clone() as MText;
                            mtCopy.Attachment = AttachmentPoint.MiddleCenter;
                            mtCopy.Contents = textVal;
                            mtCopy.Location = insertPt;
                            mtCopy.Rotation = rotation;

                            btr.AppendEntity(mtCopy);
                            tr.AddNewlyCreatedDBObject(mtCopy, true);
                        }
                        else if (templateDBText != null)
                        {
                            DBText dtCopy = templateDBText.Clone() as DBText;
                            dtCopy.HorizontalMode = TextHorizontalMode.TextCenter;
                            dtCopy.VerticalMode = TextVerticalMode.TextVerticalMid;
                            dtCopy.TextString = textVal;
                            dtCopy.Position = insertPt;
                            dtCopy.AlignmentPoint = insertPt;
                            dtCopy.Rotation = rotation;

                            btr.AppendEntity(dtCopy);
                            tr.AddNewlyCreatedDBObject(dtCopy, true);
                        }
                        else
                        {
                            using (MText mt = new MText())
                            {
                                mt.Layer = labelLayer;
                                mt.Attachment = AttachmentPoint.MiddleCenter;
                                mt.Contents = textVal;
                                mt.Location = insertPt;
                                mt.Height = 2.5;
                                mt.Rotation = rotation;

                                btr.AppendEntity(mt);
                                tr.AddNewlyCreatedDBObject(mt, true);
                            }
                        }

                        // Delete point marker
                        pt.UpgradeOpen();
                        pt.Erase();

                        currentCounter++;
                        placedCount++;
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nLabeled {placedCount} parcels and removed point markers.");
                    PaletteManager.Log($"Converted {placedCount} points.");
                }
            }
            ed.Regen();
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

        private static double GetLongestSegmentAngle(Polyline pl)
        {
            double maxSegLen = 0.0;
            double bestAng = 0.0;
            int numVerts = pl.NumberOfVertices;

            for (int k = 0; k < numVerts; k++)
            {
                Point2d pa = pl.GetPoint2dAt(k);
                int nextIdx = (k + 1) % numVerts;
                if (nextIdx == 0 && !pl.Closed) continue;

                Point2d pb = pl.GetPoint2dAt(nextIdx);
                double curSegLen = pa.GetDistanceTo(pb);
                if (curSegLen > maxSegLen)
                {
                    maxSegLen = curSegLen;
                    bestAng = Math.Atan2(pb.Y - pa.Y, pb.X - pa.X);
                }
            }

            // Keep readable text angle between -pi/2 and pi/2
            while (bestAng > Math.PI / 2.0) bestAng -= Math.PI;
            while (bestAng <= -Math.PI / 2.0) bestAng += Math.PI;

            return bestAng;
        }

        private static Point3d CalculateCentroid(Polyline pl)
        {
            try
            {
                using (DBObjectCollection curves = new DBObjectCollection())
                {
                    curves.Add(pl.Clone() as Curve);
                    using (DBObjectCollection regions = Region.CreateFromCurves(curves))
                    {
                        if (regions != null && regions.Count > 0)
                        {
                            Region region = regions[0] as Region;
                            if (region != null)
                            {
                                Point3d origin = Point3d.Origin;
                                Vector3d xAxis = Vector3d.XAxis;
                                Vector3d yAxis = Vector3d.YAxis;
                                RegionAreaProperties props = region.AreaProperties(ref origin, ref xAxis, ref yAxis);
                                Point3d centroid = new Point3d(props.Centroid.X, props.Centroid.Y, 0.0);

                                foreach (DBObject obj in regions) obj.Dispose();
                                return centroid;
                            }
                        }
                    }
                }
            }
            catch { }

            // Fallback average
            double sumX = 0;
            double sumY = 0;
            int numVerts = pl.NumberOfVertices;
            for (int i = 0; i < numVerts; i++)
            {
                Point2d pt = pl.GetPoint2dAt(i);
                sumX += pt.X;
                sumY += pt.Y;
            }
            return new Point3d(sumX / numVerts, sumY / numVerts, 0.0);
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];

            lt.UpgradeOpen();
            using (LayerTableRecord ltr = new LayerTableRecord())
            {
                ltr.Name = name;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                ObjectId id = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return id;
            }
        }
    }
}
