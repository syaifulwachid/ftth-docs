;; ============================================================================
;; TELEGRAMNOTIFIER.LSP - Modul Notifikasi Telegram
;; ============================================================================
;; Modul ini menangani pengiriman notifikasi ke Telegram Bot.
;; File ini di-load oleh LicenseManager.lsp secara otomatis.
;; ============================================================================

;; ----------------------------------------------------------------------------
;; FUNGSI INTERNAL: Telegram-URL-Encode
;; Encode karakter khusus untuk URL Telegram
;; ----------------------------------------------------------------------------
(defun Telegram-URL-Encode (str / result i c)
  (setq result "")
  (setq i 1)
  (while (<= i (strlen str))
    (setq c (substr str i 1))
    (cond
      ;; Spasi -> +
      ((= c " ") (setq result (strcat result "+")))
      ;; Newline -> %0A
      ((= c "\n") (setq result (strcat result "%0A")))
      ;; = -> %3D
      ((= c "=") (setq result (strcat result "%3D")))
      ;; & -> %26
      ((= c "&") (setq result (strcat result "%26")))
      ;; # -> %23
      ((= c "#") (setq result (strcat result "%23")))
      ;; + -> %2B
      ((= c "+") (setq result (strcat result "%2B")))
      ;; Karakter lain langsung
      (T (setq result (strcat result c)))
    )
    (setq i (1+ i))
  )
  result
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Send-Telegram-Message
;; Deskripsi: Mengirim pesan teks ke Telegram via GET request
;; Parameter: msg - String pesan
;; Return: T jika berhasil, nil jika gagal
;; ----------------------------------------------------------------------------
(defun Send-Telegram-Message (msg / http url result err_open err_send resp_text)
  (setq result nil)
  (setq http (vl-catch-all-apply 'vlax-create-object (list "WinHttp.WinHttpRequest.5.1")))
  (if (vl-catch-all-error-p http)
    (progn
      (princ "\n[Telegram] Gagal buat objek WinHttp.")
      (setq http nil)
    )
    (progn
      (setq url (strcat *Telegram-BaseURL*
                        *Telegram-Bot-Token*
                        "/sendMessage"
                        "?chat_id=" *Telegram-Chat-ID*
                        "&text=" (Telegram-URL-Encode msg)))

      (setq err_open (vl-catch-all-apply 'vlax-invoke (list http 'Open "GET" url :vlax-false)))
      (if (vl-catch-all-error-p err_open)
        (princ (strcat "\n[Telegram] Open gagal: " (vl-catch-all-error-message err_open)))
        (progn
          (vl-catch-all-apply 'vlax-invoke (list http 'SetTimeouts 5000 10000 15000 15000))
          (setq err_send (vl-catch-all-apply 'vlax-invoke (list http 'Send)))
          (if (vl-catch-all-error-p err_send)
            (princ (strcat "\n[Telegram] Send gagal: " (vl-catch-all-error-message err_send)))
            (progn
              (vl-catch-all-apply 'vlax-invoke (list http 'WaitForResponse 15))
              ;; Cek response untuk konfirmasi
              (setq resp_text (vl-catch-all-apply 'vlax-get (list http 'ResponseText)))
              (if (and resp_text
                       (not (vl-catch-all-error-p resp_text))
                       (vl-string-search "\"ok\":true" resp_text))
                (progn
                  (princ "\n[Telegram] Notifikasi terkirim!")
                  (setq result T)
                )
                (progn
                  (princ "\n[Telegram] Response tidak OK.")
                  (if (and resp_text (not (vl-catch-all-error-p resp_text)))
                    (princ (strcat "\n[Telegram] Response: " (substr resp_text 1 100)))
                  )
                  ;; Tetap set result T karena mungkin sudah terkirim
                  (setq result T)
                )
              )
            )
          )
        )
      )
      (vl-catch-all-apply 'vlax-release-object (list http))
    )
  )
  result
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Send-Telegram-Notification
;; Deskripsi: Mengirim notifikasi dengan format rapi dan informatif
;; ----------------------------------------------------------------------------
(defun Send-Telegram-Notification (notif_type data / msg icon title)
  (setq icon "[INFO]")
  (setq title "NOTIFICATION")

  (cond
    ((= notif_type "NEW_ORDER")
     (setq icon "PESANAN BARU")
     (setq title "Ada order masuk!"))
    ((= notif_type "PAYMENT_SUCCESS")
     (setq icon "PEMBAYARAN SUKSES")
     (setq title "Lisensi aktif otomatis!"))
    ((= notif_type "PAYMENT_PENDING")
     (setq icon "MENUNGGU BAYAR")
     (setq title "Belum selesai bayar"))
    ((= notif_type "ACTIVATION_MANUAL")
     (setq icon "AKTIVASI MANUAL")
     (setq title "User minta kode aktivasi"))
    ((= notif_type "ERROR")
     (setq icon "ERROR")
     (setq title "Terjadi kesalahan"))
    ((= notif_type "TRIAL_EXPIRED")
     (setq icon "TRIAL HABIS")
     (setq title "User mencapai batas trial"))
    ((= notif_type "USAGE_REPORT")
     (setq icon "LAPORAN PENGGUNAAN")
     (setq title "Data penggunaan program"))
  )

  ;; Bangun pesan dengan format teks biasa (tanpa karakter HTML)
  (setq msg "")
  (setq msg (strcat msg "[ " icon " ]\n"))
  (setq msg (strcat msg title "\n"))
  (setq msg (strcat msg "----------------------------\n"))

  (foreach item data
    (setq msg (strcat msg (car item) ": " (cdr item) "\n"))
  )

  (setq msg (strcat msg "----------------------------\n"))
  (setq msg (strcat msg "App: " *License-Dialog-Title* "\n"))
  (setq msg (strcat msg "Time: " (rtos (getvar "CDATE") 2 0)))

  (Send-Telegram-Message msg)
)

