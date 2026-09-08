# Persyaratan Sistem & Kompatibilitas 💻

Sebelum menginstal **FTTH Design Planner**, pastikan perangkat komputer Anda memenuhi spesifikasi minimum di bawah ini untuk memastikan performa drafting berjalan lancar, terutama saat memuat peta satelit dan ribuan aset objek.

---

## 🖥️ Kompatibilitas AutoCAD

FTTH Design Planner dibangun menggunakan teknologi modern multi-targeting (.NET Framework 4.8 & .NET 8), sehingga mendukung berbagai versi AutoCAD:

| Versi AutoCAD | Target Framework | Status Dukungan |
|---|---|:---:|
| **AutoCAD 2025, 2026, 2027** | .NET 8.0 Windows | ✅ Didukung Penuh (Optimal) |
| **AutoCAD 2021, 2022, 2023, 2024** | .NET Framework 4.8 | ✅ Didukung Penuh |
| **AutoCAD 2020 ke Bawah** | .NET Lama | ⚠️ Tidak Didukung |
| **AutoCAD LT (Light)** | Tanpa API .NET | ❌ Tidak Didukung (LT tidak mendukung plugin C#) |

!!! tip "Dukungan AutoCAD Vertikal"
    Plugin ini kompatibel dengan seluruh varian AutoCAD standar, termasuk **AutoCAD Civil 3D**, **AutoCAD Map 3D**, dan **AutoCAD Architecture** edisi 64-bit.

---

## ⚙️ Spesifikasi Hardware & Sistem Operasi

| Komponen | Spesifikasi Minimum | Spesifikasi Rekomendasi |
|---|---|---|
| **Sistem Operasi** | Windows 10 64-bit | Windows 10 / 11 64-bit (Update Terbaru) |
| **Prosesor (CPU)** | Intel Core i3 / AMD Ryzen 3 (2.5 GHz+) | Intel Core i5/i7 atau AMD Ryzen 5/7 (3.0 GHz+) |
| **Memori (RAM)** | 8 GB | 16 GB atau lebih (Sangat disarankan untuk citra satelit HD) |
| **Penyimpanan** | 500 MB ruang kosong untuk plugin | SSD dengan ruang kosong minimal 10 GB (untuk cache tile peta) |
| **Koneksi Internet** | Diperlukan untuk download tile peta & aktivasi lisensi | Internet stabil minimal 10 Mbps |
| **Komponen Tambahan** | Microsoft Edge WebView2 Runtime | Sudah terpasang otomatis di Windows 10/11 terbaru |

---

## 🔍 Cara Memeriksa Versi AutoCAD Anda
1. Buka aplikasi AutoCAD Anda.
2. Ketik perintah `ABOUT` di Command Line AutoCAD lalu tekan ++enter++.
3. Periksa versi tahun rilis dan pastikan arsitekturnya adalah **64-bit**.
