;;; ==============================================================================
;;; PROGRAM: FTTH Grouping & Boundary Tool
;;; BAHASA: AutoLISP
;;; FUNGSI: Mengelompokkan MTEXT/TEXT secara interaktif dan membuat garis batas 
;;;         (boundary) yang mengikuti bentuk ruas/garis rumah di sekitarnya.
;;; ==============================================================================

(vl-load-com)

;; Variabel Global
(if (not *ftth-color-index*) (setq *ftth-color-index* 0))
(if (not *ftth-group-counter*) (setq *ftth-group-counter* 1))
(setq *ftth-colors* '(1 2 3 4 5 6 30))

;;; ==============================================================================
;;; COMMAND: FTTH-GROUP
;;; ==============================================================================
(defun c:FTTH-GROUP ( / ss e i count master_list current_group grp_msg exit_loop cmdecho_old nomutt_old)
  (setq cmdecho_old (getvar "CMDECHO"))
  (setq nomutt_old (getvar "NOMUTT"))
  (setvar "CMDECHO" 0)
  
  (setq master_list '())
  (setq exit_loop nil)
  
  (princ "\n--- FTTH TEXT GROUPING (Otomatis per 16 Teks) ---")
  (princ "\nSeleksi Teks satu-per-satu atau sekalian banyak. Batas per grup adalah 16.")
  
  ;; Looping Seleksi Interaktif
  (while (not exit_loop)
    (setq count (length master_list))
    (setq grp_msg (strcat "\n[\t" (itoa count) " / 16 \t] Seleksi teks [Tekan ENTER untuk akhiri/Group sisa]: "))
    (princ grp_msg)
    
    (setvar "NOMUTT" 1)
    (setq ss (vl-catch-all-apply 'ssget (list ":S" '((0 . "TEXT,MTEXT")))))
    (setvar "NOMUTT" 0)
    
    (if (vl-catch-all-error-p ss)
      (progn
        ;; User menekan ESC
        (foreach e master_list (redraw e 4)) ;; Hilangkan highlight teks yang batal di-group
        (setq exit_loop T)
      )
      (if (not ss)
        ;; User menekan Enter / Spasi (ssget mengembalikan nil)
        (if (> (length master_list) 0)
          (progn
            (ftth:process_group master_list)
            (setq master_list '())
          )
        )
        ;; User menyeleksi sesuatu
        (progn
          ;; Tambahkan teks terpilih ke master_list tanpa duplikasi
          (setq i 0)
          (while (< i (sslength ss))
            (setq e (ssname ss i))
            (if (not (vl-position e master_list))
              (progn
                (setq master_list (append master_list (list e)))
                (redraw e 3)
              )
            )
            (setq i (1+ i))
          )
          
          ;; Potong dan Group jika jumlahnya mencapai 16
          (while (>= (length master_list) 16)
            (setq current_group '())
            (repeat 16
              (setq current_group (append current_group (list (car master_list))))
              (setq master_list (cdr master_list))
            )
            (ftth:process_group current_group)
          )
        )
      )
    )
  )
  
  (setvar "CMDECHO" cmdecho_old)
  (setvar "NOMUTT" nomutt_old)
  (princ "\n[ Selesai ] Proses FTTH-GROUP berakhir.\n")
  (princ)
)

;; Fungsi Pemroses dan Analisa Group
(defun ftth:process_group (lst / pts p1 p2 d max_d clr grp_name ed pt10 acadObj doc grps existingGrp newGrp objs_array i vla_e)
  (setq pts '())
  (foreach e lst
    (setq ed (entget e))
    ;; Ambil titik kordinat sisip teks
    (setq pt10 (cdr (assoc 10 ed)))
    (setq pts (cons pt10 pts))
  )
  
  ;; Kalkulasi jarak maksimal
  (setq max_d 0.0)
  (foreach p1 pts
    (foreach p2 pts
      (setq d (distance p1 p2))
      (if (> d max_d) (setq max_d d))
    )
  )
  
  ;; Penentuan Warna dan Nama
  (setq clr (nth *ftth-color-index* *ftth-colors*))
  (setq *ftth-color-index* (rem (1+ *ftth-color-index*) (length *ftth-colors*)))
  
  (setq grp_name (strcat "FTTH-GRP_" (itoa *ftth-group-counter*)))
  (setq *ftth-group-counter* (1+ *ftth-group-counter*))
  
  ;; Implementasi Auto Warna via VLA untuk menghindari command prompt nyangkut
  (foreach e lst
    (redraw e 4) ;; Hilangkan mode Highlight
    (setq vla_e (vlax-ename->vla-object e))
    (vl-catch-all-apply 'vla-put-color (list vla_e clr))
  )
  
  ;; Pembuatan Group secara Native API VBA/COM (Tanpa interaksi Command Line)
  (setq acadObj (vlax-get-acad-object))
  (setq doc (vla-get-ActiveDocument acadObj))
  (setq grps (vla-get-Groups doc))
  
  ;; Hapus jika grup dengan nama tersebut sudah ada (fallback layer keamanan)
  (setq existingGrp (vl-catch-all-apply 'vla-item (list grps grp_name)))
  (if (not (vl-catch-all-error-p existingGrp))
    (vl-catch-all-apply 'vla-delete (list existingGrp))
  )
  
  ;; Buat grup baru
  (setq newGrp (vl-catch-all-apply 'vla-Add (list grps grp_name)))
  
  (if (not (vl-catch-all-error-p newGrp))
    (progn
      ;; Rangkum objek ke dalam Array untuk dimasukkan ke grup
      (setq objs_array (vlax-make-safearray vlax-vbObject (cons 0 (1- (length lst)))))
      (setq i 0)
      (foreach e lst
        (vlax-safearray-put-element objs_array i (vlax-ename->vla-object e))
        (setq i (1+ i))
      )
      ;; Gabungkan objek ke dalam Group yg baru dibuat
      (vl-catch-all-apply 'vla-AppendItems (list newGrp objs_array))
    )
  )
  
  ;; Laporan
  (princ (strcat "\n>>> Berhasil Group: " grp_name " (" (itoa (length lst)) " ODP). Jarak Terjauh: " (rtos max_d 2 2) "m."))
  
  ;; Peringatan Jarak
  (if (> max_d 50.0)
    (princ " *** PERINGATAN: JARAK MELEBIHI 50 METER! ***")
  )
)

;;; Helper: Mendapatkan seluruh daftar group beserta membernya via VLA COM API
(defun ftth:get-all-groups ( / doc groups grp_list items i obj e)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))
  (setq groups (vla-get-groups doc))
  (setq grp_list '())
  (vlax-for grp groups
    (if (> (vla-get-count grp) 0)
      (progn
        (setq items '())
        (setq i 0)
        (while (< i (vla-get-count grp))
          (setq obj (vl-catch-all-apply 'vla-item (list grp i)))
          (if (and (not (vl-catch-all-error-p obj)) obj)
            (setq items (cons (vlax-vla-object->ename obj) items))
          )
          (setq i (1+ i))
        )
        (if items (setq grp_list (cons items grp_list)))
      )
    )
  )
  grp_list
)

;;; Helper: Menggabungkan (Merge) kumpulan Group Batch yang saling berbagi anggota (Super-Group Detection)
(defun ftth:merge-intersecting-batches (batches / res merged temp b1 b2 can_merge e)
  (setq res '())
  (while batches
    (setq b1 (car batches))
    (setq batches (cdr batches))
    
    (setq merged T)
    (while merged
      (setq merged nil)
      (setq temp '())
      (foreach b2 batches
        (setq can_merge nil)
        (foreach e b1
          (if (vl-position e b2) (setq can_merge T))
        )
        (if can_merge
          (progn
            ;; Gabungkan anggota dari b2 ke b1 tanpa duplikat
            (foreach e b2
              (if (not (vl-position e b1))
                (setq b1 (cons e b1))
              )
            )
            (setq merged T)
          )
          (setq temp (cons b2 temp))
        )
      )
      (setq batches temp)
    )
    (setq res (cons b1 res))
  )
  res
)

;;; ==============================================================================
;;; COMMAND: FTTH-BOUNDARY
;;; ==============================================================================
(defun c:FTTH-BOUNDARY ( / ss i e selected_ents all_groups batch_list processed_ents current_batch has_match ungrouped_batch cmdecho_old delobj_old pd_old hpb_old hg_old exp_old hpid_old )
  (setq cmdecho_old (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)
  (setq delobj_old (getvar "DELOBJ"))
  (setq pd_old (getvar "PEDITACCEPT"))
  (setq hpb_old (getvar "HPBOUND"))
  (setq hg_old (getvar "HPGAPTOL"))
  
  (setq exp_old (getvar "EXPERT"))
  (setvar "EXPERT" 5)
  (setq hpid_old (vl-catch-all-apply 'getvar (list "HPISLANDDETECTION")))
  (if (not (vl-catch-all-error-p hpid_old)) (vl-catch-all-apply 'setvar (list "HPISLANDDETECTION" 2)))
  
  (princ "\nPilih Teks atau Group yang ingin dibuat Boundary percil-nya: ")
  ;; Filter hanya Text 
  (setq ss (ssget '((0 . "TEXT,MTEXT"))))
  (if (not ss)
    (progn (princ "\nTidak ada yang dipilih. Dibatalkan.") (exit))
  )
  
  (setq selected_ents '())
  (setq i 0)
  (while (< i (sslength ss))
    (setq e (ssname ss i))
    (setq selected_ents (cons e selected_ents))
    (setq i (1+ i))
  )
  
  ;; DETEKSI GRUP MENGGUNAKAN COM API AMAN
  (setq all_groups (ftth:get-all-groups))
  (setq batch_list '())
  (setq processed_ents '())
  
  (foreach grp_items all_groups
    (setq current_batch '())
    (setq has_match nil)
    ;; Cek apakah ada satupun entitas terpilih yang masuk di grup ini
    (foreach e selected_ents
      (if (vl-position e grp_items)
        (setq has_match T)
      )
    )
    
    (if has_match
      (progn
        ;; Kumpulkan seluruh member dari grup tsb (termasuk yg tidak diselect)
        (foreach e grp_items
          (if (vl-position (cdr (assoc 0 (entget e))) '("TEXT" "MTEXT"))
             (progn
               (if (not (vl-position e current_batch)) (setq current_batch (cons e current_batch)))
               (if (not (vl-position e processed_ents)) (setq processed_ents (cons e processed_ents)))
             )
          )
        )
        (if current_batch (setq batch_list (cons current_batch batch_list)))
      )
    )
  )
  
  ;; Batch untuk teks liar (yang sama sekali belum di-FTTH-GROUP)
  (setq ungrouped_batch '())
  (foreach e selected_ents
    (if (not (vl-position e processed_ents))
       (setq ungrouped_batch (cons e ungrouped_batch))
    )
  )
  (if ungrouped_batch (setq batch_list (cons ungrouped_batch batch_list)))
  
  ;; SATUKAN BATCH OVERLAP: Jika user men-group ulang objek sehingga beberapa Group berbagi member, LEBURKAN!
  (setq batch_list (ftth:merge-intersecting-batches batch_list))
  
  (princ (strcat "\n>> Ditemukan " (itoa (length batch_list)) " Batch Group (Setelah evaluasi Sub-Group). Mulai memproses Boundary cerdas..."))
  
  ;; Datar-kan semua kelompok teks agar bisa dijadikan pelindung "hak milik"
  (setq flat_all_groups '())
  (foreach grp all_groups
    (foreach e grp (setq flat_all_groups (cons e flat_all_groups)))
  )
  
  ;; PROSES SETIAP BATCH SECARA INDEPENDEN MENGGUNAKAN SMART OVERLAP FILTERING
  (setq global_processed '())
  (setq remaining_batches batch_list)
  
  (while remaining_batches
    (setq current_batch (car remaining_batches))
    (setq remaining_batches (cdr remaining_batches))
    
    ;; Saring current_batch untuk menghilangkan teks yang sudah dieksekusi oleh Convex Hull dari Batch sebelumnya
    (setq clean_batch '())
    (foreach e current_batch
      (if (not (vl-position e global_processed))
        (setq clean_batch (cons e clean_batch))
      )
    )
    
    (if clean_batch
      (progn
        ;; Proses batch ini. Convex Hull akan meng-ekspansi teks terdekat yang LIAR saja.
        (setq final_ents (ftth:process_single_boundary_batch clean_batch flat_all_groups))
        
        ;; Tandai semua entitas (termasuk yang direkrut paksa oleh Convex Hull) sebagai "Sudah Diproses"
        (foreach e final_ents
          (if (not (vl-position e global_processed))
            (setq global_processed (cons e global_processed))
          )
        )
      )
    )
  )

  ;; Restorasi Monitor Zoom dan Global Variables
  (vl-cmdf "_.ZOOM" "_P")
  (setvar "CMDECHO" cmdecho_old)
  (setvar "DELOBJ" delobj_old)
  (setvar "PEDITACCEPT" pd_old)
  (setvar "EXPERT" exp_old)
  (vl-catch-all-apply 'setvar (list "HPBOUND" hpb_old))
  (if (not (vl-catch-all-error-p hpid_old)) (vl-catch-all-apply 'setvar (list "HPISLANDDETECTION" hpid_old)))
  (setvar "HPGAPTOL" hg_old)
  (princ "\nPerintah FTTH-BOUNDARY Selesai Total.")
  (princ)
)

;; Helper: Ekstrak Handle untuk Debug
(defun ftth:get-handle (vla_obj / h)
  (setq h (vl-catch-all-apply 'vla-get-Handle (list vla_obj)))
  (if (vl-catch-all-error-p h) "DELETED" h)
)

;; Helper: Stepped Offset (Mencicil offset panjang agar CAD tidak melintir/error membentuk simpul)
(defun ftth:step_offset (vla_pl dist steps / step_val current_objs next_objs off o_arr new_arr id res clean_next a)
  (setq step_val (/ dist (float steps)))
  (setq current_objs (list vla_pl))
  
  (setq s 0)
  (while (and current_objs (< s steps))
    (setq next_objs '())
    (foreach obj current_objs
      (setq off (vl-catch-all-apply 'vla-offset (list obj step_val)))
      (if (not (vl-catch-all-error-p off))
        (progn
          (setq o_arr (vlax-safearray->list (vlax-variant-value off)))
          (setq next_objs (append next_objs o_arr))
        )
      )
    )
    
    ;; Filter loop sampah (luas < 1.0) hasil dari self-intersection agar tidak mengganggu akurasi geometri
    (setq clean_next '())
    (foreach o next_objs
      (setq a (vl-catch-all-apply 'vla-get-area (list o)))
      (if (and (not (vl-catch-all-error-p a)) (> a 1.0))
        (setq clean_next (cons o clean_next))
        (vl-catch-all-apply 'vla-delete (list o))
      )
    )
    (setq next_objs clean_next)
    
    ;; Bersihkan bayangan step sebelumnya agar tidak menumpuk di file (jangan delete aslinya)
    (if (not (equal current_objs (list vla_pl)))
      (foreach o current_objs (vl-catch-all-apply 'vla-delete (list o)))
    )
    
    (setq current_objs next_objs)
    (setq s (1+ s))
  )
  
  (if current_objs
    (progn
      (setq new_arr (vlax-make-safearray vlax-vbObject (cons 0 (1- (length current_objs)))))
      (setq id 0)
      (foreach o current_objs
        (vlax-safearray-put-element new_arr id o)
        (setq id (1+ id))
      )
      (setq res (vlax-make-variant new_arr))
    )
    (setq res (vl-catch-all-apply 'abs (list nil))) ;; Bypass error trigger
  )
  res
)

;; Helper: Hacking Geometri Ide User - Membuat celah (Gap 1m) di tengah tembok, Offset, Tutup kembali + Eliminasi Sampah
(defun ftth:force_hack_offset (pl_obj dist / coords len i pt_list A B L dx dy Pmid_x Pmid_y P1x P1y P2x P2y v_list s_idx new_coords x temp_pl off_test res success objs max_a best_o a new_arr doc ms)
  (setq success nil)
  (setq res (vl-catch-all-apply 'abs (list nil))) ; error object default
  (if (= (vla-get-ObjectName pl_obj) "AcDbPolyline")
    (progn
      (setq coords (vlax-safearray->list (vlax-variant-value (vla-get-Coordinates pl_obj))))
      (setq pt_list '())
      (setq i 0)
      (while (< i (length coords))
        (setq pt_list (append pt_list (list (list (nth i coords) (nth (1+ i) coords)))))
        (setq i (+ i 2))
      )
      (setq len (length pt_list))
      (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
      (setq ms (vla-get-ModelSpace doc))
      
      ;; Sisir seluruh dinding, cari yang paling panjang
      (setq max_L -1.0 longest_i -1)
      (setq i 0)
      (while (< i len)
        (setq A (nth i pt_list))
        (setq B (nth (rem (1+ i) len) pt_list))
        (setq L (distance (list (car A) (cadr A) 0.0) (list (car B) (cadr B) 0.0)))
        (if (> L max_L)
          (progn
            (setq max_L L)
            (setq longest_i i)
          )
        )
        (setq i (1+ i))
      )
      
      ;; Gunting Celah HANYA di dinding TERPANJANG (Dipastikan aman dari gangguan vertex sudut)
      (if (> max_L 0.5)
        (progn
          (setq i longest_i)
          (setq A (nth i pt_list))
          (setq B (nth (rem (1+ i) len) pt_list))
          
          (setq dx (/ (- (car B) (car A)) max_L))
          (setq dy (/ (- (cadr B) (cadr A)) max_L))
          
          ;; GAP YANG SANGAT KECIL: Total celah hanya 10 cm (0.1 meter)
          ;; P1: mundur 0.05m dari tengah | P2: maju 0.05m dari tengah. 
          (setq Pmid_x (/ (+ (car A) (car B)) 2.0))
          (setq Pmid_y (/ (+ (cadr A) (cadr B)) 2.0))
          
          (setq P1x (- Pmid_x (* dx 0.05)))
          (setq P1y (- Pmid_y (* dy 0.05)))
          
          (setq P2x (+ Pmid_x (* dx 0.05)))
          (setq P2y (+ Pmid_y (* dy 0.05)))
          
          ;; Urutkan vertex BARU: P2 -> Sudut B -> C -> D -> A -> P1 (Poligon Terbuka tapi bentuk tetap sama)
          (setq v_list (list (list P2x P2y)))
          
          (setq s_idx 1)
          (while (< s_idx len)
             (setq v_list (append v_list (list (nth (rem (+ i s_idx) len) pt_list))))
             (setq s_idx (1+ s_idx))
          )
          (setq v_list (append v_list (list (list P1x P1y))))
          
          ;; Konversi Array Baru
          (setq new_coords (vlax-make-safearray vlax-vbDouble (cons 0 (1- (* (length v_list) 2)))))
          (setq x 0)
          (foreach pt v_list
            (vlax-safearray-put-element new_coords x (car pt))
            (vlax-safearray-put-element new_coords (1+ x) (cadr pt))
            (setq x (+ x 2))
          )
          
          (setq temp_pl (vla-addLightWeightPolyline ms new_coords))
          (vla-put-Closed temp_pl :vlax-false)
          
          (setq off_test (vl-catch-all-apply 'vla-offset (list temp_pl dist)))
          (if (not (vl-catch-all-error-p off_test))
            (progn
              (setq success T)
              (setq objs (vlax-safearray->list (vlax-variant-value off_test)))
              
              ;; Paksa jahit / tutup ujung lukanya! Otomatis nyambung lurus menutupi celah 10cm tadi
              (foreach o objs (vla-put-Closed o :vlax-true))
              
              ;; Filter Area Terbesar
              (setq max_a -1.0 best_o nil)
              (foreach o objs
                (setq a (vl-catch-all-apply 'vla-get-area (list o)))
                (if (and (not (vl-catch-all-error-p a)) (> a max_a))
                  (progn (setq max_a a) (setq best_o o))
                )
              )
              
              ;; Hapus sampah
              (foreach o objs
                (if (not (eq o best_o)) (vl-catch-all-apply 'vla-delete (list o)))
              )
              
              ;; Kemas Variant
              (if best_o
                (progn
                  (setq new_arr (vlax-make-safearray vlax-vbObject '(0 . 0)))
                  (vlax-safearray-put-element new_arr 0 best_o)
                  (setq res (vlax-make-variant new_arr))
                )
              )
            )
          )
          (vl-catch-all-apply 'vla-delete (list temp_pl))
        )
      )
    )
  )
  res
)

(defun ftth:process_single_boundary_batch (batch_entities flat_all_groups / i e ed lst pts minX maxX minY maxY pt hg poly_ss max_pl max_area line_ss reg_ss union_reg join_ss p1 p2 minp maxp vla_e a off1 off2 a1 a2 inward dilate_ss off_objs a0 outward off_res ent_trap all_joined all_corners hull_pts ss2 lst1 lst2 delobj_old try_dist)
  
  (if (not batch_entities) (exit))

  ;; 1. Ambil 4 sudut Bounding Box
  (setq all_corners '())
  (foreach e batch_entities
    (setq vla_e (vlax-ename->vla-object e))
    (setq minp nil maxp nil)
    (vl-catch-all-apply 'vla-getboundingbox (list vla_e 'minp 'maxp))
    (if (and minp maxp)
      (progn
        (setq p1 (vlax-safearray->list minp))
        (setq p2 (vlax-safearray->list maxp))
        (setq all_corners (cons p1 all_corners))
        (setq all_corners (cons p2 all_corners))
        (setq all_corners (cons (list (car p1) (cadr p2) 0.0) all_corners))
        (setq all_corners (cons (list (car p2) (cadr p1) 0.0) all_corners))
      )
    )
  )
  (if (not all_corners) (exit))

  ;; 2. Kalkulasi jaring Convex Hull
  (setq hull_pts (ftth:convex-hull all_corners))
  
  ;; 3. Zoom Wajib sebelum Crossing Polygon
  (setq minX 1e99 minY 1e99 maxX -1e99 maxY -1e99)
  (foreach pt hull_pts
    (if (< (car pt) minX) (setq minX (car pt)))
    (if (< (cadr pt) minY) (setq minY (cadr pt)))
    (if (> (car pt) maxX) (setq maxX (car pt)))
    (if (> (cadr pt) maxY) (setq maxY (cadr pt)))
  )
  (vl-cmdf "_.ZOOM" "_W" (list (- minX 20) (- minY 20)) (list (+ maxX 20) (+ maxY 20)))
  
  ;; 4. Tangkap sela-sela via CP (HANYA tangkap yang liar, JANGAN curi milik group tetangga!)
  (setq ss2 (vl-catch-all-apply 'ssget (list "_CP" hull_pts '((0 . "TEXT,MTEXT")))))
  (if (and ss2 (= (type ss2) 'PICKSET))
    (progn
      (setq i 0)
      (while (< i (sslength ss2))
        (setq e (ssname ss2 i))
        (if (not (vl-position e batch_entities))
          ;; Proteksi Hak Milik Grup: Jangan culik kalau dia terdaftar di grup orang lain
          (if (not (vl-position e flat_all_groups))
            (setq batch_entities (cons e batch_entities))
          )
        )
        (setq i (1+ i))
      )
    )
  )
  
  (ftth:ensure_layer "FAT AREA" 106)

  (setq lst '() pts '())
  (setq minX 1e99 minY 1e99 maxX -1e99 maxY -1e99)
  
  (foreach e batch_entities
    (setq lst (cons e lst))
    (setq ed (entget e))
    
    (setq minp nil maxp nil)
    (vl-catch-all-apply 'vla-getboundingbox (list (vlax-ename->vla-object e) 'minp 'maxp))
    (if (and minp maxp)
      (progn
        (setq p1 (vlax-safearray->list minp))
        (setq p2 (vlax-safearray->list maxp))
        (setq pt (list (/ (+ (car p1) (car p2)) 2.0) (/ (+ (cadr p1) (cadr p2)) 2.0)))
        
        ;; PENTING: Menyimpan koordinat p1 dan p2 untuk jaring pengaman Rectangle
        (setq pts (cons (list pt p1 p2) pts))
        
        (if (< (car p1) minX) (setq minX (car p1)))
        (if (< (cadr p1) minY) (setq minY (cadr p1)))
        (if (> (car p2) maxX) (setq maxX (car p2)))
        (if (> (cadr p2) maxY) (setq maxY (cadr p2)))
      )
    )
    
    ;; Sembunyikan
    (if (assoc 60 ed)
      (setq ed (subst '(60 . 1) (assoc 60 ed) ed))
      (setq ed (append ed '((60 . 1))))
    )
    (entmod ed)
  )
  
  (vl-cmdf "_.ZOOM" "_W" (list (- minX 20) (- minY 20)) (list (+ maxX 20) (+ maxY 20)))
  
  (setq hg (getvar "HPGAPTOL"))
  (setvar "HPGAPTOL" 0.5) 
  
  ;; PEMBENTUKAN BOUNDARY AREA
  (setq poly_ss (ssadd))
  (foreach item pts
    (setq pt (car item))
    (setq p1 (cadr item))
    (setq p2 (caddr item))

    (setq pl_ename nil)
    ;; Gunakan internal AutoLISP bpoly universal (tanpa parameter SS agar kompatibel dgn semua versi CAD)
    ;; Karena layar sudah di-Zoom secukupnya, kalkulasi boundary otomatis terbatas pada area yang tampak saja.
    (setq pl_ename (vl-catch-all-apply 'bpoly (list pt)))
    
    (if (and pl_ename (not (vl-catch-all-error-p pl_ename)) (= (type pl_ename) 'ENAME) 
             (vl-position (cdr (assoc 0 (entget pl_ename))) '("POLYLINE" "LWPOLYLINE")))
      ;; Jika bpoly berhasil mendeteksi dinding percil
      (ssadd pl_ename poly_ss)
      
      ;; JARING PENGAMAN: THE FAKE HOUSE FALLBACK
      (progn
        ;; Jika Bpoly gagal (karena rumah bocor/tanah kosong), buat Bounding Box palsu!
        ;; Pad 1.0 meter mengelilingi MTEXT tersebut.
        (setq pad 1.0)
        (setq p1_pad (list (- (car p1) pad) (- (cadr p1) pad)))
        (setq p2_pad (list (+ (car p2) pad) (+ (cadr p2) pad)))
        
        (setq last_e (entlast))
        (vl-cmdf "_.RECTANG" p1_pad p2_pad)
        (setq pl_ename (entnext last_e))
        (if pl_ename (ssadd pl_ename poly_ss))
      )
    )
  )
  (setvar "HPGAPTOL" hg)
  
  ;; LOGIKA CROSS-STREET DILATION (JEMBATAN GAIB 8M) & UNION
  (if (> (sslength poly_ss) 0)
    (progn
      ;; Tahap Dilation (+8m)
      (setq dilate_ss (ssadd))
      (setq i 0)
      (while (< i (sslength poly_ss))
        (setq e (ssname poly_ss i))
        (setq vla_e (vlax-ename->vla-object e))
        (setq a0 (vla-get-area vla_e))
        
        (setq off1 (vl-catch-all-apply 'vla-offset (list vla_e 8.0)))
        (setq off2 (vl-catch-all-apply 'vla-offset (list vla_e -8.0)))
        
        (setq lst1 (if (not (vl-catch-all-error-p off1)) (vlax-safearray->list (vlax-variant-value off1)) nil))
        (setq lst2 (if (not (vl-catch-all-error-p off2)) (vlax-safearray->list (vlax-variant-value off2)) nil))
        
        (setq a1 0.0) (if lst1 (foreach o lst1 (setq a (vl-catch-all-apply 'vla-get-area (list o))) (if (not (vl-catch-all-error-p a)) (setq a1 (+ a1 a)))))
        (setq a2 0.0) (if lst2 (foreach o lst2 (setq a (vl-catch-all-apply 'vla-get-area (list o))) (if (not (vl-catch-all-error-p a)) (setq a2 (+ a2 a)))))
        
        (setq outward nil)
        (if (and (> a1 a0) (>= a1 a2))
          (setq outward lst1)
          (if (> a2 a0)
            (setq outward lst2)
            (setq outward (list vla_e)) ;; fallback
          )
        )
        (foreach o outward
          (if (not (eq o vla_e)) (ssadd (vlax-vla-object->ename o) dilate_ss))
        )
        
        ;; Hapus sisa-sisa polylines yang salah offset dan BPOLY aslinya
        (if (and lst1 (not (eq lst1 outward))) (foreach o lst1 (vl-catch-all-apply 'vla-delete (list o))))
        (if (and lst2 (not (eq lst2 outward))) (foreach o lst2 (vl-catch-all-apply 'vla-delete (list o))))
        (if (not (vl-position vla_e outward)) (vl-catch-all-apply 'vla-delete (list vla_e)))
        
        (setq i (1+ i))
      )
      
      (setq delobj_old (getvar "DELOBJ"))
      (setvar "DELOBJ" 1)
      
      (setq ent_trap (entlast))
      (vl-cmdf "_.REGION" dilate_ss "")
      
      (setq reg_ss (ssadd))
      (while (setq ent_trap (entnext ent_trap))
        (if (= (cdr (assoc 0 (entget ent_trap))) "REGION")
          (ssadd ent_trap reg_ss)
        )
      )
      
      (setvar "DELOBJ" delobj_old)
      
      (if (> (sslength reg_ss) 0)
        (progn
          ;; Gabungkan (Union) semua boundary tiap mtext menjadi satu kesatuan
          (vl-cmdf "_.UNION" reg_ss "")
          (setq union_reg (entlast))
          (if (and union_reg (= (cdr (assoc 0 (entget union_reg))) "REGION"))
            (progn
              ;; Hancurkan (Explode) hasil region untuk mendapatkan garis-garis pembentuknya
              (setq ent_trap (entlast))
              (vl-cmdf "_.EXPLODE" union_reg)
              (setq line_ss (ssadd))
              (while (setq ent_trap (entnext ent_trap))
                (ssadd ent_trap line_ss)
              )
              
              (setvar "PEDITACCEPT" 1)
              
              ;; Rangkai/Join ulang garis menjadi Polyline
              (setq ent_trap (entlast))
              (vl-cmdf "_.PEDIT" "_M" line_ss "" "_J" 0.1 "")
              
              (setq join_ss (ssadd))
              (while (setq ent_trap (entnext ent_trap))
                (if (= (cdr (assoc 0 (entget ent_trap))) "LWPOLYLINE")
                  (ssadd ent_trap join_ss)
                )
              )
              
              (setq max_area -1 max_pl nil all_joined '())
              (setq i 0)
              
              ;; Sortir Polylines untuk mengambil yang terluar, sisanya dihapus
              (while (< i (sslength join_ss))
                (setq e (ssname join_ss i))
                (if (= (cdr (assoc 0 (entget e))) "LWPOLYLINE")
                  (progn
                    (setq vla_e (vlax-ename->vla-object e))
                    (setq all_joined (cons vla_e all_joined))
                    (setq a (vla-get-area vla_e))
                    (if (> a max_area)
                      (progn (setq max_area a) (setq max_pl vla_e))
                    )
                  )
                )
                (setq i (1+ i))
              )
              
              ;; Hapus object boundary bantu (lubang pulau)
              (foreach o all_joined
                (if (not (eq o max_pl)) (vl-catch-all-apply 'vla-delete (list o)))
              )
              
              ;; Proses EROSION ADAPTIF (Target mundur 9 meter. Memakai Hack "Buka Tutup" Ide User jika kusut)
              (if max_pl
                (progn
                  (setq inward nil)
                  (setq try_dist 9.0)
                  
                  (while (and (not inward) (>= try_dist 1.0))
                    ;; DILEMBUTKAN: Gunakan 18 langkah (0.5 meter per langkah untuk 9 meter).
                    ;; Sangat mujarab mengikis sudut sempit tanpa harus melukai vertexnya.
                    (setq off1 (ftth:step_offset max_pl try_dist 18))
                    
                    ;; JIKA NORMAL GAGAL, GUNAKAN HACK BUKA JAHIT IDE USER!
                    (if (vl-catch-all-error-p off1)
                      (setq off1 (ftth:force_hack_offset max_pl try_dist))
                    )
                    
                    (setq off2 (ftth:step_offset max_pl (- try_dist) 18))
                    (if (vl-catch-all-error-p off2)
                      (setq off2 (ftth:force_hack_offset max_pl (- try_dist)))
                    )
                    
                    (setq lst1 (if (not (vl-catch-all-error-p off1)) (vlax-safearray->list (vlax-variant-value off1)) nil))
                    (setq lst2 (if (not (vl-catch-all-error-p off2)) (vlax-safearray->list (vlax-variant-value off2)) nil))
                    
                    ;; FILTER UNIVERSAL: AutoCAD sering mereturn multiple poligon (Swallowtail / Loop hantu) walau di offset NORMAL.
                    ;; Kita WAJIB memusnahkan duplikatnya dan hanya simpan 1 poligon terluar (Area Terbesar)
                    (if (and lst1 (> (length lst1) 1))
                      (progn
                        (setq mx_a -1.0 b_o nil)
                        (foreach o lst1
                          (setq a (vl-catch-all-apply 'vla-get-area (list o)))
                          (if (and (not (vl-catch-all-error-p a)) (> a mx_a)) (progn (setq mx_a a) (setq b_o o)))
                        )
                        (foreach o lst1 (if (not (eq o b_o)) (vl-catch-all-apply 'vla-delete (list o))))
                        (setq lst1 (list b_o))
                      )
                    )
                    (if (and lst2 (> (length lst2) 1))
                      (progn
                        (setq mx_a -1.0 b_o nil)
                        (foreach o lst2
                          (setq a (vl-catch-all-apply 'vla-get-area (list o)))
                          (if (and (not (vl-catch-all-error-p a)) (> a mx_a)) (progn (setq mx_a a) (setq b_o o)))
                        )
                        (foreach o lst2 (if (not (eq o b_o)) (vl-catch-all-apply 'vla-delete (list o))))
                        (setq lst2 (list b_o))
                      )
                    )
                    
                    (setq a1 0.0) (if lst1 (foreach o lst1 (setq a (vl-catch-all-apply 'vla-get-area (list o))) (if (not (vl-catch-all-error-p a)) (setq a1 (+ a1 a)))))
                    (setq a2 0.0) (if lst2 (foreach o lst2 (setq a (vl-catch-all-apply 'vla-get-area (list o))) (if (not (vl-catch-all-error-p a)) (setq a2 (+ a2 a)))))
                    
                    ;; Kita mencari area terkecil (offset ke dalam) dari luasan aslinya.
                    (if (and lst1 (> a1 0.0) (< a1 max_area) (or (= a2 0.0) (<= a1 a2)))
                      (setq inward lst1)
                      (if (and lst2 (> a2 0.0) (< a2 max_area))
                        (setq inward lst2)
                      )
                    )
                    
                    (if inward
                      ;; Berhasil menemukan offset yang tidak gagal, hapus sampah sisa percobaan
                      (progn
                        (if (and lst1 (not (eq lst1 inward))) (foreach o lst1 (vl-catch-all-apply 'vla-delete (list o))))
                        (if (and lst2 (not (eq lst2 inward))) (foreach o lst2 (vl-catch-all-apply 'vla-delete (list o))))
                      )
                      ;; Jika kedua offset melebar / error (a1 dan a2 0 / invalid), hapus object dan turunkan jarak
                      (progn
                        (if lst1 (foreach o lst1 (vl-catch-all-apply 'vla-delete (list o))))
                        (if lst2 (foreach o lst2 (vl-catch-all-apply 'vla-delete (list o))))
                        (setq try_dist (- try_dist 1.5)) ;; Turunkan angka drastis bertahap untuk lolos area sempit
                        (princ (strcat "\n[*] Info: Batas terjebit, adaptasi penyusutan ulang sejauh: -" (rtos try_dist 2 1) "m"))
                      )
                    )
                  )
                  
                  ;; Terapkan ke layer dan XData
                  (if inward
                    (progn
                      (foreach o inward
                        (vla-put-color o 256) ; ByLayer
                        (vla-put-layer o "FAT AREA")
                        (ftth:writexdata (vlax-vla-object->ename o) (length lst))
                      )
                    )
                    ;; Fallback Error Ekstrim jika gagal sekalipun di jarak aman terendah
                    (progn
                      (vla-put-color max_pl 256)
                      (vla-put-layer max_pl "FAT AREA")
                      (ftth:writexdata (vlax-vla-object->ename max_pl) (length lst))
                      (setq max_pl nil)
                      (princ "\n[!] Peringatan: Geometri terlalu kusut diproses, batas akan menggunakan garis asli.")
                    )
                  )
                  
                  ;; Hapus boundary luar (tulang punggung besar)
                  (if max_pl (vl-catch-all-apply 'vla-delete (list max_pl)))
                )
              )
            )
          )
        )
      )
    )
  )
  
  ;; Memunculkan Ulang Titik Teks 
  (foreach e lst
    (setq ed (entget e))
    (if (assoc 60 ed)
      (setq ed (subst '(60 . 0) (assoc 60 ed) ed))
      (setq ed (append ed '((60 . 0))))
    )
    (entmod ed)
  )
  
  ;; Return nilai koleksi entitas akhir yang dieksekusi termasuk komplotan hasil culikan Convex Hull
  batch_entities
)

;; Helper: Persiapan Layer khusus
(defun ftth:ensure_layer (layname col)
  (if (not (tblsearch "LAYER" layname))
    (vl-cmdf "_.LAYER" "_M" layname "_C" col "" "")
  )
)

;; Helper: Metadata Binding (Untuk Custom Program Lain Integratornya)
(defun ftth:writexdata (ent count / ed xdata)
  (if (not (tblsearch "APPID" "FTTH-GROUP_DATA"))
    (regapp "FTTH-GROUP_DATA")
  )
  (setq ed (entget ent))
  (setq xdata (list -3 (list "FTTH-GROUP_DATA" (cons 1000 "ObjectCount") (cons 1070 count))))
  (setq ed (vl-remove-if '(lambda (x) (= (car x) -3)) ed))
  (setq ed (append ed (list xdata)))
  (entmod ed)
)

;; Helper: Convex Hull Cross Product
(defun ftth:cross-product (o a b)
  (- (* (- (car a) (car o)) (- (cadr b) (cadr o)))
     (* (- (car b) (car o)) (- (cadr a) (cadr o))))
)

;; Helper: Convex Hull Algorithm
(defun ftth:convex-hull (pts / p0 pts1 upper lower p)
  (if (<= (length pts) 3)
    pts
    (progn
      (setq pts (vl-sort pts
                  '(lambda (a b)
                     (if (equal (car a) (car b) 1e-6)
                       (< (cadr a) (cadr b))
                       (< (car a) (car b))))))
      (setq upper '())
      (foreach p pts
        (while (and (>= (length upper) 2)
                    (<= (ftth:cross-product (cadr upper) (car upper) p) 0.0))
          (setq upper (cdr upper))
        )
        (setq upper (cons p upper))
      )
      
      (setq lower '())
      (setq pts (reverse pts))
      (foreach p pts
        (while (and (>= (length lower) 2)
                    (<= (ftth:cross-product (cadr lower) (car lower) p) 0.0))
          (setq lower (cdr lower))
        )
        (setq lower (cons p lower))
      )
      
      (append (reverse (cdr upper)) (reverse (cdr lower)))
    )
  )
)

(princ "\n=> Script [FTTH Tools] Berhasil di-Load. Perintah Aktif: FTTH-GROUP & FTTH-BOUNDARY")
(princ)
