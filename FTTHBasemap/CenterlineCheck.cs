using System;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using FTTHBasemap.Model;
using FTTHBasemap.UI;

namespace FTTHBasemap
{
    public static class CenterlineCheck
    {
        [CommandMethod("FTTH_CHECKGAPS")]
        public static void CheckGaps()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            double tolerance = 1.0;
            var panel = PaletteManager.BasemapPanel;
            if (panel != null)
            {
                panel.Dispatcher.Invoke(() =>
                {
                    if (double.TryParse(panel.TxtGapTolerance.Text, out double val))
                    {
                        tolerance = val;
                    }
                });
            }

            ed.WriteMessage($"\n[FTTH] Checking centerline connection gaps with tolerance: {tolerance:F2} m...");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. Clear old warnings
                    ClearGapsInternal(db, tr);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    string centerlineLayer = BasemapSettings.Instance.LayerCenterline;
                    string gapLayer = "FTTH-GAP-WARNING";
                    short gapColor = 1; // Red

                    // 2. Gather all open curves on LayerCenterline
                    List<Curve> curves = new List<Curve>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c != null && c.Layer.Equals(centerlineLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            curves.Add(c);
                        }
                    }

                    if (curves.Count == 0)
                    {
                        ed.WriteMessage($"\n[FTTH] Error: No centerline curves found on layer '{centerlineLayer}'.");
                        tr.Commit();
                        return;
                    }

                    // Create warning layer if it doesn't exist
                    BasemapPanel.GetOrCreateLayer(db, tr, gapLayer, gapColor);

                    List<Point3d> gapPoints = new List<Point3d>();
                    int gapCount = 0;

                    // 3. Check endpoints of each curve against all other curves
                    foreach (Curve curve in curves)
                    {
                        if (curve.Closed) continue; // Closed curves have no open endpoints

                        CheckEndpoint(curve.StartPoint, curve, curves, tolerance, tr, modelSpace, gapLayer, gapColor, gapPoints, ref gapCount);
                        CheckEndpoint(curve.EndPoint, curve, curves, tolerance, tr, modelSpace, gapLayer, gapColor, gapPoints, ref gapCount);
                    }

                    tr.Commit();

                    ed.WriteMessage($"\n[FTTH] Connection check completed. Found {gapCount} gap(s).");
                    if (panel != null)
                    {
                        panel.Dispatcher.Invoke(() =>
                        {
                            panel.LogMessage($"Gap check: Found {gapCount} gap(s). Warning markers placed on layer '{gapLayer}'.");
                        });
                    }

