;; ============================================================================
;; LICENSEUTILS.LSP - Helper Functions untuk License Module
;; ============================================================================
;; Fungsi-fungsi utilitas yang digunakan oleh modul lain.
;; File ini di-load oleh LicenseManager.lsp secara otomatis.
;;
;; FUNGSI TERSEDIA:
;; - (Get-Machine-ID)                  : Ambil Machine ID dari BIOS
;; - (Generate-Software-ID machine_id) : Generate Software ID terenkripsi
;; - (Generate-License-Key machine_id) : Generate license key hash
;; - (Simple-Hash str)                 : Hash sederhana
;; - (Check-License-File machine_id)   : Cek validitas file lisensi
;; - (Save-License-Key key)            : Simpan license key ke file
;; - (URL-Encode str)                  : Encode string untuk URL
;; - (Format-Telegram-Message msg)     : Format pesan untuk Telegram
;; ============================================================================

;; ----------------------------------------------------------------------------
;; FUNGSI: Get-Machine-ID
;; Deskripsi: Mengambil Serial Number BIOS sebagai Machine ID unik
;; Return: String Machine ID (uppercase, trimmed)
;; ----------------------------------------------------------------------------
(defun Get-Machine-ID (/ wmi services items item id)
  (setq id "UNKNOWN-MACHINE")
  (vl-catch-all-apply
    '(lambda ()
       (setq wmi (vlax-create-object "WbemScripting.SWbemLocator"))
       (setq services (vlax-invoke wmi 'ConnectServer "." "root\\cimv2"))
       (setq items (vlax-invoke services 'ExecQuery "Select SerialNumber from Win32_BIOS"))
       (vlax-for item items (setq id (vlax-get item 'SerialNumber)))
       (vlax-release-object items)
       (vlax-release-object services)
       (vlax-release-object wmi)
    )
  )
  (strcase (vl-string-trim " " id))
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Generate-Software-ID
;; Deskripsi: Generate Software ID dari Machine ID (terenkripsi sederhana)
;; Parameter: machine_id - String Machine ID dari Get-Machine-ID
;; Return: String Software ID dengan format FT-XXXX-XXXX
;; ----------------------------------------------------------------------------
(defun Generate-Software-ID (machine_id / lst res out)
  (setq lst (vl-string->list machine_id))
  (setq res (mapcar '(lambda (x) (+ x 3)) (reverse lst)))
  (setq out "")
  (foreach c res (setq out (strcat out (chr c))))
  (strcat *License-Prefix* (substr out 1 4) "-" (substr out 5 4))
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Simple-Hash
;; Deskripsi: Fungsi hash sederhana untuk generate key
;; Parameter: str - String yang akan di-hash
;; Return: String hash numerik
;; ----------------------------------------------------------------------------
(defun Simple-Hash (str / res)
  (setq res 0)
  (foreach n (vl-string->list str)
    (setq res (+ res (* n 41)))
  )
  (itoa res)
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Generate-License-Key
;; Deskripsi: Generate license key dari Machine ID dan secret key
;; Parameter: machine_id - String Machine ID
;; Return: String License Key (hash)
;; ----------------------------------------------------------------------------
(defun Generate-License-Key (machine_id /)
  (Simple-Hash (strcat machine_id *License-Secret-Key*))
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Get-App-Path
;; Deskripsi: Mendapatkan absolute path dari folder konfigurasi %APPDATA%
;; ----------------------------------------------------------------------------
(defun Get-App-Path (/ appdata folder)
  (setq appdata (getenv "APPDATA"))
  (setq folder (strcat appdata "\\SWDSoftDeveloper"))
  (vl-catch-all-apply 'vl-mkdir (list folder))
  (strcat folder "\\")
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Check-License-File
;; Deskripsi: Mengecek apakah file lisensi valid dan cocok dengan machine
;; Parameter: machine_id - String Machine ID
;; Return: T jika valid, nil jika tidak
;; ----------------------------------------------------------------------------
(defun Check-License-File (machine_id / f_path f stored_key expected_key)
  (setq expected_key (Generate-License-Key machine_id))
  (setq f_path (strcat (Get-App-Path) *License-File-Name*))
  (if (findfile f_path)
    (progn
      (setq f (open f_path "r"))
      (setq stored_key (read-line f))
      (close f)
      (= stored_key expected_key)
    )
    nil
  )
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Save-License-Key
;; Deskripsi: Menyimpan license key ke file
;; Parameter: key - String license key yang akan disimpan
;; Return: T jika berhasil, nil jika gagal
;; ----------------------------------------------------------------------------
(defun Save-License-Key (key / f result f_path)
  (setq result nil)
  (setq f_path (strcat (Get-App-Path) *License-File-Name*))
  (vl-catch-all-apply
    '(lambda ()
       (setq f (open f_path "w"))
       (write-line key f)
       (close f)
       (setq result T)
    )
  )
  result
)

;; ----------------------------------------------------------------------------
;; FUNGSI: URL-Encode
;; Deskripsi: Encode string untuk URL (mengganti spasi dengan +)
;; Parameter: str - String yang akan di-encode
;; Return: String yang sudah di-encode
;; ----------------------------------------------------------------------------
(defun URL-Encode (str /)
  (vl-string-translate " " "+" str)
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Format-Telegram-Message
;; Deskripsi: Format pesan untuk Telegram (encode newline untuk URL)
;; Parameter: msg - String pesan
;; Return: String yang sudah diformat untuk URL
;; ----------------------------------------------------------------------------
(defun Format-Telegram-Message (msg /)
  (vl-string-translate "\n" "%0A" msg)
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Get-Computer-Name
;; Deskripsi: Mengambil nama komputer (hostname) dari Windows
;; Return: String nama komputer
;; ----------------------------------------------------------------------------
(defun Get-Computer-Name (/ wsh result)
  (setq result "UNKNOWN-PC")
  (vl-catch-all-apply
    '(lambda ()
       (setq wsh (vlax-create-object "WScript.Shell"))
       (setq result (vlax-invoke wsh 'ExpandEnvironmentStrings "%COMPUTERNAME%"))
       (vlax-release-object wsh)
    )
  )
  result
)

;; ----------------------------------------------------------------------------
;; FUNGSI: Get-Windows-Username
;; Deskripsi: Mengambil nama user Windows yang sedang login
;; Return: String username Windows
;; ----------------------------------------------------------------------------
(defun Get-Windows-Username (/ wsh result)
  (setq result "UNKNOWN-USER")
  (vl-catch-all-apply
    '(lambda ()
       (setq wsh (vlax-create-object "WScript.Shell"))
       (setq result (vlax-invoke wsh 'ExpandEnvironmentStrings "%USERNAME%"))
       (vlax-release-object wsh)
    )
  )
  result
)

(princ "\n[License Module] Utils loaded.")
(princ)
