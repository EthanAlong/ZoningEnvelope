using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using ZoningEnvelope.Model;
using ZoningEnvelope.Session;

namespace ZoningEnvelope.UI
{
    /// <summary>
    /// Main panel: parcel / massing / rule set selection, options, the rule table
    /// (what the code says and what it means for this lot) and the notes below it.
    /// The plan diagram and the compliance check live in their own panels.
    /// </summary>
    [System.Runtime.InteropServices.Guid("5a1e8c3d-2b4f-4e7a-9c6d-8f0a1b2c3d4e")]
    public class ZoningPanel : Panel
    {
        public static Guid PanelId => typeof(ZoningPanel).GUID;

        private readonly Label _parcelLabel = Ui.L("none");
        private readonly Label _massingLabel = Ui.L("none");
        private readonly DropDown _codes = new DropDown { Font = Ui.Body };
        private readonly CheckBox _sprinklered = new CheckBox { Text = "Sprinklered", Checked = true, Font = Ui.Body };
        private readonly CheckBox _pitched = new CheckBox { Text = "Pitched roof", Font = Ui.Body };
        private readonly CheckBox _show = new CheckBox { Text = "Show envelope", Checked = true, Font = Ui.Body };
        private readonly Scrollable _rulesHost = Ui.Scroll(new Panel());
        private readonly TextArea _notes = new TextArea { ReadOnly = true, Wrap = true, Font = Ui.Body };
        private readonly Label _status = new Label { Text = "", Wrap = WrapMode.Word, TextColor = Ui.Bad, Font = Ui.Body };
        private bool _suppress;

        public ZoningPanel(uint documentSerialNumber)
        {
            Build();
            ZoningSession.Current.Updated += Refresh;
            Refresh();
        }

