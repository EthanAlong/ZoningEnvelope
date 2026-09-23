using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Rhino.Geometry;
using ZoningEnvelope.Engine;
using ZoningEnvelope.Model;

// Smoke test for the parts of the engine that do not need a running Rhino:
// rule JSON parsing, setback resolution, half-plane clipping, openings table lookup.
class Program
{
    static int _fail;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fail++;
    }

    static bool Near(double a, double b, double eps = 1e-6) => Math.Abs(a - b) < eps;

    static int Main()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var asm = Assembly.GetExecutingAssembly();
        var codes = new Dictionary<string, ZoneCode>();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("codes.")))
        {
            using (var s = asm.GetManifestResourceStream(res))
            using (var r = new StreamReader(s))
            {
                var c = JsonSerializer.Deserialize<ZoneCode>(r.ReadToEnd(), opts);
                codes[c.Id] = c;
                Console.WriteLine("loaded " + c.Id + "  (" + c.Kind + ")  " + c.Name);
            }
        }
        Check(codes.Count == 6, "six rule sets embedded");

        Console.WriteLine("\nLAMC R1 on a 50 x 150 lot");
        var r1 = codes["lamc-r1-hd1"];
        Check(Near(r1.Setbacks.Front.Resolve(50, 150, 1), 20), "front = 20% of 150 capped at 20 -> 20");
        Check(Near(r1.Setbacks.Front.Resolve(50, 80, 1), 16), "front = 20% of 80 -> 16");
        Check(Near(r1.Setbacks.Side.Resolve(50, 150, 1), 5), "side = 5 on a 50 ft lot");
        Check(Near(r1.Setbacks.Side.Resolve(40, 150, 1), 4), "side = 10% of 40 = 4 on a narrow lot");
        Check(Near(r1.Setbacks.Side.Resolve(25, 150, 1), 3), "side = max(2.5, 3) = 3 on a 25 ft lot");
        Check(Near(r1.Setbacks.Rear.Resolve(50, 150, 1), 15), "rear = 15");
        Check(Near(r1.Height.Effective(false), 28) && Near(r1.Height.Effective(true), 33), "height 28 flat / 33 pitched");
        Check(r1.Planes.Count == 1 && r1.Planes[0].AppliesTo(EdgeRole.Front) && r1.Planes[0].AppliesTo(EdgeRole.Side) && !r1.Planes[0].AppliesTo(EdgeRole.Rear), "encroachment plane on front + side only");
        Check(!r1.IsStoryDependent, "R1 is not story dependent");

        Console.WriteLine("\nLAMC R3 side yard per story");
        var r3 = codes["lamc-r3-hd1"];
        Check(Near(r3.Setbacks.Side.Resolve(50, 150, 2), 5), "story 2 -> 5");
        Check(Near(r3.Setbacks.Side.Resolve(50, 150, 3), 6), "story 3 -> 6");
        Check(Near(r3.Setbacks.Side.Resolve(50, 150, 20), 16), "story 20 -> capped at 16");
        Check(r3.IsStoryDependent, "R3 is story dependent");

        Console.WriteLine("\nSanta Monica R2");
        var r2 = codes["smmc-r2"];
        Check(Near(r2.Setbacks.Side.Resolve(50, 150, 1), 8), "50 ft lot -> 8");
        Check(Near(r2.Setbacks.Side.Resolve(40, 150, 1), 6.4), "40 ft lot -> 16% = 6.4");
        Check(Near(r2.Setbacks.Side.Resolve(20, 150, 1), 4), "20 ft lot -> max(3.2, 4) = 4");
        Check(r2.Stepbacks.Count == 1 && r2.Stepbacks[0].AppliesTo(EdgeRole.Side), "side stepback present");

        Console.WriteLine("\nTransitional height steps");
        var c2 = codes["lamc-c2-1vl"];
        Check(c2.TransitionalHeight != null && c2.TransitionalHeight.Steps.Count == 3 && Near(c2.TransitionalHeight.Steps[2].MaxHeightFeet, 61), "3 steps, last 61 ft");

        Console.WriteLine("\nCBC 705.8 lookup");
        var t = codes["cbc-2022-705-8"];
        OpeningsRow Row(double fsd) => t.Rows.First(r => r.Contains(fsd));
        Check(Row(2).UnprotectedSprinklered == 0, "2 ft -> not permitted");
        Check(Row(4).UnprotectedSprinklered == 15 && Row(4).UnprotectedNonsprinklered == 0, "4 ft -> 15% spk / NP no spk");
        Check(Row(12).UnprotectedSprinklered == 45, "12 ft -> 45%");
        Check(Row(19.9).UnprotectedSprinklered == 75, "19.9 ft -> 75%");
        Check(Row(22).UnprotectedSprinklered == -1 && Row(22).UnprotectedNonsprinklered == 45, "22 ft -> no limit spk / 45% no spk");
        Check(Row(31).UnprotectedNonsprinklered == -1, "31 ft -> no limit");
        Check(OpeningsRow.Format(-1) == "No limit" && OpeningsRow.Format(0) == "Not permitted" && OpeningsRow.Format(45) == "45%", "format");

        Console.WriteLine("\nHalf-plane clipping (50 x 100 CCW rectangle)");
        var rect = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(50, 0, 0), new Point3d(50, 100, 0), new Point3d(0, 100, 0) };
        // edge 0: (0,0)->(50,0) front along +X, inward = +Y
        var clipped = PolygonClip.ClipHalfPlane(rect, new Point3d(0, 0, 0), new Vector3d(0, 1, 0), 20);
        Check(clipped.Count == 4 && Near(PolygonClip.Area(clipped), 50 * 80), "front 20 -> 50 x 80");
        clipped = PolygonClip.ClipHalfPlane(clipped, new Point3d(50, 0, 0), new Vector3d(-1, 0, 0), 5);   // right side
        clipped = PolygonClip.ClipHalfPlane(clipped, new Point3d(50, 100, 0), new Vector3d(0, -1, 0), 15); // rear
        clipped = PolygonClip.ClipHalfPlane(clipped, new Point3d(0, 100, 0), new Vector3d(1, 0, 0), 5);   // left side
        Check(Near(PolygonClip.Area(clipped), 40 * 65), "after all four setbacks -> 40 x 65 = 2600");
        var gone = PolygonClip.ClipHalfPlane(rect, new Point3d(0, 0, 0), new Vector3d(0, 1, 0), 120);
        Check(gone.Count == 0, "setback larger than the lot -> empty");

        // triangle lot: clipping should keep a triangle
        var tri = new List<Point3d> { new Point3d(0, 0, 0), new Point3d(60, 0, 0), new Point3d(0, 60, 0) };
        var triClipped = PolygonClip.ClipHalfPlane(tri, new Point3d(0, 0, 0), new Vector3d(0, 1, 0), 10);
        Check(triClipped.Count == 3 && Near(PolygonClip.Area(triClipped), 0.5 * 50 * 50), "triangle front 10 -> 50 x 50 / 2");

        Console.WriteLine(_fail == 0 ? "\nALL PASSED" : "\n" + _fail + " FAILED");
        return _fail == 0 ? 0 : 1;
    }
}
