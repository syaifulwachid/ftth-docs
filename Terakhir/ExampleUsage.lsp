;; ============================================================================
;; EXAMPLEUSAGE.LSP - Template Integrasi License Module untuk Proyek Baru
;; ============================================================================
;; Salin file ini sebagai titik awal untuk setiap LSP baru Anda.
;; Ganti semua bagian bertanda [GANTI INI] sesuai kebutuhan proyek.
;;
;; LANGKAH INTEGRASI:
;; 1. Copy semua file modul ke folder AutoCAD Support Path
;; 2. Salin template ini dan rename sesuai nama program Anda
;; 3. Ganti konfigurasi di bagian SETUP PROYEK di bawah
;; 4. Implementasikan fungsi core program Anda
;; ============================================================================

;; ============================================================================
;; SETUP PROYEK - Edit bagian ini untuk setiap proyek baru
;; ============================================================================

;; Load License Module (wajib ada di AutoCAD Support Path)
(if (findfile "LicenseManager.lsp")
  (load (findfile "LicenseManager.lsp"))
  (alert "ERROR: LicenseManager.lsp tidak ditemukan!\nPastikan semua file modul ada di AutoCAD Support Path.")
)

;; Override konfigurasi khusus untuk proyek ini
;; (Jalankan SETELAH load LicenseManager.lsp)
(setq *License-File-Name*    "sysNamaProgram.cfg")    ;; [GANTI INI] Nama file lisensi unik
(setq *License-Secret-Key*   "SECRET_KEY_UNIK_2026")  ;; [GANTI INI] Secret key unik per program
(setq *License-Dialog-Title* "Aktivasi Nama Program")  ;; [GANTI INI] Judul dialog
(setq *License-Price-Text*   "Harga: Rp 200.000 (Lifetime)") ;; [GANTI INI] Teks harga
(setq *License-Developer-Name* "Nama Anda - Perusahaan")     ;; [GANTI INI] Nama developer
(setq *License-LinkedIn-URL* "https://linkedin.com/in/profil-anda") ;; [GANTI INI] LinkedIn
(setq *TemanQRIS-Description* "Aktivasi Nama Program Pro")   ;; [GANTI INI] Deskripsi pembayaran
(setq *TemanQRIS-Amount* 200000)                             ;; [GANTI INI] Nominal (Rp)

;; Re-inisialisasi dengan konfigurasi yang sudah diupdate
(Initialize-License-System)

;; ============================================================================
;; COMMAND UTAMA PROGRAM
;; Ganti NAMA-PROGRAM dengan nama command program Anda
;; ============================================================================

(defun C:NAMA-PROGRAM (/ is_pro)
  (vl-load-com)

  ;; STEP 1: Tampilkan splash screen dengan status lisensi
  (Show-License-Splash "Nama Program Pro" "1.0")  ;; [GANTI INI]

  ;; STEP 2: Cek status lisensi
  (setq is_pro (Check-Is-Pro))

  ;; STEP 3: Jalankan program dengan logika trial/pro
  (if is_pro
    ;; Mode PRO - tidak ada batasan
    (Run-Full-Feature)
    ;; Mode Trial - ada batasan
    (Run-Trial-Feature)
  )

  (princ)
)

;; ============================================================================
;; IMPLEMENTASI FITUR PROGRAM
;; Ganti isi fungsi ini dengan kode program Anda
;; ============================================================================

(defun Run-Full-Feature (/)
  ;; [GANTI INI] Implementasi fitur lengkap PRO
  (princ "\n[PRO] Menjalankan fitur lengkap...")

  ;; Contoh: loop tanpa batasan
  (setq count 0)
  (while (< count 1000)  ;; Tidak ada batasan untuk PRO
    ;; ... proses Anda di sini ...
    (setq count (1+ count))
  )

  ;; Kirim laporan penggunaan silent ke Telegram (untuk research)
  (vl-catch-all-apply
    '(lambda ()
       (Send-Usage-Report
         *License-Software-ID*
         (vl-filename-base (getvar "DWGNAME"))
         count
         T  ;; is_pro = T
       )
    )
  )

  (princ (strcat "\n[PRO] Selesai! " (itoa count) " item diproses."))
  (princ)
)

