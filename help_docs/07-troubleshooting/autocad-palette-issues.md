# Palette AutoCAD Tidak Muncul / Command Error 🖥️

Jika setelah instalasi panel FTTH Design Planner tidak muncul atau muncul pesan error di Command Line AutoCAD, ikuti langkah perbaikan di bawah ini.

---

## ❓ 1. Muncul Pesan "Unknown command FTTHBASEMAP"
**Penyebab**: Plugin belum termuat otomatis ke dalam memori AutoCAD.

**Solusi**:
1. Ketik perintah `NETLOAD` di Command Line AutoCAD lalu tekan ++enter++.
2. Arahkan ke folder bundle plugin:
   - Untuk **AutoCAD 2021–2024**:
     `%APPDATA%\Autodesk\ApplicationPlugins\FTTHBasemap.bundle\Contents\net48\FTTHBasemap.dll`
   - Untuk **AutoCAD 2025–2027**:
     `%APPDATA%\Autodesk\ApplicationPlugins\FTTHBasemap.bundle\Contents\net8\FTTHBasemap.dll`
3. Pilih file `FTTHBasemap.dll` lalu klik **Open**.
4. Ketik kembali perintah `FTTHBASEMAP`.

---

## ❓ 2. Peringatan Keamanan "Security - Unsigned Executable"
**Penyebab**: Sistem keamanan AutoCAD mendeteksi file DLL baru yang belum pernah dijalankan sebelumnya.

**Solusi**:
- Pada jendela pop-up peringatan, klik tombol **Always Load**.
- Jangan memilih *Do Not Load* atau *Load Once*.

---

## ❓ 3. Palette Tersembunyi di Luar Layar Monitor
**Penyebab**: Terjadi jika Anda sebelumnya menggunakan monitor ganda (*dual monitor*) lalu beralih ke layar tunggal laptop.

**Solusi**:
- Ketik perintah `FTTHBASEMAP_SHOW` untuk mereset posisi jendela palette kembali ke tengah layar utama.
