using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Rhino;
using Rhino.PlugIns;
using Rhino.UI;
using ZoningEnvelope.Session;
using ZoningEnvelope.UI;

[assembly: PlugInDescription(DescriptionType.Organization, "Ethan Huang")]
[assembly: PlugInDescription(DescriptionType.Country, "United States")]
[assembly: System.Runtime.InteropServices.Guid("7f3c2a41-9b8e-4d6f-a2c5-3e1b9d7f8a01")]

namespace ZoningEnvelope
{
    public class ZoningEnvelopePlugIn : PlugIn
    {
        public static ZoningEnvelopePlugIn Instance { get; private set; }

        public ZoningEnvelopePlugIn() { Instance = this; }

        public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            try
            {
                Panels.RegisterPanel(this, typeof(ZoningPanel), "Zoning Envelope", CreateIcon(), PanelType.System);
                ZoningSession.Current.Start();
                RhinoApp.WriteLine("Zoning Envelope loaded. Commands: ZoneEnvelope, ZoneSetParcel, ZoneSetMassing, ZoneEdgeFlags, ZoneBake, ZoneReloadCodes.");
                return LoadReturnCode.Success;
            }
            catch (Exception ex)
            {
                errorMessage = ex.ToString();
                return LoadReturnCode.ErrorShowDialog;
            }
        }

        protected override void OnShutdown()
        {
            ZoningSession.Current.Stop();
            base.OnShutdown();
        }

        private static System.Drawing.Icon CreateIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var outline = new Pen(Color.FromArgb(40, 40, 40), 3f))
                    g.DrawRectangle(outline, 3, 3, 26, 26);
                using (var fill = new SolidBrush(Color.FromArgb(40, 140, 255)))
                    g.FillRectangle(fill, 9, 9, 14, 14);
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
