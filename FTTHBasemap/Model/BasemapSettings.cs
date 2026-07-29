using System;
using System.IO;
using System.Xml.Serialization;

namespace FTTHBasemap.Model
{
    public class BasemapSettings
    {
        // Generation Toggles
        public bool GenerateRoad { get; set; } = true;
        public bool GenerateSidewalk { get; set; } = true;
        public bool GenerateRow { get; set; } = true;

        // KML Export Settings
        public bool AutoOpenKml { get; set; } = true;
        public double KmlLabelSearchTol { get; set; } = 9.0;
        public bool KmlSortHomepass { get; set; } = true;

        // Smart Polyline & Smoothing Settings
        public double SmartPlineMaxDist { get; set; } = 5.0;
        public int SmoothIterations { get; set; } = 2;
        public double RoadLabelDist { get; set; } = 30.0;
        public string PlacerPrefix { get; set; } = "NN-";
        public int PlacerStart { get; set; } = 1;

        // Auto-Generate Poles Settings
        public string AutoPoleType { get; set; } = "NP 7 2.5";
        public double AutoPoleInterval { get; set; } = 35.0;
        public double AutoPoleAvoidDist { get; set; } = 10.0;
        public double AutoPoleStartOffset { get; set; } = 5.0;
        public string AutoPoleSide { get; set; } = "LEFT"; // "LEFT" or "RIGHT"

        // Dimensions (in meters)
        public double RoadWidth { get; set; } = 6.0;
        public double SidewalkWidth { get; set; } = 1.0;
        public double RowWidth { get; set; } = 17.0;

        // Fillet Radii (in meters)
        public double FilletRoadRadius { get; set; } = 1.0;
        public double FilletSidewalkRadius { get; set; } = 0.5;
        public double FilletRowRadius { get; set; } = 0.5;

        // Gap Checker Settings
        public double GapTolerance { get; set; } = 1.0;

        // Row Generation Mode
        public string RowMode { get; set; } = "MIDPOINT"; // "MIDPOINT" or "MAXFIT"

        // Layer Names
        public string LayerCenterline { get; set; } = "FTTH-CENTERLINE";
        public string LayerRoadEdge { get; set; } = "FTTH-ROAD-EDGE";
        public string LayerSidewalk { get; set; } = "FTTH-TROTOAR";
        public string LayerRow { get; set; } = "FTTH-ROW";
        public string LayerPreview { get; set; } = "FTTH-PREVIEW";
        public string LayerBreakmark { get; set; } = "FTTH-BREAKMARK";
        public string LayerParcel { get; set; } = "FTTH-PERCIL";
        public string LayerParcelPoint { get; set; } = "FTTH-PERCIL-POINT";
        public string LayerRoadLabel { get; set; } = "Nama Jalan";
        public string LayerFatArea { get; set; } = "FAT AREA";

        // Layer Colors (AutoCAD Color Index)
        public short ColorCenterline { get; set; } = 4; // Cyan
        public short ColorRoadEdge { get; set; } = 7;   // White
        public short ColorSidewalk { get; set; } = 3;   // Green
        public short ColorRow { get; set; } = 1;        // Red
        public short ColorPreview { get; set; } = 2;    // Yellow
        public short ColorBreakmark { get; set; } = 1;  // Red
        public short ColorParcel { get; set; } = 30;    // Orange
        public short ColorParcelPoint { get; set; } = 2; // Yellow
        public short ColorRoadLabel { get; set; } = 3;  // Green
        public short ColorFatArea { get; set; } = 106; // Pinkish Red

        // Licensing Properties
        public string LicenseKey { get; set; } = "";
        public string LastUsedSalt { get; set; } = "";
        public string OfflineGraceTimestamp { get; set; } = "";
        public string PendingSalt { get; set; } = "";

