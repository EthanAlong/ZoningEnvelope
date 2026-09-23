using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino.Geometry;
using ZoningEnvelope.Model;
using ZoningEnvelope.Session;

namespace ZoningEnvelope.UI
{
    /// <summary>2D plan of the parcel with setback lines, buildable footprints per story and edge labels.</summary>
    public class PlanDiagram : Drawable
    {
        private static readonly Color Ink = Color.FromArgb(40, 40, 40);
        private static readonly Color Muted = Color.FromArgb(140, 140, 140);
        private static readonly Color Env = Color.FromArgb(40, 140, 255);
        private static readonly Color Massing = Color.FromArgb(230, 120, 30);

        private readonly Font _font = Fonts.Sans(8);
        private readonly Font _small = Fonts.Sans(7);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var rect = new RectangleF(0, 0, Width, Height);
            g.FillRectangle(Colors.White, rect);

            var s = ZoningSession.Current;
            var parcel = s.Parcel;
            if (parcel == null)
            {
                g.DrawText(_font, Muted, 8, 8, "Plan: set a parcel to see setbacks here.");
                return;
            }

            // fit
            var bb = parcel.BoundingBox;
            double w = Math.Max(bb.Max.X - bb.Min.X, 1e-6), h = Math.Max(bb.Max.Y - bb.Min.Y, 1e-6);
            float margin = 26;
            double scale = Math.Min((Width - 2 * margin) / w, (Height - 2 * margin) / h);
            double cx = (bb.Min.X + bb.Max.X) / 2, cy = (bb.Min.Y + bb.Max.Y) / 2;
            Func<Point3d, PointF> P = p => new PointF((float)(Width / 2.0 + (p.X - cx) * scale), (float)(Height / 2.0 - (p.Y - cy) * scale));

            // story footprints, ground first (darkest)
            var env = s.Envelope;
            if (env != null)
            {
                int n = env.Stories.Count;
                for (int i = 0; i < n; i++)
                {
                    var poly = env.Stories[i].Polygon.Select(P).ToArray();
                    int alpha = Math.Max(25, 90 - i * 25);
                    g.FillPolygon(Color.FromArgb(Env.Rb, Env.Gb, Env.Bb, alpha), poly);
                    using (var pen = new Pen(Env, i == 0 ? 1.5f : 1f) { DashStyle = i == 0 ? DashStyles.Solid : DashStyles.Dash })
                        g.DrawPolygon(pen, poly);
                }
            }

            // massing footprint
            var comp = s.Compliance;
            if (comp != null)
                foreach (var fp in comp.FootprintOutlines)
                    using (var pen = new Pen(Massing, 1.5f))
                        g.DrawPolygon(pen, fp.Select(P).ToArray());

            // parcel
            var outline = parcel.Points.Select(P).ToArray();
            using (var pen = new Pen(Ink, 2f)) g.DrawPolygon(pen, outline);

            // edge labels
            double m2f = 1.0 / s.FeetToModel;
            for (int i = 0; i < parcel.EdgeCount; i++)
            {
                var edge = parcel.Edges[i];
                var es = i < s.Setup.Edges.Count ? s.Setup.Edges[i] : new EdgeSetup();
                string role = es.Role == EdgeRole.Front ? "Front" : es.Role == EdgeRole.Rear ? "Rear" : "Side";
                string txt = (i + 1) + " " + role + " " + (edge.Length * m2f).ToString("0") + "'";
                if (env?.Ground != null) txt += "  sb " + env.Ground.SetbacksFeet[i].ToString("0.#") + "'";
                if (es.Street) txt += " (street" + (es.StreetWidthFeet > 0 ? " " + es.StreetWidthFeet + "')" : ")");
                if (es.AdjacentLowDensity) txt += " [adj low-density]";
                var size = g.MeasureString(_small, txt);
                var mid = edge.Mid + edge.Inward * (-8 / scale);   // just outside the line
                var pt = P(mid);
                float x = pt.X - size.Width / 2, y = pt.Y - size.Height / 2;
                x = Math.Max(2, Math.Min(Width - size.Width - 2, x));
                y = Math.Max(2, Math.Min(Height - size.Height - 2, y));
                g.FillRectangle(Color.FromArgb(255, 255, 255, 200), new RectangleF(x - 1, y, size.Width + 2, size.Height));
                g.DrawText(_small, es.Role == EdgeRole.Front ? Ink : Muted, x, y, txt);
            }

            string title = (s.Code?.DisplayName ?? "") + "   lot " + (env?.LotAreaSqFt ?? 0).ToString("#,0") + " sf";
            g.DrawText(_font, Ink, 6, Height - 16, title);
        }
    }
}
