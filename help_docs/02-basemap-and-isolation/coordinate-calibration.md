# Kalibrasi Koordinat Dunia Nyata / CRS UTM (Eps. 7) 🎯

> 💡 **Nilai Bisnis & Efisiensi**: Menghilangkan risiko gambar bergeser (shift) atau skala meleset saat diekspor ke Google Earth, ArcGIS, QGIS, atau diserahkan ke tim lapangan. Menjamin akurasi posisi hingga tingkat sentimeter.

---

### 1. Cara Akses di AutoCAD
- **Command CAD**:
  - `FTTH_SCALEINPLACE` (Menyesuaikan skala gambar di posisi asli)
  - `FTTH_SCALEBASE` (Kalibrasi titik dasar koordinat)
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Coordinate & Scale Tools**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Tentukan satu atau dua titik referensi acuan (Benchmark / Titik GPS Lapangan) yang koordinatnya diketahui pasti.
2. Jalankan perintah `FTTH_SCALEINPLACE` atau `FTTH_SCALEBASE`.
3. Pilih objek gambar yang ingin diselaraskan.
4. Tentukan titik acuan (*base point*) dan masukkan nilai koordinat geografis / UTM target.
5. Program akan melakukan transformasi koordinat dan skala secara in-place tanpa merusak relasi spasial antar objek (tiang, rumah, kabel).

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Gambar verifikasi koordinat benchmark titik GPS di AutoCAD vs Google Earth -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 7](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Mengapa Kalibrasi Wajib, [01:15] Menentukan Benchmark Point, [02:40] Menjalankan Command FTTH_SCALEINPLACE, [04:10] Verifikasi Hasil GPS Tracker).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- ⚠️ **Satuan Gambar (Units)**: Pastikan variabel `INSUNITS` di AutoCAD Anda bernilai `6` (Meters) agar perhitungan jarak kabel konsisten dengan satuan meter internasional.
