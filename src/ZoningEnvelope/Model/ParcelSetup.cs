using System;
using System.Collections.Generic;
using System.Text.Json;
using Rhino;
using Rhino.DocObjects;

namespace ZoningEnvelope.Model
{
    /// <summary>Per-edge role and flags, stored on the parcel curve as user strings.</summary>
    public class EdgeSetup
    {
        public EdgeRole Role { get; set; } = EdgeRole.Side;
        /// <summary>Edge faces a street: fire separation distance is measured to the street centerline.</summary>
        public bool Street { get; set; }
        public double StreetWidthFeet { get; set; }
        /// <summary>Edge abuts a lot in a more restrictive (low-density) zone: triggers transitional height / boundary setbacks.</summary>
        public bool AdjacentLowDensity { get; set; }
    }

    /// <summary>
    /// Everything the tool needs to know about a parcel, persisted on the parcel curve so it
    /// survives save/reopen: zone id, edge roles/flags, linked massing object, options.
    /// </summary>
    public class ParcelSetup
    {
        public const string KeyCode = "ZE.code";
        public const string KeyEdges = "ZE.edges";
        public const string KeyMassing = "ZE.massing";
        public const string KeySprinklered = "ZE.sprinklered";
        public const string KeyPitched = "ZE.pitchedRoof";
        public const string DocKeyParcel = "ZE.parcel";

        public string CodeId { get; set; }
        public List<EdgeSetup> Edges { get; set; } = new List<EdgeSetup>();
        /// <summary>Linked massing objects (one or more closed solids).</summary>
        public List<Guid> MassingIds { get; set; } = new List<Guid>();
        public bool HasMassing => MassingIds.Count > 0;
        public bool Sprinklered { get; set; } = true;
        public bool PitchedRoof { get; set; }

        public static ParcelSetup Load(RhinoObject obj)
        {
            var s = new ParcelSetup();
            if (obj == null) return s;
            var a = obj.Attributes;
            s.CodeId = a.GetUserString(KeyCode);
            var edges = a.GetUserString(KeyEdges);
            if (!string.IsNullOrEmpty(edges))
            {
                try { s.Edges = JsonSerializer.Deserialize<List<EdgeSetup>>(edges, CodeLibrary.JsonOptions) ?? new List<EdgeSetup>(); }
                catch { s.Edges = new List<EdgeSetup>(); }
            }
            foreach (var part in (a.GetUserString(KeyMassing) ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Guid g;
                if (Guid.TryParse(part, out g) && g != Guid.Empty) s.MassingIds.Add(g);
            }
            s.Sprinklered = a.GetUserString(KeySprinklered) != "false";
            s.PitchedRoof = a.GetUserString(KeyPitched) == "true";
            return s;
        }

        public void Save(RhinoDoc doc, RhinoObject obj)
        {
            if (doc == null || obj == null) return;
            var a = obj.Attributes.Duplicate();
            a.SetUserString(KeyCode, CodeId ?? "");
            a.SetUserString(KeyEdges, JsonSerializer.Serialize(Edges, CodeLibrary.JsonOptions));
            a.SetUserString(KeyMassing, string.Join(",", MassingIds));
            a.SetUserString(KeySprinklered, Sprinklered ? "true" : "false");
            a.SetUserString(KeyPitched, PitchedRoof ? "true" : "false");
            doc.Objects.ModifyAttributes(obj, a, true);
            doc.Strings.SetString(DocKeyParcel, obj.Id.ToString());
        }

        /// <summary>Make sure there is one EdgeSetup per parcel edge.</summary>
        public void Resize(int edgeCount)
        {
            while (Edges.Count < edgeCount) Edges.Add(new EdgeSetup());
            if (Edges.Count > edgeCount) Edges.RemoveRange(edgeCount, Edges.Count - edgeCount);
        }
    }
}
