using System.Collections.Generic;
using Rhino.Geometry;

namespace ZoningEnvelope.Engine
{
    /// <summary>Sutherland-Hodgman half-plane clipping in plan (Z is carried through unchanged).</summary>
    public static class PolygonClip
    {
        /// <summary>
        /// Keep the part of the polygon at least <paramref name="offset"/> inside the line through
        /// <paramref name="a"/> with inward normal <paramref name="inward"/>.
        /// </summary>
        public static List<Point3d> ClipHalfPlane(List<Point3d> poly, Point3d a, Vector3d inward, double offset)
        {
            var result = new List<Point3d>();
            if (poly == null || poly.Count < 3) return result;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % n];
                double fp = Dist(p, a, inward) - offset;
                double fq = Dist(q, a, inward) - offset;
                bool pin = fp >= 0, qin = fq >= 0;
                if (pin && qin) result.Add(q);
                else if (pin && !qin) result.Add(Lerp(p, q, fp / (fp - fq)));
                else if (!pin && qin) { result.Add(Lerp(p, q, fp / (fp - fq))); result.Add(q); }
            }
            // remove near-duplicate consecutive points
            for (int i = result.Count - 1; i >= 0 && result.Count > 1; i--)
            {
                int j = (i + 1) % result.Count;
                if (result[i].DistanceTo(result[j]) < 1e-7) result.RemoveAt(i);
            }
            return result.Count >= 3 ? result : new List<Point3d>();
        }

        public static double Area(List<Point3d> poly)
        {
            if (poly == null || poly.Count < 3) return 0;
            double s = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                s += p.X * q.Y - q.X * p.Y;
            }
            return System.Math.Abs(s) * 0.5;
        }

        private static double Dist(Point3d p, Point3d a, Vector3d inward)
        {
            return (p.X - a.X) * inward.X + (p.Y - a.Y) * inward.Y;
        }

        private static Point3d Lerp(Point3d p, Point3d q, double t)
        {
            return new Point3d(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t);
        }
    }
}