        // Singleton Instance for active session
        private static BasemapSettings _instance;
        public static BasemapSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        private static string GetConfigFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "FTTHBasemap");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, "settings.xml");
        }

        public void Save()
        {
            try
            {
                string path = GetConfigFilePath();
                XmlSerializer serializer = new XmlSerializer(typeof(BasemapSettings));
                using (StreamWriter writer = new StreamWriter(path))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch
            {
                // Silently ignore configuration save errors
            }
        }

        public BasemapSettings()
        {
            InitializeDefaultOltCodes();
        }

        public void InitializeDefaultOltCodes()
        {
            if (OltCodes == null) OltCodes = new System.Collections.Generic.List<OltEntry>();
            if (OltCodes.Count == 0)
            {
                OltCodes.Add(new OltEntry("MLG.100.0201", "OLT Smart Malang"));
                OltCodes.Add(new OltEntry("MLG.100.0202", "OLT Smart Malang 2"));
                OltCodes.Add(new OltEntry("MLG.100.0203", "OLT Smart Malang 3"));
                OltCodes.Add(new OltEntry("MLG.100.0204", "OLT Smart Malang 4"));
                OltCodes.Add(new OltEntry("MLG.100.0205", "OLT Smart Malang 5"));
                OltCodes.Add(new OltEntry("MLG.100.0206", "OLT Smart Malang 6"));
                OltCodes.Add(new OltEntry("MLG.100.0207", "OLT Smart Malang 7"));
                OltCodes.Add(new OltEntry("MLG.100.0401", "OLT KEDUNG KANDANG"));
                OltCodes.Add(new OltEntry("MLG.100.0402", "OLT KEDUNG KANDANG 2"));
                OltCodes.Add(new OltEntry("MLG.100.0403", "OLT KEDUNG KANDANG 3"));
                OltCodes.Add(new OltEntry("MLG.100.0404", "OLT KEDUNG KANDANG 4"));
                OltCodes.Add(new OltEntry("MLG.100.0405", "OLT KEDUNG KANDANG 5"));
                OltCodes.Add(new OltEntry("MLG.100.0406", "OLT KEDUNG KANDANG 6"));
                OltCodes.Add(new OltEntry("MLG.100.0501", "OLT SUKUN"));
                OltCodes.Add(new OltEntry("MLG.100.0502", "OLT SUKUN 2"));
                OltCodes.Add(new OltEntry("MLG.100.0601", "OLT BATU"));
                OltCodes.Add(new OltEntry("MLG.100.0701", "OLT BURING"));
                OltCodes.Add(new OltEntry("MLG.100.0702", "OLT BURING 2"));
                OltCodes.Add(new OltEntry("MLG.100.0801", "OLT SINGOSARI"));
                OltCodes.Add(new OltEntry("MLG.100.0802", "OLT SINGOSARI 2"));
                OltCodes.Add(new OltEntry("MLG.100.0901", "OLT KEPANJEN"));
                OltCodes.Add(new OltEntry("MLG.100.0902", "OLT KEPANJEN 2"));
                OltCodes.Add(new OltEntry("MLG.100.1001", "OLT PAKIS"));
                OltCodes.Add(new OltEntry("MLG.100.1101", "OLT GONDANGLEGI"));
                OltCodes.Add(new OltEntry("MLG.100.1201", "OLT WAJAK"));
                OltCodes.Add(new OltEntry("MLG.100.1301", "OLT PESANGGRAHAN BATU"));
            }
        }

        public static BasemapSettings Load()
        {
            try
            {
                string path = GetConfigFilePath();
                if (File.Exists(path))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(BasemapSettings));
                    using (StreamReader reader = new StreamReader(path))
                    {
                        var settings = (BasemapSettings)serializer.Deserialize(reader);
                        if (settings.OltCodes == null || settings.OltCodes.Count == 0)
                        {
                            settings.InitializeDefaultOltCodes();
                        }
                        return settings;
                    }
                }
            }
            catch
            {
                // Silently fall back to default settings
            }
            return new BasemapSettings();
        }

        // Card Collapse/Expand states
        public System.Collections.Generic.List<StringBoolEntry> CollapsedCardStates { get; set; } = new System.Collections.Generic.List<StringBoolEntry>();

        public System.Collections.Generic.List<OltEntry> OltCodes { get; set; } = new System.Collections.Generic.List<OltEntry>();

        [XmlIgnore]
        private System.Collections.Generic.Dictionary<string, bool> _collapsedCardsDict = null;

        [XmlIgnore]
        public System.Collections.Generic.Dictionary<string, bool> CollapsedCards
        {
            get
            {
                if (_collapsedCardsDict == null)
                {
                    _collapsedCardsDict = new System.Collections.Generic.Dictionary<string, bool>();
                    if (CollapsedCardStates != null)
                    {
                        foreach (var entry in CollapsedCardStates)
                        {
                            if (!string.IsNullOrEmpty(entry.Key))
                            {
                                _collapsedCardsDict[entry.Key] = entry.Value;
                            }
                        }
                    }
                }
                return _collapsedCardsDict;
            }
        }

        public void SetCardCollapsed(string key, bool collapsed)
        {
            CollapsedCards[key] = collapsed;
            CollapsedCardStates.Clear();
            foreach (var kvp in CollapsedCards)
            {
                CollapsedCardStates.Add(new StringBoolEntry(kvp.Key, kvp.Value));
            }
            Save();
        }

        public bool IsCardCollapsed(string key, bool defaultValue = false)
        {
            if (CollapsedCards.TryGetValue(key, out bool val))
            {
                return val;
            }
            return defaultValue;
        }
    }

    public class StringBoolEntry
    {
        public string Key { get; set; }
        public bool Value { get; set; }

        public StringBoolEntry() { }
        public StringBoolEntry(string key, bool value)
        {
            Key = key;
            Value = value;
        }
    }

    public class OltEntry
    {
        public string Code { get; set; }
        public string Description { get; set; }

        public OltEntry() { }
        public OltEntry(string code, string desc)
        {
            Code = code;
            Description = desc;
        }
    }
}
