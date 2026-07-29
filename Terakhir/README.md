# FTTH License Library — Modul Lisensi Reusable untuk AutoLISP

Modul lisensi reusable untuk AutoCAD LSP yang mencakup **pembayaran QRIS (TemanQRIS)**, **aktivasi lisensi**, dan **notifikasi Telegram**. Bekerja seperti DLL — load sekali, gunakan di LSP manapun.

---

## 📁 Struktur File

```
FTTH_License_Lib/
├── Config.lsp              ← Konfigurasi API keys & pengaturan (EDIT INI)
├── LicenseUtils.lsp        ← Fungsi helper (Machine ID, hash, enkripsi)
├── TelegramNotifier.lsp    ← Modul notifikasi Telegram Bot
├── PaymentModule.lsp       ← Modul pembayaran TemanQRIS QRIS
├── LicenseManager.lsp      ← Entry point utama (LOAD INI SAJA)
├── ExampleUsage.lsp        ← Template untuk proyek baru
├── Placer.lsp              ← Contoh implementasi (FTTH Master Placer v5.2)
└── README.md               ← Dokumentasi ini
```

---

## 📦 Instalasi

### Langkah 1: Daftarkan Folder ke AutoCAD Support Path

1. Buka AutoCAD → ketik `OPTIONS` di command line
2. Tab **Files** → klik **Support File Search Path**
3. Klik **Add** → Browse ke folder `FTTH_License_Lib`
4. Klik **OK** → **Apply**

### Langkah 2: Verifikasi Instalasi

Ketik di AutoCAD command line:
```
(findfile "LicenseManager.lsp")
```
Jika mengembalikan path file, instalasi berhasil.

---

## 🚀 Cara Membuat Program Baru (Quick Start)

### 1. Salin Template

Salin [`ExampleUsage.lsp`](ExampleUsage.lsp) dan rename sesuai nama program Anda.

### 2. Edit Konfigurasi di Awal File

```lisp
;; Ganti semua bagian [GANTI INI]
(setq *License-File-Name*    "sysNamaProgram.cfg")    ;; Nama file lisensi unik
(setq *License-Secret-Key*   "SECRET_KEY_UNIK_2026")  ;; Secret key unik per program
(setq *License-Dialog-Title* "Aktivasi Nama Program")
(setq *License-Price-Text*   "Harga: Rp 200.000 (Lifetime)")
(setq *License-Developer-Name* "Nama Anda - Perusahaan")
(setq *License-LinkedIn-URL* "https://linkedin.com/in/profil-anda")
(setq *TemanQRIS-Description* "Aktivasi Nama Program Pro")
(setq *TemanQRIS-Amount* 200000)
```

### 3. Implementasikan Fungsi Program

```lisp
(defun C:NAMA-PROGRAM (/ is_pro)
  (vl-load-com)
  (Show-License-Splash "Nama Program Pro" "1.0")
  (setq is_pro (Check-Is-Pro))
  ;; ... kode program Anda ...
  (princ)
)
```

---

## 📋 Referensi Fungsi Lengkap

### `LicenseManager.lsp` — Entry Point

| Fungsi | Parameter | Return | Deskripsi |
|--------|-----------|--------|-----------|
| `(Initialize-License-System)` | - | Association list | Inisialisasi sistem |
| `(Check-Is-Pro)` | - | T / nil | Cek status PRO |
| `(Get-Software-ID)` | - | String | Ambil Software ID |
| `(Get-License-Status-Text)` | - | String | Teks status lisensi |
| `(Show-License-Dialog)` | - | nil | Dialog aktivasi |
| `(Show-Manual-Activation-Dialog)` | - | nil | Dialog aktivasi manual |
| `(Show-Help-Dialog name ver)` | String, String | nil | Dialog panduan |
| `(Require-License-Pro feature)` | String | T / nil | Guard fitur PRO |
| `(Show-License-Splash name ver)` | String, String | nil | Splash screen |

**AutoCAD Commands:**
- `LICENSEINFO` — Dialog lisensi/aktivasi
- `LICENSESTATUS` — Status lisensi di command line
- `RESETLICENSE` — Reset ke Trial *(hanya aktif saat Dev-Mode = T)*
- `TESTNOTIF` — Test kirim notifikasi Telegram *(hanya aktif saat Dev-Mode = T)*

---

### `PaymentModule.lsp` — Pembayaran QRIS

| Fungsi | Parameter | Return | Deskripsi |
|--------|-----------|--------|-----------|
| `(Generate-Payment-Link soft_id)` | String | String URL / nil | Generate link QRIS |
| `(Check-Payment-Status order_id)` | String | "PAID"/"PENDING"/"EXPIRED"/"ERROR" | Cek status bayar |
| `(Process-Payment-Flow soft_id)` | String | T / nil | Generate link + buka browser |
| `(Verify-and-Activate m_id s_id)` | String, String | T / nil | Verifikasi + aktifkan lisensi |

