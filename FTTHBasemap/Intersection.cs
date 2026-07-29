using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using FTTHBasemap.Model;

namespace FTTHBasemap
{
    public static class Intersection
    {
        /// <summary>
        /// Generates yellow preview circles at all intersection points on active layers.
        /// </summary>
        public static List<Point3d> GeneratePreviews(Database db, Transaction tr, BasemapSettings settings)
        {
            // Clear old previews first
            ClearPreviews(db, tr, settings);

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            // Collect active layers
            List<string> activeLayers = new List<string>();
            if (settings.GenerateRoad) activeLayers.Add(settings.LayerRoadEdge);
            if (settings.GenerateSidewalk) activeLayers.Add(settings.LayerSidewalk);
            if (settings.GenerateRow) activeLayers.Add(settings.LayerRow);

            // Collect curves on active layers
            List<Curve> curves = new List<Curve>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c == null) continue;

                if (activeLayers.Contains(c.Layer))
                {
                    curves.Add(c);
                }
            }

            // Find all intersection points
            List<Point3d> intersectPoints = new List<Point3d>();
            for (int i = 0; i < curves.Count; i++)
            {
                for (int j = i + 1; j < curves.Count; j++)
                {
                    // Only intersect if they are on the same layer
                    if (curves[i].Layer != curves[j].Layer) continue;

                    Point3dCollection pts = new Point3dCollection();
                    curves[i].IntersectWith(curves[j], Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);

                    foreach (Point3d pt in pts)
                    {
                        // Deduplicate within 0.5m tolerance
                        bool exists = false;
                        foreach (Point3d existingPt in intersectPoints)
                        {
                            if (existingPt.DistanceTo(pt) < 0.5)
                            {
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                        {
                            intersectPoints.Add(new Point3d(pt.X, pt.Y, 0.0)); // Force Z = 0
                        }
                    }
                }
            }

            // Draw preview circles
            if (intersectPoints.Count > 0)
            {
                btr.UpgradeOpen();
                foreach (Point3d pt in intersectPoints)
                {
                    using (Circle circle = new Circle())
                    {
                        circle.Center = pt;
                        circle.Radius = 0.5; // Circle radius
                        circle.Layer = settings.LayerPreview;
                        circle.ColorIndex = settings.ColorPreview;
                        btr.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);
                    }
                }
            }

            return intersectPoints;
        }

