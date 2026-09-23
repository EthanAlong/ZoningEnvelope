using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly GridView _rules = new GridView { Height = 150, ShowHeader = true };
        private readonly GridView _checks = new GridView { Height = 120, ShowHeader = true };
        private readonly GridView _openings = new GridView { Height = 90, ShowHeader = true };
        private readonly PlanDiagram _plan = new PlanDiagram();
        private readonly Label _status = new Label { Text = "", Wrap = WrapMode.Word };
        private readonly Label _sourceLabel = new Label { Text = "", Wrap = WrapMode.Word, TextColor = Color.FromArgb(120, 120, 120) };
        private bool _suppress;

        public ZoningPanel(uint documentSerialNumber)
        {
            Build();
            ZoningSession.Current.Updated += Refresh;
            Refresh();
        }

        private void Build()
        {
            var pickParcel = new Button { Text = "Set parcel" };
            pickParcel.Click += (s, e) => RhinoApp.RunScript("_ZoneSetParcel", false);
            var pickMassing = new Button { Text = "Set massing" };
            pickMassing.Click += (s, e) => RhinoApp.RunScript("_ZoneSetMassing", false);
            var edges = new Button { Text = "Edge flags" };
            edges.Click += (s, e) => RhinoApp.RunScript("_ZoneEdgeFlags", false);
            var bake = new Button { Text = "Bake" };
            bake.Click += (s, e) => RhinoApp.RunScript("_ZoneBake", false);

            var reload = new Button { Text = "Reload" };
            reload.Click += (s, e) => ZoningSession.Current.ReloadLibrary();
            var edit = new Button { Text = "Edit JSON" };
            edit.Click += (s, e) => EditCurrentCode();
            var folder = new Button { Text = "Folder" };
            folder.Click += (s, e) => OpenFolder();

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

            _rules.Columns.Add(new GridColumn { HeaderText = "Rule", DataCell = new TextBoxCell(nameof(RuleRow.Rule)), Width = 110 });
            _rules.Columns.Add(new GridColumn { HeaderText = "Code says", DataCell = new TextBoxCell(nameof(RuleRow.Value)), Width = 220 });
            _rules.Columns.Add(new GridColumn { HeaderText = "This parcel", DataCell = new TextBoxCell(nameof(RuleRow.Applied)), Width = 120 });

            _checks.Columns.Add(new GridColumn { HeaderText = "Check", DataCell = new TextBoxCell(nameof(CheckRow.Item)), Width = 110 });
            _checks.Columns.Add(new GridColumn { HeaderText = "Allowed", DataCell = new TextBoxCell(nameof(CheckRow.Allowed)), Width = 110 });
            _checks.Columns.Add(new GridColumn { HeaderText = "Massing", DataCell = new TextBoxCell(nameof(CheckRow.Massing)), Width = 110 });
            _checks.Columns.Add(new GridColumn { HeaderText = "Status", DataCell = new TextBoxCell(nameof(CheckRow.Status)), Width = 90 });

            _openings.Columns.Add(new GridColumn { HeaderText = "Facade faces", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Edge)), Width = 150 });
            _openings.Columns.Add(new GridColumn { HeaderText = "FSD", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Fsd)), Width = 60 });
            _openings.Columns.Add(new GridColumn { HeaderText = "Unprotected", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Unprotected)), Width = 100 });
            _openings.Columns.Add(new GridColumn { HeaderText = "Protected", DataCell = new TextBoxCell(nameof(OpeningsUiRow.Protected)), Width = 100 });

            var top = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { pickParcel, _parcelLabel, pickMassing, _massingLabel, edges, bake }
            };
            var codeRow = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { new Label { Text = "Code" }, new StackLayoutItem(_codes, true), reload, edit, folder }
            };
            var optRow = new StackLayout
            {
                Orientation = Orientation.Horizontal, Spacing = 12, VerticalContentAlignment = VerticalAlignment.Center,
                Items = { _sprinklered, _pitched, _show }
            };

            Content = new TableLayout
            {
                Padding = 6,
                Spacing = new Size(4, 4),
                Rows =
                {
                    new TableRow(top),
                    new TableRow(codeRow),
                    new TableRow(_sourceLabel),
                    new TableRow(optRow),
                    new TableRow(new Label { Text = "Rules", Font = SystemFonts.Bold() }),
                    new TableRow(_rules),
                    new TableRow(new Label { Text = "Compliance", Font = SystemFonts.Bold() }),
                    new TableRow(_checks),
                    new TableRow(new Label { Text = "Exterior wall openings (fire separation distance)", Font = SystemFonts.Bold() }),
                    new TableRow(_openings),
                    new TableRow(_plan) { ScaleHeight = true },
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
                // code list
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

                _parcelLabel.Text = s.Parcel == null ? (s.ParcelError ?? "none") : (s.Parcel.EdgeCount + " edges");
                _massingLabel.Text = s.Setup.MassingId == Guid.Empty ? "none" : (s.Compliance?.HasMassing == true ? "linked" : "missing");
                _sourceLabel.Text = s.Code == null ? "" : s.Code.Source + (string.IsNullOrEmpty(s.Code.Verified) ? "" : "  |  " + s.Code.Verified);

                _rules.DataStore = BuildRules(s);
                _checks.DataStore = BuildChecks(s);
                _openings.DataStore = BuildOpenings(s);

                var msgs = new List<string>();
                if (s.Envelope != null) msgs.AddRange(s.Envelope.Warnings);
                if (s.Compliance != null) msgs.AddRange(s.Compliance.Warnings);
                msgs.AddRange(s.Library.Errors);
                _status.Text = string.Join("\n", msgs.Distinct());
                _plan.Invalidate();
            }
            finally { _suppress = false; }
        }

        private static List<RuleRow> BuildRules(ZoningSession s)
        {
            var rows = new List<RuleRow>();
            var code = s.Code; var env = s.Envelope;
            if (code == null) return rows;
            double w = env?.LotWidthFeet ?? 0, d = env?.LotDepthFeet ?? 0;
            string Ft(double v) => v.ToString("0.#") + " ft";

            rows.Add(new RuleRow { Rule = "Front setback", Value = code.Setbacks.Front.Describe(), Applied = env == null ? "" : Ft(code.Setbacks.Front.Resolve(w, d, 1)) });
            rows.Add(new RuleRow { Rule = "Side setback", Value = code.Setbacks.Side.Describe(), Applied = env == null ? "" : Ft(code.Setbacks.Side.Resolve(w, d, 1)) });
            rows.Add(new RuleRow { Rule = "Rear setback", Value = code.Setbacks.Rear.Describe(), Applied = env == null ? "" : Ft(code.Setbacks.Rear.Resolve(w, d, 1)) });
            if (code.AdjacentLowDensitySetbacks != null)
                rows.Add(new RuleRow { Rule = "Low-density boundary", Value = "side " + code.AdjacentLowDensitySetbacks.SideFeet + " ft, rear " + code.AdjacentLowDensitySetbacks.RearFeet + " ft", Applied = s.Setup.Edges.Any(e => e.AdjacentLowDensity) ? "applied" : "no edge flagged" });
            foreach (var st in code.Stepbacks)
                rows.Add(new RuleRow { Rule = st.Label ?? "Stepback", Value = "+" + st.AdditionalFeet + " ft on " + string.Join("/", st.Sides) + " above story " + st.AboveStory, Applied = "" });

            string hv = code.Height.MaxFeet + " ft";
            if (code.Height.PitchedRoofMaxFeet > 0) hv += " flat / " + code.Height.PitchedRoofMaxFeet + " ft pitched";
            if (code.Height.MaxStories > 0) hv += ", " + code.Height.MaxStories + " stories";
            rows.Add(new RuleRow { Rule = "Height", Value = hv, Applied = env == null ? "" : Ft(env.HeightFeet) });
            foreach (var p in code.Planes)
                rows.Add(new RuleRow { Rule = p.Label ?? "Plane", Value = p.StartHeightFeet + " ft at " + (p.FromLotLine ? "lot line" : "setback line") + ", " + p.AngleDegrees + " deg, " + string.Join("/", p.Sides), Applied = "" });
            if (code.TransitionalHeight != null)
                rows.Add(new RuleRow { Rule = code.TransitionalHeight.Label ?? "Transitional height", Value = string.Join("; ", code.TransitionalHeight.Steps.Select(t => "<" + t.UpToFeet + " ft: " + t.MaxHeightFeet + " ft")), Applied = s.Setup.Edges.Any(e => e.AdjacentLowDensity) ? "applied" : "no edge flagged" });
            if (code.FloorArea != null && code.FloorArea.Ratio > 0)
                rows.Add(new RuleRow { Rule = code.FloorArea.Label ?? "FAR", Value = code.FloorArea.Ratio.ToString("0.##") + " x lot area", Applied = env == null ? "" : (code.FloorArea.Ratio * env.LotAreaSqFt).ToString("#,0") + " sf" });
            if (code.Coverage != null && code.Coverage.MaxPercent > 0)
                rows.Add(new RuleRow { Rule = "Lot coverage", Value = code.Coverage.MaxPercent + "%", Applied = env == null ? "" : (code.Coverage.MaxPercent / 100.0 * env.LotAreaSqFt).ToString("#,0") + " sf" });
            if (code.Density != null && code.Density.LotAreaPerUnitSqFt > 0)
                rows.Add(new RuleRow { Rule = "Density", Value = code.Density.LotAreaPerUnitSqFt + " sf lot per unit", Applied = env == null ? "" : Math.Floor(env.LotAreaSqFt / code.Density.LotAreaPerUnitSqFt) + " units" });
            if (env != null)
                rows.Add(new RuleRow { Rule = "Lot", Value = "width " + Ft(w) + " (front edge), depth " + Ft(d), Applied = env.LotAreaSqFt.ToString("#,0") + " sf" });
            foreach (var n in code.Notes)
                rows.Add(new RuleRow { Rule = "note", Value = n, Applied = "" });
            return rows;
        }

        private static List<CheckRow> BuildChecks(ZoningSession s)
        {
            var rows = new List<CheckRow>();
            var c = s.Compliance; var env = s.Envelope;
            if (c == null || env == null) return rows;
            bool m = c.HasMassing;
            string Ok(bool ok) => !m ? "" : (ok ? "OK" : "OVER");
            rows.Add(new CheckRow { Item = "Height", Allowed = env.HeightFeet.ToString("0.#") + " ft", Massing = m ? c.HeightFeet.ToString("0.#") + " ft" : "", Status = Ok(c.HeightOk) });
            rows.Add(new CheckRow { Item = "Inside envelope", Allowed = env.VolumeCuFt.ToString("#,0") + " cf envelope", Massing = m ? (c.ExcessVolumeCuFt.HasValue ? c.ExcessVolumeCuFt.Value.ToString("#,0") + " cf outside" : "n/a") : "", Status = m && !c.ExcessVolumeCuFt.HasValue ? "?" : Ok(c.EnvelopeOk) });
            if (c.AllowedFloorAreaSqFt > 0)
                rows.Add(new CheckRow { Item = c.FloorAreaLabel, Allowed = c.AllowedFloorAreaSqFt.ToString("#,0") + " sf", Massing = m ? c.GrossFloorAreaSqFt.ToString("#,0") + " sf" : "", Status = Ok(c.FloorAreaOk) });
            else if (m)
                rows.Add(new CheckRow { Item = "Floor area", Allowed = "no rule", Massing = c.GrossFloorAreaSqFt.ToString("#,0") + " sf", Status = "" });
            if (c.AllowedCoverageSqFt > 0)
                rows.Add(new CheckRow { Item = "Coverage", Allowed = c.AllowedCoverageSqFt.ToString("#,0") + " sf", Massing = m ? c.FootprintSqFt.ToString("#,0") + " sf" : "", Status = Ok(c.CoverageOk) });
            else if (m)
                rows.Add(new CheckRow { Item = "Footprint", Allowed = env.FootprintSqFt.ToString("#,0") + " sf buildable", Massing = c.FootprintSqFt.ToString("#,0") + " sf", Status = "" });
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
                rows.Add(new OpeningsUiRow { Edge = o.Label, Fsd = o.FsdFeet.ToString("0.#") + " ft", Unprotected = o.Allowed + (s.Setup.Sprinklered ? " (spk)" : " (no spk)"), Protected = o.AllowedProtected });
            return rows;
        }
    }
}
