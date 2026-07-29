;;; ==========================================================================
;;; LVSMOOTH - Live Smooth Polyline
;;; ==========================================================================
;;; Deskripsi: Menghaluskan polyline (hasil digitasi) secara interaktif
;;; dengan slider untuk tingkat iterasi (Chaikin's Algorithm) dan filter jarak.
;;; Segmen yang melebihi batas jarak (misal: garis lurus antar tikungan) 
;;; akan dipertahankan persis seperti aslinya.
;;; ==========================================================================

(defun c:LSMOOTH (/ ent ent-data orig-pts dcl-file dcl-id iter max-dist update-preview accept new-pts)
  (vl-load-com)
  
  ;; 1. Pemilihan Objek
  (setq ent (car (entsel "\nPilih LWPolyline untuk dihaluskan: ")))
  (if (not ent)
    (progn (princ "\nTidak ada objek yang dipilih.") (exit))
  )
  (setq ent-data (entget ent))
  (if (/= (cdr (assoc 0 ent-data)) "LWPOLYLINE")
    (progn (alert "Objek yang dipilih bukan LWPolyline.") (exit))
  )
  
  ;; 2. Ekstrak Titik Vertex Asli
  (setq orig-pts nil)
  (foreach item ent-data
    (if (= (car item) 10)
      (setq orig-pts (cons (cdr item) orig-pts))
    )
  )
  (setq orig-pts (reverse orig-pts))
  
  ;; 3. Nilai Awal Parameter
  (setq iter 2)
  (setq max-dist 5.0) ; Default batas maksimum jarak (5 Meter)
  (setq accept nil)
  
  ;; 4. Fungsi Update Preview
  (defun update-preview ()
    (set_tile "txt_iter" (itoa iter))
    (set_tile "eb_dist" (rtos max-dist 2 2))
    
    ;; Proses Titik
    (setq new-pts (process-points orig-pts max-dist iter))
    
    ;; Update Polyline di Layar
    (update-lwpolyline ent new-pts)
  )
  
  ;; 5. Persiapan dan Load DCL
  (setq dcl-file (write-dcl-lvsmooth))
  (setq dcl-id (load_dialog dcl-file))
  (if (not (new_dialog "lvsmooth_dlg" dcl-id))
    (progn (alert "Gagal memuat dialog DCL.") (exit))
  )
  
  ;; 6. Inisialisasi Nilai DCL
  (set_tile "sld_iter" (itoa iter))
  (set_tile "sld_dist" (itoa (fix (* max-dist 10.0))))
  (set_tile "eb_dist" (rtos max-dist 2 2))
  
  ;; 7. Action Tiles (Event Listener)
  (action_tile "sld_iter" "(setq iter (atoi $value)) (update-preview)")
  ;; Slider untuk desimal: range 1-1000 mewakili 0.1m - 100.0m
  (action_tile "sld_dist" "(setq max-dist (/ (atof $value) 10.0)) (update-preview)")
  (action_tile "eb_dist" "(setq max-dist (atof $value)) (set_tile \"sld_dist\" (itoa (fix (* max-dist 10.0)))) (update-preview)")
  
  (action_tile "accept" "(setq accept T) (done_dialog)")
  (action_tile "cancel" "(done_dialog)")
  
  ;; Eksekusi awal untuk preview pertama
  (update-preview)
  
  ;; 8. Tampilkan Dialog
  (start_dialog)
  
  ;; 9. Pembersihan
  (unload_dialog dcl-id)
  (vl-file-delete dcl-file)
  
  ;; 10. Hasil Akhir
  (if accept
    (princ "\nPenghalusan Polyline selesai diterapkan.")
    (progn
      (princ "\nDibatalkan. Mengembalikan Polyline ke bentuk semula.")
      (update-lwpolyline ent orig-pts)
    )
  )
  (princ)
)

;;; ==========================================================================
;;; FUNGSI PENDUKUNG
;;; ==========================================================================

;; Fungsi untuk menulis file DCL sementara
(defun write-dcl-lvsmooth (/ dcl-file f)
  (setq dcl-file (vl-filename-mktemp "lvsmooth.dcl"))
  (setq f (open dcl-file "w"))
  (write-line "lvsmooth_dlg : dialog {" f)
  (write-line "    label = \"Live Polyline Smoother\";" f)
  (write-line "    : boxed_column {" f)
  (write-line "        label = \"Parameter Penghalusan\";" f)
  (write-line "        : row {" f)
  (write-line "            : text { label = \"Tingkat Kehalusan (Iterasi):\"; width = 28; }" f)
  (write-line "            : slider { key = \"sld_iter\"; min_value = 0; max_value = 6; small_increment = 1; width = 15; }" f)
  (write-line "            : text { key = \"txt_iter\"; value = \"0\"; width = 3; }" f)
  (write-line "        }" f)
  (write-line "        : row {" f)
  (write-line "            : text { label = \"Batas Jarak Maks. (Meter):\"; width = 28; }" f)
  (write-line "            : slider { key = \"sld_dist\"; min_value = 1; max_value = 1000; small_increment = 1; width = 15; }" f)
  (write-line "            : edit_box { key = \"eb_dist\"; edit_width = 6; }" f)
  (write-line "        }" f)
  (write-line "    }" f)
  (write-line "    ok_cancel;" f)
  (write-line "}" f)
  (close f)
  dcl-file
)

;; Fungsi untuk memodifikasi LWPolyline di AutoCAD secara live (entmod)
(defun update-lwpolyline (ent new-pts / ent-data new-data verts)
  (setq ent-data (entget ent))
  (setq new-data nil)
  
  ;; Pertahankan semua data kecuali vertex (10, 40, 41, 42) dan jumlah vertex (90, 91)
  (foreach item ent-data
    (if (not (member (car item) '(90 10 40 41 42 91)))
      (setq new-data (cons item new-data))
    )
  )
  (setq new-data (reverse new-data))
  
  ;; Buat struktur list vertex baru
  (setq verts (list (cons 90 (length new-pts))))
  (foreach pt new-pts
    (setq verts (cons (cons 10 (list (car pt) (cadr pt))) verts))
  )
  (setq verts (reverse verts))
  
  ;; Gabungkan dan terapkan perubahan ke layar
  (setq new-data (append new-data verts))
  (entmod new-data)
  (entupd ent)
)

;; Logika Utama: Memecah berdasarkan jarak lalu menghaluskan segmen yang rapat - O(N) Performance
(defun process-points (pts max-dist iterations / current-sequence final-pts p1 p2 dist smoothed remain-pts)
  (if (= iterations 0)
    pts
    (progn
      (setq final-pts nil)
      (setq current-sequence (list (car pts)))
      (setq remain-pts pts)
      
      (while (cdr remain-pts)
        (setq p1 (car remain-pts))
        (setq p2 (cadr remain-pts))
        (setq dist (distance p1 p2))
        
        (if (<= dist max-dist)
          ;; Jarak pendek (misal tikungan): masukkan ke sequence
          (setq current-sequence (cons p2 current-sequence))
          (progn
            ;; Jarak > Max. Haluskan yang terkumpul.
            (setq current-sequence (reverse current-sequence))
            (setq smoothed (smooth-chaikin current-sequence iterations))
            
            ;; Hindari titik duplikat di titik potong antar segmen
            (if final-pts
              (setq smoothed (cdr smoothed))
            )
            
            ;; Gabungkan ke list titik akhir (dibalik untuk performa O(N))
            (foreach pt smoothed
              (setq final-pts (cons pt final-pts))
            )
            
            ;; Mulai sequence baru dari ujung segmen lurus ini
            (setq current-sequence (list p2))
          )
        )
        (setq remain-pts (cdr remain-pts))
      )
      
      ;; Proses sisa sequence terakhir
      (setq current-sequence (reverse current-sequence))
      (setq smoothed (smooth-chaikin current-sequence iterations))
      
      (if final-pts
        (setq smoothed (cdr smoothed))
      )
      
      (foreach pt smoothed
        (setq final-pts (cons pt final-pts))
      )
      
      (reverse final-pts)
    )
  )
)

;; Algoritma Chaikin's Corner Cutting (Satu Langkah/Iterasi) - O(N) Performance
(defun chaikin-step (pts / newpts p1 p2 q r remain-pts is-first)
  (if (< (length pts) 3)
    pts ; Tidak bisa dihaluskan jika kurang dari 3 titik
    (progn
      (setq newpts (list (car pts))) ; Ujung awal selalu tetap
      (setq remain-pts pts)
      (setq is-first T)
      
      (while (cdr remain-pts)
        (setq p1 (car remain-pts))
        (setq p2 (cadr remain-pts))
        
        ;; Hitung titik kontrol 1/4 dan 3/4
        (setq q (list (+ (* 0.75 (car p1)) (* 0.25 (car p2)))
                      (+ (* 0.75 (cadr p1)) (* 0.25 (cadr p2)))))
        (setq r (list (+ (* 0.25 (car p1)) (* 0.75 (car p2)))
                      (+ (* 0.25 (cadr p1)) (* 0.75 (cadr p2)))))
        
        (if (not is-first)
          (setq newpts (cons q newpts))
        )
        
        (if (cddr remain-pts)
          (setq newpts (cons r newpts))
        )
        
        (setq is-first nil)
        (setq remain-pts (cdr remain-pts))
      )
      (setq newpts (cons (last pts) newpts)) ; Ujung akhir selalu tetap
      (reverse newpts)
    )
  )
)

;; Loop Algoritma Chaikin sesuai jumlah Iterasi
(defun smooth-chaikin (pts iterations / result i)
  (setq result pts)
  (setq i 0)
  (while (< i iterations)
    (setq result (chaikin-step result))
    (setq i (1+ i))
  )
  result
)

(princ "\nModule LiveSmoothPoly ter-load. Ketik LSMOOTH untuk mulai.")
(princ)
