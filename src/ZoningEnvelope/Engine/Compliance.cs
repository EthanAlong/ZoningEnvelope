using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.DocObjects;
using Rhino.Geometry;
using ZoningEnvelope.Model;

namespace ZoningEnvelope.Engine
{
    public class EdgeOpenings
    {
        public int EdgeIndex;
        public string Label;
        public double FsdFeet;              // fire separation distance of the closest facade facing this edge
        public double FacadeAreaSqFt;
        public string Allowed;              // for the selected sprinkler state (unprotected openings)
        public string AllowedProtected;
    }

    public class ComplianceResult
    {
        public bool HasMassing;
        public double HeightFeet;
        public double MaxHeightFeet;
        public double FootprintSqFt;
        public double GrossFloorAreaSqFt;
        public double AllowedFloorAreaSqFt;      // 0 = no rule
        public string FloorAreaLabel = "FAR";
        public double AllowedCoverageSqFt;       // 0 = no rule
        public double MaxUnits;                  // 0 = no rule
        public double MassingVolumeCuFt;
        public double? ExcessVolumeCuFt;         // null = boolean failed
        public List<Brep> ExcessBreps { get; } = new List<Brep>();
        public List<EdgeOpenings> Openings { get; } = new List<EdgeOpenings>();
        public List<List<Point3d>> FootprintOutlines { get; } = new List<List<Point3d>>();
        public List<string> Warnings { get; } = new List<string>();

        public bool HeightOk => !HasMassing || HeightFeet <= MaxHeightFeet + 0.01;
        public bool FloorAreaOk => !HasMassing || AllowedFloorAreaSqFt <= 0 || GrossFloorAreaSqFt <= AllowedFloorAreaSqFt + 0.5;
        public bool CoverageOk => !HasMassing || AllowedCoverageSqFt <= 0 || FootprintSqFt <= AllowedCoverageSqFt + 0.5;
        public bool EnvelopeOk => !HasMassing || (ExcessVolumeCuFt.HasValue && ExcessVolumeCuFt.Value < 1.0);
    }

    public static class Compliance
    {
        public static Brep ToBrep(RhinoObject obj)
        {
            if (obj == null) return null;
            var g = obj.Geometry;
            if (g is Brep b) return b.DuplicateBrep();
            if (g is Extrusion e) return e.ToBrep(true);
            if (g is Mesh m) return Brep.CreateFromMesh(m, true);
            if (g is SubD sd) return sd.ToBrep();
            return null;
        }

        public static ComplianceResult Evaluate(ParcelGeometry parcel, ParcelSetup setup, ZoneCode code, ZoneCode openings,
                                                EnvelopeResult env, Brep massing, double ftToModel, double tol)
        {
            var c = new ComplianceResult();
            double m2f = 1.0 / ftToModel;
            c.MaxHeightFeet = env?.HeightFeet ?? 0;
            if (code != null)
            {
                if (code.FloorArea != null && code.FloorArea.Ratio > 0)
                {
                    c.AllowedFloorAreaSqFt = code.FloorArea.Ratio * env.LotAreaSqFt;
                    c.FloorAreaLabel = code.FloorArea.Label ?? "FAR";
                }
                if (code.Coverage != null && code.Coverage.MaxPercent > 0)
                    c.AllowedCoverageSqFt = code.Coverage.MaxPercent / 100.0 * env.LotAreaSqFt;
                if (code.Density != null && code.Density.LotAreaPerUnitSqFt > 0)
                    c.MaxUnits = Math.Floor(env.LotAreaSqFt / code.Density.LotAreaPerUnitSqFt);
            }
            if (massing == null) return c;
            c.HasMassing = true;

            var bb = massing.GetBoundingBox(true);
            c.HeightFeet = (bb.Max.Z - parcel.Grade) * m2f;

            var vm = VolumeMassProperties.Compute(massing);
            if (vm != null) c.MassingVolumeCuFt = vm.Volume * m2f * m2f * m2f;

            // floor plates: horizontal sections every story height, 0.5 ft above each floor
            double sh = (code?.Height?.StoryHeightFeet ?? 10) * ftToModel;
            double z = parcel.Grade + 0.5 * ftToModel;
            bool first = true;
            while (z < bb.Max.Z)
            {
                double a = SectionArea(massing, z, first ? c.FootprintOutlines : null);
                if (first) { c.FootprintSqFt = a * m2f * m2f; first = false; }
                c.GrossFloorAreaSqFt += a * m2f * m2f;
                z += sh;
            }

            // volume outside the envelope
            if (env != null && env.Breps.Count > 0)
            {
                try
                {
                    var diff = Brep.CreateBooleanDifference(new[] { massing }, env.Breps, tol);
                    if (diff == null) { c.ExcessVolumeCuFt = null; c.Warnings.Add("Could not compute the volume outside the envelope (boolean failed)."); }
                    else
                    {
                        double v = 0;
                        foreach (var d in diff)
                        {
                            var p = VolumeMassProperties.Compute(d);
                            if (p != null && p.Volume > tol) { v += p.Volume; c.ExcessBreps.Add(d); }
                        }
                        c.ExcessVolumeCuFt = v * m2f * m2f * m2f;
                    }
                }
                catch (Exception ex) { c.ExcessVolumeCuFt = null; c.Warnings.Add("Envelope check failed: " + ex.Message); }
            }

            // openings per parcel edge (CBC 705.8 style table)
            if (openings != null && openings.Rows != null && openings.Rows.Count > 0)
                EvaluateOpenings(parcel, setup, openings, massing, ftToModel, c);

            return c;
        }

