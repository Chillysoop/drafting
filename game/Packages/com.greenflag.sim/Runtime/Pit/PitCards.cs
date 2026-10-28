using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum PitLane { Tire, Fuel, Any, Adjust }

    /// <summary>
    /// A pit card (step 6b): a speed card for one crew lane with odds and a time change per outcome, Fix the Damage, or an
    /// adjustment that installs a Setup. A "+" card is the Crew HQ upgrade (Q23): +10 odds, bigger gains, smaller misses.
    /// </summary>
    public sealed class PitCardDef
    {
        public string Id, Name, Text, Miss;
        public PitLane Lane;
        /// <summary>Base odds (0 = no roll: Fix and adjustments).</summary>
        public int Base;
        /// <summary>A safe card never makes the lane slower.</summary>
        public bool Safe;
        /// <summary>Seconds added to the lane by outcome (critical success, success, neutral, failure).</summary>
        public double DCs, DS, DN, DF;
        /// <summary>Fix the Damage: seconds in the adjust lane and damage fixed.</summary>
        public double FixTime, Fix;
        /// <summary>The Setup an adjustment installs.</summary>
        public string Setup;
        /// <summary>The upgraded version (a Setup installs at Level II).</summary>
        public bool Up;

        public double Delta(Tier t) => t == Tier.CritSuccess ? DCs : t == Tier.Success ? DS : t == Tier.Neutral ? DN : t == Tier.Fail ? DF : 0;
        public bool Rolls => Base > 0;
        public bool IsFix => Fix > 0;
    }

    /// <summary>A Setup: an ongoing bonus from a pit adjustment, Level I or II. Up to three at once; pairs make Packages.</summary>
    public sealed class SetupDef
    {
        public string Id, Name, Short;
        /// <summary>The card family it boosts (and draws more of), or null for Air Pressure.</summary>
        public CardFamily? Family;
        public int[] Odds;
        public double[] Wear;

        public string Text(int lvl) => Family != null
            ? $"{CardRules.FamilyName(Family.Value)} cards +{Odds[lvl - 1]}, drawn more"
            : $"Tire wear -{(int)SimMath.RoundJs((1 - Wear[lvl - 1]) * 100)}%";
    }

    public sealed class PackageDef
    {
        public string Id, Name, Text;
        public string[] Of;
        public double Wear = 1;
        public Dictionary<CardFamily, int> Odds = new Dictionary<CardFamily, int>();
    }

    public static class PitCards
    {
        static readonly Dictionary<string, PitCardDef> byId = new Dictionary<string, PitCardDef>();
        static readonly List<PitCardDef> all = new List<PitCardDef>();
        public static IReadOnlyList<PitCardDef> All => all;

        public static readonly IReadOnlyList<string> StartingDeck = new[]
        {
            "quick_jack", "quick_jack", "air_gun", "air_gun", "lug_gamble", "fast_fill", "catch_can", "utility", "fix",
            "adj_wedge", "adj_wedge", "adj_trackbar", "adj_air", "adj_spring", "adj_spring", "adj_tape",
        };

        /// <summary>Crew HQ shop cards (step 19).</summary>
        public static readonly IReadOnlyList<string> Shop = new[] { "lightning_lugs", "gas_man", "swing_man" };

        public static readonly IReadOnlyDictionary<string, SetupDef> Setups = new Dictionary<string, SetupDef>
        {
            ["wedge"] = new SetupDef { Id = "wedge", Name = "Round of Wedge", Short = "Wedge", Family = CardFamily.Draft, Odds = new[] { 6, 12 } },
            ["spring"] = new SetupDef { Id = "spring", Name = "Spring Rubber", Short = "Springs", Family = CardFamily.Pass, Odds = new[] { 5, 10 } },
            ["trackbar"] = new SetupDef { Id = "trackbar", Name = "Track Bar Up", Short = "Track bar", Family = CardFamily.Defend, Odds = new[] { 6, 12 } },
            ["tape"] = new SetupDef { Id = "tape", Name = "Tape the Grille", Short = "Tape", Family = CardFamily.Restart, Odds = new[] { 8, 15 } },
            ["air"] = new SetupDef { Id = "air", Name = "Air Pressure", Short = "Air", Wear = new[] { 0.85, 0.72 } },
        };

        public static readonly IReadOnlyList<PackageDef> Packages = new[]
        {
            new PackageDef { Id = "longrun", Name = "Long-Run Package", Of = new[] { "wedge", "air" }, Wear = 0.88, Odds = { [CardFamily.Draft] = 4 }, Text = "Tire wear -12% more, Drafting +4" },
            new PackageDef { Id = "restart", Name = "Restart Package", Of = new[] { "trackbar", "spring" }, Odds = { [CardFamily.Restart] = 10, [CardFamily.Defend] = 4 }, Text = "Restart cards +10, Defending +4" },
            new PackageDef { Id = "shortrun", Name = "Short-Run Package", Of = new[] { "tape", "spring" }, Odds = { [CardFamily.Pass] = 4, [CardFamily.Restart] = 4 }, Text = "Passing and Restart cards +4" },
        };

        static PitCardDef Speed(string id, string n, PitLane lane, int b, double cs, double s, double ne, double f, string txt, string miss, bool safe = false) =>
            new PitCardDef { Id = id, Name = n, Lane = lane, Base = b, DCs = cs, DS = s, DN = ne, DF = f, Text = txt, Miss = miss, Safe = safe };

        static PitCards()
        {
            var bases = new List<PitCardDef>
            {
                Speed("quick_jack", "Quick Jack", PitLane.Tire, 62, -1.5, -1, 0, 1.5, "Tires -1 s", "miss +1.5 s"),
                Speed("air_gun", "Air Gun Ace", PitLane.Tire, 80, -1, -0.6, -0.2, 0, "Tires -0.6 s", "never slower", true),
                Speed("lug_gamble", "Four-Lug Gamble", PitLane.Tire, 45, -2.5, -2, 0, 2.5, "Tires -2 s", "miss +2.5 s"),
                Speed("fast_fill", "Fast Fill", PitLane.Fuel, 62, -1.5, -1, 0, 1.5, "Fuel -1 s", "miss +1.5 s"),
                Speed("catch_can", "Catch Can Pro", PitLane.Fuel, 80, -0.8, -0.5, -0.2, 0, "Fuel -0.5 s", "never slower", true),
                Speed("utility", "Utility Man", PitLane.Any, 70, -1.2, -0.8, 0, 1, "Slowest lane -0.8 s", "miss +1 s"),
                new PitCardDef { Id = "fix", Name = "Fix the Damage", Lane = PitLane.Adjust, FixTime = 4, Fix = 20, Text = "+4 s · fixes 20, worst spot first" },
                new PitCardDef { Id = "adj_wedge", Name = "Round of Wedge", Lane = PitLane.Adjust, Setup = "wedge" },
                new PitCardDef { Id = "adj_trackbar", Name = "Track Bar Up", Lane = PitLane.Adjust, Setup = "trackbar" },
                new PitCardDef { Id = "adj_air", Name = "Air Pressure", Lane = PitLane.Adjust, Setup = "air" },
                new PitCardDef { Id = "adj_spring", Name = "Spring Rubber", Lane = PitLane.Adjust, Setup = "spring" },
                new PitCardDef { Id = "adj_tape", Name = "Tape the Grille", Lane = PitLane.Adjust, Setup = "tape" },
                Speed("lightning_lugs", "Lightning Lugs", PitLane.Tire, 58, -3, -2.2, 0, 2, "Tires -2.2 s", "miss +2 s"),
                Speed("gas_man", "Gas Man Pro", PitLane.Fuel, 78, -1.4, -1, -0.3, 0, "Fuel -1 s", "never slower", true),
                Speed("swing_man", "Swing Man", PitLane.Any, 80, -1, -0.7, -0.2, 0, "Slowest lane -0.7 s", "never slower", true),
            };
            foreach (var d in bases) Add(d);
            foreach (var d in bases) Add(Upgrade(d));
        }

        /// <summary>The slice's "+" card: +10 odds (max 95), gains x1.25 and misses x0.75 (rounded to 0.01 s); Fix 3 s for 30.</summary>
        static PitCardDef Upgrade(PitCardDef d)
        {
            var u = (PitCardDef)MemberwiseCloneOf(d);
            u.Id = d.Id + "+"; u.Name = d.Name + "+"; u.Up = true;
            if (d.Rolls)
            {
                u.Base = Math.Min(95, d.Base + 10);
                double Sc(double v) => Math.Round(v * (v < 0 ? 1.25 : 0.75), 2, MidpointRounding.AwayFromZero);
                u.DCs = Sc(d.DCs); u.DS = Sc(d.DS); u.DN = Sc(d.DN); u.DF = Sc(d.DF);
                u.Text = d.Text.Substring(0, d.Text.LastIndexOf('-')) + "-" + Fmt(-u.DS) + " s";
                u.Miss = d.Safe ? d.Miss : "miss +" + Fmt(u.DF) + " s";
            }
            else if (d.Setup != null) u.Text = "Installs at Level II";
            else if (d.IsFix) { u.FixTime = 3; u.Fix = 30; u.Text = "+3 s · fixes 30, worst spot first"; }
            return u;
        }

        static string Fmt(double v) => (Math.Round(Math.Abs(v) * 10) / 10).ToString(System.Globalization.CultureInfo.InvariantCulture);

        static object MemberwiseCloneOf(PitCardDef d) => new PitCardDef
        {
            Id = d.Id, Name = d.Name, Text = d.Text, Miss = d.Miss, Lane = d.Lane, Base = d.Base, Safe = d.Safe,
            DCs = d.DCs, DS = d.DS, DN = d.DN, DF = d.DF, FixTime = d.FixTime, Fix = d.Fix, Setup = d.Setup, Up = d.Up,
        };

        static void Add(PitCardDef d) { byId[d.Id] = d; all.Add(d); }

        public static PitCardDef Get(string id)
        {
            if (byId.TryGetValue(id, out var d)) return d;
            throw new KeyNotFoundException("No pit card with id " + id);
        }
    }
}
