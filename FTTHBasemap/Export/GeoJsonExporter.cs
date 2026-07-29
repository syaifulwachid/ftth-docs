using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FTTHBasemap.Export
{
    public class GeoJsonExporter
    {
        public void Generate(ExportModel model, string filePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"type\": \"FeatureCollection\",");
            sb.AppendLine("  \"features\": [");
            
            List<string> features = new List<string>();

            // FDT
            if (!string.IsNullOrEmpty(model.Fdt.CapacityText))
            {
                features.Add(CreatePointFeature(model.Fdt.Uuid, model.Fdt.Longitude, model.Fdt.Latitude, model.Fdt.CapacityText, "FDT", "", model.Fdt.Layer));
            }

            foreach (var line in model.Lines)
            {
                foreach (var fat in line.Fats)
                {
                    // Boundary
                    if (fat.BoundaryPoints.Count > 0)
                    {
                        features.Add(CreatePolygonFeature(fat.BoundaryUuid, fat.BoundaryPoints, fat.SequenceName, "BOUNDARY FAT", line.LineName, fat.BoundaryLayer));
                    }
                    
                    // FAT
                    if (!string.IsNullOrEmpty(fat.RawText))
                    {
                        features.Add(CreatePointFeature(fat.Uuid, fat.FatLon, fat.FatLat, fat.RawText, "FAT", line.LineName, fat.Layer, fat.SequenceName));
                    }

                    // HP Covers
                    foreach (var hp in fat.HpCovers)
                    {
                        features.Add(CreatePointFeature(hp.Uuid, hp.Lon, hp.Lat, hp.Name, "HP COVER", line.LineName, hp.Layer, fat.SequenceName));
                    }
                }

                foreach (var pole in line.Poles)
                    features.Add(CreatePointFeature(pole.Uuid, pole.Lon, pole.Lat, pole.Name, pole.Category, line.LineName, pole.Layer));
                
                foreach (var slack in line.SlackHangers)
                    features.Add(CreatePointFeature(slack.Uuid, slack.Lon, slack.Lat, slack.Name, "SLACK HANGER", line.LineName, slack.Layer));
                
                foreach (var closure in line.Closures)
                    features.Add(CreatePointFeature(closure.Uuid, closure.Lon, closure.Lat, closure.Name, "CLOSURE", line.LineName, closure.Layer));
                
                if (line.CustomMarkers != null)
                {
                    foreach (var marker in line.CustomMarkers)
                        features.Add(CreatePointFeature(marker.Uuid, marker.Lon, marker.Lat, marker.Name, marker.Category, line.LineName, marker.Layer));
                }
                
                foreach (var cable in line.DistributionCables)
                    features.Add(CreateLineStringFeature(cable.Uuid, cable.LinePoints, cable.Name, "DISTRIBUTION CABLE", line.LineName, cable.Layer));
                
                foreach (var sling in line.SlingWires)
                    features.Add(CreateLineStringFeature(sling.Uuid, sling.LinePoints, sling.Name, "SLING WIRE", line.LineName, sling.Layer));
            }

            sb.AppendLine(string.Join(",\n", features));
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(filePath, sb.ToString());
        }

        private string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private string CreatePointFeature(string uuid, double lon, double lat, string name, string category, string line, string layer, string sequence = "")
        {
            return $@"    {{
      ""type"": ""Feature"",
      ""geometry"": {{ ""type"": ""Point"", ""coordinates"": [{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}] }},
      ""properties"": {{ ""uuid"": ""{EscapeJson(uuid)}"", ""name"": ""{EscapeJson(name)}"", ""category"": ""{EscapeJson(category)}"", ""line"": ""{EscapeJson(line)}"", ""layer"": ""{EscapeJson(layer)}"", ""sequence"": ""{EscapeJson(sequence)}"" }}
    }}";
        }

        private string CreateLineStringFeature(string uuid, List<(double Lon, double Lat)> points, string name, string category, string line, string layer)
        {
            string coords = string.Join(", ", points.Select(p => $"[{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}]"));
            return $@"    {{
      ""type"": ""Feature"",
      ""geometry"": {{ ""type"": ""LineString"", ""coordinates"": [{coords}] }},
      ""properties"": {{ ""uuid"": ""{EscapeJson(uuid)}"", ""name"": ""{EscapeJson(name)}"", ""category"": ""{EscapeJson(category)}"", ""line"": ""{EscapeJson(line)}"", ""layer"": ""{EscapeJson(layer)}"" }}
    }}";
        }

        private string CreatePolygonFeature(string uuid, List<(double Lon, double Lat)> points, string name, string category, string line, string layer)
        {
            if (points.Count > 0)
            {
                var first = points.First();
                var last = points.Last();
                if (first.Lon != last.Lon || first.Lat != last.Lat)
                {
                    points.Add(first);
                }
            }
            string coords = string.Join(", ", points.Select(p => $"[{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}]"));
            return $@"    {{
      ""type"": ""Feature"",
      ""geometry"": {{ ""type"": ""Polygon"", ""coordinates"": [[{coords}]] }},
      ""properties"": {{ ""uuid"": ""{EscapeJson(uuid)}"", ""name"": ""{EscapeJson(name)}"", ""category"": ""{EscapeJson(category)}"", ""line"": ""{EscapeJson(line)}"", ""layer"": ""{EscapeJson(layer)}"" }}
    }}";
        }
    }
}
