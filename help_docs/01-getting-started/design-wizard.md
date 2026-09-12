# Mode Panduan Alur Kerja (Design Wizard 8-Langkah) 🧙

> 💡 **Nilai Bisnis & Efisiensi**: Menghilangkan kebingungan langkah drafting bagi drafter pemula atau desainer baru dalam tim Anda. Design Wizard memandu proses perencanaan FTTH langkah-demi-langkah dari garis as jalan awal hingga siap diekspor ke KMZ dan Excel dengan garansi standar QA 100%!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_WIZARD`
- **Lokasi di Panel**: Klik tombol ikon Wizard `🧙` di sudut kanan atas header panel `FTTH Basemap`.

---

### 2. Alur 8-Langkah Terpandu (The 8-Step Pipeline)

```mermaid
graph LR
    S1[1. As Jalan & Basemap] --> S2[2. Cek Celah Simpang]
    S2 --> S3[3. Persil & Homepass]
    S3 --> S4[4. Penempatan Tiang]
    S4 --> S5[5. Klaster ODP & FAT]
    S5 --> S6[6. Auto-Route Kabel]
    S6 --> S7[7. Labeling & Budget]
    S7 --> S8[8. Audit DRC & Export]
```

| Langkah | Nama Tahap | Deskripsi & Tindakan Utama | Perintah CAD Terkait |
|:---:|---|---|---|
| **1** | **As Jalan & Basemap** | Mengimpor batas wilayah, download peta satelit, dan membuat basemap jalan otomatis dari garis centerline. | `FTTH_IMPORT_BOUNDARY`, `FTTH_GENERATE` |
| **2** | **Cek Celah & Simpang** | Memeriksa celah as jalan (*gap tolerance*) dan membersihkan seluruh persimpangan jalan agar terhubung rapi. | `FTTH_INTERSECT` |
| **3** | **Persil & Homepass** | Membuat bidang kavling/atap bangunan dan menomori ribuan hompass pelanggan (HPNUM) terurut. | `FTTH_GENERATE_PARCELS`, `FTTH_TAG_HOMPASS` |
| **4** | **Penempatan Tiang** | Memasang blok tiang standar (NP725, NP73, EP74) dan melakukan penomoran urut otomatis di sepanjang rute. | `FTTH_PLACE_DIRECT`, `FTTH_GEN_POLE_LABEL` |
| **5** | **Klaster ODP & FAT** | Mengelompokkan hompass (maksimal 16 rumah per FAT) dan menempatkan FAT otomatis pada tiang terdekat. | `FTTH_GROUP` |
| **6** | **Auto-Route Kabel** | Menghubungkan FDT ke seluruh FAT dengan algoritma rute terpendek Dijkstra dan penentuan kapasitas core (24C-48C). | `FTTH_DRAW_SMART_PLINE`, `FTTH_AUTO_ROUTE_CABLE` |
| **7** | **Labeling & Link Budget** | Menghasilkan label bentang kabel, skematik FAT-CORE, dan menghitung estimasi redaman optik link budget. | `FTTH_GEN_SPAN_LABELS`, `FTTH_CALC_BUDGET` |
| **8** | **Audit DRC & Export** | Memindai kesalahan desain (DRC QA check) dan mengekspor deliverables (KMZ APD/ABD, BOM Excel, HPDB). | `FTTH_DRC`, `FTTH_EXPORT_KMZ_APD`, `FTTH_EXPORT_BOM` |

---

### 3. Stepper Pills & Fitur Interaktif
- **Pills Indikator 1–8**: Klik angka pada pil navigator di panel untuk melompat langsung ke langkah yang Anda inginkan.
- **Pro-Tip Box**: Di setiap langkah, panel menyajikan *Engineering Pro-Tip* khusus yang mengingatkan aturan teknis lapangan (misal batas bending radius kabel atau span tiang).
- **Tombol Action Langsung**: Setiap langkah menyediakan tombol eksekusi perintah CAD langsung di dalam kartu wizard, sehingga Anda tidak perlu mencari-cari tombol di kartu lain.
