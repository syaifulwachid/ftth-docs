# FDT Line Hierarchy Manager & Multi-FDT Routing 🔀

> 💡 **Nilai Bisnis & Efisiensi**: Mengelola proyek jaringan skala besar yang memiliki lebih dari satu FDT (*Multi-FDT*) atau rute kabel distribusi bercabang rumit. Memungkinkan penataan nomor urut jalur, pertukaran (*swap*) rute kabel, dan visualisasi arah aliran core dengan panah bantu!

---

### 1. Cara Akses di AutoCAD
- **Command CAD**:
  - `FTTH_MANAGE_FDT_LINES` (Membuka Jendela FDT Line Hierarchy Manager)
  - `FTTH_PICK_MULTI_FDT` (Memilih dan mengurutkan FDT sumber rute)
  - `FTTH_SWAP_CABLE_LINES` (Menukar urutan prioritas 2 rute kabel)
  - `FTTH_DRAW_HELPER_ARROW` (Menggambar panah visual penunjuk arah aliran sinyal)
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Tab **ROUTING & DESIGN** ➔ Tombol **Manage FDT Lines**.

---

### 2. Fitur FDT Line Hierarchy Manager Window

Jendela interaktif **FdtLineManagerWindow** menyediakan tabel kontrol menyeluruh:
1. **Daftar FDT Aktif**: Menampilkan seluruh FDT yang terdeteksi di gambar kerja beserta jumlah cabang kabel keluar.
2. **Urutan Jalur Distribusi (Line Sequence)**: Mengatur urutan cabang kabel (Line 1, Line 2, Line 3, dst) yang keluar dari masing-masing FDT.
3. **Move Up / Move Down**: Memindahkan prioritas alokasi tube/core serat optik pada cabang kabel tertentu.
4. **Swap Lines**: Menukar urutan alokasi dua segmen kabel secara cepat saat terjadi re-routing lapangan.
5. **Helper Flow Arrows**: Otomatis menggambar panah arah aliran core (`FTTH_DRAW_HELPER_ARROW`) dari FDT menuju FAT-FAT hilir untuk memastikan gambar mudah dipahami oleh tim instalasi kabel udara.

---

### 3. Langkah Penggunaan (Step-by-Step)
1. Jalankan perintah `FTTH_MANAGE_FDT_LINES`.
2. Jendela pengelola rute akan menampilkan pohon hirarki seluruh FDT dan jalur kabel yang menempel.
3. Pilih jalur kabel yang ingin disesuaikan.
4. Gunakan tombol **Set Sequence** atau **Swap** untuk mengatur prioritas core kabel.
5. Klik **Apply & Update Drawing** untuk memperbarui atribut skematik core dan nomor rute secara serentak di AutoCAD.
