using System;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;

namespace FTTHBasemap
{
    public static class AssetDrawer
    {
        public static List<ObjectId> DrawAsset(Database db, Transaction tr, BlockTableRecord modelSpace, string jsonPath, Point3d targetPosition, double rotationAngle, double scaleFactor, string targetLayer)
        {
            var createdIds = new List<ObjectId>();
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException("Asset JSON file not found: " + jsonPath);
            }

            string jsonContent = File.ReadAllText(jsonPath);
            var root = JObject.Parse(jsonContent);

            // 1. Create layers
            var layers = root["layers"];
            if (layers != null)
            {
                foreach (var lyr in layers)
                {
                    SetupLayer(db, tr, lyr);
                }
            }

            // 2. Create textstyles
            var textstyles = root["textstyles"];
            if (textstyles != null)
            {
                foreach (var ts in textstyles)
                {
                    SetupTextStyle(db, tr, ts);
                }
            }

            // 3. Create block definitions
            var blocks = root["blocks"];
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            if (blocks != null)
            {
                foreach (var block in blocks)
                {
                    string bName = block["name"].Value<string>();
                    if (!blockTable.Has(bName))
                    {
                        blockTable.UpgradeOpen();
                        using (var btr = new BlockTableRecord())
                        {
                            btr.Name = bName;
                            btr.Origin = ParsePoint3d(block["base_point"]);

                            blockTable.Add(btr);
                            tr.AddNewlyCreatedDBObject(btr, true);

                            // Add sub-entities to block definition
                            var subEntities = block["entities"];
                            if (subEntities != null)
                            {
                                foreach (var ent in subEntities)
                                {
                                    try
                                    {
                                        Entity entityObj = CreateEntityFromJToken(ent, db, tr);
                                        if (entityObj != null)
                                        {
                                            btr.AppendEntity(entityObj);
                                            tr.AddNewlyCreatedDBObject(entityObj, true);

                                            // Handle Hatch loops
                                            if (entityObj is Hatch hat && ent["loops"] != null)
                                            {
                                                AddHatchLoops(btr, tr, hat, ent["loops"]);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Error creating sub-entity inside block {bName}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 4. Create top-level entities in ModelSpace
            var topEntities = root["entities"];
            if (topEntities != null)
            {
                foreach (var ent in topEntities)
                {
                    try
                    {
                        Entity entityObj = CreateEntityFromJToken(ent, db, tr);
                        if (entityObj != null)
                        {
                            // If it is a block reference, set its custom insertion properties
                            if (entityObj is BlockReference br)
                            {
                                // Set base insertion properties
                                Point3d rawPos = ParsePoint3d(ent["position"]);
                                double rawRot = ent["rotation"] != null ? ent["rotation"].Value<double>() : 0.0;
                                double scaleX = ent["scale"]?[0]?.Value<double>() ?? 1.0;
                                double scaleY = ent["scale"]?[1]?.Value<double>() ?? 1.0;
                                double scaleZ = ent["scale"]?[2]?.Value<double>() ?? 1.0;

                                // Rotate position vector and scale it
                                Vector3d posVec = new Vector3d(rawPos.X, rawPos.Y, rawPos.Z);
                                posVec = posVec.RotateBy(rotationAngle, Vector3d.ZAxis) * scaleFactor;

                                br.Position = targetPosition + posVec;
                                br.Rotation = rawRot + rotationAngle;
                                br.ScaleFactors = new Scale3d(scaleX * scaleFactor, scaleY * scaleFactor, scaleZ * scaleFactor);

                                if (!string.IsNullOrEmpty(targetLayer))
                                {
                                    br.Layer = targetLayer;
                                }
                            }
                            else
                            {
                                // Translate and rotate other top-level entities
                                entityObj.TransformBy(Matrix3d.Scaling(scaleFactor, targetPosition) *
                                                      Matrix3d.Rotation(rotationAngle, Vector3d.ZAxis, targetPosition) *
                                                      Matrix3d.Displacement(targetPosition - Point3d.Origin));

                                if (!string.IsNullOrEmpty(targetLayer))
                                {
                                    entityObj.Layer = targetLayer;
                                }
                            }

                            modelSpace.AppendEntity(entityObj);
                            tr.AddNewlyCreatedDBObject(entityObj, true);
                            createdIds.Add(entityObj.ObjectId);

                             if (entityObj is BlockReference blockRef)
                             {
                                 var btr = (BlockTableRecord)tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead);
                                 if (btr.HasAttributeDefinitions)
                                 {
                                     foreach (ObjectId entId in btr)
                                     {
                                         DBObject dbObj = tr.GetObject(entId, OpenMode.ForRead);
                                         if (dbObj is AttributeDefinition ad && !ad.Constant)
                                         {
                                             var ar = new AttributeReference();
                                             ar.SetAttributeFromBlock(ad, blockRef.BlockTransform);
                                             ar.TextString = ad.TextString;
 
                                             // Find matching attribute in JSON
                                             var attrsJson = ent["attributes"];
                                             if (attrsJson != null)
                                             {
                                                 foreach (var attr in attrsJson)
                                                 {
                                                     if (attr["tag"] != null && attr["tag"].Value<string>().Equals(ad.Tag, StringComparison.OrdinalIgnoreCase))
                                                     {
                                                         ar.TextString = attr["value"]?.Value<string>() ?? ad.TextString;
                                                         break;
                                                     }
                                                 }
                                             }
                                             blockRef.AttributeCollection.AppendAttribute(ar);
                                             tr.AddNewlyCreatedDBObject(ar, true);
                                         }
                                     }
                                 }
                             }

                            // Add Hatch loops if top-level entity is a Hatch
                            if (entityObj is Hatch hat && ent["loops"] != null)
                            {
                                AddHatchLoops(modelSpace, tr, hat, ent["loops"]);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error creating top-level entity: {ex.Message}");
                    }
                }
            }

            return createdIds;
        }

        private static void SetupLayer(Database db, Transaction tr, JToken lyr)
        {
            string name = lyr["name"].Value<string>();
            short color = lyr["color"] != null ? lyr["color"].Value<short>() : (short)7;
            string ltName = lyr["linetype"] != null ? lyr["linetype"].Value<string>() : "Continuous";
            string lwName = lyr["lineweight"] != null ? lyr["lineweight"].Value<string>() : "ByLayer";

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                using (var ltr = new LayerTableRecord())
                {
                    ltr.Name = name;
                    ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, color);
                    ltr.LineWeight = MapLineWeight(lwName);

                    // Set linetype
                    var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (ltt.Has(ltName))
                    {
                        ltr.LinetypeObjectId = ltt[ltName];
                    }

                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
            }
        }

        private static LineWeight MapLineWeight(string lwName)
        {
            if (string.IsNullOrEmpty(lwName)) return LineWeight.ByLayer;
            if (lwName.Equals("ByLayer", StringComparison.OrdinalIgnoreCase)) return LineWeight.ByLayer;
            if (lwName.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)) return LineWeight.ByBlock;

            if (lwName.StartsWith("LineWeight", StringComparison.OrdinalIgnoreCase))
            {
                string val = lwName.Substring(10);
                if (int.TryParse(val, out int num))
                {
                    try
                    {
                        return (LineWeight)num;
                    }
                    catch { }
                }
            }
            return LineWeight.ByLayer;
        }

        private static void SetupTextStyle(Database db, Transaction tr, JToken ts)
        {
            string name = ts["name"].Value<string>();
            string fontFile = ts["font_file_name"].Value<string>();
            double widthFactor = ts["width_factor"] != null ? ts["width_factor"].Value<double>() : 1.0;
            double oblique = ts["obliquing_angle"] != null ? ts["obliquing_angle"].Value<double>() : 0.0;

            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (!tst.Has(name))
            {
                tst.UpgradeOpen();
                using (var tstr = new TextStyleTableRecord())
                {
                    tstr.Name = name;
                    tstr.FileName = fontFile;
                    tstr.XScale = widthFactor;
                    tstr.ObliquingAngle = oblique;

                    tst.Add(tstr);
                    tr.AddNewlyCreatedDBObject(tstr, true);
                }
            }
        }

        private static Entity CreateEntityFromJToken(JToken ent, Database db, Transaction tr)
        {
            string type = ent["type"].Value<string>();
            Entity entityObj = null;

            switch (type.ToUpper())
            {
                case "CIRCLE":
                    entityObj = CreateCircle(ent);
                    break;
                case "LINE":
                    entityObj = CreateLine(ent);
                    break;
                case "POLYLINE":
                    entityObj = CreatePolyline(ent);
                    break;
                case "TEXT":
                    entityObj = CreateText(ent, db, tr);
                    break;
                case "MTEXT":
                    entityObj = CreateMText(ent, db, tr);
                    break;
                case "ELLIPSE":
                    entityObj = CreateEllipse(ent);
                    break;
                case "ARC":
                    entityObj = CreateArc(ent);
                    break;
                case "HATCH":
                    entityObj = CreateHatch(ent);
                    break;
                case "ATTRIBUTEDEFINITION":
                    entityObj = CreateAttributeDefinition(ent);
                    break;
                case "BLOCKREFERENCE":
                    entityObj = CreateBlockReference(ent, db);
                    break;
            }

            if (entityObj != null)
            {
                if (ent["layer"] != null) entityObj.Layer = ent["layer"].Value<string>();
                if (ent["color"] != null)
                {
                    short colorVal = ent["color"].Value<short>();
                    if (colorVal == 0) entityObj.ColorIndex = 0; // ByBlock
                    else if (colorVal == 256) entityObj.ColorIndex = 256; // ByLayer
                    else entityObj.ColorIndex = colorVal;
                }
            }

            return entityObj;
        }

        private static Circle CreateCircle(JToken t)
        {
            var c = new Circle();
            c.Center = ParsePoint3d(t["center"]);
            c.Radius = t["radius"].Value<double>();
            return c;
        }

        private static Line CreateLine(JToken t)
        {
            var l = new Line();
            l.StartPoint = ParsePoint3d(t["start"] ?? t["start_point"]);
            l.EndPoint = ParsePoint3d(t["end"] ?? t["end_point"]);
            return l;
        }

        private static Polyline CreatePolyline(JToken t)
        {
            var pl = new Polyline();
            var verts = t["vertices"];
            if (verts != null)
            {
                int idx = 0;
                foreach (var v in verts)
                {
                    double x = v["x"].Value<double>();
                    double y = v["y"].Value<double>();
                    double bulge = v["bulge"] != null ? v["bulge"].Value<double>() : 0.0;
                    pl.AddVertexAt(idx++, new Point2d(x, y), bulge, 0.0, 0.0);
                }
            }
            if (t["closed"] != null) pl.Closed = t["closed"].Value<bool>();
            return pl;
        }

        private static DBText CreateText(JToken t, Database db, Transaction tr)
        {
            var txt = new DBText();
            txt.TextString = t["text_string"]?.Value<string>() ?? "";
            txt.Position = ParsePoint3d(t["position"]);
            txt.Height = t["height"].Value<double>();
            if (t["rotation"] != null) txt.Rotation = t["rotation"].Value<double>();
            if (t["style"] != null) txt.TextStyleId = GetTextStyleId(db, tr, t["style"].Value<string>());
            if (t["width_factor"] != null) txt.WidthFactor = t["width_factor"].Value<double>();
            if (t["obliquing_angle"] != null) txt.Oblique = t["obliquing_angle"].Value<double>();

            if (t["horizontal_mode"] != null || t["vertical_mode"] != null)
            {
                string hMode = t["horizontal_mode"]?.Value<string>();
                string vMode = t["vertical_mode"]?.Value<string>();

                txt.Justify = MapJustification(hMode, vMode);
                if (t["alignment_point"] != null)
                {
                    txt.AlignmentPoint = ParsePoint3d(t["alignment_point"]);
                }
            }
            return txt;
        }

        private static MText CreateMText(JToken t, Database db, Transaction tr)
        {
            var mt = new MText();
            mt.Contents = t["contents"]?.Value<string>() ?? t["text_string"]?.Value<string>() ?? "";
            mt.Location = ParsePoint3d(t["location"] ?? t["position"]);
            mt.TextHeight = t["height"].Value<double>();
            if (t["rotation"] != null) mt.Rotation = t["rotation"].Value<double>();
            if (t["style"] != null) mt.TextStyleId = GetTextStyleId(db, tr, t["style"].Value<string>());
            if (t["width"] != null) mt.Width = t["width"].Value<double>();
            if (t["attachment"] != null)
            {
                try
                {
                    mt.Attachment = (AttachmentPoint)Enum.Parse(typeof(AttachmentPoint), t["attachment"].Value<string>(), true);
                }
                catch { }
            }
            return mt;
        }

        private static Arc CreateArc(JToken t)
        {
            var a = new Arc();
            a.Center = ParsePoint3d(t["center"]);
            a.Radius = t["radius"].Value<double>();
            a.StartAngle = t["start_angle"].Value<double>();
            a.EndAngle = t["end_angle"].Value<double>();
            return a;
        }

        private static Ellipse CreateEllipse(JToken t)
        {
            var c = ParsePoint3d(t["center"]);
            var major = ParseVector3d(t["major_axis"]);
            double ratio = t["radius_ratio"].Value<double>();
            double start = t["start_angle"].Value<double>();
            double end = t["end_angle"].Value<double>();

            return new Ellipse(c, Vector3d.ZAxis, major, ratio, start, end);
        }

        private static Hatch CreateHatch(JToken t)
        {
            var hat = new Hatch();
            string patName = t["pattern_name"] != null ? t["pattern_name"].Value<string>() : "SOLID";
            double patScale = t["pattern_scale"] != null ? t["pattern_scale"].Value<double>() : 1.0;
            double patAngle = t["pattern_angle"] != null ? t["pattern_angle"].Value<double>() : 0.0;

            hat.SetHatchPattern(HatchPatternType.PreDefined, patName);
            hat.PatternScale = patScale;
            hat.PatternAngle = patAngle;
            hat.Associative = false;
            return hat;
        }

        private static AttributeDefinition CreateAttributeDefinition(JToken t)
        {
            var att = new AttributeDefinition();
            att.Tag = t["tag"]?.Value<string>() ?? "TAG";
            att.Prompt = t["prompt"]?.Value<string>() ?? "Prompt";
            att.TextString = t["text_string"]?.Value<string>() ?? "";
            att.Position = ParsePoint3d(t["position"]);
            att.Height = t["height"] != null ? t["height"].Value<double>() : 1.0;
            if (t["rotation"] != null) att.Rotation = t["rotation"].Value<double>();
            return att;
        }

        private static BlockReference CreateBlockReference(JToken t, Database db)
        {
            string bName = t["block_name"].Value<string>();
            ObjectId btrId = ObjectId.Null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(bName))
                {
                    btrId = bt[bName];
                }
                tr.Commit();
            }

            return new BlockReference(Point3d.Origin, btrId);
        }

        private static void AddHatchLoops(BlockTableRecord btr, Transaction tr, Hatch hat, JToken loops)
        {
            foreach (var loop in loops)
            {
                try
                {
                    bool isPoly = loop["is_polyline"] != null ? loop["is_polyline"].Value<bool>() : false;
                    if (isPoly)
                    {
                        using (var pl = new Polyline())
                        {
                            int vIdx = 0;
                            var verts = loop["vertices"];
                            if (verts != null)
                            {
                                foreach (var v in verts)
                                {
                                    double x = v["x"].Value<double>();
                                    double y = v["y"].Value<double>();
                                    double bulge = v["bulge"] != null ? v["bulge"].Value<double>() : 0.0;
                                    pl.AddVertexAt(vIdx++, new Point2d(x, y), bulge, 0.0, 0.0);
                                }
                            }
                            pl.Closed = true;
                            btr.AppendEntity(pl);
                            tr.AddNewlyCreatedDBObject(pl, true);

                            ObjectIdCollection ids = new ObjectIdCollection(new ObjectId[] { pl.ObjectId });
                            hat.AppendLoop(HatchLoopTypes.Default, ids);

                            pl.UpgradeOpen();
                            pl.Erase();
                        }
                    }
                    else
                    {
                        var edges = loop["edges"];
                        if (edges != null)
                        {
                            var tempIds = new ObjectIdCollection();
                            var tempEntities = new List<Entity>();
                            foreach (var edge in edges)
                            {
                                string edgeType = edge["type"]?.Value<string>() ?? "";
                                if (edgeType.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                                {
                                    var start = edge["start"];
                                    var end = edge["end"];
                                    if (start != null && end != null)
                                    {
                                        var line = new Line(ParsePoint3d(start), ParsePoint3d(end));
                                        btr.AppendEntity(line);
                                        tr.AddNewlyCreatedDBObject(line, true);
                                        tempIds.Add(line.ObjectId);
                                        tempEntities.Add(line);
                                    }
                                }
                                else if (edgeType.Equals("ARC", StringComparison.OrdinalIgnoreCase))
                                {
                                    var center = edge["center"];
                                    double radius = edge["radius"]?.Value<double>() ?? 1.0;
                                    double startAngle = edge["start_angle"]?.Value<double>() ?? 0.0;
                                    double endAngle = edge["end_angle"]?.Value<double>() ?? 0.0;

                                    var arc = new Arc(ParsePoint3d(center), radius, startAngle, endAngle);
                                    btr.AppendEntity(arc);
                                    tr.AddNewlyCreatedDBObject(arc, true);
                                    tempIds.Add(arc.ObjectId);
                                    tempEntities.Add(arc);
                                }
                            }

                            if (tempIds.Count > 0)
                            {
                                hat.AppendLoop(HatchLoopTypes.Default, tempIds);
                                
                                foreach (var ent in tempEntities)
                                {
                                    ent.UpgradeOpen();
                                    ent.Erase();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error adding hatch loop: {ex.Message}");
                }
            }
            try
            {
                hat.EvaluateHatch(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error evaluating hatch: {ex.Message}");
            }
        }

        private static Point3d ParsePoint3d(JToken t)
        {
            if (t == null) return Point3d.Origin;
            double x = t[0]?.Value<double>() ?? 0.0;
            double y = t[1]?.Value<double>() ?? 0.0;
            double z = t[2]?.Value<double>() ?? 0.0;
            return new Point3d(x, y, z);
        }

        private static Vector3d ParseVector3d(JToken t)
        {
            if (t == null) return Vector3d.XAxis;
            double x = t[0]?.Value<double>() ?? 0.0;
            double y = t[1]?.Value<double>() ?? 0.0;
            double z = t[2]?.Value<double>() ?? 0.0;
            return new Vector3d(x, y, z);
        }

        private static ObjectId GetTextStyleId(Database db, Transaction tr, string styleName)
        {
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tst.Has(styleName))
            {
                return tst[styleName];
            }
            return db.Textstyle;
        }

        private static AttachmentPoint MapJustification(string hMode, string vMode)
        {
            if (hMode == "TextCenter" && vMode == "TextVerticalMid") return AttachmentPoint.MiddleCenter;
            if (hMode == "TextCenter" && vMode == "TextBase") return AttachmentPoint.BottomCenter;
            if (hMode == "TextCenter" && vMode == "TextTop") return AttachmentPoint.TopCenter;
            if (hMode == "TextLeft" && vMode == "TextBase") return AttachmentPoint.BottomLeft;
            if (hMode == "TextLeft" && vMode == "TextVerticalMid") return AttachmentPoint.MiddleLeft;
            if (hMode == "TextLeft" && vMode == "TextTop") return AttachmentPoint.TopLeft;
            if (hMode == "TextRight" && vMode == "TextBase") return AttachmentPoint.BottomRight;
            if (hMode == "TextRight" && vMode == "TextVerticalMid") return AttachmentPoint.MiddleRight;
            if (hMode == "TextRight" && vMode == "TextTop") return AttachmentPoint.TopRight;

            return AttachmentPoint.BaseLeft;
        }
    }
}
