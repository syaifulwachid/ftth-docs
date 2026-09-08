# Ekspor ke Format HPDB & GeoJSON 🗃️

> 💡 **Nilai Bisnis & Efisiensi**: Menjembatani integrasi data AutoCAD dengan sistem database spasial korporat (HPDB - Homepass Database) serta software GIS (QGIS, ArcGIS, MapInfo) melalui format standar terbuka GeoJSON.

---

### 1. Ekspor Format HPDB (Homepass Database)
Format HPDB adalah format tabular terstandar yang digunakan oleh operator telekomunikasi besar (seperti Telkomsel, Fiberhome, Linknet) untuk merekam data pelanggan:
- **ID Hompass**: Nomor unik rumah.
- **FAT Induk**: Kode FAT pengumpan.
- **Port Alokasi**: Nomor port drop cable.
- **Koordinat Spasial**: Latitude dan Longitude WGS84.
- **Status Bangunan**: Rumah tinggal, ruko, kantor, atau tanah kosong.

---

### 2. Ekspor Format GeoJSON
GeoJSON memungkinkan seluruh layer AutoCAD (garis jalan, batas isolasi, kabel, tiang, dan FAT) dibuka di platform peta modern:
- **Command CAD**: `FTTH_EXPORT_GEOJSON`
- **Output File**: File `.geojson` berbasis teks standar RFC 7946 yang memuat atribut fitur lengkap (*feature properties*) dan geometri titik/garis/polygon.
- **Kompatibilitas**: Siap dimuat ke QGIS, Mapbox, Google Maps API, atau sistem web monitoring internal perusahaan Anda.
