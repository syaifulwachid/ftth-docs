using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;

namespace FTTHBasemap.UI
{
    public partial class InfoWindow : Window
    {
        public InfoWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                // Only drag if the click is on the window background, not on interactive text or buttons
                if (e.OriginalSource != null && !(e.OriginalSource is TextBlock) && !(e.OriginalSource is Button))
                {
                    this.DragMove();
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void TxtLinkedin_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                string url = "https://www.linkedin.com/in/syaiful-wachid-5373n/";
                
                // 1. Try launching Chrome directly (most common browser)
                try
                {
                    Process.Start("chrome.exe", url);
                    return;
                }
                catch { }

                // 2. Try launching Microsoft Edge directly (guaranteed to be on Windows 10/11)
                try
                {
                    Process.Start("msedge.exe", url);
                    return;
                }
                catch { }

                // 3. Try launching Firefox directly
                try
                {
                    Process.Start("firefox.exe", url);
                    return;
                }
                catch { }

                // 4. Fallback: Shell execute URL (in case app paths aren't configured but shell associations work)
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return;
                }
                catch (Exception ex1)
                {
                    try
                    {
                        // 5. Fallback: Command interpreter start command
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd",
                            Arguments = $"/c start \"\" \"{url}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch (Exception ex2)
                    {
                        MessageBox.Show(
                            $"Tidak dapat membuka browser secara otomatis.\n\n" +
                            $"Detail Kesalahan 1: {ex1.Message}\n" +
                            $"Detail Kesalahan 2: {ex2.Message}\n\n" +
                            $"Silakan salin tautan berikut secara manual:\n{url}",
                            "FTTH Basemap - Link Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                }
            }
        }
    }
}
