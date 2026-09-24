using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using ZoningEnvelope.Display;
using ZoningEnvelope.Engine;
using ZoningEnvelope.Model;

namespace ZoningEnvelope.Session
{
    /// <summary>
    /// Live state: which curve is the parcel, which object is the massing, which rule set applies,
    /// plus the last computed envelope and compliance. Listens to document changes and recomputes
    /// on Rhino's idle event so edits to the parcel, the massing, or a rule file show up immediately.
    /// </summary>
    public class ZoningSession
    {
        public static ZoningSession Current { get; } = new ZoningSession();

        public CodeLibrary Library { get; } = new CodeLibrary();
        public Guid ParcelId { get; private set; } = Guid.Empty;
        public ParcelSetup Setup { get; private set; } = new ParcelSetup();
        public ParcelGeometry Parcel { get; private set; }
        public ZoneCode Code { get; private set; }
        public ZoneCode OpeningsTable { get; private set; }
        public EnvelopeResult Envelope { get; private set; }
        public ComplianceResult Compliance { get; private set; }
        public string ParcelError { get; private set; }
        public double FeetToModel { get; private set; } = 1;
        public bool ShowEnvelope { get => _conduit.Enabled; set { _conduit.Enabled = value; RhinoDoc.ActiveDoc?.Views.Redraw(); } }

        /// <summary>Raised on the UI thread after every recompute.</summary>
        public event Action Updated;

        private readonly EnvelopeConduit _conduit = new EnvelopeConduit();
        private bool _dirty;
        private bool _started;

        public void Start()
        {
            if (_started) return;
            _started = true;
            Library.Reload();
            Library.Changed += () => RhinoApp.InvokeOnUiThread(new Action(() => { Library.Reload(); _dirty = true; }));
            RhinoDoc.ReplaceRhinoObject += OnReplace;
            RhinoDoc.DeleteRhinoObject += OnDelete;
            RhinoDoc.UndeleteRhinoObject += OnUndelete;
            RhinoDoc.EndOpenDocument += (s, e) => { if (!e.Merge) Restore(e.Document); };
            RhinoDoc.NewDocument += (s, e) => Clear();
            RhinoDoc.CloseDocument += (s, e) => Clear();
            RhinoApp.Idle += OnIdle;
            _conduit.Enabled = true;
            Restore(RhinoDoc.ActiveDoc);
        }

        public void Stop()
        {
            RhinoDoc.ReplaceRhinoObject -= OnReplace;
            RhinoDoc.DeleteRhinoObject -= OnDelete;
            RhinoDoc.UndeleteRhinoObject -= OnUndelete;
            RhinoApp.Idle -= OnIdle;
            _conduit.Enabled = false;
            Library.Dispose();
        }

        // ---------------- public actions ----------------

        public void SetParcel(RhinoDoc doc, Guid id, int frontEdge)
        {
            var obj = doc.Objects.FindId(id);
            if (obj == null) return;
            string err;
            var geo = ParcelGeometry.TryCreate(obj.Geometry as Curve, out err);
            if (geo == null) { RhinoApp.WriteLine("Zoning Envelope: " + err); return; }
            ParcelId = id;
            Setup = ParcelSetup.Load(obj);
            if (string.IsNullOrEmpty(Setup.CodeId) || Library.Find(Setup.CodeId) == null)
                Setup.CodeId = Library.Zones.FirstOrDefault()?.Id;
            geo.AssignRoles(Setup, Math.Max(0, Math.Min(frontEdge, geo.EdgeCount - 1)));
            Setup.Save(doc, obj);
            _dirty = true;
        }

        public void SetMassing(RhinoDoc doc, IEnumerable<Guid> ids)
        {
            Setup.MassingIds = ids.Where(g => g != Guid.Empty).Distinct().ToList();
            Persist(doc);
            _dirty = true;
        }

        public void SetCode(string id)
        {
            Setup.CodeId = id;
            Persist(RhinoDoc.ActiveDoc);
            _dirty = true;
        }

        public void SetOptions(bool sprinklered, bool pitchedRoof)
        {
            Setup.Sprinklered = sprinklered;
            Setup.PitchedRoof = pitchedRoof;
            Persist(RhinoDoc.ActiveDoc);
            _dirty = true;
        }

        public void UpdateEdge(int index, Action<EdgeSetup> edit)
        {
            if (Parcel == null) return;
            Setup.Resize(Parcel.EdgeCount);
            if (index < 0 || index >= Setup.Edges.Count) return;
            edit(Setup.Edges[index]);
            // only one front edge; if a new front was set, re-derive rear/sides
            if (Setup.Edges[index].Role == EdgeRole.Front)
            {
                var flags = Setup.Edges.Select(e => new EdgeSetup { Street = e.Street, StreetWidthFeet = e.StreetWidthFeet, AdjacentLowDensity = e.AdjacentLowDensity }).ToList();
                Parcel.AssignRoles(Setup, index);
                for (int i = 0; i < flags.Count; i++)
                {
                    Setup.Edges[i].Street = flags[i].Street;
                    Setup.Edges[i].StreetWidthFeet = flags[i].StreetWidthFeet;
                    Setup.Edges[i].AdjacentLowDensity = flags[i].AdjacentLowDensity;
                }
            }
            Persist(RhinoDoc.ActiveDoc);
            _dirty = true;
        }

