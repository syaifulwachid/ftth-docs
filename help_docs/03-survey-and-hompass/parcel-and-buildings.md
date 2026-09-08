# Persil Bidang Bangunan & Polygon Generator 🏢

> 💡 **Nilai Bisnis & Efisiensi**: Mengonversi garis batas kavling atau atap bangunan menjadi polygon persil tanah (*building footprint*) yang rapi dengan centroid dan atribut lengkap untuk analisis penetrasi pasar ISP.

---

### 1. Cara Akses di AutoCAD
- **Command CAD**: `FTTH_GEN_PARCEL` atau `FTTH_AUTOCENTROID`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Hompass & Survey Data** ➔ Tombol **Generate Building Parcels**.

---

### 2. Langkah Penggunaan (Step-by-Step)
1. Pilih garis atau polyline yang membentuk kavling tanah atau denah bangunan.
2. Jalankan perintah `FTTH_GEN_PARCEL`.
3. Program akan:
   - Memastikan seluruh garis tertutup membentuk polygon (`LWPOLYLINE`).
   - Menghitung titik tengah (*geometric centroid*) dari masing-masing bidang.
   - Menempatkan blok point hompass tepat di centroid bidang bangunan.
   - Menyimpan luas bidang bangunan (dalam $m^2$) ke dalam atribut XData.

---

### 3. Visual & Tangkapan Layar
> 📸 *(Area Screenshot / Animasi GIF)*
<!-- Placeholder: Gambar pembentukan polygon persil dan titik centroid hompass -->

---

### 4. Tips & Hal yang Perlu Diperhatikan
- 💡 **AutoCentroid**: Fitur ini sangat berguna untuk data hasil digitasi dari peta satelit atau data GIS shapefile shp/kml dari pemerintah daerah.
