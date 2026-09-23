using System;
using System.Collections.Generic;
using Rhino.Geometry;
using ZoningEnvelope.Model;

namespace ZoningEnvelope.Engine
{
    public class ParcelEdge
    {
        public int Index;
        public Point3d A;
        public Point3d B;
        public Vector3d Direction;      // unit, A -> B
        public Vector3d Inward;         // unit, points into the parcel (parcel is CCW)
        public double Length;
        public Point3d Mid => (A + B) * 0.5;

        /// <summary>Signed distance from the edge line, positive inside the parcel.</summary>
        public double InwardDistance(Point3d p)
        {
            var d = p - A; d.Z = 0;
            return d * Inward;
        }
    }

    /// <summary>A closed, planar, straight-sided parcel boundary in model units, oriented counter-clockwise in plan.</summary>
    public class ParcelGeometry
    {
        public List<Point3d> Points { get; } = new List<Point3d>();
        public List<ParcelEdge> Edges { get; } = new List<ParcelEdge>();
        public double Grade { get; private set; }
        public double Area { get; private set; }
        public Point3d Centroid { get; private set; }
        public BoundingBox BoundingBox { get; private set; }

        public static ParcelGeometry TryCreate(Curve curve, out string error)
        {
            error = null;
            if (curve == null) { error = "No curve."; return null; }
            if (!curve.IsClosed) { error = "Parcel curve must be closed."; return null; }
            Polyline pl;
            if (!curve.TryGetPolyline(out pl))
            {
                var pc = curve.ToPolyline(0, 0, 0.05, 0, 0, 0.01, 0, 0, true);
                if (pc == null || !pc.TryGetPolyline(out pl)) { error = "Parcel curve must be a polyline (straight segments)."; return null; }
            }
            var pts = new List<Point3d>(pl);
            if (pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) < 1e-6) pts.RemoveAt(pts.Count - 1);
            // drop zero-length segments
            for (int i = pts.Count - 1; i > 0; i--)
                if (pts[i].DistanceTo(pts[i - 1]) < 1e-6) pts.RemoveAt(i);
            if (pts.Count < 3) { error = "Parcel needs at least 3 corners."; return null; }

            double z = 0; foreach (var p in pts) z += p.Z; z /= pts.Count;
            for (int i = 0; i < pts.Count; i++) pts[i] = new Point3d(pts[i].X, pts[i].Y, z);

            double signed = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % pts.Count];
                signed += p.X * q.Y - q.X * p.Y;
            }
            if (Math.Abs(signed) < 1e-9) { error = "Parcel has no area."; return null; }
            if (signed < 0) pts.Reverse();

            var g = new ParcelGeometry();
            g.Points.AddRange(pts);
            g.Grade = z;
            g.Area = Math.Abs(signed) * 0.5;
            g.BoundingBox = new BoundingBox(pts);
            double cx = 0, cy = 0;
            foreach (var p in pts) { cx += p.X; cy += p.Y; }
            g.Centroid = new Point3d(cx / pts.Count, cy / pts.Count, z);
            for (int i = 0; i < pts.Count; i++)
            {
                var e = new ParcelEdge { Index = i, A = pts[i], B = pts[(i + 1) % pts.Count] };
                e.Direction = e.B - e.A; e.Length = e.Direction.Length; e.Direction.Unitize();
                e.Inward = new Vector3d(-e.Direction.Y, e.Direction.X, 0);
                g.Edges.Add(e);
            }
            return g;
        }

        public int EdgeCount => Edges.Count;

        /// <summary>Index of the edge closest to a point (in plan).</summary>
        public int ClosestEdge(Point3d p)
        {
            int best = 0; double bestD = double.MaxValue;
            foreach (var e in Edges)
            {
                var line = new Line(e.A, e.B);
                double d = line.ClosestPoint(p, true).DistanceTo(new Point3d(p.X, p.Y, e.A.Z));
                if (d < bestD) { bestD = d; best = e.Index; }
            }
            return best;
        }

        /// <summary>Assign Front to the given edge, Rear to the most opposite far edge, Side to the rest.</summary>
        public void AssignRoles(ParcelSetup setup, int frontIndex)
        {
            setup.Resize(EdgeCount);
            var front = Edges[frontIndex];
            int rear = -1; double bestScore = double.MinValue;
            foreach (var e in Edges)
            {
                if (e.Index == frontIndex) continue;
                double dot = e.Direction * front.Direction;           // -1 = exactly opposite
                double dist = front.InwardDistance(e.Mid);
                double score = dist * Math.Max(0.05, -dot);
                if (score > bestScore) { bestScore = score; rear = e.Index; }
            }
            for (int i = 0; i < EdgeCount; i++)
                setup.Edges[i].Role = i == frontIndex ? EdgeRole.Front : (i == rear ? EdgeRole.Rear : EdgeRole.Side);
        }

        public int FrontIndex(ParcelSetup setup)
        {
            for (int i = 0; i < Math.Min(EdgeCount, setup.Edges.Count); i++)
                if (setup.Edges[i].Role == EdgeRole.Front) return i;
            return -1;
        }

        /// <summary>Lot width = length of the front edge (model units).</summary>
        public double LotWidth(ParcelSetup setup)
        {
            int f = FrontIndex(setup);
            if (f < 0) return Math.Min(BoundingBox.Max.X - BoundingBox.Min.X, BoundingBox.Max.Y - BoundingBox.Min.Y);
            return Edges[f].Length;
        }

        /// <summary>Lot depth = farthest corner from the front edge line (model units).</summary>
        public double LotDepth(ParcelSetup setup)
        {
            int f = FrontIndex(setup);
            if (f < 0) return Math.Max(BoundingBox.Max.X - BoundingBox.Min.X, BoundingBox.Max.Y - BoundingBox.Min.Y);
            double d = 0;
            foreach (var p in Points) d = Math.Max(d, Edges[f].InwardDistance(p));
            return d;
        }

        public Polyline ClosedPolyline()
        {
            var pl = new Polyline(Points);
            pl.Add(Points[0]);
            return pl;
        }
    }
}
