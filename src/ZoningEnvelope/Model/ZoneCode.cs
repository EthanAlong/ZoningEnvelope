using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZoningEnvelope.Model
{
    /// <summary>
    /// One rule set loaded from a JSON file. kind = "zone" (setbacks, height, planes...)
    /// or "openings" (a fire-separation-distance table applied to any zone).
    /// All lengths are feet, all areas square feet; the engine converts to model units.
    /// </summary>
    public class ZoneCode
    {
        public string Id { get; set; }
        public string Kind { get; set; } = "zone";
        public string Jurisdiction { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string Verified { get; set; }
        public List<string> Notes { get; set; } = new List<string>();

        public SetbackSet Setbacks { get; set; }
        public HeightRule Height { get; set; }
        public FloorAreaRule FloorArea { get; set; }
        public CoverageRule Coverage { get; set; }
        public DensityRule Density { get; set; }
        public List<StepbackRule> Stepbacks { get; set; } = new List<StepbackRule>();
        public List<PlaneRule> Planes { get; set; } = new List<PlaneRule>();
        public TransitionalHeightRule TransitionalHeight { get; set; }
        public AdjacentLowDensitySetbacks AdjacentLowDensitySetbacks { get; set; }

        // kind == "openings"
        public List<OpeningsRow> Rows { get; set; } = new List<OpeningsRow>();

        [JsonIgnore] public string FilePath { get; set; }
        [JsonIgnore] public bool BuiltIn { get; set; }

        [JsonIgnore]
        public string DisplayName => string.IsNullOrEmpty(Jurisdiction) ? Name : Jurisdiction + " - " + Name;

        [JsonIgnore]
        public bool IsStoryDependent
        {
            get
            {
                if (Stepbacks != null && Stepbacks.Count > 0) return true;
                if (Setbacks == null) return false;
                return Setbacks.Front.PerStoryFeet > 0 || Setbacks.Side.PerStoryFeet > 0 || Setbacks.Rear.PerStoryFeet > 0;
            }
        }
    }

    public class SetbackSet
    {
        public SetbackRule Front { get; set; } = new SetbackRule();
        public SetbackRule Side { get; set; } = new SetbackRule();
        public SetbackRule Rear { get; set; } = new SetbackRule();

        public SetbackRule For(EdgeRole role)
        {
            switch (role)
            {
                case EdgeRole.Front: return Front;
                case EdgeRole.Rear: return Rear;
                default: return Side;
            }
        }
    }

    public class SetbackRule
    {
        /// <summary>Base setback in feet.</summary>
        public double Feet { get; set; }
        /// <summary>Setback as a percentage of lot depth (front/rear rules).</summary>
        public double PercentOfLotDepth { get; set; }
        /// <summary>Setback as a percentage of lot width (side rules).</summary>
        public double PercentOfLotWidth { get; set; }
        public double MinFeet { get; set; }
        /// <summary>0 = no cap.</summary>
        public double MaxFeet { get; set; }
        public NarrowLotRule NarrowLot { get; set; }
        /// <summary>Stories above this one get PerStoryFeet added each (e.g. 2 = above the 2nd story).</summary>
        public int PerStoryAboveStory { get; set; }
        public double PerStoryFeet { get; set; }
        /// <summary>0 = no cap.</summary>
        public double PerStoryMaxFeet { get; set; }

        /// <summary>Resolve the required setback in feet for the given parcel dimensions and story (1-based).</summary>
        public double Resolve(double lotWidthFt, double lotDepthFt, int story)
        {
            double v = Feet;
            if (PercentOfLotDepth > 0) v = Math.Max(v, lotDepthFt * PercentOfLotDepth / 100.0);
            if (PercentOfLotWidth > 0) v = Math.Max(v, lotWidthFt * PercentOfLotWidth / 100.0);
            if (NarrowLot != null && NarrowLot.WidthLessThanFeet > 0 && lotWidthFt < NarrowLot.WidthLessThanFeet)
            {
                double n = NarrowLot.Feet;
                if (NarrowLot.PercentOfLotWidth > 0) n = Math.Max(n, lotWidthFt * NarrowLot.PercentOfLotWidth / 100.0);
                v = Math.Max(n, NarrowLot.MinFeet);
            }
            if (MinFeet > 0) v = Math.Max(v, MinFeet);
            if (MaxFeet > 0) v = Math.Min(v, MaxFeet);
            if (PerStoryFeet > 0 && story > PerStoryAboveStory)
            {
                v += (story - PerStoryAboveStory) * PerStoryFeet;
                if (PerStoryMaxFeet > 0) v = Math.Min(v, PerStoryMaxFeet);
            }
            return v;
        }

        public string Describe()
        {
            var parts = new List<string>();
            if (PercentOfLotDepth > 0) parts.Add(PercentOfLotDepth + "% of lot depth");
            if (PercentOfLotWidth > 0) parts.Add(PercentOfLotWidth + "% of lot width");
            if (Feet > 0 || parts.Count == 0) parts.Insert(0, Feet + " ft");
            if (MinFeet > 0) parts.Add("min " + MinFeet + " ft");
            if (MaxFeet > 0) parts.Add("max " + MaxFeet + " ft");
            if (NarrowLot != null && NarrowLot.WidthLessThanFeet > 0)
            {
                string n = NarrowLot.PercentOfLotWidth > 0 ? NarrowLot.PercentOfLotWidth + "% of width" : NarrowLot.Feet + " ft";
                if (NarrowLot.MinFeet > 0) n += ", min " + NarrowLot.MinFeet + " ft";
                parts.Add("lots under " + NarrowLot.WidthLessThanFeet + " ft wide: " + n);
            }
            if (PerStoryFeet > 0)
            {
                string s = "+" + PerStoryFeet + " ft per story above story " + PerStoryAboveStory;
                if (PerStoryMaxFeet > 0) s += " (max " + PerStoryMaxFeet + " ft)";
                parts.Add(s);
            }
            return string.Join(", ", parts);
        }
    }

    public class NarrowLotRule
    {
        public double WidthLessThanFeet { get; set; }
        public double Feet { get; set; }
        public double PercentOfLotWidth { get; set; }
        public double MinFeet { get; set; }
    }

    public class HeightRule
    {
        /// <summary>0 = no height limit (the envelope is drawn to DisplayCapFeet).</summary>
        public double MaxFeet { get; set; }
        /// <summary>Alternative limit when the roof is pitched (0 = same as MaxFeet).</summary>
        public double PitchedRoofMaxFeet { get; set; }
        /// <summary>0 = no story cap.</summary>
        public int MaxStories { get; set; }
        /// <summary>Assumed floor-to-floor for story slabs and floor-area estimates.</summary>
        public double StoryHeightFeet { get; set; } = 10;
        /// <summary>Height used for drawing when there is no limit.</summary>
        public double DisplayCapFeet { get; set; } = 150;

        [JsonIgnore] public bool IsUnlimited => MaxFeet <= 0 && PitchedRoofMaxFeet <= 0;

        public double Effective(bool pitchedRoof)
        {
            if (IsUnlimited) return DisplayCapFeet > 0 ? DisplayCapFeet : 150;
            if (pitchedRoof && PitchedRoofMaxFeet > 0) return PitchedRoofMaxFeet;
            return MaxFeet > 0 ? MaxFeet : PitchedRoofMaxFeet;
        }
    }

    public class FloorAreaRule
    {
        /// <summary>Allowed floor area = Ratio x lot area.</summary>
        public double Ratio { get; set; }
        public string Label { get; set; } = "FAR";
    }

    public class CoverageRule
    {
        public double MaxPercent { get; set; }
    }

    public class DensityRule
    {
        public double LotAreaPerUnitSqFt { get; set; }
        /// <summary>Optional cap: "or N units, whichever is less". 0 = no cap.</summary>
        public int MaxUnits { get; set; }

        public double MaxUnitsFor(double lotAreaSqFt)
        {
            if (LotAreaPerUnitSqFt <= 0) return 0;
            double n = Math.Floor(lotAreaSqFt / LotAreaPerUnitSqFt);
            if (MaxUnits > 0) n = Math.Min(n, MaxUnits);
            return n;
        }

        public string Describe()
        {
            string s = "1 unit per " + LotAreaPerUnitSqFt.ToString("#,0") + " sf of lot";
            if (MaxUnits > 0) s += ", max " + MaxUnits + " units";
            return s;
        }
    }

    public class StepbackRule
    {
        public List<string> Sides { get; set; } = new List<string>();
        /// <summary>Stories above this one (1-based) get the additional setback.</summary>
        public int AboveStory { get; set; } = 1;
        public double AdditionalFeet { get; set; }
        public string Label { get; set; }

        public bool AppliesTo(EdgeRole role) => RuleSides.Match(Sides, role);
    }

    public class PlaneRule
    {
        public List<string> Sides { get; set; } = new List<string>();
        public double StartHeightFeet { get; set; }
        public double AngleDegrees { get; set; } = 45;
        /// <summary>"setbackLine" (default) or "lotLine".</summary>
        public string From { get; set; } = "setbackLine";
        public string Label { get; set; }

        public bool AppliesTo(EdgeRole role) => RuleSides.Match(Sides, role);
        public bool FromLotLine => string.Equals(From, "lotLine", StringComparison.OrdinalIgnoreCase);
    }

    public class TransitionalHeightRule
    {
        /// <summary>Currently only "adjacentLowDensity": applies to edges flagged as abutting a low-density lot.</summary>
        public string Trigger { get; set; } = "adjacentLowDensity";
        public List<TransitionalStep> Steps { get; set; } = new List<TransitionalStep>();
        public string Label { get; set; }
    }

    public class TransitionalStep
    {
        public double UpToFeet { get; set; }
        public double MaxHeightFeet { get; set; }
    }

    public class AdjacentLowDensitySetbacks
    {
        public double SideFeet { get; set; }
        public double RearFeet { get; set; }
        public string Label { get; set; }
    }

    public class OpeningsRow
    {
        public double FromFeet { get; set; }
        /// <summary>-1 = open ended.</summary>
        public double ToFeet { get; set; }
        /// <summary>Percent of wall area; -1 = no limit, 0 = not permitted.</summary>
        public int UnprotectedNonsprinklered { get; set; }
        public int UnprotectedSprinklered { get; set; }
        public int Protected { get; set; }

        public bool Contains(double fsdFeet) => fsdFeet >= FromFeet && (ToFeet < 0 || fsdFeet < ToFeet);

        public static string Format(int pct)
        {
            if (pct < 0) return "No limit";
            if (pct == 0) return "Not permitted";
            return pct + "%";
        }
    }

    public enum EdgeRole { Front, Side, Rear }

    internal static class RuleSides
    {
        public static bool Match(List<string> sides, EdgeRole role)
        {
            if (sides == null) return false;
            foreach (var s in sides)
            {
                if (string.Equals(s, "all", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, role.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
