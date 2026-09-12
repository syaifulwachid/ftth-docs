# Panduan Instalasi (Inno Setup & Autodesk Bundle) 📦

Menginstal **FTTH Design Planner** sangat mudah dan tidak memerlukan pengaturan manual file DLL yang rumit. Seluruh proses instalasi ditangani secara otomatis oleh paket installer Inno Setup.

---

## 📥 Langkah-Langkah Instalasi

### 1. Tutup AutoCAD Terlebih Dahulu
Pastikan seluruh jendela Autodesk AutoCAD yang sedang terbuka telah ditutup sebelum menjalankan installer. Hal ini penting agar file DLL plugin tidak terkunci oleh sistem Windows.

### 2. Jalankan File Installer
1. Unduh file installer resmi terbaru (saat ini: `FTTH_Design_Planner_v3.6.9_r22_Setup.exe`).
2. Klik kanan pada file installer lalu pilih **Run as administrator**.
3. Ikuti wizard instalasi di layar (klik **Next**).

### 3. Pembersihan Registri & Konflik Versi Lama (Otomatis)
Installer secara cerdas akan:
- Memeriksa apakah ada proses `acad.exe` yang masih berjalan.
- Membersihkan riwayat cache `APPLOAD` lama di Registry Windows agar tidak terjadi konflik file DLL ganda.
- Mendaftarkan paket plugin ke direktori standar Autodesk ApplicationPlugins:
  ```text
  %APPDATA%\Autodesk\ApplicationPlugins\FTTHBasemap.bundle
  ```

### 4. Buka AutoCAD
Setelah instalasi selesai:
1. Jalankan aplikasi AutoCAD Anda seperti biasa.
2. Saat AutoCAD pertama kali dibuka, Anda mungkin akan melihat dialog keamanan Autodesk:
   > *"Security - Unsigned Executable File"* ➔ Pilih **Always Load**.
3. Ketik perintah `FTTHBASEMAP` pada Command Line AutoCAD lalu tekan ++enter++.
4. Panel antarmuka FTTH Design Planner akan langsung muncul di samping area kerja AutoCAD Anda.

---

## 🔄 Pembaruan Versi Otomatis (Auto-Update Checker)
Plugin dilengkapi sistem pengecekan pembaruan otomatis yang terhubung ke server manifest GitHub Pages:
- Tombol **`🔄` Cek Update** di header panel dilengkapi **Badge Merah** yang otomatis menyala ketika versi baru dirilis.
- Jika ada update baru, banner notifikasi hijau akan muncul di atas palette.
- Cukup klik banner atau tombol **Yes** pada dialog pop-up untuk langsung mengunduh installer versi terbaru tanpa kehilangan data pengaturan Anda.

