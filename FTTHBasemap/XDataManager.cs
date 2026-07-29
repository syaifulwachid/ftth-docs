using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace FTTHBasemap
{
    public class FTTHMetadata
    {
        public string SourceHandle { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // "LEFT" | "RIGHT" | "CENTER"
        public double RoadWidth { get; set; } = 0.0;
        public double OffsetDistance { get; set; } = 0.0;
        public string ObjectType { get; set; } = string.Empty; // "CENTERLINE" | "EDGE" | "TROTOAR" | "ROW"
    }

    public static class XDataManager
    {
        public const string AppName = "FTTH_BASEMAP";

        /// <summary>
        /// Registers the AppID for XData if it doesn't already exist.
        /// </summary>
        public static void RegisterApp(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(AppName))
                {
                    rat.UpgradeOpen();
                    using (RegAppTableRecord ratr = new RegAppTableRecord())
                    {
                        ratr.Name = AppName;
                        rat.Add(ratr);
                        tr.AddNewlyCreatedDBObject(ratr, true);
                    }
                    tr.Commit();
                }
            }
        }

        /// <summary>
        /// Writes FTTH metadata to a DBObject.
        /// </summary>
        public static void WriteMetadata(DBObject obj, FTTHMetadata data)
        {
            if (obj == null) return;

            Database db = obj.Database;
            RegisterApp(db);

            if (!obj.IsWriteEnabled)
            {
                obj.UpgradeOpen();
            }

            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, data.SourceHandle ?? string.Empty),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, data.Side ?? string.Empty),
                new TypedValue((int)DxfCode.ExtendedDataReal, data.RoadWidth),
                new TypedValue((int)DxfCode.ExtendedDataReal, data.OffsetDistance),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, data.ObjectType ?? string.Empty)
            ))
            {
                obj.XData = rb;
            }
        }

        /// <summary>
        /// Reads FTTH metadata from a DBObject. Returns null if XData is missing.
        /// </summary>
        public static FTTHMetadata ReadMetadata(DBObject obj)
        {
            if (obj == null) return null;

            using (ResultBuffer rb = obj.GetXDataForApplication(AppName))
            {
                if (rb == null) return null;

                TypedValue[] tvs = rb.AsArray();
                if (tvs.Length < 6) return null;

                try
                {
                    return new FTTHMetadata
                    {
                        SourceHandle = tvs[1].Value.ToString(),
                        Side = tvs[2].Value.ToString(),
                        RoadWidth = Convert.ToDouble(tvs[3].Value),
                        OffsetDistance = Convert.ToDouble(tvs[4].Value),
                        ObjectType = tvs[5].Value.ToString()
                    };
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets a specific field of the metadata from a DBObject.
        /// </summary>
        public static string GetMetadataType(DBObject obj)
        {
            var meta = ReadMetadata(obj);
            return meta != null ? meta.ObjectType : string.Empty;
        }

        public static double GetMetadataWidth(DBObject obj)
        {
            var meta = ReadMetadata(obj);
            return meta != null ? meta.RoadWidth : 0.0;
        }

        public static string GetMetadataSourceHandle(DBObject obj)
        {
            var meta = ReadMetadata(obj);
            return meta != null ? meta.SourceHandle : string.Empty;
        }
    }
}
