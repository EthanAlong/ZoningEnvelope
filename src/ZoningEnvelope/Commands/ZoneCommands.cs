using System;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.UI;
using ZoningEnvelope.Model;
using ZoningEnvelope.Session;
using ZoningEnvelope.UI;

namespace ZoningEnvelope.Commands
{
    [System.Runtime.InteropServices.Guid("1b2c3d4e-5f60-4a7b-8c9d-0e1f2a3b4c01")]
    public class ZoneEnvelopeCommand : Command
    {
        public override string EnglishName => "ZoneEnvelope";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            Panels.OpenPanel(ZoningPanel.PanelId);
            return Result.Success;
        }
    }

    [System.Runtime.InteropServices.Guid("1b2c3d4e-5f60-4a7b-8c9d-0e1f2a3b4c02")]
    public class ZoneSetParcelCommand : Command
    {
        public override string EnglishName => "ZoneSetParcel";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var go = new GetObject();
            go.SetCommandPrompt("Select the parcel boundary (closed polyline)");
            go.GeometryFilter = ObjectType.Curve;
            go.GeometryAttributeFilter = GeometryAttributeFilter.ClosedCurve;
            go.SubObjectSelect = false;
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();
            var objRef = go.Object(0);
            var curve = objRef.Curve();
            string err;
            var geo = Engine.ParcelGeometry.TryCreate(curve, out err);
            if (geo == null) { RhinoApp.WriteLine("Zoning Envelope: " + err); return Result.Failure; }

            var gp = new GetPoint();
            gp.SetCommandPrompt("Click near the FRONT (street) edge");
            gp.Get();
            if (gp.CommandResult() != Result.Success) return gp.CommandResult();
            int front = geo.ClosestEdge(gp.Point());

            ZoningSession.Current.SetParcel(doc, objRef.ObjectId, front);
            Panels.OpenPanel(ZoningPanel.PanelId);
            doc.Objects.UnselectAll();
            RhinoApp.WriteLine("Zoning Envelope: parcel set, edge {0} is the front. Use ZoneEdgeFlags for street width / adjacent low-density edges.", front + 1);
            return Result.Success;
        }
    }

    [System.Runtime.InteropServices.Guid("1b2c3d4e-5f60-4a7b-8c9d-0e1f2a3b4c03")]
    public class ZoneSetMassingCommand : Command
    {
        public override string EnglishName => "ZoneSetMassing";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var s = ZoningSession.Current;
            if (s.ParcelId == Guid.Empty) { RhinoApp.WriteLine("Zoning Envelope: set a parcel first (ZoneSetParcel)."); return Result.Failure; }
            var go = new GetObject();
            go.SetCommandPrompt("Select the massing (closed polysurface, extrusion, mesh or SubD)");
            go.GeometryFilter = ObjectType.Brep | ObjectType.Extrusion | ObjectType.Mesh | ObjectType.SubD;
            go.SubObjectSelect = false;
            go.Get();
            if (go.CommandResult() != Result.Success) return go.CommandResult();
            s.SetMassing(doc, go.Object(0).ObjectId);
            doc.Objects.UnselectAll();
            return Result.Success;
        }
    }

    [System.Runtime.InteropServices.Guid("1b2c3d4e-5f60-4a7b-8c9d-0e1f2a3b4c04")]
    public class ZoneEdgeFlagsCommand : Command
    {
        public override string EnglishName => "ZoneEdgeFlags";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var s = ZoningSession.Current;
            if (s.Parcel == null) { RhinoApp.WriteLine("Zoning Envelope: set a parcel first (ZoneSetParcel)."); return Result.Failure; }

            var gp = new GetPoint();
            gp.SetCommandPrompt("Click near the parcel edge to edit");
            gp.Get();
            if (gp.CommandResult() != Result.Success) return gp.CommandResult();
            int idx = s.Parcel.ClosestEdge(gp.Point());
            s.Setup.Resize(s.Parcel.EdgeCount);
            var cur = s.Setup.Edges[idx];

            var roles = new[] { "Front", "Side", "Rear" };
            int roleIdx = cur.Role == EdgeRole.Front ? 0 : cur.Role == EdgeRole.Rear ? 2 : 1;
            var street = new OptionToggle(cur.Street, "No", "Yes");
            var width = new OptionDouble(cur.StreetWidthFeet, 0, 500);
            var adj = new OptionToggle(cur.AdjacentLowDensity, "No", "Yes");

            var go = new GetOption();
            go.SetCommandPrompt(string.Format("Edge {0}: set options, Enter to apply", idx + 1));
            go.AcceptNothing(true);
            while (true)
            {
                go.ClearCommandOptions();
                int roleOpt = go.AddOptionList("Role", roles, roleIdx);
                int streetOpt = go.AddOptionToggle("Street", ref street);
                int widthOpt = go.AddOptionDouble("StreetWidthFt", ref width);
                int adjOpt = go.AddOptionToggle("AdjacentLowDensity", ref adj);
                var res = go.Get();
                if (res == GetResult.Nothing) break;
                if (res != GetResult.Option) return go.CommandResult();
                var opt = go.Option();
                if (opt.Index == roleOpt) roleIdx = opt.CurrentListOptionIndex;
            }

            s.UpdateEdge(idx, e =>
            {
                e.Role = roleIdx == 0 ? EdgeRole.Front : roleIdx == 2 ? EdgeRole.Rear : EdgeRole.Side;
                e.Street = street.CurrentValue;
                e.StreetWidthFeet = width.CurrentValue;
                e.AdjacentLowDensity = adj.CurrentValue;
            });
            return Result.Success;
        }
    }

    [System.Runtime.InteropServices.Guid("1b2c3d4e-5f60-4a7b-8c9d-0e1f2a3b4c05")]
    public class ZoneBakeCommand : Command
    {
        public override string EnglishName => "ZoneBake";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            int n = ZoningSession.Current.Bake(doc);
            RhinoApp.WriteLine("Zoning Envelope: baked {0} envelope solid(s) to layer 'Zoning Envelope'.", n);
            return n > 0 ? Result.Success : Result.Nothing;
        }
    }

    [System.Runtime.InteropServices.Guid("1b2c3d4e-5f60-4a7b-8c9d-0e1f2a3b4c06")]
    public class ZoneReloadCodesCommand : Command
    {
        public override string EnglishName => "ZoneReloadCodes";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var s = ZoningSession.Current;
            s.ReloadLibrary();
            RhinoApp.WriteLine("Zoning Envelope: {0} rule sets loaded from built-in + {1}", s.Library.Codes.Count, CodeLibrary.UserFolder);
            foreach (var e in s.Library.Errors) RhinoApp.WriteLine("  " + e);
            return Result.Success;
        }
    }
}
