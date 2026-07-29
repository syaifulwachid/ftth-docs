using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FTTHBasemap.Model;

namespace FTTHBasemap.Export
{
    public class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int lenX = x.Length;
            int lenY = y.Length;
            int i = 0, j = 0;

            while (i < lenX && j < lenY)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    long numX = 0;
                    while (i < lenX && char.IsDigit(x[i]))
                    {
                        numX = numX * 10 + (x[i] - '0');
                        i++;
                    }

                    long numY = 0;
                    while (j < lenY && char.IsDigit(y[j]))
                    {
                        numY = numY * 10 + (y[j] - '0');
                        j++;
                    }

                    int compare = numX.CompareTo(numY);
                    if (compare != 0) return compare;
                }
                else
                {
                    int compare = x[i].CompareTo(y[j]);
                    if (compare != 0) return compare;
                    i++;
                    j++;
                }
            }

            return lenX.CompareTo(lenY);
        }
    }

    public static class AutoKmlExporter
    {
        public static bool IsPointInPolygon(Point2d pt, Polyline poly)
        {
            int numVertices = poly.NumberOfVertices;
            bool inside = false;
            for (int i = 0, j = numVertices - 1; i < numVertices; j = i++)
            {
                Point3d ptI = poly.GetPoint3dAt(i);
                Point3d ptJ = poly.GetPoint3dAt(j);
                Point2d pi = new Point2d(ptI.X, ptI.Y);
                Point2d pj = new Point2d(ptJ.X, ptJ.Y);
                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
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

        private static bool IsPoleText(string text, string layerName)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string upperLayer = layerName.ToUpper();
            string upperText = text.ToUpper().Trim();

            if (upperLayer.StartsWith("FTTH-POLE-") || 
                upperLayer.Contains("TIANG") || 
                upperLayer == "EXT POLE" || 
                upperLayer == "NEW POLE" || 
                upperLayer.Contains("LABEL-TIANG") ||
                upperLayer.Contains("LABEL-POLE"))
            {
                return true;
            }

            if (upperLayer.Contains("LABEL") || upperLayer.Contains("TXT") || upperLayer.Contains("TEXT"))
            {
                return upperText.StartsWith("EXT ") || 
                       upperText.StartsWith("NP ") || 
                       upperText.StartsWith("EP ") || 
                       upperText.Contains("POLE") || 
                       upperText.Contains("TIANG");
            }

            return false;
        }

        private static string GetFdtShortName(string fullName, int index)
        {
            if (string.IsNullOrEmpty(fullName)) return $"FDT {index}";
            var match = System.Text.RegularExpressions.Regex.Match(fullName, @"FDT\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Value.ToUpper();
            }
            match = System.Text.RegularExpressions.Regex.Match(fullName, @"FDT_\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Value.Replace("_", " ").ToUpper();
            }
            return $"FDT {index}";
        }

        private static string GetPoleCategory(string blockName, string layerName)
        {
            string upperBlock = blockName.ToUpper();
            string upperLayer = layerName.ToUpper();

            bool isExisting = upperBlock.Contains("EXT") || 
                              upperBlock.Contains("EP") || 
                              upperLayer.Contains("EXISTING") || 
                              upperLayer.Contains("EXIST") || 
                              upperBlock.StartsWith("EP");

            string size = "7-4";
            if (upperBlock.Contains("94") || upperBlock.Contains("9-4") || upperBlock.Contains("9M") || upperBlock.Contains("NP94") ||
                upperLayer.Contains("94") || upperLayer.Contains("9-4") || upperLayer.Contains("9M") || upperLayer.Contains("NP94"))
            {
                size = "9-4";
            }
            else if (upperBlock.Contains("74") || upperBlock.Contains("7-4") || upperBlock.Contains("7M 4") || upperBlock.Contains("7M-4") || upperBlock.Contains("NP74") ||
                     upperLayer.Contains("74") || upperLayer.Contains("7-4") || upperLayer.Contains("7M 4") || upperLayer.Contains("7M-4") || upperLayer.Contains("NP74"))
            {
                size = "7-4";
            }
            else if (upperBlock.Contains("73") || upperBlock.Contains("7-3") || upperBlock.Contains("7M 3") || upperBlock.Contains("7M-3") || upperBlock.Contains("NP73") ||
                     upperLayer.Contains("73") || upperLayer.Contains("7-3") || upperLayer.Contains("7M 3") || upperLayer.Contains("7M-3") || upperLayer.Contains("NP73"))
            {
                size = "7-3";
            }
            else if (upperBlock.Contains("725") || upperBlock.Contains("7-2.5") || upperBlock.Contains("7M 2.5") || upperBlock.Contains("7M-2.5") || upperBlock.Contains("2.5") || upperBlock.Contains("NP725") ||
                     upperLayer.Contains("725") || upperLayer.Contains("7-2.5") || upperLayer.Contains("7M 2.5") || upperLayer.Contains("7M-2.5") || upperLayer.Contains("2.5") || upperLayer.Contains("NP725"))
            {
                size = "7-2.5";
            }

            if (isExisting)
            {
                if (upperBlock.Contains("TEL") || upperLayer.Contains("TEL") ||
                    upperBlock.Contains("MTI") || upperLayer.Contains("MTI") ||
                    upperBlock.Contains("LINK") || upperLayer.Contains("LINK") ||
                    upperBlock.Contains("PLN") || upperLayer.Contains("PLN") ||
                    upperBlock.Contains("PARTNER") || upperLayer.Contains("PARTNER"))
                {
                    return $"EXISTING POLE PARTNER {size}";
                }
                return $"EXISTING POLE EMR {size}";
            }
            return $"NEW POLE {size}";
        }

        public static ExportModel BuildExportModel(Database db, string defaultFdtName = "FDT_1", Action<double, string> progressCallback = null)
        {
            ExportModel model = new ExportModel();
            progressCallback?.Invoke(10, "Membaca database gambar AutoCAD...");

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var fdtBlocks = new List<BlockReference>();
                var fatBlocks = new List<BlockReference>();
                var boundaryPolylines = new List<Polyline>();
                var homepassTexts = new List<Entity>();
                var cablePolylines = new List<Curve>();
                var slingPolylines = new List<Curve>();
                var poleBlocks = new List<BlockReference>();
                var poleTexts = new List<Entity>();
                var closureBlocks = new List<BlockReference>();
                var handholeBlocks = new List<BlockReference>();
                var slackHangerBlocks = new List<BlockReference>();
                var labelTexts = new List<Tuple<Point3d, string, string>>();
                var helperLines = new List<Curve>();

                foreach (ObjectId id in modelSpace)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                    if (obj is Entity ent)
                    {
                        string lyrUpper = ent.Layer.ToUpper();
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

                        string bNameUpper = bName.ToUpper();
                        string layerUpper = br.Layer.ToUpper();

                        if (bNameUpper.Contains("FDT") || layerUpper.Contains("FDT"))
                        {
                            fdtBlocks.Add(br);
                        }
                        else if (bNameUpper.Contains("FAT") || bNameUpper.Contains("ODP") || layerUpper.Contains("FAT") || layerUpper.Contains("ODP"))
                        {
                            fatBlocks.Add(br);
                        }
                        else if (IsPoleBlock(bName, br.Layer) || bNameUpper.Contains("POLE") || bNameUpper.Contains("TIANG") || layerUpper.Contains("POLE") || layerUpper.Contains("TIANG"))
                        {
                            poleBlocks.Add(br);
                        }
                        else if (bNameUpper.Contains("CLOSURE") || bNameUpper.Contains("JC") || layerUpper.Contains("CLOSURE") || layerUpper.Contains("JC"))
                        {
                            closureBlocks.Add(br);
                        }
                        else if (bNameUpper.Contains("HANDHOLE") || bNameUpper.Contains("HH") || bNameUpper.Contains("PIT") || layerUpper.Contains("HANDHOLE") || layerUpper.Contains("HH") || layerUpper.Contains("PIT"))
                        {
                            handholeBlocks.Add(br);
                        }
                        else if (bNameUpper.Contains("HANGER") || bNameUpper.Contains("HOLDER") || bNameUpper.Contains("LOOP") || layerUpper.Contains("HANGER") || layerUpper.Contains("HOLDER") || layerUpper.Contains("LOOP"))
                        {
                            slackHangerBlocks.Add(br);
                        }
                    }
                    else if (obj is Polyline poly)
                    {
                        if (poly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                        {
                            cablePolylines.Add(poly);
                        }
                        else if (poly.Layer.Equals("FTTH-HELPER-ARROW-FAT", StringComparison.OrdinalIgnoreCase))
                        {
                            helperLines.Add(poly);
                        }
                        else if (poly.Layer.IndexOf("WIRE", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                 poly.Layer.IndexOf("SLING", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            slingPolylines.Add(poly);
                        }
                        else if ((poly.Closed || poly.StartPoint.DistanceTo(poly.EndPoint) < 0.2) && 
                                 (poly.Layer.IndexOf("BOUNDARY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  poly.Layer.IndexOf("ARIAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  poly.Layer.IndexOf("COV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  poly.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  poly.Layer.IndexOf("POLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  poly.Layer.IndexOf("AREA", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            boundaryPolylines.Add(poly);
                        }
                    }
                    else if (obj is Polyline2d poly2d)
                    {
                        Polyline dummyPoly = new Polyline();
                        dummyPoly.SetDatabaseDefaults();
                        try
                        {
                            int idx = 0;
                            foreach (ObjectId vId in poly2d)
                            {
                                Vertex2d v2d = tr.GetObject(vId, OpenMode.ForRead) as Vertex2d;
                                if (v2d != null)
                                {
                                    dummyPoly.AddVertexAt(idx++, new Point2d(v2d.Position.X, v2d.Position.Y), v2d.Bulge, v2d.StartWidth, v2d.EndWidth);
                                }
                            }
                            dummyPoly.Closed = poly2d.Closed;
                            dummyPoly.Layer = poly2d.Layer;

                            if (dummyPoly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                            {
                                cablePolylines.Add(dummyPoly);
                            }
                            else if (dummyPoly.Layer.Equals("FTTH-HELPER-ARROW-FAT", StringComparison.OrdinalIgnoreCase))
                            {
                                helperLines.Add(dummyPoly);
                            }
                            else if (dummyPoly.Layer.IndexOf("WIRE", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                     dummyPoly.Layer.IndexOf("SLING", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                slingPolylines.Add(dummyPoly);
                            }
                            else if ((dummyPoly.Closed || dummyPoly.StartPoint.DistanceTo(dummyPoly.EndPoint) < 0.2) && 
                                     (dummyPoly.Layer.IndexOf("BOUNDARY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      dummyPoly.Layer.IndexOf("ARIAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      dummyPoly.Layer.IndexOf("COV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      dummyPoly.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      dummyPoly.Layer.IndexOf("POLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      dummyPoly.Layer.IndexOf("AREA", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                boundaryPolylines.Add(dummyPoly);
                            }
                        }
                        catch {}
                    }
                    else if (obj is Line line)
                    {
                        if (line.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                        {
                            cablePolylines.Add(line);
                        }
                        else if (line.Layer.Equals("FTTH-HELPER-ARROW-FAT", StringComparison.OrdinalIgnoreCase))
                        {
                            helperLines.Add(line);
                        }
                        else if (line.Layer.IndexOf("WIRE", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                 line.Layer.IndexOf("SLING", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            slingPolylines.Add(line);
                        }
                    }
                    else if (obj is Leader leader)
                    {
                        if (leader.Layer.Equals("FTTH-HELPER-ARROW-FAT", StringComparison.OrdinalIgnoreCase))
                        {
                            helperLines.Add(leader);
                        }
                    }
                    else if (obj is DBText dt)
                    {
                        if (dt.Layer.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                        {
                            homepassTexts.Add(dt);
                        }
                        else if (IsPoleText(dt.TextString, dt.Layer))
                        {
                            poleTexts.Add(dt);
                        }
                        else
                        {
                            labelTexts.Add(Tuple.Create(dt.Position, dt.TextString, dt.Layer));
                        }
                    }
                    else if (obj is MText mt)
                    {
                        if (mt.Layer.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                        {
                            homepassTexts.Add(mt);
                        }
                        else if (IsPoleText(mt.Text, mt.Layer))
                        {
                            poleTexts.Add(mt);
                        }
                        else
                        {
                            labelTexts.Add(Tuple.Create(mt.Location, mt.Contents, mt.Layer));
                        }
                    }
                }

                progressCallback?.Invoke(35, "Mengelompokkan data objek (FDT, FAT, Kabel)...");

                // 1. Process FDT Nodes
                var allFdts = new List<FdtNode>();
                var fdtPositions = new List<Tuple<FdtNode, Point3d>>();

                foreach (var fdtBr in fdtBlocks)
                {
                    string fdtName = defaultFdtName;
                    string capacityText = "FDT 48C";
                    string stoCode = "";

                    if (fdtBr.AttributeCollection.Count > 0)
                    {
                        foreach (ObjectId attId in fdtBr.AttributeCollection)
                        {
                            AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (attRef != null)
                            {
                                if (attRef.Tag.Equals("NAMA_FDT", StringComparison.OrdinalIgnoreCase) || 
                                    attRef.Tag.Equals("CODE", StringComparison.OrdinalIgnoreCase) ||
                                    attRef.Tag.Equals("NAMA", StringComparison.OrdinalIgnoreCase))
                                {
                                    fdtName = attRef.TextString;
                                }
                                else if (attRef.Tag.Equals("KAPASITAS", StringComparison.OrdinalIgnoreCase))
                                {
                                    capacityText = attRef.TextString;
                                }
                            }
                        }
                    }

                    // Look for labels on layer FTTH-FDT-LABEL close to the FDT block (within KmlLabelSearchTol)
                    var fdtLabels = labelTexts
                        .Where(l => l.Item3.Equals("FTTH-FDT-LABEL", StringComparison.OrdinalIgnoreCase) && l.Item1.DistanceTo(fdtBr.Position) < BasemapSettings.Instance.KmlLabelSearchTol)
                        .Select(l => l.Item2)
                        .ToList();

                    var allLines = new List<string>();
                    foreach (var label in fdtLabels)
                    {
                        string cleanLabel = label.Replace("\\P", "\n").Replace("\r", "");
                        var parts = cleanLabel.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            string trimmed = part.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                allLines.Add(trimmed);
                            }
                        }
                    }

                    if (allLines.Count > 0)
                    {
                        // Check if we can find one with 2 dots (like MLG.100.0405)
                        var stoCodeLine = allLines.FirstOrDefault(s => s.Split('.').Length >= 3);
                        if (stoCodeLine != null)
                        {
                            stoCode = stoCodeLine;
                            var otherLine = allLines.FirstOrDefault(s => s != stoCodeLine);
                            if (otherLine != null)
                            {
                                fdtName = otherLine;
                            }
                            else
                            {
                                fdtName = stoCodeLine;
                                stoCode = "";
                            }
                        }
                        else
                        {
                            fdtName = allLines[0];
                            if (allLines.Count > 1)
                            {
                                stoCode = allLines[1];
                            }
                        }
                    }

                    string formattedCapacity = capacityText.ToUpper().StartsWith("FDT") 
                        ? capacityText 
                        : $"FDT {capacityText}";

                    string fdtDesc = string.IsNullOrEmpty(stoCode) 
                        ? formattedCapacity 
                        : $"{formattedCapacity}\n{stoCode}";

                    Point3d fdtLonLat = TileMapManager.ProjectWcsToLonLat(db, fdtBr.Position);
                    var node = new FdtNode
                    {
                        EntityHandle = fdtBr.Handle.ToString(),
                        Name = fdtName,
                        CapacityText = capacityText,
                        Description = fdtDesc,
                        Longitude = fdtLonLat.X,
                        Latitude = fdtLonLat.Y,
                        Rotation = fdtBr.Rotation,
                        Layer = fdtBr.Layer
                    };
                    allFdts.Add(node);
                    fdtPositions.Add(Tuple.Create(node, fdtBr.Position));
                }

                // Filter FDTs to exclude legend blocks (must be within 25.0m of some cable)
                var activeFdts = new List<FdtNode>();
                var activeFdtPositions = new List<Tuple<FdtNode, Point3d>>();
                foreach (var fp in fdtPositions)
                {
                    double minDistToCable = double.MaxValue;
                    foreach (var poly in cablePolylines)
                    {
                        try
                        {
                            Point3d cp = poly.GetClosestPointTo(fp.Item2, false);
                            double d = fp.Item2.DistanceTo(cp);
                            if (d < minDistToCable) minDistToCable = d;
                        }
                        catch {}
                    }

                    if (minDistToCable <= 25.0 || cablePolylines.Count == 0)
                    {
                        activeFdts.Add(fp.Item1);
                        activeFdtPositions.Add(fp);
                    }
                }
                allFdts = activeFdts;
                fdtPositions = activeFdtPositions;

                model.Fdts = allFdts;
                if (allFdts.Count > 0)
                {
                    model.Fdt = allFdts[0];
                }
                else
                {
                    model.Fdt = new FdtNode
                    {
                        Name = defaultFdtName,
                        CapacityText = "FDT 48C",
                        Description = "FDT 48C",
                        Longitude = 0,
                        Latitude = 0
                    };
                }

                progressCallback?.Invoke(50, "Memetakan FDT...");

                // 2. Map FAT blocks and boundaries
                var fatDataList = new List<FatData>();

                var boundaryToFatMap = new Dictionary<string, string>(); // Boundary Handle -> FAT Handle
                var fatToBoundaryMap = new Dictionary<string, Polyline>(); // FAT Handle -> Boundary Polyline

                // Pass 1: Match by helper arrows (Explicit user intent)
                if (helperLines.Count > 0)
                {
                    foreach (var fatBr in fatBlocks)
                    {
                        string fatHandle = fatBr.Handle.ToString();
                        Point3d fatPos = fatBr.Position;

                        foreach (var helper in helperLines)
                        {
                            Point3d startPt = helper.StartPoint;
                            Point3d endPt = helper.EndPoint;

                            bool startNearFat = startPt.DistanceTo(fatPos) < 5.0;
                            bool endNearFat = endPt.DistanceTo(fatPos) < 5.0;

                            if (startNearFat || endNearFat)
                            {
                                Point3d otherEnd = startNearFat ? endPt : startPt;

                                foreach (var poly in boundaryPolylines)
                                {
                                    string boundaryHandle = poly.Handle.ToString();
                                    if (IsPointInPolygon(new Point2d(otherEnd.X, otherEnd.Y), poly))
                                    {
                                        if (!fatToBoundaryMap.ContainsKey(fatHandle) && !boundaryToFatMap.ContainsKey(boundaryHandle))
                                        {
                                            fatToBoundaryMap[fatHandle] = poly;
                                            boundaryToFatMap[boundaryHandle] = fatHandle;
                                        }
                                        break;
                                    }
                                }
                            }
                            if (fatToBoundaryMap.ContainsKey(fatHandle)) break;
                        }
                    }
                }

                // Pass 2: Match by Point-in-Polygon (Physically inside)
                foreach (var fatBr in fatBlocks)
                {
                    string fatHandle = fatBr.Handle.ToString();
                    if (fatToBoundaryMap.ContainsKey(fatHandle)) continue;

                    Point3d fatPos = fatBr.Position;
                    foreach (var poly in boundaryPolylines)
                    {
                        string boundaryHandle = poly.Handle.ToString();
                        if (boundaryToFatMap.ContainsKey(boundaryHandle)) continue;

                        if (IsPointInPolygon(new Point2d(fatPos.X, fatPos.Y), poly))
                        {
                            fatToBoundaryMap[fatHandle] = poly;
                            boundaryToFatMap[boundaryHandle] = fatHandle;
                            break;
                        }
                    }
                }

                // Pass 3: Fallback by closest centroid (Max 25m)
                foreach (var fatBr in fatBlocks)
                {
                    string fatHandle = fatBr.Handle.ToString();
                    if (fatToBoundaryMap.ContainsKey(fatHandle)) continue;

                    Point3d fatPos = fatBr.Position;
                    Polyline bestPoly = null;
                    double minCentroidDist = double.MaxValue;

                    foreach (var poly in boundaryPolylines)
                    {
                        string boundaryHandle = poly.Handle.ToString();
                        if (boundaryToFatMap.ContainsKey(boundaryHandle)) continue;

                        double sumX = 0;
                        double sumY = 0;
                        int count = poly.NumberOfVertices;
                        for (int i = 0; i < count; i++)
                        {
                            Point3d wcsPt = poly.GetPoint3dAt(i);
                            sumX += wcsPt.X;
                            sumY += wcsPt.Y;
                        }
                        Point2d centroid = new Point2d(sumX / count, sumY / count);
                        double dx = centroid.X - fatPos.X;
                        double dy = centroid.Y - fatPos.Y;
                        double d = Math.Sqrt(dx * dx + dy * dy);

                        if (d < minCentroidDist)
                        {
                            try
                            {
                                Point3d cp = poly.GetClosestPointTo(fatPos, false);
                                double edgeDist = fatPos.DistanceTo(cp);
                                if (edgeDist <= 100.0)
                                {
                                    minCentroidDist = d;
                                    bestPoly = poly;
                                }
                            }
                            catch {}
                        }
                    }

                    if (bestPoly != null)
                    {
                        string boundaryHandle = bestPoly.Handle.ToString();
                        fatToBoundaryMap[fatHandle] = bestPoly;
                        boundaryToFatMap[boundaryHandle] = fatHandle;
                    }
                }

                progressCallback?.Invoke(75, "Menganalisis area cakupan FAT (Boundary)...");

                foreach (var fatBr in fatBlocks)
                {
                    string fatHandle = fatBr.Handle.ToString();
                    Point3d fatPos = fatBr.Position;
                    Point3d fatLonLat = TileMapManager.ProjectWcsToLonLat(db, fatPos);

                    Polyline matchedBoundary = null;
                    if (fatToBoundaryMap.ContainsKey(fatHandle))
                    {
                        matchedBoundary = fatToBoundaryMap[fatHandle];
                    }

                    // 4. Deteksi Teks Label FAT
                    string sequenceName = "A01";
                    string rawText = "";
                    double labelSearchTol = BasemapSettings.Instance.KmlLabelSearchTol;
                    Tuple<Point3d, string, string> matchedLabelTuple = null;

                    // Prioritas 1: Jika boundary FAT terdeteksi, cari label FAT yang ada di dalam boundary tersebut
                    if (matchedBoundary != null)
                    {
                        matchedLabelTuple = labelTexts
                            .FirstOrDefault(l => {
                                string lyr = l.Item3;
                                bool isFatLabelLayer = lyr.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                                       lyr.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) || 
                                                       (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);
                                if (!isFatLabelLayer) return false;

                                return IsPointInPolygon(new Point2d(l.Item1.X, l.Item1.Y), matchedBoundary);
                            });
                    }

                    // Prioritas 2: Fallback radius pencarian terdekat menggunakan KmlLabelSearchTol
                    if (matchedLabelTuple == null)
                    {
                        matchedLabelTuple = labelTexts
                            .Where(l => {
                                double dist = l.Item1.DistanceTo(fatPos);
                                if (dist >= labelSearchTol) return false;
                                string lyr = l.Item3;
                                bool isFatLabelLayer = lyr.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                                       lyr.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) || 
                                                       (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);
                                bool isPoleLabelLayer = lyr.IndexOf("TIANG", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                        lyr.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0;
                                return isFatLabelLayer || !isPoleLabelLayer;
                            })
                            .OrderBy(l => {
                                string lyr = l.Item3;
                                bool isFatLabelLayer = lyr.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                                       lyr.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) || 
                                                       (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);
                                double dist = l.Item1.DistanceTo(fatPos);
                                return isFatLabelLayer ? dist * 0.1 : dist;
                            })
                            .FirstOrDefault();
                    }

                    if (matchedLabelTuple != null)
                    {
                        rawText = matchedLabelTuple.Item2;
                        string cleanText = System.Text.RegularExpressions.Regex.Match(rawText, @"[A-Za-z]\d{2}$").Value;
                        if (!string.IsNullOrEmpty(cleanText))
                        {
                            sequenceName = cleanText.ToUpper();
                        }
                        else
                        {
                            cleanText = System.Text.RegularExpressions.Regex.Match(rawText, @"[A-Za-z]\d{2}").Value;
                            if (!string.IsNullOrEmpty(cleanText))
                            {
                                sequenceName = cleanText.ToUpper();
                            }
                        }
                    }

                    var fatData = new FatData
                    {
                        EntityHandle = fatBr.Handle.ToString(),
                        SequenceName = sequenceName,
                        RawText = string.IsNullOrEmpty(rawText) ? sequenceName : rawText,
                        FatLon = fatLonLat.X,
                        FatLat = fatLonLat.Y,
                        Rotation = fatBr.Rotation,
                        Layer = fatBr.Layer,
                        HpCovers = new List<EntityItem>()
                    };

                    // Check if FAT is close to any cable to classify it as a real FAT
                    bool isRealFat = false;
                    foreach (var poly in cablePolylines)
                    {
                        try
                        {
                            Point3d cp = poly.GetClosestPointTo(fatPos, false);
                            if (fatPos.DistanceTo(cp) <= 50.0) // Within 50 meters of any cable
                            {
                                isRealFat = true;
                                break;
                            }
                        }
                        catch {}
                    }

                    if (matchedBoundary == null && boundaryPolylines.Count > 0 && !isRealFat)
                    {
                        continue; // Skip legend FAT
                    }

                    if (matchedBoundary != null)
                    {
                        fatData.BoundaryHandle = matchedBoundary.Handle.ToString();
                        fatData.BoundaryLayer = matchedBoundary.Layer;
                        fatData.BoundaryPoints = new List<(double Lon, double Lat)>();
                        for (int i = 0; i < matchedBoundary.NumberOfVertices; i++)
                        {
                            Point2d pt = matchedBoundary.GetPoint2dAt(i);
                            Point3d wcsPt = new Point3d(pt.X, pt.Y, 0.0);
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(db, wcsPt);
                            fatData.BoundaryPoints.Add((latLon.X, latLon.Y));
                        }

                        foreach (Entity hpObj in homepassTexts)
                        {
                            Point3d hpPos = hpObj is DBText dt ? dt.Position : ((MText)hpObj).Location;
                            if (IsPointInPolygon(new Point2d(hpPos.X, hpPos.Y), matchedBoundary))
                            {
                                string hpText = hpObj is DBText dt2 ? dt2.TextString : ((MText)hpObj).Text;
                                double rot = hpObj is DBText dt3 ? dt3.Rotation : ((MText)hpObj).Rotation;
                                double h = hpObj is DBText dt4 ? dt4.Height : ((MText)hpObj).TextHeight;
                                Point3d hpLonLat = TileMapManager.ProjectWcsToLonLat(db, hpPos);

                                fatData.HpCovers.Add(new EntityItem
                                {
                                    EntityHandle = hpObj.Handle.ToString(),
                                    Name = hpText,
                                    Category = "HP COVER",
                                    Lon = hpLonLat.X,
                                    Lat = hpLonLat.Y,
                                    Rotation = rot,
                                    TextHeight = h,
                                    Layer = hpObj.Layer,
                                    SequenceName = sequenceName
                                });
                            }
                        }

                        if (BasemapSettings.Instance.KmlSortHomepass && fatData.HpCovers.Count > 0)
                        {
                            var comparer = new NaturalStringComparer();
                            fatData.HpCovers = fatData.HpCovers.OrderBy(hp => hp.Name, comparer).ToList();
                        }
                    }

                    fatDataList.Add(fatData);
                }

                // 3. Reconstruct Line Folders
                var linesDict = new Dictionary<string, LineData>(StringComparer.OrdinalIgnoreCase);

                foreach (var fat in fatDataList)
                {
                    string lineLetter = new string(fat.SequenceName.TakeWhile(char.IsLetter).ToArray());
                    if (string.IsNullOrEmpty(lineLetter)) lineLetter = "A";

                    string baseLineName = $"LINE {lineLetter}".ToUpper();
                    string lineName = baseLineName;

                    FdtNode closestFdtForFat = null;
                    if (fdtPositions.Count > 0)
                    {
                        try
                        {
                            Point3d fatWcs = TileMapManager.ProjectLonLatToWcs(db, fat.FatLon, fat.FatLat);
                            double minDistToFdt = double.MaxValue;
                            foreach (var fp in fdtPositions)
                            {
                                double d = fp.Item2.DistanceTo(fatWcs);
                                if (d < minDistToFdt)
                                {
                                    minDistToFdt = d;
                                    closestFdtForFat = fp.Item1;
                                }
                            }
                        }
                        catch { }
                    }

                    if (allFdts.Count > 1 && closestFdtForFat != null)
                    {
                        string fdtShortName = GetFdtShortName(closestFdtForFat.Name, allFdts.IndexOf(closestFdtForFat) + 1);
                        lineName = $"{baseLineName}-{fdtShortName}";
                    }

                    if (!linesDict.ContainsKey(lineName))
                    {
                        linesDict[lineName] = new LineData
                        {
                            LineName = lineName,
                            FdtHandle = closestFdtForFat != null ? closestFdtForFat.EntityHandle : (allFdts.Count > 0 ? allFdts[0].EntityHandle : ""),
                            Fats = new List<FatData>(),
                            Poles = new List<EntityItem>(),
                            DistributionCables = new List<EntityItem>(),
                            SlackHangers = new List<EntityItem>(),
                            SlingWires = new List<EntityItem>(),
                            Closures = new List<EntityItem>(),
                            CustomMarkers = new List<EntityItem>()
                        };
                    }

                    fat.HpCovers.ForEach(hp => hp.LineAffiliation = lineName);
                    linesDict[lineName].Fats.Add(fat);
                }

                if (linesDict.Count == 0)
                {
                    string fallbackLineName = allFdts.Count > 1 ? "LINE A-FDT 1" : "LINE A";
                    linesDict[fallbackLineName] = new LineData
                    {
                        LineName = fallbackLineName,
                        FdtHandle = allFdts.Count > 0 ? allFdts[0].EntityHandle : "",
                        Fats = new List<FatData>(),
                        Poles = new List<EntityItem>(),
                        DistributionCables = new List<EntityItem>(),
                        SlackHangers = new List<EntityItem>(),
                        SlingWires = new List<EntityItem>(),
                        Closures = new List<EntityItem>(),
                        CustomMarkers = new List<EntityItem>()
                    };
                }

                // 4. Map Cable Polylines to Lines
                var mappedCables = new List<EntityItem>();
                var mappedRoutes = new List<Tuple<Curve, string, bool>>();
                var cableLineAffiliations = new Dictionary<ObjectId, string>();
                foreach (var poly in cablePolylines)
                {
                    double midDist = poly.GetDistanceAtParameter(poly.EndParam) / 2.0;
                    Point3d midPt = poly.GetPointAtDist(midDist);

                    string baseLineAffiliation = "";
                    var nearestLabel = labelTexts
                        .Where(l => l.Item2.IndexOf("LINE", StringComparison.OrdinalIgnoreCase) >= 0 && l.Item1.DistanceTo(midPt) < 10.0)
                        .OrderBy(l => l.Item1.DistanceTo(midPt))
                        .FirstOrDefault();

                    if (nearestLabel != null)
                    {
                        string lbl = nearestLabel.Item2.ToUpper();
                        if (lbl.Contains("LINE A")) baseLineAffiliation = "LINE A";
                        else if (lbl.Contains("LINE B")) baseLineAffiliation = "LINE B";
                        else if (lbl.Contains("LINE C")) baseLineAffiliation = "LINE C";
                        else if (lbl.Contains("LINE D")) baseLineAffiliation = "LINE D";
                        else if (lbl.Contains("LINE E")) baseLineAffiliation = "LINE E";
                        else if (lbl.Contains("LINE F")) baseLineAffiliation = "LINE F";
                    }

                    if (string.IsNullOrEmpty(baseLineAffiliation))
                    {
                        double minDistance = double.MaxValue;
                        foreach (var fat in fatDataList)
                        {
                            try
                            {
                                Point3d fatWcs = TileMapManager.ProjectLonLatToWcs(db, fat.FatLon, fat.FatLat);
                                double d = poly.GetClosestPointTo(fatWcs, false).DistanceTo(fatWcs);
                                if (d < minDistance)
                                {
                                    minDistance = d;
                                    string lineLetter = new string(fat.SequenceName.TakeWhile(char.IsLetter).ToArray());
                                    if (string.IsNullOrEmpty(lineLetter)) lineLetter = "A";
                                    baseLineAffiliation = $"LINE {lineLetter}";
                                }
                            }
                            catch { }
                        }
                    }

                    if (string.IsNullOrEmpty(baseLineAffiliation))
                    {
                        baseLineAffiliation = "LINE A";
                    }

                    string lineAffiliation = baseLineAffiliation;

                    FdtNode closestFdtForCable = null;
                    if (fdtPositions.Count > 0)
                    {
                        double minDistToFdt = double.MaxValue;
                        foreach (var fp in fdtPositions)
                        {
                            double d = fp.Item2.DistanceTo(midPt);
                            if (d < minDistToFdt)
                            {
                                minDistToFdt = d;
                                closestFdtForCable = fp.Item1;
                            }
                        }
                    }

                    if (allFdts.Count > 1 && closestFdtForCable != null)
                    {
                        string fdtShortName = GetFdtShortName(closestFdtForCable.Name, allFdts.IndexOf(closestFdtForCable) + 1);
                        lineAffiliation = $"{baseLineAffiliation}-{fdtShortName}";
                    }

                    int cores = GetCableCores(poly, db, tr);
                    int tubes = Math.Max(1, cores / 12);
                    string defaultName = $"FO {cores}C/{tubes}T";

                    // 1. Find closest cable label text on layer "FTTH-CABLE-LABEL" within 15.0 meters
                    string cableLabelContent = "";
                    var nearestCableLabel = labelTexts
                        .Where(l => l.Item3.Equals("FTTH-CABLE-LABEL", StringComparison.OrdinalIgnoreCase) && l.Item1.DistanceTo(midPt) < 15.0)
                        .OrderBy(l => l.Item1.DistanceTo(midPt))
                        .FirstOrDefault();

                    if (nearestCableLabel != null)
                    {
                        cableLabelContent = nearestCableLabel.Item2;
                    }

                    string cableName = defaultName;
                    string cableDesc = "";

                    // Parse/generate Scenario 2
                    string parsedSyncedPrefix = "";
                    string parsedLineName = "";
                    string parsedFoSpec = "";
                    string parsedCategory = "";
                    int parsedLen = 0;

                    if (!string.IsNullOrEmpty(cableLabelContent))
                    {
                        ParseCableLabel(cableLabelContent, out parsedSyncedPrefix, out parsedLineName, out parsedFoSpec, out parsedCategory, out parsedLen);
                    }

                    // Fallbacks for empty fields
                    if (string.IsNullOrEmpty(parsedSyncedPrefix))
                    {
                        if (linesDict.ContainsKey(lineAffiliation))
                        {
                            parsedSyncedPrefix = GetSyncedPrefix(linesDict[lineAffiliation].Fats);
                        }
                        if (string.IsNullOrEmpty(parsedSyncedPrefix)) parsedSyncedPrefix = "PDA6.051";
                    }

                    if (string.IsNullOrEmpty(parsedLineName))
                    {
                        parsedLineName = "CABLE " + lineAffiliation;
                    }
                    else if (!parsedLineName.ToUpper().StartsWith("CABLE"))
                    {
                        parsedLineName = "CABLE " + parsedLineName;
                    }

                    if (string.IsNullOrEmpty(parsedFoSpec))
                    {
                        parsedFoSpec = $"FO {cores}C/{tubes}T";
                    }
                    parsedFoSpec = parsedFoSpec.Replace("(", "").Replace(")", "").Trim();

                    if (string.IsNullOrEmpty(parsedCategory))
                    {
                        parsedCategory = poly.Layer.ToUpper().Contains("UG") ? "UG" : "AE";
                    }

                    int totalRoute = (int)Math.Round(poly.GetDistanceAtParameter(poly.EndParam));
                    int slackFdtCount = 1;
                    int fatSlackCount = linesDict.ContainsKey(lineAffiliation) ? linesDict[lineAffiliation].Fats.Count : 0;

                    int tolerancePercent = 5; // default
                    if (!string.IsNullOrEmpty(cableLabelContent))
                    {
                        var tolMatch = System.Text.RegularExpressions.Regex.Match(cableLabelContent, @"Toleransi\s*=\s*(\d+)%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (tolMatch.Success)
                        {
                            int.TryParse(tolMatch.Groups[1].Value, out tolerancePercent);
                        }
                    }

                    int totalSlackCount = slackFdtCount + fatSlackCount;
                    int slackLength = totalSlackCount * 20;
                    int routePlusSlack = totalRoute + slackLength;
                    int toleranceVal = (int)Math.Round(routePlusSlack * (tolerancePercent / 100.0));
                    int calculatedLength = routePlusSlack + toleranceVal;

                    if (!string.IsNullOrEmpty(cableLabelContent) && cableLabelContent.IndexOf("Total Route", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cableDesc = CleanMTextFormat(cableLabelContent);
                        string cleanedLabel = CleanMTextFormat(cableLabelContent);
                        string[] lines = cleanedLabel.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0)
                        {
                            cableName = lines[lines.Length - 1].Trim();
                        }
                        else
                        {
                            cableName = $"{parsedSyncedPrefix} - {parsedLineName} ({parsedFoSpec}) - {parsedCategory} - {calculatedLength} M";
                        }
                    }
                    else
                    {
                        cableDesc = $"Total Route = {totalRoute} M\n" +
                                    $"Total Slack = {totalSlackCount} unit ({slackFdtCount} slack FDT & {fatSlackCount} slack FAT/SF400) @20M\n" +
                                    $"Toleransi = {tolerancePercent}%\n" +
                                    $"Total Length Cable = {totalRoute}+{slackLength} : {routePlusSlack}M + ({routePlusSlack} x {tolerancePercent}%) = {toleranceVal}M\n" +
                                    $"{parsedSyncedPrefix} - {parsedLineName} ({parsedFoSpec}) - {parsedCategory} - {calculatedLength} M";

                        cableName = $"{parsedSyncedPrefix} - {parsedLineName} ({parsedFoSpec}) - {parsedCategory} - {calculatedLength} M";
                    }

                    var cableItem = new EntityItem
                    {
                        EntityHandle = poly.Handle.ToString(),
                        Name = cableName,
                        Category = cores.ToString(),
                        LinePoints = new List<(double Lon, double Lat)>(),
                        Layer = poly.Layer,
                        LineAffiliation = lineAffiliation,
                        Description = cableDesc
                    };

                    if (poly is Polyline pl)
                    {
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            Point2d pt = pl.GetPoint2dAt(i);
                            Point3d wcsPt = new Point3d(pt.X, pt.Y, 0.0);
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(db, wcsPt);
                            cableItem.LinePoints.Add((latLon.X, latLon.Y));
                        }
                    }
                    else if (poly is Line ln)
                    {
                        Point3d wcsPt1 = ln.StartPoint;
                        Point3d wcsPt2 = ln.EndPoint;
                        Point3d latLon1 = TileMapManager.ProjectWcsToLonLat(db, wcsPt1);
                        Point3d latLon2 = TileMapManager.ProjectWcsToLonLat(db, wcsPt2);
                        cableItem.LinePoints.Add((latLon1.X, latLon1.Y));
                        cableItem.LinePoints.Add((latLon2.X, latLon2.Y));
                    }

                    mappedCables.Add(cableItem);
                    mappedRoutes.Add(Tuple.Create(poly, lineAffiliation, false));
                    cableLineAffiliations[poly.ObjectId] = lineAffiliation;

                    if (linesDict.ContainsKey(lineAffiliation))
                    {
                        linesDict[lineAffiliation].DistributionCables.Add(cableItem);
                    }
                    else
                    {
                        linesDict["LINE A"].DistributionCables.Add(cableItem);
                    }
                }

                // 8. Associate Sling Wires to Lines
                foreach (var poly in slingPolylines)
                {
                    double length = Math.Round(poly.GetDistanceAtParameter(poly.EndParam));
                    string name = $"{length} m";

                    // Check if either start or end of the sling wire is close to a fiber cable (within 150m)
                    double minSlingDistToFiber = double.MaxValue;
                    string lineAff = "LINE A";
                    foreach (var fiberPoly in cablePolylines)
                    {
                        try
                        {
                            double dStart = fiberPoly.GetClosestPointTo(poly.StartPoint, false).DistanceTo(poly.StartPoint);
                            double dEnd = fiberPoly.GetClosestPointTo(poly.EndPoint, false).DistanceTo(poly.EndPoint);
                            double dMin = Math.Min(dStart, dEnd);
                            if (dMin < minSlingDistToFiber)
                            {
                                minSlingDistToFiber = dMin;
                                if (cableLineAffiliations.ContainsKey(fiberPoly.ObjectId))
                                    lineAff = cableLineAffiliations[fiberPoly.ObjectId];
                            }
                        }
                        catch {}
                    }

                    if (minSlingDistToFiber > 150.0) continue; // Skip legend/stray sling wires

                    mappedRoutes.Add(Tuple.Create(poly, lineAff, true)); // Add as sling wire (isSling = true)

                    var item = new EntityItem
                    {
                        EntityHandle = poly.Handle.ToString(),
                        Name = name,
                        Category = "SLING WIRE",
                        Layer = poly.Layer,
                        LineAffiliation = lineAff,
                        LinePoints = new List<(double Lon, double Lat)>()
                    };

                    if (poly is Polyline pl)
                    {
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            Point2d pt = pl.GetPoint2dAt(i);
                            Point3d wcsPt = new Point3d(pt.X, pt.Y, 0.0);
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(db, wcsPt);
                            item.LinePoints.Add((latLon.X, latLon.Y));
                        }
                    }
                    else if (poly is Line ln)
                    {
                        Point3d wcsPt1 = ln.StartPoint;
                        Point3d wcsPt2 = ln.EndPoint;
                        Point3d latLon1 = TileMapManager.ProjectWcsToLonLat(db, wcsPt1);
                        Point3d latLon2 = TileMapManager.ProjectWcsToLonLat(db, wcsPt2);
                        item.LinePoints.Add((latLon1.X, latLon1.Y));
                        item.LinePoints.Add((latLon2.X, latLon2.Y));
                    }

                    if (linesDict.ContainsKey(lineAff))
                        linesDict[lineAff].SlingWires.Add(item);
                }

                Func<Point3d, Tuple<string, double, bool>> getClosestLineInfo = (pos) =>
                {
                    double minDist = double.MaxValue;
                    string bestLine = "LINE A";
                    bool isSling = false;

                    foreach (var route in mappedRoutes)
                    {
                        try
                        {
                            Curve poly = route.Item1;
                            if (poly != null)
                            {
                                double d = poly.GetClosestPointTo(pos, false).DistanceTo(pos);
                                if (d < minDist)
                                {
                                    minDist = d;
                                    bestLine = route.Item2;
                                    isSling = route.Item3;
                                }
                            }
                        }
                        catch { }
                    }
                    return Tuple.Create(bestLine, minDist, isSling);
                };

                // 5. Associate Poles to Lines (Deduplicated Texts and Blocks, using label text as name)
                var finalPoles = new List<Tuple<Point3d, string, string, string, double, double, string>>(); // Position, Name, Category, Layer, Rotation, TextHeight, Handle
                
                foreach (Entity poleEnt in poleTexts)
                {
                    Point3d pos = poleEnt is DBText dt ? dt.Position : ((MText)poleEnt).Location;
                    string name = poleEnt is DBText dt2 ? dt2.TextString : ((MText)poleEnt).Text;
                    double rot = poleEnt is DBText dt3 ? dt3.Rotation : ((MText)poleEnt).Rotation;
                    double h = poleEnt is DBText dt4 ? dt4.Height : ((MText)poleEnt).TextHeight;
                    string cat = GetPoleCategory(name, poleEnt.Layer);

                    finalPoles.Add(Tuple.Create(pos, name, cat, poleEnt.Layer, rot, h, poleEnt.Handle.ToString()));
                }

                foreach (var poleBr in poleBlocks)
                {
                    bool duplicate = false;
                    foreach (var fp in finalPoles)
                    {
                        if (poleBr.Position.DistanceTo(fp.Item1) < 2.0)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        string name = poleBr.Name;
                        string cat = GetPoleCategory(name, poleBr.Layer);
                        finalPoles.Add(Tuple.Create(poleBr.Position, name, cat, poleBr.Layer, poleBr.Rotation, 0.5, poleBr.Handle.ToString()));
                    }
                }

                foreach (var pole in finalPoles)
                {
                    Point3d pos = pole.Item1;
                    var lineInfo = getClosestLineInfo(pos);
                    double allowedDist = 25.0;
                    if (lineInfo.Item2 > allowedDist) continue; // Skip legend/stray poles

                    string name = pole.Item2;
                    string cat = pole.Item3;
                    string lyr = pole.Item4;
                    double rot = pole.Item5;
                    double h = pole.Item6;
                    string handle = pole.Item7;

                    Point3d lonLat = TileMapManager.ProjectWcsToLonLat(db, pos);
                    string lineAff = lineInfo.Item1;

                    var item = new EntityItem
                    {
                        EntityHandle = handle,
                        Name = name,
                        Category = cat,
                        Lon = lonLat.X,
                        Lat = lonLat.Y,
                        Rotation = rot,
                        TextHeight = h,
                        Layer = lyr,
                        LineAffiliation = lineAff
                    };

                    if (linesDict.ContainsKey(lineAff))
                        linesDict[lineAff].Poles.Add(item);
                }

                // 6. Associate Closures to Lines
                foreach (var closureBr in closureBlocks)
                {
                    var lineInfo = getClosestLineInfo(closureBr.Position);
                    double allowedDist = 25.0;
                    if (lineInfo.Item2 > allowedDist) continue; // Skip legend/stray closures

                    string name = closureBr.Name.Contains("144") ? "JOINT CLOSURE 144C" : "JOINT CLOSURE 48C";
                    Point3d lonLat = TileMapManager.ProjectWcsToLonLat(db, closureBr.Position);
                    string lineAff = lineInfo.Item1;

                    var item = new EntityItem
                    {
                        EntityHandle = closureBr.Handle.ToString(),
                        Name = name,
                        Category = "JOINT CLOSURE",
                        Lon = lonLat.X,
                        Lat = lonLat.Y,
                        Rotation = closureBr.Rotation,
                        Layer = closureBr.Layer,
                        LineAffiliation = lineAff
                    };

                    if (linesDict.ContainsKey(lineAff))
                        linesDict[lineAff].Closures.Add(item);
                }

                // 7. Associate Handholes to Lines (Custom Markers)
                foreach (var hhBr in handholeBlocks)
                {
                    var lineInfo = getClosestLineInfo(hhBr.Position);
                    double allowedDist = 25.0;
                    if (lineInfo.Item2 > allowedDist) continue; // Skip legend/stray handholes

                    string cat = hhBr.Name.Contains("20") ? "NEW HH 20X20X20" : "NEW HH 40X40X60";
                    Point3d lonLat = TileMapManager.ProjectWcsToLonLat(db, hhBr.Position);
                    string lineAff = lineInfo.Item1;

                    var item = new EntityItem
                    {
                        EntityHandle = hhBr.Handle.ToString(),
                        Name = cat,
                        Category = cat,
                        Lon = lonLat.X,
                        Lat = lonLat.Y,
                        Rotation = hhBr.Rotation,
                        Layer = hhBr.Layer,
                        LineAffiliation = lineAff
                    };

                    if (linesDict.ContainsKey(lineAff))
                        linesDict[lineAff].CustomMarkers.Add(item);
                }

                // Sling wires moved to be processed before getClosestLineInfo/Poles

                // 9. Associate Slack Hangers to Lines (including physical ones and auto-generated ones)
                foreach (var shBr in slackHangerBlocks)
                {
                    var lineInfo = getClosestLineInfo(shBr.Position);
                    double allowedDist = 25.0;
                    if (lineInfo.Item2 > allowedDist) continue; // Skip legend/stray slack hangers

                    string name = "SLACK CABLE HANGER";
                    Point3d lonLat = TileMapManager.ProjectWcsToLonLat(db, shBr.Position);
                    string lineAff = lineInfo.Item1;

                    var item = new EntityItem
                    {
                        EntityHandle = shBr.Handle.ToString(),
                        Name = name,
                        Category = "SLACK HANGER",
                        Lon = lonLat.X,
                        Lat = lonLat.Y,
                        Rotation = shBr.Rotation,
                        Layer = shBr.Layer,
                        LineAffiliation = lineAff
                    };

                    if (linesDict.ContainsKey(lineAff))
                        linesDict[lineAff].SlackHangers.Add(item);
                }

                model.Lines = linesDict.Values.OrderBy(l => l.LineName).ToList();
                foreach (var line in model.Lines)
                {
                    line.Fats = line.Fats.OrderBy(f => f.SequenceName).ToList();

                    // Generate Slack Hangers dynamically for FDT and FATs (ensuring correct APD/ABD count)
                    if (line.SlackHangers.Count == 0)
                    {
                        FdtNode matchedFdt = null;
                        if (!string.IsNullOrEmpty(line.FdtHandle) && model.Fdts != null)
                        {
                            matchedFdt = model.Fdts.FirstOrDefault(f => f.EntityHandle == line.FdtHandle);
                        }
                        if (matchedFdt == null)
                        {
                            matchedFdt = model.Fdt;
                        }

                        if (matchedFdt != null && !string.IsNullOrEmpty(matchedFdt.EntityHandle))
                        {
                            line.SlackHangers.Add(new EntityItem
                            {
                                Category = "SLACK HANGER",
                                Name = "SLACK_FDT",
                                Lon = matchedFdt.Longitude,
                                Lat = matchedFdt.Latitude,
                                Layer = "SLACK HANGER",
                                LineAffiliation = line.LineName
                            });
                        }

                        foreach (var fat in line.Fats)
                        {
                            line.SlackHangers.Add(new EntityItem
                            {
                                Category = "SLACK HANGER",
                                Name = $"SLACK_{fat.SequenceName}",
                                Lon = fat.FatLon,
                                Lat = fat.FatLat,
                                Layer = "SLACK HANGER",
                                LineAffiliation = line.LineName
                            });
                        }
                    }
                }

                progressCallback?.Invoke(95, "Menyusun struktur rute kabel & tiang...");
                tr.Commit();
            }

            progressCallback?.Invoke(100, "Pemindaian database selesai!");
            return model;
        }

        private static int GetCableCores(Curve poly, Database db, Transaction tr)
        {
            int cores = 24;
            bool capacityFound = false;

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

        private static string CleanMTextFormat(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string cleaned = text.Replace("\\P", "\n").Replace("\\p", "\n").Replace("\\N", "\n").Replace("\\n", "\n");

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\\[CcHhFfAaTtiWwQq]\d+(\.\d+)?;?", "");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\\f[^;]+;", "");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[{}]", "");

            return cleaned.Trim();
        }

        private static void ParseCableLabel(string text, out string syncedPrefix, out string lineName, out string foSpec, out string category, out int parsedLength)
        {
            syncedPrefix = "";
            lineName = "";
            foSpec = "";
            category = "";
            parsedLength = 0;

            if (string.IsNullOrEmpty(text)) return;

            string cleaned = CleanMTextFormat(text);
            string[] lines = cleaned.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            // 1. Try to parse as Scenario 2 (last line containing full info)
            string lastLine = lines[lines.Length - 1].Trim();
            var matchS2 = System.Text.RegularExpressions.Regex.Match(lastLine, 
                @"^([^-]+)\s*-\s*([^-]+)\s*\(([^)]+)\)\s*-\s*([^-]+)\s*-\s*(\d+)\s*M$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (matchS2.Success)
            {
                syncedPrefix = matchS2.Groups[1].Value.Trim();
                lineName = matchS2.Groups[2].Value.Trim();
                foSpec = matchS2.Groups[3].Value.Trim();
                category = matchS2.Groups[4].Value.Trim();
                int.TryParse(matchS2.Groups[5].Value.Trim(), out parsedLength);
                return;
            }

            // 2. Scenario 1 parser
            string firstLine = lines[0].Trim();
            var firstLineParts = System.Text.RegularExpressions.Regex.Split(firstLine, @"\s*-\s*");
            if (firstLineParts.Length >= 3)
            {
                syncedPrefix = firstLineParts[1].Trim();
                lineName = firstLineParts[2].Trim();
            }
            else if (firstLineParts.Length == 2)
            {
                syncedPrefix = firstLineParts[0].Trim();
                lineName = firstLineParts[1].Trim();
            }

            if (lines.Length >= 2)
            {
                string secondLine = lines[1].Trim();
                
                // Parse foSpec using parenthesis
                var foMatch = System.Text.RegularExpressions.Regex.Match(secondLine, @"\(([^)]+)\)");
                if (foMatch.Success)
                {
                    foSpec = foMatch.Groups[1].Value.Trim();
                }

                // Parse category and length
                var catMatch = System.Text.RegularExpressions.Regex.Match(secondLine, @"(?:\)|-|^|\s)([A-Za-z]+)\s*-\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (catMatch.Success)
                {
                    category = catMatch.Groups[1].Value.Trim();
                    int.TryParse(catMatch.Groups[2].Value, out parsedLength);
                }
            }
        }

        private static string GetSyncedPrefix(List<FatData> fats)
        {
            if (fats != null && fats.Count > 0)
            {
                string seq = fats[0].SequenceName;
                int lastDot = seq.LastIndexOf('.');
                if (lastDot > 0)
                {
                    return seq.Substring(0, lastDot);
                }
            }
            return "PDA6.051";
        }
    }
}
