# Download Peta Satelit Offline Georeferenced (Eps. 2) 🛰️

> 💡 **Nilai Bisnis & Efisiensi**: Menampilkan citra satelit resolusi tinggi langsung di workspace AutoCAD tanpa perlu membuka browser terpisah. Tidak perlu lagi scaling, rotasi, atau align gambar peta secara manual karena seluruh tile peta terpasang dengan koordinat bumi asli (*georeferenced*).

---

### 1. Cara Akses di AutoCAD
- **Command CAD**:
  - `FTTH_DOWNLOAD_BY_POLYGON` (Download Google/Esri/Bing Tiles per polygon)
  - `FTTH_GEOMAP_AERIAL` (Aktifkan satelit Bing bawaan AutoCAD)
  - `FTTH_GEOMAP_ROAD` (Aktifkan peta jalan Bing bawaan AutoCAD)
  - `FTTH_GEOMAP_OFF` (Matikan tampilan peta online AutoCAD)
  - `FTTH_GEOMAP_CAPTURE` (Sematkan/potong area peta Bing ke file DWG)
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Tab **MAP & BASEMAP** ➔ Kartu **Boundary & Map Downloader**.

---

### 2. Metode 1: Download Tile Satelit HD per Polygon (`FTTH_DOWNLOAD_BY_POLYGON`)
1. Pastikan Anda telah memiliki polyline batas wilayah di layar kerja.
2. Jalankan perintah `FTTH_DOWNLOAD_BY_POLYGON` atau klik tombol di panel.
3. Klik pada garis polyline boundary target Anda di AutoCAD.
4. Pilih **Provider**:
   - Google Satellite
   - Bing Maps
   - ESRI World Imagery
5. Pilih **Resolution & Zoom Level**:
   - *Draft (16 tiles)*
   - *Standard (64 tiles)*
   - *High (256 tiles - Rekomendasi)*
   - *Ultra High (1024 tiles)*
   - *Manual Zoom Level* (Zoom 17 ~1.2m/px hingga Zoom 20 ~0.15m/px)
6. Pilih **Color Mode**: Full Color atau Grayscale.
7. Tekan tombol **DOWNLOAD BY POLYGON**. Plugin akan mengunduh tile citra, menggabungkannya (*stitching*), dan menempatkan gambar raster di posisi koordinat UTM yang presisi pada layer `FTTH-SATELLITE-MAP`.

---

### 3. Metode 2: AutoCAD Native GEOMAP (Bing Built-in)
Bagi pengguna AutoCAD dengan akun Autodesk aktif, plugin menyediakan tombol integrasi instan di dalam panel:
- **Bing Aerial** (`FTTH_GEOMAP_AERIAL`): Menampilkan citra satelit Bing secara streaming langsung di viewport CAD.
- **Bing Road** (`FTTH_GEOMAP_ROAD`): Menampilkan peta vektor jalan lengkap dengan nama jalan resmi.
- **Capture Area** (`FTTH_GEOMAP_CAPTURE`): Mengubah area peta Bing yang sedang tampil di layar menjadi gambar raster statis yang tersimpan permanen di file DWG tanpa perlu koneksi internet lagi.
- **Turn Off Map** (`FTTH_GEOMAP_OFF`): Menonaktifkan tampilan peta streaming.

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 2](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Keunggulan Fitur, [01:00] Memilih Polygon Boundary, [02:20] Pemilihan Zoom Level, [03:45] Auto-georeferencing Peta ke Model Space).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- ⚠️ **Koneksi Internet**: Diperlukan koneksi internet yang stabil saat proses download berlangsung.
- 💡 **Manajemen Performa CAD**: Gunakan tombol **Show/Hide Map** di panel untuk menyembunyikan sementara layer peta satelit jika gambar mulai terasa berat.
- 💡 **Cache Lokal**: Tile peta otomatis disimpan di disk lokal sehingga saat gambar CAD dibuka kembali, peta tidak perlu diunduh ulang.
