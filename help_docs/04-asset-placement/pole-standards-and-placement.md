# Standar Blok Tiang & Spesifikasi Teknis (Poles) 📍

FTTH Design Planner telah dilengkapi dengan blok simbol dan layer terstandarisasi untuk berbagai tipe tiang telekomunikasi sesuai acuan vendor dan operator di Indonesia.

---

## 🏗️ Daftar Tipe Tiang Standar

| Nama Blok | Layer CAD | Keterangan & Spesifikasi |
|---|---|---|
| **NP725** | `FTTH-POLE-NP725` | New Pole 7 Meter, diameter pipa 2.5 inch (Standar tiang distribusi gang) |
| **POLE73IN** | `FTTH-POLE-NP73` | New Pole 7 Meter, diameter pipa 3.0 inch (Tiang distribusi jalan lingkungan) |
| **POLE74IN** | `FTTH-POLE-NP74` | New Pole 7 Meter, diameter pipa 4.0 inch (Tiang feeder / sudut belokan berat) |
| **POLE94IN** | `FTTH-POLE-NP94` | New Pole 9 Meter, diameter pipa 4.0 inch (Tiang crossing jalan raya / rel KA) |
| **EP74** | `FTTH-POLE-EXISTING` | Existing Pole 7 Meter (Tiang sewa milik pihak ketiga / PLN / Telkom) |

---

## 🎯 Cara Penempatan Tiang di AutoCAD
- **Command CAD**: `FTTH_PLACE_POLE`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Asset Placement** ➔ Pilih tipe tiang dari dropdown ➔ Klik tombol **Place Pole**.

### Langkah Penempatan:
1. Pilih jenis tiang yang ingin dipasang dari dropdown panel.
2. Klik titik penempatan pada gambar AutoCAD di sepanjang tepi bahu jalan.
3. Blok tiang akan terpasang lengkap dengan atribut koordinat, tipe, dan layer yang sesuai secara otomatis.

---

## 📏 Standar Aturan Jarak (Span) Tiang
- **Jarak Standar Antar Tiang**: 35 – 45 meter.
- **Jarak Maksimum (Maksimal Span)**: 50 meter (untuk menghindari kabel kendur atau melorot).
- **Crossing Jalan Raya**: Wajib menggunakan tiang minimal 9 meter (`POLE94IN`) dengan tinggi bebas kabel (*clearance*) minimal 5.5 meter dari permukaan aspal.