        private static double SectionArea(Brep brep, double z, List<List<Point3d>> outlines)
        {
            var plane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
            Curve[] curves;
            try { curves = Brep.CreateContourCurves(brep, plane); } catch { return 0; }
            if (curves == null) return 0;
            double a = 0;
            foreach (var cv in curves)
            {
                if (!cv.IsClosed) continue;
                var amp = AreaMassProperties.Compute(cv);
                if (amp != null) a += amp.Area;
                if (outlines != null)
                {
                    Polyline pl;
                    if (cv.TryGetPolyline(out pl) || (cv.ToPolyline(0, 0, 0.05, 0, 0, 0.01, 0, 0, true)?.TryGetPolyline(out pl) ?? false))
                        outlines.Add(new List<Point3d>(pl));
                }
            }
            return a;
        }

        private static void EvaluateOpenings(ParcelGeometry parcel, ParcelSetup setup, ZoneCode table, Brep massing, double ftToModel, ComplianceResult c)
        {
            double m2f = 1.0 / ftToModel;
            var best = new Dictionary<int, EdgeOpenings>();
            foreach (var face in massing.Faces)
            {
                var dom0 = face.Domain(0); var dom1 = face.Domain(1);
                var n = face.NormalAt(dom0.Mid, dom1.Mid);
                if (face.OrientationIsReversed) n = -n;
                if (Math.Abs(n.Z) > 0.3) continue;       // roofs / floors
                n.Z = 0; if (!n.Unitize()) continue;

                Brep fb = null;
                try { fb = face.DuplicateFace(false); } catch { }
                if (fb == null) continue;
                var amp = AreaMassProperties.Compute(fb);
                if (amp == null) continue;
                var centroid = amp.Centroid;
                // make sure n points out of the solid, whatever the face orientation flags say
                if (massing.IsSolid)
                {
                    double eps = 0.1 * ftToModel;
                    if (massing.IsPointInside(centroid + n * eps, eps * 0.1, false)) n = -n;
                }

                // the parcel edge this facade faces: outward normals agree, and the edge is in front of the face
                ParcelEdge target = null; double bestDot = 0.5;
                foreach (var e in parcel.Edges)
                {
                    double dot = n * (-e.Inward);
                    if (dot > bestDot) { bestDot = dot; target = e; }
                }
                if (target == null) continue;
                double dist = target.InwardDistance(centroid);          // facade to lot line, perpendicular
                if (dist < -0.01 * ftToModel) continue;                  // face beyond the lot line
                var es = setup.Edges[target.Index];
                double fsdFt = dist * m2f + (es.Street && es.StreetWidthFeet > 0 ? es.StreetWidthFeet * 0.5 : 0);

                EdgeOpenings eo;
                if (!best.TryGetValue(target.Index, out eo))
                {
                    eo = new EdgeOpenings { EdgeIndex = target.Index, FsdFeet = double.MaxValue };
                    best[target.Index] = eo;
                }
                eo.FacadeAreaSqFt += amp.Area * m2f * m2f;
                if (fsdFt < eo.FsdFeet) eo.FsdFeet = fsdFt;
            }

            foreach (var eo in best.Values.OrderBy(o => o.EdgeIndex))
            {
                var es = setup.Edges[eo.EdgeIndex];
                eo.Label = "Edge " + (eo.EdgeIndex + 1) + " (" + es.Role.ToString().ToLower() + (es.Street ? ", street" : "") + ")";
                var row = table.Rows.FirstOrDefault(r => r.Contains(eo.FsdFeet));
                if (row == null) { eo.Allowed = "?"; eo.AllowedProtected = "?"; }
                else
                {
                    eo.Allowed = OpeningsRow.Format(setup.Sprinklered ? row.UnprotectedSprinklered : row.UnprotectedNonsprinklered);
                    eo.AllowedProtected = OpeningsRow.Format(row.Protected);
                }
                c.Openings.Add(eo);
            }
        }
    }
}
