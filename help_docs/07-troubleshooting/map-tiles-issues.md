# Peta Satelit Blank atau Gagal Download 🛰️

Panduan menyelesaikan masalah saat citra satelit tidak muncul di Model Space AutoCAD.

---

## ❓ 1. Gambar Peta Satelit Berwarna Putih / Blank
**Penyebab**: Koneksi internet terputus saat proses download berlangsung atau server penyedia tile sedang mengalami limit permintaan (rate limit).

**Solusi**:
1. Periksa koneksi internet Anda.
2. Di panel download, coba ganti **Provider Peta** (misal dari *Google Satellite* ke *Esri World Imagery* atau *Bing Maps*).
3. Turunkan **Zoom Level** satu tingkat (misal dari Level 19 ke Level 18) untuk mempercepat proses unduh dan meringankan beban koneksi.

---

## ❓ 2. Gambar Satelit Menutupi Garis Gambar Desain
**Solusi**:
1. Pilih gambar raster peta di AutoCAD.
2. Klik kanan ➔ pilih **Draw Order** ➔ **Send to Back**.
3. Gambar peta akan berpindah ke lapisan paling belakang sehingga garis jalan, kabel, dan blok tiang tetap terlihat jelas di bagian atas.
