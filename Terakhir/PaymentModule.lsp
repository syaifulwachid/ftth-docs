;; ============================================================================
;; PAYMENTMODULE.LSP - Modul Pembayaran TemanQRIS
;; ============================================================================
;; Modul ini menangani integrasi dengan API TemanQRIS untuk pembayaran QRIS.
;; File ini di-load oleh LicenseManager.lsp secara otomatis.
;;
;; FUNGSI UTAMA:
;; - (Generate-Payment-Link software_id)              : Generate link pembayaran
;; - (Check-Payment-Status order_id)                  : Cek status pembayaran
;; - (Process-Payment-Flow software_id)               : Flow pembayaran + buka browser
;; - (Verify-and-Activate machine_id software_id)     : Verifikasi + aktifkan lisensi
;;
;; API TemanQRIS:
;; - POST /payment-link  : Buat payment link
;; - GET  /orders/:id    : Cek status order
;; ============================================================================

;; ----------------------------------------------------------------------------
;; FUNGSI INTERNAL: Make-HTTP-Request
;; Deskripsi: Helper untuk membuat HTTP request dengan WinHttp
;; Parameter:
;;   method  - String "GET" atau "POST"
;;   url     - String URL lengkap
;;   payload - String JSON body (nil untuk GET)
;; Return: String response body atau nil jika error
;; ----------------------------------------------------------------------------
(defun Make-HTTP-Request (method url payload / http result err_open err_send err_wait)
  (setq result nil)
  (setq http nil)

  ;; Buat objek HTTP - gunakan WinHttpRequest dengan mode synchronous
  (setq http (vl-catch-all-apply 'vlax-create-object (list "WinHttp.WinHttpRequest.5.1")))
  (if (vl-catch-all-error-p http)
    (progn
      (princ (strcat "\n[HTTP] Gagal buat objek WinHttp: " (vl-catch-all-error-message http)))
      (setq http nil)
    )
    (progn
      ;; Open connection - parameter ke-4 FALSE = synchronous mode
      (setq err_open (vl-catch-all-apply 'vlax-invoke (list http 'Open method url :vlax-false)))
      (if (vl-catch-all-error-p err_open)
        (princ (strcat "\n[HTTP] Open gagal: " (vl-catch-all-error-message err_open)))
        (progn
          ;; Set headers
          (vl-catch-all-apply 'vlax-invoke (list http 'SetRequestHeader "X-API-Key" *TemanQRIS-API-Key*))
          (vl-catch-all-apply 'vlax-invoke (list http 'SetRequestHeader "Content-Type" "application/json"))
          ;; Set TLS 1.2 support
          (vl-catch-all-apply 'vlax-put (list http 'Option 9 2048))
          ;; Set timeout: resolve=5s, connect=10s, send=30s, receive=30s (dalam ms)
          (vl-catch-all-apply 'vlax-invoke (list http 'SetTimeouts 5000 10000 30000 30000))

          ;; Send request - POST dengan payload, GET tanpa body
          (if payload
            (setq err_send (vl-catch-all-apply 'vlax-invoke (list http 'Send payload)))
            (setq err_send (vl-catch-all-apply 'vlax-invoke (list http 'Send)))
          )

          (if (vl-catch-all-error-p err_send)
            (princ (strcat "\n[HTTP] Send gagal: " (vl-catch-all-error-message err_send)))
            (progn
              ;; WaitForResponse - tunggu sampai response tersedia (timeout 30 detik)
              (setq err_wait (vl-catch-all-apply 'vlax-invoke (list http 'WaitForResponse 30)))
              (if (vl-catch-all-error-p err_wait)
                (princ (strcat "\n[HTTP] WaitForResponse gagal: " (vl-catch-all-error-message err_wait)))
              )
              ;; Baca response text
              (setq result (vl-catch-all-apply 'vlax-get (list http 'ResponseText)))
              (if (vl-catch-all-error-p result)
                (progn
                  (princ (strcat "\n[HTTP] Get response gagal: " (vl-catch-all-error-message result)))
                  (setq result nil)
                )
                (princ (strcat "\n[HTTP] Response diterima (" (itoa (strlen result)) " chars)"))
              )
            )
          )
        )
      )
    )
  )

  (if (and http (not (vl-catch-all-error-p http)))
    (vl-catch-all-apply 'vlax-release-object (list http))
  )
  result
)

