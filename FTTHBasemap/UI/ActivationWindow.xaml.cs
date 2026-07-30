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
        private int _suffix = -1;
        private System.Collections.Generic.Dictionary<string, double> _prices;

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

                // Load prices online
                TxtStatus.Text = "Memuat daftar harga dari server...";
                _prices = await LicensingService.Instance.GetPricesOnlineAsync();

                // If trial price is not returned (user already used it or disabled), hide/remove the option
                if (_prices != null && !_prices.ContainsKey("BasePrice_1M_Trial"))
                {
                    System.Windows.Controls.ComboBoxItem trialItem = null;
                    foreach (System.Windows.Controls.ComboBoxItem item in CboPackage.Items)
                    {
                        if (item.Tag != null && item.Tag.ToString() == "1_trial")
                        {
                            trialItem = item;
                            break;
                        }
                    }
                    if (trialItem != null)
                    {
                        CboPackage.Items.Remove(trialItem);
                        // Select the 1 Month regular package by default
                        CboPackage.SelectedIndex = 0;
                    }
                }

                // Update ComboBox Item Content based on server prices
                if (_prices != null && _prices.Count > 0)
                {
                    foreach (System.Windows.Controls.ComboBoxItem item in CboPackage.Items)
                    {
                        if (item.Tag != null)
                        {
                            string tagStr = item.Tag.ToString();
                            string priceKey = "";
                            string prefix = "";
                            
                            if (tagStr == "1_trial")
                            {
                                priceKey = "BasePrice_1M_Trial";
                                prefix = "1 Bulan (TRIAL)";
                            }
                            else if (int.TryParse(tagStr, out int dur))
                            {
                                priceKey = $"BasePrice_{dur}M";
                                prefix = $"{dur} Bulan";
                            }
                            
                            if (!string.IsNullOrEmpty(priceKey) && _prices.ContainsKey(priceKey))
                            {
                                double priceVal = _prices[priceKey];
                                item.Content = $"{prefix} (Rp {priceVal:N0})".Replace(",", ".");
                            }
                        }
                    }
                }

                // Check if we have an active transaction pending to maintain same amount suffix
                if (!string.IsNullOrEmpty(settings.PendingSalt) && settings.PendingSalt.Contains("-"))
                {
                    string[] parts = settings.PendingSalt.Split('-');
                    if (parts.Length == 2)
                    {
                        _txSalt = parts[0];
                        string suffixStr = parts[1];
                        int.TryParse(suffixStr, out _suffix);
                    }
                }

                // If no pending transaction exists, generate a new one
                if (_suffix == -1)
                {
                    var rand = new Random();
                    _suffix = rand.Next(100, 999); // 3-digit suffix
                    _txSalt = GenerateRandomSalt(6);

                    // Save state so if window is closed and opened during same hour, suffix is persistent
                    settings.PendingSalt = $"{_txSalt}-{_suffix}";
                    settings.Save();
                }

                // Update payment information and load QR Code
                UpdatePaymentInfo();

                TxtStatus.Text = "Status: Silakan isi kontak & klik Kirim Request.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGray;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal inisialisasi aktivasi: {ex.Message}", "FTTH Basemap");
            }
        }

        private void UpdatePaymentInfo()
        {
            if (_prices == null || _suffix == -1) return;

            // Get selected package duration
            int durationMonths = 1;
            bool isTrial = false;
            if (CboPackage.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null)
                {
                    string tagStr = selectedItem.Tag.ToString();
                    if (tagStr.Contains("trial"))
                    {
                        isTrial = true;
                        durationMonths = 1;
                    }
                    else if (int.TryParse(tagStr, out int dur))
                    {
                        durationMonths = dur;
                    }
                }
            }

            // Get corresponding base price
            string priceKey = isTrial ? "BasePrice_1M_Trial" : $"BasePrice_{durationMonths}M";
            double basePrice = isTrial ? 30000 : 100000; // default fallback
            if (_prices.ContainsKey(priceKey))
            {
                basePrice = _prices[priceKey];
            }
            else if (_prices.ContainsKey("BasePrice"))
            {
                // Fallback to legacy base price and apply discount formula
                double legacyPrice = _prices["BasePrice"];
                if (isTrial) basePrice = 30000;
                else if (durationMonths == 3) basePrice = Math.Round(legacyPrice * 3 * 0.9);
                else if (durationMonths == 6) basePrice = Math.Round(legacyPrice * 6 * 0.85);
                else if (durationMonths == 12) basePrice = Math.Round(legacyPrice * 12 * 0.75);
                else basePrice = legacyPrice;
            }

            _amount = basePrice + _suffix;
            _paymentId = $"FTTH-{_suffix:D3}-{_txSalt}";

            TxtAmount.Text = $"Rp {_amount:N0}".Replace(",", ".");
            TxtPaymentId.Text = _paymentId;

            // Generate dynamic QRIS and load QR Code image
            string qrisPayload = LicensingService.Instance.GenerateDynamicQris(_amount);
            string qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={Uri.EscapeDataString(qrisPayload)}";
            LoadQrCodeImage(qrUrl);
        }

        private async void LoadQrCodeImage(string qrUrl)
        {
            try
            {
                TxtQrPlaceholder.Visibility = Visibility.Collapsed;
                ImgQrCode.Source = null;

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
        }

        private void CboPackage_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdatePaymentInfo();
        }

        private async void BtnRequestActivation_Click(object sender, RoutedEventArgs e)
        {
            string telegramContact = TxtTelegramContact.Text.Trim();
            if (string.IsNullOrEmpty(telegramContact))
            {
                MessageBox.Show("Silakan masukkan Username Telegram atau No WhatsApp Anda agar Admin dapat memverifikasi pembayaran Anda.", "Kontak Diperlukan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Disable controls to prevent double clicks and changes mid-transaction
            CboPackage.IsEnabled = false;
            TxtTelegramContact.IsEnabled = false;
            BtnRequestActivation.IsEnabled = false;

            TxtStatus.Text = "Mengirim permintaan aktivasi ke Admin...";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Yellow;

            int durationMonths = 1;
            if (CboPackage.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                if (selectedItem.Tag != null)
                {
                    string tagStr = selectedItem.Tag.ToString();
                    if (tagStr.Contains("trial"))
                    {
                        durationMonths = 1;
                    }
                    else if (int.TryParse(tagStr, out int dur))
                    {
                        durationMonths = dur;
                    }
                }
            }

            bool success = await LicensingService.Instance.RequestActivationOnlineAsync(_paymentId, telegramContact, durationMonths, _amount);
            if (success)
            {
                TxtStatus.Text = "Status: Menunggu transfer & persetujuan Admin...";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;

                // Start polling timer
                _pollTimer = new DispatcherTimer();
                _pollTimer.Interval = TimeSpan.FromSeconds(5);
                _pollTimer.Tick += PollTimer_Tick;
                _pollTimer.Start();
            }
            else
            {
                TxtStatus.Text = "Status: Gagal mengirim request. Silakan coba lagi.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.Red;

                // Re-enable controls
                CboPackage.IsEnabled = true;
                TxtTelegramContact.IsEnabled = true;
                BtnRequestActivation.IsEnabled = true;

                MessageBox.Show("Gagal mengirim request ke server. Periksa koneksi internet Anda lalu coba lagi.", "Koneksi Bermasalah", MessageBoxButton.OK, MessageBoxImage.Error);
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
