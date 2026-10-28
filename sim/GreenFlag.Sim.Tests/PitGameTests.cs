using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>The pit stop mini-game: pit hand, lanes, GO, the release window, loose wheels, Perfect Stops and Setups.</summary>
    public class PitGameTests
    {
        /// <summary>Ask for a stop and run until your car is in the box with the pit game open.</summary>
        static Race ToStall(PitPolicy pol, PlayerLoadout l = null, uint seed = 61) => TryStall(pol, l, seed) ?? throw new Exception("never reached the stall");

        static Race TryStall(PitPolicy pol, PlayerLoadout l = null, uint seed = 61)
        {
            var r = Scenario.Make("carver", l, seed, pol);
            while (r.Phase != RacePhase.Green) r.Tick();
            for (int i = 0; i < 20 * 30; i++) r.Tick();
            r.PressPit();
            int g = 0;
            while (r.PitGame == null && !r.Done && g++ < 20 * 300) r.Tick();
            return r.PitGame != null ? r : null;
        }

        [Fact]
        public void PitDeckDefinitionsAreComplete()
        {
            foreach (var id in PitCards.StartingDeck.Concat(PitCards.Shop))
            {
                var d = PitCards.Get(id);
                Assert.False(string.IsNullOrEmpty(d.Name), id);
                Assert.True(d.Rolls || d.IsFix || d.Setup != null, id);
                if (d.Setup != null) Assert.True(PitCards.Setups.ContainsKey(d.Setup), id);
                Assert.Contains(PitCards.All, x => x.Id == id + "+");
                if (d.Rolls)
                {
                    var p = new Race(RaceSetup.Quick(Tracks.ById("carver"), 61, Scenario.Plain(false))).PitOdds(d);
                    Assert.Equal(1, p.Sum(), 9);
                    Assert.True(d.DCs <= d.DS && d.DS <= d.DN, id);
                    if (d.Safe) Assert.True(d.DF <= 0, id);
                }
            }
        }

        [Fact]
        public void BotStopRunsItselfAndReleases()
        {
            var r = ToStall(PitPolicy.Bot);
            Assert.Equal(PitGamePhase.Work, r.PitGame.Phase);
            Assert.False(r.Holding);
            Assert.True(r.PitGame.Hand.Count >= r.Bal.PgHand);
            while (r.Me.Pit != null) r.Tick();
            Assert.NotNull(r.LastPitGame);
            Assert.Equal(PitGamePhase.Done, r.LastPitGame.Phase);
            Assert.Single(r.CardStats.StopTimes);
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.PitGameRelease);
        }

        [Fact]
        public void PlayerStopHoldsTheRaceUntilGo()
        {
            var r = ToStall(PitPolicy.Player);
            Assert.True(r.Holding);
            double t = r.T;
            for (int i = 0; i < 100; i++) r.Tick();
            Assert.Equal(t, r.T);
            int k = r.PitGame.Hand.FindIndex(h => h.Def.Rolls);
            Assert.Null(r.PitToggle(k));
            Assert.True(r.PitGame.Hand[k].On);
            r.PitGo();
            Assert.False(r.Holding);
            Assert.Equal(PitGamePhase.Work, r.PitGame.Phase);
            Assert.All(r.PitGame.Hand.Where(h => h.On && h.Def.Rolls), h => Assert.NotNull(h.Roll));
        }

        [Fact]
        public void EarlyReleaseIsRefusedAndLateOneIsTakenForYou()
        {
            var r = ToStall(PitPolicy.Player);
            r.PitGo();
            Assert.Equal("The crew is still working", r.PitRelease());
            // never pressing release: the crew chief drops the jack ChiefLate seconds after the window closes
            var pg = r.PitGame;
            while (r.Me.Pit != null && r.Me.Pit.Stage == PitStage.Box) r.Tick();
            Assert.True(pg.ChiefRelease);
            Assert.False(pg.Perfect);
            Assert.InRange(pg.StopSeconds, pg.Total + pg.Window + r.Bal.ChiefLate - 0.06, pg.Total + pg.Window + r.Bal.ChiefLate + 0.06);
        }

        [Fact]
        public void ReleaseInTheWindowWithACleanPlanIsAPerfectStop()
        {
            foreach (uint seed in Enumerable.Range(61, 20).Select(x => (uint)x))
            {
                var r = TryStall(PitPolicy.Player, null, seed);
                if (r == null) continue;
                var pg = r.PitGame;
                r.PitAutoPlan(pg, false);
                if (pg.Hand.Count(h => h.On) < 2) continue;
                r.PitGo();
                if (pg.Hand.Any(h => h.On && h.Roll == Tier.Fail)) continue;
                while (r.T - pg.T0 < pg.Total + 0.01) r.Tick();
                double focus0 = r.FocusMeter; int mom0 = r.Momentum;
                Assert.Null(r.PitRelease());
                Assert.True(pg.Perfect);
                Assert.Equal(1, r.FocusMeter);
                Assert.Equal(Math.Min(5, mom0 + 1), r.Momentum);
                Assert.Contains(r.Log, e => e.Kind == RaceEventKind.PerfectStop);
                return;
            }
            Assert.Fail("no seed gave a clean two-card plan");
        }

        [Fact]
        public void ReleasingJustEarlyCanLooseAWheel()
        {
            int loose = 0, tries = 0;
            foreach (uint seed in Enumerable.Range(61, 30).Select(x => (uint)x))
            {
                var r = TryStall(PitPolicy.Player, null, seed);
                if (r == null) continue; // a caution got in the way
                var pg = r.PitGame;
                r.PitGo();
                while (r.T - pg.T0 < pg.Total - 0.9) r.Tick();
                tries++;
                if (r.PitRelease() == "Loose wheel") { loose++; Assert.Equal(1, pg.Loose); Assert.Equal(PitGamePhase.Work, pg.Phase); }
            }
            // risk is about 0.9 s x 0.6 = 54% with tires
            Assert.InRange(loose, tries / 5, tries * 4 / 5);
        }

        [Fact]
        public void AutoPlanOnlyKeepsCardsThatHelp()
        {
            var r = ToStall(PitPolicy.Player);
            var pg = r.PitGame;
            double none = r.PitLanesOf(pg, false).Total;
            r.PitAutoPlan(pg, false);
            Assert.True(r.PitLanesOf(pg, false).Total <= none);
            Assert.DoesNotContain(pg.Hand, h => h.On && h.Def.Setup != null);
        }

        [Fact]
        public void AdjustmentsCapAtThree()
        {
            var l = Scenario.Plain(false);
            l.PitDeck = new List<string> { "adj_wedge", "adj_trackbar", "adj_air", "adj_spring", "adj_tape", "quick_jack" };
            var r = ToStall(PitPolicy.Player, l);
            var pg = r.PitGame;
            var adj = pg.Hand.Select((h, i) => (h, i)).Where(x => x.h.Def.Lane == PitLane.Adjust).ToList();
            Assert.True(adj.Count >= 4);
            for (int k = 0; k < 3; k++) Assert.Null(r.PitToggle(adj[k].i));
            Assert.Equal("Max 3 adjustments", r.PitToggle(adj[3].i));
        }

        [Fact]
        public void SetupsInstallLevelUpMakePackagesAndNudgeTheTune()
        {
            var l = Scenario.Plain(false);
            l.PitDeck = new List<string> { "adj_wedge", "adj_air", "adj_wedge+" };
            var r = ToStall(PitPolicy.Player, l);
            var pg = r.PitGame;
            int aero = r.Tune.Aero;
            for (int i = 0; i < pg.Hand.Count; i++) if (pg.Hand[i].Id != "adj_wedge+") r.PitToggle(i);
            r.PitGo();
            while (r.T - pg.T0 < pg.Total) r.Tick();
            r.PitRelease();
            Assert.Equal(2, r.Setups.Count);
            Assert.All(r.Setups, s => Assert.Equal(1, s.Lvl));
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.PackageComplete && e.Text == "Long-Run Package");
            Assert.Equal(Math.Min(2, aero + 1), r.Tune.Aero);
            // the package and the Wedge both read on a drafting card
            var o = r.ComputeOdds(CardBook.Get("tuck_in"), r.Ctx());
            Assert.Equal(6 + 4, o.Mods["setup"]);
        }

        [Fact]
        public void UpgradedSetupCardInstallsAtLevelTwo()
        {
            var l = Scenario.Plain(false);
            l.PitDeck = new List<string> { "adj_spring+" };
            var r = ToStall(PitPolicy.Player, l);
            r.PitToggle(0);
            r.PitGo();
            while (r.T - r.PitGame.T0 < r.PitGame.Total) r.Tick();
            r.PitRelease();
            Assert.Equal(2, r.Setups.Single().Lvl);
        }

        [Fact]
        public void SetupsGoStaleAfterAFuelRunOfGreenLaps()
        {
            var l = Scenario.Plain(false);
            l.PitDeck = new List<string> { "adj_trackbar" };
            var r = ToStall(PitPolicy.Player, l);
            r.PitToggle(0);
            r.PitGo();
            while (r.T - r.PitGame.T0 < r.PitGame.Total) r.Tick();
            r.PitRelease();
            Assert.Single(r.Setups);
            r.PitPolicy = PitPolicy.Bot; // later stops run themselves
            while (!r.Done && r.Setups.Count > 0) r.Tick();
            if (r.Setups.Count > 0) return; // race ended first
            var ev = r.Log.Last(e => e.Kind == RaceEventKind.SetupsStale);
            Assert.Equal("trackbar gone", ev.Text);
        }

        [Fact]
        public void BoardCardsBecomePrepCardsInThePitHand()
        {
            var r = Scenario.Make("carver", null, 61, PitPolicy.Player);
            while (r.Phase != RacePhase.Green) r.Tick();
            for (int i = 0; i < 20 * 30; i++) r.Tick();
            r.PressPit();
            while (r.Me.Pit == null) r.Tick();
            r.Board = 2; // set on pit road: no draw beats there, so no stale cards fill the board first
            while (r.PitGame == null) r.Tick();
            Assert.Equal(2, r.PitGame.Prep);
            Assert.Equal(0, r.Board);
            Assert.Equal(2, r.PitGame.Hand.Count(h => h.Prep));
        }

        [Fact]
        public void PitGameRacesAreDeterministic()
        {
            string Run()
            {
                var r = Scenario.Make("carver", PlayerLoadout.ForPersona("gambler"), 7);
                while (!r.Done) { r.Tick(); if (r.Phase == RacePhase.Green && r.MyPlan?.Lap == r.LapOf(r.Me) && r.PitRequest == null && r.Me.Pit == null) r.PressPit(); }
                return r.Fingerprint() + "|" + string.Join(",", r.CardStats.StopTimes);
            }
            Assert.Equal(Run(), Run());
        }
    }
}
