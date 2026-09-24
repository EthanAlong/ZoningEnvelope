using System;
using Eto.Drawing;
using Eto.Forms;
using ZoningEnvelope.Session;

namespace ZoningEnvelope.UI
{
    /// <summary>Plan panel: the 2D diagram of the parcel, setbacks and story footprints.</summary>
    [System.Runtime.InteropServices.Guid("6b2f9d4e-3c5a-4f8b-a1d7-9e0c2b3a4d5f")]
    public class ZoningPlanPanel : Panel
    {
        public static Guid PanelId => typeof(ZoningPlanPanel).GUID;

        private readonly PlanDiagram _plan = new PlanDiagram();

        public ZoningPlanPanel(uint documentSerialNumber)
        {
            Size = new Size(420, 420);
            Content = new TableLayout { Padding = 4, Rows = { new TableRow(_plan) { ScaleHeight = true } } };
            ZoningSession.Current.Updated += () => _plan.Invalidate();
        }
    }
}
