# Smart Routing Kabel FDT ke FAT (Algoritma Dijkstra) (Eps. 8) ⚡

> 💡 **Nilai Bisnis & Efisiensi**: Inti kecerdasan buatan FTTH Design Planner. Menghubungkan titik FDT ke seluruh FAT melewati jaringan tiang yang ada secara otomatis dengan jalur terpendek (*shortest path Dijkstra*). Menghilangkan proses tracing kabel manual yang menghabiskan waktu berhari-hari.

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_DRAW_SMART_PLINE` atau `FTTH_AUTOROUTE`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Cable Routing Tools** ➔ Tombol **Auto Route Cables**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Pastikan FDT, seluruh FAT, dan jaringan tiang sudah ditempatkan di gambar kerja.
2. Klik tombol **Auto Route Cables** atau ketik `FTTH_DRAW_SMART_PLINE`.
3. Klik titik blok FDT sebagai sumber distribusi utama (*origin*).
4. Pilih seluruh FAT dan tiang yang menjadi target rute.
5. Algoritma graf akan otomatis:
   - Membangun topologi jaringan jalan dan tiang (*Network Graph*).
   - Menghitung rute terpendek dari FDT menuju setiap FAT menggunakan algoritma Dijkstra.
   - Menggabungkan segmen-segmen kabel yang searah untuk meminimalkan penarikan kabel ganda.
   - Men-snap garis kabel tepat di titik tengah tiang-tiang yang dilalui.
   - Menentukan kapasitas core kabel (24C, 36C, 48C) secara dinamis sesuai jumlah FAT hilir.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Animasi penarikan kabel otomatis bercabang dari FDT ke puluhan FAT dalam 3 detik -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 8](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Demo Auto Cable Routing, [01:50] Logika Penentuan Kapasitas Core, [03:20] Penanganan Tektok di Jalan Buntu, [05:10] Smoothing Kabel, [06:45] Layer Kabel).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- 💡 **Snapping Tiang Presisi**: Tiang yang berada di luar jangkauan toleransi (misal terpisah lebih dari 60m tanpa ada tiang perantara) akan ditandai sebagai peringatan rute terputus.
