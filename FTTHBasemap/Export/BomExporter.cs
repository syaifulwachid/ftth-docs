using System.IO;
using System.Linq;
using System.Text;

namespace FTTHBasemap.Export
{
    public class BomExporter
    {
        public void Generate(ExportModel model, string filePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("FTTH BILL OF MATERIALS (BOM)");
            sb.AppendLine($"FDT Name,{model.Fdt.Name}");
            sb.AppendLine($"Capacity,{model.Fdt.CapacityText}");
            sb.AppendLine();

            int totalHpCovers = 0;
            double totalCableLength = 0;
            double totalSlingLength = 0;
            int grandTotalFats = 0;
            System.Collections.Generic.Dictionary<string, int> grandTotalPoles = new System.Collections.Generic.Dictionary<string, int>();
            System.Collections.Generic.Dictionary<string, int> grandTotalCustom = new System.Collections.Generic.Dictionary<string, int>();

            foreach (var line in model.Lines)
            {
                sb.AppendLine($"--- {line.LineName} ---");
                sb.AppendLine("Category,Item Name,Quantity / Length (m)");

                int lineHpCovers = 0;
                foreach (var fat in line.Fats)
                {
                    sb.AppendLine($"FAT,FAT {fat.SequenceName} ({fat.RawText}),1");
                    sb.AppendLine($"HP Cover,HP Covers at {fat.SequenceName},{fat.HpCovers.Count}");
                    lineHpCovers += fat.HpCovers.Count;
                }
                grandTotalFats += line.Fats.Count;
                sb.AppendLine($",Total FAT on {line.LineName},{line.Fats.Count}");
                totalHpCovers += lineHpCovers;
                sb.AppendLine($",Total HP Covers on {line.LineName},{lineHpCovers}");
                sb.AppendLine();

                int lineTotalPoles = 0;
                var poleGroups = line.Poles.GroupBy(p => p.Category);
                foreach (var g in poleGroups)
                {
                    int count = g.Count();
                    sb.AppendLine($"Pole,{g.Key},{count}");
                    lineTotalPoles += count;
                    
                    if (grandTotalPoles.ContainsKey(g.Key))
                        grandTotalPoles[g.Key] += count;
                    else
                        grandTotalPoles[g.Key] = count;
                }
                sb.AppendLine($",Total Poles on {line.LineName},{lineTotalPoles}");
                sb.AppendLine();

                double lineCableLength = 0;
                foreach (var cable in line.DistributionCables)
                {
                    double length = ExtractLength(cable.Name);
                    lineCableLength += length;
                    sb.AppendLine($"Distribution Cable,{cable.Name},{length}");
                }
                totalCableLength += lineCableLength;
                sb.AppendLine($",Total Cable Length on {line.LineName},{lineCableLength}");
                sb.AppendLine();

                double lineSlingLength = 0;
                foreach (var sling in line.SlingWires)
                {
                    double length = ExtractLength(sling.Name);
                    lineSlingLength += length;
                    sb.AppendLine($"Sling Wire,{sling.Name},{length}");
                }
                totalSlingLength += lineSlingLength;
                sb.AppendLine($",Total Sling Wire Length on {line.LineName},{lineSlingLength}");
                sb.AppendLine();

                sb.AppendLine($"Slack Hanger,Slack Hangers,{line.SlackHangers.Count}");
                sb.AppendLine($"Closure,Closures,{line.Closures.Count}");
                
                if (line.CustomMarkers != null && line.CustomMarkers.Count > 0)
                {
                    var customGroups = line.CustomMarkers.GroupBy(c => c.Category);
                    foreach (var g in customGroups)
                    {
                        int count = g.Count();
                        sb.AppendLine($"{g.Key},{g.Key},{count}");
                        if (grandTotalCustom.ContainsKey(g.Key))
                            grandTotalCustom[g.Key] += count;
                        else
                            grandTotalCustom[g.Key] = count;
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("--- GRAND TOTAL ---");
            sb.AppendLine($"Total FATs,,{grandTotalFats}");
            sb.AppendLine($"Total HP Covers,,{totalHpCovers}");
            sb.AppendLine($"Total Distribution Cable (m),,{totalCableLength}");
            sb.AppendLine($"Total Sling Wire (m),,{totalSlingLength}");
            
            foreach (var kvp in grandTotalPoles)
            {
                sb.AppendLine($"Total Pole: {kvp.Key},,{kvp.Value}");
            }

            foreach (var kvp in grandTotalCustom)
            {
                sb.AppendLine($"Total {kvp.Key},,{kvp.Value}");
            }

            File.WriteAllText(filePath, sb.ToString());
        }

        private double ExtractLength(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+(\.\d+)?) m");
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    return val;
                }
            }
            return 0;
        }
    }
}
