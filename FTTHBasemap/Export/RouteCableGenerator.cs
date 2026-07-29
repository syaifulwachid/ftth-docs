using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using FTTHBasemap.UI;

namespace FTTHBasemap.Export
{
    public class RouteCableGenerator
    {
        public static void WriteLog(string message)
        {
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    string docDir = Path.GetDirectoryName(doc.Name);
                    if (!string.IsNullOrEmpty(docDir) && Directory.Exists(docDir))
                    {
                        string logPath = Path.Combine(docDir, "FTTH_AutoRoute_Log.txt");
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        File.AppendAllText(logPath, $"[{timestamp}] {message}\r\n");
                    }
                }
            }
            catch { }
        }

        public class PoleNode
        {
            public Point3d Position { get; set; }
            public ObjectId EntityId { get; set; }
            public string LayerName { get; set; }
            public string PoleType { get; set; }
            public List<PoleEdge> Neighbors { get; set; } = new List<PoleEdge>();
        }

        public class PoleEdge
        {
            public PoleNode Target { get; set; }
            public double Length { get; set; }
            public Curve StreetSegment { get; set; }
        }

        public class HomeCluster
        {
            public List<Point3d> HomePositions { get; set; } = new List<Point3d>();
            public List<ObjectId> HomeIds { get; set; } = new List<ObjectId>();
            public Point3d Centerpoint { get; set; }
            public PoleNode AssignedPole { get; set; }
            public string SequenceName { get; set; }
        }

        public class HomeNode
        {
            public Point3d Position { get; set; }
            public ObjectId EntityId { get; set; }
        }

        public class CableLine
        {
            public string LineName { get; set; } // e.g. "Line A"
            public List<PoleNode> PathNodes { get; set; } = new List<PoleNode>();
            public List<HomeCluster> ConnectedFats { get; set; } = new List<HomeCluster>();
            public string CoreSpecification { get; set; } // "24C", "36C", "48C"
        }

        private class FatCandidate
        {
            public HomeCluster Cluster { get; set; }
            public List<PoleNode> Path { get; set; }
            public double PathLength { get; set; }
            public int BranchIndex { get; set; }
            public bool HasDownstream { get; set; }
            public double BacktrackDist { get; set; }
            public double BranchLen { get; set; }
            public int LastVisitedIdx { get; set; }
        }

        // Helper to check if layer is a pole layer
        private static bool IsPoleLayer(string layerName)
        {
            string upper = layerName.ToUpper();
            return upper.StartsWith("FTTH-POLE") || upper.Contains("POLE") || upper.Contains("TIANG");
        }

        // Helper to identify pole type (simplified fallback matching KmlSndKasarExporter)
        private static string IdentifyPoleType(Entity ent, string layerName)
        {
            string upperLayer = layerName.ToUpper();
            if (upperLayer.Equals("FTTH-POLE-NP725", StringComparison.OrdinalIgnoreCase)) return "NP 7 2.5\"";
            if (upperLayer.Equals("FTTH-POLE-NP73", StringComparison.OrdinalIgnoreCase)) return "NP 7 3\"";
            if (upperLayer.Equals("FTTH-POLE-NP74", StringComparison.OrdinalIgnoreCase)) return "NP 7 4\"";
            if (upperLayer.Equals("FTTH-POLE-NP94", StringComparison.OrdinalIgnoreCase)) return "NP 9 4\"";
            if (upperLayer.Equals("FTTH-POLE-EXISTING", StringComparison.OrdinalIgnoreCase)) return "EXT TEL";
            return "POLE";
        }

        // 1. Build the network graph from centerline and poles
        public static List<PoleNode> BuildNetworkGraph(Database db, Transaction tr)
        {
            var nodes = new List<PoleNode>();
            var centerlines = new List<Curve>();

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            // Scan model space for poles and centerlines
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (!(obj is Entity ent)) continue;

                if (ent.Layer.Equals("FTTH-CENTERLINE", StringComparison.OrdinalIgnoreCase) && ent is Curve curve)
                {
                    centerlines.Add(curve);
                }
                else if (IsPoleLayer(ent.Layer))
                {
                    Point3d pos;
                    if (ent is BlockReference br)
                    {
                        pos = br.Position;
                    }
                    else if (ent is Circle circle)
                    {
                        pos = circle.Center;
                    }
                    else
                    {
                        continue;
                    }

                    nodes.Add(new PoleNode
                    {
                        Position = pos,
                        EntityId = id,
                        LayerName = ent.Layer,
                        PoleType = IdentifyPoleType(ent, ent.Layer)
                    });
                }
            }

            // Link nodes that are adjacent along the same centerline curve
            double proximityThreshold = 15.0; // max meters a pole can be from centerline to be considered on that street
            foreach (Curve cl in centerlines)
            {
                var polesOnStreet = new List<(PoleNode Node, double Param)>();
                foreach (var node in nodes)
                {
                    try
                    {
                        Point3d closestPt = cl.GetClosestPointTo(node.Position, false);
                        double dist = node.Position.DistanceTo(closestPt);
                        if (dist <= proximityThreshold)
                        {
                            double param = cl.GetParameterAtPoint(closestPt);
                            polesOnStreet.Add((node, param));
                        }
                    }
                    catch { }
                }

                // Sort poles along the centerline parameter
                polesOnStreet.Sort((a, b) => a.Param.CompareTo(b.Param));

                // Connect adjacent poles along the street segment
                for (int i = 0; i < polesOnStreet.Count - 1; i++)
                {
                    var nodeA = polesOnStreet[i].Node;
                    var nodeB = polesOnStreet[i + 1].Node;
                    double dist = nodeA.Position.DistanceTo(nodeB.Position);

                    // Add edge in both directions if not already present
                    if (!nodeA.Neighbors.Any(e => e.Target == nodeB))
                    {
                        nodeA.Neighbors.Add(new PoleEdge { Target = nodeB, Length = dist, StreetSegment = cl });
                    }
                    if (!nodeB.Neighbors.Any(e => e.Target == nodeA))
                    {
                        nodeB.Neighbors.Add(new PoleEdge { Target = nodeA, Length = dist, StreetSegment = cl });
                    }
                }
            }

            // Link different street networks at intersections/junctions (T-junctions, crossroads, or close endpoints)
            for (int i = 0; i < centerlines.Count; i++)
            {
                Curve cl1 = centerlines[i];
                for (int j = i + 1; j < centerlines.Count; j++)
                {
                    Curve cl2 = centerlines[j];
                    
                    Point3dCollection intersectPts = new Point3dCollection();
                    try
                    {
                        cl1.IntersectWith(cl2, Intersect.OnBothOperands, intersectPts, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch { }

                    List<Point3d> connectionPts = new List<Point3d>();
                    foreach (Point3d pt in intersectPts)
                    {
                        connectionPts.Add(new Point3d(pt.X, pt.Y, 0));
                    }

                    // Check endpoint proximity to the other curve (for gaps up to 15.0 meters)
                    double junctionThreshold = 15.0;
                    if (connectionPts.Count == 0)
                    {
                        try
                        {
                            Point3d[] cl1Ends = { cl1.StartPoint, cl1.EndPoint };
                            foreach (Point3d ep in cl1Ends)
                            {
                                Point3d proj = cl2.GetClosestPointTo(ep, false);
                                if (ep.DistanceTo(proj) <= junctionThreshold)
                                {
                                    connectionPts.Add(new Point3d((ep.X + proj.X) / 2.0, (ep.Y + proj.Y) / 2.0, 0));
                                }
                            }

                            Point3d[] cl2Ends = { cl2.StartPoint, cl2.EndPoint };
                            foreach (Point3d ep in cl2Ends)
                            {
                                Point3d proj = cl1.GetClosestPointTo(ep, false);
                                if (ep.DistanceTo(proj) <= junctionThreshold)
                                {
                                    connectionPts.Add(new Point3d((ep.X + proj.X) / 2.0, (ep.Y + proj.Y) / 2.0, 0));
                                }
                            }
                        }
                        catch { }
                    }

                    // Connect the closest poles of the two streets near the meeting/intersection point
                    foreach (Point3d jPt in connectionPts)
                    {
                        PoleNode bestPole1 = null;
                        double bestDist1 = double.MaxValue;

                        PoleNode bestPole2 = null;
                        double bestDist2 = double.MaxValue;

                        foreach (var node in nodes)
                        {
                            // Check if node is close to cl1
                            try
                            {
                                Point3d proj1 = cl1.GetClosestPointTo(node.Position, false);
                                if (node.Position.DistanceTo(proj1) <= proximityThreshold)
                                {
                                    double d = node.Position.DistanceTo(jPt);
                                    if (d < bestDist1) { bestDist1 = d; bestPole1 = node; }
                                }
                            }
                            catch { }

                            // Check if node is close to cl2
                            try
                            {
                                Point3d proj2 = cl2.GetClosestPointTo(node.Position, false);
                                if (node.Position.DistanceTo(proj2) <= proximityThreshold)
                                {
                                    double d = node.Position.DistanceTo(jPt);
                                    if (d < bestDist2) { bestDist2 = d; bestPole2 = node; }
                                }
                            }
                            catch { }
                        }

                        if (bestPole1 != null && bestPole2 != null && bestPole1 != bestPole2)
                        {
                            double linkDist = bestPole1.Position.DistanceTo(bestPole2.Position);
                            if (linkDist <= 60.0) // Max span crossing the intersection
                            {
                                if (!bestPole1.Neighbors.Any(e => e.Target == bestPole2))
                                {
                                    bestPole1.Neighbors.Add(new PoleEdge { Target = bestPole2, Length = linkDist, StreetSegment = cl1 });
                                }
                                if (!bestPole2.Neighbors.Any(e => e.Target == bestPole1))
                                {
                                    bestPole2.Neighbors.Add(new PoleEdge { Target = bestPole1, Length = linkDist, StreetSegment = cl2 });
                                }
                            }
                        }
                    }
                }
            }

            // Fallback for isolated poles: link them to their nearest neighbor pole within 50 meters
            foreach (var node in nodes)
            {
                if (node.Neighbors.Count == 0)
                {
                    PoleNode nearest = null;
                    double minDist = double.MaxValue;
                    foreach (var other in nodes)
                    {
                        if (other == node) continue;
                        double d = node.Position.DistanceTo(other.Position);
                        if (d < minDist)
                        {
                            minDist = d;
                            nearest = other;
                        }
                    }

                    if (nearest != null && minDist <= 50.0)
                    {
                        if (!node.Neighbors.Any(e => e.Target == nearest))
                        {
                            node.Neighbors.Add(new PoleEdge { Target = nearest, Length = minDist, StreetSegment = null });
                        }
                        if (!nearest.Neighbors.Any(e => e.Target == node))
                        {
                            nearest.Neighbors.Add(new PoleEdge { Target = node, Length = minDist, StreetSegment = null });
                        }
                    }
                }
            }

            return nodes;
        }

        // Helper to retrieve the next available index for FTTH Group
        private static int GetNextGroupIndex(Database db)
        {
            int maxIdx = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead) as DBDictionary;
                if (groupDict != null)
                {
                    foreach (DBDictionaryEntry entry in groupDict)
                    {
                        if (entry.Key.StartsWith("FTTH-GRP_"))
                        {
                            string idxStr = entry.Key.Substring(9);
                            if (int.TryParse(idxStr, out int idx))
                            {
                                if (idx > maxIdx) maxIdx = idx;
                            }
                        }
                    }
                }
                tr.Commit();
            }
            return maxIdx + 1;
        }

        // Helper to get poles sorted by network shortest path distance (falling back to Euclidean) from the FDT start pole
        private static List<PoleNode> GetPolesSortedByDistance(List<PoleNode> graphNodes, PoleNode startNode)
        {
            var distances = new Dictionary<PoleNode, double>();
            foreach (var node in graphNodes)
            {
                distances[node] = double.MaxValue;
            }
            distances[startNode] = 0;

            var queue = new List<PoleNode> { startNode };
            var visited = new HashSet<PoleNode>();

            while (queue.Count > 0)
            {
                queue.Sort((x, y) => distances[x].CompareTo(distances[y]));
                var current = queue[0];
                queue.RemoveAt(0);

                if (!visited.Add(current)) continue;

                foreach (var edge in current.Neighbors)
                {
                    var neighbor = edge.Target;
                    double alt = distances[current] + edge.Length;
                    if (alt < distances[neighbor])
                    {
                        distances[neighbor] = alt;
                        if (!visited.Contains(neighbor))
                        {
                            queue.Add(neighbor);
                        }
                    }
                }
            }

            foreach (var node in graphNodes)
            {
                if (distances[node] == double.MaxValue)
                {
                    distances[node] = startNode.Position.DistanceTo(node.Position);
                }
            }

            return graphNodes.OrderBy(n => distances[n]).ToList();
        }

        // Helper to check if the shortest path between start and end poles contains any occupied poles (excluding start and end itself)
        private static bool HasOccupiedPoleInPath(List<PoleNode> allNodes, PoleNode start, PoleNode end, HashSet<ObjectId> assignedPoles, out string reason)
        {
            reason = "";
            if (start == end) return false;

            var path = FindShortestPath(allNodes, start, end);
            if (path == null)
            {
                reason = $"Graf Terputus (tidak ada jalur jalan yang menghubungkan tiang {start.Position} dan {end.Position})";
                return true;
            }
            if (path.Count <= 2) return false;

            for (int i = 1; i < path.Count - 1; i++)
            {
                if (assignedPoles.Contains(path[i].EntityId))
                {
                    reason = $"Jalur melewati tiang occupied di {path[i].Position}";
                    return true;
                }
            }
            return false;
        }

        private static bool HasOccupiedPoleInPath(List<PoleNode> allNodes, PoleNode start, PoleNode end, HashSet<ObjectId> assignedPoles)
        {
            string dummy;
            return HasOccupiedPoleInPath(allNodes, start, end, assignedPoles, out dummy);
        }

        // --- GEOMETRIC VALIDATION FOR BOUNDARIES (NON-OVERLAPPING CONVEX HULLS) ---

        private static List<Point2d> ConvexHull2D(List<Point2d> pts)
        {
            if (pts.Count <= 3) return pts;

            var sorted = pts.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

            List<Point2d> lower = new List<Point2d>();
            foreach (var p in sorted)
            {
                while (lower.Count >= 2 && CrossProduct2D(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0.0)
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(p);
            }

            List<Point2d> upper = new List<Point2d>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                Point2d p = sorted[i];
                while (upper.Count >= 2 && CrossProduct2D(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0.0)
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);

            lower.AddRange(upper);
            return lower;
        }

        private static double CrossProduct2D(Point2d o, Point2d a, Point2d b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        private static bool IsPointInPolygon2D(Point2d pt, List<Point2d> poly)
        {
            int numVerts = poly.Count;
            if (numVerts < 3) return false;

            bool inside = false;
            for (int i = 0, j = numVerts - 1; i < numVerts; j = i++)
            {
                Point2d pi = poly[i];
                Point2d pj = poly[j];

                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool DoSegmentsIntersect(Point2d p1, Point2d q1, Point2d p2, Point2d q2)
        {
            // If they share an endpoint, they don't cross (they only touch at the end)
            if ((Math.Abs(p1.X - p2.X) < 0.001 && Math.Abs(p1.Y - p2.Y) < 0.001) ||
                (Math.Abs(p1.X - q2.X) < 0.001 && Math.Abs(p1.Y - q2.Y) < 0.001) ||
                (Math.Abs(q1.X - p2.X) < 0.001 && Math.Abs(q1.Y - p2.Y) < 0.001) ||
                (Math.Abs(q1.X - q2.X) < 0.001 && Math.Abs(q1.Y - q2.Y) < 0.001))
            {
                return false;
            }

            double o1 = Orientation(p1, q1, p2);
            double o2 = Orientation(p1, q1, q2);
            double o3 = Orientation(p2, q2, p1);
            double o4 = Orientation(p2, q2, q1);

            if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) &&
                ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0)))
                return true;

            if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;

            return false;
        }

        private static double Orientation(Point2d p, Point2d q, Point2d r)
        {
            double val = (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);
            if (Math.Abs(val) < 1e-9) return 0;
            return (val > 0) ? 1 : -1;
        }

        private static bool OnSegment(Point2d p, Point2d q, Point2d r)
        {
            if (q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) &&
                q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y))
                return true;
            return false;
        }

        private static bool CrossesOtherClusterBoundary(Point3d homePos, Point3d fatPos, List<HomeCluster> otherClusters, out string reason)
        {
            reason = "";
            Point2d h2d = new Point2d(homePos.X, homePos.Y);
            Point2d f2d = new Point2d(fatPos.X, fatPos.Y);

            foreach (var cluster in otherClusters)
            {
                if (cluster.HomePositions.Count == 0) continue;

                // Same-pole check to prevent same-pole boundary collision
                if (cluster.AssignedPole != null && cluster.AssignedPole.Position.DistanceTo(fatPos) < 1.0)
                {
                    continue;
                }

                // Check intersection with actual drop lines of the other cluster
                if (cluster.AssignedPole != null)
                {
                    Point2d otherPole2d = new Point2d(cluster.AssignedPole.Position.X, cluster.AssignedPole.Position.Y);
                    for (int idx = 0; idx < cluster.HomePositions.Count; idx++)
                    {
                        Point2d otherHome2d = new Point2d(cluster.HomePositions[idx].X, cluster.HomePositions[idx].Y);
                        if (DoSegmentsIntersect(h2d, f2d, otherHome2d, otherPole2d))
                        {
                            reason = $"Kabel drop melintasi jalur kabel drop milik klaster tiang {cluster.AssignedPole.Position} ke rumah {cluster.HomePositions[idx]}";
                            return true;
                        }
                    }
                }

                var pts = cluster.HomePositions.Select(p => new Point2d(p.X, p.Y)).ToList();

                if (pts.Count >= 3)
                {
                    var hull = ConvexHull2D(pts);
                    
                    if (IsPointInPolygon2D(h2d, hull))
                    {
                        reason = $"Rumah berada di dalam wilayah klaster tiang {cluster.AssignedPole?.Position}";
                        return true;
                    }

                    for (int i = 0; i < hull.Count; i++)
                    {
                        Point2d p1 = hull[i];
                        Point2d p2 = hull[(i + 1) % hull.Count];
                        if (DoSegmentsIntersect(h2d, f2d, p1, p2))
                        {
                            reason = $"Kabel drop melintasi batas wilayah (boundary edge) klaster tiang {cluster.AssignedPole?.Position}";
                            return true;
                        }
                    }
                }
                else if (pts.Count == 2)
                {
                    Point2d p1 = pts[0];
                    Point2d p2 = pts[1];
                    if (DoSegmentsIntersect(h2d, f2d, p1, p2))
                    {
                        reason = $"Kabel drop melintasi garis penghubung klaster tiang {cluster.AssignedPole?.Position}";
                        return true;
                    }
                }
            }

            return false;
        }

        private static PoleNode FindClosestPole(List<PoleNode> poles, Point3d pos)
        {
            PoleNode closest = null;
            double minDist = double.MaxValue;
            foreach (var p in poles)
            {
                double dist = p.Position.DistanceTo(pos);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = p;
                }
            }
            return closest;
        }

        private class CenterlineDistance
        {
            public Curve Curve { get; set; }
            public Point3d PtOnCl { get; set; }
            public double Dist { get; set; }
        }

        private static List<Curve> GetCenterlineCurves(Database db, Transaction tr)
        {
            var centerlines = new List<Curve>();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Curve curve && curve.Layer.Equals("FTTH-CENTERLINE", StringComparison.OrdinalIgnoreCase))
                {
                    centerlines.Add(curve);
                }
            }
            return centerlines;
        }

        private static bool IsBackToBackConnection(Point3d homePos, Point3d polePos, List<Curve> centerlines)
        {
            if (centerlines == null || centerlines.Count == 0) return false;

            double d_min = double.MaxValue;
            var distances = new List<CenterlineDistance>();

            foreach (var cl in centerlines)
            {
                try
                {
                    Point3d closestPt = cl.GetClosestPointTo(homePos, false);
                    double dist = homePos.DistanceTo(closestPt);
                    distances.Add(new CenterlineDistance { Curve = cl, PtOnCl = closestPt, Dist = dist });
                    if (dist < d_min) d_min = dist;
                }
                catch { }
            }

            if (distances.Count == 0) return false;

            // Sort by distance so the closest centerline is at index 0
            distances = distances.OrderBy(d => d.Dist).ToList();
            var closestStreet = distances[0];
            Vector2d v_closest = new Vector2d(closestStreet.PtOnCl.X - homePos.X, closestStreet.PtOnCl.Y - homePos.Y);

            double threshold = Math.Max(d_min + 5.0, d_min * 1.3);
            threshold = Math.Min(threshold, 25.0);

            bool allowed = false;
            foreach (var item in distances)
            {
                if (item.Dist > threshold) continue;

                Vector2d v_street = new Vector2d(item.PtOnCl.X - homePos.X, item.PtOnCl.Y - homePos.Y);

                // Exclude any other street that is opposite to the closest street (e.g. parallel back alley)
                if (item != closestStreet && v_closest.Length > 0.1 && v_street.Length > 0.1)
                {
                    double dot_cl = v_closest.X * v_street.X + v_closest.Y * v_street.Y;
                    double cos_cl = dot_cl / (v_closest.Length * v_street.Length);
                    if (cos_cl < -0.3) // opposite direction (angle > 107 degrees)
                    {
                        continue;
                    }
                }

                Vector2d v_pole = new Vector2d(polePos.X - homePos.X, polePos.Y - homePos.Y);

                if (v_street.Length < 0.1 || v_pole.Length < 0.1)
                {
                    allowed = true;
                    break;
                }

                double dot = v_street.X * v_pole.X + v_street.Y * v_pole.Y;
                double cos_theta = dot / (v_street.Length * v_pole.Length);
                if (cos_theta >= -0.17)
                {
                    allowed = true;
                    break;
                }
            }

            return !allowed;
        }

        // 2. Perform clustering and place FAT blocks
        public static void EraseExistingFatsAndBoundaries(Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            // Erase entities on layer "FAT Symbol", "FAT", or "FAT AREA"
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Entity ent)
                {
                    if (ent.Layer.Equals("FAT Symbol", StringComparison.OrdinalIgnoreCase) ||
                        ent.Layer.Equals("FAT", StringComparison.OrdinalIgnoreCase) ||
                        ent.Layer.Equals("FAT AREA", StringComparison.OrdinalIgnoreCase))
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                    }
                }
            }

            // Erase previous auto-generated groups starting with FTTH-AUTO-GRP_
            DBDictionary groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite) as DBDictionary;
            if (groupDict != null)
            {
                var keysToErase = new List<string>();
                foreach (DBDictionaryEntry entry in groupDict)
                {
                    if (entry.Key.StartsWith("FTTH-AUTO-GRP_"))
                    {
                        keysToErase.Add(entry.Key);
                    }
                }

                foreach (string key in keysToErase)
                {
                    try
                    {
                        ObjectId grpId = groupDict.GetAt(key);
                        Group grp = tr.GetObject(grpId, OpenMode.ForWrite) as Group;
                        if (grp != null)
                        {
                            grp.Erase();
                        }
                    }
                    catch { }
                }
            }
        }

        public static List<HomeCluster> ClusterAndPlaceFats(Database db, int maxSub, int minSub, double maxDrop, string assetPathDir, Point3d fdtPt, bool hasFdt, bool allowImprovisation, Action<int, string> progressCallback = null)
        {
            var clusters = new List<HomeCluster>();
            var unassignedHomes = new List<HomeNode>();
            var uncoveredHomes = new List<HomeNode>();

            WriteLog("----------------------------------------------------------------------");
            WriteLog($"MULAI PROSES CLUSTERING & PENEMPATAN FAT (Max Sub per FAT: {maxSub}, Min Sub: {minSub}, Max Drop: {maxDrop}m)");
            if (hasFdt)
            {
                WriteLog($"Lokasi FDT terdeteksi pada koordinat: {fdtPt}");
            }
            else
            {
                WriteLog("Lokasi FDT belum ditentukan (Mode Auto-Place FDT aktif saat routing).");
            }

            progressCallback?.Invoke(5, "Memulai pencarian rumah pelanggan...");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Clear old FAT symbols, boundaries, and automatic groups
                EraseExistingFatsAndBoundaries(db, tr);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var centerlineCurves = GetCenterlineCurves(db, tr);

                // Find all subscriber homes first
                var allSubscriberHomes = new List<HomeNode>();
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (!(obj is Entity ent)) continue;

                    if (ent.Layer.Equals("FTTH-HOMEPASS", StringComparison.OrdinalIgnoreCase) ||
                        ent.Layer.Equals("FTTH-NOMOR-RUMAH", StringComparison.OrdinalIgnoreCase))
                    {
                        Point3d pos;
                        if (ent is Polyline poly)
                        {
                            double sx = 0, sy = 0;
                            for (int i = 0; i < poly.NumberOfVertices; i++)
                            {
                                Point2d p = poly.GetPoint2dAt(i);
                                sx += p.X; sy += p.Y;
                            }
                            pos = new Point3d(sx / poly.NumberOfVertices, sy / poly.NumberOfVertices, 0);
                        }
                        else if (ent is Circle circle) pos = circle.Center;
                        else if (ent is BlockReference br) pos = br.Position;
                        else if (ent is DBText txt) pos = txt.Position;
                        else if (ent is MText mt) pos = mt.Location;
                        else continue;

                        allSubscriberHomes.Add(new HomeNode { Position = pos, EntityId = id });
                    }
                }

                if (allSubscriberHomes.Count == 0)
                {
                    throw new InvalidOperationException("Tidak ditemukan rumah pelanggan pada layer FTTH-HOMEPASS atau FTTH-NOMOR-RUMAH!");
                }

                progressCallback?.Invoke(25, $"Menemukan {allSubscriberHomes.Count} rumah. Membangun graf jaringan tiang...");
                var graphNodes = BuildNetworkGraph(db, tr);
                if (graphNodes.Count == 0)
                {
                    throw new InvalidOperationException("Tidak ditemukan tiang di sepanjang jalan! Tempatkan tiang terlebih dahulu.");
                }

                progressCallback?.Invoke(40, $"Graf tiang dibangun dengan {graphNodes.Count} tiang. Memetakan lokasi...");

                var assignedPoles = new HashSet<ObjectId>();
                PoleNode fdtStartNode = null;
                if (hasFdt && fdtPt != Point3d.Origin)
                {
                    fdtStartNode = graphNodes.FirstOrDefault(n => n.Position.DistanceTo(fdtPt) < 1.0);
                    if (fdtStartNode != null)
                    {
                        assignedPoles.Add(fdtStartNode.EntityId);
                        WriteLog($"[RULE EXCLUSION] Tiang FDT pada {fdtStartNode.Position} terdaftar sebagai occupied agar tidak dipasang FAT.");
                    }
                }

                // Process Manual Groups
                var manuallyClusteredHomeIds = new HashSet<ObjectId>();
                DBDictionary groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead) as DBDictionary;
                if (groupDict != null)
                {
                    foreach (DBDictionaryEntry entry in groupDict)
                    {
                        if (entry.Key.StartsWith("FTTH-MAN-GRP_"))
                        {
                            Group grp = tr.GetObject(entry.Value, OpenMode.ForRead) as Group;
                            if (grp != null && grp.NumEntities > 0)
                            {
                                var manualCluster = new HomeCluster();
                                foreach (ObjectId entId in grp.GetAllEntityIds())
                                {
                                    if (entId.IsErased) continue;
                                    var homeNode = allSubscriberHomes.FirstOrDefault(h => h.EntityId == entId);
                                    if (homeNode != null)
                                    {
                                        manualCluster.HomeIds.Add(entId);
                                        manualCluster.HomePositions.Add(homeNode.Position);
                                        manuallyClusteredHomeIds.Add(entId);
                                    }
                                }

                                if (manualCluster.HomeIds.Count > 0)
                                {
                                    double ccx = 0, ccy = 0;
                                    foreach (var p in manualCluster.HomePositions) { ccx += p.X; ccy += p.Y; }
                                    manualCluster.Centerpoint = new Point3d(ccx / manualCluster.HomePositions.Count, ccy / manualCluster.HomePositions.Count, 0);

                                    // Find nearest unoccupied pole
                                    PoleNode bestPole = graphNodes
                                        .Where(n => !assignedPoles.Contains(n.EntityId))
                                        .OrderBy(n => n.Position.DistanceTo(manualCluster.Centerpoint))
                                        .FirstOrDefault();

                                    if (bestPole != null)
                                    {
                                        manualCluster.AssignedPole = bestPole;
                                        assignedPoles.Add(bestPole.EntityId);
                                        clusters.Add(manualCluster);
                                        WriteLog($"[MANUAL GROUP RESPECT] Grup manual {entry.Key} ({manualCluster.HomeIds.Count} pelanggan) dipetakan ke tiang {bestPole.Position}.");
                                    }
                                    else
                                    {
                                        WriteLog($"[MANUAL GROUP WARNING] Grup manual {entry.Key} tidak mendapat tiang kosong karena semua tiang sudah terisi!");
                                    }
                                }
                            }
                        }
                    }
                }

                // Populate unassignedHomes with those not in manual groups
                foreach (var home in allSubscriberHomes)
                {
                    if (!manuallyClusteredHomeIds.Contains(home.EntityId))
                    {
                        unassignedHomes.Add(home);
                    }
                }

                WriteLog($"Rumah Pelanggan: Total={allSubscriberHomes.Count}, Grup Manual={manuallyClusteredHomeIds.Count}, Sisa untuk Otomatis={unassignedHomes.Count}");

                if (fdtStartNode == null)
                {
                    // Fallback to the pole closest to the centroid of all homes
                    double hx = 0, hy = 0;
                    var refHomeList = unassignedHomes.Count > 0 ? unassignedHomes : allSubscriberHomes;
                    foreach (var h in refHomeList) { hx += h.Position.X; hy += h.Position.Y; }
                    Point3d homeCentroid = new Point3d(hx / refHomeList.Count, hy / refHomeList.Count, 0);
                    fdtStartNode = graphNodes.OrderBy(n => n.Position.DistanceTo(homeCentroid)).FirstOrDefault();
                    WriteLog($"Titik acuan awal traversal tiang diset pada tiang terdekat dari centroid pelanggan: {fdtStartNode?.Position}");
                }

                // Sort poles along the shortest network path distance from fdtStartNode outwards
                var sortedPoles = GetPolesSortedByDistance(graphNodes, fdtStartNode);

                progressCallback?.Invoke(60, "Mengelompokkan rumah ke klaster FAT...");

                short[] shiftingColors = new short[] { 1, 2, 3, 4, 5, 6, 30 };
                int startGroupIndex = GetNextGroupIndex(db);

                // --- PHASE 1: INITIAL CLUSTERING ---
                foreach (var refPole in sortedPoles)
                {
                    if (unassignedHomes.Count == 0)
                        break;

                    WriteLog($"[CLUSTERING-DEBUG] Mengevaluasi tiang referensi di {refPole.Position} (tipe={refPole.PoleType})");

                    // Find all unassigned homes within maxDrop from refPole that don't cross occupied poles or other cluster boundaries
                    var tempCandidates = new List<HomeNode>();
                    foreach (var h in unassignedHomes)
                    {
                        double dist = h.Position.DistanceTo(refPole.Position);
                        if (dist > maxDrop) continue;

                        var homePole = FindClosestPole(graphNodes, h.Position);
                        if (homePole != null && homePole != refPole)
                        {
                            string pathReason;
                            if (HasOccupiedPoleInPath(graphNodes, homePole, refPole, assignedPoles, out pathReason))
                            {
                                WriteLog($"  [CLUSTERING-EXCLUDE] Rumah di {h.Position} diabaikan untuk tiang {refPole.Position} karena: {pathReason}");
                                continue;
                            }
                        }

                        if (IsBackToBackConnection(h.Position, refPole.Position, centerlineCurves))
                        {
                            WriteLog($"  [CLUSTERING-EXCLUDE] Rumah di {h.Position} diabaikan untuk tiang {refPole.Position} karena: Koneksi membelakangi jalan (Back-to-Back)");
                            continue;
                        }

                        tempCandidates.Add(h);
                    }

                    // Sort candidates by distance to refPole
                    tempCandidates = tempCandidates.OrderBy(h => h.Position.DistanceTo(refPole.Position)).Take(maxSub).ToList();
                    WriteLog($"  [CLUSTERING-CANDIDATES] Tiang {refPole.Position} memiliki {tempCandidates.Count} kandidat rumah valid.");

                    if (tempCandidates.Count == 0)
                        continue;

                    // Calculate centroid of tempCandidates
                    double cx = 0, cy = 0;
                    foreach (var h in tempCandidates) { cx += h.Position.X; cy += h.Position.Y; }
                    Point3d centroid = new Point3d(cx / tempCandidates.Count, cy / tempCandidates.Count, 0);

                    // Find the best unoccupied pole closest to this centroid
                    PoleNode bestPole = null;
                    double bestDist = double.MaxValue;
                    foreach (var pole in graphNodes)
                    {
                        if (assignedPoles.Contains(pole.EntityId))
                            continue;

                        double dist = pole.Position.DistanceTo(centroid);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestPole = pole;
                        }
                    }

                    if (bestPole == null)
                    {
                        // Fallback: pick any unoccupied pole closest to the first candidate home
                        var firstHome = tempCandidates[0];
                        bestPole = graphNodes
                            .Where(n => !assignedPoles.Contains(n.EntityId))
                            .OrderBy(n => n.Position.DistanceTo(firstHome.Position))
                            .FirstOrDefault();
                        if (bestPole != null)
                        {
                            WriteLog($"  [CLUSTERING-FALLBACK] Memilih tiang fallback terdekat dari rumah pertama: {bestPole.Position}");
                        }
                    }
                    else
                    {
                        WriteLog($"  [CLUSTERING-POLE-SELECT] Tiang terpilih untuk klaster (centroid: {centroid}): {bestPole.Position} (tipe={bestPole.PoleType})");
                    }

                    if (bestPole != null)
                    {
                        // Gather all unassigned homes within maxDrop of bestPole, respecting both Non-Crossing and Boundary constraints
                        var finalClusterHomes = new List<HomeNode>();
                        foreach (var h in unassignedHomes)
                        {
                            double dist = h.Position.DistanceTo(bestPole.Position);
                            if (dist > maxDrop) continue;

                            var homePole = FindClosestPole(graphNodes, h.Position);
                            if (homePole != null && homePole != bestPole)
                            {
                                string pathReason;
                                if (HasOccupiedPoleInPath(graphNodes, homePole, bestPole, assignedPoles, out pathReason))
                                {
                                    WriteLog($"    [CLUSTERING-FINAL-EXCLUDE] Rumah {h.Position} diabaikan dari tiang terpilih {bestPole.Position} karena: {pathReason}");
                                    continue;
                                }
                            }

                            string boundaryReason;
                            if (CrossesOtherClusterBoundary(h.Position, bestPole.Position, clusters, out boundaryReason))
                            {
                                WriteLog($"    [CLUSTERING-FINAL-EXCLUDE] Rumah {h.Position} diabaikan dari tiang terpilih {bestPole.Position} karena: {boundaryReason}");
                                continue;
                            }

                            if (IsBackToBackConnection(h.Position, bestPole.Position, centerlineCurves))
                            {
                                WriteLog($"    [CLUSTERING-FINAL-EXCLUDE] Rumah {h.Position} diabaikan dari tiang terpilih {bestPole.Position} karena: Koneksi membelakangi jalan (Back-to-Back)");
                                continue;
                            }

                            finalClusterHomes.Add(h);
                        }

                        finalClusterHomes = finalClusterHomes.OrderBy(h => h.Position.DistanceTo(bestPole.Position)).Take(maxSub).ToList();

                        if (finalClusterHomes.Count == 0)
                        {
                            var closestHomeInTemp = tempCandidates
                                .OrderBy(h => h.Position.DistanceTo(bestPole.Position))
                                .FirstOrDefault();
                            if (closestHomeInTemp != null)
                            {
                                finalClusterHomes.Add(closestHomeInTemp);
                                WriteLog($"    [CLUSTERING-FINAL-FALLBACK] Memasukkan paksa rumah terdekat {closestHomeInTemp.Position} ke tiang {bestPole.Position} karena finalCluster kosong.");
                            }
                        }

                        if (finalClusterHomes.Count > 0)
                        {
                            assignedPoles.Add(bestPole.EntityId);

                            foreach (var ch in finalClusterHomes)
                            {
                                unassignedHomes.Remove(ch);
                            }

                            var cluster = new HomeCluster();
                            cluster.AssignedPole = bestPole;
                            foreach (var ch in finalClusterHomes)
                            {
                                cluster.HomePositions.Add(ch.Position);
                                cluster.HomeIds.Add(ch.EntityId);
                            }

                            double ccx = 0, ccy = 0;
                            foreach (var p in cluster.HomePositions) { ccx += p.X; ccy += p.Y; }
                            cluster.Centerpoint = new Point3d(ccx / cluster.HomePositions.Count, ccy / cluster.HomePositions.Count, 0);

                            clusters.Add(cluster);

                            WriteLog($"Klaster Awal {clusters.Count}: centroid={cluster.Centerpoint}, terpilih tiang di {bestPole.Position} (tipe={bestPole.PoleType}) untuk {cluster.HomeIds.Count} pelanggan.");
                        }
                    }
                }

                // --- PHASE 2: MERGE AND BALANCE CLUSTERS TO ENFORCE MIN_SUB ---
                bool changesMade = true;
                int mergeIterationCount = 0;
                while (changesMade && mergeIterationCount < 100)
                {
                    changesMade = false;
                    mergeIterationCount++;
                    WriteLog($"[MERGE-ITERATION] Memulai iterasi merge ke-{mergeIterationCount}.");

                    // Sort clusters by size (ascending) to merge smaller clusters into larger ones
                    clusters = clusters.OrderBy(c => c.HomeIds.Count).ToList();

                    for (int i = 0; i < clusters.Count; i++)
                    {
                        var c1 = clusters[i];
                        if (c1.HomeIds.Count == 0) continue;
                        if (c1.HomeIds.Count >= maxSub) continue; // Skip if already at max capacity

                        WriteLog($"  [MERGE-DEBUG] Mengevaluasi klaster ke-{i} di tiang {c1.AssignedPole?.Position} berukuran {c1.HomeIds.Count} untuk merge...");

                        // Find the best target cluster to merge into
                        HomeCluster bestMergeTarget = null;
                        double bestMergeScore = double.MinValue;

                        for (int j = 0; j < clusters.Count; j++)
                        {
                            if (i == j) continue;
                            var c2 = clusters[j];
                            if (c2.HomeIds.Count == 0) continue;

                            // Check capacity constraint
                            int combinedSize = c1.HomeIds.Count + c2.HomeIds.Count;
                            if (combinedSize > maxSub)
                            {
                                continue;
                            }

                            // Check if c2's assigned pole can cover all homes of c1 within maxDrop without crossing
                            bool allCoveredByC2Pole = true;
                            foreach (var pos in c1.HomePositions)
                            {
                                double dropDist = pos.DistanceTo(c2.AssignedPole.Position);
                                if (dropDist > maxDrop)
                                {
                                    allCoveredByC2Pole = false;
                                    break;
                                }

                                var homePole = FindClosestPole(graphNodes, pos);
                                if (homePole != null && homePole != c2.AssignedPole)
                                {
                                    // Temporarily remove c1's assigned pole from assignedPoles check because c1's pole is going to be freed!
                                    var tempAssigned = new HashSet<ObjectId>(assignedPoles);
                                    tempAssigned.Remove(c1.AssignedPole.EntityId);
                                    string pathReason;
                                    if (HasOccupiedPoleInPath(graphNodes, homePole, c2.AssignedPole, tempAssigned, out pathReason))
                                    {
                                        allCoveredByC2Pole = false;
                                        break;
                                    }
                                }

                                // Check boundary crossing against other clusters (excluding c1 and c2 themselves)
                                var otherClusters = clusters.Where(c => c != c1 && c != c2).ToList();
                                string boundaryReason;
                                if (CrossesOtherClusterBoundary(pos, c2.AssignedPole.Position, otherClusters, out boundaryReason))
                                {
                                    allCoveredByC2Pole = false;
                                    break;
                                }

                                if (IsBackToBackConnection(pos, c2.AssignedPole.Position, centerlineCurves))
                                {
                                    allCoveredByC2Pole = false;
                                    break;
                                }
                            }

                            if (allCoveredByC2Pole)
                            {
                                double dist = c1.Centerpoint.DistanceTo(c2.Centerpoint);
                                // Score: higher combined size is better (saving FATs), smaller distance is better as tie-breaker
                                double score = combinedSize * 1000.0 - dist;
                                if (score > bestMergeScore)
                                {
                                    bestMergeScore = score;
                                    bestMergeTarget = c2;
                                }
                            }
                        }

                        if (bestMergeTarget != null)
                        {
                            WriteLog($"[MERGE] Menggabungkan klaster di tiang {c1.AssignedPole.Position} ({c1.HomeIds.Count} sub) ke tiang {bestMergeTarget.AssignedPole.Position} ({bestMergeTarget.HomeIds.Count} sub). Gabungan: {c1.HomeIds.Count + bestMergeTarget.HomeIds.Count} sub.");
                            
                            // Free c1's pole
                            assignedPoles.Remove(c1.AssignedPole.EntityId);

                            // Add c1's homes to c2
                            bestMergeTarget.HomePositions.AddRange(c1.HomePositions);
                            bestMergeTarget.HomeIds.AddRange(c1.HomeIds);

                            // Recalculate c2's centroid
                            double ccx = 0, ccy = 0;
                            foreach (var p in bestMergeTarget.HomePositions) { ccx += p.X; ccy += p.Y; }
                            bestMergeTarget.Centerpoint = new Point3d(ccx / bestMergeTarget.HomePositions.Count, ccy / bestMergeTarget.HomePositions.Count, 0);

                            // Clear c1
                            c1.HomeIds.Clear();
                            c1.HomePositions.Clear();
                            c1.AssignedPole = null;

                            changesMade = true;
                        }
                    }

                    // Remove empty clusters
                    clusters.RemoveAll(c => c.HomeIds.Count == 0);
                }

                // If there are still small clusters, try balancing (stealing) from neighbors
                changesMade = true;
                int balanceIterationCount = 0;
                while (changesMade && balanceIterationCount < 10)
                {
                    changesMade = false;
                    balanceIterationCount++;
                    WriteLog($"[BALANCE-ITERATION] Memulai iterasi penyeimbangan ke-{balanceIterationCount}.");

                    for (int i = 0; i < clusters.Count; i++)
                    {
                        var c1 = clusters[i];
                        if (c1.HomeIds.Count >= minSub) continue;

                        WriteLog($"  [BALANCE-DEBUG] Klaster di tiang {c1.AssignedPole?.Position} berukuran {c1.HomeIds.Count} (< {minSub}). Mencari donor...");

                        // Try to find a donor cluster c2
                        for (int j = 0; j < clusters.Count; j++)
                        {
                            if (i == j) continue;
                            var c2 = clusters[j];
                            if (c2.HomeIds.Count <= minSub)
                            {
                                WriteLog($"    [BALANCE-SKIP] Klaster donor {c2.AssignedPole?.Position} dilewati karena ukurannya ({c2.HomeIds.Count}) <= minimal ({minSub})");
                                continue;
                            }

                            // Find candidate homes in c2 that are close enough to c1's pole and don't cross occupied poles or boundary limits
                            var candidatesToSteal = new List<(Point3d Pos, ObjectId Id, double Distance)>();
                            for (int k = 0; k < c2.HomeIds.Count; k++)
                            {
                                Point3d pos = c2.HomePositions[k];
                                ObjectId id = c2.HomeIds[k];
                                double dropDist = pos.DistanceTo(c1.AssignedPole.Position);

                                if (dropDist <= maxDrop)
                                {
                                    var homePole = FindClosestPole(graphNodes, pos);
                                    if (homePole != null && homePole != c1.AssignedPole)
                                    {
                                        string pathReason;
                                        if (HasOccupiedPoleInPath(graphNodes, homePole, c1.AssignedPole, assignedPoles, out pathReason))
                                        {
                                            WriteLog($"    [BALANCE-CANDIDATE-EXCLUDE] Rumah donor {pos} tidak bisa ditarik ke {c1.AssignedPole.Position} karena: {pathReason}");
                                            continue;
                                        }
                                    }

                                    // Check boundary crossing against other clusters (excluding c1 only, so we check against c2's remaining homes)
                                    var otherClusters = clusters.Where(c => c != c1).ToList();
                                    string boundaryReason;
                                    if (CrossesOtherClusterBoundary(pos, c1.AssignedPole.Position, otherClusters, out boundaryReason))
                                    {
                                        WriteLog($"    [BALANCE-CANDIDATE-EXCLUDE] Rumah donor {pos} tidak bisa ditarik ke {c1.AssignedPole.Position} karena: {boundaryReason}");
                                        continue;
                                    }

                                    if (IsBackToBackConnection(pos, c1.AssignedPole.Position, centerlineCurves))
                                    {
                                        WriteLog($"    [BALANCE-CANDIDATE-EXCLUDE] Rumah donor {pos} tidak bisa ditarik ke {c1.AssignedPole.Position} karena: Koneksi membelakangi jalan (Back-to-Back)");
                                        continue;
                                    }

                                    candidatesToSteal.Add((pos, id, dropDist));
                                }
                                else
                                {
                                    WriteLog($"    [BALANCE-CANDIDATE-EXCLUDE] Rumah donor {pos} terlalu jauh dari {c1.AssignedPole.Position} ({dropDist:F2}m > {maxDrop}m)");
                                }
                            }

                            // Sort candidate stolen homes by distance to c1's pole (closest first)
                            candidatesToSteal = candidatesToSteal.OrderBy(c => c.Distance).ToList();

                            int needed = minSub - c1.HomeIds.Count;
                            int available = c2.HomeIds.Count - minSub; // maximum we can steal without dropping c2 below minSub
                            int stealCount = Math.Min(needed, Math.Min(candidatesToSteal.Count, available));

                            if (stealCount > 0)
                            {
                                WriteLog($"[BALANCE] Memindahkan {stealCount} sub dari klaster {c2.AssignedPole.Position} ke klaster {c1.AssignedPole.Position} agar mencapai batas minimal.");
                                for (int k = 0; k < stealCount; k++)
                                {
                                    var target = candidatesToSteal[k];
                                    c2.HomeIds.Remove(target.Id);
                                    c2.HomePositions.Remove(target.Pos);

                                    c1.HomeIds.Add(target.Id);
                                    c1.HomePositions.Add(target.Pos);
                                    WriteLog($"  [BALANCE-STEAL] Memindahkan rumah {target.Pos} (jarak {target.Distance:F2}m)");
                                }

                                // Recalculate centroids
                                double ccx1 = 0, ccy1 = 0;
                                foreach (var p in c1.HomePositions) { ccx1 += p.X; ccy1 += p.Y; }
                                c1.Centerpoint = new Point3d(ccx1 / c1.HomePositions.Count, ccy1 / c1.HomePositions.Count, 0);

                                double ccx2 = 0, ccy2 = 0;
                                foreach (var p in c2.HomePositions) { ccx2 += p.X; ccy2 += p.Y; }
                                c2.Centerpoint = new Point3d(ccx2 / c2.HomePositions.Count, ccy2 / c2.HomePositions.Count, 0);

                                changesMade = true;
                                break;
                            }
                            else
                            {
                                WriteLog($"    [BALANCE-FAIL] Tidak ada rumah dari klaster {c2.AssignedPole?.Position} yang memenuhi syarat untuk dicuri (butuh={needed}, tersedia={available}, kandidat valid={candidatesToSteal.Count})");
                            }
                        }
                        if (changesMade) break;
                    }
                }

                // --- PHASE 3: ORPHAN CLEAN-UP (FINISHING LOGIC) ---
                if (unassignedHomes.Count > 0)
                {
                    WriteLog($"[FINISHING-PHASE] Ditemukan {unassignedHomes.Count} rumah yang belum terkelompokkan. Memulai proses finishing...");

                    var remainingOrphans = new List<HomeNode>(unassignedHomes);
                    foreach (var orphan in remainingOrphans)
                    {
                        // 1. Try to find the closest existing cluster that has space (< maxSub) and is within maxDrop, respecting crossing & boundary limits
                        HomeCluster bestCluster = null;
                        double bestDist = double.MaxValue;

                        foreach (var c in clusters)
                        {
                            if (c.HomeIds.Count >= maxSub) continue; // HARD LIMIT: Max capacity (16)

                            double dist = orphan.Position.DistanceTo(c.AssignedPole.Position);
                            if (dist > maxDrop) continue; // HARD LIMIT: Max drop distance (50m)

                            // Check graph-based path crossing
                            var homePole = FindClosestPole(graphNodes, orphan.Position);
                            if (homePole != null && homePole != c.AssignedPole)
                            {
                                string pathReason;
                                if (HasOccupiedPoleInPath(graphNodes, homePole, c.AssignedPole, assignedPoles, out pathReason))
                                {
                                    continue;
                                }
                            }

                            // Check spatial boundary crossing (excluding c itself)
                            var otherClusters = clusters.Where(x => x != c).ToList();
                            string boundaryReason;
                            if (CrossesOtherClusterBoundary(orphan.Position, c.AssignedPole.Position, otherClusters, out boundaryReason))
                            {
                                continue;
                            }

                            if (IsBackToBackConnection(orphan.Position, c.AssignedPole.Position, centerlineCurves))
                            {
                                continue;
                            }

                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestCluster = c;
                            }
                        }

                        if (bestCluster != null)
                        {
                            // Assign to this cluster
                            bestCluster.HomeIds.Add(orphan.EntityId);
                            bestCluster.HomePositions.Add(orphan.Position);
                            unassignedHomes.Remove(orphan);

                            // Recalculate centroid
                            double ccx = 0, ccy = 0;
                            foreach (var p in bestCluster.HomePositions) { ccx += p.X; ccy += p.Y; }
                            bestCluster.Centerpoint = new Point3d(ccx / bestCluster.HomePositions.Count, ccy / bestCluster.HomePositions.Count, 0);

                            WriteLog($"  [FINISHING-ASSIGN] Rumah {orphan.Position} dimasukkan ke klaster tiang {bestCluster.AssignedPole.Position} (jarak: {bestDist:F2}m, ukuran baru: {bestCluster.HomeIds.Count}).");
                        }
                    }

                    // 2. If there are still orphans, create new small clusters on closest unoccupied poles, adhering strictly to maxDrop & non-crossing
                    var stillOrphans = new List<HomeNode>(unassignedHomes);
                    foreach (var orphan in stillOrphans)
                    {
                        if (!unassignedHomes.Contains(orphan)) continue; // might have been grouped with another orphan in this step

                        // Find the closest unoccupied pole
                        PoleNode bestPole = null;
                        double bestPoleDist = double.MaxValue;
                        foreach (var pole in graphNodes)
                        {
                            if (assignedPoles.Contains(pole.EntityId)) continue;
                            double dist = pole.Position.DistanceTo(orphan.Position);
                            if (dist < bestPoleDist)
                            {
                                bestPoleDist = dist;
                                bestPole = pole;
                            }
                        }

                        if (bestPole != null)
                        {
                            // Create a new cluster at this pole and gather any other remaining orphans within maxDrop
                            var newCluster = new HomeCluster { AssignedPole = bestPole };
                            
                            var candidates = new List<HomeNode>();
                            foreach (var h in unassignedHomes)
                            {
                                double dist = h.Position.DistanceTo(bestPole.Position);
                                if (dist > maxDrop) continue; // HARD LIMIT: Max drop (50m)

                                var homePole = FindClosestPole(graphNodes, h.Position);
                                if (homePole != null && homePole != bestPole)
                                {
                                    string pathReason;
                                    if (HasOccupiedPoleInPath(graphNodes, homePole, bestPole, assignedPoles, out pathReason))
                                    {
                                        continue;
                                    }
                                }

                                string boundaryReason;
                                if (CrossesOtherClusterBoundary(h.Position, bestPole.Position, clusters, out boundaryReason))
                                {
                                    continue;
                                }

                                if (IsBackToBackConnection(h.Position, bestPole.Position, centerlineCurves))
                                {
                                    continue;
                                }

                                candidates.Add(h);
                            }

                            // Sort by distance and take up to maxSub (16)
                            var finalNewClusterHomes = candidates
                                .OrderBy(h => h.Position.DistanceTo(bestPole.Position))
                                .Take(maxSub)
                                .ToList();

                            if (finalNewClusterHomes.Count > 0)
                            {
                                foreach (var co in finalNewClusterHomes)
                                {
                                    newCluster.HomeIds.Add(co.EntityId);
                                    newCluster.HomePositions.Add(co.Position);
                                    unassignedHomes.Remove(co);
                                }

                                // Calculate centroid
                                double ccx = 0, ccy = 0;
                                foreach (var p in newCluster.HomePositions) { ccx += p.X; ccy += p.Y; }
                                newCluster.Centerpoint = new Point3d(ccx / newCluster.HomePositions.Count, ccy / newCluster.HomePositions.Count, 0);

                                assignedPoles.Add(bestPole.EntityId);
                                clusters.Add(newCluster);

                                WriteLog($"  [FINISHING-NEW-FAT] Membuat FAT baru di tiang {bestPole.Position} untuk {newCluster.HomeIds.Count} rumah ter-skip yang tersisa (mengikuti aturan maxDrop dan non-crossing).");
                            }
                            else
                            {
                                // If the orphan is too far from even the closest unoccupied pole, we cannot group it to a new FAT without violating maxDrop
                                WriteLog($"  [FINISHING-FAIL] ERROR: Rumah {orphan.Position} tidak dapat dimasukkan ke tiang baru {bestPole.Position} karena jarak drop melebihi {maxDrop}m.");
                                // Remove from active unassigned list to prevent infinite loop on this orphan, but log it as unserviced
                                uncoveredHomes.Add(orphan);
                                unassignedHomes.Remove(orphan);
                            }
                        }
                        else
                        {
                            WriteLog($"  [FINISHING-FAIL] ERROR: Rumah {orphan.Position} tidak dapat diproses karena tidak ada tiang kosong tersisa dan seluruh FAT sekitar sudah penuh.");
                            uncoveredHomes.Add(orphan);
                            unassignedHomes.Remove(orphan);
                        }
                    }

                    // Add any remaining unassigned homes to uncovered (safety net)
                    foreach (var remaining in unassignedHomes)
                    {
                        if (!uncoveredHomes.Any(h => h.EntityId == remaining.EntityId))
                        {
                            uncoveredHomes.Add(remaining);
                        }
                    }
                    unassignedHomes.Clear();

                    if (!allowImprovisation)
                    {
                        var invalidClusters = clusters.Where(c => c.HomeIds.Count < minSub).ToList();
                        foreach (var invalidCluster in invalidClusters)
                        {
                            for (int k = 0; k < invalidCluster.HomeIds.Count; k++)
                            {
                                var entId = invalidCluster.HomeIds[k];
                                var pos = invalidCluster.HomePositions[k];
                                if (!uncoveredHomes.Any(h => h.EntityId == entId))
                                {
                                    uncoveredHomes.Add(new HomeNode { EntityId = entId, Position = pos });
                                }
                            }
                            clusters.Remove(invalidCluster);
                            if (invalidCluster.AssignedPole != null)
                            {
                                assignedPoles.Remove(invalidCluster.AssignedPole.EntityId);
                            }
                            WriteLog($"[STRICT MIN_SUB CONSTRAINT] Menghapus klaster di tiang {invalidCluster.AssignedPole?.Position} karena ukurannya ({invalidCluster.HomeIds.Count}) < minSub ({minSub}) dan 'Izinkan Improvisasi' dinonaktifkan.");
                        }
                    }
                }

                // Create Groups and change colors for each cluster
                if (clusters.Count > 0)
                {
                    int groupIdx = startGroupIndex;
                    int colorIdx = 0;
                    foreach (var cluster in clusters)
                    {
                        if (cluster.HomeIds.Count == 0) continue;

                        short color = shiftingColors[colorIdx % shiftingColors.Length];
                        colorIdx++;

                        // Update entity colors
                        foreach (ObjectId id in cluster.HomeIds)
                        {
                            try
                            {
                                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                                if (ent != null)
                                {
                                    ent.ColorIndex = color;
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"Error updating color for entity {id}: {ex.Message}");
                            }
                        }

                        // Create native group
                        try
                        {
                            DBDictionary autoGroupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite) as DBDictionary;
                            string groupName = $"FTTH-AUTO-GRP_{groupIdx}";

                            if (autoGroupDict.Contains(groupName))
                            {
                                ObjectId existingId = autoGroupDict.GetAt(groupName);
                                Group existingGrp = tr.GetObject(existingId, OpenMode.ForWrite) as Group;
                                existingGrp.Erase();
                            }

                            using (Group newGrp = new Group("FTTH Group", true))
                            {
                                autoGroupDict.SetAt(groupName, newGrp);
                                tr.AddNewlyCreatedDBObject(newGrp, true);

                                ObjectIdCollection idCol = new ObjectIdCollection(cluster.HomeIds.ToArray());
                                newGrp.Append(idCol);
                            }

                            groupIdx++;
                            WriteLog($"Grup AutoCAD berhasil dibuat: {groupName} dengan {cluster.HomeIds.Count} pelanggan.");
                        }
                        catch (Exception ex)
                        {
                            WriteLog($"Error creating AutoCAD group for cluster: {ex.Message}");
                        }
                    }
                }

                // Move uncovered/eliminated homes to FTTH-UNCOVER-HOME layer
                if (uncoveredHomes.Count > 0)
                {
                    try
                    {
                        ObjectId uncoverLayerId = BasemapPanel.GetOrCreateLayer(db, tr, "FTTH-UNCOVER-HOME", 8); // color index 8 = Grey
                        // Force color of existing layer to be grey (8)
                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (lt.Has("FTTH-UNCOVER-HOME"))
                        {
                            LayerTableRecord ltr = tr.GetObject(lt["FTTH-UNCOVER-HOME"], OpenMode.ForWrite) as LayerTableRecord;
                            if (ltr != null)
                            {
                                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 8);
                            }
                        }

                        foreach (var home in uncoveredHomes)
                        {
                            try
                            {
                                Entity ent = tr.GetObject(home.EntityId, OpenMode.ForWrite) as Entity;
                                if (ent != null)
                                {
                                    ent.Layer = "FTTH-UNCOVER-HOME";
                                    ent.ColorIndex = 256; // ByLayer (which will be Grey)
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteLog($"Error moving entity {home.EntityId} to FTTH-UNCOVER-HOME layer: {ex.Message}");
                            }
                        }
                        WriteLog($"Berhasil memindahkan {uncoveredHomes.Count} rumah pelanggan ter-skip/tidak tercover ke layer FTTH-UNCOVER-HOME.");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error creating layer or moving uncovered homes: {ex.Message}");
                    }
                }

                tr.Commit();
            }

            progressCallback?.Invoke(80, $"Menempatkan {clusters.Count} blok FAT pada tiang...");
            string fatJsonPath = Path.Combine(assetPathDir, "FAT Symbol n Label on Pole.JSON");
            if (!File.Exists(fatJsonPath))
            {
                throw new FileNotFoundException("Berkas JSON FAT tidak ditemukan: " + fatJsonPath);
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                int tempIndex = 1;
                foreach (var cluster in clusters)
                {
                    if (cluster.AssignedPole == null) continue;
                    Point3d pos = cluster.AssignedPole.Position;

                    var createdIds = AssetDrawer.DrawAsset(db, tr, modelSpace, fatJsonPath, pos, 0.0, 1.0, "");

                    foreach (ObjectId id in createdIds)
                    {
                        DBObject createdObj = tr.GetObject(id, OpenMode.ForWrite);
                        if (createdObj is MText mtext)
                        {
                            if (mtext.Contents.Contains("KNR") || mtext.Layer.Equals("FAT Symbol", StringComparison.OrdinalIgnoreCase))
                            {
                                mtext.Contents = $"FAT-TEMP{tempIndex:D2}";
                            }
                        }
                    }
                    tempIndex++;
                }

                tr.Commit();
            }

            // Boundary generation will be invoked asynchronously from UI after command completion to run in proper AutoCAD context

            WriteLog($"PROSES CLUSTERING FAT SELESAI. Total klaster/FAT terbentuk: {clusters.Count}");
            progressCallback?.Invoke(100, "Penempatan klaster FAT selesai.");
            return clusters;
        }

        // 3. Dijkstra pathfinder
        public static List<PoleNode> FindShortestPath(List<PoleNode> allNodes, PoleNode start, PoleNode end)
        {
            var distances = new Dictionary<PoleNode, double>();
            var previous = new Dictionary<PoleNode, PoleNode>();
            var nodes = new List<PoleNode>();

            foreach (var node in allNodes)
            {
                if (node == start) distances[node] = 0;
                else distances[node] = double.MaxValue;
                nodes.Add(node);
            }

            while (nodes.Count != 0)
            {
                nodes.Sort((x, y) => distances[x].CompareTo(distances[y]));
                var smallest = nodes[0];
                nodes.Remove(smallest);

                if (smallest == end)
                {
                    var path = new List<PoleNode>();
                    while (previous.ContainsKey(smallest))
                    {
                        path.Add(smallest);
                        smallest = previous[smallest];
                    }
                    path.Add(start);
                    path.Reverse();
                    return path;
                }

                if (distances[smallest] == double.MaxValue)
                {
                    break;
                }

                foreach (var neighbor in smallest.Neighbors)
                {
                    double alt = distances[smallest] + neighbor.Length;
                    if (alt < distances[neighbor.Target])
                    {
                        distances[neighbor.Target] = alt;
                        previous[neighbor.Target] = smallest;
                    }
                }
            }
            return null;
        }

        // Helper to find the nearest pole in graph from WCS position
        private static PoleNode FindNearestPoleInGraph(List<PoleNode> graph, Point3d pos)
        {
            PoleNode nearest = null;
            double minDist = double.MaxValue;
            foreach (var node in graph)
            {
                double dist = node.Position.DistanceTo(pos);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = node;
                }
            }
            return nearest;
        }

        private static bool IsFatLabel(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.Contains("\\P"))
            {
                text = text.Split(new string[] { "\\P" }, StringSplitOptions.None)[0];
            }
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\\[A-Za-z].*?;", "");
            return System.Text.RegularExpressions.Regex.IsMatch(text, @"^(?:FDT\d+-)?FAT-(?:TEMP|[A-Z]\d{2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // 4. Route Cable Logic (Dijkstra + Tektok + Core constraints)
        public static List<CableLine> RouteCablesFromFdt(Database db, Point3d fdtPt, List<HomeCluster> placedFats, int fdtCapacity, double maxTektokDist, bool autoPlaceFdt, string assetPathDir, Action<int, string> progressCallback = null, int targetLineCount = 0, bool eraseExistingCables = true)
        {
            var cableLines = new List<CableLine>();

            WriteLog("----------------------------------------------------------------------");
            WriteLog($"MULAI PROSES ROUTING KABEL");
            WriteLog($"Parameter: Kapasitas FDT={fdtCapacity}, Max Tektok={maxTektokDist}m, Auto-Place FDT={autoPlaceFdt}");

            progressCallback?.Invoke(5, "Membangun graf jaringan tiang untuk routing...");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Build the network graph
                var graphNodes = BuildNetworkGraph(db, tr);
                if (graphNodes.Count == 0)
                {
                    throw new InvalidOperationException("Graf jaringan tiang kosong! Pastikan gambar memiliki tiang.");
                }

                // Map assigned poles in placedFats to the new graphNodes references using EntityId or Position
                foreach (var fat in placedFats)
                {
                    if (fat.AssignedPole != null)
                    {
                        var mappedPole = graphNodes.FirstOrDefault(n => n.EntityId == fat.AssignedPole.EntityId);
                        if (mappedPole == null)
                        {
                            mappedPole = graphNodes.OrderBy(n => n.Position.DistanceTo(fat.AssignedPole.Position)).FirstOrDefault();
                            if (mappedPole != null && mappedPole.Position.DistanceTo(fat.AssignedPole.Position) > 1.0)
                            {
                                mappedPole = null; // Too far to be the same pole
                            }
                        }

                        if (mappedPole != null)
                        {
                            fat.AssignedPole = mappedPole;
                        }
                    }
                }

                progressCallback?.Invoke(15, "Menentukan node awal FDT...");
                // Determine FDT Start Node
                PoleNode fdtNode = null;
                if (autoPlaceFdt)
                {
                    // Find a suitable pole close to centroid of FATs (prefer NP 7 4" or existing pole)
                    double cx = 0, cy = 0;
                    foreach (var fat in placedFats) { cx += fat.AssignedPole.Position.X; cy += fat.AssignedPole.Position.Y; }
                    cx /= placedFats.Count; cy /= placedFats.Count;
                    Point3d centroid = new Point3d(cx, cy, 0);
                    WriteLog($"Auto-Place FDT Centroid klaster FAT: {centroid}");

                    // Prefer NP 7 4" or EXT, but EXCLUDE any pole that is already a FAT pole!
                    var fatPoles = new HashSet<ObjectId>();
                    foreach (var fat in placedFats)
                    {
                        if (fat.AssignedPole != null)
                        {
                            fatPoles.Add(fat.AssignedPole.EntityId);
                        }
                    }

                    fdtNode = graphNodes
                        .Where(n => !fatPoles.Contains(n.EntityId) && (n.PoleType.Contains("NP 7 4") || n.PoleType.Contains("EXT") || n.PoleType.Contains("TEL")))
                        .OrderBy(n => n.Position.DistanceTo(centroid))
                        .FirstOrDefault();

                    if (fdtNode == null)
                    {
                        fdtNode = graphNodes
                            .Where(n => !fatPoles.Contains(n.EntityId))
                            .OrderBy(n => n.Position.DistanceTo(centroid))
                            .FirstOrDefault();
                    }

                    if (fdtNode != null)
                    {
                        WriteLog($"[RULE EXCLUSION] Auto-Place FDT memilih tiang pada {fdtNode.Position} (tipe={fdtNode.PoleType}) dan menghindari tiang-tiang FAT ({fatPoles.Count} tiang).");
                    }
                    else
                    {
                        WriteLog("WARNING: Gagal menemukan tiang kosong untuk Auto-Place FDT yang terhindar dari tiang FAT!");
                    }

                    // Place FDT block using AssetDrawer
                    string fdtJsonPath = Path.Combine(assetPathDir, fdtCapacity == 48 ? "FDT48.JSON" : "FDT72.JSON");
                    if (File.Exists(fdtJsonPath))
                    {
                        BlockTable btFdt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord modelSpaceFdt = (BlockTableRecord)tr.GetObject(btFdt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        AssetDrawer.DrawAsset(db, tr, modelSpaceFdt, fdtJsonPath, fdtNode.Position, 0.0, 1.0, "");
                    }
                }
                else
                {
                    fdtNode = FindNearestPoleInGraph(graphNodes, fdtPt);
                    WriteLog($"FDT Manual terpilih pada tiang terdekat: {fdtNode.Position} (tipe={fdtNode.PoleType})");
                }

                if (fdtNode == null)
                {
                    throw new InvalidOperationException("Gagal menempatkan atau mencari tiang FDT!");
                }

                // Reachability check (BFS) for debugging
                try
                {
                    var reachableNodes = new HashSet<PoleNode>();
                    var queue = new Queue<PoleNode>();
                    queue.Enqueue(fdtNode);
                    reachableNodes.Add(fdtNode);
                    while (queue.Count > 0)
                    {
                        var curr = queue.Dequeue();
                        foreach (var edge in curr.Neighbors)
                        {
                            if (!reachableNodes.Contains(edge.Target))
                            {
                                reachableNodes.Add(edge.Target);
                                queue.Enqueue(edge.Target);
                            }
                        }
                    }

                    WriteLog($"[ROUTING-DEBUG] Reachability BFS: Total tiang di graf={graphNodes.Count}. Tiang dapat dicapai dari FDT={reachableNodes.Count}.");
                    int reachableFats = 0;
                    foreach (var fat in placedFats)
                    {
                        if (fat.AssignedPole == null) continue;
                        bool canReach = reachableNodes.Contains(fat.AssignedPole);
                        if (canReach) reachableFats++;
                        WriteLog($"  FAT pada tiang {fat.AssignedPole.Position} (tipe={fat.AssignedPole.PoleType}): Dapat dicapai={canReach}, Jumlah tetangga tiang FAT={fat.AssignedPole.Neighbors.Count}");
                    }
                    WriteLog($"[ROUTING-DEBUG] Total FAT yang dapat dicapai secara topologi dari FDT: {reachableFats} dari {placedFats.Count}");
                }
                catch (Exception ex)
                {
                    WriteLog($"[ROUTING-DEBUG] Error running reachability BFS: {ex.Message}");
                }

                progressCallback?.Invoke(25, "Memulai pencarian rute kabel (Dijkstra + Tektok)...");

                // Run routing logic with retry loop if targetLineCount is specified
                double currentMaxTektok = maxTektokDist;
                List<CableLine> bestCableLines = null;
                int minLineCountFound = int.MaxValue;
                double bestMaxTektokUsed = maxTektokDist;

                int maxRetries = targetLineCount > 0 ? 5 : 1;
                double tektokStep = 5.0;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    WriteLog($"[ROUTE-RETRY] Percobaan {attempt}: Menjalankan routing dengan Max Tektok = {currentMaxTektok:F1}m...");
                    
                    var currentAttemptLines = RunRoutingLogic(graphNodes, fdtNode, placedFats, fdtCapacity, currentMaxTektok);

                    WriteLog($"[ROUTE-RETRY] Hasil Percobaan {attempt}: Menghasilkan {currentAttemptLines.Count} line.");

                    if (bestCableLines == null || currentAttemptLines.Count < minLineCountFound)
                    {
                        bestCableLines = currentAttemptLines;
                        minLineCountFound = currentAttemptLines.Count;
                        bestMaxTektokUsed = currentMaxTektok;
                    }

                    if (targetLineCount > 0 && currentAttemptLines.Count <= targetLineCount)
                    {
                        WriteLog($"[ROUTE-RETRY] Sukses! Target line ({targetLineCount}) tercapai dengan {currentAttemptLines.Count} line pada Max Tektok {currentMaxTektok}m.");
                        break;
                    }

                    if (attempt < maxRetries)
                    {
                        currentMaxTektok += tektokStep;
                        if (currentMaxTektok > 120.0) // Clamp
                            break;
                    }
                }

                // Use the best routing result found
                cableLines = bestCableLines;
                WriteLog($"[ROUTE-RETRY] Hasil akhir terpilih: {cableLines.Count} line dengan Max Tektok = {bestMaxTektokUsed:F1}m.");

                if (eraseExistingCables)
                {
                    progressCallback?.Invoke(75, "Membersihkan kabel lama dan memperbarui label FAT...");
                    EraseExistingCables(db, tr);
                }

                // 5. Update FAT text labels in AutoCAD ModelSpace to their proper sequence name
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (var line in cableLines)
                {
                    foreach (var fat in line.ConnectedFats)
                    {
                        foreach (ObjectId entId in modelSpace)
                        {
                            if (entId.IsErased) continue;
                            DBObject obj = tr.GetObject(entId, OpenMode.ForRead);
                            if (obj is MText mtext && mtext.Location.DistanceTo(fat.AssignedPole.Position) < 2.0)
                            {
                                if (IsFatLabel(mtext.Text) || IsFatLabel(mtext.Contents))
                                {
                                    mtext.UpgradeOpen();
                                    mtext.Contents = fat.SequenceName;
                                    break;
                                }
                            }
                        }
                    }
                }

                progressCallback?.Invoke(85, "Menggambar polyline kabel snapped ke tiang...");
                // 6. Draw cable polylines snapped to poles
                foreach (var line in cableLines)
                {
                    if (line.PathNodes.Count < 2) continue;

                    string layerName = $"FTTH-CABLE-{line.CoreSpecification}";
                    short colorIndex = 3; // Default 24C Green
                    if (line.CoreSpecification == "36C") colorIndex = 14; // Maroon
                    else if (line.CoreSpecification == "48C") colorIndex = 6; // Purple

                    // Ensure layer exists
                    BasemapPanel.GetOrCreateLayer(db, tr, layerName, colorIndex);

                    modelSpace.UpgradeOpen();
                    using (Polyline poly = new Polyline())
                    {
                        poly.Layer = layerName;
                        poly.ColorIndex = colorIndex;

                        for (int i = 0; i < line.PathNodes.Count; i++)
                        {
                            Point3d pt = line.PathNodes[i].Position;
                            poly.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0.0, 0.0, 0.0);
                        }

                        modelSpace.AppendEntity(poly);
                        tr.AddNewlyCreatedDBObject(poly, true);
                    }
                }

                // --- ADD/UPDATE FDT LINE COUNT LABEL ---
                try
                {
                    BasemapPanel.GetOrCreateLayer(db, tr, "FTTH-FDT-LABEL", 2);
                    bool labelUpdated = false;
                    foreach (ObjectId entId in modelSpace)
                    {
                        if (entId.IsErased) continue;
                        DBObject obj = tr.GetObject(entId, OpenMode.ForRead);
                        if (obj is MText mtext && mtext.Location.DistanceTo(fdtPt) < 3.0)
                        {
                            string cleanContents = mtext.Contents;
                            var lines = cleanContents.Split(new[] { "\\P", "\\p", "\n", "\r" }, System.StringSplitOptions.None).ToList();
                            lines.RemoveAll(l => l.Contains("Line") || l.Contains("[") || l.Contains("Kabel") || l.Contains("line"));

                            string newContents = string.Join("\\P", lines) + $"\\P[{cableLines.Count} Line]";
                            mtext.UpgradeOpen();
                            mtext.Contents = newContents;
                            labelUpdated = true;
                            break;
                        }
                        else if (obj is DBText dbtext && dbtext.Position.DistanceTo(fdtPt) < 3.0)
                        {
                            string origText = dbtext.TextString;
                            int bracketIdx = origText.IndexOf(" [");
                            if (bracketIdx >= 0)
                            {
                                origText = origText.Substring(0, bracketIdx);
                            }
                            dbtext.UpgradeOpen();
                            dbtext.TextString = origText + $" [{cableLines.Count} Line]";
                            labelUpdated = true;
                            break;
                        }
                    }

                    if (!labelUpdated)
                    {
                        using (MText newLabel = new MText())
                        {
                            newLabel.Contents = $"FDT\\P[{cableLines.Count} Line]";
                            newLabel.Location = fdtPt + new Vector3d(1.5, -1.5, 0); // slightly offset
                            newLabel.Height = 1.2;
                            newLabel.Layer = "FTTH-FDT-LABEL";
                            newLabel.ColorIndex = 2; // Yellow
                            modelSpace.UpgradeOpen();
                            modelSpace.AppendEntity(newLabel);
                            tr.AddNewlyCreatedDBObject(newLabel, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Error updating FDT line count label: {ex.Message}");
                }

                tr.Commit();
            }

            WriteLog($"PROSES ROUTING KABEL SELESAI. Total line kabel digambar: {cableLines.Count}");
            progressCallback?.Invoke(100, "Routing kabel selesai.");
            return cableLines;
        }

        private static void FinalizeLine(CableLine cableLine, char lineChar)
        {
            cableLine.ConnectedFats.Sort((x, y) =>
            {
                int idxX = -1;
                if (x.AssignedPole != null)
                {
                    idxX = cableLine.PathNodes.IndexOf(x.AssignedPole);
                }
                int idxY = -1;
                if (y.AssignedPole != null)
                {
                    idxY = cableLine.PathNodes.IndexOf(y.AssignedPole);
                }
                return idxX.CompareTo(idxY);
            });

            for (int i = 0; i < cableLine.ConnectedFats.Count; i++)
            {
                var fat = cableLine.ConnectedFats[i];
                fat.SequenceName = $"FAT-{lineChar}{(i + 1):D2}";
                WriteLog($"[SORTED-LABEL] FAT {fat.SequenceName} di {fat.AssignedPole.Position}");
            }

            int numFats = cableLine.ConnectedFats.Count;
            if (numFats <= 10) cableLine.CoreSpecification = "24C";
            else if (numFats <= 15) cableLine.CoreSpecification = "36C";
            else cableLine.CoreSpecification = "48C";

            WriteLog($"Line {lineChar} selesai dirangkai: total FAT={numFats}, kapasitas terpilih={cableLine.CoreSpecification}");
        }

        public static List<CableLine> RouteCablesManually(
            Database db,
            Point3d fdtPt,
            List<HomeCluster> placedFats,
            List<HomeCluster> correctSequence,
            List<HomeCluster> newSequenceToAppend,
            int fdtCapacity,
            double maxTektokDist,
            string assetPathDir,
            Action<int, string> progressCallback = null,
            int targetLineCount = 0)
        {
            var cableLines = new List<CableLine>();

            WriteLog("----------------------------------------------------------------------");
            WriteLog($"MULAI PROSES ROUTING KABEL SECARA MANUAL/KOREKSI DENGAN TARGET LINE = {targetLineCount}");

            progressCallback?.Invoke(5, "Membangun graf jaringan tiang untuk routing...");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var graphNodes = BuildNetworkGraph(db, tr);
                if (graphNodes.Count == 0)
                {
                    throw new InvalidOperationException("Graf jaringan tiang kosong! Pastikan gambar memiliki tiang.");
                }

                double currentMaxTektok = maxTektokDist;
                List<CableLine> bestCableLines = null;
                int minLineCountFound = int.MaxValue;
                double bestMaxTektokUsed = maxTektokDist;

                int maxRetries = targetLineCount > 0 ? 5 : 1;
                double tektokStep = 5.0;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    WriteLog($"[ROUTE-MANUAL-RETRY] Percobaan {attempt}: Menjalankan routing manual dengan Max Tektok = {currentMaxTektok:F1}m...");
                    
                    // Reset SequenceNames in placedFats for this attempt
                    foreach (var fat in placedFats) { fat.SequenceName = ""; }
                    foreach (var fat in correctSequence) { fat.SequenceName = ""; }
                    foreach (var fat in newSequenceToAppend) { fat.SequenceName = ""; }

                    var currentAttemptLines = RunManualRoutingLogicInternal(
                        db,
                        fdtPt,
                        placedFats,
                        correctSequence,
                        newSequenceToAppend,
                        fdtCapacity,
                        currentMaxTektok,
                        graphNodes
                    );

                    WriteLog($"[ROUTE-MANUAL-RETRY] Hasil Percobaan {attempt}: Menghasilkan {currentAttemptLines.Count} line.");

                    if (bestCableLines == null || currentAttemptLines.Count < minLineCountFound)
                    {
                        bestCableLines = currentAttemptLines;
                        minLineCountFound = currentAttemptLines.Count;
                        bestMaxTektokUsed = currentMaxTektok;
                    }

                    if (targetLineCount > 0 && currentAttemptLines.Count <= targetLineCount)
                    {
                        WriteLog($"[ROUTE-MANUAL-RETRY] Sukses! Target line ({targetLineCount}) tercapai dengan {currentAttemptLines.Count} line pada Max Tektok {currentMaxTektok}m.");
                        break;
                    }

                    if (attempt < maxRetries)
                    {
                        currentMaxTektok += tektokStep;
                        if (currentMaxTektok > 120.0) // Clamp
                            break;
                    }
                }

                // Use the best routing result found
                cableLines = bestCableLines;
                WriteLog($"[ROUTE-MANUAL-RETRY] Hasil akhir terpilih: {cableLines.Count} line dengan Max Tektok = {bestMaxTektokUsed:F1}m.");

                // Clean old cables and draw new ones
                progressCallback?.Invoke(75, "Membersihkan kabel lama dan memperbarui label FAT...");
                EraseExistingCables(db, tr);

                // Update FAT text labels in AutoCAD ModelSpace to their proper sequence name
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (var line in cableLines)
                {
                    foreach (var fat in line.ConnectedFats)
                    {
                        foreach (ObjectId entId in modelSpace)
                        {
                            if (entId.IsErased) continue;
                            DBObject obj = tr.GetObject(entId, OpenMode.ForRead);
                            if (obj is MText mtext && mtext.Location.DistanceTo(fat.AssignedPole.Position) < 2.0)
                            {
                                if (IsFatLabel(mtext.Text) || IsFatLabel(mtext.Contents))
                                {
                                    mtext.UpgradeOpen();
                                    mtext.Contents = fat.SequenceName;
                                    break;
                                }
                            }
                        }
                    }
                }

                progressCallback?.Invoke(85, "Menggambar polyline kabel snapped ke tiang...");
                foreach (var line in cableLines)
                {
                    if (line.PathNodes.Count < 2) continue;

                    string layerName = $"FTTH-CABLE-{line.CoreSpecification}";
                    short colorIndex = 3; // Default 24C Green
                    if (line.CoreSpecification == "36C") colorIndex = 14;
                    else if (line.CoreSpecification == "48C") colorIndex = 6;

                    BasemapPanel.GetOrCreateLayer(db, tr, layerName, colorIndex);

                    modelSpace.UpgradeOpen();
                    using (Polyline poly = new Polyline())
                    {
                        poly.Layer = layerName;
                        poly.ColorIndex = colorIndex;

                        for (int i = 0; i < line.PathNodes.Count; i++)
                        {
                            Point3d pt = line.PathNodes[i].Position;
                            poly.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0.0, 0.0, 0.0);
                        }

                        modelSpace.AppendEntity(poly);
                        tr.AddNewlyCreatedDBObject(poly, true);
                    }
                }

                tr.Commit();
            }

            WriteLog($"PROSES ROUTING KABEL SELESAI. Total line kabel digambar: {cableLines.Count}");
            progressCallback?.Invoke(100, "Routing kabel selesai.");
            return cableLines;
        }

        private static List<CableLine> RunManualRoutingLogicInternal(
            Database db,
            Point3d fdtPt,
            List<HomeCluster> placedFats,
            List<HomeCluster> correctSequence,
            List<HomeCluster> newSequenceToAppend,
            int fdtCapacity,
            double maxTektokDist,
            List<PoleNode> graphNodes)
        {
            var cableLines = new List<CableLine>();

            // Helper to map a HomeCluster using its AssignedPole Position
            Action<List<HomeCluster>> mapFats = (list) =>
            {
                foreach (var fat in list)
                {
                    if (fat.AssignedPole != null)
                    {
                        var mappedPole = graphNodes.FirstOrDefault(n => n.EntityId == fat.AssignedPole.EntityId);
                        if (mappedPole == null)
                        {
                            mappedPole = graphNodes.OrderBy(n => n.Position.DistanceTo(fat.AssignedPole.Position)).FirstOrDefault();
                            if (mappedPole != null && mappedPole.Position.DistanceTo(fat.AssignedPole.Position) > 1.0)
                            {
                                mappedPole = null;
                            }
                        }
                        if (mappedPole != null)
                        {
                            fat.AssignedPole = mappedPole;
                        }
                    }
                }
            };

            mapFats(placedFats);
            mapFats(correctSequence);
            mapFats(newSequenceToAppend);

            PoleNode fdtNode = FindNearestPoleInGraph(graphNodes, fdtPt);
            if (fdtNode == null)
            {
                throw new InvalidOperationException("Gagal menempatkan atau mencari tiang FDT!");
            }

            var manualSequence = new List<HomeCluster>(newSequenceToAppend);
            var autoSequence = placedFats
                .Where(f => !correctSequence.Any(c => c.AssignedPole != null && f.AssignedPole != null && c.AssignedPole.Position.DistanceTo(f.AssignedPole.Position) < 0.5) &&
                            !manualSequence.Any(m => m.AssignedPole != null && f.AssignedPole != null && m.AssignedPole.Position.DistanceTo(f.AssignedPole.Position) < 0.5))
                .ToList();

            var unvisitedFats = new List<HomeCluster>();
            unvisitedFats.AddRange(manualSequence);
            unvisitedFats.AddRange(autoSequence);

            int totalFatsToRoute = unvisitedFats.Count;
            int lineIndex = 0;
            int maxFatsAllowed = fdtCapacity == 48 ? 20 : 30;
            int totalFatsCovered = correctSequence.Count;

            CableLine currentLine = null;
            PoleNode currNode = fdtNode;

            if (correctSequence.Count > 0)
            {
                lineIndex++;
                char lineChar = (char)('A' + (lineIndex - 1));
                string lineName = $"Line {lineChar}";
                currentLine = new CableLine { LineName = lineName };

                foreach (var fat in correctSequence)
                {
                    var pathFromCurr = FindShortestPath(graphNodes, currNode, fat.AssignedPole);
                    if (pathFromCurr != null)
                    {
                        var pathA = FindShortestPath(graphNodes, fdtNode, currNode);
                        var visitedNodesSet = (pathA != null) ? new HashSet<PoleNode>(pathA) : new HashSet<PoleNode>();
                        visitedNodesSet.Add(fdtNode);

                        int lcaIdx = 0;
                        for (int i = 0; i < pathFromCurr.Count; i++)
                        {
                            bool isLcaCandidate = visitedNodesSet.Contains(pathFromCurr[i]);
                            if (isLcaCandidate)
                            {
                                lcaIdx = i;
                            }
                        }

                        int lastVisitedIdx = 0;
                        for (int i = 0; i <= lcaIdx; i++)
                        {
                            if (visitedNodesSet.Contains(pathFromCurr[i]))
                            {
                                lastVisitedIdx = i;
                            }
                            else
                            {
                                break;
                            }
                        }

                        double backtrackDist = 0;
                        for (int i = 0; i < lastVisitedIdx; i++)
                        {
                            backtrackDist += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                        }

                        double branchLen = 0;
                        for (int i = lcaIdx; i < pathFromCurr.Count - 1; i++)
                        {
                            branchLen += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                        }

                        bool hasDownstream = false;
                        var remainingList = new List<HomeCluster>();
                        int idx = correctSequence.IndexOf(fat);
                        for (int k = idx + 1; k < correctSequence.Count; k++) remainingList.Add(correctSequence[k]);
                        remainingList.AddRange(unvisitedFats);

                        foreach (var other in remainingList)
                        {
                            var otherPath = FindShortestPath(graphNodes, fdtNode, other.AssignedPole);
                            if (otherPath != null && otherPath.Contains(fat.AssignedPole))
                            {
                                hasDownstream = true;
                                break;
                            }
                        }

                        int category = 2;
                        if (backtrackDist <= maxTektokDist && branchLen <= maxTektokDist && !hasDownstream && branchLen > 0)
                        {
                            category = 1;
                        }

                        if (currentLine.PathNodes.Count == 0)
                        {
                            currentLine.PathNodes.AddRange(pathFromCurr);
                        }
                        else
                        {
                            for (int i = 1; i < pathFromCurr.Count; i++)
                            {
                                currentLine.PathNodes.Add(pathFromCurr[i]);
                            }
                        }

                        if (category == 1)
                        {
                            for (int i = pathFromCurr.Count - 2; i >= lcaIdx; i--)
                            {
                                currentLine.PathNodes.Add(pathFromCurr[i]);
                            }
                            currNode = pathFromCurr[lcaIdx];
                        }
                        else
                        {
                            currNode = fat.AssignedPole;
                        }

                        currentLine.ConnectedFats.Add(fat);
                    }
                }
            }

            while (unvisitedFats.Count > 0 && totalFatsCovered < maxFatsAllowed)
            {
                if (currentLine == null || currentLine.ConnectedFats.Count >= 20)
                {
                    if (currentLine != null)
                    {
                        FinalizeLine(currentLine, (char)('A' + (lineIndex - 1)));
                        cableLines.Add(currentLine);
                    }

                    lineIndex++;
                    char lineChar = (char)('A' + (lineIndex - 1));
                    string lineName = $"Line {lineChar}";
                    currentLine = new CableLine { LineName = lineName };
                    currNode = fdtNode;
                }

                char currentLineChar = (char)('A' + (lineIndex - 1));

                HomeCluster nextFat = null;
                bool isManualNext = false;
                if (manualSequence.Count > 0)
                {
                    nextFat = manualSequence[0];
                    isManualNext = true;
                }

                List<PoleNode> shortestPath = null;
                double minPathLen = 0;
                double backtrackDistSelected = 0;
                int bestCategory = 3;
                int selectedLcaIdx = 0;
                bool bestHasDownstream = false;
                double selectedBranchLen = 0;

                var fdtPathsToUnvisited = new Dictionary<HomeCluster, List<PoleNode>>();
                foreach (var other in unvisitedFats)
                {
                    var p = FindShortestPath(graphNodes, fdtNode, other.AssignedPole);
                    if (p != null)
                    {
                        fdtPathsToUnvisited[other] = p;
                    }
                }

                if (isManualNext)
                {
                    var pathFromCurr = FindShortestPath(graphNodes, currNode, nextFat.AssignedPole);
                    if (pathFromCurr != null)
                    {
                        var pathA = FindShortestPath(graphNodes, fdtNode, currNode);
                        var visitedNodesSet = (pathA != null) ? new HashSet<PoleNode>(pathA) : new HashSet<PoleNode>();
                        visitedNodesSet.Add(fdtNode);

                        int lcaIdx = 0;
                        for (int i = 0; i < pathFromCurr.Count; i++)
                        {
                            bool isLcaCandidate = visitedNodesSet.Contains(pathFromCurr[i]);
                            if (!isLcaCandidate)
                            {
                                foreach (var kvp in fdtPathsToUnvisited)
                                {
                                    if (kvp.Key == nextFat) continue;
                                    if (kvp.Value.Contains(pathFromCurr[i]))
                                    {
                                        isLcaCandidate = true;
                                        break;
                                    }
                                }
                            }

                            if (isLcaCandidate)
                            {
                                lcaIdx = i;
                            }
                        }

                        int lastVisitedIdx = 0;
                        for (int i = 0; i <= lcaIdx; i++)
                        {
                            if (visitedNodesSet.Contains(pathFromCurr[i]))
                            {
                                lastVisitedIdx = i;
                            }
                            else
                            {
                                break;
                            }
                        }

                        double backtrackDist = 0;
                        for (int i = 0; i < lastVisitedIdx; i++)
                        {
                            backtrackDist += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                        }

                        double branchLen = 0;
                        for (int i = lcaIdx; i < pathFromCurr.Count - 1; i++)
                        {
                            branchLen += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                        }

                        bool hasDownstream = false;
                        foreach (var other in unvisitedFats)
                        {
                            if (other == nextFat) continue;
                            var otherPath = fdtPathsToUnvisited.ContainsKey(other) ? fdtPathsToUnvisited[other] : null;
                            if (otherPath != null && otherPath.Contains(nextFat.AssignedPole))
                            {
                                hasDownstream = true;
                                break;
                            }
                        }

                        bool hasUnvisitedFatBeforeLca = false;
                        for (int i = 1; i <= lcaIdx; i++)
                        {
                            foreach (var other in unvisitedFats)
                            {
                                if (other.AssignedPole == pathFromCurr[i])
                                {
                                    hasUnvisitedFatBeforeLca = true;
                                    break;
                                }
                            }
                            if (hasUnvisitedFatBeforeLca) break;
                        }

                        int category = 3;
                        if (backtrackDist <= maxTektokDist && branchLen <= maxTektokDist && !hasDownstream && branchLen > 0 && !hasUnvisitedFatBeforeLca)
                        {
                            category = 1;
                        }
                        else if (backtrackDist == 0)
                        {
                            category = 2;
                        }

                        double pathLen = 0;
                        for (int i = 0; i < pathFromCurr.Count - 1; i++)
                        {
                            pathLen += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                        }

                        shortestPath = pathFromCurr;
                        minPathLen = pathLen;
                        backtrackDistSelected = backtrackDist;
                        bestCategory = category;
                        selectedLcaIdx = lcaIdx;
                        bestHasDownstream = hasDownstream;
                        selectedBranchLen = branchLen;
                    }
                }
                else
                {
                    var candidateList = new List<FatCandidate>();
                    foreach (var fat in unvisitedFats)
                    {
                        var pathFromCurr = FindShortestPath(graphNodes, currNode, fat.AssignedPole);
                        if (pathFromCurr != null)
                        {
                            var pathA = FindShortestPath(graphNodes, fdtNode, currNode);
                            var visitedNodesSet = (pathA != null) ? new HashSet<PoleNode>(pathA) : new HashSet<PoleNode>();
                            visitedNodesSet.Add(fdtNode);

                            int lcaIdx = 0;
                            for (int i = 0; i < pathFromCurr.Count; i++)
                            {
                                bool isLcaCandidate = visitedNodesSet.Contains(pathFromCurr[i]);
                                if (!isLcaCandidate)
                                {
                                    foreach (var kvp in fdtPathsToUnvisited)
                                    {
                                        if (kvp.Key == fat) continue;
                                        if (kvp.Value.Contains(pathFromCurr[i]))
                                        {
                                            isLcaCandidate = true;
                                            break;
                                        }
                                    }
                                }

                                if (isLcaCandidate)
                                {
                                    lcaIdx = i;
                                }
                            }

                            int lastVisitedIdx = 0;
                            for (int i = 0; i <= lcaIdx; i++)
                            {
                                if (visitedNodesSet.Contains(pathFromCurr[i]))
                                {
                                    lastVisitedIdx = i;
                                }
                                else
                                {
                                    break;
                                }
                            }

                            double backtrackDist = 0;
                            for (int i = 0; i < lastVisitedIdx; i++)
                            {
                                backtrackDist += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                            }

                            double branchLen = 0;
                            for (int i = lcaIdx; i < pathFromCurr.Count - 1; i++)
                            {
                                branchLen += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                            }

                            bool hasDownstream = false;
                            foreach (var other in unvisitedFats)
                            {
                                if (other == fat) continue;
                                var otherPath = fdtPathsToUnvisited.ContainsKey(other) ? fdtPathsToUnvisited[other] : null;
                                if (otherPath != null && otherPath.Contains(fat.AssignedPole))
                                {
                                    hasDownstream = true;
                                    break;
                                }
                            }

                            bool hasUnvisitedFatBeforeLca = false;
                            for (int i = 1; i <= lcaIdx; i++)
                            {
                                foreach (var other in unvisitedFats)
                                {
                                    if (other.AssignedPole == pathFromCurr[i])
                                    {
                                        hasUnvisitedFatBeforeLca = true;
                                        break;
                                    }
                                }
                                if (hasUnvisitedFatBeforeLca) break;
                            }

                            int category = 3;
                            if (backtrackDist <= maxTektokDist && branchLen <= maxTektokDist && !hasDownstream && branchLen > 0 && !hasUnvisitedFatBeforeLca)
                            {
                                category = 1;
                            }
                            else if (backtrackDist == 0)
                            {
                                category = 2;
                            }

                            double pathLen = 0;
                            for (int i = 0; i < pathFromCurr.Count - 1; i++)
                            {
                                pathLen += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                            }

                            candidateList.Add(new FatCandidate
                            {
                                Cluster = fat,
                                Path = pathFromCurr,
                                PathLength = pathLen,
                                BranchIndex = category,
                                HasDownstream = hasDownstream,
                                BacktrackDist = backtrackDist,
                                BranchLen = branchLen,
                                LastVisitedIdx = lcaIdx
                            });
                        }
                    }

                    if (candidateList.Count > 0)
                    {
                        candidateList.Sort((x, y) =>
                        {
                            int cmp = x.BranchIndex.CompareTo(y.BranchIndex);
                            if (cmp != 0) return cmp;
                            return x.PathLength.CompareTo(y.PathLength);
                        });

                        var bestCandidate = candidateList[0];
                        nextFat = bestCandidate.Cluster;
                        shortestPath = bestCandidate.Path;
                        minPathLen = bestCandidate.PathLength;
                        backtrackDistSelected = bestCandidate.BacktrackDist;
                        bestCategory = bestCandidate.BranchIndex;
                        selectedLcaIdx = bestCandidate.LastVisitedIdx;
                        bestHasDownstream = bestCandidate.HasDownstream;
                        selectedBranchLen = bestCandidate.BranchLen;
                    }
                }

                if (nextFat == null || shortestPath == null)
                {
                    break;
                }

                if (bestCategory == 3)
                {
                    FinalizeLine(currentLine, currentLineChar);
                    cableLines.Add(currentLine);
                    currentLine = null;
                    continue;
                }

                unvisitedFats.Remove(nextFat);
                if (isManualNext)
                {
                    manualSequence.RemoveAt(0);
                }
                totalFatsCovered++;

                if (currentLine.PathNodes.Count == 0)
                {
                    currentLine.PathNodes.AddRange(shortestPath);
                }
                else
                {
                    for (int i = 1; i < shortestPath.Count; i++)
                    {
                        currentLine.PathNodes.Add(shortestPath[i]);
                    }
                }

                if (bestCategory == 1)
                {
                    for (int i = shortestPath.Count - 2; i >= selectedLcaIdx; i--)
                    {
                        currentLine.PathNodes.Add(shortestPath[i]);
                    }
                    currNode = shortestPath[selectedLcaIdx];
                }
                else
                {
                    currNode = nextFat.AssignedPole;
                }

                currentLine.ConnectedFats.Add(nextFat);
                nextFat.SequenceName = $"FAT-{currentLineChar}{currentLine.ConnectedFats.Count:D2}";
            }

            if (currentLine != null)
            {
                FinalizeLine(currentLine, (char)('A' + (lineIndex - 1)));
                cableLines.Add(currentLine);
            }

            return cableLines;
        }

        public static void EraseExistingCables(Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            foreach (ObjectId entId in modelSpace)
            {
                if (entId.IsErased) continue;
                DBObject obj = tr.GetObject(entId, OpenMode.ForRead);
                if (obj is Entity ent)
                {
                    if (!string.IsNullOrEmpty(ent.Layer) && ent.Layer.StartsWith("FTTH-CABLE-", StringComparison.OrdinalIgnoreCase))
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                    }
                }
            }
        }

        private static List<CableLine> RunRoutingLogic(
            List<PoleNode> graphNodes,
            PoleNode fdtNode,
            List<HomeCluster> placedFats,
            int fdtCapacity,
            double maxTektokDist)
        {
            foreach (var fat in placedFats)
            {
                fat.SequenceName = "";
            }

            var cableLines = new List<CableLine>();
            var unvisitedFats = new List<HomeCluster>(placedFats);
            int totalFatsToRoute = unvisitedFats.Count;
            int lineIndex = 0;

            int maxFatsAllowed = fdtCapacity == 48 ? 20 : 30;
            int totalFatsCovered = 0;

            while (unvisitedFats.Count > 0 && totalFatsCovered < maxFatsAllowed)
            {
                lineIndex++;
                char lineChar = (char)('A' + (lineIndex - 1));
                string lineName = $"Line {lineChar}";

                var cableLine = new CableLine { LineName = lineName };
                PoleNode currNode = fdtNode;

                int maxFatsForThisLine = 20;

                while (cableLine.ConnectedFats.Count < maxFatsForThisLine && unvisitedFats.Count > 0)
                {
                    var pathA = FindShortestPath(graphNodes, fdtNode, currNode);
                    var visitedNodesSet = (pathA != null) ? new HashSet<PoleNode>(pathA) : new HashSet<PoleNode>();
                    visitedNodesSet.Add(fdtNode);

                    var fdtPathsToUnvisited = new Dictionary<HomeCluster, List<PoleNode>>();
                    foreach (var other in unvisitedFats)
                    {
                        var p = FindShortestPath(graphNodes, fdtNode, other.AssignedPole);
                        if (p != null)
                        {
                            fdtPathsToUnvisited[other] = p;
                        }
                    }

                    var candidateList = new List<FatCandidate>();
                    foreach (var fat in unvisitedFats)
                    {
                        var pathFromCurr = FindShortestPath(graphNodes, currNode, fat.AssignedPole);

                        if (pathFromCurr != null)
                        {
                            int lcaIdx = 0;
                            for (int i = 0; i < pathFromCurr.Count; i++)
                            {
                                bool isLcaCandidate = visitedNodesSet.Contains(pathFromCurr[i]);
                                if (!isLcaCandidate)
                                {
                                    foreach (var kvp in fdtPathsToUnvisited)
                                    {
                                        if (kvp.Key == fat) continue;
                                        if (kvp.Value.Contains(pathFromCurr[i]))
                                        {
                                            isLcaCandidate = true;
                                            break;
                                        }
                                    }
                                }

                                if (isLcaCandidate)
                                {
                                    lcaIdx = i;
                                }
                            }

                            int lastVisitedIdx = 0;
                            for (int i = 0; i <= lcaIdx; i++)
                            {
                                if (visitedNodesSet.Contains(pathFromCurr[i]))
                                {
                                    lastVisitedIdx = i;
                                }
                                else
                                {
                                    break;
                                }
                            }

                            double backtrackDist = 0;
                            for (int i = 0; i < lastVisitedIdx; i++)
                            {
                                backtrackDist += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                            }

                            double branchLen = 0;
                            for (int i = lcaIdx; i < pathFromCurr.Count - 1; i++)
                            {
                                branchLen += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                            }

                            bool hasDownstream = false;
                            foreach (var other in unvisitedFats)
                            {
                                if (other == fat) continue;
                                var otherPath = fdtPathsToUnvisited.ContainsKey(other) ? fdtPathsToUnvisited[other] : null;
                                if (otherPath != null && otherPath.Contains(fat.AssignedPole))
                                {
                                    hasDownstream = true;
                                    break;
                                }
                            }

                            bool hasUnvisitedFatBeforeLca = false;
                            for (int i = 1; i <= lcaIdx; i++)
                            {
                                foreach (var other in unvisitedFats)
                                {
                                    if (other.AssignedPole == pathFromCurr[i])
                                    {
                                        hasUnvisitedFatBeforeLca = true;
                                        break;
                                    }
                                }
                                if (hasUnvisitedFatBeforeLca) break;
                            }

                            int category = 3;
                            if (backtrackDist <= maxTektokDist && branchLen <= maxTektokDist && !hasDownstream && branchLen > 0 && !hasUnvisitedFatBeforeLca)
                            {
                                category = 1;
                            }
                            else if (backtrackDist == 0)
                            {
                                category = 2;
                            }

                            double pathLen = 0;
                            for (int i = 0; i < pathFromCurr.Count - 1; i++)
                            {
                                pathLen += pathFromCurr[i].Position.DistanceTo(pathFromCurr[i + 1].Position);
                            }

                            candidateList.Add(new FatCandidate
                            {
                                Cluster = fat,
                                Path = pathFromCurr,
                                PathLength = pathLen,
                                BranchIndex = category,
                                HasDownstream = hasDownstream,
                                BacktrackDist = backtrackDist,
                                BranchLen = branchLen,
                                LastVisitedIdx = lcaIdx
                            });
                        }
                    }

                    if (candidateList.Count == 0)
                    {
                        break;
                    }

                    candidateList.Sort((x, y) =>
                    {
                        int cmp = x.BranchIndex.CompareTo(y.BranchIndex);
                        if (cmp != 0) return cmp;
                        return x.PathLength.CompareTo(y.PathLength);
                    });

                    var bestCandidate = candidateList[0];
                    var nearestFat = bestCandidate.Cluster;
                    var shortestPath = bestCandidate.Path;

                    if (bestCandidate.BranchIndex == 3)
                    {
                        break;
                    }

                    unvisitedFats.Remove(nearestFat);
                    totalFatsCovered++;

                    if (cableLine.PathNodes.Count == 0)
                    {
                        cableLine.PathNodes.AddRange(shortestPath);
                    }
                    else
                    {
                        for (int i = 1; i < shortestPath.Count; i++)
                        {
                            cableLine.PathNodes.Add(shortestPath[i]);
                        }
                    }

                    if (bestCandidate.BranchIndex == 1)
                    {
                        for (int i = shortestPath.Count - 2; i >= bestCandidate.LastVisitedIdx; i--)
                        {
                            cableLine.PathNodes.Add(shortestPath[i]);
                        }
                        currNode = shortestPath[bestCandidate.LastVisitedIdx];
                    }
                    else
                    {
                        currNode = nearestFat.AssignedPole;
                    }

                    cableLine.ConnectedFats.Add(nearestFat);
                    nearestFat.SequenceName = $"FAT-{lineChar}{cableLine.ConnectedFats.Count:D2}";
                }

                FinalizeLine(cableLine, lineChar);
                cableLines.Add(cableLine);
            }

            return cableLines;
        }

        public class PlacedFdt
        {
            public Point3d Position { get; set; }
            public int Capacity { get; set; } // 48 or 72
            public string Name { get; set; }
            public ObjectId EntityId { get; set; }
        }

        public static List<PlacedFdt> ScanFdtsInDrawing(Database db)
        {
            var result = new List<PlacedFdt>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                int fdtIndex = 0;
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                    if (obj is BlockReference br)
                    {
                        string blockName = "";
                        try
                        {
                            if (br.BlockTableRecord != ObjectId.Null)
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                                blockName = btr.Name;
                            }
                        }
                        catch { }

                        bool isFdt = false;
                        if (blockName.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0 || br.Layer.IndexOf("FDT", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isFdt = true;
                        }

                        if (isFdt)
                        {
                            fdtIndex++;
                            int capacity = 48; // Default
                            if (blockName.Contains("72") || br.Layer.Contains("72"))
                            {
                                capacity = 72;
                            }

                            // Look for nearby MText or DBText as the FDT Name/Label (e.g. "FDT 1")
                            string fdtName = $"FDT {fdtIndex}";
                            foreach (ObjectId textId in ms)
                            {
                                if (textId == id || textId.IsErased) continue;
                                DBObject textObj = tr.GetObject(textId, OpenMode.ForRead);
                                string contentText = "";
                                Point3d textPos = Point3d.Origin;

                                if (textObj is MText mt) { contentText = mt.Contents; textPos = mt.Location; }
                                else if (textObj is DBText dt) { contentText = dt.TextString; textPos = dt.Position; }

                                if (!string.IsNullOrEmpty(contentText) && textPos.DistanceTo(br.Position) < 3.0)
                                {
                                    if (contentText.StartsWith("FDT", StringComparison.OrdinalIgnoreCase))
                                    {
                                        fdtName = contentText;
                                        break;
                                    }
                                }
                            }

                            result.Add(new PlacedFdt
                            {
                                Position = br.Position,
                                Capacity = capacity,
                                Name = fdtName,
                                EntityId = id
                            });
                        }
                    }
                }
                tr.Commit();
            }
            return result;
        }

        public static List<HomeCluster> ScanPlacedFats(Database db, List<PoleNode> graphNodes)
        {
            var result = new List<HomeCluster>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    string text = "";
                    Point3d pos = Point3d.Origin;

                    if (obj is MText mt)
                    {
                        text = mt.Contents;
                        pos = mt.Location;
                    }
                    else if (obj is DBText dt)
                    {
                        text = dt.TextString;
                        pos = dt.Position;
                    }

                    if (IsFatLabel(text))
                    {
                        string cleanText = text;
                        if (cleanText.Contains("\\P"))
                        {
                            cleanText = cleanText.Split(new string[] { "\\P" }, StringSplitOptions.None)[0];
                        }
                        cleanText = System.Text.RegularExpressions.Regex.Replace(cleanText, @"\\[A-Za-z].*?;", "");

                        // Find closest pole node in the network graph
                        var closestPole = graphNodes.OrderBy(n => n.Position.DistanceTo(pos)).FirstOrDefault();
                        if (closestPole != null && closestPole.Position.DistanceTo(pos) < 5.0) // Snapping radius
                        {
                            if (!result.Any(c => c.AssignedPole != null && c.AssignedPole.EntityId == closestPole.EntityId))
                            {
                                result.Add(new HomeCluster
                                {
                                    Centerpoint = pos,
                                    AssignedPole = closestPole,
                                    SequenceName = cleanText
                                });
                            }
                        }
                    }
                }
                tr.Commit();
            }
            return result;
        }

        public class FatCluster
        {
            public List<HomeCluster> Fats { get; set; } = new List<HomeCluster>();
            public Point3d Centroid
            {
                get
                {
                    if (Fats.Count == 0) return Point3d.Origin;
                    double x = 0, y = 0;
                    foreach (var fat in Fats) { x += fat.Centerpoint.X; y += fat.Centerpoint.Y; }
                    return new Point3d(x / Fats.Count, y / Fats.Count, 0);
                }
            }
        }

        public static List<FatCluster> ClusterFatsForFdts(List<HomeCluster> fats, int maxCapacity)
        {
            var clusters = new List<FatCluster>();
            foreach (var fat in fats)
            {
                var c = new FatCluster();
                c.Fats.Add(fat);
                clusters.Add(c);
            }

            while (true)
            {
                double minDistance = double.MaxValue;
                int bestI = -1;
                int bestJ = -1;

                for (int i = 0; i < clusters.Count; i++)
                {
                    for (int j = i + 1; j < clusters.Count; j++)
                    {
                        if (clusters[i].Fats.Count + clusters[j].Fats.Count <= maxCapacity)
                        {
                            double d = clusters[i].Centroid.DistanceTo(clusters[j].Centroid);
                            if (d < minDistance)
                            {
                                minDistance = d;
                                bestI = i;
                                bestJ = j;
                            }
                        }
                    }
                }

                if (bestI != -1 && bestJ != -1)
                {
                    clusters[bestI].Fats.AddRange(clusters[bestJ].Fats);
                    clusters.RemoveAt(bestJ);
                }
                else
                {
                    break;
                }
            }

            return clusters;
        }

        public static void RecommendFdts(int n, out int count48, out int count72)
        {
            count48 = 0;
            count72 = 0;
            int minTotalFdts = int.MaxValue;
            int minTotalCapacity = int.MaxValue;

            for (int c72 = 0; c72 <= (n / 30) + 1; c72++)
            {
                for (int c48 = 0; c48 <= (n / 20) + 1; c48++)
                {
                    int totalCapacity = c72 * 30 + c48 * 20;
                    if (totalCapacity >= n)
                    {
                        int totalFdts = c72 + c48;
                        if (totalFdts < minTotalFdts)
                        {
                            minTotalFdts = totalFdts;
                            minTotalCapacity = c72 * 72 + c48 * 48;
                            count48 = c48;
                            count72 = c72;
                        }
                        else if (totalFdts == minTotalFdts)
                        {
                            int cap = c72 * 72 + c48 * 48;
                            if (cap < minTotalCapacity)
                            {
                                minTotalCapacity = cap;
                                count48 = c48;
                                count72 = c72;
                            }
                        }
                    }
                }
            }
        }

        public static string AutoPlaceFdts(Database db, string assetPathDir, Action<int, string> progressCallback = null)
        {
            WriteLog("----------------------------------------------------------------------");
            WriteLog("MULAI PENEMPATAN BANYAK FDT OTOMATIS");

            progressCallback?.Invoke(10, "Membangun graf jaringan tiang...");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var graphNodes = BuildNetworkGraph(db, tr);
                if (graphNodes.Count == 0)
                {
                    return "Error: Graf jaringan tiang kosong! Pastikan gambar memiliki tiang.";
                }

                progressCallback?.Invoke(30, "Memindai FAT terpasang...");
                var fats = ScanPlacedFats(db, graphNodes);
                if (fats.Count == 0)
                {
                    return "Error: Tidak ditemukan FAT di gambar. Pasang FAT terlebih dahulu (Langkah 1).";
                }

                RecommendFdts(fats.Count, out int count48, out int count72);
                WriteLog($"Hasil Rekomendasi untuk {fats.Count} FAT: FDT48={count48}, FDT72={count72}");

                progressCallback?.Invoke(50, "Mengelompokkan FAT geografis...");
                var clusters = ClusterFatsForFdts(fats, 30);
                WriteLog($"Clustering menghasilkan {clusters.Count} klaster.");

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                BasemapPanel.GetOrCreateLayer(db, tr, "FTTH-FDT-LABEL", 2);

                int placedFdtCount = 0;
                var fatPoles = new HashSet<ObjectId>();
                foreach (var fat in fats) { if (fat.AssignedPole != null) fatPoles.Add(fat.AssignedPole.EntityId); }

                var existingFdts = ScanFdtsInDrawing(db);
                foreach (var fdt in existingFdts)
                {
                    try
                    {
                        var ent = tr.GetObject(fdt.EntityId, OpenMode.ForWrite) as Entity;
                        if (ent != null) ent.Erase();
                    }
                    catch { }
                }

                for (int i = 0; i < clusters.Count; i++)
                {
                    var cluster = clusters[i];
                    Point3d centroid = cluster.Centroid;

                    int capacity = cluster.Fats.Count <= 20 ? 48 : 72;
                    string fdtJsonFilename = capacity == 48 ? "FDT48.JSON" : "FDT72.JSON";
                    string fdtJsonPath = Path.Combine(assetPathDir, fdtJsonFilename);

                    var fdtNode = graphNodes
                        .Where(n => !fatPoles.Contains(n.EntityId) && (n.PoleType.Contains("NP 7 4") || n.PoleType.Contains("EXT") || n.PoleType.Contains("TEL")))
                        .OrderBy(n => n.Position.DistanceTo(centroid))
                        .FirstOrDefault();

                    if (fdtNode == null)
                    {
                        fdtNode = graphNodes
                            .Where(n => !fatPoles.Contains(n.EntityId))
                            .OrderBy(n => n.Position.DistanceTo(centroid))
                            .FirstOrDefault();
                    }

                    if (fdtNode == null)
                    {
                        fdtNode = graphNodes
                            .OrderBy(n => n.Position.DistanceTo(centroid))
                            .FirstOrDefault();
                    }

                    if (fdtNode == null)
                    {
                        WriteLog($"WARNING: Gagal menemukan tiang kosong untuk FDT ke-{i + 1}");
                        continue;
                    }

                    string fdtName = $"FDT {i + 1}";
                    if (File.Exists(fdtJsonPath))
                    {
                        var createdIds = AssetDrawer.DrawAsset(db, tr, ms, fdtJsonPath, fdtNode.Position, 0.0, 1.0, "FTTH-FDT");
                        if (createdIds.Count > 0)
                        {
                            placedFdtCount++;
                            Entity mainEnt = tr.GetObject(createdIds[0], OpenMode.ForWrite) as Entity;
                            if (mainEnt != null)
                            {
                                using (RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead))
                                {
                                    if (!rat.Has("FTTH_FDT_DATA"))
                                    {
                                        rat.UpgradeOpen();
                                        using (var ratr = new RegAppTableRecord())
                                        {
                                            ratr.Name = "FTTH_FDT_DATA";
                                            rat.Add(ratr);
                                            tr.AddNewlyCreatedDBObject(ratr, true);
                                        }
                                    }
                                }
                                using (ResultBuffer rb = new ResultBuffer(
                                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, "FTTH_FDT_DATA"),
                                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, fdtName),
                                    new TypedValue((int)DxfCode.ExtendedDataInteger32, capacity)
                                ))
                                {
                                    mainEnt.XData = rb;
                                }
                            }

                            using (MText label = new MText())
                            {
                                label.Contents = fdtName + "\\P(" + capacity + "C)";
                                label.Location = fdtNode.Position + new Vector3d(1.5, 1.5, 0);
                                label.Height = 1.2;
                                label.Layer = "FTTH-FDT-LABEL";
                                label.ColorIndex = 2; // Yellow
                                ms.AppendEntity(label);
                                tr.AddNewlyCreatedDBObject(label, true);
                            }
                        }
                    }
                    else
                    {
                        using (Circle c = new Circle())
                        {
                            c.Center = fdtNode.Position;
                            c.Radius = 1.0;
                            c.Layer = "FTTH-FDT";
                            c.ColorIndex = 1; // Red
                            ms.AppendEntity(c);
                            tr.AddNewlyCreatedDBObject(c, true);
                            placedFdtCount++;
                        }
                    }
                }

                tr.Commit();
                return $"Sukses menaruh {placedFdtCount} FDT secara otomatis di gambar!";
            }
        }

        public static List<CableLine> RouteCablesFromMultipleFdts(Database db, List<HomeCluster> placedFats, double maxTektokDist, string assetPathDir, Action<int, string> progressCallback = null)
        {
            var allCables = new List<CableLine>();

            progressCallback?.Invoke(5, "Membangun graf jaringan tiang...");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var graphNodes = BuildNetworkGraph(db, tr);
                if (graphNodes.Count == 0)
                {
                    throw new InvalidOperationException("Graf jaringan tiang kosong! Pastikan gambar memiliki tiang.");
                }

                progressCallback?.Invoke(15, "Memindai FDT di gambar...");
                var fdts = ScanFdtsInDrawing(db);
                if (fdts.Count == 0)
                {
                    throw new InvalidOperationException("Tidak ada FDT yang terdeteksi di gambar! Jalankan 'AUTO-PLACE BANYAK FDT' atau taruh manual FDT terlebih dahulu.");
                }

                progressCallback?.Invoke(25, "Memindai FAT di gambar...");
                var activeFats = ScanPlacedFats(db, graphNodes);
                if (activeFats.Count == 0)
                {
                    throw new InvalidOperationException("Tidak ada FAT yang terdeteksi di gambar! Jalankan 'CLUSTER & PLACE FAT' terlebih dahulu.");
                }

                EraseExistingCables(db, tr);
                tr.Commit();

                WriteLog($"Routing Multi-FDT: Terdeteksi {fdts.Count} FDT dan {activeFats.Count} FAT.");

                var fdtAssignments = new Dictionary<PlacedFdt, List<HomeCluster>>();
                foreach (var fdt in fdts) { fdtAssignments[fdt] = new List<HomeCluster>(); }

                var pairs = new List<FdtFatPair>();
                foreach (var fdt in fdts)
                {
                    foreach (var fat in activeFats)
                    {
                        pairs.Add(new FdtFatPair
                        {
                            Fdt = fdt,
                            Fat = fat,
                            Distance = fdt.Position.DistanceTo(fat.Centerpoint)
                        });
                    }
                }
                pairs.Sort((x, y) => x.Distance.CompareTo(y.Distance));

                var assignedFats = new HashSet<HomeCluster>();
                foreach (var pair in pairs)
                {
                    if (assignedFats.Contains(pair.Fat)) continue;

                    int limit = pair.Fdt.Capacity == 48 ? 20 : 30;
                    if (fdtAssignments[pair.Fdt].Count < limit)
                    {
                        fdtAssignments[pair.Fdt].Add(pair.Fat);
                        assignedFats.Add(pair.Fat);
                    }
                }

                foreach (var fdt in fdts)
                {
                    WriteLog($"FDT {fdt.Name} (kapasitas={fdt.Capacity}): Ditugaskan untuk melayani {fdtAssignments[fdt].Count} FAT.");
                }

                int unassignedCount = activeFats.Count - assignedFats.Count;
                if (unassignedCount > 0)
                {
                    WriteLog($"WARNING: Terdapat {unassignedCount} FAT yang tidak terlayani karena kapasitas FDT penuh!");
                }

                int fdtIndex = 0;
                foreach (var fdt in fdts)
                {
                    fdtIndex++;
                    var assignedList = fdtAssignments[fdt];
                    if (assignedList.Count == 0) continue;

                    double pctStart = 25.0 + ((fdtIndex - 1) * 60.0 / fdts.Count);
                    double pctEnd = 25.0 + (fdtIndex * 60.0 / fdts.Count);

                    progressCallback?.Invoke((int)pctStart, $"Routing kabel untuk {fdt.Name} ({assignedList.Count} FAT)...");

                    var subCables = RouteCablesFromFdt(
                        db,
                        fdt.Position,
                        assignedList,
                        fdt.Capacity,
                        maxTektokDist,
                        false,
                        assetPathDir,
                        (percent, msg) => { },
                        0,
                        false
                    );

                    using (Transaction tr2 = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr2.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr2.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                        foreach (var line in subCables)
                        {
                            string cleanFdtName = fdt.Name.Replace(" ", "");
                            for (int i = 0; i < line.ConnectedFats.Count; i++)
                            {
                                var fat = line.ConnectedFats[i];
                                fat.SequenceName = $"{cleanFdtName}-{line.LineName.Replace("Line ", "FAT-")}{(i + 1):D2}";
                            }

                            foreach (var fat in line.ConnectedFats)
                            {
                                foreach (ObjectId entId in ms)
                                {
                                    if (entId.IsErased) continue;
                                    DBObject obj = tr2.GetObject(entId, OpenMode.ForRead);
                                    if (obj is MText mtext && mtext.Location.DistanceTo(fat.AssignedPole.Position) < 2.0)
                                    {
                                        if (IsFatLabel(mtext.Text) || IsFatLabel(mtext.Contents))
                                        {
                                            mtext.UpgradeOpen();
                                            mtext.Contents = fat.SequenceName;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        tr2.Commit();
                    }

                    allCables.AddRange(subCables);
                }
            }

            progressCallback?.Invoke(100, "Routing kabel multi-FDT selesai.");
            return allCables;
        }

        private class FdtFatPair
        {
            public PlacedFdt Fdt { get; set; }
            public HomeCluster Fat { get; set; }
            public double Distance { get; set; }
        }
    }
}
