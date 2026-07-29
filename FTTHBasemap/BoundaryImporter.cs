using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FTTHBasemap.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace FTTHBasemap
{
    public class KmlFeature
    {
        public string Name { get; set; }
        public string Type { get; set; } // "Point", "Polygon", "LineString"
        public string FolderName { get; set; }
        public List<Point3d> Coordinates { get; set; } = new List<Point3d>();
    }

    public class PdfGeoMetadata
    {
        public double XMin { get; set; }
        public double YMin { get; set; }
        public double XMax { get; set; }
        public double YMax { get; set; }

        public double Lat1 { get; set; }
        public double Lon1 { get; set; }
        public double Lat2 { get; set; }
        public double Lon2 { get; set; }
        public double Lat3 { get; set; }
        public double Lon3 { get; set; }
        public double Lat4 { get; set; }
        public double Lon4 { get; set; }

        public bool IsValid { get; set; }
    }

    public static class BoundaryImporter
    {
        private const string BoundaryLayerName = "FTTH-BOUNDARY";
        private const short BoundaryLayerColor = 1; // Red

        public static void ImportKmlBoundary()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptOpenFileOptions pofo = new PromptOpenFileOptions("Select KML or KMZ Boundary File")
            {
                Filter = "KML/KMZ Files (*.kml;*.kmz)|*.kml;*.kmz|KML Files (*.kml)|*.kml|KMZ Files (*.kmz)|*.kmz|All Files (*.*)|*.*"
            };

            PromptFileNameResult pfnr = ed.GetFileNameForOpen(pofo);
            if (pfnr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[FTTH] KML Import canceled.");
                return;
            }

            string filePath = pfnr.StringResult;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                ed.WriteMessage("\n[FTTH] Error: Selected file does not exist.");
                return;
            }

            try
            {
                List<KmlFeature> features = null;
                string ext = Path.GetExtension(filePath).ToLower();

                if (ext == ".kml")
                {
                    using (Stream fs = File.OpenRead(filePath))
                    {
                        features = ParseKmlFeatures(fs);
                    }
                }
                else if (ext == ".kmz")
                {
                    using (Stream fs = File.OpenRead(filePath))
                    using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
                        if (entry != null)
                        {
                            using (Stream es = entry.Open())
                            {
                                features = ParseKmlFeatures(es);
                            }
                        }
                        else
                        {
                            ed.WriteMessage("\n[FTTH] Error: No KML entry found inside KMZ file.");
                            return;
                        }
                    }
                }

                if (features == null || features.Count == 0)
                {
                    ed.WriteMessage("\n[FTTH] No suitable features found in the KML/KMZ file.");
                    return;
                }

                // Show selection window
                var selectionWin = new ImportSelectionWindow("KML", features, null);
                bool? result = Application.ShowModalWindow(selectionWin);
                if (result != true || selectionWin.SelectedKmlFeatures.Count == 0)
                {
                    ed.WriteMessage("\n[FTTH] Import selection canceled.");
                    return;
                }

                var selectedFeatures = selectionWin.SelectedKmlFeatures;
                bool placeBlocks = selectionWin.PlaceBlocks;
                string blockType = selectionWin.SelectedBlockType;

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // Ensure boundary layer exists (Red)
                        GetOrCreateLayer(db, tr, BoundaryLayerName, BoundaryLayerColor);

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        bool isGeoreferenced = !db.GeoDataObject.IsNull;
                        int boundaryCount = 0;
                        int pointCount = 0;

                        var panel = PaletteManager.BasemapPanel;

                        foreach (var feat in selectedFeatures)
                        {
                            if (feat.Type == "Point")
                            {
                                Point3d rawPt = feat.Coordinates[0];
                                Point3d drawingPt = isGeoreferenced ? ProjectWgs84ToDrawing(db, tr, rawPt.X, rawPt.Y, rawPt.Z) : rawPt;

                                if (placeBlocks && panel != null && !string.IsNullOrEmpty(blockType))
                                {
                                    // Map block placement
                                    string jsonFile = "";
                                    string targetLayer = "";
                                    short colorIndex = 256;

                                    if (blockType.StartsWith("EXT") || blockType.StartsWith("NP"))
                                    {
                                        jsonFile = panel.GetPoleJsonFilename(blockType);
                                        targetLayer = blockType.StartsWith("EXT") ? "FTTH-POLE-EXISTING" : panel.GetNewPoleLayer(blockType);
                                        colorIndex = blockType.StartsWith("EXT") ? (short)8 : panel.GetNewPoleColor(blockType);
                                    }
                                    else
                                    {
                                        jsonFile = blockType;
                                        if (jsonFile == "FAT Label.JSON")
                                        {
                                            targetLayer = "FTTH-FAT";
                                            colorIndex = 170;
                                        }
                                        else if (jsonFile == "FDT48.JSON")
                                        {
                                            targetLayer = "FTTH-FDT48";
                                            colorIndex = 5;
                                        }
                                        else if (jsonFile == "FDT72.JSON")
                                        {
                                            targetLayer = "FTTH-FDT72";
                                            colorIndex = 5;
                                        }
                                    }

                                    string assetPath = panel.GetAssetPath(jsonFile);
                                    if (File.Exists(assetPath))
                                    {
                                        if (!string.IsNullOrEmpty(targetLayer))
                                        {
                                            GetOrCreateLayer(db, tr, targetLayer, colorIndex);
                                        }
                                        AssetDrawer.DrawAsset(db, tr, btr, assetPath, drawingPt, 0.0, 1.0, targetLayer);
                                        pointCount++;
                                    }
                                }
                                else
                                {
                                    // Draw standard DBPoint
                                    DBPoint pt = new DBPoint(drawingPt)
                                    {
                                        Layer = BoundaryLayerName
                                    };
                                    btr.AppendEntity(pt);
                                    tr.AddNewlyCreatedDBObject(pt, true);
                                    pointCount++;
                                }
                            }
                            else // Polygon or LineString
                            {
                                using (Polyline pl = new Polyline())
                                {
                                    pl.Layer = BoundaryLayerName;
                                    pl.ColorIndex = BoundaryLayerColor;

                                    for (int i = 0; i < feat.Coordinates.Count; i++)
                                    {
                                        Point3d rawPt = feat.Coordinates[i];
                                        Point3d drawingPt = isGeoreferenced ? ProjectWgs84ToDrawing(db, tr, rawPt.X, rawPt.Y, rawPt.Z) : rawPt;
                                        pl.AddVertexAt(i, new Point2d(drawingPt.X, drawingPt.Y), 0.0, 0.0, 0.0);
                                    }

                                    if (feat.Type == "Polygon")
                                    {
                                        pl.Closed = true;
                                    }

                                    btr.AppendEntity(pl);
                                    tr.AddNewlyCreatedDBObject(pl, true);
                                    boundaryCount++;
                                }
                            }
                        }

                        tr.Commit();
                        ed.WriteMessage($"\n[FTTH] KML Import success: Boundary={boundaryCount}, Points/Blocks={pointCount}");
                    }
                }
                ed.Regen();
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[FTTH] Error importing KML: {ex.Message}");
            }
        }

        public static void ImportPdfBoundary()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptOpenFileOptions pofo = new PromptOpenFileOptions("Select QGIS PDF Basemap File")
            {
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
            };

            PromptFileNameResult pfnr = ed.GetFileNameForOpen(pofo);
            if (pfnr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[FTTH] PDF Import canceled.");
                return;
            }

            string filePath = pfnr.StringResult;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                ed.WriteMessage("\n[FTTH] Error: Selected file does not exist.");
                return;
            }

            // Parse geo-metadata
            var geoMeta = ParsePdfGeoMetadata(filePath);
            if (!geoMeta.IsValid)
            {
                ed.WriteMessage("\n[FTTH] Warning: PDF is not georeferenced or metadata not found. Boundaries will be imported in raw page-space coordinates.");
            }

            // Record pre-import entities
            var preImportIds = new HashSet<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms) preImportIds.Add(id);
                tr.Commit();
            }

            ed.WriteMessage("\n[FTTH] Running PDF Import engine. Please wait...");

            try
            {
                // Run AutoCAD's native -PDFIMPORT command line synchronously
                // parameters: _File, path, page, insertionpoint, scale, rotation
                ed.Command("_-PDFIMPORT", "_File", filePath, 1, new Point3d(0, 0, 0), 1.0, 0.0);
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[FTTH] Error executing PDFIMPORT: {ex.Message}");
                return;
            }

            // Get post-import entities
            var importedIds = new List<ObjectId>();
            var importedLayers = new HashSet<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!preImportIds.Contains(id))
                    {
                        importedIds.Add(id);
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && !string.IsNullOrEmpty(ent.Layer))
                        {
                            importedLayers.Add(ent.Layer);
                        }
                    }
                }
                tr.Commit();
            }

            if (importedIds.Count == 0)
            {
                ed.WriteMessage("\n[FTTH] No geometry was imported from the PDF.");
                return;
            }

            // Determine target layer
            string targetLayer = "";
            string defaultLayerKeyword = "Layers_Map_Frame_Layers1_Polygons";
            
            // Look for imported layer ending with the keyword
            foreach (string lyr in importedLayers)
            {
                if (lyr.EndsWith(defaultLayerKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    targetLayer = lyr;
                    break;
                }
            }

            // If default layer not found, ask the user to select
            if (string.IsNullOrEmpty(targetLayer))
            {
                var layersList = importedLayers.OrderBy(l => l).ToList();
                var selectionWin = new ImportSelectionWindow("PDF", null, layersList);
                bool? result = Application.ShowModalWindow(selectionWin);
                if (result == true && !string.IsNullOrEmpty(selectionWin.SelectedPdfLayer))
                {
                    targetLayer = selectionWin.SelectedPdfLayer;
                }
                else
                {
                    // User canceled, clean up everything imported
                    CleanUpImported(db, importedIds, importedLayers);
                    ed.WriteMessage("\n[FTTH] PDF Import canceled.");
                    return;
                }
            }

            // Write debug/coordinate logging for inspection
            try
            {
                var debugLines = new List<string>
                {
                    $"PDF File: {filePath}",
                    $"IsValid Geo: {geoMeta.IsValid}",
                    $"Imported entities count: {importedIds.Count}",
                    $"Imported layers: {string.Join(", ", importedLayers)}",
                    $"Target layer: {targetLayer}"
                };

                if (geoMeta.IsValid)
                {
                    debugLines.Add($"BBox: XMin={geoMeta.XMin}, YMin={geoMeta.YMin}, XMax={geoMeta.XMax}, YMax={geoMeta.YMax}");
                    debugLines.Add($"GPTS: {geoMeta.Lat1}, {geoMeta.Lon1}, {geoMeta.Lat2}, {geoMeta.Lon2}, {geoMeta.Lat3}, {geoMeta.Lon3}, {geoMeta.Lat4}, {geoMeta.Lon4}");
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in importedIds)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && ent.Layer == targetLayer)
                        {
                            debugLines.Add($"Found entity type on target layer: {ent.GetType().Name}");
                            Polyline pl = ent as Polyline;
                            if (pl != null)
                            {
                                debugLines.Add($"Polyline vertices count: {pl.NumberOfVertices}");
                                for (int i = 0; i < Math.Min(pl.NumberOfVertices, 20); i++)
                                {
                                    Point2d pt = pl.GetPoint2dAt(i);
                                    debugLines.Add($"  Vertex {i}: X={pt.X}, Y={pt.Y}");
                                }
                            }
                            break;
                        }
                    }
                    tr.Commit();
                }

                string logDir = @"C:\Users\VICTUS\.gemini\antigravity\brain\71bc5c6c-3ba4-4076-9cf9-1ce69e2c28c3\scratch";
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                File.WriteAllLines(Path.Combine(logDir, "pdf_coords.txt"), debugLines);
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[FTTH] Debug logging error: {ex.Message}");
            }

            // Extract, project and place the boundary polylines
            int extractedCount = 0;
            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    GetOrCreateLayer(db, tr, BoundaryLayerName, BoundaryLayerColor);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    bool isGeoreferenced = !db.GeoDataObject.IsNull;

                    foreach (ObjectId id in importedIds)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent != null && ent.Layer == targetLayer)
                        {
                            // We construct a new projected polyline
                            Polyline originalPoly = ent as Polyline;
                            if (originalPoly != null)
                            {
                                using (Polyline newPoly = new Polyline())
                                {
                                    newPoly.Layer = BoundaryLayerName;
                                    newPoly.ColorIndex = BoundaryLayerColor;
                                    newPoly.Closed = originalPoly.Closed;

                                    int numVerts = originalPoly.NumberOfVertices;
                                    for (int i = 0; i < numVerts; i++)
                                    {
                                        Point2d vertexPt = originalPoly.GetPoint2dAt(i);
                                        Point3d projectedPt;

                                        if (geoMeta.IsValid && isGeoreferenced)
                                        {
                                            // Scale CAD coordinates (inches) to PDF points (1 inch = 72 points)
                                            double xPdf = vertexPt.X * 72.0;
                                            double yPdf = vertexPt.Y * 72.0;
                                            Point2d wgs = TransformPdfToWgs84(geoMeta, xPdf, yPdf);
                                            projectedPt = ProjectWgs84ToDrawing(db, tr, wgs.X, wgs.Y, 0.0);
                                        }
                                        else
                                        {
                                            projectedPt = new Point3d(vertexPt.X, vertexPt.Y, 0.0);
                                        }

                                        newPoly.AddVertexAt(i, new Point2d(projectedPt.X, projectedPt.Y), 0.0, 0.0, 0.0);
                                    }

                                    ms.AppendEntity(newPoly);
                                    tr.AddNewlyCreatedDBObject(newPoly, true);
                                    extractedCount++;
                                }
                            }
                        }
                    }

                    // Erase all imported entities (cleaning the workspace)
                    foreach (ObjectId id in importedIds)
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                        obj.Erase();
                    }

                    // Purge empty PDF_* layers
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    foreach (string lyr in importedLayers)
                    {
                        if (lt.Has(lyr))
                        {
                            LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[lyr], OpenMode.ForWrite);
                            try
                            {
                                ltr.Erase();
                            }
                            catch { }
                        }
                    }

                    tr.Commit();
                }
            }

            ed.WriteMessage($"\n[FTTH] PDF Import success: Extracted {extractedCount} boundary lines onto layer '{BoundaryLayerName}'");
            ed.Regen();
        }

        private static void CleanUpImported(Database db, List<ObjectId> importedIds, HashSet<string> importedLayers)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in importedIds)
                    {
                        try
                        {
                            DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                            obj.Erase();
                        }
                        catch { }
                    }

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    foreach (string lyr in importedLayers)
                    {
                        if (lt.Has(lyr))
                        {
                            try
                            {
                                LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[lyr], OpenMode.ForWrite);
                                ltr.Erase();
                            }
                            catch { }
                        }
                    }
                    tr.Commit();
                }
            }
            doc.Editor.Regen();
        }

        private static PdfGeoMetadata ParsePdfGeoMetadata(string filePath)
        {
            var metadata = new PdfGeoMetadata { IsValid = false };
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                string text = Encoding.ASCII.GetString(bytes);

                // Find /VP block using non-greedy dotall match
                var vpMatch = Regex.Match(text, @"/VP\s*\[\s*<<(.*?)>>\s*\]", RegexOptions.Singleline);
                if (!vpMatch.Success)
                {
                    vpMatch = Regex.Match(text, @"/Measure\s*<<(.*?)>>", RegexOptions.Singleline);
                }

                if (vpMatch.Success)
                {
                    string block = vpMatch.Value;

                    // Parse BBox
                    var bboxMatch = Regex.Match(block, @"/BBox\s*\[\s*([^\]]+)\]");
                    if (bboxMatch.Success)
                    {
                        var parts = bboxMatch.Groups[1].Value.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            metadata.XMin = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.YMin = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.XMax = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.YMax = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }

                    // Parse GPTS
                    var gptsMatch = Regex.Match(block, @"/GPTS\s*\[\s*([^\]]+)\]");
                    if (gptsMatch.Success)
                    {
                        var parts = gptsMatch.Groups[1].Value.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 8)
                        {
                            metadata.Lat1 = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.Lon1 = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.Lat2 = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.Lon2 = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.Lat3 = double.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.Lon3 = double.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.Lat4 = double.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.Lon4 = double.Parse(parts[7], System.Globalization.CultureInfo.InvariantCulture);
                            metadata.IsValid = true;
                        }
                    }
                }
            }
            catch { }
            return metadata;
        }

        private static Point2d TransformPdfToWgs84(PdfGeoMetadata meta, double x, double y)
        {
            double width = meta.XMax - meta.XMin;
            double height = meta.YMax - meta.YMin;
            if (width == 0 || height == 0) return new Point2d(0, 0);

            // Compute normalized coordinates nx, ny
            double nx = (x - meta.XMin) / width;
            double ny = (y - meta.YMin) / height;

            // Bilinear interpolation of lat, lon based on the 4 corners:
            // P1 (0,0) -> Lat1, Lon1
            // P2 (0,1) -> Lat2, Lon2
            // P3 (1,1) -> Lat3, Lon3
            // P4 (1,0) -> Lat4, Lon4
            double lat = meta.Lat1 * (1.0 - nx) * (1.0 - ny) +
                         meta.Lat2 * (1.0 - nx) * ny +
                         meta.Lat3 * nx * ny +
                         meta.Lat4 * nx * (1.0 - ny);

            double lon = meta.Lon1 * (1.0 - nx) * (1.0 - ny) +
                         meta.Lon2 * (1.0 - nx) * ny +
                         meta.Lon3 * nx * ny +
                         meta.Lon4 * nx * (1.0 - ny);

            return new Point2d(lon, lat);
        }

        private static List<KmlFeature> ParseKmlFeatures(Stream stream)
        {
            var features = new List<KmlFeature>();
            XDocument doc = XDocument.Load(stream);

            var placemarks = doc.Descendants().Where(e => e.Name.LocalName == "Placemark");
            foreach (var pm in placemarks)
            {
                var nameNode = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "name");
                string name = nameNode != null ? nameNode.Value : "Unnamed Feature";

                // Find Folder parent
                string folderName = "Default";
                var folderParent = pm.Ancestors().FirstOrDefault(e => e.Name.LocalName == "Folder");
                if (folderParent != null)
                {
                    var fNameNode = folderParent.Elements().FirstOrDefault(e => e.Name.LocalName == "name");
                    if (fNameNode != null) folderName = fNameNode.Value;
                }

                // Check if Point
                var ptNode = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "Point");
                if (ptNode != null)
                {
                    var coordNode = ptNode.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                    if (coordNode != null)
                    {
                        var pts = ParseCoordinatesString(coordNode.Value);
                        if (pts.Count > 0)
                        {
                            features.Add(new KmlFeature { Name = name, Type = "Point", FolderName = folderName, Coordinates = pts });
                        }
                    }
                    continue;
                }

                // Check if Polygon
                var polyNode = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "Polygon");
                if (polyNode != null)
                {
                    var outerNode = polyNode.Elements().FirstOrDefault(e => e.Name.LocalName == "outerBoundaryIs");
                    if (outerNode != null)
                    {
                        var ringNode = outerNode.Elements().FirstOrDefault(e => e.Name.LocalName == "LinearRing");
                        if (ringNode != null)
                        {
                            var coordNode = ringNode.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                            if (coordNode != null)
                            {
                                var pts = ParseCoordinatesString(coordNode.Value);
                                if (pts.Count > 0)
                                {
                                    features.Add(new KmlFeature { Name = name, Type = "Polygon", FolderName = folderName, Coordinates = pts });
                                }
                            }
                        }
                    }
                    continue;
                }

                // Check if LineString
                var lineNode = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "LineString");
                if (lineNode != null)
                {
                    var coordNode = lineNode.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                    if (coordNode != null)
                    {
                        var pts = ParseCoordinatesString(coordNode.Value);
                        if (pts.Count > 0)
                        {
                            features.Add(new KmlFeature { Name = name, Type = "LineString", FolderName = folderName, Coordinates = pts });
                        }
                    }
                }
            }
            return features;
        }

        private static List<Point3d> ParseCoordinatesString(string coordText)
        {
            var pts = new List<Point3d>();
            string[] tuples = coordText.Trim().Split(new char[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string tuple in tuples)
            {
                string[] parts = tuple.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    if (double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lon) &&
                        double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double lat))
                    {
                        double alt = 0.0;
                        if (parts.Length >= 3)
                        {
                            double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out alt);
                        }
                        pts.Add(new Point3d(lon, lat, alt));
                    }
                }
            }
            return pts;
        }

        private static Point3d ProjectWgs84ToDrawing(Database db, Transaction tr, double lon, double lat, double alt)
        {
            if (!db.GeoDataObject.IsNull)
            {
                using (GeoLocationData geoData = tr.GetObject(db.GeoDataObject, OpenMode.ForRead) as GeoLocationData)
                {
                    if (geoData != null)
                    {
                        Point3d geoPt = new Point3d(lon, lat, alt);
                        return geoData.TransformFromLonLatAlt(geoPt);
                    }
                }
            }
            return new Point3d(lon, lat, alt);
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                ObjectId id = lt[name];
                using (LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite))
                {
                    ltr.IsFrozen = false;
                    ltr.IsOff = false;
                }
                return id;
            }

            if (!lt.IsWriteEnabled) lt.UpgradeOpen();
            using (LayerTableRecord ltr = new LayerTableRecord())
            {
                ltr.Name = name;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                ltr.IsFrozen = false;
                ltr.IsOff = false;
                ObjectId id = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return id;
            }
        }
    }
}
