# Auto-Generate Nomor Rumah / Hompass (HPNUM) (Eps. 4) 🏠

> 💡 **Nilai Bisnis & Efisiensi**: Menomori unit hompass/rumah pelanggan satu per satu secara manual adalah proses paling rawan salah dan membuang waktu drafter. Dengan fitur ini, ribuan rumah diberi nomor urut teratur mengikuti arah jalan secara instan dan bebas duplikasi!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**:
  - `FTTH_TAG_HOMPASS` (Menomori hompass baru)
  - `FTTH_REGEN_HPNUM` (Mengurutkan dan memperbarui nomor hompass yang sudah ada)
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Hompass & Survey Data** ➔ Tombol **Generate Hompass Numbers**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Siapkan blok rumah / centroid hompass pada gambar Anda.
2. Di panel, tentukan parameter penomoran:
   - **Prefix**: Awalan teks (misal: `HP-`, `R-`, atau kosong).
   - **Start Index**: Nomor awal (biasanya mulai dari `1`).
   - **Suffix**: Akhiran teks jika diperlukan.
3. Jalankan perintah `FTTH_TAG_HOMPASS`.
4. Pilih objek rumah atau pilih garis rute centerline jalan yang menjadi acuan pengurutan.
5. Plugin akan secara cerdas menomori rumah berurutan dari pangkal jalan menuju ujung jalan.
6. Jika ada penambahan rumah baru di tengah-tengah proyek, cukup jalankan `FTTH_REGEN_HPNUM` untuk menyusun ulang nomor secara otomatis tanpa perlu mengedit teks manual satu per satu.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Gambar penomoran hompass terurut rapi mengikuti arah jalan -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 4](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Urgensi Database Hompass, [01:20] Konfigurasi Format Nomor, [02:40] Penomoran Massal Arah Jalan, [04:15] Regenerasi Nomor FTTH_REGEN_HPNUM).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- 💡 **Integrasi ke FAT**: Nomor hompass ini nantinya akan menjadi acuan saat melakukan clustering 16 rumah ke 1 FAT pada tahap penempatan FAT otomatis.
