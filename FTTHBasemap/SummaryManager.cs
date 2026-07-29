using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using FTTHBasemap.UI;

namespace FTTHBasemap
{
    public static class SummaryManager
    {
        public class SummaryData
        {
            public int NewPole7M4 { get; set; }
            public int NewPole7M3 { get; set; }
            public int NewPole7M25 { get; set; }
            public int NewPole9M4 { get; set; }

            public int ExtEmr7M4 { get; set; }
            public int ExtEmr7M3 { get; set; }
            public int ExtEmr7M25 { get; set; }
            public int ExtEmr9M4 { get; set; }

            public int ExtTelkom { get; set; }
            public int ExtMti { get; set; }
            public int ExtLinknet { get; set; }
            public int TotalPoleAllType { get; set; }

            public int HomepassCount { get; set; }
            public int Fdt48Count { get; set; }
            public int Fdt72Count { get; set; }

            public int FatAerialCount { get; set; }
            public int FatPedestalCount { get; set; }

            public int Closure144Count { get; set; }
            public int Closure48Count { get; set; }

            public int Handhole40Count { get; set; }
            public int Handhole20Count { get; set; }

            public int HangerCount { get; set; }

            public double Len72C { get; set; }
            public double Len48C { get; set; }
            public double Len36C { get; set; }
            public double Len24C { get; set; }

            public double TotalAerialRoute { get; set; }
            public double TotalDuctRoute { get; set; }
            public double TotalRouteLengthCable { get; set; }
        }

        private static bool IsPoleBlock(string blockName, string layerName)
        {
            string upperBlock = blockName.ToUpper();
            string upperLayer = layerName.ToUpper();

            if (upperBlock == "NP725" || upperBlock == "NP73" || upperBlock == "NP74" || upperBlock == "NP94" ||
                upperBlock == "EP725" || upperBlock == "EP73" || upperBlock == "EP74" || upperBlock == "EP94" ||
                upperBlock == "EXT_POLE" || upperBlock == "POLE73IN" || upperBlock == "POLE73EX" || upperBlock == "POLETEL" ||
                upperBlock == "EXT TEL")
            {
                return true;
            }

            if (upperLayer.StartsWith("FTTH-POLE-") || upperLayer == "EXT POLE" || upperLayer == "NEW POLE 7M 2.5INCH")
            {
                return true;
            }

            return false;
        }

