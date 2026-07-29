using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using FTTHBasemap.UI;

namespace FTTHBasemap
{
    public static class CameraIndicatorManager
    {
        private static readonly List<DBObject> _transients = new List<DBObject>();
        private static Database _transientDb = null;

        /// <summary>
        /// Clears the transient graphics camera indicator.
        /// </summary>
        public static void ClearIndicator()
        {
            if (_transients.Count == 0) return;

            try
            {
                var tm = TransientManager.CurrentTransientManager;
                var emptyCol = new IntegerCollection();
                foreach (var obj in _transients)
                {
                    try
                    {
                        tm.EraseTransient(obj, emptyCol);
                    }
                    catch { }
                    obj.Dispose();
                }
            }
            catch { }
            _transients.Clear();
            _transientDb = null;
        }

        /// <summary>
        /// Updates the real-time transient camera position and heading indicator (arrow and circle) in model space.
        /// </summary>
        public static void UpdateIndicator(double lat, double lon, double heading)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                ClearIndicator();
                return;
            }

            Database db = doc.Database;

            // If database changed, clear old transients first
            if (_transientDb != db)
            {
                ClearIndicator();
            }

            double xCam = 0.0;
            double yCam = 0.0;

            try
            {
                Point3d camWcs = TileMapManager.ProjectLonLatToWcs(db, lon, lat);
                xCam = camWcs.X;
                yCam = camWcs.Y;
            }
            catch
            {
                ClearIndicator();
                return;
            }

            Point3d ptCam = new Point3d(xCam, yCam, 0);

            // 3. Compute arrow geometry based on camera heading
            double theta = heading * Math.PI / 180.0;
            double dx = Math.Sin(theta);
            double dy = Math.Cos(theta);
            Vector3d dir = new Vector3d(dx, dy, 0);
            Vector3d perp = new Vector3d(-dy, dx, 0);

            double length = 5.0; // total length of the arrow
            double arrowWidth = 1.2; // half-width of the arrowhead

            Point3d ptEnd = ptCam + dir * length;
            Point3d ptLeft = ptCam + dir * (length * 0.6) + perp * arrowWidth;
            Point3d ptRight = ptCam + dir * (length * 0.6) - perp * arrowWidth;

            // Lock document to safely interact with TransientManager
            using (doc.LockDocument())
            {
                try
                {
                    var tm = TransientManager.CurrentTransientManager;
                    var emptyCol = new IntegerCollection();

                    // Erase previous transients
                    foreach (var obj in _transients)
                    {
                        try
                        {
                            tm.EraseTransient(obj, emptyCol);
                        }
                        catch { }
                        obj.Dispose();
                    }
                    _transients.Clear();

                    // Create circle at camera position (Yellow)
                    Circle circle = new Circle
                    {
                        Center = ptCam,
                        Radius = 0.5,
                        ColorIndex = 2
                    };

                    // Create arrow as a continuous polyline (Yellow)
                    Autodesk.AutoCAD.DatabaseServices.Polyline arrow = new Autodesk.AutoCAD.DatabaseServices.Polyline();
                    arrow.AddVertexAt(0, new Point2d(ptCam.X, ptCam.Y), 0, 0, 0);
                    arrow.AddVertexAt(1, new Point2d(ptEnd.X, ptEnd.Y), 0, 0, 0);
                    arrow.AddVertexAt(2, new Point2d(ptLeft.X, ptLeft.Y), 0, 0, 0);
                    arrow.AddVertexAt(3, new Point2d(ptEnd.X, ptEnd.Y), 0, 0, 0);
                    arrow.AddVertexAt(4, new Point2d(ptRight.X, ptRight.Y), 0, 0, 0);
                    arrow.ColorIndex = 2;

                    // Display transients
                    tm.AddTransient(circle, TransientDrawingMode.Main, 128, emptyCol);
                    tm.AddTransient(arrow, TransientDrawingMode.Main, 128, emptyCol);

                    _transients.Add(circle);
                    _transients.Add(arrow);
                    _transientDb = db;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error drawing transient: " + ex.Message);
                }
            }
        }
    }
}
