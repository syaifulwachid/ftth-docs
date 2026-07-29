using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FTTHBasemap.UI
{
    public partial class ImportSelectionWindow : Window
    {
        public string SelectedPdfLayer { get; private set; }
        public List<KmlFeature> SelectedKmlFeatures { get; private set; } = new List<KmlFeature>();
        public bool PlaceBlocks { get; private set; }
        public string SelectedBlockType { get; private set; }

        private List<KmlFeature> _kmlFeatures;
        private List<string> _pdfLayers;
        private string _mode; // "KML" or "PDF"

        public ImportSelectionWindow(string mode, List<KmlFeature> kmlFeatures, List<string> pdfLayers)
        {
            InitializeComponent();
            _mode = mode;
            _kmlFeatures = kmlFeatures;
            _pdfLayers = pdfLayers;

            if (_mode == "KML")
            {
                TxtHeaderTitle.Text = "KML/KMZ FEATURE SELECTOR";
                GridKmlMode.Visibility = Visibility.Visible;
                GridPdfMode.Visibility = Visibility.Collapsed;
                PanelPointOptions.Visibility = Visibility.Visible;
                PopulateKmlTree();
            }
            else
            {
                TxtHeaderTitle.Text = "SELECT BOUNDARY LAYER FROM PDF";
                GridKmlMode.Visibility = Visibility.Collapsed;
                GridPdfMode.Visibility = Visibility.Visible;
                PanelPointOptions.Visibility = Visibility.Collapsed;
                PopulatePdfList();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSelectAllKml_Click(object sender, RoutedEventArgs e)
        {
            SetAllKmlCheckedState(true);
        }

        private void BtnDeselectAllKml_Click(object sender, RoutedEventArgs e)
        {
            SetAllKmlCheckedState(false);
        }

        private void SetAllKmlCheckedState(bool isChecked)
        {
            foreach (TreeViewItem folderItem in TreeKmlFeatures.Items)
            {
                var folderPanel = folderItem.Header as StackPanel;
                if (folderPanel != null && folderPanel.Children[0] is CheckBox folderCheck)
                {
                    folderCheck.IsChecked = isChecked;
                }

                foreach (TreeViewItem childItem in folderItem.Items)
                {
                    var childPanel = childItem.Header as StackPanel;
                    if (childPanel != null && childPanel.Children[0] is CheckBox childCheck)
                    {
                        childCheck.IsChecked = isChecked;
                    }
                }
            }
        }

        private void ChkPlaceBlocks_Changed(object sender, RoutedEventArgs e)
        {
            if (GridBlockSelection != null)
            {
                GridBlockSelection.IsEnabled = ChkPlaceBlocks.IsChecked == true;
            }
        }

        private void PopulatePdfList()
        {
            LstPdfLayers.ItemsSource = _pdfLayers;
            if (_pdfLayers.Count > 0) LstPdfLayers.SelectedIndex = 0;
        }

        private void PopulateKmlTree()
        {
            TreeKmlFeatures.Items.Clear();
            if (_kmlFeatures == null) return;

            // Group by folder
            var folders = _kmlFeatures.GroupBy(f => f.FolderName).OrderBy(g => g.Key);
            foreach (var folderGroup in folders)
            {
                var folderItem = new TreeViewItem();
                var folderHeader = new StackPanel { Orientation = Orientation.Horizontal };
                var folderCheck = new CheckBox { Content = $"{folderGroup.Key} ({folderGroup.Count()} elements)", FontWeight = FontWeights.Bold };
                folderHeader.Children.Add(folderCheck);
                folderItem.Header = folderHeader;
                folderItem.IsExpanded = true;

                var childItems = new List<CheckBox>();
                foreach (var feature in folderGroup)
                {
                    var childItem = new TreeViewItem();
                    var childHeader = new StackPanel { Orientation = Orientation.Horizontal };
                    string geomIcon = feature.Type == "Point" ? "📍" : "⬡";
                    var childCheck = new CheckBox { Content = $"{geomIcon} {feature.Name} [{feature.Type}]", Tag = feature };
                    childHeader.Children.Add(childCheck);
                    childItem.Header = childHeader;
                    folderItem.Items.Add(childItem);
                    childItems.Add(childCheck);

                    // Child checkbox click handler
                    childCheck.Checked += (s, e) => { UpdateFolderCheckState(folderCheck, childItems); };
                    childCheck.Unchecked += (s, e) => { UpdateFolderCheckState(folderCheck, childItems); };
                }

                // Folder checkbox click handler
                folderCheck.Click += (s, e) =>
                {
                    bool isChecked = folderCheck.IsChecked == true;
                    foreach (var childCheck in childItems)
                    {
                        childCheck.IsChecked = isChecked;
                    }
                };

                // Default checked
                folderCheck.IsChecked = true;
                foreach (var childCheck in childItems) childCheck.IsChecked = true;

                TreeKmlFeatures.Items.Add(folderItem);
            }
        }

        private void UpdateFolderCheckState(CheckBox folderCheck, List<CheckBox> childItems)
        {
            int checkedCount = childItems.Count(c => c.IsChecked == true);
            if (checkedCount == 0)
            {
                folderCheck.IsChecked = false;
            }
            else if (checkedCount == childItems.Count)
            {
                folderCheck.IsChecked = true;
            }
            else
            {
                folderCheck.IsChecked = null; // Indeterminate state
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == "KML")
            {
                SelectedKmlFeatures.Clear();
                // Find all checked features in tree
                foreach (TreeViewItem folderItem in TreeKmlFeatures.Items)
                {
                    foreach (TreeViewItem childItem in folderItem.Items)
                    {
                        var panel = childItem.Header as StackPanel;
                        if (panel != null && panel.Children[0] is CheckBox chk && chk.IsChecked == true && chk.Tag is KmlFeature feature)
                        {
                            SelectedKmlFeatures.Add(feature);
                        }
                    }
                }

                if (SelectedKmlFeatures.Count == 0)
                {
                    MessageBox.Show("Please select at least one feature to import.", "Selection Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                PlaceBlocks = ChkPlaceBlocks.IsChecked == true;
                if (PlaceBlocks && CboBlockType.SelectedItem is ComboBoxItem selectedItem)
                {
                    SelectedBlockType = selectedItem.Tag as string;
                }
            }
            else
            {
                if (LstPdfLayers.SelectedItem == null)
                {
                    MessageBox.Show("Please select a layer from the list.", "Selection Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedPdfLayer = LstPdfLayers.SelectedItem.ToString();
            }

            DialogResult = true;
            Close();
        }
    }
}
