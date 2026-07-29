using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;

namespace AutoCADPathConfigurator
{
    class Program
    {
        static string logFile;

        static void Main(string[] args)
        {
            // Folder tempat EXE ini berada = folder program FAS yang baru di-extract installer
            string myPath = AppDomain.CurrentDomain.BaseDirectory
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Log file ditulis di folder yang sama dengan EXE
            logFile = Path.Combine(myPath, "Path_Register_Log.txt");

            // Hapus log lama agar tidak menumpuk
            try { File.Delete(logFile); } catch { }

            Log("AutoCAD Path & Startup Suite Registrar");
            Log("========================================");
            Log($"Folder  : {myPath}");
            Log($"Waktu   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Log("");

            try
            {
                bool anyChange = false;

                // === METODE 1: HKEY_CURRENT_USER (user yang sedang login) ===
                Log("[INFO] Mendaftarkan ke HKEY_CURRENT_USER...");
                bool changeHKCU = RegisterToHive(Registry.CurrentUser, myPath);
                anyChange = anyChange || changeHKCU;

                // === METODE 2: HKEY_USERS (semua SID user yang ada) ===
                Log("");
                Log("[INFO] Mencari semua SID user di HKEY_USERS...");
                using (RegistryKey hkUsers = Registry.Users)
                {
                    if (hkUsers != null)
                    {
                        string[] sids = hkUsers.GetSubKeyNames();
                        Log($"[DEBUG] Ditemukan {sids.Length} SID di HKEY_USERS");

                        foreach (string sid in sids)
                        {
                            // Skip SID sistem dan SID classes
                            if (sid.EndsWith("_Classes") || sid == ".DEFAULT" ||
                                sid == "S-1-5-18" || sid == "S-1-5-19" || sid == "S-1-5-20")
                                continue;

                            // Cek apakah SID ini punya AutoCAD
                            string autocadPath = $@"{sid}\Software\Autodesk\AutoCAD";
                            using (RegistryKey testKey = hkUsers.OpenSubKey(autocadPath))
                            {
                                if (testKey == null) continue;

                                Log($"[INFO] Ditemukan AutoCAD di SID: {sid}");
                                using (RegistryKey sidKey = hkUsers.OpenSubKey(sid, true))
                                {
                                    if (sidKey != null)
                                    {
                                        bool changeSID = RegisterToHive(sidKey, myPath);
                                        anyChange = anyChange || changeSID;
                                    }
                                }
                            }
                        }
                    }
                }

                Log("");
                Log("=== HASIL AKHIR ===");
                if (anyChange)
                    Log("[OK] Pendaftaran berhasil. Restart AutoCAD jika sedang terbuka.");
                else
                    Log("[INFO] Tidak ada perubahan (semua path sudah terdaftar).");

                Environment.Exit(0);
            }
            catch (UnauthorizedAccessException)
            {
                Log("[ERROR] Akses registry ditolak. Coba jalankan sebagai Administrator.");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Log($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Mendaftarkan path ke semua versi AutoCAD dalam satu registry hive
        /// </summary>
        static bool RegisterToHive(RegistryKey hive, string myPath)
        {
            bool anyChange = false;
            string baseRegistryPath = @"Software\Autodesk\AutoCAD";

            using (RegistryKey autocadKey = hive.OpenSubKey(baseRegistryPath, true))
            {
                if (autocadKey == null)
                {
                    Log($"[DEBUG] AutoCAD tidak ditemukan di hive ini.");
                    return false;
                }

                string[] versions = autocadKey.GetSubKeyNames();
                Log($"[DEBUG] Ditemukan {versions.Length} versi AutoCAD: {string.Join(", ", versions)}");

                // Cari semua file .fas di folder exe ini berada
                string[] fasFiles = Directory.GetFiles(myPath, "*.fas");
                Log($"[INFO] Ditemukan {fasFiles.Length} file .fas: {string.Join(", ", Array.ConvertAll(fasFiles, Path.GetFileName))}");

                bool anySupportPathChange = false;
                bool anyTrustedPathChange = false;
                bool anyStartupSuiteChange = false;

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
                                if (profilesKey == null)
                                {
                                    Log($"[DEBUG] Profiles key tidak ditemukan: {profilesRootPath}");
                                    continue;
                                }

                                Log($"[DEBUG] Profiles key: {profilesRootPath}");

                                // Deteksi nama profil aktif
                                string activeProfileName =
                                    profilesKey.GetValue("CURRENTPROFILE")?.ToString();

                                if (string.IsNullOrEmpty(activeProfileName))
                                {
                                    activeProfileName = profilesKey.GetValue("")?.ToString();
                                    Log($"        Value '' = '{activeProfileName}'");
                                }

                                if (string.IsNullOrEmpty(activeProfileName))
                                {
                                    string[] profileNames = profilesKey.GetSubKeyNames();
                                    if (profileNames.Length > 0)
                                    {
                                        activeProfileName = profileNames[0];
                                        Log($"[WARN] Fallback ke profil pertama: {activeProfileName}");
                                    }
                                    else
                                    {
                                        Log($"[WARN] Tidak ada profil ditemukan di {profilesRootPath}");
                                        continue;
                                    }
                                }

                                Log($"[DEBUG] Profil aktif: {activeProfileName}");

                                // === 1. DAFTARKAN SUPPORT FILE SEARCH PATH ===
                                string generalKeyPath = $@"{profilesRootPath}\{activeProfileName}\General";
                                using (RegistryKey generalKey = autocadKey.OpenSubKey(generalKeyPath, true))
                                {
                                    if (generalKey == null)
                                    {
                                        Log($"[WARN] General key tidak ditemukan: {generalKeyPath}");
                                    }
                                    else
                                    {
                                        string currentPaths = generalKey.GetValue("ACAD")?.ToString() ?? "";
                                        
                                        if (!currentPaths.ToLower().Contains(myPath.ToLower()))
                                        {
                                            string newPaths = string.IsNullOrEmpty(currentPaths)
                                                ? myPath
                                                : myPath + ";" + currentPaths;

                                            generalKey.SetValue("ACAD", newPaths, RegistryValueKind.ExpandString);
                                            Log($"[OK] Support Path didaftarkan -> {myPath}");
                                            anySupportPathChange = true;
                                        }
                                        else
                                        {
                                            Log($"[INFO] Support Path sudah terdaftar.");
                                        }
                                    }
                                }

                                // === 2. DAFTARKAN KE TRUSTED LOCATIONS ===
                                string variablesKeyPath = $@"{profilesRootPath}\{activeProfileName}\Variables";
                                using (RegistryKey variablesKey = autocadKey.OpenSubKey(variablesKeyPath, true)
                                                                ?? autocadKey.CreateSubKey(variablesKeyPath))
                                {
                                    if (variablesKey != null)
                                    {
                                        string trustedPaths = variablesKey.GetValue("TRUSTEDPATHS")?.ToString() ?? "";

                                        if (!trustedPaths.ToLower().Contains(myPath.ToLower()))
                                        {
                                            string newTrusted = string.IsNullOrEmpty(trustedPaths)
                                                ? myPath
                                                : myPath + ";" + trustedPaths;

                                            variablesKey.SetValue("TRUSTEDPATHS", newTrusted, RegistryValueKind.String);
                                            Log($"[OK] Trusted Location didaftarkan (TRUSTEDPATHS) -> {myPath}");
                                            anyTrustedPathChange = true;
                                        }
                                        else
                                        {
                                            Log($"[INFO] Trusted Location sudah terdaftar (TRUSTEDPATHS).");
                                        }
                                    }
                                    else
                                    {
                                        Log($"[WARN] Tidak dapat membuka/membuat Variables key: {variablesKeyPath}");
                                    }
                                }

                                // === 3. DAFTARKAN FILE .FAS KE STARTUP SUITE ===
                                string apploadKeyPath = $@"{profilesRootPath}\{activeProfileName}\Dialogs\Appload";
                                using (RegistryKey apploadKey = autocadKey.OpenSubKey(apploadKeyPath, true)
                                                                ?? autocadKey.CreateSubKey(apploadKeyPath))
                                {
                                    if (apploadKey != null)
                                    {
                                        string currentAddAppDialog = apploadKey.GetValue("AddAppDialog")?.ToString() ?? "";
                                        string targetDialogPath = myPath.EndsWith("\\") ? myPath : myPath + "\\";
                                        if (currentAddAppDialog == "" || currentAddAppDialog != targetDialogPath)
                                        {
                                            apploadKey.SetValue("AddAppDialog", targetDialogPath, RegistryValueKind.ExpandString);
                                        }
                                    }
                                }

                                if (fasFiles.Length > 0)
                                {
                                    string startupKeyPath = $@"{profilesRootPath}\{activeProfileName}\Dialogs\Appload\Startup";
                                    using (RegistryKey suiteKey = autocadKey.OpenSubKey(startupKeyPath, true)
                                                                ?? autocadKey.CreateSubKey(startupKeyPath))
                                    {
                                        if (suiteKey != null)
                                        {
                                            int numStartup = 0;
                                            int.TryParse(suiteKey.GetValue("NumStartup")?.ToString() ?? "0", out numStartup);

                                            List<string> existingFiles = new List<string>();
                                            for (int i = 1; i <= numStartup; i++)
                                            {
                                                string val = suiteKey.GetValue($"{i}Startup")?.ToString() ?? "";
                                                if (!string.IsNullOrEmpty(val))
                                                    existingFiles.Add(val.ToLower());
                                            }

                                            Log($"[DEBUG] Startup key: {numStartup} file sudah terdaftar.");

                                            int addedCount = 0;
                                            foreach (string fasFile in fasFiles)
                                            {
                                                if (!existingFiles.Contains(fasFile.ToLower()))
                                                {
                                                    numStartup++;
                                                    string valueName = $"{numStartup}Startup";
                                                    suiteKey.SetValue(valueName, fasFile, RegistryValueKind.String);
                                                    Log($"[OK] Startup Suite ditambahkan: '{valueName}' = '{Path.GetFileName(fasFile)}'");
                                                    addedCount++;
                                                    anyStartupSuiteChange = true;
                                                }
                                                else
                                                {
                                                    Log($"[INFO] Startup Suite: sudah ada -> {Path.GetFileName(fasFile)}");
                                                }
                                            }

                                            if (addedCount > 0)
                                                suiteKey.SetValue("NumStartup", numStartup.ToString(), RegistryValueKind.String);
                                        }
                                        else
                                        {
                                            Log($"[WARN] Tidak dapat membuka/membuat key: {startupKeyPath}");
                                        }
                                    }
                                }
                                
                                Log("");
                            }
                        }
                    }
                }

                anyChange = anySupportPathChange || anyTrustedPathChange || anyStartupSuiteChange;

                Log($"[INFO] Support Path {(anySupportPathChange ? "DIPERBARUI" : "tidak berubah (sudah terdaftar)")}.");
                Log($"[INFO] Trusted Location {(anyTrustedPathChange ? "DIPERBARUI" : "tidak berubah (sudah terdaftar)")}.");
                Log($"[INFO] Startup Suite {(anyStartupSuiteChange ? "DIPERBARUI" : "tidak berubah (semua file sudah terdaftar)")}.");
            }

            return anyChange;
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
            try { File.AppendAllText(logFile, message + Environment.NewLine); }
            catch { }
        }
    }
}
