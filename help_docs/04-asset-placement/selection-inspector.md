# FTTH Selection Inspector (Inspeksi Objek Mendalam) 🔎

> 💡 **Nilai Bisnis & Efisiensi**: Mengetahui identitas lengkap, atribut XData, koordinat geografis/UTM, dan relasi jaringan dari objek CAD apa pun di layar hanya dengan 1 kali klik. Sangat berguna untuk audit cepat dan verifikasi data saat revisi gambar.

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_INSPECT`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Tab **SURVEY & ASSETS** ➔ Tombol **Inspect Entity**.

---

### 2. Informasi yang Diekstrak Otomatis

Ketika Anda memilih objek di AutoCAD, **Selection Inspector** akan membaca dan menampilkan data terstruktur:

| Kategori Objek | Informasi & Atribut yang Ditampilkan |
|---|---|
| **Tiang (Pole)** | Tipe Tiang (`NP725`, `POLE73`, `EP74`), ID/Nomor Tiang, Koordinat $(X, Y, Z)$, Handle Objek, Status Grounding, Jumlah Kabel Menempel. |
| **FAT / ODP** | Nomor FAT (`FAT_NO`), FDT Induk (`FDT_CODE`), Kapasitas Port (8/16), Jumlah Hompass Terhubung, Nomor Core Layanan (`SERVICE_CORE`). |
| **FDT** | Kode FDT, Total Kapasitas Core (48/96), Jumlah Jalur Feeder/Distribusi yang Keluar. |
| **Kabel (Cable)** | Kapasitas Core (`24C`, `48C`, `96C`), Panjang Bentang Kabel (meter), Tiang Asal & Tiang Tujuan, Layer Kabel. |
| **Hompass** | Nomor Hompass (`HPNUM`), FAT Pengumpan, Jarak Drop Cable ke FAT, Status Persil Bidang. |
| **Marker DRC QA** | Tipe Kesalahan (`ERROR` / `WARNING`), Deskripsi Pelanggaran Aturan Desain, Saran Perbaikan (*Suggestion*). |

---

### 3. Fitur Copy Summary ke Clipboard
Pada panel inspector tersedia tombol **Copy Summary**. Dengan mengklik tombol ini, seluruh detail teknis objek disalin ke clipboard dalam format teks rapi, siap ditempelkan (*paste*) ke pesan WhatsApp koordinasi lapangan atau laporan excel progres proyek.
