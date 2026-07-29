;; ===========================================================================
;; Program      : FTTH Master Placer Pro
;; Versi        : 5.2 (Refactored - Modular License System)
;; Developer    : Syaiful Wachid - Fiberhome Indonesia
;; Telegram Bot : Wildan (Notifikasi Aktif)
;; ===========================================================================
;; PERUBAHAN v5.2:
;; - Modul lisensi, pembayaran, dan notifikasi dipindahkan ke file terpisah
;; - Core logic program tidak berubah
;; - Load LicenseManager.lsp sebagai dependensi
;; ===========================================================================

;; ---------------------------------------------------------------------------
;; LOAD MODUL LISENSI (Wajib ada di folder yang sama dengan Placer.lsp)
;; Prioritas: .fas (distribusi) > .lsp (development)
;; ---------------------------------------------------------------------------
(cond
  ;; Prioritas 1: Load LicenseManager.fas (mode distribusi)
  ((findfile "LicenseManager.fas")
   (load (findfile "LicenseManager.fas")))
  ;; Prioritas 2: Load LicenseManager.lsp (mode development)
  ((findfile "LicenseManager.lsp")
   (load (findfile "LicenseManager.lsp")))
  ;; Tidak ditemukan
  (T
   (alert "ERROR: LicenseManager tidak ditemukan!\nPastikan semua file modul (.fas atau .lsp)\nada di folder AutoCAD Support Path."))
)

;; ---------------------------------------------------------------------------
;; KONFIGURASI KHUSUS PLACER (Override Config.lsp jika diperlukan)
;; ---------------------------------------------------------------------------
(setq *License-File-Name* "sysFTTH.cfg")           ;; File lisensi khusus Placer
(setq *License-Secret-Key* "FIBERHOME_PRO_2026")   ;; Secret key Placer
(setq *License-Dialog-Title* "Aktivasi FTTH Master Placer")
(setq *License-Price-Text* "Harga: Rp 200.000 (Lifetime)")
(setq *TemanQRIS-Description* "Aktivasi FTTH Pro")

;; Re-inisialisasi dengan konfigurasi yang sudah diupdate
(Initialize-License-System)

;; ===========================================================================
;; COMMAND UTAMA: PLACER
;; ===========================================================================

