using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FTTHBasemap.Model;
using FTTHBasemap.Licensing;

namespace FTTHBasemap.UI
{
    public partial class ActivationWindow : Window
    {
        private DispatcherTimer _pollTimer;
        private string _paymentId;
        private string _txSalt;
        private double _amount;


        public ActivationWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtDeviceId.Text = $"Device ID: {LicensingService.Instance.GetDeviceId()}";

                var settings = BasemapSettings.Instance;
                
                // Fetch dynamic base price from online database
                double basePrice = await LicensingService.Instance.GetBasePriceOnlineAsync();

                // Check if we have an active transaction pending to maintain same amount suffix
                if (!string.IsNullOrEmpty(settings.PendingSalt) && settings.PendingSalt.Contains("-"))
                {
                    string[] parts = settings.PendingSalt.Split('-');
                    if (parts.Length == 2)
                    {
                        _txSalt = parts[0];
                        string suffixStr = parts[1];
                        if (int.TryParse(suffixStr, out int suffix))
                        {
                            _amount = basePrice + suffix;
                            _paymentId = $"FTTH-{suffix:D3}-{_txSalt}";
                        }
                    }
                }

                // If no pending transaction exists, generate a new one
                if (string.IsNullOrEmpty(_paymentId))
                {
                    var rand = new Random();
                    int suffix = rand.Next(100, 999); // 3-digit suffix
                    _txSalt = GenerateRandomSalt(6);
                    _amount = basePrice + suffix;
                    _paymentId = $"FTTH-{suffix:D3}-{_txSalt}";

                    // Save state so if window is closed and opened during same hour, suffix is persistent
                    settings.PendingSalt = $"{_txSalt}-{suffix}";
                    settings.Save();
                }

                // Set UI texts
                TxtAmount.Text = $"Rp {_amount:N0}".Replace(",", ".");
                TxtPaymentId.Text = _paymentId;
                TxtTelegramCmd.Text = $"/pay {_paymentId}";

                // Generate dynamic QRIS and load QR Code image
                string qrisPayload = LicensingService.Instance.GenerateDynamicQris(_amount);
                string qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={Uri.EscapeDataString(qrisPayload)}";

                try
                {
                    byte[] qrBytes;
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                        qrBytes = await client.GetByteArrayAsync(qrUrl);
                    }

                    var bmi = new BitmapImage();
                    using (var ms = new System.IO.MemoryStream(qrBytes))
                    {
                        bmi.BeginInit();
                        bmi.CacheOption = BitmapCacheOption.OnLoad;
                        bmi.StreamSource = ms;
                        bmi.EndInit();
                    }
                    bmi.Freeze();
                    ImgQrCode.Source = bmi;
                }
                catch
                {
                    TxtQrPlaceholder.Visibility = Visibility.Visible;
                    TxtQrPlaceholder.Text = "Gagal memuat QR Code.\nPeriksa koneksi internet.";
                }

                // Start polling timer
                _pollTimer = new DispatcherTimer();
                _pollTimer.Interval = TimeSpan.FromSeconds(5);
                _pollTimer.Tick += PollTimer_Tick;
                _pollTimer.Start();
                TxtStatus.Text = "Status: Menunggu transfer (Polling aktif)...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal inisialisasi aktivasi: {ex.Message}", "FTTH Basemap");
            }
        }

        private async void PollTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_paymentId)) return;

            var result = await LicensingService.Instance.CheckPaymentStatusAsync(_paymentId);
            if (result != null && result.Status == "Approved" && !string.IsNullOrEmpty(result.ActivationKey))
            {
                _pollTimer.Stop();

                // Save new license
                var settings = BasemapSettings.Instance;
                settings.LicenseKey = result.ActivationKey;
                settings.OfflineGraceTimestamp = DateTime.UtcNow.ToString("o");
                
                // Track salt to prevent reuse
                string devId, salt;
                DateTime exp;
                if (LicensingService.Instance.VerifyActivationKey(result.ActivationKey, out devId, out salt, out exp))
                {
                    settings.LastUsedSalt = salt;
                }

                // Clear pending transaction state
                settings.PendingSalt = "";
                settings.Save();

                TxtStatus.Text = "Status: AKTIF! Menutup form...";
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;

                MessageBox.Show("Pembayaran Terverifikasi! Software FTTH Basemap telah berhasil diaktifkan.", "Aktivasi Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
        }

        private void BtnManualActivate_Click(object sender, RoutedEventArgs e)
        {
            string key = TxtActivationKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Silahkan masukkan Serial Key terlebih dahulu.", "Aktivasi Gagal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string devId, salt;
            DateTime exp;
            if (LicensingService.Instance.VerifyActivationKey(key, out devId, out salt, out exp))
            {
                if (devId != LicensingService.Instance.GetDeviceId())
                {
                    MessageBox.Show("Serial Key ini dibuat untuk perangkat lain.", "Aktivasi Gagal", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (DateTime.UtcNow > exp)
                {
                    MessageBox.Show("Serial Key ini sudah kadaluarsa.", "Aktivasi Gagal", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var settings = BasemapSettings.Instance;
                if (salt == settings.LastUsedSalt)
                {
                    MessageBox.Show("Serial Key ini sudah pernah digunakan sebelumnya untuk siklus ini.", "Aktivasi Gagal", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Valid key manually inputted
                if (_pollTimer != null) _pollTimer.Stop();

                settings.LicenseKey = key;
                settings.LastUsedSalt = salt;
                settings.OfflineGraceTimestamp = DateTime.UtcNow.ToString("o");
                settings.PendingSalt = "";
                settings.Save();

                MessageBox.Show("Aktivasi Manual Sukses! Software FTTH Basemap aktif secara penuh.", "Aktivasi Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Serial Key tidak valid. Periksa kembali karakter yang Anda masukkan.", "Aktivasi Gagal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCopyPaymentId_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_paymentId);
                MessageBox.Show("Payment ID berhasil disalin ke Clipboard.", "FTTH Basemap", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void BtnCopyCmd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText($"/pay {_paymentId}");
                MessageBox.Show("Perintah Telegram berhasil disalin.", "FTTH Basemap", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch { }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
        }

        private string GenerateRandomSalt(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var result = new char[length];
            var rand = new Random();
            for (int i = 0; i < length; i++)
            {
                result[i] = chars[rand.Next(chars.Length)];
            }
            return new string(result);
        }
    }
}
