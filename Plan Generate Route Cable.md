Jadi logikanya begini:
dalam gambar pada autocad jaringan FTTH ada komponen utama yang perlu saya sebutkan dulu yaitu:
1. Berbgai macam jenis tiang di sepanjang jalur baju jalan
2. FDT/ODC
3. Rumah Rumah pelanggan
4. FAT/ODP
jado 4 hal itu yang plaing crusial, karen aini akan menentukan topologi jaringan yang nanti akan di bangun, jadi sebelum penentuan rute kabel kita sudah harus memiliki elemen elemen di atas. untuk kemudian nanti bisa di otomatisaasi.

rinciannya sebgai berikut:
## 1. Berbgai macam jenis tiang di sepanjang jalur bahu jalan #

### A. Tiang Eksisting (Existing - EXT)

- **`EXT TEL`** (UI: `RadExtTel`)
- **`EXT 7 2.5"`** (UI: `RadExt7_2_5`)
- **`EXT 7 3"`** (UI: `RadExt7_3`)
- **`EXT 7 4"`** (UI: `RadExt7_4`)
- **`EXT 9 4"`** (UI: `RadExt9_4`)

### B. Tiang Baru (New Pole - NP)

- **`NP 7 2.5"`** (UI: `RadNp7_2_5`)
- **`NP 7 3"`** (UI: `RadNp7_3`)
- **`NP 7 4"`** (UI: `RadNp7_4`)
- **`NP 9 4"`** (UI: `RadNp9_4`)


## 2. FDT/ODC (UI CheckBox: `ChkFdtOdc`)
- setelah tiang sidah punya baru nanti user akan menentukan titik FDT ini pada tiang tertentu
## 3. Rumah Rumah pelanggan
- lalu kode akan menelusuri seluruh polyline representasi dari rute jalan (layer: FTTH-CENTERLINE )
- lalu program akan membuat pengelompokan titik rumah rumah subscriber ini per maximal 16 rumah tiap 1 FAT, jadi buat dulu pengelompokan rumah rumah ini yang berada di kanan dan kiri jalan ke seluruh jalur FTTH-CENTERLINE
## 4. FAT/ODP (UI CheckBox: `ChkFatOdp`)
- lalu letakkan FAT/ODP pada tiang terdekat dengan masing masing kelompok group node subscriber rumah tersebut di sepanjang jalur jalan FTTH-CENTERLINE
- cari cara algoritma yang tepat untuk menangani hal ini baik pada jalur lurus maupun persimpangan 3 dan persimpangan 4( perempatan).

lalau langkah selanjutnya adalah, program harus membuat jalur kabel secara otomatis dari titik FDT menuju titik FAT terdekat dan di lanjut ke titik  titik FAT berikutnya mengikuti jalur titik titik tiang. jadi batas yang bisa di cover dalam 1 line kabel mengikuti kapasitas kabel yang di pakai, dan batas jumlah line kabel yang di pakai tergantung jumlah kapasitas FDT yang di pakai.
untuk FDT 48 dia hanya bisa meng cover 20 FAT, dan untuk FDT 72 dia hanya bisa meng cover 30 FAT. begitu juga dengan kapasitas kabel, untuk kabel 48 bisa mencover 20 FAT maximal, kabel 36 mengcover 15 FAT maximal, dan kabel 24 core mengcover 10 FAT maximal.

untuk jalur kabel tidak harus lurus atau berbelok L shape saja melainkan dia bisa putar balik untuk menuju FAT paling akhir yang bisa di cover untuk memaksimalkan penggunakaan sesuai kapasitas maximal kabel.
putar balik maksudnya adalah kabelnya berbalik arah atau istilahnya tektok tetap melewati tiang sebelumnya yang tadi sudah dilewati tadi.
dan aturan kabel tektok ini hanya boleh jika panjang bolak baliknya tidak lebih dari 60 meter, 