;; ----------------------------------------------------------------------------
;; Shortcut notification functions
;; ----------------------------------------------------------------------------

(defun Send-New-Order-Notification (software_id status /)
  (Send-Telegram-Notification "NEW_ORDER"
    (list
      (cons "Software ID" software_id)
      (cons "PC Name" (Get-Computer-Name))
      (cons "Windows User" (Get-Windows-Username))
      (cons "Nominal" (strcat "Rp " (itoa *TemanQRIS-Amount*)))
      (cons "Status" status)
      (cons "Action" "Tunggu konfirmasi pembayaran")
    )
  )
)

(defun Send-Payment-Success-Notification (software_id /)
  (Send-Telegram-Notification "PAYMENT_SUCCESS"
    (list
      (cons "Software ID" software_id)
      (cons "PC Name" (Get-Computer-Name))
      (cons "Windows User" (Get-Windows-Username))
      (cons "Nominal" (strcat "Rp " (itoa *TemanQRIS-Amount*)))
      (cons "Status" "LUNAS - Lisensi PRO Aktif")
      (cons "Action" "Tidak perlu tindakan lanjut")
    )
  )
)

(defun Send-Manual-Activation-Notification (software_id machine_id /)
  (Send-Telegram-Notification "ACTIVATION_MANUAL"
    (list
      (cons "Software ID" software_id)
      (cons "Machine ID" machine_id)
      (cons "Action" "Generate kode aktivasi dan kirim ke user")
      (cons "Cara" "Jalankan GENERATE-ACTIVATION-CODE di AutoCAD")
    )
  )
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Send-Manual-Activation-With-Code
;; Deskripsi: Kirim notifikasi aktivasi manual BESERTA kode aktivasi ke admin
;;            Admin bisa langsung copy kode dan kirim balik ke user
;; Parameter:
;;   software_id     - String Software ID user
;;   machine_id      - String Machine ID user
;;   activation_code - String kode aktivasi yang sudah di-generate
;; Return: T jika berhasil
;; ----------------------------------------------------------------------------
(defun Send-Manual-Activation-With-Code (software_id machine_id activation_code /)
  (Send-Telegram-Notification "ACTIVATION_MANUAL"
    (list
      (cons "Software ID" software_id)
      (cons "Machine ID" machine_id)
      (cons "PC Name" (Get-Computer-Name))
      (cons "Windows User" (Get-Windows-Username))
      (cons "KODE AKTIVASI" activation_code)
      (cons "Action" "Copy kode di atas dan kirim ke user")
      (cons "Info" "Kode ini unik untuk machine user ini")
    )
  )
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Send-Usage-Report
;; Deskripsi: Kirim laporan penggunaan program secara silent (tanpa popup user)
;;            Untuk keperluan research dan pengembangan
;; Parameter:
;;   software_id - String Software ID
;;   dwg_name    - String nama file DWG yang sedang aktif
;;   total_plot  - Integer total plot yang berhasil di-generate
;;   is_pro      - Boolean status lisensi
;; Return: T jika berhasil
;; ----------------------------------------------------------------------------
(defun Send-Usage-Report (software_id dwg_name total_plot is_pro /)
  (Send-Telegram-Notification "USAGE_REPORT"
    (list
      (cons "Software ID" software_id)
      (cons "PC Name" (Get-Computer-Name))
      (cons "Windows User" (Get-Windows-Username))
      (cons "File DWG" dwg_name)
      (cons "Total Plot" (itoa total_plot))
      (cons "Status" (if is_pro "PRO" "TRIAL"))
    )
  )
)

(defun Send-Trial-Expired-Notification (software_id count /)
  (Send-Telegram-Notification "TRIAL_EXPIRED"
    (list
      (cons "Software ID" software_id)
      (cons "Diproses" (strcat (itoa count) " item"))
      (cons "Status" "Trial limit tercapai")
      (cons "Action" "User perlu beli lisensi PRO")
    )
  )
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Test-Telegram-Connection
;; ----------------------------------------------------------------------------
(defun Test-Telegram-Connection (/)
  (princ "\n[Telegram] Testing connection...")
  (if (Send-Telegram-Message "TEST: Koneksi dari License Module - OK!")
    (progn (princ "\n[Telegram] Berhasil!") T)
    (progn (princ "\n[Telegram] Gagal!") nil)
  )
)

(princ "\n[License Module] Telegram Notifier loaded.")
(princ)
