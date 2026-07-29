;; ============================================================================
;; COMPILE.LSP - Script Kompilasi Semua Modul ke Format .FAS
;; ============================================================================
;;
;; *** CARA KOMPILASI YANG BENAR (IKUTI LANGKAH INI) ***
;;
;; LANGKAH 1: Buka Visual LISP IDE
;;   - Di AutoCAD command line, ketik: VLISP
;;   - Visual LISP IDE akan terbuka
;;
;; LANGKAH 2: Load Compile.lsp di IDE
;;   - Di IDE: klik menu File > Open
;;   - Pilih file Compile.lsp
;;   - Klik menu Tools > Load Text in Editor
;;   - Atau tekan Ctrl+Alt+L
;;
;; LANGKAH 3: Jalankan Kompilasi di Console IDE
;;   - Di panel Console IDE (bagian bawah, ada prompt "_$")
;;   - Ketik perintah berikut lalu tekan Enter:
;;     (C:COMPILEDIRECT)
;;
;; LANGKAH 4: Selesai!
;;   - Semua file .fas akan dibuat/diperbarui di folder yang sama
;;   - Distribusikan file .fas ke user
;;
;; ============================================================================
;; CATATAN PENTING:
;; - Kompilasi TIDAK bisa dari AutoCAD command line biasa
;; - Harus dari Visual LISP IDE (ketik VLISP untuk membuka)
;; - Setiap kali ada perubahan .lsp, kompilasi ulang dengan langkah di atas
;; ============================================================================

(defun Compile-One-File (lsp_file / fas_file dir_path result)
  ;; Tentukan nama output .fas dengan path yang benar
  (setq dir_path (vl-filename-directory lsp_file))
  ;; Pastikan path diakhiri backslash
  (if (not (= (substr dir_path (strlen dir_path)) "\\"))
    (setq dir_path (strcat dir_path "\\"))
  )
  (setq fas_file (strcat dir_path (vl-filename-base lsp_file) ".fas"))

  (princ (strcat "\n  Mengkompilasi: " (vl-filename-base lsp_file) ".lsp ..."))

  ;; Coba kompilasi dengan vlisp-compile
  (setq result
    (vl-catch-all-apply
      '(lambda ()
         ;; vlisp-compile hanya tersedia di Visual LISP environment
         (vlisp-compile 'st lsp_file fas_file)
       )
    )
  )

  (if (vl-catch-all-error-p result)
    (progn
      (princ " GAGAL")
      (princ (strcat "\n  Error: " (vl-catch-all-error-message result)))
      nil
    )
    (progn
      (princ " OK")
      T
    )
  )
)

(defun Compile-All-Modules (/ files f base_path ok_count fail_count found_any)
  (princ "\n================================================")
  (princ "\n  KOMPILASI FTTH LICENSE LIBRARY")
  (princ "\n================================================")

  ;; Cari path folder modul - coba berbagai file
  (setq base_path "")
  (setq found_any nil)

  ;; Coba cari dari berbagai file (lsp atau fas)
  (foreach try_file (list "Config.lsp" "LicenseUtils.lsp" "LicenseManager.lsp"
                          "Config.fas" "LicenseUtils.fas" "LicenseManager.fas")
    (if (and (not found_any) (findfile try_file))
      (progn
        (setq base_path (vl-filename-directory (findfile try_file)))
        (setq found_any T)
      )
    )
  )

  ;; Jika masih tidak ditemukan, minta user input path manual
  (if (= base_path "")
    (progn
      (setq base_path (getstring T "\nMasukkan path folder modul (contoh: D:\\MyTools\\): "))
    )
  )

  ;; Pastikan path diakhiri backslash
  (if (not (= (substr base_path (strlen base_path)) "\\"))
    (setq base_path (strcat base_path "\\"))
  )

  (princ (strcat "\n  Folder: " base_path))
  (princ "\n------------------------------------------------")

  ;; Daftar file yang dikompilasi (urutan penting!)
  (setq files (list
    "Config.lsp"
    "LicenseUtils.lsp"
    "TelegramNotifier.lsp"
    "PaymentModule.lsp"
    "LicenseManager.lsp"
  ))

  (setq ok_count 0)
  (setq fail_count 0)

  (foreach f files
    (setq full_path (strcat base_path f))
    (if (findfile full_path)
      (if (Compile-One-File full_path)
        (setq ok_count (1+ ok_count))
        (setq fail_count (1+ fail_count))
      )
      (progn
        (princ (strcat "\n  SKIP: " f " tidak ditemukan."))
        (setq fail_count (1+ fail_count))
      )
    )
  )

  (princ "\n------------------------------------------------")
  (princ (strcat "\n  Berhasil: " (itoa ok_count) " file"))
  (princ (strcat "\n  Gagal   : " (itoa fail_count) " file"))
  (princ "\n================================================")

  (if (= fail_count 0)
    (progn
      (princ "\n  Semua modul berhasil dikompilasi!")
      (princ "\n  File .fas siap didistribusikan.")
    )
    (progn
      (princ "\n  CATATAN: Kompilasi harus dijalankan dari")
      (princ "\n  Visual LISP IDE (ketik VLISP di AutoCAD)")
    )
  )
  (princ "\n================================================")
  (princ)
)

