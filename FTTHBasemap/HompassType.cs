using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace FTTHBasemap
{
    public static class HompassTypeManager
    {
        // Static state variables for the current active tag type
        public static string ActiveLabel { get; set; } = "TK";
        public static short ActiveColor { get; set; } = 30;
        public static string ActiveLayer { get; set; } = "TK_LAYER";

        /// <summary>
        /// Prompts selection and changes text entities to the active Hompass Type one by one in a loop.
        /// </summary>
        public static void ProcessSelection(string teksBaru, short warna, string layerName)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions(string.Format("\nPilih teks untuk diubah menjadi [{0}] (ESC untuk keluar): ", teksBaru));
            peo.SetRejectMessage("\nHanya bisa memilih objek Text atau MText.");
            peo.AddAllowedClass(typeof(DBText), true);
            peo.AddAllowedClass(typeof(MText), true);

            int count = 0;
            bool keepSelecting = true;

            while (keepSelecting)
            {
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.OK)
                {
                    ObjectId id = per.ObjectId;
                    using (DocumentLock docLock = doc.LockDocument())
                    {
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                            if (!lt.Has(layerName))
                            {
                                lt.UpgradeOpen();
                                using (LayerTableRecord ltr = new LayerTableRecord())
                                {
                                    ltr.Name = layerName;
                                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, warna);
                                    ltr.IsFrozen = false;
                                    ltr.IsOff = false;
                                    lt.Add(ltr);
                                    tr.AddNewlyCreatedDBObject(ltr, true);
                                }
                            }
                            else
                            {
                                using (LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite))
                                {
                                    ltr.IsFrozen = false;
                                    ltr.IsOff = false;
                                }
                            }

                            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                            if (!rat.Has("NAMAHP_BACKUP"))
                            {
                                rat.UpgradeOpen();
                                using (RegAppTableRecord ratr = new RegAppTableRecord())
                                {
                                    ratr.Name = "NAMAHP_BACKUP";
                                    rat.Add(ratr);
                                    tr.AddNewlyCreatedDBObject(ratr, true);
                                }
                            }

                            Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                            if (ent != null && (ent is DBText || ent is MText))
                            {
                                using (ResultBuffer rbCheck = ent.GetXDataForApplication("NAMAHP_BACKUP"))
                                {
                                    if (rbCheck == null)
                                    {
                                        string origText = "";
                                        if (ent is DBText txtCheck)
                                        {
                                            origText = txtCheck.TextString;
                                        }
                                        else if (ent is MText mtxtCheck)
                                        {
                                            origText = mtxtCheck.Contents;
                                        }

                                        string origLayer = ent.Layer;
                                        short origColor = (short)ent.ColorIndex;

                                        if (origText.Length > 255) origText = origText.Substring(0, 255);

                                        using (ResultBuffer rb = new ResultBuffer(
                                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, "NAMAHP_BACKUP"),
                                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, origText),
                                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, origLayer),
                                            new TypedValue((int)DxfCode.ExtendedDataInteger16, origColor)
                                        ))
                                        {
                                            ent.XData = rb;
                                        }
                                    }
                                }

                                if (ent is DBText txt)
                                {
                                    txt.TextString = teksBaru;
                                }
                                else if (ent is MText mtxt)
                                {
                                    mtxt.Contents = teksBaru;
                                }

                                ent.Layer = layerName;
                                ent.ColorIndex = warna;
                                count++;
                            }
                            tr.Commit();
                        }
                    }
                    ed.UpdateScreen();
                    ed.WriteMessage(string.Format("\nBerhasil mengganti objek teks ke \"{0}\".", teksBaru));
                }
                else
                {
                    keepSelecting = false;
                }
            }

            if (count > 0)
            {
                PaletteManager.Log($"Changed {count} texts to \"{teksBaru}\".");
            }
        }

        /// <summary>
        /// Automatically extracts an unused TK number and allows placing it.
        /// </summary>
        public static void ProcessExtractTK()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            string extractedNumber = "";

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                HashSet<string> visibleSet;
                List<string> unused = GetUnusedTkNumbers(tr, btr, out visibleSet);

                if (unused.Count == 0)
                {
                    ed.WriteMessage("\nTidak ada nilai TK yang tersisa/belum terpakai di drawing ini!\n");
                    PaletteManager.Log("No unused TK values remaining.");
                    return;
                }
                extractedNumber = unused[0];
                tr.Commit();
            }

            PromptEntityOptions peo2 = new PromptEntityOptions(string.Format("\nStok nilai [{0}] diambil otomatis! Pilih teks referensi untuk diduplikat: ", extractedNumber));
            peo2.SetRejectMessage("\nHanya bisa memilih teks.");
            peo2.AddAllowedClass(typeof(DBText), true);
            peo2.AddAllowedClass(typeof(MText), true);
            PromptEntityResult per2 = ed.GetEntity(peo2);
            if (per2.Status != PromptStatus.OK) return;

            PromptPointOptions ppo = new PromptPointOptions("\nKlik lokasi penempatan untuk teks baru: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity refEnt = tr.GetObject(per2.ObjectId, OpenMode.ForRead) as Entity;
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    if (refEnt is DBText refTxt)
                    {
                        DBText newTxt = (DBText)refTxt.Clone();
                        newTxt.Position = ppr.Value;
                        newTxt.TextString = extractedNumber;
                        btr.AppendEntity(newTxt);
                        tr.AddNewlyCreatedDBObject(newTxt, true);
                    }
                    else if (refEnt is MText refMtxt)
                    {
                        MText newMtxt = (MText)refMtxt.Clone();
                        newMtxt.Location = ppr.Value;
                        newMtxt.Contents = extractedNumber;
                        btr.AppendEntity(newMtxt);
                        tr.AddNewlyCreatedDBObject(newMtxt, true);
                    }

                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (lt.Has("TK_LAYER"))
                    {
                        using (LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt["TK_LAYER"], OpenMode.ForWrite))
                        {
                            ltr.IsFrozen = false;
                            ltr.IsOff = false;
                        }
                    }

                    tr.Commit();
                    ed.WriteMessage(string.Format("\nBerhasil membuat duplikat dengan nomor [{0}].\n", extractedNumber));
                    PaletteManager.Log($"Extracted TK {extractedNumber} successfully.");
                }
            }
            ed.Regen();
        }

        /// <summary>
        /// Restores original text strings, layers, and colors from NAMAHP_BACKUP xdata.
        /// </summary>
        public static void RestoreNamaHp()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionResult selRes = ed.GetSelection();
            if (selRes.Status != PromptStatus.OK) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    HashSet<string> visibleSet;
                    List<string> unused = GetUnusedTkNumbers(tr, btr, out visibleSet);

                    int count = 0;
                    foreach (SelectedObject so in selRes.Value)
                    {
                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                        if (ent == null) continue;

                        if (ent is DBText || ent is MText)
                        {
                            using (ResultBuffer rb = ent.GetXDataForApplication("NAMAHP_BACKUP"))
                            {
                                if (rb != null)
                                {
                                    TypedValue[] tvs = rb.AsArray();
                                    if (tvs.Length >= 4)
                                    {
                                        string origText = tvs[1].Value.ToString();
                                        string origLayer = tvs[2].Value.ToString();
                                        short origColor = (short)tvs[3].Value;

                                        string targetText = origText;
                                        if (visibleSet.Contains(origText))
                                        {
                                            if (unused.Count > 0)
                                            {
                                                targetText = unused[0];
                                                unused.RemoveAt(0); // Mark as used
                                                visibleSet.Add(targetText);
                                            }
                                            else
                                            {
                                                ed.WriteMessage(string.Format("\nMelewati objek: Nilai '{0}' sudah terpakai dan tidak ada stok nilai nganggur.\n", origText));
                                                continue; // Skip this one
                                            }
                                        }
                                        else
                                        {
                                            visibleSet.Add(origText);
                                        }

                                        if (ent is DBText txt)
                                        {
                                            txt.TextString = targetText;
                                        }
                                        else if (ent is MText mtxt)
                                        {
                                            mtxt.Contents = targetText;
                                        }

                                        ent.Layer = origLayer;
                                        ent.ColorIndex = origColor;

                                        // Clean XData since it's restored
                                        using (ResultBuffer rbEmpty = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "NAMAHP_BACKUP")))
                                        {
                                            ent.XData = rbEmpty;
                                        }
                                        count++;
                                    }
                                }
                            }
                        }
                    }
                    tr.Commit();
                    ed.WriteMessage(string.Format("\nBerhasil mengembalikan {0} teks.\n", count));
                    PaletteManager.Log($"Restored {count} text names successfully.");
                }
            }
            ed.Regen();
        }

        public static List<string> GetUnusedTkNumbers(Transaction tr, BlockTableRecord btr, out HashSet<string> visibleSet)
        {
            HashSet<string> backupSet = new HashSet<string>();
            visibleSet = new HashSet<string>();

            RXClass dbTextClass = RXObject.GetClass(typeof(DBText));
            RXClass mTextClass = RXObject.GetClass(typeof(MText));

            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.IsDerivedFrom(dbTextClass) || id.ObjectClass.IsDerivedFrom(mTextClass))
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (ent is DBText txt)
                    {
                        visibleSet.Add(txt.TextString);
                        ExtractBackup(ent, backupSet);
                    }
                    else if (ent is MText mtxt)
                    {
                        visibleSet.Add(mtxt.Contents);
                        ExtractBackup(ent, backupSet);
                    }
                }
            }

            List<string> unused = new List<string>();
            foreach (string b in backupSet)
            {
                if (!visibleSet.Contains(b)) unused.Add(b);
            }
            unused.Sort();
            return unused;
        }

        private static void ExtractBackup(Entity ent, HashSet<string> backupSet)
        {
            using (ResultBuffer rb = ent.GetXDataForApplication("NAMAHP_BACKUP"))
            {
                if (rb != null)
                {
                    TypedValue[] tvs = rb.AsArray();
                    if (tvs.Length >= 4)
                    {
                        backupSet.Add(tvs[1].Value.ToString());
                    }
                }
            }
        }
    }
}
