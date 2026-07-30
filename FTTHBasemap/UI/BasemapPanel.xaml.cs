using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using FTTHBasemap.Model;
using FTTHBasemap.Export;

namespace FTTHBasemap.UI
{
    public partial class BasemapPanel : UserControl
    {
        private bool _isInitializing = true;
        private bool _isUpdatingFdtCombo = false;
        private List<List<ObjectId>> _placedSteps = new List<List<ObjectId>>();

        private static readonly HashSet<string> PlacerIsolatedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FTTH-ROW",
            "FTTH-PERCIL",
            "FTTH-TROTOAR",
            "FTTH-ROAD-EDGE",
            "FTTH-NOMOR-RUMAH"
        };
        private static Dictionary<string, bool> _previousLayerStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static ObjectId _previousActiveLayerId = ObjectId.Null;
        private static bool _isPlacerIsolated = false;

        // APD/ABD Exporter Model
        public ExportModel ApdModel { get; private set; } = new ExportModel();

        // Auto Route Cable Generator State
        private Point3d _selectedAutoFdtPoint = Point3d.Origin;
        private string _selectedAutoFdtHandle = string.Empty;
        private List<RouteCableGenerator.HomeCluster> _activeClusters = new List<RouteCableGenerator.HomeCluster>();

        public BasemapPanel()
        {
            InitializeComponent();
            LoadSettingsToUI();
            InitializeApdTab();
            _isInitializing = false;

            // Synchronize TxtLabelFatPrefix and TxtLabelFdtPrefix bidirectionally
            if (TxtLabelFatPrefix != null && TxtLabelFdtPrefix != null)
            {
                TxtLabelFdtPrefix.Text = TxtLabelFatPrefix.Text;
                TxtLabelFatPrefix.TextChanged += (s, e) =>
                {
                    if (TxtLabelFdtPrefix.Text != TxtLabelFatPrefix.Text)
                    {
                        TxtLabelFdtPrefix.Text = TxtLabelFatPrefix.Text;
                    }
                };
                TxtLabelFdtPrefix.TextChanged += (s, e) =>
                {
                    if (TxtLabelFatPrefix.Text != TxtLabelFdtPrefix.Text)
                    {
                        TxtLabelFatPrefix.Text = TxtLabelFdtPrefix.Text;
                    }
                };
            }

            this.Loaded += BasemapPanel_Loaded;
            this.Unloaded += (s, e) => CameraIndicatorManager.ClearIndicator();

            // Register document activation event to update geolocation status dynamically
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentActivated += (s, e) =>
                {
                    if (e.Document != null)
                    {
                        UpdateGeolocationStatus();
                        try { RefreshLayoutList(); } catch { }
                        try { PopulateAlignBlocksOrLayers(); } catch { }
                        try { AutoDetectMajorityPrefixes(); } catch { }
                    }
                };
            }
            catch { }
        }

        private void LoadSettingsToUI()
        {
            var settings = BasemapSettings.Instance;

            // Checkboxes
            ChkRoad.IsChecked = settings.GenerateRoad;
            ChkSidewalk.IsChecked = settings.GenerateSidewalk;
            ChkRow.IsChecked = settings.GenerateRow;

            // KML Checkboxes
            if (ChkAutoOpenKmlSndKasar != null) ChkAutoOpenKmlSndKasar.IsChecked = settings.AutoOpenKml;
            if (ChkAutoOpenKmlApd != null) ChkAutoOpenKmlApd.IsChecked = settings.AutoOpenKml;
            if (ChkAutoOpenKmlAuto != null) ChkAutoOpenKmlAuto.IsChecked = settings.AutoOpenKml;
            if (ChkKmlSortHomepass != null) ChkKmlSortHomepass.IsChecked = settings.KmlSortHomepass;
            if (SldKmlLabelSearchTol != null) SldKmlLabelSearchTol.Value = settings.KmlLabelSearchTol;
            if (TxtKmlLabelSearchTol != null) TxtKmlLabelSearchTol.Text = settings.KmlLabelSearchTol.ToString("F1");

            // Widths
            SldRoadWidth.Value = settings.RoadWidth;
            TxtRoadWidth.Text = settings.RoadWidth.ToString("F1");
            SldSidewalkWidth.Value = settings.SidewalkWidth;
            TxtSidewalkWidth.Text = settings.SidewalkWidth.ToString("F1");
            SldRowWidth.Value = settings.RowWidth;
            TxtRowWidth.Text = settings.RowWidth.ToString("F1");

            // Fillets
            SldRoadFillet.Value = settings.FilletRoadRadius;
            TxtRoadFillet.Text = settings.FilletRoadRadius.ToString("F1");
            SldSidewalkFillet.Value = settings.FilletSidewalkRadius;
            TxtSidewalkFillet.Text = settings.FilletSidewalkRadius.ToString("F1");
            SldRowFillet.Value = settings.FilletRowRadius;
            TxtRowFillet.Text = settings.FilletRowRadius.ToString("F1");

            // Smart Polyline & Smoothing
            SldPlineMax.Value = settings.SmartPlineMaxDist;
            TxtPlineMax.Text = settings.SmartPlineMaxDist.ToString("F1");
            SldSmoothIter.Value = settings.SmoothIterations;
            TxtSmoothIter.Text = settings.SmoothIterations.ToString();

            // Placer
            TxtPlacerPrefix.Text = settings.PlacerPrefix;
            TxtPlacerStart.Text = settings.PlacerStart.ToString();
            MasterPlacer.TextPrefix = settings.PlacerPrefix;
            MasterPlacer.StartIndex = settings.PlacerStart;

            // Road Labeling
            SldRoadLabelDist.Value = settings.RoadLabelDist;
            TxtRoadLabelDist.Text = settings.RoadLabelDist.ToString("F1");

            // Gap Checker
            if (SldGapTolerance != null) SldGapTolerance.Value = settings.GapTolerance;
            if (TxtGapTolerance != null) TxtGapTolerance.Text = settings.GapTolerance.ToString("F2");

            // Auto-Generate Poles Settings
            if (CmbAutoPoleType != null)
            {
                foreach (ComboBoxItem item in CmbAutoPoleType.Items)
                {
                    if (item.Content.ToString() == settings.AutoPoleType)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }
            if (TxtAutoPoleInterval != null) TxtAutoPoleInterval.Text = settings.AutoPoleInterval.ToString("F1");
            if (TxtAutoPoleAvoidDist != null) TxtAutoPoleAvoidDist.Text = settings.AutoPoleAvoidDist.ToString("F1");
            if (TxtAutoPoleStartOffset != null) TxtAutoPoleStartOffset.Text = settings.AutoPoleStartOffset.ToString("F1");
            if (CmbAutoPoleSide != null)
            {
                foreach (ComboBoxItem item in CmbAutoPoleSide.Items)
                {
                    if (item.Tag != null && item.Tag.ToString() == settings.AutoPoleSide)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
            }

            if (CmbLabelFdtOltCode != null && settings.OltCodes != null)
            {
                CmbLabelFdtOltCode.Items.Clear();
                foreach (var entry in settings.OltCodes)
                {
                    CmbLabelFdtOltCode.Items.Add($"{entry.Code} - {entry.Description}");
                }
                if (CmbLabelFdtOltCode.Items.Count > 0)
                {
                    CmbLabelFdtOltCode.SelectedIndex = 0;
                }
            }

            // Auto-extract cluster name from active DWG name
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    string activeDwgName = System.IO.Path.GetFileNameWithoutExtension(doc.Name);
                    if (!string.IsNullOrEmpty(activeDwgName) && activeDwgName.IndexOf("Drawing", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        string defaultCluster = activeDwgName.Replace('_', ' ').Replace('-', ' ').ToUpper();
                        if (TxtSummaryClusterName != null) TxtSummaryClusterName.Text = defaultCluster;
                        if (TxtAutoFdtName != null) TxtAutoFdtName.Text = defaultCluster;
                    }
                }
            }
            catch { }

            UpdatePanelsVisibility();
            UpdateGeolocationStatus();
        }

        private void UpdateGeolocationStatus()
        {
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                Database db = doc.Database;
                TxtXRef.IsEnabled = true;
                TxtYRef.IsEnabled = true;
                TxtLatRef.IsEnabled = true;
                TxtLonRef.IsEnabled = true;

                if (TileMapManager.IsDrawingGeoreferenced(db, out string crsName))
                {
                    TxtGeoStatus.Text = $"Active (Native: {crsName})";
                    TxtGeoStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGreen);
                }
                else
                {
                    TxtGeoStatus.Text = "Manual Calibration Required";
                    TxtGeoStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F38BA8"));
                }
                UpdateCalibrationShiftDisplay();
            }
            catch (Exception ex)
            {
                TxtGeoStatus.Text = "Error Checking Georeference";
                System.Diagnostics.Debug.WriteLine("Georeference check error: " + ex.Message);
            }
        }

        private void UpdateCalibrationShiftDisplay()
        {
            if (TxtCalibrationShift == null) return;

            string txtX = TxtXRef.Text;
            string txtY = TxtYRef.Text;
            string txtLat = TxtLatRef.Text;
            string txtLon = TxtLonRef.Text;

            if (string.IsNullOrEmpty(txtX) || string.IsNullOrEmpty(txtY) ||
                string.IsNullOrEmpty(txtLat) || string.IsNullOrEmpty(txtLon))
            {
                TxtCalibrationShift.Text = "Shift: ΔLat = 0.000000°, ΔLon = 0.000000°";
                return;
            }

            if (!double.TryParse(txtX, out double xRef) ||
                !double.TryParse(txtY, out double yRef) ||
                !double.TryParse(txtLat, out double latRef) ||
                !double.TryParse(txtLon, out double lonRef))
            {
                TxtCalibrationShift.Text = "Shift: Invalid acuan values";
                return;
            }

            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    Database db = doc.Database;
                    if (TileMapManager.IsDrawingGeoreferenced(db, out _))
                    {
                        Point3d rawRefProj = TileMapManager.TransformWcsToLonLat(db, new Point3d(xRef, yRef, 0.0));
                        double deltaLat = latRef - rawRefProj.Y;
                        double deltaLon = lonRef - rawRefProj.X;
                        TxtCalibrationShift.Text = $"Shift: ΔLat = {deltaLat:+#.000000;-#.000000;0.000000}°, ΔLon = {deltaLon:+#.000000;-#.000000;0.000000}°";
                        return;
                    }
                }
            }
            catch { }

            TxtCalibrationShift.Text = "Shift: Manual projection mode active";
        }

        private void TxtCalibration_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCalibrationShiftDisplay();
        }

        private void ChkAutoOpenKml_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            var settings = BasemapSettings.Instance;
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                settings.AutoOpenKml = isChecked;
                
                // Keep checkboxes in sync
                if (ChkAutoOpenKmlSndKasar != null && ChkAutoOpenKmlSndKasar.IsChecked != isChecked)
                    ChkAutoOpenKmlSndKasar.IsChecked = isChecked;
                if (ChkAutoOpenKmlApd != null && ChkAutoOpenKmlApd.IsChecked != isChecked)
                    ChkAutoOpenKmlApd.IsChecked = isChecked;
                if (ChkAutoOpenKmlAuto != null && ChkAutoOpenKmlAuto.IsChecked != isChecked)
                    ChkAutoOpenKmlAuto.IsChecked = isChecked;

                settings.Save();
            }
        }

        private void SaveSettingsFromUI()
        {
            if (_isInitializing) return;

            var settings = BasemapSettings.Instance;

            // Checkboxes
            settings.GenerateRoad = ChkRoad.IsChecked ?? false;
            settings.GenerateSidewalk = ChkSidewalk.IsChecked ?? false;
            settings.GenerateRow = ChkRow.IsChecked ?? false;
            settings.AutoOpenKml = ChkAutoOpenKmlSndKasar?.IsChecked ?? true;
            if (ChkKmlSortHomepass != null) settings.KmlSortHomepass = ChkKmlSortHomepass.IsChecked ?? true;
            if (TxtKmlLabelSearchTol != null && double.TryParse(TxtKmlLabelSearchTol.Text, out double kLst)) settings.KmlLabelSearchTol = kLst;

            // Widths
            if (double.TryParse(TxtRoadWidth.Text, out double rw)) settings.RoadWidth = rw;
            if (double.TryParse(TxtSidewalkWidth.Text, out double sw)) settings.SidewalkWidth = sw;
            if (double.TryParse(TxtRowWidth.Text, out double rowW)) settings.RowWidth = rowW;

            // Fillets
            if (double.TryParse(TxtRoadFillet.Text, out double rf)) settings.FilletRoadRadius = rf;
            if (double.TryParse(TxtSidewalkFillet.Text, out double sf)) settings.FilletSidewalkRadius = sf;
            if (double.TryParse(TxtRowFillet.Text, out double rowF)) settings.FilletRowRadius = rowF;

            // Smart Polyline & Smoothing
            if (double.TryParse(TxtPlineMax.Text, out double pm)) settings.SmartPlineMaxDist = pm;
            if (int.TryParse(TxtSmoothIter.Text, out int si)) settings.SmoothIterations = si;

            // Placer
            settings.PlacerPrefix = TxtPlacerPrefix.Text;
            if (int.TryParse(TxtPlacerStart.Text, out int ps)) settings.PlacerStart = ps;
            MasterPlacer.TextPrefix = settings.PlacerPrefix;
            MasterPlacer.StartIndex = settings.PlacerStart;

            // Road Labeling
            if (double.TryParse(TxtRoadLabelDist.Text, out double rd)) settings.RoadLabelDist = rd;

            // Gap Checker
            if (TxtGapTolerance != null && double.TryParse(TxtGapTolerance.Text, out double gt)) settings.GapTolerance = gt;

            // Auto-Generate Poles Settings
            if (CmbAutoPoleType != null && CmbAutoPoleType.SelectedItem != null)
            {
                settings.AutoPoleType = (CmbAutoPoleType.SelectedItem as ComboBoxItem).Content.ToString();
            }
            if (TxtAutoPoleInterval != null && double.TryParse(TxtAutoPoleInterval.Text, out double api))
            {
                settings.AutoPoleInterval = api;
            }
            if (TxtAutoPoleAvoidDist != null && double.TryParse(TxtAutoPoleAvoidDist.Text, out double apad))
            {
                settings.AutoPoleAvoidDist = apad;
            }
            if (TxtAutoPoleStartOffset != null && double.TryParse(TxtAutoPoleStartOffset.Text, out double apso))
            {
                settings.AutoPoleStartOffset = apso;
            }
            if (CmbAutoPoleSide != null && CmbAutoPoleSide.SelectedItem != null)
            {
                settings.AutoPoleSide = (CmbAutoPoleSide.SelectedItem as ComboBoxItem).Tag.ToString();
            }

            settings.Save();
        }

        private void UpdatePanelsVisibility()
        {
            if (PanRoadSettings != null && ChkRoad != null)
                PanRoadSettings.IsEnabled = ChkRoad.IsChecked ?? false;
            if (PanSidewalkSettings != null && ChkSidewalk != null)
                PanSidewalkSettings.IsEnabled = ChkSidewalk.IsChecked ?? false;
            if (PanRowSettings != null && ChkRow != null)
                PanRowSettings.IsEnabled = ChkRow.IsChecked ?? false;
        }

        public void LogMessage(string msg)
        {
            TxtStatus.Text = msg;
        }

        private void SendCommandToAutoCAD(string cmdName)
        {
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    // Prepend ESC sequence (\x03\x03) to cancel any active command (e.g. interactive selection loop)
                    doc.SendStringToExecute("\x03\x03" + cmdName + "\n", true, false, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error running command: {ex.Message}", "FTTH Basemap");
            }
        }

        // --- BUTTON EVENTS ---

        private void BtnAutoGeneratePoles_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            SendCommandToAutoCAD("FTTH_AUTO_GENERATE_POLES");
        }

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var infoWin = new InfoWindow();
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(infoWin);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing Info: {ex.Message}", "FTTH Basemap");
            }
        }

        private void BtnTagWidth_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            SendCommandToAutoCAD("FTTH_SETWIDTH");
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            SendCommandToAutoCAD("FTTH_GENERATE");
        }

        private void BtnCleanIntersect_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            SendCommandToAutoCAD("FTTH_INTERSECT");
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_RESET");
        }

        private void BtnClearPreviews_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_CLEARPREVIEWS");
        }

        private void BtnFreezeCL_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_FREEZE_CL");
        }

        private void BtnThawCL_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_THAW_CL");
        }

        private void BtnCreatePoint_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_CREATE_POINT");
        }

        private void BtnImportPoint_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_IMPORT_POINT");
        }

        private void BtnGenerateParcels_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GENERATE_PARCELS");
        }

        private void BtnPlaceAssetManual_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (tag.StartsWith("EXT") || tag.StartsWith("NP"))
                {
                    string poleType = tag;
                    string jsonFile = GetPoleJsonFilename(poleType);
                    string targetLayer = poleType.StartsWith("EXT") ? "FTTH-POLE-EXISTING" : GetNewPoleLayer(poleType);
                    short colorIndex = poleType.StartsWith("EXT") ? (short)8 : GetNewPoleColor(poleType);

                    AssetPlacer.ActiveAssetFile = jsonFile;
                    AssetPlacer.ActiveAssetLayer = targetLayer;
                    AssetPlacer.ActiveAssetColor = colorIndex;
                    AssetPlacer.ActiveAssetName = poleType;
                }
                else
                {
                    string jsonFile = tag;
                    string targetLayer = "";
                    short colorIndex = 256; // ByLayer

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

                    AssetPlacer.ActiveAssetFile = jsonFile;
                    AssetPlacer.ActiveAssetLayer = targetLayer;
                    AssetPlacer.ActiveAssetColor = colorIndex;
                    AssetPlacer.ActiveAssetName = jsonFile.Replace(".JSON", "");
                }

                SendCommandToAutoCAD("FTTH_PLACE_MANUAL_ASSET");
            }
        }

        private void BtnCheckGaps_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            SendCommandToAutoCAD("FTTH_CHECKGAPS");
        }

        private void BtnFixGaps_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            SendCommandToAutoCAD("FTTH_FIXGAPS");
        }

        private void BtnClearGaps_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_CLEARGAPS");
        }

        private void SldGapTolerance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtGapTolerance != null) TxtGapTolerance.Text = e.NewValue.ToString("F2");
            SaveSettingsFromUI();
        }

        private void TxtGapTolerance_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtGapTolerance.Text, out double val))
            {
                if (SldGapTolerance != null && val >= SldGapTolerance.Minimum && val <= SldGapTolerance.Maximum)
                {
                    SldGapTolerance.Value = val;
                }
                SaveSettingsFromUI();
            }
        }

        // --- CHANGE EVENTS ---

        private void ChkRoad_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePanelsVisibility();
            SaveSettingsFromUI();
        }

        private void ChkSidewalk_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePanelsVisibility();
            SaveSettingsFromUI();
        }

        private void ChkRow_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePanelsVisibility();
            SaveSettingsFromUI();
        }

        private void ChkKmlSortHomepass_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
        }

        private void SldKmlLabelSearchTol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtKmlLabelSearchTol != null) TxtKmlLabelSearchTol.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtKmlLabelSearchTol_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtKmlLabelSearchTol.Text, out double val))
            {
                if (SldKmlLabelSearchTol != null && val >= SldKmlLabelSearchTol.Minimum && val <= SldKmlLabelSearchTol.Maximum)
                    SldKmlLabelSearchTol.Value = val;
                SaveSettingsFromUI();
            }
        }

        // Sliders & Textboxes sync for Widths
        private void SldRoadWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRoadWidth != null) TxtRoadWidth.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtRoadWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtRoadWidth.Text, out double val))
            {
                if (SldRoadWidth != null && val >= SldRoadWidth.Minimum && val <= SldRoadWidth.Maximum)
                    SldRoadWidth.Value = val;
                SaveSettingsFromUI();
            }
        }

        private void SldSidewalkWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSidewalkWidth != null) TxtSidewalkWidth.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtSidewalkWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtSidewalkWidth.Text, out double val))
            {
                if (SldSidewalkWidth != null && val >= SldSidewalkWidth.Minimum && val <= SldSidewalkWidth.Maximum)
                    SldSidewalkWidth.Value = val;
                SaveSettingsFromUI();
            }
        }

        private void SldRowWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRowWidth != null) TxtRowWidth.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtRowWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtRowWidth.Text, out double val))
            {
                if (SldRowWidth != null && val >= SldRowWidth.Minimum && val <= SldRowWidth.Maximum)
                    SldRowWidth.Value = val;
                SaveSettingsFromUI();
            }
        }

        // Sliders & Textboxes sync for Fillets
        private void SldRoadFillet_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRoadFillet != null) TxtRoadFillet.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtRoadFillet_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtRoadFillet.Text, out double val))
            {
                if (SldRoadFillet != null && val >= SldRoadFillet.Minimum && val <= SldRoadFillet.Maximum)
                    SldRoadFillet.Value = val;
                SaveSettingsFromUI();
            }
        }

        private void SldSidewalkFillet_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSidewalkFillet != null) TxtSidewalkFillet.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtSidewalkFillet_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtSidewalkFillet.Text, out double val))
            {
                if (SldSidewalkFillet != null && val >= SldSidewalkFillet.Minimum && val <= SldSidewalkFillet.Maximum)
                    SldSidewalkFillet.Value = val;
                SaveSettingsFromUI();
            }
        }

        private void SldRowFillet_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRowFillet != null) TxtRowFillet.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtRowFillet_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtRowFillet.Text, out double val))
            {
                if (SldRowFillet != null && val >= SldRowFillet.Minimum && val <= SldRowFillet.Maximum)
                    SldRowFillet.Value = val;
            }
        }

        // --- SMART PLINE & SMOOTHING EVENTS ---

        private void SldPlineMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtPlineMax != null) TxtPlineMax.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
        }

        private void TxtPlineMax_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtPlineMax.Text, out double val))
            {
                if (SldPlineMax != null && val >= SldPlineMax.Minimum && val <= SldPlineMax.Maximum)
                    SldPlineMax.Value = val;
                SaveSettingsFromUI();
            }
        }

        private void SldSmoothIter_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSmoothIter != null) TxtSmoothIter.Text = ((int)e.NewValue).ToString();
            SaveSettingsFromUI();
        }

        private void TxtSmoothIter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && int.TryParse(TxtSmoothIter.Text, out int val))
            {
                if (SldSmoothIter != null && val >= SldSmoothIter.Minimum && val <= SldSmoothIter.Maximum)
                    SldSmoothIter.Value = val;
                SaveSettingsFromUI();
            }
        }

        private void BtnDrawSmartPline_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_DRAW_SMART_PLINE");
        }

        private void BtnFtthPlines_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_PLINES");
        }

        private void BtnSmoothPolyline_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_SMOOTH_PLINE");
        }

        private void BtnDrawHelperArrow_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_DRAW_HELPER_ARROW");
        }

        // --- PLACER EVENTS ---

        private void TxtPlacerPrefix_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveSettingsFromUI();
        }

        private void TxtPlacerStart_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveSettingsFromUI();
        }

        private void BtnSelectTemplate_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_SELECTTEMPLATE");
        }

        private void BtnRegenHpHum_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to regenerate all Homepass numbers on the target layer? This will overwrite the existing numbers.",
                "Confirm Number Regeneration",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK)
                return;

            // Sync static values from UI
            MasterPlacer.TextPrefix = TxtPlacerPrefix.Text;
            if (int.TryParse(TxtPlacerStart.Text, out int ps))
            {
                MasterPlacer.StartIndex = ps;
            }
            else
            {
                MasterPlacer.StartIndex = 1;
            }

            SendCommandToAutoCAD("FTTH_REGEN_HPNUM");
        }

        private void BtnRunPlacer_Click(object sender, RoutedEventArgs e)
        {
            IsolatePlacerLayers();
            SendCommandToAutoCAD("FTTH_RUN_PLACER");
        }

        private void BtnIsolatePlacerLayers_Click(object sender, RoutedEventArgs e)
        {
            IsolatePlacerLayers();
        }

        private void BtnUnisolatePlacerLayers_Click(object sender, RoutedEventArgs e)
        {
            UnisolatePlacerLayers();
        }

        public void IsolatePlacerLayers()
        {
            if (_isPlacerIsolated) return; // Prevent overwriting previous states if already isolated

            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                Database db = doc.Database;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                        string targetActiveLayer = null;
                        ObjectId activeLayerId = ObjectId.Null;
                        foreach (var layerName in PlacerIsolatedLayers)
                        {
                            if (lt.Has(layerName))
                            {
                                activeLayerId = lt[layerName];
                                targetActiveLayer = layerName;
                                break;
                            }
                        }

                        if (activeLayerId == ObjectId.Null)
                        {
                            targetActiveLayer = "FTTH-ROW";
                            activeLayerId = GetOrCreateLayer(db, tr, targetActiveLayer, 7);
                        }

                        // Store current active layer
                        _previousActiveLayerId = db.Clayer;

                        _previousLayerStates.Clear();
                        foreach (ObjectId id in lt)
                        {
                            LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                            if (ltr.IsDependent) continue;

                            using (LayerTableRecord ltrWrite = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite))
                            {
                                _previousLayerStates[ltrWrite.Name] = ltrWrite.IsOff;

                                if (PlacerIsolatedLayers.Contains(ltrWrite.Name))
                                {
                                    if (ltrWrite.IsOff) ltrWrite.IsOff = false;
                                    if (ltrWrite.IsFrozen) ltrWrite.IsFrozen = false; // Thaw it first
                                }
                                else
                                {
                                    if (ltrWrite.IsOff == false) ltrWrite.IsOff = true;
                                }
                            }
                        }

                        // Now set Clayer safely since it's guaranteed to be thawed
                        if (activeLayerId != ObjectId.Null)
                        {
                            db.Clayer = activeLayerId;
                        }

                        tr.Commit();
                    }
                }

                _isPlacerIsolated = true; // Mark as isolated

                doc.Editor.Regen();
                LogMessage("Layers isolated for Auto Placer.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error isolating layers: {ex.Message}", "FTTH Basemap");
            }
        }

        public void UnisolatePlacerLayers()
        {
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                Database db = doc.Database;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                        foreach (ObjectId id in lt)
                        {
                            LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                            if (ltr.IsDependent) continue;

                            bool wasOff = false;
                            bool hasState = _isPlacerIsolated && _previousLayerStates.TryGetValue(ltr.Name, out wasOff);

                            if (hasState)
                            {
                                if (ltr.IsOff != wasOff)
                                {
                                    using (LayerTableRecord ltrWrite = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite))
                                    {
                                        ltrWrite.IsOff = wasOff;
                                    }
                                }
                            }
                            else
                            {
                                if (ltr.IsOff)
                                {
                                    using (LayerTableRecord ltrWrite = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite))
                                    {
                                        ltrWrite.IsOff = false;
                                    }
                                }
                            }
                        }

                        // Restore previous active layer if saved
                        if (_isPlacerIsolated && _previousActiveLayerId != ObjectId.Null && !_previousActiveLayerId.IsErased)
                        {
                            try
                            {
                                using (LayerTableRecord ltrActive = (LayerTableRecord)tr.GetObject(_previousActiveLayerId, OpenMode.ForRead))
                                {
                                    if (!ltrActive.IsDependent)
                                    {
                                        db.Clayer = _previousActiveLayerId;
                                    }
                                }
                            }
                            catch { }
                        }

                        tr.Commit();
                    }
                }

                _isPlacerIsolated = false; // Reset isolation flag

                doc.Editor.Regen();
                LogMessage("Layers unisolated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error unisolating layers: {ex.Message}", "FTTH Basemap");
            }
        }

        private void BtnLabelParcels_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_LABEL_PARCELS");
        }

        // --- ROAD LABEL EVENTS ---

        private void SldRoadLabelDist_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRoadLabelDist != null) TxtRoadLabelDist.Text = e.NewValue.ToString("F1");
            SaveSettingsFromUI();
            if (!_isInitializing && RoadLabelGenerator.IsLiveSessionActive)
            {
                RoadLabelGenerator.UpdateLivePreview(e.NewValue);
            }
        }

        private void TxtRoadLabelDist_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtRoadLabelDist.Text, out double val))
            {
                if (SldRoadLabelDist != null && val >= SldRoadLabelDist.Minimum && val <= SldRoadLabelDist.Maximum)
                    SldRoadLabelDist.Value = val;
                SaveSettingsFromUI();
                if (RoadLabelGenerator.IsLiveSessionActive)
                {
                    RoadLabelGenerator.UpdateLivePreview(val);
                }
            }
        }

        private void BtnGenerateRoadLabels_Click(object sender, RoutedEventArgs e)
        {
            if (!RoadLabelGenerator.IsLiveSessionActive)
            {
                bool started = RoadLabelGenerator.StartLiveSession();
                if (started)
                {
                    RoadLabelGenerator.UpdateLivePreview(SldRoadLabelDist.Value);
                    BtnGenerateRoadLabels.Content = "COMMIT";
                    BtnCancelRoadLabels.IsEnabled = true;
                }
                else
                {
                    MessageBox.Show("No new centerlines found on layer " + BasemapSettings.Instance.LayerCenterline, "FTTH Basemap");
                }
            }
            else
            {
                RoadLabelGenerator.CommitLiveSession();
                BtnGenerateRoadLabels.Content = "GENERATE LABELS";
                BtnCancelRoadLabels.IsEnabled = false;
            }
        }

        private void BtnCancelRoadLabels_Click(object sender, RoutedEventArgs e)
        {
            RoadLabelGenerator.RevertLiveSession();
            BtnGenerateRoadLabels.Content = "GENERATE LABELS";
            BtnCancelRoadLabels.IsEnabled = false;
        }

        // --- FTTH GROUP & BOUNDARY EVENTS ---

        private void BtnCreateGroup_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GROUP");
        }

        private void BtnGenerateBoundary_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_BOUNDARY");
        }

        // --- SCALE TOOLS EVENTS ---

        private void SldScaleFactor_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtScaleFactor != null) TxtScaleFactor.Text = e.NewValue.ToString("F2");
            if (!_isInitializing)
            {
                ScaleTools.ApplyScaleFactor(e.NewValue);
            }
        }

        private void TxtScaleFactor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtScaleFactor.Text, out double val))
            {
                if (SldScaleFactor != null && val >= SldScaleFactor.Minimum && val <= SldScaleFactor.Maximum)
                {
                    SldScaleFactor.Value = val;
                    ScaleTools.ApplyScaleFactor(val);
                }
            }
        }

        private void BtnScaleInPlace_Click(object sender, RoutedEventArgs e)
        {
            // Reset slider to 1.0 before selection starts
            _isInitializing = true;
            SldScaleFactor.Value = 1.0;
            TxtScaleFactor.Text = "1.00";
            _isInitializing = false;

            SendCommandToAutoCAD("FTTH_SCALEINPLACE");
        }

        private void BtnRevertScale_Click(object sender, RoutedEventArgs e)
        {
            ScaleTools.RevertScale();
            
            _isInitializing = true;
            SldScaleFactor.Value = 1.0;
            TxtScaleFactor.Text = "1.00";
            _isInitializing = false;
        }

        private void BtnScaleBase_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_SCALEBASE");
        }

        // ==========================================
        // --- STREETVIEW TAGGER METHODS & EVENTS ---
        // ==========================================

        public void UpdateCamReadout(double lat, double lon, double heading)
        {
            if (this.Dispatcher.CheckAccess())
            {
                TxtCamCoords.Text = $"{lat:F6}, {lon:F6}";
                TxtCamOrient.Text = $"H: {heading:F1}° | P: 90.0°";
                CameraIndicatorManager.UpdateIndicator(lat, lon, heading);
            }
            else
            {
                this.Dispatcher.Invoke(() => UpdateCamReadout(lat, lon, heading));
            }
        }

        public void PlaceMarkerFromStreetViewClick(double clientX, double clientY, double viewWidth, double viewHeight)
        {
            try
            {
                PaletteManager.LogToFile("PlaceMarkerFromStreetViewClick started.");
                var wpfViewer = PaletteManager.StreetViewViewer;
                if (wpfViewer == null)
                {
                    PaletteManager.LogToFile("PlaceMarkerFromStreetViewClick failed: StreetViewViewer is null.");
                    return;
                }

                double camLat = wpfViewer.CurrentLat;
                double camLon = wpfViewer.CurrentLon;
                double heading = wpfViewer.CurrentHeading;
                double pitch = wpfViewer.CurrentPitch;
                double fov = wpfViewer.CurrentFov;

                PaletteManager.LogToFile($"Cam state: Lat={camLat:F6}, Lon={camLon:F6}, Heading={heading:F1}, Pitch={pitch:F1}, Fov={fov:F1}");

                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    PaletteManager.LogToFile("PlaceMarkerFromStreetViewClick failed: MdiActiveDocument is null.");
                    return;
                }
                Database db = doc.Database;

                Point3d camWcs;
                try
                {
                    camWcs = TileMapManager.ProjectLonLatToWcs(db, camLon, camLat);
                    PaletteManager.LogToFile($"Camera WCS: X={camWcs.X:F4}, Y={camWcs.Y:F4}, Z={camWcs.Z:F4}");
                }
                catch (Exception ex)
                {
                    string errMsg = "Coordinate projection failed: " + ex.Message;
                    MessageBox.Show(errMsg, "Projection Error");
                    PaletteManager.LogToFile(errMsg);
                    return;
                }

                // Use camera heading directly, matching the yellow preview indicator arrow
                double clickedHeadingGsv = heading;
                double thetaCad = (90.0 - clickedHeadingGsv) * Math.PI / 180.0;
                Vector3d rayDir = new Vector3d(Math.Cos(thetaCad), Math.Sin(thetaCad), 0);
                PaletteManager.LogToFile($"Ray direction: X={rayDir.X:F4}, Y={rayDir.Y:F4}");

                Point3d targetPt;
                bool snapped = false;
                double roadAngle = thetaCad;
                string centerlineHandle = "";

                double searchRadius = 20.0;
                double minDistance;
                ObjectId nearestCenterlineId = FindNearestCenterlineId(db, camWcs, searchRadius, out minDistance);
                PaletteManager.LogToFile($"Nearest centerline lookup result: ID={nearestCenterlineId.ToString()}, Distance={minDistance:F2}m");

                if (nearestCenterlineId != ObjectId.Null)
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Curve centerline = tr.GetObject(nearestCenterlineId, OpenMode.ForRead) as Curve;
                        if (centerline != null)
                        {
                            centerlineHandle = centerline.Handle.ToString();
                            var meta = XDataManager.ReadMetadata(centerline);
                            double roadWidth = meta != null ? meta.RoadWidth : BasemapSettings.Instance.RoadWidth;
                            double sidewalkWidth = BasemapSettings.Instance.SidewalkWidth;
                            double D_middle = roadWidth / 2.0 + sidewalkWidth / 2.0;

                            Point3d? intersectPt = GetRayMiddleLaneIntersection(camWcs, rayDir, centerline, D_middle, out double intersectAngle);
                            PaletteManager.LogToFile($"Ray intersection with middle lane at offset {D_middle:F2}m. Success={intersectPt.HasValue}");
                            if (intersectPt.HasValue)
                            {
                                targetPt = intersectPt.Value;
                                roadAngle = intersectAngle;
                                snapped = true;
                            }
                            else
                            {
                                double fallbackDist = SldPoleOffset.Value;
                                targetPt = camWcs + rayDir * fallbackDist;
                                PaletteManager.LogToFile($"Intersection not found. Using fallback offset distance: {fallbackDist:F2}m");
                            }
                        }
                        else
                        {
                            double fallbackDist = SldPoleOffset.Value;
                            targetPt = camWcs + rayDir * fallbackDist;
                        }
                        tr.Commit();
                    }
                }
                else
                {
                    double fallbackDist = SldPoleOffset.Value;
                    targetPt = camWcs + rayDir * fallbackDist;
                    PaletteManager.LogToFile($"No centerline within {searchRadius}m. Using fallback offset distance: {fallbackDist:F2}m");
                }

                PaletteManager.LogToFile($"Placement target point: X={targetPt.X:F4}, Y={targetPt.Y:F4}, RoadAngle={roadAngle:F4}rad");

                string poleType = GetSelectedPoleType();
                string targetLayer = poleType.StartsWith("EXT") ? "FTTH-POLE-EXISTING" : GetNewPoleLayer(poleType);
                short colorIndex = poleType.StartsWith("EXT") ? (short)8 : GetNewPoleColor(poleType);

                List<ObjectId> createdIds = new List<ObjectId>();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        GetOrCreateLayer(db, tr, targetLayer, colorIndex);

                        string assetJsonFilename = GetPoleJsonFilename(poleType);
                        string assetJsonPath = GetAssetPath(assetJsonFilename);
                        PaletteManager.LogToFile($"Drawing pole asset JSON: {assetJsonFilename} at {assetJsonPath}");

                        if (File.Exists(assetJsonPath))
                        {
                            var ids = AssetDrawer.DrawAsset(db, tr, modelSpace, assetJsonPath, targetPt, 0.0, 1.0, targetLayer);
                            createdIds.AddRange(ids);
                            PaletteManager.LogToFile($"Pole drawn successfully. Created {ids.Count} entities.");
                        }
                        else
                        {
                            PaletteManager.LogToFile($"Pole JSON file not found. Fallback to drawing a simple circle.");
                            using (Circle circle = new Circle())
                            {
                                circle.Center = targetPt;
                                circle.Radius = 0.5;
                                circle.Layer = targetLayer;
                                circle.ColorIndex = colorIndex;
                                modelSpace.AppendEntity(circle);
                                tr.AddNewlyCreatedDBObject(circle, true);
                                createdIds.Add(circle.ObjectId);
                            }
                        }

                        if (snapped && ChkHelperLine.IsChecked == true)
                        {
                            string helperLayer = BasemapSettings.Instance.LayerPreview;
                            short helperColor = BasemapSettings.Instance.ColorPreview;
                            GetOrCreateLayer(db, tr, helperLayer, helperColor);

                            using (Line helperLine = new Line(camWcs, targetPt))
                            {
                                helperLine.Layer = helperLayer;
                                helperLine.ColorIndex = helperColor;
                                modelSpace.AppendEntity(helperLine);
                                tr.AddNewlyCreatedDBObject(helperLine, true);
                                createdIds.Add(helperLine.ObjectId);
                            }
                        }

                        // Place accessories exactly at targetPt (overlap)
                        List<string> checkedAccs = new List<string>();
                        Vector3d zeroOffset = new Vector3d(0, 0, 0);

                        if (ChkSlackCable.IsChecked == true) { checkedAccs.Add("SLACK CABLE"); DrawAccessory(db, tr, modelSpace, "Slack Cable.JSON", targetPt, 0.0, zeroOffset, ref createdIds); }
                        if (ChkFatOdp.IsChecked == true) { checkedAccs.Add("FAT/ODP"); DrawAccessory(db, tr, modelSpace, "FAT Symbol n Label on Pole.JSON", targetPt, 0.0, zeroOffset, ref createdIds); }
                        if (ChkFdtOdc48.IsChecked == true) { checkedAccs.Add("FDT/ODC 48C"); DrawAccessory(db, tr, modelSpace, "FDT48.JSON", targetPt, 0.0, zeroOffset, ref createdIds); }
                        if (ChkFdtOdc72.IsChecked == true) { checkedAccs.Add("FDT/ODC 72C"); DrawAccessory(db, tr, modelSpace, "FDT72.JSON", targetPt, 0.0, zeroOffset, ref createdIds); }
                        if (ChkClosure.IsChecked == true) { checkedAccs.Add("CLOSURE"); DrawAccessory(db, tr, modelSpace, "Closure144.JSON", targetPt, 0.0, zeroOffset, ref createdIds); }

                        PaletteManager.LogToFile("Accessories process done. Checked accessories count: " + checkedAccs.Count);

                        if (createdIds.Count > 0)
                        {
                            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                            if (!rat.Has("FTTH_POLE_DATA"))
                            {
                                if (!rat.IsWriteEnabled) rat.UpgradeOpen();
                                using (RegAppTableRecord ratr = new RegAppTableRecord())
                                {
                                    ratr.Name = "FTTH_POLE_DATA";
                                    rat.Add(ratr);
                                    tr.AddNewlyCreatedDBObject(ratr, true);
                                }
                            }

                            Entity mainEnt = tr.GetObject(createdIds[0], OpenMode.ForWrite) as Entity;
                            if (mainEnt != null)
                            {
                                string accessoriesStr = string.Join(",", checkedAccs);
                                using (ResultBuffer rb = new ResultBuffer(
                                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, "FTTH_POLE_DATA"),
                                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, poleType),
                                    new TypedValue((int)DxfCode.ExtendedDataReal, camLat),
                                    new TypedValue((int)DxfCode.ExtendedDataReal, camLon),
                                    new TypedValue((int)DxfCode.ExtendedDataReal, heading),
                                    new TypedValue((int)DxfCode.ExtendedDataReal, SldPoleOffset.Value),
                                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, accessoriesStr)
                                ))
                                {
                                    mainEnt.XData = rb;
                                }
                            }
                        }

                        tr.Commit();

                        if (createdIds.Count > 0)
                        {
                            _placedSteps.Add(createdIds);
                            TxtLastProjected.Text = $"{targetPt.X:F2}, {targetPt.Y:F2}";
                            LogMessage($"Placed {poleType} at {targetPt.X:F2}, {targetPt.Y:F2}");
                            PaletteManager.LogToFile($"Placement step recorded. Pole & accessories added successfully.");
                        }
                    }
                }

                // If FAT or FDT is placed, call ATTSYNC to show attributes
                List<string> syncBlocks = new List<string>();
                if (ChkFatOdp.IsChecked == true) syncBlocks.Add("FAT");
                if (ChkFdtOdc48.IsChecked == true) syncBlocks.Add("FDT48");
                if (ChkFdtOdc72.IsChecked == true) syncBlocks.Add("FDT72");

                if (syncBlocks.Count > 0)
                {
                    string cmd = "";
                    foreach (string bName in syncBlocks)
                    {
                        cmd += $"_.ATTSYNC _Name {bName}\n";
                    }
                    PaletteManager.LogToFile("Street View placement triggering ATTSYNC: " + cmd.Replace("\n", " "));
                    doc.SendStringToExecute(cmd, true, false, false);
                }

                doc.Editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                string errMsg = "Exception in PlaceMarkerFromStreetViewClick: " + ex.Message + "\nStack: " + ex.StackTrace;
                PaletteManager.LogToFile(errMsg);
            }
        }

        public string GetSelectedPoleType()
        {
            if (RadExtTel.IsChecked == true) return "EXT TEL";
            if (RadExt7_2_5.IsChecked == true) return "EXT 7 2.5\"";
            if (RadExt7_3.IsChecked == true) return "EXT 7 3\"";
            if (RadExt7_4.IsChecked == true) return "EXT 7 4\"";
            if (RadExt9_4.IsChecked == true) return "EXT 9 4\"";
            if (RadNp7_2_5.IsChecked == true) return "NP 7 2.5\"";
            if (RadNp7_3.IsChecked == true) return "NP 7 3\"";
            if (RadNp7_4.IsChecked == true) return "NP 7 4\"";
            if (RadNp9_4.IsChecked == true) return "NP 9 4\"";
            return "POLE";
        }

        public string GetNewPoleLayer(string poleType)
        {
            switch (poleType)
            {
                case "NP 7 2.5\"": return "FTTH-POLE-NP725";
                case "NP 7 3\"": return "FTTH-POLE-NP73";
                case "NP 7 4\"": return "FTTH-POLE-NP74";
                case "NP 9 4\"": return "FTTH-POLE-NP94";
                default: return "FTTH-POLE-NEW";
            }
        }

        public short GetNewPoleColor(string poleType)
        {
            switch (poleType)
            {
                case "NP 7 2.5\"": return 2; // Yellow
                case "NP 7 3\"": return 3;   // Green
                case "NP 7 4\"": return 5;   // Blue
                case "NP 9 4\"": return 6;   // Magenta
                default: return 2;
            }
        }

        public string GetPoleJsonFilename(string poleType)
        {
            switch (poleType)
            {
                case "EXT TEL": return "EXT TEL.JSON";
                case "NP 7 2.5\"": return "NP 7 2.5.JSON";
                case "NP 7 3\"": return "NP 7 3.JSON";
                case "NP 7 4\"": return "NP 7 4.JSON";
                case "NP 9 4\"": return "NP 9 4.JSON";
                case "EXT 7 2.5\"": return "EP725.JSON";
                case "EXT 7 3\"": return "EP73.JSON";
                case "EXT 7 4\"": return "EP74.JSON";
                case "EXT 9 4\"": return "EP94.JSON";
                default: return "EXT TEL.JSON";
            }
        }

        public string GetAssetPath(string filename)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                string docDir = Path.GetDirectoryName(doc.Name);
                if (!string.IsNullOrEmpty(docDir))
                {
                    string currentDir = docDir;
                    while (!string.IsNullOrEmpty(currentDir))
                    {
                        string localPath = Path.Combine(currentDir, "FTTH_Asset", filename);
                        if (File.Exists(localPath)) return localPath;

                        // Check if the directory itself exists if looking for the folder
                        string assetFolder = Path.Combine(currentDir, "FTTH_Asset");
                        if (string.IsNullOrEmpty(filename) && Directory.Exists(assetFolder))
                            return assetFolder;

                        try
                        {
                            currentDir = Path.GetDirectoryName(currentDir);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }
            string assemblyDir = "";
            try
            {
                string codeBase = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;
                var uri = new Uri(codeBase);
                assemblyDir = Path.GetDirectoryName(uri.LocalPath);
            }
            catch
            {
                assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            string fallbackDir = Path.Combine(assemblyDir, "FTTH_Asset");
            
            if (string.IsNullOrEmpty(filename)) return fallbackDir;
            return Path.Combine(fallbackDir, filename);
        }


        public void DrawAccessory(Database db, Transaction tr, BlockTableRecord modelSpace, string jsonFilename, Point3d basePt, double rotationAngle, Vector3d offsetVector, ref List<ObjectId> createdIds)
        {
            Point3d targetPt = basePt + offsetVector;
            string path = GetAssetPath(jsonFilename);
            if (File.Exists(path))
            {
                var ids = AssetDrawer.DrawAsset(db, tr, modelSpace, path, targetPt, rotationAngle, 1.0, "");
                createdIds.AddRange(ids);
            }
        }

        public ObjectId FindNearestCenterlineId(Database db, Point3d point, double maxRadius, out double minDistance)
        {
            minDistance = double.MaxValue;
            ObjectId nearestId = ObjectId.Null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                    if (c != null && c.Layer.Equals("FTTH-CENTERLINE", StringComparison.OrdinalIgnoreCase))
                    {
                        Point3d closestPt = c.GetClosestPointTo(point, false);
                        double dist = point.DistanceTo(closestPt);
                        if (dist < minDistance && dist <= maxRadius)
                        {
                            minDistance = dist;
                            nearestId = id;
                        }
                    }
                }
                tr.Commit();
            }

            return nearestId;
        }

        private Point3d? GetRayMiddleLaneIntersection(Point3d rayStart, Vector3d rayDir, Curve centerline, double offsetDist, out double intersectAngle)
        {
            intersectAngle = 0.0;
            Point3d? bestIntersection = null;
            double bestDist = double.MaxValue;

            DBObjectCollection offsetsLeft = null;
            DBObjectCollection offsetsRight = null;

            try { offsetsLeft = centerline.GetOffsetCurves(offsetDist); } catch { }
            try { offsetsRight = centerline.GetOffsetCurves(-offsetDist); } catch { }

            List<Curve> offsetCurves = new List<Curve>();
            if (offsetsLeft != null)
            {
                foreach (DBObject obj in offsetsLeft)
                {
                    if (obj is Curve c) offsetCurves.Add(c);
                    else obj.Dispose();
                }
            }
            if (offsetsRight != null)
            {
                foreach (DBObject obj in offsetsRight)
                {
                    if (obj is Curve c) offsetCurves.Add(c);
                    else obj.Dispose();
                }
            }

            if (offsetCurves.Count == 0) return null;

            using (Ray ray = new Ray())
            {
                ray.BasePoint = rayStart;
                ray.UnitDir = rayDir;

                foreach (Curve offsetCurve in offsetCurves)
                {
                    Point3dCollection pts = new Point3dCollection();
                    offsetCurve.IntersectWith(ray, Intersect.ExtendThis, pts, IntPtr.Zero, IntPtr.Zero);

                    foreach (Point3d pt in pts)
                    {
                        Vector3d toPt = pt - rayStart;
                        if (toPt.DotProduct(rayDir) > 0)
                        {
                            double d = rayStart.DistanceTo(pt);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestIntersection = pt;

                                Point3d closestCenterPt = centerline.GetClosestPointTo(pt, false);
                                double param = centerline.GetParameterAtPoint(closestCenterPt);
                                Vector3d tangent = centerline.GetFirstDerivative(param).GetNormal();
                                intersectAngle = tangent.AngleOnPlane(new Plane(Point3d.Origin, Vector3d.ZAxis));
                            }
                        }
                    }
                    offsetCurve.Dispose();
                }
            }

            return bestIntersection;
        }

        public void AddPlacedStep(List<ObjectId> step)
        {
            _placedSteps.Add(step);
        }

        private void BtnPlaceDirectCAD_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_PLACE_DIRECT");
        }

        private void BtnDrawSlingWire_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_DRAW_SLINGWIRE");
        }

        public void AutoDetectMajorityPrefixes()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            var fatPrefixCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var poleCodeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var obj = tr.GetObject(id, OpenMode.ForRead);

                        if (obj is BlockReference br)
                        {
                            if (br.AttributeCollection.Count > 0)
                            {
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    if (attRef != null && !string.IsNullOrEmpty(attRef.TextString))
                                    {
                                        string val = attRef.TextString.Trim();
                                        string tag = attRef.Tag.ToUpper();

                                        if (tag == "NAMA_FAT" || tag == "CODE" || tag == "NAMA" || tag == "FDT_CODE" || tag == "FDT_NAME")
                                        {
                                            ProcessFatString(val, fatPrefixCounts);
                                        }
                                    }
                                }
                            }
                        }
                        else if (obj is MText mt)
                        {
                            string layer = mt.Layer.ToUpper();
                            string text = mt.Text.Trim();
                            if (layer.Contains("FAT") || layer.Contains("FDT") || layer == "FAT_INFO" || layer == "FTTH-FAT-LABEL" || layer == "FTTH-FDT-LABEL")
                            {
                                ProcessFatString(text, fatPrefixCounts);
                            }
                            else if (layer.Contains("POLE") || layer.Contains("TIANG") || layer == "FTTH-POLE-LABEL")
                            {
                                ProcessPoleString(text, poleCodeCounts);
                            }
                        }
                        else if (obj is DBText txt)
                        {
                            string layer = txt.Layer.ToUpper();
                            string text = txt.TextString.Trim();
                            if (layer.Contains("FAT") || layer.Contains("FDT") || layer == "FAT_INFO" || layer == "FTTH-FAT-LABEL" || layer == "FTTH-FDT-LABEL")
                            {
                                ProcessFatString(text, fatPrefixCounts);
                            }
                            else if (layer.Contains("POLE") || layer.Contains("TIANG") || layer == "FTTH-POLE-LABEL")
                            {
                                ProcessPoleString(text, poleCodeCounts);
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch {}

            // Find majority FAT Prefix
            if (fatPrefixCounts.Count > 0)
            {
                var majorityFat = fatPrefixCounts.OrderByDescending(kv => kv.Value).First();
                if (majorityFat.Value >= 2)
                {
                    string bestFatPrefix = majorityFat.Key;
                    if (TxtLabelFatPrefix != null) TxtLabelFatPrefix.Text = bestFatPrefix;
                    if (TxtLabelFdtPrefix != null) TxtLabelFdtPrefix.Text = bestFatPrefix;
                }
            }

            // Find majority Pole Code
            if (poleCodeCounts.Count > 0)
            {
                var majorityPole = poleCodeCounts.OrderByDescending(kv => kv.Value).First();
                if (majorityPole.Value >= 2)
                {
                    string bestPoleCode = majorityPole.Key;
                    if (TxtLabelPoleLocationCode != null) TxtLabelPoleLocationCode.Text = bestPoleCode;
                }
            }
        }

        private void ProcessFatString(string val, Dictionary<string, int> counts)
        {
            if (string.IsNullOrEmpty(val)) return;
            string clean = LabelingManager.StripMTextFormatting(val).Trim();
            if (clean.Contains("."))
            {
                int lastDot = clean.LastIndexOf('.');
                if (lastDot > 0)
                {
                    string prefix = clean.Substring(0, lastDot).Trim();
                    if (prefix.Length > 2 && !prefix.Contains(" ") && !prefix.Contains("\n") && !prefix.Contains("\r"))
                    {
                        if (counts.ContainsKey(prefix)) counts[prefix]++;
                        else counts[prefix] = 1;
                    }
                }
            }
        }

        private void ProcessPoleString(string val, Dictionary<string, int> counts)
        {
            if (string.IsNullOrEmpty(val)) return;
            string clean = LabelingManager.StripMTextFormatting(val).Trim();

            // 1. Support standard format: MR.PDA6.P01 or EXT.MR.PDA6.P01
            if (clean.IndexOf("MR.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string[] parts = clean.Split('.');
                if (parts.Length >= 4 && parts[0].Equals("EXT", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("MR", StringComparison.OrdinalIgnoreCase))
                {
                    string code = parts[2].Trim();
                    if (code.Length >= 2 && !code.Contains(" ") && !code.Contains("\n") && !code.Contains("\r"))
                    {
                        if (counts.ContainsKey(code)) counts[code]++;
                        else counts[code] = 1;
                        return;
                    }
                }
                else if (parts.Length >= 3 && parts[0].Equals("MR", StringComparison.OrdinalIgnoreCase))
                {
                    string code = parts[1].Trim();
                    if (code.Length >= 2 && !code.Contains(" ") && !code.Contains("\n") && !code.Contains("\r"))
                    {
                        if (counts.ContainsKey(code)) counts[code]++;
                        else counts[code] = 1;
                        return;
                    }
                }
            }

            // 2. Fallback to slash format: PDA6/1
            if (clean.Contains("/"))
            {
                int firstSlash = clean.IndexOf('/');
                if (firstSlash > 0)
                {
                    string prefix = clean.Substring(0, firstSlash).Trim();
                    if (prefix.Length >= 2 && !prefix.Contains(" ") && !prefix.Contains("\n") && !prefix.Contains("\r"))
                    {
                        if (counts.ContainsKey(prefix)) counts[prefix]++;
                        else counts[prefix] = 1;
                        return;
                    }
                }
            }

            // 3. Fallback to dot format without MR: PDA6.1 or PDA6.01
            if (clean.Contains("."))
            {
                string[] parts = clean.Split('.');
                if (parts.Length == 2)
                {
                    string prefix = parts[0].Trim();
                    if (prefix.Length >= 2 && !prefix.Contains(" ") && !prefix.Contains("\n") && !prefix.Contains("\r"))
                    {
                        if (counts.ContainsKey(prefix)) counts[prefix]++;
                        else counts[prefix] = 1;
                        return;
                    }
                }
            }
        }

        private void BasemapPanel_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeCardCollapseStates();
            UpdateLicenseStatus();
            CheckLicenseOnlineInBackground();
            try { RefreshLayoutList(); } catch { }
            try { PopulateAlignBlocksOrLayers(); } catch { }
            try { AutoDetectMajorityPrefixes(); } catch { }
        }

        private void BtnLicense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var actWin = new ActivationWindow();
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(actWin);
                UpdateLicenseStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Activation: {ex.Message}", "FTTH Basemap");
            }
        }

        private void BtnImportKmlBoundary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as Button;
                if (btn == null) return;

                var menu = new ContextMenu();
                
                var item1 = new MenuItem { Header = "Import Boundary / Marker from KML/KMZ", FontSize = 11 };
                item1.Click += (s, ev) => { SendCommandToAutoCAD("FTTH_IMPORT_KML"); };
                
                var item2 = new MenuItem { Header = "Import Boundary from QGIS PDF", FontSize = 11 };
                item2.Click += (s, ev) => { SendCommandToAutoCAD("FTTH_IMPORT_PDF"); };
                
                menu.Items.Add(item1);
                menu.Items.Add(item2);
                
                menu.PlacementTarget = btn;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing menu: {ex.Message}", "FTTH Design Planner");
            }
        }

        public void UpdateLicenseStatus()
        {
            try
            {
                var service = FTTHBasemap.Licensing.LicensingService.Instance;
                bool isActive = service.IsLicenseActive();

                if (!isActive)
                {
                    MainTabControl.IsEnabled = false;
                    LicenseNotificationBar.Visibility = System.Windows.Visibility.Visible;
                    LicenseNotificationBar.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E74C3C"));
                    TxtLicenseWarningIcon.Text = "❌";
                    TxtLicenseWarningMessage.Text = "Lisensi Expired atau Tidak Aktif! Silahkan klik tombol kunci (🔑) di atas untuk mengaktifkan.";
                }
                else
                {
                    double remainingDays = service.GetRemainingDays();
                    if (remainingDays <= 2.0)
                    {
                        MainTabControl.IsEnabled = true;
                        LicenseNotificationBar.Visibility = System.Windows.Visibility.Visible;
                        LicenseNotificationBar.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F39C12"));
                        TxtLicenseWarningIcon.Text = "⚠️";
                        TxtLicenseWarningMessage.Text = $"Peringatan: Lisensi Anda akan berakhir dalam {remainingDays:F1} hari. Silahkan klik tombol kunci (🔑) untuk memperpanjang.";
                    }
                    else
                    {
                        MainTabControl.IsEnabled = true;
                        LicenseNotificationBar.Visibility = System.Windows.Visibility.Collapsed;
                    }
                }
            }
            catch { }
        }

        private async void CheckLicenseOnlineInBackground()
        {
            try
            {
                var result = await FTTHBasemap.Licensing.LicensingService.Instance.CheckLicenseOnlineAsync();
                Dispatcher.Invoke(() => {
                    UpdateLicenseStatus();
                });
            }
            catch { }
        }

        private void BtnToggleCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Grid grid && grid.Tag != null)
            {
                string cardName = grid.Tag as string;
                if (!string.IsNullOrEmpty(cardName))
                {
                    DependencyObject parent = System.Windows.Media.VisualTreeHelper.GetParent(grid);
                    if (parent is StackPanel sp)
                    {
                        TextBlock arrowText = null;
                        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(grid); i++)
                        {
                            var child = System.Windows.Media.VisualTreeHelper.GetChild(grid, i);
                            if (child is TextBlock tb && Grid.GetColumn(tb) > 0)
                            {
                                arrowText = tb;
                                break;
                            }
                        }

                        UIElement controlsPanel = null;
                        for (int i = 0; i < sp.Children.Count; i++)
                        {
                            if (sp.Children[i] != grid)
                            {
                                controlsPanel = sp.Children[i];
                                break;
                            }
                        }

                        if (controlsPanel != null)
                        {
                            bool isCurrentlyVisible = controlsPanel.Visibility == System.Windows.Visibility.Visible;
                            bool newCollapsed = isCurrentlyVisible; // if currently visible, it will become collapsed

                            if (isCurrentlyVisible)
                            {
                                controlsPanel.Visibility = System.Windows.Visibility.Collapsed;
                                if (arrowText != null) arrowText.Text = "▼";
                            }
                            else
                            {
                                controlsPanel.Visibility = System.Windows.Visibility.Visible;
                                if (arrowText != null) arrowText.Text = "▲";
                            }

                            // Save the state
                            BasemapSettings.Instance.SetCardCollapsed(cardName, newCollapsed);
                        }
                    }
                }
            }
        }

        private void InitializeCardCollapseStates()
        {
            try
            {
                FindAndRestoreCardStates(this, BasemapSettings.Instance);
            }
            catch { }
        }

        private void FindAndRestoreCardStates(DependencyObject parent, BasemapSettings settings)
        {
            if (parent == null) return;
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Grid grid && grid.Tag != null && grid.Cursor == System.Windows.Input.Cursors.Hand)
                {
                    string cardName = grid.Tag as string;
                    if (!string.IsNullOrEmpty(cardName))
                    {
                        DependencyObject p = System.Windows.Media.VisualTreeHelper.GetParent(grid);
                        if (p is StackPanel sp)
                        {
                            UIElement controlsPanel = null;
                            for (int j = 0; j < sp.Children.Count; j++)
                            {
                                if (sp.Children[j] != grid)
                                {
                                    controlsPanel = sp.Children[j];
                                    break;
                                }
                            }

                            TextBlock arrowText = null;
                            for (int j = 0; j < System.Windows.Media.VisualTreeHelper.GetChildrenCount(grid); j++)
                            {
                                var gridChild = System.Windows.Media.VisualTreeHelper.GetChild(grid, j);
                                if (gridChild is TextBlock tb && Grid.GetColumn(tb) > 0)
                                {
                                    arrowText = tb;
                                    break;
                                }
                            }

                            if (controlsPanel != null)
                            {
                                bool defaultCollapsed = (cardName == "Card_Card13" || cardName == "Card_Card14");
                                bool collapsed = settings.IsCardCollapsed(cardName, defaultCollapsed);
                                if (collapsed)
                                {
                                    controlsPanel.Visibility = System.Windows.Visibility.Collapsed;
                                    if (arrowText != null) arrowText.Text = "▼";
                                }
                                else
                                {
                                    controlsPanel.Visibility = System.Windows.Visibility.Visible;
                                    if (arrowText != null) arrowText.Text = "▲";
                                }
                            }
                        }
                    }
                }
                
                FindAndRestoreCardStates(child, settings);
            }
        }

        private void BtnToggleMap_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            string layerName = "FTTH-SATELLITE-MAP";

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(layerName))
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                        ltr.IsOff = !ltr.IsOff; // Toggle visibility

                        string state = ltr.IsOff ? "HIDDEN" : "SHOWN";
                        TxtMapStatus.Text = $"Map: {state}";
                        LogMessage($"Satellite map layer is now {state}.");
                    }
                    else
                    {
                        MessageBox.Show("No satellite map has been downloaded yet.", "Toggle Failed");
                    }
                    tr.Commit();
                }
            }
            doc.Editor.Regen();
        }

        private void SldPoleOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtPoleOffset != null) TxtPoleOffset.Text = e.NewValue.ToString("F1");
        }

        private void TxtPoleOffset_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtPoleOffset.Text, out double val))
            {
                if (SldPoleOffset != null && val >= SldPoleOffset.Minimum && val <= SldPoleOffset.Maximum)
                    SldPoleOffset.Value = val;
            }
        }

        private void SldSnapOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSnapOffset != null) TxtSnapOffset.Text = e.NewValue.ToString("F1");
        }

        private void TxtSnapOffset_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing && double.TryParse(TxtSnapOffset.Text, out double val))
            {
                if (SldSnapOffset != null && val >= SldSnapOffset.Minimum && val <= SldSnapOffset.Maximum)
                    SldSnapOffset.Value = val;
            }
        }

        private void BtnPickCadPoint_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_LOCATE_SV");
        }

        private void BtnFindLocate_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtLocateCoord.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show("Please enter coordinates to locate.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Clean degree symbols and unify separators (commas/semicolons/tabs -> spaces)
            string cleanText = text.Replace("°", "").Replace(",", " ").Replace(";", " ").Replace("\t", " ").Trim();
            
            // Split by space, filtering out extra whitespace
            string[] parts = cleanText.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                MessageBox.Show("Invalid coordinate format. Supported formats:\n-7.928755 112.602926\n-7.928755, 112.602926\n-7.928755° 112.602926°\nX, Y (UTM)", "Format Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val1) ||
                !double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val2))
            {
                MessageBox.Show("Could not parse coordinates. Please verify they are valid numbers.", "Format Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            double lat = 0.0, lon = 0.0;
            double x = 0.0, y = 0.0;

            // Detect if Lat/Lon:
            // One value is typical Latitude (-15 to 15 degrees for Indonesia/vicinity)
            // One value is typical Longitude (90 to 150 degrees)
            if ((val1 >= -15 && val1 <= 15 && val2 >= 90 && val2 <= 150) ||
                (val2 >= -15 && val2 <= 15 && val1 >= 90 && val1 <= 150))
            {
                if (val1 >= -15 && val1 <= 15)
                {
                    lat = val1;
                    lon = val2;
                }
                else
                {
                    lat = val2;
                    lon = val1;
                }

                // Project geographic to WCS
                try
                {
                    Point3d wcsPt = TileMapManager.ProjectLonLatToWcs(db, lon, lat);
                    x = wcsPt.X;
                    y = wcsPt.Y;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Geographic projection failed: {ex.Message}", "Projection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                // Treat as UTM/WCS coordinates
                x = val1;
                y = val2;

                // Project WCS to LonLat
                try
                {
                    Point3d wcsPt = new Point3d(x, y, 0.0);
                    Point3d lonLat = TileMapManager.ProjectWcsToLonLat(db, wcsPt);
                    lon = lonLat.X;
                    lat = lonLat.Y;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"WCS projection failed: {ex.Message}", "Projection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Update Street View panorama position
            var wpfViewer = PaletteManager.StreetViewViewer;
            if (wpfViewer != null)
            {
                wpfViewer.LoadCoordinates(lat, lon);
            }
            else
            {
                MessageBox.Show("StreetView window is not open. Opening it now...", "StreetView Closed", MessageBoxButton.OK, MessageBoxImage.Information);
                SendCommandToAutoCAD("FTTH_SHOWSTREETVIEW");
            }

            // Center and Zoom AutoCAD editor view on (x, y)
            try
            {
                using (doc.LockDocument())
                {
                    using (ViewTableRecord view = ed.GetCurrentView())
                    {
                        // Transform WCS coordinate to DCS coordinate for proper display mapping
                        Matrix3d eyeToWorld = Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) *
                                              Matrix3d.Displacement(view.Target - Point3d.Origin) *
                                              Matrix3d.PlaneToWorld(view.ViewDirection);
                        Matrix3d worldToEye = eyeToWorld.Inverse();

                        Point3d wcsPt = new Point3d(x, y, 0.0);
                        Point3d eyePt = wcsPt.TransformBy(worldToEye);

                        view.CenterPoint = new Point2d(eyePt.X, eyePt.Y);
                        double oldHeight = view.Height;
                        double oldWidth = view.Width;
                        view.Height = 30.0; // Zoom in close (30 units/meters)
                        view.Width = 30.0 * (oldWidth / oldHeight);
                        ed.SetCurrentView(view);
                    }
                }
                ed.UpdateScreen();
                LogMessage($"Located and zoomed to coordinates: {lat:F6}, {lon:F6}");
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[FTTH] Center view failed: {ex.Message}");
            }
        }

        private void BtnRunCpyVal_Click(object sender, RoutedEventArgs e)
        {
            Commands.CpyValEraseSource = ChkCpyValEraseSource.IsChecked == true;
            SendCommandToAutoCAD("FTTH_CPYVAL");
        }

        private void CboReplaceScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboReplaceScope == null || LblReplaceSourceFilter == null || CboReplaceSourceFilter == null || LblReplaceRadius == null || TxtReplaceRadius == null)
                return;

            var selectedItem = CboReplaceScope.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            string tag = selectedItem.Tag?.ToString() ?? "0";
            if (tag == "0") // Individual
            {
                LblReplaceSourceFilter.IsEnabled = false;
                CboReplaceSourceFilter.IsEnabled = false;
                LblReplaceRadius.IsEnabled = false;
                TxtReplaceRadius.IsEnabled = false;
            }
            else if (tag == "1") // Global
            {
                LblReplaceSourceFilter.IsEnabled = true;
                CboReplaceSourceFilter.IsEnabled = true;
                LblReplaceRadius.IsEnabled = false;
                TxtReplaceRadius.IsEnabled = false;
            }
            else if (tag == "2") // Centerline
            {
                LblReplaceSourceFilter.IsEnabled = false;
                CboReplaceSourceFilter.IsEnabled = false;
                LblReplaceRadius.IsEnabled = true;
                TxtReplaceRadius.IsEnabled = true;
            }
        }

        private void BtnReplacePoles_Click(object sender, RoutedEventArgs e)
        {
            // Read target pole
            var targetItem = CboReplaceTarget.SelectedItem as ComboBoxItem;
            Commands.ReplaceTargetPole = targetItem?.Tag?.ToString() ?? "NP 7 4\"";

            // Read scope
            var scopeItem = CboReplaceScope.SelectedItem as ComboBoxItem;
            if (scopeItem != null && int.TryParse(scopeItem.Tag?.ToString(), out int scopeVal))
            {
                Commands.ReplaceScope = scopeVal;
            }
            else
            {
                Commands.ReplaceScope = 0;
            }

            // Read filter
            var filterItem = CboReplaceSourceFilter.SelectedItem as ComboBoxItem;
            Commands.ReplaceSourceFilter = filterItem?.Tag?.ToString() ?? "ALL";

            // Read radius
            if (double.TryParse(TxtReplaceRadius.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double radius))
            {
                Commands.ReplaceSearchRadius = radius;
            }
            else
            {
                Commands.ReplaceSearchRadius = 10.0;
            }

            // Call command
            SendCommandToAutoCAD("FTTH_REPLACE_POLES");
        }



        private void BtnOpenStreetViewWindow_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_SHOWSTREETVIEW");
        }

        private void BtnDownloadByPolygon_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_DOWNLOAD_BY_POLYGON");
        }

        private void CboMapSettings_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (CboMapProvider == null || CboMapType == null) return;
            
            var providerItem = CboMapProvider.SelectedItem as ComboBoxItem;
            if (providerItem == null) return;
            
            string provider = providerItem.Tag?.ToString() ?? "google";
            
            if (provider == "esri")
            {
                foreach (ComboBoxItem item in CboMapType.Items)
                {
                    if (item.Tag?.ToString() == "hybrid")
                    {
                        item.IsEnabled = false;
                        if (CboMapType.SelectedItem == item)
                        {
                            CboMapType.SelectedIndex = 0; // Default to Satellite
                        }
                    }
                }
            }
            else
            {
                foreach (ComboBoxItem item in CboMapType.Items)
                {
                    item.IsEnabled = true;
                }
            }
        }

        private void CboMapResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboMapResolution == null || CboZoomLevel == null || TxtCalculatedZoom == null || LblZoomLevel == null) return;

            var resItem = CboMapResolution.SelectedItem as ComboBoxItem;
            if (resItem == null) return;

            string res = resItem.Tag?.ToString() ?? "manual";

            if (res == "manual")
            {
                CboZoomLevel.Visibility = System.Windows.Visibility.Visible;
                LblZoomLevel.Content = "Zoom Level:";
                TxtCalculatedZoom.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                CboZoomLevel.Visibility = System.Windows.Visibility.Collapsed;
                LblZoomLevel.Content = "Zoom Level (Auto):";
                TxtCalculatedZoom.Visibility = System.Windows.Visibility.Visible;
                TxtCalculatedZoom.Text = "Auto (Calculated)";
            }
        }

        private void BtnUndoPoint_Click(object sender, RoutedEventArgs e)
        {
            if (_placedSteps.Count == 0)
            {
                LogMessage("No points to undo.");
                return;
            }

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            List<ObjectId> lastStepIds = _placedSteps[_placedSteps.Count - 1];

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        foreach (ObjectId id in lastStepIds)
                        {
                            if (id.IsValid && !id.IsErased)
                            {
                                DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                                if (obj != null)
                                {
                                    obj.Erase();
                                }
                            }
                        }
                        tr.Commit();
                        _placedSteps.RemoveAt(_placedSteps.Count - 1);
                        LogMessage("Undid last placed marker and its accessories.");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Undo failed: {ex.Message}");
                    }
                }
            }

            doc.Editor.UpdateScreen();
        }

        private void BtnCalibrateCAD_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions("\nPick calibration reference point in AutoCAD: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status == PromptStatus.OK)
            {
                TxtXRef.Text = ppr.Value.X.ToString("F4");
                TxtYRef.Text = ppr.Value.Y.ToString("F4");
                LogMessage($"Calibrated reference point in AutoCAD: {ppr.Value.X:F4}, {ppr.Value.Y:F4}");
            }
        }

        private void BtnSyncCam_Click(object sender, RoutedEventArgs e)
        {
            var wpfViewer = PaletteManager.StreetViewViewer;
            if (wpfViewer == null || string.IsNullOrEmpty(wpfViewer.CurrentUrl))
            {
                MessageBox.Show("Please open StreetView window first and load a location.", "Sync Failed");
                return;
            }

            TxtLatRef.Text = wpfViewer.CurrentLat.ToString("F6");
            TxtLonRef.Text = wpfViewer.CurrentLon.ToString("F6");
            LogMessage($"Synced camera coordinate: {wpfViewer.CurrentLat:F6}, {wpfViewer.CurrentLon:F6}");
        }

        private void TxtCamCoords_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBox tb && !string.IsNullOrEmpty(tb.Text))
            {
                try
                {
                    System.Windows.Clipboard.SetText(tb.Text);
                    LogMessage("Coordinates copied to clipboard!");
                    tb.SelectAll();
                }
                catch { }
            }
        }

        private void ResetCalibration()
        {
            TxtXRef.Text = "";
            TxtYRef.Text = "";
            TxtLatRef.Text = "";
            TxtLonRef.Text = "";

            var wpfViewer = PaletteManager.StreetViewViewer;
            if (wpfViewer != null && !string.IsNullOrEmpty(wpfViewer.CurrentUrl))
            {
                UpdateCamReadout(wpfViewer.CurrentLat, wpfViewer.CurrentLon, wpfViewer.CurrentHeading);
            }
            else
            {
                CameraIndicatorManager.ClearIndicator();
            }
        }

        private void BtnResetCalibration_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration();
            LogMessage("Calibration reset. Reverted to raw coordinates.");
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

        private void BtnExportKML_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            bool isGeoreferenced = TileMapManager.IsDrawingGeoreferenced(db, out _);
            double xRef = 0, yRef = 0, latRef = 0, lonRef = 0;
            bool hasCalibration = false;

            if (!string.IsNullOrEmpty(TxtXRef.Text) && !string.IsNullOrEmpty(TxtYRef.Text) &&
                !string.IsNullOrEmpty(TxtLatRef.Text) && !string.IsNullOrEmpty(TxtLonRef.Text))
            {
                hasCalibration = double.TryParse(TxtXRef.Text, out xRef) &&
                                 double.TryParse(TxtYRef.Text, out yRef) &&
                                 double.TryParse(TxtLatRef.Text, out latRef) &&
                                 double.TryParse(TxtLonRef.Text, out lonRef);
            }

            if (!isGeoreferenced && !hasCalibration)
            {
                MessageBox.Show("Please calibrate first to export to KML.", "Export Failed");
                return;
            }

            Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "KMZ Files (*.kmz)|*.kmz|KML Files (*.kml)|*.kml",
                FileName = "ftth_design.kmz"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var layerPlacemarks = new Dictionary<string, List<string>>();
                    var layerColors = new Dictionary<string, System.Drawing.Color>();

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                        foreach (ObjectId id in btr)
                        {
                            if (id.IsErased) continue;
                            DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                            if (!(obj is Entity ent)) continue;

                            string layer = ent.Layer;
                            // Skip reference / template / preview layers
                            if (layer == "FTTH-SATELLITE-MAP" || layer == "FTTH-PREVIEW" || 
                                layer == "0" || layer == "Defpoints" || layer.StartsWith("FTTH-TEMP", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            System.Drawing.Color color = GetEntityColor(db, tr, ent);
                            layerColors[layer] = color;

                            string placemarkXml = "";

                            if (ent is Circle circle)
                            {
                                ResultBuffer rb = circle.GetXDataForApplication("FTTH_POLE_DATA");
                                double lat, lon;
                                try
                                {
                                    Point3d geoPt = TileMapManager.ProjectWcsToLonLat(db, circle.Center, hasCalibration, xRef, yRef, latRef, lonRef);
                                    lon = geoPt.X;
                                    lat = geoPt.Y;
                                }
                                catch { continue; }

                                string name = "Pole";
                                string htmlDesc = "";

                                if (rb != null)
                                {
                                    string poleType = "POLE";
                                    double camLat = 0, camLon = 0, heading = 0, offset = 0;
                                    string accessories = "";

                                    var enumerator = rb.GetEnumerator();
                                    int step = 0;
                                    while (enumerator.MoveNext())
                                    {
                                        TypedValue val = (TypedValue)enumerator.Current;
                                        if (step == 1 && val.TypeCode == (int)DxfCode.ExtendedDataAsciiString) poleType = val.Value.ToString();
                                        if (step == 2 && val.TypeCode == (int)DxfCode.ExtendedDataReal) camLat = (double)val.Value;
                                        if (step == 3 && val.TypeCode == (int)DxfCode.ExtendedDataReal) camLon = (double)val.Value;
                                        if (step == 4 && val.TypeCode == (int)DxfCode.ExtendedDataReal) heading = (double)val.Value;
                                        if (step == 5 && val.TypeCode == (int)DxfCode.ExtendedDataReal) offset = (double)val.Value;
                                        if (step == 6 && val.TypeCode == (int)DxfCode.ExtendedDataAsciiString) accessories = val.Value.ToString();
                                        step++;
                                    }
                                    rb.Dispose();

                                    name = poleType;
                                    htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #007ACC; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">FTTH Pole Attribute</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Object Type</td><td style=""padding: 4px;"">{poleType}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layer}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{circle.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Accessories</td><td style=""padding: 4px;"">{accessories}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Camera Position</td><td style=""padding: 4px;"">{camLat:F6}, {camLon:F6}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Camera Heading</td><td style=""padding: 4px;"">{heading:F1}°</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Offset</td><td style=""padding: 4px;"">{offset:F1} m</td></tr>
  </table>";
                                }
                                else
                                {
                                    name = layer.Contains("HOMEPASS") ? "HOMEPASS" : "Circle Marker";
                                    htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Circle Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layer}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{circle.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Radius</td><td style=""padding: 4px;"">{circle.Radius:F2} m</td></tr>
  </table>";
                                }

                                placemarkXml = $@"    <Placemark>
      <name>{name}</name>
      <styleUrl>#style_{layer}</styleUrl>
      <description><![CDATA[{htmlDesc}]]></description>
      <Point>
        <coordinates>{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},0</coordinates>
      </Point>
    </Placemark>";
                            }
                            else if (ent is DBPoint ptEntity)
                            {
                                double lat, lon;
                                try
                                {
                                    Point3d geoPt = TileMapManager.ProjectWcsToLonLat(db, ptEntity.Position, hasCalibration, xRef, yRef, latRef, lonRef);
                                    lon = geoPt.X;
                                    lat = geoPt.Y;
                                }
                                catch { continue; }

                                string htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Point Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layer}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{ptEntity.Handle}</td></tr>
  </table>";

                                placemarkXml = $@"    <Placemark>
      <name>Point</name>
      <styleUrl>#style_{layer}</styleUrl>
      <description><![CDATA[{htmlDesc}]]></description>
      <Point>
        <coordinates>{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},0</coordinates>
      </Point>
    </Placemark>";
                            }
                            else if (ent is DBText txt)
                            {
                                double lat, lon;
                                try
                                {
                                    Point3d geoPt = TileMapManager.ProjectWcsToLonLat(db, txt.Position, hasCalibration, xRef, yRef, latRef, lonRef);
                                    lon = geoPt.X;
                                    lat = geoPt.Y;
                                }
                                catch { continue; }

                                string cleanText = txt.TextString;
                                string htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Text Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Text Value</td><td style=""padding: 4px;"">{cleanText}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layer}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{txt.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Height</td><td style=""padding: 4px;"">{txt.Height:F2} m</td></tr>
  </table>";

                                placemarkXml = $@"    <Placemark>
      <name>{cleanText}</name>
      <styleUrl>#style_{layer}</styleUrl>
      <description><![CDATA[{htmlDesc}]]></description>
      <Point>
        <coordinates>{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},0</coordinates>
      </Point>
    </Placemark>";
                            }
                            else if (ent is MText mtxt)
                            {
                                double lat, lon;
                                try
                                {
                                    Point3d geoPt = TileMapManager.ProjectWcsToLonLat(db, mtxt.Location, hasCalibration, xRef, yRef, latRef, lonRef);
                                    lon = geoPt.X;
                                    lat = geoPt.Y;
                                }
                                catch { continue; }

                                string cleanText = mtxt.Text; // AutoCAD API automatically strips formatting!
                                string htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD MText Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Text Value</td><td style=""padding: 4px;"">{cleanText}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layer}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{mtxt.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Height</td><td style=""padding: 4px;"">{mtxt.TextHeight:F2} m</td></tr>
  </table>";

                                placemarkXml = $@"    <Placemark>
      <name>{cleanText}</name>
      <styleUrl>#style_{layer}</styleUrl>
      <description><![CDATA[{htmlDesc}]]></description>
      <Point>
        <coordinates>{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},0</coordinates>
      </Point>
    </Placemark>";
                            }
                            else if (ent is Curve curve)
                            {
                                Point3dCollection vertices = GetCurveVertices(tr, curve);
                                if (vertices.Count < 2) continue;

                                var coordList = new List<string>();
                                foreach (Point3d pt in vertices)
                                {
                                    try
                                    {
                                        Point3d geoPt = TileMapManager.ProjectWcsToLonLat(db, pt, hasCalibration, xRef, yRef, latRef, lonRef);
                                        coordList.Add($"{geoPt.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{geoPt.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},0");
                                    }
                                    catch { }
                                }

                                if (coordList.Count < 2) continue;

                                bool isClosed = false;
                                if (curve is Polyline polyEnt && polyEnt.Closed) isClosed = true;
                                else if (curve is Polyline3d poly3dEnt && poly3dEnt.Closed) isClosed = true;

                                string geomXml = "";
                                string htmlDesc = "";

                                if (isClosed)
                                {
                                    // Close ring by adding first point at the end
                                    coordList.Add(coordList[0]);
                                    string coordStr = string.Join(" ", coordList);

                                    geomXml = $@"      <Polygon>
        <tessellate>1</tessellate>
        <outerBoundaryIs>
          <LinearRing>
            <coordinates>{coordStr}</coordinates>
          </LinearRing>
        </outerBoundaryIs>
      </Polygon>";

                                    double area = 0.0;
                                    if (curve is Polyline poly) area = poly.Area;
                                    htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Polygon Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layer}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{curve.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Area</td><td style=""padding: 4px;"">{area:F2} m²</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Perimeter</td><td style=""padding: 4px;"">{curve.EndParam:F2} m</td></tr>
  </table>";
                                }
                                else
                                {
                                    string coordStr = string.Join(" ", coordList);
                                    geomXml = $@"      <LineString>
        <tessellate>1</tessellate>
        <coordinates>{coordStr}</coordinates>
      </LineString>";

                                    double length = 0.0;
                                    try { length = curve.GetDistanceAtParameter(curve.EndParam); } catch { }

                                    htmlDesc = $@"<table border=""1"" style=""border-collapse: collapse; font-family: Segoe UI, sans-serif; width: 100%;"">
    <tr style=""background-color: #333333; color: white;"">
      <th colspan=""2"" style=""padding: 6px;"">AutoCAD Polyline Properties</th>
    </tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Layer</td><td style=""padding: 4px;"">{layer}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Handle</td><td style=""padding: 4px;"">{curve.Handle}</td></tr>
    <tr><td style=""padding: 4px; font-weight: bold;"">Length</td><td style=""padding: 4px;"">{length:F2} m</td></tr>
  </table>";
                                }

                                placemarkXml = $@"    <Placemark>
      <name>{layer} Path</name>
      <styleUrl>#style_{layer}</styleUrl>
      <description><![CDATA[{htmlDesc}]]></description>
{geomXml}
    </Placemark>";
                            }

                            if (!string.IsNullOrEmpty(placemarkXml))
                            {
                                if (!layerPlacemarks.ContainsKey(layer))
                                {
                                    layerPlacemarks[layer] = new List<string>();
                                }
                                layerPlacemarks[layer].Add(placemarkXml);
                            }
                        }

                        tr.Commit();
                    }

                    // Build KML XML content
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
                    sb.AppendLine("  <Document>");
                    sb.AppendLine("    <name>FTTH Placed Elements</name>");

                    // 1. Generate Styles for each active layer
                    foreach (var pair in layerColors)
                    {
                        string layerName = pair.Key;
                        System.Drawing.Color color = pair.Value;
                        string hexColor = ConvertColorToKmlHex(color, 255);
                        string fillHexColor = ConvertColorToKmlHex(color, 64); // 25% transparent fill

                        sb.AppendLine($@"    <Style id=""style_{layerName}"">
      <LineStyle>
        <color>{hexColor}</color>
        <width>2.5</width>
      </LineStyle>
      <PolyStyle>
        <color>{fillHexColor}</color>
        <fill>1</fill>
        <outline>1</outline>
      </PolyStyle>
      <IconStyle>
        <color>{hexColor}</color>
        <scale>1.1</scale>
        <Icon>
          <href>http://maps.google.com/mapfiles/kml/pushpin/wht-pushpin.png</href>
        </Icon>
      </IconStyle>
      <LabelStyle>
        <color>{hexColor}</color>
        <scale>0.8</scale>
      </LabelStyle>
    </Style>");
                    }

                    // 2. Generate Folders for each layer containing placemarks
                    foreach (var pair in layerPlacemarks)
                    {
                        string layerName = pair.Key;
                        List<string> placemarks = pair.Value;

                        sb.AppendLine($"    <Folder>");
                        sb.AppendLine($"      <name>{layerName}</name>");
                        
                        foreach (string p in placemarks)
                        {
                            sb.AppendLine(p);
                        }

                        sb.AppendLine($"    </Folder>");
                    }

                    sb.AppendLine("  </Document>");
                    sb.AppendLine("</kml>");

                    string finalKmlContent = sb.ToString();

                    // If file extension is .kmz, create zipped archive containing doc.kml
                    if (sfd.FileName.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
                    {
                        using (FileStream fs = new FileStream(sfd.FileName, FileMode.Create))
                        {
                            using (System.IO.Compression.ZipArchive archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                            {
                                System.IO.Compression.ZipArchiveEntry kmlEntry = archive.CreateEntry("doc.kml");
                                using (StreamWriter writer = new StreamWriter(kmlEntry.Open()))
                                {
                                    writer.Write(finalKmlContent);
                                }
                            }
                        }
                        MessageBox.Show("Successfully exported to KMZ!", "Export Complete");
                        LogMessage("KMZ export complete.");
                    }
                    else
                    {
                        File.WriteAllText(sfd.FileName, finalKmlContent);
                        MessageBox.Show("Successfully exported to KML!", "Export Complete");
                        LogMessage("KML export complete.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export: {ex.Message}", "Export Error");
                    LogMessage("Export failed: " + ex.Message);
                }
            }
        }

        public static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex)
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

        private void BtnHpTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr)
            {
                string[] parts = tagStr.Split('|');
                if (parts.Length >= 3)
                {
                    string label = parts[0];
                    if (short.TryParse(parts[1], out short color) && !string.IsNullOrEmpty(parts[2]))
                    {
                        string layerName = parts[2];
                        FTTHBasemap.HompassTypeManager.ActiveLabel = label;
                        FTTHBasemap.HompassTypeManager.ActiveColor = color;
                        FTTHBasemap.HompassTypeManager.ActiveLayer = layerName;
                        SendCommandToAutoCAD("FTTH_TAG_HOMPASS");
                    }
                }
            }
        }

        private void BtnExtractTk_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_EXTRACT_TK");
        }

        private void BtnRestoreNamaHp_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("RESTORENAMAHP");
        }

        private void BtnGenPoleLabel_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_POLE_LABEL");
        }

        private void BtnPickFdtOrder_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_PICK_FDT_ORDER");
        }

        private void BtnResetFdtOrder_Click(object sender, RoutedEventArgs e)
        {
            LabelingManager.ManualFdtOrderHandles.Clear();
            UpdateFdtOrderLabel("Default (Spasial)");
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage("\n[FTTH] Pengaturan urutan FDT manual di-reset.");
            }
        }

        public void UpdateFdtOrderLabel(string text)
        {
            TxtFdtOrderState.Text = text;
        }

        private void BtnGenFatLabel_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_FAT_LABEL");
        }

        public double GetFatTableOltPower() => double.TryParse(TxtFatTableOltPower.Text, out double val) ? val : 3.0;
        public double GetFatTableFiberAtt() => double.TryParse(TxtFatTableFiberAtt.Text, out double val) ? val : 0.35;
        public double GetFatTableFdtSplitter() => double.TryParse(TxtFatTableFdtSplitter.Text, out double val) ? val : 7.25;
        public double GetFatTableFatSplitter() => double.TryParse(TxtFatTableFatSplitter.Text, out double val) ? val : 10.38;
        public double GetFatTableSpliceLoss() => double.TryParse(TxtFatTableSpliceLoss.Text, out double val) ? val : 0.15;
        public double GetFatTableConnLoss() => double.TryParse(TxtFatTableConnLoss.Text, out double val) ? val : 0.50;
        public double GetFatTableFatSlack() => double.TryParse(TxtFatTableFatSlack.Text, out double val) ? val : 20.0;
        public double GetFatTableFdtSlack() => double.TryParse(TxtFatTableFdtSlack.Text, out double val) ? val : 20.0;

        private void BtnGenFatTable_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_FAT_TABLE");
        }

        private void BtnGroupFatTables_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GROUP_FAT_TABLES");
        }

        private void BtnUngroupFatTables_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_UNGROUP_FAT_TABLES");
        }

        private void BtnGenFdtLabel_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_FDT_LABEL");
        }

        private void BtnGenFdtTable_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_FDT_TABLE");
        }

        private void BtnGenCableLabel_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_CABLE_LABEL");
        }

        private void BtnGenSpanLabels_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_SPAN_LABELS");
        }

        private void BtnCalculateSummary_Click(object sender, RoutedEventArgs e)
        {
            SummaryManager.CalculateAndShowSummary(this);
        }

        private void BtnGenSummaryInCad_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_GEN_SUMMARY");
        }

        private void RadCableScenario_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelCableScenario2Params == null) return;
            if (RadCableScenario2.IsChecked == true)
            {
                PanelCableScenario2Params.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                PanelCableScenario2Params.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void RadSummaryScenario_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelSummaryScenario2Params == null) return;
            if (RadSummaryScenario2.IsChecked == true)
            {
                PanelSummaryScenario2Params.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                PanelSummaryScenario2Params.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnManageOltCodes_Click(object sender, RoutedEventArgs e)
        {
            var settings = BasemapSettings.Instance;

            // Create a small window
            var win = new Window
            {
                Title = "Kelola Daftar Kode OLT",
                Width = 450,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E22")),
                Foreground = System.Windows.Media.Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true
            };

            // Main layout grid
            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ListBox for current codes
            var listBox = new ListBox
            {
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A30")),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#45475A"))
            };

            // Populate list box
            void PopulateList()
            {
                listBox.Items.Clear();
                if (settings.OltCodes != null)
                {
                    foreach (var entry in settings.OltCodes)
                    {
                        listBox.Items.Add($"{entry.Code} - {entry.Description}");
                    }
                }
            }
            PopulateList();

            grid.Children.Add(listBox);

            // Bottom control panel for Add/Delete/Close
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnAdd = new Button
            {
                Content = "Tambah OLT",
                Width = 90,
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#313244")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#45475A"))
            };

            var btnDelete = new Button
            {
                Content = "Hapus",
                Width = 70,
                Height = 28,
                Margin = new Thickness(0, 0, 5, 0),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F38BA8")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Transparent
            };

            var btnClose = new Button
            {
                Content = "Selesai",
                Width = 70,
                Height = 28,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007ACC")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Transparent
            };

            btnPanel.Children.Add(btnAdd);
            btnPanel.Children.Add(btnDelete);
            btnPanel.Children.Add(btnClose);

            Grid.SetRow(btnPanel, 1);
            grid.Children.Add(btnPanel);

            win.Content = grid;

            // Handlers
            btnAdd.Click += (s, ev) =>
            {
                var addWin = new Window
                {
                    Title = "Tambah Kode OLT Baru",
                    Width = 350,
                    SizeToContent = SizeToContent.Height,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E22")),
                    Foreground = System.Windows.Media.Brushes.White,
                    ResizeMode = ResizeMode.NoResize,
                    Topmost = true
                };

                var addStack = new StackPanel { Margin = new Thickness(15) };

                var lblCode = new Label { Content = "Kode OLT (Contoh: MLG.100.1401):", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 2) };
                var txtCode = new TextBox { Height = 26, Margin = new Thickness(0, 0, 0, 10), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A30")), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#45475A")), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 0, 0) };

                var lblDesc = new Label { Content = "Deskripsi / Nama OLT:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 2) };
                var txtDesc = new TextBox { Height = 26, Margin = new Thickness(0, 0, 0, 15), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A30")), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#45475A")), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 0, 0) };

                var addBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var btnSave = new Button { Content = "Simpan", Width = 75, Height = 28, Margin = new Thickness(0, 0, 8, 0), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007ACC")), Foreground = System.Windows.Media.Brushes.White, BorderBrush = System.Windows.Media.Brushes.Transparent };
                var btnCancel = new Button { Content = "Batal", Width = 75, Height = 28, Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#313244")), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#45475A")) };

                addBtnPanel.Children.Add(btnSave);
                addBtnPanel.Children.Add(btnCancel);

                addStack.Children.Add(lblCode);
                addStack.Children.Add(txtCode);
                addStack.Children.Add(lblDesc);
                addStack.Children.Add(txtDesc);
                addStack.Children.Add(addBtnPanel);

                addWin.Content = addStack;

                btnCancel.Click += (s2, ev2) => addWin.Close();
                btnSave.Click += (s2, ev2) =>
                {
                    string code = txtCode.Text.Trim();
                    string desc = txtDesc.Text.Trim();
                    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(desc))
                    {
                        MessageBox.Show("Kode OLT dan Deskripsi tidak boleh kosong!", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (settings.OltCodes.Any(o => o.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("Kode OLT sudah terdaftar!", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    settings.OltCodes.Add(new OltEntry(code, desc));
                    settings.Save();
                    PopulateList();

                    // Update main dropdown
                    CmbLabelFdtOltCode.Items.Clear();
                    foreach (var entry in settings.OltCodes)
                    {
                        CmbLabelFdtOltCode.Items.Add($"{entry.Code} - {entry.Description}");
                    }
                    CmbLabelFdtOltCode.SelectedItem = $"{code} - {desc}";

                    addWin.Close();
                };

                addWin.ShowDialog();
            };

            btnDelete.Click += (s, ev) =>
            {
                if (listBox.SelectedItem == null)
                {
                    MessageBox.Show("Pilih kode OLT yang ingin dihapus!", "Peringatan", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string selectedItemText = listBox.SelectedItem.ToString();
                string code = selectedItemText.Split(new string[] { " - " }, StringSplitOptions.None)[0].Trim();

                var entryToDelete = settings.OltCodes.FirstOrDefault(o => o.Code == code);
                if (entryToDelete != null)
                {
                    settings.OltCodes.Remove(entryToDelete);
                    settings.Save();
                    PopulateList();

                    // Update main dropdown
                    CmbLabelFdtOltCode.Items.Clear();
                    foreach (var entry in settings.OltCodes)
                    {
                        CmbLabelFdtOltCode.Items.Add($"{entry.Code} - {entry.Description}");
                    }
                    if (CmbLabelFdtOltCode.Items.Count > 0) CmbLabelFdtOltCode.SelectedIndex = 0;
                }
            };

            btnClose.Click += (s, ev) => win.Close();

            win.ShowDialog();
        }

        public string GetLabelPoleLocationCode()
        {
            return TxtLabelPoleLocationCode.Text;
        }

        public int GetLabelPoleStartIndex()
        {
            if (int.TryParse(TxtLabelPoleStartIndex.Text, out int result))
            {
                return result;
            }
            return 1;
        }

        public bool IsLabelPoleNew()
        {
            return RadLabelPoleNew.IsChecked == true;
        }

        public bool IsLabelPoleScenarioConstant()
        {
            return RadLabelPoleScenario1.IsChecked == true;
        }

        public bool IsLabelPoleScenarioLineTrace()
        {
            return RadLabelPoleScenario3.IsChecked == true;
        }

        public string GetLabelFatPrefix()
        {
            return TxtLabelFatPrefix.Text;
        }

        public int GetLabelFatStartIndex()
        {
            if (int.TryParse(TxtLabelFatStart.Text, out int result))
            {
                return result;
            }
            return 1;
        }

        public string GetLabelFdtSelectedOltCode()
        {
            string selectedText = "";
            if (CmbLabelFdtOltCode != null)
            {
                selectedText = CmbLabelFdtOltCode.Text;
            }
            if (string.IsNullOrEmpty(selectedText))
            {
                return "MLG.100.0201";
            }
            if (selectedText.Contains(" - "))
            {
                return selectedText.Split(new string[] { " - " }, StringSplitOptions.None)[0].Trim();
            }
            return selectedText.Trim();
        }

        public int GetCableScenario()
        {
            if (RadCableScenario2 != null && RadCableScenario2.IsChecked == true)
            {
                return 2;
            }
            return 1;
        }

        public double GetCableTolerance()
        {
            if (TxtCableTolerance != null && double.TryParse(TxtCableTolerance.Text, out double result))
            {
                return result;
            }
            return 5.0;
        }



        public int GetCableSlackFdt()
        {
            if (TxtCableSlackFdt != null && int.TryParse(TxtCableSlackFdt.Text, out int result))
            {
                return result;
            }
            return 1;
        }

        public double GetSpanTextHeight()
        {
            if (TxtSpanTextHeight != null && double.TryParse(TxtSpanTextHeight.Text, out double result))
            {
                return result;
            }
            return 1.0;
        }

        public string GetSummaryClusterName()
        {
            return TxtSummaryClusterName != null ? TxtSummaryClusterName.Text.Trim() : "";
        }

        public string GetSummaryAddress()
        {
            return TxtSummaryAddress != null ? TxtSummaryAddress.Text.Trim() : "";
        }

        public void SetSummaryDetails(string text)
        {
            if (TxtSummaryDetails != null)
            {
                TxtSummaryDetails.Text = text;
            }
        }

        public int GetSummaryCableScenario()
        {
            if (RadSummaryScenario2 != null && RadSummaryScenario2.IsChecked == true)
            {
                return 2;
            }
            return 1;
        }

        public int GetSummarySlackFdt()
        {
            if (TxtSummarySlackFdt != null && int.TryParse(TxtSummarySlackFdt.Text, out int result))
            {
                return result;
            }
            return 1;
        }

        public double GetSummaryTolerance()
        {
            if (TxtSummaryTolerance != null && double.TryParse(TxtSummaryTolerance.Text, out double result))
            {
                return result;
            }
            return 5.0;
        }

        // --- APD/ABD Exporter Implementation ---

        private void InitializeApdTab()
        {
            try
            {
                EnsureApdLineExists("LINE A");
                CmbApdLine.SelectedIndex = 0;
                ApdRefreshFdtCombo();
                UpdateApdHpCoverCombo();
                ApdRefreshTreeView();
            }
            catch { }
        }

        private string GetSelectedFdtName()
        {
            if (CmbApdFdt == null) return "FDT terpilih akan dilist disini";
            if (CmbApdFdt.SelectedItem is ComboBoxItem cbi)
            {
                return cbi.Content?.ToString()?.Trim() ?? "FDT terpilih akan dilist disini";
            }
            string selected = CmbApdFdt.SelectedItem?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(selected)) return selected;
            
            // Fallback: construct from active FDT's index
            if (ApdModel != null && ApdModel.Fdt != null && ApdModel.Fdts != null)
            {
                int idx = ApdModel.Fdts.IndexOf(ApdModel.Fdt);
                if (idx >= 0)
                {
                    return $"{ApdModel.Fdt.Name} FDT {idx + 1}";
                }
            }
            return "FDT terpilih akan dilist disini";
        }

        private string GetApdLineName(string baseLineName)
        {
            if (ApdModel != null && ApdModel.Fdts != null && ApdModel.Fdts.Count > 1)
            {
                string fdtName = GetSelectedFdtName(); // e.g. "BDL4.062 FDT 1" or "FDT terpilih..."
                
                // Extract friendly name index format "FDT X" from the selected name
                string friendlyFdt = "FDT 1";
                var match = System.Text.RegularExpressions.Regex.Match(fdtName, @"FDT\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    friendlyFdt = match.Value.ToUpper();
                }
                else if (ApdModel.Fdt != null)
                {
                    int idx = ApdModel.Fdts.IndexOf(ApdModel.Fdt);
                    if (idx >= 0) friendlyFdt = $"FDT {idx + 1}";
                }
                
                return $"{baseLineName} - {friendlyFdt}";
            }
            return baseLineName;
        }

        private void EnsureApdLineExists(string baseLineName)
        {
            if (ApdModel == null) ApdModel = new ExportModel();
            string lineName = GetApdLineName(baseLineName);
            if (ApdModel.Lines == null) ApdModel.Lines = new List<LineData>();
            if (!ApdModel.Lines.Any(l => l.LineName.Equals(lineName, StringComparison.OrdinalIgnoreCase)))
            {
                ApdModel.Lines.Add(new LineData 
                { 
                    LineName = lineName, 
                    FdtHandle = ApdModel.Fdt?.EntityHandle ?? "",
                    Fats = new List<FatData>(), 
                    Poles = new List<EntityItem>(), 
                    DistributionCables = new List<EntityItem>(), 
                    SlackHangers = new List<EntityItem>(), 
                    SlingWires = new List<EntityItem>(), 
                    Closures = new List<EntityItem>(), 
                    CustomMarkers = new List<EntityItem>() 
                });
            }
        }

        private LineData CurrentApdLine
        {
            get
            {
                string baseLineName = "LINE A";
                if (CmbApdLine != null && CmbApdLine.SelectedItem != null)
                {
                    baseLineName = (CmbApdLine.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "LINE A";
                }
                EnsureApdLineExists(baseLineName);
                string lineName = GetApdLineName(baseLineName);
                return ApdModel.Lines.First(l => l.LineName.Equals(lineName, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void CmbApdFdt_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFdtCombo) return;
            if (CmbApdFdt == null || CmbApdFdt.SelectedItem == null) return;
            
            string fdtName = GetSelectedFdtName(); // e.g. "BDL4.062 FDT 1"
            
            // Extract the friendly name index (e.g. from "FDT 1")
            int index = 0;
            var match = System.Text.RegularExpressions.Regex.Match(fdtName, @"FDT\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int idxVal))
            {
                index = idxVal - 1;
            }

            if (ApdModel != null && ApdModel.Fdts != null && index >= 0 && index < ApdModel.Fdts.Count)
            {
                ApdModel.Fdt = ApdModel.Fdts[index];
            }

            // Since FDT changed, the resolved CurrentApdLine changes, so we must refresh UI
            UpdateApdHpCoverCombo();
            ApdRefreshTreeView();
        }

        private void ApdRefreshFdtCombo()
        {
            if (CmbApdFdt == null) return;
            
            _isUpdatingFdtCombo = true;
            try
            {
                CmbApdFdt.Items.Clear();
                
                int count = 0;
                if (ApdModel != null && ApdModel.Fdts != null)
                {
                    count = ApdModel.Fdts.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var fdt = ApdModel.Fdts[i];
                        string displayText = $"{fdt.Name} FDT {i + 1}";
                        CmbApdFdt.Items.Add(new ComboBoxItem { Content = displayText });
                    }
                }
                
                if (CmbApdFdt.Items.Count == 0)
                {
                    CmbApdFdt.Items.Add(new ComboBoxItem { Content = "FDT terpilih akan dilist disini" });
                }
                
                // Find which index to select
                int selectIndex = 0;
                if (ApdModel != null && ApdModel.Fdt != null && ApdModel.Fdts != null)
                {
                    int idx = ApdModel.Fdts.IndexOf(ApdModel.Fdt);
                    if (idx >= 0) selectIndex = idx;
                }
                
                if (selectIndex >= 0 && selectIndex < CmbApdFdt.Items.Count)
                {
                    CmbApdFdt.SelectedIndex = selectIndex;
                }
            }
            finally
            {
                _isUpdatingFdtCombo = false;
            }
        }

        private void UpdateApdHpCoverCombo()
        {
            if (CmbApdHpCoverFat == null) return;
            CmbApdHpCoverFat.Items.Clear();
            var curLine = CurrentApdLine;
            if (curLine != null && curLine.Fats != null)
            {
                foreach (var fat in curLine.Fats)
                {
                    CmbApdHpCoverFat.Items.Add(fat.SequenceName);
                }
            }
            if (CmbApdHpCoverFat.Items.Count > 0)
            {
                CmbApdHpCoverFat.SelectedIndex = 0;
            }
        }

        private string GetApdLinePrefix()
        {
            if (CmbApdLine == null || CmbApdLine.SelectedItem == null) return "A";
            string lineName = (CmbApdLine.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "LINE A";
            if (lineName.Length > 0)
            {
                return lineName.Substring(lineName.Length - 1, 1).ToUpper();
            }
            return "A";
        }

        private void ApdHideAndExecute(Action action)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    action();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError: {ex.Message}");
            }
            finally
            {
                ApdRefreshTreeView();
            }
        }

        private void ApdRefreshTreeView()
        {
            if (TreeApdHierarchy == null) return;
            TreeApdHierarchy.Items.Clear();

            TreeViewItem rootNode = new TreeViewItem { Header = string.IsNullOrEmpty(ApdModel.Fdt.Name) ? "FDT: Not Set" : $"FDT: {ApdModel.Fdt.Name} ({ApdModel.Fdt.CapacityText})" };
            TreeApdHierarchy.Items.Add(rootNode);

            foreach (var line in ApdModel.Lines)
            {
                if (line.Fats.Count == 0 && line.Poles.Count == 0 && line.DistributionCables.Count == 0 && line.SlackHangers.Count == 0 && line.SlingWires.Count == 0 && (line.CustomMarkers == null || line.CustomMarkers.Count == 0))
                    continue;

                TreeViewItem lineNode = new TreeViewItem { Header = line.LineName };
                rootNode.Items.Add(lineNode);

                foreach (var fat in line.Fats)
                {
                    TreeViewItem fatNode = new TreeViewItem { Header = $"FAT {fat.SequenceName}: {fat.RawText}" };
                    lineNode.Items.Add(fatNode);

                    if (fat.HpCovers.Count > 0)
                    {
                        fatNode.Items.Add(new TreeViewItem { Header = $"HP Covers ({fat.HpCovers.Count})" });
                    }
                    if (fat.BoundaryPoints.Count > 0)
                    {
                        fatNode.Items.Add(new TreeViewItem { Header = $"Boundary (1)" });
                    }
                }

                if (line.Poles.Count > 0)
                {
                    TreeViewItem polesNode = new TreeViewItem { Header = $"Poles ({line.Poles.Count})" };
                    var groupedPoles = line.Poles.GroupBy(p => p.Category);
                    foreach (var group in groupedPoles)
                    {
                        polesNode.Items.Add(new TreeViewItem { Header = $"{group.Key} ({group.Count()})" });
                    }
                    lineNode.Items.Add(polesNode);
                }
                if (line.DistributionCables.Count > 0) lineNode.Items.Add(new TreeViewItem { Header = $"Dist. Cables ({line.DistributionCables.Count})" });
                if (line.SlackHangers.Count > 0) lineNode.Items.Add(new TreeViewItem { Header = $"Slack Hangers ({line.SlackHangers.Count})" });
                if (line.SlingWires.Count > 0) lineNode.Items.Add(new TreeViewItem { Header = $"Sling Wires ({line.SlingWires.Count})" });
                if (line.Closures.Count > 0) lineNode.Items.Add(new TreeViewItem { Header = $"Closures ({line.Closures.Count})" });
                if (line.CustomMarkers != null && line.CustomMarkers.Count > 0)
                {
                    TreeViewItem customNode = new TreeViewItem { Header = $"Handholes & Trenching ({line.CustomMarkers.Count})" };
                    var groupedCustom = line.CustomMarkers.GroupBy(c => c.Category);
                    foreach (var group in groupedCustom)
                    {
                        customNode.Items.Add(new TreeViewItem { Header = $"{group.Key} ({group.Count()})" });
                    }
                    lineNode.Items.Add(customNode);
                }
            }

            foreach (var item in TreeApdHierarchy.Items)
            {
                ApdExpandAll((TreeViewItem)item);
            }
        }

        private void ApdExpandAll(TreeViewItem item)
        {
            item.IsExpanded = true;
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem)
                    ApdExpandAll(childItem);
            }
        }

        private void CmbApdLine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbApdLine == null || CmbApdLine.SelectedItem == null) return;
            string lineName = (CmbApdLine.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "LINE A";
            EnsureApdLineExists(lineName);
            UpdateApdHpCoverCombo();

            // Sync FDT to UI ComboBox and ApdModel.Fdt when switching lines
            if (ApdModel != null && !string.IsNullOrEmpty(CurrentApdLine.FdtHandle) && ApdModel.Fdts != null)
            {
                var matchedFdt = ApdModel.Fdts.FirstOrDefault(f => f.EntityHandle == CurrentApdLine.FdtHandle);
                if (matchedFdt != null)
                {
                    int matchedIndex = ApdModel.Fdts.IndexOf(matchedFdt);
                    if (matchedIndex >= 0 && matchedIndex < CmbApdFdt.Items.Count)
                    {
                        CmbApdFdt.SelectionChanged -= CmbApdFdt_SelectionChanged;
                        CmbApdFdt.SelectedIndex = matchedIndex;
                        CmbApdFdt.SelectionChanged += CmbApdFdt_SelectionChanged;
                    }

                    ApdModel.Fdt = matchedFdt;
                }
            }
        }

        private void BtnApdSaveProject_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "FTTH Project File|*.ftthproj";
            string fdtName = string.IsNullOrEmpty(ApdModel?.Fdt?.Name) ? "Project" : ApdModel.Fdt.Name;
            dlg.FileName = $"{fdtName}.ftthproj";
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string json = JsonConvert.SerializeObject(ApdModel, Formatting.Indented);
                    File.WriteAllText(dlg.FileName, json);
                    MessageBox.Show("Project saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnApdLoadProject_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "FTTH Project File|*.ftthproj";
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dlg.FileName);
                    var loadedModel = JsonConvert.DeserializeObject<ExportModel>(json);
                    
                    if (loadedModel != null)
                    {
                        ApdModel = loadedModel;

                        // Sync FDT to the current line's FDT if it exists in multi-FDT model
                        string baseLineName = (CmbApdLine.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "LINE A";
                        string lineName = GetApdLineName(baseLineName);
                        var curLine = ApdModel.Lines?.FirstOrDefault(l => l.LineName.Equals(lineName, StringComparison.OrdinalIgnoreCase));
                        if (curLine != null && !string.IsNullOrEmpty(curLine.FdtHandle) && ApdModel.Fdts != null)
                        {
                            var matchedFdt = ApdModel.Fdts.FirstOrDefault(f => f.EntityHandle == curLine.FdtHandle);
                            if (matchedFdt != null)
                            {
                                ApdModel.Fdt = matchedFdt;
                            }
                        }

                        ApdRefreshFdtCombo();
                        UpdateApdHpCoverCombo();
                        ApdRefreshTreeView();
                        MessageBox.Show("Project loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnApdPickFDT_Click(object sender, RoutedEventArgs e)
        {
            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                PromptEntityOptions peo = new PromptEntityOptions("\nSelect FDT Text: ");
                peo.SetRejectMessage("\nPlease select a Text or MText.");
                peo.AddAllowedClass(typeof(DBText), true);
                peo.AddAllowedClass(typeof(MText), true);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK)
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        Point3d pos;
                        string text = "";
                        if (ent is DBText dbText)
                        {
                            text = dbText.TextString;
                            pos = dbText.Position;
                        }
                        else
                        {
                            MText mText = (MText)ent;
                            text = mText.Text;
                            pos = mText.Location;
                        }

                        Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, pos);

                        // Clean MText formatting and parse FDT Name
                        string cleaned = text.Replace("\\P", "\n").Replace("\\p", "\n").Replace("\\N", "\n").Replace("\\n", "\n");
                        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\\[CcHhFfAaTtiWwQq]\d+(\.\d+)?;?", "");
                        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\\f[^;]+;", "");
                        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[{}]", "");
                        cleaned = cleaned.Trim();

                        string[] lines = cleaned.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        string parsedFdtName = "";
                        if (lines.Length > 0)
                        {
                            // Clean each line
                            for (int i = 0; i < lines.Length; i++)
                            {
                                lines[i] = lines[i].Trim();
                            }

                            // Prioritize the FDT Code on the first line (e.g. BDL4.062 or BDL4.063)
                            string firstLine = lines[0];
                            bool isFdtKeyword = firstLine.StartsWith("FDT", StringComparison.OrdinalIgnoreCase);
                            bool isCapacity = System.Text.RegularExpressions.Regex.IsMatch(firstLine, @"^\d+[cC]$");
                            
                            if (!isFdtKeyword && !isCapacity && firstLine.Length > 1)
                            {
                                parsedFdtName = firstLine;
                            }
                            else
                            {
                                // Fallback: search for "FDT X" or "FDT [something]" in any line
                                foreach (var l in lines)
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(l, @"FDT\s*[\w\.\-]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (match.Success)
                                    {
                                        parsedFdtName = match.Value;
                                        break;
                                    }
                                }
                                
                                // If still not found, fallback to the first line or cleaned text
                                if (string.IsNullOrEmpty(parsedFdtName))
                                {
                                    parsedFdtName = firstLine;
                                }
                            }
                        }

                        // Normalize casing if it starts with FDT
                        if (parsedFdtName.StartsWith("fdt", StringComparison.OrdinalIgnoreCase))
                        {
                            var rest = parsedFdtName.Substring(3).Trim();
                            parsedFdtName = $"FDT {rest}";
                        }

                        // Create/Update a new FDT Node
                        var newFdt = new FdtNode
                        {
                            Name = parsedFdtName,
                            CapacityText = text,
                            EntityHandle = ent.Handle.ToString(),
                            Longitude = latLon.X,
                            Latitude = latLon.Y,
                            Layer = ent.Layer,
                            Rotation = (ent is DBText dbTextObj1) ? dbTextObj1.Rotation : ((ent is MText mTextObj1) ? mTextObj1.Rotation : 0),
                            TextHeight = (ent is DBText dbTextObj2) ? dbTextObj2.Height : ((ent is MText mTextObj2) ? mTextObj2.TextHeight : 0)
                        };
                        ApdModel.Fdt = newFdt;

                        // Multi-FDT Support: Add/Update in ApdModel.Fdts list
                        if (ApdModel.Fdts == null) ApdModel.Fdts = new List<FdtNode>();
                        var existingIndex = ApdModel.Fdts.FindIndex(f => f.EntityHandle == ent.Handle.ToString());
                        if (existingIndex >= 0)
                        {
                            ApdModel.Fdts[existingIndex] = newFdt;
                        }
                        else
                        {
                            ApdModel.Fdts.Add(newFdt);
                        }

                        // Link current selected line to this FDT
                        CurrentApdLine.FdtHandle = ent.Handle.ToString();

                        ApdRefreshFdtCombo();

                        ent.Highlight();
                        TxtApdStatus.Text = $"FDT Selected: {parsedFdtName} (Label: {cleaned.Replace("\n", " ")})";
                        tr.Commit();
                    }
                }
            });
        }

        private void BtnApdAddBoundary_Click(object sender, RoutedEventArgs e)
        {
            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                int startIndex = CurrentApdLine.Fats.Count + 1;
                string prefix = GetApdLinePrefix();
                while (true)
                {
                    PromptEntityOptions peo = new PromptEntityOptions($"\nSelect Boundary FAT (Polyline) for sequence {prefix}{startIndex:D2} [Undo/Exit] <Exit>: ");
                    peo.SetRejectMessage("\nPlease select a Polyline.");
                    peo.AddAllowedClass(typeof(Polyline), true);
                    peo.AllowNone = true;
                    peo.Keywords.Add("Undo");
                    peo.Keywords.Add("Exit");

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.Keyword)
                    {
                        if (per.StringResult.Equals("Undo", StringComparison.OrdinalIgnoreCase))
                        {
                            if (CurrentApdLine.Fats.Count > 0)
                            {
                                var lastFat = CurrentApdLine.Fats.Last();
                                try
                                {
                                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                                    {
                                        long handleLong = long.Parse(lastFat.BoundaryHandle, System.Globalization.NumberStyles.HexNumber);
                                        Handle h = new Handle(handleLong);
                                        ObjectId lastId = doc.Database.GetObjectId(false, h, 0);
                                        var poly = tr.GetObject(lastId, OpenMode.ForWrite) as Polyline;
                                        if (poly != null) poly.Unhighlight();
                                        tr.Commit();
                                    }
                                }
                                catch {}
                                string removedName = lastFat.SequenceName;
                                CurrentApdLine.Fats.RemoveAt(CurrentApdLine.Fats.Count - 1);
                                startIndex = CurrentApdLine.Fats.Count + 1;
                                ed.WriteMessage($"\nUndid last selection. Removed {removedName}.");
                            }
                            else
                            {
                                ed.WriteMessage("\nNo boundaries to undo.");
                            }
                            continue;
                        }
                        else if (per.StringResult.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                    if (per.Status != PromptStatus.OK) break;

                    string clickedHandle = per.ObjectId.Handle.ToString();
                    FatData existingFat = CurrentApdLine.Fats.FirstOrDefault(f => f.BoundaryHandle == clickedHandle);
                    if (existingFat != null)
                    {
                        // Toggle off (deselect)
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            Polyline poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForWrite);
                            poly.Unhighlight();
                            tr.Commit();
                        }
                        string removedSeqName = existingFat.SequenceName;
                        CurrentApdLine.Fats.Remove(existingFat);

                        // Renumber remaining Fats
                        for (int i = 0; i < CurrentApdLine.Fats.Count; i++)
                        {
                            CurrentApdLine.Fats[i].SequenceName = $"{prefix}{(i + 1):D2}";
                        }
                        startIndex = CurrentApdLine.Fats.Count + 1;

                        ed.WriteMessage($"\nRemoved Boundary {removedSeqName} (Deselected). Remaining boundaries re-sequenced.");
                        continue;
                    }

                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Polyline poly = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        poly.Highlight();
                        FatData fat = new FatData { SequenceName = $"{prefix}{startIndex:D2}", BoundaryLayer = poly.Layer, BoundaryHandle = poly.Handle.ToString(), BoundaryPoints = new List<(double Lon, double Lat)>(), HpCovers = new List<EntityItem>() };
                        
                        for (int i = 0; i < poly.NumberOfVertices; i++)
                        {
                            Point2d pt = poly.GetPoint2dAt(i);
                            Point3d wcsPt = new Point3d(pt.X, pt.Y, 0.0);
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, wcsPt);
                            fat.BoundaryPoints.Add((latLon.X, latLon.Y));
                        }

                        CurrentApdLine.Fats.Add(fat);
                        ed.WriteMessage($"\nAdded Boundary {fat.SequenceName}");
                        startIndex++;
                        tr.Commit();
                    }
                }
                UpdateApdHpCoverCombo();
                TxtApdStatus.Text = $"Added {CurrentApdLine.Fats.Count} Boundaries to {CurrentApdLine.LineName}";
            });
        }

        private void BtnApdAddFat_Click(object sender, RoutedEventArgs e)
        {
            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                int index = 0;
                // Find first unmapped FAT to start with
                for (int i = 0; i < CurrentApdLine.Fats.Count; i++)
                {
                    if (string.IsNullOrEmpty(CurrentApdLine.Fats[i].EntityHandle))
                    {
                        index = i;
                        break;
                    }
                    if (i == CurrentApdLine.Fats.Count - 1)
                    {
                        index = CurrentApdLine.Fats.Count;
                    }
                }

                while (true)
                {
                    string promptStr = "";
                    if (index < CurrentApdLine.Fats.Count)
                    {
                        promptStr = $"\nSelect FAT Text for {CurrentApdLine.Fats[index].SequenceName} [Undo/Exit] <Exit>: ";
                    }
                    else
                    {
                        promptStr = "\nAll sequences mapped. Click any mapped text to deselect, or press ENTER to finish [Undo/Exit] <Exit>: ";
                    }

                    PromptEntityOptions peo = new PromptEntityOptions(promptStr);
                    peo.SetRejectMessage("\nPlease select a Text or MText.");
                    peo.AddAllowedClass(typeof(DBText), true);
                    peo.AddAllowedClass(typeof(MText), true);
                    peo.AllowNone = true;
                    peo.Keywords.Add("Undo");
                    peo.Keywords.Add("Exit");

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.Keyword)
                    {
                        if (per.StringResult.Equals("Undo", StringComparison.OrdinalIgnoreCase))
                        {
                            if (index > 0)
                            {
                                index--;
                                FatData prevFat = CurrentApdLine.Fats[index];
                                if (!string.IsNullOrEmpty(prevFat.EntityHandle))
                                {
                                    try
                                    {
                                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                                        {
                                            long handleLong = long.Parse(prevFat.EntityHandle, System.Globalization.NumberStyles.HexNumber);
                                            Handle h = new Handle(handleLong);
                                            ObjectId lastId = doc.Database.GetObjectId(false, h, 0);
                                            var ent = tr.GetObject(lastId, OpenMode.ForWrite) as Entity;
                                            if (ent != null) ent.Unhighlight();
                                            tr.Commit();
                                        }
                                    }
                                    catch {}
                                }
                                
                                string prevName = prevFat.SequenceName;
                                prevFat.RawText = null;
                                prevFat.EntityHandle = null;
                                prevFat.FatLon = 0;
                                prevFat.FatLat = 0;
                                prevFat.Layer = null;
                                prevFat.Rotation = 0;
                                prevFat.TextHeight = 0;

                                ed.WriteMessage($"\nUndid mapping for {prevName}. Please re-select.");
                            }
                            else
                            {
                                ed.WriteMessage("\nNo mappings to undo.");
                            }
                            continue;
                        }
                        else if (per.StringResult.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }

                    if (index >= CurrentApdLine.Fats.Count && (per.Status == PromptStatus.None || per.Status == PromptStatus.Cancel))
                    {
                        break;
                    }

                    if (per.Status != PromptStatus.OK) break;

                    string clickedHandle = per.ObjectId.Handle.ToString();

                    // 1. If this text is already linked to ANY FAT, toggle/deselect it
                    FatData existingFat = CurrentApdLine.Fats.FirstOrDefault(f => f.EntityHandle == clickedHandle);
                    if (existingFat != null)
                    {
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForWrite);
                            ent.Unhighlight();
                            tr.Commit();
                        }
                        string removedSeqName = existingFat.SequenceName;
                        existingFat.RawText = null;
                        existingFat.EntityHandle = null;
                        existingFat.FatLon = 0;
                        existingFat.FatLat = 0;
                        existingFat.Layer = null;
                        existingFat.Rotation = 0;
                        existingFat.TextHeight = 0;

                        // Jump index back to this FAT's position so the user can re-select it
                        index = CurrentApdLine.Fats.IndexOf(existingFat);

                        ed.WriteMessage($"\nUnlinked FAT text from {removedSeqName} (Deselected). Jumped back to sequence {removedSeqName}.");
                        continue;
                    }

                    if (index >= CurrentApdLine.Fats.Count)
                    {
                        ed.WriteMessage("\nAll sequences are already mapped. Click a mapped text to deselect it first.");
                        continue;
                    }

                    FatData currentFat = CurrentApdLine.Fats[index];
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        ent.Highlight();
                        string text = ent is DBText dbt ? dbt.TextString : ((MText)ent).Text;
                        Point3d pos = ent is DBText db ? db.Position : ((MText)ent).Location;

                        Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, pos);
                        currentFat.RawText = text;
                        currentFat.EntityHandle = ent.Handle.ToString();
                        currentFat.FatLon = latLon.X;
                        currentFat.FatLat = latLon.Y;
                        currentFat.Layer = ent.Layer;
                        if (ent is DBText dbt2) { currentFat.Rotation = dbt2.Rotation; currentFat.TextHeight = dbt2.Height; }
                        else if (ent is MText mt2) { currentFat.Rotation = mt2.Rotation; currentFat.TextHeight = mt2.TextHeight; }

                        ed.WriteMessage($"\nLinked FAT {text} to {currentFat.SequenceName}");
                        
                        index++;
                        while (index < CurrentApdLine.Fats.Count && !string.IsNullOrEmpty(CurrentApdLine.Fats[index].EntityHandle))
                        {
                            index++;
                        }
                        
                        tr.Commit();
                    }
                }
                TxtApdStatus.Text = "Linked FAT Texts.";
            });
        }

        private void BtnApdAddHpCoverSequence_Click(object sender, RoutedEventArgs e)
        {
            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                int index = 0;
                // Find first unmapped FAT to start with
                for (int i = 0; i < CurrentApdLine.Fats.Count; i++)
                {
                    if (CurrentApdLine.Fats[i].HpCovers == null || CurrentApdLine.Fats[i].HpCovers.Count == 0)
                    {
                        index = i;
                        break;
                    }
                    if (i == CurrentApdLine.Fats.Count - 1)
                    {
                        index = CurrentApdLine.Fats.Count;
                    }
                }

                while (true)
                {
                    string promptStr = "";
                    if (index < CurrentApdLine.Fats.Count)
                    {
                        promptStr = $"\nSelect any Text in the GROUP for {CurrentApdLine.Fats[index].SequenceName} [Undo/Exit] <Exit>: ";
                    }
                    else
                    {
                        promptStr = "\nAll sequences mapped. Click any mapped text to deselect, or press ENTER to finish [Undo/Exit] <Exit>: ";
                    }

                    PromptEntityOptions peo = new PromptEntityOptions(promptStr);
                    peo.SetRejectMessage("\nPlease select a Text or MText.");
                    peo.AddAllowedClass(typeof(DBText), true);
                    peo.AddAllowedClass(typeof(MText), true);
                    peo.AllowNone = true;
                    peo.Keywords.Add("Undo");
                    peo.Keywords.Add("Exit");

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.Keyword)
                    {
                        if (per.StringResult.Equals("Undo", StringComparison.OrdinalIgnoreCase))
                        {
                            if (index > 0)
                            {
                                index--;
                                FatData prevFat = CurrentApdLine.Fats[index];
                                foreach (var hp in prevFat.HpCovers)
                                {
                                    try
                                    {
                                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                                        {
                                            long handleLong = long.Parse(hp.EntityHandle, System.Globalization.NumberStyles.HexNumber);
                                            Handle h = new Handle(handleLong);
                                            ObjectId hpId = doc.Database.GetObjectId(false, h, 0);
                                            var ent = tr.GetObject(hpId, OpenMode.ForWrite) as Entity;
                                            if (ent != null) ent.Unhighlight();
                                            tr.Commit();
                                        }
                                    }
                                    catch {}
                                }
                                string prevName = prevFat.SequenceName;
                                prevFat.HpCovers.Clear();
                                ed.WriteMessage($"\nUndid HP Cover mapping for {prevName}. Please re-select.");
                            }
                            else
                            {
                                ed.WriteMessage("\nNo mappings to undo.");
                            }
                            continue;
                        }
                        else if (per.StringResult.Equals("Exit", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }

                    if (index >= CurrentApdLine.Fats.Count && (per.Status == PromptStatus.None || per.Status == PromptStatus.Cancel))
                    {
                        break;
                    }

                    if (per.Status != PromptStatus.OK) break;

                    string clickedHandle = per.ObjectId.Handle.ToString();

                    // Gather handles that would be added
                    var newHpHandles = new HashSet<string>();
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        Autodesk.AutoCAD.DatabaseServices.Group targetGroup = null;
                        ObjectIdCollection reactorIds = ent.GetPersistentReactorIds();
                        if (reactorIds != null)
                        {
                            foreach (ObjectId id in reactorIds)
                            {
                                if (id.ObjectClass.Name == "AcDbGroup")
                                {
                                    targetGroup = tr.GetObject(id, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Group;
                                    break;
                                }
                            }
                        }
                        if (targetGroup != null)
                        {
                            foreach (ObjectId memberId in targetGroup.GetAllEntityIds())
                            {
                                newHpHandles.Add(memberId.Handle.ToString());
                            }
                        }
                        else
                        {
                            newHpHandles.Add(clickedHandle);
                        }
                    }

                    // 1. If any of these handles are already linked to ANY FAT, toggle/deselect it
                    FatData existingFat = CurrentApdLine.Fats.FirstOrDefault(f => f.HpCovers.Any(hp => newHpHandles.Contains(hp.EntityHandle)));
                    if (existingFat != null)
                    {
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            foreach (var hp in existingFat.HpCovers)
                            {
                                try
                                {
                                    long handleLong = long.Parse(hp.EntityHandle, System.Globalization.NumberStyles.HexNumber);
                                    Handle h = new Handle(handleLong);
                                    ObjectId hpId = doc.Database.GetObjectId(false, h, 0);
                                    var ent = tr.GetObject(hpId, OpenMode.ForWrite) as Entity;
                                    if (ent != null) ent.Unhighlight();
                                }
                                catch {}
                            }
                            tr.Commit();
                        }
                        string removedSeqName = existingFat.SequenceName;
                        int removedCount = existingFat.HpCovers.Count;
                        existingFat.HpCovers.Clear();

                        // Jump index back to this FAT's position so the user can re-select it
                        index = CurrentApdLine.Fats.IndexOf(existingFat);

                        ed.WriteMessage($"\nRemoved {removedCount} HP Covers from {removedSeqName} (Deselected). Jumped back to sequence {removedSeqName}.");
                        continue;
                    }

                    if (index >= CurrentApdLine.Fats.Count)
                    {
                        ed.WriteMessage("\nAll sequences are already mapped. Click a mapped text to deselect it first.");
                        continue;
                    }

                    FatData currentFat = CurrentApdLine.Fats[index];
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        
                        Autodesk.AutoCAD.DatabaseServices.Group targetGroup = null;
                        ObjectIdCollection reactorIds = ent.GetPersistentReactorIds();
                        if (reactorIds != null)
                        {
                            foreach (ObjectId id in reactorIds)
                            {
                                if (id.ObjectClass.Name == "AcDbGroup")
                                {
                                    targetGroup = tr.GetObject(id, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Group;
                                    break;
                                }
                            }
                        }

                        // 2. Prevent duplicate mapping on other FATs
                        int duplicateRemovedCount = 0;
                        foreach (var otherFat in CurrentApdLine.Fats)
                        {
                            if (otherFat == currentFat) continue;
                            int beforeCount = otherFat.HpCovers.Count;
                            var duplicates = otherFat.HpCovers.Where(hp => newHpHandles.Contains(hp.EntityHandle)).ToList();
                            if (duplicates.Count > 0)
                            {
                                foreach (var dup in duplicates)
                                {
                                    try
                                    {
                                        long handleLong = long.Parse(dup.EntityHandle, System.Globalization.NumberStyles.HexNumber);
                                        Handle h = new Handle(handleLong);
                                        ObjectId dupId = doc.Database.GetObjectId(false, h, 0);
                                        var memberEnt = tr.GetObject(dupId, OpenMode.ForWrite) as Entity;
                                        if (memberEnt != null) memberEnt.Unhighlight();
                                    }
                                    catch {}
                                }
                                otherFat.HpCovers.RemoveAll(hp => newHpHandles.Contains(hp.EntityHandle));
                                duplicateRemovedCount += (beforeCount - otherFat.HpCovers.Count);
                            }
                        }

                        if (duplicateRemovedCount > 0)
                        {
                            ed.WriteMessage($"\nRemoved {duplicateRemovedCount} duplicate HP Cover mappings from other FAT sequences.");
                        }

                        currentFat.HpCovers.Clear();

                        if (targetGroup != null)
                        {
                            ObjectId[] members = targetGroup.GetAllEntityIds();
                            foreach (ObjectId memberId in members)
                            {
                                Entity memberEnt = tr.GetObject(memberId, OpenMode.ForRead) as Entity;
                                memberEnt.Highlight();
                                if (memberEnt is DBText || memberEnt is MText)
                                {
                                    string text = memberEnt is DBText dbt ? dbt.TextString : ((MText)memberEnt).Text;
                                    Point3d pos = memberEnt is DBText db ? db.Position : ((MText)memberEnt).Location;
                                    double rot = memberEnt is DBText db2 ? db2.Rotation : ((MText)memberEnt).Rotation;
                                    double height = memberEnt is DBText db3 ? db3.Height : ((MText)memberEnt).TextHeight;
                                    Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, pos);
                                    
                                    currentFat.HpCovers.Add(new EntityItem
                                    {
                                        Category = "HP COVER",
                                        Name = text,
                                        EntityHandle = memberEnt.Handle.ToString(),
                                        Lon = latLon.X,
                                        Lat = latLon.Y,
                                        Layer = memberEnt.Layer,
                                        Rotation = rot,
                                        TextHeight = height,
                                        LineAffiliation = CurrentApdLine.LineName,
                                        SequenceName = currentFat.SequenceName
                                    });
                                }
                            }
                            ed.WriteMessage($"\nAdded {currentFat.HpCovers.Count} HP Covers from Group to {currentFat.SequenceName}");
                        }
                        else
                        {
                            ent.Highlight();
                            string text = ent is DBText dbt ? dbt.TextString : ((MText)ent).Text;
                            Point3d pos = ent is DBText db ? db.Position : ((MText)ent).Location;
                            double rot = ent is DBText db2 ? db2.Rotation : ((MText)ent).Rotation;
                            double height = ent is DBText db3 ? db3.Height : ((MText)ent).TextHeight;
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, pos);
                                    
                            currentFat.HpCovers.Add(new EntityItem
                            {
                                Category = "HP COVER",
                                Name = text,
                                EntityHandle = ent.Handle.ToString(),
                                Lon = latLon.X,
                                Lat = latLon.Y,
                                Layer = ent.Layer,
                                Rotation = rot,
                                TextHeight = height,
                                LineAffiliation = CurrentApdLine.LineName,
                                SequenceName = currentFat.SequenceName
                            });
                            ed.WriteMessage($"\nWarning: Object was not in a Group. Added 1 HP Cover to {currentFat.SequenceName}");
                        }
                        
                        index++;
                        while (index < CurrentApdLine.Fats.Count && CurrentApdLine.Fats[index].HpCovers != null && CurrentApdLine.Fats[index].HpCovers.Count > 0)
                        {
                            index++;
                        }
                        
                        tr.Commit();
                    }
                }
                TxtApdStatus.Text = "Finished HP Cover Sequence.";
            });
        }

        private void BtnApdResetSequences_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var curLine = CurrentApdLine;
            if (curLine == null || curLine.Fats == null || curLine.Fats.Count == 0)
            {
                TxtApdStatus.Text = "No sequence data to reset.";
                return;
            }

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to reset all {curLine.Fats.Count} sequences for {curLine.LineName}?",
                "Reset Sequences",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question
            );

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                ApdHideAndExecute(() =>
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (var fat in curLine.Fats)
                        {
                            // Unhighlight boundary polyline
                            if (!string.IsNullOrEmpty(fat.BoundaryHandle))
                            {
                                try
                                {
                                    long handleLong = long.Parse(fat.BoundaryHandle, System.Globalization.NumberStyles.HexNumber);
                                    Handle h = new Handle(handleLong);
                                    ObjectId polyId = doc.Database.GetObjectId(false, h, 0);
                                    var poly = tr.GetObject(polyId, OpenMode.ForWrite) as Polyline;
                                    if (poly != null) poly.Unhighlight();
                                }
                                catch {}
                            }

                            // Unhighlight FAT text
                            if (!string.IsNullOrEmpty(fat.EntityHandle))
                            {
                                try
                                {
                                    long handleLong = long.Parse(fat.EntityHandle, System.Globalization.NumberStyles.HexNumber);
                                    Handle h = new Handle(handleLong);
                                    ObjectId fatId = doc.Database.GetObjectId(false, h, 0);
                                    var ent = tr.GetObject(fatId, OpenMode.ForWrite) as Entity;
                                    if (ent != null) ent.Unhighlight();
                                }
                                catch {}
                            }

                            // Unhighlight HP cover texts
                            if (fat.HpCovers != null)
                            {
                                foreach (var hp in fat.HpCovers)
                                {
                                    try
                                    {
                                        long handleLong = long.Parse(hp.EntityHandle, System.Globalization.NumberStyles.HexNumber);
                                        Handle h = new Handle(handleLong);
                                        ObjectId hpId = doc.Database.GetObjectId(false, h, 0);
                                        var ent = tr.GetObject(hpId, OpenMode.ForWrite) as Entity;
                                        if (ent != null) ent.Unhighlight();
                                    }
                                    catch {}
                                }
                            }
                        }
                        tr.Commit();
                    }

                    curLine.Fats.Clear();
                    UpdateApdHpCoverCombo();
                    TxtApdStatus.Text = $"All sequences for {curLine.LineName} have been reset.";
                    ed.WriteMessage($"\n[FTTH] Resetted and cleared all sequence data for {curLine.LineName}.");
                });
            }
        }

        private void BtnApdPickHpCover_Click(object sender, RoutedEventArgs e)
        {
            if (CmbApdHpCoverFat.SelectedItem == null) return;
            string selectedFat = CmbApdHpCoverFat.SelectedItem.ToString();

            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = $"\nWindow-Select HP Cover texts for FAT {selectedFat}: ";
                
                TypedValue[] filterList = new TypedValue[] {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(filterList);

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    FatData fat = CurrentApdLine.Fats.First(f => f.SequenceName == selectedFat);
                    fat.HpCovers.Clear();

                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            Entity ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                            ent.Highlight();
                            string text = ent is DBText dbt ? dbt.TextString : ((MText)ent).Text;
                            Point3d pos = ent is DBText db ? db.Position : ((MText)ent).Location;
                            double rot = ent is DBText db2 ? db2.Rotation : ((MText)ent).Rotation;
                            double height = ent is DBText db3 ? db3.Height : ((MText)ent).TextHeight;
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, pos);

                            fat.HpCovers.Add(new EntityItem
                            {
                                Category = "HP COVER",
                                Name = text,
                                EntityHandle = ent.Handle.ToString(),
                                Lon = latLon.X,
                                Lat = latLon.Y,
                                Layer = ent.Layer,
                                Rotation = rot,
                                TextHeight = height,
                                LineAffiliation = CurrentApdLine.LineName,
                                SequenceName = fat.SequenceName
                            });
                        }
                        tr.Commit();
                    }
                    TxtApdStatus.Text = $"Added {fat.HpCovers.Count} HP Covers to {selectedFat}";
                }
            });
        }

        private void ApdAddPointEntities(string category, List<EntityItem> targetList)
        {
            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = $"\nSelect {category} texts/points: ";
                
                TypedValue[] filterList = new TypedValue[] {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(filterList);

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            Entity ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                            ent.Highlight();
                            string text = ent is DBText dbt ? dbt.TextString : ((MText)ent).Text;
                            Point3d pos = ent is DBText db ? db.Position : ((MText)ent).Location;
                            double rot = ent is DBText db2 ? db2.Rotation : ((MText)ent).Rotation;
                            double height = ent is DBText db3 ? db3.Height : ((MText)ent).TextHeight;
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, pos);

                            targetList.Add(new EntityItem
                            {
                                Category = category,
                                Name = text,
                                EntityHandle = ent.Handle.ToString(),
                                Lon = latLon.X,
                                Lat = latLon.Y,
                                Layer = ent.Layer,
                                Rotation = rot,
                                TextHeight = height,
                                LineAffiliation = CurrentApdLine.LineName
                            });
                        }
                        tr.Commit();
                    }
                    TxtApdStatus.Text = $"Added {psr.Value.Count} {category}";
                }
            });
        }

        private void ApdAddLineEntities(string category, List<EntityItem> targetList)
        {
            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = $"\nSelect {category} (Polylines): ";
                
                TypedValue[] filterList = new TypedValue[] {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                };
                SelectionFilter filter = new SelectionFilter(filterList);

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            Polyline poly = (Polyline)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                            poly.Highlight();
                            
                            string name = poly.Layer;
                            double length = Math.Round(poly.Length);

                            if (category == "SLING WIRE")
                            {
                                name = $"{length} m";
                            }
                            else if (category == "DISTRIBUTION CABLE")
                            {
                                name = $"{poly.Layer} {length} m";
                            }

                            EntityItem item = new EntityItem { 
                                Category = category, 
                                Name = name,
                                EntityHandle = poly.Handle.ToString(),
                                Layer = poly.Layer,
                                LineAffiliation = CurrentApdLine.LineName,
                                LinePoints = new List<(double Lon, double Lat)>()
                            };
                            for (int i = 0; i < poly.NumberOfVertices; i++)
                            {
                                Point2d pt = poly.GetPoint2dAt(i);
                                Point3d wcsPt = new Point3d(pt.X, pt.Y, 0.0);
                                Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, wcsPt);
                                item.LinePoints.Add((latLon.X, latLon.Y));
                            }
                            targetList.Add(item);
                        }
                        tr.Commit();
                    }
                    TxtApdStatus.Text = $"Added {psr.Value.Count} {category}";
                }
            });
        }

        private void BtnApdPickPoles_Click(object sender, RoutedEventArgs e)
        {
            if (CmbApdPoleType.SelectedItem == null) return;
            string poleType = (CmbApdPoleType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NEW POLE 7-2.5";
            ApdAddPointEntities(poleType, CurrentApdLine.Poles);
        }

        private void BtnApdAddCable_Click(object sender, RoutedEventArgs e)
        {
            ApdAddLineEntities("DISTRIBUTION CABLE", CurrentApdLine.DistributionCables);
        }

        private void BtnApdAddSlack_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Tekan YES untuk mengikuti titik FAT dan FDT secara otomatis, atau tekan NO untuk memilih titik object slack hanger secara manual.",
                "Smart Slack Hanger", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                CurrentApdLine.SlackHangers.Clear();

                if (!string.IsNullOrEmpty(ApdModel.Fdt.CapacityText))
                {
                    CurrentApdLine.SlackHangers.Add(new EntityItem
                    {
                        Category = "SLACK HANGER",
                        Name = "SLACK_FDT",
                        Lon = ApdModel.Fdt.Longitude,
                        Lat = ApdModel.Fdt.Latitude,
                        Layer = "SLACK HANGER",
                        LineAffiliation = CurrentApdLine.LineName
                    });
                }

                foreach (var fat in CurrentApdLine.Fats)
                {
                    if (!string.IsNullOrEmpty(fat.RawText))
                    {
                        CurrentApdLine.SlackHangers.Add(new EntityItem
                        {
                            Category = "SLACK HANGER",
                            Name = $"SLACK_{fat.SequenceName}",
                            Lon = fat.FatLon,
                            Lat = fat.FatLat,
                            Layer = "SLACK HANGER",
                            LineAffiliation = CurrentApdLine.LineName
                        });
                    }
                }

                ApdRefreshTreeView();
                TxtApdStatus.Text = $"Auto-generated {CurrentApdLine.SlackHangers.Count} Slack Hangers.";
            }
            else if (result == MessageBoxResult.No)
            {
                ApdAddPointEntities("SLACK HANGER", CurrentApdLine.SlackHangers);
            }
        }

        private void BtnApdAddSling_Click(object sender, RoutedEventArgs e)
        {
            ApdAddLineEntities("SLING WIRE", CurrentApdLine.SlingWires);
        }

        private void BtnApdAddClosure_Click(object sender, RoutedEventArgs e)
        {
            ApdAddPointEntities("CLOSURE", CurrentApdLine.Closures);
        }

        private string ApdFindNearbyTextFast(Point3d pos, double searchRadius)
        {
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return null;
                Editor ed = doc.Editor;

                Point3d p1 = new Point3d(pos.X - searchRadius, pos.Y - searchRadius, 0);
                Point3d p2 = new Point3d(pos.X + searchRadius, pos.Y + searchRadius, 0);
                
                TypedValue[] filterList = new TypedValue[] {
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "or>")
                };
                SelectionFilter filter = new SelectionFilter(filterList);
                
                PromptSelectionResult psr = ed.SelectCrossingWindow(p1, p2, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        double minDistance = double.MaxValue;
                        string bestText = null;
                        foreach (SelectedObject so in psr.Value)
                        {
                            Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                Point3d textPos = (ent is DBText dbt) ? dbt.Position : ((MText)ent).Location;
                                double dist = pos.DistanceTo(textPos);
                                if (dist < minDistance)
                                {
                                    minDistance = dist;
                                    bestText = (ent is DBText dbt2) ? dbt2.TextString : ((MText)ent).Text;
                                }
                            }
                        }
                        tr.Commit();
                        return bestText;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private void BtnApdAddHandholesTrenching_Click(object sender, RoutedEventArgs e)
        {
            ApdHideAndExecute(() =>
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                Editor ed = doc.Editor;

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect Handhole blocks or Circle Tees [Press Enter when done]: ";
                
                TypedValue[] filterList = new TypedValue[] {
                    new TypedValue((int)DxfCode.Operator, "<not"),
                    new TypedValue((int)DxfCode.Operator, "<or"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Operator, "or>"),
                    new TypedValue((int)DxfCode.Operator, "not>")
                };
                SelectionFilter filter = new SelectionFilter(filterList);

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    int addedCount = 0;
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            Entity ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                            if (ent == null) continue;

                            Point3d pos = Point3d.Origin;
                            bool posValid = false;
                            
                            if (ent is BlockReference br)
                            {
                                pos = br.Position;
                                posValid = true;
                            }
                            else if (ent is Circle circle)
                            {
                                pos = circle.Center;
                                posValid = true;
                            }
                            else if (ent.Bounds.HasValue)
                            {
                                var bounds = ent.Bounds.Value;
                                pos = new Point3d(
                                    (bounds.MinPoint.X + bounds.MaxPoint.X) / 2.0,
                                    (bounds.MinPoint.Y + bounds.MaxPoint.Y) / 2.0,
                                    (bounds.MinPoint.Z + bounds.MaxPoint.Z) / 2.0
                                );
                                posValid = true;
                            }
                            
                            if (!posValid) continue;

                            ent.Highlight();
                            Point3d latLon = TileMapManager.ProjectWcsToLonLat(doc.Database, pos);
                            
                            string resolvedName = ApdFindNearbyTextFast(pos, 5.0);
                            
                            string blockName = "";
                            string attrCombined = "";
                            if (ent is BlockReference blockRef)
                            {
                                ObjectId btrId = blockRef.IsDynamicBlock ? blockRef.AnonymousBlockTableRecord : blockRef.BlockTableRecord;
                                using (BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead))
                                {
                                    blockName = btr.Name;
                                }
                                var attrs = new List<string>();
                                foreach (ObjectId attrId in blockRef.AttributeCollection)
                                {
                                    var attRef = tr.GetObject(attrId, OpenMode.ForRead) as AttributeReference;
                                    if (attRef != null && !string.IsNullOrWhiteSpace(attRef.TextString))
                                    {
                                        attrs.Add(attRef.TextString);
                                    }
                                }
                                if (attrs.Count > 0)
                                {
                                    attrCombined = string.Join(" ", attrs);
                                }
                            }
                            
                            string finalName = "";
                            if (!string.IsNullOrWhiteSpace(resolvedName))
                            {
                                finalName = resolvedName.Trim();
                            }
                            else if (!string.IsNullOrWhiteSpace(attrCombined))
                            {
                                finalName = attrCombined.Trim();
                            }
                            else if (ent is BlockReference && !string.IsNullOrWhiteSpace(blockName) && !blockName.StartsWith("*"))
                            {
                                finalName = blockName;
                            }

                            if (!string.IsNullOrEmpty(finalName))
                            {
                                if (finalName.IndexOf("HH_40", StringComparison.OrdinalIgnoreCase) >= 0)
                                    finalName = finalName.Replace("HH_40", "HH 40").Replace("hh_40", "HH 40");
                                if (finalName.IndexOf("UG_Pedestal", StringComparison.OrdinalIgnoreCase) >= 0)
                                    finalName = finalName.Replace("UG_Pedestal", "UG Pedestal").Replace("ug_pedestal", "UG Pedestal");
                                if (finalName.IndexOf("Small_HH20", StringComparison.OrdinalIgnoreCase) >= 0)
                                    finalName = finalName.Replace("Small_HH20", "HH20").Replace("small_hh20", "HH20");
                            }

                            if (blockName.Equals("HH_40", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrWhiteSpace(finalName) || finalName.Equals("HH_40", StringComparison.OrdinalIgnoreCase))
                                    finalName = "HH 40";
                            }
                            else if (blockName.Equals("UG_Pedestal", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrWhiteSpace(finalName) || finalName.Equals("UG_Pedestal", StringComparison.OrdinalIgnoreCase))
                                    finalName = "UG Pedestal";
                            }
                            else if (blockName.Equals("Small_HH20", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrWhiteSpace(finalName) || finalName.Equals("Small_HH20", StringComparison.OrdinalIgnoreCase))
                                    finalName = "HH20";
                            }

                            if (string.IsNullOrWhiteSpace(finalName))
                            {
                                if (ent is Circle)
                                {
                                    finalName = string.Format(System.Globalization.CultureInfo.InvariantCulture, "TEE ({0:F5}, {1:F5})", latLon.X, latLon.Y);
                                }
                                else
                                {
                                    finalName = string.Format(System.Globalization.CultureInfo.InvariantCulture, "HH ({0:F5}, {1:F5})", latLon.X, latLon.Y);
                                }
                            }
                            
                            string category = "NEW HH 40X40X60";
                            string searchStr = (finalName + " " + ent.Layer + " " + blockName + " " + attrCombined).ToUpper();
                            
                            if (ent is Circle || searchStr.Contains("TEE") || searchStr.Contains("TRENCHING") || searchStr.Contains("PIPA"))
                            {
                                category = "OPEN TRENCHING";
                            }
                            else if (searchStr.Contains("20X20") || searchStr.Contains("20") || searchStr.Contains("HH20"))
                            {
                                category = "NEW HH 20X20X20";
                            }
                            else if (searchStr.Contains("40X40") || searchStr.Contains("40") || searchStr.Contains("60") || searchStr.Contains("80") || searchStr.Contains("HH80") || searchStr.Contains("PEDESTAL"))
                            {
                                category = "NEW HH 40X40X60";
                            }
                            
                            double rotation = 0;
                            double height = 0.5;
                            if (ent is BlockReference bRef)
                            {
                                rotation = bRef.Rotation;
                            }
                            
                            if (CurrentApdLine.CustomMarkers == null) CurrentApdLine.CustomMarkers = new List<EntityItem>();
                            CurrentApdLine.CustomMarkers.Add(new EntityItem
                            {
                                Category = category,
                                Name = finalName,
                                EntityHandle = ent.Handle.ToString(),
                                Lon = latLon.X,
                                Lat = latLon.Y,
                                Layer = ent.Layer,
                                Rotation = rotation,
                                TextHeight = height,
                                LineAffiliation = CurrentApdLine.LineName
                            });
                            
                            addedCount++;
                        }
                        tr.Commit();
                    }
                    TxtApdStatus.Text = $"Successfully added {addedCount} Handholes / Trenching Tee markers.";
                }
            });
        }

        private void BtnApdExportKml_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration();
            string activeDwgName = "";
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    activeDwgName = Path.GetFileNameWithoutExtension(doc.Name);
                }
            }
            catch {}
            if (string.IsNullOrEmpty(activeDwgName)) activeDwgName = string.IsNullOrEmpty(ApdModel?.Fdt?.Name) ? "Export" : ApdModel.Fdt.Name;

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "KMZ File|*.kmz|KML File|*.kml";
            dlg.FileName = $"{activeDwgName}.kmz";
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    KmlGeneratorApdAbd gen = new KmlGeneratorApdAbd();
                    bool objectOnly = ChkApdObjectOnlyMode.IsChecked == true;
                    bool isSubfeeder = RbApdSubfeederMode.IsChecked == true;
                    
                    gen.Generate(ApdModel, dlg.FileName, objectOnly, isSubfeeder);
                    MessageBox.Show("Export Successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogMessage("KML APD/ABD export complete.");

                    // Calculate summary on main thread to avoid AutoCAD cross-threading transaction issues
                    double routeLength = 0;
                    int hompassCount = 0;
                    int poleCount = 0;
                    try
                    {
                        var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                        if (doc != null)
                        {
                            var summary = KmlSndKasarExporter.CalculateSummary(doc.Database);
                            if (summary != null)
                            {
                                routeLength = summary.TotalRouteLength;
                                hompassCount = summary.TotalHompass;
                                poleCount = summary.TotalPoles;
                            }
                        }
                    }
                    catch { }

                    // Post telemetry asynchronously in background
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            string fileName = System.IO.Path.GetFileName(dlg.FileName);
                            string dirPath = System.IO.Path.GetDirectoryName(dlg.FileName);
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

                    if (BasemapSettings.Instance.AutoOpenKml && File.Exists(dlg.FileName))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                        }
                        catch (Exception exStart)
                        {
                            var activeDoc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                            if (activeDoc != null)
                            {
                                activeDoc.Editor.WriteMessage($"\n[FTTH] Warning: Failed to automatically open KML file: {exStart.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogMessage("KML APD/ABD export failed: " + ex.Message);
                }
            }
        }

        private void BtnAutoExportKml_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration();
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            string defaultFdt = TxtAutoFdtName.Text;
            TxtAutoStatusLog.Text = "Scanning AutoCAD drawing database...\n";
            UpdateProgress(0, "Memulai ekspor KML...");

            try
            {
                ExportModel autoModel;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    // Run the automatic model generator with progress callback
                    autoModel = AutoKmlExporter.BuildExportModel(doc.Database, defaultFdt, (val, text) => {
                        UpdateProgress(val, text);
                    });
                }

                // Write a log summary to TxtAutoStatusLog
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("Scan completed successfully!");
                sb.AppendLine("----------------------------------------");
                sb.AppendLine($"FDT Name      : {autoModel.Fdt.Name} (Capacity: {autoModel.Fdt.CapacityText})");
                sb.AppendLine($"Total Lines   : {autoModel.Lines.Count}");
                foreach (var line in autoModel.Lines)
                {
                    sb.AppendLine($" - {line.LineName}:");
                    sb.AppendLine($"    * FATs         : {line.Fats.Count}");
                    sb.AppendLine($"    * Cables       : {line.DistributionCables.Count}");
                    sb.AppendLine($"    * Poles        : {line.Poles.Count}");
                    sb.AppendLine($"    * Closures     : {line.Closures.Count}");
                    sb.AppendLine($"    * Handholes    : {line.CustomMarkers.Count}");
                    sb.AppendLine($"    * Hangers      : {line.SlackHangers.Count}");
                    int totalHps = line.Fats.Sum(f => f.HpCovers.Count);
                    sb.AppendLine($"    * Homepasses   : {totalHps}");
                }
                TxtAutoStatusLog.Text = sb.ToString();

                // Check if anything was found
                if (autoModel.Lines.Count == 0 || (autoModel.Lines.Count == 1 && autoModel.Lines[0].Fats.Count == 0 && autoModel.Lines[0].DistributionCables.Count == 0))
                {
                    UpdateProgress(0, "Gagal: Tidak ada objek terdeteksi.");
                    MessageBox.Show("No APD/ABD entities (Cables/FATs) detected. Please ensure cables are on layers starting with 'FTTH-CABLE-' and FAT blocks/labels are placed.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string activeDwgName = "";
                try
                {
                    if (doc != null && !string.IsNullOrEmpty(doc.Name))
                    {
                        activeDwgName = Path.GetFileNameWithoutExtension(doc.Name);
                    }
                }
                catch {}
                if (string.IsNullOrEmpty(activeDwgName)) activeDwgName = autoModel.Fdt.Name;

                // Show save dialog
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Filter = "KMZ File|*.kmz|KML File|*.kml";
                dlg.FileName = $"{activeDwgName}_AutoExport.kmz";

                if (dlg.ShowDialog() == true)
                {
                    UpdateProgress(90, "Menghasilkan file KMZ...");
                    KmlGeneratorApdAbd gen = new KmlGeneratorApdAbd();
                    bool objectOnly = ChkAutoObjectOnlyMode.IsChecked == true;
                    bool isSubfeeder = RbAutoSubfeederMode.IsChecked == true;

                    gen.Generate(autoModel, dlg.FileName, objectOnly, isSubfeeder);
                    
                    // Set progress to 100% and update status. No popup as requested!
                    UpdateProgress(100, "Ekspor KML Berhasil Selesai!");
                    LogMessage("Auto KML APD/ABD export complete.");

                    // Calculate summary on main thread to avoid AutoCAD cross-threading transaction issues
                    double routeLength = 0;
                    int hompassCount = 0;
                    int poleCount = 0;
                    try
                    {
                        var summary = KmlSndKasarExporter.CalculateSummary(doc.Database);
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
                            string fileName = System.IO.Path.GetFileName(dlg.FileName);
                            string dirPath = System.IO.Path.GetDirectoryName(dlg.FileName);
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

                    if (BasemapSettings.Instance.AutoOpenKml && File.Exists(dlg.FileName))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                        }
                        catch (Exception exStart)
                        {
                            doc.Editor.WriteMessage($"\n[FTTH] Warning: Failed to automatically open KML file: {exStart.Message}");
                        }
                    }
                }
                else
                {
                    // Hide progress bar if save dialog was canceled
                    ProgExportKml.Visibility = System.Windows.Visibility.Collapsed;
                    TxtProgKmlStatus.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                UpdateProgress(0, $"Error: {ex.Message}");
                MessageBox.Show($"Auto Export Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage("Auto KML APD/ABD export failed: " + ex.Message);
                TxtAutoStatusLog.Text += $"\nError: {ex.Message}\nStacktrace: {ex.StackTrace}";
            }
        }

        private void UpdateProgress(double value, string statusText)
        {
            if (ProgExportKml == null || TxtProgKmlStatus == null) return;

            ProgExportKml.Visibility = System.Windows.Visibility.Visible;
            TxtProgKmlStatus.Visibility = System.Windows.Visibility.Visible;
            ProgExportKml.Value = value;
            TxtProgKmlStatus.Text = statusText;

            // Force visual refresh of elements on UI thread
            ProgExportKml.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            TxtProgKmlStatus.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
        }

        private void BtnApdExportGeoJson_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "GeoJSON File|*.geojson";
            string fdtName = string.IsNullOrEmpty(ApdModel?.Fdt?.Name) ? "Export" : ApdModel.Fdt.Name;
            dlg.FileName = $"{fdtName}.geojson";
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    GeoJsonExporter gen = new GeoJsonExporter();
                    gen.Generate(ApdModel, dlg.FileName);
                    MessageBox.Show("GeoJSON Export Successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogMessage("GeoJSON APD/ABD export complete.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"GeoJSON Export Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogMessage("GeoJSON export failed: " + ex.Message);
                }
            }
        }

        private void BtnApdExportBom_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "CSV File|*.csv";
            string fdtName = string.IsNullOrEmpty(ApdModel?.Fdt?.Name) ? "Export" : ApdModel.Fdt.Name;
            dlg.FileName = $"{fdtName}_BOM.csv";
            
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    BomExporter gen = new BomExporter();
                    gen.Generate(ApdModel, dlg.FileName);
                    MessageBox.Show("BOM Export Successful!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogMessage("BOM APD/ABD export complete.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"BOM Export Failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogMessage("BOM export failed: " + ex.Message);
                }
            }
        }

        private void BtnApdQACheck_Click(object sender, RoutedEventArgs e)
        {
            List<string> errors = new List<string>();

            // If we have no multi-FDTs yet, ensure the single primary FDT is verified
            bool hasMultiFdts = ApdModel.Fdts != null && ApdModel.Fdts.Count > 0;
            if (!hasMultiFdts)
            {
                ApdModel.Fdt.Name = GetSelectedFdtName();
                if (string.IsNullOrEmpty(ApdModel.Fdt.Name) || ApdModel.Fdt.Name == "FDT_1" || ApdModel.Fdt.Name == "FDT terpilih akan dilist disini")
                {
                    errors.Add("- FDT Name is still the default or empty.");
                }
                if (string.IsNullOrEmpty(ApdModel.Fdt.CapacityText))
                {
                    errors.Add("- FDT Point has not been picked.");
                }
            }

            foreach (var line in ApdModel.Lines)
            {
                bool lineHasObjects = line.Fats.Count > 0 || line.Poles.Count > 0 || line.DistributionCables.Count > 0;
                if (lineHasObjects)
                {
                    if (string.IsNullOrEmpty(line.FdtHandle))
                    {
                        errors.Add($"- {line.LineName} has objects but no FDT is selected.");
                    }
                    else if (ApdModel.Fdts != null)
                    {
                        var matchedFdt = ApdModel.Fdts.FirstOrDefault(f => f.EntityHandle == line.FdtHandle);
                        if (matchedFdt == null)
                        {
                            errors.Add($"- {line.LineName} refers to an FDT that is not found in the project.");
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(matchedFdt.Name) || matchedFdt.Name == "FDT_1" || matchedFdt.Name == "FDT terpilih akan dilist disini")
                            {
                                errors.Add($"- {line.LineName}: Linked FDT name is still the default or empty.");
                            }
                            if (string.IsNullOrEmpty(matchedFdt.CapacityText))
                            {
                                errors.Add($"- {line.LineName}: Linked FDT point has not been picked.");
                            }
                        }
                    }
                }

                int expectedIndex = 1;
                foreach (var fat in line.Fats)
                {
                    if (fat.SequenceName.Length >= 3)
                    {
                        string prefix = fat.SequenceName.Substring(0, 1);
                        string numStr = fat.SequenceName.Substring(1);
                        if (int.TryParse(numStr, out int num))
                        {
                            if (num != expectedIndex)
                            {
                                errors.Add($"- {line.LineName}: Missing FAT sequence. Expected {prefix}{expectedIndex:D2}, but found {fat.SequenceName}.");
                                expectedIndex = num;
                            }
                        }
                    }
                    expectedIndex++;

                    if (fat.HpCovers.Count == 0)
                    {
                        errors.Add($"- {line.LineName} / {fat.SequenceName}: No HP Covers picked for this FAT.");
                    }
                }
            }

            if (errors.Count > 0)
            {
                MessageBox.Show("QA Check found the following issues:\n\n" + string.Join("\n", errors), "QA Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show("QA Check passed! All sequential selections and FDT links are valid.", "QA Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExportSndKasar_Click(object sender, RoutedEventArgs e)
        {
            ResetCalibration();
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            string dwgName = string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(doc.Name))
                {
                    dwgName = Path.GetFileNameWithoutExtension(doc.Name);
                }
            }
            catch { }

            string defaultFileName = "FTTH_Snd_Kasar.kmz";
            if (!string.IsNullOrEmpty(dwgName) && dwgName.IndexOf("SnD Kasar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                defaultFileName = dwgName + ".kmz";
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "KMZ File|*.kmz|KML File|*.kml";
            sfd.FileName = defaultFileName;

            if (sfd.ShowDialog() == true)
            {
                PnlSndKasarProgress.Visibility = System.Windows.Visibility.Visible;
                PrgSndKasar.Value = 0;
                TxtSndKasarProgress.Text = "Initializing export...";

                PrgSndKasar.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));

                try
                {
                    KmlSndKasarExporter.Export(doc.Database, sfd.FileName, (percent, statusText) =>
                    {
                        PrgSndKasar.Value = percent;
                        TxtSndKasarProgress.Text = statusText;

                        PrgSndKasar.Dispatcher.Invoke(
                            System.Windows.Threading.DispatcherPriority.Background,
                            new Action(() => { }));
                    });

                    MessageBox.Show("Successfully exported Snd Kasar to " + Path.GetFileName(sfd.FileName) + "!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogMessage("Snd Kasar export complete.");

                    if (BasemapSettings.Instance.AutoOpenKml && File.Exists(sfd.FileName))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                        }
                        catch (Exception exStart)
                        {
                            doc.Editor.WriteMessage($"\n[FTTH] Warning: Failed to automatically open KML file: {exStart.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export Snd Kasar: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogMessage("Snd Kasar export failed: " + ex.Message);
                }
                finally
                {
                    PnlSndKasarProgress.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
        }

        private void BtnHitungSndKasarSummary_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            PnlSndKasarProgress.Visibility = System.Windows.Visibility.Visible;
            PrgSndKasar.Value = 0;
            TxtSndKasarProgress.Text = "Initializing summary calculation...";

            PrgSndKasar.Dispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => { }));

            try
            {
                SndKasarSummary summary = null;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    summary = KmlSndKasarExporter.CalculateSummary(doc.Database, (percent, statusText) =>
                    {
                        PrgSndKasar.Value = percent;
                        TxtSndKasarProgress.Text = statusText;

                        PrgSndKasar.Dispatcher.Invoke(
                            System.Windows.Threading.DispatcherPriority.Background,
                            new Action(() => { }));
                    });
                }

                if (summary != null)
                {
                    TxtSummaryRouteLength.Text = $"{summary.TotalRouteLength:N2} m";
                    TxtSummaryHompass.Text = summary.TotalHompass.ToString();

                    if (summary.TotalPoles == 0)
                    {
                        TxtSummaryPoles.Text = "Tidak ditemukan tiang.";
                    }
                    else
                    {
                        var poleDetails = new List<string>();
                        foreach (var pair in summary.PoleCounts.OrderBy(p => p.Key))
                        {
                            poleDetails.Add($"• {pair.Key}: {pair.Value}");
                        }
                        poleDetails.Add($"Total Tiang: {summary.TotalPoles}");
                        TxtSummaryPoles.Text = string.Join("\n", poleDetails);
                    }

                    if (summary.OtherCounts.Count == 0)
                    {
                        TxtSummaryOthers.Text = "Tidak ditemukan entitas lain.";
                    }
                    else
                    {
                        var otherDetails = new List<string>();
                        int totalOthers = 0;
                        foreach (var pair in summary.OtherCounts.OrderBy(p => p.Key))
                        {
                            string displayName = pair.Key.Replace("_LAYER", "");
                            otherDetails.Add($"• {displayName}: {pair.Value}");
                            totalOthers += pair.Value;
                        }
                        otherDetails.Add($"Total Entitas Lain: {totalOthers}");
                        TxtSummaryOthers.Text = string.Join("\n", otherDetails);
                    }

                    PnlSndKasarSummaryResult.Visibility = System.Windows.Visibility.Visible;
                    LogMessage("SnD Kasar summary calculated successfully.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal menghitung summary: {ex.Message}", "Error Summary", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage("Summary calculation failed: " + ex.Message);
            }
            finally
            {
                PnlSndKasarProgress.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnPickAutoFdt_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Focus();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    PromptEntityOptions peo = new PromptEntityOptions("\nSelect FDT (BlockReference, DBText, or MText): ");
                    peo.SetRejectMessage("\nPlease select a Block, Text, or MText.");
                    peo.AddAllowedClass(typeof(BlockReference), true);
                    peo.AddAllowedClass(typeof(DBText), true);
                    peo.AddAllowedClass(typeof(MText), true);

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.OK)
                    {
                        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                        {
                            Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                            Point3d pos;
                            string name = "";

                            if (ent is BlockReference br)
                            {
                                pos = br.Position;
                                name = br.Name;
                            }
                            else if (ent is DBText dbText)
                            {
                                pos = dbText.Position;
                                name = dbText.TextString;
                            }
                            else if (ent is MText mText)
                            {
                                pos = mText.Location;
                                name = mText.Text;
                            }
                            else
                            {
                                return;
                            }

                            _selectedAutoFdtPoint = pos;
                            _selectedAutoFdtHandle = ent.Handle.ToString();

                            TxtAutoFdtLocation.Text = $"FDT: {name} ({pos.X:F2}, {pos.Y:F2})";
                            LogMessage($"FDT selected at {pos.X:F2}, {pos.Y:F2} (Handle: {_selectedAutoFdtHandle})");
                            tr.Commit();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to pick FDT: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage("Pick FDT failed: " + ex.Message);
            }
        }

        private void BtnClusterAndPlaceFat_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!int.TryParse(TxtAutoMaxSub.Text, out int maxSub) || maxSub <= 0)
            {
                MessageBox.Show("Please enter a valid positive integer for Max Subscriber.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtAutoMinSub.Text, out int minSub) || minSub <= 0)
            {
                MessageBox.Show("Please enter a valid positive integer for Min Subscriber.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtAutoMaxDrop.Text, out double maxDrop) || maxDrop <= 0)
            {
                MessageBox.Show("Please enter a valid positive number for Max Drop Cable Distance.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Visible;
            PrgAutoRoute.Value = 0;
            TxtAutoRouteProgress.Text = "Initializing FAT clustering...";
            PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));

            try
            {
                string assetPathDir = GetAssetPath("");
                if (string.IsNullOrEmpty(assetPathDir) || !Directory.Exists(assetPathDir))
                {
                    MessageBox.Show("FTTH_Asset folder could not be located!", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool allowImprovisation = ChkAllowImprovisation.IsChecked == true;
                List<RouteCableGenerator.HomeCluster> clusters;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    clusters = RouteCableGenerator.ClusterAndPlaceFats(doc.Database, maxSub, minSub, maxDrop, assetPathDir, _selectedAutoFdtPoint, _selectedAutoFdtPoint != Point3d.Origin, allowImprovisation, (percent, msg) =>
                    {
                        PrgAutoRoute.Value = percent;
                        TxtAutoRouteProgress.Text = msg;
                        PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                    });
                }

                if (clusters != null && clusters.Count > 0)
                {
                    _activeClusters = clusters;
                    MessageBox.Show($"Successfully clustered homes and placed {clusters.Count} FAT blocks!", "Clustering Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogMessage($"Clustering complete: placed {clusters.Count} FATs.");
                    SendCommandToAutoCAD("FTTH_AUTO_BOUNDARY");
                }
                else
                {
                    MessageBox.Show("No FAT clusters were generated.", "Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to cluster and place FATs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage("FAT clustering failed: " + ex.Message);
            }
            finally
            {
                PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnRouteCable_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            bool autoPlaceFdt = ChkAutoPlaceFdt.IsChecked == true;
            bool multiFdt = ChkMultiFdt.IsChecked == true;

            if (!multiFdt && !autoPlaceFdt && (_selectedAutoFdtPoint == Point3d.Origin && string.IsNullOrEmpty(_selectedAutoFdtHandle)))
            {
                MessageBox.Show("Please select an FDT location first using 'Pick FDT Location', or check 'Auto Place FDT'.", "FDT Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_activeClusters == null || _activeClusters.Count == 0)
            {
                if (multiFdt)
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var graphNodes = RouteCableGenerator.BuildNetworkGraph(doc.Database, tr);
                        _activeClusters = RouteCableGenerator.ScanPlacedFats(doc.Database, graphNodes);
                        tr.Commit();
                    }
                }

                if (_activeClusters == null || _activeClusters.Count == 0)
                {
                    MessageBox.Show("Please perform FAT clustering first using '1. CLUSTER & PLACE FAT'.", "FAT Clusters Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (!double.TryParse(TxtAutoMaxTektok.Text, out double maxTektok) || maxTektok <= 0)
            {
                MessageBox.Show("Please enter a valid positive number for Max Tektok Distance.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int fdtCapacity = RadAutoFdt48.IsChecked == true ? 48 : 72;

            PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Visible;
            PrgAutoRoute.Value = 0;
            TxtAutoRouteProgress.Text = "Initializing cable routing...";
            PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));

            try
            {
                string assetPathDir = GetAssetPath("");
                if (string.IsNullOrEmpty(assetPathDir) || !Directory.Exists(assetPathDir))
                {
                    MessageBox.Show("FTTH_Asset folder could not be located!", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int targetLineCount = 0;
                if (TxtTargetLineCount != null && !string.IsNullOrEmpty(TxtTargetLineCount.Text))
                {
                    int.TryParse(TxtTargetLineCount.Text, out targetLineCount);
                }

                List<RouteCableGenerator.CableLine> cables;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    if (multiFdt)
                    {
                        var existingFdts = RouteCableGenerator.ScanFdtsInDrawing(doc.Database);
                        if (existingFdts.Count == 0)
                        {
                            PrgAutoRoute.Value = 10;
                            TxtAutoRouteProgress.Text = "No FDTs found. Placing FDTs automatically...";
                            PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));

                            RouteCableGenerator.AutoPlaceFdts(doc.Database, assetPathDir, (pct, msg) =>
                            {
                                PrgAutoRoute.Value = 10 + (pct * 0.15); // Map to 10-25%
                                TxtAutoRouteProgress.Text = "[Auto FDT] " + msg;
                                PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                            });
                        }

                        cables = RouteCableGenerator.RouteCablesFromMultipleFdts(
                            doc.Database,
                            _activeClusters,
                            maxTektok,
                            assetPathDir,
                            (percent, msg) =>
                            {
                                double mappedPercent = 25.0 + (percent * 0.75); // Map to 25-100%
                                PrgAutoRoute.Value = mappedPercent;
                                TxtAutoRouteProgress.Text = msg;
                                PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                            }
                        );
                    }
                    else
                    {
                        cables = RouteCableGenerator.RouteCablesFromFdt(
                            doc.Database,
                            _selectedAutoFdtPoint,
                            _activeClusters,
                            fdtCapacity,
                            maxTektok,
                            autoPlaceFdt,
                            assetPathDir,
                            (percent, msg) =>
                            {
                                PrgAutoRoute.Value = percent;
                                TxtAutoRouteProgress.Text = msg;
                                PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                            },
                            targetLineCount
                        );
                    }
                }

                if (cables != null && cables.Count > 0)
                {
                    if (targetLineCount > 0 && cables.Count > targetLineCount)
                    {
                        MessageBox.Show($"Gagal mencapai target {targetLineCount} line kabel (hasil akhir: {cables.Count} line) setelah mencoba menaikkan Max Tektok.\n\nSaran: Gunakan fitur 'Koreksi Rute Manual (Advanced)' pada panel untuk memandu rute di area percabangan secara spesifik.", "Target Line Tidak Tercapai", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"Successfully routed {cables.Count} cable lines!", "Routing Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    LogMessage($"Cable routing complete: generated {cables.Count} lines.");
                }
                else
                {
                    MessageBox.Show("No cable routes were generated.", "Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to route cables: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage("Cable routing failed: " + ex.Message);
            }
            finally
            {
                PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void ChkMultiFdt_Checked(object sender, RoutedEventArgs e)
        {
            if (PnlSingleFdtSettings != null) PnlSingleFdtSettings.Visibility = System.Windows.Visibility.Collapsed;
            if (PnlMultiFdtButtons != null) PnlMultiFdtButtons.Visibility = System.Windows.Visibility.Visible;
        }

        private void ChkMultiFdt_Unchecked(object sender, RoutedEventArgs e)
        {
            if (PnlSingleFdtSettings != null) PnlSingleFdtSettings.Visibility = System.Windows.Visibility.Visible;
            if (PnlMultiFdtButtons != null) PnlMultiFdtButtons.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void ChkAutoLineCount_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtTargetLineCount != null)
            {
                TxtTargetLineCount.IsEnabled = ChkAutoLineCount.IsChecked == false;
            }
        }

        private void BtnRecommendFdts_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                int fatCount = 0;
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var graphNodes = RouteCableGenerator.BuildNetworkGraph(doc.Database, tr);
                    var fats = RouteCableGenerator.ScanPlacedFats(doc.Database, graphNodes);
                    fatCount = fats.Count;
                    tr.Commit();
                }

                if (fatCount == 0)
                {
                    MessageBox.Show("Tidak ditemukan FAT terpasang di gambar. Pasang FAT terlebih dahulu (Langkah 1).", "Rekomendasi FDT", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                RouteCableGenerator.RecommendFdts(fatCount, out int c48, out int c72);
                int totalCapacity = c48 * 20 + c72 * 30;

                string msg = $"--- REKOMENDASI PENEMPATAN FDT ---\n\n" +
                             $"Total FAT terdeteksi: {fatCount} unit\n" +
                             $"Kebutuhan FDT:\n" +
                             $" - FDT 48 Core (Maks 20 FAT): {c48} unit\n" +
                             $" - FDT 72 Core (Maks 30 FAT): {c72} unit\n\n" +
                             $"Total Kapasitas Terencana: {totalCapacity} FAT\n" +
                             $"Efisiensi Kapasitas: {((double)fatCount / totalCapacity * 100):F1}%\n\n" +
                             $"Silakan klik '⚡ AUTO-PLACE BANYAK FDT' untuk meletakkan titik FDT ini secara otomatis pada posisi terbaik lapangan.";

                MessageBox.Show(msg, "Perencana Kapasitas FDT", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal melakukan rekomendasi: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAutoPlaceFdts_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            string assetPathDir = GetAssetPath("");
            if (string.IsNullOrEmpty(assetPathDir) || !Directory.Exists(assetPathDir))
            {
                MessageBox.Show("FTTH_Asset folder could not be located!", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Visible;
            PrgAutoRoute.Value = 0;
            TxtAutoRouteProgress.Text = "Starting auto FDT placement...";
            PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));

            try
            {
                string result = "";
                using (DocumentLock docLock = doc.LockDocument())
                {
                    result = RouteCableGenerator.AutoPlaceFdts(doc.Database, assetPathDir, (percent, msg) =>
                    {
                        PrgAutoRoute.Value = percent;
                        TxtAutoRouteProgress.Text = msg;
                        PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                    });
                }

                MessageBox.Show(result, "Auto Place FDT", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal menempatkan FDT otomatis: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnCompleteAutoDesign_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!int.TryParse(TxtAutoMaxSub.Text, out int maxSub) || maxSub <= 0)
            {
                MessageBox.Show("Please enter a valid positive integer for Max Subscriber.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtAutoMinSub.Text, out int minSub) || minSub <= 0)
            {
                MessageBox.Show("Please enter a valid positive integer for Min Subscriber.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtAutoMaxDrop.Text, out double maxDrop) || maxDrop <= 0)
            {
                MessageBox.Show("Please enter a valid positive number for Max Drop Cable Distance.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtAutoMaxTektok.Text, out double maxTektok) || maxTektok <= 0)
            {
                MessageBox.Show("Please enter a valid positive number for Max Tektok Distance.", "Invalid Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool autoPlaceFdt = ChkAutoPlaceFdt.IsChecked == true;
            bool multiFdt = ChkMultiFdt.IsChecked == true;
            bool allowImprovisation = ChkAllowImprovisation.IsChecked == true;
            int fdtCapacity = RadAutoFdt48.IsChecked == true ? 48 : 72;

            if (!multiFdt && !autoPlaceFdt && (_selectedAutoFdtPoint == Point3d.Origin && string.IsNullOrEmpty(_selectedAutoFdtHandle)))
            {
                MessageBox.Show("Please select an FDT location first using 'Pick FDT Location', or check 'Auto Place FDT' to run complete auto design.", "FDT Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Visible;
            PrgAutoRoute.Value = 0;
            TxtAutoRouteProgress.Text = "Starting complete auto design...";
            PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));

            try
            {
                string assetPathDir = GetAssetPath("");
                if (string.IsNullOrEmpty(assetPathDir) || !Directory.Exists(assetPathDir))
                {
                    MessageBox.Show("FTTH_Asset folder could not be located!", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                using (DocumentLock docLock = doc.LockDocument())
                {
                    LogMessage("Starting Complete Auto Design...");
                    
                    // Step 1: Cluster and place FATs
                    List<RouteCableGenerator.HomeCluster> clusters = RouteCableGenerator.ClusterAndPlaceFats(doc.Database, maxSub, minSub, maxDrop, assetPathDir, _selectedAutoFdtPoint, _selectedAutoFdtPoint != Point3d.Origin && !autoPlaceFdt && !multiFdt, allowImprovisation, (percent, msg) =>
                    {
                        double mappedPercent = percent * 0.30;
                        PrgAutoRoute.Value = mappedPercent;
                        TxtAutoRouteProgress.Text = "[Langkah 1/3] " + msg;
                        PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                    });

                    if (clusters == null || clusters.Count == 0)
                    {
                        MessageBox.Show("No FAT clusters were generated.", "Auto Design Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _activeClusters = clusters;
                    LogMessage($"Auto Design Step 1: Placed {clusters.Count} FATs.");

                    // Step 2: Auto-Place FDTs if multi-FDT
                    if (multiFdt)
                    {
                        TxtAutoRouteProgress.Text = "[Langkah 2/3] Menempatkan FDT otomatis...";
                        PrgAutoRoute.Value = 35.0;
                        PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));

                        RouteCableGenerator.AutoPlaceFdts(doc.Database, assetPathDir);
                    }

                    int targetLineCount = 0;
                    if (ChkAutoLineCount != null && ChkAutoLineCount.IsChecked == false && TxtTargetLineCount != null && !string.IsNullOrEmpty(TxtTargetLineCount.Text))
                    {
                        int.TryParse(TxtTargetLineCount.Text, out targetLineCount);
                    }

                    // Step 3: Route cables
                    List<RouteCableGenerator.CableLine> cables;
                    if (multiFdt)
                    {
                        cables = RouteCableGenerator.RouteCablesFromMultipleFdts(
                            doc.Database,
                            _activeClusters,
                            maxTektok,
                            assetPathDir,
                            (percent, msg) =>
                            {
                                double mappedPercent = 40.0 + (percent * 0.60);
                                PrgAutoRoute.Value = mappedPercent;
                                TxtAutoRouteProgress.Text = "[Langkah 3/3] " + msg;
                                PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                            }
                        );
                    }
                    else
                    {
                        cables = RouteCableGenerator.RouteCablesFromFdt(
                            doc.Database,
                            _selectedAutoFdtPoint,
                            _activeClusters,
                            fdtCapacity,
                            maxTektok,
                            autoPlaceFdt,
                            assetPathDir,
                            (percent, msg) =>
                            {
                                double mappedPercent = 40.0 + (percent * 0.60);
                                PrgAutoRoute.Value = mappedPercent;
                                TxtAutoRouteProgress.Text = "[Langkah 2/2] " + msg;
                                PrgAutoRoute.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                            },
                            targetLineCount
                        );
                    }

                    if (cables != null && cables.Count > 0)
                    {
                        if (targetLineCount > 0 && cables.Count > targetLineCount)
                        {
                            MessageBox.Show($"Gagal mencapai target {targetLineCount} line kabel (hasil akhir: {cables.Count} line) setelah mencoba menaikkan Max Tektok.\n\nSaran: Gunakan fitur 'Koreksi Rute Manual (Advanced)' pada panel untuk memandu rute di area percabangan secara spesifik.", "Target Line Tidak Tercapai", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            MessageBox.Show($"Successfully completed auto design!\nPlaced {clusters.Count} FATs and routed {cables.Count} cable lines.", "Auto Design Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        LogMessage($"Auto Design complete: placed {clusters.Count} FATs, routed {cables.Count} lines.");
                        SendCommandToAutoCAD("FTTH_AUTO_BOUNDARY");
                    }
                    else
                    {
                        MessageBox.Show("FATs were placed but no cable lines were generated.", "Complete with Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Complete Auto Design failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage("Complete Auto Design failed: " + ex.Message);
            }
            finally
            {
                PnlAutoRouteProgress.Visibility = System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnKoreksiPenuh_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            if (_activeClusters == null || _activeClusters.Count == 0)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var graphNodes = RouteCableGenerator.BuildNetworkGraph(db, tr);
                    _activeClusters = RouteCableGenerator.ScanPlacedFats(db, graphNodes);
                    tr.Commit();
                }
            }

            if (_activeClusters == null || _activeClusters.Count == 0)
            {
                MessageBox.Show("Belum ada FAT yang terpasang di gambar! Jalankan '1. CLUSTER & PLACE FAT' terlebih dahulu.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtAutoMaxTektok.Text, out double maxTektok) || maxTektok <= 0)
            {
                MessageBox.Show("Masukkan nilai Max Tektok yang valid!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int targetLineCount = 0;
            if (ChkAutoLineCount != null && ChkAutoLineCount.IsChecked == false && TxtTargetLineCount != null && !string.IsNullOrEmpty(TxtTargetLineCount.Text))
            {
                int.TryParse(TxtTargetLineCount.Text, out targetLineCount);
            }

            int fdtCapacity = RadAutoFdt48.IsChecked == true ? 48 : 72;

            try
            {
                Point3d fdtPt = _selectedAutoFdtPoint;
                if (fdtPt == Point3d.Origin)
                {
                    PromptEntityOptions peoFdt = new PromptEntityOptions("\nSelect FDT in AutoCAD (BlockReference, DBText, or MText): ");
                    peoFdt.SetRejectMessage("\nPlease select a Block, Text, or MText.");
                    peoFdt.AddAllowedClass(typeof(BlockReference), true);
                    peoFdt.AddAllowedClass(typeof(DBText), true);
                    peoFdt.AddAllowedClass(typeof(MText), true);
                    PromptEntityResult perFdt = ed.GetEntity(peoFdt);
                    if (perFdt.Status != PromptStatus.OK) return;

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(perFdt.ObjectId, OpenMode.ForRead);
                        if (ent is BlockReference br) fdtPt = br.Position;
                        else if (ent is DBText txt) fdtPt = txt.Position;
                        else if (ent is MText mt) fdtPt = mt.Location;
                        tr.Commit();
                    }
                    _selectedAutoFdtPoint = fdtPt;
                    TxtAutoFdtLocation.Text = $"FDT Picked: ({fdtPt.X:F1}, {fdtPt.Y:F1})";
                }

                var selectedFats = new List<RouteCableGenerator.HomeCluster>();
                ed.WriteMessage("\n--- MEMULAI PEMILIHAN URUTAN FAT MANUAL ---");
                while (true)
                {
                    PromptEntityOptions peo = new PromptEntityOptions($"\nPilih FAT ke-{selectedFats.Count + 1} (Block, Text, atau Boundary) [ENTER/ESC untuk selesai]: ");
                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                        break;

                    Point3d selectPos = Point3d.Origin;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        if (ent is BlockReference br) selectPos = br.Position;
                        else if (ent is DBText txt) selectPos = txt.Position;
                        else if (ent is MText mt) selectPos = mt.Location;
                        else
                        {
                            var bounds = ent.Bounds;
                            if (bounds.HasValue)
                            {
                                var min = bounds.Value.MinPoint;
                                var max = bounds.Value.MaxPoint;
                                selectPos = new Point3d((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
                            }
                        }
                        tr.Commit();
                    }

                    if (selectPos == Point3d.Origin)
                    {
                        ed.WriteMessage("\nObjek yang dipilih tidak memiliki koordinat valid!");
                        continue;
                    }

                    var cluster = _activeClusters.OrderBy(c => c.AssignedPole.Position.DistanceTo(selectPos)).FirstOrDefault();
                    if (cluster == null || cluster.AssignedPole.Position.DistanceTo(selectPos) > 5.0)
                    {
                        ed.WriteMessage("\nTidak ditemukan tiang FAT yang cocok di dekat pilihan Anda!");
                        continue;
                    }

                    if (selectedFats.Contains(cluster))
                    {
                        ed.WriteMessage($"\nFAT {cluster.SequenceName} sudah dipilih sebelumnya!");
                        continue;
                    }

                    selectedFats.Add(cluster);
                    ed.WriteMessage($"\n[OK] Memilih FAT {selectedFats.Count}: {cluster.SequenceName}");
                }

                if (selectedFats.Count == 0)
                {
                    MessageBox.Show("Pemilihan FAT dibatalkan atau tidak ada FAT yang dipilih.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string assetPathDir = GetAssetPath("");
                List<RouteCableGenerator.CableLine> cables;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    cables = RouteCableGenerator.RouteCablesManually(
                        db,
                        fdtPt,
                        _activeClusters,
                        new List<RouteCableGenerator.HomeCluster>(),
                        selectedFats,
                        fdtCapacity,
                        maxTektok,
                        assetPathDir,
                        (percent, msg) => { },
                        targetLineCount
                    );
                }

                if (cables != null && cables.Count > 0)
                {
                    MessageBox.Show($"Sukses melakukan koreksi manual! Menghasilkan {cables.Count} line kabel.", "Koreksi Selesai", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Koreksi penuh gagal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnKoreksiParsial_Click(object sender, RoutedEventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            if (_activeClusters == null || _activeClusters.Count == 0)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var graphNodes = RouteCableGenerator.BuildNetworkGraph(db, tr);
                    _activeClusters = RouteCableGenerator.ScanPlacedFats(db, graphNodes);
                    tr.Commit();
                }
            }

            if (_activeClusters == null || _activeClusters.Count == 0)
            {
                MessageBox.Show("Belum ada FAT yang terpasang di gambar! Jalankan '1. CLUSTER & PLACE FAT' terlebih dahulu.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtAutoMaxTektok.Text, out double maxTektok) || maxTektok <= 0)
            {
                MessageBox.Show("Masukkan nilai Max Tektok yang valid!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int targetLineCount = 0;
            if (ChkAutoLineCount != null && ChkAutoLineCount.IsChecked == false && TxtTargetLineCount != null && !string.IsNullOrEmpty(TxtTargetLineCount.Text))
            {
                int.TryParse(TxtTargetLineCount.Text, out targetLineCount);
            }

            int fdtCapacity = RadAutoFdt48.IsChecked == true ? 48 : 72;

            try
            {
                PromptEntityOptions peoPoly = new PromptEntityOptions("\nPilih polyline kabel yang ingin dikoreksi: ");
                peoPoly.SetRejectMessage("\nPlease select a Polyline.");
                peoPoly.AddAllowedClass(typeof(Polyline), true);
                PromptEntityResult perPoly = ed.GetEntity(peoPoly);
                if (perPoly.Status != PromptStatus.OK) return;

                PromptEntityOptions peoLastCorrect = new PromptEntityOptions("\nPilih FAT terakhir yang jalurnya sudah benar: ");
                PromptEntityResult perLast = ed.GetEntity(peoLastCorrect);
                if (perLast.Status != PromptStatus.OK) return;

                Point3d lastCorrectPos = Point3d.Origin;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity ent = (Entity)tr.GetObject(perLast.ObjectId, OpenMode.ForRead);
                    if (ent is BlockReference br) lastCorrectPos = br.Position;
                    else if (ent is DBText txt) lastCorrectPos = txt.Position;
                    else if (ent is MText mt) lastCorrectPos = mt.Location;
                    else
                    {
                        var bounds = ent.Bounds;
                        if (bounds.HasValue)
                        {
                            var min = bounds.Value.MinPoint;
                            var max = bounds.Value.MaxPoint;
                            lastCorrectPos = new Point3d((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
                        }
                    }
                    tr.Commit();
                }

                var lastCorrectCluster = _activeClusters.OrderBy(c => c.AssignedPole.Position.DistanceTo(lastCorrectPos)).FirstOrDefault();
                if (lastCorrectCluster == null || lastCorrectCluster.AssignedPole.Position.DistanceTo(lastCorrectPos) > 5.0)
                {
                    MessageBox.Show("Tidak ditemukan tiang FAT yang cocok di dekat pilihan Anda!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Trace back correct sequence from Polyline
                var correctSequence = new List<RouteCableGenerator.HomeCluster>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline poly = (Polyline)tr.GetObject(perPoly.ObjectId, OpenMode.ForRead);
                    
                    // Sort active clusters by distance along polyline
                    var candidates = new List<(RouteCableGenerator.HomeCluster Cluster, double Dist)>();
                    foreach (var c in _activeClusters)
                    {
                        Point3d closestPt = poly.GetClosestPointTo(c.AssignedPole.Position, false);
                        if (closestPt.DistanceTo(c.AssignedPole.Position) < 5.0)
                        {
                            double dist = poly.GetDistAtPoint(closestPt);
                            candidates.Add((c, dist));
                        }
                    }

                    candidates = candidates.OrderBy(x => x.Dist).ToList();
                    
                    bool foundLast = false;
                    foreach (var pair in candidates)
                    {
                        correctSequence.Add(pair.Cluster);
                        if (pair.Cluster == lastCorrectCluster)
                        {
                            foundLast = true;
                            break;
                        }
                    }

                    if (!foundLast)
                    {
                        MessageBox.Show("FAT terakhir tidak berada di sepanjang rute kabel yang dipilih!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    tr.Commit();
                }

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(perPoly.ObjectId, OpenMode.ForWrite);
                        ent.Erase();
                        tr.Commit();
                    }
                }

                var newSequenceToAppend = new List<RouteCableGenerator.HomeCluster>();
                ed.WriteMessage("\n--- MEMULAI PEMILIHAN URUTAN FAT LANJUTAN ---");
                while (true)
                {
                    PromptEntityOptions peo = new PromptEntityOptions($"\nPilih FAT Lanjutan ke-{newSequenceToAppend.Count + 1} (Block, Text, atau Boundary) [ENTER/ESC untuk selesai]: ");
                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                        break;

                    Point3d selectPos = Point3d.Origin;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                        if (ent is BlockReference br) selectPos = br.Position;
                        else if (ent is DBText txt) selectPos = txt.Position;
                        else if (ent is MText mt) selectPos = mt.Location;
                        else
                        {
                            var bounds = ent.Bounds;
                            if (bounds.HasValue)
                            {
                                var min = bounds.Value.MinPoint;
                                var max = bounds.Value.MaxPoint;
                                selectPos = new Point3d((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
                            }
                        }
                        tr.Commit();
                    }

                    if (selectPos == Point3d.Origin)
                    {
                        ed.WriteMessage("\nObjek yang dipilih tidak memiliki koordinat valid!");
                        continue;
                    }

                    var cluster = _activeClusters.OrderBy(c => c.AssignedPole.Position.DistanceTo(selectPos)).FirstOrDefault();
                    if (cluster == null || cluster.AssignedPole.Position.DistanceTo(selectPos) > 5.0)
                    {
                        ed.WriteMessage("\nTidak ditemukan tiang FAT yang cocok di dekat pilihan Anda!");
                        continue;
                    }

                    if (correctSequence.Contains(cluster))
                    {
                        ed.WriteMessage($"\nFAT {cluster.SequenceName} sudah ada dalam rute yang benar!");
                        continue;
                    }

                    if (newSequenceToAppend.Contains(cluster))
                    {
                        ed.WriteMessage($"\nFAT {cluster.SequenceName} sudah dipilih sebelumnya!");
                        continue;
                    }

                    newSequenceToAppend.Add(cluster);
                    ed.WriteMessage($"\n[OK] Memilih FAT Lanjutan {newSequenceToAppend.Count}: {cluster.SequenceName}");
                }

                if (newSequenceToAppend.Count == 0)
                {
                    MessageBox.Show("Koreksi parsial dibatalkan.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string assetPathDir = GetAssetPath("");
                Point3d fdtPt = _selectedAutoFdtPoint;
                List<RouteCableGenerator.CableLine> cables;
                using (DocumentLock docLock = doc.LockDocument())
                {
                    cables = RouteCableGenerator.RouteCablesManually(
                        db,
                        fdtPt,
                        _activeClusters,
                        correctSequence,
                        newSequenceToAppend,
                        fdtCapacity,
                        maxTektok,
                        assetPathDir,
                        (percent, msg) => { },
                        targetLineCount
                    );
                }

                if (cables != null && cables.Count > 0)
                {
                    MessageBox.Show($"Sukses melakukan koreksi parsial!", "Koreksi Selesai", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Koreksi parsial gagal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void RefreshLayoutList()
        {
            if (CboPubSourceLayout == null) return;

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            try
            {
                using (doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        DBDictionary layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                        List<Layout> layoutObjects = new List<Layout>();
                        foreach (DBDictionaryEntry entry in layoutDict)
                        {
                            if (!entry.Key.Equals("Model", StringComparison.OrdinalIgnoreCase))
                            {
                                Layout lay = tr.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                                if (lay != null) layoutObjects.Add(lay);
                            }
                        }

                        var sortedLayoutNames = layoutObjects
                            .OrderBy(l => l.TabOrder)
                            .Select(l => l.LayoutName)
                            .ToList();

                        string currentText = CboPubSourceLayout.Text;
                        CboPubSourceLayout.Items.Clear();
                        foreach (string name in sortedLayoutNames)
                        {
                            CboPubSourceLayout.Items.Add(name);
                        }

                        if (CboPubSourceLayout.Items.Count > 0)
                        {
                            CboPubSourceLayout.SelectedIndex = 0;
                            for (int i = 0; i < CboPubSourceLayout.Items.Count; i++)
                            {
                                if (CboPubSourceLayout.Items[i]?.ToString() == currentText)
                                {
                                    CboPubSourceLayout.SelectedIndex = i;
                                    break;
                                }
                            }
                        }

                        tr.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                PaletteManager.LogToFile($"Error in RefreshLayoutList: {ex.Message}");
            }
        }

        private void BtnRefreshLayouts_Click(object sender, RoutedEventArgs e)
        {
            try { RefreshLayoutList(); } catch { }
        }

        private void ChkPubUsePrefix_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtPubPrefix != null)
            {
                TxtPubPrefix.IsEnabled = ChkPubUsePrefix.IsChecked == true;
            }
        }

        private void ChkPubUseSuffix_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtPubSuffix != null)
            {
                TxtPubSuffix.IsEnabled = ChkPubUseSuffix.IsChecked == true;
            }
        }

        private void BtnPubQuickCopy_Click(object sender, RoutedEventArgs e)
        {
            if (CboPubSourceLayout.SelectedItem == null)
            {
                MessageBox.Show("Please select a source layout first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Commands.PubSourceLayout = CboPubSourceLayout.SelectedItem?.ToString() ?? "";
            Commands.PubUsePrefix = ChkPubUsePrefix.IsChecked == true;
            Commands.PubPrefix = TxtPubPrefix.Text;

            SendCommandToAutoCAD("FTTH_PUB_QUICKCOPY");
        }

        private void BtnPubDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (CboPubSourceLayout.SelectedItem == null)
            {
                MessageBox.Show("Please select a source layout first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Commands.PubSourceLayout = CboPubSourceLayout.SelectedItem?.ToString() ?? "";
            Commands.PubUsePrefix = ChkPubUsePrefix.IsChecked == true;
            Commands.PubPrefix = TxtPubPrefix.Text;
            Commands.PubUseSuffix = ChkPubUseSuffix.IsChecked == true;
            Commands.PubSuffix = TxtPubSuffix.Text;
            Commands.PubUseAlpha = ChkPubUseAlpha.IsChecked == true;

            if (int.TryParse(TxtPubCopyCount.Text, out int copyCount) && copyCount > 0)
            {
                Commands.PubCopyCount = copyCount;
            }
            else
            {
                Commands.PubCopyCount = 5;
            }

            SendCommandToAutoCAD("FTTH_PUB_DUPLICATE");
        }

        public void PopulateAlignBlocksOrLayers()
        {
            if (CboAlignMethod == null || CboAlignBlockOrLayer == null) return;

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var selectedItem = CboAlignMethod.SelectedItem as ComboBoxItem;
            string method = selectedItem?.Tag?.ToString() ?? "BLOCK";

            Database db = doc.Database;
            try
            {
                using (doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        List<string> items = new List<string>();

                        if (method == "BLOCK")
                        {
                            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            foreach (ObjectId id in bt)
                            {
                                BlockTableRecord btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                                if (btr != null && !btr.IsLayout && !btr.IsAnonymous && !btr.Name.StartsWith("*"))
                                {
                                    items.Add(btr.Name);
                                }
                            }
                        }
                        else // POLYLINE
                        {
                            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                            foreach (ObjectId id in lt)
                            {
                                LayerTableRecord ltr = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                                if (ltr != null)
                                {
                                    items.Add(ltr.Name);
                                }
                            }
                        }

                        items.Sort();

                        string currentText = CboAlignBlockOrLayer.Text;
                        CboAlignBlockOrLayer.Items.Clear();
                        foreach (string name in items)
                        {
                            CboAlignBlockOrLayer.Items.Add(name);
                        }

                        if (CboAlignBlockOrLayer.Items.Count > 0)
                        {
                            CboAlignBlockOrLayer.SelectedIndex = 0;
                            for (int i = 0; i < CboAlignBlockOrLayer.Items.Count; i++)
                            {
                                if (CboAlignBlockOrLayer.Items[i]?.ToString() == currentText)
                                {
                                    CboAlignBlockOrLayer.SelectedIndex = i;
                                    break;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(currentText))
                        {
                            CboAlignBlockOrLayer.Text = currentText;
                        }

                        tr.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                PaletteManager.LogToFile($"Error in PopulateAlignBlocksOrLayers: {ex.Message}");
            }
        }

        private void CboAlignMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboAlignMethod == null || LblAlignBlockOrLayer == null || LblAlignIdSource == null || CboAlignIdSource == null || LblAlignIdTag == null || TxtAlignIdTag == null)
                return;

            var selectedItem = CboAlignMethod.SelectedItem as ComboBoxItem;
            string method = selectedItem?.Tag?.ToString() ?? "BLOCK";

            if (method == "BLOCK")
            {
                LblAlignBlockOrLayer.Text = "Block Name Filter:";
                LblAlignIdSource.Visibility = System.Windows.Visibility.Visible;
                CboAlignIdSource.Visibility = System.Windows.Visibility.Visible;
                var srcItem = CboAlignIdSource.SelectedItem as ComboBoxItem;
                string src = srcItem?.Tag?.ToString() ?? "ATTRIBUTE";
                LblAlignIdTag.Visibility = src == "ATTRIBUTE" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                TxtAlignIdTag.Visibility = src == "ATTRIBUTE" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            else // POLYLINE
            {
                LblAlignBlockOrLayer.Text = "Layer Name Filter:";
                LblAlignIdSource.Visibility = System.Windows.Visibility.Collapsed;
                CboAlignIdSource.Visibility = System.Windows.Visibility.Collapsed;
                LblAlignIdTag.Visibility = System.Windows.Visibility.Collapsed;
                TxtAlignIdTag.Visibility = System.Windows.Visibility.Collapsed;
            }

            try { PopulateAlignBlocksOrLayers(); } catch { }
        }

        private void CboAlignIdSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboAlignIdSource == null || LblAlignIdTag == null || TxtAlignIdTag == null)
                return;

            var selectedItem = CboAlignIdSource.SelectedItem as ComboBoxItem;
            string src = selectedItem?.Tag?.ToString() ?? "ATTRIBUTE";

            LblAlignIdTag.Visibility = src == "ATTRIBUTE" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            TxtAlignIdTag.Visibility = src == "ATTRIBUTE" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void BtnPickFrameTemplate_Click(object sender, RoutedEventArgs e)
        {
            SendCommandToAutoCAD("FTTH_PUB_PICKFRAME");
        }

        private void BtnAlignViewports_Click(object sender, RoutedEventArgs e)
        {
            var methodItem = CboAlignMethod.SelectedItem as ComboBoxItem;
            Commands.AlignMethod = methodItem?.Tag?.ToString() ?? "BLOCK";

            Commands.AlignBlockOrLayer = CboAlignBlockOrLayer.SelectedItem?.ToString() ?? CboAlignBlockOrLayer.Text;

            var srcItem = CboAlignIdSource.SelectedItem as ComboBoxItem;
            Commands.AlignIdSource = srcItem?.Tag?.ToString() ?? "ATTRIBUTE";

            Commands.AlignIdTag = TxtAlignIdTag.Text;

            SendCommandToAutoCAD("FTTH_PUB_ALIGNVIEWPORTS");
        }

        public void SetSelectedAlignBlockOrLayer(string name)
        {
            if (CboAlignBlockOrLayer != null)
            {
                Dispatcher.Invoke(() => {
                    if (!CboAlignBlockOrLayer.Items.Contains(name))
                    {
                        CboAlignBlockOrLayer.Items.Add(name);
                    }
                    int idx = CboAlignBlockOrLayer.Items.IndexOf(name);
                    if (idx >= 0)
                    {
                        CboAlignBlockOrLayer.SelectedIndex = idx;
                    }
                });
            }
        }
    }
}
