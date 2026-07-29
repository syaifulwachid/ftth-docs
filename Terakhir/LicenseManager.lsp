;; ============================================================================
;; LICENSEMANAGER.LSP - Modul Manajemen Lisensi Utama (Entry Point)
;; ============================================================================
;; Modul ini adalah file utama yang perlu di-load oleh LSP Anda.
;; Secara otomatis akan me-load semua dependensi (Config, Utils, Telegram, Payment).
;;
;; CARA PENGGUNAAN DASAR:
;;   1. Load modul: (load "LicenseManager.lsp")
;;   2. Cek lisensi: (Check-Is-Pro) -> return T atau nil
;;   3. Tampilkan splash: (Show-License-Splash "Nama App" "1.0")
;;   4. Buka dialog: (Show-License-Dialog)
;;
;; COMMANDS AUTOCAD:
;; - LICENSEINFO    : Buka dialog lisensi
;; - LICENSESTATUS  : Tampilkan status lisensi di command line
;; - PLACERHELP     : Panduan penggunaan program
;; ============================================================================

;; ============================================================================
;; SISTEM LOAD MODUL - Deteksi path folder secara otomatis
;; ============================================================================

(if (not (boundp '*LicenseModule-Path*))
  (progn
    (setq *LicenseModule-Path* nil)

    ;; Cari LicenseManager.fas terlebih dahulu (mode distribusi)
    (if (findfile "LicenseManager.fas")
      (setq *LicenseModule-Path*
        (vl-filename-directory (findfile "LicenseManager.fas"))
      )
    )

    ;; Fallback: cari LicenseManager.lsp (mode development)
    (if (and (not *LicenseModule-Path*) (findfile "LicenseManager.lsp"))
      (setq *LicenseModule-Path*
        (vl-filename-directory (findfile "LicenseManager.lsp"))
      )
    )

    ;; Fallback terakhir: folder drawing aktif
    (if (not *LicenseModule-Path*)
      (setq *LicenseModule-Path* (getvar "DWGPREFIX"))
    )

    ;; Pastikan path diakhiri backslash
    (if *LicenseModule-Path*
      (if (not (= (substr *LicenseModule-Path* (strlen *LicenseModule-Path*)) "\\"))
        (setq *LicenseModule-Path* (strcat *LicenseModule-Path* "\\"))
      )
    )
  )
)

(defun Load-Module-File (filename / base_name fas_name lsp_name full_fas full_lsp found_path)
  ;; Coba load .fas terlebih dahulu, fallback ke .lsp
  (setq base_name (vl-filename-base filename))
  (setq fas_name (strcat base_name ".fas"))
  (setq lsp_name (strcat base_name ".lsp"))
  (setq found_path nil)

  ;; Prioritas 1: .fas di folder modul
  (setq full_fas (strcat *LicenseModule-Path* fas_name))
  (if (findfile full_fas) (setq found_path full_fas))

  ;; Prioritas 2: .fas di AutoCAD support path
  (if (and (not found_path) (findfile fas_name))
    (setq found_path (findfile fas_name))
  )

  ;; Prioritas 3: .lsp di folder modul
  (setq full_lsp (strcat *LicenseModule-Path* lsp_name))
  (if (and (not found_path) (findfile full_lsp))
    (setq found_path full_lsp)
  )

  ;; Prioritas 4: .lsp di AutoCAD support path
  (if (and (not found_path) (findfile lsp_name))
    (setq found_path (findfile lsp_name))
  )

  (if found_path
    (load found_path)
    (princ (strcat "\n[License Module] WARNING: " base_name " tidak ditemukan."))
  )
)

(Load-Module-File "Config.lsp")
(Load-Module-File "LicenseUtils.lsp")
(Load-Module-File "TelegramNotifier.lsp")
(Load-Module-File "PaymentModule.lsp")

;; ----------------------------------------------------------------------------
;; VARIABEL GLOBAL STATE
;; ----------------------------------------------------------------------------
(setq *License-Machine-ID* nil)
(setq *License-Software-ID* nil)
(setq *License-Is-Pro* nil)
(setq *License-Initialized* nil)

;; ============================================================================
;; FUNGSI INTI LISENSI
;; ============================================================================

(defun Initialize-License-System (/)
  (setq *License-Machine-ID* (Get-Machine-ID))
  (setq *License-Software-ID* (Generate-Software-ID *License-Machine-ID*))
  (setq *License-Is-Pro* (Check-License-File *License-Machine-ID*))
  (setq *License-Initialized* T)
  (list
    (cons "MachineID" *License-Machine-ID*)
    (cons "SoftwareID" *License-Software-ID*)
    (cons "IsPro" (if *License-Is-Pro* "true" "false"))
  )
)

(defun Check-Is-Pro (/)
  (if (not *License-Initialized*) (Initialize-License-System))
  *License-Is-Pro*
)

(defun Get-License-Status-Text (/)
  (if (Check-Is-Pro)
    "[PRO VERSION - ACTIVE]"
    (strcat "Software ID: " (Get-Software-ID) " (TRIAL - Maks 30 item)")
  )
)

(defun Get-Software-ID (/)
  (if (not *License-Initialized*) (Initialize-License-System))
  *License-Software-ID*
)

;; ============================================================================
;; DIALOG: PANDUAN PEMBELIAN (Step-by-step)
;; ============================================================================

(defun Show-Purchase-Guide-Dialog (/ dcl_file f dcl_id result)
  (setq dcl_file (vl-filename-mktemp "guide_dlg.dcl"))
  (setq f (open dcl_file "w"))

  (write-line "guide_dlg : dialog {" f)
  (write-line "  label = \"Panduan Pembelian Lisensi PRO\";" f)
  (write-line "  : column {" f)

  ;; Pesan psikologis / motivasi
  (write-line "    : boxed_column { label = \"Mengapa Harus PRO?\";" f)
  (write-line "      : text { label = \"Tools ini dibuat dengan sepenuh hati untuk membantu\"; }" f)
  (write-line "      : text { label = \"teman-teman yang bekerja di bidang FTTx agar bisa\"; }" f)
  (write-line "      : text { label = \"bekerja lebih cepat, rapi, dan minim human error.\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"Hasil pembelian Anda digunakan untuk:\"; }" f)
  (write-line "      : text { label = \"  - Biaya internet, kopi & rokok teman ngoding\"; }" f)
  (write-line "      : text { label = \"  - Biaya riset & pengembangan fitur baru\"; }" f)
  (write-line "      : text { label = \"  - Menghargai waktu & dedikasi developer\"; }" f)
  (write-line "      : text { label = \"  - Menjamin support & update berkelanjutan\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"Dengan membeli lisensi, Anda turut mendukung\"; }" f)
  (write-line "      : text { label = \"ekosistem tools FTTx yang terus berkembang!\"; }" f)
  (write-line "    }" f)
  (write-line "    spacer;" f)

  ;; Step-by-step panduan
  (write-line "    : boxed_column { label = \"Langkah Pembelian (Ikuti Urutan Ini):\";" f)
  (write-line "      : text { label = \"STEP 1: Klik tombol BELI LISENSI (QRIS)\"; }" f)
  (write-line "      : text { label = \"        Browser akan terbuka dengan halaman QRIS.\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"STEP 2: Scan QRIS & selesaikan pembayaran.\"; }" f)
  (write-line "      : text { label = \"        Nominal: Rp 200.000 (Lifetime License).\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"STEP 3: Kembali ke AutoCAD, buka LICENSEINFO.\"; }" f)
  (write-line "      : text { label = \"        Klik VERIFIKASI PEMBAYARAN.\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"STEP 4: Lisensi PRO aktif otomatis!\"; }" f)
  (write-line "      : text { label = \"        Tidak perlu restart AutoCAD.\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"ALTERNATIF: Hubungi admin via LinkedIn untuk\"; }" f)
  (write-line "      : text { label = \"aktivasi manual jika ada kendala.\"; }" f)
  (write-line "    }" f)
  (write-line "    spacer;" f)
  (write-line "    : row {" f)
  (write-line "      : button { label = \"Lanjut ke Pembelian\"; key = \"buy\"; is_default = true; width = 25; }" f)
  (write-line "      : button { label = \"Tutup\"; key = \"cancel\"; width = 15; }" f)
  (write-line "    }" f)
  (write-line "  }" f)
  (write-line "}" f)
  (close f)

  (setq dcl_id (load_dialog dcl_file))
  (new_dialog "guide_dlg" dcl_id)
  (action_tile "buy"    "(done_dialog 1)")
  (action_tile "cancel" "(done_dialog 0)")
  (setq result (start_dialog))
  (unload_dialog dcl_id)
  (vl-file-delete dcl_file)
  result
)

;; ============================================================================
;; DIALOG: VERIFIKASI PEMBAYARAN (Setelah buka browser)
;; ============================================================================

(defun Show-Verify-Dialog (/ dcl_file f dcl_id result)
  (setq dcl_file (vl-filename-mktemp "verify_dlg.dcl"))
  (setq f (open dcl_file "w"))

  (write-line "verify_dlg : dialog {" f)
  (write-line "  label = \"Verifikasi Pembayaran\";" f)
  (write-line "  : column {" f)
  (write-line "    : boxed_column { label = \"Status Pembayaran\";" f)
  (write-line "      : text { label = \"Browser sudah terbuka dengan halaman QRIS.\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"Setelah selesai scan & bayar QRIS,\"; }" f)
  (write-line "      : text { label = \"klik tombol VERIFIKASI di bawah ini.\"; }" f)
  (write-line "      spacer;" f)
  (write-line "      : text { label = \"Lisensi akan aktif otomatis jika pembayaran\"; }" f)
  (write-line "      : text { label = \"sudah dikonfirmasi oleh sistem.\"; }" f)
  (write-line "    }" f)
  (write-line "    spacer;" f)
  (write-line "    : row {" f)
  (write-line "      : button { label = \"VERIFIKASI PEMBAYARAN\"; key = \"verify\"; is_default = true; width = 25; }" f)
  (write-line "      : button { label = \"Nanti Saja\"; key = \"cancel\"; width = 15; }" f)
  (write-line "    }" f)
  (write-line "  }" f)
  (write-line "}" f)
  (close f)

  (setq dcl_id (load_dialog dcl_file))
  (new_dialog "verify_dlg" dcl_id)
  (action_tile "verify" "(done_dialog 1)")
  (action_tile "cancel" "(done_dialog 0)")
  (setq result (start_dialog))
  (unload_dialog dcl_id)
  (vl-file-delete dcl_file)
  result
)

;; ============================================================================
;; DIALOG: LISENSI UTAMA
;; ============================================================================

(defun Show-License-Dialog (/ dcl_file f dcl_id result flow_result)
  (if (not *License-Initialized*) (Initialize-License-System))

  (setq dcl_file (vl-filename-mktemp "license_dlg.dcl"))
  (setq f (open dcl_file "w"))

  (write-line "license_dlg : dialog {" f)
  (write-line (strcat "  label = \"" *License-Dialog-Title* "\";") f)
  (write-line "  : column {" f)

  ;; Developer Profile
  (write-line "    : boxed_column { label = \"Developer\";" f)
  (write-line (strcat "      : text { label = \"" *License-Developer-Name* "\"; }") f)
  (write-line "      : button { label = \"LinkedIn Profile\"; key = \"linkedin\"; }" f)
  (write-line "    }" f)
  (write-line "    spacer;" f)

  ;; Status & Lisensi
  (write-line "    : boxed_column { label = \"Status Lisensi\";" f)
  (write-line (strcat "      : text { label = \"Software ID : " *License-Software-ID* "\"; }") f)
  (write-line (strcat "      : text { label = \"" *License-Price-Text* "\"; }") f)
  (write-line "      spacer;" f)

  (if *License-Is-Pro*
    (progn
      (write-line "      : text { label = \"Status : PRO VERSION - AKTIF\"; }" f)
      (write-line "      : text { label = \"Terima kasih telah mendukung developer!\"; }" f)
    )
    (progn
      (write-line "      : text { label = \"Status : TRIAL (Maks 30 item)\"; }" f)
      (write-line "      spacer;" f)
      (write-line "      : button { label = \"1. PANDUAN & BELI LISENSI (QRIS)\"; key = \"guide\"; is_default = true; }" f)
      (write-line "      : button { label = \"2. VERIFIKASI PEMBAYARAN\"; key = \"verify\"; }" f)
      (write-line "      : button { label = \"3. AKTIVASI MANUAL (Kode dari Admin)\"; key = \"manual\"; }" f)
    )
  )

  (write-line "    }" f)
  (write-line "    spacer;" f)
  (write-line "    ok_only;" f)
  (write-line "  }" f)
  (write-line "}" f)
  (close f)

  (setq dcl_id (load_dialog dcl_file))
  (new_dialog "license_dlg" dcl_id)
  (action_tile "linkedin" "(done_dialog 1)")
  (action_tile "guide"    "(done_dialog 2)")
  (action_tile "verify"   "(done_dialog 3)")
  (action_tile "manual"   "(done_dialog 4)")
  (setq result (start_dialog))
  (unload_dialog dcl_id)
  (vl-file-delete dcl_file)

  (cond
    ;; LinkedIn
    ((= result 1)
     (startapp "explorer" *License-LinkedIn-URL*))

    ;; Panduan + Beli
    ((= result 2)
     (progn
       ;; Tampilkan panduan dulu
       (if (= (Show-Purchase-Guide-Dialog) 1)
         (progn
           ;; User klik "Lanjut ke Pembelian" - generate link BARU dan buka browser
           (setq flow_result (Process-Payment-Flow *License-Software-ID*))
           ;; Jika browser berhasil dibuka, tampilkan dialog verifikasi
           (if flow_result
             (if (= (Show-Verify-Dialog) 1)
               ;; User klik Verifikasi - cek status order terakhir
               (if (Verify-and-Activate *License-Machine-ID* *License-Software-ID*)
                 (setq *License-Is-Pro* T)
               )
             )
           )
         )
       )
     ))

    ;; Verifikasi langsung (tombol 2)
    ((= result 3)
     (progn
       ;; Cek apakah sudah PRO (file lisensi ada)
       (if (Check-License-File *License-Machine-ID*)
         (progn
           (setq *License-Is-Pro* T)
           (alert "Lisensi PRO Anda sudah AKTIF!\n\nTidak perlu verifikasi ulang.")
         )
         (progn
           ;; Cek apakah ada order terakhir yang bisa diverifikasi
           (if (findfile "sysLastOrder.tmp")
             (progn
               ;; Ada order terakhir - verifikasi
               (if (Verify-and-Activate *License-Machine-ID* *License-Software-ID*)
                 (setq *License-Is-Pro* T)
               )
             )
             (progn
               ;; Tidak ada order - minta user beli dulu
               (alert "Belum ada pembayaran yang bisa diverifikasi.\n\nSilakan klik:\n\"1. PANDUAN & BELI LISENSI (QRIS)\"\nterlebih dahulu.")
             )
           )
         )
       )
     ))

    ;; Aktivasi Manual
    ((= result 4)
     (Show-Manual-Activation-Dialog))
  )

  (princ)
)

;; ============================================================================
;; DIALOG: AKTIVASI MANUAL
;; ============================================================================

(defun Show-Manual-Activation-Dialog (/ key activation_code)
  ;; Generate kode aktivasi untuk machine ini
  (setq activation_code (Generate-License-Key *License-Machine-ID*))

  ;; Kirim notifikasi ke admin via Telegram DENGAN KODE AKTIVASI
  ;; Admin bisa langsung copy kode dan kirim balik ke user
  (Send-Manual-Activation-With-Code *License-Software-ID* *License-Machine-ID* activation_code)

  ;; Tampilkan info ke user
  (alert (strcat
    "AKTIVASI MANUAL\n\n"
    "Software ID Anda:\n"
    "  " *License-Software-ID* "\n\n"
    "Notifikasi sudah dikirim ke Admin.\n"
    "Admin akan segera mengirimkan Kode Aktivasi.\n\n"
    "Hubungi via LinkedIn jika belum ada respon:\n"
    *License-LinkedIn-URL*
  ))

  ;; Minta input kode dari user
  (setq key (getstring T "\nMasukkan Kode Aktivasi dari Admin: "))
  (if (= key activation_code)
    (progn
      (Save-License-Key key)
      (setq *License-Is-Pro* T)
      (alert "AKTIVASI BERHASIL!\n\nLisensi PRO Anda kini sudah aktif.\nTerima kasih telah mendukung developer!")
    )
    (alert "KODE SALAH!\n\nSilakan periksa kembali kode yang diberikan admin.")
  )
  (princ)
)

;; ============================================================================
;; DIALOG: HELP / PANDUAN PENGGUNAAN PROGRAM
;; ============================================================================

(defun Show-Help-Dialog (app_name app_version / dcl_file f dcl_id)
  (setq dcl_file (vl-filename-mktemp "help_dlg.dcl"))
  (setq f (open dcl_file "w"))

  (write-line "help_dlg : dialog {" f)
  (write-line (strcat "  label = \"Panduan " app_name " v" app_version "\";") f)
  (write-line "  : column {" f)

  (write-line "    : boxed_column { label = \"Cara Menggunakan Program\";" f)
  (write-line "      : text { label = \"LANGKAH 1: Ketik PLACER di command line AutoCAD.\"; }" f)
  (write-line "      : text { label = \"LANGKAH 2: Pilih MText/Text sebagai template label.\"; }" f)
  (write-line "      : text { label = \"LANGKAH 3: Masukkan awalan (contoh: NN-) & nomor awal.\"; }" f)
  (write-line "      : text { label = \"LANGKAH 4: Pilih basemap (LINE/LWPOLYLINE/POLYLINE).\"; }" f)
  (write-line "      : text { label = \"LANGKAH 5: Program otomatis menempatkan label!\"; }" f)
  (write-line "    }" f)
  (write-line "    spacer;" f)

  (write-line "    : boxed_column { label = \"Daftar Command\";" f)
  (write-line "      : text { label = \"PLACER       - Jalankan program utama\"; }" f)
  (write-line "      : text { label = \"LICENSEINFO  - Buka dialog lisensi/aktivasi\"; }" f)
  (write-line "      : text { label = \"LICENSESTATUS- Cek status lisensi\"; }" f)
  (write-line "      : text { label = \"AKTIVASI     - Aktivasi manual dengan kode\"; }" f)
  (write-line (strcat "      : text { label = \"PLACERHELP   - Panduan " app_name "\"; }") f)
  (write-line "    }" f)
  (write-line "    spacer;" f)

  (write-line "    : boxed_column { label = \"Perbedaan Trial vs PRO\";" f)
  (write-line "      : text { label = \"TRIAL : Maksimal 30 label per sesi\"; }" f)
  (write-line "      : text { label = \"PRO   : Tidak ada batasan, semua fitur aktif\"; }" f)
  (write-line "      : text { label = \"PRO   : Support & update seumur hidup\"; }" f)
  (write-line "      : text { label = \"PRO   : Harga Rp 200.000 (Lifetime)\"; }" f)
  (write-line "    }" f)
  (write-line "    spacer;" f)

  (write-line "    : boxed_column { label = \"Kontak & Support\";" f)
  (write-line "      : text { label = \"Developer : Syaiful Wachid\"; }" f)
  (write-line "      : text { label = \"Company   : Fiberhome Indonesia\"; }" f)
  (write-line "      : text { label = \"LinkedIn  : syaiful-wachid-5373n\"; }" f)
  (write-line "    }" f)
  (write-line "    spacer;" f)

  (write-line "    : row {" f)
  (write-line "      : button { label = \"Beli Lisensi PRO\"; key = \"buy\"; width = 20; }" f)
  (write-line "      : button { label = \"Tutup\"; key = \"close\"; is_default = true; width = 15; }" f)
  (write-line "    }" f)
  (write-line "  }" f)
  (write-line "}" f)
  (close f)

  (setq dcl_id (load_dialog dcl_file))
  (new_dialog "help_dlg" dcl_id)
  (action_tile "buy"   "(done_dialog 1)")
  (action_tile "close" "(done_dialog 0)")
  (setq result (start_dialog))
  (unload_dialog dcl_id)
  (vl-file-delete dcl_file)

  (if (= result 1) (Show-License-Dialog))
  (princ)
)

;; ============================================================================
;; FUNGSI UTILITAS
;; ============================================================================

(defun Require-License-Pro (feature_name /)
  (if (Check-Is-Pro)
    T
    (progn
      (princ (strcat "\n[License] Fitur \"" feature_name "\" memerlukan lisensi PRO."))
      (Show-License-Dialog)
      (Check-Is-Pro)
    )
  )
)

(defun Show-License-Splash (app_name app_version /)
  (if (not *License-Initialized*) (Initialize-License-System))
  (if (not (boundp '*Dev-Mode*)) (setq *Dev-Mode* nil))
  (princ "\n================================================")
  (princ (strcat "\n   " app_name))
  (princ (strcat "\n   Versi: " app_version))
  (if *Dev-Mode*
    (progn
      (princ "\n   *** DEV MODE - TESTING ***")
      (princ (strcat "\n   Nominal: Rp " (itoa *TemanQRIS-Amount*) " (Testing)"))
    )
  )
  (princ "\n------------------------------------------------")
  (princ (strcat "\n   " (Get-License-Status-Text)))
  (if (not *License-Is-Pro*)
    (princ "\n   Ketik LICENSEINFO untuk aktivasi PRO")
  )
  (princ "\n================================================")
  (princ)
)

;; ============================================================================
;; COMMANDS AUTOCAD
;; ============================================================================

(defun C:LICENSEINFO (/)
  (Show-License-Dialog)
  (princ)
)

(defun C:LICENSESTATUS (/)
  (if (not *License-Initialized*) (Initialize-License-System))
  (princ "\n================================================")
  (princ (strcat "\n  Machine ID  : " *License-Machine-ID*))
  (princ (strcat "\n  Software ID : " *License-Software-ID*))
  (princ (strcat "\n  Status      : " (if *License-Is-Pro* "PRO - AKTIF" "TRIAL (Maks 30 item)")))
  (princ "\n================================================")
  (princ)
)

;; ============================================================================
;; COMMAND: TESTNOTIF (DEV MODE - Test kirim notifikasi Telegram)
;; ============================================================================
(defun C:TESTNOTIF (/)
  (if (not (boundp '*Dev-Mode*)) (setq *Dev-Mode* nil))
  (if *Dev-Mode*
    (progn
      (princ "\n[DEV] Mengirim test notifikasi ke Telegram...")
      (if (not *License-Initialized*) (Initialize-License-System))
      ;; Test kirim semua format notifikasi
      (Send-Telegram-Notification "NEW_ORDER"
        (list
          (cons "Software ID" *License-Software-ID*)
          (cons "Nominal" (strcat "Rp " (itoa *TemanQRIS-Amount*)))
          (cons "Status" "Menunggu Pembayaran")
          (cons "Action" "Tunggu konfirmasi pembayaran")
        )
      )
      (princ "\n[DEV] Notifikasi NEW_ORDER terkirim.")
      (Send-Telegram-Notification "PAYMENT_SUCCESS"
        (list
          (cons "Software ID" *License-Software-ID*)
          (cons "Nominal" (strcat "Rp " (itoa *TemanQRIS-Amount*)))
          (cons "Status" "LUNAS - Lisensi PRO Aktif")
          (cons "Action" "Tidak perlu tindakan lanjut")
        )
      )
      (princ "\n[DEV] Notifikasi PAYMENT_SUCCESS terkirim.")
      (alert "[DEV] 2 notifikasi test sudah dikirim ke Telegram.\nCek bot Telegram Anda untuk melihat format baru.")
    )
    (princ "\n[INFO] Command TESTNOTIF tidak tersedia di production mode.")
  )
  (princ)
)

;; ============================================================================
;; COMMAND: RESETLICENSE
;; Deskripsi: Reset lisensi ke mode Trial (hanya aktif saat *Dev-Mode* = T)
;; Saat *Dev-Mode* = nil (production), command ini tidak berfungsi.
;; ============================================================================
(defun C:RESETLICENSE (/ f_path confirm)
  ;; Cek apakah dalam mode development
  (if (not (boundp '*Dev-Mode*)) (setq *Dev-Mode* nil))
  (if *Dev-Mode*
    (progn
      (setq confirm (getstring T "\n[DEV] Reset lisensi ke TRIAL? Ketik YES untuk konfirmasi: "))
      (if (= (strcase confirm) "YES")
        (progn
          (setq f_path (findfile *License-File-Name*))
          (if f_path
            (progn
              (vl-file-delete f_path)
              (setq *License-Is-Pro* nil)
              (setq *License-Initialized* nil)
              (Initialize-License-System)
              (princ "\n[DEV] Lisensi berhasil direset ke mode TRIAL.")
              (alert "[DEV MODE] Lisensi direset ke TRIAL.\n\nSilakan test ulang proses pembelian.")
            )
            (progn
              (princ "\n[DEV] File lisensi tidak ditemukan - sudah dalam mode TRIAL.")
              (alert "[DEV MODE] Sudah dalam mode TRIAL.\nFile lisensi tidak ditemukan.")
            )
          )
        )
        (princ "\n[DEV] Reset dibatalkan.")
      )
    )
    ;; Production mode - command tidak aktif
    (progn
      (princ "\n[INFO] Command RESETLICENSE tidak tersedia.")
    )
  )
  (princ)
)

;; Inisialisasi otomatis saat modul di-load
(Initialize-License-System)

(princ "\n[License Module] License Manager loaded successfully.")
(princ "\n[License Module] Commands: LICENSEINFO, LICENSESTATUS, RESETLICENSE")
(princ)
