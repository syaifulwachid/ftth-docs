# Auto-Tagging & Urutan Kode Tiang Massal (Eps. 6) 🏷️

> 💡 **Nilai Bisnis & Efisiensi**: Menamai dan menomori ratusan hingga ribuan tiang secara terurut dari pangkal ke ujung rute kabel. Mencegah duplikasi nomor tiang dan memangkas waktu kerja berjam-jam menjadi hitungan detik.

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_GEN_POLE_LABEL`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Asset Placement** ➔ Tombol **Auto Tag Poles**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Buka kartu **Asset Placement** di panel.
2. Tentukan format kode penomoran:
   - **Prefix Tiang Baru**: misal `NP-` (menjadi `NP-01`, `NP-02`, dst).
   - **Prefix Tiang Existing**: misal `EP-` atau `EXT-`.
   - **Start Number**: Angka awal urutan.
3. Jalankan perintah `FTTH_GEN_POLE_LABEL`.
4. Pilih rute garis jalan atau buat window seleksi pada tiang-tiang yang ingin diberi label.
5. Plugin akan secara cerdas menelusuri tiang dari titik awal rute ke titik akhir dan menyematkan label nama tiang lengkap dengan *wipeout background* agar teks tidak tertabrak garis gambar lain.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Animasi proses tagging nomor tiang terurut rapi di sepanjang jalan -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 6](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Mengenal Tipe Tiang EXT & NP, [01:10] Konfigurasi Penomoran Berdasarkan Rute, [02:30] Menjalankan Command Massal, [04:00] Visualisasi Text Style & Layer).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- 💡 **Re-Index Otomatis**: Jika di kemudian hari disisipkan tiang baru di tengah rute, jalankan kembali fitur ini untuk mengindeks ulang nomor seluruh tiang berikutnya secara otomatis.