        private void Build()
        {
            var pickParcel = Ui.Btn("Set parcel...", () => RhinoApp.RunScript("_ZoneSetParcel", false));
            var edges = Ui.Btn("Edge flags...", () => RhinoApp.RunScript("_ZoneEdgeFlags", false));
            var pickMassing = Ui.Btn("Set massing...", () => RhinoApp.RunScript("_ZoneSetMassing", false));
            var bake = Ui.Btn("Bake envelope", () => RhinoApp.RunScript("_ZoneBake", false));
            var reload = Ui.Btn("Reload rules", () => ZoningSession.Current.ReloadLibrary());
            var edit = Ui.Btn("Edit JSON", EditCurrentCode);
            var folder = Ui.Btn("Rule folder", OpenFolder);
            var panels = Ui.Btn("Plan + Check panels", () => RhinoApp.RunScript("_ZoneEnvelope", false));

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

            var objects = new TableLayout
            {
                Spacing = new Size(8, 6),
                Rows =
                {
                    new TableRow(Ui.L("Parcel"), new TableCell(_parcelLabel, true), pickParcel, edges),
                    new TableRow(Ui.L("Massing"), new TableCell(_massingLabel, true), pickMassing, bake),
                }
            };
            var codeButtons = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { reload, edit, folder, panels }
            };
            var optRow = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 14, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _sprinklered, _pitched, _show }
            };

            var split = new Splitter
            {
                Orientation = Orientation.Vertical,
                Panel1 = new TableLayout { Rows = { new TableRow(Ui.H("Rules")), new TableRow(_rulesHost) { ScaleHeight = true } }, Spacing = new Size(0, 4) },
                Panel2 = new TableLayout { Rows = { new TableRow(Ui.H("Notes")), new TableRow(_notes) { ScaleHeight = true } }, Spacing = new Size(0, 4) },
                Position = 380,
                FixedPanel = SplitterFixedPanel.None,
            };

            Content = new TableLayout
            {
                Padding = 8,
                Spacing = new Size(4, 8),
                Rows =
                {
                    new TableRow(objects),
                    new TableRow(Ui.L("Rule set")),
                    new TableRow(_codes),
                    new TableRow(codeButtons),
                    new TableRow(optRow),
                    new TableRow(split) { ScaleHeight = true },
                    new TableRow(_status),
                }
            };
        }

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
                _codes.SelectedKey = DisplayCode(s)?.Id;
                _sprinklered.Checked = s.Setup.Sprinklered;
                _pitched.Checked = s.Setup.PitchedRoof;
                _show.Checked = s.ShowEnvelope;

                var env = s.Envelope;
                _parcelLabel.Text = s.Parcel == null
                    ? (s.ParcelError ?? "none")
                    : s.Parcel.EdgeCount + " edges, " + (env?.LotAreaSqFt ?? 0).ToString("#,0") + " sf, " + (env?.LotWidthFeet ?? 0).ToString("0") + " x " + (env?.LotDepthFeet ?? 0).ToString("0") + " ft";
                _massingLabel.Text = !s.Setup.HasMassing ? "none"
                    : (s.Compliance?.HasMassing == true ? (s.Compliance.MassingCount == 1 ? "1 object linked" : s.Compliance.MassingCount + " objects linked") : "missing");

                _rulesHost.Content = Ui.Table(new[] { "Rule", "Code says", "Here" }, new[] { 130, 0, 110 }, BuildRules(s));
                _notes.Text = BuildNotes(s);

                var msgs = new List<string>();
                if (env != null) msgs.AddRange(env.Warnings);
                if (s.Compliance != null) msgs.AddRange(s.Compliance.Warnings);
                msgs.AddRange(s.Library.Errors);
                _status.Text = string.Join("\n", msgs.Distinct());
                _status.Visible = _status.Text.Length > 0;
            }
            finally { _suppress = false; }
        }

        /// <summary>The rule set to show: the computed one, else the chosen id, else the first zone (before a parcel is set).</summary>
        private static ZoneCode DisplayCode(ZoningSession s) =>
            s.Code ?? s.Library.Find(s.Setup.CodeId) ?? s.Library.Zones.FirstOrDefault();

        private static List<string[]> BuildRules(ZoningSession s)
        {
            var rows = new List<string[]>();
            var code = DisplayCode(s); var env = s.Envelope;
            if (code == null) return rows;
            double w = env?.LotWidthFeet ?? 0, d = env?.LotDepthFeet ?? 0;
            bool flagged = s.Setup.Edges.Any(e => e.AdjacentLowDensity);

            rows.Add(new[] { "Front setback", code.Setbacks.Front.Describe(), env == null ? "" : Ui.Ft(code.Setbacks.Front.Resolve(w, d, 1)) });
            rows.Add(new[] { "Side setback", code.Setbacks.Side.Describe(), env == null ? "" : Ui.Ft(code.Setbacks.Side.Resolve(w, d, 1)) });
            rows.Add(new[] { "Rear setback", code.Setbacks.Rear.Describe(), env == null ? "" : Ui.Ft(code.Setbacks.Rear.Resolve(w, d, 1)) });
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
            rows.Add(new[] { "Height", hv, env == null ? "" : Ui.Ft(env.HeightFeet) });
            foreach (var p in code.Planes)
                rows.Add(new[] { p.Label ?? "Plane", p.StartHeightFeet + " ft at " + (p.FromLotLine ? "lot line" : "setback line") + ", " + p.AngleDegrees + " deg, " + string.Join("/", p.Sides), "" });
            if (code.TransitionalHeight != null)
                rows.Add(new[] { code.TransitionalHeight.Label ?? "Transitional height", string.Join("; ", code.TransitionalHeight.Steps.Select(t => "<" + t.UpToFeet + " ft: " + t.MaxHeightFeet + " ft")), flagged ? "applied" : "no edge flagged" });
            if (code.FloorArea != null && code.FloorArea.Ratio > 0)
                rows.Add(new[] { code.FloorArea.Label ?? "FAR", code.FloorArea.Ratio.ToString("0.##") + " x lot area", env == null ? "" : Ui.Sf(code.FloorArea.Ratio * env.LotAreaSqFt) });
            if (code.Coverage != null && code.Coverage.MaxPercent > 0)
                rows.Add(new[] { "Lot coverage", code.Coverage.MaxPercent + "%", env == null ? "" : Ui.Sf(code.Coverage.MaxPercent / 100.0 * env.LotAreaSqFt) });
            if (code.Density != null && code.Density.LotAreaPerUnitSqFt > 0)
                rows.Add(new[] { "Density", code.Density.Describe(), env == null ? "" : code.Density.MaxUnitsFor(env.LotAreaSqFt) + " units" });
            return rows;
        }

        private static string BuildNotes(ZoningSession s)
        {
            var code = DisplayCode(s);
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
