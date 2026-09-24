using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace ZoningEnvelope.UI
{
    /// <summary>Fonts, colors and the small wrapped-label table used by all three panels.</summary>
    internal static class Ui
    {
        public static readonly float BaseSize = SystemFonts.Default().Size;
        public static readonly Font Body = SystemFonts.Default(BaseSize * 1.2f);
        public static readonly Font BodyBold = SystemFonts.Bold(BaseSize * 1.2f);
        public static readonly Font Head = SystemFonts.Bold(BaseSize * 1.35f);
        public static readonly Color Stripe = Color.FromArgb(0, 0, 0, 14);
        public static readonly Color Muted = Color.FromArgb(110, 110, 110);
        public static readonly Color Bad = Color.FromArgb(190, 50, 30);
        public static readonly Color Good = Color.FromArgb(30, 130, 60);

        public static Label L(string text) =>
            new Label { Text = text, Font = Body, Wrap = WrapMode.Word, VerticalAlignment = VerticalAlignment.Center };

        public static Label H(string text) =>
            new Label { Text = text, Font = Head, Wrap = WrapMode.Word };

        public static Button Btn(string text, Action click)
        {
            var b = new Button { Text = text, Font = Body };
            b.Click += (s, e) => click();
            return b;
        }

        public static Control Cell(string text, Font font, Color? bg, int width, Color? fg = null, WrapTable owner = null)
        {
            var l = new Label { Text = text ?? "", Font = font, Wrap = WrapMode.Word, VerticalAlignment = VerticalAlignment.Top };
            if (fg.HasValue) l.TextColor = fg.Value;
            if (width > 0) l.Width = width;
            else { l.Width = 200; owner?.Register(l); }
            var p = new Panel { Content = l, Padding = new Padding(6, 4) };
            if (bg.HasValue) p.BackgroundColor = bg.Value;
            return p;
        }

        /// <summary>
        /// A table whose "0-width" columns share the width left over by the fixed columns.
        /// Their labels get an explicit width on every resize so they wrap instead of
        /// pushing the table wider than the panel.
        /// </summary>
        public class WrapTable : TableLayout
        {
            private readonly List<Label> _expand = new List<Label>();
            private int _fixed;
            private int _expandCount;
            private int _last = -1;

            internal void Register(Label l) => _expand.Add(l);
            internal void SetColumns(int[] widths)
            {
                _fixed = 0; _expandCount = 0;
                foreach (var w in widths) { if (w > 0) _fixed += w + 12; else _expandCount++; }
                _fixed += 12 * _expandCount;
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Fit();
            }

            public void Fit()
            {
                if (_expandCount == 0 || Width <= 0) return;
                int w = Math.Max(90, (Width - _fixed) / _expandCount);
                if (w == _last) return;
                _last = w;
                foreach (var l in _expand) l.Width = w;
            }
        }

        /// <param name="widths">pixel width per column; 0 = this column takes the remaining width.</param>
        public static WrapTable Table(string[] headers, int[] widths, List<string[]> rows, Func<int, int, Color?> color = null)
        {
            var t = new WrapTable { Spacing = new Size(0, 0) };
            t.SetColumns(widths);
            var h = new List<TableCell>();
            for (int i = 0; i < headers.Length; i++) h.Add(new TableCell(Cell(headers[i], BodyBold, null, widths[i], Muted, t), widths[i] == 0));
            t.Rows.Add(new TableRow(h));
            for (int r = 0; r < rows.Count; r++)
            {
                var cells = new List<TableCell>();
                Color? bg = r % 2 == 0 ? Stripe : (Color?)null;
                for (int i = 0; i < headers.Length; i++)
                {
                    string txt = i < rows[r].Length ? rows[r][i] : "";
                    cells.Add(new TableCell(Cell(txt, Body, bg, widths[i], color?.Invoke(r, i), t), widths[i] == 0));
                }
                t.Rows.Add(new TableRow(cells));
            }
            return t;
        }

        public static Scrollable Scroll(Control content) =>
            new Scrollable { Content = content, ExpandContentWidth = true, ExpandContentHeight = false, Border = BorderType.None };

        public static string Ft(double v) => v.ToString("0.#") + " ft";
        public static string Sf(double v) => v.ToString("#,0") + " sf";
    }
}