(defun Run-Trial-Feature (/ count stop_trial trial_limit)
  ;; [GANTI INI] Implementasi fitur trial dengan batasan
  (setq trial_limit *License-Trial-Limit*)  ;; Default 30 dari Config.lsp
  (setq count 0)
  (setq stop_trial nil)

  (princ (strcat "\n[TRIAL] Batas: " (itoa trial_limit) " item."))

  (while (and (< count 1000) (not stop_trial))
    ;; Cek batas trial
    (if (>= count trial_limit)
      (progn
        (setq stop_trial T)
        ;; Tampilkan dialog lisensi saat trial habis
        (Show-License-Dialog)
      )
      (progn
        ;; ... proses Anda di sini ...
        (setq count (1+ count))
      )
    )
  )

  ;; Kirim laporan penggunaan silent ke Telegram
  (vl-catch-all-apply
    '(lambda ()
       (Send-Usage-Report
         *License-Software-ID*
         (vl-filename-base (getvar "DWGNAME"))
         count
         nil  ;; is_pro = nil
       )
    )
  )

  (if stop_trial
    (princ (strcat "\n[TRIAL] Batas trial tercapai. " (itoa count) " item diproses."))
    (princ (strcat "\n[TRIAL] Selesai! " (itoa count) " item diproses."))
  )
  (princ)
)

;; ============================================================================
;; COMMAND TAMBAHAN (Opsional)
;; ============================================================================

;; Command untuk membuka dialog lisensi
(defun C:NAMA-PROGRAM-INFO (/)
  (Show-License-Dialog)
  (princ)
)

;; Command untuk panduan penggunaan
(defun C:NAMA-PROGRAM-HELP (/)
  (Show-Help-Dialog "Nama Program Pro" "1.0")  ;; [GANTI INI]
  (princ)
)

;; Command aktivasi manual
(defun C:NAMA-PROGRAM-AKTIVASI (/)
  (Show-Manual-Activation-Dialog)
  (princ)
)

;; ============================================================================
;; CONTOH: GUARD FITUR PREMIUM
;; Gunakan Require-License-Pro untuk membatasi fitur tertentu
;; ============================================================================

(defun C:FITUR-PREMIUM (/)
  (vl-load-com)
  ;; Cek lisensi - jika belum PRO, dialog aktivasi muncul otomatis
  (if (Require-License-Pro "Fitur Premium")  ;; [GANTI INI] nama fitur
    (progn
      (princ "\n[PRO] Menjalankan fitur premium...")
      ;; ... kode fitur premium Anda di sini ...
    )
    (princ "\n[TRIAL] Fitur ini memerlukan lisensi PRO.")
  )
  (princ)
)

;; ============================================================================
;; CONTOH: KIRIM NOTIFIKASI CUSTOM KE TELEGRAM
;; ============================================================================

(defun C:TEST-NOTIF-CUSTOM (/)
  ;; Kirim notifikasi dengan data custom
  (Send-Telegram-Notification "NEW_ORDER"
    (list
      (cons "Program" "Nama Program Pro")
      (cons "Software ID" (Get-Software-ID))
      (cons "PC Name" (Get-Computer-Name))
      (cons "Windows User" (Get-Windows-Username))
      (cons "Action" "Test notifikasi custom")
    )
  )
  (princ "\n[Telegram] Notifikasi custom terkirim!")
  (princ)
)

;; ============================================================================
;; STARTUP MESSAGE
;; ============================================================================
(princ "\n================================================")
(princ "\n   Nama Program Pro v1.0 Loaded")           ;; [GANTI INI]
(princ "\n   Commands: NAMA-PROGRAM, NAMA-PROGRAM-HELP")  ;; [GANTI INI]
(princ "\n================================================")
(princ)
