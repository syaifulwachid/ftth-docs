(defun c:EXTPERCIL ( / ssRow ssTrotoar ssPercil i ent pStart pEnd entRows entTrotoars allTargets toleransi counter diExtend? ptBaruStart ptBaruEnd )
  (vl-load-com)
  (princ "\n=== Memulai Proses Jalur Mandiri FTTH-PERCIL ===")
  
  (setq toleransi 0.05) ; Toleransi jarak menempel (bisa disesuaikan)
  
  ;; 1. Ambil target pembatas dan garis percil
  (setq ssRow (ssget "X" '((0 . "LINE,LWPOLYLINE,POLYLINE") (8 . "[Ff][Tt][Tt][Hh]-[Rr][Oo][Ww]"))))
  (setq ssTrotoar (ssget "X" '((0 . "LINE,LWPOLYLINE,POLYLINE") (8 . "[Ff][Tt][Tt][Hh]-[Tt][Rr][Oo][Tt][Oo][Aa][Rr]"))))
  (setq ssPercil (ssget "X" '((0 . "LINE,LWPOLYLINE,POLYLINE") (8 . "[Ff][Tt][Tt][Hh]-[Pp][Ee][Rr][Cc][Ii][Ll]"))))
  
  ;; Fungsi internal konversi selection set ke list entitas
  (defun ss-to-list (ss / i lst)
    (if ss (repeat (setq i (sslength ss)) (setq lst (cons (ssname ss (setq i (1- i))) lst))))
    lst
  )
  
  (setq entRows (ss-to-list ssRow))
  (setq entTrotoars (ss-to-list ssTrotoar))
  (setq allTargets (append entRows entTrotoars))
  
  ;; Fungsi internal untuk mencari titik potong terdekat searah vektor garis
  (defun cari-titik-potong-terdekat (pBasis pArah targetList / dirVek pRay targetObj intPt coords tPt dist minDist hasilPt)
    (setq dirVek (vlax-3d-point (mapcar '- pArah pBasis)))
    (setq pRay (vlax-invoke (vlax-get-acad-object) 'createRay (vlax-3d-point pArah) dirVek))
    (setq minDist 1e99 hasilPt nil)
    
    (foreach tgt targetList
      (setq targetObj (vlax-ename->vla-object tgt))
      (setq intPt (vlax-variant-value (vla-IntersectWith pRay targetObj acExtendNone)))
      (if (and intPt (> (vlax-safearray-get-u-bound intPt 1) 0))
        (progn
          (setq coords (vlax-safearray->list intPt))
          (while coords
            (setq tPt (list (car coords) (cadr coords) (caddr coords)))
            (setq coords (cdddr coords))
            (setq dist (distance pArah tPt))
            (if (< dist minDist)
              (setq minDist dist hasilPt tPt))
          )
        )
      )
    )
    (vla-delete pRay)
    hasilPt
  )

  ;; Fungsi untuk memperbarui koordinat ujung entitas secara langsung (DXF)
  (defun update-ujung-entitas (ent ptBaru opsiUjung / edata)
    (setq edata (entget ent))
    (cond
      ;; Jika tipe objek adalah LINE murni
      ((eq (cdr (assoc 0 edata)) "LINE")
       (if (= opsiUjung "START")
         (setq edata (subst (cons 10 ptBaru) (assoc 10 edata) edata))
         (setq edata (subst (cons 11 ptBaru) (assoc 11 edata) edata))
       )
       (entmod edata)
      )
      ;; Jika tipe objek adalah LWPOLYLINE
      ((eq (cdr (assoc 0 edata)) "LWPOLYLINE")
       (let ((vlist nil) (newvlist nil) (idx 0) (targetIdx 0))
         (foreach item edata
           (if (= (car item) 10) (setq vlist (cons idx vlist) idx (1+ idx)))
         )
         (if (= opsiUjung "END") (setq targetIdx (1- (length vlist))))
         
         (setq idx 0)
         (foreach item edata
           (if (= (car item) 10)
             (progn
               (if (= idx targetIdx)
                 (setq newvlist (cons (list 10 (car ptBaru) (cadr ptBaru)) newvlist))
                 (setq newvlist (cons item newvlist))
               )
               (setq idx (1+ idx))
             )
             (setq newvlist (cons item newvlist))
           )
         )
         (entmod (reverse newvlist))
       )
      )
    )
    (entupd ent)
  )

  ;; --- PROSES UTAMA ---
  (if (and allTargets ssPercil)
    (progn
      (vla-StartUndoMark (vla-get-ActiveDocument (vlax-get-acad-object)))
      (setq counter 0)
      
      (repeat (setq i (sslength ssPercil))
        (setq ent (ssname ssPercil (setq i (1- i))))
        (setq obj (vlax-ename->vla-object ent))
        
        (setq pStart (vlax-curve-getStartPoint obj))
        (setq pEnd (vlax-curve-getEndPoint obj))
        (setq diExtend? nil)
        
        ;; ---------------- KHUSUS UJUNG START ----------------
        (setq startMenempel nil)
        (foreach tgt allTargets
          (if (< (distance pStart (vlax-curve-getClosestPointTo (vlax-ename->vla-object tgt) pStart)) toleransi)
            (setq startMenempel t)
          )
        )
        ;; Jika Ujung Start tidak menempel ke mana pun, cari extend-nya
        (if (not startMenempel)
          (progn
            (setq ptBaruStart (cari-titik-potong-terdekat pEnd pStart allTargets))
            (if ptBaruStart
              (progn
                (update-ujung-entitas ent ptBaruStart "START")
                ;; Segarkan ulang koordinat pStart karena entitas sudah berubah
                (setq pStart ptBaruStart) 
                (setq diExtend? t)
              )
            )
          )
        )
        
        ;; ---------------- KHUSUS UJUNG END ----------------
        (setq endMenempel nil)
        (foreach tgt allTargets
          (if (< (distance pEnd (vlax-curve-getClosestPointTo (vlax-ename->vla-object tgt) pEnd)) toleransi)
            (setq endMenempel t)
          )
        )
        ;; Jika Ujung End tidak menempel ke mana pun, cari extend-nya
        (if (not endMenempel)
          (progn
            (setq ptBaruEnd (cari-titik-potong-terdekat pStart pEnd allTargets))
            (if ptBaruEnd
              (progn
                (update-ujung-entitas ent ptBaruEnd "END")
                (setq diExtend? t)
              )
            )
          )
        )
        
        (if diExtend? (setq counter (1+ counter)))
      )
      
      (vla-EndUndoMark (vla-get-ActiveDocument (vlax-get-acad-object)))
      (princ (strcat "\nSukses: Selesai memproses. " (itoa counter) " objek percil disesuaikan."))
    )
    (princ "\nError: Objek layer target atau percil tidak ditemukan.")
  )
  (princ)
)

(princ "\nKetik EXTPERCIL untuk menjalankan LISP.")
(princ)