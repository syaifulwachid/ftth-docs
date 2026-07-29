using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using FTTHBasemap.Model;

namespace FTTHBasemap
{
    public static class ParcelGenerator
    {
        /// <summary>
        /// Sets up the parcel and point layers and configures point styling.
        /// </summary>
        public static void SetupParcelLayers(Database db, Transaction tr)
        {
            var settings = BasemapSettings.Instance;

            // Ensure point display mode is visible (PDMODE = 35: circle with cross, PDSIZE = 0.5: absolute size of 0.5m)
            db.Pdmode = 35;
            db.Pdsize = 0.5;

            GetOrCreateLayer(db, tr, settings.LayerParcel, settings.ColorParcel);
            GetOrCreateLayer(db, tr, settings.LayerParcelPoint, settings.ColorParcelPoint);
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                return lt[name];
            }

            lt.UpgradeOpen();
            using (LayerTableRecord ltr = new LayerTableRecord())
            {
                ltr.Name = name;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                ObjectId id = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return id;
            }
        }

        /// <summary>
        /// Enters an interactive point placement mode in AutoCAD.
        /// </summary>
        public static void CreatePointsInteractive()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage("\n[FTTH] Point placement mode active. Click in drawing to add reference points.");
            ed.WriteMessage("\n[FTTH] Press ENTER, ESC, or Right-Click to finish.");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    SetupParcelLayers(db, tr);
                    tr.Commit();
                }

                int count = 0;
                bool placing = true;

                PromptPointOptions ppo = new PromptPointOptions("\n[FTTH] Pick reference point:")
                {
                    AllowNone = true
                };

                while (placing)
                {
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status == PromptStatus.OK)
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                            DBPoint pt = new DBPoint(ppr.Value)
                            {
                                Layer = settings.LayerParcelPoint,
                                ColorIndex = settings.ColorParcelPoint
                            };

                            btr.AppendEntity(pt);
                            tr.AddNewlyCreatedDBObject(pt, true);
                            tr.Commit();
                        }
                        
                        ed.UpdateScreen();
                        count++;
                    }
                    else
                    {
                        placing = false;
                    }
                }

                ed.WriteMessage($"\n[FTTH] Successfully created {count} points on layer {settings.LayerParcelPoint}.");
                PaletteManager.Log($"Placed {count} points.");
            }
        }

        /// <summary>
        /// Prompts the user to select a KML/KMZ file, parses WGS84 markers, and inserts points.
        /// </summary>
        public static void ImportPointsFromFile()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            PromptOpenFileOptions pofo = new PromptOpenFileOptions("Select KML or KMZ Marker File")
            {
                Filter = "KML/KMZ Files (*.kml;*.kmz)|*.kml;*.kmz|KML Files (*.kml)|*.kml|KMZ Files (*.kmz)|*.kmz|All Files (*.*)|*.*"
            };

            PromptFileNameResult pfnr = ed.GetFileNameForOpen(pofo);
            if (pfnr.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[FTTH] Import canceled.");
                PaletteManager.Log("Import canceled.");
                return;
            }

            string filePath = pfnr.StringResult;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                ed.WriteMessage("\n[FTTH] Selected file does not exist.");
                return;
            }

            ed.WriteMessage($"\n[FTTH] Importing from {Path.GetFileName(filePath)}...");

            try
            {
                List<Point3d> coords = new List<Point3d>();
                string ext = Path.GetExtension(filePath).ToLower();

                if (ext == ".kml")
                {
                    using (Stream fs = File.OpenRead(filePath))
                    {
                        coords = ParseKmlStream(fs);
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
                                coords = ParseKmlStream(es);
                            }
                        }
                        else
                        {
                            ed.WriteMessage("\n[FTTH] Error: No KML entry found inside the KMZ file.");
                            return;
                        }
                    }
                }
                else
                {
                    ed.WriteMessage("\n[FTTH] Error: Unsupported file format.");
                    return;
                }

                if (coords.Count == 0)
                {
                    ed.WriteMessage("\n[FTTH] No coordinates or point markers found in the selected file.");
                    PaletteManager.Log("No points found in file.");
                    return;
                }

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        SetupParcelLayers(db, tr);

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        bool isGeoreferenced = !db.GeoDataObject.IsNull;
                        if (!isGeoreferenced)
                        {
                            ed.WriteMessage("\n[FTTH] Warning: Drawing is not georeferenced! Coordinates imported as raw (lon, lat).");
                        }

                        int importedCount = 0;
                        foreach (Point3d rawPt in coords)
                        {
                            Point3d drawingPt;
                            if (isGeoreferenced)
                            {
                                drawingPt = ProjectWgs84ToDrawing(db, tr, rawPt.X, rawPt.Y, rawPt.Z);
                            }
                            else
                            {
                                drawingPt = rawPt;
                            }

                            DBPoint pt = new DBPoint(drawingPt)
                            {
                                Layer = settings.LayerParcelPoint,
                                ColorIndex = settings.ColorParcelPoint
                            };

                            btr.AppendEntity(pt);
                            tr.AddNewlyCreatedDBObject(pt, true);
                            importedCount++;
                        }

                        tr.Commit();
                        ed.WriteMessage($"\n[FTTH] Successfully imported {importedCount} points onto layer {settings.LayerParcelPoint}.");
                        PaletteManager.Log($"Imported {importedCount} points.");
                    }
                }
                doc.Editor.Regen();
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[FTTH] Error importing file: {ex.Message}");
                PaletteManager.Log("Import error.");
            }
        }

        private static List<Point3d> ParseKmlStream(Stream stream)
        {
            List<Point3d> points = new List<Point3d>();
            XDocument doc = XDocument.Load(stream);

            // Namespace-independent search for Placemarks
            var placemarks = doc.Descendants().Where(e => e.Name.LocalName == "Placemark");
            foreach (var pm in placemarks)
            {
                var ptNode = pm.Elements().FirstOrDefault(e => e.Name.LocalName == "Point");
                if (ptNode == null) continue;

                var coordNode = ptNode.Elements().FirstOrDefault(e => e.Name.LocalName == "coordinates");
                if (coordNode == null) continue;

                string coordText = coordNode.Value.Trim();
                string[] parts = coordText.Split(new char[] { ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

                        points.Add(new Point3d(lon, lat, alt));
                    }
                }
            }

            return points;
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

        /// <summary>
        /// Generates closed parcel polylines between sidewalks and ROW using points as house centroids.
        /// Moves points to the geometric center (centroids) of the closed polygons.
        /// </summary>
        public static void GenerateParcels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var settings = BasemapSettings.Instance;

            ed.WriteMessage("\n[FTTH] Generating house parcels...");

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    SetupParcelLayers(db, tr);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Collection to hold point movements to execute after all geometries are created
                    List<Tuple<DBPoint, Point3d>> pointsToMove = new List<Tuple<DBPoint, Point3d>>();

                    // 1. Clear old parcels to avoid overlapping duplicates
                    int erasedParcels = 0;
                    foreach (ObjectId entId in btr)
                    {
                        if (entId.IsErased) continue;
                        Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                        if (ent != null && ent.Layer.Equals(settings.LayerParcel, StringComparison.OrdinalIgnoreCase))
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                            erasedParcels++;
                        }
                    }
                    if (erasedParcels > 0)
                    {
                        ed.WriteMessage($"\n[FTTH] Cleared {erasedParcels} old parcels.");
                    }

                    // 2. Gather all sidewalk curves
                    List<Curve> sidewalks = new List<Curve>();
                    foreach (ObjectId entId in btr)
                    {
                        if (entId.IsErased) continue;
                        Curve c = tr.GetObject(entId, OpenMode.ForRead) as Curve;
                        if (c != null && c.Layer.Equals(settings.LayerSidewalk, StringComparison.OrdinalIgnoreCase))
                        {
                            sidewalks.Add(c);
                        }
                    }

                    if (sidewalks.Count == 0)
                    {
                        ed.WriteMessage($"\n[FTTH] Error: No sidewalk lines found on layer '{settings.LayerSidewalk}'. Run Basemap generation and clean first.");
                        PaletteManager.Log("No sidewalks found.");
                        tr.Commit();
                        return;
                    }

                    // 3. Gather all reference points on layer FTTH-PERCIL-POINT
                    List<DBPoint> points = new List<DBPoint>();
                    foreach (ObjectId entId in btr)
                    {
                        if (entId.IsErased) continue;
                        DBPoint pt = tr.GetObject(entId, OpenMode.ForRead) as DBPoint;
                        if (pt != null && pt.Layer.Equals(settings.LayerParcelPoint, StringComparison.OrdinalIgnoreCase))
                        {
                            points.Add(pt);
                        }
                    }

                    if (points.Count == 0)
                    {
                        ed.WriteMessage($"\n[FTTH] Error: No points found on layer '{settings.LayerParcelPoint}'. Create or import points first.");
                        PaletteManager.Log("No points found.");
                        tr.Commit();
                        return;
                    }

                    // 4. Map each point to its closest sidewalk curve
                    Dictionary<Curve, List<DBPoint>> sidewalkToPointsMap = new Dictionary<Curve, List<DBPoint>>();
                    foreach (Curve s in sidewalks)
                    {
                        sidewalkToPointsMap[s] = new List<DBPoint>();
                    }

                    int unmatchedCount = 0;
                    foreach (DBPoint pt in points)
                    {
                        Curve closestSidewalk = null;
                        double minDist = double.MaxValue;

                        foreach (Curve s in sidewalks)
                        {
                            try
                            {
                                Point3d proj = s.GetClosestPointTo(pt.Position, false);
                                double dist = pt.Position.DistanceTo(proj);
                                if (dist < minDist)
                                {
                                    minDist = dist;
                                    closestSidewalk = s;
                                }
                            }
                            catch { }
                        }

                        // Map points only if they are within 30 meters of the closest sidewalk
                        if (closestSidewalk != null && minDist < 30.0)
                        {
                            sidewalkToPointsMap[closestSidewalk].Add(pt);
                        }
                        else
                        {
                            unmatchedCount++;
                        }
                    }

                    if (unmatchedCount > 0)
                    {
                        ed.WriteMessage($"\n[FTTH] Info: {unmatchedCount} points ignored because they were too far (>30m) from any sidewalk.");
                    }

                    // 5. Generate parcels for each sidewalk segment
                    int parcelCount = 0;
                    foreach (var pair in sidewalkToPointsMap)
                    {
                        Curve s = pair.Key;
                        List<DBPoint> sPoints = pair.Value;

                        if (sPoints.Count == 0) continue;

                        // Find matching ROW curve for this sidewalk segment
                        Curve r = FindMatchingRowCurve(db, tr, s, settings.LayerRow);
                        if (r == null)
                        {
                            ed.WriteMessage($"\n[FTTH] Warning: No matching ROW line found for sidewalk {s.Handle}. Slicing skipped for this street segment.");
                            continue;
                        }

                        // Project points onto sidewalk and store parameter `t` along curve
                        var ptParams = new List<Tuple<DBPoint, double>>();
                        foreach (DBPoint pt in sPoints)
                        {
                            try
                            {
                                Point3d closestPtOnS = s.GetClosestPointTo(pt.Position, false);
                                double t = s.GetParameterAtPoint(closestPtOnS);
                                ptParams.Add(new Tuple<DBPoint, double>(pt, t));
                            }
                            catch { }
                        }

                        // Sort points along sidewalk direction
                        ptParams = ptParams.OrderBy(x => x.Item2).ToList();
                        int n = ptParams.Count;

                        // Calculate division parameters along sidewalk (size n + 1)
                        double[] M = new double[n + 1];
                        M[0] = s.StartParam;
                        for (int k = 1; k < n; k++)
                        {
                            M[k] = (ptParams[k - 1].Item2 + ptParams[k].Item2) / 2.0;
                        }
                        M[n] = s.EndParam;

                        for (int k = 0; k < n; k++)
                        {
                            DBPoint targetPt = ptParams[k].Item1;

                            double a = M[k];
                            double b = M[k + 1];

                            // Make sure range is non-degenerate
                            if (Math.Abs(b - a) < 1e-4) continue;

                            try
                            {
                                Point3d ptSStart = s.GetPointAtParameter(a);
                                Point3d ptSEnd = s.GetPointAtParameter(b);

                                Point3d ptRStart = r.GetClosestPointTo(ptSStart, false);
                                Point3d ptREnd = r.GetClosestPointTo(ptSEnd, false);

                                double uRStart = r.GetParameterAtPoint(ptRStart);
                                double uREnd = r.GetParameterAtPoint(ptREnd);

                                // Extract sidewalk sub-curve and ROW sub-curve
                                using (Curve sSeg = GetSubCurve(s, a, b))
                                using (Curve rSeg = GetSubCurve(r, uRStart, uREnd))
                                {
                                    if (sSeg == null || rSeg == null) continue;

                                    using (Polyline parcelPoly = new Polyline())
                                    {
                                        parcelPoly.Layer = settings.LayerParcel;
                                        parcelPoly.ColorIndex = settings.ColorParcel;

                                        // 1. Add vertices from sidewalk segment (forces bulges to 0)
                                        AddVerticesFromCurve(parcelPoly, sSeg, false);

                                        // 2. Add vertices from ROW segment (reverse if directions match, otherwise forward)
                                        bool reverseR = (uRStart < uREnd);
                                        AddVerticesFromCurve(parcelPoly, rSeg, reverseR);

                                        // Close the polyline
                                        parcelPoly.Closed = true;

                                        // 3. Add parcel to database
                                        btr.AppendEntity(parcelPoly);
                                        tr.AddNewlyCreatedDBObject(parcelPoly, true);
                                        parcelCount++;

                                        // 4. Calculate centroid of this parcel
                                        Point3d centroid = CalculateCentroid(parcelPoly, tr);

                                        // 5. Collect the point movement for execution later
                                        pointsToMove.Add(new Tuple<DBPoint, Point3d>(targetPt, centroid));
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                ed.WriteMessage($"\n[FTTH] Slicing error at index {k}: {ex.Message}");
                            }
                        }
                    }

                    // 6. Relocate all reference points to their centroids after all geometries are completed
                    int movedPointsCount = 0;
                    foreach (var move in pointsToMove)
                    {
                        try
                        {
                            move.Item1.UpgradeOpen();
                            move.Item1.Position = move.Item2;
                            movedPointsCount++;
                        }
                        catch { }
                    }
                    if (movedPointsCount > 0)
                    {
                        ed.WriteMessage($"\n[FTTH] Relocated {movedPointsCount} reference points to centroids.");
                    }

                    tr.Commit();
                    ed.WriteMessage($"\n[FTTH] Successfully generated {parcelCount} closed parcels.");
                    PaletteManager.Log($"Generated {parcelCount} parcels.");
                }
            }
            doc.Editor.Regen();
        }



        private static Curve FindMatchingRowCurve(Database db, Transaction tr, Curve sidewalk, string rowLayerName)
        {
            var metaS = XDataManager.ReadMetadata(sidewalk);

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            Curve bestRow = null;
            double minDistance = double.MaxValue;

            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c == null) continue;

                if (c.Layer.Equals(rowLayerName, StringComparison.OrdinalIgnoreCase))
                {
                    // Match by XData metadata if available
                    if (metaS != null && !string.IsNullOrEmpty(metaS.SourceHandle))
                    {
                        var metaR = XDataManager.ReadMetadata(c);
                        if (metaR != null &&
                            metaR.SourceHandle == metaS.SourceHandle &&
                            metaR.Side == metaS.Side)
                        {
                            return c; // Perfect match
                        }
                    }

                    // Fallback to spatial proximity (closest ROW midpoint to sidewalk midpoint)
                    try
                    {
                        Point3d midS = sidewalk.GetPointAtParameter(sidewalk.StartParam + (sidewalk.EndParam - sidewalk.StartParam) / 2.0);
                        Point3d closestPt = c.GetClosestPointTo(midS, false);
                        double dist = midS.DistanceTo(closestPt);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            bestRow = c;
                        }
                    }
                    catch { }
                }
            }

            // Return closest ROW curve if it is within a reasonable street boundary width (30m)
            if (minDistance < 30.0)
            {
                return bestRow;
            }

            return null;
        }

        private static Curve GetSubCurve(Curve curve, double a, double b)
        {
            if (a > b)
            {
                double temp = a;
                a = b;
                b = temp;
            }

            double start = curve.StartParam;
            double end = curve.EndParam;

            if (a < start) a = start;
            if (b > end) b = end;

            if (Math.Abs(a - start) < 1e-6 && Math.Abs(b - end) < 1e-6)
            {
                return curve.Clone() as Curve;
            }

            DoubleCollection splitParams = new DoubleCollection();
            bool splitAtA = (a > start + 1e-6);
            bool splitAtB = (b < end - 1e-6);

            if (splitAtA) splitParams.Add(a);
            if (splitAtB) splitParams.Add(b);

            DBObjectCollection pieces = curve.GetSplitCurves(splitParams);
            if (pieces == null || pieces.Count == 0) return null;

            Curve result = null;
            if (splitAtA && splitAtB)
            {
                result = pieces[1] as Curve;
                pieces[0].Dispose();
                if (pieces.Count > 2) pieces[2].Dispose();
            }
            else if (splitAtA)
            {
                result = pieces[1] as Curve;
                pieces[0].Dispose();
            }
            else if (splitAtB)
            {
                result = pieces[0] as Curve;
                pieces[1].Dispose();
            }

            return result;
        }

        private static void AddVerticesFromCurve(Polyline dest, Curve src, bool reverse)
        {
            if (src is Polyline pl)
            {
                int numVerts = pl.NumberOfVertices;
                if (numVerts == 0) return;

                if (!reverse)
                {
                    for (int i = 0; i < numVerts; i++)
                    {
                        Point2d pt = pl.GetPoint2dAt(i);
                        // Force all bulges to 0 to make all boundaries straight line segments
                        double bulge = 0.0;
                        double startW = pl.GetStartWidthAt(i);
                        double endW = pl.GetEndWidthAt(i);
                        dest.AddVertexAt(dest.NumberOfVertices, pt, bulge, startW, endW);
                    }
                }
                else
                {
                    for (int i = numVerts - 1; i >= 0; i--)
                    {
                        Point2d pt = pl.GetPoint2dAt(i);
                        // Force all bulges to 0 to make all boundaries straight line segments
                        double bulge = 0.0;
                        double startW = pl.GetEndWidthAt(Math.Max(0, i - 1));
                        double endW = pl.GetStartWidthAt(Math.Max(0, i - 1));
                        dest.AddVertexAt(dest.NumberOfVertices, pt, bulge, startW, endW);
                    }
                }
            }
            else if (src is Line line)
            {
                if (!reverse)
                {
                    dest.AddVertexAt(dest.NumberOfVertices, new Point2d(line.StartPoint.X, line.StartPoint.Y), 0, 0, 0);
                    dest.AddVertexAt(dest.NumberOfVertices, new Point2d(line.EndPoint.X, line.EndPoint.Y), 0, 0, 0);
                }
                else
                {
                    dest.AddVertexAt(dest.NumberOfVertices, new Point2d(line.EndPoint.X, line.EndPoint.Y), 0, 0, 0);
                    dest.AddVertexAt(dest.NumberOfVertices, new Point2d(line.StartPoint.X, line.StartPoint.Y), 0, 0, 0);
                }
            }
            else
            {
                // Discretization fallback for Splines or complex curves (always straight segments)
                int numSamples = 20;
                double startParam = src.StartParam;
                double endParam = src.EndParam;
                double step = (endParam - startParam) / numSamples;

                if (!reverse)
                {
                    for (int i = 0; i <= numSamples; i++)
                    {
                        double p = startParam + i * step;
                        Point3d pt = src.GetPointAtParameter(p);
                        dest.AddVertexAt(dest.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);
                    }
                }
                else
                {
                    for (int i = numSamples; i >= 0; i--)
                    {
                        double p = startParam + i * step;
                        Point3d pt = src.GetPointAtParameter(p);
                        dest.AddVertexAt(dest.NumberOfVertices, new Point2d(pt.X, pt.Y), 0, 0, 0);
                    }
                }
            }
        }

        private static Point3d CalculateCentroid(Polyline pl, Transaction tr)
        {
            try
            {
                using (DBObjectCollection curves = new DBObjectCollection())
                {
                    // Add a clone to ensure we do not modify the original database object during region creation
                    curves.Add(pl.Clone() as Curve);
                    using (DBObjectCollection regions = Region.CreateFromCurves(curves))
                    {
                        if (regions != null && regions.Count > 0)
                        {
                            Region region = regions[0] as Region;
                            if (region != null)
                            {
                                Point3d origin = Point3d.Origin;
                                Vector3d xAxis = Vector3d.XAxis;
                                Vector3d yAxis = Vector3d.YAxis;
                                RegionAreaProperties props = region.AreaProperties(ref origin, ref xAxis, ref yAxis);
                                Point3d centroid = new Point3d(props.Centroid.X, props.Centroid.Y, 0.0);

                                // Clean up region objects immediately
                                foreach (DBObject obj in regions)
                                {
                                    obj.Dispose();
                                }

                                return centroid;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback on region failure
            }

            // Fallback: simple average of vertices
            double sumX = 0;
            double sumY = 0;
            int numVerts = pl.NumberOfVertices;
            for (int i = 0; i < numVerts; i++)
            {
                Point2d pt = pl.GetPoint2dAt(i);
                sumX += pt.X;
                sumY += pt.Y;
            }
            return new Point3d(sumX / numVerts, sumY / numVerts, 0.0);
        }
    }
}
