using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Every card in the slice exists as data, reads sensible odds, and does what its text says when it lands.</summary>
    public class CardTests
    {
        public static readonly string[] SliceStock =
        {
            "tuck_in", "bump_draft", "side_draft", "slingshot", "draft_train", "dive_bomb", "high_line", "bump_run", "crossover", "block", "clear_air",
            "save_tires", "push_limit", "hot_laps", "spotter_focus", "regroup", "restart_jump", "choose_lane", "fast_stop", "send_it", "stay_out",
            "evade", "reel_in", "tape_grille",
        };
        public static readonly string[] SliceKart =
        {
            "k_tow", "k_push", "k_pullout", "k_show", "k_switch", "k_outside", "k_owninside", "k_latebrake", "k_door", "k_cover", "k_smooth", "k_limit", "k_board",
        };

        public static IEnumerable<object[]> AllCards() => CardBook.All.Select(c => new object[] { c.Id });

        [Fact]
        public void EverySliceCardExistsAndNothingElse()
        {
            var ids = CardBook.All.Select(c => c.Id).OrderBy(x => x).ToArray();
            Assert.Equal(SliceStock.Concat(SliceKart).OrderBy(x => x).ToArray(), ids);
            Assert.Equal(37, ids.Length);
        }

        [Theory]
        [MemberData(nameof(AllCards))]
        public void CardIsCompleteData(string id)
        {
            var cd = CardBook.Get(id);
            Assert.False(string.IsNullOrWhiteSpace(cd.Name));
            Assert.False(string.IsNullOrWhiteSpace(cd.Text));
            Assert.False(string.IsNullOrWhiteSpace(cd.Real));
            Assert.NotNull(cd.Read);
            Assert.NotNull(cd.OnSuccess);
            Assert.NotNull(cd.OnCritSuccess);
            Assert.NotNull(cd.OnNeutral);
            Assert.Equal(cd.Kart, id.StartsWith("k_"));
            if (cd.Special) Assert.Equal(TossKind.None, cd.Toss);
            else Assert.NotEqual(TossKind.None, cd.Toss);
            if (cd.Kart) Assert.NotNull(cd.Next);
            if (cd.Next != null) foreach (var n in cd.Next) Assert.True(CardBook.Exists(n), n);
        }

        [Theory]
        [MemberData(nameof(AllCards))]
        public void OddsAreAProbabilityDistribution(string id)
        {
            var cd = CardBook.Get(id);
            var r = Scenario.Green(cd.Kart ? "hollow" : "carver");
            var o = r.ComputeOdds(cd, r.Ctx());
            Assert.Equal(1.0, o.P.Sum(), 9);
            Assert.All(o.P, p => Assert.True(p >= -1e-12));
            Assert.True(o.Succ <= r.Bal.OddsCap + 1e-9);
            if (!cd.CanWreck) Assert.Equal(0, o.Wreck);
            if (cd.NoFail) { Assert.Equal(0, o[Tier.Fail]); Assert.Equal(0, o[Tier.CritFail]); }
            Assert.InRange(o.Read, -100, 200);
        }

        // ---------------------------------------------------------------- what each card does

        sealed class Spec
        {
            public string Track = "carver";
            /// <summary>Play it on the grid before the green (restart cards).</summary>
            public bool Start;
            public Tier Tier = Tier.Success;
            public Action<Race> Setup;
            public Action<Race, Snap> Check;
        }

        sealed class Snap
        {
            public RaceCar Ahead, Behind;
            public int Mom, Hand, Heat, Tow, Fair;
            public double Tires, Damage;
            public int Wrecks;
        }

        static bool HasPace(RaceCar c, double m) => c.Effects.Exists(e => e.Kind == EffectKind.Pace && Math.Abs(e.Mult - m) < 1e-9);
        static bool HasWear(RaceCar c, double m) => c.Effects.Exists(e => e.Kind == EffectKind.Wear && Math.Abs(e.Mult - m) < 1e-9);
        static bool Slowed(RaceCar c) => c.Effects.Exists(e => e.Yield);
        static bool Passing(Race r, int n) => r.Me.Overtake != null && r.Me.Overtake.Left == n - 1;
        static double BlockLeft(RaceCar c, Race r) => c.Effects.Where(e => e.Kind == EffectKind.Block).Select(e => e.Until - r.T).DefaultIfEmpty(0).Max();

        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            ["tuck_in"] = new Spec { Check = (r, s) => { Assert.True(HasPace(r.Me, 1.03)); Assert.Equal(s.Mom + 1, r.Momentum); } },
            ["bump_draft"] = new Spec { Check = (r, s) => { Assert.True(HasPace(r.Me, 1.05)); Assert.True(HasPace(s.Ahead, 1.03)); TireCost(r, s, 0); } },
            ["side_draft"] = new Spec { Check = (r, s) => { Assert.True(Slowed(s.Ahead)); Assert.True(HasPace(r.Me, 1.02)); } },
            ["slingshot"] = new Spec { Setup = r => r.Momentum = 2, Check = (r, s) => { Assert.True(Passing(r, 1)); Assert.Equal(0, r.Momentum); } },
            ["draft_train"] = new Spec { Check = (r, s) => Assert.True(Passing(r, 2)) },
            ["dive_bomb"] = new Spec { Tier = Tier.Fail, Check = (r, s) => { Assert.True(r.Me.Damage > s.Damage); Assert.True(r.Me.Zones.Side > 0); TireCost(r, s, 3); Assert.Equal(s.Heat + 1, r.Heat); } },
            ["high_line"] = new Spec { Check = (r, s) => Assert.True(Passing(r, 1)) },
            ["bump_run"] = new Spec { Check = (r, s) => { Assert.True(Passing(r, 1)); Assert.True(Slowed(s.Ahead)); } },
            ["crossover"] = new Spec { Setup = r => { r.LostTo = r.AheadOf(r.Me); r.LostAt = r.T; }, Check = (r, s) => Assert.True(Passing(r, 1)) },
            ["block"] = new Spec { Setup = Scenario.CarBehind, Check = (r, s) => Assert.InRange(BlockLeft(r.Me, r), 19, 20) },
            ["clear_air"] = new Spec { Setup = r => { var a = r.AheadOf(r.Me); a.Dist += 0.1; }, Check = (r, s) => Assert.True(HasPace(r.Me, 1.025)) },
            ["save_tires"] = new Spec { Check = (r, s) => { Assert.True(HasWear(r.Me, 0.5)); Assert.True(HasPace(r.Me, 0.99)); } },
            ["push_limit"] = new Spec { Tier = Tier.Fail, Check = (r, s) => { TireCost(r, s, 5); Assert.True(r.Me.Damage > s.Damage); Assert.True(Slowed(r.Me)); } },
            ["hot_laps"] = new Spec { Check = (r, s) => { Assert.True(HasPace(r.Me, 1.04)); Assert.True(r.Hand.Count >= s.Hand); } },
            ["spotter_focus"] = new Spec { Check = (r, s) => Assert.Equal(15, r.Buffs.Single().V) },
            ["regroup"] = new Spec { Setup = r => r.Momentum = 1, Check = (r, s) => { Assert.True(r.Hand.Count >= s.Hand + 1); Assert.Equal(0, r.Momentum); } },
            ["restart_jump"] = new Spec { Start = true, Check = (r, s) => Assert.True(Passing(r, 2)) },
            ["choose_lane"] = new Spec { Setup = Scenario.ToCaution, Check = (r, s) => { Assert.Equal(20, r.RestartBuff); Assert.Equal(1, r.StartGain); } },
            ["fast_stop"] = new Spec { Setup = Scenario.ToCaution, Check = (r, s) => Assert.Equal(2, r.PitBonus) },
            ["send_it"] = new Spec { Tier = Tier.CritFail, Check = (r, s) => { Assert.Equal(s.Wrecks + 1, r.Wrecks); Assert.True(r.Me.Damage > s.Damage); Assert.Equal(s.Heat + 2, r.Heat); } },
            ["stay_out"] = new Spec { Setup = Scenario.ToCaution, Check = (r, s) => { Assert.Equal(PitService.Stay, r.Caution.Mine); Assert.Equal(1, r.StartGain); Assert.Equal(10, r.RestartBuff); } },
            ["evade"] = new Spec { Check = (r, s) => Assert.True(HasPace(r.Me, 1.02)) },
            ["reel_in"] = new Spec { Check = (r, s) => Assert.True(HasPace(r.Me, 1.10)) },
            ["tape_grille"] = new Spec { Tier = Tier.CritFail, Check = (r, s) => { Assert.True(r.Me.Damage >= s.Damage + 20); Assert.True(r.Me.Zones.Nose > 0); } },
            ["k_tow"] = new Spec { Track = "hollow", Check = (r, s) => { Assert.Equal(s.Tow + 1, r.Tow); Assert.True(HasPace(r.Me, 1.03)); } },
            ["k_push"] = new Spec { Track = "hollow", Tier = Tier.Neutral, Check = (r, s) => { Assert.Equal(s.Fair + 1, r.Fairing); Assert.True(HasPace(r.Me, 1.01)); } },
            ["k_pullout"] = new Spec { Track = "hollow", Setup = r => r.Tow = 2, Check = (r, s) => { Assert.Equal(1, r.Tow); Assert.True(Passing(r, 1)); } },
            ["k_show"] = new Spec { Track = "hollow", Check = (r, s) => Assert.True(r.Covering(s.Ahead)) },
            ["k_switch"] = new Spec { Track = "hollow", Check = (r, s) => Assert.True(Passing(r, 1)) },
            ["k_outside"] = new Spec { Track = "hollow", Check = (r, s) => { Assert.True(Passing(r, 1)); Assert.True(r.OutsideFresh); } },
            ["k_owninside"] = new Spec { Track = "hollow", Setup = r => r.KOutAt = r.T, Check = (r, s) => { Assert.True(Passing(r, 1)); Assert.False(r.OutsideFresh); } },
            ["k_latebrake"] = new Spec { Track = "hollow", Tier = Tier.CritSuccess, Setup = r => r.Tow = 3, Check = (r, s) => { Assert.Equal(0, r.Tow); Assert.True(Passing(r, 2)); } },
            ["k_door"] = new Spec { Track = "hollow", Tier = Tier.Fail, Check = (r, s) => { Assert.Equal(s.Fair + 2, r.Fairing); Assert.True(r.Me.Damage > s.Damage); } },
            ["k_cover"] = new Spec { Track = "hollow", Setup = Scenario.CarBehind, Check = (r, s) => Assert.InRange(BlockLeft(r.Me, r), 17, 18) },
            ["k_smooth"] = new Spec { Track = "hollow", Check = (r, s) => { Assert.True(HasPace(r.Me, 1.03)); Assert.True(HasWear(r.Me, 0.6)); } },
            ["k_limit"] = new Spec { Track = "hollow", Check = (r, s) => Assert.True(HasPace(r.Me, 1.05)) },
            ["k_board"] = new Spec { Track = "hollow", Check = (r, s) => Assert.Equal(18, r.Buffs.Single().V) },
        };

        [Fact]
        public void EveryCardHasAnOutcomeTest() => Assert.Equal(CardBook.All.Select(c => c.Id).OrderBy(x => x), Specs.Keys.OrderBy(x => x));

        [Theory]
        [MemberData(nameof(AllCards))]
        public void CardDoesWhatItSays(string id)
        {
            var sp = Specs[id];
            var r = sp.Start ? Scenario.Make(sp.Track) : Scenario.Green(sp.Track);
            if (sp.Start) { r.Tick(); r.Hand.Clear(); }
            sp.Setup?.Invoke(r);
            var snap = new Snap
            {
                Ahead = r.AheadOf(r.Me), Behind = r.BehindOf(r.Me), Mom = r.Momentum, Heat = r.Heat, Tow = r.Tow, Fair = r.Fairing,
                Tires = r.Me.Tires, Damage = r.Me.Damage, Wrecks = r.Wrecks,
            };
            var h = r.GiveCard(id);
            Assert.Null(r.CardBlock(h));
            r.ForceNextTier = sp.Tier;
            Assert.Null(r.PlayCard(h.Uid));
            snap.Hand = r.Hand.Count;
            if (CardBook.Get(id).MomCost > 0) snap.Mom = r.Momentum;
            var tires = r.Me.Tires;
            int guard = 0;
            while (r.Attempt != null && guard++ < 200) r.Tick();
            Assert.Null(r.Attempt);
            var res = r.Log.Last(e => e.Kind == RaceEventKind.CardResolved || e.Kind == RaceEventKind.CardReturned);
            Assert.Equal(RaceEventKind.CardResolved, res.Kind);
            Assert.Equal(id, res.Card);
            Assert.Contains(CardRules.TierName(sp.Tier), res.Text);
            snap.Tires = tires;
            sp.Check(r, snap);
        }

        /// <summary>Tires lost since the card was played, less up to 1.5 for normal wear during its setup seconds.</summary>
        static void TireCost(Race r, Snap s, double cost) => Assert.InRange(s.Tires - r.Me.Tires, cost, cost + 1.5);
    }

    /// <summary>Races set up for card tests: green flag, your car mid-pack with cars close ahead and behind.</summary>
    public static class Scenario
    {
        public static PlayerLoadout Plain(bool kart) => new PlayerLoadout { Deck = new List<string>(kart ? CardBook.KartBase : CardBook.StockBase) };

        public static Race Make(string track, PlayerLoadout l = null, uint seed = 61, PitPolicy pit = PitPolicy.Bot)
        {
            var t = Tracks.ById(track);
            var s = RaceSetup.Quick(t, seed, l ?? Plain(t.Kart));
            s.PitPolicy = pit;
            return new Race(s) { Log = new List<RaceEvent>() };
        }

        public static Race Green(string track, PlayerLoadout l = null, uint seed = 61)
        {
            var r = Make(track, l, seed);
            while (r.Phase != RacePhase.Green) r.Tick();
            r.Tick();
            r.Hand.Clear();
            return r;
        }

        /// <summary>Drop the car ahead in behind you (your car starts at the back on some seeds).</summary>
        public static void CarBehind(Race r)
        {
            if (r.BehindOf(r.Me) != null) return;
            var a = r.AheadOf(r.Me);
            a.Dist = r.Me.Dist - r.MinGap * 1.3;
            int i = r.Order.IndexOf(a);
            r.Order[i] = r.Me; r.Order[i + 1] = a;
        }

        public static void ToCaution(Race r)
        {
            r.StartCaution("test");
            r.Tick();
        }
    }
}
