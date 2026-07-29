using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.GraphicsInterface;
using FTTHBasemap.Model;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace FTTHBasemap
{

    public static class SmartSmoothPolyline
    {
        private static Point3d _lastPoint;
        private static double _maxDistance;
        private static Line _transientLine;
        private static Point3d _currentConstrainedPoint;

        private static void OnPointMonitor(object sender, PointMonitorEventArgs e)
        {
            try
            {
                Point3d cursorPt = e.Context.ComputedPoint;
                double dist = _lastPoint.DistanceTo(cursorPt);
                Point3d constrainedPt;
                if (dist > _maxDistance)
                {
                    Vector3d vec = (cursorPt - _lastPoint).GetNormal();
                    constrainedPt = _lastPoint + vec * _maxDistance;
                }
                else
                {
                    constrainedPt = cursorPt;
                }

                _currentConstrainedPoint = constrainedPt;

                if (_transientLine != null)
                {
                    _transientLine.EndPoint = constrainedPt;
                    TransientManager.CurrentTransientManager.UpdateTransient(_transientLine, new IntegerCollection());
                }
            }
            catch (System.Exception)
            {
            }
        }

        public static void DrawSmartPolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            PromptPointResult ppr = ed.GetPoint("\nSpecify start point: ");
            if (ppr.Status != PromptStatus.OK)
            {
                return;
            }

            using (DocumentLock docLock = doc.LockDocument())
            {
                Point3dCollection points = new Point3dCollection();
                points.Add(ppr.Value);
                _lastPoint = ppr.Value;
                _maxDistance = settings.SmartPlineMaxDist;
                _currentConstrainedPoint = ppr.Value;

                // Create transient objects for dynamic preview
                Polyline transientPl = new Polyline();
                transientPl.ColorIndex = 256; // ByLayer (Default color)
                transientPl.AddVertexAt(0, new Point2d(ppr.Value.X, ppr.Value.Y), 0, 0, 0);

                _transientLine = new Line(ppr.Value, ppr.Value);
                _transientLine.ColorIndex = 1; // Red

                var tm = TransientManager.CurrentTransientManager;
                var ic = new IntegerCollection();

                tm.AddTransient(transientPl, TransientDrawingMode.DirectShortTerm, 128, ic);
                tm.AddTransient(_transientLine, TransientDrawingMode.DirectShortTerm, 128, ic);

                bool done = false;
                ed.PointMonitor += OnPointMonitor;
                int vertexIndex = 1;

                try
                {
                    while (!done)
                    {
                        PromptPointOptions ppo = new PromptPointOptions("\nSpecify next point or [Distance/Undo]: ");
                        ppo.Keywords.Add("Undo", "U", "Undo");
                        ppo.Keywords.Add("Distance", "D", "Distance");
                        ppo.AllowNone = true; // Enter/Space to finish

                        PromptPointResult pprNext = ed.GetPoint(ppo);

                        if (pprNext.Status == PromptStatus.Keyword)
                        {
                            if (pprNext.StringResult == "Undo")
                            {
                                if (points.Count > 1)
                                {
                                    points.RemoveAt(points.Count - 1);
                                    
                                    // Remove last vertex from transient polyline
                                    transientPl.RemoveVertexAt(transientPl.NumberOfVertices - 1);
                                    tm.UpdateTransient(transientPl, ic);

                                    Point3d prevPt = points[points.Count - 1];
                                    _transientLine.StartPoint = prevPt;
                                    _transientLine.EndPoint = prevPt;
                                    tm.UpdateTransient(_transientLine, ic);

                                    _lastPoint = prevPt;
                                    vertexIndex--;
                                    ed.WriteMessage("\nUndid last point.");
                                }
                                else
                                {
                                    ed.WriteMessage("\nNothing to undo.");
                                }
                            }
                            else if (pprNext.StringResult == "Distance")
                            {
                                PromptDoubleOptions pdo = new PromptDoubleOptions($"\nEnter new maximum distance <{_maxDistance:F2}>: ")
                                {
                                    AllowNegative = false,
                                    AllowZero = false
                                };
                                PromptDoubleResult pdr = ed.GetDouble(pdo);
                                if (pdr.Status == PromptStatus.OK)
                                {
                                    _maxDistance = pdr.Value;
                                    settings.SmartPlineMaxDist = pdr.Value;
                                    settings.Save();
                                }
                            }
                        }
                        else if (pprNext.Status == PromptStatus.OK)
                        {
                            Point3d newPt = _currentConstrainedPoint;
                            points.Add(newPt);

                            // Add to existing segments transient polyline
                            transientPl.AddVertexAt(transientPl.NumberOfVertices, new Point2d(newPt.X, newPt.Y), 0, 0, 0);
                            tm.UpdateTransient(transientPl, ic);

                            // Update active segment start point
                            _transientLine.StartPoint = newPt;
                            _transientLine.EndPoint = newPt;
                            tm.UpdateTransient(_transientLine, ic);

                            _lastPoint = newPt;
                            vertexIndex++;
                        }
                        else
                        {
                            done = true;
                        }
                    }
                }
                finally
                {
                    ed.PointMonitor -= OnPointMonitor;
                    tm.EraseTransient(transientPl, ic);
                    tm.EraseTransient(_transientLine, ic);
                    transientPl.Dispose();
                    _transientLine.Dispose();
                }

                if (points.Count > 1)
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        using (Polyline pl = new Polyline())
                        {
                            var layerRecord = tr.GetObject(db.Clayer, OpenMode.ForRead) as LayerTableRecord;
                            pl.Layer = layerRecord != null ? layerRecord.Name : "0";
                            for (int i = 0; i < points.Count; i++)
                            {
                                Point3d pt = points[i];
                                pl.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0, 0, 0);
                            }

                            btr.AppendEntity(pl);
                            tr.AddNewlyCreatedDBObject(pl, true);
                        }

                        tr.Commit();
                    }
                    ed.Regen();
                }
            }
        }

        public static void SmoothSelectedPolyline()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect Polyline to smooth: ");
            peo.SetRejectMessage("\nMust be a Polyline.");
            peo.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline pl = tr.GetObject(per.ObjectId, OpenMode.ForWrite) as Polyline;
                    if (pl != null)
                    {
                        // Get original points
                        List<Point2d> origPoints = new List<Point2d>();
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            origPoints.Add(pl.GetPoint2dAt(i));
                        }

                        // Smooth them
                        List<Point2d> newPoints = ProcessPoints(origPoints, settings.SmartPlineMaxDist, settings.SmoothIterations);

                        if (newPoints.Count >= 2)
                        {
                            // Update Polyline in-place safely without violating the 2-vertex constraint
                            int originalCount = pl.NumberOfVertices;
                            int newCount = newPoints.Count;

                            // 1. Update existing vertices
                            int minCount = Math.Min(originalCount, newCount);
                            for (int i = 0; i < minCount; i++)
                            {
                                pl.SetPointAt(i, newPoints[i]);
                            }

                            // 2. Add extra vertices if the new polyline has more vertices
                            if (newCount > originalCount)
                            {
                                for (int i = originalCount; i < newCount; i++)
                                {
                                    pl.AddVertexAt(i, newPoints[i], 0, 0, 0);
                                }
                            }
                            // 3. Remove excess vertices from the end if the new polyline has fewer vertices
                            else if (newCount < originalCount)
                            {
                                for (int i = originalCount - 1; i >= newCount; i--)
                                {
                                    pl.RemoveVertexAt(i);
                                }
                            }

                            ed.WriteMessage($"\nPolyline smoothed successfully from {originalCount} to {newPoints.Count} vertices.");
                        }
                    }
                    tr.Commit();
                }
            }
            ed.Regen();
        }

        private static List<Point2d> ProcessPoints(List<Point2d> pts, double maxDist, int iterations)
        {
            if (iterations <= 0 || pts.Count < 3)
                return pts;

            List<Point2d> finalPts = new List<Point2d>();
            List<Point2d> currentSequence = new List<Point2d> { pts[0] };

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Point2d p1 = pts[i];
                Point2d p2 = pts[i + 1];
                double dist = p1.GetDistanceTo(p2);

                if (dist <= maxDist)
                {
                    currentSequence.Add(p2);
                }
                else
                {
                    // Large distance segment. Smooth current accumulated sequence
                    List<Point2d> smoothed = SmoothChaikin(currentSequence, iterations);
                    if (finalPts.Count > 0)
                    {
                        smoothed.RemoveAt(0); // Avoid duplicates at connection point
                    }
                    finalPts.AddRange(smoothed);

                    // Start new sequence from the end of the straight segment
                    currentSequence = new List<Point2d> { p2 };
                }
            }

            // Process any remaining points in sequence
            if (currentSequence.Count > 0)
            {
                List<Point2d> smoothed = SmoothChaikin(currentSequence, iterations);
                if (finalPts.Count > 0 && smoothed.Count > 0)
                {
                    smoothed.RemoveAt(0);
                }
                finalPts.AddRange(smoothed);
            }

            return finalPts;
        }

        private static List<Point2d> ChaikinStep(List<Point2d> pts)
        {
            if (pts.Count < 3) return pts;

            List<Point2d> newPts = new List<Point2d> { pts[0] };
            bool isFirst = true;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Point2d p1 = pts[i];
                Point2d p2 = pts[i + 1];

                Point2d q = new Point2d(0.75 * p1.X + 0.25 * p2.X, 0.75 * p1.Y + 0.25 * p2.Y);
                Point2d r = new Point2d(0.25 * p1.X + 0.75 * p2.X, 0.25 * p1.Y + 0.75 * p2.Y);

                if (!isFirst)
                {
                    newPts.Add(q);
                }
                if (i < pts.Count - 2)
                {
                    newPts.Add(r);
                }

                isFirst = false;
            }

            newPts.Add(pts[pts.Count - 1]);
            return newPts;
        }

        private static List<Point2d> SmoothChaikin(List<Point2d> pts, int iterations)
        {
            List<Point2d> result = pts;
            for (int i = 0; i < iterations; i++)
            {
                result = ChaikinStep(result);
            }
            return result;
        }
    }
}
