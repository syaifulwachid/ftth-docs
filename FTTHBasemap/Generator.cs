using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FTTHBasemap.Model;

namespace FTTHBasemap
{
    public static class Generator
    {
        /// <summary>
        /// Sets up layers and their colors based on settings.
        /// </summary>
        public static void SetupLayers(Database db, Transaction tr)
        {
            var settings = BasemapSettings.Instance;
            GetOrCreateLayer(db, tr, settings.LayerCenterline, settings.ColorCenterline);
            GetOrCreateLayer(db, tr, settings.LayerRoadEdge, settings.ColorRoadEdge);
            GetOrCreateLayer(db, tr, settings.LayerSidewalk, settings.ColorSidewalk);
            GetOrCreateLayer(db, tr, settings.LayerRow, settings.ColorRow);
            GetOrCreateLayer(db, tr, settings.LayerPreview, settings.ColorPreview);
            GetOrCreateLayer(db, tr, settings.LayerBreakmark, settings.ColorBreakmark);

            // Setup new parcel layers and point settings
            ParcelGenerator.SetupParcelLayers(db, tr);
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name))
            {
                return lt[name];
            }

            lt.UpgradeOpen();
            using (LayerTableRecord ltr = new LayerTableRecord())
            {
                ltr.Name = name;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                ObjectId id = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return id;
            }
        }

        /// <summary>
        /// Colors centerlines based on their width according to the rules:
        /// 3m -> Red (1), 4m -> Yellow (2), 5m -> Green (3), 6m -> Cyan (4), 7m -> Blue (5), 8m -> Magenta (6), >8m -> Red (1)
        /// </summary>
        public static short GetColorForWidth(double width)
        {
            if (width <= 3.0) return 1; // Red
            if (width <= 4.0) return 2; // Yellow
            if (width <= 5.0) return 3; // Green
            if (width <= 6.0) return 4; // Cyan
            if (width <= 7.0) return 5; // Blue
            if (width <= 8.0) return 6; // Magenta
            return 1;                   // Default Red (>8m)
        }

        /// <summary>
        /// Processes a single centerline polyline to generate roads, sidewalks, and ROW.
        /// </summary>
        public static int ProcessCenterline(Database db, Transaction tr, BlockTableRecord btr, ObjectId clId, BasemapSettings settings)
        {
            Curve cl = tr.GetObject(clId, OpenMode.ForRead) as Curve;
            if (cl == null) return 0;

            // 1. Read width from XData
            var metadata = XDataManager.ReadMetadata(cl);
            if (metadata == null || metadata.RoadWidth <= 0)
            {
                return 0;
            }

            double width = metadata.RoadWidth;
            double halfWidth = width / 2.0;

            // Ensure centerline is on LayerCenterline and set its metadata and color
            cl.UpgradeOpen();
            cl.Layer = settings.LayerCenterline;
            cl.ColorIndex = GetColorForWidth(width);
            XDataManager.WriteMetadata(cl, new FTTHMetadata
            {
                SourceHandle = cl.Handle.ToString(),
                Side = "CENTER",
                RoadWidth = width,
                OffsetDistance = 0.0,
                ObjectType = "CENTERLINE"
            });

            // Calculate normals at the midpoint of the centerline
            double midParam = cl.StartParam + (cl.EndParam - cl.StartParam) / 2.0;
            Point3d midPt = cl.GetPointAtParameter(midParam);
            Vector3d tangent = cl.GetFirstDerivative(midParam).GetNormal();
            
            // CCW Normal (LEFT) and CW Normal (RIGHT) in 2D
            Vector3d leftNormal = new Vector3d(-tangent.Y, tangent.X, 0).GetNormal();
            Vector3d rightNormal = new Vector3d(tangent.Y, -tangent.X, 0).GetNormal();

            Curve leftEdge = null;
            Curve rightEdge = null;
            Curve leftSidewalk = null;
            Curve rightSidewalk = null;

            // 2. Generate Road Edges
            if (settings.GenerateRoad)
            {
                leftEdge = CreateOffsetCurve(cl, halfWidth, leftNormal, btr, tr, settings.LayerRoadEdge, settings.ColorRoadEdge);
                if (leftEdge != null)
                {
                    XDataManager.WriteMetadata(leftEdge, new FTTHMetadata
                    {
                        SourceHandle = cl.Handle.ToString(),
                        Side = "LEFT",
                        RoadWidth = width,
                        OffsetDistance = halfWidth,
                        ObjectType = "EDGE"
                    });
                }

                rightEdge = CreateOffsetCurve(cl, halfWidth, rightNormal, btr, tr, settings.LayerRoadEdge, settings.ColorRoadEdge);
                if (rightEdge != null)
                {
                    XDataManager.WriteMetadata(rightEdge, new FTTHMetadata
                    {
                        SourceHandle = cl.Handle.ToString(),
                        Side = "RIGHT",
                        RoadWidth = width,
                        OffsetDistance = halfWidth,
                        ObjectType = "EDGE"
                    });
                }
            }

            // 3. Generate Sidewalks
            if (settings.GenerateSidewalk)
            {
                Curve leftBase = leftEdge ?? cl;
                double leftOffsetDist = leftEdge != null ? settings.SidewalkWidth : halfWidth + settings.SidewalkWidth;
                Vector3d leftDir = leftNormal;

                leftSidewalk = CreateOffsetCurve(leftBase, leftOffsetDist, leftDir, btr, tr, settings.LayerSidewalk, settings.ColorSidewalk);
                if (leftSidewalk != null)
                {
                    XDataManager.WriteMetadata(leftSidewalk, new FTTHMetadata
                    {
                        SourceHandle = cl.Handle.ToString(),
                        Side = "LEFT",
                        RoadWidth = width,
                        OffsetDistance = halfWidth + settings.SidewalkWidth,
                        ObjectType = "TROTOAR"
                    });
                }

                Curve rightBase = rightEdge ?? cl;
                double rightOffsetDist = rightEdge != null ? settings.SidewalkWidth : halfWidth + settings.SidewalkWidth;
                Vector3d rightDir = rightNormal;

                rightSidewalk = CreateOffsetCurve(rightBase, rightOffsetDist, rightDir, btr, tr, settings.LayerSidewalk, settings.ColorSidewalk);
                if (rightSidewalk != null)
                {
                    XDataManager.WriteMetadata(rightSidewalk, new FTTHMetadata
                    {
                        SourceHandle = cl.Handle.ToString(),
                        Side = "RIGHT",
                        RoadWidth = width,
                        OffsetDistance = halfWidth + settings.SidewalkWidth,
                        ObjectType = "TROTOAR"
                    });
                }
            }

            // 4. Generate ROW
            if (settings.GenerateRow)
            {
                Curve leftBase = leftSidewalk ?? leftEdge ?? cl;
                double leftOffsetDist = settings.RowWidth;
                
                // If we are offsetting from centerline, the total offset distance is halfWidth + sidewalkWidth + rowWidth
                double totalLeftOffset = settings.RowWidth;
                if (leftSidewalk != null) totalLeftOffset = halfWidth + settings.SidewalkWidth + settings.RowWidth;
                else if (leftEdge != null) totalLeftOffset = halfWidth + settings.RowWidth;

                Curve leftRow = CreateOffsetCurve(leftBase, leftOffsetDist, leftNormal, btr, tr, settings.LayerRow, settings.ColorRow);
                if (leftRow != null)
                {
                    XDataManager.WriteMetadata(leftRow, new FTTHMetadata
                    {
                        SourceHandle = cl.Handle.ToString(),
                        Side = "LEFT",
                        RoadWidth = width,
                        OffsetDistance = totalLeftOffset,
                        ObjectType = "ROW"
                    });
                }

                Curve rightBase = rightSidewalk ?? rightEdge ?? cl;
                double rightOffsetDist = settings.RowWidth;
                
                double totalRightOffset = settings.RowWidth;
                if (rightSidewalk != null) totalRightOffset = halfWidth + settings.SidewalkWidth + settings.RowWidth;
                else if (rightEdge != null) totalRightOffset = halfWidth + settings.RowWidth;

                Curve rightRow = CreateOffsetCurve(rightBase, rightOffsetDist, rightNormal, btr, tr, settings.LayerRow, settings.ColorRow);
                if (rightRow != null)
                {
                    XDataManager.WriteMetadata(rightRow, new FTTHMetadata
                    {
                        SourceHandle = cl.Handle.ToString(),
                        Side = "RIGHT",
                        RoadWidth = width,
                        OffsetDistance = totalRightOffset,
                        ObjectType = "ROW"
                    });
                }
            }

            return 1;
        }

        /// <summary>
        /// Creates an offset curve and selects the correct direction based on normal vector.
        /// </summary>
        private static Curve CreateOffsetCurve(Curve baseCurve, double distance, Vector3d directionNormal, BlockTableRecord btr, Transaction tr, string layerName, short colorIndex)
        {
            if (baseCurve == null || distance <= 0.001) return null;

            try
            {
                // Try positive offset
                DBObjectCollection offsets = baseCurve.GetOffsetCurves(distance);
                Curve chosen = null;

                if (offsets != null && offsets.Count > 0)
                {
                    chosen = offsets[0] as Curve;
                    // Dispose other unused offsets
                    for (int i = 1; i < offsets.Count; i++)
                    {
                        offsets[i].Dispose();
                    }
                }

                // Try negative offset
                DBObjectCollection negOffsets = baseCurve.GetOffsetCurves(-distance);
                Curve negChosen = null;

                if (negOffsets != null && negOffsets.Count > 0)
                {
                    negChosen = negOffsets[0] as Curve;
                    for (int i = 1; i < negOffsets.Count; i++)
                    {
                        negOffsets[i].Dispose();
                    }
                }

                // Select the curve that lies in the direction of the normal vector
                Curve finalCurve = null;
                if (chosen != null && negChosen != null)
                {
                    Point3d baseMid = baseCurve.GetPointAtParameter(baseCurve.StartParam + (baseCurve.EndParam - baseCurve.StartParam) / 2.0);

                    Point3d chosenClosest = chosen.GetClosestPointTo(baseMid, false);
                    Point3d negClosest = negChosen.GetClosestPointTo(baseMid, false);

                    Vector3d chosenDir = chosenClosest - baseMid;
                    Vector3d negDir = negClosest - baseMid;

                    double chosenDot = chosenDir.DotProduct(directionNormal);
                    double negDot = negDir.DotProduct(directionNormal);

                    if (chosenDot > negDot)
                    {
                        finalCurve = chosen;
                        negChosen.Dispose();
                    }
                    else
                    {
                        finalCurve = negChosen;
                        chosen.Dispose();
                    }
                }
                else if (chosen != null)
                {
                    finalCurve = chosen;
                }
                else if (negChosen != null)
                {
                    finalCurve = negChosen;
                }

                if (finalCurve != null)
                {
                    finalCurve.Layer = layerName;
                    finalCurve.ColorIndex = colorIndex;
                    btr.AppendEntity(finalCurve);
                    tr.AddNewlyCreatedDBObject(finalCurve, true);
                    return finalCurve;
                }
            }
            catch (System.Exception ex)
            {
                // Offset failed for this curve (likely self-intersecting or too tight radius)
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[FTTH] Offset error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Generates missing layers incrementally by offsetting already existing cleaned components.
        /// </summary>
        public static int ProcessIncremental(Database db, Transaction tr, BlockTableRecord btr, BasemapSettings settings)
        {
            var existingEdges = new System.Collections.Generic.List<Curve>();
            var existingSidewalks = new System.Collections.Generic.List<Curve>();
            var existingRows = new System.Collections.Generic.List<Curve>();
            var centerlines = new System.Collections.Generic.List<Curve>();

            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (c == null) continue;

                if (c.Layer.Equals(settings.LayerRoadEdge, StringComparison.OrdinalIgnoreCase))
                    existingEdges.Add(c);
                else if (c.Layer.Equals(settings.LayerSidewalk, StringComparison.OrdinalIgnoreCase))
                    existingSidewalks.Add(c);
                else if (c.Layer.Equals(settings.LayerRow, StringComparison.OrdinalIgnoreCase))
                    existingRows.Add(c);
                else if (c.Layer.Equals(settings.LayerCenterline, StringComparison.OrdinalIgnoreCase))
                    centerlines.Add(c);
            }

            int count = 0;

            // 1. Incremental Sidewalk Generation (from Road Edges)
            if (settings.GenerateSidewalk)
            {
                foreach (Curve cEdge in existingEdges)
                {
                    var metaEdge = XDataManager.ReadMetadata(cEdge);
                    if (metaEdge == null || string.IsNullOrEmpty(metaEdge.SourceHandle)) continue;

                    // Check if sidewalk already exists
                    bool sidewalkExists = false;
                    foreach (Curve cSide in existingSidewalks)
                    {
                        var metaSide = XDataManager.ReadMetadata(cSide);
                        if (metaSide != null && 
                            metaSide.SourceHandle == metaEdge.SourceHandle && 
                            metaSide.Side == metaEdge.Side)
                        {
                            sidewalkExists = true;
                            break;
                        }
                    }

                    if (!sidewalkExists)
                    {
                        if (GenerateOffsetFromBase(db, tr, btr, cEdge, metaEdge, settings.SidewalkWidth, settings.LayerSidewalk, settings.ColorSidewalk, "TROTOAR", settings, centerlines))
                        {
                            count++;
                        }
                    }
                }

                // Re-fetch existing sidewalks to include newly generated ones for ROW offset
                existingSidewalks.Clear();
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased) continue;
                    Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                    if (c != null && c.Layer.Equals(settings.LayerSidewalk, StringComparison.OrdinalIgnoreCase))
                    {
                        existingSidewalks.Add(c);
                    }
                }
            }

            // 2. Incremental ROW Generation (from Sidewalks or Road Edges)
            if (settings.GenerateRow)
            {
                var baseCurves = existingSidewalks.Count > 0 ? existingSidewalks : existingEdges;

                foreach (Curve cBase in baseCurves)
                {
                    var metaBase = XDataManager.ReadMetadata(cBase);
                    if (metaBase == null || string.IsNullOrEmpty(metaBase.SourceHandle)) continue;

                    // Check if ROW already exists
                    bool rowExists = false;
                    foreach (Curve cRow in existingRows)
                    {
                        var metaRow = XDataManager.ReadMetadata(cRow);
                        if (metaRow != null && 
                            metaRow.SourceHandle == metaBase.SourceHandle && 
                            metaRow.Side == metaBase.Side)
                        {
                            rowExists = true;
                            break;
                        }
                    }

                    if (!rowExists)
                    {
                        if (GenerateOffsetFromBase(db, tr, btr, cBase, metaBase, settings.RowWidth, settings.LayerRow, settings.ColorRow, "ROW", settings, centerlines))
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Generates an offset curve from an existing base curve outwards from the centerline.
        /// </summary>
        private static bool GenerateOffsetFromBase(Database db, Transaction tr, BlockTableRecord btr, Curve baseCurve, FTTHMetadata metaBase, double offsetDist, string targetLayer, short targetColor, string objectType, BasemapSettings settings, System.Collections.Generic.List<Curve> centerlines)
        {
            try
            {
                double midParam = baseCurve.StartParam + (baseCurve.EndParam - baseCurve.StartParam) / 2.0;
                Point3d baseMid = baseCurve.GetPointAtParameter(midParam);

                Curve cl = null;
                double minDist = double.MaxValue;
                Point3d clPt = Point3d.Origin;

                // Find the closest centerline in the drawing to project onto dynamically
                if (centerlines != null && centerlines.Count > 0)
                {
                    foreach (var cLine in centerlines)
                    {
                        try
                        {
                            Point3d pt = cLine.GetClosestPointTo(baseMid, false);
                            double dist = baseMid.DistanceTo(pt);
                            if (dist < minDist)
                            {
                                minDist = dist;
                                cl = cLine;
                                clPt = pt;
                            }
                        }
                        catch { }
                    }
                }

                // Fallback to the SourceHandle centerline if no centerlines were found dynamically
                if (cl == null)
                {
                    ObjectId clId = ObjectId.Null;
                    try
                    {
                        long val = Convert.ToInt64(metaBase.SourceHandle, 16);
                        clId = db.GetObjectId(false, new Handle(val), 0);
                    }
                    catch { }

                    if (!clId.IsNull)
                    {
                        cl = tr.GetObject(clId, OpenMode.ForRead) as Curve;
                        if (cl != null)
                        {
                            clPt = cl.GetClosestPointTo(baseMid, false);
                        }
                    }
                }

                if (cl == null) return false;

                // Compute normal direction at the midpoint of the base curve pointing AWAY from the centerline
                Point3d closestPointOnCl = cl.GetClosestPointTo(baseMid, false);
                Vector3d directionNormal = (baseMid - closestPointOnCl).GetNormal();

                // Generate the offset curve
                Curve offsetCurve = CreateOffsetCurve(baseCurve, offsetDist, directionNormal, btr, tr, targetLayer, targetColor);
                if (offsetCurve != null)
                {
                    double newTotalOffset = metaBase.OffsetDistance + offsetDist;

                    XDataManager.WriteMetadata(offsetCurve, new FTTHMetadata
                    {
                        SourceHandle = metaBase.SourceHandle,
                        Side = metaBase.Side,
                        RoadWidth = metaBase.RoadWidth,
                        OffsetDistance = newTotalOffset,
                        ObjectType = objectType
                    });
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Resets the drawing, removing generated road, sidewalk, row, and preview elements.
        /// </summary>
        public static void ResetDrawing(Database db, Transaction tr, BasemapSettings settings)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                // Check layer or type
                if (ent.Layer == settings.LayerRoadEdge || 
                    ent.Layer == settings.LayerSidewalk || 
                    ent.Layer == settings.LayerRow ||
                    ent.Layer == settings.LayerPreview ||
                    ent.Layer == settings.LayerBreakmark ||
                    ent.Layer == settings.LayerParcel ||
                    ent.Layer == settings.LayerParcelPoint)
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
                else if (ent.Layer == settings.LayerCenterline)
                {
                    // Revert centerline back to pending state
                    var meta = XDataManager.ReadMetadata(ent);
                    if (meta != null)
                    {
                        ent.UpgradeOpen();
                        XDataManager.WriteMetadata(ent, new FTTHMetadata
                        {
                            SourceHandle = ent.Handle.ToString(),
                            Side = "CENTER",
                            RoadWidth = meta.RoadWidth,
                            OffsetDistance = 0.0,
                            ObjectType = "CENTERLINE_PENDING"
                        });
                    }
                }
            }
        }
    }
}
