using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System.Windows.Forms;

namespace FTTHBasemap
{
    public static class FtthPlines
    {
        private const double MarkerSize = 1.625;

        private static void AllowCurveTypes(PromptEntityOptions options)
        {
            options.AddAllowedClass(typeof(Polyline), false);
            options.AddAllowedClass(typeof(Line), false);
            options.AddAllowedClass(typeof(Polyline2d), false);
            options.AddAllowedClass(typeof(Polyline3d), false);
        }

        private static void CreateConnectingPolyline(BlockTableRecord btr, Transaction tr, Point3d ptStart, Point3d ptEnd)
        {
            Database db = btr.Database;
            var settings = Model.BasemapSettings.Instance;
            string targetLayer = settings.LayerParcel;
            short colorIndex = settings.ColorParcel;

            // Ensure the layer exists
            UI.BasemapPanel.GetOrCreateLayer(db, tr, targetLayer, colorIndex);

            // If the Z coordinates are practically identical, we can use a standard lightweight Polyline
            if (Math.Abs(ptStart.Z - ptEnd.Z) < 1e-6)
            {
                using (Polyline pl = new Polyline())
                {
                    pl.AddVertexAt(0, new Point2d(ptStart.X, ptStart.Y), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(ptEnd.X, ptEnd.Y), 0, 0, 0);
                    pl.Elevation = ptStart.Z;
                    pl.Layer = targetLayer;
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                }
            }
            else
            {
                // If the Z coordinates are different, a 2D Polyline cannot span them.
                // We create a Polyline3d to ensure they are connected and touch in 3D.
                Point3dCollection pts = new Point3dCollection(new Point3d[] { ptStart, ptEnd });
                using (Polyline3d pl3d = new Polyline3d(Poly3dType.SimplePoly, pts, false))
                {
                    pl3d.Layer = targetLayer;
                    btr.AppendEntity(pl3d);
                    tr.AddNewlyCreatedDBObject(pl3d, true);
                }
            }
        }

        [CommandMethod("FTTH_PLINES")]
        public static void RunFtthPlines()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            bool continueLoop = true;

            while (continueLoop)
            {
                // Directly prompt for the first curve.
                PromptEntityOptions peo1 = new PromptEntityOptions("\nPilih garis/polyline PERTAMA (Asal) [ENTER untuk keluar]: ");
                peo1.SetRejectMessage("\nObjek harus berupa garis (Line atau Polyline).");
                AllowCurveTypes(peo1);
                peo1.AllowNone = true;
                
                PromptEntityResult per1 = ed.GetEntity(peo1);

                if (per1.Status == PromptStatus.None || per1.Status == PromptStatus.Cancel)
                {
                    ed.WriteMessage("\nKeluar dari FTTH_PLINES.");
                    continueLoop = false;
                    break;
                }

                if (per1.Status != PromptStatus.OK)
                {
                    continue;
                }

                // Check if Alt was held down during the click selection of the first object
                bool isAltMode = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;

                ObjectId ent1 = per1.ObjectId;
                Point3d ptClickA = per1.PickedPoint;

                if (isAltMode)
                {
                    // --- MODE A-B: Pilih dua titik di layar (Alt tahan saat klik ke-1) ---
                    Point3d ptStart;

                    // Get projected start point on curve1
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Curve curve1 = tr.GetObject(ent1, OpenMode.ForRead) as Curve;
                        if (curve1 == null)
                        {
                            tr.Commit();
                            continue;
                        }
                        ptStart = curve1.GetClosestPointTo(ptClickA, false);
                        tr.Commit();
                    }

                    // Draw temporary marker circle at ptStart
                    ObjectId markerId = ObjectId.Null;
                    using (DocumentLock docLock = doc.LockDocument())
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                            using (Circle marker = new Circle())
                            {
                                marker.Center = ptStart;
                                marker.Radius = MarkerSize / 4.0;
                                marker.ColorIndex = 30; // Orange
                                marker.Layer = "0";
                                markerId = btr.AppendEntity(marker);
                                tr.AddNewlyCreatedDBObject(marker, true);
                            }
                            tr.Commit();
                        }
                    }

                    // Force redraw to display the marker
                    ed.UpdateScreen();

                    // Prompt for Point B on a polyline/line
                    PromptEntityOptions peoB = new PromptEntityOptions("\nPilih titik B pada polyline (klik tepat pada titik): ");
                    peoB.SetRejectMessage("\nObjek harus berupa garis (Line atau Polyline).");
                    AllowCurveTypes(peoB);
                    PromptEntityResult perB = ed.GetEntity(peoB);

