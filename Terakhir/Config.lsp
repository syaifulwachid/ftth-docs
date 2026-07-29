;; ============================================================================
;; CONFIG.LSP - Konfigurasi Global untuk License Module
;; ============================================================================
;; File ini berisi semua konfigurasi API keys, URL, dan pengaturan.
;; Edit file ini sesuai kebutuhan project Anda.
;;
;; !! CHECKLIST SEBELUM DISTRIBUSI KE USER !!
;; [ ] *Dev-Mode* diset ke nil (bukan T)
;; [ ] *TemanQRIS-Amount* diset ke 200000
;; [ ] File sysFTTH.cfg dihapus dari folder distribusi
;; ============================================================================

;; ----------------------------------------------------------------------------
;; MODE DEVELOPMENT / TESTING
;; Set ke T saat testing, set ke nil saat production/distribusi
;; ----------------------------------------------------------------------------
(setq *Dev-Mode* nil)  ;; Production mode - set ke T hanya saat testing

;; ----------------------------------------------------------------------------
;; KONFIGURASI API TEMANQRIS
;; ----------------------------------------------------------------------------
(setq *TemanQRIS-API-Key* "6a0e769df6f0447d99c361588b17e5ebaeb6fc8a0d08472eb52fa1ef299547c1")
(setq *TemanQRIS-BaseURL* "https://temanqris.com/api/qris")

;; Nominal otomatis: Rp 5.000 saat Dev-Mode, Rp 200.000 saat production
(if *Dev-Mode*
  (setq *TemanQRIS-Amount* 5000)       ;; Testing: Rp 5.000
  (setq *TemanQRIS-Amount* 200000)     ;; Production: Rp 200.000
)

(setq *TemanQRIS-Description* "Aktivasi Pro License") ;; Deskripsi pembayaran

;; ----------------------------------------------------------------------------
;; KONFIGURASI TELEGRAM BOT
;; ----------------------------------------------------------------------------
(setq *Telegram-Bot-Token* "8600273064:AAGVILrDf9lfUkWydeVELJXWAwnxXGgLDKg")
(setq *Telegram-Chat-ID* "247653844")                 ;; ID chat penerima notifikasi
(setq *Telegram-BaseURL* "https://api.telegram.org/bot")

;; ----------------------------------------------------------------------------
;; KONFIGURASI LISENSI
;; ----------------------------------------------------------------------------
(setq *License-Secret-Key* "FIBERHOME_PRO_202626")      ;; Secret key untuk generate hash
(setq *License-File-Name* "sysLicense.cfg")           ;; Nama file penyimpanan lisensi
(setq *License-Prefix* "FT-")                         ;; Prefix untuk Software ID
(setq *License-Trial-Limit* 30)                       ;; Batas trial (jika digunakan)

;; ----------------------------------------------------------------------------
;; KONFIGURASI WEBHOOK (Opsional - untuk verifikasi server-side)
;; ----------------------------------------------------------------------------
(setq *Webhook-Secret-Key* "3ef046575815f481a1021cd1a4562d1b5c7b3f6fae4ae8812463dfa26a0e6004")

;; ----------------------------------------------------------------------------
;; KONFIGURASI UI/DIALOG
;; ----------------------------------------------------------------------------
(setq *License-Dialog-Title* "Aktivasi License Pro")
(setq *License-Price-Text* "Harga: Rp 200.000 (Lifetime)")
(setq *License-Developer-Name* "Syaiful Wachid - Fiberhome Indonesia")
(setq *License-LinkedIn-URL* "https://www.linkedin.com/in/syaiful-wachid-5373n/")

(princ "\n[License Module] Konfigurasi dimuat.")
(princ)
