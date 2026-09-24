using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;
using ZoningEnvelope.Engine;
using ZoningEnvelope.Model;
using ZoningEnvelope.Session;

namespace ZoningEnvelope.UI
{
    /// <summary>
    /// Docked panel. Header (parcel / massing / code), options, then tabs:
    /// Rules (what the code says and what it means here), Check (compliance + openings),
    /// Plan (2D diagram), Notes (sources, what is not modeled, applied rules).
    /// Tables are plain wrapped labels so text stays readable at any panel width and DPI.
    /// </summary>
    [System.Runtime.InteropServices.Guid("5a1e8c3d-2b4f-4e7a-9c6d-8f0a1b2c3d4e")]
    public class ZoningPanel : Panel
    {
        public static Guid PanelId => typeof(ZoningPanel).GUID;

        private static readonly float BaseSize = SystemFonts.Default().Size;
        private static readonly Font Body = SystemFonts.Default(BaseSize * 1.2f);
        private static readonly Font BodyBold = SystemFonts.Bold(BaseSize * 1.2f);
        private static readonly Font Head = SystemFonts.Bold(BaseSize * 1.35f);
        private static readonly Color Stripe = Color.FromArgb(0, 0, 0, 14);
        private static readonly Color Muted = Color.FromArgb(110, 110, 110);
        private static readonly Color Bad = Color.FromArgb(190, 50, 30);
        private static readonly Color Good = Color.FromArgb(30, 130, 60);

        private readonly Label _parcelLabel = L("none");
        private readonly Label _massingLabel = L("none");
        private readonly DropDown _codes = new DropDown { Font = Body };
        private readonly CheckBox _sprinklered = new CheckBox { Text = "Sprinklered", Checked = true, Font = Body };
        private readonly CheckBox _pitched = new CheckBox { Text = "Pitched roof", Font = Body };
        private readonly CheckBox _show = new CheckBox { Text = "Show envelope", Checked = true, Font = Body };
        private readonly TabPage _rulesPage = new TabPage { Text = "Rules" };
        private readonly TabPage _checkPage = new TabPage { Text = "Check" };
        private readonly TabPage _planPage = new TabPage { Text = "Plan" };
        private readonly TabPage _notesPage = new TabPage { Text = "Notes" };
        private readonly PlanDiagram _plan = new PlanDiagram();
        private readonly TextArea _notes = new TextArea { ReadOnly = true, Wrap = true, Font = Body };
        private readonly Label _status = new Label { Text = "", Wrap = WrapMode.Word, TextColor = Bad, Font = Body };
        private bool _suppress;

        public ZoningPanel(uint documentSerialNumber)
        {
            Size = new Size(520, 820);
            MinimumSize = new Size(340, 420);
            Build();
            ZoningSession.Current.Updated += Refresh;
            Refresh();
        }

        private static Label L(string text) => new Label { Text = text, Font = Body, Wrap = WrapMode.Word, VerticalAlignment = VerticalAlignment.Center };

        private static Button Btn(string text, Action click)
        {
            var b = new Button { Text = text, Font = Body, MinimumSize = new Size(0, 0) };
            b.Click += (s, e) => click();
            return b;
        }

        private void Build()
        {
            var pickParcel = Btn("Set...", () => RhinoApp.RunScript("_ZoneSetParcel", false));
            var edges = Btn("Edges...", () => RhinoApp.RunScript("_ZoneEdgeFlags", false));
            var pickMassing = Btn("Set...", () => RhinoApp.RunScript("_ZoneSetMassing", false));
            var bake = Btn("Bake", () => RhinoApp.RunScript("_ZoneBake", false));
            var reload = Btn("Reload", () => ZoningSession.Current.ReloadLibrary());
            var edit = Btn("Edit JSON", EditCurrentCode);
            var folder = Btn("Open rule folder", OpenFolder);

            _codes.SelectedValueChanged += (s, e) =>
            {
                if (_suppress || _codes.SelectedKey == null) return;
                ZoningSession.Current.SetCode(_codes.SelectedKey);
            };
            EventHandler<EventArgs> opt = (s, e) =>
            {
                if (_suppress) return;
                ZoningSession.Current.SetOptions(_sprinklered.Checked == true, _pitched.Checked == true);
            };
            _sprinklered.CheckedChanged += opt;
            _pitched.CheckedChanged += opt;
            _show.CheckedChanged += (s, e) => { if (!_suppress) ZoningSession.Current.ShowEnvelope = _show.Checked == true; };

            var header = new TableLayout
            {
                Spacing = new Size(8, 6),
                Rows =
                {
                    new TableRow(L("Parcel"), new TableCell(_parcelLabel, true), pickParcel, edges),
                    new TableRow(L("Massing"), new TableCell(_massingLabel, true), pickMassing, bake),
                    new TableRow(L("Code"), new TableCell(_codes, true), reload, edit),
                }
            };
            var optRow = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 14, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _sprinklered, _pitched, _show }
            };

