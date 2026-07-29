using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using FTTHBasemap.UI;

namespace FTTHBasemap
{
    public static class LabelingManager
    {
        public static List<string> ManualFdtOrderHandles = new List<string>();

        public static void GeneratePoleLabels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var panel = PaletteManager.BasemapPanel;

            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active. Please open it using FTTH_SHOWPANEL.");
                return;
            }

            // Read settings from UI safely
            string locationCode = "";
            int startIndex = 1;
            bool isNewPole = true;
            bool isScenarioConstant = true;
            bool isScenarioLineTrace = false;

            panel.Dispatcher.Invoke(() =>
            {
                locationCode = panel.GetLabelPoleLocationCode();
                startIndex = panel.GetLabelPoleStartIndex();
                isNewPole = panel.IsLabelPoleNew();
                isScenarioConstant = panel.IsLabelPoleScenarioConstant();
                isScenarioLineTrace = panel.IsLabelPoleScenarioLineTrace();
            });

            if (string.IsNullOrEmpty(locationCode))
            {
                locationCode = "PDA6";
            }
            locationCode = locationCode.ToUpper();

            ed.WriteMessage($"\n[FTTH] Generating pole labels... (Location: {locationCode}, Start Index: {startIndex}, Scenario: {(isScenarioConstant ? "Constant" : "Series")})");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1. Gather all poles
                    var poleList = new List<PoleInfo>();

                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br == null) continue;

                            // Resolve block name (including dynamic blocks)
                            string blockName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    blockName = btr.Name;
                                }
                                catch { }
                            }

                            string layerName = br.Layer;

                            if (IsPoleBlock(blockName, layerName, br, tr))
                            {
                                bool isExt = IsExistingPole(blockName, layerName);
                                poleList.Add(new PoleInfo
                                {
                                    Position = br.Position,
                                    Layer = br.Layer,
                                    BlockName = blockName,
                                    IsExisting = isExt
                                });
                            }
                        }
                    }

                    if (poleList.Count == 0)
                    {
                        ed.WriteMessage("\n[FTTH] No poles found in ModelSpace.");
                        tr.Commit();
                        return;
                    }

                    // 2. Sort poles based on scenario
                    List<PoleInfo> orderedPoles;
                    if (isScenarioConstant)
                    {
                        orderedPoles = poleList;
                    }
                    else if (isScenarioLineTrace)
                    {
                        orderedPoles = SortPolesByLineTrace(poleList, modelSpace, tr);
                    }
                    else
                    {
                        orderedPoles = poleList.OrderBy(p => p.Position.X).ThenBy(p => p.Position.Y).ToList();
                    }

                    // Filter based on whether we are labeling New Poles or Existing Poles
                    var filteredPoles = orderedPoles.Where(p => p.IsExisting == !isNewPole).ToList();

                    // 3. Clear existing label layers to prevent duplicate overlaps (only for the layers we are generating labels on)
                    var labelLayers = filteredPoles.Select(p => GetTargetPoleLabelLayer(p.BlockName, p.Layer, p.IsExisting)).Distinct().ToList();
                    ClearLabelLayers(db, tr, modelSpace, labelLayers);

                    // 4. Generate new labels
                    int sequence = startIndex;
                    int labelCount = 0;

                    string labelJsonFilename = "Pole Label.JSON";
                    string labelJsonPath = panel.Dispatcher.Invoke(() => panel.GetAssetPath(labelJsonFilename));

                    if (!File.Exists(labelJsonPath))
                    {
                        ed.WriteMessage($"\n[FTTH] Error: Pole Label template JSON not found at: {labelJsonPath}");
                        tr.Commit();
                        return;
                    }

                    foreach (var pole in filteredPoles)
                    {
                        string seqStr;
                        if (isScenarioConstant)
                        {
                            seqStr = "000";
                        }
                        else
                        {
                            seqStr = sequence.ToString();
                            if (sequence < 10) seqStr = "00" + seqStr;
                            else if (sequence < 100) seqStr = "0" + seqStr;
                            sequence++;
                        }

                        string textLabel = pole.IsExisting 
                            ? $"EXT.MR.{locationCode}.P{seqStr}" 
                            : $"MR.{locationCode}.P{seqStr}";

                        string targetLabelLayer = GetTargetPoleLabelLayer(pole.BlockName, pole.Layer, pole.IsExisting);

                        // Create label layer if it doesn't exist, forced to Red (color index 1)
                        GetOrCreateLayer(db, tr, targetLabelLayer, 1);

                        // Draw label using template asset (rotation 0.0, scale 1.0)
                        var createdIds = AssetDrawer.DrawAsset(db, tr, modelSpace, labelJsonPath, pole.Position, 0.0, 1.0, targetLabelLayer);
                        
                        // Find the MText entity in createdIds and update its text content
                        foreach (ObjectId entId in createdIds)
                        {
                            MText mt = tr.GetObject(entId, OpenMode.ForWrite) as MText;
                            if (mt != null)
                            {
                                mt.Contents = textLabel;
                                break;
                            }
                        }

                        labelCount++;
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully generated {labelCount} pole labels.");
                    panel.Dispatcher.Invoke(() => panel.LogMessage($"Generated {labelCount} pole labels."));
                }
            }

            doc.Editor.UpdateScreen();
        }

        private static bool IsPoleBlock(string blockName, string layerName, BlockReference br, Transaction tr)
        {
            string upperBlock = blockName.ToUpper();
            string upperLayer = layerName.ToUpper();

            // Direct match block names
            if (upperBlock == "NP725" || upperBlock == "NP73" || upperBlock == "NP74" || upperBlock == "NP94" ||
                upperBlock == "EP725" || upperBlock == "EP73" || upperBlock == "EP74" || upperBlock == "EP94" ||
                upperBlock == "EXT_POLE" || upperBlock == "POLE73IN" || upperBlock == "POLE73EX" || upperBlock == "POLETEL" ||
                upperBlock == "EXT TEL")
            {
                return true;
            }

            // Layer checks
            if (upperLayer.StartsWith("FTTH-POLE-") || upperLayer == "EXT POLE" || upperLayer == "NEW POLE 7M 2.5INCH")
            {
                return true;
            }

            // XData check
            try
            {
                if (br.XData != null)
                {
                    foreach (TypedValue tv in br.XData)
                    {
                        if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName && tv.Value?.ToString() == "FTTH_POLE_DATA")
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsExistingPole(string blockName, string layerName)
        {
            string upperBlock = blockName.ToUpper();
            string upperLayer = layerName.ToUpper();

            return upperBlock.Contains("EXT") || 
                   upperBlock.Contains("EP") || 
                   upperLayer.Contains("EXISTING") || 
                   upperLayer.Contains("EXIST") || 
                   upperBlock.StartsWith("EP");
        }

        private static short GetLayerColor(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
                var ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForRead);
                return ltr.Color.ColorIndex;
            }
            return 1; // Default to Red
        }

        private static void GetOrCreateLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                using (var ltr = new LayerTableRecord())
                {
                    ltr.Name = layerName;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
            }
            else
            {
                var ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
            }
        }

        private static void ClearLabelLayers(Database db, Transaction tr, BlockTableRecord modelSpace, List<string> labelLayers)
        {
            var hashLayers = new HashSet<string>(labelLayers.Select(l => l.ToUpper()));

            foreach (ObjectId id in modelSpace)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Entity ent)
                {
                    if (hashLayers.Contains(ent.Layer.ToUpper()))
                    {
                        if (ent is DBText || ent is MText)
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                        }
                    }
                }
            }
        }

        private class PoleInfo
        {
            public Point3d Position { get; set; }
            public string Layer { get; set; }
            public string BlockName { get; set; }
            public bool IsExisting { get; set; }
        }

        private class FdtInfo
        {
            public Point3d Position { get; set; }
            public string Handle { get; set; }
        }

        private static List<double> GetCandidateDistancesAlongPolyline(Polyline poly, Point3d polePos, double maxDist = 1.5)
        {
            var candidates = new List<double>();
            int numVertices = poly.NumberOfVertices;
            Point3d poleXY = new Point3d(polePos.X, polePos.Y, 0.0);

            double distToSegmentStart = 0.0;
            for (int i = 0; i < numVertices - 1; i++)
            {
                Point3d pStart = poly.GetPoint3dAt(i);
                Point3d pEnd = poly.GetPoint3dAt(i + 1);

                Point3d pStartXY = new Point3d(pStart.X, pStart.Y, 0.0);
                Point3d pEndXY = new Point3d(pEnd.X, pEnd.Y, 0.0);

                using (LineSegment3d seg = new LineSegment3d(pStartXY, pEndXY))
                {
                    Point3d closestPt = seg.GetClosestPointTo(poleXY).Point;
                    double d = closestPt.DistanceTo(poleXY);
                    if (d < maxDist)
                    {
                        double distOnSeg = pStartXY.DistanceTo(closestPt);
                        candidates.Add(distToSegmentStart + distOnSeg);
                    }
                }
                distToSegmentStart += pStartXY.DistanceTo(pEndXY);
            }

            if (poly.Closed && numVertices > 1)
            {
                Point3d pStart = poly.GetPoint3dAt(numVertices - 1);
                Point3d pEnd = poly.GetPoint3dAt(0);

                Point3d pStartXY = new Point3d(pStart.X, pStart.Y, 0.0);
                Point3d pEndXY = new Point3d(pEnd.X, pEnd.Y, 0.0);

                using (LineSegment3d seg = new LineSegment3d(pStartXY, pEndXY))
                {
                    Point3d closestPt = seg.GetClosestPointTo(poleXY).Point;
                    double d = closestPt.DistanceTo(poleXY);
                    if (d < maxDist)
                    {
                        double distOnSeg = pStartXY.DistanceTo(closestPt);
                        candidates.Add(distToSegmentStart + distOnSeg);
                    }
                }
            }

            return candidates;
        }

        private static List<PoleInfo> SortPolesByLineTrace(List<PoleInfo> poleList, BlockTableRecord modelSpace, Transaction tr)
        {
            var orderedPoles = new List<PoleInfo>();
            var labeledPoles = new HashSet<PoleInfo>();

            try
            {
                // 1. Find all FDT blocks in the drawing
                var fdtList = new List<FdtInfo>();
                foreach (ObjectId id in modelSpace)
                {
                    if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                    {
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;

                        string blockName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            try
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                blockName = btr.Name;
                            }
                            catch { }
                        }

                        if (blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            fdtList.Add(new FdtInfo
                            {
                                Position = br.Position,
                                Handle = br.Handle.ToString()
                            });
                        }
                    }
                }

                var fdtPoints = new List<Point3d>();
                if (fdtList.Count == 0)
                {
                    fdtPoints.Add(Point3d.Origin);
                }
                else
                {
                    // Sort FDTs
                    if (ManualFdtOrderHandles != null && ManualFdtOrderHandles.Count > 0)
                    {
                        // Sort based on the manual order. FDTs not in the manual list go to the end, sorted spatially.
                        fdtList = fdtList.OrderBy(f =>
                        {
                            int idx = ManualFdtOrderHandles.IndexOf(f.Handle);
                            return idx >= 0 ? idx : int.MaxValue;
                        })
                        .ThenBy(f => f.Position.X)
                        .ThenBy(f => f.Position.Y)
                        .ToList();
                    }
                    else
                    {
                        // Default spatial sorting
                        fdtList = fdtList.OrderBy(f => f.Position.X).ThenBy(f => f.Position.Y).ToList();
                    }

                    fdtPoints = fdtList.Select(f => f.Position).ToList();
                }

                // 2. Find all wire lines/polylines on the "WIRE CABLE" layer
                var wirePointsList = new List<List<Point3d>>();
                foreach (ObjectId id in modelSpace)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is Entity ent && ent.Layer.Equals("WIRE CABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ent is Line line)
                        {
                            wirePointsList.Add(new List<Point3d> { line.StartPoint, line.EndPoint });
                        }
                        else if (ent is Polyline poly)
                        {
                            var pts = new List<Point3d>();
                            for (int i = 0; i < poly.NumberOfVertices; i++)
                            {
                                pts.Add(poly.GetPoint3dAt(i));
                            }
                            wirePointsList.Add(pts);
                        }
                        else if (ent is Polyline3d poly3d)
                        {
                            var pts = new List<Point3d>();
                            foreach (ObjectId vId in poly3d)
                            {
                                DBObject vObj = tr.GetObject(vId, OpenMode.ForRead);
                                if (vObj is PolylineVertex3d v3d)
                                {
                                    pts.Add(v3d.Position);
                                }
                            }
                            wirePointsList.Add(pts);
                        }
                    }
                }

                // Build adjacency list for poles connected via WIRE CABLE
                var wireAdjacency = new Dictionary<PoleInfo, List<PoleInfo>>();
                foreach (var pole in poleList)
                {
                    wireAdjacency[pole] = new List<PoleInfo>();
                }

                foreach (var pts in wirePointsList)
                {
                    var connectedPoles = new List<PoleInfo>();
                    foreach (var pole in poleList)
                    {
                        bool isClose = false;
                        foreach (var pt in pts)
                        {
                            if (pole.Position.DistanceTo(pt) < 1.5)
                            {
                                isClose = true;
                                break;
                            }
                        }
                        if (isClose)
                        {
                            connectedPoles.Add(pole);
                        }
                    }

                    for (int i = 0; i < connectedPoles.Count; i++)
                    {
                        for (int j = i + 1; j < connectedPoles.Count; j++)
                        {
                            var p1 = connectedPoles[i];
                            var p2 = connectedPoles[j];
                            if (!wireAdjacency[p1].Contains(p2)) wireAdjacency[p1].Add(p2);
                            if (!wireAdjacency[p2].Contains(p1)) wireAdjacency[p2].Add(p1);
                        }
                    }
                }

                // 3. Find all main cable polylines (layer starts with "FTTH-CABLE-")
                var cablePolylines = new List<Polyline>();
                foreach (ObjectId id in modelSpace)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is Polyline poly && poly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                    {
                        cablePolylines.Add(poly);
                    }
                    else if (obj is Polyline2d poly2d && poly2d.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
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
                            cablePolylines.Add(dummyPoly);
                        }
                        catch {}
                    }
                }

                // Identify which poles are on the main paths vs HC-only poles
                var mainPathPoles = new HashSet<PoleInfo>();
                foreach (var pole in poleList)
                {
                    foreach (var poly in cablePolylines)
                    {
                        try
                        {
                            Point3d closestPt = poly.GetClosestPointTo(pole.Position, false);
                            if (pole.Position.DistanceTo(closestPt) < 1.5)
                            {
                                mainPathPoles.Add(pole);
                                break;
                            }
                        }
                        catch { }
                    }
                }

                var hcPoles = new HashSet<PoleInfo>(poleList.Where(p => !mainPathPoles.Contains(p)));

                // Sort the cable polylines by associated FDT first, then by distance to that FDT
                var sortedCables = cablePolylines.Select(poly =>
                {
                    double minDist = double.MaxValue;
                    int bestFdtIdx = 0;
                    Point3d startPt = poly.StartPoint;
                    Point3d endPt = poly.EndPoint;
                    for (int idx = 0; idx < fdtPoints.Count; idx++)
                    {
                        var fdt = fdtPoints[idx];
                        double dStart = startPt.DistanceTo(fdt);
                        double dEnd = endPt.DistanceTo(fdt);
                        double d = Math.Min(dStart, dEnd);
                        if (d < minDist)
                        {
                            minDist = d;
                            bestFdtIdx = idx;
                        }
                    }
                    return new { Polyline = poly, FdtIdx = bestFdtIdx, DistanceToFDT = minDist };
                })
                .OrderBy(item => item.FdtIdx)
                .ThenBy(item => item.DistanceToFDT)
                .Select(item => item.Polyline)
                .ToList();

                // 4. Trace along each sorted cable line
                foreach (var poly in sortedCables)
                {
                    Point3d startPt = poly.StartPoint;
                    Point3d endPt = poly.EndPoint;
                    
                    double minStartDist = double.MaxValue;
                    double minEndDist = double.MaxValue;
                    foreach (var fdt in fdtPoints)
                    {
                        minStartDist = Math.Min(minStartDist, startPt.DistanceTo(fdt));
                        minEndDist = Math.Min(minEndDist, endPt.DistanceTo(fdt));
                    }

                    bool startIsCloserToFDT = minStartDist <= minEndDist;

                    var polesOnCable = new List<Tuple<PoleInfo, double>>();
                    foreach (var pole in poleList)
                    {
                        try
                        {
                            var candidates = GetCandidateDistancesAlongPolyline(poly, pole.Position, 1.5);
                            if (candidates.Count > 0)
                            {
                                double chosenDist = startIsCloserToFDT ? candidates.Min() : candidates.Max();
                                polesOnCable.Add(new Tuple<PoleInfo, double>(pole, chosenDist));
                            }
                        }
                        catch { }
                    }

                    List<PoleInfo> sortedPolesOnCable;
                    if (startIsCloserToFDT)
                    {
                        sortedPolesOnCable = polesOnCable.OrderBy(t => t.Item2).Select(t => t.Item1).ToList();
                    }
                    else
                    {
                        sortedPolesOnCable = polesOnCable.OrderByDescending(t => t.Item2).Select(t => t.Item1).ToList();
                    }

                    foreach (var pole in sortedPolesOnCable)
                    {
                        if (!labeledPoles.Contains(pole))
                        {
                            labeledPoles.Add(pole);
                            orderedPoles.Add(pole);

                            // Trigger DFS to label all connected HC poles
                            LabelHCPolesDFS(pole, wireAdjacency, hcPoles, labeledPoles, orderedPoles);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                    "\n[FTTH] Warning in Line Trace: " + ex.Message + ". Falling back to spatial ordering."
                );
            }

            // 5. Fail-safe: add any remaining unlabeled poles
            var remainingPoles = poleList.Where(p => !labeledPoles.Contains(p))
                                         .OrderBy(p => p.Position.X)
                                         .ThenBy(p => p.Position.Y)
                                         .ToList();
            foreach (var pole in remainingPoles)
            {
                labeledPoles.Add(pole);
                orderedPoles.Add(pole);
            }

            return orderedPoles;
        }

        private static void LabelHCPolesDFS(
            PoleInfo currentPole,
            Dictionary<PoleInfo, List<PoleInfo>> wireAdjacency,
            HashSet<PoleInfo> hcPoles,
            HashSet<PoleInfo> labeledPoles,
            List<PoleInfo> orderedPoles)
        {
            if (wireAdjacency.ContainsKey(currentPole))
            {
                var neighbors = wireAdjacency[currentPole]
                    .Where(p => hcPoles.Contains(p) && !labeledPoles.Contains(p))
                    .OrderBy(p => p.Position.DistanceTo(currentPole.Position))
                    .ToList();

                foreach (var neighbor in neighbors)
                {
                    if (labeledPoles.Contains(neighbor)) continue;

                    labeledPoles.Add(neighbor);
                    orderedPoles.Add(neighbor);

                }
            }
        }

        public static void GenerateFatLabels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var panel = PaletteManager.BasemapPanel;

            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active. Please open it using FTTH_SHOWPANEL.");
                return;
            }

            string rawPrefix = "";
            int startIndex = 1;

            panel.Dispatcher.Invoke(() =>
            {
                rawPrefix = panel.GetLabelFatPrefix();
                startIndex = panel.GetLabelFatStartIndex();
            });

            if (string.IsNullOrEmpty(rawPrefix))
            {
                rawPrefix = "PDA6.051";
            }

            ed.WriteMessage($"\n[FTTH] Generating FAT labels... (Prefix: {rawPrefix}, Start Index: {startIndex})");

            // Split prefix by '.' to parse cluster name and FDT base code
            string clusterCode = "PDA6";
            string baseFdtCode = "051";
            string[] prefixParts = rawPrefix.Split('.');
            if (prefixParts.Length >= 2)
            {
                clusterCode = prefixParts[0];
                baseFdtCode = prefixParts[1];
            }
            else if (prefixParts.Length == 1)
            {
                clusterCode = prefixParts[0];
                baseFdtCode = "051"; // Default fallback if no dot
            }

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1. Find all FDT blocks in the drawing
                    var fdtPoints = new List<Point3d>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br == null) continue;

                            string blockName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    blockName = btr.Name;
                                }
                                catch { }
                            }

                            if (blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                fdtPoints.Add(br.Position);
                            }
                        }
                    }

                    // Sort FDTs to be deterministic (left-to-right, top-to-bottom)
                    fdtPoints = fdtPoints.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

                    // If no FDT blocks found, add coordinate origin as a fallback FDT 1
                    if (fdtPoints.Count == 0)
                    {
                        fdtPoints.Add(Point3d.Origin);
                    }

                    // 2. Find all main cable polylines (layer starts with "FTTH-CABLE-")
                    var cablePolylines = new List<Polyline>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Polyline poly && poly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                        {
                            cablePolylines.Add(poly);
                        }
                    }

                    // 3. Find all FAT label MText/DBText entities
                    var fatLabels = new List<FatLabelInfo>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        string text = "";
                        Point3d pos = Point3d.Origin;
                        string layerName = "";

                        if (obj is MText mt)
                        {
                            text = mt.Contents;
                            pos = mt.Location;
                            layerName = mt.Layer;
                        }
                        else if (obj is DBText dt)
                        {
                            text = dt.TextString;
                            pos = dt.Position;
                            layerName = dt.Layer;
                        }
                        else
                        {
                            continue;
                        }

                        bool isFat = layerName.Equals("FTTH-FAT", StringComparison.OrdinalIgnoreCase) ||
                                     layerName.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) ||
                                     IsFatLabel(text) ||
                                     System.Text.RegularExpressions.Regex.IsMatch(text, @"^[A-Za-z0-9]+\.[A-Za-z0-9]+\.[A-Z]\d{2}$");

                        if (isFat)
                        {
                            fatLabels.Add(new FatLabelInfo
                            {
                                EntityId = id,
                                Position = pos,
                                CurrentText = text,
                                Layer = layerName
                            });
                        }
                    }

                    if (fatLabels.Count == 0)
                    {
                        ed.WriteMessage("\n[FTTH] No FAT labels found in ModelSpace.");
                        tr.Commit();
                        return;
                    }

                    // 4. Map each cable polyline to its closest FDT
                    var cablesWithFdt = sortedCablesMappedToFdt(cablePolylines, fdtPoints);

                    // Group cables by FDT index
                    var fdtToCables = new Dictionary<int, List<Polyline>>();
                    for (int i = 0; i < fdtPoints.Count; i++)
                    {
                        fdtToCables[i] = new List<Polyline>();
                    }
                    foreach (var item in cablesWithFdt)
                    {
                        fdtToCables[item.FdtIndex].Add(item.Polyline);
                    }

                    // 5. Track labeled FAT entities to prevent duplicate assignments
                    var labeledFatIds = new HashSet<ObjectId>();
                    int labelCount = 0;

                    // Get list of active FDT indices (indices in fdtPoints that actually have cables mapped to them)
                    var activeFdtIndices = fdtToCables.Where(kvp => kvp.Value.Count > 0)
                                                      .Select(kvp => kvp.Key)
                                                      .OrderBy(idx => idx)
                                                      .ToList();

                    if (activeFdtIndices.Count > 0)
                    {
                        for (int activeIdx = 0; activeIdx < activeFdtIndices.Count; activeIdx++)
                        {
                            int fdtIdx = activeFdtIndices[activeIdx];
                            Point3d fdtPt = fdtPoints[fdtIdx];

                             // Calculate FDT code for this active FDT index based on activeIdx (0-based order of active FDTs)
                             string currentFdtCode = CalculateFdtCode(baseFdtCode, activeIdx);

                             // Sync prefix with existing FDT label if it exists in the drawing
                             string syncedPrefix = GetPrefixFromExistingFdtLabel(fdtPt, db, tr, clusterCode, currentFdtCode);
                             string currentCluster = clusterCode;
                             string currentFdt = currentFdtCode;
                             string[] syncedParts = syncedPrefix.Split('.');
                             if (syncedParts.Length >= 2)
                             {
                                 currentCluster = syncedParts[0];
                                 currentFdt = syncedParts[1];
                             }

                            // Sort cables of this FDT by distance to the FDT
                            var cablesForThisFdt = fdtToCables[fdtIdx]
                                .Select(poly =>
                                {
                                    double dStart = poly.StartPoint.DistanceTo(fdtPt);
                                    double dEnd = poly.EndPoint.DistanceTo(fdtPt);
                                    return new { Polyline = poly, Dist = Math.Min(dStart, dEnd) };
                                })
                                .OrderBy(item => item.Dist)
                                .Select(item => item.Polyline)
                                .ToList();

                            // Letter index for lines (A, B, C...)
                            int lineIndex = 0;

                            foreach (var poly in cablesForThisFdt)
                            {
                                char lineLetter = (char)('A' + (lineIndex % 26));
                                string lineCode = lineLetter.ToString();
                                if (lineIndex >= 26)
                                {
                                    lineCode = ((char)('A' + (lineIndex / 26 - 1))).ToString() + ((char)('A' + (lineIndex % 26))).ToString();
                                }
                                lineIndex++;

                                // Find which endpoint of the polyline is closer to the FDT
                                Point3d startPt = poly.StartPoint;
                                Point3d endPt = poly.EndPoint;
                                bool startIsCloser = startPt.DistanceTo(fdtPt) <= endPt.DistanceTo(fdtPt);

                                // Find all FAT labels close to this polyline (within 2.0 meters)
                                var fatsOnCable = new List<Tuple<FatLabelInfo, double>>();
                                foreach (var fat in fatLabels)
                                {
                                    if (labeledFatIds.Contains(fat.EntityId)) continue;

                                    try
                                    {
                                        Point3d closestPt = poly.GetClosestPointTo(fat.Position, false);
                                        if (fat.Position.DistanceTo(closestPt) < 2.0)
                                        {
                                            double distAlong = poly.GetDistAtPoint(closestPt);
                                            fatsOnCable.Add(new Tuple<FatLabelInfo, double>(fat, distAlong));
                                        }
                                    }
                                    catch { }
                                }

                                // Sort FATs along the polyline from FDT-side outward
                                List<FatLabelInfo> sortedFats;
                                if (startIsCloser)
                                {
                                    sortedFats = fatsOnCable.OrderBy(t => t.Item2).Select(t => t.Item1).ToList();
                                }
                                else
                                {
                                    sortedFats = fatsOnCable.OrderByDescending(t => t.Item2).Select(t => t.Item1).ToList();
                                }

                                // Label each FAT sequentially
                                int fatSeq = startIndex;
                                foreach (var fat in sortedFats)
                                {
                                    labeledFatIds.Add(fat.EntityId);
                                    string seqStr = fatSeq.ToString("D2");
                                    fatSeq++;

                                    // Formatted label: {Cluster}.{FDT}.{Line}{Number}
                                    string newLabel = $"{currentCluster}.{currentFdt}.{lineCode}{seqStr}";

                                    // Update entity
                                    DBObject entObj = tr.GetObject(fat.EntityId, OpenMode.ForWrite);
                                    if (entObj is MText mt)
                                    {
                                        mt.Contents = newLabel;
                                    }
                                    else if (entObj is DBText dt)
                                    {
                                        dt.TextString = newLabel;
                                    }
                                    labelCount++;
                                }
                            }
                        }
                    }

                    // 6. Fail-safe: Label any remaining/unconnected FATs
                    var remainingFats = fatLabels.Where(f => !labeledFatIds.Contains(f.EntityId))
                                                 .OrderBy(f => f.Position.X)
                                                 .ThenBy(f => f.Position.Y)
                                                 .ToList();
                    int fallbackSeq = startIndex;
                    foreach (var fat in remainingFats)
                    {
                        labeledFatIds.Add(fat.EntityId);
                        string seqStr = fallbackSeq.ToString("D2");
                        fallbackSeq++;

                        string currentFdtCode = CalculateFdtCode(baseFdtCode, 0);
                        string newLabel = $"{clusterCode}.{currentFdtCode}.X{seqStr}";

                        DBObject entObj = tr.GetObject(fat.EntityId, OpenMode.ForWrite);
                        if (entObj is MText mt)
                        {
                            mt.Contents = newLabel;
                        }
                        else if (entObj is DBText dt)
                        {
                            dt.TextString = newLabel;
                        }
                        labelCount++;
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully generated {labelCount} FAT labels.");
                    panel.Dispatcher.Invoke(() => panel.LogMessage($"Generated {labelCount} FAT labels."));
                }
            }

            doc.Editor.Regen();
            doc.Editor.UpdateScreen();
        }

        private static List<CableMappingItem> sortedCablesMappedToFdt(List<Polyline> cablePolylines, List<Point3d> fdtPoints)
        {
            var result = new List<CableMappingItem>();
            foreach (var poly in cablePolylines)
            {
                double minDist = double.MaxValue;
                int nearestFdtIdx = 0;
                Point3d startPt = poly.StartPoint;
                Point3d endPt = poly.EndPoint;

                for (int i = 0; i < fdtPoints.Count; i++)
                {
                    double dStart = startPt.DistanceTo(fdtPoints[i]);
                    double dEnd = endPt.DistanceTo(fdtPoints[i]);
                    double d = Math.Min(dStart, dEnd);
                    if (d < minDist)
                    {
                        minDist = d;
                        nearestFdtIdx = i;
                    }
                }
                result.Add(new CableMappingItem
                {
                    Polyline = poly,
                    Distance = minDist,
                    FdtIndex = nearestFdtIdx
                });
            }
            return result.OrderBy(r => r.Distance).ToList();
        }

        private static bool IsFatLabel(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.Contains("\\P"))
            {
                text = text.Split(new string[] { "\\P" }, StringSplitOptions.None)[0];
            }
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\\[A-Za-z].*?;", "");
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"^(?:FDT\d+-)?FAT-(?:TEMP|[A-Z]\d{2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string CalculateFdtCode(string baseFdtCode, int fdtIndex)
        {
            var match = System.Text.RegularExpressions.Regex.Match(baseFdtCode, @"\d+$");
            if (match.Success)
            {
                string digits = match.Value;
                int num = int.Parse(digits);
                int width = digits.Length;
                int incrementedNum = num + fdtIndex;
                string newDigits = incrementedNum.ToString().PadLeft(width, '0');
                string prefixPart = baseFdtCode.Substring(0, baseFdtCode.Length - digits.Length);
                return prefixPart + newDigits;
            }
            return baseFdtCode + (fdtIndex > 0 ? fdtIndex.ToString() : "");
        }

        private class CableMappingItem
        {
            public Polyline Polyline { get; set; }
            public double Distance { get; set; }
            public int FdtIndex { get; set; }
        }

        private class FatLabelInfo
        {
            public ObjectId EntityId { get; set; }
            public Point3d Position { get; set; }
            public string CurrentText { get; set; }
            public string Layer { get; set; }
        }

        public static void GenerateFdtLabels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var panel = PaletteManager.BasemapPanel;

            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active. Please open it using FTTH_SHOWPANEL.");
                return;
            }

            string rawFatPrefix = "";
            string oltCode = "";
            panel.Dispatcher.Invoke(() =>
            {
                rawFatPrefix = panel.GetLabelFatPrefix();
                oltCode = panel.GetLabelFdtSelectedOltCode();
            });

            if (string.IsNullOrEmpty(rawFatPrefix))
            {
                rawFatPrefix = "PDA6.051";
            }

            // Split rawFatPrefix by '.' to parse cluster name and FDT base code
            string clusterCode = "PDA6";
            string baseFdtCode = "051";
            string[] prefixParts = rawFatPrefix.Split('.');
            if (prefixParts.Length >= 2)
            {
                clusterCode = prefixParts[0];
                baseFdtCode = prefixParts[1];
            }
            else if (prefixParts.Length == 1)
            {
                clusterCode = prefixParts[0];
                baseFdtCode = "051";
            }

            ed.WriteMessage($"\n[FTTH] Generating FDT labels... (Cluster: {clusterCode}, OLT Code: {oltCode})");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1. Scan all FDT block references in ModelSpace
                    var fdtBlocks = new List<FdtBlockInfo>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br == null) continue;

                            string blockName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    blockName = btr.Name;
                                }
                                catch { }
                            }

                            bool isFdtBlock = blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (isFdtBlock)
                            {
                                string capacity = "72C";
                                if (blockName.Contains("48") || br.Layer.Contains("48"))
                                {
                                    capacity = "48C";
                                }
                                else if (blockName.Contains("72") || br.Layer.Contains("72"))
                                {
                                    capacity = "72C";
                                }

                                // Scan block attributes for capacity
                                if (br.AttributeCollection.Count > 0)
                                {
                                    foreach (ObjectId attId in br.AttributeCollection)
                                    {
                                        AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                        if (attRef != null)
                                        {
                                            string attVal = attRef.TextString;
                                            if (attVal.IndexOf("48", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                capacity = "48C";
                                                break;
                                            }
                                            else if (attVal.IndexOf("72", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                capacity = "72C";
                                                break;
                                            }
                                        }
                                    }
                                }

                                fdtBlocks.Add(new FdtBlockInfo
                                {
                                    Position = br.Position,
                                    Capacity = capacity
                                });
                            }
                        }
                    }

                    // 2. Scan all main cable polylines to filter design FDTs vs legend FDTs
                    var cablePolylines = new List<Polyline>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Polyline poly && poly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                        {
                            cablePolylines.Add(poly);
                        }
                    }

                    // Filter out legend FDTs
                    var activeFdtBlocks = new List<FdtBlockInfo>();
                    foreach (var fdt in fdtBlocks)
                    {
                        bool nearPoleOrCable = false;

                        // 1. Check if near any cable polyline (within 2.0m)
                        foreach (var poly in cablePolylines)
                        {
                            try
                            {
                                Point3d closestPt = poly.GetClosestPointTo(fdt.Position, false);
                                if (fdt.Position.DistanceTo(closestPt) < 2.0)
                                {
                                    nearPoleOrCable = true;
                                    break;
                                }
                            }
                            catch { }
                        }

                        // 2. Check if near any pole (within 2.0m)
                        if (!nearPoleOrCable)
                        {
                            foreach (ObjectId id in modelSpace)
                            {
                                if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                                {
                                    BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                    if (br == null) continue;

                                    string blockName = br.Name;
                                    if (br.IsDynamicBlock)
                                    {
                                        try
                                        {
                                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                            blockName = btr.Name;
                                        }
                                        catch { }
                                    }

                                    if (IsPoleBlock(blockName, br.Layer, br, tr) || br.Layer.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("TIANG", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (fdt.Position.DistanceTo(br.Position) < 2.0)
                                        {
                                            nearPoleOrCable = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (nearPoleOrCable)
                        {
                            activeFdtBlocks.Add(fdt);
                        }
                    }

                    // Fallback to all FDT blocks if no active design FDTs detected (e.g. empty/test drawing)
                    if (activeFdtBlocks.Count == 0)
                    {
                        activeFdtBlocks = fdtBlocks;
                    }

                    // Sort FDTs deterministically by X and Y
                    activeFdtBlocks = activeFdtBlocks.OrderBy(b => b.Position.X).ThenBy(b => b.Position.Y).ToList();

                    if (activeFdtBlocks.Count == 0)
                    {
                        ed.WriteMessage("\n[FTTH] No active FDT blocks found in ModelSpace.");
                        tr.Commit();
                        return;
                    }

                    // Ensure target layer and text style exist
                    GetOrCreateLayer(db, tr, "FTTH-FDT-LABEL", 7);
                    ObjectId styleId = GetOrCreateTextStyle(db, tr, "MANTP");

                    // 3. Scan all existing label entities (MText/DBText) in the drawing
                    var fatLabels = new List<FatLabelInfo>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (!(obj is Entity ent)) continue;
                        string layerName = ent.Layer;

                        string tempText = "";
                        if (obj is MText tempMt) tempText = tempMt.Contents;
                        else if (obj is DBText tempDt) tempText = tempDt.TextString;

                        if (tempText.Contains("\\P"))
                        {
                            tempText = tempText.Split(new string[] { "\\P" }, StringSplitOptions.None)[0];
                        }
                        tempText = System.Text.RegularExpressions.Regex.Replace(tempText, @"\\[A-Za-z].*?;", "").Trim();

                        bool isFdtLabel = layerName.Equals("FTTH-FDT-LABEL", StringComparison.OrdinalIgnoreCase) ||
                                          layerName.Equals("FDT Label", StringComparison.OrdinalIgnoreCase) ||
                                          (layerName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 && layerName.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                          System.Text.RegularExpressions.Regex.IsMatch(tempText, @"^FDT\s*\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        if (isFdtLabel)
                        {
                            Point3d pos = Point3d.Origin;
                            if (obj is MText mt) pos = mt.Location;
                            else if (obj is DBText dt) pos = dt.Position;

                            fatLabels.Add(new FatLabelInfo
                            {
                                EntityId = id,
                                Position = pos,
                                CurrentText = tempText,
                                Layer = layerName
                            });
                        }
                    }

                    // 4. Process each active FDT block
                    int updatedCount = 0;
                    int createdCount = 0;
                    var processedLabelIds = new HashSet<ObjectId>();

                    for (int fdtIdx = 0; fdtIdx < activeFdtBlocks.Count; fdtIdx++)
                    {
                        var fdtBlock = activeFdtBlocks[fdtIdx];
                        Point3d fdtPt = fdtBlock.Position;
                        string capacity = fdtBlock.Capacity;

                        // Calculate FDT code (e.g. 051, 052...)
                        string currentFdtCode = CalculateFdtCode(baseFdtCode, fdtIdx);

                        // Sync prefix with existing FAT labels if they exist
                        string syncedPrefix = GetPrefixFromExistingFatLabels(fdtPt, db, tr, clusterCode, currentFdtCode);
                        string currentCluster = clusterCode;
                        string currentFdt = currentFdtCode;
                        string[] syncedParts = syncedPrefix.Split('.');
                        if (syncedParts.Length >= 2)
                        {
                            currentCluster = syncedParts[0];
                            currentFdt = syncedParts[1];
                        }

                        // Find closest existing labels within 20.0 meters of this FDT block
                        var nearbyLabels = fatLabels.Where(l => !processedLabelIds.Contains(l.EntityId) && l.Position.DistanceTo(fdtPt) < 20.0).ToList();

                        // Determine FDT label prefix (preserve FDT number like "FDT 1")
                        string firstLine = $"FDT {fdtIdx + 1}";
                        if (nearbyLabels.Count > 0)
                        {
                            var bestLabel = nearbyLabels.OrderBy(l => l.Position.DistanceTo(fdtPt)).First();
                            var match = System.Text.RegularExpressions.Regex.Match(bestLabel.CurrentText, @"FDT\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                firstLine = match.Value.ToUpper();
                            }
                        }

                        // Formatted content matching FDT Label.JSON
                        string newContent = $"{currentCluster}.{currentFdt}\\P\\P\\P\n{firstLine}\\P{capacity}\\P{oltCode}";

                        if (nearbyLabels.Count > 0)
                        {
                            // Update existing labels
                            foreach (var labelInfo in nearbyLabels)
                            {
                                processedLabelIds.Add(labelInfo.EntityId);
                                DBObject writeObj = tr.GetObject(labelInfo.EntityId, OpenMode.ForWrite);

                                if (writeObj is MText writeMt)
                                {
                                    writeMt.Contents = newContent;
                                    writeMt.Layer = "FTTH-FDT-LABEL";
                                    writeMt.TextStyleId = styleId;
                                    writeMt.TextHeight = 1.4;
                                    writeMt.Attachment = AttachmentPoint.MiddleCenter;
                                    writeMt.Width = 5.78;
                                    writeMt.ColorIndex = 256; // ByLayer
                                    updatedCount++;
                                }
                                else if (writeObj is DBText writeDt)
                                {
                                    writeDt.TextString = $"{currentCluster}.{currentFdt} {firstLine} {capacity} {oltCode}";
                                    writeDt.Layer = "FTTH-FDT-LABEL";
                                    writeDt.TextStyleId = styleId;
                                    writeDt.Height = 1.4;
                                    writeDt.ColorIndex = 256;
                                    updatedCount++;
                                }
                            }
                        }
                        else
                        {
                            // Create new MText label at offset position matching FDT Label.JSON basepoint
                            Point3d labelPos = new Point3d(fdtPt.X + 0.54432429264557, fdtPt.Y - 3.60388149162895, fdtPt.Z);

                            MText newMt = new MText
                            {
                                Contents = newContent,
                                Location = labelPos,
                                Layer = "FTTH-FDT-LABEL",
                                TextStyleId = styleId,
                                TextHeight = 1.4,
                                Attachment = AttachmentPoint.MiddleCenter,
                                Width = 5.78,
                                ColorIndex = 256 // ByLayer
                            };

                            modelSpace.AppendEntity(newMt);
                            tr.AddNewlyCreatedDBObject(newMt, true);
                            createdCount++;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully processed FDT labels: updated {updatedCount}, created {createdCount}.");
                    panel.Dispatcher.Invoke(() => panel.LogMessage($"FDT Labels: {updatedCount} updated, {createdCount} created."));
                }
            }

            doc.Editor.Regen();
            doc.Editor.UpdateScreen();
        }

        public static void GenerateCableLabels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var panel = PaletteManager.BasemapPanel;

            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active. Please open it using FTTH_SHOWPANEL.");
                return;
            }

            string rawFatPrefix = "";
            string oltCode = "";
            int cableScenario = 1;
            double tolerancePercent = 5.0;
            int slackFdtCount = 1;

            panel.Dispatcher.Invoke(() =>
            {
                rawFatPrefix = panel.GetLabelFatPrefix();
                oltCode = panel.GetLabelFdtSelectedOltCode();
                cableScenario = panel.GetCableScenario();
                tolerancePercent = panel.GetCableTolerance();
                slackFdtCount = panel.GetCableSlackFdt();
            });

            if (string.IsNullOrEmpty(rawFatPrefix))
            {
                rawFatPrefix = "PDA6.051";
            }

            string clusterCode = "PDA6";
            string baseFdtCode = "051";
            string[] prefixParts = rawFatPrefix.Split('.');
            if (prefixParts.Length >= 2)
            {
                clusterCode = prefixParts[0];
                baseFdtCode = prefixParts[1];
            }
            else if (prefixParts.Length == 1)
            {
                clusterCode = prefixParts[0];
                baseFdtCode = "051";
            }

            ed.WriteMessage($"\n[FTTH] Generating Cable labels... (OLT Code: {oltCode})");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // 1. Find all FDT blocks in the drawing
                    var fdtPoints = new List<Point3d>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br == null) continue;

                            string blockName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    blockName = btr.Name;
                                }
                                catch { }
                            }

                            if (blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                fdtPoints.Add(br.Position);
                            }
                        }
                    }

                    // Sort FDTs deterministically
                    fdtPoints = fdtPoints.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

                    // 2. Scan all main cable polylines
                    var cablePolylines = new List<Polyline>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Polyline poly && poly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                        {
                            cablePolylines.Add(poly);
                        }
                    }

                    if (cablePolylines.Count == 0)
                    {
                        ed.WriteMessage("\n[FTTH] No cable polylines found on layer starts with 'FTTH-CABLE-'.");
                        tr.Commit();
                        return;
                    }

                    // Filter out legend FDTs
                    var activeFdtPoints = new List<Point3d>();
                    foreach (var pt in fdtPoints)
                    {
                        bool nearPoleOrCable = false;

                        // 1. Check if near any cable polyline (within 2.0m)
                        foreach (var poly in cablePolylines)
                        {
                            try
                            {
                                Point3d closestPt = poly.GetClosestPointTo(pt, false);
                                if (pt.DistanceTo(closestPt) < 2.0)
                                {
                                    nearPoleOrCable = true;
                                    break;
                                }
                            }
                            catch { }
                        }

                        // 2. Check if near any pole (within 2.0m)
                        if (!nearPoleOrCable)
                        {
                            foreach (ObjectId id in modelSpace)
                            {
                                if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                                {
                                    BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                    if (br == null) continue;

                                    string blockName = br.Name;
                                    if (br.IsDynamicBlock)
                                    {
                                        try
                                        {
                                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                            blockName = btr.Name;
                                        }
                                        catch { }
                                    }

                                    if (IsPoleBlock(blockName, br.Layer, br, tr) || br.Layer.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("TIANG", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (pt.DistanceTo(br.Position) < 2.0)
                                        {
                                            nearPoleOrCable = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (nearPoleOrCable)
                        {
                            activeFdtPoints.Add(pt);
                        }
                    }

                    if (activeFdtPoints.Count == 0)
                    {
                        activeFdtPoints = fdtPoints;
                    }

                    // Map cables to FDTs
                    var cablesWithFdt = sortedCablesMappedToFdt(cablePolylines, activeFdtPoints);

                    // Ensure target layer and text style exist
                    GetOrCreateLayer(db, tr, "FTTH-CABLE-LABEL", 7);
                    ObjectId styleId = GetOrCreateTextStyle(db, tr, "MANTP");

                    // 3. Scan all existing MText/DBTexts in ModelSpace to gather label positions and count slack
                    var labelTexts = new List<Tuple<Point3d, string, string>>();
                    var allFatLabels = new List<string>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is DBText dt)
                        {
                            labelTexts.Add(Tuple.Create(dt.Position, dt.TextString, dt.Layer));
                            string layerName = dt.Layer;
                            if (layerName.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) ||
                                layerName.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) ||
                                (layerName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && layerName.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                allFatLabels.Add(dt.TextString);
                            }
                        }
                        else if (obj is MText mt)
                        {
                            labelTexts.Add(Tuple.Create(mt.Location, mt.Contents, mt.Layer));
                            string layerName = mt.Layer;
                            if (layerName.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) ||
                                layerName.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) ||
                                (layerName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && layerName.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                allFatLabels.Add(mt.Contents);
                            }
                        }
                    }

                    // Build fatInfoList of all FATs in drawing with their matched names and parsed numbers
                    var fatInfoList = new List<FatInfo>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br == null) continue;
                            string blockName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    blockName = btr.Name;
                                }
                                catch { }
                            }

                            if (blockName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                blockName.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                br.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                br.Layer.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string fName = "FAT_1";
                                if (br.AttributeCollection.Count > 0)
                                {
                                    foreach (ObjectId attId in br.AttributeCollection)
                                    {
                                        var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                        if (attRef != null)
                                        {
                                            if (attRef.Tag.Equals("NAMA_FAT", StringComparison.OrdinalIgnoreCase) || 
                                                attRef.Tag.Equals("CODE", StringComparison.OrdinalIgnoreCase) ||
                                                attRef.Tag.Equals("NAMA", StringComparison.OrdinalIgnoreCase))
                                            {
                                                fName = attRef.TextString;
                                            }
                                        }
                                    }
                                }

                                if (string.IsNullOrEmpty(fName) || fName == "FAT_1")
                                {
                                    var matchedLabel = labelTexts
                                        .Where(l => {
                                            double dist = l.Item1.DistanceTo(br.Position);
                                            if (dist >= 10.0) return false;
                                            string lyr = l.Item3;
                                            return lyr.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                                   lyr.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) || 
                                                   lyr.IndexOf("FAT Symbol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("SYMBOL", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                   (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);
                                        })
                                        .OrderBy(l => l.Item1.DistanceTo(br.Position))
                                        .FirstOrDefault();
                                    if (matchedLabel != null)
                                    {
                                        fName = matchedLabel.Item2;
                                    }
                                }

                                fatInfoList.Add(new FatInfo
                                {
                                    Position = br.Position,
                                    Name = fName,
                                    Number = ParseFatNumber(fName)
                                });
                            }
                        }
                    }

                    // 4. Scan all existing cable labels in ModelSpace to prevent double creation
                    var existingLabels = new List<Tuple<ObjectId, Point3d, string>>();
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (!(obj is Entity ent)) continue;

                        string layerName = ent.Layer;
                        if (layerName.Equals("FTTH-CABLE-LABEL", StringComparison.OrdinalIgnoreCase) ||
                            layerName.Equals("Cable Label", StringComparison.OrdinalIgnoreCase))
                        {
                            string text = "";
                            Point3d pos = Point3d.Origin;
                            if (obj is MText mt)
                            {
                                text = mt.Contents;
                                pos = mt.Location;
                            }
                            else if (obj is DBText dt)
                            {
                                text = dt.TextString;
                                pos = dt.Position;
                            }
                            else continue;

                            existingLabels.Add(new Tuple<ObjectId, Point3d, string>(id, pos, text));
                        }
                    }

                    // Group cables by FDT index
                    var fdtToCables = new Dictionary<int, List<Polyline>>();
                    for (int i = 0; i < activeFdtPoints.Count; i++)
                    {
                        fdtToCables[i] = new List<Polyline>();
                    }
                    foreach (var item in cablesWithFdt)
                    {
                        fdtToCables[item.FdtIndex].Add(item.Polyline);
                    }

                    int updatedCount = 0;
                    int createdCount = 0;
                    var processedLabelIds = new HashSet<ObjectId>();

                    // Map each FAT block to its single closest cable polyline to prevent double counting on overlapping routes
                    var fatBlockMapping = new Dictionary<ObjectId, List<Point3d>>();
                    foreach (var poly in cablePolylines)
                    {
                        fatBlockMapping[poly.ObjectId] = new List<Point3d>();
                    }

                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (br == null) continue;

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

                            bool isFatBlock = bName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              bName.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              br.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              br.Layer.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isFatBlock)
                            {
                                Polyline closestPoly = null;
                                double minDistance = double.MaxValue;

                                foreach (var poly in cablePolylines)
                                {
                                    try
                                    {
                                        Point3d closestPt = poly.GetClosestPointTo(br.Position, false);
                                        double dist = br.Position.DistanceTo(closestPt);
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
                                    fatBlockMapping[closestPoly.ObjectId].Add(br.Position);
                                }
                            }
                        }
                    }

                    // 4. Process each active FDT and its sorted cables
                    for (int fdtIdx = 0; fdtIdx < activeFdtPoints.Count; fdtIdx++)
                    {
                        Point3d fdtPt = activeFdtPoints[fdtIdx];

                        // Sort cables of this FDT by distance to FDT
                        var cablesForThisFdt = fdtToCables[fdtIdx]
                            .Select(poly =>
                            {
                                double dStart = poly.StartPoint.DistanceTo(fdtPt);
                                double dEnd = poly.EndPoint.DistanceTo(fdtPt);
                                return new { Polyline = poly, Dist = Math.Min(dStart, dEnd) };
                            })
                            .OrderBy(item => item.Dist)
                            .Select(item => item.Polyline)
                            .ToList();

                        for (int lineIndex = 0; lineIndex < cablesForThisFdt.Count; lineIndex++)
                        {
                            Polyline poly = cablesForThisFdt[lineIndex];

                            // Midpoint of the cable
                            double midDist = poly.Length / 2.0;
                            Point3d midPt = poly.GetPointAtDist(midDist);

                            // Sync prefix from existing FDT or FAT labels
                            string currentFdtCode = CalculateFdtCode(baseFdtCode, fdtIdx);
                            string syncedPrefix = GetPrefixFromExistingFdtLabel(fdtPt, db, tr, clusterCode, currentFdtCode);

                            if (syncedPrefix == $"{clusterCode}.{currentFdtCode}")
                            {
                                syncedPrefix = GetPrefixFromExistingFatLabels(fdtPt, db, tr, clusterCode, currentFdtCode);
                            }

                            // Calculate line letter
                            char lineLetter = (char)('A' + (lineIndex % 26));
                            string lineCode = lineLetter.ToString();
                            if (lineIndex >= 26)
                            {
                                lineCode = ((char)('A' + (lineIndex / 26 - 1))).ToString() + ((char)('A' + (lineIndex % 26))).ToString();
                            }
                            string lineName = $"CABLE LINE {lineCode}";

                            // Determine cable type: Subfeeder vs Distribution
                            string cableType = "DISTRIBUTION CABLE";
                            if (poly.Layer.IndexOf("FEEDER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                poly.Layer.IndexOf("SUBFEEDER", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                cableType = "SUBFEEDER CABLE";
                            }

                            // Parse capacity/core count automatically with layered priorities (Layer Name first)
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

                            // 4. Fallback for subfeeder: check FDT block capacity reference
                            if (!capacityFound && cableType == "SUBFEEDER CABLE" && activeFdtPoints.Count > 0)
                            {
                                Point3d nearestFdtPt = activeFdtPoints[Math.Min(fdtIdx, activeFdtPoints.Count - 1)];
                                foreach (ObjectId id in modelSpace)
                                {
                                    if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                                    {
                                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                        if (br != null && br.Position.DistanceTo(nearestFdtPt) < 1.0)
                                        {
                                            string blockName = br.Name;
                                            if (br.IsDynamicBlock)
                                            {
                                                try
                                                {
                                                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                                    blockName = btr.Name;
                                                }
                                                catch { }
                                            }

                                            if (blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
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
                                                    if (blockName.Contains("48") || br.Layer.Contains("48")) fdtCapacity = "48";
                                                    else if (blockName.Contains("72") || br.Layer.Contains("72")) fdtCapacity = "72";
                                                    else if (blockName.Contains("96") || br.Layer.Contains("96")) fdtCapacity = "96";
                                                    else if (blockName.Contains("36") || br.Layer.Contains("36")) fdtCapacity = "36";
                                                    else if (blockName.Contains("24") || br.Layer.Contains("24")) fdtCapacity = "24";
                                                }

                                                if (!string.IsNullOrEmpty(fdtCapacity))
                                                {
                                                    string numStr = System.Text.RegularExpressions.Regex.Match(fdtCapacity, @"\d+").Value;
                                                    if (int.TryParse(numStr, out int parsedCores))
                                                    {
                                                        cores = parsedCores;
                                                        capacityFound = true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            int tubes = Math.Max(1, cores / 12);
                            int length = (int)Math.Round(poly.Length);

                            string foSpec = $"FO {cores}C/{tubes}T";

                            string category = "AE";
                            if (poly.Layer.IndexOf("UG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                poly.Layer.IndexOf("UNDERGROUND", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                category = "UG";
                            }

                            // 1. Primary method: Count existing FAT labels matching current syncedPrefix and lineCode (e.g. "PDA6.051.A01")
                            int fatSlackCount = 0;
                            string escapedPrefix = System.Text.RegularExpressions.Regex.Escape(syncedPrefix);
                            string pattern = $@"\b{escapedPrefix}\.{lineCode}\d+";
                            foreach (var text in allFatLabels)
                            {
                                if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                                {
                                    fatSlackCount++;
                                }
                            }

                            // 2. Fallback method: If no FAT labels matched, use spatial FAT blocks mapping
                            if (fatSlackCount == 0 && fatBlockMapping.ContainsKey(poly.ObjectId))
                            {
                                fatSlackCount = fatBlockMapping[poly.ObjectId].Count;
                            }

                            // Interactive OTDR length prompt per line if Scenario 2 is active
                            int currentOtdrValue = 0;
                            if (cableScenario == 2)
                            {
                                try
                                {
                                    poly.Highlight();
                                    db.TransactionManager.QueueForGraphicsFlush();
                                    ed.UpdateScreen();

                                    PromptIntegerOptions otdrOpts = new PromptIntegerOptions($"\n[FTTH] Masukkan nilai OTDR untuk {syncedPrefix} - {lineName} (Panjang Rute: {length}M) [Tekan Enter untuk skip]: ");
                                    otdrOpts.AllowNone = true;
                                    otdrOpts.DefaultValue = 0;
                                    otdrOpts.UseDefaultValue = true;

                                    PromptIntegerResult otdrRes = ed.GetInteger(otdrOpts);
                                    if (otdrRes.Status == PromptStatus.OK)
                                    {
                                        currentOtdrValue = otdrRes.Value;
                                    }
                                }
                                catch { }
                                finally
                                {
                                    try { poly.Unhighlight(); } catch { }
                                }
                            }

                            // Compile contents
                            string newContent = "";
                            if (cableScenario == 2)
                            {
                                // Calculate routeDistance segment-by-segment if there are FATs on this line
                                string linePrefix = $"{syncedPrefix}.{lineCode}";
                                var lineFats = fatInfoList
                                    .Where(f => !string.IsNullOrEmpty(f.Name) && 
                                                f.Name.Trim().ToUpper().StartsWith(linePrefix.ToUpper()))
                                    .OrderBy(f => f.Number)
                                    .ToList();

                                double routeDistance = length; // fallback to polyline length
                                if (lineFats.Count > 0)
                                {
                                    try
                                    {
                                        double pathDist = GetPathLength(FindCablePath(db, fdtPt, lineFats[0].Position));
                                        for (int i = 1; i < lineFats.Count; i++)
                                        {
                                            pathDist += GetPathLength(FindCablePath(db, lineFats[i - 1].Position, lineFats[i].Position));
                                        }
                                        if (pathDist > 0.1)
                                        {
                                            routeDistance = pathDist;
                                        }
                                    }
                                    catch { }
                                }

                                int totalRoute = (int)Math.Round(routeDistance);
                                int totalSlackCount = slackFdtCount + fatSlackCount;
                                int slackLength = totalSlackCount * 20;
                                int routePlusSlack = totalRoute + slackLength;
                                int toleranceVal = (int)Math.Round(routePlusSlack * (tolerancePercent / 100.0));
                                int calculatedLength = routePlusSlack + toleranceVal;

                                newContent = $"Total Route = {totalRoute} M\\P" +
                                             $"Total Slack = {totalSlackCount} unit ({slackFdtCount} slack FDT & {fatSlackCount} slack FAT/SF400) @20M\\P" +
                                             $"Toleransi = {tolerancePercent}%\\P" +
                                             $"Total Length Cable = {totalRoute}+{slackLength} : {routePlusSlack}M + ({routePlusSlack} x {tolerancePercent}%) = {toleranceVal}M\\P" +
                                             $"{syncedPrefix} - {lineName} ({foSpec}) - {category} - {calculatedLength} M";

                                if (currentOtdrValue > 0)
                                {
                                    newContent += $"\\Pby (OTDR:{currentOtdrValue} M)\\P" +
                                                  $"{syncedPrefix} - {lineName} ({foSpec}) - {category} - {currentOtdrValue} M";
                                }
                            }
                            else
                            {
                                // Scenario 1: Standard Format
                                newContent = $"{oltCode} - {syncedPrefix} - {cableType}\\P({foSpec}) - {category}-{length} m";
                            }

                            // Find closest existing label within 10.0 meters of midpoint
                            var nearbyLabels = existingLabels.Where(l => !processedLabelIds.Contains(l.Item1) && l.Item2.DistanceTo(midPt) < 10.0).ToList();

                            if (nearbyLabels.Count > 0)
                            {
                                var bestLabel = nearbyLabels.OrderBy(l => l.Item2.DistanceTo(midPt)).First();
                                processedLabelIds.Add(bestLabel.Item1);

                                DBObject writeObj = tr.GetObject(bestLabel.Item1, OpenMode.ForWrite);
                                if (writeObj is MText writeMt)
                                {
                                    writeMt.Contents = newContent;
                                    writeMt.Layer = "FTTH-CABLE-LABEL";
                                    writeMt.TextStyleId = styleId;
                                    writeMt.TextHeight = 1.4;
                                    writeMt.Attachment = AttachmentPoint.MiddleCenter;
                                    writeMt.ColorIndex = 256; // ByLayer
                                    updatedCount++;
                                }
                                else if (writeObj is DBText writeDt)
                                {
                                    writeDt.TextString = newContent.Replace("\\P", " ").Replace("\n", " ");
                                    writeDt.Layer = "FTTH-CABLE-LABEL";
                                    writeDt.TextStyleId = styleId;
                                    writeDt.Height = 1.4;
                                    writeDt.ColorIndex = 256;
                                    updatedCount++;
                                }
                            }
                            else
                            {
                                // Create new MText label at midpoint
                                Point3d labelPos = new Point3d(midPt.X, midPt.Y + 2.0, midPt.Z);

                                MText newMt = new MText
                                {
                                    Contents = newContent,
                                    Location = labelPos,
                                    Layer = "FTTH-CABLE-LABEL",
                                    TextStyleId = styleId,
                                    TextHeight = 1.4,
                                    Attachment = AttachmentPoint.MiddleCenter,
                                    ColorIndex = 256 // ByLayer
                                };

                                modelSpace.AppendEntity(newMt);
                                tr.AddNewlyCreatedDBObject(newMt, true);
                                createdCount++;
                            }
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully processed cable labels: updated {updatedCount}, created {createdCount}.");
                    panel.Dispatcher.Invoke(() => panel.LogMessage($"Cable Labels: {updatedCount} updated, {createdCount} created."));
                }
            }

            doc.Editor.Regen();
            doc.Editor.UpdateScreen();
        }

        private static ObjectId GetOrCreateTextStyle(Database db, Transaction tr, string styleName)
        {
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(styleName))
            {
                return tst[styleName];
            }

            tst.UpgradeOpen();
            TextStyleTableRecord tstr = new TextStyleTableRecord();
            tstr.Name = styleName;
            tstr.FileName = "ROMANS";
            tstr.XScale = 1.0;
            tstr.PriorSize = 1.4;
            ObjectId styleId = tst.Add(tstr);
            tr.AddNewlyCreatedDBObject(tstr, true);
            return styleId;
        }

        private static string GetPrefixFromExistingFdtLabel(Point3d fdtPt, Database db, Transaction tr, string defaultClusterCode, string defaultFdtCode)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            double minDist = double.MaxValue;
            string bestPrefix = null;

            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased) continue;
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (!(obj is Entity ent)) continue;

                string layerName = ent.Layer;
                string tempText = "";
                Point3d pos = Point3d.Origin;

                if (obj is MText mt)
                {
                    tempText = mt.Contents;
                    pos = mt.Location;
                }
                else if (obj is DBText dt)
                {
                    tempText = dt.TextString;
                    pos = dt.Position;
                }
                else continue;

                bool isFdtLabel = layerName.Equals("FTTH-FDT-LABEL", StringComparison.OrdinalIgnoreCase) ||
                                  layerName.Equals("FDT Label", StringComparison.OrdinalIgnoreCase) ||
                                  (layerName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 && layerName.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isFdtLabel)
                {
                    double dist = pos.DistanceTo(fdtPt);
                    if (dist < 10.0 && dist < minDist) // Search within 10.0m, find closest
                    {
                        string cleanText = tempText;
                        if (cleanText.Contains("\\P"))
                        {
                            cleanText = cleanText.Split(new string[] { "\\P" }, StringSplitOptions.None)[0];
                        }
                        cleanText = System.Text.RegularExpressions.Regex.Replace(cleanText, @"\\[A-Za-z].*?;", "").Trim();

                        var match = System.Text.RegularExpressions.Regex.Match(cleanText, @"^([A-Za-z0-9]+)\.([A-Za-z0-9]+)");
                        if (match.Success)
                        {
                            minDist = dist;
                            bestPrefix = match.Value;
                        }
                    }
                }
            }

            if (bestPrefix != null)
            {
                return bestPrefix;
            }
            return $"{defaultClusterCode}.{defaultFdtCode}";
        }

        private static string GetPrefixFromExistingFatLabels(Point3d fdtPt, Database db, Transaction tr, string defaultClusterCode, string defaultFdtCode)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            var nearbyFatPrefixes = new List<Tuple<string, double>>();

            foreach (ObjectId id in modelSpace)
            {
                if (id.IsErased) continue;
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (!(obj is Entity ent)) continue;

                string layerName = ent.Layer;
                string tempText = "";
                Point3d pos = Point3d.Origin;

                if (obj is MText mt)
                {
                    tempText = mt.Contents;
                    pos = mt.Location;
                }
                else if (obj is DBText dt)
                {
                    tempText = dt.TextString;
                    pos = dt.Position;
                }
                else continue;

                bool isFatLabel = layerName.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) ||
                                  layerName.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) ||
                                  (layerName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && layerName.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isFatLabel)
                {
                    double dist = pos.DistanceTo(fdtPt);
                    if (dist < 20.0) // Search within 20.0m instead of 50.0m
                    {
                        string cleanText = tempText;
                        if (cleanText.Contains("\\P"))
                        {
                            cleanText = cleanText.Split(new string[] { "\\P" }, StringSplitOptions.None)[0];
                        }
                        cleanText = System.Text.RegularExpressions.Regex.Replace(cleanText, @"\\[A-Za-z].*?;", "").Trim();

                        var parts = cleanText.Split('.');
                        if (parts.Length >= 2)
                        {
                            nearbyFatPrefixes.Add(new Tuple<string, double>($"{parts[0]}.{parts[1]}", dist));
                        }
                    }
                }
            }

            if (nearbyFatPrefixes.Count > 0)
            {
                // Return the prefix of the CLOSEST FAT label found near this FDT block
                return nearbyFatPrefixes.OrderBy(p => p.Item2).First().Item1;
            }

            return $"{defaultClusterCode}.{defaultFdtCode}";
        }

        public static void GenerateSpanLabels()
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

            double textHeight = 1.0;
            panel.Dispatcher.Invoke(() =>
            {
                textHeight = panel.GetSpanTextHeight();
            });

            ed.WriteMessage($"\n[FTTH] Generating span labels along cables and wire lines... (Text Height: {textHeight})");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Ensure target layer and text style exist
                    string targetLayer = "FTTH-SPAN-LABEL";
                    GetOrCreateLayer(db, tr, targetLayer, 2); // Yellow color
                    ObjectId styleId = GetOrCreateTextStyle(db, tr, "MANTP");

                    // 1. Scan and delete existing span labels in this layer to prevent duplicates
                    int deletedCount = 0;
                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Entity ent && ent.Layer.Equals(targetLayer, StringComparison.OrdinalIgnoreCase))
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                            deletedCount++;
                        }
                    }

                    // 2. Scan all cable polylines and wire polylines
                    int labelCount = 0;
                    var placedMidpoints = new List<Point3d>();

                    foreach (ObjectId id in modelSpace)
                    {
                        if (id.IsErased) continue;
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Polyline poly)
                        {
                            string layerName = poly.Layer;
                            bool isTargetPoly = layerName.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase) ||
                                               layerName.IndexOf("WIRE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               layerName.IndexOf("SLING", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isTargetPoly)
                            {
                                int vertexCount = poly.NumberOfVertices;
                                for (int i = 0; i < vertexCount - 1; i++)
                                {
                                    Point3d pt1 = poly.GetPoint3dAt(i);
                                    Point3d pt2 = poly.GetPoint3dAt(i + 1);

                                    double segLen = pt1.DistanceTo(pt2);
                                    if (segLen < 0.5) continue; // Skip extremely short segments

                                    Point3d midPt = new Point3d((pt1.X + pt2.X) / 2.0, (pt1.Y + pt2.Y) / 2.0, (pt1.Z + pt2.Z) / 2.0);

                                    // Deduplicate labels: Skip if this span's midpoint is close to an already labeled span's midpoint
                                    bool alreadyLabeled = false;
                                    foreach (var placedPt in placedMidpoints)
                                    {
                                        if (midPt.DistanceTo(placedPt) < 1.5)
                                        {
                                            alreadyLabeled = true;
                                            break;
                                        }
                                    }

                                    if (alreadyLabeled) continue;

                                    placedMidpoints.Add(midPt);

                                    // Calculate angle and rotation
                                    double dx = pt2.X - pt1.X;
                                    double dy = pt2.Y - pt1.Y;
                                    double angle = Math.Atan2(dy, dx);

                                    // Adjust rotation to keep text right-side up
                                    if (angle > Math.PI / 2.0) angle -= Math.PI;
                                    else if (angle < -Math.PI / 2.0) angle += Math.PI;

                                    // Calculate perpendicular offset unit vector to place text above the line
                                    double ux = -dy / segLen;
                                    double uy = dx / segLen;

                                    // Place text offset slightly (e.g. 0.6 units perpendicular)
                                    Point3d labelPos = new Point3d(midPt.X + ux * 0.6, midPt.Y + uy * 0.6, midPt.Z);

                                    // Create text string
                                    string textStr = $"{Math.Round(segLen)} M";

                                    MText mt = new MText
                                    {
                                        Contents = textStr,
                                        Location = labelPos,
                                        Layer = targetLayer,
                                        TextStyleId = styleId,
                                        TextHeight = textHeight,
                                        Rotation = angle,
                                        Attachment = AttachmentPoint.MiddleCenter,
                                        ColorIndex = 256 // ByLayer
                                    };

                                    modelSpace.AppendEntity(mt);
                                    tr.AddNewlyCreatedDBObject(mt, true);
                                    labelCount++;
                                }
                            }
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully generated {labelCount} span labels (cleared {deletedCount} old labels).");
                    panel.Dispatcher.Invoke(() => panel.LogMessage($"Span Labels: {labelCount} generated."));
                }
            }

            doc.Editor.Regen();
            doc.Editor.UpdateScreen();
        }

        private class FdtBlockInfo
        {
            public Point3d Position { get; set; }
            public string Capacity { get; set; }
        }

        private static string GetTargetPoleLabelLayer(string blockName, string layerName, bool isExisting)
        {
            if (!isExisting)
            {
                return layerName + "-LABEL";
            }

            string upperBlock = blockName.ToUpper();
            string upperLayer = layerName.ToUpper();

            bool isPartner = upperBlock.Contains("TEL") || upperLayer.Contains("TEL") ||
                             upperBlock.Contains("MTI") || upperLayer.Contains("MTI") ||
                             upperBlock.Contains("LINK") || upperLayer.Contains("LINK") ||
                             upperBlock.Contains("PLN") || upperLayer.Contains("PLN") ||
                             upperBlock.Contains("PARTNER") || upperLayer.Contains("PARTNER");

            string sizeStr = "74"; // default
            if (upperBlock.Contains("94") || upperBlock.Contains("9-4") || upperBlock.Contains("9M") || upperBlock.Contains("NP94") ||
                upperLayer.Contains("94") || upperLayer.Contains("9-4") || upperLayer.Contains("9M") || upperLayer.Contains("NP94") || upperLayer.Contains("EP94"))
            {
                sizeStr = "94";
            }
            else if (upperBlock.Contains("74") || upperBlock.Contains("7-4") || upperBlock.Contains("7M 4") || upperBlock.Contains("7M-4") || upperBlock.Contains("NP74") ||
                     upperLayer.Contains("74") || upperLayer.Contains("7-4") || upperLayer.Contains("7M 4") || upperLayer.Contains("7M-4") || upperLayer.Contains("NP74") || upperLayer.Contains("EP74"))
            {
                sizeStr = "74";
            }
            else if (upperBlock.Contains("73") || upperBlock.Contains("7-3") || upperBlock.Contains("7M 3") || upperBlock.Contains("7M-3") || upperBlock.Contains("NP73") ||
                     upperLayer.Contains("73") || upperLayer.Contains("7-3") || upperLayer.Contains("7M 3") || upperLayer.Contains("7M-3") || upperLayer.Contains("NP73") || upperLayer.Contains("EP73"))
            {
                sizeStr = "73";
            }
            else if (upperBlock.Contains("725") || upperBlock.Contains("7-2.5") || upperBlock.Contains("7M 2.5") || upperBlock.Contains("7M-2.5") || upperBlock.Contains("2.5") || upperBlock.Contains("NP725") ||
                     upperLayer.Contains("725") || upperLayer.Contains("7-2.5") || upperLayer.Contains("7M 2.5") || upperLayer.Contains("7M-2.5") || upperLayer.Contains("2.5") || upperLayer.Contains("NP725") || upperLayer.Contains("EP725"))
            {
                sizeStr = "725";
            }

            if (isPartner)
            {
                return $"FTTH-POLE-PARTNER-{sizeStr}-LABEL";
            }
            else
            {
                return $"FTTH-POLE-EP{sizeStr}-LABEL";
            }
        }

        private class GraphNode
        {
            public Point3d Point { get; set; }
            public List<GraphEdge> Edges { get; } = new List<GraphEdge>();
        }

        private class GraphEdge
        {
            public GraphNode Target { get; set; }
            public double Weight { get; set; }
        }

        private class FatInfo
        {
            public Point3d Position { get; set; }
            public string Name { get; set; }
            public int Number { get; set; }
        }

        private static int ParseFatNumber(string name)
        {
            if (string.IsNullOrEmpty(name)) return 999;
            var matches = System.Text.RegularExpressions.Regex.Matches(name, @"\d+");
            if (matches.Count > 0)
            {
                string lastNumStr = matches[matches.Count - 1].Value;
                if (int.TryParse(lastNumStr, out int val))
                {
                    return val;
                }
            }
            return 999;
        }

        private static string GetFatPrefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int lastDigitIdx = -1;
            for (int i = name.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(name[i]))
                {
                    lastDigitIdx = i;
                }
                else if (lastDigitIdx != -1)
                {
                    return name.Substring(0, i + 1);
                }
            }
            return name;
        }

        private static List<Point3d> FindCablePath(Database db, Point3d startPt, Point3d endPt)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var polylines = new List<Polyline>();
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is Polyline poly && poly.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                    {
                        polylines.Add(poly);
                    }
                }

                if (polylines.Count == 0)
                {
                    return new List<Point3d> { startPt, endPt };
                }

                // Collect all vertices
                var nodeList = new List<GraphNode>();
                GraphNode GetOrCreateNode(Point3d pt)
                {
                    foreach (var n in nodeList)
                    {
                        if (n.Point.DistanceTo(pt) < 1.0) // Merge within 1 meter
                        {
                            return n;
                        }
                    }
                    var node = new GraphNode { Point = pt };
                    nodeList.Add(node);
                    return node;
                }

                // Add edges along polylines
                foreach (var poly in polylines)
                {
                    int n = poly.NumberOfVertices;
                    if (n < 2) continue;

                    var prevNode = GetOrCreateNode(poly.GetPoint3dAt(0));
                    for (int i = 1; i < n; i++)
                    {
                        var currPt = poly.GetPoint3dAt(i);
                        var currNode = GetOrCreateNode(currPt);

                        double dist = 0.0;
                        try
                        {
                            dist = poly.GetDistanceAtParameter(i) - poly.GetDistanceAtParameter(i - 1);
                        }
                        catch {}
                        if (dist < 0.01) dist = prevNode.Point.DistanceTo(currNode.Point);

                        prevNode.Edges.Add(new GraphEdge { Target = currNode, Weight = dist });
                        currNode.Edges.Add(new GraphEdge { Target = prevNode, Weight = dist });

                        prevNode = currNode;
                    }
                }

                // Project start and end points onto the closest polylines
                Point3d projectedStart = startPt;
                Point3d projectedEnd = endPt;
                Polyline startPoly = null;
                Polyline endPoly = null;
                double minStartDist = double.MaxValue;
                double minEndDist = double.MaxValue;

                foreach (var poly in polylines)
                {
                    try
                    {
                        Point3d clsStart = poly.GetClosestPointTo(startPt, false);
                        double dS = startPt.DistanceTo(clsStart);
                        if (dS < minStartDist)
                        {
                            minStartDist = dS;
                            projectedStart = clsStart;
                            startPoly = poly;
                        }

                        Point3d clsEnd = poly.GetClosestPointTo(endPt, false);
                        double dE = endPt.DistanceTo(clsEnd);
                        if (dE < minEndDist)
                        {
                            minEndDist = dE;
                            projectedEnd = clsEnd;
                            endPoly = poly;
                        }
                    }
                    catch {}
                }

                // Add start and end projection nodes to the graph
                var startNode = GetOrCreateNode(projectedStart);
                var endNode = GetOrCreateNode(projectedEnd);

                // Helper to connect a projection point to the segment vertices of a polyline
                void ConnectProjectionToSegment(GraphNode projNode, Point3d originalProj, Polyline poly)
                {
                    if (poly == null) return;
                    try
                    {
                        double param = poly.GetParameterAtPoint(originalProj);
                        int idx1 = (int)Math.Floor(param);
                        int idx2 = (int)Math.Ceiling(param);

                        if (idx1 >= 0 && idx1 < poly.NumberOfVertices)
                        {
                            var v1Node = GetOrCreateNode(poly.GetPoint3dAt(idx1));
                            double dist1 = Math.Abs(poly.GetDistanceAtParameter(param) - poly.GetDistanceAtParameter(idx1));
                            projNode.Edges.Add(new GraphEdge { Target = v1Node, Weight = dist1 });
                            v1Node.Edges.Add(new GraphEdge { Target = projNode, Weight = dist1 });
                        }
                        if (idx2 >= 0 && idx2 < poly.NumberOfVertices && idx2 != idx1)
                        {
                            var v2Node = GetOrCreateNode(poly.GetPoint3dAt(idx2));
                            double dist2 = Math.Abs(poly.GetDistanceAtParameter(idx2) - poly.GetDistanceAtParameter(param));
                            projNode.Edges.Add(new GraphEdge { Target = v2Node, Weight = dist2 });
                            v2Node.Edges.Add(new GraphEdge { Target = projNode, Weight = dist2 });
                        }
                    }
                    catch {}
                }

                ConnectProjectionToSegment(startNode, projectedStart, startPoly);
                ConnectProjectionToSegment(endNode, projectedEnd, endPoly);

                // Dijkstra shortest path
                var distances = new Dictionary<GraphNode, double>();
                var previous = new Dictionary<GraphNode, GraphNode>();
                var visited = new HashSet<GraphNode>();
                foreach (var node in nodeList)
                {
                    distances[node] = double.MaxValue;
                    previous[node] = null;
                }
                distances[startNode] = 0.0;

                while (true)
                {
                    GraphNode curr = null;
                    double minD = double.MaxValue;
                    foreach (var pair in distances)
                    {
                        if (!visited.Contains(pair.Key) && pair.Value < minD)
                        {
                            minD = pair.Value;
                            curr = pair.Key;
                        }
                    }

                    if (curr == null || curr == endNode) break;

                    visited.Add(curr);

                    foreach (var edge in curr.Edges)
                    {
                        double newD = distances[curr] + edge.Weight;
                        if (newD < distances[edge.Target])
                        {
                            distances[edge.Target] = newD;
                            previous[edge.Target] = curr;
                        }
                    }
                }

                var pathPoints = new List<Point3d>();
                if (distances[endNode] != double.MaxValue)
                {
                    var curr = endNode;
                    while (curr != null)
                    {
                        pathPoints.Add(curr.Point);
                        curr = previous[curr];
                    }
                    pathPoints.Reverse();
                }
                else
                {
                    pathPoints.Add(startPt);
                    pathPoints.Add(endPt);
                }

                tr.Commit();
                return pathPoints;
            }
        }

        private static double GetPathLength(List<Point3d> path)
        {
            double len = 0.0;
            for (int i = 1; i < path.Count; i++)
            {
                len += path[i].DistanceTo(path[i - 1]);
            }
            return len;
        }

        private static double GetDistanceToSegment(Point3d p, Point3d a, Point3d b)
        {
            Vector3d ab = b - a;
            Vector3d ap = p - a;

            double abLen2 = ab.LengthSqrd;
            if (abLen2 < 1e-6) return p.DistanceTo(a);

            double t = (ap.X * ab.X + ap.Y * ab.Y + ap.Z * ab.Z) / abLen2;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;

            Point3d projection = a + t * ab;
            return p.DistanceTo(projection);
        }

        private static bool IsPointNearPath(Point3d pt, List<Point3d> path, double maxDistance)
        {
            if (path == null || path.Count == 0) return false;
            for (int i = 1; i < path.Count; i++)
            {
                double d = GetDistanceToSegment(pt, path[i - 1], path[i]);
                if (d < maxDistance) return true;
            }
            return false;
        }

        private static double GetDistanceAlongPath(Point3d pt, List<Point3d> path)
        {
            if (path == null || path.Count < 2) return 0.0;

            double minDist = double.MaxValue;
            double distAlongPath = 0.0;
            double bestDistAlongPath = 0.0;

            for (int i = 1; i < path.Count; i++)
            {
                Point3d a = path[i - 1];
                Point3d b = path[i];

                Vector3d ab = b - a;
                Vector3d ap = pt - a;

                double abLen2 = ab.LengthSqrd;
                double t = 0.0;
                if (abLen2 > 1e-6)
                {
                    t = (ap.X * ab.X + ap.Y * ab.Y + ap.Z * ab.Z) / abLen2;
                    if (t < 0.0) t = 0.0;
                    if (t > 1.0) t = 1.0;
                }

                Point3d projection = a + t * ab;
                double d = pt.DistanceTo(projection);

                if (d < minDist)
                {
                    minDist = d;
                    bestDistAlongPath = distAlongPath + t * ab.Length;
                }

                distAlongPath += ab.Length;
            }

            return bestDistAlongPath;
        }

        private static Point3d GetIntersectionWithBox(Point3d start, Point3d end, Extents3d box)
        {
            double minX = box.MinPoint.X;
            double maxX = box.MaxPoint.X;
            double minY = box.MinPoint.Y;
            double maxY = box.MaxPoint.Y;

            var segments = new[]
            {
                new { P1 = new Point2d(minX, minY), P2 = new Point2d(maxX, minY) }, // Bottom
                new { P1 = new Point2d(maxX, minY), P2 = new Point2d(maxX, maxY) }, // Right
                new { P1 = new Point2d(maxX, maxY), P2 = new Point2d(minX, maxY) }, // Top
                new { P1 = new Point2d(minX, maxY), P2 = new Point2d(minX, minY) }  // Left
            };

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;

            Point3d bestIntersection = end;
            double minT = 1.0;

            foreach (var seg in segments)
            {
                double x1 = start.X, y1 = start.Y;
                double x2 = end.X, y2 = end.Y;
                double x3 = seg.P1.X, y3 = seg.P1.Y;
                double x4 = seg.P2.X, y4 = seg.P2.Y;

                double denominator = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);
                if (Math.Abs(denominator) < 1e-6) continue;

                double ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denominator;
                double ub = ((x2 - x1) * (y1 - y3) - (y2 - y1) * (x1 - x3)) / denominator;

                if (ua >= 0.0 && ua <= 1.0 && ub >= 0.0 && ub <= 1.0)
                {
                    if (ua < minT)
                    {
                        minT = ua;
                        bestIntersection = new Point3d(
                            start.X + ua * (x2 - x1),
                            start.Y + ua * (y2 - y1),
                            start.Z
                        );
                    }
                }
            }

            return bestIntersection;
        }

        private static double CalculateAttenuation(double distanceMeter, double oltPower, double fiberAtt, double fdtSplitter, double fatSplitter, double spliceLoss, double connLoss)
        {
            double distanceKm = distanceMeter / 1000.0;
            double fiberLoss = fiberAtt * distanceKm;
            double totalLoss = fdtSplitter + fatSplitter + fiberLoss + spliceLoss + connLoss;
            double receivedPower = oltPower - totalLoss;
            return receivedPower;
        }

        public static void GenerateFatTables()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active. Please open it using FTTH_SHOWPANEL.");
                return;
            }

            string labelJsonFilename = "FAT Tabel Info.json";
            string labelJsonPath = panel.Dispatcher.Invoke(() => panel.GetAssetPath(labelJsonFilename));
            if (!File.Exists(labelJsonPath))
            {
                ed.WriteMessage($"\n[FTTH] Error: FAT Tabel Info template JSON not found at: {labelJsonPath}");
                return;
            }

            // Retrieve parameter settings from UI
            double oltPower = 3.0;
            double fiberAtt = 0.35;
            double fdtSplitter = 7.25;
            double fatSplitter = 10.38;
            double spliceLoss = 0.15;
            double connLoss = 0.50;
            double fatSlack = 20.0;
            double fdtSlack = 20.0;

            panel.Dispatcher.Invoke(() =>
            {
                oltPower = panel.GetFatTableOltPower();
                fiberAtt = panel.GetFatTableFiberAtt();
                fdtSplitter = panel.GetFatTableFdtSplitter();
                fatSplitter = panel.GetFatTableFatSplitter();
                spliceLoss = panel.GetFatTableSpliceLoss();
                connLoss = panel.GetFatTableConnLoss();
                fatSlack = panel.GetFatTableFatSlack();
                fdtSlack = panel.GetFatTableFdtSlack();
            });

            // Step 1: Scan ModelSpace for all FDT blocks to build a list of FDT positions, and all FAT blocks
            var fdtPoints = new List<Point3d>();
            var fatPoints = new List<Point3d>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference))))
                    {
                        var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;
                        string blockName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            try
                            {
                                var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                blockName = btr.Name;
                            }
                            catch {}
                        }

                        if (blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            fdtPoints.Add(br.Position);
                        }
                        else if (blockName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                 blockName.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 br.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 br.Layer.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            fatPoints.Add(br.Position);
                        }
                    }
                }
                tr.Commit();
            }

            if (fdtPoints.Count == 0)
            {
                ed.WriteMessage("\n[FTTH] Error: No FDT block found in the drawing. Please place an FDT block first.");
                return;
            }

            // Step 2: Extract all Text/MText on layer FTTH-FAT-LABEL and block attributes to match FAT labels
            var labelTexts = new List<Tuple<Point3d, string, string>>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is DBText dt)
                    {
                        labelTexts.Add(Tuple.Create(dt.Position, dt.TextString, dt.Layer));
                    }
                    else if (obj is MText mt)
                    {
                        labelTexts.Add(Tuple.Create(mt.Location, mt.Text, mt.Layer));
                    }
                }
                tr.Commit();
            }

            // Step 2b: Build list of all FATs in drawing with their matched names and parsed numbers
            var fatInfoList = new List<FatInfo>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference))))
                    {
                        var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;
                        string blockName = br.Name;
                        if (br.IsDynamicBlock)
                        {
                            try
                            {
                                var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                blockName = btr.Name;
                            }
                            catch {}
                        }

                        if (blockName.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 || 
                            blockName.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            br.Layer.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            br.Layer.IndexOf("ODP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string fName = "FAT_1";
                            // Match attributes
                            if (br.AttributeCollection.Count > 0)
                            {
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    if (attRef != null)
                                    {
                                        if (attRef.Tag.Equals("NAMA_FAT", StringComparison.OrdinalIgnoreCase) || 
                                            attRef.Tag.Equals("CODE", StringComparison.OrdinalIgnoreCase) ||
                                            attRef.Tag.Equals("NAMA", StringComparison.OrdinalIgnoreCase))
                                        {
                                            fName = attRef.TextString;
                                        }
                                    }
                                }
                            }

                            // If not found in attributes, check nearby label texts
                            if (string.IsNullOrEmpty(fName) || fName == "FAT_1")
                            {
                                var matchedLabel = labelTexts
                                    .Where(l => {
                                        double dist = l.Item1.DistanceTo(br.Position);
                                        if (dist >= 10.0) return false;
                                        string lyr = l.Item3;
                                        return lyr.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                               lyr.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) || 
                                               lyr.IndexOf("FAT Symbol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                               (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("SYMBOL", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                               (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);
                                    })
                                    .OrderBy(l => l.Item1.DistanceTo(br.Position))
                                    .FirstOrDefault();
                                if (matchedLabel != null)
                                {
                                    fName = matchedLabel.Item2;
                                }
                            }

                            fatInfoList.Add(new FatInfo
                            {
                                Position = br.Position,
                                Name = fName,
                                Number = ParseFatNumber(fName)
                            });
                        }
                    }
                }
                tr.Commit();
            }

            // Check if there are existing tables with FTTH_FAT_TABLE_ID to allow refresh
            bool hasExistingTables = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.XData != null)
                    {
                        var tvs = ent.XData.AsArray();
                        if (tvs.Any(tv => tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName && tv.Value?.ToString() == "FTTH_FAT_TABLE_ID"))
                        {
                            hasExistingTables = true;
                            break;
                        }
                    }
                }
                tr.Commit();
            }

            if (hasExistingTables)
            {
                var pKeyOpts = new PromptKeywordOptions("\nDitemukan FAT Tabel yang sudah ada di gambar. Pilih tindakan [Refresh/Create] <Refresh>: ");
                pKeyOpts.Keywords.Add("Refresh");
                pKeyOpts.Keywords.Add("Create");
                pKeyOpts.Keywords.Default = "Refresh";

                var pKeyRes = ed.GetKeywords(pKeyOpts);
                if (pKeyRes.Status == PromptStatus.OK)
                {
                    if (pKeyRes.StringResult == "Refresh")
                    {
                        RegenerateAllFatTables(db, ed, oltPower, fiberAtt, fdtSplitter, fatSplitter, spliceLoss, connLoss, fatSlack, fdtSlack, fdtPoints, fatPoints, labelTexts, fatInfoList);
                        return;
                    }
                }
                else if (pKeyRes.Status == PromptStatus.Cancel)
                {
                    return;
                }
            }

            // Step 3: Interactive Selection Loop
            while (true)
            {
                var peo = new PromptEntityOptions("\nSelect FAT block in AutoCAD (or press Enter/Esc to exit): ");
                peo.SetRejectMessage("\nMust be a block reference representing a FAT.");
                peo.AddAllowedClass(typeof(BlockReference), true);

                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    break; // User cancelled or pressed Enter
                }

                Point3d fatPos;
                string fatName = "FAT_1";
                string blockName = "";
                string layerName = "";

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (br == null)
                    {
                        ed.WriteMessage("\nInvalid selection.");
                        tr.Commit();
                        continue;
                    }

                    fatPos = br.Position;
                    blockName = br.Name;
                    if (br.IsDynamicBlock)
                    {
                        try
                        {
                            var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                            blockName = btr.Name;
                        }
                        catch {}
                    }
                    layerName = br.Layer;

                    // Match attributes
                    if (br.AttributeCollection.Count > 0)
                    {
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (attRef != null)
                            {
                                if (attRef.Tag.Equals("NAMA_FAT", StringComparison.OrdinalIgnoreCase) || 
                                    attRef.Tag.Equals("CODE", StringComparison.OrdinalIgnoreCase) ||
                                    attRef.Tag.Equals("NAMA", StringComparison.OrdinalIgnoreCase))
                                {
                                    fatName = attRef.TextString;
                                }
                            }
                        }
                    }
                    tr.Commit();
                }

                // If not found in attributes, look for nearby FAT label text within 10 meters (including FAT Symbol, FTTH-FAT-LABEL, etc.)
                if (string.IsNullOrEmpty(fatName) || fatName == "FAT_1")
                {
                    var matchedLabelTuple = labelTexts
                        .Where(l => {
                            double dist = l.Item1.DistanceTo(fatPos);
                            if (dist >= 10.0) return false;
                            string lyr = l.Item3;
                            bool isFatLabelLayer = lyr.Equals("FTTH-FAT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                                   lyr.Equals("FAT Label", StringComparison.OrdinalIgnoreCase) || 
                                                   lyr.IndexOf("FAT Symbol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("SYMBOL", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                   (lyr.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 && lyr.IndexOf("LABEL", StringComparison.OrdinalIgnoreCase) >= 0);
                            return isFatLabelLayer;
                        })
                        .OrderBy(l => l.Item1.DistanceTo(fatPos))
                        .FirstOrDefault();
                    if (matchedLabelTuple != null)
                    {
                        fatName = matchedLabelTuple.Item2;
                    }
                }

                // Find nearest FDT block
                var nearestFdt = fdtPoints.OrderBy(p => p.DistanceTo(fatPos)).First();

                // Find path along polylines from FDT to selected FAT
                var pathPoints = FindCablePath(db, nearestFdt, fatPos);
                double cableDistance = GetPathLength(pathPoints);

                // Count FATs along the path (using a 5.0-meter proximity threshold to match cable mapping)
                int numFatsOnPath = 0;
                foreach (var fPos in fatPoints)
                {
                    if (IsPointNearPath(fPos, pathPoints, 5.0))
                    {
                        numFatsOnPath++;
                    }
                }

                // Get tolerance percent and FDT slack count from UI
                double tolerancePercent = 5.0;
                double slackFdtCount = 1.0;
                panel.Dispatcher.Invoke(() =>
                {
                    tolerancePercent = panel.GetCableTolerance();
                    slackFdtCount = panel.GetCableSlackFdt();
                });

                // Match prefix of selected FAT to find all FATs on the same line
                string selectedPrefix = GetFatPrefix(fatName);
                var sortedFats = new List<FatInfo>();

                if (!string.IsNullOrEmpty(selectedPrefix))
                {
                    var lineFats = fatInfoList
                        .Where(f => GetFatPrefix(f.Name).Equals(selectedPrefix, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    sortedFats = lineFats.OrderBy(f => f.Number).ToList();
                }

                // Find index of the selected FAT in the sorted line list
                int idx = -1;
                for (int i = 0; i < sortedFats.Count; i++)
                {
                    if (sortedFats[i].Position.DistanceTo(fatPos) < 1.0)
                    {
                        idx = i;
                        break;
                    }
                }

                double routeDistance = cableDistance;
                double slackLength = fatSlack + fdtSlack; // fallback

                if (idx >= 0 && sortedFats.Count > 0)
                {
                    // Calculate route distance segment-by-segment from FDT to selected FAT to capture loops/tektok correctly
                    routeDistance = GetPathLength(FindCablePath(db, nearestFdt, sortedFats[0].Position));
                    for (int i = 1; i <= idx; i++)
                    {
                        routeDistance += GetPathLength(FindCablePath(db, sortedFats[i - 1].Position, sortedFats[i].Position));
                    }

                    if (idx == sortedFats.Count - 1)
                    {
                        // Last FAT on the path includes full slack for the end coil
                        slackLength = slackFdtCount * fdtSlack + (idx + 1) * fatSlack;
                    }
                    else
                    {
                        // Intermediate FATs include half-slack on incoming side
                        slackLength = slackFdtCount * fdtSlack + idx * fatSlack + (fatSlack / 2.0);
                    }
                }
                else
                {
                    // Fallback using single shortest path if prefix matching didn't yield results
                    routeDistance = cableDistance;
                    slackLength = slackFdtCount * fdtSlack + numFatsOnPath * fatSlack;
                }

                double routePlusSlack = routeDistance + slackLength;
                double toleranceVal = Math.Round(routePlusSlack * (tolerancePercent / 100.0));
                double totalDistance = Math.Round(routePlusSlack + toleranceVal);

                // Cumulative distance is equal to total distance (since we calculate from FDT)
                double cumulativeDistance = totalDistance;

                // Calculate estimated attenuation based on cumulative fiber length
                double attenuation = CalculateAttenuation(cumulativeDistance, oltPower, fiberAtt, fdtSplitter, fatSplitter, spliceLoss, connLoss);

                // Ask user to specify insertion point for the FAT Label Info table
                var ppo = new PromptPointOptions($"\nSpecify insertion point for FAT {fatName} Table: ");
                var ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK)
                {
                    continue;
                }
                Point3d insertPt = ppr.Value;

                // Place the table assets
                using (var docLock = doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        // Register RegApp for XData
                        var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
                        if (!regTable.Has("FTTH_FAT_TABLE_ID"))
                        {
                            using (var regApp = new RegAppTableRecord())
                            {
                                regApp.Name = "FTTH_FAT_TABLE_ID";
                                regTable.Add(regApp);
                                tr.AddNewlyCreatedDBObject(regApp, true);
                            }
                        }

                        string uniqueTableId = Guid.NewGuid().ToString();

                        // Draw table using AssetDrawer
                        string targetLayer = "FTTH-FAT-TABEL";
                        GetOrCreateLayer(db, tr, targetLayer, 5); // Force Blue (color index 5) BEFORE drawing assets
                        var createdIds = AssetDrawer.DrawAsset(db, tr, ms, labelJsonPath, insertPt, 0.0, 1.0, targetLayer);

                        // Attach XData and update MTexts inside createdIds
                        foreach (ObjectId entId in createdIds)
                        {
                            var entObj = tr.GetObject(entId, OpenMode.ForWrite) as Entity;
                            if (entObj != null)
                            {
                                using (var rb = new ResultBuffer(
                                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, "FTTH_FAT_TABLE_ID"),
                                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, uniqueTableId)
                                ))
                                {
                                    entObj.XData = rb;
                                }

                                var mt = entObj as MText;
                                if (mt != null)
                                {
                                    string content = mt.Contents;
                                    if (content.Contains("A01") || content.IndexOf(fatName, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        mt.Contents = fatName;
                                    }
                                    else if (content.Contains("m") && !content.Contains("nm"))
                                    {
                                        mt.Contents = $"{totalDistance} m";
                                    }
                                    else if (content.Contains("dB") || content.Contains("redaman"))
                                    {
                                        mt.Contents = $"{attenuation:F2} dB";
                                    }
                                }
                            }
                        }

                        // Draw connecting line to center of pole where FAT is located
                        Point3d poleCenter = fatPos; // default
                        double minPoleDist = 2.0;
                        foreach (ObjectId id in ms)
                        {
                            if (id.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(BlockReference))))
                            {
                                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                                if (br == null) continue;
                                string bName = br.Name;
                                if (br.IsDynamicBlock)
                                {
                                    try
                                    {
                                        var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                        bName = btr.Name;
                                    }
                                    catch {}
                                }

                                if (bName.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    double d = br.Position.DistanceTo(fatPos);
                                    if (d < minPoleDist)
                                    {
                                        minPoleDist = d;
                                        poleCenter = br.Position;
                                    }
                                }
                            }
                        }

                        // Calculate table bounding box to trim the connecting line at the border
                        Extents3d tableBounds = new Extents3d();
                        bool boundsInitialized = false;
                        foreach (ObjectId entId in createdIds)
                        {
                            var entObj = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                            if (entObj != null)
                            {
                                try
                                {
                                    Extents3d entBounds = entObj.GeometricExtents;
                                    if (!boundsInitialized)
                                    {
                                        tableBounds = entBounds;
                                        boundsInitialized = true;
                                    }
                                    else
                                    {
                                        tableBounds.AddExtents(entBounds);
                                    }
                                }
                                catch {}
                            }
                        }

                        // Determine the endpoint of the connecting line
                        Point3d lineEndPt = insertPt;
                        if (boundsInitialized)
                        {
                            double margin = 0.2;
                            Extents3d boxWithMargin = new Extents3d(
                                new Point3d(tableBounds.MinPoint.X - margin, tableBounds.MinPoint.Y - margin, tableBounds.MinPoint.Z),
                                new Point3d(tableBounds.MaxPoint.X + margin, tableBounds.MaxPoint.Y + margin, tableBounds.MaxPoint.Z)
                            );
                            lineEndPt = GetIntersectionWithBox(poleCenter, insertPt, boxWithMargin);
                        }

                        GetOrCreateLayer(db, tr, targetLayer, 5); // Force Blue (color index 5)

                        using (var connLine = new Line(poleCenter, lineEndPt))
                        {
                            connLine.Layer = targetLayer;
                            connLine.ColorIndex = 256; // ByLayer
                            ms.AppendEntity(connLine);
                            tr.AddNewlyCreatedDBObject(connLine, true);
                        }

                        tr.Commit();
                    }
                }
                ed.UpdateScreen();
                ed.WriteMessage($"\n[FTTH] Placed info table for FAT {fatName} at {insertPt}.");
            }
        }

        public static void GenerateFdtTables()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            var panel = PaletteManager.BasemapPanel;
            if (panel == null)
            {
                ed.WriteMessage("\n[FTTH] UI panel is not active. Please open it using FTTH_SHOWPANEL.");
                return;
            }

            // Check if FDT tables already exist in ModelSpace
            bool hasExistingTables = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.Layer.Equals("FTTH-FDT-TABEL", StringComparison.OrdinalIgnoreCase) && ent.XData != null)
                    {
                        var tvs = ent.XData.AsArray();
                        for (int i = 0; i < tvs.Length - 1; i++)
                        {
                            if (tvs[i].TypeCode == (int)DxfCode.ExtendedDataRegAppName && 
                                tvs[i].Value?.ToString() == "FTTH_FAT_TABLE_ID")
                            {
                                hasExistingTables = true;
                                break;
                            }
                        }
                    }
                    if (hasExistingTables) break;
                }
                tr.Commit();
            }

            string action = "Create";
            if (hasExistingTables)
            {
                var prOpt = new PromptKeywordOptions("\nDitemukan FDT Tabel yang sudah ada di gambar. Pilih tindakan [Refresh/Create] <Refresh>: ");
                prOpt.Keywords.Add("Refresh");
                prOpt.Keywords.Add("Create");
                prOpt.Keywords.Default = "Refresh";
                
                var prRes = ed.GetKeywords(prOpt);
                if (prRes.Status != PromptStatus.OK) return;
                action = prRes.StringResult;
            }

            if (action.Equals("Refresh", StringComparison.OrdinalIgnoreCase))
            {
                RegenerateAllFdtTables(db, ed);
                return;
            }

            string labelJsonFilename = "FDT Tabel Info.json";
            string labelJsonPath = panel.Dispatcher.Invoke(() => panel.GetAssetPath(labelJsonFilename));
            if (!File.Exists(labelJsonPath))
            {
                ed.WriteMessage($"\n[FTTH] Error: FDT Tabel Info template JSON not found at: {labelJsonPath}");
                return;
            }

            // Step 3: Interactive Selection Loop
            while (true)
            {
                // Step 1: Ask user to select an FDT block
                var promptOpt = new PromptEntityOptions("\nSelect FDT Block (or press Enter/Esc to exit): ");
                promptOpt.SetRejectMessage("\nEntity must be a Block Reference.");
                promptOpt.AddAllowedClass(typeof(BlockReference), true);
                var promptRes = ed.GetEntity(promptOpt);
                if (promptRes.Status != PromptStatus.OK) break;

                string fdtName = "FDT_1";
                Point3d insertPt = Point3d.Origin;
                string oltCode = "MLG.100.0401";
                string cleanFdtCode = "FDT_1";
                string cleanOltCode = "MLG.100.0401";
                panel.Dispatcher.Invoke(() =>
                {
                    oltCode = panel.GetLabelFdtSelectedOltCode();
                });

                using (var docLock = doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var selectedFdt = tr.GetObject(promptRes.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (selectedFdt == null)
                        {
                            ed.WriteMessage("\n[FTTH] Selected entity is not a block reference.");
                            tr.Commit();
                            continue;
                        }

                        fdtName = "FDT_1";
                        Point3d fdtPos = selectedFdt.Position;

                        // Try to get name from FDT block attributes
                        foreach (ObjectId attId in selectedFdt.AttributeCollection)
                        {
                            var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (att != null && (att.Tag.Equals("FDT_CODE", StringComparison.OrdinalIgnoreCase) || att.Tag.Equals("FDT_NAME", StringComparison.OrdinalIgnoreCase) || att.Tag.Equals("NAME", StringComparison.OrdinalIgnoreCase) || att.Tag.Equals("CODE", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!string.IsNullOrEmpty(att.TextString))
                                {
                                    fdtName = att.TextString;
                                    break;
                                }
                            }
                        }

                        // Scan for FDT labels if name was not found in attributes
                        if (fdtName == "FDT_1")
                        {
                            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                            var labelTexts = new List<Tuple<Point3d, string, string>>();

                            foreach (ObjectId id in ms)
                            {
                                if (id.IsErased) continue;
                                var obj = tr.GetObject(id, OpenMode.ForRead);
                                if (obj is DBText dt)
                                {
                                    labelTexts.Add(Tuple.Create(dt.Position, dt.TextString, dt.Layer));
                                }
                                else if (obj is MText mt)
                                {
                                    labelTexts.Add(Tuple.Create(mt.Location, mt.Contents, mt.Layer));
                                }
                            }

                            var matchedLabelTuple = labelTexts
                                .Where(l => {
                                    double dist = l.Item1.DistanceTo(fdtPos);
                                    if (dist >= 10.0) return false;
                                    string lyr = l.Item3;
                                    return lyr.Equals("FTT-FDT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                           lyr.Equals("FTTH-FDT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                           lyr.IndexOf("FDT Label", StringComparison.OrdinalIgnoreCase) >= 0;
                                })
                                .OrderBy(l => l.Item1.DistanceTo(fdtPos))
                                .FirstOrDefault();

                            if (matchedLabelTuple != null)
                            {
                                fdtName = matchedLabelTuple.Item2;
                            }
                        }

                        // Clean up fdtName and extract clean OLT code if present in multi-line MText
                        cleanFdtCode = "FDT_1";
                        cleanOltCode = oltCode;
                        if (!string.IsNullOrEmpty(fdtName))
                        {
                            string normalized = fdtName.Replace("\\P", "\n").Replace("\\p", "\n");
                            var lines = normalized.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                                  .Select(s => s.Trim())
                                                  .ToList();
                            if (lines.Count > 0)
                            {
                                cleanFdtCode = lines[0];
                                foreach (var line in lines)
                                {
                                    if (line.IndexOf("MLG.", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("OLT", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (!line.Equals(cleanFdtCode, StringComparison.OrdinalIgnoreCase))
                                        {
                                            cleanOltCode = line;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        // Step 2: Ask user for table insertion point
                        var ptPrompt = new PromptPointOptions("\nSelect insertion point for FDT Table: ");
                        var ptRes = ed.GetPoint(ptPrompt);
                        if (ptRes.Status != PromptStatus.OK)
                        {
                            tr.Commit();
                            continue;
                        }
                        insertPt = ptRes.Value;

                        // Register RegApp for XData
                        var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
                        if (!regTable.Has("FTTH_FAT_TABLE_ID"))
                        {
                            using (var regApp = new RegAppTableRecord())
                            {
                                regApp.Name = "FTTH_FAT_TABLE_ID";
                                regTable.Add(regApp);
                                tr.AddNewlyCreatedDBObject(regApp, true);
                            }
                        }

                        string uniqueTableId = Guid.NewGuid().ToString();

                        // Step 3: Draw FDT table
                        var btWrite = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var msWrite = (BlockTableRecord)tr.GetObject(btWrite[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        string targetLayer = "FTTH-FDT-TABEL";
                        GetOrCreateLayer(db, tr, targetLayer, 1); // Force Red (color index 1) BEFORE drawing assets
                        var createdIds = AssetDrawer.DrawAsset(db, tr, msWrite, labelJsonPath, insertPt, 0.0, 1.0, targetLayer);

                        // Update DBTexts inside createdIds and attach XData
                        foreach (ObjectId entId in createdIds)
                        {
                            var entObj = tr.GetObject(entId, OpenMode.ForWrite) as Entity;
                            if (entObj != null)
                            {
                                using (var rb = new ResultBuffer(
                                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, "FTTH_FAT_TABLE_ID"),
                                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, uniqueTableId)
                                ))
                                {
                                    entObj.XData = rb;
                                }

                                var dt = entObj as DBText;
                                if (dt != null)
                                {
                                    string content = dt.TextString;
                                    if (content.Contains("MLG.100.0401"))
                                    {
                                        dt.TextString = $"{cleanOltCode} .{cleanFdtCode}";
                                    }
                                    else if (content.Trim().Equals("MLW12.127", StringComparison.OrdinalIgnoreCase))
                                    {
                                        dt.TextString = cleanFdtCode;
                                    }
                                }
                            }
                        }

                        // Calculate table bounding box to trim the connecting line at the border
                        Extents3d tableBounds = new Extents3d();
                        bool boundsInitialized = false;
                        foreach (ObjectId entId in createdIds)
                        {
                            var entObj = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                            if (entObj != null)
                            {
                                try
                                {
                                    Extents3d entBounds = entObj.GeometricExtents;
                                    if (!boundsInitialized)
                                    {
                                        tableBounds = entBounds;
                                        boundsInitialized = true;
                                    }
                                    else
                                    {
                                        tableBounds.AddExtents(entBounds);
                                    }
                                }
                                catch {}
                            }
                        }

                        // Determine the endpoint of the connecting line
                        Point3d lineEndPt = insertPt;
                        if (boundsInitialized)
                        {
                            double margin = 0.2;
                            Extents3d boxWithMargin = new Extents3d(
                                new Point3d(tableBounds.MinPoint.X - margin, tableBounds.MinPoint.Y - margin, tableBounds.MinPoint.Z),
                                new Point3d(tableBounds.MaxPoint.X + margin, tableBounds.MaxPoint.Y + margin, tableBounds.MaxPoint.Z)
                            );
                            lineEndPt = GetIntersectionWithBox(fdtPos, insertPt, boxWithMargin);
                        }

                        using (var connLine = new Line(fdtPos, lineEndPt))
                        {
                            connLine.Layer = targetLayer;
                            connLine.ColorIndex = 256; // ByLayer
                            msWrite.AppendEntity(connLine);
                            tr.AddNewlyCreatedDBObject(connLine, true);
                        }

                        tr.Commit();
                    }
                }
                ed.UpdateScreen();
                ed.WriteMessage($"\n[FTTH] Placed info table for FDT {cleanFdtCode} at {insertPt}.");
            }
        }

        public static void GroupAllFatTables()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var docLock = doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    // 1. Gather all pole positions
                    var polePositions = new List<Point3d>();
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br != null)
                        {
                            string bName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    bName = btr.Name;
                                }
                                catch {}
                            }
                            if (bName.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("POLE", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                polePositions.Add(br.Position);
                            }
                        }
                    }

                    // 1b. Gather all FDT positions
                    var fdtPositions = new List<Point3d>();
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br != null)
                        {
                            string bName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    bName = btr.Name;
                                }
                                catch {}
                            }
                            if (bName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                fdtPositions.Add(br.Position);
                            }
                        }
                    }

                    // 2. Gather all entities on layer FTTH-FAT-TABEL and FTTH-FDT-TABEL, separating connecting lines
                    var xdataEntities = new Dictionary<string, List<Entity>>();
                    var noXdataEntities = new List<Entity>();

                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && (ent.Layer.Equals("FTTH-FAT-TABEL", StringComparison.OrdinalIgnoreCase) || ent.Layer.Equals("FTTH-FDT-TABEL", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (ent is Line line)
                            {
                                // Check if it is a connecting line
                                double dx = Math.Abs(line.StartPoint.X - line.EndPoint.X);
                                double dy = Math.Abs(line.StartPoint.Y - line.EndPoint.Y);
                                bool isSlanted = (dx > 0.01 && dy > 0.01);
                                bool touchesFdtOrPole = false;
                                foreach (var pPos in polePositions)
                                {
                                    if (line.StartPoint.DistanceTo(pPos) < 2.0 || line.EndPoint.DistanceTo(pPos) < 2.0)
                                    {
                                        touchesFdtOrPole = true;
                                        break;
                                    }
                                }
                                if (!touchesFdtOrPole)
                                {
                                    foreach (var fPos in fdtPositions)
                                    {
                                        if (line.StartPoint.DistanceTo(fPos) < 2.0 || line.EndPoint.DistanceTo(fPos) < 2.0)
                                        {
                                            touchesFdtOrPole = true;
                                            break;
                                        }
                                    }
                                }

                                if (isSlanted || touchesFdtOrPole)
                                {
                                    continue; // Skip connecting line
                                }
                            }

                            // Read XData under "FTTH_FAT_TABLE_ID" application
                            string tableId = null;
                            var rb = ent.GetXDataForApplication("FTTH_FAT_TABLE_ID");
                            if (rb != null)
                            {
                                foreach (TypedValue tv in rb)
                                {
                                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                    {
                                        tableId = tv.Value as string;
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(tableId))
                            {
                                if (!xdataEntities.ContainsKey(tableId))
                                {
                                    xdataEntities[tableId] = new List<Entity>();
                                }
                                xdataEntities[tableId].Add(ent);
                            }
                            else
                            {
                                noXdataEntities.Add(ent);
                            }
                        }
                    }

                    if (xdataEntities.Count == 0 && noXdataEntities.Count == 0)
                    {
                        ed.WriteMessage("\n[FTTH] No FAT/FDT table entities found on layer FTTH-FAT-TABEL or FTTH-FDT-TABEL to group.");
                        tr.Commit();
                        return;
                    }

                    // 3. Clear any existing groups that contain entities on FTTH-FAT-TABEL or FTTH-FDT-TABEL first
                    var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                    var groupsToDelete = new List<ObjectId>();
                    foreach (DBDictionaryEntry entry in gd)
                    {
                        var gp = tr.GetObject(entry.Value, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Group;
                        if (gp != null)
                        {
                            bool hasFatTableMember = false;
                            foreach (ObjectId memberId in gp.GetAllEntityIds())
                            {
                                try
                                {
                                    var ent = tr.GetObject(memberId, OpenMode.ForRead) as Entity;
                                    if (ent != null && (ent.Layer.Equals("FTTH-FAT-TABEL", StringComparison.OrdinalIgnoreCase) || ent.Layer.Equals("FTTH-FDT-TABEL", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        hasFatTableMember = true;
                                        break;
                                    }
                                }
                                catch {}
                            }
                            if (hasFatTableMember)
                            {
                                groupsToDelete.Add(entry.Value);
                            }
                        }
                    }

                    foreach (var gId in groupsToDelete)
                    {
                        var gp = tr.GetObject(gId, OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Group;
                        if (gp != null)
                        {
                            gp.Erase();
                        }
                    }

                    int groupCount = 0;

                    // 4. Group Category A: Entities with unique XData IDs (exact matching)
                    foreach (var kvp in xdataEntities)
                    {
                        var cluster = kvp.Value;
                        if (cluster.Count > 1)
                        {
                            using (var gp = new Autodesk.AutoCAD.DatabaseServices.Group())
                            {
                                gp.Description = "FAT Table Group";
                                gp.Selectable = true;
                                foreach (var ent in cluster)
                                {
                                    gp.Append(ent.Id);
                                }
                                gd.SetAt("*", gp);
                                tr.AddNewlyCreatedDBObject(gp, true);
                                groupCount++;
                            }
                        }
                    }

                    // 5. Group Category B: Spatial fallback for legacy tables
                    var legacyClusters = new List<List<Entity>>();
                    foreach (var ent in noXdataEntities)
                    {
                        Point3d center = GetEntityCenter(ent);
                        List<Entity> matchedCluster = null;

                        foreach (var cluster in legacyClusters)
                        {
                            foreach (var member in cluster)
                            {
                                Point3d memberCenter = GetEntityCenter(member);
                                if (center.DistanceTo(memberCenter) < 25.0)
                                {
                                    matchedCluster = cluster;
                                    break;
                                }
                            }
                            if (matchedCluster != null) break;
                        }

                        if (matchedCluster != null)
                        {
                            matchedCluster.Add(ent);
                        }
                        else
                        {
                            legacyClusters.Add(new List<Entity> { ent });
                        }
                    }

                    foreach (var cluster in legacyClusters)
                    {
                        if (cluster.Count > 1)
                        {
                            using (var gp = new Autodesk.AutoCAD.DatabaseServices.Group())
                            {
                                gp.Description = "FAT Table Group";
                                gp.Selectable = true;
                                foreach (var ent in cluster)
                                {
                                    gp.Append(ent.Id);
                                }
                                gd.SetAt("*", gp);
                                tr.AddNewlyCreatedDBObject(gp, true);
                                groupCount++;
                            }
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully grouped {groupCount} FAT/FDT tables.");
                }
            }
            ed.UpdateScreen();
        }

        private static Point3d GetEntityCenter(Entity ent)
        {
            if (ent is MText mt) return mt.Location;
            if (ent is DBText dt) return dt.Position;
            try
            {
                Extents3d ext = ent.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0
                );
            }
            catch
            {
                if (ent is Line line)
                {
                    return new Point3d(
                        (line.StartPoint.X + line.EndPoint.X) / 2.0,
                        (line.StartPoint.Y + line.EndPoint.Y) / 2.0,
                        (line.StartPoint.Z + line.EndPoint.Z) / 2.0
                    );
                }
                return Point3d.Origin;
            }
        }

        public static void UngroupAllFatTables()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var docLock = doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                    var groupsToDelete = new List<ObjectId>();

                    foreach (DBDictionaryEntry entry in gd)
                    {
                        var gp = tr.GetObject(entry.Value, OpenMode.ForRead) as Group;
                        if (gp != null)
                        {
                            bool hasFatTableMember = false;
                            foreach (ObjectId memberId in gp.GetAllEntityIds())
                            {
                                try
                                {
                                    var ent = tr.GetObject(memberId, OpenMode.ForRead) as Entity;
                                    if (ent != null && (ent.Layer.Equals("FTTH-FAT-TABEL", StringComparison.OrdinalIgnoreCase) || ent.Layer.Equals("FTTH-FDT-TABEL", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        hasFatTableMember = true;
                                        break;
                                    }
                                }
                                catch {}
                            }
                            if (hasFatTableMember)
                            {
                                groupsToDelete.Add(entry.Value);
                            }
                        }
                    }

                    int deleteCount = 0;
                    foreach (var gId in groupsToDelete)
                    {
                        var gp = tr.GetObject(gId, OpenMode.ForWrite) as Group;
                        if (gp != null)
                        {
                            gp.Erase();
                            deleteCount++;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully ungrouped {deleteCount} FAT/FDT tables.");
                }
            }
            ed.UpdateScreen();
        }

        public static string StripMTextFormatting(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string res = text;
            // Strip format codes like {\fArial|b0|i0|c0|p34;PDA6.051.A01}
            res = System.Text.RegularExpressions.Regex.Replace(res, @"[{}]", "");
            res = System.Text.RegularExpressions.Regex.Replace(res, @"\\[a-zA-Z0-9|:;.,#+-\/\*]+;", "");
            res = res.Replace("\\P", "\n").Replace("\\p", "\n");
            return res.Trim();
        }

        private static double GetDistanceToEntity(Point3d pt, Entity ent)
        {
            try
            {
                var ext = ent.GeometricExtents;
                Point3d center = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2.0, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0, 0.0);
                double dCenter = pt.DistanceTo(center);
                double dMin = pt.DistanceTo(ext.MinPoint);
                double dMax = pt.DistanceTo(ext.MaxPoint);
                return Math.Min(dCenter, Math.Min(dMin, dMax));
            }
            catch
            {
                if (ent is MText mt) return pt.DistanceTo(mt.Location);
                if (ent is Line ln) return Math.Min(pt.DistanceTo(ln.StartPoint), pt.DistanceTo(ln.EndPoint));
                if (ent is Polyline pl) return pt.DistanceTo(pl.StartPoint);
                return double.MaxValue;
            }
        }

        private static void RegenerateAllFatTables(
            Database db, 
            Editor ed, 
            double oltPower, 
            double fiberAtt, 
            double fdtSplitter, 
            double fatSplitter, 
            double spliceLoss, 
            double connLoss, 
            double fatSlack, 
            double fdtSlack,
            List<Point3d> fdtPoints,
            List<Point3d> fatPoints,
            List<Tuple<Point3d, string, string>> labelTexts,
            List<FatInfo> fatInfoList)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            ed.WriteMessage("\n[FTTH] Meregenerasi nilai semua FAT Tabel yang sudah ada di gambar...");

            int updatedCount = 0;

            using (var docLock = doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    // Gather all connecting lines (Line, Polyline, Polyline2d) on layer FTTH-FAT-TABEL or FTTH-FDT-TABEL (using 8-meter search radius)
                    var connectingLines = new List<LineSegment3d>();
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Entity ent && (ent.Layer.Equals("FTTH-FAT-TABEL", StringComparison.OrdinalIgnoreCase) || 
                                                  ent.Layer.Equals("FTTH-FDT-TABEL", StringComparison.OrdinalIgnoreCase)))
                        {
                            Point3d startPt = Point3d.Origin;
                            Point3d endPt = Point3d.Origin;
                            bool isLine = false;

                            if (ent is Line line)
                            {
                                startPt = line.StartPoint;
                                endPt = line.EndPoint;
                                isLine = true;
                            }
                            else if (ent is Polyline poly)
                            {
                                if (poly.NumberOfVertices >= 2)
                                {
                                    startPt = poly.StartPoint;
                                    endPt = poly.EndPoint;
                                    isLine = true;
                                }
                            }
                            else if (ent is Polyline2d poly2d)
                            {
                                var pts = new List<Point3d>();
                                foreach (ObjectId vId in poly2d)
                                {
                                    if (tr.GetObject(vId, OpenMode.ForRead) is Vertex2d v2d)
                                    {
                                        pts.Add(new Point3d(v2d.Position.X, v2d.Position.Y, 0.0));
                                    }
                                }
                                if (pts.Count >= 2)
                                {
                                    startPt = pts[0];
                                    endPt = pts[pts.Count - 1];
                                    isLine = true;
                                }
                            }

                            if (isLine)
                            {
                                double dx = Math.Abs(startPt.X - endPt.X);
                                double dy = Math.Abs(startPt.Y - endPt.Y);
                                bool isSlanted = (dx > 0.01 && dy > 0.01);
                                
                                bool touchesFdtOrPole = false;
                                foreach (var fdt in fdtPoints)
                                {
                                    if (startPt.DistanceTo(fdt) < 8.0 || endPt.DistanceTo(fdt) < 8.0)
                                    {
                                        touchesFdtOrPole = true;
                                        break;
                                    }
                                }
                                if (!touchesFdtOrPole)
                                {
                                    foreach (var fat in fatPoints)
                                    {
                                        if (startPt.DistanceTo(fat) < 8.0 || endPt.DistanceTo(fat) < 8.0)
                                        {
                                            touchesFdtOrPole = true;
                                            break;
                                        }
                                    }
                                }

                                if (isSlanted || touchesFdtOrPole)
                                {
                                    connectingLines.Add(new LineSegment3d(startPt, endPt));
                                }
                            }
                        }
                    }

                    // Group entities by unique table ID in XData using index loop (robust)
                    var tableGroups = new Dictionary<string, List<Entity>>();
                    int totalTableEntities = 0;

                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && ent.XData != null)
                        {
                            var tvs = ent.XData.AsArray();
                            for (int i = 0; i < tvs.Length - 1; i++)
                            {
                                if (tvs[i].TypeCode == (int)DxfCode.ExtendedDataRegAppName && 
                                    tvs[i].Value?.ToString() == "FTTH_FAT_TABLE_ID")
                                {
                                    if (tvs[i + 1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                    {
                                        string uniqueId = tvs[i + 1].Value?.ToString();
                                        if (!string.IsNullOrEmpty(uniqueId))
                                        {
                                            if (!tableGroups.ContainsKey(uniqueId))
                                            {
                                                tableGroups[uniqueId] = new List<Entity>();
                                            }
                                            tableGroups[uniqueId].Add(ent);
                                            totalTableEntities++;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    ed.WriteMessage($"\n[FTTH] Scan Gambar: Ditemukan {totalTableEntities} komponen tabel, terbagi dalam {tableGroups.Count} grup tabel.");

                    // Process each table group
                    foreach (var group in tableGroups)
                    {
                        MText nameMText = null;
                        MText distMText = null;
                        MText attMText = null;
                        FatInfo matchedFat = null;

                        // 1. Try to match FAT via connecting line (checking distance to ANY entity in the group)
                        LineSegment3d bestConnLine = null;
                        double minLineDist = 8.0;
                        foreach (var line in connectingLines)
                        {
                            foreach (var ent in group.Value)
                            {
                                double dStart = GetDistanceToEntity(line.StartPoint, ent);
                                double dEnd = GetDistanceToEntity(line.EndPoint, ent);
                                double d = Math.Min(dStart, dEnd);
                                if (d < minLineDist)
                                {
                                    minLineDist = d;
                                    bestConnLine = line;
                                }
                            }
                        }

                        if (bestConnLine != null)
                        {
                            // Identify which endpoint is closer to the table entities
                            double dStartToTable = double.MaxValue;
                            double dEndToTable = double.MaxValue;
                            foreach (var ent in group.Value)
                            {
                                dStartToTable = Math.Min(dStartToTable, GetDistanceToEntity(bestConnLine.StartPoint, ent));
                                dEndToTable = Math.Min(dEndToTable, GetDistanceToEntity(bestConnLine.EndPoint, ent));
                            }

                            Point3d poleEnd = (dStartToTable > dEndToTable) ? bestConnLine.StartPoint : bestConnLine.EndPoint;

                            var closestFat = fatInfoList.OrderBy(f => f.Position.DistanceTo(poleEnd)).FirstOrDefault();
                            if (closestFat != null && closestFat.Position.DistanceTo(poleEnd) < 8.0)
                            {
                                matchedFat = closestFat;
                                ed.WriteMessage($"\n[FTTH] Pemetaan sukses via garis penghubung (8m): Tabel dipetakan ke FAT '{matchedFat.Name}'");
                            }
                        }

                        // 2. Classify the table MTexts to find name, distance, and attenuation value cells (using Regex patterns)
                        foreach (var ent in group.Value)
                        {
                            if (ent is MText mt)
                            {
                                string text = mt.Text.Trim();
                                string contents = mt.Contents.Trim();

                                // Skip labels
                                if (text.Contains("Distance to FDT:") || contents.Contains("Distance to FDT:"))
                                {
                                    continue;
                                }
                                if (text.Contains("Power @1310") || contents.Contains("Power @1310"))
                                {
                                    continue;
                                }

                                // Check for distance value cell (e.g., "125 m", "125m", "125.5 m")
                                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+(\.\d+)?\s*m$") || 
                                    System.Text.RegularExpressions.Regex.IsMatch(contents, @"^\d+(\.\d+)?\s*m$"))
                                {
                                    distMText = mt;
                                }
                                // Check for attenuation value cell (e.g., "-15.36 dB", "15.36dB", "dB", "redaman")
                                else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^-?\d+(\.\d+)?\s*dB$") || 
                                         System.Text.RegularExpressions.Regex.IsMatch(contents, @"^-?\d+(\.\d+)?\s*dB$") || 
                                         text.Contains("dB") || contents.Contains("dB") || 
                                         text.Contains("redaman") || contents.Contains("redaman"))
                                {
                                    attMText = mt;
                                }
                                // It must be the name cell!
                                else
                                {
                                    nameMText = mt;
                                }
                            }
                        }

                        // 3. Fallback name-based FAT matching if connecting line matching failed
                        if (matchedFat == null && nameMText != null)
                        {
                            string text = nameMText.Text.Trim();
                            string contents = nameMText.Contents.Trim();
                            string strippedText = StripMTextFormatting(text);
                            string strippedContents = StripMTextFormatting(contents);

                            var f = fatInfoList.FirstOrDefault(fat => 
                                fat.Name.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                                fat.Name.Equals(contents, StringComparison.OrdinalIgnoreCase) ||
                                fat.Name.Equals(strippedText, StringComparison.OrdinalIgnoreCase) ||
                                fat.Name.Equals(strippedContents, StringComparison.OrdinalIgnoreCase)
                            );

                            if (f == null)
                            {
                                f = fatInfoList.FirstOrDefault(fat => 
                                    text.IndexOf(fat.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    contents.IndexOf(fat.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    strippedText.IndexOf(fat.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    strippedContents.IndexOf(fat.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    fat.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    fat.Name.IndexOf(strippedText, StringComparison.OrdinalIgnoreCase) >= 0
                                );
                            }

                            if (f != null)
                            {
                                matchedFat = f;
                                ed.WriteMessage($"\n[FTTH] Pemetaan sukses via nama text: Tabel dipetakan ke FAT '{matchedFat.Name}'");
                            }
                        }

                        if (matchedFat == null)
                        {
                            ed.WriteMessage($"\n[FTTH] Warning: Grup tabel '{group.Key.Substring(0, Math.Min(8, group.Key.Length))}...' tidak cocok dengan FAT mana pun.");
                            continue; 
                        }

                        // Run calculations for the matched FAT
                        Point3d fatPos = matchedFat.Position;
                        var nearestFdt = fdtPoints.OrderBy(p => p.DistanceTo(fatPos)).First();
                        var pathPoints = FindCablePath(db, nearestFdt, fatPos);
                        double cableDistance = GetPathLength(pathPoints);

                        int numFatsOnPath = 0;
                        foreach (var fPos in fatPoints)
                        {
                            if (IsPointNearPath(fPos, pathPoints, 5.0))
                            {
                                numFatsOnPath++;
                            }
                        }

                        double tolerancePercent = 5.0;
                        double slackFdtCount = 1.0;
                        var panel = PaletteManager.BasemapPanel;
                        if (panel != null)
                        {
                            panel.Dispatcher.Invoke(() =>
                            {
                                tolerancePercent = panel.GetCableTolerance();
                                slackFdtCount = panel.GetCableSlackFdt();
                            });
                        }

                        string selectedPrefix = GetFatPrefix(matchedFat.Name);
                        var sortedFats = new List<FatInfo>();
                        if (!string.IsNullOrEmpty(selectedPrefix))
                        {
                            var lineFats = fatInfoList
                                .Where(f => GetFatPrefix(f.Name).Equals(selectedPrefix, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            sortedFats = lineFats.OrderBy(f => f.Number).ToList();
                        }

                        int idxFat = -1;
                        for (int i = 0; i < sortedFats.Count; i++)
                        {
                            if (sortedFats[i].Position.DistanceTo(fatPos) < 1.0)
                            {
                                idxFat = i;
                                break;
                            }
                        }

                        double routeDistance = cableDistance;
                        double slackLength = fatSlack + fdtSlack;

                        if (idxFat >= 0 && sortedFats.Count > 0)
                        {
                            routeDistance = GetPathLength(FindCablePath(db, nearestFdt, sortedFats[0].Position));
                            for (int i = 1; i <= idxFat; i++)
                            {
                                routeDistance += GetPathLength(FindCablePath(db, sortedFats[i - 1].Position, sortedFats[i].Position));
                            }

                            if (idxFat == sortedFats.Count - 1)
                            {
                                slackLength = slackFdtCount * fdtSlack + (idxFat + 1) * fatSlack;
                            }
                            else
                            {
                                slackLength = slackFdtCount * fdtSlack + idxFat * fatSlack + (fatSlack / 2.0);
                            }
                        }
                        else
                        {
                            routeDistance = cableDistance;
                            slackLength = slackFdtCount * fdtSlack + numFatsOnPath * fatSlack;
                        }

                        double routePlusSlack = routeDistance + slackLength;
                        double toleranceVal = Math.Round(routePlusSlack * (tolerancePercent / 100.0));
                        double totalDistance = Math.Round(routePlusSlack + toleranceVal);
                        double cumulativeDistance = totalDistance;

                        double attenuation = CalculateAttenuation(cumulativeDistance, oltPower, fiberAtt, fdtSplitter, fatSplitter, spliceLoss, connLoss);

                        // Update value cell MTexts
                        if (nameMText != null)
                        {
                            nameMText.UpgradeOpen();
                            nameMText.Contents = matchedFat.Name;
                        }
                        if (distMText != null)
                        {
                            distMText.UpgradeOpen();
                            distMText.Contents = $"{totalDistance} m";
                        }
                        if (attMText != null)
                        {
                            attMText.UpgradeOpen();
                            attMText.Contents = $"{attenuation:F2} dB";
                        }

                        ed.WriteMessage($"\n[FTTH] Berhasil memetakan & memperbarui Tabel FAT: {matchedFat.Name} (Jarak: {totalDistance}m, Redaman: {attenuation:F2}dB)");
                        updatedCount++;
                    }

                    tr.Commit();
                }
            }

            ed.WriteMessage($"\n[FTTH] Selesai. Total {updatedCount} FAT Tabel berhasil diperbarui langsung di gambar.");
        }

        private static void RegenerateAllFdtTables(Database db, Editor ed)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            ed.WriteMessage("\n[FTTH] Meregenerasi nilai semua FDT Tabel yang sudah ada di gambar...");

            int updatedCount = 0;

            using (var docLock = doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    // 1. Gather all FDT text labels in ModelSpace first
                    var fdtLabels = new List<Tuple<Point3d, string, string>>();
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is DBText dt && (dt.Layer.Equals("FTT-FDT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                                 dt.Layer.Equals("FTTH-FDT-LABEL", StringComparison.OrdinalIgnoreCase)))
                        {
                            fdtLabels.Add(Tuple.Create(dt.Position, dt.TextString, dt.Layer));
                        }
                        else if (obj is MText mt && (mt.Layer.Equals("FTT-FDT-LABEL", StringComparison.OrdinalIgnoreCase) || 
                                                     mt.Layer.Equals("FTTH-FDT-LABEL", StringComparison.OrdinalIgnoreCase)))
                        {
                            fdtLabels.Add(Tuple.Create(mt.Location, mt.Contents, mt.Layer));
                        }
                    }

                    // Gather all FDT blocks in ModelSpace
                    var fdtList = new List<Tuple<Point3d, string>>(); // Position, FdtName
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br != null)
                        {
                            string bName = br.Name;
                            if (br.IsDynamicBlock)
                            {
                                try
                                {
                                    var btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
                                    bName = btr.Name;
                                }
                                catch {}
                            }

                            bool isFdtBlock = false;

                            // A. Check block name or layer name containing "FDT"
                            if (bName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isFdtBlock = true;
                            }

                            // B. Check if block has FDT attribute tag
                            if (!isFdtBlock)
                            {
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    if (att != null && att.Tag.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        isFdtBlock = true;
                                        break;
                                    }
                                }
                            }

                            // C. Check if block has FDT label nearby (within 10.0m)
                            if (!isFdtBlock)
                            {
                                bool hasNearbyFdtLabel = fdtLabels.Any(l => l.Item1.DistanceTo(br.Position) < 10.0);
                                if (hasNearbyFdtLabel)
                                {
                                    isFdtBlock = true;
                                }
                            }

                            if (isFdtBlock)
                            {
                                string fdtName = "";
                                Point3d fdtPos = br.Position;

                                // A. Try to find FDT label near this block (within 10 meters)
                                var nearbyLabels = fdtLabels
                                    .Where(l => l.Item1.DistanceTo(fdtPos) < 10.0)
                                    .OrderBy(l => l.Item1.DistanceTo(fdtPos))
                                    .ToList();

                                string bestLabelText = "";
                                foreach (var lbl in nearbyLabels)
                                {
                                    string cleanText = StripMTextFormatting(lbl.Item2).Trim();
                                    string norm = cleanText.Replace("\\P", "\n").Replace("\\p", "\n");
                                    var lines = norm.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(s => s.Trim())
                                                    .ToList();
                                    if (lines.Count > 1)
                                    {
                                        bestLabelText = lines[0];
                                        break;
                                    }
                                    else if (lines.Count == 1)
                                    {
                                        if (lines[0].IndexOf("FDT", StringComparison.OrdinalIgnoreCase) < 0)
                                        {
                                            bestLabelText = lines[0];
                                            break;
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(bestLabelText))
                                {
                                    fdtName = bestLabelText;
                                }

                                // B. Fallback to block attributes
                                if (string.IsNullOrEmpty(fdtName))
                                {
                                    foreach (ObjectId attId in br.AttributeCollection)
                                    {
                                        var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                        if (att != null && (att.Tag.Equals("FDT_CODE", StringComparison.OrdinalIgnoreCase) || 
                                                             att.Tag.Equals("FDT_NAME", StringComparison.OrdinalIgnoreCase) || 
                                                             att.Tag.Equals("NAME", StringComparison.OrdinalIgnoreCase) || 
                                                             att.Tag.Equals("CODE", StringComparison.OrdinalIgnoreCase)))
                                        {
                                            if (!string.IsNullOrEmpty(att.TextString))
                                            {
                                                fdtName = att.TextString;
                                                break;
                                            }
                                        }
                                    }
                                }

                                // C. Fallback to active UI prefix parameter
                                if (string.IsNullOrEmpty(fdtName) || fdtName.Equals("FDT_1", StringComparison.OrdinalIgnoreCase))
                                {
                                    var activePanel = PaletteManager.BasemapPanel;
                                    if (activePanel != null)
                                    {
                                        activePanel.Dispatcher.Invoke(() =>
                                        {
                                            fdtName = activePanel.TxtLabelFdtPrefix.Text.Trim();
                                        });
                                    }
                                }

                                if (string.IsNullOrEmpty(fdtName))
                                {
                                    fdtName = "FDT_1";
                                }

                                fdtList.Add(Tuple.Create(fdtPos, fdtName));
                            }
                        }
                    }

                    // 2. Gather all connecting lines on layer FTTH-FDT-TABEL
                    var connectingLines = new List<LineSegment3d>();
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Entity ent && ent.Layer.Equals("FTTH-FDT-TABEL", StringComparison.OrdinalIgnoreCase))
                        {
                            Point3d startPt = Point3d.Origin;
                            Point3d endPt = Point3d.Origin;
                            bool isLine = false;

                            if (ent is Line line)
                            {
                                startPt = line.StartPoint;
                                endPt = line.EndPoint;
                                isLine = true;
                            }
                            else if (ent is Polyline poly)
                            {
                                if (poly.NumberOfVertices >= 2)
                                {
                                    startPt = poly.StartPoint;
                                    endPt = poly.EndPoint;
                                    isLine = true;
                                }
                            }
                            else if (ent is Polyline2d poly2d)
                            {
                                var pts = new List<Point3d>();
                                foreach (ObjectId vId in poly2d)
                                {
                                    if (tr.GetObject(vId, OpenMode.ForRead) is Vertex2d v2d)
                                    {
                                        pts.Add(new Point3d(v2d.Position.X, v2d.Position.Y, 0.0));
                                    }
                                }
                                if (pts.Count >= 2)
                                {
                                    startPt = pts[0];
                                    endPt = pts[pts.Count - 1];
                                    isLine = true;
                                }
                            }

                            if (isLine)
                            {
                                double dx = Math.Abs(startPt.X - endPt.X);
                                double dy = Math.Abs(startPt.Y - endPt.Y);
                                bool isSlanted = (dx > 0.01 && dy > 0.01);
                                bool touchesFdt = false;
                                foreach (var fdt in fdtList)
                                {
                                    if (startPt.DistanceTo(fdt.Item1) < 15.0 || endPt.DistanceTo(fdt.Item1) < 15.0)
                                    {
                                        touchesFdt = true;
                                        break;
                                    }
                                }
                                if (isSlanted || touchesFdt)
                                {
                                    connectingLines.Add(new LineSegment3d(startPt, endPt));
                                }
                            }
                        }
                    }

                    // 3. Group FDT table entities by unique table ID
                    var tableGroups = new Dictionary<string, List<Entity>>();
                    int totalTableEntities = 0;
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && ent.Layer.Equals("FTTH-FDT-TABEL", StringComparison.OrdinalIgnoreCase) && ent.XData != null)
                        {
                            var tvs = ent.XData.AsArray();
                            for (int i = 0; i < tvs.Length - 1; i++)
                            {
                                if (tvs[i].TypeCode == (int)DxfCode.ExtendedDataRegAppName && 
                                    tvs[i].Value?.ToString() == "FTTH_FAT_TABLE_ID")
                                {
                                    if (tvs[i + 1].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                    {
                                        string uniqueId = tvs[i + 1].Value?.ToString();
                                        if (!string.IsNullOrEmpty(uniqueId))
                                        {
                                            if (!tableGroups.ContainsKey(uniqueId))
                                            {
                                                tableGroups[uniqueId] = new List<Entity>();
                                            }
                                            tableGroups[uniqueId].Add(ent);
                                            totalTableEntities++;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    ed.WriteMessage($"\n[FTTH] Scan Gambar: Ditemukan {totalTableEntities} komponen FDT tabel, terbagi dalam {tableGroups.Count} grup tabel.");

                    string oltCode = "MLG.100.0401";
                    var panel = PaletteManager.BasemapPanel;
                    if (panel != null)
                    {
                        panel.Dispatcher.Invoke(() =>
                        {
                            oltCode = panel.GetLabelFdtSelectedOltCode();
                        });
                    }

                    // 4. Update each FDT table group
                    foreach (var group in tableGroups)
                    {
                        Tuple<Point3d, string> matchedFdt = null;

                        // Try to match FDT via connecting line (distance to ANY entity in the group)
                        LineSegment3d bestConnLine = null;
                        double minLineDist = 15.0;
                        foreach (var line in connectingLines)
                        {
                            foreach (var ent in group.Value)
                            {
                                double dStart = GetDistanceToEntity(line.StartPoint, ent);
                                double dEnd = GetDistanceToEntity(line.EndPoint, ent);
                                double d = Math.Min(dStart, dEnd);
                                if (d < minLineDist)
                                {
                                    minLineDist = d;
                                    bestConnLine = line;
                                }
                            }
                        }

                        if (bestConnLine != null)
                        {
                            double dStartToTable = double.MaxValue;
                            double dEndToTable = double.MaxValue;
                            foreach (var ent in group.Value)
                            {
                                dStartToTable = Math.Min(dStartToTable, GetDistanceToEntity(bestConnLine.StartPoint, ent));
                                dEndToTable = Math.Min(dEndToTable, GetDistanceToEntity(bestConnLine.EndPoint, ent));
                            }
                            Point3d poleEnd = (dStartToTable > dEndToTable) ? bestConnLine.StartPoint : bestConnLine.EndPoint;

                            var closestFdt = fdtList.OrderBy(f => f.Item1.DistanceTo(poleEnd)).FirstOrDefault();
                            if (closestFdt != null && closestFdt.Item1.DistanceTo(poleEnd) < 15.0)
                            {
                                matchedFdt = closestFdt;
                                ed.WriteMessage($"\n[FTTH] Pemetaan sukses via garis penghubung (15m): Tabel dipetakan ke FDT '{matchedFdt.Item2}'");
                            }
                        }

                        // Try fallback name-based matching if connecting line failed
                        DBText fdtNameText = null;
                        DBText oltText = null;

                        foreach (var ent in group.Value)
                        {
                            if (ent is DBText dt)
                            {
                                string content = dt.TextString.Trim();

                                // Skip static labels and constant values
                                if (content.Contains("Distance") || 
                                    content.Contains("Power") || 
                                    content.Contains("nm") ||
                                    content.Equals(":", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                // Skip distance value (e.g. "0 m", "10 m")
                                if (System.Text.RegularExpressions.Regex.IsMatch(content, @"^\d+(\.\d+)?\s*m$"))
                                {
                                    continue;
                                }

                                // Skip dB power value (e.g. "2.00 dB", "2.00dB")
                                if (System.Text.RegularExpressions.Regex.IsMatch(content, @"^-?\d+(\.\d+)?\s*dB$"))
                                {
                                    continue;
                                }

                                // Identify FDT Name cell vs OLT & FDT code cell
                                if (content.Contains("MLG") || content.Contains("OLT") || content.Contains(" ."))
                                {
                                    oltText = dt;
                                }
                                else
                                {
                                    fdtNameText = dt;
                                }
                            }
                        }

                        if (matchedFdt == null && fdtNameText != null)
                        {
                            string text = fdtNameText.TextString.Trim();
                            var f = fdtList.FirstOrDefault(fdt => fdt.Item2.Equals(text, StringComparison.OrdinalIgnoreCase));
                            if (f != null)
                            {
                                matchedFdt = f;
                                ed.WriteMessage($"\n[FTTH] Pemetaan sukses via nama text: Tabel dipetakan ke FDT '{matchedFdt.Item2}'");
                            }
                        }

                        if (matchedFdt == null)
                        {
                            ed.WriteMessage($"\n[FTTH] Warning: Grup FDT tabel '{group.Key.Substring(0, Math.Min(8, group.Key.Length))}...' tidak cocok dengan FDT mana pun.");
                            continue;
                        }

                        string cleanFdtCode = matchedFdt.Item2;
                        string cleanOltCode = oltCode;

                        // Update FDT table texts
                        if (fdtNameText != null)
                        {
                            fdtNameText.UpgradeOpen();
                            fdtNameText.TextString = cleanFdtCode;
                        }
                        if (oltText != null)
                        {
                            oltText.UpgradeOpen();
                            oltText.TextString = $"{cleanOltCode} .{cleanFdtCode}";
                        }

                        ed.WriteMessage($"\n[FTTH] Berhasil memetakan & memperbarui Tabel FDT: {cleanFdtCode}");
                        updatedCount++;
                    }

                    tr.Commit();
                }
            }

            ed.WriteMessage($"\n[FTTH] Selesai. Total {updatedCount} FDT Tabel berhasil diperbarui langsung di gambar.");
        }
    }
}