**API TemanQRIS yang digunakan:**
- `POST /payment-link` — Buat payment link
- `GET /orders/:orderId` — Cek status order

---

### `TelegramNotifier.lsp` — Notifikasi Telegram

| Fungsi | Parameter | Return | Deskripsi |
|--------|-----------|--------|-----------|
| `(Send-Telegram-Message msg)` | String | T / nil | Kirim pesan teks |
| `(Send-Telegram-Notification type data)` | String, List | T / nil | Kirim notifikasi terformat |
| `(Send-New-Order-Notification s_id status)` | String, String | T / nil | Notifikasi order baru |
| `(Send-Payment-Success-Notification s_id)` | String | T / nil | Notifikasi bayar sukses |
| `(Send-Manual-Activation-With-Code s_id m_id code)` | String, String, String | T / nil | Notifikasi aktivasi manual + kode |
| `(Send-Trial-Expired-Notification s_id count)` | String, Integer | T / nil | Notifikasi trial habis |
| `(Send-Usage-Report s_id dwg count is_pro)` | String, String, Int, Bool | T / nil | Laporan penggunaan (silent) |
| `(Test-Telegram-Connection)` | - | T / nil | Test koneksi bot |

**Tipe Notifikasi:**
- `"NEW_ORDER"` — Pesanan baru masuk
- `"PAYMENT_SUCCESS"` — Pembayaran berhasil
- `"PAYMENT_PENDING"` — Menunggu pembayaran
- `"ACTIVATION_MANUAL"` — Aktivasi manual + kode
- `"TRIAL_EXPIRED"` — Trial habis
- `"USAGE_REPORT"` — Laporan penggunaan (silent)
- `"ERROR"` — Error terjadi

---

### `LicenseUtils.lsp` — Helper Functions

| Fungsi | Parameter | Return | Deskripsi |
|--------|-----------|--------|-----------|
| `(Get-Machine-ID)` | - | String | BIOS Serial Number |
| `(Get-Computer-Name)` | - | String | Hostname Windows |
| `(Get-Windows-Username)` | - | String | Username Windows |
| `(Generate-Software-ID m_id)` | String | String | Software ID terenkripsi |
| `(Generate-License-Key m_id)` | String | String | License key hash |
| `(Simple-Hash str)` | String | String | Hash sederhana |
| `(Check-License-File m_id)` | String | T / nil | Validasi file lisensi |
| `(Save-License-Key key)` | String | T / nil | Simpan key ke file |
| `(URL-Encode str)` | String | String | Encode untuk URL |
| `(Telegram-URL-Encode str)` | String | String | Encode khusus Telegram |

---

## ⚙️ Konfigurasi (`Config.lsp`)

```lisp
;; MODE DEVELOPMENT (T = testing, nil = production)
(setq *Dev-Mode* nil)           ;; WAJIB nil saat distribusi ke user

;; API TemanQRIS
(setq *TemanQRIS-API-Key* "API_KEY_ANDA")
(setq *TemanQRIS-Amount* 200000)          ;; Nominal dalam Rupiah
(setq *TemanQRIS-Description* "Aktivasi Pro License")

;; Telegram Bot
(setq *Telegram-Bot-Token* "BOT_TOKEN_ANDA")
(setq *Telegram-Chat-ID* "CHAT_ID_ANDA")

;; Lisensi
(setq *License-Secret-Key* "SECRET_KEY_UNIK_ANDA")  ;; GANTI per program!
(setq *License-File-Name* "sysLicense.cfg")
(setq *License-Trial-Limit* 30)

;; UI
(setq *License-Developer-Name* "Nama Anda")
(setq *License-Price-Text* "Harga: Rp 200.000 (Lifetime)")
(setq *License-LinkedIn-URL* "https://linkedin.com/in/profil-anda")
```

> ⚠️ **PENTING:** Ganti `*License-Secret-Key*` dengan string unik untuk setiap program berbeda!

---

## 🔄 Alur Kerja Sistem

```
User Jalankan Program
        │
        ▼
(Load LicenseManager.lsp)
        │
        ▼
(Initialize-License-System)
  ├── Get-Machine-ID (BIOS Serial)
  ├── Generate-Software-ID
  └── Check-License-File
        │
        ├── File valid ──► Status: PRO ──► Jalankan fitur penuh
        │
        └── File tidak ada ──► Status: TRIAL
                │
                ▼
        (Show-License-Dialog)
          ├── [1] LinkedIn ──► Buka browser
          │
          ├── [2] Panduan + Beli QRIS
          │     └── Show-Purchase-Guide-Dialog
          │           └── Process-Payment-Flow ──► Buka browser QRIS
          │                 └── Show-Verify-Dialog
          │                       └── Verify-and-Activate
          │                             ├── PAID ──► Save-License-Key
          │                             │              └── Send-Payment-Success (Telegram)
          │                             └── PENDING ──► Alert "Belum bayar"
          │
          ├── [3] Verifikasi Langsung ──► Verify-and-Activate
          │
          └── [4] Aktivasi Manual
                └── Generate-License-Key
                      └── Send-Manual-Activation-With-Code (Telegram + kode)
                            └── User input kode ──► Save-License-Key
```