        private static int GetCableCores(Polyline poly, Database db, Transaction tr)
        {
            int cores = 24; // Default fallback
            bool capacityFound = false;

            // 1. Check cable layer name (Highest Priority)
            string layerUpper = poly.Layer.ToUpper();
            int[] possibleCores = { 288, 144, 96, 72, 48, 36, 24, 12, 8, 4 };
            foreach (int cVal in possibleCores)
            {
                if (layerUpper.Contains(cVal.ToString()))
                {
                    cores = cVal;
                    capacityFound = true;
                    break;
                }
            }

            // 2. Check polyline explicit linetype name
            if (!capacityFound && !poly.Linetype.Equals("ByLayer", StringComparison.OrdinalIgnoreCase))
            {
                string ltUpper = poly.Linetype.ToUpper();
                foreach (int cVal in possibleCores)
                {
                    if (ltUpper.Contains(cVal.ToString()))
                    {
                        cores = cVal;
                        capacityFound = true;
                        break;
                    }
                }
            }

            // 3. Check layer linetype name
            if (!capacityFound)
            {
                try
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(poly.Layer))
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[poly.Layer], OpenMode.ForRead);
                        LinetypeTableRecord ltrLt = (LinetypeTableRecord)tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead);
                        string layerLtUpper = ltrLt.Name.ToUpper();
                        foreach (int cVal in possibleCores)
                        {
                            if (layerLtUpper.Contains(cVal.ToString()))
                            {
                                cores = cVal;
                                capacityFound = true;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            return cores;
        }

        public static SummaryData CalculateSummaryData(Database db, int scenario = 1, int slackFdt = 1, double tolerance = 5.0)
        {
            SummaryData data = new SummaryData();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var polePositions = new List<Point3d>();
                var cablePolylines = new List<Polyline>();
                var fatPositions = new List<Point3d>();

                // First pass: collect all cables and FATs for Scenario 2 mapping
                foreach (ObjectId id in modelSpace)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                    if (obj is Entity entity)
                    {
                        string lyrUpper = entity.Layer.ToUpper();
                        if (lyrUpper == "DESIGN SUMMARY" || lyrUpper == "ETIKET EMR-FH" || lyrUpper == "LEGEND" || lyrUpper == "LEGENDA")
                        {
                            continue;
                        }
                    }

                    if (obj is Polyline poly && poly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                    {
                        cablePolylines.Add(poly);
                    }
                    else if (obj is BlockReference br)
                    {
                        string bName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            try
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                bName = btr.Name;
                            }
                            catch { }
                        }

                        bool isFat = bName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     bName.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     br.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     br.Layer.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isFat)
                        {
                            fatPositions.Add(br.Position);
                        }
                    }
                }

                // Map FAT positions to closest cable polyline (within 5.0 meters)
                var fatBlockMapping = new Dictionary<ObjectId, int>();
                foreach (var poly in cablePolylines)
                {
                    fatBlockMapping[poly.ObjectId] = 0;
                }

                foreach (var fatPos in fatPositions)
                {
                    Polyline closestPoly = null;
                    double minDistance = double.MaxValue;

                    foreach (var poly in cablePolylines)
                    {
                        try
                        {
                            Point3d closestPt = poly.GetClosestPointTo(fatPos, false);
                            double dist = fatPos.DistanceTo(closestPt);
                            if (dist < minDistance)
                            {
                                minDistance = dist;
                                closestPoly = poly;
                            }
                        }
                        catch { }
                    }

                    if (closestPoly != null && minDistance < 5.0)
                    {
                        fatBlockMapping[closestPoly.ObjectId]++;
                    }
                }

                foreach (ObjectId id in modelSpace)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                    if (obj is Entity entity)
                    {
                        string lyrUpper = entity.Layer.ToUpper();
                        if (lyrUpper == "DESIGN SUMMARY" || lyrUpper == "ETIKET EMR-FH" || lyrUpper == "LEGEND" || lyrUpper == "LEGENDA")
                        {
                            continue;
                        }
                    }

                    // Count Poles
                    if (obj is BlockReference br)
                    {
                        string bName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            try
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                bName = btr.Name;
                            }
                            catch { }
                        }

                        bool isPole = IsPoleBlock(bName, br.Layer) ||
                                      bName.ToUpper().Contains("POLE") ||
                                      bName.ToUpper().Contains("TIANG") ||
                                      br.Layer.ToUpper().Contains("POLE") ||
                                      br.Layer.ToUpper().Contains("TIANG");

                        if (isPole)
                        {
                            polePositions.Add(br.Position);

                            string upperBlock = bName.ToUpper();
                            string upperLayer = br.Layer.ToUpper();

                            bool isExisting = upperBlock.Contains("EXT") || 
                                              upperBlock.Contains("EP") || 
                                              upperLayer.Contains("EXISTING") || 
                                              upperLayer.Contains("EXIST") || 
                                              upperBlock.StartsWith("EP");

                            if (isExisting)
                            {
                                if (upperBlock.Contains("TEL") || upperLayer.Contains("TEL"))
                                {
                                    data.ExtTelkom++;
                                }
                                else if (upperBlock.Contains("MTI") || upperLayer.Contains("MTI"))
                                {
                                    data.ExtMti++;
                                }
                                else if (upperBlock.Contains("LINKNET") || upperBlock.Contains("LNET") || upperLayer.Contains("LINKNET") || upperLayer.Contains("LNET"))
                                {
                                    data.ExtLinknet++;
                                }
                                else // Existing EMR Pole
                                {
                                    if (upperBlock.Contains("94") || upperBlock.Contains("9M") || upperLayer.Contains("9M"))
                                        data.ExtEmr9M4++;
                                    else if (upperBlock.Contains("74") || upperLayer.Contains("7M 4") || upperLayer.Contains("7M-4"))
                                        data.ExtEmr7M4++;
                                    else if (upperBlock.Contains("73") || upperLayer.Contains("7M 3") || upperLayer.Contains("7M-3"))
                                        data.ExtEmr7M3++;
                                    else if (upperBlock.Contains("725") || upperBlock.Contains("2.5") || upperLayer.Contains("7M 2.5") || upperLayer.Contains("7M-2.5") || upperLayer.Contains("2.5"))
                                        data.ExtEmr7M25++;
                                }
                            }
                            else // New Pole
                            {
                                if (upperBlock.Contains("94") || upperBlock.Contains("9M") || upperLayer.Contains("9M"))
                                    data.NewPole9M4++;
                                else if (upperBlock.Contains("74") || upperLayer.Contains("7M 4") || upperLayer.Contains("7M-4"))
                                    data.NewPole7M4++;
                                else if (upperBlock.Contains("73") || upperLayer.Contains("7M 3") || upperLayer.Contains("7M-3"))
                                    data.NewPole7M3++;
                                else if (upperBlock.Contains("725") || upperBlock.Contains("2.5") || upperLayer.Contains("7M 2.5") || upperLayer.Contains("7M-2.5") || upperLayer.Contains("2.5"))
                                    data.NewPole7M25++;
                            }
                        }

                        // Count FAT
                        bool isFat = bName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     bName.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     br.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     br.Layer.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isFat)
                        {
                            bool isPedestal = bName.IndexOf("PEDESTAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              bName.IndexOf("PED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              br.Layer.IndexOf("PEDESTAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              br.Layer.IndexOf("UG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              br.Layer.IndexOf("UNDERGROUND", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isPedestal) data.FatPedestalCount++;
                            else data.FatAerialCount++;
                        }

                        // Count Closures
                        bool isClosure = bName.IndexOf("CLOSURE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         bName.IndexOf("JC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         br.Layer.IndexOf("CLOSURE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         br.Layer.IndexOf("JC", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isClosure)
                        {
                            if (bName.Contains("144") || br.Layer.Contains("144"))
                                data.Closure144Count++;
                            else
                                data.Closure48Count++;
                        }

                        // Count Handholes
                        bool isHH = bName.IndexOf("HANDHOLE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    bName.IndexOf("HH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    bName.IndexOf("PIT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    br.Layer.IndexOf("HANDHOLE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    br.Layer.IndexOf("HH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    br.Layer.IndexOf("PIT", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isHH)
                        {
                            if (bName.Contains("20") || bName.IndexOf("PIT", StringComparison.OrdinalIgnoreCase) >= 0)
                                data.Handhole20Count++;
                            else
                                data.Handhole40Count++;
                        }

                        // Count Loop Hangers
                        if (bName.IndexOf("HANGER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            bName.IndexOf("HOLDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            bName.IndexOf("LOOP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            br.Layer.IndexOf("HANGER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            br.Layer.IndexOf("HOLDER", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            data.HangerCount++;
                        }
                    }

                    // Count Homepass (TK) Texts
                    // Count Homepass (HP) Texts on layer "FTTH-NOMOR-RUMAH" (Active subscribers)
                    else if (obj is DBText dt)
                    {
                        if (dt.Layer.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                        {
                            data.HomepassCount++;
                        }
                    }
                    else if (obj is MText mt)
                    {
                        if (mt.Layer.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                        {
                            data.HomepassCount++;
                        }
                    }

                    // Count Cables (Polylines)
                    else if (obj is Polyline poly)
                    {
                        string layerName = poly.Layer;
                        if (layerName.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                        {
                            int cores = GetCableCores(poly, db, tr);
                            double finalLength = poly.Length;

                            if (scenario == 2)
                            {
                                int routeLength = (int)Math.Round(poly.Length);
                                int fatCount = fatBlockMapping.ContainsKey(poly.ObjectId) ? fatBlockMapping[poly.ObjectId] : 0;
                                int totalSlackCount = slackFdt + fatCount;
                                int slackLength = totalSlackCount * 20;
                                int routePlusSlack = routeLength + slackLength;
                                int toleranceVal = (int)Math.Round(routePlusSlack * (tolerance / 100.0));
                                finalLength = routePlusSlack + toleranceVal;
                            }

                            bool isAerial = poly.Layer.IndexOf("UG", StringComparison.OrdinalIgnoreCase) < 0 &&
                                            poly.Layer.IndexOf("UNDERGROUND", StringComparison.OrdinalIgnoreCase) < 0;

                            if (isAerial)
                            {
                                data.TotalAerialRoute += finalLength;
                                if (cores == 72) data.Len72C += finalLength;
                                else if (cores == 48) data.Len48C += finalLength;
                                else if (cores == 36) data.Len36C += finalLength;
                                else if (cores == 24) data.Len24C += finalLength;
                            }
                            else
                            {
                                data.TotalDuctRoute += finalLength;
                            }
                        }
                    }
                }

                // Scan again for FDT blocks (filtering out legends)
                foreach (ObjectId id in modelSpace)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                    if (obj is Entity entity)
                    {
                        string lyrUpper = entity.Layer.ToUpper();
                        if (lyrUpper == "DESIGN SUMMARY" || lyrUpper == "ETIKET EMR-FH" || lyrUpper == "LEGEND" || lyrUpper == "LEGENDA")
                        {
                            continue;
                        }
                    }

                    if (obj is BlockReference br)
                    {
                        string bName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            try
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                bName = btr.Name;
                            }
                            catch { }
                        }

                        if (bName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            bool isLegend = true;
                            foreach (var polePos in polePositions)
                            {
                                if (br.Position.DistanceTo(polePos) < 2.0)
                                {
                                    isLegend = false;
                                    break;
                                }
                            }

                            if (isLegend) continue;

                            string fdtCapacity = "";
                            if (br.AttributeCollection.Count > 0)
                            {
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    if (attRef != null && attRef.Tag.Equals("KAPASITAS", StringComparison.OrdinalIgnoreCase))
                                    {
                                        fdtCapacity = attRef.TextString;
                                        break;
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(fdtCapacity))
                            {
                                if (bName.Contains("48") || br.Layer.Contains("48")) fdtCapacity = "48";
                                else if (bName.Contains("72") || br.Layer.Contains("72")) fdtCapacity = "72";
                            }

                            if (fdtCapacity.Contains("48")) data.Fdt48Count++;
                            else if (fdtCapacity.Contains("72")) data.Fdt72Count++;
                            else data.Fdt48Count++;
                        }
                    }
                }

                // Sum all poles
                data.TotalPoleAllType = data.NewPole7M4 + data.NewPole7M3 + data.NewPole7M25 + data.NewPole9M4 +
                                       data.ExtEmr7M4 + data.ExtEmr7M3 + data.ExtEmr7M25 + data.ExtEmr9M4 +
                                       data.ExtTelkom + data.ExtMti + data.ExtLinknet;

                // Total route cable
                data.TotalRouteLengthCable = data.TotalAerialRoute + data.TotalDuctRoute;

                // Fallback for loop hangers
                if (data.HangerCount == 0)
                {
                    data.HangerCount = (data.Fdt48Count + data.Fdt72Count) + (data.FatAerialCount + data.FatPedestalCount);
                }

                tr.Commit();
            }

            return data;
        }

        public static void CalculateAndShowSummary(BasemapPanel panel)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            int scenario = panel.GetSummaryCableScenario();
            int slackFdt = panel.GetSummarySlackFdt();
            double tolerance = panel.GetSummaryTolerance();

            SummaryData data = CalculateSummaryData(doc.Database, scenario, slackFdt, tolerance);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=========================================");
            sb.AppendLine("         DETAILED DESIGN SUMMARY         ");
            sb.AppendLine("=========================================");
            sb.AppendLine($"Cluster Name: {panel.GetSummaryClusterName()}");
            sb.AppendLine($"Address     : {panel.GetSummaryAddress()}");
            sb.AppendLine($"Cable Scen. : {(scenario == 2 ? $"Scenario 2 (Slack FDT={slackFdt}, Tol={tolerance}%)" : "Scenario 1 (As-Is Phys. Length)")}");
            sb.AppendLine("-----------------------------------------");
            sb.AppendLine("POLES COUNT:");
            sb.AppendLine($" - New Pole 7M 4\"        : {data.NewPole7M4} Pcs");
            sb.AppendLine($" - New Pole 7M 3\"        : {data.NewPole7M3} Pcs");
            sb.AppendLine($" - New Pole 7M 2.5\"      : {data.NewPole7M25} Pcs");
            sb.AppendLine($" - New Pole 9M 4\"        : {data.NewPole9M4} Pcs");
            sb.AppendLine($" - Ext EMR Pole 7M 4\"    : {data.ExtEmr7M4} Pcs");
            sb.AppendLine($" - Ext EMR Pole 7M 3\"    : {data.ExtEmr7M3} Pcs");
            sb.AppendLine($" - Ext EMR Pole 7M 2.5\"  : {data.ExtEmr7M25} Pcs");
            sb.AppendLine($" - Ext EMR Pole 9M 4\"    : {data.ExtEmr9M4} Pcs");
            sb.AppendLine($" - Ext Telkom Pole        : {data.ExtTelkom} Pcs");
            sb.AppendLine($" - Ext MTI Pole           : {data.ExtMti} Pcs");
            sb.AppendLine($" - Ext LinkNet Pole       : {data.ExtLinknet} Pcs");
            sb.AppendLine($"* TOTAL POLES             : {data.TotalPoleAllType} Pcs");
            sb.AppendLine("-----------------------------------------");
            sb.AppendLine("FIBER NETWORK & ASSETS:");
            sb.AppendLine($" - Total Homepass (HP)    : {data.HomepassCount} HP");
            sb.AppendLine($" - FDT 48C                : {data.Fdt48Count} Pcs");
            sb.AppendLine($" - FDT 72C                : {data.Fdt72Count} Pcs");
            sb.AppendLine($" - FAT Aerial (16 Port)   : {data.FatAerialCount} Pcs");
            sb.AppendLine($" - FAT Pedestal (16 Port) : {data.FatPedestalCount} Pcs");
            sb.AppendLine($" - Closure 144C           : {data.Closure144Count} Pcs");
            sb.AppendLine($" - Closure 48C            : {data.Closure48Count} Pcs");
            sb.AppendLine($" - Handhole 40x40x60      : {data.Handhole40Count} Pcs");
            sb.AppendLine($" - Handhole Pit 20x20x20  : {data.Handhole20Count} Pcs");
            sb.AppendLine($" - Cable Loop Hanger      : {data.HangerCount} Pcs");
            sb.AppendLine("-----------------------------------------");
            sb.AppendLine("CABLE ROUTE LENGTHS:");
            sb.AppendLine($" - FO Aerial 72C          : {Math.Round(data.Len72C)} M");
            sb.AppendLine($" - FO Aerial 48C          : {Math.Round(data.Len48C)} M");
            sb.AppendLine($" - FO Aerial 36C          : {Math.Round(data.Len36C)} M");
            sb.AppendLine($" - FO Aerial 24C          : {Math.Round(data.Len24C)} M");
            sb.AppendLine($" - Total Aerial Route     : {Math.Round(data.TotalAerialRoute)} M");
            sb.AppendLine($" - Total Duct/UG Route    : {Math.Round(data.TotalDuctRoute)} M");
            sb.AppendLine($" - Total HDPE (40/32 mm)  : {Math.Round(data.TotalDuctRoute)} M");
            sb.AppendLine($"* TOTAL ROUTE CABLE       : {Math.Round(data.TotalRouteLengthCable)} M");
            sb.AppendLine("=========================================");

            panel.SetSummaryDetails(sb.ToString());
            panel.LogMessage("Drawing scanned and summary updated successfully.");
        }

        public static void GenerateSummaryTable()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var panel = PaletteManager.BasemapPanel;

            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active.");
                return;
            }

            string clusterName = "";
            string clusterAddress = "";
            int scenario = 1;
            int slackFdt = 1;
            double tolerance = 5.0;

            panel.Dispatcher.Invoke(() =>
            {
                clusterName = panel.GetSummaryClusterName();
                clusterAddress = panel.GetSummaryAddress();
                scenario = panel.GetSummaryCableScenario();
                slackFdt = panel.GetSummarySlackFdt();
                tolerance = panel.GetSummaryTolerance();
            });

            ed.WriteMessage("\n[FTTH] Scanning drawing database to update Design Summary...");
            SummaryData data = CalculateSummaryData(db, scenario, slackFdt, tolerance);

            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
            string summaryJsonPath = Path.Combine(assemblyDir, "FTTH_Asset", "Summary.json");
            if (!File.Exists(summaryJsonPath))
            {
                summaryJsonPath = @"C:\Users\VICTUS\OneDrive\FTTH Design\LISP Command\13. Update\FTTH-Basemap_Refraktory\FTTH_Asset\Summary.json";
            }

            if (!File.Exists(summaryJsonPath))
            {
                ed.WriteMessage($"\n[FTTH] Error: Summary template JSON not found at: {summaryJsonPath}");
                return;
            }

            string jsonContent = File.ReadAllText(summaryJsonPath);
            JObject json = JObject.Parse(jsonContent);

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    string targetLayer = "DESIGN SUMMARY";
                    GetOrCreateLayer(db, tr, targetLayer, 7);

                    Point3d? existingBasePoint = null;
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject dbObj = tr.GetObject(id, OpenMode.ForRead);
                        if (dbObj is DBText dt && dt.Layer.Equals(targetLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            if (dt.TextString.Equals("DESIGN SUMMARY", StringComparison.OrdinalIgnoreCase))
                            {
                                existingBasePoint = new Point3d(
                                    dt.Position.X - 32.63121475863005,
                                    dt.Position.Y - 90.95376011658745,
                                    0.0
                                );
                                break;
                            }
                        }
                    }

                    double X_ins = 0;
                    double Y_ins = 0;

                    if (existingBasePoint.HasValue)
                    {
                        X_ins = existingBasePoint.Value.X;
                        Y_ins = existingBasePoint.Value.Y;
                        ed.WriteMessage($"\n[FTTH] Found existing summary at [{X_ins:F2}, {Y_ins:F2}]. Updating in-place...");

                        var entitiesToDelete = new List<ObjectId>();
                        foreach (ObjectId id in modelSpace)
                        {
                            if (id.IsErased) continue;
                            DBObject dbObj = tr.GetObject(id, OpenMode.ForRead);
                            if (dbObj is Entity ent)
                            {
                                string lyr = ent.Layer;
                                if (lyr.Equals("DESIGN SUMMARY", StringComparison.OrdinalIgnoreCase) ||
                                    lyr.Equals("ETIKET EMR-FH", StringComparison.OrdinalIgnoreCase))
                                {
                                    Point3d pos = Point3d.Origin;
                                    if (ent is DBText dt) pos = dt.Position;
                                    else if (ent is MText mt) pos = mt.Location;
                                    else if (ent is Line line) pos = line.StartPoint;
                                    else if (ent is BlockReference br) pos = br.Position;

                                    if (pos.X >= X_ins - 5 && pos.X <= X_ins + 125 &&
                                        pos.Y >= Y_ins - 5 && pos.Y <= Y_ins + 100)
                                    {
                                        entitiesToDelete.Add(id);
                                    }
                                }
                            }
                        }

                        foreach (ObjectId delId in entitiesToDelete)
                        {
                            Entity ent = tr.GetObject(delId, OpenMode.ForWrite) as Entity;
                            if (ent != null) ent.Erase();
                        }
                    }
                    else
                    {
                        PromptPointOptions ppo = new PromptPointOptions("\n[FTTH] Klik lokasi penempatan untuk tabel Summary: ");
                        PromptPointResult ppr = ed.GetPoint(ppo);
                        if (ppr.Status != PromptStatus.OK)
                        {
                            ed.WriteMessage("\n[FTTH] Placement cancelled.");
                            return;
                        }
                        X_ins = ppr.Value.X;
                        Y_ins = ppr.Value.Y;
                    }

                    // 2. Ensure layers exist
                    var layersArr = json["layers"] as JArray;
                    if (layersArr != null)
                    {
                        foreach (var lyr in layersArr)
                        {
                            string lyrName = lyr["name"]?.ToString();
                            short color = lyr["color"] != null ? (short)lyr["color"] : (short)7;
                            GetOrCreateLayer(db, tr, lyrName, color);
                        }
                    }

                    // 3. Ensure textstyles exist
                    var tsArr = json["textstyles"] as JArray;
                    if (tsArr != null)
                    {
                        foreach (var ts in tsArr)
                        {
                            string tsName = ts["name"]?.ToString();
                            string fontFile = ts["font_file_name"]?.ToString();
                            GetOrCreateTextStyle(db, tr, tsName, fontFile);
                        }
                    }

                    var blocksDict = new Dictionary<string, JObject>();
                    var blocksArr = json["blocks"] as JArray;
                    if (blocksArr != null)
                    {
                        foreach (JObject bObj in blocksArr)
                        {
                            string bName = bObj["name"]?.ToString();
                            if (!string.IsNullOrEmpty(bName))
                            {
                                blocksDict[bName] = bObj;
                            }
                        }
                    }

                    // 4. Draw entities
                    var entitiesArr = json["entities"] as JArray;
                    if (entitiesArr != null)
                    {
                        foreach (var ent in entitiesArr)
                        {
                            string entType = ent["type"]?.ToString();
                            string layer = ent["layer"]?.ToString();
                            string id = ent["id"]?.ToString();

                            if (entType == "TEXT")
                            {
                                string textStr = ent["text_string"]?.ToString();

                                // Value mappings based on unique entity IDs
                                if (id == "ent_124") textStr = data.NewPole7M4.ToString();
                                else if (id == "ent_057") textStr = data.HomepassCount.ToString();
                                else if (id == "ent_089") textStr = data.Fdt48Count.ToString();
                                else if (id == "ent_095") textStr = data.NewPole7M3.ToString();
                                else if (id == "ent_072") textStr = data.Fdt72Count.ToString();
                                else if (id == "ent_001") textStr = data.NewPole7M25.ToString();
                                else if (id == "ent_079") textStr = data.NewPole9M4.ToString();
                                else if (id == "ent_087") textStr = ((int)Math.Round(data.Len72C)).ToString();
                                else if (id == "ent_170") textStr = data.ExtEmr7M4.ToString();
                                else if (id == "ent_086") textStr = ((int)Math.Round(data.Len48C)).ToString();
                                else if (id == "ent_094") textStr = data.ExtEmr7M3.ToString();
                                else if (id == "ent_068") textStr = ((int)Math.Round(data.Len36C)).ToString();
                                else if (id == "ent_007") textStr = data.ExtEmr7M25.ToString();
                                else if (id == "ent_063") textStr = ((int)Math.Round(data.Len24C)).ToString();
                                else if (id == "ent_055") textStr = data.ExtEmr9M4.ToString();
                                else if (id == "ent_078") textStr = data.Handhole40Count.ToString();
                                else if (id == "ent_050") textStr = data.ExtMti.ToString();
                                else if (id == "ent_023") textStr = data.Handhole20Count.ToString();
                                else if (id == "ent_036") textStr = data.ExtLinknet.ToString();
                                else if (id == "ent_169") textStr = ((int)Math.Round(data.TotalDuctRoute)).ToString();
                                else if (id == "ent_031") textStr = data.ExtTelkom.ToString();
                                else if (id == "ent_168") textStr = data.FatAerialCount.ToString();
                                else if (id == "ent_042") textStr = data.TotalPoleAllType.ToString();
                                else if (id == "ent_015") textStr = data.FatPedestalCount.ToString();
                                else if (id == "ent_093") textStr = ((int)Math.Round(data.TotalAerialRoute)).ToString();
                                else if (id == "ent_040") textStr = data.Closure144Count.ToString();
                                else if (id == "ent_092") textStr = ((int)Math.Round(data.TotalDuctRoute)).ToString();
                                else if (id == "ent_085") textStr = data.Closure48Count.ToString();
                                else if (id == "ent_091") textStr = ((int)Math.Round(data.TotalRouteLengthCable)).ToString();
                                else if (id == "ent_084") textStr = data.HangerCount.ToString();

                                double pX = (double)ent["position"][0] + X_ins;
                                double pY = (double)ent["position"][1] + Y_ins;
                                double pZ = (double)ent["position"][2];

                                double height = (double)ent["height"];
                                double rotation = (double)ent["rotation"];
                                string styleName = ent["style"]?.ToString();
                                string horizMode = ent["horizontal_mode"]?.ToString();
                                string vertMode = ent["vertical_mode"]?.ToString();
                                double wFactor = ent["width_factor"] != null ? (double)ent["width_factor"] : 1.0;
                                double oblique = ent["obliquing_angle"] != null ? (double)ent["obliquing_angle"] : 0.0;

                                DBText dt = new DBText
                                {
                                    TextString = textStr,
                                    Position = new Point3d(pX, pY, pZ),
                                    Height = height,
                                    Rotation = rotation,
                                    WidthFactor = wFactor,
                                    Oblique = oblique,
                                    Layer = layer,
                                    TextStyleId = GetOrCreateTextStyle(db, tr, styleName, "Romans")
                                };

                                if (horizMode != "TextLeft" || vertMode != "TextBase")
                                {
                                    try
                                    {
                                        dt.HorizontalMode = (TextHorizontalMode)Enum.Parse(typeof(TextHorizontalMode), horizMode);
                                        dt.VerticalMode = (TextVerticalMode)Enum.Parse(typeof(TextVerticalMode), vertMode);
                                        double aX = (double)ent["alignment_point"][0] + X_ins;
                                        double aY = (double)ent["alignment_point"][1] + Y_ins;
                                        double aZ = (double)ent["alignment_point"][2];
                                        dt.AlignmentPoint = new Point3d(aX, aY, aZ);
                                        dt.AdjustAlignment(db);
                                    }
                                    catch { }
                                }

                                modelSpace.AppendEntity(dt);
                                tr.AddNewlyCreatedDBObject(dt, true);
                            }
                            else if (entType == "LINE")
                            {
                                double sX = (double)ent["start_point"][0] + X_ins;
                                double sY = (double)ent["start_point"][1] + Y_ins;
                                double sZ = (double)ent["start_point"][2];

                                double eX = (double)ent["end_point"][0] + X_ins;
                                double eY = (double)ent["end_point"][1] + Y_ins;
                                double eZ = (double)ent["end_point"][2];

                                Line line = new Line(new Point3d(sX, sY, sZ), new Point3d(eX, eY, eZ))
                                {
                                    Layer = layer
                                };

                                modelSpace.AppendEntity(line);
                                tr.AddNewlyCreatedDBObject(line, true);
                            }
                            else if (entType == "BLOCKREFERENCE")
                            {
                                string bName = ent["block_name"]?.ToString();
                                double bX = (double)ent["position"][0] + X_ins;
                                double bY = (double)ent["position"][1] + Y_ins;
                                double bZ = (double)ent["position"][2];

                                if (blocksDict.ContainsKey(bName))
                                {
                                    var blockDef = blocksDict[bName];
                                    var subEntities = blockDef["entities"] as JArray;
                                    if (subEntities != null)
                                    {
                                        foreach (var subEnt in subEntities)
                                        {
                                            string subType = subEnt["type"]?.ToString();
                                            string subLayer = subEnt["layer"]?.ToString();

                                            if (subType == "TEXT")
                                            {
                                                string subText = subEnt["text_string"]?.ToString();
                                                if (bName == "Cluster Name Layout")
                                                {
                                                    subText = clusterName;
                                                }
                                                else if (bName == "Alamat Cluster Name Layout")
                                                {
                                                    subText = clusterAddress;
                                                }

                                                double sxX = (double)subEnt["position"][0] + bX;
                                                double sxY = (double)subEnt["position"][1] + bY;
                                                double sxZ = (double)subEnt["position"][2] + bZ;

                                                double height = (double)subEnt["height"];
                                                double rotation = (double)subEnt["rotation"];
                                                string styleName = subEnt["style"]?.ToString();
                                                string horizMode = subEnt["horizontal_mode"]?.ToString();
                                                string vertMode = subEnt["vertical_mode"]?.ToString();
                                                double wFactor = subEnt["width_factor"] != null ? (double)subEnt["width_factor"] : 1.0;
                                                double oblique = subEnt["obliquing_angle"] != null ? (double)subEnt["obliquing_angle"] : 0.0;

                                                DBText dt = new DBText
                                                {
                                                    TextString = subText,
                                                    Position = new Point3d(sxX, sxY, sxZ),
                                                    Height = height,
                                                    Rotation = rotation,
                                                    WidthFactor = wFactor,
                                                    Oblique = oblique,
                                                    Layer = subLayer,
                                                    TextStyleId = GetOrCreateTextStyle(db, tr, styleName, "Romans")
                                                };

                                                if (horizMode != "TextLeft" || vertMode != "TextBase")
                                                {
                                                    try
                                                    {
                                                        dt.HorizontalMode = (TextHorizontalMode)Enum.Parse(typeof(TextHorizontalMode), horizMode);
                                                        dt.VerticalMode = (TextVerticalMode)Enum.Parse(typeof(TextVerticalMode), vertMode);
                                                        double saX = (double)subEnt["alignment_point"][0] + bX;
                                                        double saY = (double)subEnt["alignment_point"][1] + bY;
                                                        double saZ = (double)subEnt["alignment_point"][2] + bZ;
                                                        dt.AlignmentPoint = new Point3d(saX, saY, saZ);
                                                        dt.AdjustAlignment(db);
                                                    }
                                                    catch { }
                                                }

                                                modelSpace.AppendEntity(dt);
                                                tr.AddNewlyCreatedDBObject(dt, true);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    // Add border box outline around the summary table
                    using (Polyline borderBox = new Polyline())
                    {
                        borderBox.AddVertexAt(0, new Point2d(X_ins, Y_ins), 0, 0, 0);
                        borderBox.AddVertexAt(1, new Point2d(X_ins + 109.0, Y_ins), 0, 0, 0);
                        borderBox.AddVertexAt(2, new Point2d(X_ins + 109.0, Y_ins + 96.0), 0, 0, 0);
                        borderBox.AddVertexAt(3, new Point2d(X_ins, Y_ins + 96.0), 0, 0, 0);
                        borderBox.Closed = true;
                        borderBox.Layer = targetLayer;
                        
                        modelSpace.AppendEntity(borderBox);
                        tr.AddNewlyCreatedDBObject(borderBox, true);
                    }

                    tr.Commit();
                    ed.WriteMessage("\n[FTTH] Design Summary updated successfully on layer 'DESIGN SUMMARY'.");
                }
            }

            doc.Editor.Regen();
            doc.Editor.UpdateScreen();
        }

        private static ObjectId GetOrCreateTextStyle(Database db, Transaction tr, string styleName, string fontFile = "ROMANS")
        {
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(styleName))
            {
                return tst[styleName];
            }

            tst.UpgradeOpen();
            TextStyleTableRecord tstr = new TextStyleTableRecord();
            tstr.Name = styleName;
            tstr.FileName = fontFile.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) || fontFile.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? fontFile : fontFile + ".shx";
            ObjectId id = tst.Add(tstr);
            tr.AddNewlyCreatedDBObject(tstr, true);
            return id;
        }

        private static void GetOrCreateLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }
    }
}
