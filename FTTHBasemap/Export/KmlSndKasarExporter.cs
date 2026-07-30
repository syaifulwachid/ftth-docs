using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FTTHBasemap.Export
{
    public static class KmlSndKasarExporter
    {
        private static XNamespace ns = "http://www.opengis.net/kml/2.2";

        public static void Export(Database db, string filePath, Action<int, string> progressCallback = null)
        {
            // Verify coordinate projection works (will throw if neither georeferenced nor calibrated)
            // Just test with origin to verify calibration/georeference exists before proceeding
            try
            {
                TileMapManager.ProjectWcsToLonLat(db, Point3d.Origin);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Gagal melakukan proyeksi koordinat. Pastikan gambar sudah ter-georeferensi atau lakukan Kalibrasi Koordinat terlebih dahulu di tab STREETVIEW TAGGER!\n\nDetail: " + ex.Message);
            }

            if (progressCallback != null)
            {
                progressCallback(5, "Scanning drawing database...");
            }

            var layerPlacemarks = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
            var layerColors = new Dictionary<string, System.Drawing.Color>(StringComparer.OrdinalIgnoreCase);
            var poleStyles = new HashSet<string>(); // Keep track of dynamically generated pole styles

            XDocument doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"));
            XElement documentNode = new XElement(ns + "Document", new XElement(ns + "name", "FTTH Snd Kasar Export"));
            doc.Add(new XElement(ns + "kml", documentNode));

            ObjectId[] ids;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                ids = btr.Cast<ObjectId>().ToArray();
                tr.Commit();
            }

            int total = ids.Length;
            int processed = 0;
            int lastReportedPercent = -1;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < total; i++)
                {
                    ObjectId id = ids[i];
                    processed++;

                    // Report progress
                    int percent = (int)((double)processed / total * 90.0); // Allocate up to 90% for database scanning
                    if (percent != lastReportedPercent && progressCallback != null)
                    {
                        lastReportedPercent = percent;
                        progressCallback(percent, $"Scanning entities: {processed} / {total} ({percent}%)");
                    }

                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (!(obj is Entity ent)) continue;

                    string layerName = ent.Layer;

                    // Filter layers to export
                    if (!ShouldExportLayer(layerName))
                    {
                        continue;
                    }

                    System.Drawing.Color color = GetEntityColor(db, tr, ent);
                    layerColors[layerName] = color;

                    // Process entity
                    if (IsPoleLayer(layerName))
                    {
                        // For pole layers, convert BlockReferences or Circles to markers
                        if (ent is BlockReference br)
                        {
                            string poleType = IdentifyPoleType(db, tr, br, layerName);
                            AddPoleMarker(db, br.Position, poleType, layerName, color, layerPlacemarks, documentNode, poleStyles);
                        }
                        else if (ent is Circle circle)
                        {
                            string poleType = IdentifyPoleType(db, tr, circle, layerName);
                            AddPoleMarker(db, circle.Center, poleType, layerName, color, layerPlacemarks, documentNode, poleStyles);
                        }
                        else
                        {
                            // Other objects on pole layer (like text labels) exported normally
                            ExportNormalEntity(db, tr, ent, layerName, layerPlacemarks);
                        }
                    }
                    else
                    {
                        // Export non-pole objects normally
                        ExportNormalEntity(db, tr, ent, layerName, layerPlacemarks);
                    }
                }

                tr.Commit();
            }

            if (progressCallback != null)
            {
                progressCallback(92, "Generating KML styles...");
            }

            // 1. Generate Styles for each active layer (for lines, polygons, and standard pushpins)
            foreach (var pair in layerColors)
            {
                string layerName = pair.Key;
                System.Drawing.Color color = pair.Value;
                string hexColor = ConvertColorToKmlHex(color, 255);
                string fillHexColor = ConvertColorToKmlHex(color, 64); // 25% transparent fill

                string iconHref = "http://maps.google.com/mapfiles/kml/pushpin/wht-pushpin.png";
                string iconScale = "1.1";
                string iconColor = hexColor;

                if (IsHompassTypeLayer(layerName))
                {
                    iconScale = "0.0"; // Hide icon, only show label
                }
                else if (layerName.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                {
                    iconHref = "http://maps.google.com/mapfiles/kml/shapes/homegardenbusiness.png";
                    iconScale = "0.8";
                    iconColor = "ff00ff00"; // Green color in AABBGGRR
                }

                XElement style = new XElement(ns + "Style", new XAttribute("id", $"style_{layerName}"),
                    new XElement(ns + "LineStyle",
                        new XElement(ns + "color", hexColor),
                        new XElement(ns + "width", "2.5")
                    ),
                    new XElement(ns + "PolyStyle",
                        new XElement(ns + "color", fillHexColor),
                        new XElement(ns + "fill", "1"),
                        new XElement(ns + "outline", "1")
                    ),
                    new XElement(ns + "IconStyle",
                        new XElement(ns + "color", iconColor),
                        new XElement(ns + "scale", iconScale),
                        new XElement(ns + "Icon",
                            new XElement(ns + "href", iconHref)
                        )
                    ),
                    new XElement(ns + "LabelStyle",
                        new XElement(ns + "color", hexColor),
                        new XElement(ns + "scale", "0.8")
                    )
                );
                documentNode.Add(style);
            }

            if (progressCallback != null)
            {
                progressCallback(95, "Sorting folders (BaseMap, Hompass, Utilitas)...");
            }

            // 2. Generate Folders and place them in the correct hierarchy (BaseMap, Hompass, Utilitas)
            XElement baseMapFolder = new XElement(ns + "Folder", new XElement(ns + "name", "BaseMap"));
            XElement hompassFolder = new XElement(ns + "Folder", new XElement(ns + "name", "Hompass"));
            XElement utilitasFolder = new XElement(ns + "Folder", new XElement(ns + "name", "Utilitas"));

            // List of layers that belong inside the BaseMap folder
            var baseMapLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "FTTH-ROAD-EDGE",
                "FTTH-TROTOAR",
                "FTTH-ROW",
                "FTTH-PERCIL",
                "FTTH-HOMEPASS"
            };

            foreach (var pair in layerPlacemarks)
            {
                string layerName = pair.Key;
                List<XElement> placemarks = pair.Value;

                XElement folder = new XElement(ns + "Folder", new XElement(ns + "name", layerName));
                foreach (var p in placemarks)
                {
                    folder.Add(p);
                }

                if (baseMapLayers.Contains(layerName))
                {
                    baseMapFolder.Add(folder);
                }
                else if (layerName.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                {
                    hompassFolder.Add(folder);
                }
                else
                {
                    utilitasFolder.Add(folder);
                }
            }

            // Add main folders to documentNode if they contain any items
            if (baseMapFolder.Elements().Any(e => e.Name == ns + "Folder"))
            {
                documentNode.Add(baseMapFolder);
            }
            if (hompassFolder.Elements().Any(e => e.Name == ns + "Folder"))
            {
                documentNode.Add(hompassFolder);
            }
            if (utilitasFolder.Elements().Any(e => e.Name == ns + "Folder"))
            {
                documentNode.Add(utilitasFolder);
            }

            if (progressCallback != null)
            {
                progressCallback(98, "Writing KML / KMZ package to disk...");
            }

            // Save KML / KMZ file
            if (filePath.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
            {
                // Export as zipped KMZ containing doc.kml
                using (FileStream fs = new FileStream(filePath, FileMode.Create))
                {
                    using (System.IO.Compression.ZipArchive archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        System.IO.Compression.ZipArchiveEntry kmlEntry = archive.CreateEntry("doc.kml");
                        using (StreamWriter writer = new StreamWriter(kmlEntry.Open()))
                        {
                            writer.Write(doc.ToString());
                        }
                    }
                }
            }
            else
            {
                // Export as raw KML
                doc.Save(filePath);
            }

            // Calculate summary on main thread to avoid AutoCAD cross-threading transaction issues
            double routeLength = 0;
            int hompassCount = 0;
            int poleCount = 0;
            try
            {
                var summary = CalculateSummary(db);
                if (summary != null)
                {
                    routeLength = summary.TotalRouteLength;
                    hompassCount = summary.TotalHompass;
                    poleCount = summary.TotalPoles;
                }
            }
            catch { }

            // Post telemetry asynchronously in background
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    string fileName = System.IO.Path.GetFileName(filePath);
                    string dirPath = System.IO.Path.GetDirectoryName(filePath);
                    await FTTHBasemap.Licensing.LicensingService.Instance.SendExportTelemetryAsync(
                        fileName,
                        dirPath,
                        routeLength,
                        hompassCount,
                        poleCount
                    );
                }
                catch { }
            });
        }

        private static bool ShouldExportLayer(string layerName)
        {
            string upper = layerName.ToUpper();

            // Core layers
            if (upper == "FTTH-HOMEPASS" ||
                upper == "FTTH-ROAD-EDGE" ||
                upper == "FTTH-TROTOAR" ||
                upper == "FTTH-ROW" ||
                upper == "FTTH-PERCIL" ||
                upper == "FTTH-NOMOR-RUMAH")
            {
                return true;
            }

            // Pole layers
            if (IsPoleLayer(layerName))
            {
                return true;
            }

            // Hompass type layers
            if (IsHompassTypeLayer(layerName))
            {
                return true;
            }

            return false;
        }

        private static bool IsPoleLayer(string layerName)
        {
            string upper = layerName.ToUpper();
            return upper.StartsWith("FTTH-POLE") || upper.Contains("POLE") || upper.Contains("TIANG");
        }

        private static bool IsHompassTypeLayer(string layerName)
        {
            string upper = layerName.ToUpper();
            if (upper.EndsWith("_LAYER")) return true;
            if (upper == "TK_LAYER" || upper == "RR_LAYER" || upper == "RK_LAYER" || upper == "UH_LAYER") return true;

            var known = new HashSet<string> {
                "MASJID_LAYER", "GEREJA_LAYER", "ALFAMART_LAYER", "INDOMART_LAYER",
                "FASUM_LAYER", "LAPANGAN_LAYER", "MAKAM_LAYER", "PABRIK_LAYER",
                "SEKOLAHAN_LAYER", "GUDANG_LAYER", "POS_LAYER", "GARDURONDA_LAYER",
                "BALAIRTRW_LAYER", "BALAIDESA_LAYER", "KANTORKELURAHAN_LAYER", "KANDANGAYAM_LAYER"
            };
            return known.Contains(upper);
        }

        private static string IdentifyPoleType(Database db, Transaction tr, Entity ent, string layerName)
        {
            // 1. Try to read XData
            ResultBuffer rb = ent.GetXDataForApplication("FTTH_POLE_DATA");
            if (rb != null)
            {
                var enumerator = rb.GetEnumerator();
                int step = 0;
                while (enumerator.MoveNext())
                {
                    TypedValue val = (TypedValue)enumerator.Current;
                    if (step == 1 && val.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                    {
                        string poleType = val.Value.ToString();
                        rb.Dispose();
                        return poleType;
                    }
                    step++;
                }
                rb.Dispose();
            }

            // 2. Try to get block name if BlockReference
            string blockName = "";
            if (ent is BlockReference br)
            {
                ObjectId btrId = br.IsDynamicBlock ? br.AnonymousBlockTableRecord : br.BlockTableRecord;
                using (BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead))
                {
                    blockName = btr.Name;
                }
            }

            // 3. Match using block name or layer name
            string upperBlock = blockName.ToUpper();
            if (upperBlock.Contains("NP 7 2.5") || upperBlock.Contains("NP725") || upperBlock.Contains("POLE725")) return "NP 7 2.5\"";
            if (upperBlock.Contains("NP 7 3") || upperBlock.Contains("NP73") || upperBlock.Contains("POLE73")) return "NP 7 3\"";
            if (upperBlock.Contains("NP 7 4") || upperBlock.Contains("NP74") || upperBlock.Contains("POLE74")) return "NP 7 4\"";
            if (upperBlock.Contains("NP 9 4") || upperBlock.Contains("POLE94") || upperBlock.Contains("POLE94IN")) return "NP 9 4\"";
            if (upperBlock.Contains("EP725") || upperBlock.Contains("EP 7 2.5")) return "EXT 7 2.5\"";
            if (upperBlock.Contains("EP73") || upperBlock.Contains("EP 7 3")) return "EXT 7 3\"";
            if (upperBlock.Contains("EP74") || upperBlock.Contains("EP 7 4")) return "EXT 7 4\"";
            if (upperBlock.Contains("EP94") || upperBlock.Contains("EP 9 4")) return "EXT 9 4\"";
            if (upperBlock.Contains("EXT TEL") || upperBlock.Contains("EXT_TEL") || upperBlock.Contains("TEL")) return "EXT TEL";

            string upperLayer = layerName.ToUpper();
            if (upperLayer.Equals("FTTH-POLE-NP725", StringComparison.OrdinalIgnoreCase)) return "NP 7 2.5\"";
            if (upperLayer.Equals("FTTH-POLE-NP73", StringComparison.OrdinalIgnoreCase)) return "NP 7 3\"";
            if (upperLayer.Equals("FTTH-POLE-NP74", StringComparison.OrdinalIgnoreCase)) return "NP 7 4\"";
            if (upperLayer.Equals("FTTH-POLE-NP94", StringComparison.OrdinalIgnoreCase)) return "NP 9 4\"";
            if (upperLayer.Equals("FTTH-POLE-EXISTING", StringComparison.OrdinalIgnoreCase)) return "EXT TEL";

            return "POLE";
        }

        private static void AddPoleMarker(Database db, Point3d wcsPos, string poleType, string layerName, System.Drawing.Color color, Dictionary<string, List<XElement>> layerPlacemarks, XElement docNode, HashSet<string> poleStyles)
        {
            Point3d geoPt;
            try
            {
                geoPt = TileMapManager.ProjectWcsToLonLat(db, wcsPos);
            }
            catch { return; }

            // Follow APD/ABD style colors using ExportStyleHelper.GetPoleColor
            string hexColor = ExportStyleHelper.GetPoleColor(poleType);
            if (hexColor.Equals("ffffffff", StringComparison.OrdinalIgnoreCase))
            {
                hexColor = ConvertColorToKmlHex(color, 255);
            }
            string styleId = $"pole_style_{layerName}_{hexColor}";

            // Ensure pole style exists in document node
            if (!poleStyles.Contains(styleId))
            {
                XElement style = new XElement(ns + "Style", new XAttribute("id", styleId),
                    new XElement(ns + "IconStyle",
                        new XElement(ns + "color", hexColor),
                        new XElement(ns + "scale", "0.8"),
                        new XElement(ns + "Icon",
                            new XElement(ns + "href", "http://maps.google.com/mapfiles/kml/shapes/placemark_circle.png")
                        )
                    ),
                    new XElement(ns + "LabelStyle",
                        new XElement(ns + "color", hexColor),
                        new XElement(ns + "scale", "0.8")
                    )
                );
                docNode.Add(style);
                poleStyles.Add(styleId);
            }

            string htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #007ACC; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">Tiang Snd Kasar</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Tipe Tiang</td><td style=""padding: 4px;"">{poleType}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layerName}</td></tr>
  </table>";

            XElement p = new XElement(ns + "Placemark",
                new XElement(ns + "name", poleType),
                new XElement(ns + "styleUrl", $"#{styleId}"),
                new XElement(ns + "description", new XCData(htmlDesc)),
                new XElement(ns + "Point",
                    new XElement(ns + "coordinates", 
                        $"{geoPt.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{geoPt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},0")
                )
            );

            if (!layerPlacemarks.ContainsKey(layerName))
            {
                layerPlacemarks[layerName] = new List<XElement>();
            }
            layerPlacemarks[layerName].Add(p);
        }

        private static void ExportNormalEntity(Database db, Transaction tr, Entity ent, string layerName, Dictionary<string, List<XElement>> layerPlacemarks)
        {
            string htmlDesc = "";

            if (ent is Circle circle)
            {
                Point3d geoPt;
                try { geoPt = TileMapManager.ProjectWcsToLonLat(db, circle.Center); } catch { return; }

                htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Circle Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layerName}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{circle.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Radius</td><td style=""padding: 4px;"">{circle.Radius:F2} m</td></tr>
  </table>";

                XElement p = new XElement(ns + "Placemark",
                    new XElement(ns + "name", layerName.Contains("HOMEPASS") ? "HOMEPASS" : "Circle"),
                    new XElement(ns + "styleUrl", $"#style_{layerName}"),
                    new XElement(ns + "description", new XCData(htmlDesc)),
                    new XElement(ns + "Point",
                        new XElement(ns + "coordinates", 
                            $"{geoPt.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{geoPt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},0")
                    )
                );

                AddPlacemarkToLayer(layerName, p, layerPlacemarks);
            }
            else if (ent is DBPoint ptEntity)
            {
                Point3d geoPt;
                try { geoPt = TileMapManager.ProjectWcsToLonLat(db, ptEntity.Position); } catch { return; }

                htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Point Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layerName}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{ptEntity.Handle}</td></tr>
  </table>";

                XElement p = new XElement(ns + "Placemark",
                    new XElement(ns + "name", "Point"),
                    new XElement(ns + "styleUrl", $"#style_{layerName}"),
                    new XElement(ns + "description", new XCData(htmlDesc)),
                    new XElement(ns + "Point",
                        new XElement(ns + "coordinates", 
                            $"{geoPt.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{geoPt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},0")
                    )
                );

                AddPlacemarkToLayer(layerName, p, layerPlacemarks);
            }
            else if (ent is DBText txt)
            {
                Point3d geoPt;
                try { geoPt = TileMapManager.ProjectWcsToLonLat(db, txt.Position); } catch { return; }

                string cleanText = txt.TextString;
                htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Text Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Text Value</td><td style=""padding: 4px;"">{cleanText}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layerName}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{txt.Handle}</td></tr>
  </table>";

                XElement p = new XElement(ns + "Placemark",
                    new XElement(ns + "name", cleanText),
                    new XElement(ns + "styleUrl", $"#style_{layerName}"),
                    new XElement(ns + "description", new XCData(htmlDesc)),
                    new XElement(ns + "Point",
                        new XElement(ns + "coordinates", 
                            $"{geoPt.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{geoPt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},0")
                    )
                );

                AddPlacemarkToLayer(layerName, p, layerPlacemarks);
            }
            else if (ent is MText mtxt)
            {
                Point3d geoPt;
                try { geoPt = TileMapManager.ProjectWcsToLonLat(db, mtxt.Location); } catch { return; }

                string cleanText = mtxt.Text;
                htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD MText Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Text Value</td><td style=""padding: 4px;"">{cleanText}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layerName}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{mtxt.Handle}</td></tr>
  </table>";

                XElement p = new XElement(ns + "Placemark",
                    new XElement(ns + "name", cleanText),
                    new XElement(ns + "styleUrl", $"#style_{layerName}"),
                    new XElement(ns + "description", new XCData(htmlDesc)),
                    new XElement(ns + "Point",
                        new XElement(ns + "coordinates", 
                            $"{geoPt.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{geoPt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},0")
                    )
                );

                AddPlacemarkToLayer(layerName, p, layerPlacemarks);
            }
            else if (ent is Curve curve)
            {
                Point3dCollection vertices = GetCurveVertices(tr, curve);
                if (vertices.Count < 2) return;

                var coordList = new List<string>();
                foreach (Point3d pt in vertices)
                {
                    try
                    {
                        Point3d geoPt = TileMapManager.ProjectWcsToLonLat(db, pt);
                        coordList.Add($"{geoPt.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{geoPt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},0");
                    }
                    catch { }
                }

                if (coordList.Count < 2) return;

                bool isClosed = false;
                if (curve is Polyline polyEnt && polyEnt.Closed) isClosed = true;
                else if (curve is Polyline3d poly3dEnt && poly3dEnt.Closed) isClosed = true;

                XElement geomNode;

                if (isClosed)
                {
                    coordList.Add(coordList[0]);
                    string coordStr = string.Join(" ", coordList);

                    geomNode = new XElement(ns + "Polygon",
                        new XElement(ns + "tessellate", "1"),
                        new XElement(ns + "outerBoundaryIs",
                            new XElement(ns + "LinearRing",
                                new XElement(ns + "coordinates", coordStr)
                            )
                        )
                    );

                    double area = 0.0;
                    if (curve is Polyline poly) area = poly.Area;
                    htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Polygon Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layerName}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{curve.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Area</td><td style=""padding: 4px;"">{area:F2} m²</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Perimeter</td><td style=""padding: 4px;"">{curve.EndParam:F2} m</td></tr>
  </table>";
                }
                else
                {
                    string coordStr = string.Join(" ", coordList);
                    geomNode = new XElement(ns + "LineString",
                        new XElement(ns + "tessellate", "1"),
                        new XElement(ns + "coordinates", coordStr)
                    );

                    double length = 0.0;
                    try { length = curve.GetDistanceAtParameter(curve.EndParam); } catch { }

                    htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Polyline Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layerName}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{curve.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Length</td><td style=""padding: 4px;"">{length:F2} m</td></tr>
  </table>";
                }

                XElement p = new XElement(ns + "Placemark",
                    new XElement(ns + "name", $"{layerName} Path"),
                    new XElement(ns + "styleUrl", $"#style_{layerName}"),
                    new XElement(ns + "description", new XCData(htmlDesc)),
                    geomNode
                );

                AddPlacemarkToLayer(layerName, p, layerPlacemarks);
            }
        }

        private static void AddPlacemarkToLayer(string layerName, XElement placemark, Dictionary<string, List<XElement>> layerPlacemarks)
        {
            if (!layerPlacemarks.ContainsKey(layerName))
            {
                layerPlacemarks[layerName] = new List<XElement>();
            }
            layerPlacemarks[layerName].Add(placemark);
        }

        private static Point3dCollection GetCurveVertices(Transaction tr, Curve curve)
        {
            Point3dCollection pts = new Point3dCollection();
            if (curve is Line line)
            {
                pts.Add(line.StartPoint);
                pts.Add(line.EndPoint);
            }
            else if (curve is Polyline poly)
            {
                for (int i = 0; i < poly.NumberOfVertices; i++)
                {
                    pts.Add(poly.GetPoint3dAt(i));
                }
            }
            else if (curve is Polyline3d poly3d)
            {
                foreach (ObjectId vId in poly3d)
                {
                    using (PolylineVertex3d v = (PolylineVertex3d)tr.GetObject(vId, OpenMode.ForRead))
                    {
                        pts.Add(v.Position);
                    }
                }
            }
            else
            {
                pts.Add(curve.StartPoint);
                pts.Add(curve.EndPoint);
            }
            return pts;
        }

        private static System.Drawing.Color GetEntityColor(Database db, Transaction tr, Entity ent)
        {
            Autodesk.AutoCAD.Colors.Color acColor = ent.Color;
            if (acColor.IsByLayer)
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (lt.Has(ent.Layer))
                {
                    using (LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[ent.Layer], OpenMode.ForRead))
                    {
                        acColor = ltr.Color;
                    }
                }
            }

            if (acColor.IsByBlock)
            {
                return System.Drawing.Color.White;
            }

            try
            {
                return acColor.ColorValue;
            }
            catch
            {
                return System.Drawing.Color.White;
            }
        }

        private static string ConvertColorToKmlHex(System.Drawing.Color color, byte alpha = 255)
        {
            return string.Format("{0:x2}{1:x2}{2:x2}{3:x2}", alpha, color.B, color.G, color.R);
        }

        public static SndKasarSummary CalculateSummary(Database db, Action<int, string> progressCallback = null)
        {
            var summary = new SndKasarSummary();

            if (progressCallback != null)
            {
                progressCallback(5, "Scanning drawing database...");
            }

            ObjectId[] ids;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                ids = btr.Cast<ObjectId>().ToArray();
                tr.Commit();
            }

            int total = ids.Length;
            int processed = 0;
            int lastReportedPercent = -1;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < total; i++)
                {
                    ObjectId id = ids[i];
                    processed++;

                    int percent = (int)((double)processed / total * 95.0);
                    if (percent != lastReportedPercent && progressCallback != null)
                    {
                        lastReportedPercent = percent;
                        progressCallback(percent, $"Scanning entities for summary: {processed} / {total} ({percent}%)");
                    }

                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (!(obj is Entity ent)) continue;

                    string layerName = ent.Layer;
                    if (string.IsNullOrEmpty(layerName)) continue;

                    // 1. Route length from FTTH-CENTERLINE
                    if (layerName.Equals("FTTH-CENTERLINE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ent is Curve curve)
                        {
                            try
                            {
                                double len = curve.GetDistanceAtParameter(curve.EndParam);
                                summary.TotalRouteLength += len;
                            }
                            catch { }
                        }
                    }

                    // 2. Hompass (Nomor Rumah) count from FTTH-NOMOR-RUMAH
                    if (layerName.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                    {
                        summary.TotalHompass++;
                    }

                    // 3. Pole count from pole layers (only BlockReferences and Circles)
                    if (IsPoleLayer(layerName))
                    {
                        if (ent is BlockReference br)
                        {
                            string poleType = IdentifyPoleType(db, tr, br, layerName);
                            if (!summary.PoleCounts.ContainsKey(poleType))
                            {
                                summary.PoleCounts[poleType] = 0;
                            }
                            summary.PoleCounts[poleType]++;
                            summary.TotalPoles++;
                        }
                        else if (ent is Circle circle)
                        {
                            string poleType = IdentifyPoleType(db, tr, circle, layerName);
                            if (!summary.PoleCounts.ContainsKey(poleType))
                            {
                                summary.PoleCounts[poleType] = 0;
                            }
                            summary.PoleCounts[poleType]++;
                            summary.TotalPoles++;
                        }
                    }

                    // 4. Other entities from HompassType layers (TK, Masjid, etc.)
                    if (IsHompassTypeLayer(layerName))
                    {
                        if (!summary.OtherCounts.ContainsKey(layerName))
                        {
                            summary.OtherCounts[layerName] = 0;
                        }
                        summary.OtherCounts[layerName]++;
                    }
                }
                tr.Commit();
            }

            if (progressCallback != null)
            {
                progressCallback(100, "Summary calculation complete.");
            }

            return summary;
        }
    }

    public class SndKasarSummary
    {
        public double TotalRouteLength { get; set; }
        public int TotalHompass { get; set; }
        public Dictionary<string, int> PoleCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int TotalPoles { get; set; }
        public Dictionary<string, int> OtherCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
