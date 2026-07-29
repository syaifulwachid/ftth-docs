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
    public static class ScaleTools
    {
        public static List<Tuple<ObjectId, Point3d>> ActiveScaleSelection = new List<Tuple<ObjectId, Point3d>>();
        public static double PreviousScale { get; set; } = 1.0;
        public static double CurrentScale { get; set; } = 1.0;

        /// <summary>
        /// Command: FTTH_SCALEINPLACE. Starts modeless relative scaling mode.
        /// </summary>
        public static void StartScaleInPlace()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionResult psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nNo objects selected. Canceled.");
                return;
            }

            ActiveScaleSelection.Clear();
            PreviousScale = 1.0;
            CurrentScale = 1.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        Point3d center = GetCenterPoint(ent);
                        ActiveScaleSelection.Add(new Tuple<ObjectId, Point3d>(so.ObjectId, center));
                    }
                }
                tr.Commit();
            }

            ed.WriteMessage($"\n[FTTH] Scale-in-place mode active for {ActiveScaleSelection.Count} objects. Use the panel slider to scale.");
            PaletteManager.Log($"Scaling {ActiveScaleSelection.Count} objects.");
        }

        /// <summary>
        /// Applies relative scaling to active selection.
        /// </summary>
        public static void ApplyScaleFactor(double newScale)
        {
            if (ActiveScaleSelection.Count == 0) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;

            if (Math.Abs(newScale - PreviousScale) < 0.001) return;

            double rel = newScale / PreviousScale;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var item in ActiveScaleSelection)
                    {
                        try
                        {
                            Entity ent = tr.GetObject(item.Item1, OpenMode.ForWrite) as Entity;
                            if (ent != null)
                            {
                                Matrix3d mat = Matrix3d.Scaling(rel, item.Item2);
                                ent.TransformBy(mat);
                            }
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }

            PreviousScale = newScale;
            CurrentScale = newScale;
            doc.Editor.Regen();
        }

        /// <summary>
        /// Reverts scaling back to 1.0 and clears active selection.
        /// </summary>
        public static void RevertScale()
        {
            if (ActiveScaleSelection.Count == 0) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            double rel = 1.0 / CurrentScale;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var item in ActiveScaleSelection)
                    {
                        try
                        {
                            Entity ent = tr.GetObject(item.Item1, OpenMode.ForWrite) as Entity;
                            if (ent != null)
                            {
                                Matrix3d mat = Matrix3d.Scaling(rel, item.Item2);
                                ent.TransformBy(mat);
                            }
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }

            ActiveScaleSelection.Clear();
            PreviousScale = 1.0;
            CurrentScale = 1.0;
            doc.Editor.Regen();
            doc.Editor.WriteMessage("\nScale In Place reverted.");
            PaletteManager.Log("Scale reverted.");
        }

        /// <summary>
        /// Commits the current scale and clears active selection.
        /// </summary>
        public static void CommitScale()
        {
            ActiveScaleSelection.Clear();
            PreviousScale = 1.0;
            CurrentScale = 1.0;
            PaletteManager.Log("Scale committed.");
        }

        /// <summary>
        /// Command: FTTH_SCALEBASE. Match target heights/scales to source reference.
        /// </summary>
        public static void RunScaleBase()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect Reference Object (Block, Text, or MText): ");
            peo.SetRejectMessage("\nMust be BlockReference, Text, or MText.");
            peo.AddAllowedClass(typeof(BlockReference), true);
            peo.AddAllowedClass(typeof(DBText), true);
            peo.AddAllowedClass(typeof(MText), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            double refScale = 1.0;
            string srcType = "";

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBObject obj = tr.GetObject(per.ObjectId, OpenMode.ForRead);
                if (obj is BlockReference br)
                {
                    refScale = br.ScaleFactors.X;
                    srcType = "BLOCK";
                }
                else if (obj is DBText dt)
                {
                    refScale = dt.Height;
                    srcType = "TEXT";
                }
                else if (obj is MText mt)
                {
                    refScale = mt.Height;
                    srcType = "MTEXT";
                }
                tr.Commit();
            }

            ed.WriteMessage($"\nReference scale factor/height found: {refScale:F4} ({srcType}).");
            ed.WriteMessage("\nSelect target entities to match scale: ");

            PromptSelectionResult psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK) return;

            int count = 0;
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject so in psr.Value)
                    {
                        DBObject obj = tr.GetObject(so.ObjectId, OpenMode.ForWrite);
                        if (obj is BlockReference brTarget)
                        {
                            brTarget.ScaleFactors = new Scale3d(refScale, refScale, refScale);
                            count++;
                        }
                        else if (obj is DBText dtTarget)
                        {
                            dtTarget.Height = refScale;
                            count++;
                        }
                        else if (obj is MText mtTarget)
                        {
                            mtTarget.Height = refScale;
                            count++;
                        }
                    }
                    tr.Commit();
                }
            }

            ed.WriteMessage($"\nSuccessfully matched scale properties of {count} targets.");
            PaletteManager.Log($"Matched scale of {count} targets.");
            ed.Regen();
        }

        private static Point3d GetCenterPoint(Entity ent)
        {
            if (ent is BlockReference br)
            {
                return br.Position;
            }
            if (ent is DBText dt)
            {
                // If it is center/middle aligned, use AlignmentPoint, else use Position
                if (dt.Justify == AttachmentPoint.BaseLeft) return dt.Position;
                return dt.AlignmentPoint;
            }
            if (ent is MText mt)
            {
                return mt.Location;
            }

            // BoundingBox fallback
            Extents3d? ext = ent.Bounds;
            if (ext.HasValue)
            {
                Point3d min = ext.Value.MinPoint;
                Point3d max = ext.Value.MaxPoint;
                return new Point3d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);
            }

            return Point3d.Origin;
        }
    }
}
