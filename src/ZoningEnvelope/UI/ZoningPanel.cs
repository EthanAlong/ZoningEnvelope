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
    public class RuleRow
    {
        public string Rule { get; set; }
        public string Value { get; set; }
        public string Applied { get; set; }
    }

    public class CheckRow
    {
        public string Item { get; set; }
        public string Allowed { get; set; }
        public string Massing { get; set; }
        public string Status { get; set; }
    }

    public class OpeningsUiRow
    {
        public string Edge { get; set; }
        public string Fsd { get; set; }
        public string Unprotected { get; set; }
        public string Protected { get; set; }
    }

    /// <summary>
    /// Docked panel. Header (parcel / massing / code), options, then tabs:
    /// Rules (what the code says and what it means here), Check (compliance + openings),
    /// Plan (2D diagram), Notes (sources, what is not modeled, applied rules).
    /// </summary>
    [System.Runtime.InteropServices.Guid("5a1e8c3d-2b4f-4e7a-9c6d-8f0a1b2c3d4e")]
    public class ZoningPanel : Panel
    {
        public static Guid PanelId => typeof(ZoningPanel).GUID;

        private readonly Label _parcelLabel = new Label { Text = "none" };
        private readonly Label _massingLabel = new Label { Text = "none" };
        private readonly DropDown _codes = new DropDown();
        private readonly CheckBox _sprinklered = new CheckBox { Text = "Sprinklered", Checked = true };
        private readonly CheckBox _pitched = new CheckBox { Text = "Pitched roof" };
        private readonly CheckBox _show = new CheckBox { Text = "Show envelope", Checked = true };
        private readonly GridView _rules = new GridView { ShowHeader = true, GridLines = GridLines.Horizontal };
        private readonly GridView _checks = new GridView { ShowHeader = true, GridLines = GridLines.Horizontal };
        private readonly GridView _openings = new GridView { ShowHeader = true, GridLines = GridLines.Horizontal };
        private readonly PlanDiagram _plan = new PlanDiagram();
        private readonly TextArea _notes = new TextArea { ReadOnly = true, Wrap = true };
        private readonly Label _status = new Label { Text = "", Wrap = WrapMode.Word, TextColor = Color.FromArgb(180, 60, 40) };
        private readonly Label _summary = new Label { Text = "", Wrap = WrapMode.Word };
        private bool _suppress;

        public ZoningPanel(uint documentSerialNumber)
        {
            Build();
            ZoningSession.Current.Updated += Refresh;
            Refresh();
        }

        private static Button Btn(string text, Action click)
        {
            var b = new Button { Text = text, MinimumSize = new Size(0, 0) };
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

            _rules.Columns.Add(new GridColumn { HeaderText = "Rule", DataCell = new TextBoxCell(nameof(RuleRow.Rule)), Width = 105 });
            _rules.Columns.Add(new GridColumn { HeaderText = "Code says", DataCell = new TextBoxCell(nameof(RuleRow.Value)), Expand = true });
            _rules.Columns.Add(new GridColumn { HeaderText = "Here", DataCell = new TextBoxCell(nameof(RuleRow.Applied)), Width = 95 });

            _checks.Columns.Add(new GridColumn { HeaderText = "Check", DataCell = new TextBoxCell(nameof(CheckRow.Item)), Width = 95 });
            _checks.Columns.Add(new GridColumn { HeaderText = "Allowed", DataCell = new TextBoxCell(nameof(CheckRow.Allowed)), Expand = true });
            _checks.Columns.Add(new GridColumn { HeaderText = "Massing", DataCell = new TextBoxCell(nameof(CheckRow.Massing)), Width = 90 });
            _checks.Columns.Add(new GridColumn { HeaderText = "", DataCell = new TextBoxCell(nameof(CheckRow.Status)), Width = 48 });

            _openings.Columns.Add(new GridColumn { HeaderText = "Facade faces", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Edge)), Expand = true });
            _openings.Columns.Add(new GridColumn { HeaderText = "FSD", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Fsd)), Width = 55 });
            _openings.Columns.Add(new GridColumn { HeaderText = "Unprot.", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Unprotected)), Width = 80 });
            _openings.Columns.Add(new GridColumn { HeaderText = "Prot.", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Protected)), Width = 80 });

            var header = new TableLayout
            {
                Spacing = new Size(6, 3),
                Rows =
                {
                    new TableRow(new Label { Text = "Parcel" }, new TableCell(_parcelLabel, true), pickParcel, edges),
                    new TableRow(new Label { Text = "Massing" }, new TableCell(_massingLabel, true), pickMassing, bake),
                    new TableRow(new Label { Text = "Code" }, new TableCell(_codes, true), reload, edit),
                }
            };
            var optRow = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 10, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _sprinklered, _pitched, _show }
            };

            var checkPage = new TableLayout
            {
                Spacing = new Size(0, 4),
                Rows =
                {
                    new TableRow(_summary),
                    new TableRow(_checks) { ScaleHeight = true },
                    new TableRow(new Label { Text = "Exterior wall openings, % of wall per story (CBC 705.8)", Font = SystemFonts.Bold() }),
                    new TableRow(_openings) { ScaleHeight = true },
                }
            };
            var notesPage = new TableLayout
            {
                Spacing = new Size(0, 4),
                Rows =
                {
                    new TableRow(_notes) { ScaleHeight = true },
                    new TableRow(folder),
                }
            };

            var tabs = new TabControl();
            tabs.Pages.Add(new TabPage { Text = "Rules", Content = _rules });
            tabs.Pages.Add(new TabPage { Text = "Check", Content = checkPage });
            tabs.Pages.Add(new TabPage { Text = "Plan", Content = _plan });
            tabs.Pages.Add(new TabPage { Text = "Notes", Content = notesPage });

            Content = new TableLayout
            {
                Padding = 6,
                Spacing = new Size(4, 6),
                Rows =
                {
                    new TableRow(header),
                    new TableRow(optRow),
                    new TableRow(tabs) { ScaleHeight = true },
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
                _codes.SelectedKey = s.Code?.Id ?? s.Setup.CodeId;
                _sprinklered.Checked = s.Setup.Sprinklered;
                _pitched.Checked = s.Setup.PitchedRoof;
                _show.Checked = s.ShowEnvelope;

                var env = s.Envelope;
                _parcelLabel.Text = s.Parcel == null
                    ? (s.ParcelError ?? "none")
                    : s.Parcel.EdgeCount + " edges, " + (env?.LotAreaSqFt ?? 0).ToString("#,0") + " sf, " + (env?.LotWidthFeet ?? 0).ToString("0") + " x " + (env?.LotDepthFeet ?? 0).ToString("0") + " ft";
                _massingLabel.Text = s.Setup.MassingId == Guid.Empty ? "none" : (s.Compliance?.HasMassing == true ? "linked" : "missing");

                _rules.DataStore = BuildRules(s);
                _checks.DataStore = BuildChecks(s);
                _openings.DataStore = BuildOpenings(s);
                _summary.Text = BuildSummary(s);
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

        private static List<RuleRow> BuildRules(ZoningSession s)
        {
            var rows = new List<RuleRow>();
            var code = s.Code; var env = s.Envelope;
            if (code == null) return rows;
            double w = env?.LotWidthFeet ?? 0, d = env?.LotDepthFeet ?? 0;

            rows.Add(new RuleRow { Rule = "Front setback", Value = code.Setbacks.Front.Describe(), Applied = env == null ? "" : Ft(code.Setbacks.Front.Resolve(w, d, 1)) });
            rows.Add(new RuleRow { Rule = "Side setback", Value = code.Setbacks.Side.Describe(), Applied = env == null ? "" : Ft(code.Setbacks.Side.Resolve(w, d, 1)) });
            rows.Add(new RuleRow { Rule = "Rear setback", Value = code.Setbacks.Rear.Describe(), Applied = env == null ? "" : Ft(code.Setbacks.Rear.Resolve(w, d, 1)) });
            if (code.AdjacentLowDensitySetbacks != null)
                rows.Add(new RuleRow { Rule = code.AdjacentLowDensitySetbacks.Label ?? "Low-density boundary", Value = "side " + code.AdjacentLowDensitySetbacks.SideFeet + " ft, rear " + code.AdjacentLowDensitySetbacks.RearFeet + " ft", Applied = s.Setup.Edges.Any(e => e.AdjacentLowDensity) ? "applied" : "no edge flagged" });
            foreach (var st in code.Stepbacks)
                rows.Add(new RuleRow { Rule = st.Label ?? "Stepback", Value = "+" + st.AdditionalFeet + " ft on " + string.Join("/", st.Sides) + " above story " + st.AboveStory, Applied = "" });

            string hv;
            if (code.Height.IsUnlimited) hv = "no limit (drawn to " + code.Height.DisplayCapFeet + " ft)";
            else
            {
                hv = code.Height.MaxFeet + " ft";
                if (code.Height.PitchedRoofMaxFeet > 0) hv += " flat / " + code.Height.PitchedRoofMaxFeet + " ft pitched";
            }
            if (code.Height.MaxStories > 0) hv += ", " + code.Height.MaxStories + " stories";
            rows.Add(new RuleRow { Rule = "Height", Value = hv, Applied = env == null ? "" : Ft(env.HeightFeet) });
            foreach (var p in code.Planes)
                rows.Add(new RuleRow { Rule = p.Label ?? "Plane", Value = p.StartHeightFeet + " ft at " + (p.FromLotLine ? "lot line" : "setback line") + ", " + p.AngleDegrees + " deg, " + string.Join("/", p.Sides), Applied = "" });
            if (code.TransitionalHeight != null)
                rows.Add(new RuleRow { Rule = code.TransitionalHeight.Label ?? "Transitional height", Value = string.Join("; ", code.TransitionalHeight.Steps.Select(t => "<" + t.UpToFeet + " ft: " + t.MaxHeightFeet + " ft")), Applied = s.Setup.Edges.Any(e => e.AdjacentLowDensity) ? "applied" : "no edge flagged" });
            if (code.FloorArea != null && code.FloorArea.Ratio > 0)
                rows.Add(new RuleRow { Rule = code.FloorArea.Label ?? "FAR", Value = code.FloorArea.Ratio.ToString("0.##") + " x lot area", Applied = env == null ? "" : Sf(code.FloorArea.Ratio * env.LotAreaSqFt) });
            if (code.Coverage != null && code.Coverage.MaxPercent > 0)
                rows.Add(new RuleRow { Rule = "Lot coverage", Value = code.Coverage.MaxPercent + "%", Applied = env == null ? "" : Sf(code.Coverage.MaxPercent / 100.0 * env.LotAreaSqFt) });
            if (code.Density != null && code.Density.LotAreaPerUnitSqFt > 0)
                rows.Add(new RuleRow { Rule = "Density", Value = code.Density.Describe(), Applied = env == null ? "" : code.Density.MaxUnitsFor(env.LotAreaSqFt) + " units" });
            return rows;
        }

        private static string BuildSummary(ZoningSession s)
        {
            var env = s.Envelope;
            if (env == null) return "Set a parcel to see the envelope.";
            var sb = new StringBuilder();
            sb.Append("Envelope: ").Append(Ft(env.HeightFeet)).Append(" high, footprint ").Append(Sf(env.FootprintSqFt))
              .Append(" of ").Append(Sf(env.LotAreaSqFt)).Append(" lot, ").Append(env.VolumeCuFt.ToString("#,0")).Append(" cf.");
            if (s.Compliance != null && !s.Compliance.HasMassing) sb.Append("  Link a massing to check it.");
            return sb.ToString();
        }

        private static List<CheckRow> BuildChecks(ZoningSession s)
        {
            var rows = new List<CheckRow>();
            var c = s.Compliance; var env = s.Envelope;
            if (c == null || env == null) return rows;
            bool m = c.HasMassing;
            string Ok(bool ok) => !m ? "" : (ok ? "OK" : "OVER");
            rows.Add(new CheckRow { Item = "Height", Allowed = Ft(env.HeightFeet), Massing = m ? Ft(c.HeightFeet) : "", Status = Ok(c.HeightOk) });
            rows.Add(new CheckRow { Item = "Envelope", Allowed = "inside", Massing = m ? (c.ExcessVolumeCuFt.HasValue ? c.ExcessVolumeCuFt.Value.ToString("#,0") + " cf out" : "n/a") : "", Status = m && !c.ExcessVolumeCuFt.HasValue ? "?" : Ok(c.EnvelopeOk) });
            if (c.AllowedFloorAreaSqFt > 0)
                rows.Add(new CheckRow { Item = c.FloorAreaLabel, Allowed = Sf(c.AllowedFloorAreaSqFt), Massing = m ? Sf(c.GrossFloorAreaSqFt) : "", Status = Ok(c.FloorAreaOk) });
            else if (m)
                rows.Add(new CheckRow { Item = "Floor area", Allowed = "no rule", Massing = Sf(c.GrossFloorAreaSqFt), Status = "" });
            if (c.AllowedCoverageSqFt > 0)
                rows.Add(new CheckRow { Item = "Coverage", Allowed = Sf(c.AllowedCoverageSqFt), Massing = m ? Sf(c.FootprintSqFt) : "", Status = Ok(c.CoverageOk) });
            else if (m)
                rows.Add(new CheckRow { Item = "Footprint", Allowed = Sf(env.FootprintSqFt) + " buildable", Massing = Sf(c.FootprintSqFt), Status = "" });
            if (c.MaxUnits > 0)
                rows.Add(new CheckRow { Item = "Units", Allowed = c.MaxUnits.ToString("0"), Massing = "", Status = "" });
            return rows;
        }

        private static List<OpeningsUiRow> BuildOpenings(ZoningSession s)
        {
            var rows = new List<OpeningsUiRow>();
            var c = s.Compliance;
            if (c == null) return rows;
            foreach (var o in c.Openings)
                rows.Add(new OpeningsUiRow { Edge = o.Label, Fsd = o.FsdFeet.ToString("0.#") + "'", Unprotected = o.Allowed, Protected = o.AllowedProtected });
            if (rows.Count == 0 && c.HasMassing) rows.Add(new OpeningsUiRow { Edge = "no vertical facade found", Fsd = "", Unprotected = "", Protected = "" });
            return rows;
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
