# Smart Smooth Polyline (Menghaluskan Belokan Kabel) ➰

> 💡 **Nilai Bisnis & Efisiensi**: Mengubah sudut-sudut tajam polyline kabel menjadi lengkungan halus (*fillet / arc curve*) secara interaktif menggunakan slider langsung di AutoCAD. Menjamin radius lentur kabel fiber optik (*bending radius*) tidak melanggar batas tekukan kabel fisik.

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_SMOOTH_PLINE`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Cable Routing Tools** ➔ Tombol **Smooth Polyline**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Pilih garis polyline rute kabel yang memiliki sudut belokan tajam.
2. Jalankan perintah `FTTH_SMOOTH_PLINE`.
3. Jendela interaktif dengan kontrol **Slider Smoothing Radius** akan muncul di layar.
4. Geser slider ke kanan untuk memperbesar radius lengkungan, atau ke kiri untuk memperkecil.
5. Anda dapat melihat perubahan kelengkungan kabel secara langsung di Model Space (*Live Real-Time Preview*).
6. Tekan tombol **Apply / OK** untuk menetapkan bentuk polyline lengkung.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Animasi slider penghalus lekukan kabel secara real-time -->

---

### 4. Tips & Hal yang Perlu Diperhatikan
- ⚠️ **Aturan Bending Radius**: Kabel fiber optik outdoor memiliki batas tekukan minimum (sekitar 20x diameter kabel). Penggunaan fitur ini memastikan gambar desain mematuhi kaidah standar instalasi lapangan.
