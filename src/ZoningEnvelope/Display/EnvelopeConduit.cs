using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;
using ZoningEnvelope.Engine;
using ZoningEnvelope.Model;

namespace ZoningEnvelope.Display
{
    /// <summary>Draws the envelope, setback lines, edge labels and any massing volume outside the envelope.</summary>
    public class EnvelopeConduit : DisplayConduit
    {
        private readonly List<Brep> _envelope = new List<Brep>();
        private readonly List<Brep> _excess = new List<Brep>();
        private readonly List<Polyline> _footprints = new List<Polyline>();
        private readonly List<KeyValuePair<Point3d, string>> _labels = new List<KeyValuePair<Point3d, string>>();
        private BoundingBox _bbox = BoundingBox.Empty;

        private static readonly Color EnvelopeColor = Color.FromArgb(40, 140, 255);
        private static readonly Color ExcessColor = Color.FromArgb(230, 40, 40);
        private readonly DisplayMaterial _envMat = new DisplayMaterial(EnvelopeColor, 0.65);
        private readonly DisplayMaterial _excessMat = new DisplayMaterial(ExcessColor, 0.25);

        public void Clear()
        {
            _envelope.Clear(); _excess.Clear(); _footprints.Clear(); _labels.Clear();
            _bbox = BoundingBox.Empty;
        }

        public void Update(ParcelGeometry parcel, ParcelSetup setup, EnvelopeResult env, ComplianceResult comp, double ftToModel)
        {
            Clear();
            if (env != null)
            {
                _envelope.AddRange(env.Breps);
                foreach (var s in env.Stories)
                {
                    var pl = new Polyline(s.Polygon); pl.Add(s.Polygon[0]);
                    // draw each story footprint at its floor level
                    for (int i = 0; i < pl.Count; i++) pl[i] = new Point3d(pl[i].X, pl[i].Y, s.BottomZ);
                    _footprints.Add(pl);
                }
                _bbox.Union(env.BoundingBox);
            }
            if (comp != null) _excess.AddRange(comp.ExcessBreps);
            if (parcel != null)
            {
                _bbox.Union(parcel.BoundingBox);
                var ground = env?.Ground;
                for (int i = 0; i < parcel.EdgeCount; i++)
                {
                    var e = parcel.Edges[i];
                    var es = i < setup.Edges.Count ? setup.Edges[i] : new EdgeSetup();
                    string role = es.Role == EdgeRole.Front ? "F" : es.Role == EdgeRole.Rear ? "R" : "S";
                    string txt = (i + 1) + " " + role;
                    if (ground != null) txt += " " + ground.SetbacksFeet[i].ToString("0.#") + "'";
                    if (es.Street) txt += " st";
                    if (es.AdjacentLowDensity) txt += " adj";
                    double off = System.Math.Min(e.Length * 0.1, 6 * ftToModel);
                    _labels.Add(new KeyValuePair<Point3d, string>(e.Mid + e.Inward * off, txt));
                }
            }
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            base.CalculateBoundingBox(e);
            if (_bbox.IsValid) e.IncludeBoundingBox(_bbox);
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            base.PostDrawObjects(e);
            foreach (var b in _envelope)
            {
                e.Display.DrawBrepShaded(b, _envMat);
                e.Display.DrawBrepWires(b, EnvelopeColor, -1);
            }
            foreach (var pl in _footprints)
                e.Display.DrawPatternedPolyline(pl, EnvelopeColor, 0x0F0F, 2, false);
            foreach (var b in _excess)
            {
                e.Display.DrawBrepShaded(b, _excessMat);
                e.Display.DrawBrepWires(b, ExcessColor, 1);
            }
            foreach (var l in _labels)
                e.Display.DrawDot(l.Key, l.Value, Color.FromArgb(30, 30, 30), Color.White);
        }
    }
}
