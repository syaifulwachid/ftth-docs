using System;
using System.Text.RegularExpressions;

namespace FTTHBasemap.Export
{
    public static class ExportStyleHelper
    {
        // KML Color Format: AABBGGRR
        
        public static string GetFdtColor(string text)
        {
            if (text.Contains("48")) return "ffff00aa"; // #aa00ff
            if (text.Contains("72")) return "ff000055"; // #550000
            if (text.Contains("96")) return "ff0000ff"; // #ff0000
            if (text.Contains("144")) return "ff00ffff"; // #ffff00
            if (text.Contains("288")) return "ff00aaff"; // #ffaa00
            return "ffffffff"; // default white
        }

        public static string GetFatColor(string text)
        {
            if (text.Contains("48")) return "ffff00aa"; // #aa00ff
            if (text.Contains("32")) return "ffff00ff"; // #ff00ff
            if (text.Contains("24")) return "ff0000ff"; // #ff0000
            if (text.Contains("16")) return "ff00ffff"; // #ffff00
            if (text.Contains("8")) return "ff00ff00"; // #00ff00
            return "ff00ffff"; // default yellow (#ffff00)
        }

        public static string GetCableColor(string text)
        {
            if (text.Contains("12")) return "ffffaa00"; // #00aaff
            if (text.Contains("24")) return "ff00ff00"; // #00ff00
            if (text.Contains("36")) return "ffff00ff"; // #ff00ff
            if (text.Contains("48")) return "ffff00aa"; // #aa00ff
            if (text.Contains("72")) return "ff000055"; // #550000
            if (text.Contains("96")) return "ff0000ff"; // #ff0000
            if (text.Contains("144")) return "ff00ffff"; // #ffff00
            if (text.Contains("288")) return "ff00aaff"; // #ffaa00
            return "ffffffff";
        }
        
        public static string GetClosureColor(string text)
        {
            if (text.Contains("24")) return "ff00ff00"; // #00ff00
            if (text.Contains("36")) return "ffff00ff"; // #ff00ff
            if (text.Contains("48")) return "ffff00aa"; // #aa00ff
            if (text.Contains("72")) return "ff000055"; // #550000
            if (text.Contains("96")) return "ff0000ff"; // #ff0000
            if (text.Contains("144")) return "ff00ffff"; // #ffff00
            if (text.Contains("288")) return "ff00aaff"; // #ffaa00
            return "ffffffff";
        }

        public static string GetPoleColor(string poleCategory)
        {
            poleCategory = poleCategory.ToUpper();
            
            // New Poles
            if (poleCategory.Contains("NEW POLE 6-2.5") || poleCategory.Contains("NP 6 2.5") || poleCategory.Contains("NP625") || poleCategory.Contains("POLE 6M")) return "ff00ffff"; // #ffff00
            if (poleCategory.Contains("NEW POLE 7-2.5") || poleCategory.Contains("NP 7 2.5") || poleCategory.Contains("NP725")) return "ffff00aa"; // #aa00ff
            if (poleCategory.Contains("NEW POLE 7-3") || poleCategory.Contains("NP 7 3") || poleCategory.Contains("NP73")) return "ffffff00"; // #00ffff
            if (poleCategory.Contains("NEW POLE 7-4") || poleCategory.Contains("NP 7 4") || poleCategory.Contains("NP74")) return "ff00ff00"; // #00ff00
            if (poleCategory.Contains("NEW POLE 9-4") || poleCategory.Contains("NP 9 4") || poleCategory.Contains("NP94")) return "ff0000ff"; // #ff0000
            if (poleCategory.Contains("NEW POLE 12-12") || poleCategory.Contains("NP 12 12") || poleCategory.Contains("NP1212")) return "ff000055"; // #550000
            if (poleCategory.Contains("ULIN 8-3") || poleCategory.Contains("ULIN 8 3") || poleCategory.Contains("ULIN83") || poleCategory.Contains("ULIN")) return "ff0000ff"; // #0000ff
            
            // Existing Partner Poles
            if (poleCategory.Contains("PARTNER") || poleCategory.Contains("TEL") || poleCategory.Contains("MTI") || poleCategory.Contains("LINK") || poleCategory.Contains("PLN")) return "ffffffff"; // #ffffff
            
            // Existing EMR Poles (default for other existing EMR)
            if (poleCategory.Contains("EXISTING") || poleCategory.Contains("EP") || poleCategory.Contains("EXT")) return "ffffffff"; // #ffffff
            
            return "ffffffff"; 
        }

        public static string GetCapacityFromText(string text)
        {
            var match = Regex.Match(text, @"(8|16|24|32|36|48|72|96|144|288)");
            return match.Success ? match.Value : "";
        }
    }
}