;; ----------------------------------------------------------------------------
;; FUNGSI INTERNAL: JSON-Get-String
;; Deskripsi: Ekstrak nilai string dari JSON response sederhana
;; Parameter:
;;   json - String JSON response
;;   key  - String nama field yang dicari (tanpa tanda kutip)
;; Return: String nilai atau nil
;; ----------------------------------------------------------------------------
(defun JSON-Get-String (json key / search_str pos val_start val_end)
  (setq search_str (strcat "\"" key "\":\""))
  (if (setq pos (vl-string-search search_str json))
    (progn
      (setq val_start (+ pos (strlen search_str)))
      (setq val_end (vl-string-search "\"" json val_start))
      (if val_end
        (substr json (1+ val_start) (- val_end val_start))
        nil
      )
    )
    nil
  )
)

;; ----------------------------------------------------------------------------
;; FUNGSI INTERNAL: Generate-Unique-Order-ID
;; Deskripsi: Generate order_id unik dari software_id + timestamp singkat
;;            Setiap panggilan menghasilkan order_id yang berbeda
;;            Max 30 chars sesuai batasan TemanQRIS API
;; Parameter: software_id - String Software ID (format FT-XXXX-XXXX = 12 chars)
;; Return: String order_id unik (max 30 chars)
;; ----------------------------------------------------------------------------
(defun Generate-Unique-Order-ID (software_id / ts ts_full dot_pos ts_str order_id)
  ;; Ambil timestamp dari CDATE AutoCAD
  ;; CDATE format: 20260320.122345 (YYYYMMDD.HHMMSS)
  ;; Gunakan presisi 6 agar titik desimal muncul
  (setq ts_full (rtos (getvar "CDATE") 2 6))
  ;; Cari posisi titik desimal
  (setq dot_pos (vl-string-search "." ts_full))
  (if dot_pos
    ;; Ada titik - ambil 6 digit setelah titik (HHMMSS)
    (setq ts_str (substr ts_full (+ dot_pos 2) 6))
    ;; Tidak ada titik - gunakan 6 digit terakhir dari string
    (setq ts_str (substr ts_full (max 1 (- (strlen ts_full) 5)) 6))
  )
  ;; Pastikan ts_str tidak nil dan tidak kosong
  (if (or (not ts_str) (= ts_str ""))
    (setq ts_str (itoa (fix (getvar "CDATE"))))
  )
  ;; Gabungkan: FT-XXXX-XXXX-HHMMSS = 19 chars (aman < 30)
  (setq order_id (strcat software_id "-" ts_str))
  order_id
)

;; ----------------------------------------------------------------------------
;; FUNGSI INTERNAL: Get-Temp-Path
;; Deskripsi: Mendapatkan folder konfigurasi %APPDATA%
;; ----------------------------------------------------------------------------
(defun Get-Temp-Path (/ appdata folder)
  (setq appdata (getenv "APPDATA"))
  (setq folder (strcat appdata "\\SWDSoftDeveloper"))
  (vl-catch-all-apply 'vl-mkdir (list folder))
  (strcat folder "\\")
)

;; ----------------------------------------------------------------------------
;; FUNGSI INTERNAL: Save-Last-Order-ID / Load-Last-Order-ID
;; Deskripsi: Simpan/baca order_id terakhir untuk keperluan verifikasi
;; ----------------------------------------------------------------------------
(defun Save-Last-Order-ID (order_id p_url / f f_path)
  (setq f_path (strcat (Get-Temp-Path) "sysLastOrder.tmp"))
  (vl-catch-all-apply
    '(lambda ()
       (setq f (open f_path "w"))
       (if f
         (progn
           (write-line order_id f)
           (if p_url (write-line p_url f))
           (close f)
         )
       )
    )
  )
)