            _planPage.Content = _plan;
            _notesPage.Content = new TableLayout
            {
                Spacing = new Size(0, 6),
                Rows = { new TableRow(_notes) { ScaleHeight = true }, new TableRow(folder) }
            };

            var tabs = new TabControl();
            tabs.Pages.Add(_rulesPage);
            tabs.Pages.Add(_checkPage);
            tabs.Pages.Add(_planPage);
            tabs.Pages.Add(_notesPage);

            Content = new TableLayout
            {
                Padding = 8,
                Spacing = new Size(4, 8),
                Rows =
                {
                    new TableRow(header),
                    new TableRow(optRow),
                    new TableRow(tabs) { ScaleHeight = true },
                    new TableRow(_status),
                }
            };
        }

        // ---------- simple wrapped-label tables ----------

        private static Control Cell(string text, Font font, Color? bg, int width, Color? fg = null)
        {
            var l = new Label { Text = text ?? "", Font = font, Wrap = WrapMode.Word, VerticalAlignment = VerticalAlignment.Top };
            if (fg.HasValue) l.TextColor = fg.Value;
            if (width > 0) l.Width = width;
            var p = new Panel { Content = l, Padding = new Padding(5, 4) };
            if (bg.HasValue) p.BackgroundColor = bg.Value;
            return p;
        }

        /// <param name="widths">pixel width per column; 0 = this column takes the remaining width.</param>
        private static TableLayout Table(string[] headers, int[] widths, List<string[]> rows, Func<int, int, Color?> color = null)
        {
            var t = new TableLayout { Spacing = new Size(0, 0) };
            var h = new List<TableCell>();
            for (int i = 0; i < headers.Length; i++) h.Add(new TableCell(Cell(headers[i], BodyBold, null, widths[i], Muted), widths[i] == 0));
            t.Rows.Add(new TableRow(h));
            for (int r = 0; r < rows.Count; r++)
            {
                var cells = new List<TableCell>();
                Color? bg = r % 2 == 0 ? Stripe : (Color?)null;
                for (int i = 0; i < headers.Length; i++)
                {
                    string txt = i < rows[r].Length ? rows[r][i] : "";
                    cells.Add(new TableCell(Cell(txt, Body, bg, widths[i], color?.Invoke(r, i)), widths[i] == 0));
                }
                t.Rows.Add(new TableRow(cells));
            }
            return t;
        }

        private static Scrollable Scroll(Control content) =>
            new Scrollable { Content = content, ExpandContentWidth = true, ExpandContentHeight = false, Border = BorderType.None };

        // ---------- actions ----------

        private void EditCurrentCode()
        {
            var s = ZoningSession.Current;
            var code = s.Code;
            if (code == null) return;
            try
            {
                string path = code.FilePath ?? s.Library.ExportBuiltIn(code);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                RhinoApp.WriteLine("Zoning Envelope: editing " + path + " (saved changes reload automatically)");
            }
            catch (Exception ex) { RhinoApp.WriteLine("Zoning Envelope: " + ex.Message); }
        }

        private static void OpenFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(CodeLibrary.UserFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + CodeLibrary.UserFolder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { RhinoApp.WriteLine("Zoning Envelope: " + ex.Message); }
        }

        // ---------- refresh ----------

        private void Refresh()
        {
            var s = ZoningSession.Current;
            _suppress = true;
            try
            {
                var zones = s.Library.Zones.ToList();
                var keys = zones.Select(z => z.Id).ToList();
                if (!_codes.Items.Select(i => i.Key).SequenceEqual(keys))
                {
                    _codes.Items.Clear();
                    foreach (var z in zones) _codes.Items.Add(new ListItem { Text = z.DisplayName + (z.BuiltIn ? "" : " *"), Key = z.Id });
                }
                _codes.SelectedKey = s.Code?.Id ?? s.Setup.CodeId;
                _sprinklered.Checked = s.Setup.Sprinklered;
                _pitched.Checked = s.Setup.PitchedRoof;
                _show.Checked = s.ShowEnvelope;

                var env = s.Envelope;
                _parcelLabel.Text = s.Parcel == null
                    ? (s.ParcelError ?? "none")
                    : s.Parcel.EdgeCount + " edges, " + (env?.LotAreaSqFt ?? 0).ToString("#,0") + " sf, " + (env?.LotWidthFeet ?? 0).ToString("0") + " x " + (env?.LotDepthFeet ?? 0).ToString("0") + " ft";
                _massingLabel.Text = s.Setup.MassingId == Guid.Empty ? "none" : (s.Compliance?.HasMassing == true ? "linked" : "missing");

                _rulesPage.Content = Scroll(Table(new[] { "Rule", "Code says", "Here" }, new[] { 120, 0, 110 }, BuildRules(s)));
                _checkPage.Content = Scroll(BuildCheckPage(s));
                _notes.Text = BuildNotes(s);

                var msgs = new List<string>();
                if (env != null) msgs.AddRange(env.Warnings);
                if (s.Compliance != null) msgs.AddRange(s.Compliance.Warnings);
                msgs.AddRange(s.Library.Errors);
                _status.Text = string.Join("\n", msgs.Distinct());
                _status.Visible = _status.Text.Length > 0;
                _plan.Invalidate();
            }
            finally { _suppress = false; }
        }

        private static string Ft(double v) => v.ToString("0.#") + " ft";
        private static string Sf(double v) => v.ToString("#,0") + " sf";

        private static List<string[]> BuildRules(ZoningSession s)
        {
            var rows = new List<string[]>();
            var code = s.Code; var env = s.Envelope;
            if (code == null) return rows;
            double w = env?.LotWidthFeet ?? 0, d = env?.LotDepthFeet ?? 0;
            bool flagged = s.Setup.Edges.Any(e => e.AdjacentLowDensity);

            rows.Add(new[] { "Front setback", code.Setbacks.Front.Describe(), env == null ? "" : Ft(code.Setbacks.Front.Resolve(w, d, 1)) });
            rows.Add(new[] { "Side setback", code.Setbacks.Side.Describe(), env == null ? "" : Ft(code.Setbacks.Side.Resolve(w, d, 1)) });
            rows.Add(new[] { "Rear setback", code.Setbacks.Rear.Describe(), env == null ? "" : Ft(code.Setbacks.Rear.Resolve(w, d, 1)) });
            if (code.AdjacentLowDensitySetbacks != null)
                rows.Add(new[] { code.AdjacentLowDensitySetbacks.Label ?? "Low-density boundary", "side " + code.AdjacentLowDensitySetbacks.SideFeet + " ft, rear " + code.AdjacentLowDensitySetbacks.RearFeet + " ft", flagged ? "applied" : "no edge flagged" });
            foreach (var st in code.Stepbacks)
                rows.Add(new[] { st.Label ?? "Stepback", "+" + st.AdditionalFeet + " ft on " + string.Join("/", st.Sides) + " above story " + st.AboveStory, "" });

            string hv;
            if (code.Height.IsUnlimited) hv = "no limit (drawn to " + code.Height.DisplayCapFeet + " ft)";
            else
            {
                hv = code.Height.MaxFeet + " ft";
                if (code.Height.PitchedRoofMaxFeet > 0) hv += " flat / " + code.Height.PitchedRoofMaxFeet + " ft pitched";
            }
            if (code.Height.MaxStories > 0) hv += ", " + code.Height.MaxStories + " stories";
            rows.Add(new[] { "Height", hv, env == null ? "" : Ft(env.HeightFeet) });
            foreach (var p in code.Planes)
                rows.Add(new[] { p.Label ?? "Plane", p.StartHeightFeet + " ft at " + (p.FromLotLine ? "lot line" : "setback line") + ", " + p.AngleDegrees + " deg, " + string.Join("/", p.Sides), "" });
            if (code.TransitionalHeight != null)
                rows.Add(new[] { code.TransitionalHeight.Label ?? "Transitional height", string.Join("; ", code.TransitionalHeight.Steps.Select(t => "<" + t.UpToFeet + " ft: " + t.MaxHeightFeet + " ft")), flagged ? "applied" : "no edge flagged" });
            if (code.FloorArea != null && code.FloorArea.Ratio > 0)
                rows.Add(new[] { code.FloorArea.Label ?? "FAR", code.FloorArea.Ratio.ToString("0.##") + " x lot area", env == null ? "" : Sf(code.FloorArea.Ratio * env.LotAreaSqFt) });
            if (code.Coverage != null && code.Coverage.MaxPercent > 0)
                rows.Add(new[] { "Lot coverage", code.Coverage.MaxPercent + "%", env == null ? "" : Sf(code.Coverage.MaxPercent / 100.0 * env.LotAreaSqFt) });
            if (code.Density != null && code.Density.LotAreaPerUnitSqFt > 0)
                rows.Add(new[] { "Density", code.Density.Describe(), env == null ? "" : code.Density.MaxUnitsFor(env.LotAreaSqFt) + " units" });
            return rows;
        }

        private Control BuildCheckPage(ZoningSession s)
        {
            var stack = new StackLayout { Orientation = Orientation.Vertical, HorizontalContentAlignment = HorizontalAlignment.Stretch, Spacing = 8, Padding = new Padding(0, 4) };
            var env = s.Envelope; var c = s.Compliance;
            if (env == null)
            {
                stack.Items.Add(L("Set a parcel to see the envelope."));
                return stack;
            }
            var sb = new StringBuilder();
            sb.Append("Envelope ").Append(Ft(env.HeightFeet)).Append(" high, footprint ").Append(Sf(env.FootprintSqFt))
              .Append(" of a ").Append(Sf(env.LotAreaSqFt)).Append(" lot, ").Append(env.VolumeCuFt.ToString("#,0")).Append(" cf.");
            if (c != null && !c.HasMassing) sb.Append("  Link a massing to check it.");
            stack.Items.Add(L(sb.ToString()));

            if (c != null)
            {
                bool m = c.HasMassing;
                string Ok(bool ok) => !m ? "" : (ok ? "OK" : "OVER");
                var rows = new List<string[]>();
                rows.Add(new[] { "Height", Ft(env.HeightFeet), m ? Ft(c.HeightFeet) : "", Ok(c.HeightOk) });
                rows.Add(new[] { "Envelope", "inside", m ? (c.ExcessVolumeCuFt.HasValue ? c.ExcessVolumeCuFt.Value.ToString("#,0") + " cf outside" : "n/a") : "", m && !c.ExcessVolumeCuFt.HasValue ? "?" : Ok(c.EnvelopeOk) });
                if (c.AllowedFloorAreaSqFt > 0) rows.Add(new[] { c.FloorAreaLabel, Sf(c.AllowedFloorAreaSqFt), m ? Sf(c.GrossFloorAreaSqFt) : "", Ok(c.FloorAreaOk) });
                else if (m) rows.Add(new[] { "Floor area", "no rule", Sf(c.GrossFloorAreaSqFt), "" });
                if (c.AllowedCoverageSqFt > 0) rows.Add(new[] { "Coverage", Sf(c.AllowedCoverageSqFt), m ? Sf(c.FootprintSqFt) : "", Ok(c.CoverageOk) });
                else if (m) rows.Add(new[] { "Footprint", Sf(env.FootprintSqFt) + " buildable", Sf(c.FootprintSqFt), "" });
                if (c.MaxUnits > 0) rows.Add(new[] { "Units", c.MaxUnits.ToString("0"), "", "" });
                stack.Items.Add(Table(new[] { "Check", "Allowed", "Massing", "" }, new[] { 100, 0, 120, 60 }, rows,
                    (r, i) => i == 3 ? (rows[r][3] == "OK" ? Good : rows[r][3] == "OVER" ? Bad : (Color?)null) : null));

                stack.Items.Add(new Label { Text = "Exterior wall openings, % of wall per story (CBC 705.8)", Font = Head, Wrap = WrapMode.Word });
                var orows = c.Openings.Select(o => new[] { o.Label, o.FsdFeet.ToString("0.#") + " ft", o.Allowed + (s.Setup.Sprinklered ? "" : " (no sprinklers)"), o.AllowedProtected }).ToList();
                if (orows.Count == 0) orows.Add(new[] { m ? "no vertical facade found" : "link a massing", "", "", "" });
                stack.Items.Add(Table(new[] { "Facade faces", "FSD", "Unprotected", "Protected" }, new[] { 0, 70, 120, 110 }, orows));
            }
            return stack;
        }

        private static string BuildNotes(ZoningSession s)
        {
            var code = s.Code;
            if (code == null) return "No rule set selected.";
            var sb = new StringBuilder();
            sb.AppendLine(code.DisplayName);
            sb.AppendLine("id: " + code.Id + (code.BuiltIn ? " (built-in)" : " (user file: " + code.FilePath + ")"));
            sb.AppendLine();
            if (!string.IsNullOrEmpty(code.Source)) sb.AppendLine("Source: " + code.Source);
            if (!string.IsNullOrEmpty(code.Verified)) sb.AppendLine("Verified: " + code.Verified);
            if (code.Notes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Notes:");
                foreach (var n in code.Notes) sb.AppendLine("  - " + n);
            }
            if (s.Envelope != null && s.Envelope.AppliedRules.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Applied to this parcel:");
                foreach (var a in s.Envelope.AppliedRules) sb.AppendLine("  - " + a);
            }
            if (s.OpeningsTable != null)
            {
                sb.AppendLine();
                sb.AppendLine("Openings table: " + s.OpeningsTable.Name + " (" + s.OpeningsTable.Source + ")");
            }
            sb.AppendLine();
            sb.AppendLine("User rule folder: " + CodeLibrary.UserFolder);
            sb.AppendLine("Drop a JSON file there (or Edit JSON) to add or override a rule set. Saved files reload automatically.");
            return sb.ToString();
        }
    }
}
