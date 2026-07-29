using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Linq;

namespace AutoCADPathConfigurator
{
    class Program
    {
        static string logFile;

        static void Main(string[] args)
        {
            string myPath = AppDomain.CurrentDomain.BaseDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            logFile = Path.Combine(myPath, "Uninstaller_Log.txt");
            try { File.Delete(logFile); } catch { }

            Log("AutoCAD Path & Startup Suite Uninstaller");
            Log("========================================");
            Log($"Folder  : {myPath}");
            Log($"Waktu   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Log("");

            try
            {
                bool anyChange = false;

                // === METODE 1: HKEY_CURRENT_USER ===
                Log("[INFO] Membersihkan dari HKEY_CURRENT_USER...");
                bool changeHKCU = UnregisterFromHive(Registry.CurrentUser, myPath);
                anyChange = anyChange || changeHKCU;

                // === METODE 2: HKEY_USERS ===
                Log("");
                Log("[INFO] Mencari semua SID user di HKEY_USERS...");
                using (RegistryKey hkUsers = Registry.Users)
                {
                    if (hkUsers != null)
                    {
                        string[] sids = hkUsers.GetSubKeyNames();
                        foreach (string sid in sids)
                        {
                            if (sid.EndsWith("_Classes") || sid == ".DEFAULT" ||
                                sid == "S-1-5-18" || sid == "S-1-5-19" || sid == "S-1-5-20")
                                continue;

                            string autocadPath = $@"{sid}\Software\Autodesk\AutoCAD";
                            using (RegistryKey testKey = hkUsers.OpenSubKey(autocadPath))
                            {
                                if (testKey == null) continue;

                                Log($"[INFO] Ditemukan AutoCAD di SID: {sid}");
                                using (RegistryKey sidKey = hkUsers.OpenSubKey(sid, true))
                                {
                                    if (sidKey != null)
                                    {
                                        bool changeSID = UnregisterFromHive(sidKey, myPath);
                                        anyChange = anyChange || changeSID;
                                    }
                                }
                            }
                        }
                    }
                }

                // === METODE 3: HAPUS SEMUA FILE (KECUALI EXE INI) ===
                Log("\n[INFO] Menghapus file-file di folder instalasi...");
                CleanDirectory(myPath);
                
                // === METODE 4: HAPUS FILE LISENSI DI APPDATA ===
                Log("\n[INFO] Menghapus jejak lisensi di APPDATA di HKEY_USERS/Semua Profil...");
                try 
                {
                    string usersDir = @"C:\Users";
                    if (Directory.Exists(usersDir))
                    {
                        string[] userDirs = Directory.GetDirectories(usersDir);
                        foreach (string dir in userDirs)
                        {
                            try 
                            {
                                string swdFolder = Path.Combine(dir, @"AppData\Roaming\SWDSoftDeveloper");
                                string licFile = Path.Combine(swdFolder, "sysLicense.cfg");
                                string orderFile = Path.Combine(swdFolder, "sysLastOrder.tmp");
                                
                                if (File.Exists(licFile)) {
                                    File.Delete(licFile);
                                    Log($"[OK] Dihapus file lisensi profil: {licFile}");
                                }
                                if (File.Exists(orderFile)) {
                                    File.Delete(orderFile);
                                    Log($"[OK] Dihapus order ID profil: {orderFile}");
                                }
                                
                                // Hapus folder jika kosong
                                if (Directory.Exists(swdFolder) && Directory.GetFileSystemEntries(swdFolder).Length == 0)
                                {
                                    Directory.Delete(swdFolder);
                                }
                            }
                            catch { }
                        }
                    }
                } 
                catch { }

                Log("");
                Log("=== HASIL AKHIR ===");
                Log("[OK] Pembersihan selesai. Semua lisensi dan path registrasi telah dicabut.");
                
            }
            catch (UnauthorizedAccessException)
            {
                Log("[ERROR] Akses registry ditolak. Pastikan Uninstaller berjalan sebagai Administrator.");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            }
        }

        static bool UnregisterFromHive(RegistryKey hive, string myPath)
        {
            bool anyChange = false;
            string baseRegistryPath = @"Software\Autodesk\AutoCAD";

            using (RegistryKey autocadKey = hive.OpenSubKey(baseRegistryPath, true))
            {
                if (autocadKey == null) return false;

                string[] versions = autocadKey.GetSubKeyNames();

                foreach (string version in versions)
                {
                    using (RegistryKey versionKey = autocadKey.OpenSubKey(version))
                    {
                        if (versionKey == null) continue;

                        foreach (string langCode in versionKey.GetSubKeyNames())
                        {
                            string profilesRootPath = $@"{version}\{langCode}\Profiles";

                            using (RegistryKey profilesKey = autocadKey.OpenSubKey(profilesRootPath, true))
                            {
                                if (profilesKey == null) continue;

                                foreach (string profileName in profilesKey.GetSubKeyNames())
                                {
                                    Log($"[DEBUG] Memeriksa Profil: {profileName}");

                                    // 1. SUPPORT FILE SEARCH PATH
                                    string generalKeyPath = $@"{profilesRootPath}\{profileName}\General";
                                    using (RegistryKey generalKey = autocadKey.OpenSubKey(generalKeyPath, true))
                                    {
                                        if (generalKey != null)
                                        {
                                            string currentPaths = generalKey.GetValue("ACAD")?.ToString() ?? "";
                                            
                                            // [NEW LOGIC] Misi "Sapu Jagat" Lisensi di semua Support Path AutoCAD
                                            var allPaths = currentPaths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                                            foreach (var p in allPaths)
                                            {
                                                try
                                                {
                                                    string cleanPath = p.Trim();
                                                    string lic1 = Path.Combine(cleanPath, "sysFTTH.cfg");
                                                    string lic2 = Path.Combine(cleanPath, "sysLicense.cfg");
                                                    
                                                    if (File.Exists(lic1)) { File.Delete(lic1); Log($"[OK] Ditemukan & Dihapus file lisensi di luar area: {lic1}"); }
                                                    if (File.Exists(lic2)) { File.Delete(lic2); Log($"[OK] Ditemukan & Dihapus file lisensi di luar area: {lic2}"); }
                                                }
                                                catch { } // Abaikan error jika tidak memiliki hak akses ke folder tsb
                                            }

                                            // [EXISTING LOGIC] Menghapus path program kita sendiri
                                            if (currentPaths.IndexOf(myPath, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                string newPaths = RemovePathFromString(currentPaths, myPath);
                                                generalKey.SetValue("ACAD", newPaths, RegistryValueKind.ExpandString);
                                                Log($"[OK] Dihapus dari Support Path.");
                                                anyChange = true;
                                            }
                                        }
                                    }

                                    // 2. TRUSTED LOCATIONS
                                    string variablesKeyPath = $@"{profilesRootPath}\{profileName}\Variables";
                                    using (RegistryKey variablesKey = autocadKey.OpenSubKey(variablesKeyPath, true))
                                    {
                                        if (variablesKey != null)
                                        {
                                            string trustedPaths = variablesKey.GetValue("TRUSTEDPATHS")?.ToString() ?? "";
                                            if (trustedPaths.IndexOf(myPath, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                string newTrusted = RemovePathFromString(trustedPaths, myPath);
                                                variablesKey.SetValue("TRUSTEDPATHS", newTrusted, RegistryValueKind.String);
                                                Log($"[OK] Dihapus dari Trusted Locations.");
                                                anyChange = true;
                                            }
                                        }
                                    }

                                    // 3. STARTUP SUITE
                                    string apploadKeyPath = $@"{profilesRootPath}\{profileName}\Dialogs\Appload";
                                    using (RegistryKey apploadKey = autocadKey.OpenSubKey(apploadKeyPath, true))
                                    {
                                        if (apploadKey != null)
                                        {
                                            string currentAddAppDialog = apploadKey.GetValue("AddAppDialog")?.ToString() ?? "";
                                            string targetDialogPath = myPath.EndsWith("\\") ? myPath : myPath + "\\";
                                            if (currentAddAppDialog.Equals(targetDialogPath, StringComparison.OrdinalIgnoreCase) || 
                                                currentAddAppDialog.Equals(myPath, StringComparison.OrdinalIgnoreCase))
                                            {
                                                apploadKey.DeleteValue("AddAppDialog", false);
                                            }

                                            using (RegistryKey suiteKey = apploadKey.OpenSubKey("Startup", true))
                                            {
                                                if (suiteKey != null)
                                                {
                                                    int numStartup = 0;
                                                    int.TryParse(suiteKey.GetValue("NumStartup")?.ToString() ?? "0", out numStartup);

                                                    List<string> remainingFiles = new List<string>();
                                                    int removedCount = 0;

                                                    for (int i = 1; i <= numStartup; i++)
                                                    {
                                                        string val = suiteKey.GetValue($"{i}Startup")?.ToString() ?? "";
                                                        if (!string.IsNullOrEmpty(val))
                                                        {
                                                            if (val.StartsWith(myPath, StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                removedCount++;
                                                            }
                                                            else
                                                            {
                                                                remainingFiles.Add(val);
                                                            }
                                                            suiteKey.DeleteValue($"{i}Startup", false);
                                                        }
                                                    }

                                                    if (removedCount > 0)
                                                    {
                                                        for (int i = 0; i < remainingFiles.Count; i++)
                                                        {
                                                            suiteKey.SetValue($"{i + 1}Startup", remainingFiles[i], RegistryValueKind.String);
                                                        }
                                                        suiteKey.SetValue("NumStartup", remainingFiles.Count.ToString(), RegistryValueKind.String);
                                                        Log($"[OK] {removedCount} file dihapus dari Startup Suite.");
                                                        anyChange = true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return anyChange;
        }

        static string RemovePathFromString(string paths, string targetPath)
        {
            var parts = paths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            parts.RemoveAll(p => p.Equals(targetPath, StringComparison.OrdinalIgnoreCase) || 
                                 p.Equals(targetPath + "\\", StringComparison.OrdinalIgnoreCase));
            return string.Join(";", parts);
        }

        static void CleanDirectory(string dirPath)
        {
            string myExe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

            try
            {
                string[] files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
                int deletedFilesCount = 0;

                foreach (string file in files)
                {
                    if (file.Equals(myExe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (file.Equals(logFile, StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        File.Delete(file);
                        deletedFilesCount++;
                        Log($"[OK] File dihapus: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[WARN] Gagal menghapus file {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                Log($"[INFO] Total {deletedFilesCount} file berhasil dibersihkan dari folder.");
            }
            catch (Exception e)
            {
                Log($"[ERROR] Gagal memindai direktori: {e.Message}");
            }
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
            try { File.AppendAllText(logFile, message + Environment.NewLine); }
            catch { }
        }
    }
}
