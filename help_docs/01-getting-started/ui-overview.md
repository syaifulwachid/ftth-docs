# Pengenalan Antarmuka (WPF Palette) 🎨

Antarmuka FTTH Design Planner dirancang menggunakan teknologi modern **WPF (Windows Presentation Foundation)** yang menyatu secara elegan di dalam jendela AutoCAD dengan tema gelap **Modern Slate-Indigo**.

---

## 🖥️ Bagian-Bagian Antarmuka Utama

```text
+-------------------------------------------------------------+
| FTTH Design Planner v3.6.9              [_] [?] [X]         |
+-------------------------------------------------------------+
| Status Lisensi: [ ACTIVE ]  Masa Aktif: 30 Hari Tersisa    |
+-------------------------------------------------------------+
| [▲] 1. Pengaturan Proyek & CRS                              |
|     Sistem Koordinat (WGS84 / UTM 49S / 50S)                |
+-------------------------------------------------------------+
| [▲] 2. Persiapan Basemap & Area Isolasi                     |
|     [Import Boundary] [Download Map] [Create Basemap]       |
+-------------------------------------------------------------+
| [▲] 3. Hompass & Data Lapangan                              |
|     [Tag Hompass] [Remark Facilities] [Generate Parcel]     |
+-------------------------------------------------------------+
| [▲] 4. Penempatan Aset & FAT                                |
|     [Tag Poles] [Auto Group FAT] [Pole Replacement]         |
+-------------------------------------------------------------+
| [▲] 5. Otomatisasi Kabel & Routing                          |
|     [Auto Route Dijkstra] [Ortho Connect] [Smooth Cable]   |
+-------------------------------------------------------------+
| [▲] 6. Ekspor Deliverables                                  |
|     [Export KMZ APD] [Export KMZ ABD] [Export BOM Excel]    |
+-------------------------------------------------------------+
```

### 1. Kartu Lipat Interaktif (Collapsible Cards)
Setiap kelompok fitur dibungkus dalam kartu yang dapat dilipat (**Collapsible Card**):
- Klik pada bilah judul kartu untuk melipat atau membukanya.
- Program secara cerdas mengingat posisi buka/tutup kartu terakhir Anda, sehingga saat AutoCAD dibuka kembali, tampilan tetap rapi sesuai kebiasaan kerja Anda.

### 2. Standar Palet Warna Slate-Indigo
- **Latar Belakang Palette**: Dark Navy (`#0B1120`) – Sangat nyaman di mata saat bekerja berjam-jam di ruang redup.
- **Kartu & Kontainer**: Slate Dark (`#1E293B`) & Slate Deep (`#0F172A`).
- **Aksen Tombol Aktif**: Indigo Modern (`#6366F1`) dengan efek hover dinamis.
- **Scrollbar & Dropdown Kustom**: Dibuat ramping dan gelap, menggantikan kontrol abu-abu klasik Windows yang kaku.

### 3. Ikon Bantuan Kontekstual `[ ? ]`
Di setiap sudut kartu terdapat tombol bantuan cepat `[ ? ]`. Saat Anda membutuhkan panduan terkait fitur pada kartu tersebut, klik tombol tersebut untuk langsung membuka halaman panduan resmi ini di browser Anda.
