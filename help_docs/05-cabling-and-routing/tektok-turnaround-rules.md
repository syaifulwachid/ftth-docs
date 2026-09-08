# Aturan Putar Balik Kabel (Tektok) di Tiang & Jalan Buntu ↩️

Dalam desain jaringan FTTH, jalur kabel sering kali harus masuk ke gang buntu (*cul-de-sac*) untuk melayani FAT di ujung jalan, kemudian "putar balik" (*tektok*) pada tiang yang sama untuk melanjutkan distribusi ke jalan lain.

---

## 🛑 Aturan Batasan Jarak Tektok (Maksimal 60 Meter)

1. **Batas Toleransi Tektok**: Maksimum **60 meter**.
2. **Kondisi Jalan Buntu**: Jika panjang gang buntu dari persimpangan $\le 60$ meter, kabel diperbolehkan tektok pada tiang yang sama.
3. **Kondisi Lebih dari 60 Meter**: Jika panjang gang buntu $> 60$ meter, sistem akan merekomendasikan:
   - Penempatan sub-feeder khusus atau closure branching di mulut gang.
   - Pemasangan tiang tambahan atau FAT ganda untuk membagi rute.

---

## 🎨 Visualisasi Offset Garis Kabel Tektok
Agar dua jalur kabel yang berada pada bentang tiang yang sama tidak saling menumpuk (*coincident lines*), FTTH Design Planner secara otomatis menerapkan **Offset Paralel 10–15 cm**:
- Garis kabel pergi berada di sisi kiri centerline tiang.
- Garis kabel pulang (*tektok*) berada di sisi kanan centerline tiang.
- Kerapian gambar kerja tetap terjaga dan sangat jelas terbaca oleh pengawas lapangan maupun tim audit klien.
