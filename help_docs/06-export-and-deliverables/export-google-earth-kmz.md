# Ekspor ke Google Earth (KMZ APD & ABD Berstruktur) 🌍

> 💡 **Nilai Bisnis & Efisiensi**: Menghasilkan file deliverables Google Earth (.KMZ) berstandar industri dengan struktur folder rapi (Tiang, FAT, FDT, Kabel 24C/48C, Boundary) dan popup HTML interaktif. Klien, vendor, dan manajemen dapat langsung meninjau hasil desain di HP maupun laptop tanpa perlu software CAD!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**:
  - `FTTH_EXPORT_KMZ_APD` (Format As Planned Design)
  - `FTTH_EXPORT_KMZ_ABD` (Format As Built Drawing)
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Export Deliverables** ➔ Tombol **Export KMZ**.

---

### 2. Struktur Hirarki Folder KMZ Otomatis

File `.kmz` yang dihasilkan memiliki struktur pohon folder yang sangat teratur di Google Earth:

```text
📁 [NAMA_PROYEK]_FTTH_DESIGN.kmz
├── 📁 01_BOUNDARY & CLUSTER
│   └── 📄 Area Isolasi 1
├── 📁 02_TIANG (POLES)
│   ├── 📍 New Poles (NP725 / NP73 / NP74 / NP94)
│   └── 📍 Existing Poles (EP74)
├── 📁 03_TERMINASI & PERANGKAT
│   ├── 📦 FDT (Fiber Distribution Terminal)
│   └── 📦 FAT / ODP (Lengkap dengan ID & Port)
├── 📁 04_JALUR KABEL (CABLES)
│   ├── ➖ Kabel Distribusi 24C (Warna Hijau)
│   ├── ➖ Kabel Distribusi 48C (Warna Biru)
│   └── ➖ Kabel Feeder 96C (Warna Magenta)
└── 📁 05_REMARK & FASILITAS
    └── ⚠️ Jembatan, Crossing Jalan & Fasum
```

---

### 3. Popup Balon Informasi Interaktif (HTML Balloon)
Ketika salah satu tiang, FAT, atau kabel diklik di Google Earth, jendela popup informatif akan muncul memuat:
- **Nama & Kode Objek**: (misal `FAT-PL-01`).
- **Koordinat Latitude & Longitude**: Dalam format derajat desimal.
- **Kapasitas & Alokasi**: Port aktif, tipe tiang, panjang bentang kabel.
- **Timestamp & Versi Desain**: Tanggal rilis dan nama perencana.

---

### 4. Tips & Hal yang Perlu Diperhatikan
- 💡 **Icon Otomatis Embedded**: Seluruh ikon grafis tiang, FAT, dan FDT disematkan langsung di dalam file `.kmz`, sehingga saat file dikirim ke klien via WhatsApp atau Email, ikon tetap muncul sempurna tanpa gambar silang merah (*missing asset*).
