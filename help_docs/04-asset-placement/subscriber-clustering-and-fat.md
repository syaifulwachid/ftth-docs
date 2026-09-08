# Clustering Pelanggan & Penempatan FAT Otomatis (Eps. 9) 🌐

> 💡 **Nilai Bisnis & Efisiensi**: Mengelompokkan ribuan calon pelanggan (hompass) secara adil dan matematis ke dalam cluster maksimal 16 rumah per FAT (Fiber Access Terminal / ODP), serta otomatis meletakkan titik FAT pada tiang terdekat yang paling efisien kabel drop-nya!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_GROUP` atau `FTTH_CLUSTER_FAT`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **FAT & Clustering Tools** ➔ Tombol **Auto Cluster & Place FAT**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Pastikan objek hompass (rumah) dan tiang-tiang sudah berada di Model Space AutoCAD.
2. Tentukan batasan kapasitas:
   - **Kapasitas Port FAT**: Standar 16 Port (maksimal 16 rumah per 1 FAT).
   - **Maksimal Jarak Drop Cable**: 150 meter dari tiang FAT ke rumah terjauh.
3. Jalankan perintah `FTTH_GROUP`.
4. Pilih seluruh hompass dan tiang yang ingin diproses.
5. Algoritma spatial clustering akan bekerja secara otomatis:
   - Mengelompokkan rumah-rumah yang berdekatan di sepanjang koridor jalan menjadi kelompok beranggotakan $\le 16$ rumah.
   - Menghitung titik pusat (*centroid*) dari masing-masing kelompok rumah.
   - Menemukan tiang terdekat dari centroid tersebut.
   - Memasang blok simbol `FATSW` (pada layer `FAT`) pada tiang terpilih dan memberi kode nama FAT secara berurutan (`FAT-01`, `FAT-02`, dst).
   - Menggambar garis polygon batas area jangkauan (*boundary coverage*) masing-masing FAT.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Animasi pengelompokan warna rumah dan penempatan blok FAT otomatis pada tiang -->

---

### 4. Video Tutorial YouTube
Tonton video panduan lengkap langkah demi langkah pada tautan di bawah ini:

[![Tonton Video Tutorial Eps 9](https://img.youtube.com/vi/videoseries?list=PLLr975-jxOLU/0.jpg)](https://www.youtube.com/playlist?list=PLLr975-jxOLU)

*(Materi Video: [00:00] Aturan Desain 16 Port, [01:15] Menjalankan Command FTTH_GROUP, [02:50] Logika Penemuan Tiang Terdekat, [04:20] Auto Boundary Coverage FAT).*

---

### 5. Tips & Hal yang Perlu Diperhatikan
- 💡 **Efisiensi Drop Cable**: Dengan penempatan berbasis centroid tiang, rata-rata panjang kabel drop pelanggan menjadi jauh lebih pendek, menghemat biaya material roll kabel drop secara signifikan bagi kontraktor/ISP!