(defun c:PLACER (/ *error* adoc temp_obj ss_basemap old_os old_nomutt count 
                   min_area dist_tolerance placed_centroids ent_last 
                   new_ent obj p1 p2 ang_line len_line search_points 
                   pt_test b_obj ss_exist total i success_in_line
                   prefix start_num cur_num str_num final_text is_pro machine_id 
                   stop_trial t_layer software_id existing_count trial_remaining)
  (vl-load-com)
  
  (setvar "NOMUTT" 0) (setvar "CMDECHO" 1)
  (setq adoc (vla-get-ActiveDocument (vlax-get-acad-object)))
  
  ;; Ambil ID dari modul lisensi (menggantikan get-machine-id & scramble-id inline)
  (setq machine_id (Get-Machine-ID))
  (setq software_id (Generate-Software-ID machine_id))
  (setq is_pro (Check-License-File machine_id))

  (defun *error* (msg)
    (if (not (member msg '("Function cancelled" "quit / exit abort")))
      (princ (strcat "\nError: " msg)))
    (setvar "NOMUTT" 0) (setvar "CMDECHO" 1) (setvar "OSMODE" old_os)
    (vl-cmdf "_.REDRAW") (vla-EndUndoMark adoc) (princ)
  )

  (princ "\n==============================================")
  (princ "\n   FTTH MASTER PLACER PRO - Syaiful Wachid    ")
  (if is_pro 
    (princ "\n   Status: [PRO VERSION - ACTIVE]             ")
    (princ (strcat "\n   Software ID: " software_id " (TRIAL) ")))
  (princ "\n==============================================")

  (setq prefix (getstring t "\nMasukkan Awalan (contoh NN-): "))
  (if (= prefix "") (setq prefix "NN-"))
  (setq start_num (getint "\nNomor Urut Mulai (contoh 1): "))
  (if (not start_num) (setq start_num 1))
  (setq cur_num start_num)

  (princ "\n[1] Pilih MText sebagai template: ")
  (setq sel_template (ssget ":S" '((0 . "MTEXT,TEXT"))))
  (if (not sel_template) (progn (princ "\nBatal.") (exit)) (setq temp_obj (vlax-ename->vla-object (ssname sel_template 0))))
  (setq t_layer (vla-get-Layer temp_obj))

  (vla-StartUndoMark adoc)
  (setq old_os (getvar "OSMODE")) (setq old_nomutt (getvar "NOMUTT"))
  (setvar "OSMODE" 0) (setvar "CMDECHO" 0)

  (setq placed_centroids '())
  (setq ss_exist (ssget "_X" (list '(0 . "MTEXT,TEXT") (cons 8 t_layer))))
  (if ss_exist
    (progn
      (setq i 0)
      (repeat (sslength ss_exist)
        (setq e_vla (vlax-ename->vla-object (ssname ss_exist i)))
        (setq ins_pt (vl-catch-all-apply 'vlax-get-property (list e_vla (if (= (vla-get-ObjectName e_vla) "AcDbMText") "InsertionPoint" "TextAlignmentPoint"))))
        (if (not (vl-catch-all-error-p ins_pt)) (setq placed_centroids (cons (vlax-safearray->list (vlax-variant-value ins_pt)) placed_centroids)))
        (setq i (1+ i))
      )
    )
  )

  ;; -----------------------------------------------------------------------
  ;; TRIAL GUARD: Hitung total existing label di layer ini
  ;; Jika mode Trial dan existing sudah >= 30, langsung stop sebelum generate
  ;; Ini menutup celah user yang generate berulang kali untuk bypass limit
  ;; -----------------------------------------------------------------------
  (setq existing_count (if ss_exist (sslength ss_exist) 0))
  (if (and (not is_pro) (>= existing_count 30))
    (progn
      (setvar "NOMUTT" 0) (setvar "CMDECHO" 1) (setvar "OSMODE" old_os)
      (vla-EndUndoMark adoc)
      (princ (strcat "\n[TRIAL] Layer ini sudah memiliki " (itoa existing_count) " label (batas 30)."))
      (Send-Trial-Expired-Notification software_id existing_count)
      (Show-License-Dialog)
      (exit)
    )
  )
  ;; Sisa kuota trial = 30 - existing_count
  (setq trial_remaining (- 30 existing_count))

  (princ "\n[3] Pilih Basemap: ")
  (if (setq ss_basemap (ssget '((0 . "LINE,LWPOLYLINE,POLYLINE"))))
    (progn
      (setq total (sslength ss_basemap) count 0 i 0 stop_trial nil)
      (setvar "NOMUTT" 1) 
      (while (and (< i total) (not stop_trial))
        ;; Gunakan trial_remaining sebagai batas, bukan hardcode 30
        (if (and (not is_pro) (>= count trial_remaining)) (setq stop_trial t) 
          (progn
            (if (= (rem i 20) 0) (progn (setvar "NOMUTT" 0) (princ (strcat "\rProses: " (itoa i) " / " (itoa total) " | Berhasil: " (itoa count) "   ")) (setvar "NOMUTT" 1) (gc)))
            (setq ent (ssname ss_basemap i) obj (vlax-ename->vla-object ent) success_in_line nil)
            (setq p1 (vl-catch-all-apply 'vlax-curve-getStartPoint (list obj)) p2 (vl-catch-all-apply 'vlax-curve-getEndPoint (list obj)))
            (if (and p1 p2 (not (vl-catch-all-error-p p1)))
              (progn
                (setq ang_line (angle p1 p2) len_line (vlax-curve-getDistAtParam obj (vlax-curve-getEndParam obj)))
                (if (> len_line 0.05)
                  (progn
                    (setq search_points (list (polar (vlax-curve-getPointAtDist obj (* len_line 0.5)) (+ ang_line (/ pi 2)) 0.7)
                                              (polar (vlax-curve-getPointAtDist obj (* len_line 0.5)) (- ang_line (/ pi 2)) 0.7)))
                    (foreach pt_test search_points
                      (if (and pt_test (not success_in_line))
                        (progn
                          (setq is_near nil) (foreach pc placed_centroids (if (< (distance pt_test pc) 1.0) (setq is_near t)))
                          (if (not is_near)
                            (progn
                              (setq ent_last (entlast)) (vl-cmdf "-boundary" pt_test "") (setq new_ent (entnext ent_last))
                              (while new_ent
                                (setq b_obj (vlax-ename->vla-object new_ent))
                                (if (and (not (vlax-erased-p b_obj)) (= (vla-get-ObjectName b_obj) "AcDbPolyline"))
                                  (progn
                                    (setq area (vla-get-area b_obj))
                                    (if (> area 0.8)
                                      (progn
                                        (setq coords (vlax-get b_obj 'Coordinates) k 0 max_seg_len 0.0 best_ang 0.0)
                                        (while (< k (- (length coords) 2))
                                          (setq pa (list (nth k coords) (nth (+ k 1) coords)) pb (list (nth (+ k 2) coords) (nth (+ k 3) coords)) cur_seg_len (distance pa pb))
                                          (if (> cur_seg_len max_seg_len) (setq max_seg_len cur_seg_len best_ang (angle pa pb))) (setq k (+ k 2))
                                        )
                                        (while (> best_ang (/ pi 2)) (setq best_ang (- best_ang pi))) (while (<= best_ang (/ pi -2)) (setq best_ang (+ best_ang pi)))
                                        (vla-GetBoundingBox b_obj 'minpt 'maxpt)
                                        (setq centroid (list (/ (+ (car (vlax-safearray->list minpt)) (car (vlax-safearray->list maxpt))) 2.0) (/ (+ (cadr (vlax-safearray->list minpt)) (cadr (vlax-safearray->list maxpt))) 2.0) 0.0))
                                        (setq is_dup nil) (foreach pc placed_centroids (if (< (distance centroid pc) 1.0) (setq is_dup t)))
                                        (if (not is_dup)
                                          (progn
                                            (setq placed_centroids (cons centroid placed_centroids) txt_obj (vla-Copy temp_obj))
                                            (vla-put-AttachmentPoint txt_obj acAttachmentPointMiddleCenter)
                                            (setq str_num (itoa cur_num)) (while (< (strlen str_num) 3) (setq str_num (strcat "0" str_num)))
                                            (vla-put-TextString txt_obj (strcat prefix str_num))
                                            (vla-put-InsertionPoint txt_obj (vlax-3d-point centroid)) (vla-put-Rotation txt_obj best_ang)
                                            (setq cur_num (1+ cur_num) count (1+ count) success_in_line t)
                                          )
                                        )
                                      )
                                    )
                                    (vla-Delete b_obj)
                                  )
                                )
                                (setq new_ent (entnext new_ent))
                              )
                            )
                          )
                        )
                      )
                    )
                  )
                )
              )
            )
            (setq i (1+ i))
          )
        )
      )
      (setvar "NOMUTT" 0) (vl-cmdf "_.REDRAW")

      ;; Kirim laporan penggunaan secara silent ke Telegram (untuk research)
      ;; Tidak ada popup/notifikasi yang muncul ke user
      (vl-catch-all-apply
        '(lambda ()
           (Send-Usage-Report
             software_id
             (vl-filename-base (getvar "DWGNAME"))  ;; Nama file DWG tanpa path
             (+ existing_count count)               ;; Total plot (existing + baru)
             is_pro
           )
        )
      )

      ;; Tampilkan dialog lisensi jika trial habis
      (if stop_trial (Show-License-Dialog))
      (princ (strcat "\nSelesai! " (itoa count) " label terpasang."))
    )
  )
  (vla-EndUndoMark adoc) (setvar "OSMODE" old_os) (setvar "CMDECHO" 1) (princ)
)

;; ===========================================================================
;; COMMAND: AKTIVASI
;; Deskripsi: Aktivasi manual dengan kode dari admin
;; ===========================================================================
(defun c:AKTIVASI (/)
  (Show-Manual-Activation-Dialog)
  (princ)
)

;; ===========================================================================
;; COMMAND: PLACER_INFO
;; Deskripsi: Tampilkan dialog info/aktivasi lisensi
;; ===========================================================================
(defun c:PLACER_INFO (/)
  (Show-License-Dialog)
  (princ)
)

;; ===========================================================================
;; COMMAND: PLACERHELP
;; Deskripsi: Tampilkan panduan penggunaan FTTH Master Placer
;; ===========================================================================
(defun c:PLACERHELP (/)
  (Show-Help-Dialog "FTTH Master Placer" "5.2")
  (princ)
)

(princ "\n==============================================")
(princ "\n   FTTH MASTER PLACER PRO v5.2 Loaded        ")
(princ "\n   Commands: PLACER, AKTIVASI, PLACER_INFO   ")
(princ "\n             PLACERHELP, LICENSEINFO         ")
(princ "\n==============================================")
(princ)
