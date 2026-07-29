using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FTTHBasemap
{
    public static class TileMapManager
    {
        private static string GetCleanCrsName(string crsXml)
        {
            if (string.IsNullOrEmpty(crsXml)) return "Unknown";
            
            int nameStart = crsXml.IndexOf("<Name>");
            if (nameStart != -1)
            {
                int nameEnd = crsXml.IndexOf("</Name>", nameStart);
                if (nameEnd != -1)
                {
                    return crsXml.Substring(nameStart + 6, nameEnd - nameStart - 6);
                }
            }

            int idIndex = crsXml.IndexOf("id=\"");
            if (idIndex != -1)
            {
                int valStart = idIndex + 4;
                int valEnd = crsXml.IndexOf("\"", valStart);
                if (valEnd != -1)
                {
                    return crsXml.Substring(valStart, valEnd - valStart);
                }
            }
            
            return crsXml;
        }

        public static bool IsDrawingGeoreferenced(Database db, out string crsName)
        {
            crsName = "";
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId geoId = db.GeoDataObject;
                    if (geoId.IsValid && !geoId.IsErased)
                    {
                        GeoLocationData gd = tr.GetObject(geoId, OpenMode.ForRead) as GeoLocationData;
                        if (gd != null)
                        {
                            crsName = GetCleanCrsName(gd.CoordinateSystem);
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static Point3d TransformLonLatToWcs(Database db, double lon, double lat)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId geoId = db.GeoDataObject;
                GeoLocationData gd = tr.GetObject(geoId, OpenMode.ForRead) as GeoLocationData;
                if (gd != null)
                {
                    return gd.TransformFromLonLatAlt(new Point3d(lon, lat, 0.0));
                }
                throw new InvalidOperationException("Drawing is not georeferenced.");
            }
        }

        public static Point3d TransformWcsToLonLat(Database db, Point3d wcsPt)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId geoId = db.GeoDataObject;
                GeoLocationData gd = tr.GetObject(geoId, OpenMode.ForRead) as GeoLocationData;
                if (gd != null)
                {
                    return gd.TransformToLonLatAlt(wcsPt);
                }
                throw new InvalidOperationException("Drawing is not georeferenced.");
            }
        }

        public static double TileXToLon(int x, int zoom)
        {
            return x / Math.Pow(2.0, zoom) * 360.0 - 180.0;
        }

        public static double TileYToLat(int y, int zoom)
        {
            double n = Math.PI - 2.0 * Math.PI * y / Math.Pow(2.0, zoom);
            return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
        }

        public static int LonToTileX(double lon, int zoom)
        {
            return (int)Math.Floor((lon + 180.0) / 360.0 * Math.Pow(2.0, zoom));
        }

        public static int LatToTileY(double lat, int zoom)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * Math.Pow(2.0, zoom));
        }

        public static string TileToQuadKey(int tx, int ty, int zoom)
        {
            System.Text.StringBuilder quadKey = new System.Text.StringBuilder();
            for (int i = zoom; i > 0; i--)
            {
                char digit = '0';
                int mask = 1 << (i - 1);
                if ((tx & mask) != 0)
                {
                    digit++;
                }
                if ((ty & mask) != 0)
                {
                    digit += (char)2;
                }
                quadKey.Append(digit);
            }
            return quadKey.ToString();
        }

        public static string GetTileUrl(string provider, string mapType, int tx, int ty, int zoom)
        {
            provider = provider.ToLower();
            mapType = mapType.ToLower();

            int serverIndex = (tx + ty) % 4;

            if (provider == "google")
            {
                string lyrs = "s";
                if (mapType == "map" || mapType == "roadmap")
                    lyrs = "m";
                else if (mapType == "hybrid")
                    lyrs = "y";
                else if (mapType == "terrain")
                    lyrs = "p";

                return $"https://mt{serverIndex}.google.com/vt/lyrs={lyrs}&x={tx}&y={ty}&z={zoom}";
            }
            else if (provider == "bing")
            {
                string quadkey = TileToQuadKey(tx, ty, zoom);
                if (mapType == "map" || mapType == "roadmap")
                    return $"http://ecn.t{serverIndex}.tiles.virtualearth.net/tiles/r{quadkey}.jpeg?g=129";
                else if (mapType == "hybrid")
                    return $"http://ecn.t{serverIndex}.tiles.virtualearth.net/tiles/h{quadkey}.jpeg?g=129";
                else // satellite
                    return $"http://ecn.t{serverIndex}.tiles.virtualearth.net/tiles/a{quadkey}.jpeg?g=129";
            }
            else if (provider == "esri")
            {
                if (mapType == "map" || mapType == "roadmap")
                    return $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{zoom}/{ty}/{tx}";
                else // satellite (ESRI satellite is World_Imagery)
                    return $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{zoom}/{ty}/{tx}";
            }

            return $"https://mt{serverIndex}.google.com/vt/lyrs=s&x={tx}&y={ty}&z={zoom}";
        }

        public static int CalculateZoomLevel(double latMin, double latMax, double lonMin, double lonMax, int maxTiles)
        {
            for (int z = 21; z >= 1; z--)
            {
                int tileXMin = LonToTileX(lonMin, z);
                int tileXMax = LonToTileX(lonMax, z);
                int tileYMin = LatToTileY(latMax, z);
                int tileYMax = LatToTileY(latMin, z);

                int numTilesX = tileXMax - tileXMin + 1;
                int numTilesY = tileYMax - tileYMin + 1;
                int totalTiles = numTilesX * numTilesY;

                if (totalTiles <= maxTiles)
                {
                    return z;
                }
            }
            return 1;
        }

        public static Bitmap MakeGrayscale(Bitmap original)
        {
            Bitmap newBitmap = new Bitmap(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(newBitmap))
            {
                ColorMatrix colorMatrix = new ColorMatrix(
                    new float[][]
                    {
                        new float[] {.3f, .3f, .3f, 0, 0},
                        new float[] {.59f, .59f, .59f, 0, 0},
                        new float[] {.11f, .11f, .11f, 0, 0},
                        new float[] {0, 0, 0, 1, 0},
                        new float[] {0, 0, 0, 0, 1}
                    });
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(colorMatrix);
                    g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            return newBitmap;
        }

        public static string DownloadAndStitchTilesSync(
            int tileXMin, int tileXMax, int tileYMin, int tileYMax, int zoom, 
            string outputPath, string provider, string mapType, string colorMode,
            Action<int, int> progressCallback = null)
        {
            int numTilesX = tileXMax - tileXMin + 1;
            int numTilesY = tileYMax - tileYMin + 1;

            int width = numTilesX * 256;
            int height = numTilesY * 256;

            using (Bitmap stitchedImage = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(stitchedImage))
                {
                    g.Clear(Color.Black);

                    int totalTiles = numTilesX * numTilesY;
                    int downloadedTiles = 0;

                    for (int ty = tileYMin; ty <= tileYMax; ty++)
                    {
                        for (int tx = tileXMin; tx <= tileXMax; tx++)
                        {
                            string url = GetTileUrl(provider, mapType, tx, ty, zoom);
                            try
                            {
                                using (WebClient client = new WebClient())
                                {
                                    // Set request headers to look like a modern browser request to bypass rate limit / 403 Forbidden
                                    client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                                    client.Headers.Add("Accept", "image/webp,image/apng,image/png,image/*,*/*;q=0.8");
                                    client.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                                    
                                    string providerLower = provider.ToLower();
                                    if (providerLower == "google")
                                    {
                                        client.Headers.Add("Referer", "https://www.google.com/");
                                    }
                                    else if (providerLower == "bing")
                                    {
                                        client.Headers.Add("Referer", "https://www.bing.com/maps");
                                    }

                                    byte[] data = client.DownloadData(url);
                                    using (var ms = new MemoryStream(data))
                                    {
                                        using (Bitmap tile = new Bitmap(ms))
                                        {
                                            int posX = (tx - tileXMin) * 256;
                                            int posY = (ty - tileYMin) * 256;
                                            g.DrawImage(tile, posX, posY);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to download tile {tx},{ty}: {ex.Message}");
                                try
                                {
                                    Document doc = Application.DocumentManager.MdiActiveDocument;
                                    if (doc != null)
                                    {
                                        doc.Editor.WriteMessage($"\n[FTTH] Warning: Failed to download tile {tx},{ty} at zoom {zoom}: {ex.Message}");
                                    }
                                }
                                catch { }
                            }

                            downloadedTiles++;
                            progressCallback?.Invoke(downloadedTiles, totalTiles);

                            // Small delay (100ms) to avoid rate limiting
                            System.Threading.Thread.Sleep(100);
                        }
                    }
                }

                if (colorMode.Equals("grayscale", StringComparison.OrdinalIgnoreCase))
                {
                    using (Bitmap grayscaleImage = MakeGrayscale(stitchedImage))
                    {
                        grayscaleImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                }
                else
                {
                    stitchedImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
            }
            return outputPath;
        }

        public static async Task<string> DownloadAndStitchTiles(int tileXMin, int tileXMax, int tileYMin, int tileYMax, int zoom, string outputPath)
        {
            int numTilesX = tileXMax - tileXMin + 1;
            int numTilesY = tileYMax - tileYMin + 1;

            int width = numTilesX * 256;
            int height = numTilesY * 256;

            using (Bitmap stitchedImage = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(stitchedImage))
                {
                    g.Clear(Color.Black);

                    using (WebClient client = new WebClient())
                    {
                        // Set User-Agent to prevent getting 403 Forbidden from tile server
                        client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                        for (int ty = tileYMin; ty <= tileYMax; ty++)
                        {
                            for (int tx = tileXMin; tx <= tileXMax; tx++)
                            {
                                string url = $"https://mt1.google.com/vt/lyrs=s&x={tx}&y={ty}&z={zoom}";
                                try
                                {
                                    byte[] data = await client.DownloadDataTaskAsync(url);
                                    using (var ms = new MemoryStream(data))
                                    {
                                        using (Bitmap tile = new Bitmap(ms))
                                        {
                                            int posX = (tx - tileXMin) * 256;
                                            int posY = (ty - tileYMin) * 256;
                                            g.DrawImage(tile, posX, posY);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Failed to download tile {tx},{ty}: {ex.Message}");
                                    try
                                    {
                                        Document doc = Application.DocumentManager.MdiActiveDocument;
                                        if (doc != null)
                                        {
                                            doc.Editor.WriteMessage($"\n[FTTH] Warning: Failed to download tile {tx},{ty} at zoom {zoom}: {ex.Message}");
                                        }
                                    }
                                    catch { }
                                }

                                // Small delay to avoid rate limiting
                                await Task.Delay(50);
                            }
                        }
                    }
                }
                stitchedImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            return outputPath;
        }

        public static void InsertRasterImage(Database db, string imagePath, Point3d ptSW, Point3d ptSE, Point3d ptNE, Point3d ptNW, Point3dCollection polylinePoints = null, int imgW = 0, int imgH = 0)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. Get or create the image dictionary
                    ObjectId imageDictId = RasterImageDef.GetImageDictionary(db);
                    if (imageDictId == ObjectId.Null)
                    {
                        imageDictId = RasterImageDef.CreateImageDictionary(db);
                    }

                    DBDictionary imageDict = (DBDictionary)tr.GetObject(imageDictId, OpenMode.ForWrite);

                    // Generate a unique dictionary name based on filename hash to prevent overlaps
                    string fileName = Path.GetFileNameWithoutExtension(imagePath);
                    string dictName = "FTTH_SAT_" + fileName;

                    ObjectId imageDefId;
                    RasterImageDef imageDef;

                    if (imageDict.Contains(dictName))
                    {
                        imageDefId = imageDict.GetAt(dictName);
                        imageDef = (RasterImageDef)tr.GetObject(imageDefId, OpenMode.ForWrite);
                    }
                    else
                    {
                        imageDef = new RasterImageDef();
                        imageDef.SourceFileName = imagePath;
                        imageDefId = imageDict.SetAt(dictName, imageDef);
                        tr.AddNewlyCreatedDBObject(imageDef, true);
                    }

                    // Load the image definition
                    imageDef.Load();

                    // 2. Ensure the raster layer exists and is colored gray
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    string layerName = "FTTH-SATELLITE-MAP";
                    if (!lt.Has(layerName))
                    {
                        lt.UpgradeOpen();
                        using (LayerTableRecord ltr = new LayerTableRecord())
                        {
                            ltr.Name = layerName;
                            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 9); // Light Gray
                            lt.Add(ltr);
                            tr.AddNewlyCreatedDBObject(ltr, true);
                        }
                    }

                    // 3. Create the RasterImage entity
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    using (RasterImage image = new RasterImage())
                    {
                        image.ImageDefId = imageDefId;

                        Vector3d uVec = ptSE - ptSW;
                        Vector3d vVec = ptNW - ptSW;

                        // Place bottom-left at ptSW, orienting and stretching using uVec and vVec
                        image.Orientation = new CoordinateSystem3d(ptSW, uVec, vVec);

                        image.Layer = layerName;
                        image.ShowImage = true;

                        btr.AppendEntity(image);
                        tr.AddNewlyCreatedDBObject(image, true);

                        // Hook up reactor
                        image.AssociateRasterDef(imageDef);

                        // Apply clip boundary if polyline points are provided
                        if (polylinePoints != null && polylinePoints.Count > 2)
                        {
                            try
                            {
                                double imgWVal = imgW;
                                double imgHVal = imgH;
                                if (imgWVal == 0 || imgHVal == 0)
                                {
                                    try
                                    {
                                        Vector2d imgSize = image.ImageSize(true);
                                        imgWVal = imgSize.X;
                                        imgHVal = imgSize.Y;
                                    }
                                    catch
                                    {
                                        imgWVal = 1024;
                                        imgHVal = 1024;
                                    }
                                }

                                // Get transformation from Model space to Pixel space natively from AutoCAD
                                Matrix3d modelToPixel = image.PixelToModelTransform.Inverse();

                                Point2dCollection pixelPoints = new Point2dCollection();
                                foreach (Point3d pt in polylinePoints)
                                {
                                    Point3d pixelPt = pt.TransformBy(modelToPixel);
                                    double px = pixelPt.X;
                                    double py = pixelPt.Y;

                                    // Clamp to image pixel bounds with a small margin
                                    double minValX = -0.5 + 0.01;
                                    double maxValX = imgWVal - 0.5 - 0.01;
                                    double minValY = -0.5 + 0.01;
                                    double maxValY = imgHVal - 0.5 - 0.01;

                                    if (px < minValX) px = minValX;
                                    if (px > maxValX) px = maxValX;
                                    if (py < minValY) py = minValY;
                                    if (py > maxValY) py = maxValY;

                                    pixelPoints.Add(new Point2d(px, py));
                                    doc.Editor.WriteMessage($"\n[FTTH] Clipping vertex: WCS({pt.X:F2},{pt.Y:F2}) -> Pixel({px:F1},{py:F1})");
                                }

                                // Remove consecutive duplicates and redundant closing point
                                if (pixelPoints.Count > 2)
                                {
                                    // Remove consecutive duplicates
                                    Point2dCollection cleanedPoints = new Point2dCollection();
                                    cleanedPoints.Add(pixelPoints[0]);
                                    for (int i = 1; i < pixelPoints.Count; i++)
                                    {
                                        Point2d current = pixelPoints[i];
                                        Point2d prev = cleanedPoints[cleanedPoints.Count - 1];
                                        if (current.GetDistanceTo(prev) > 0.01)
                                        {
                                            cleanedPoints.Add(current);
                                        }
                                    }

                                    // Ensure the boundary is closed by duplicating the first point at the end if not already closed
                                    if (cleanedPoints.Count > 2)
                                    {
                                        Point2d first = cleanedPoints[0];
                                        Point2d last = cleanedPoints[cleanedPoints.Count - 1];
                                        if (first.GetDistanceTo(last) > 0.01)
                                        {
                                            cleanedPoints.Add(first);
                                        }
                                    }

                                    if (cleanedPoints.Count >= 4)
                                    {
                                        image.SetClipBoundary(ClipBoundaryType.Poly, cleanedPoints);
                                        image.IsClipped = true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                doc.Editor.WriteMessage($"\n[FTTH] Clipping failed: {ex.Message}");
                            }
                        }
                    }

                    tr.Commit();
                }
            }
            doc.Editor.UpdateScreen();
        }

        private static bool TryGetCalibrationValues(out double xRef, out double yRef, out double latRef, out double lonRef)
        {
            xRef = 0; yRef = 0; latRef = 0; lonRef = 0;
            var panel = PaletteManager.BasemapPanel;
            if (panel == null) return false;

            string txtX = "", txtY = "", txtLat = "", txtLon = "";
            bool success = false;
            try
            {
                panel.Dispatcher.Invoke(() =>
                {
                    txtX = panel.TxtXRef.Text;
                    txtY = panel.TxtYRef.Text;
                    txtLat = panel.TxtLatRef.Text;
                    txtLon = panel.TxtLonRef.Text;
                });

                if (!string.IsNullOrEmpty(txtX) && !string.IsNullOrEmpty(txtY) &&
                    !string.IsNullOrEmpty(txtLat) && !string.IsNullOrEmpty(txtLon))
                {
                    success = double.TryParse(txtX, out xRef) &&
                              double.TryParse(txtY, out yRef) &&
                              double.TryParse(txtLat, out latRef) &&
                              double.TryParse(txtLon, out lonRef);
                }
            }
            catch { }
            return success;
        }

        private static double GetUnitsToMetersFactor(Database db)
        {
            switch (db.Insunits)
            {
                case UnitsValue.Millimeters:
                    return 0.001;
                case UnitsValue.Centimeters:
                    return 0.01;
                case UnitsValue.Decimeters:
                    return 0.1;
                case UnitsValue.Meters:
                    return 1.0;
                case UnitsValue.Kilometers:
                    return 1000.0;
                case UnitsValue.Inches:
                    return 0.0254;
                case UnitsValue.Feet:
                    return 0.3048;
                case UnitsValue.Yards:
                    return 0.9144;
                case UnitsValue.Miles:
                    return 1609.344;
                default:
                    return 1.0;
            }
        }

        public static Point3d ProjectLonLatToWcs(Database db, double lon, double lat, bool hasCalibration, double xRef, double yRef, double latRef, double lonRef)
        {
            bool hasGeo = IsDrawingGeoreferenced(db, out _);

            if (hasGeo)
            {
                if (hasCalibration)
                {
                    // Compute shift in Lat/Lon space directly
                    Point3d rawRefProj = TransformWcsToLonLat(db, new Point3d(xRef, yRef, 0.0));
                    double deltaLat = latRef - rawRefProj.Y;
                    double deltaLon = lonRef - rawRefProj.X;
                    
                    // Subtract delta to get the raw unshifted Lat/Lon before native projection
                    double rawLon = lon - deltaLon;
                    double rawLat = lat - deltaLat;
                    return TransformLonLatToWcs(db, rawLon, rawLat);
                }
                return TransformLonLatToWcs(db, lon, lat);
            }
            else
            {
                if (hasCalibration)
                {
                    double dLatCam = lat - latRef;
                    double dLonCam = lon - lonRef;
                    double dY = dLatCam * 111132.954;
                    double dX = dLonCam * 111319.9 * Math.Cos(latRef * Math.PI / 180.0);
                    
                    double toUnits = 1.0 / GetUnitsToMetersFactor(db);
                    return new Point3d(xRef + dX * toUnits, yRef + dY * toUnits, 0.0);
                }
                throw new InvalidOperationException("Please calibrate coordinate system first!");
            }
        }

        public static Point3d ProjectLonLatToWcs(Database db, double lon, double lat)
        {
            double xRef, yRef, latRef, lonRef;
            bool hasCalibration = TryGetCalibrationValues(out xRef, out yRef, out latRef, out lonRef);
            return ProjectLonLatToWcs(db, lon, lat, hasCalibration, xRef, yRef, latRef, lonRef);
        }

        public static Point3d ProjectWcsToLonLat(Database db, Point3d wcsPt, bool hasCalibration, double xRef, double yRef, double latRef, double lonRef)
        {
            bool hasGeo = IsDrawingGeoreferenced(db, out _);

            if (hasGeo)
            {
                Point3d rawLatLon = TransformWcsToLonLat(db, wcsPt);
                if (hasCalibration)
                {
                    // Compute shift in Lat/Lon space directly
                    Point3d rawRefProj = TransformWcsToLonLat(db, new Point3d(xRef, yRef, 0.0));
                    double deltaLat = latRef - rawRefProj.Y;
                    double deltaLon = lonRef - rawRefProj.X;
                    return new Point3d(rawLatLon.X + deltaLon, rawLatLon.Y + deltaLat, rawLatLon.Z);
                }
                return rawLatLon;
            }
            else
            {
                if (hasCalibration)
                {
                    double toMeters = GetUnitsToMetersFactor(db);
                    double dX = (wcsPt.X - xRef) * toMeters;
                    double dY = (wcsPt.Y - yRef) * toMeters;
                    double lat = latRef + (dY / 111132.954);
                    double lon = lonRef + (dX / (111319.9 * Math.Cos(latRef * Math.PI / 180.0)));
                    return new Point3d(lon, lat, 0.0);
                }
                throw new InvalidOperationException("Please calibrate coordinate system first!");
            }
        }

        public static Point3d ProjectWcsToLonLat(Database db, Point3d wcsPt)
        {
            double xRef, yRef, latRef, lonRef;
            bool hasCalibration = TryGetCalibrationValues(out xRef, out yRef, out latRef, out lonRef);
            return ProjectWcsToLonLat(db, wcsPt, hasCalibration, xRef, yRef, latRef, lonRef);
        }
    }
}
