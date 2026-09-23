using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using ZoningEnvelope.Model;

namespace ZoningEnvelope.Engine
{
    public class StoryFootprint
    {
        public int Story;
        public double BottomZ;
        public double TopZ;
        public List<Point3d> Polygon;
        public double[] SetbacksFeet;   // per parcel edge
    }

    public class EnvelopeResult
    {
        public List<Brep> Breps { get; } = new List<Brep>();
        public List<StoryFootprint> Stories { get; } = new List<StoryFootprint>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> AppliedRules { get; } = new List<string>();
        public double HeightFeet;
        public double LotWidthFeet;
        public double LotDepthFeet;
        public double LotAreaSqFt;
        public double FootprintSqFt;        // story 1 buildable area
        public double VolumeCuFt;
        public double Grade;
        public BoundingBox BoundingBox = BoundingBox.Empty;

        public StoryFootprint Ground => Stories.Count > 0 ? Stories[0] : null;
    }

    /// <summary>
    /// Turns a parcel + zone rules into a buildable envelope:
    /// per-edge setback clipping per story, extrusion, then subtractive cuts for
    /// encroachment/daylight planes and transitional height bands.
    /// </summary>
    public static class EnvelopeBuilder
    {
        public static EnvelopeResult Build(ParcelGeometry parcel, ParcelSetup setup, ZoneCode code, double ftToModel, double tol)
        {
            var r = new EnvelopeResult();
            if (parcel == null || code == null || code.Kind != "zone") { r.Warnings.Add("No zone rules selected."); return r; }
            setup.Resize(parcel.EdgeCount);
            if (parcel.FrontIndex(setup) < 0) r.Warnings.Add("No front edge set: all edges treated as side yards.");

            double modelToFt = 1.0 / ftToModel;
            r.Grade = parcel.Grade;
            r.LotWidthFeet = parcel.LotWidth(setup) * modelToFt;
            r.LotDepthFeet = parcel.LotDepth(setup) * modelToFt;
            r.LotAreaSqFt = parcel.Area * modelToFt * modelToFt;
            r.HeightFeet = code.Height.Effective(setup.PitchedRoof);
            if (r.HeightFeet <= 0) { r.Warnings.Add("Height rule is missing or zero."); return r; }

            // ---- stories ----
            double H = r.HeightFeet;
            int stories;
            double[] tops;
            if (code.Height.MaxStories > 0)
            {
                stories = code.Height.MaxStories;
                tops = Enumerable.Range(1, stories).Select(k => H * k / stories).ToArray();
            }
            else
            {
                double sh = code.Height.StoryHeightFeet;
                stories = Math.Max(1, (int)Math.Ceiling(H / sh - 1e-9));
                tops = Enumerable.Range(1, stories).Select(k => Math.Min(k * sh, H)).ToArray();
            }
            bool storyDependent = code.IsStoryDependent;
            int slabCount = storyDependent ? stories : 1;

            var basePoly = new List<Point3d>(parcel.Points);
            double bottom = 0;
            for (int k = 1; k <= slabCount; k++)
            {
                double top = storyDependent ? tops[k - 1] : H;
                var fp = new StoryFootprint { Story = k, BottomZ = parcel.Grade + bottom * ftToModel, TopZ = parcel.Grade + top * ftToModel };
                fp.SetbacksFeet = new double[parcel.EdgeCount];
                var poly = basePoly;
                for (int i = 0; i < parcel.EdgeCount; i++)
                {
                    double sb = SetbackFeet(setup.Edges[i], code, r.LotWidthFeet, r.LotDepthFeet, k);
                    fp.SetbacksFeet[i] = sb;
                    var e = parcel.Edges[i];
                    poly = PolygonClip.ClipHalfPlane(poly, e.A, e.Inward, sb * ftToModel);
                    if (poly.Count < 3) break;
                }
                fp.Polygon = poly;
                if (poly.Count < 3)
                {
                    if (k == 1) r.Warnings.Add("Setbacks consume the whole parcel: nothing is buildable.");
                    break;
                }
                r.Stories.Add(fp);
                var brep = Extrude(poly, fp.BottomZ, fp.TopZ, tol);
                if (brep != null) r.Breps.Add(brep);
                bottom = top;
            }
            if (r.Stories.Count == 0) return r;
            r.FootprintSqFt = PolygonClip.Area(r.Stories[0].Polygon) * modelToFt * modelToFt;

            // union the slabs into one solid (keeps the plan diagram/story list intact either way)
            if (r.Breps.Count > 1)
            {
                var u = Brep.CreateBooleanUnion(r.Breps, tol);
                if (u != null && u.Length > 0) { r.Breps.Clear(); r.Breps.AddRange(u); }
                else r.Warnings.Add("Story slabs could not be unioned; showing them separately.");
            }

            r.AppliedRules.Add(DescribeSetbacks(code, r));
            r.AppliedRules.Add("Height " + H + " ft" + (setup.PitchedRoof && code.Height.PitchedRoofMaxFeet > 0 ? " (pitched roof)" : "") +
                               (code.Height.MaxStories > 0 ? ", max " + code.Height.MaxStories + " stories" : ""));

            // ---- cutters ----
            double big = Math.Max(parcel.BoundingBox.Diagonal.Length * 20, H * ftToModel * 20);
            var cutters = new List<Brep>();

            foreach (var plane in code.Planes ?? new List<PlaneRule>())
            {
                bool any = false;
                for (int i = 0; i < parcel.EdgeCount; i++)
                {
                    var es = setup.Edges[i];
                    if (!plane.AppliesTo(es.Role)) continue;
                    double sb = plane.FromLotLine ? 0 : r.Stories[0].SetbacksFeet[i];
                    var c = PlaneCutter(parcel.Edges[i], sb * ftToModel, parcel.Grade + plane.StartHeightFeet * ftToModel, plane.AngleDegrees, big);
                    if (c != null) { cutters.Add(c); any = true; }
                }
                if (any) r.AppliedRules.Add((plane.Label ?? "Plane") + ": " + plane.StartHeightFeet + " ft at " + (plane.FromLotLine ? "lot line" : "setback line") + ", " + plane.AngleDegrees + " deg");
            }

            if (code.TransitionalHeight != null && code.TransitionalHeight.Steps != null && code.TransitionalHeight.Steps.Count > 0)
            {
                var steps = code.TransitionalHeight.Steps.OrderBy(s => s.UpToFeet).ToList();
                bool any = false;
                for (int i = 0; i < parcel.EdgeCount; i++)
                {
                    if (!setup.Edges[i].AdjacentLowDensity) continue;
                    double prev = 0;
                    foreach (var s in steps)
                    {
                        if (s.MaxHeightFeet < H)
                            cutters.Add(BandCutter(parcel.Edges[i], prev * ftToModel, s.UpToFeet * ftToModel, parcel.Grade + s.MaxHeightFeet * ftToModel, big));
                        prev = s.UpToFeet;
                        any = true;
                    }
                }
                if (any) r.AppliedRules.Add((code.TransitionalHeight.Label ?? "Transitional height") + ": " +
                    string.Join("; ", steps.Select(s => "within " + s.UpToFeet + " ft: " + s.MaxHeightFeet + " ft")));
                else r.AppliedRules.Add((code.TransitionalHeight.Label ?? "Transitional height") + ": no edge flagged 'adjacent low density', not applied");
            }

            foreach (var cutter in cutters)
            {
                var next = new List<Brep>();
                foreach (var b in r.Breps)
                {
                    var diff = Brep.CreateBooleanDifference(b, cutter, tol);
                    if (diff == null) { next.Add(b); r.Warnings.Add("A plane cut failed on part of the envelope; that part is shown uncut."); }
                    else next.AddRange(diff);
                }
                r.Breps.Clear();
                r.Breps.AddRange(next);
            }

            foreach (var b in r.Breps)
            {
                r.BoundingBox.Union(b.GetBoundingBox(false));
                var vm = VolumeMassProperties.Compute(b);
                if (vm != null) r.VolumeCuFt += vm.Volume * modelToFt * modelToFt * modelToFt;
            }
            return r;
        }