        public void RequestRecompute() { _dirty = true; }

        public void ReloadLibrary()
        {
            Library.Reload();
            _dirty = true;
        }

        public int Bake(RhinoDoc doc)
        {
            if (Envelope == null || Envelope.Breps.Count == 0) return 0;
            int layer = doc.Layers.FindByFullPath("Zoning Envelope", -1);
            if (layer < 0)
            {
                var l = new Layer { Name = "Zoning Envelope", Color = System.Drawing.Color.FromArgb(40, 140, 255) };
                layer = doc.Layers.Add(l);
            }
            var attrs = new ObjectAttributes { LayerIndex = layer, Name = "Envelope " + (Code?.Id ?? "") };
            int n = 0;
            foreach (var b in Envelope.Breps)
                if (doc.Objects.AddBrep(b, attrs) != Guid.Empty) n++;
            doc.Views.Redraw();
            return n;
        }

        // ---------------- internals ----------------

        private void Persist(RhinoDoc doc)
        {
            if (doc == null || ParcelId == Guid.Empty) return;
            var obj = doc.Objects.FindId(ParcelId);
            if (obj != null) Setup.Save(doc, obj);
        }

        private void Clear()
        {
            ParcelId = Guid.Empty;
            Setup = new ParcelSetup();
            Parcel = null; Code = null; Envelope = null; Compliance = null; ParcelError = null;
            _conduit.Clear();
            Updated?.Invoke();
        }

        private void Restore(RhinoDoc doc)
        {
            Clear();
            if (doc == null) return;
            Guid id;
            if (Guid.TryParse(doc.Strings.GetValue(ParcelSetup.DocKeyParcel), out id) && doc.Objects.FindId(id) != null)
            {
                ParcelId = id;
                Setup = ParcelSetup.Load(doc.Objects.FindId(id));
                _dirty = true;
            }
        }

        private bool Tracked(Guid id) => id == ParcelId || Setup.MassingIds.Contains(id);

        private void OnReplace(object sender, RhinoReplaceObjectEventArgs e) { if (Tracked(e.ObjectId)) _dirty = true; }
        private void OnDelete(object sender, RhinoObjectEventArgs e) { if (Tracked(e.ObjectId)) _dirty = true; }
        private void OnUndelete(object sender, RhinoObjectEventArgs e) { if (Tracked(e.ObjectId)) _dirty = true; }

        private void OnIdle(object sender, EventArgs e)
        {
            if (!_dirty) return;
            _dirty = false;
            try { Recompute(RhinoDoc.ActiveDoc); }
            catch (Exception ex) { RhinoApp.WriteLine("Zoning Envelope: " + ex.Message); }
        }

        public void Recompute(RhinoDoc doc)
        {
            if (doc == null) return;
            var parcelObj = ParcelId == Guid.Empty ? null : doc.Objects.FindId(ParcelId);
            if (parcelObj == null || parcelObj.IsDeleted)
            {
                Parcel = null; Envelope = null; Compliance = null; Code = null;
                ParcelError = ParcelId == Guid.Empty ? null : "Parcel curve was deleted.";
                _conduit.Clear();
                doc.Views.Redraw();
                Updated?.Invoke();
                return;
            }

            FeetToModel = doc.ModelUnitSystem == UnitSystem.None ? 1.0 : RhinoMath.UnitScale(UnitSystem.Feet, doc.ModelUnitSystem);
            double tol = doc.ModelAbsoluteTolerance;

            string err;
            Parcel = ParcelGeometry.TryCreate(parcelObj.Geometry as Curve, out err);
            ParcelError = err;
            if (Parcel == null) { Envelope = null; Compliance = null; _conduit.Clear(); doc.Views.Redraw(); Updated?.Invoke(); return; }

            // keep edge setup aligned with the (possibly edited) polyline
            if (Setup.Edges.Count != Parcel.EdgeCount)
            {
                int front = Parcel.FrontIndex(Setup);
                Parcel.AssignRoles(Setup, front < 0 ? 0 : Math.Min(front, Parcel.EdgeCount - 1));
                Setup.Save(doc, parcelObj);
            }

            Code = Library.Find(Setup.CodeId) ?? Library.Zones.FirstOrDefault();
            OpeningsTable = Library.OpeningsTables.FirstOrDefault();

            Envelope = EnvelopeBuilder.Build(Parcel, Setup, Code, FeetToModel, tol);

            var massings = new List<Brep>();
            foreach (var id in Setup.MassingIds)
            {
                var mo = doc.Objects.FindId(id);
                if (mo == null || mo.IsDeleted) continue;
                var b = Engine.Compliance.ToBrep(mo);
                if (b != null) massings.Add(b);
            }
            Compliance = Engine.Compliance.Evaluate(Parcel, Setup, Code, OpeningsTable, Envelope, massings, FeetToModel, tol);

            _conduit.Update(Parcel, Setup, Envelope, Compliance, FeetToModel);
            doc.Views.Redraw();
            Updated?.Invoke();
        }
    }
}