                    if (perB.Status == PromptStatus.OK)
                    {
                        using (DocumentLock docLock = doc.LockDocument())
                        {
                            using (Transaction tr = db.TransactionManager.StartTransaction())
                            {
                                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                                Curve curve2 = tr.GetObject(perB.ObjectId, OpenMode.ForRead) as Curve;
                                if (curve2 != null)
                                {
                                    Point3d ptEnd = curve2.GetClosestPointTo(perB.PickedPoint, false);

                                    // Make polyline between ptStart and ptEnd.
                                    // Snaps perfectly at both ends
                                    CreateConnectingPolyline(btr, tr, ptStart, ptEnd);
                                    ed.WriteMessage("\nGaris A-B berhasil dibuat.");
                                }

                                // Erase temporary marker
                                if (markerId != ObjectId.Null)
                                {
                                    DBObject markerObj = tr.GetObject(markerId, OpenMode.ForWrite);
                                    markerObj.Erase();
                                }

                                tr.Commit();
                            }
                        }
                    }
                    else
                    {
                        using (DocumentLock docLock = doc.LockDocument())
                        {
                            using (Transaction tr = db.TransactionManager.StartTransaction())
                            {
                                if (markerId != ObjectId.Null)
                                {
                                    DBObject markerObj = tr.GetObject(markerId, OpenMode.ForWrite);
                                    markerObj.Erase();
                                }
                                tr.Commit();
                            }
                        }
                        ed.WriteMessage("\nB dibatalkan.");
                    }
                }
                else
                {
                    // --- MODE BIASA: Pilih dua objek polyline/line ---
                    PromptEntityOptions peo2 = new PromptEntityOptions("\nPilih garis/polyline KEDUA (Tujuan): ");
                    peo2.SetRejectMessage("\nObjek harus berupa garis (Line atau Polyline).");
                    AllowCurveTypes(peo2);
                    PromptEntityResult per2 = ed.GetEntity(peo2);

                    if (per2.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nTidak ada objek terpilih sebagai tujuan.");
                        continue;
                    }

                    ObjectId ent2 = per2.ObjectId;

                    ed.WriteMessage("\nKlik titik-titik di sepanjang garis pertama. Tekan ENTER/SPASI jika selesai.");

                    List<Point3d> pts = new List<Point3d>();
                    List<ObjectId> tempMarkers = new List<ObjectId>();

                    // Temp disable OSNAP during clicking (OSMODE=0)
                    short oldOsmode = (short)Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("OSMODE");
                    Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("OSMODE", 0);

                    try
                    {
                        while (true)
                        {
                            PromptPointOptions ppoMarker = new PromptPointOptions("\nTentukan titik marker: ");
                            ppoMarker.AllowNone = true; // Allow Enter/Space to return None and finish selection
                            PromptPointResult pprMarker = ed.GetPoint(ppoMarker);

                            if (pprMarker.Status != PromptStatus.OK)
                                break;

                            Point3d ptStart;

                            using (Transaction tr = db.TransactionManager.StartTransaction())
                            {
                                Curve curve1 = tr.GetObject(ent1, OpenMode.ForRead) as Curve;
                                if (curve1 == null)
                                {
                                    tr.Commit();
                                    break;
                                }
                                ptStart = curve1.GetClosestPointTo(pprMarker.Value, false);
                                tr.Commit();
                            }

                            pts.Add(ptStart);

                            // Draw temp marker
                            using (DocumentLock docLock = doc.LockDocument())
                            {
                                using (Transaction tr = db.TransactionManager.StartTransaction())
                                {
                                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                                    using (Circle c = new Circle())
                                    {
                                        c.Center = ptStart;
                                        c.Radius = MarkerSize / 4.0;
                                        c.ColorIndex = 30; // Orange
                                        c.Layer = "0";
                                        tempMarkers.Add(btr.AppendEntity(c));
                                        tr.AddNewlyCreatedDBObject(c, true);
                                    }
                                    tr.Commit();
                                }
                            }

                            ed.UpdateScreen();
                        }
                    }
                    finally
                    {
                        // Restore OSMODE
                        Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("OSMODE", oldOsmode);
                    }

                    if (pts.Count > 0)
                    {
                        using (DocumentLock docLock = doc.LockDocument())
                        {
                            using (Transaction tr = db.TransactionManager.StartTransaction())
                            {
                                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                                Curve curve2 = tr.GetObject(ent2, OpenMode.ForRead) as Curve;
                                if (curve2 != null)
                                {
                                    foreach (Point3d p in pts)
                                    {
                                        Point3d ptEnd = curve2.GetClosestPointTo(p, false);

                                        // Make polyline between p (projected on curve1) and ptEnd (projected on curve2)
                                        // Snaps perfectly at both ends
                                        CreateConnectingPolyline(btr, tr, p, ptEnd);
                                    }
                                }

                                // Clean up temp markers
                                foreach (ObjectId mId in tempMarkers)
                                {
                                    DBObject obj = tr.GetObject(mId, OpenMode.ForWrite);
                                    obj.Erase();
                                }

                                tr.Commit();
                            }
                        }

                        ed.WriteMessage($"\nSelesai! {pts.Count} polyline telah dibuat. Marker dibersihkan.");
                    }
                    else
                    {
                        // Clean up temp markers if canceled
                        if (tempMarkers.Count > 0)
                        {
                            using (DocumentLock docLock = doc.LockDocument())
                            {
                                using (Transaction tr = db.TransactionManager.StartTransaction())
                                {
                                    foreach (ObjectId mId in tempMarkers)
                                    {
                                        DBObject obj = tr.GetObject(mId, OpenMode.ForWrite);
                                        obj.Erase();
                                    }
                                    tr.Commit();
                                }
                            }
                        }
                        ed.WriteMessage("\nProses dibatalkan.");
                    }
                }
                ed.Regen();
            }
        }
    }
}