        /// <summary>
        /// Clears all preview circles and break marks.
        /// </summary>
        public static void ClearPreviews(Database db, Transaction tr, BasemapSettings settings)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent.Layer == settings.LayerPreview || ent.Layer == settings.LayerBreakmark)
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
            }
        }

        /// <summary>
        /// Executes the complete intersection cleaning: Split, Clean, and Fillet.
        /// </summary>
        public static void CleanIntersections(Database db, Transaction tr, BasemapSettings settings)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            // 1. Generate intersection points
            List<Point3d> pts = GeneratePreviews(db, tr, settings);
            if (pts.Count == 0)
            {
                ed.WriteMessage("\n[FTTH] No intersections found to clean.");
                return;
            }

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            List<string> activeLayers = new List<string>();
            if (settings.GenerateRoad) activeLayers.Add(settings.LayerRoadEdge);
            if (settings.GenerateSidewalk) activeLayers.Add(settings.LayerSidewalk);
            if (settings.GenerateRow) activeLayers.Add(settings.LayerRow);

            // 2. Split curves at intersection points (Single-Pass Logic)
            ed.WriteMessage($"\n[FTTH] Splitting curves at {pts.Count} intersection points...");
            
            // Gather original active layer curves
            List<ObjectId> originalCurveIds = new List<ObjectId>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c != null && activeLayers.Contains(c.Layer))
                {
                    originalCurveIds.Add(id);
                }
            }

            foreach (ObjectId cId in originalCurveIds)
            {
                Curve c = tr.GetObject(cId, OpenMode.ForRead) as Curve;
                if (c == null || c.IsErased) continue;

                // Collect split parameters for this curve
                List<double> paramsToSplit = new List<double>();
                foreach (Point3d pt in pts)
                {
                    try
                    {
                        Point3d closest = c.GetClosestPointTo(pt, false);
                        if (closest.DistanceTo(pt) < 1e-3) // within 1mm
                        {
                            // Make sure it is not an endpoint
                            if (c.StartPoint.DistanceTo(closest) > 1e-3 && c.EndPoint.DistanceTo(closest) > 1e-3)
                            {
                                double param = c.GetParameterAtPoint(closest);

                                // Check if parameter already added
                                bool exists = false;
                                foreach (double p in paramsToSplit)
                                {
                                    if (Math.Abs(p - param) < 1e-5)
                                    {
                                        exists = true;
                                        break;
                                    }
                                }

                                if (!exists)
                                {
                                    paramsToSplit.Add(param);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Split curve if it has intersection parameters
                if (paramsToSplit.Count > 0)
                {
                    paramsToSplit.Sort();
                    try
                    {
                        c.UpgradeOpen();
                        DoubleCollection dc = new DoubleCollection(paramsToSplit.ToArray());
                        DBObjectCollection splitCurves = c.GetSplitCurves(dc);
                        if (splitCurves != null && splitCurves.Count > 0)
                        {
                            var meta = XDataManager.ReadMetadata(c);

                            foreach (DBObject dbObj in splitCurves)
                            {
                                Curve child = dbObj as Curve;
                                if (child != null)
                                {
                                    child.Layer = c.Layer;
                                    child.ColorIndex = c.ColorIndex;

                                    btr.AppendEntity(child);
                                    tr.AddNewlyCreatedDBObject(child, true);

                                    if (meta != null)
                                    {
                                        XDataManager.WriteMetadata(child, meta);
                                    }
                                }
                            }
                            c.Erase();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FTTH] Split error for curve {c.Handle}: {ex.Message}");
                    }
                }
            }

            // Draw red visual breakmarks at split points
            foreach (Point3d pt in pts)
            {
                using (Line line1 = new Line(new Point3d(pt.X - 0.15, pt.Y - 0.15, 0), new Point3d(pt.X + 0.15, pt.Y + 0.15, 0)))
                using (Line line2 = new Line(new Point3d(pt.X + 0.15, pt.Y - 0.15, 0), new Point3d(pt.X - 0.15, pt.Y + 0.15, 0)))
                {
                    line1.Layer = settings.LayerBreakmark;
                    line1.ColorIndex = settings.ColorBreakmark;
                    line2.Layer = settings.LayerBreakmark;
                    line2.ColorIndex = settings.ColorBreakmark;
                    btr.AppendEntity(line1);
                    tr.AddNewlyCreatedDBObject(line1, true);
                    btr.AppendEntity(line2);
                    tr.AddNewlyCreatedDBObject(line2, true);
                }
            }

            // Gather all active centerlines in drawing
            List<Curve> centerlines = new List<Curve>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c == null) continue;

                if (c.Layer == settings.LayerCenterline)
                {
                    centerlines.Add(c);
                }
            }

            // 2.5 Extend split segments to Centerlines
            ed.WriteMessage("\n[FTTH] Extending split segments to Centerlines...");
            foreach (string layer in activeLayers)
            {
                ExtendToCenterline(db, tr, btr, layer, centerlines, pts, 3.0);
            }

            // 3. Delete fragments crossing foreign centerlines
            ed.WriteMessage("\n[FTTH] Cleaning overlap fragments inside intersections...");

            // Re-fetch active fragments
            List<ObjectId> activeFragmentIds = new List<ObjectId>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c != null && activeLayers.Contains(c.Layer))
                {
                    activeFragmentIds.Add(id);
                }
            }

            List<ObjectId> fragsToDelete = new List<ObjectId>();
            foreach (ObjectId fId in activeFragmentIds)
            {
                Curve c = tr.GetObject(fId, OpenMode.ForRead) as Curve;
                if (c == null || c.IsErased) continue;

                string srcHdl = XDataManager.GetMetadataSourceHandle(c);
                if (string.IsNullOrEmpty(srcHdl)) continue;

                bool shouldDelete = false;
                foreach (Curve cl in centerlines)
                {
                    // Skip if it is the source centerline
                    if (cl.Handle.ToString().Equals(srcHdl, StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        // Check physical intersection
                        Point3dCollection intPts = new Point3dCollection();
                        c.IntersectWith(cl, Intersect.OnBothOperands, intPts, IntPtr.Zero, IntPtr.Zero);
                        if (intPts.Count > 0)
                        {
                            shouldDelete = true;
                            break;
                        }

                        // Check minimum distance to foreign centerline
                        double minDist = GetMinDistance(c, cl);
                        if (minDist < 0.1) // touching or within 10cm
                        {
                            shouldDelete = true;
                            break;
                        }
                    }
                    catch { }
                }

                if (shouldDelete)
                {
                    fragsToDelete.Add(fId);
                }
            }

            // Erase crossing fragments
            int erasedCount = 0;
            foreach (ObjectId id in fragsToDelete)
            {
                Curve c = tr.GetObject(id, OpenMode.ForWrite) as Curve;
                if (c != null && !c.IsErased)
                {
                    c.Erase();
                    erasedCount++;
                }
            }
            ed.WriteMessage($"\n[FTTH] Erased {erasedCount} crossing fragments.");

            // 4. Perform native mathematical filleting between remaining ends
            ed.WriteMessage("\n[FTTH] Running native mathematical filleting...");
            int filletCount = 0;

            foreach (Point3d pt in pts)
            {
                // Find preserved curves on active layers that start or end at this point
                List<Curve> curvesAtPoint = new List<Curve>();
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased) continue;
                    Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                    if (c == null || c.IsErased) continue;

                    if (activeLayers.Contains(c.Layer))
                    {
                        if (c.StartPoint.DistanceTo(pt) < 1e-3 || c.EndPoint.DistanceTo(pt) < 1e-3)
                        {
                            curvesAtPoint.Add(c);
                        }
                    }
                }

                // Group by layer
                Dictionary<string, List<Curve>> layerGroups = new Dictionary<string, List<Curve>>();
                foreach (Curve c in curvesAtPoint)
                {
                    if (!layerGroups.ContainsKey(c.Layer))
                    {
                        layerGroups[c.Layer] = new List<Curve>();
                    }
                    layerGroups[c.Layer].Add(c);
                }

                foreach (var kvp in layerGroups)
                {
                    // Fillet corners if exactly two curves on the same layer meet at the split point
                    if (kvp.Value.Count == 2)
                    {
                        Curve f1 = kvp.Value[0];
                        Curve f2 = kvp.Value[1];

                        double R = settings.FilletRoadRadius;
                        if (f1.Layer == settings.LayerSidewalk) R = settings.FilletSidewalkRadius;
                        else if (f1.Layer == settings.LayerRow) R = settings.FilletRowRadius;

                        if (ApplyFilletMath(db, tr, btr, f1, f2, pt, R))
                        {
                            filletCount++;
                        }
                    }
                }
            }
            ed.WriteMessage($"\n[FTTH] Completed {filletCount} filleted corners.");

            // Clear visual aids
            ClearPreviews(db, tr, settings);
        }

        /// <summary>
        /// Mathematically trims two curves and inserts a fillet bulge, joining them in-memory.
        /// </summary>
        private static bool ApplyFilletMath(Database db, Transaction tr, BlockTableRecord btr, Curve f1, Curve f2, Point3d P, double R)
        {
            try
            {
                Polyline pl1 = f1 as Polyline;
                Polyline pl2 = f2 as Polyline;
                if (pl1 == null || pl2 == null) return false;

                // 1. Determine tangent directions pointing AWAY from P
                Vector3d tangent1;
                double f1Len = pl1.GetDistanceAtParameter(pl1.EndParam);
                bool f1StartsAtP = pl1.StartPoint.DistanceTo(P) < 1e-3;
                if (f1StartsAtP)
                {
                    tangent1 = pl1.GetFirstDerivative(pl1.StartParam).GetNormal();
                }
                else
                {
                    tangent1 = -pl1.GetFirstDerivative(pl1.EndParam).GetNormal();
                }

                Vector3d tangent2;
                double f2Len = pl2.GetDistanceAtParameter(pl2.EndParam);
                bool f2StartsAtP = pl2.StartPoint.DistanceTo(P) < 1e-3;
                if (f2StartsAtP)
                {
                    tangent2 = pl2.GetFirstDerivative(pl2.StartParam).GetNormal();
                }
                else
                {
                    tangent2 = -pl2.GetFirstDerivative(pl2.EndParam).GetNormal();
                }

                // 2. Compute angle and bisector
                double cosAngle = tangent1.DotProduct(tangent2);
                cosAngle = Math.Max(-1.0, Math.Min(1.0, cosAngle));
                double angle = Math.Acos(cosAngle);
                double alpha = angle / 2.0;

                // If lines are parallel or too close to parallel, skip fillet
                if (alpha < 0.05 || alpha > 1.52) return false;

                // T = distance from P to tangent points
                double T = R / Math.Tan(alpha);

                if (T >= f1Len || T >= f2Len) return false;

                // 3. Compute parameters and tangent points on the curves safely
                double paramT1 = f1StartsAtP ? pl1.GetParameterAtDistance(T) : pl1.GetParameterAtDistance(f1Len - T);
                double paramT2 = f2StartsAtP ? pl2.GetParameterAtDistance(T) : pl2.GetParameterAtDistance(f2Len - T);

                Point3d T1 = pl1.GetPointAtParameter(paramT1);
                Point3d T2 = pl2.GetPointAtParameter(paramT2);

                // 4. Compute Arc center
                Vector3d bisector = (tangent1 + tangent2).GetNormal();
                Point3d Center = P + bisector * (R / Math.Sin(alpha));
                Point3d midPt = Center - (P - Center).GetNormal() * R; // Fixed bulge vector direction

                // 5. Trim curves in memory
                Polyline kept1 = null;
                Polyline kept2 = null;

                pl1.UpgradeOpen();
                DoubleCollection dc1 = new DoubleCollection(new double[] { paramT1 });
                DBObjectCollection split1 = pl1.GetSplitCurves(dc1);
                if (split1 != null && split1.Count > 0)
                {
                    kept1 = (f1StartsAtP ? split1[1] : split1[0]) as Polyline;
                }

                pl2.UpgradeOpen();
                DoubleCollection dc2 = new DoubleCollection(new double[] { paramT2 });
                DBObjectCollection split2 = pl2.GetSplitCurves(dc2);
                if (split2 != null && split2.Count > 0)
                {
                    kept2 = (f2StartsAtP ? split2[1] : split2[0]) as Polyline;
                }

                if (kept1 == null || kept2 == null)
                {
                    if (split1 != null) { foreach (DBObject obj in split1) obj.Dispose(); }
                    if (split2 != null) { foreach (DBObject obj in split2) obj.Dispose(); }
                    return false;
                }

                // 6. Ensure proper vertex orientation (kept1 ends at T1, kept2 starts at T2)
                if (kept1.EndPoint.DistanceTo(T1) > 1e-3)
                {
                    kept1.ReverseCurve();
                }
                if (kept2.StartPoint.DistanceTo(T2) > 1e-3)
                {
                    kept2.ReverseCurve();
                }

                // 7. Calculate exact Bulge value for the connecting fillet arc segment
                Point2d t1_2d = new Point2d(T1.X, T1.Y);
                Point2d t2_2d = new Point2d(T2.X, T2.Y);
                Point2d m_2d = new Point2d(midPt.X, midPt.Y);

                Vector2d v1 = t2_2d - t1_2d;
                Vector2d v2 = m_2d - t1_2d;
                double cross = v1.X * v2.Y - v1.Y * v2.X;

                Point3d chordMid = new Point3d((T1.X + T2.X) / 2.0, (T1.Y + T2.Y) / 2.0, 0.0);
                double h = R - Center.DistanceTo(chordMid); // Robust minor arc height calculation
                double d = T1.DistanceTo(T2) / 2.0;

                double bulgeValue = h / d;
                if (cross < 0) bulgeValue = -bulgeValue;

                // 8. Build the final merged polyline in database
                Polyline mergedPL = new Polyline();
                mergedPL.Layer = pl1.Layer;
                mergedPL.ColorIndex = pl1.ColorIndex;

                int vertexCount1 = kept1.NumberOfVertices;
                for (int i = 0; i < vertexCount1; i++)
                {
                    mergedPL.AddVertexAt(i, kept1.GetPoint2dAt(i), kept1.GetBulgeAt(i), kept1.GetStartWidthAt(i), kept1.GetEndWidthAt(i));
                }

                // Set bulge at T1 vertex to link to T2
                mergedPL.SetBulgeAt(vertexCount1 - 1, bulgeValue);

                int vertexCount2 = kept2.NumberOfVertices;
                for (int i = 0; i < vertexCount2; i++)
                {
                    mergedPL.AddVertexAt(vertexCount1 + i, kept2.GetPoint2dAt(i), kept2.GetBulgeAt(i), kept2.GetStartWidthAt(i), kept2.GetEndWidthAt(i));
                }

                btr.AppendEntity(mergedPL);
                tr.AddNewlyCreatedDBObject(mergedPL, true);

                // Re-write metadata
                XDataManager.WriteMetadata(mergedPL, XDataManager.ReadMetadata(pl1));

                // Erase the old un-split curves from AutoCAD database
                pl1.Erase();
                pl2.Erase();

                // Dispose the in-memory split curves to prevent memory leaks
                foreach (DBObject obj in split1) obj.Dispose();
                foreach (DBObject obj in split2) obj.Dispose();

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Helper to calculate the minimum distance between two curves.
        /// </summary>
        private static double GetMinDistance(Curve c1, Curve c2)
        {
            try
            {
                int samples = 10;
                double minLength = double.MaxValue;
                double startParam = c1.StartParam;
                double endParam = c1.EndParam;
                double step = (endParam - startParam) / samples;

                for (int i = 0; i <= samples; i++)
                {
                    double param = startParam + i * step;
                    Point3d pt = c1.GetPointAtParameter(param);
                    Point3d closestOnC2 = c2.GetClosestPointTo(pt, false);
                    double dist = pt.DistanceTo(closestOnC2);
                    if (dist < minLength)
                    {
                        minLength = dist;
                    }
                }
                return minLength;
            }
            catch
            {
                return double.MaxValue;
            }
        }

        /// <summary>
        /// Extends polyline endpoints to the nearest centerline within tolerance.
        /// </summary>
        private static void ExtendToCenterline(Database db, Transaction tr, BlockTableRecord btr, string layer, List<Curve> centerlines, List<Point3d> previewPoints, double tolerance)
        {
            List<Polyline> polylines = new List<Polyline>();
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Polyline pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                if (pl != null && pl.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))
                {
                    polylines.Add(pl);
                }
            }

            foreach (Polyline pl in polylines)
            {
                if (pl.NumberOfVertices < 2) continue;

                Point3d startPt = pl.StartPoint;
                Point3d endPt = pl.EndPoint;
                Point3d startPtZero = new Point3d(startPt.X, startPt.Y, 0.0);
                Point3d endPtZero = new Point3d(endPt.X, endPt.Y, 0.0);

                bool startInCircle = false;
                bool endInCircle = false;

                foreach (Point3d cc in previewPoints)
                {
                    if (startPtZero.DistanceTo(cc) < 0.5) startInCircle = true;
                    if (endPtZero.DistanceTo(cc) < 0.5) endInCircle = true;
                }

                if (startInCircle && endInCircle) continue;

                Point3d? bestStartPt = null;
                double bestStartDist = tolerance;
                Point3d? bestEndPt = null;
                double bestEndDist = tolerance;

                foreach (Curve cl in centerlines)
                {
                    try
                    {
                        Point3dCollection intPts = new Point3dCollection();
                        pl.IntersectWith(cl, Intersect.ExtendThis, intPts, IntPtr.Zero, IntPtr.Zero);

                        foreach (Point3d pt in intPts)
                        {
                            Point3d ptZero = new Point3d(pt.X, pt.Y, 0.0);

                            if (!startInCircle)
                            {
                                double dStart = startPtZero.DistanceTo(ptZero);
                                if (dStart > 0.001 && dStart < bestStartDist)
                                {
                                    bestStartDist = dStart;
                                    bestStartPt = ptZero;
                                }
                            }

                            if (!endInCircle)
                            {
                                double dEnd = endPtZero.DistanceTo(ptZero);
                                if (dEnd > 0.001 && dEnd < bestEndDist)
                                {
                                    bestEndDist = dEnd;
                                    bestEndPt = ptZero;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (bestStartPt.HasValue || bestEndPt.HasValue)
                {
                    pl.UpgradeOpen();
                    if (bestStartPt.HasValue)
                    {
                        pl.SetPointAt(0, new Point2d(bestStartPt.Value.X, bestStartPt.Value.Y));
                    }
                    if (bestEndPt.HasValue)
                    {
                        pl.SetPointAt(pl.NumberOfVertices - 1, new Point2d(bestEndPt.Value.X, bestEndPt.Value.Y));
                    }
                }
            }
        }
    }
}
