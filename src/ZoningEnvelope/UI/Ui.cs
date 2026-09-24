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
            // WPF sizes the button with the default font before our larger one is applied,
            // so measure the text ourselves and set an explicit size.
            var m = Body.MeasureString(text);
            var b = new Button { Text = text, Font = Body, Size = new Size((int)Math.Ceiling(m.Width) + 26, (int)Math.Ceiling(m.Height) + 12) };
            b.Click += (s, e) => click();
            return b;
        }

        /// <summary>
        /// A table drawn in one Drawable: header row, striped rows, word-wrapped cells.
        /// Columns with width 0 share whatever width the fixed columns leave.
        /// Drawing is cheap, so resizing the panel does not trigger a WPF layout storm
        /// (which is what a grid of wrapping Labels did).
        /// </summary>
        public class TextTable : Drawable
        {
            private const int PadX = 6, PadY = 4;
            private readonly string[] _headers;
            private readonly int[] _widths;
            private readonly List<string[]> _rows;
            private readonly Func<int, int, Color?> _color;
            private int _lastHeight = -1;

            public TextTable(string[] headers, int[] widths, List<string[]> rows, Func<int, int, Color?> color)
            {
                _headers = headers; _widths = widths; _rows = rows ?? new List<string[]>(); _color = color;
                Size = new Size(-1, 30);
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int W = Width;
                if (W <= 0) return;

                int fixedSum = 0, nExpand = 0;
                foreach (var w in _widths) { if (w > 0) fixedSum += w + 2 * PadX; else nExpand++; }
                int expandW = nExpand > 0 ? Math.Max(60, (W - fixedSum) / nExpand - 2 * PadX) : 0;
                int ColW(int i) => _widths[i] > 0 ? _widths[i] : expandW;

                float y = 0;
                var texts = new FormattedText[_headers.Length];
                for (int r = -1; r < _rows.Count; r++)
                {
                    float rowH = 0;
                    for (int i = 0; i < _headers.Length; i++)
                    {
                        string txt = r < 0 ? _headers[i] : (i < _rows[r].Length ? _rows[r][i] : "");
                        Color fg = r < 0 ? Muted : (_color?.Invoke(r, i) ?? SystemColors.ControlText);
                        var ft = new FormattedText
                        {
                            Text = txt ?? "",
                            Font = r < 0 ? BodyBold : Body,
                            Wrap = FormattedTextWrapMode.Word,
                            MaximumSize = new SizeF(ColW(i), 100000),
                            ForegroundBrush = new SolidBrush(fg),
                        };
                        texts[i] = ft;
                        rowH = Math.Max(rowH, ft.Measure().Height);
                    }
                    rowH += 2 * PadY;
                    if (r >= 0 && r % 2 == 0) g.FillRectangle(Stripe, new RectangleF(0, y, W, rowH));
                    float x = 0;
                    for (int i = 0; i < _headers.Length; i++)
                    {
                        g.DrawText(texts[i], new PointF(x + PadX, y + PadY));
                        x += ColW(i) + 2 * PadX;
                    }
                    y += rowH;
                    if (r < 0) g.DrawLine(Color.FromArgb(0, 0, 0, 40), 0, y, W, y);
                }

                int total = (int)Math.Ceiling(y) + 2;
                if (Math.Abs(total - _lastHeight) > 1)
                {
                    _lastHeight = total;
                    Size = new Size(-1, total);
                }
            }
        }

        /// <param name="widths">pixel width per column; 0 = this column takes the remaining width.</param>
        public static Control Table(string[] headers, int[] widths, List<string[]> rows, Func<int, int, Color?> color = null)
            => new TextTable(headers, widths, rows, color);

        public static Scrollable Scroll(Control content) =>
            new Scrollable { Content = content, ExpandContentWidth = true, ExpandContentHeight = false, Border = BorderType.None };

        public static string Ft(double v) => v.ToString("0.#") + " ft";
        public static string Sf(double v) => v.ToString("#,0") + " sf";
    }
}
