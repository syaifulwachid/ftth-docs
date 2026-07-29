using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace FTTHBasemap.Export
{
    public class KmlGeneratorApdAbd
    {
        private XNamespace ns = "http://www.opengis.net/kml/2.2";

        public void Generate(ExportModel model, string filePath, bool objectOnly = false, bool isSubfeeder = false)
        {
            string clusterName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            if (clusterName.EndsWith("_AutoExport", System.StringComparison.OrdinalIgnoreCase))
            {
                clusterName = clusterName.Substring(0, clusterName.Length - "_AutoExport".Length);
            }

            string rootName = isSubfeeder 
                ? (string.IsNullOrEmpty(model.Fdt.Name) ? "SUBFEEDER CODE_AE" : model.Fdt.Name)
                : clusterName;

            XDocument doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"));
            XElement documentNode = new XElement(ns + "Document", 
                new XElement(ns + "name", rootName),
                new XElement(ns + "description", rootName)
            );
            doc.Add(new XElement(ns + "kml", documentNode));

            // Create styles
            CreateStyle(documentNode, "FdtStyle48", "ffff00aa", 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/cross-hairs.png");
            CreateStyle(documentNode, "FdtStyle72", "ff000055", 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/cross-hairs.png");
            CreateStyle(documentNode, "FdtStyle96", "ff0000ff", 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/cross-hairs.png");
            CreateStyle(documentNode, "FdtStyle144", "ff00ffff", 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/cross-hairs.png");
            CreateStyle(documentNode, "FdtStyle288", "ff00aaff", 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/cross-hairs.png");
            CreateStyle(documentNode, "FdtStyleDefault", "ffffffff", 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/cross-hairs.png");

            CreateStyle(documentNode, "SlackStyle", "ff0000ff", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/target.png");
            CreateLineStyle(documentNode, "SlingStyle", "ffffff00", 3.0);
            CreatePolygonStyle(documentNode, "FatAreaStyle", "ffffffff", "8000aaff", 2.0);
            
            CreateStyle(documentNode, "HpCoverStyle", "ff00ff00", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/homegardenbusiness.png");

            if (isSubfeeder)
            {
                // ==========================================
                // SUBFEEDER MODE
                // ==========================================
                XElement rootFolder = documentNode;

                // Define standard subfeeder folders
                var folders = new Dictionary<string, XElement>(System.StringComparer.OrdinalIgnoreCase)
                {
                    { "JOINT CLOSURE", new XElement(ns + "Folder", new XElement(ns + "name", "JOINT CLOSURE")) },
                    { "EXISTING POLE EMR 7-2.5", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 7-2.5")) },
                    { "EXISTING POLE EMR 7-3", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 7-3")) },
                    { "EXISTING POLE EMR 7-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 7-4")) },
                    { "EXISTING POLE EMR 7-5", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 7-5")) },
                    { "EXISTING POLE EMR 9-5", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 9-5")) },
                    { "EXISTING POLE EMR 9-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 9-4")) },
                    { "EXISTING POLE PARTNER 7-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE PARTNER 7-4")) },
                    { "EXISTING POLE PARTNER 9-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE PARTNER 9-4")) },
                    { "NEW POLE 7-5", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 7-5")) },
                    { "NEW POLE 9-5", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 9-5")) },
                    { "NEW POLE 7-4", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 7-4")) },
                    { "NEW POLE 9-4", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 9-4")) },
                    { "CABLE", new XElement(ns + "Folder", new XElement(ns + "name", "CABLE")) },
                    { "SLACK HANGER", new XElement(ns + "Folder", new XElement(ns + "name", "SLACK HANGER")) }
                };

                // Populate objects across all lines
                foreach (var line in model.Lines)
                {
                    // Closures -> JOINT CLOSURE
                    foreach (var closure in line.Closures)
                    {
                        string color = ExportStyleHelper.GetClosureColor(closure.Name);
                        string styleId = $"ClosureStyle_{color}";
                        EnsureStyleExists(documentNode, styleId, color, 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/forbidden.png");
                        XElement extClosure = CreateExtendedData(closure.Uuid, closure.Rotation, closure.TextHeight, closure.Layer, closure.LineAffiliation);
                        folders["JOINT CLOSURE"].Add(CreatePlacemark(closure.Name, "", $"#{styleId}", closure.Lon, closure.Lat, extClosure));
                    }

                    // Poles -> Respective pole folders
                    foreach (var pole in line.Poles)
                    {
                        string poleCat = pole.Category.ToUpper().Trim();
                        XElement targetFolder = null;

                        if (folders.ContainsKey(poleCat))
                        {
                            targetFolder = folders[poleCat];
                        }
                        else
                        {
                            string matchedKey = folders.Keys.FirstOrDefault(k => k.Equals(poleCat, System.StringComparison.OrdinalIgnoreCase));
                            if (matchedKey != null)
                            {
                                targetFolder = folders[matchedKey];
                            }
                            else
                            {
                                targetFolder = new XElement(ns + "Folder", new XElement(ns + "name", pole.Category));
                                folders[pole.Category] = targetFolder;
                            }
                        }

                        string color = ExportStyleHelper.GetPoleColor(pole.Category);
                        string styleId = $"PoleStyle_{color}";
                        EnsureStyleExists(documentNode, styleId, color, 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/placemark_circle.png");
                        XElement extPole = CreateExtendedData(pole.Uuid, pole.Rotation, pole.TextHeight, pole.Layer, pole.LineAffiliation);
                        targetFolder.Add(CreatePlacemark(pole.Name, "", $"#{styleId}", pole.Lon, pole.Lat, extPole));
                    }

                    // Cables -> CABLE
                    foreach (var cable in line.DistributionCables)
                    {
                        string color = ExportStyleHelper.GetCableColor(cable.Name);
                        string styleId = $"CableStyle_{color}";
                        EnsureLineStyleExists(documentNode, styleId, color, 3.0);
                        XElement extCable = CreateExtendedData(cable.Uuid, cable.Rotation, cable.TextHeight, cable.Layer, cable.LineAffiliation);
                        folders["CABLE"].Add(CreateLineString(cable.Name, cable.Description, $"#{styleId}", cable.LinePoints, extCable));
                    }

                    // Slack Hanger -> SLACK HANGER
                    foreach (var slack in line.SlackHangers)
                    {
                        XElement extSlack = CreateExtendedData(slack.Uuid, slack.Rotation, slack.TextHeight, slack.Layer, slack.LineAffiliation);
                        folders["SLACK HANGER"].Add(CreatePlacemark(slack.Name, "", "#SlackStyle", slack.Lon, slack.Lat, extSlack));
                    }
                }

                // Add folders to rootFolder
                foreach (var kvp in folders)
                {
                    XElement folder = kvp.Value;
                    bool hasObjects = folder.Elements().Any(e => e.Name != ns + "name");
                    if (!objectOnly || hasObjects)
                    {
                        rootFolder.Add(folder);
                    }
                }

                // Append Custom Markers (Handholes & Trenching) at the very end
                var allCustomMarkers = model.Lines.SelectMany(l => l.CustomMarkers ?? new List<EntityItem>()).ToList();
                if (allCustomMarkers.Count > 0)
                {
                    var customGroups = allCustomMarkers.GroupBy(c => c.Category);
                    foreach (var group in customGroups)
                    {
                        XElement folder = new XElement(ns + "Folder", new XElement(ns + "name", group.Key));
                        foreach (var marker in group)
                        {
                            string styleId;
                            if (marker.Name.IndexOf("UG Pedestal", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                styleId = "UgPedestalStyle";
                                EnsureStyleExists(documentNode, styleId, "ffffffff", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/paddle/grn-blank.png");
                            }
                            else if (marker.Name.IndexOf("HH20", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                styleId = "SmallHh20Style";
                                EnsureStyleExists(documentNode, styleId, "ff00ff00", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/square.png");
                            }
                            else if (group.Key == "OPEN TRENCHING" || group.Key == "T WAY")
                            {
                                styleId = "TeeStyle";
                                EnsureStyleExists(documentNode, styleId, "ff00ffff", 0.8, 0.0, "http://maps.google.com/mapfiles/kml/shapes/open-diamond.png");
                            }
                            else if (group.Key == "NEW HH 20X20X20")
                            {
                                styleId = "Hh20Style";
                                EnsureStyleExists(documentNode, styleId, "ff00ff00", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/square.png");
                            }
                            else
                            {
                                styleId = "Hh40Style";
                                EnsureStyleExists(documentNode, styleId, "ff0000ff", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/square.png");
                            }

                            XElement ext = CreateExtendedData(marker.Uuid, marker.Rotation, marker.TextHeight, marker.Layer, marker.LineAffiliation);
                            folder.Add(CreatePlacemark(marker.Name, "", $"#{styleId}", marker.Lon, marker.Lat, ext));
                        }
                        rootFolder.Add(folder);
                    }
                }
            }
            else
            {
                // ==========================================
                // CLUSTER MODE (STANDARD)
                // ==========================================
                XElement rootFolder = documentNode;

                // Add BOUNDARY CLUSTER folder under Root
                if (!objectOnly)
                {
                    XElement boundaryClusterFolder = new XElement(ns + "Folder", new XElement(ns + "name", "BOUNDARY CLUSTER"));
                    rootFolder.Add(boundaryClusterFolder);
                }

                // Add FDT folder under Root
                XElement fdtFolder = new XElement(ns + "Folder", new XElement(ns + "name", "FDT"));
                rootFolder.Add(fdtFolder);

                var fdtsList = (model.Fdts != null && model.Fdts.Count > 0) ? model.Fdts : new List<FdtNode> { model.Fdt };
                foreach (var fdt in fdtsList)
                {
                    if (fdt != null && !string.IsNullOrEmpty(fdt.CapacityText))
                    {
                        string cap = ExportStyleHelper.GetCapacityFromText(fdt.CapacityText);
                        string styleUrl = string.IsNullOrEmpty(cap) ? "#FdtStyleDefault" : $"#FdtStyle{cap}";
                        XElement ext = CreateExtendedData(fdt.Uuid, fdt.Rotation, fdt.TextHeight, fdt.Layer);
                        fdtFolder.Add(CreatePlacemark(fdt.Name, fdt.Description ?? "", styleUrl, fdt.Longitude, fdt.Latitude, ext));
                    }
                }

                // Process Line Folders
                foreach (var line in model.Lines)
                {
                    XElement lineFolder = new XElement(ns + "Folder", new XElement(ns + "name", line.LineName));

                    // Define standard cluster folders in order
                    var folders = new Dictionary<string, XElement>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        { "BOUNDARY FAT", new XElement(ns + "Folder", new XElement(ns + "name", "BOUNDARY FAT")) },
                        { "FAT", new XElement(ns + "Folder", new XElement(ns + "name", "FAT")) },
                        { "HP COVER", new XElement(ns + "Folder", new XElement(ns + "name", "HP COVER")) },
                        { "HP UNCOVER", new XElement(ns + "Folder", new XElement(ns + "name", "HP UNCOVER")) },
                        { "EXISTING POLE EMR 7-2.5", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 7-2.5")) },
                        { "EXISTING POLE EMR 7-3", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 7-3")) },
                        { "EXISTING POLE EMR 7-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 7-4")) },
                        { "EXISTING POLE EMR 9-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE EMR 9-4")) },
                        { "EXISTING POLE PARTNER 7-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE PARTNER 7-4")) },
                        { "EXISTING POLE PARTNER 9-4", new XElement(ns + "Folder", new XElement(ns + "name", "EXISTING POLE PARTNER 9-4")) },
                        { "NEW POLE 7-2.5", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 7-2.5")) },
                        { "NEW POLE 7-3", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 7-3")) },
                        { "NEW POLE 7-4", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 7-4")) },
                        { "NEW POLE 9-4", new XElement(ns + "Folder", new XElement(ns + "name", "NEW POLE 9-4")) },
                        { "DISTRIBUTION CABLE", new XElement(ns + "Folder", new XElement(ns + "name", "DISTRIBUTION CABLE")) },
                        { "SLACK HANGER", new XElement(ns + "Folder", new XElement(ns + "name", "SLACK HANGER")) },
                        { "SLING WIRE", new XElement(ns + "Folder", new XElement(ns + "name", "SLING WIRE")) }
                    };

                    // BOUNDARY FAT
                    foreach (var fat in line.Fats)
                    {
                        if (fat.BoundaryPoints.Count > 0)
                        {
                            XElement extBound = CreateExtendedData(fat.BoundaryUuid, 0, 0, fat.BoundaryLayer, line.LineName, fat.SequenceName);
                            folders["BOUNDARY FAT"].Add(CreatePolygon(fat.SequenceName, "#FatAreaStyle", fat.BoundaryPoints, extBound));
                        }
                    }

                    // FAT Node
                    foreach (var fat in line.Fats)
                    {
                        if (!string.IsNullOrEmpty(fat.RawText))
                        {
                            string color = ExportStyleHelper.GetFatColor(fat.RawText);
                            string styleId = $"FatStyle_{color}";
                            EnsureStyleExists(documentNode, styleId, color, 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/triangle.png");
                            XElement extFat = CreateExtendedData(fat.Uuid, fat.Rotation, fat.TextHeight, fat.Layer, line.LineName, fat.SequenceName);
                            folders["FAT"].Add(CreatePlacemark(fat.RawText, "", $"#{styleId}", fat.FatLon, fat.FatLat, extFat));
                        }
                    }

                    // HP Covers
                    foreach (var fat in line.Fats)
                    {
                        if (fat.HpCovers.Count > 0)
                        {
                            XElement subHpFolder = new XElement(ns + "Folder", new XElement(ns + "name", fat.SequenceName));
                            folders["HP COVER"].Add(subHpFolder);
                            foreach (var hp in fat.HpCovers)
                            {
                                XElement extHp = CreateExtendedData(hp.Uuid, hp.Rotation, hp.TextHeight, hp.Layer, hp.LineAffiliation, hp.SequenceName);
                                subHpFolder.Add(CreatePlacemark(hp.Name, "", "#HpCoverStyle", hp.Lon, hp.Lat, extHp));
                            }
                        }
                    }

                    // Poles
                    foreach (var pole in line.Poles)
                    {
                        string poleCat = pole.Category.ToUpper().Trim();
                        XElement targetFolder = null;

                        if (folders.ContainsKey(poleCat))
                        {
                            targetFolder = folders[poleCat];
                        }
                        else
                        {
                            string matchedKey = folders.Keys.FirstOrDefault(k => k.Equals(poleCat, System.StringComparison.OrdinalIgnoreCase));
                            if (matchedKey != null)
                            {
                                targetFolder = folders[matchedKey];
                            }
                            else
                            {
                                targetFolder = new XElement(ns + "Folder", new XElement(ns + "name", pole.Category));
                                folders[pole.Category] = targetFolder;
                            }
                        }

                        string color = ExportStyleHelper.GetPoleColor(pole.Category);
                        string styleId = $"PoleStyle_{color}";
                        EnsureStyleExists(documentNode, styleId, color, 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/placemark_circle.png");
                        XElement extPole = CreateExtendedData(pole.Uuid, pole.Rotation, pole.TextHeight, pole.Layer, pole.LineAffiliation);
                        targetFolder.Add(CreatePlacemark(pole.Name, "", $"#{styleId}", pole.Lon, pole.Lat, extPole));
                    }

                    // Distribution Cable
                    foreach (var cable in line.DistributionCables)
                    {
                        string color = ExportStyleHelper.GetCableColor(cable.Name);
                        string styleId = $"CableStyle_{color}";
                        EnsureLineStyleExists(documentNode, styleId, color, 3.0);
                        XElement extCable = CreateExtendedData(cable.Uuid, cable.Rotation, cable.TextHeight, cable.Layer, cable.LineAffiliation);
                        folders["DISTRIBUTION CABLE"].Add(CreateLineString(cable.Name, cable.Description, $"#{styleId}", cable.LinePoints, extCable));
                    }

                    // Slack Hanger
                    foreach (var slack in line.SlackHangers)
                    {
                        XElement extSlack = CreateExtendedData(slack.Uuid, slack.Rotation, slack.TextHeight, slack.Layer, slack.LineAffiliation);
                        folders["SLACK HANGER"].Add(CreatePlacemark(slack.Name, "", "#SlackStyle", slack.Lon, slack.Lat, extSlack));
                    }

                    // Sling Wire
                    foreach (var sling in line.SlingWires)
                    {
                        XElement extSling = CreateExtendedData(sling.Uuid, sling.Rotation, sling.TextHeight, sling.Layer, sling.LineAffiliation);
                        folders["SLING WIRE"].Add(CreateLineString(sling.Name, "", "#SlingStyle", sling.LinePoints, extSling));
                    }

                    // Closure
                    if (folders.ContainsKey("CLOSURE"))
                    {
                        foreach (var closure in line.Closures)
                        {
                            string color = ExportStyleHelper.GetClosureColor(closure.Name);
                            string styleId = $"ClosureStyle_{color}";
                            EnsureStyleExists(documentNode, styleId, color, 0.8, 1.0, "http://maps.google.com/mapfiles/kml/shapes/forbidden.png");
                            XElement extClosure = CreateExtendedData(closure.Uuid, closure.Rotation, closure.TextHeight, closure.Layer, closure.LineAffiliation);
                            folders["CLOSURE"].Add(CreatePlacemark(closure.Name, "", $"#{styleId}", closure.Lon, closure.Lat, extClosure));
                        }
                    }

                    // Add folders to lineFolder based on mode
                    bool lineHasObjects = false;
                    foreach (var kvp in folders)
                    {
                        XElement folder = kvp.Value;
                        bool hasObjects = folder.Elements().Any(e => e.Name != ns + "name");
                        if (hasObjects) lineHasObjects = true;

                        if (!objectOnly || hasObjects)
                        {
                            lineFolder.Add(folder);
                        }
                    }

                    // Append Custom Markers at the end of the Line folder
                    if (line.CustomMarkers != null && line.CustomMarkers.Count > 0)
                    {
                        lineHasObjects = true;
                        var customGroups = line.CustomMarkers.GroupBy(c => c.Category);
                        foreach (var group in customGroups)
                        {
                            XElement folder = new XElement(ns + "Folder", new XElement(ns + "name", group.Key));
                            foreach (var marker in group)
                            {
                                string styleId;
                                if (marker.Name.IndexOf("UG Pedestal", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    styleId = "UgPedestalStyle";
                                    EnsureStyleExists(documentNode, styleId, "ffffffff", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/paddle/grn-blank.png");
                                }
                                else if (marker.Name.IndexOf("HH20", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    styleId = "SmallHh20Style";
                                    EnsureStyleExists(documentNode, styleId, "ff00ff00", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/square.png");
                                }
                                else if (group.Key == "OPEN TRENCHING" || group.Key == "T WAY")
                                {
                                    styleId = "TeeStyle";
                                    EnsureStyleExists(documentNode, styleId, "ff00ffff", 0.8, 0.0, "http://maps.google.com/mapfiles/kml/shapes/open-diamond.png");
                                }
                                else if (group.Key == "NEW HH 20X20X20")
                                {
                                    styleId = "Hh20Style";
                                    EnsureStyleExists(documentNode, styleId, "ff00ff00", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/square.png");
                                }
                                else
                                {
                                    styleId = "Hh40Style";
                                    EnsureStyleExists(documentNode, styleId, "ff0000ff", 0.8, 0.8, "http://maps.google.com/mapfiles/kml/shapes/square.png");
                                }

                                XElement ext = CreateExtendedData(marker.Uuid, marker.Rotation, marker.TextHeight, marker.Layer, marker.LineAffiliation);
                                folder.Add(CreatePlacemark(marker.Name, "", $"#{styleId}", marker.Lon, marker.Lat, ext));
                            }
                            lineFolder.Add(folder);
                        }
                    }

                    // Add lineFolder to rootFolder if not objectOnly or if line has any objects
                    if (!objectOnly || lineHasObjects)
                    {
                        rootFolder.Add(lineFolder);
                    }
                }
            }
            doc.Save(filePath);
        }

        private void EnsureStyleExists(XElement docNode, string id, string color, double iconScale, double labelScale, string iconUrl)
        {
            if (!docNode.Elements(ns + "Style").Any(e => e.Attribute("id")?.Value == id))
            {
                CreateStyle(docNode, id, color, iconScale, labelScale, iconUrl);
            }
        }

        private void EnsureLineStyleExists(XElement docNode, string id, string color, double width)
        {
            if (!docNode.Elements(ns + "Style").Any(e => e.Attribute("id")?.Value == id))
            {
                CreateLineStyle(docNode, id, color, width);
            }
        }

        private void CreateStyle(XElement docNode, string id, string color, double iconScale, double labelScale, string iconUrl)
        {
            XElement style = new XElement(ns + "Style", new XAttribute("id", id),
                new XElement(ns + "IconStyle",
                    new XElement(ns + "color", color),
                    new XElement(ns + "scale", iconScale.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new XElement(ns + "Icon", new XElement(ns + "href", iconUrl))
                ),
                new XElement(ns + "LabelStyle",
                    new XElement(ns + "color", color),
                    new XElement(ns + "scale", labelScale.ToString(System.Globalization.CultureInfo.InvariantCulture))
                )
            );
            docNode.Add(style);
        }

        private void CreateLineStyle(XElement docNode, string id, string color, double width)
        {
            XElement style = new XElement(ns + "Style", new XAttribute("id", id),
                new XElement(ns + "LineStyle",
                    new XElement(ns + "color", color),
                    new XElement(ns + "width", width.ToString(System.Globalization.CultureInfo.InvariantCulture))
                )
            );
            docNode.Add(style);
        }

        private void CreatePolygonStyle(XElement docNode, string id, string lineColor, string fillColor, double width)
        {
            XElement style = new XElement(ns + "Style", new XAttribute("id", id),
                new XElement(ns + "LineStyle",
                    new XElement(ns + "color", lineColor),
                    new XElement(ns + "width", width.ToString(System.Globalization.CultureInfo.InvariantCulture))
                ),
                new XElement(ns + "PolyStyle",
                    new XElement(ns + "color", fillColor)
                )
            );
            docNode.Add(style);
        }

        private XElement CreateExtendedData(string uuid, double rotation, double textHeight, string layer, string lineAffiliation = "", string sequenceName = "")
        {
            XElement extData = new XElement(ns + "ExtendedData");
            if (!string.IsNullOrEmpty(uuid))
                extData.Add(new XElement(ns + "Data", new XAttribute("name", "AutoCAD_UUID"), new XElement(ns + "value", uuid)));
            extData.Add(new XElement(ns + "Data", new XAttribute("name", "AutoCAD_Layer"), new XElement(ns + "value", layer ?? "")));
            extData.Add(new XElement(ns + "Data", new XAttribute("name", "AutoCAD_Rotation"), new XElement(ns + "value", rotation.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            extData.Add(new XElement(ns + "Data", new XAttribute("name", "AutoCAD_TextHeight"), new XElement(ns + "value", textHeight.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            
            if (!string.IsNullOrEmpty(lineAffiliation))
                extData.Add(new XElement(ns + "Data", new XAttribute("name", "FTTH_Line"), new XElement(ns + "value", lineAffiliation)));
            if (!string.IsNullOrEmpty(sequenceName))
                extData.Add(new XElement(ns + "Data", new XAttribute("name", "FTTH_Sequence"), new XElement(ns + "value", sequenceName)));
            
            return extData;
        }

        private XElement CreatePlacemark(string name, string description, string styleUrl, double lon, double lat, XElement extendedData = null)
        {
            XElement p = new XElement(ns + "Placemark",
                new XElement(ns + "name", name),
                new XElement(ns + "styleUrl", styleUrl),
                new XElement(ns + "Point",
                    new XElement(ns + "coordinates", 
                        $"{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},0")
                )
            );
            if (!string.IsNullOrEmpty(description)) p.Add(new XElement(ns + "description", description));
            if (extendedData != null) p.Add(extendedData);
            return p;
        }

        private XElement CreateLineString(string name, string description, string styleUrl, List<(double Lon, double Lat)> points, XElement extendedData = null)
        {
            string coords = string.Join(" ", points.Select(p => 
                $"{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},0"));

            XElement placemark = new XElement(ns + "Placemark",
                new XElement(ns + "name", name),
                new XElement(ns + "styleUrl", styleUrl),
                new XElement(ns + "LineString",
                    new XElement(ns + "tessellate", "1"),
                    new XElement(ns + "coordinates", coords)
                )
            );
            if (!string.IsNullOrEmpty(description)) placemark.Add(new XElement(ns + "description", description));
            if (extendedData != null) placemark.Add(extendedData);
            return placemark;
        }

        private XElement CreatePolygon(string name, string styleUrl, List<(double Lon, double Lat)> points, XElement extendedData = null)
        {
            // Ensure polygon is closed (last point = first point)
            if (points.Count > 0)
            {
                var first = points.First();
                var last = points.Last();
                if (first.Lon != last.Lon || first.Lat != last.Lat)
                {
                    points.Add(first);
                }
            }

            string coords = string.Join(" ", points.Select(p => 
                $"{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},0"));

            XElement placemark = new XElement(ns + "Placemark",
                new XElement(ns + "name", name),
                new XElement(ns + "styleUrl", styleUrl),
                new XElement(ns + "Polygon",
                    new XElement(ns + "tessellate", "1"),
                    new XElement(ns + "outerBoundaryIs",
                        new XElement(ns + "LinearRing",
                            new XElement(ns + "coordinates", coords)
                        )
                    )
                )
            );
            if (extendedData != null) placemark.Add(extendedData);
            return placemark;
        }
    }
}
