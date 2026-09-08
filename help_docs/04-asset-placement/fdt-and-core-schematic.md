# FDT & Skematik Alokasi FAT-CORE 🔌

Penataan core kabel dari FDT (Fiber Distribution Terminal) menuju setiap FAT harus memenuhi standar alokasi warna tube dan nomor core serat optik.

---

## 🗄️ Standar Blok FDT & Skematik Core

| Komponen | Layer AutoCAD | Keterangan & Fungsi |
|---|---|---|
| **FDT48** | `FTTH-FDT48` | Titik distribusi utama kapasitas 48 Core (mencakup hingga 48 FAT atau splitter rasio 1:4/1:8) |
| **Core_Alocation_Schematic** | `FTTH-FAT-CORE` | Tabel skematik alokasi core pada tiang atau detail drawing |

---

## 🎨 Standar Pewarnaan Tube & Core (TIA/EIA-598)
Software mengikuti urutan 12 warna standar internasional:
1. Biru (Blue)
2. Oranye (Orange)
3. Hijau (Green)
4. Cokelat (Brown)
5. Abu-abu (Slate/Grey)
6. Putih (White)
7. Merah (Red)
8. Hitam (Black)
9. Kuning (Yellow)
10. Ungu (Violet)
11. Merah Muda (Rose/Pink)
12. Toska (Aqua)

---

## 📊 Atribut Skematik FAT-CORE
Setiap blok skematik menyimpan metadata otomatis yang dapat dibaca oleh sistem maupun diekspor ke laporan:
- `FAT_NO`: Nomor identitas FAT yang dilayani (misal `FAT-01`).
- `FDT_CODE`: Kode induk FDT pengumpan (misal `FDT-KRAKSAAN-01`).
- `SERVICE_CORE`: Nomor core aktif yang digunakan untuk layanan pelanggan.
- `EXPANSION_CORE`: Nomor core cadangan untuk ekspansi kapasitas di masa depan.
- `MONITORING_CORE`: Nomor core yang disiapkan untuk pengujian OTDR berkala.