lalau jika sudah selesai membuat jalur kabel pada line A ( pertama ) dan ternyata masih ada FAT yang tersisa lalu kapasitas FDT masih memungkinkan maka tarik jalur kabel baru lagi dari titik FDT menuju FAT sisanya melalui tiang tiang yang ada, jadi saat membuat jalur kabel dengan polyline titik titik vertexnya harus tepat pada posisi insertion point dari tiang.

jadi dengan begini program bisa menentukan jenis kabelnya yang akan di gunakan untuk membuat jalur mau menggunakan kabel 24 core. 36 core atau 48 core pada jalur masing masing line


# Summary

Untuk menangani otomatisasi desain jaringan FTTH yang sangat kompleks ini di AutoCAD (menggunakan Python dengan pustaka seperti `pyautocad`, `ezdxf`, atau AutoLISP), Anda membutuhkan kombinasi beberapa algoritma **Geospatial**, **Clustering (Pengelompokan)**, dan **Graph Theory (Teori Graf)**.

Berikut adalah rekomendasi logika algoritma yang cocok beserta rencana eksekusi sistematisnya.

### Bagian 1: Logika & Algoritma yang Cocok

1. **Spatial Matching (Proximity & Projection):** Untuk memproyeksikan posisi rumah ke jalur jalan (`FTTH-CENTERLINE`) dan mencari tiang terdekat.
    
2. **Constrained K-Means Clustering atau Agglomerative Clustering:** Untuk mengelompokkan rumah-rumah pelanggan menjadi maksimal 16 rumah per kelompok berdasarkan kedekatan geografis di sepanjang jalur jalan.
    
3. **Graph Representation (NetworkX):** Mengubah seluruh tiang (baik EXT maupun NP) dan jalur jalan menjadi struktur _Graph_ (Node = Tiang, Edge = Jalur kabel antar tiang). Tiang yang berada di persimpangan akan menjadi node dengan banyak cabang (_junction_).
    
4. **Greedy Traveling Salesperson Problem (TSP) dengan Aturan Tektok (Constraint):** Untuk menentukan urutan FAT yang akan dilewati kabel dari FDT, dengan batas kapasitas core dan toleransi putar balik (tektok) maksimal 60 meter.
    

### Bagian 2: Rencana Eksekusi Program (Step-by-Step)

#### Langkah 1: Inisialisasi & Pembentukan Peta Graf (Tiang & Jalan)

Sebelum mendesain kabel, program harus membaca kondisi lapangan dari AutoCAD.

- **Aksi:** Program membaca semua _Insertion Point_ dari blok Tiang (EXT dan NP) serta garis `FTTH-CENTERLINE`.
    
- **Logika Persimpangan (Pertigaan/Perempatan):** Garis centerline jalan dipecah pada titik potong persimpangan. Tiang-tiang di area persimpangan akan terhubung ke lebih dari 2 tiang tetangga dalam struktur data Graf.
    
- **Output:** Sebuah struktur jaringan (Graf) di mana kabel hanya bisa ditarik dari satu tiang ke tiang lain yang bertetangga.
    

#### Langkah 2: Proyeksi & Pengelompokan Rumah (Maksimal 16 Rumah per FAT)

Menentukan klaster pelanggan tanpa memedulikan apakah mereka di kiri atau kanan jalan, karena acuannya adalah bentangan jarak sepanjang centerline jalan.

- **Aksi:** 1. Cari titik terdekat (_orthogonal projection_) dari setiap rumah ke `FTTH-CENTERLINE`.
    
    2. Urutkan rumah berdasarkan posisinya di sepanjang jalur jalan.
    
    3. Gunakan algoritma **Sliding Window** atau **Agglomerative Clustering**: Mulai dari ujung jalan, kelompokkan setiap 16 rumah terdekat menjadi 1 Kelompok ODP. Jika ada persimpangan, kelompokkan rumah di cabang persimpangan tersebut terlebih dahulu.
    
- **Output:** Daftar kelompok rumah (Klaster 1, Klaster 2, dst.) yang masing-masing berisi maksimal 16 rumah.
    

#### Langkah 3: Penempatan Titik FAT/ODP pada Tiang

