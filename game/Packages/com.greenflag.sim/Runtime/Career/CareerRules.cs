using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>The slice's Information dial. The engine race always shows full odds for now; the dial only sets the purse bonus.</summary>
    public enum InfoLevel { Full, Reveal, Feel }

    /// <summary>The slice's Stakes dial: what a DNF costs and whether a race can be retried.</summary>
    public enum Stakes { Standard, Ironman, Permadeath }

    /// <summary>How a race went, for rewards: worst first.</summary>
    public enum RewardTier { Rough, Solid, Strong, Victory }

    /// <summary>A deck size range for a kind of car (the slice's CAR_TYPES).</summary>
    public readonly struct CarType
    {
        public readonly string Name;
        public readonly int Min, Max;
        public CarType(string name, int min, int max) { Name = name; Min = min; Max = max; }
    }

    /// <summary>A card family group with a slot cap that the tune moves (the slice's SLOT).</summary>
    public sealed class DeckSlot
    {
        public string Id, Name;
        public CardFamily[] Families;
        public int Base;
        public bool Has(CardFamily f) => Array.IndexOf(Families, f) >= 0;
    }

    /// <summary>A shop upgrade: a car or driver stat that goes up one level for money (the slice's SHOP and STAT_INFO).</summary>
    public sealed class ShopStat
    {
        public string Id, Name, Text, Group;
        public Func<int, int> Cost;
    }

    /// <summary>
    /// The numbers behind the reward screen, the garage shop and the race deck builder (roadmap step 34), as in slice v47:
    /// purse and bonus, reward tiers, reroll prices, shop prices, deck sizes, slot caps and each track's family minimums.
    /// </summary>
    public static class CareerRules
    {
        public const int StartMoney = 6000;
        /// <summary>The parts dealer's one part.</summary>
        public const int PartPrice = 7000;
        /// <summary>Skipping the reward card pays this.</summary>
        public const int SkipCash = 1000;
        /// <summary>Ironman (and a Survive token on Permadeath): a DNF costs this and a random spare part.</summary>
        public const int DnfFine = 3000;
        public const int StatMax = 10;

        /// <summary>Race pay and sponsor money by series (step 23's ECON).</summary>
        public static double Econ(Series s) => s == Series.Kart ? 0.15 : s == Series.Late ? 0.3 : 0.4;

        public static double InfoBonus(InfoLevel i) => i == InfoLevel.Feel ? 0.25 : i == InfoLevel.Reveal ? 0.1 : 0;
        public static double StakesBonus(Stakes s) => s == Stakes.Permadeath ? 0.2 : s == Stakes.Ironman ? 0.1 : 0;

        /// <summary>The first reroll's price on each AI skill; every reroll after it costs $1,000 more.</summary>
        public static int RerollBase(DifficultyLevel d) => d == DifficultyLevel.Easy ? 1000 : d == DifficultyLevel.Hard ? 2000 : d == DifficultyLevel.Brutal ? 2500 : 1500;
        public static int RerollCost(DifficultyLevel d, int rerollsSoFar) => RerollBase(d) + rerollsSoFar * 1000;

        /// <summary>Everything that scales race pay: AI skill, Information, Stakes and the series.</summary>
        public static double PayMul(Difficulty d, InfoLevel info, Stakes stakes, Series s) => d.Purse * (1 + InfoBonus(info) + StakesBonus(stakes)) * Econ(s);

        public static int Purse(int finish, double mul) => (int)SimMath.RoundJs((1500 + (21 - finish) * 350) * mul);
        public static int Bonus(int finish, int expected, double mul) => (int)SimMath.RoundJs(Math.Max(0, expected - finish) * 400 * mul);

        /// <summary>A DNF or finishing 4+ spots behind expected is rough; a win, or a podium 8+ spots ahead, is victory lane; 4+ ahead is strong.</summary>
        public static RewardTier TierFor(int finish, int expected, bool dnf)
        {
            int diff = expected - finish;
            if (dnf || finish > expected + 3) return RewardTier.Rough;
            if (finish == 1 || (finish <= 3 && diff >= 8)) return RewardTier.Victory;
            return diff >= 4 ? RewardTier.Strong : RewardTier.Solid;
        }

        public static string TierName(RewardTier t) => t == RewardTier.Rough ? "Rough day" : t == RewardTier.Solid ? "Solid run" : t == RewardTier.Strong ? "Strong run" : "Victory lane";

        /// <summary>Strong runs and victories also offer a part.</summary>
        public static bool PartPick(RewardTier t) => t >= RewardTier.Strong;

        public static CarType CarTypeOf(Series s) => s == Series.Kart ? new CarType("Kart", 10, 13) : s == Series.Late ? new CarType("Late model", 11, 15) : new CarType("Stock car", 12, 16);

        public static readonly DeckSlot Attack = new DeckSlot { Id = "attack", Name = "Attack", Families = new[] { CardFamily.Pass, CardFamily.Risk }, Base = 6 };
        public static readonly DeckSlot Defense = new DeckSlot { Id = "defense", Name = "Defense", Families = new[] { CardFamily.Defend, CardFamily.Car }, Base = 6 };
        public static readonly DeckSlot Drafting = new DeckSlot { Id = "draft", Name = "Drafting", Families = new[] { CardFamily.Draft }, Base = 5 };
        public static readonly IReadOnlyList<DeckSlot> Slots = new[] { Attack, Defense, Drafting };

        public static DeckSlot SlotOf(CardFamily f)
        {
            foreach (var s in Slots) if (s.Has(f)) return s;
            return null;
        }

        /// <summary>The family minimums each track puts on the race deck.</summary>
        static readonly Dictionary<string, (CardFamily f, int n)[]> trackMin = new Dictionary<string, (CardFamily, int)[]>
        {
            ["mill"] = new[] { (CardFamily.Defend, 2) },
            ["carver"] = new[] { (CardFamily.Car, 2) },
            ["talon"] = new[] { (CardFamily.Draft, 3) },
            ["pine"] = new[] { (CardFamily.Pass, 2) },
            ["ridge"] = new[] { (CardFamily.Car, 2) },
            ["pinek"] = new[] { (CardFamily.Pass, 2) },
            ["hollow"] = new[] { (CardFamily.Draft, 1) },
            ["bramble"] = new[] { (CardFamily.Pass, 3) },
            ["lake"] = new[] { (CardFamily.Draft, 2) },
            ["cedar"] = new[] { (CardFamily.Defend, 2) },
            ["milll"] = new[] { (CardFamily.Defend, 2) },
            ["cross"] = new[] { (CardFamily.Car, 1) },
            ["gran"] = new[] { (CardFamily.Car, 2) },
            ["ridgel"] = new[] { (CardFamily.Car, 2) },
        };

        public static IReadOnlyList<(CardFamily f, int n)> TrackMin(TrackDef t) => trackMin.TryGetValue(t.Id, out var m) ? m : Array.Empty<(CardFamily, int)>();

        public static int TrackMinFor(TrackDef t, CardFamily f)
        {
            foreach (var (ff, n) in TrackMin(t)) if (ff == f) return n;
            return 0;
        }

        /// <summary>The season calendar for a series: karts 6 races, late models 8, stock cars 10 on mixed shapes (board Q35, Q82).
        /// Karts (step 40) visit all four kart circuits: Pinewood opens, the two new ones run twice, the finale is at Lakeshore.
        /// Late models (step 41) are mostly short ovals (Cedar Bowl, Millbrook, Granite Falls twice each) plus the figure-8 and the
        /// Ridgeback road course; the finale is the tire race at Granite Falls.</summary>
        public static IReadOnlyList<string> Calendar(Series s) =>
            s == Series.Kart ? new[] { "pinek", "bramble", "hollow", "lake", "bramble", "lake" }
            : s == Series.Late ? new[] { "cedar", "gran", "milll", "cross", "cedar", "ridgel", "milll", "gran" }
            : new[] { "mill", "pine", "carver", "ridge", "talon", "mill", "pine", "carver", "ridge", "mill" };

        public static readonly IReadOnlyList<ShopStat> Shop = new[]
        {
            new ShopStat { Id = "power", Group = "Car", Name = "Power", Text = "Straight-line pace.", Cost = CarCost },
            new ShopStat { Id = "handling", Group = "Car", Name = "Handling", Text = "Corner pace (matters most on short tracks) and corner card reads.", Cost = CarCost },
            new ShopStat { Id = "aero", Group = "Car", Name = "Aero", Text = "How much the draft pulls you.", Cost = CarCost },
            new ShopStat { Id = "durability", Group = "Car", Name = "Durability", Text = "Less damage from wrecks and contact.", Cost = CarCost },
            new ShopStat { Id = "racecraft", Group = "Driver coaching", Name = "Racecraft", Text = "+2 read per point above 5 on Passing and Drafting cards; better auto moves.", Cost = CoachCost },
            new ShopStat { Id = "defense", Group = "Driver coaching", Name = "Defense", Text = "+2.5 read per point above 5 on Defending cards; harder to pass.", Cost = CoachCost },
            new ShopStat { Id = "consistency", Group = "Driver coaching", Name = "Consistency", Text = "Narrower outcome spread on every card and move.", Cost = CoachCost },
            new ShopStat { Id = "tireMgmt", Group = "Driver coaching", Name = "Tire management", Text = "Slower tire wear.", Cost = CoachCost },
        };

        static int CarCost(int level) => 3500 + 1500 * Math.Max(0, level - 3);
        static int CoachCost(int level) => 3000 + 1200 * Math.Max(0, level - 4);

        public static ShopStat ShopStatOf(string id)
        {
            foreach (var s in Shop) if (s.Id == id) return s;
            throw new KeyNotFoundException("No shop stat " + id);
        }

        /// <summary>A car or driver stat by the slice's name.</summary>
        public static int Stat(CarStats c, string id)
        {
            switch (id)
            {
                case "power": return c.Power;
                case "handling": return c.Handling;
                case "aero": return c.Aero;
                case "durability": return c.Durability;
                case "racecraft": return c.Racecraft;
                case "defense": return c.Defense;
                case "consistency": return c.Consistency;
                case "tireMgmt": return c.TireMgmt;
                default: throw new ArgumentException("Unknown stat " + id);
            }
        }

        public static string SlotName(PartSlot s) => s == PartSlot.Engine ? "engine" : s == PartSlot.Aero ? "aero" : s == PartSlot.Chassis ? "chassis" : s == PartSlot.Tires ? "tires" : "crew";
    }
}
