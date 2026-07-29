using System;
using Autodesk.AutoCAD.Windows;
using FTTHBasemap.UI;

namespace FTTHBasemap
{
    public static class PaletteManager
    {
        private static PaletteSet _basemapPaletteSet;
        private static BasemapPanel _basemapPanel;

        private static PaletteSet _streetViewPaletteSet;
        private static StreetViewViewer _streetViewViewer;

        public static BasemapPanel BasemapPanel => _basemapPanel;
        public static StreetViewViewer StreetViewViewer => _streetViewViewer;

        /// <summary>
        /// Shows the FTTH Basemap WPF Dockable Palette.
        /// </summary>
        public static void ShowPanel()
        {
            if (_basemapPaletteSet == null)
            {
                // Unique GUID for AutoCAD to persist size and docking position
                _basemapPaletteSet = new PaletteSet("FTTH Design Planner v3.6.9", new Guid("9DE9F225-C119-4F54-BF19-335DF42F6B8C"))
                {
                    Size = new System.Drawing.Size(300, 600),
                    MinimumSize = new System.Drawing.Size(250, 400),
                    Style = PaletteSetStyles.ShowCloseButton |
                            PaletteSetStyles.ShowAutoHideButton |
                            PaletteSetStyles.ShowPropertiesMenu
                };

                _basemapPanel = new BasemapPanel();
                _basemapPaletteSet.AddVisual("FTTH Design Planner v3.6.9", _basemapPanel);
            }

            _basemapPaletteSet.Visible = true;
        }

        /// <summary>
        /// Shows the floating FTTH StreetView Viewer.
        /// </summary>
        public static void ShowStreetView()
        {
            if (_streetViewPaletteSet == null)
            {
                // Unique GUID for the StreetView window
                _streetViewPaletteSet = new PaletteSet("FTTH StreetView Viewer", new Guid("8DE9F225-C119-4F54-BF19-335DF42F6B8B"))
                {
                    Size = new System.Drawing.Size(600, 500),
                    MinimumSize = new System.Drawing.Size(300, 300),
                    Style = PaletteSetStyles.ShowCloseButton |
                            PaletteSetStyles.ShowAutoHideButton |
                            PaletteSetStyles.ShowPropertiesMenu
                };

                _streetViewViewer = new StreetViewViewer();
                _streetViewPaletteSet.AddVisual("StreetView Canvas", _streetViewViewer);
            }

            _streetViewPaletteSet.Visible = true;
        }

        /// <summary>
        /// Logs a status message to the status textblock on the WPF panel.
        /// </summary>
        public static void Log(string msg)
        {
            if (_basemapPanel != null)
            {
                if (_basemapPanel.Dispatcher.CheckAccess())
                {
                    _basemapPanel.LogMessage(msg);
                }
                else
                {
                    _basemapPanel.Dispatcher.Invoke(() => _basemapPanel.LogMessage(msg));
                }
            }
        }

        private static readonly object _fileLock = new object();

        public static void LogToFile(string msg)
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                string logDir = "";
                if (doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    logDir = System.IO.Path.GetDirectoryName(doc.Name);
                }
                if (string.IsNullOrEmpty(logDir) || !System.IO.Directory.Exists(logDir))
                {
                    logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FTTHBasemap");
                }
                System.IO.Directory.CreateDirectory(logDir);
                string logPath = System.IO.Path.Combine(logDir, "ftth_debug.log");
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}";
                lock (_fileLock)
                {
                    System.IO.File.AppendAllText(logPath, logLine);
                }
            }
            catch { }
        }
    }
}