                    // 4. Zoom to first gap point if found
                    if (gapPoints.Count > 0)
                    {
                        Point3d firstGap = gapPoints[0];
                        Point3d minPt = new Point3d(firstGap.X - 5.0, firstGap.Y - 5.0, 0);
                        Point3d maxPt = new Point3d(firstGap.X + 5.0, firstGap.Y + 5.0, 0);
                        try
                        {
                            ed.Command("_.ZOOM", "_W", minPt, maxPt);
                            ed.WriteMessage($"\n[FTTH] Zoomed to first gap location: {firstGap.X:F2}, {firstGap.Y:F2}");
                        }
                        catch { }
                    }
                }
            }
        }

        [CommandMethod("FTTH_CLEARGAPS")]
        public static void ClearGaps()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ClearGapsInternal(db, tr);
                    tr.Commit();
                }
            }
            ed.WriteMessage("\n[FTTH] Gap warning markers cleared.");
            var panel = PaletteManager.BasemapPanel;
            if (panel != null)
            {
                panel.Dispatcher.Invoke(() =>
                {
                    panel.LogMessage("Gap warnings cleared.");
                });
            }
        }

        private static void ClearGapsInternal(Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            string gapLayer = "FTTH-GAP-WARNING";

            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased) continue;
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null && ent.Layer.Equals(gapLayer, StringComparison.OrdinalIgnoreCase))
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
            }
        }

        private static void CheckEndpoint(Point3d endPt, Curve currentCurve, List<Curve> allCurves, double tolerance, Transaction tr, BlockTableRecord modelSpace, string gapLayer, short gapColor, List<Point3d> gapPoints, ref int gapCount)
        {
            double minDist = double.MaxValue;
            Point3d closestPtOnOther = Point3d.Origin;

            foreach (Curve otherCurve in allCurves)
            {
                if (otherCurve.ObjectId == currentCurve.ObjectId) continue;

                Point3d closestPt = otherCurve.GetClosestPointTo(endPt, false);
                double d = endPt.DistanceTo(closestPt);
                if (d < minDist)
                {
                    minDist = d;
                    closestPtOnOther = closestPt;
                }
            }

            // A gap exists if the endpoint is within tolerance but not touching (distance > 1mm to prevent floating point issues)
            if (minDist >= 0.001 && minDist <= tolerance)
            {
                // Deduplicate warnings at the exact same location
                bool alreadyRegistered = false;
                foreach (Point3d registeredPt in gapPoints)
                {
                    if (registeredPt.DistanceTo(endPt) < 0.05) // 5cm tolerance for overlapping marks
                    {
                        alreadyRegistered = true;
                        break;
                    }
                }

                if (!alreadyRegistered)
                {
                    gapPoints.Add(endPt);
                    gapCount++;

                    // Draw a red circle warning marker (radius 1.0m)
                    Circle warningCircle = new Circle();
                    warningCircle.Center = endPt;
                    warningCircle.Radius = 1.0;
                    warningCircle.Layer = gapLayer;
                    warningCircle.ColorIndex = gapColor;
                    modelSpace.AppendEntity(warningCircle);
                    tr.AddNewlyCreatedDBObject(warningCircle, true);

                    // Draw a yellow connecting line showing where it should attach
                    Line warningLine = new Line(endPt, closestPtOnOther);
                    warningLine.Layer = gapLayer;
                    warningLine.ColorIndex = 2; // Yellow
                    modelSpace.AppendEntity(warningLine);
                    tr.AddNewlyCreatedDBObject(warningLine, true);
                }
            }
        }

        [CommandMethod("FTTH_FIXGAPS")]
        public static void FixGaps()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            double tolerance = 1.0;
            var panel = PaletteManager.BasemapPanel;
            if (panel != null)
            {
                panel.Dispatcher.Invoke(() =>
                {
                    if (double.TryParse(panel.TxtGapTolerance.Text, out double val))
                    {
                        tolerance = val;
                    }
                });
            }

            ed.WriteMessage($"\n[FTTH] Auto-fixing centerline gaps (Tolerance: {tolerance:F2} m)...");

            int fixedGaps = 0;
            int trimmedExcess = 0;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Clear old warnings
                    ClearGapsInternal(db, tr);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    string centerlineLayer = BasemapSettings.Instance.LayerCenterline;

                    // Gather all centerline curves
                    List<ObjectId> curveIds = new List<ObjectId>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c != null && c.Layer.Equals(centerlineLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            curveIds.Add(id);
                        }
                    }

                    if (curveIds.Count == 0)
                    {
                        ed.WriteMessage($"\n[FTTH] Error: No centerline curves found on layer '{centerlineLayer}'.");
                        tr.Commit();
                        return;
                    }

                    // Process each curve
                    foreach (ObjectId cId in curveIds)
                    {
                        Curve curve = tr.GetObject(cId, OpenMode.ForRead) as Curve;
                        if (curve == null || curve.Closed) continue;

                        // Check StartPoint
                        ProcessFixEndpoint(cId, true, curveIds, tolerance, tr, ref fixedGaps, ref trimmedExcess);
                        
                        // Check EndPoint
                        ProcessFixEndpoint(cId, false, curveIds, tolerance, tr, ref fixedGaps, ref trimmedExcess);
                    }

                    tr.Commit();
                }
            }

            ed.WriteMessage($"\n[FTTH] Auto-fix complete. Fixed {fixedGaps} gap(s), trimmed {trimmedExcess} excess segment(s).");
            if (panel != null)
            {
                panel.Dispatcher.Invoke(() =>
                {
                    panel.LogMessage($"Auto-fix: Fixed {fixedGaps} gap(s), trimmed {trimmedExcess} excess(es).");
                });
            }
            doc.Editor.Regen();
        }

        private static void ProcessFixEndpoint(ObjectId curveId, bool isStart, List<ObjectId> allCurveIds, double tolerance, Transaction tr, ref int fixedGaps, ref int trimmedExcess)
        {
            Curve curve = tr.GetObject(curveId, OpenMode.ForRead) as Curve;
            if (curve == null) return;

            Point3d endPt = isStart ? curve.StartPoint : curve.EndPoint;

            // 1. Check for excess crossing trim first
            Point3d? bestIntersectPt = null;
            double minIntersectDist = double.MaxValue;

            foreach (ObjectId otherId in allCurveIds)
            {
                if (otherId == curveId) continue;
                Curve otherCurve = tr.GetObject(otherId, OpenMode.ForRead) as Curve;
                if (otherCurve == null) continue;

                Point3dCollection intersectPoints = new Point3dCollection();
                curve.IntersectWith(otherCurve, Intersect.OnBothOperands, intersectPoints, IntPtr.Zero, IntPtr.Zero);

                foreach (Point3d intPt in intersectPoints)
                {
                    double d = endPt.DistanceTo(intPt);
                    if (d > 0.001 && d <= tolerance)
                    {
                        if (d < minIntersectDist)
                        {
                            minIntersectDist = d;
                            bestIntersectPt = intPt;
                        }
                    }
                }
            }

            if (bestIntersectPt.HasValue)
            {
                Point3d trimPt = bestIntersectPt.Value;
                curve.UpgradeOpen();
                UpdateCurveEndpoint(curve, isStart, trimPt);
                trimmedExcess++;
                return; // Trimmed successfully, no need to extend
            }

            // 2. Check for gap extension
            double minGapDist = double.MaxValue;
            Point3d bestClosestPt = Point3d.Origin;

            foreach (ObjectId otherId in allCurveIds)
            {
                if (otherId == curveId) continue;
                Curve otherCurve = tr.GetObject(otherId, OpenMode.ForRead) as Curve;
                if (otherCurve == null) continue;

                Point3d closestPt = otherCurve.GetClosestPointTo(endPt, false);
                double d = endPt.DistanceTo(closestPt);
                if (d < minGapDist)
                {
                    minGapDist = d;
                    bestClosestPt = closestPt;
                }
            }

            if (minGapDist > 0.001 && minGapDist <= tolerance)
            {
                curve.UpgradeOpen();
                UpdateCurveEndpoint(curve, isStart, bestClosestPt);
                fixedGaps++;
            }
        }

        private static void UpdateCurveEndpoint(Curve curve, bool isStart, Point3d newPt)
        {
            if (curve is Polyline pl)
            {
                int vertexIndex = isStart ? 0 : pl.NumberOfVertices - 1;
                pl.SetPointAt(vertexIndex, new Point2d(newPt.X, newPt.Y));
            }
            else if (curve is Line line)
            {
                if (isStart) line.StartPoint = newPt;
                else line.EndPoint = newPt;
            }
        }
    }
}
