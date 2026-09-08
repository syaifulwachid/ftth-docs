# Validasi Aturan Desain & Budget Optik 🔬

> 💡 **Nilai Bisnis & Efisiensi**: Melakukan audit otomatis (*pre-flight check*) terhadap seluruh gambar kerja sebelum diajukan ke klien. Mencegah revisi berulang akibat bentang tiang melebihi batas atau redaman optik melebihi standar loss link budget.

---

### 1. Pengecekan Aturan Desain (Design Rule Validation)

Sistem secara otomatis memindai gambar kerja Anda dan memberikan laporan jika ditemukan anomali:
- 🚩 **Span Tiang Berlebih**: Menandai bentang jarak antar tiang yang melebihi 50 meter.
- 🚩 **Over-Capacity FAT**: Menandai jika ada FAT yang melayani lebih dari 16 hompass.
- 🚩 **Kabel Putus / Gantung**: Menandai garis kabel yang ujungnya tidak menempel (*snap*) pada tiang atau terminasi.
- 🚩 **Tektok Berlebih**: Menandai rute kabel putar balik yang melebihi batas toleransi 60 meter.

---

### 2. Kalkulasi Redaman Optik (Optical Link Budget)

Formula perhitungan redaman kabel dari OLT/FDT menuju FAT terjauh:

$$\text{Total Loss (dB)} = (L_{\text{kabel}} \times \alpha) + (N_{\text{splice}} \times \text{Loss}_{\text{splice}}) + (N_{\text{connector}} \times \text{Loss}_{\text{conn}}) + \text{Loss}_{\text{splitter}}$$

Parameter Standar yang Digunakan:
- **Redaman Kabel Optik ($\alpha$)**: $0.35 \text{ dB/km}$ pada panjang gelombang $1310 \text{ nm}$ ($0.22 \text{ dB/km}$ pada $1550 \text{ nm}$).
- **Sambungan Fusion Splice**: $0.05 \text{ dB}$ per titik sambung.
- **Konektor SC/APC**: $0.3 \text{ dB}$ per pasang.
- **Splitter PLC 1:4**: $\approx 7.2 \text{ dB}$.
- **Splitter PLC 1:8**: $\approx 10.5 \text{ dB}$.
- **Splitter PLC 1:16**: $\approx 13.8 \text{ dB}$.

Plugin akan menampilkan status **PASS** (jika total redaman masih dalam ambang aman $\le 25 \text{ dB}$) atau **FAIL** (jika redaman terlalu besar dan membutuhkan penyesuaian splitter atau rute).
