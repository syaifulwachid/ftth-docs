# Pole Replacement Tool (Ganti Tipe Tiang Massal) (Eps. 10) 🔄

> 💡 **Nilai Bisnis & Efisiensi**: Menangani revisi tipe tiang dari klien (misalnya dari Tiang Existing ke Tiang Baru 7m, atau dari Tiang 7m ke Tiang 9m) secara instan tanpa perlu menghapus dan memasang ulang blok satu per satu. Posisi koordinat dan metadata tiang tetap utuh 100%!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_REPLACE_POLES`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Asset Placement** ➔ Tombol **Pole Replacement Tool**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Buka jendela **Pole Replacement Tool** dari panel atau ketik `FTTH_REPLACE_POLES`.
2. Tentukan kriteria penggantian:
   - **Tipe Sumber (Source)**: Pilih blok tiang yang ingin diganti (misal `EP74` - Tiang Existing).
   - **Tipe Tujuan (Target)**: Pilih blok tiang pengganti (misal `POLE73IN` - New Pole 7m 3 inch).
3. Pilih metode seleksi:
   - **Global**: Mengganti di seluruh gambar kerja.
   - **Selection Area / Window**: Mengganti hanya pada tiang di dalam area isolasi atau kotak seleksi tertentu.
4. Klik tombol **Execute Replace**.
5. Program akan memperbarui seluruh blok tiang target, memperbarui layer yang sesuai, dan memindahkan seluruh teks penomoran serta sambungan garis kabel secara otomatis.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Animasi perubahan massal tipe tiang tanpa merusak sambungan kabel -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 10](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Skenario Revisi Desain, [01:10] Menjalankan Command, [02:30] Seleksi Individu vs Massal, [03:45] Sinkronisasi Metadata Pasca Ganti).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- 💡 **Koneksi Kabel Aman**: Titik insertion point seluruh blok tiang standar telah diselaraskan pada titik pusat `(0,0)`, sehingga garis polyline kabel yang men-snap ke tiang tidak akan meleset saat tipe tiang diganti.
