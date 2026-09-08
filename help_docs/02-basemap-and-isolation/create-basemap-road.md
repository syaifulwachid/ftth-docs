# Otomatisasi Basemap Jalan, Lebar & As Jalan (Eps. 3) 🛣️

> 💡 **Nilai Bisnis & Efisiensi**: Menggambar aspal jalan, bahu jalan, dan persimpangan pertigaan/perempatan secara manual membutuhkan waktu berjam-jam dan melelahkan. Dengan fitur ini, cukup gambar garis as jalan (centerline) sederhana, seluruh bidang jalan dengan lebar bervariasi ter-generate otomatis dan persimpangan terbersihkan tanpa trim manual!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**:
  - `FTTH_SETWIDTH` (Mengatur parameter lebar jalan)
  - `FTTH_GENERATE` (Menghasilkan basemap polygon jalan otomatis)
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Basemap & Road Tools** ➔ Tombol **Generate Road Basemap**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Gambarlah garis as jalan (centerline) menggunakan polyline sederhana mengikuti jalur jalan pada peta satelit.
2. Gunakan perintah `FTTH_SETWIDTH` untuk menentukan lebar jalan (misal jalan protokol 6–8 meter, jalan gang/lingkungan 3–4 meter).
3. Jalankan perintah `FTTH_GENERATE`.
4. Pilih semua garis centerline jalan yang telah Anda gambar.
5. Plugin akan secara otomatis:
   - Membuat buffer sisi kiri dan kanan jalan secara proporsional.
   - Membersihkan seluruh titik temu persimpangan (T-Junction / 4-Way Crossroad) secara bersih.
   - Memasukkan objek ke dalam layer terstruktur (`ROAD-ASPAL`, `ROAD-CENTERLINE`, `ROAD-SHOULDER`).

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Animasi dari garis as tunggal menjadi layout jalan dan persimpangan utuh -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 3](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Mengatur Lebar Jalan, [01:30] Menjalankan Generator FTTH_GENERATE, [03:10] Pembersihan Otomatis Persimpangan, [05:00] Pengelompokan Layer Jalan).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- 💡 **Snapping Centerline**: Pastikan ujung-ujung garis centerline saling menempel (snap endpoint) di persimpangan agar algoritma pembersihan persimpangan bekerja 100% sempurna.
- 💡 **OSM Integration**: Plugin juga menyediakan opsi unduh centerline jalan otomatis langsung dari OpenStreetMap (OSM) via menu `OsmRoadService`.
