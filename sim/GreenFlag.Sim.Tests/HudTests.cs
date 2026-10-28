using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 31: the hand and beats rules (PlayDesk), the HUD layout and its pointer and key input, played as a person would.</summary>
    public class HudTests
    {
        const double Frame = 1 / 60.0;
        const double W = 1920, H = 1080;

        sealed class Rig
        {
            public PlayDesk D;
            public HudReadouts Ro = new HudReadouts();
            public HudLayout L = new HudLayout();
            public HudInput In;
            public Race R => D.Race;

            public void Step(int frames = 1)
            {
                for (int i = 0; i < frames; i++) { D.Frame(Frame); Ro.Update(D); L.Update(W, H, D, Ro); }
            }

            public void Lay() { Ro.Update(D); L.Update(W, H, D, Ro); }
            public void Click(Box b) { In.Move(b.CX, b.CY); In.Down(b.CX, b.CY); In.Up(b.CX, b.CY); Lay(); }
        }

        /// <summary>A race at the green with the hand cleared and the draw-beat pause off (tests that want it turn it on).</summary>
        static Rig Green(string track = "carver", uint seed = 61, bool beatPause = false)
        {
            var d = new PlayDesk(new RaceDriver(PlayDesk.NewRace(Tracks.ById(track), seed))) { BeatPause = beatPause };
            var g = new Rig { D = d };
            g.In = new HudInput(d, g.L);
            for (int i = 0; i < 60 * 30 && d.Race.Phase != RacePhase.Green; i++) g.Step();
            g.Step(30);
            Assert.Equal(RacePhase.Green, d.Race.Phase);
            if (d.Driver.Paused) d.TogglePause();
            d.Race.Hand.Clear(); d.Race.Selected = null;
            g.Lay();
            return g;
        }

        static HandCard Give(Rig g, string id) { var h = g.R.GiveCard(id); g.D.Refresh(); g.Lay(); return h; }

        [Fact]
        public void CardsShowOddsFirstAndSortBlockedLast()
        {
            var g = Green();
            foreach (var id in new[] { "tuck_in", "dive_bomb", "save_tires", "block", "restart_jump" }) Give(g, id);
            var d = g.D;
            Assert.Equal("Restart", CardRules.FamilyName(d.Cards.Last().Family));   // blocked on green: always last
            Assert.NotNull(d.Cards.Last().Block);
            var open = d.Cards.Where(c => c.Block == null).ToList();
            for (int i = 1; i < open.Count; i++) Assert.True(open[i - 1].Odds.Succ >= open[i].Odds.Succ);
            foreach (var f in open)
            {
                Assert.Equal((int)Math.Round(f.Odds.Succ * 100), f.Succ);
                Assert.InRange(f.Odds.P.Sum(), 0.999, 1.001);
                Assert.True(f.Chips.Count <= 3);
            }
            d.SetSort(HandSort.Safest);
            open = d.Cards.Where(c => c.Block == null).ToList();
            for (int i = 1; i < open.Count; i++) Assert.True(open[i - 1].Odds.Wreck <= open[i].Odds.Wreck);
            d.SetSort(HandSort.Family);
            open = d.Cards.Where(c => c.Block == null).ToList();
            for (int i = 1; i < open.Count; i++) Assert.True(CardStyle.OrderOf(open[i - 1].Family) <= CardStyle.OrderOf(open[i].Family));
            // the family chip hides the others, and only shown cards get number keys
            d.SetFilter(CardFamily.Draft);
            Assert.All(d.Cards.Where(c => !c.Filtered), c => Assert.Equal(CardFamily.Draft, c.Family));
            Assert.Equal(d.Cards.Count(c => !c.Filtered), d.Shown.Count);
            Assert.Equal(new[] { 1 }, d.Cards.Where(c => !c.Filtered).Select(c => c.Key));
            g.Lay();
            Assert.Single(g.L.CardBoxes);
            // chips are on screen and clicking them does what they say
            var all = g.L.ChipBoxes.First(c => c.hit.Kind == HitKind.AllFamilies);
            g.Click(all.box);
            Assert.Null(d.Filter);
            var fade = g.L.ChipBoxes.First(c => c.hit.Kind == HitKind.FadeRisk);
            g.Click(fade.box);
            Assert.True(d.FadeRisk);
            Assert.Equal(d.Cards.Where(c => c.Block == null && c.Odds.Wreck >= 0.05).Select(c => c.Uid), d.Cards.Where(c => c.Faded).Select(c => c.Uid));
        }

        [Fact]
        public void ClickPicksThenPlays()
        {
            var g = Green();
            var h = Give(g, "tuck_in");
            var box = g.L.CardBox(h.Uid).Value;
            g.Click(box);
            Assert.Equal(h.Uid, g.R.Selected);
            Assert.Null(g.R.Attempt);
            g.Click(g.L.CardBox(h.Uid).Value);
            Assert.NotNull(g.R.Attempt);
            Assert.Equal(1, g.D.Plays);
            Assert.Null(g.R.Selected);
        }

        [Fact]
        public void PickedCardRunsSlowMoUntilFocusIsGone()
        {
            var g = Green();
            var h = Give(g, "tuck_in");
            g.D.Select(h.Uid);
            double t0 = g.R.T, f0 = g.R.FocusMeter;
            g.Step(60);   // one real second
            Assert.InRange(g.R.T - t0, 0.04, 0.16);                 // 10% speed (in whole 0.05 s ticks)
            Assert.InRange(f0 - g.R.FocusMeter, 0.11, 0.14);        // 1 s of the 8 s Focus bar
            Assert.True(g.Ro.Focus.Slowing);
            Assert.True(g.Ro.Clock.Slow || g.Ro.Clock.State == ClockState.Idle);
            g.R.FocusMeter = 0;
            t0 = g.R.T;
            g.Step(60);
            Assert.InRange(g.R.T - t0, 0.9, 1.1);                    // empty: full speed
            Assert.False(g.D.Slowing);
            Assert.Equal("Empty: full speed · play or Esc", g.Ro.Focus.Note);
            g.In.Key(HudKey.Escape, true);
            Assert.Null(g.R.Selected);
        }

        [Fact]
        public void DragUpPlaysDropBackCancelsDragOnBinTosses()
        {
            var g = Green();
            var a = Give(g, "tuck_in");
            var b = Give(g, "save_tires");
            var c = Give(g, "side_draft");
            var L = g.L;

            // drop it back on the hand: nothing played, the card picked before stays picked
            g.D.Select(c.Uid); g.Lay();
            var box = L.CardBox(a.Uid).Value;
            g.In.Down(box.CX, box.CY);
            g.In.Move(box.CX + 4, box.CY - 40);
            Assert.Equal(a.Uid, g.R.Selected);                       // picking it up picks it
            g.In.Up(box.CX + 4, box.CY - 40);
            Assert.Null(g.R.Attempt);
            Assert.Equal(c.Uid, g.R.Selected);
            Assert.Equal(3, g.R.Hand.Count);

            // onto the toss bin: tossed, the Crew Board charges
            int board = g.R.Board, tossed = g.R.CardStats.Tossed;
            box = L.CardBox(b.Uid).Value;
            g.In.Down(box.CX, box.CY);
            g.In.Move(L.TossBin.CX, L.TossBin.CY);
            Assert.True(g.D.Drag.OverBin);
            Assert.False(g.D.Drag.OverPlay);
            g.In.Up(L.TossBin.CX, L.TossBin.CY);
            Assert.Equal(tossed + 1, g.R.CardStats.Tossed);
            Assert.DoesNotContain(g.R.Hand, x => x.Uid == b.Uid);
            Assert.True(g.R.Board != board || g.R.CardStats.BoardFires > 0);
            g.Lay();

            // up past the hand: played
            box = L.CardBox(a.Uid).Value;
            g.In.Down(box.CX, box.CY);
            for (int i = 1; i <= 5; i++) g.In.Move(box.CX, box.CY - i * 80);
            Assert.True(g.D.Drag.OverPlay);
            g.In.Up(box.CX, box.CY - 400);
            Assert.NotNull(g.R.Attempt);
            Assert.Equal("tuck_in", g.R.Attempt.Def.Id);
        }

        [Fact]
        public void HoldToPlayWithTheButtonOrSpace()
        {
            var g = Green();
            var a = Give(g, "tuck_in");
            var L = g.L;
            // slide off the button and let go: cancelled
            g.In.Down(L.PlayButton.CX, L.PlayButton.CY);
            Assert.NotNull(g.D.Aim);
            Assert.Equal(a.Uid, g.R.Selected);
            g.Step(10);
            Assert.True(g.D.Slowing);                                // aiming runs the race in slow-mo
            g.In.Move(L.PlayButton.CX, L.PlayButton.Y - 60);
            g.Lay();
            Assert.True(g.D.Aim.Out);
            Assert.Equal("Let go to cancel", g.Ro.Gauge.PlayLabel);
            g.In.Up(L.PlayButton.CX, L.PlayButton.Y - 60);
            Assert.Null(g.R.Attempt);
            Assert.Null(g.D.Aim);

            // Space with Shift held on release: cancelled; plain Space: played
            g.In.Key(HudKey.Space, true);
            g.In.Key(HudKey.Space, true);   // key repeat
            g.In.Key(HudKey.Space, false, shift: true);
            Assert.Null(g.R.Attempt);
            g.In.Key(HudKey.Space, true);
            g.Step(5);
            g.In.Key(HudKey.Space, false);
            Assert.NotNull(g.R.Attempt);
            Assert.Equal(a.Uid, g.R.Attempt.Card.Uid);
        }

        [Fact]
        public void HoldAimsTheBestCardWhenNothingIsPicked()
        {
            var g = Green();
            Give(g, "dive_bomb"); Give(g, "tuck_in"); Give(g, "save_tires");
            int best = g.D.Cards.Where(c => c.Block == null).OrderByDescending(c => c.Odds.Succ).First().Uid;
            Assert.Equal(best, g.D.AimTarget());
            Assert.True(g.D.Cards.First(c => c.Uid == best).Best || g.D.Cards.First(c => c.Uid == best).Odds.Succ < 0.5);
        }

        [Fact]
        public void TheRaceStopsAtEachDrawBeatUntilYouPlayOrPressP()
        {
            var g = Green(beatPause: true);
            int beats = g.R.Beats;
            for (int i = 0; i < 60 * 60 && !g.D.BeatPaused; i++) g.Step();
            Assert.True(g.D.BeatPaused);
            Assert.Equal(beats + 1, g.R.Beats);
            Assert.Equal(ClockState.Draw, g.Ro.Clock.State);
            Assert.Equal("DRAW", g.Ro.Clock.Num);
            double t = g.R.T;
            g.Step(120);
            Assert.Equal(t, g.R.T);                                  // nothing moves at a draw beat
            // picking a card at the beat doesn't drain Focus (the race isn't running)
            var pick = g.D.Cards.FirstOrDefault(c => c.Block == null) ?? g.D.Cards[0];
            double f = g.R.FocusMeter;
            g.D.Select(pick.Uid); g.Step(60);
            Assert.Equal(f, g.R.FocusMeter);
            g.In.Key(HudKey.Pause, true);
            Assert.False(g.D.BeatPaused);
            g.Step(120);   // still picked, so in slow-mo
            Assert.True(g.R.T > t);

            // the next beat: playing a card goes green
            for (int i = 0; i < 60 * 120 && !g.D.BeatPaused; i++) { g.Step(); if (g.R.Selected != null && !g.D.BeatPaused) g.D.Select(null); }
            Assert.True(g.D.BeatPaused);
            var h = Give(g, "tuck_in");
            Assert.Null(g.D.Play(h.Uid));
            Assert.False(g.D.BeatPaused);
            Assert.False(g.D.Driver.Paused);
        }

        [Fact]
        public void PitStopPlannedAndReleasedWithClicks()
        {
            var g = Green("carver", 61);
            g.D.Driver.SpeedIndex = 3;
            g.Click(g.L.PitBoxes.First(p => p.hit.Kind == HitKind.PitButton).box);
            Assert.NotNull(g.R.PitRequest);
            for (int i = 0; i < 60 * 300 && !g.R.Holding; i++) g.Step();
            Assert.True(g.R.Holding);
            Assert.Equal(PitGamePhase.Plan, g.Ro.Pit.Stop);
            double t = g.R.T;
            g.Step(60);
            Assert.Equal(t, g.R.T);                                   // the race holds while you plan
            // toggle the first card that fits, then GO
            var cards = g.L.StopBoxes.Where(b => b.hit.Kind == HitKind.PitCard).ToList();
            Assert.NotEmpty(cards);
            int on0 = g.R.PitGame.Hand.Count(c => c.On);
            for (int i = 0; i < cards.Count && g.R.PitGame.Hand.Count(c => c.On) == on0; i++) g.Click(cards[i].box);
            Assert.Equal(on0 + 1, g.R.PitGame.Hand.Count(c => c.On));
            g.Click(g.L.StopBoxes.First(b => b.hit.Kind == HitKind.PitGo).box);
            Assert.Equal(PitGamePhase.Work, g.R.PitGame.Phase);
            g.D.Driver.SpeedIndex = 0;
            var pg = g.R.PitGame;
            for (int i = 0; i < 60 * 60 && g.R.T - pg.T0 < pg.Total + pg.Window * 0.4; i++) g.Step();
            g.Click(g.L.StopBoxes.First(b => b.hit.Kind == HitKind.PitRelease).box);
            Assert.Equal(PitGamePhase.Done, pg.Phase);
            Assert.True(pg.InWindow);
            Assert.False(pg.ChiefRelease);
        }

        [Fact]
        public void CrewChiefPlansTheStopWhenThePlanTimerRunsOut()
        {
            var g = Green("carver", 62);
            g.D.Driver.SpeedIndex = 3;
            g.D.PressPit();
            for (int i = 0; i < 60 * 300 && !g.R.Holding; i++) g.Step();
            Assert.True(g.R.Holding);
            for (int i = 0; i < 60 * 13 && g.R.Holding; i++) g.Step();
            Assert.False(g.R.Holding);
            Assert.InRange(g.D.PlanT, g.R.Bal.PgPlanSec - 0.05, g.R.Bal.PgPlanSec + 0.05);
        }

        [Fact]
        public void LayoutKeepsTheHandBetweenTheCornersAtAnySize()
        {
            foreach (var (w, h) in new[] { (1280.0, 720.0), (1920.0, 1080.0), (2560.0, 1080.0), (3840.0, 2160.0) })
            {
                var g = Green();
                foreach (var id in new[] { "tuck_in", "bump_draft", "side_draft", "slingshot", "dive_bomb", "block", "save_tires", "push_limit" }) g.R.GiveCard(id);
                g.D.Refresh();
                g.Ro.Update(g.D);
                g.L.Update(w, h, g.D, g.Ro);
                var L = g.L;
                Assert.Equal(8, L.CardBoxes.Count);
                foreach (var (_, b) in L.CardBoxes)
                {
                    Assert.True(b.X >= L.Dash.Right && b.Right <= L.Gauge.X, $"card {b} between {L.Dash} and {L.Gauge} at {w}x{h}");
                    Assert.True(b.Y > L.PlayLine && b.Bottom <= L.Chips.Y);
                }
                Assert.True(L.Console.Bottom < L.PlayLine && L.Console.X >= L.Dash.Right);
                Assert.True(L.TossBin.Right <= L.Console.Right);
                Assert.All(L.ChipBoxes, c => Assert.True(c.box.Right <= L.Chips.Right));
                Assert.True(L.PitDock.Y > 0 && L.Clock.Bottom <= L.Gauge.Y);
                // the right-hand card wins where they overlap
                var last = L.CardBoxes[L.CardBoxes.Count - 1];
                Assert.Equal(new Hit(HitKind.Card, last.uid).Arg, L.HitTest(last.box.X + 2, last.box.CY, g.D).Arg);
            }
        }

        [Fact]
        public void MousePlayerFinishesARaceOnEveryTrack()
        {
            foreach (var t in Tracks.All)
            {
                // step 42: a superspeedway deck is nearly dead on a short track by design, so the short tracks bring the short-track persona
                var (d, bot) = MousePlayer.Run(t, 61, t.Kind == TrackKind.ShortTrack ? "bully" : "drafter");
                var r = d.Race;
                Assert.True(r.Done, t.Id);
                Assert.True(d.Plays >= 4, $"{t.Id}: {d.Plays} plays");
                Assert.True(bot.Drags > 0 && bot.Clicks > 0 && bot.Holds > 0, $"{t.Id}: drags {bot.Drags}, clicks {bot.Clicks}, holds {bot.Holds}");
                Assert.True(d.BeatStops > 0, t.Id);
                if (!t.Kart) Assert.True(r.CardStats.PitStops > 0 || r.Me.Dnf, $"{t.Id}: no pit stop");
            }
        }

        [Fact]
        public void SameClicksGiveTheSameRace()
        {
            var a = MousePlayer.Run(Tracks.ById("ridge"), 77).desk.Race;
            var b = MousePlayer.Run(Tracks.ById("ridge"), 77).desk.Race;
            Assert.Equal(a.Fingerprint(), b.Fingerprint());
            Assert.Equal(a.CardStats.Plays, b.CardStats.Plays);
        }
    }
}