        public static double SetbackFeet(EdgeSetup edge, ZoneCode code, double lotWidthFt, double lotDepthFt, int story)
        {
            double sb = code.Setbacks.For(edge.Role).Resolve(lotWidthFt, lotDepthFt, story);
            var adj = code.AdjacentLowDensitySetbacks;
            if (edge.AdjacentLowDensity && adj != null)
                sb = Math.Max(sb, edge.Role == EdgeRole.Rear ? adj.RearFeet : adj.SideFeet);
            foreach (var st in code.Stepbacks ?? new List<StepbackRule>())
                if (st.AppliesTo(edge.Role) && story > st.AboveStory) sb += st.AdditionalFeet;
            return sb;
        }

        private static string DescribeSetbacks(ZoneCode code, EnvelopeResult r)
        {
            var g = r.Stories[0];
            return "Setbacks (story 1): " + string.Join(", ", g.SetbacksFeet.Select((s, i) => "e" + (i + 1) + "=" + s.ToString("0.#") + " ft"));
        }

        private static Brep Extrude(List<Point3d> poly, double z0, double z1, double tol)
        {
            var pts = poly.Select(p => new Point3d(p.X, p.Y, z0)).ToList();
            pts.Add(pts[0]);
            var curve = new PolylineCurve(pts);
            var ext = Extrusion.Create(curve, z1 - z0, true);
            if (ext == null) return null;
            var brep = ext.ToBrep(true);
            if (brep == null) return null;
            var bb = brep.GetBoundingBox(false);
            if (bb.Max.Z < z0 + (z1 - z0) * 0.5)
            {
                ext = Extrusion.Create(curve, -(z1 - z0), true);
                brep = ext?.ToBrep(true);
            }
            if (brep != null && brep.SolidOrientation == BrepSolidOrientation.Inward) brep.Flip();
            return brep;
        }

        /// <summary>Half-space above a plane that passes through the (offset) edge line at startZ and rises inward at angleDeg.</summary>
        private static Brep PlaneCutter(ParcelEdge e, double offset, double startZ, double angleDeg, double big)
        {
            double a = angleDeg * Math.PI / 180.0;
            var origin = new Point3d(e.A.X + e.Inward.X * offset, e.A.Y + e.Inward.Y * offset, startZ);
            var rise = e.Inward * Math.Cos(a) + Vector3d.ZAxis * Math.Sin(a);
            var normal = Vector3d.CrossProduct(e.Direction, rise);
            if (normal.Z < 0) normal = -normal;
            if (!normal.Unitize()) return null;
            var plane = new Plane(origin, normal);
            var box = new Box(plane, new Interval(-big, big), new Interval(-big, big), new Interval(0, big));
            return box.ToBrep();
        }

        /// <summary>Everything above maxZ in the band [d0, d1] measured inward from the edge line.</summary>
        private static Brep BandCutter(ParcelEdge e, double d0, double d1, double maxZ, double big)
        {
            var plane = new Plane(e.A, e.Direction, e.Inward);
            var box = new Box(plane, new Interval(-big, big), new Interval(d0, d1), new Interval(maxZ - e.A.Z, big));
            return box.ToBrep();
        }
    }
}