---

## 🔒 Keamanan

| Aspek | Implementasi |
|-------|-------------|
| Machine ID | BIOS Serial Number (unik per hardware) |
| License Key | `Simple-Hash(MachineID + SecretKey)` |
| Trial Guard | Hitung total existing label, bukan hanya sesi ini |
| Dev Commands | `RESETLICENSE` & `TESTNOTIF` hanya aktif saat `*Dev-Mode* T` |
| File Lisensi | Disimpan di folder kerja AutoCAD |

---

## 🧪 Mode Testing vs Production

### Aktifkan Mode Testing

Edit [`Config.lsp`](Config.lsp):
```lisp
(setq *Dev-Mode* T)  ;; Aktifkan testing
```

Efek:
- Nominal otomatis Rp 5.000
- Command `RESETLICENSE` aktif
- Command `TESTNOTIF` aktif
- Indikator "DEV MODE" di splash screen

### Kembali ke Production

```lisp
(setq *Dev-Mode* nil)  ;; Production mode
```

---

## 📋 Checklist Distribusi ke User

```
[ ] Config.lsp: (setq *Dev-Mode* nil)
[ ] Config.lsp: (setq *TemanQRIS-Amount* 200000)
[ ] Hapus file sysFTTH.cfg (atau sysNamaProgram.cfg) dari folder distribusi
[ ] Test sekali dengan Dev-Mode nil sebelum distribusi
[ ] Pastikan semua file modul ada di folder yang sama
```

---

## 📝 Contoh Integrasi Minimal

```lisp
;; MY-TOOL.LSP - Contoh LSP dengan License Module

;; Load modul
(if (findfile "LicenseManager.lsp") (load (findfile "LicenseManager.lsp")))

;; Konfigurasi khusus program ini
(setq *License-File-Name* "sysMyTool.cfg")
(setq *License-Secret-Key* "MYTOOL_SECRET_2026")
(setq *License-Dialog-Title* "Aktivasi My Tool Pro")
(setq *TemanQRIS-Amount* 200000)
(Initialize-License-System)

;; Command utama
(defun C:MY-TOOL (/ is_pro count)
  (vl-load-com)
  (Show-License-Splash "My Tool Pro" "1.0")
  (setq is_pro (Check-Is-Pro))
  (setq count 0)

  (while (< count 1000)
    ;; Cek batas trial
    (if (and (not is_pro) (>= count *License-Trial-Limit*))
      (progn (Show-License-Dialog) (exit))
    )
    ;; ... proses Anda ...
    (setq count (1+ count))
  )

  ;; Laporan silent ke Telegram
  (vl-catch-all-apply
    '(lambda ()
       (Send-Usage-Report *License-Software-ID*
         (vl-filename-base (getvar "DWGNAME")) count is_pro)
    )
  )

  (princ (strcat "\nSelesai! " (itoa count) " item."))
  (princ)
)
```

---

## 🔧 Integrasi dengan FTTH Master Placer (Placer.lsp)

[`Placer.lsp`](Placer.lsp) adalah implementasi referensi lengkap yang menggunakan semua fitur modul ini.

### Perubahan dari v5.1 ke v5.2

| Fungsi Lama (Inline) | Fungsi Baru (Modul) |
|----------------------|---------------------|
| `(get-machine-id)` | `(Get-Machine-ID)` |
| `(scramble-id id)` | `(Generate-Software-ID id)` |
| `(check-license-status id)` | `(Check-License-File id)` |
| `(c:PLACER_INFO)` | `(Show-License-Dialog)` |
| `(Generate-Payment-Pro id)` | `(Process-Payment-Flow id)` |
| `(Verify-Activation m s)` | `(Verify-and-Activate m s)` |
| `(Send-Telegram msg)` | `(Send-Telegram-Message msg)` |
| `(vl-hash-sha str)` | `(Simple-Hash str)` |

### Commands FTTH Master Placer

| Command | Fungsi |
|---------|--------|
| `PLACER` | Jalankan program utama |
| `PLACERHELP` | Panduan penggunaan |
| `LICENSEINFO` | Dialog lisensi/aktivasi |
| `LICENSESTATUS` | Cek status lisensi |
| `AKTIVASI` | Aktivasi manual dengan kode |

---

## 📞 Kontak & Support

- **Developer:** Syaiful Wachid
- **Company:** Fiberhome Indonesia
- **LinkedIn:** [syaiful-wachid-5373n](https://www.linkedin.com/in/syaiful-wachid-5373n/)
- **Telegram Notifikasi:** Wildan (ID: 247653844)

---

*FTTH License Library v1.0 — Reusable License Module for AutoLISP*
*Dibuat dengan sepenuh hati untuk komunitas FTTx Indonesia*
