# Pengenalan Antarmuka (WPF Palette) 🎨

Antarmuka **FTTH Design Planner v3.6.9 (r22)** dirancang menggunakan teknologi modern **WPF (Windows Presentation Foundation)** yang menyatu secara elegan di dalam jendela AutoCAD dengan tema gelap **Modern Slate-Indigo**.

---

## 🖥️ Layout Antarmuka Terkini (v3.6.9 r22)

```text
+-------------------------------------------------------------------------+
| FTTH Design Planner v3.6.9 (r22)     [⚡ SWD AI] [🔄 Update] [🔑] [ℹ] [🧙] |
+-------------------------------------------------------------------------+
| 🔍 [ Cari fitur / kartu (Ctrl+F)...                         ] [✕]       |
+-------------------------------------------------------------------------+
| [📍 Place] [⚡ Ortho] [🏷️ Label] [🛣️ Road] [✅ DRC] [📊 Status]         |
+-------------------------------------------------------------------------+
| [Tab 1: BASEMAP] [Tab 2: SURVEY] [Tab 3: ROUTING] [Tab 4: LABELS] ...   |
+-------------------------------------------------------------------------+
| [▲] Kartu Fitur Interaktif (Collapsible Card)                           |
|     Konten kontrol, tombol aksi, input parameter, dan dropdown          |
+-------------------------------------------------------------------------+
| Log Aktivitas & Status Bar                                              |
+-------------------------------------------------------------------------+
```

---

## 🌟 Komponen Kontrol Unggulan Terbaru

### 1. SWD AI Assistant Button (`[⚡ SWD AI]`)
Tombol bergradien futuristik di pojok kanan atas untuk memanggil modul asisten cerdas FTTH AI (Command: `FTTH_ASSISTANT` / `SWD_AI`).

### 2. Quick Search Bar (`Ctrl+F`)
- Tekan ++ctrl+f++ di mana saja pada panel untuk langsung memfokuskan kursor ke kotak pencarian.
- Ketik nama fitur (contoh: *"dijkstra"*, *"fat"*, *"tiang"*, *"drc"*, *"kml"*), sistem akan menampilkan daftar saran fitur yang dapat diklik untuk langsung membuka kartu terkait.

### 3. Quick Action Bar (Bilah Aksi 1-Klik)
Bilah shortcut di bawah kolom pencarian untuk mengeksekusi operasi paling sering digunakan:
- **`📍 Place`** (`FTTH_PLACE_DIRECT`): Penempatan aset langsung di CAD.
- **`⚡ Ortho`** (`FTTH_DRAW_ORTHO`): Tarik rute kabel ortogonal siku 90°.
- **`🏷️ Label`** (`FTTH_GEN_POLE_LABEL`): Generate label tiang dan nomor urut.
- **`🛣️ Road`** (`FTTH_ROADLABEL`): Generator label nama jalan.
- **`✅ DRC`** (`FTTH_DRC`): Audit kualitas desain dan cek aturan span.
- **`📊 Status`** (`FTTH_SUMMARY_STATUS`): Dashboard rekapitulasi jumlah aset proyek.

### 4. Mode Panduan Alur (`[🧙]` Design Wizard)
Tombol ikon penyihir di header untuk membuka mode pemandu kerja 8-langkah (*stepper*) yang memastikan alur kerja desain terstruktur dari as jalan hingga siap ekspor.

### 5. Floating Toast Notification
Overlay notifikasi animasi mengambang di bagian atas panel yang memberikan laporan progres real-time tanpa memblokir interaksi mouse Anda dengan model CAD.

### 6. Standar Palet Warna Slate-Indigo
- **Latar Belakang Palette**: Dark Navy (`#0B1120`) – Sangat nyaman di mata saat bekerja berjam-jam.
- **Kartu & Kontainer**: Slate Dark (`#1E293B`) & Slate Deep (`#0F172A`).
- **Aksen Tombol Aktif**: Indigo Modern (`#6366F1`) dengan efek hover dinamis.
- **Scrollbar & Dropdown Kustom**: Dibuat ramping dan gelap, menggantikan kontrol abu-abu klasik Windows yang kaku.