- **Aksi:** Untuk setiap kelompok rumah, hitung rata-rata koordinat (_centroid_) dari posisi proyeksi rumah mereka di jalan. Kemudian, cari **Tiang Terdekat** (dari database Langkah 1) dari titik _centroid_ tersebut.
    
- **Output:** Lokasi penempatan koordinat FAT/ODP yang tepat berada di _insertion point_ tiang.
    

#### Langkah 4: Routing Kabel dari FDT ke FAT (Logika Kapasitas & Tektok)

Ini adalah inti dari otomatisasi. Program akan mensimulasikan penarikan kabel _Line_ per _Line_.

- **Algoritma Urutan Penarikan:**
    
    1. Mulai dari titik FDT yang dipilih user.
        
    2. Cari FAT terdekat yang belum terkoneksi kabel.
        
    3. Tarik kabel dari FDT melewati tiang-tiang menggunakan algoritma pencarian rute terpendek (_Dijkstra_) hingga mencapai FAT tersebut.
        
    4. Dari FAT tersebut, cari FAT berikutnya yang belum terkoneksi.
        
- **Logika Tektok (Putar Balik < 60 Meter):**
    
    - Jika FAT berikutnya berada di arah belakang (jalur buntu atau ujung percabangan), program menghitung jarak tiang saat ini $\rightarrow$ ke FAT terakhir $\rightarrow$ kembali lagi ke tiang persimpangan awal.
        
    - **Kondisi:** Jika (Jarak Pergi + Jarak Kembali) $\le 60$ meter, program mengizinkan kabel berputar balik (tektok) pada tiang yang sama dan melanjutkan sisa kapasitas kabel ke arah lain.
        
    - Jika $> 60$ meter, kabel di jalur tersebut dihentikan (di-cap) dan sisa FAT harus dicover oleh _Line_ kabel baru.
        
- **Logika Batas Kapasitas (Kabel & FDT):**
    
    - Program mendeteksi jumlah FAT yang berhasil dilewati dalam 1 _Line_.
        
    - Program secara cerdas menentukan jenis kabel berdasarkan jumlah FAT yang ditemui:
        
        - $\le 10$ FAT $\rightarrow$ Gambar dengan spesifikasi **Kabel 24 Core**.
            
        - $11 - 15$ FAT $\rightarrow$ Gambar dengan spesifikasi **Kabel 36 Core**.
            
        - $16 - 20$ FAT $\rightarrow$ Gambar dengan spesifikasi **Kabel 48 Core**.
            
    - Jika total FAT pada _Line A_ sudah mencapai 20 FAT (atau kabel sudah penuh), program akan memotong _Line_ tersebut.
        
    - Jika masih ada FAT yang tersisa di area tersebut, program memeriksa kapasitas FDT (FDT 48 max 20 FAT, FDT 72 max 30 FAT). Jika FDT masih muat, program membuka **Line B** baru dari FDT, melewati tiang-tiang dari awal lagi untuk menuju FAT sisa tersebut.
        

#### Langkah 5: Menggambar Polyline Otomatis di AutoCAD

- **Aksi:** Setelah semua jalur _Line A, Line B_, dst. selesai dihitung di memori program, perintah pembuatan `Polyline` dikirim ke AutoCAD.
    
- **Aturan:** Titik _Vertex_ (sudut) dari polyline kabel wajib dikunci (_snap_) tepat pada koordinat _Insertion Point_ tiang-tiang yang dilewati. Berikan warna atau _layer_ berbeda untuk setiap kapasitas core kabel yang terpilih (24, 36, 48 Core) agar desainer mudah melakukan validasi.
    

### Keuntungan Pendekatan Ini

Dengan membagi logika menjadi 5 tahapan di atas, program Anda tidak akan bingung saat menemui perempatan atau pertigaan jalan. Di persimpangan, algoritma Graf secara otomatis melihat ada 3 atau 4 pilihan cabang tiang, dan aturan pembatasan jarak tektok 60 meter akan menjadi penentu apakah cabang jalan tersebut harus dimasuki dengan kabel tektok atau harus ditarik kabel baru dari FDT.