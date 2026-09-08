# Pembuatan Batas Area Isolasi (FTTH-ISOLATION-AREA) 🛡️

> 💡 **Nilai Bisnis & Efisiensi**: Membagi proyek berskala besar (ribuan tiang dan hompass) menjadi beberapa zona/cluster isolasi mandiri (*Area Isolasi 1, Area Isolasi 2, dst.*). Mempermudah pelaporan progres bertahap, perhitungan Bill of Materials per cluster, dan query spasial point-in-polygon.

---

### 1. Cara Akses di AutoCAD
- **Layer Khusus**: `FTTH-ISOLATION-AREA`
- **XData App Name**: `FTTH_ISOLATION_AREA_APP`
- **Lokasi di Panel**: Buka Palette `FTTH Basemap` ➔ Kartu **Basemap & Boundary** ➔ Tombol **Manage Isolation Areas**.

---

### 2. Standar Aturan Teknis
1. Area isolasi wajib digambar menggunakan **Closed Lightweight Polyline** (`LWPOLYLINE`) pada layer `FTTH-ISOLATION-AREA`.
2. Setiap polyline area isolasi menyimpan metadata nama wilayah pada **XData** (misal: `"Area Isolasi 1"`, `"Cluster Melati"`, dll).
3. Seluruh fitur penomoran, clustering, dan ekspor dapat dijalankan secara tersegmentasi berdasarkan boundary area isolasi ini (menggunakan query spasial *crossing polygon / point-in-polygon*).

---

### 3. Langkah Penggunaan (Step-by-Step)
1. Gambarlah garis polyline tertutup yang mengelilingi cluster atau zona target Anda.
2. Tempatkan polyline tersebut pada layer `FTTH-ISOLATION-AREA`.
3. Gunakan menu **Set Area Name** di panel untuk memberikan nama unik pada cluster tersebut.
4. Saat menjalankan fitur ekspor KMZ atau perhitungan BOM, Anda dapat memilih apakah ingin memproses seluruh gambar (Global) atau hanya area isolasi tertentu.

---

### 4. Tips & Hal yang Perlu Diperhatikan
- 💡 **Polyline Wajib Tertutup**: Pastikan properti `Closed = Yes` pada polyline boundary agar algoritma point-in-polygon dapat menghitung tiang dan rumah di dalamnya secara 100% akurat.
- 💡 **Tumpang Tindih**: Hindari menggambar boundary area isolasi yang saling tumpang tindih (*overlapping*) agar tidak terjadi penghitungan aset ganda (*double-counting*) saat ekspor BOQ.
