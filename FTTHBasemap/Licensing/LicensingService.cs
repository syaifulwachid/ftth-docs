using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Management;
using System.Security.Cryptography;
using Newtonsoft.Json;
using FTTHBasemap.Model;

namespace FTTHBasemap.Licensing
{
    public class LicensingService
    {
        private static LicensingService _instance;
        public static LicensingService Instance => _instance ?? (_instance = new LicensingService());

        private LicensingService()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
            }
            catch { }
        }

        // Base API Web App URL (deployed Google Apps Script URL)
        public string ApiUrl { get; set; } = "https://script.google.com/macros/s/AKfycbxgtBcdELeHFXWibuieFIov1OUQcXowq0XPgYoSrMA4wORqgdzG6K4UbDqhUAIl_B0u9w/exec";

        // RSA Public Key (XML format) corresponding to the generated private key
        private const string PublicKeyXml = "<RSAKeyValue><Modulus>zd437M9WHjSCW2+36reqQolbh8S0h/0IDm52amg5HW5wGCEmhjiq0TPydBbCl50Qe62s4DokEIN9hyvSs4y4Aoz4acUxx2xyKnoFUDdX91NEJua1q7JBa600E4Ohwuq6zLsLK9/GbrCY6cAkOj56jgfNCCdSVpODhhk0H7OvFyCDAm1NSqJtyqCarDDX0M7SozkyR+bogYGuSEXKmNMnwHnEXMgMn6QMFtdSM6M9WAMVSkOZOubJxBMKxH2pEXwtrQOtvdgMXZYwE8LqTtEppD9wqOjDpTzY//+/Hbwi9rwowEpkwhCbA45FUVptm62MVygEgK7URgMNSE7wZuj/LQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // Base Static QRIS text (without trailing CRC code F81B)
        private const string BaseQrisText = "00020101021126760024ID.CO.SPEEDCASH.MERCHANT01189360081530003116760215ID10250031167670303UKE51440014ID.CO.QRIS.WWW0215ID10254596655490303UKE5204597853033605802ID5918SWD SOFT DEVELOPER6008LUMAJANG61056738362410509S298233110117202512011605032730703A016304";

        private string _cachedDeviceId;

        public string GetDeviceId()
        {
            if (!string.IsNullOrEmpty(_cachedDeviceId))
                return _cachedDeviceId;

            string hdid = "";
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        hdid += obj["UUID"]?.ToString();
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        hdid += obj["ProcessorId"]?.ToString();
                        break;
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(hdid) || hdid.Replace(" ", "").Length < 5)
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    {
                        if (key != null)
                        {
                            hdid = key.GetValue("MachineGuid")?.ToString();
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(hdid))
            {
                hdid = Environment.MachineName + "_" + Environment.UserName;
            }

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(hdid));
                var sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                _cachedDeviceId = sb.ToString();
                return _cachedDeviceId;
            }
        }

        public string GenerateDynamicQris(double amount)
        {
            string amountStr = Math.Round(amount).ToString();
            string searchTag = "5303360";
            int idx = BaseQrisText.IndexOf(searchTag);
            if (idx == -1)
                return BaseQrisText; // Fallback to static if tags are corrupt

            string leftPart = BaseQrisText.Substring(0, idx + searchTag.Length);
            string rightPart = BaseQrisText.Substring(idx + searchTag.Length);
            string amountTag = "54" + amountStr.Length.ToString("D2") + amountStr;

            string payloadWithoutCrc = leftPart + amountTag + rightPart;
            ushort crc = CalculateCrc16Ccitt(payloadWithoutCrc);
            return payloadWithoutCrc + crc.ToString("X4");
        }

        private static ushort CalculateCrc16Ccitt(string data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            ushort crc = 0xFFFF;
            foreach (byte b in bytes)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                    {
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    }
                    else
                    {
                        crc = (ushort)(crc << 1);
                    }
                }
            }
            return crc;
        }

        private static string PadBase64(string base64)
        {
            int mod = base64.Length % 4;
            if (mod > 0)
            {
                base64 += new string('=', 4 - mod);
            }
            return base64;
        }

        public bool VerifyActivationKey(string key, out string deviceId, out string txSalt, out DateTime expiresAt)
        {
            deviceId = "";
            txSalt = "";
            expiresAt = DateTime.MinValue;

            if (string.IsNullOrEmpty(key)) return false;

            string[] parts = key.Split('.');
            if (parts.Length != 2) return false;

            try
            {
                string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(parts[0])));
                byte[] signature = Convert.FromBase64String(PadBase64(parts[1]));

                var payload = JsonConvert.DeserializeObject<Dictionary<string, string>>(payloadJson);
                if (payload == null || !payload.ContainsKey("deviceId") || !payload.ContainsKey("txSalt") || !payload.ContainsKey("expiresAt"))
                    return false;

                deviceId = payload["deviceId"];
                txSalt = payload["txSalt"];
                expiresAt = DateTime.Parse(payload["expiresAt"]);

                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(PublicKeyXml);
                    byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                    return rsa.VerifyData(payloadBytes, CryptoConfig.MapNameToOID("SHA256"), signature);
                }
            }
            catch
            {
                return false;
            }
        }

        public bool IsLicenseActive()
        {
            var settings = BasemapSettings.Instance;
            if (string.IsNullOrEmpty(settings.LicenseKey))
                return false;

            string deviceId, txSalt;
            DateTime expiresAt;
            if (!VerifyActivationKey(settings.LicenseKey, out deviceId, out txSalt, out expiresAt))
                return false;

            // Verify device ID matches
            if (deviceId != GetDeviceId())
                return false;

            // Verify local expiration date
            if (DateTime.UtcNow > expiresAt)
                return false;

            // Offline grace check (7 days)
            if (!string.IsNullOrEmpty(settings.OfflineGraceTimestamp))
            {
                if (DateTime.TryParse(settings.OfflineGraceTimestamp, out DateTime lastOnlineCheck))
                {
                    if ((DateTime.UtcNow - lastOnlineCheck).TotalDays > 7)
                    {
                        // Exceeded offline grace period
                        return false;
                    }
                }
            }

            return true;
        }

        public double GetRemainingDays()
        {
            var settings = BasemapSettings.Instance;
            if (string.IsNullOrEmpty(settings.LicenseKey))
                return 0;

            string deviceId, txSalt;
            DateTime expiresAt;
            if (!VerifyActivationKey(settings.LicenseKey, out deviceId, out txSalt, out expiresAt))
                return 0;

            return (expiresAt - DateTime.UtcNow).TotalDays;
        }

        // Web API Methods
        public async Task<LicenseCheckResult> CheckLicenseOnlineAsync()
        {
            string url = $"{ApiUrl}?action=checkLicense&deviceId={GetDeviceId()}&version=4.0";
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<LicenseCheckResult>(content);
                        if (result != null)
                        {
                            var settings = BasemapSettings.Instance;
                            if (result.Status == "Revoked")
                            {
                                settings.LicenseKey = "";
                                settings.Save();
                            }
                            else if (result.Status == "Active")
                            {
                                settings.OfflineGraceTimestamp = DateTime.UtcNow.ToString("o");
                                settings.Save();
                            }
                            return result;
                        }
                    }
                }
            }
            catch
            {
                // Return offline evaluation state if network check fails
            }

            return new LicenseCheckResult
            {
                Status = IsLicenseActive() ? "Active" : "Expired",
                Message = "Offline Mode"
            };
        }

        public async Task<PaymentCheckResult> CheckPaymentStatusAsync(string paymentId)
        {
            string url = $"{ApiUrl}?action=checkPayment&paymentId={paymentId}&deviceId={GetDeviceId()}";
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<PaymentCheckResult>(content);
                    }
                }
            }
            catch { }
            return new PaymentCheckResult { Status = "Pending" };
        }

        public async Task<double> GetBasePriceOnlineAsync()
        {
            string url = $"{ApiUrl}?action=getPrice";
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, double>>(content);
                        if (result != null && result.ContainsKey("BasePrice"))
                        {
                            return result["BasePrice"];
                        }
                    }
                }
            }
            catch { }
            return 100000; // default fallback if offline
        }

        public async Task<bool> SendTelemetryAsync(int projectsCount)
        {
            if (projectsCount <= 0) return true;
            string url = $"{ApiUrl}?action=telemetry";
            try
            {
                using (var client = new HttpClient())
                {
                    var payload = new Dictionary<string, string>
                    {
                        { "deviceId", GetDeviceId() },
                        { "projectsCreated", projectsCount.ToString() },
                        { "version", "4.0" }
                    };
                    string json = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(url, content);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public class LicenseCheckResult
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public string ExpirationDate { get; set; }
    }

    public class PaymentCheckResult
    {
        public string Status { get; set; }
        public string ActivationKey { get; set; }
    }
}