(defun Compile-Placer (/ base_path full_path)
  (setq base_path "")
  (if (findfile "Placer.lsp")
    (setq base_path (vl-filename-directory (findfile "Placer.lsp")))
  )
  (if (= base_path "")
    (progn (princ "\n  Placer.lsp tidak ditemukan.") (exit))
  )
  (if (not (= (substr base_path (strlen base_path)) "\\"))
    (setq base_path (strcat base_path "\\"))
  )
  (setq full_path (strcat base_path "Placer.lsp"))
  (princ "\n================================================")
  (princ "\n  KOMPILASI PLACER.LSP")
  (princ "\n------------------------------------------------")
  (Compile-One-File full_path)
  (princ "\n================================================")
  (princ)
)

;; ============================================================================
;; COMMANDS AUTOCAD
;; ============================================================================

(defun C:COMPILEALL (/)
  (Compile-All-Modules)
  (princ)
)

(defun C:COMPILEPLACER (/)
  (Compile-Placer)
  (princ)
)

(defun C:COMPILEALL2 (/)
  (Compile-All-Modules)
  (Compile-Placer)
  (princ)
)

;; Kompilasi dengan path yang ditentukan user
(defun C:COMPILEDIRECT (/ path files f lsp fas found_path)
  ;; Cari path otomatis dari Compile.lsp itu sendiri
  (setq found_path nil)
  (if (findfile "Compile.lsp")
    (setq found_path (vl-filename-directory (findfile "Compile.lsp")))
  )

  ;; Jika tidak ditemukan, minta input manual
  (if (not found_path)
    (setq found_path (getstring T "\nPath folder modul (contoh D:\\Tools\\): "))
  )

  (setq path found_path)

  ;; Pastikan diakhiri backslash
  (if (and path (> (strlen path) 0))
    (if (not (= (substr path (strlen path)) "\\"))
      (setq path (strcat path "\\"))
    )
  )

  (princ (strcat "\n[Compile] Path: " path))
  (setq files (list "Config" "LicenseUtils" "TelegramNotifier"
                    "PaymentModule" "LicenseManager" "Placer"))
  (foreach f files
    (setq lsp (strcat path f ".lsp"))
    (setq fas (strcat path f ".fas"))
    (if (findfile lsp)
      (progn
        (princ (strcat "\n[Compile] " f ".lsp ..."))
        (vl-catch-all-apply '(lambda () (vlisp-compile 'st lsp fas)))
        (princ " done")
      )
      (princ (strcat "\n[Compile] SKIP: " f ".lsp tidak ada"))
    )
  )
  (princ "\n[Compile] Selesai!")
  (princ)
)

;; ============================================================================
;; PANDUAN KOMPILASI MANUAL VIA VISUAL LISP IDE
;; ============================================================================
(defun C:COMPILEGUIDE (/)
  (alert (strcat
    "PANDUAN KOMPILASI KE FORMAT .FAS\n\n"
    "CARA 1 - Visual LISP IDE (Paling Mudah):\n"
    "1. Ketik VLISP di command line AutoCAD\n"
    "2. Di IDE: File > Open > pilih file .lsp\n"
    "3. Di IDE: Tools > Compile (Ctrl+F8)\n"
    "4. File .fas akan dibuat di folder yang sama\n\n"
    "CARA 2 - Batch via Console IDE:\n"
    "1. Ketik VLISP di command line AutoCAD\n"
    "2. Di Console IDE ketik:\n"
    "   (load \"Compile.lsp\")\n"
    "   (Compile-All-Modules)\n\n"
    "CATATAN:\n"
    "- Kompilasi TIDAK bisa dari command line biasa\n"
    "- Harus dari Visual LISP IDE atau Console IDE\n"
    "- Distribusikan file .fas, BUKAN .lsp"
  ))
  (princ)
)

(princ "\n================================================")
(princ "\n   COMPILE SCRIPT - FTTH License Library")
(princ "\n================================================")
(princ "\n   Commands:")
(princ "\n   COMPILEALL    - Kompilasi semua modul")
(princ "\n   COMPILEPLACER - Kompilasi Placer.lsp")
(princ "\n   COMPILEALL2   - Kompilasi semua + Placer")
(princ "\n   COMPILEGUIDE  - Panduan kompilasi")
(princ "\n================================================")
(princ "\n   CATATAN: Jalankan dari Visual LISP IDE")
(princ "\n   Ketik VLISP untuk membuka IDE")
(princ "\n================================================")
(princ)
