using System;
using System.Collections.Generic;

namespace FTTHBasemap.Export
{
    public class ExportModel
    {
        public FdtNode Fdt { get; set; } = new FdtNode();
        public List<FdtNode> Fdts { get; set; } = new List<FdtNode>();
        public List<LineData> Lines { get; set; } = new List<LineData>();
    }

    public class FdtNode
    {
        public string Uuid { get; set; } = Guid.NewGuid().ToString();
        public string EntityHandle { get; set; }
        public string Name { get; set; }
        public string CapacityText { get; set; }
        public string Description { get; set; }
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        
        // Metadata
        public double Rotation { get; set; }
        public double TextHeight { get; set; }
        public string Layer { get; set; }
    }

    public class LineData
    {
        public string LineName { get; set; } // e.g., "LINE A"
        public string FdtHandle { get; set; } // Handle of the FDT this line belongs to
        public List<FatData> Fats { get; set; } = new List<FatData>();
        public List<EntityItem> Poles { get; set; } = new List<EntityItem>(); // Store category name and coords
        public List<EntityItem> DistributionCables { get; set; } = new List<EntityItem>();
        public List<EntityItem> SlackHangers { get; set; } = new List<EntityItem>();
        public List<EntityItem> SlingWires { get; set; } = new List<EntityItem>();
        public List<EntityItem> Closures { get; set; } = new List<EntityItem>();
        public List<EntityItem> CustomMarkers { get; set; } = new List<EntityItem>();
    }

    public class FatData
    {
        public string Uuid { get; set; } = Guid.NewGuid().ToString();
        public string BoundaryUuid { get; set; } = Guid.NewGuid().ToString();
        public string EntityHandle { get; set; } // Handle for the FAT Text
        public string BoundaryHandle { get; set; } // Handle for the Boundary Polyline
        public string SequenceName { get; set; } // "A01", "A02"
        public string RawText { get; set; } // E.g., "TJG3.023.A01"
        
        // FAT Point
        public double FatLon { get; set; }
        public double FatLat { get; set; }

        // Metadata
        public double Rotation { get; set; }
        public double TextHeight { get; set; }
        public string Layer { get; set; }

        // Boundary FAT (Polygon)
        public List<(double Lon, double Lat)> BoundaryPoints { get; set; } = new List<(double Lon, double Lat)>();
        public string BoundaryLayer { get; set; }

        // HP Covers (Points)
        public List<EntityItem> HpCovers { get; set; } = new List<EntityItem>();
    }

    public class EntityItem
    {
        public string Uuid { get; set; } = Guid.NewGuid().ToString();
        public string EntityHandle { get; set; }
        public string Category { get; set; } // "NEW POLE 7-4", "48" for cables, etc.
        public string Name { get; set; }
        
        // For Points
        public double Lon { get; set; }
        public double Lat { get; set; }
        
        // For Lines
        public List<(double Lon, double Lat)> LinePoints { get; set; } = new List<(double Lon, double Lat)>();

        // Metadata
        public double Rotation { get; set; }
        public double TextHeight { get; set; }
        public string Layer { get; set; }
        public string LineAffiliation { get; set; } // e.g. "LINE A"
        public string SequenceName { get; set; } // e.g. "A01"
        public string Description { get; set; }
    }
}
