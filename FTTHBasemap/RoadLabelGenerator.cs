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
    public static class RoadLabelGenerator
    {
        private const string RegAppName = "FTTH_ROAD_HANDLE";

        public static List<ObjectId> ActivePreviewLabels = new List<ObjectId>();
        public static List<ObjectId> ActiveCenterlines = new List<ObjectId>();
        public static bool IsLiveSessionActive = false;

        public static bool StartLiveSession()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ActiveCenterlines.Clear();
            ActivePreviewLabels.Clear();

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Ensure RegAppName exists
                    RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!rat.Has(RegAppName))
                    {
                        if (!rat.IsWriteEnabled) rat.UpgradeOpen();
                        using (RegAppTableRecord ratr = new RegAppTableRecord())
                        {
                            ratr.Name = RegAppName;
                            rat.Add(ratr);
                            tr.AddNewlyCreatedDBObject(ratr, true);
                        }
                    }

                    // Setup target layer
                    GetOrCreateLayer(db, tr, settings.LayerRoadLabel, settings.ColorRoadLabel);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1. Gather all processed road handles from existing labels
                    HashSet<string> processedHandles = new HashSet<string>();
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        MText mt = tr.GetObject(id, OpenMode.ForRead) as MText;
                        if (mt != null && mt.Layer.Equals(settings.LayerRoadLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            ResultBuffer rb = mt.GetXDataForApplication(RegAppName);
                            if (rb != null)
                            {
                                foreach (TypedValue val in rb)
                                {
                                    if (val.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                    {
                                        string handleStr = val.Value as string;
                                        if (!string.IsNullOrEmpty(handleStr))
                                        {
                                            processedHandles.Add(handleStr);
                                        }
                                    }
                                }
                                rb.Dispose();
                            }
                        }
                    }

                    // 2. Scan for centerline curves on LayerCenterline
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c != null && c.Layer.Equals(settings.LayerCenterline, StringComparison.OrdinalIgnoreCase))
                        {
                            string hStr = c.Handle.ToString();
                            if (!processedHandles.Contains(hStr))
                            {
                                ActiveCenterlines.Add(id);
                            }
                        }
                    }

                    tr.Commit();
                }
            }

            if (ActiveCenterlines.Count == 0)
            {
                return false;
            }

            IsLiveSessionActive = true;
            return true;
        }

        public static void UpdateLivePreview(double minDist)
        {
            if (!IsLiveSessionActive || ActiveCenterlines.Count == 0) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            var settings = BasemapSettings.Instance;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1. Erase previous preview labels
                    foreach (ObjectId id in ActivePreviewLabels)
                    {
                        if (!id.IsErased)
                        {
                            DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                            if (obj != null)
                            {
                                obj.Erase();
                            }
                        }
                    }
                    ActivePreviewLabels.Clear();

                    // 2. Generate new preview labels
                    Random rand = new Random(12345);
                    foreach (ObjectId lineId in ActiveCenterlines)
                    {
                        try
                        {
                            Curve cl = tr.GetObject(lineId, OpenMode.ForRead) as Curve;
                            if (cl == null) continue;

                            double len = cl.GetDistanceAtParameter(cl.EndParam);
                            string lineHandle = cl.Handle.ToString();

                            if (len <= 0.001) continue;

                            if (len < minDist)
                            {
                                double midDist = len / 2.0;
                                Point3d pt = cl.GetPointAtDist(midDist);
                                double ang = GetTangentAngleAtDistance(cl, midDist);

                                MText mt = new MText();
                                mt.Layer = settings.LayerRoadLabel;
                                mt.Attachment = AttachmentPoint.MiddleCenter;
                                mt.Contents = "Jl. Nama Jalan";
                                mt.Location = pt;
                                mt.Height = 2.5;
                                mt.Rotation = ang;

                                btr.AppendEntity(mt);
                                tr.AddNewlyCreatedDBObject(mt, true);
                                ActivePreviewLabels.Add(mt.ObjectId);

                                using (ResultBuffer rb = new ResultBuffer(
                                    new TypedValue((int)DxfCode.Start, RegAppName),
                                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, lineHandle)
                                ))
                                {
                                    mt.XData = rb;
                                }
                            }
                            else
                            {
                                double currentDist = minDist / 2.0;
                                while (currentDist < len)
                                {
                                    Point3d pt = cl.GetPointAtDist(currentDist);
                                    double ang = GetTangentAngleAtDistance(cl, currentDist);

                                    MText mt = new MText();
                                    mt.Layer = settings.LayerRoadLabel;
                                    mt.Attachment = AttachmentPoint.MiddleCenter;
                                    mt.Contents = "Jl. Nama Jalan";
                                    mt.Location = pt;
                                    mt.Height = 2.5;
                                    mt.Rotation = ang;

                                    btr.AppendEntity(mt);
                                    tr.AddNewlyCreatedDBObject(mt, true);
                                    ActivePreviewLabels.Add(mt.ObjectId);

                                    using (ResultBuffer rb = new ResultBuffer(
                                        new TypedValue((int)DxfCode.Start, RegAppName),
                                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, lineHandle)
                                    ))
                                    {
                                        mt.XData = rb;
                                    }

                                    double distStep = minDist + rand.NextDouble() * 40.0;
                                    currentDist += distStep;
                                }
                            }
                        }
                        catch { }
                    }

                    tr.Commit();
                }
            }

            doc.Editor.Regen();
        }

        public static void CommitLiveSession()
        {
            if (!IsLiveSessionActive) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage($"\n[FTTH] Committed {ActivePreviewLabels.Count} road labels.");
            }

            ActivePreviewLabels.Clear();
            ActiveCenterlines.Clear();
            IsLiveSessionActive = false;
        }

        public static void RevertLiveSession()
        {
            if (!IsLiveSessionActive) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ActivePreviewLabels)
                    {
                        if (!id.IsErased)
                        {
                            DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                            if (obj != null)
                            {
                                obj.Erase();
                            }
                        }
                    }
                    tr.Commit();
                }
            }

            ActivePreviewLabels.Clear();
            ActiveCenterlines.Clear();
            IsLiveSessionActive = false;

            doc.Editor.Regen();
            doc.Editor.WriteMessage("\nRoad labels reverted.");
        }

        public static void GenerateRoadLabels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;
            double minDist = settings.RoadLabelDist;

            ed.WriteMessage($"\n[FTTH] Generating road labels (Min Spacing: {minDist}m)...");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!rat.Has(RegAppName))
                    {
                        if (!rat.IsWriteEnabled) rat.UpgradeOpen();
                        using (RegAppTableRecord ratr = new RegAppTableRecord())
                        {
                            ratr.Name = RegAppName;
                            rat.Add(ratr);
                            tr.AddNewlyCreatedDBObject(ratr, true);
                        }
                    }

                    GetOrCreateLayer(db, tr, settings.LayerRoadLabel, settings.ColorRoadLabel);

                    HashSet<string> processedHandles = new HashSet<string>();
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        MText mt = tr.GetObject(id, OpenMode.ForRead) as MText;
                        if (mt != null && mt.Layer.Equals(settings.LayerRoadLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            ResultBuffer rb = mt.GetXDataForApplication(RegAppName);
                            if (rb != null)
                            {
                                foreach (TypedValue val in rb)
                                {
                                    if (val.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                    {
                                        string handleStr = val.Value as string;
                                        if (!string.IsNullOrEmpty(handleStr))
                                        {
                                            processedHandles.Add(handleStr);
                                        }
                                    }
                                }
                                rb.Dispose();
                            }
                        }
                    }

                    List<Curve> centerlines = new List<Curve>();
                    foreach (ObjectId id in btr)
                    {
                        if (id.IsErased) continue;
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c != null && c.Layer.Equals(settings.LayerCenterline, StringComparison.OrdinalIgnoreCase))
                        {
                            string hStr = c.Handle.ToString();
                            if (!processedHandles.Contains(hStr))
                            {
                                centerlines.Add(c);
                            }
                        }
                    }

                    if (centerlines.Count == 0)
                    {
                        ed.WriteMessage("\nAll roads are already labeled or no centerlines found.");
                        tr.Commit();
                        return;
                    }

                    ed.WriteMessage($"\nFound {centerlines.Count} new road segments to label.");
                    int labelsCreated = 0;
                    Random rand = new Random(12345);

                    foreach (Curve cl in centerlines)
                    {
                        try
                        {
                            double startParam = cl.StartParam;
                            double endParam = cl.EndParam;
                            double len = cl.GetDistanceAtParameter(endParam);
                            string lineHandle = cl.Handle.ToString();

                            if (len <= 0.001) continue;

                            if (len < minDist)
                            {
                                double midDist = len / 2.0;
                                Point3d pt = cl.GetPointAtDist(midDist);
                                double ang = GetTangentAngleAtDistance(cl, midDist);

                                CreateLabelEntity(btr, tr, settings, pt, ang, lineHandle);
                                labelsCreated++;
                            }
                            else
                            {
                                double currentDist = minDist / 2.0;
                                while (currentDist < len)
                                {
                                    Point3d pt = cl.GetPointAtDist(currentDist);
                                    double ang = GetTangentAngleAtDistance(cl, currentDist);

                                    CreateLabelEntity(btr, tr, settings, pt, ang, lineHandle);
                                    labelsCreated++;

                                    double distStep = minDist + rand.NextDouble() * 40.0;
                                    currentDist += distStep;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage($"\nError labeling segment {cl.Handle}: {ex.Message}");
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nSuccessfully generated {labelsCreated} road labels.");
                }
            }
            ed.Regen();
        }

        private static double GetTangentAngleAtDistance(Curve c, double dist)
        {
            Point3d pt = c.GetPointAtDist(dist);
            double param = c.GetParameterAtPoint(pt);
            Vector3d tangent = c.GetFirstDerivative(param);
            double ang = Math.Atan2(tangent.Y, tangent.X);

            while (ang > Math.PI / 2.0) ang -= Math.PI;
            while (ang <= -Math.PI / 2.0) ang += Math.PI;

            return ang;
        }

        private static void CreateLabelEntity(BlockTableRecord btr, Transaction tr, BasemapSettings settings, Point3d pt, double angle, string lineHandle)
        {
            MText mt = new MText();
            mt.Layer = settings.LayerRoadLabel;
            mt.Attachment = AttachmentPoint.MiddleCenter;
            mt.Contents = "Jl. Nama Jalan";
            mt.Location = pt;
            mt.Height = 2.5;
            mt.Rotation = angle;

            btr.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);

            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.Start, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, lineHandle)
            ))
            {
                mt.XData = rb;
            }
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];

            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
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
