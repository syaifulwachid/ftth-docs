# Download Peta Satelit Offline Georeferenced (Eps. 2) 🛰️

> 💡 **Nilai Bisnis & Efisiensi**: Menampilkan citra satelit resolusi tinggi langsung di workspace AutoCAD tanpa perlu membuka browser terpisah. Tidak perlu lagi scaling, rotasi, atau align gambar peta secara manual karena seluruh tile peta terpasang dengan koordinat bumi asli (*georeferenced*).

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_DOWNLOAD_BY_POLYGON`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Basemap & Boundary** ➔ Klik tombol **Download Satellite Map**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Pastikan Anda telah memiliki polyline batas wilayah di layar kerja (atau hasil import dari Langkah 1).
2. Jalankan perintah `FTTH_DOWNLOAD_BY_POLYGON` atau klik tombol di panel.
3. Klik pada garis polyline boundary target Anda di AutoCAD.
4. Pilih **Zoom Level** citra satelit (Level 17–19 disarankan untuk kejelasan visual atap rumah, tiang, dan lebar aspal jalan).
5. Pilih penyedia tile (Google Satellite / Esri World Imagery / Bing Maps).
6. Tekan tombol **Download & Place**. Plugin akan mengunduh tile citra, menggabungkannya, dan menempatkan gambar raster di posisi koordinat yang presisi pada AutoCAD Model Space.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Gambar proses seleksi polygon dan tampilan citra satelit HD di AutoCAD -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 2](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Keunggulan Fitur, [01:00] Memilih Polygon Boundary, [02:20] Pemilihan Zoom Level, [03:45] Auto-georeferencing Peta ke Model Space).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- ⚠️ **Koneksi Internet**: Diperlukan koneksi internet yang stabil saat proses download berlangsung.
- 💡 **Manajemen Performa CAD**: Gunakan perintah `DRAWORDER` ➔ `Back` agar gambar satelit berada di lapisan paling belakang dan tidak menutupi garis jalur kabel atau blok tiang.
- 💡 **Cache Lokal**: Tile peta otomatis disimpan di disk lokal sehingga saat gambar CAD dibuka kembali, peta tidak perlu diunduh ulang.
