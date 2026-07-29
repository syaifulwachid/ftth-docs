using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace FTTHBasemap.UI
{
    public partial class StreetViewViewer : UserControl
    {
        public double CurrentLat { get; private set; } = -7.790131;
        public double CurrentLon { get; private set; } = 112.739462;
        public double CurrentHeading { get; private set; } = 26.0;
        public double CurrentPitch { get; private set; } = 90.0;
        public double CurrentFov { get; private set; } = 75.0;
        public string CurrentUrl { get; private set; } = string.Empty;

        public StreetViewViewer()
        {
            InitializeComponent();
            InitializeWebView();
            this.Unloaded += (s, e) => CameraIndicatorManager.ClearIndicator();
        }

        private async void InitializeWebView()
        {
            try
            {
                PaletteManager.LogToFile("InitializeWebView() started.");
                // Specify a writable user data folder for WebView2 to prevent failure in AutoCAD program files
                string tempFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FTTHBasemap", "WebView2");
                Directory.CreateDirectory(tempFolder);
                PaletteManager.LogToFile("WebView2 user data folder: " + tempFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, tempFolder);
                PaletteManager.LogToFile("CoreWebView2Environment created successfully.");
                await MyWebView.EnsureCoreWebView2Async(env);
                PaletteManager.LogToFile("EnsureCoreWebView2Async completed.");

                // Set Desktop UserAgent to force Google Maps to show satellite tiles and pegman
                MyWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                MyWebView.WebMessageReceived += MyWebView_WebMessageReceived;
                MyWebView.SourceChanged += MyWebView_SourceChanged;
                MyWebView.NavigationCompleted += MyWebView_NavigationCompleted;

                // Inject capture-phase event listeners on document created so we catch right click and middle click before Google Maps intercepts them.
                string script = @"
                    (function() {
                        function safePostMessage(payload) {
                            if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
                                window.chrome.webview.postMessage(payload);
                            } else {
                                console.log('WebView2 postMessage not ready, retrying...');
                                setTimeout(function() {
                                    safePostMessage(payload);
                                }, 50);
                            }
                        }

                        document.addEventListener('contextmenu', function(e) {
                            e.preventDefault();
                            e.stopPropagation();
                            safePostMessage({
                                action: 'right_click',
                                clientX: e.clientX,
                                clientY: e.clientY,
                                viewWidth: window.innerWidth,
                                viewHeight: window.innerHeight
                            });
                        }, true);

                        document.addEventListener('mousedown', function(e) {
                            if (e.button === 1) { // Middle click
                                e.preventDefault();
                                e.stopPropagation();
                                safePostMessage({
                                    action: 'middle_click',
                                    clientX: e.clientX,
                                    clientY: e.clientY,
                                    viewWidth: window.innerWidth,
                                    viewHeight: window.innerHeight
                                });
                            }
                        }, true);
                    })();
                ";
                await MyWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                PaletteManager.LogToFile("AddScriptToExecuteOnDocumentCreatedAsync completed.");

                // Load initial location
                MyWebView.Source = new Uri("https://www.google.com/maps/@-7.790131,112.739462,3a,75y,26h,90t/data=!3m7!1e1!3m5!1s-LgC_R_Gf2d6jO30v6HRAQ!2e0!5s20240501T000000!7i16384!8i8192");
                PaletteManager.LogToFile("WebView2 navigation initiated to initial location.");
            }
            catch (Exception ex)
            {
                string errMsg = "WebView2 init failed in Viewer: " + ex.Message + "\nStack: " + ex.StackTrace;
                System.Diagnostics.Debug.WriteLine(errMsg);
                PaletteManager.LogToFile(errMsg);
            }
        }

        private void MyWebView_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            UpdateCamStatus();
        }

        private void MyWebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            UpdateCamStatus();
        }

        private void UpdateCamStatus()
        {
            try
            {
                if (MyWebView.Source == null) return;
                CurrentUrl = MyWebView.Source.ToString();
                
                if (ParseStreetViewUrl(CurrentUrl, out double lat, out double lon, out double heading, out double pitch, out double fov))
                {
                    CurrentLat = lat;
                    CurrentLon = lon;
                    CurrentHeading = heading;
                    CurrentPitch = pitch;
                    CurrentFov = fov;

                    // Update Main Panel UI fields if it exists
                    var panel = PaletteManager.BasemapPanel;
                    if (panel != null)
                    {
                        panel.Dispatcher.Invoke(() =>
                        {
                            panel.UpdateCamReadout(lat, lon, heading);
                        });
                    }
                }
            }
            catch { }
        }

        private void MyWebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string msg = e.WebMessageAsJson;
                PaletteManager.LogToFile("MyWebView_WebMessageReceived: " + msg);
                var payload = Newtonsoft.Json.Linq.JObject.Parse(msg);
                string action = payload["action"] != null ? payload["action"].ToString() : null;
                if (action == "right_click" || action == "middle_click")
                {
                    double clientX = payload["clientX"] != null ? (double)payload["clientX"] : 0;
                    double clientY = payload["clientY"] != null ? (double)payload["clientY"] : 0;
                    double viewWidth = payload["viewWidth"] != null ? (double)payload["viewWidth"] : 1;
                    double viewHeight = payload["viewHeight"] != null ? (double)payload["viewHeight"] : 1;

                    var panel = PaletteManager.BasemapPanel;
                    if (panel != null)
                    {
                        panel.Dispatcher.Invoke(() =>
                        {
                            bool isChecked = panel.ChkRightClickPlace.IsChecked == true;
                            PaletteManager.LogToFile("WebMessageReceived triggering placement. Checkbox state: " + isChecked);
                            if (isChecked)
                            {
                                panel.PlaceMarkerFromStreetViewClick(clientX, clientY, viewWidth, viewHeight);
                            }
                        });
                    }
                    else
                    {
                        PaletteManager.LogToFile("WebMessageReceived failed: BasemapPanel is null.");
                    }
                }
            }
            catch (Exception ex)
            {
                PaletteManager.LogToFile("Exception in MyWebView_WebMessageReceived: " + ex.Message + "\nStack: " + ex.StackTrace);
            }
        }

        public void LoadCoordinates(double lat, double lon)
        {
            if (this.Dispatcher.CheckAccess())
            {
                string urlStr = $"https://www.google.com/maps/@?api=1&map_action=pano&viewpoint={lat},{lon}";
                MyWebView.Source = new Uri(urlStr);
                CurrentLat = lat;
                CurrentLon = lon;
                
                var panel = PaletteManager.BasemapPanel;
                if (panel != null)
                {
                    panel.UpdateCamReadout(lat, lon, 90.0);
                    panel.LogMessage($"Loading Street View at: {lat:F6}, {lon:F6}");
                }
            }
            else
            {
                this.Dispatcher.Invoke(() => LoadCoordinates(lat, lon));
            }
        }

        private static bool ParseStreetViewUrl(string url, out double lat, out double lon, out double heading, out double pitch, out double fov)
        {
            lat = 0;
            lon = 0;
            heading = 0;
            pitch = 90.0;
            fov = 75.0;

            try
            {
                int atIndex = url.IndexOf('@');
                if (atIndex == -1) return false;

                string sub = url.Substring(atIndex + 1);
                string[] parts = sub.Split(',');

                if (parts.Length >= 2)
                {
                    if (double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedLat) &&
                        double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedLon))
                    {
                        lat = parsedLat;
                        lon = parsedLon;

                        foreach (string part in parts)
                        {
                            if (part.EndsWith("h") || part.EndsWith("y"))
                            {
                                string valStr = part.Substring(0, part.Length - 1);
                                if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedHeading))
                                {
                                    heading = parsedHeading;
                                }
                            }
                            else if (part.EndsWith("t"))
                            {
                                string valStr = part.Substring(0, part.Length - 1);
                                if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedPitch))
                                {
                                    pitch = parsedPitch;
                                }
                            }
                            else if (part.EndsWith("y") && part.Length > 1 && char.IsDigit(part[0]))
                            {
                                string valStr = part.Substring(0, part.Length - 1);
                                if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedFov))
                                {
                                    fov = parsedFov;
                                }
                            }
                        }
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
