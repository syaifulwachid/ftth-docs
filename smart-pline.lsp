;;; =================================================================================
;;; SMART POLYLINE (SMPL)
;;; =================================================================================
;;; Description: Draws a polyline with a maximum distance constraint per vertex.
;;; If the cursor is moved beyond the maximum distance, the visual rubber-band 
;;; and the resulting point are constrained to the maximum distance.
;;; Includes Dynamic Distance Update and Undo capability.
;;; =================================================================================

(defun c:SMPL ( / pt_start pt_last pt_new ptList states ent elist dist ang kwd done pt_new_2d pt_last_2d gr code val os_mode os_pt)
  ;; Set default max distance if it hasn't been set yet in this session
  (if (not *smpl-max-dist*)
    (setq *smpl-max-dist* 5.0)
  )
  
  ;; Prompt for the starting point
  (setq pt_start (getpoint "\nSpecify start point: "))
  (if (not pt_start)
    (progn (princ "\nCommand cancelled.") (exit))
  )
  
  ;; Initialize lists
  (setq ptList (list pt_start))
  (setq pt_last pt_start)
  (setq states (list ptList)) ; Store history for Undo functionality
  (setq done nil)
  
  ;; Draw the initial point (visually empty polyline but creates the entity)
  (smpl-draw-pline ptList)
  (setq ent (entlast))
  
  (princ (strcat "\nSmart Polyline started. Max distance = " (rtos *smpl-max-dist* 2 2)))
  
  (while (not done)
    (princ (strcat "\nSpecify next point or [Distance/Undo/Enter to finish]: "))
    (setq pt_new nil)
    
    ;; Use grread to draw the constrained rubber band
    (while (not pt_new)
      (setq gr (grread t 15 0))
      (setq code (car gr) val (cadr gr))
      
      (cond
        ;; 1. Mouse move (code 5)
        ((= code 5)
          ;; Try OSNAP if OSMODE is active
          (setq os_mode (getvar "OSMODE"))
          (if (and (> os_mode 0) (< os_mode 16384))
            (progn
              (setq os_pt (osnap val "_END,_MID,_CEN,_NOD,_QUA,_INT,_INS,_PER,_TAN,_NEA"))
              (if os_pt (setq val os_pt))
            )
          )
          
          (setq pt_new_2d (list (car val) (cadr val)))
          (setq pt_last_2d (list (car pt_last) (cadr pt_last)))
          (setq dist (distance pt_last_2d pt_new_2d))
          
          ;; Constrain if necessary
          (if (> dist *smpl-max-dist*)
            (setq pt_new_2d (polar pt_last_2d (angle pt_last_2d pt_new_2d) *smpl-max-dist*))
          )
          
          ;; Redraw temporary vector
          (redraw)
          (grdraw pt_last_2d pt_new_2d 1) ; 1 = red line for visual feedback
        )
        
        ;; 2. Left click (code 3)
        ((= code 3)
          ;; Try OSNAP if OSMODE is active
          (setq os_mode (getvar "OSMODE"))
          (if (and (> os_mode 0) (< os_mode 16384))
            (progn
              (setq os_pt (osnap val "_END,_MID,_CEN,_NOD,_QUA,_INT,_INS,_PER,_TAN,_NEA"))
              (if os_pt (setq val os_pt))
            )
          )
          
          (setq pt_new_2d (list (car val) (cadr val)))
          (setq pt_last_2d (list (car pt_last) (cadr pt_last)))
          (setq dist (distance pt_last_2d pt_new_2d))
          
          ;; Constrain if necessary
          (if (> dist *smpl-max-dist*)
            (setq pt_new_2d (polar pt_last_2d (angle pt_last_2d pt_new_2d) *smpl-max-dist*))
          )
          
          (redraw) ; clear the grdraw vector
          (setq pt_new pt_new_2d)
        )
        
        ;; 3. Keyboard input (code 2)
        ((= code 2)
          (cond
            ;; D or d (Distance)
            ((or (= val 68) (= val 100))
              (redraw)
              (setq pt_new "Distance")
            )
            ;; U or u (Undo)
            ((or (= val 85) (= val 117))
              (redraw)
              (setq pt_new "Undo")
            )
            ;; Enter or Space (Finish)
            ((or (= val 13) (= val 32))
              (redraw)
              (setq pt_new "Enter")
            )
          )
        )
        
        ;; 4. Right click (code 11 or 25) or Esc
        ((or (= code 11) (= code 25))
          (redraw)
          (setq pt_new "Enter")
        )
      )
    )
    
    ;; Process the captured input
    (cond
      ((= pt_new "Enter")
        (setq done T)
      )
      
      ((= pt_new "Distance")
        (setq kwd (getreal (strcat "\nEnter new maximum distance <" (rtos *smpl-max-dist* 2 2) ">: ")))
        (if (and kwd (> kwd 0)) 
          (setq *smpl-max-dist* kwd)
          (princ "\nDistance invalid or not changed.")
        )
      )
      
      ((= pt_new "Undo")
        (if (> (length states) 1)
          (progn
            (setq states (cdr states))
            (setq ptList (car states))
            (setq pt_last (last ptList))
            
            (if ent (entdel ent))
            (smpl-draw-pline ptList)
            (setq ent (entlast))
            (princ "\nUndid last point.")
          )
          (princ "\nNothing to undo.")
        )
      )
      
      ((listp pt_new)
        ;; Add constrained point to polyline
        (setq ptList (append ptList (list pt_new)))
        (setq states (cons ptList states))
        
        (if ent (entdel ent))
        (smpl-draw-pline ptList)
        (setq ent (entlast))
        
        (setq pt_last pt_new)
      )
    )
  )
  (princ "\nSmart Polyline finished.")
  (princ)
)

;; Helper function to draw LWPOLYLINE from a list of points
(defun smpl-draw-pline (pts / elist current_layer)
  (setq current_layer (getvar "CLAYER"))
  (setq elist (list '(0 . "LWPOLYLINE") 
                    '(100 . "AcDbEntity") 
                    '(100 . "AcDbPolyline") 
                    (cons 90 (length pts)) 
                    '(70 . 0)
                    (cons 8 current_layer))) ; Place on current layer
                    
  (foreach pt pts
    ;; Ensure pt is 2D and append as group code 10
    (setq elist (append elist (list (cons 10 (list (car pt) (cadr pt))))))
  )
  
  (entmake elist)
)

;; Startup message
(princ "\n=======================================================")
(princ "\nSmart Polyline (SMPL) is loaded successfully.")
(princ "\nType SMPL to run the command.")
(princ "\n=======================================================")
(princ)
