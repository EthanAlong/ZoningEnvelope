using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using ZoningEnvelope.Session;

namespace ZoningEnvelope.UI
{
    /// <summary>Check panel: envelope summary, massing compliance and opening limits per facade.</summary>
    [System.Runtime.InteropServices.Guid("7c3a0e5f-4d6b-4a9c-b2e8-0f1d3c4b5e6a")]
    public class ZoningCheckPanel : Panel
    {
        public static Guid PanelId => typeof(ZoningCheckPanel).GUID;

        private readonly Scrollable _host = Ui.Scroll(new Panel());

        public ZoningCheckPanel(uint documentSerialNumber)
        {
            Size = new Size(520, 520);
            MinimumSize = new Size(320, 240);
            Content = new TableLayout { Padding = 8, Rows = { new TableRow(_host) { ScaleHeight = true } } };
            ZoningSession.Current.Updated += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            var s = ZoningSession.Current;
            var stack = new StackLayout { Orientation = Orientation.Vertical, HorizontalContentAlignment = HorizontalAlignment.Stretch, Spacing = 10 };
            var env = s.Envelope; var c = s.Compliance;
            if (env == null)
            {
                stack.Items.Add(Ui.L("Set a parcel (main Zoning Envelope panel) to see the envelope here."));
                _host.Content = stack;
                return;
            }

            stack.Items.Add(Ui.H("Envelope"));
            var sb = new StringBuilder();
            sb.Append(s.Code?.DisplayName ?? "").Append('\n');
            sb.Append(Ui.Ft(env.HeightFeet)).Append(" high, footprint ").Append(Ui.Sf(env.FootprintSqFt))
              .Append(" of a ").Append(Ui.Sf(env.LotAreaSqFt)).Append(" lot, ").Append(env.VolumeCuFt.ToString("#,0")).Append(" cf.");
            stack.Items.Add(Ui.L(sb.ToString()));

            if (c != null)
            {
                bool m = c.HasMassing;
                stack.Items.Add(Ui.H(m ? "Massing check" : "Massing check (link a massing with Set massing...)"));
                string Ok(bool ok) => !m ? "" : (ok ? "OK" : "OVER");
                var rows = new List<string[]>();
                rows.Add(new[] { "Height", Ui.Ft(env.HeightFeet), m ? Ui.Ft(c.HeightFeet) : "", Ok(c.HeightOk) });
                rows.Add(new[] { "Envelope", "inside", m ? (c.ExcessVolumeCuFt.HasValue ? c.ExcessVolumeCuFt.Value.ToString("#,0") + " cf outside" : "n/a") : "", m && !c.ExcessVolumeCuFt.HasValue ? "?" : Ok(c.EnvelopeOk) });
                if (c.AllowedFloorAreaSqFt > 0) rows.Add(new[] { c.FloorAreaLabel, Ui.Sf(c.AllowedFloorAreaSqFt), m ? Ui.Sf(c.GrossFloorAreaSqFt) : "", Ok(c.FloorAreaOk) });
                else if (m) rows.Add(new[] { "Floor area", "no rule", Ui.Sf(c.GrossFloorAreaSqFt), "" });
                if (c.AllowedCoverageSqFt > 0) rows.Add(new[] { "Coverage", Ui.Sf(c.AllowedCoverageSqFt), m ? Ui.Sf(c.FootprintSqFt) : "", Ok(c.CoverageOk) });
                else if (m) rows.Add(new[] { "Footprint", Ui.Sf(env.FootprintSqFt) + " buildable", Ui.Sf(c.FootprintSqFt), "" });
                if (c.MaxUnits > 0) rows.Add(new[] { "Units", c.MaxUnits.ToString("0"), "", "" });
                stack.Items.Add(Ui.Table(new[] { "Check", "Allowed", "Massing", "" }, new[] { 110, 0, 130, 64 }, rows,
                    (r, i) => i == 3 ? (rows[r][3] == "OK" ? Ui.Good : rows[r][3] == "OVER" ? Ui.Bad : (Color?)null) : null));

                stack.Items.Add(Ui.H("Exterior wall openings, % of wall per story (CBC 705.8)"));
                var orows = c.Openings.Select(o => new[] { o.Label, o.FsdFeet.ToString("0.#") + " ft", o.Allowed + (s.Setup.Sprinklered ? "" : " (no sprinklers)"), o.AllowedProtected }).ToList();
                if (orows.Count == 0) orows.Add(new[] { m ? "no vertical facade found" : "link a massing", "", "", "" });
                stack.Items.Add(Ui.Table(new[] { "Facade faces", "FSD", "Unprotected", "Protected" }, new[] { 0, 70, 130, 110 }, orows));
                stack.Items.Add(Ui.L("Fire separation distance is measured from each facade to the parcel edge it faces, plus half the street width on edges flagged as street."));
            }

            var msgs = new List<string>();
            msgs.AddRange(env.Warnings);
            if (c != null) msgs.AddRange(c.Warnings);
            if (msgs.Count > 0)
                stack.Items.Add(new Label { Text = string.Join("\n", msgs.Distinct()), Font = Ui.Body, Wrap = WrapMode.Word, TextColor = Ui.Bad });

            _host.Content = stack;
        }
    }
}
