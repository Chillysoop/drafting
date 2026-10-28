using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Crew radio calls (step 33): when they come up, their odds and payoffs, Crew HQ perks, Wits, and the radio lane.</summary>
    public class RadioTests
    {
        static Race RunToCall(Race r, int maxTicks = 20 * 400)
        {
            for (int i = 0; i < maxTicks && r.Radio == null; i++) r.Tick();
            Assert.NotNull(r.Radio);
            return r;
        }

        [Fact]
        public void CallsComeUpOnGreenAndTimeOut()
        {
            var r = RunToCall(Scenario.Make("carver"));
            Assert.Equal(RacePhase.Green, r.Phase);
            Assert.True(r.T >= 18, $"first call at {r.T:F1} s");
            var c = r.Radio;
            Assert.Equal(c.Kind == RadioKind.Advice ? 6 : 7, c.Dur, 6);
            while (r.Radio == c) r.Tick();
            Assert.Null(r.Radio);
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.RadioTimedOut);
            Assert.InRange(r.RadioNext - r.T, 22 - 0.1, 34 + 0.1);
        }

        [Fact]
        public void ARaceWhereNobodyAnswersRollsTheSameWithOrWithoutTheRadio()
        {
            foreach (var t in new[] { "mill", "carver", "talon", "hollow" })
            {
                var off = Scenario.Plain(Tracks.ById(t).Kart); off.Radio = false;
                var a = Scenario.Make(t, off); a.RunToEnd();
                var b = Scenario.Make(t); b.RunToEnd();
                Assert.Contains(b.Log, e => e.Kind == RaceEventKind.RadioCall);
                Assert.DoesNotContain(a.Log, e => e.Kind == RaceEventKind.RadioCall);
                Assert.Equal(a.Fingerprint(), b.Fingerprint());
            }
        }

        [Fact]
        public void ACallThatWorksPaysAndAMissCosts()
        {
            var r = Scenario.Green("carver");
            r.OfferCall("Brake bias");
            r.ForceNextCall = true;
            Assert.Null(r.TakeCall());
            Assert.Equal(1.008, r.Me.PermPace, 9);
            Assert.True(r.LastAnswer.Ok);
            Assert.Null(r.Radio);

            r.OfferCall("Brake bias");
            r.ForceNextCall = false;
            r.TakeCall();
            Assert.False(r.LastAnswer.Ok);
            Assert.Contains(r.Me.Effects, e => e.Kind == EffectKind.Pace && Math.Abs(e.Mult - 0.98) < 1e-9);
            Assert.Equal(2, r.CardStats.Calls);
            Assert.Equal(1, r.CardStats.CallsWon);
            // engineer: 3 + 3 for the call that worked, 3 for the miss
            Assert.Equal(9, r.CardStats.CrewXp[CrewRole.Engineer]);
            Assert.Equal("That's just advice", AdviceOnly(r).TakeCall());
        }

        static Race AdviceOnly(Race r)
        {
            r.PassCall();
            r.ForceNextCall = null;
            typeof(Race).GetProperty("Radio").SetValue(r, new RadioCall { Kind = RadioKind.Advice, Who = "Spotter", Line = "x", Until = r.T + 6, Dur = 6 });
            return r;
        }

        [Fact]
        public void ChanceGrowsWithTheCrewTheClutchRadioAndPerks()
        {
            var l = Scenario.Plain(false);
            var r = Scenario.Green("carver", l);
            var high = RadioCalls.Get("High line");
            Assert.Equal(38 + 3 * 6, r.CallChance(high));
            Assert.Equal(1, r.CallQ(high), 9);

            var l2 = Scenario.Plain(false);
            l2.Spotter = 8; l2.Parts.Add("clutch_radio");
            Assert.Null(l2.PickPerk(CrewRole.Spotter, 0, false));       // Eyes up top +5
            Assert.Null(l2.PickPerk(CrewRole.Spotter, 1, false));       // Door to door: pays 30% more
            var r2 = Scenario.Green("carver", l2);
            Assert.Equal(95, r2.CallChance(high));                      // 38 + 48 + 15 + 5 = 106, capped
            Assert.Equal(1.5 * 1.3, r2.CallQ(high), 9);
            Assert.Equal(12 + 3 * 6 + 15, r2.CallChance(RadioCalls.Get("Setup trick")));   // engineer still level 3

            var l3 = Scenario.Plain(false);
            Assert.Equal("Needs level 8", l3.PickPerk(CrewRole.Engineer, 2, false));
        }

        [Fact]
        public void BiggerCallsUnlockAtLevelsSixAndEight()
        {
            List<string> Names(PlayerLoadout l, string t = "carver") => Scenario.Green(t, l).RadioOptions().Select(o => o.def.Name).ToList();
            var basic = Names(Scenario.Plain(false));
            Assert.DoesNotContain("Engine map", basic);
            Assert.DoesNotContain("Setup trick", basic);
            Assert.DoesNotContain("Fuel stretch", basic);
            Assert.Contains("Brake bias", basic);
            Assert.Contains("Advice", basic);

            var l6 = Scenario.Plain(false); l6.Engineer = 6; l6.CrewChief = 6;
            var six = Names(l6);
            Assert.Contains("Engine map", six);
            Assert.Contains("Fuel stretch", six);
            Assert.DoesNotContain("Setup trick", six);

            var l8 = Scenario.Plain(false); l8.Engineer = 8;
            Assert.Contains("Setup trick", Names(l8));

            // pit calls never come up in a kart race
            var k = Scenario.Plain(true); k.CrewChief = 6;
            Assert.DoesNotContain("Fuel stretch", Names(k, "hollow"));
        }

        [Fact]
        public void FuelStretchShortensTheNextStop()
        {
            var l = Scenario.Plain(false); l.CrewChief = 6;
            var r = Scenario.Green("carver", l);
            r.OfferCall("Fuel stretch");
            r.ForceNextCall = true;
            r.TakeCall();
            Assert.Equal(1.5 * 1.3, r.PitBonus, 9);
        }

        [Fact]
        public void WitsStretchesTheCountdownOfRiskyCallsOnly()
        {
            var sharp = Scenario.Plain(false); sharp.Wits = 15;
            Assert.Equal(9, Scenario.Green("carver", sharp).OfferCall("Brake bias").Dur, 9);
            var slow = Scenario.Plain(false); slow.Wits = 6;
            var c = Scenario.Green("carver", slow).OfferCall("Brake bias");
            Assert.Equal(5, c.Dur, 9);
            Assert.Equal(-2, c.Wits, 9);
            Assert.Equal(15, PlayerLoadout.ForPersona("drafter").Wits);
            Assert.Equal(12, PlayerLoadout.ForPersona("bully").Wits);
        }

        [Fact]
        public void SafetyNetSecondLookAndCoolRunning()
        {
            var l = Scenario.Plain(false);
            l.Crew.Spotter.Net = 1;
            var r = Scenario.Green("carver", l);
            r.OfferCall("High line");
            r.ForceNextCall = false;
            r.TakeCall();
            Assert.True(r.LastAnswer.Net);
            Assert.DoesNotContain(r.Me.Effects, e => e.Yield);

            var l2 = Scenario.Plain(false);
            l2.Crew.Spotter.Again = 1;
            l2.Crew.Engineer.FixOnS = 4;
            l2.Crew.MomAll = 1;
            var r2 = Scenario.Green("carver", l2);
            r2.Me.Damage = 10; r2.Me.Zones.Side = 10;
            r2.OfferCall("High line");
            r2.ForceNextCall = true;
            r2.TakeCall();
            Assert.Equal(r2.T + 2, r2.RadioNext, 9);
            Assert.Equal(1, r2.Momentum);
            r2.OfferCall("Bleed pressures");
            r2.ForceNextCall = true;
            r2.TakeCall();
            Assert.Equal(6, r2.Me.Damage, 9);
            Assert.Equal(6, r2.Me.Zones.Sum, 9);
        }

        [Fact]
        public void ABetterCrewTalksMore()
        {
            var r = Scenario.Green("carver");
            Assert.Equal(1, r.CrewRate(), 9);
            var l = Scenario.Plain(false); l.CrewChief = 8; l.Spotter = 8; l.Engineer = 8;
            Assert.Null(l.PickPerk(CrewRole.CrewChief, 2, false));   // Big picture: 15% more often
            Assert.Equal(0.9 * 0.85, Scenario.Green("carver", l).CrewRate(), 9);
        }

        [Fact]
        public void ACautionTakesTheCallOffTheRadio()
        {
            var r = Scenario.Green("carver");
            r.OfferCall("Save tires");
            Scenario.ToCaution(r);
            Assert.Null(r.Radio);
            for (int i = 0; i < 400; i++) { r.Tick(); Assert.True(r.Radio == null || r.Phase == RacePhase.Green); }
        }

        [Fact]
        public void PerksCloneWithTheLoadout()
        {
            var l = Scenario.Plain(false); l.Spotter = 4;
            var c = l.Clone();
            Assert.Null(c.PickPerk(CrewRole.Spotter, 0, false));
            Assert.Equal(0, l.Crew.Spotter.Chance);
            Assert.Equal(5, c.Crew.Spotter.Chance);
        }

        // ------------------------------------------------------------------ the radio lane

        const double W = 1920, H = 1080, Frame = 1 / 30.0;

        sealed class Rig
        {
            public PlayDesk D;
            public HudReadouts Ro = new HudReadouts();
            public HudLayout L = new HudLayout();
            public HudInput In;
            public PadInput Pad;
            public Race R => D.Race;
            public void Step(int frames = 1) { for (int i = 0; i < frames; i++) { D.Frame(Frame); Lay(); } }
            public void Lay() { Ro.Update(D); L.Update(W, H, D, Ro); }
            public void Click(Box b) { In.Move(b.CX, b.CY); In.Down(b.CX, b.CY); In.Up(b.CX, b.CY); Lay(); }
            public Box Find(HitKind k) => L.RadioBoxes.First(x => x.hit.Kind == k).box;
        }

        static Rig Green()
        {
            var d = new PlayDesk(new RaceDriver(PlayDesk.NewRace(Tracks.ById("carver"), 61))) { BeatPause = false };
            var g = new Rig { D = d };
            g.In = new HudInput(d, g.L);
            g.Pad = new PadInput(d, g.L, g.In) { Active = true };
            for (int i = 0; i < 60 * 30 && d.Race.Phase != RacePhase.Green; i++) g.Step();
            g.Step(3);
            g.Lay();
            return g;
        }

        [Fact]
        public void TheRadioLaneSitsAboveTheConsoleWithTryItAndNoThanks()
        {
            var g = Green();
            Assert.Equal(0, g.L.Radio.H);
            g.R.OfferCall("Save tires");
            g.Lay();
            var rr = g.Ro.Radio;
            Assert.True(rr.Show && rr.Risky);
            Assert.Equal("Crew chief", rr.Who);
            Assert.Equal($"Try it: {g.R.Radio.Chance}% to work", rr.TakeLabel);
            Assert.Contains("Crew chief level", rr.Crew);
            Assert.True(g.L.Radio.Bottom <= g.L.Tiles.Y && g.L.Booth.Bottom <= g.L.Radio.Y, "radio between the console and the booth");
            Assert.Equal(new HitKind[] { HitKind.RadioTake, HitKind.RadioPass }, g.L.RadioBoxes.Select(x => x.hit.Kind));

            var take = g.Find(HitKind.RadioTake);
            Assert.Equal(HitKind.RadioTake, g.L.HitTest(take.CX, take.CY, g.D).Kind);
            g.Click(take);
            Assert.Null(g.R.Radio);
            Assert.Equal(1, g.R.CardStats.Calls);
            Assert.Equal(1, g.D.CallsTaken);
            Assert.True(g.Ro.Radio.Flash);
            Assert.NotNull(g.D.Toast);

            g.R.OfferCall("Save tires"); g.Lay();
            g.Click(g.Find(HitKind.RadioPass));
            Assert.Null(g.R.Radio);
            Assert.Equal(1, g.D.CallsPassed);
            Assert.Equal(1, g.R.CardStats.Calls);
        }

        [Fact]
        public void KeysAndShouldersAnswerTheRadio()
        {
            var g = Green();
            g.R.OfferCall("Brake bias"); g.Lay();
            g.In.Key(HudKey.RadioTake, true);
            Assert.Equal(1, g.R.CardStats.Calls);
            g.R.OfferCall("Brake bias"); g.Lay();
            g.In.Key(HudKey.RadioPass, true);
            Assert.Null(g.R.Radio);

            // a pad: the shoulders answer while a call is up, and sort and filter again once it's gone
            g.R.OfferCall("Brake bias"); g.Lay();
            var prompts = PadPrompts.For(g.Pad, g.D, g.L).Select(p => p.label).ToList();
            Assert.Contains(prompts, p => p.StartsWith("Try the call"));
            var sort = g.D.Sort;
            g.Pad.Press(PadButton.Filter, true);
            Assert.Equal(2, g.R.CardStats.Calls);
            g.R.OfferCall("Brake bias"); g.Lay();
            g.Pad.Press(PadButton.Sort, true);
            Assert.Null(g.R.Radio);
            Assert.Equal(sort, g.D.Sort);
            g.Pad.Press(PadButton.Sort, true);
            Assert.NotEqual(sort, g.D.Sort);
            // and the buttons are in the focus ring
            g.R.OfferCall("Brake bias"); g.Lay();
            Assert.Contains(g.Pad.Items(), x => x.hit.Kind == HitKind.RadioTake);
        }

        [Fact]
        public void TheCrewChiefsPitCallShowsWhenTheRadioIsQuiet()
        {
            var g = Green();
            Scenario.ToCaution(g.R);
            g.Lay();
            var rr = g.Ro.Radio;
            Assert.True(rr.Show && rr.PitCall);
            Assert.Equal("Pit call", rr.Tag);
            Assert.Empty(g.L.RadioBoxes);
            g.R.SetCautionCall(g.R.Caution.Chief == PitService.Stay ? PitService.Four : PitService.Stay);
            g.Lay();
            Assert.StartsWith("\"You've got", g.Ro.Radio.Line);
        }

        [Fact]
        public void MouseAndPadPlayersTakeCalls()
        {
            var (d, bot) = MousePlayer.Run(Tracks.ById("carver"), 61);
            Assert.True(bot.CallClicks > 0 && d.Race.CardStats.Calls == d.CallsTaken && d.CallsTaken > 0, $"clicks {bot.CallClicks}, calls {d.Race.CardStats.Calls}");
            var (pd, pb) = PadPlayer.Run(Tracks.ById("talon"), 61);
            Assert.True(pb.CallShoulders > 0 && pb.CallWalks > 0, $"shoulders {pb.CallShoulders}, walks {pb.CallWalks}");
            Assert.True(pd.Race.CardStats.Calls >= pb.CallShoulders, $"calls {pd.Race.CardStats.Calls}");
            Assert.Equal(0, pb.Lost);
        }
    }
}
