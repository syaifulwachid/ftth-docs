# Ekspor Bill of Materials (BOM / BOQ) ke Excel 📊

> 💡 **Nilai Bisnis & Efisiensi**: Menghitung kuantitas seluruh material proyek (panjang kabel per kapasitas, jumlah tiang per ukuran, jumlah FAT, closure sambung, hingga aksesoris suspension dan dead-end clamp) secara otomatis ke format spreadsheet Excel (.xlsx / .csv). Menghemat waktu estimasi anggaran RAB hingga 95%!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_EXPORT_BOM`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Export Deliverables** ➔ Tombol **Export BOM to Excel**.

---

### 2. Kategori Material yang Dihitung Otomatis

| Kategori Material | Item yang Dihitung | Satuan |
|---|---|:---:|
| **Kabel Fiber Optik** | Kabel ADSS/Aerial 24C, 48C, 96C (termasuk slack cable loop) | Meter |
| **Tiang (Poles)** | New Pole 7m (2.5", 3", 4"), New Pole 9m, Pondasi Tiang | Batang / Titik |
| **Perangkat Terminasi** | FDT 48/96 Port, FAT 8/16 Port, Optical Splitter 1:8 / 1:16 | Unit |
| **Closure & Sambungan** | Dome Closure 24/48 Core, Protection Sleeve | Unit |
| **Aksesoris Tiang** | Suspension Clamp, Dead-End Clamp, Stainless Steel Band, Bracket | Set |
| **Kabel Drop** | Estimasi panjang total drop cable subscriber | Meter |

---

### 3. Langkah Penggunaan (Step-by-Step)
1. Buka kartu **Export Deliverables** di panel.
2. Tentukan scope perhitungan:
   - **Global (Seluruh Proyek)**: Menghitung total semua aset di drawing.
   - **Per Area Isolasi**: Menghitung rincian BOM per cluster wilayah tertentu.
3. Masukkan faktor toleransi *Slack Cable* (standar 10-15 meter per FAT atau per 200 meter bentang).
4. Klik tombol **Export BOM to Excel**.
5. Pilih lokasi penyimpanan file Excel Anda.
6. File spreadsheet rapi siap cetak langsung terbuka dengan formula total dan rekapitulasi biaya.
