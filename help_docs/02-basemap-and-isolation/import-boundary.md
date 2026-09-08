# Import Boundary KML / KMZ & Koordinat (Eps. 1) 🗺️

> 💡 **Nilai Bisnis & Efisiensi**: Memulai desain FTTH langsung dengan sistem koordinat dunia nyata yang 100% presisi. Mengeliminasi kesalahan penarikan batas wilayah proyek dan memangkas waktu persiapan awal dari hitungan jam menjadi hitungan detik!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_IMPORT_BOUNDARY` atau `FTTH_IMPORT_KML`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Basemap & Boundary** ➔ Klik tombol **Import Boundary**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Siapkan file polygon batas wilayah proyek Anda dalam format `.kml` atau `.kmz` (misal hasil survei lapangan atau Google Earth).
2. Di panel AutoCAD, pastikan **CRS (Sistem Koordinat)** proyek Anda sudah benar (contoh: `WGS 84 / UTM Zone 49S` atau `Zone 50S`).
3. Klik tombol **Import Boundary** atau ketik `FTTH_IMPORT_BOUNDARY`.
4. Pilih file `.kml` / `.kmz` Anda di jendela file dialog Windows.
5. Program akan membaca seluruh titik vertex geografis (latitude/longitude), mengonversinya ke sistem grid UTM WCS, dan menggambar polyline batas tertutup secara instan pada layer khusus `FTTH-BOUNDARY`.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Gambar jendela file dialog dan hasil polyline boundary ter-plot di AutoCAD -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 1](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Pengenalan Fitur, [01:15] Load File KML/KMZ, [02:45] Auto-konversi Geografis ke UTM, [04:00] Manajemen Layer FTTH-BOUNDARY).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- ⚠️ **Pastikan CRS Sesuai**: Selalu periksa Zona UTM area proyek Anda (misalnya Jawa Barat = UTM 48S/49S, Jawa Timur & Bali = UTM 49S/50S) agar skala gambar di AutoCAD akurat 1:1 terhadap meter bumi.
- 💡 **Layer Terkunci**: Polyline boundary berada di layer `FTTH-BOUNDARY` dengan warna pembeda untuk memudahkan seleksi area isolasi pada tahap berikutnya.