(defun Load-Last-Order-ID (/ f_path f order_id p_url raw_p_url)
  (setq order_id nil)
  (setq p_url nil)
  (setq f_path (strcat (Get-Temp-Path) "sysLastOrder.tmp"))
  (if (findfile f_path)
    (progn
      (setq f (open f_path "r"))
      (if f
        (progn
          (setq order_id (vl-string-trim " \t\n\r" (read-line f)))
          (setq raw_p_url (vl-catch-all-apply 'read-line (list f)))
          (if (not (vl-catch-all-error-p raw_p_url))
            (if raw_p_url (setq p_url (vl-string-trim " \t\n\r" raw_p_url)))
          )
          (close f)
        )
      )
    )
  )
  (list order_id p_url)
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Generate-Payment-Link
;; Deskripsi: Generate link pembayaran QRIS baru melalui TemanQRIS API
;;            Setiap panggilan selalu generate link BARU dengan order_id unik
;;            Endpoint: POST /payment-link
;; Parameter:
;;   software_id - String Software ID unik
;; Return: String URL pembayaran lengkap atau nil jika gagal
;; ----------------------------------------------------------------------------
(defun Generate-Payment-Link (software_id / url payload resp p_url link_url pos payment_code order_id)
  (setq p_url nil)
  (princ "\n[Payment] Menghubungkan ke TemanQRIS...")

  ;; Generate order_id UNIK dengan timestamp - selalu baru setiap request
  (setq order_id (Generate-Unique-Order-ID software_id))
  (princ (strcat "\n[Payment] Order ID: " order_id))

  (setq url (strcat *TemanQRIS-BaseURL* "/payment-link"))
  (setq payload (strcat "{"
                        "\"amount\":" (itoa *TemanQRIS-Amount*) ","
                        "\"description\":\"" *TemanQRIS-Description* " (" order_id ")\","
                        "\"order_id\":\"" order_id "\""
                        "}"))

  (setq resp (Make-HTTP-Request "POST" url payload))

  (cond
    ;; Tidak ada response (koneksi gagal)
    ((null resp)
     (princ "\n[Payment] Response nil - cek command line untuk detail error.")
     (alert (strcat "Koneksi Gagal!\n\n"
                    "Tidak dapat menghubungi server TemanQRIS.\n"
                    "Periksa koneksi internet Anda."))
     (setq p_url nil))

    ;; Response ada - proses
    (T
     (progn
       ;; Cari field "url" di dalam object payment_link
       ;; Response: {"success":true,"payment_link":{"url":"/p/XXXXX",...}}
       (setq link_url (JSON-Get-String resp "url"))
       (if link_url
         (progn
           (setq p_url (strcat "https://temanqris.com" link_url))
           ;; Simpan order_id untuk verifikasi nanti
           (Save-Last-Order-ID order_id p_url)
           (Send-New-Order-Notification software_id "Menunggu Pembayaran")
           (princ (strcat "\n[Payment] Link baru dibuat: " p_url))
         )
         (progn
           ;; Fallback: cari pola /p/ di response
           (if (setq pos (vl-string-search "/p/" resp))
             (progn
               (setq payment_code (substr resp (1+ pos)))
               (setq p_url (strcat "https://temanqris.com"
                                   (substr payment_code 1 (vl-string-search "\"" payment_code))))
               (Save-Last-Order-ID order_id p_url)
               (Send-New-Order-Notification software_id "Menunggu Pembayaran")
             )
             (progn
               (alert (strcat "Format respon server tidak dikenali.\n\nResponse:\n"
                              (substr resp 1 300)))
               (setq p_url nil)
             )
           )
         )
       )
     ))
  )

  p_url
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Check-Payment-Status
;; Deskripsi: Mengecek status pembayaran dari TemanQRIS API
;;            Endpoint: GET /orders/:orderId
;; Parameter:
;;   order_id - String order ID (sama dengan software_id yang dipakai saat generate)
;;   p_url    - String public link URL pembayaran (untuk fallback scraping)
;; Return: String status: "PAID", "PENDING", "EXPIRED", "ERROR", atau "UNKNOWN"
;; ----------------------------------------------------------------------------
(defun Check-Payment-Status (order_id p_url / url resp resp_upper result html_scraped)
  (setq result "ERROR")
  (princ "\n[Payment] Memverifikasi status pembayaran...")

  (setq url (strcat *TemanQRIS-BaseURL* "/orders/" order_id))
  (setq resp (Make-HTTP-Request "GET" url nil))

  (if (null resp)
    (progn
      (princ "\n[Payment] Gagal menghubungi server untuk verifikasi.")
      (setq result "ERROR")
    )
    (progn
      (setq resp_upper (strcase resp))
      
      ;; Hapus semua spasi, tab, newline agar mudah difilter dengan pasti
      (setq resp_clean "")
      (setq i 1)
      (while (<= i (strlen resp_upper))
        (setq ch (substr resp_upper i 1))
        (if (not (or (= ch " ") (= ch "\t") (= ch "\n") (= ch "\r")))
          (setq resp_clean (strcat resp_clean ch))
        )
        (setq i (1+ i))
      )
      
      (cond
        ;; Cek status PAID dengan exact match (tanpa spasi)
        ((or (vl-string-search "\"STATUS\":\"PAID\"" resp_clean)
             (vl-string-search "\"STATUS\":\"SUCCESS\"" resp_clean)
             (vl-string-search "\"STATUS\":\"SETTLED\"" resp_clean)
             (vl-string-search "\"PAID\":TRUE" resp_clean)
             (vl-string-search "\"IS_PAID\":TRUE" resp_clean)
             (vl-string-search "\"TRANSACTIONSTATUS\":\"SETTLEMENT\"" resp_clean)
             (vl-string-search "\"TRANSACTIONSTATUS\":\"CAPTURE\"" resp_clean))
         (setq result "PAID"))

        ;; Cek status PENDING / WAITING / UNPAID (Belum dibayar)
        ((or (vl-string-search "\"STATUS\":\"PENDING\"" resp_clean)
             (vl-string-search "\"STATUS\":\"WAITING\"" resp_clean)
             (vl-string-search "\"STATUS\":\"UNPAID\"" resp_clean)
             (vl-string-search "\"TRANSACTIONSTATUS\":\"PENDING\"" resp_clean))
         (setq result "PENDING"))

        ;; Cek status EXPIRED
        ((or (vl-string-search "\"STATUS\":\"EXPIRED\"" resp_clean)
             (vl-string-search "\"TRANSACTIONSTATUS\":\"EXPIRE\"" resp_clean)
             (vl-string-search "\"TRANSACTIONSTATUS\":\"CANCEL\"" resp_clean))
         (setq result "EXPIRED"))

        ;; Jika terdeteksi kata UNPAID atau EXPIRED dalam string aslinya 
        ((vl-string-search "UNPAID" resp_upper)
         (setq result "PENDING"))
         
        ((vl-string-search "EXPIRED" resp_upper)
         (setq result "EXPIRED"))

        (T
         (setq result "UNKNOWN"))
      )
      
      ;; === FALLBACK WEB SCRAPING ===
      ;; Karena API /payment-link sering me-reject GET /orders/ dengan "Order not found"
      ;; Kita langsung scrape HTML halaman pembayarannya jika API gagal!
      (if (and (or (= result "UNKNOWN") (= result "ERROR") (= result "EXPIRED")) 
               p_url (> (strlen p_url) 10))
        (progn
          (princ "\n[Payment] API gagal mengenali, mencoba baca status langsung via Web TemanQRIS...")
          (setq html_scraped (Make-HTTP-Request "GET" p_url nil))
          (if html_scraped
            (progn
              (if (or (vl-string-search "Pembayaran Berhasil" html_scraped)
                      (vl-string-search "diverifikasi" html_scraped))
                (progn
                  (princ "\n[Payment] BYPASS SUKSES: Ditemukan status PAID di halaman web.")
                  (setq result "PAID")
                )
                (if (vl-string-search "Sudah Bayar" html_scraped)
                  (progn
                    (princ "\n[Payment] BYPASS SUKSES: Ditemukan status PENDING di halaman web.")
                    (setq result "PENDING")
                  )
                )
              )
            )
          )
        )
      )
    )
  )

  (princ (strcat "\n[Payment] Status Terakhir: " result))
  result
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Process-Payment-Flow
;; Deskripsi: Menjalankan flow pembayaran lengkap dan buka browser
;; Parameter:
;;   software_id - String Software ID
;; Return: T jika berhasil generate link, nil jika gagal
;; ----------------------------------------------------------------------------
(defun Process-Payment-Flow (software_id / payment_url)
  ;; Selalu generate link BARU dengan order_id unik (timestamp)
  (setq payment_url (Generate-Payment-Link software_id))
  (if payment_url
    (progn
      (princ "\n[Payment] Membuka halaman pembayaran...")
      (startapp "explorer" payment_url)
      T
    )
    nil
  )
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Verify-and-Activate
;; Deskripsi: Verifikasi pembayaran dan aktifkan lisensi jika sudah bayar
;; Parameter:
;;   machine_id  - String Machine ID
;;   software_id - String Software ID (digunakan sebagai order_id)
;; Return: T jika berhasil diaktifkan, nil jika belum bayar atau error
;; ----------------------------------------------------------------------------
(defun Verify-and-Activate (machine_id software_id / status last_order_id p_url order_data)
  ;; Gunakan order_id terakhir yang disimpan, bukan software_id
  (setq order_data (Load-Last-Order-ID))
  (if order_data
    (progn
      (setq last_order_id (car order_data))
      (setq p_url (cadr order_data))
    )
  )
  
  (if (or (not last_order_id) (= last_order_id ""))
    (setq last_order_id software_id)  ;; Fallback ke software_id jika tidak ada
  )
  (princ (strcat "\n[Payment] Verifikasi order: " last_order_id))
  (setq status (Check-Payment-Status last_order_id p_url))

  (cond
    ;; Pembayaran berhasil
    ((= status "PAID")
     (progn
       (Save-License-Key (Generate-License-Key machine_id))
       (Send-Payment-Success-Notification software_id)
       (alert "PEMBAYARAN DITERIMA!\n\nLisensi PRO Anda kini sudah aktif.\nTerima kasih!")
       T
     ))

    ;; Belum bayar
    ((= status "PENDING")
     (progn
       (alert "Status Pembayaran: BELUM DIBAYAR.\n\nSilakan selesaikan pembayaran QRIS terlebih dahulu.")
       nil
     ))

    ;; Expired
    ((= status "EXPIRED")
     (progn
       (alert "Status Pembayaran: EXPIRED.\n\nSilakan generate link pembayaran baru.")
       nil
     ))

    ;; Unknown - mungkin order_id tidak ditemukan
    ((= status "UNKNOWN")
     (progn
       (alert "Status tidak dikenali.\n\nPastikan Anda sudah melakukan pembayaran terlebih dahulu.")
       nil
     ))

    ;; Error koneksi
    (T
     (progn
       (alert "Gagal memverifikasi status pembayaran.\n\nSilakan coba lagi nanti.")
       nil
     ))
  )
)

(princ "\n[License Module] Payment Module loaded.")
(princ)
