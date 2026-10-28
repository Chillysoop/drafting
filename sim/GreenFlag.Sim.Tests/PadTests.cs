using System;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 32: controller and keyboard-only play (PadInput, PadMap, PadPlayer), played through the HUD with buttons only.</summary>
    public class PadTests
    {
        const double Frame = 1 / 60.0;

        sealed class Rig
        {
            public PlayDesk D;
            public HudReadouts Ro = new HudReadouts();
            public HudLayout L = new HudLayout();
            public HudInput In;
            public PadInput Pad;
            public double W = 1920, H = 1080;
            public Race R => D.Race;

            public void Step(int frames = 1)
            {
                for (int i = 0; i < frames; i++) { D.Frame(Frame); Lay(); }
            }

            public void Lay() { Ro.Update(D); L.Update(W, H, D, Ro); Pad.Frame(); }
            public void Tap(PadButton b) { Pad.Press(b, true); Pad.Press(b, false); Lay(); }
        }

        static Rig Green(string track = "carver", uint seed = 61, bool beatPause = false, double w = 1920, double h = 1080)
        {
            var d = new PlayDesk(new RaceDriver(PlayDesk.NewRace(Tracks.ById(track), seed))) { BeatPause = beatPause };
            var g = new Rig { D = d, W = w, H = h };
            g.In = new HudInput(d, g.L);
            g.Pad = new PadInput(d, g.L, g.In) { Active = true };
            for (int i = 0; i < 60 * 30 && d.Race.Phase != RacePhase.Green; i++) g.Step();
            g.Step(30);
            if (d.Driver.Paused) d.TogglePause();
            d.Race.Hand.Clear(); d.Race.Selected = null;
            g.Lay();
            return g;
        }

        static void Give(Rig g, params string[] ids) { foreach (var id in ids) g.R.GiveCard(id); g.D.Refresh(); g.Lay(); }

        [Fact]
        public void NintendoPadsConfirmWithTheRightHandButton()
        {
            Assert.Equal(PadButton.Confirm, PadMap.Of(PadStyle.Xbox, PadPhysical.South));
            Assert.Equal(PadButton.Back, PadMap.Of(PadStyle.Xbox, PadPhysical.East));
            Assert.Equal(PadButton.Confirm, PadMap.Of(PadStyle.PlayStation, PadPhysical.South));
            Assert.Equal(PadButton.Confirm, PadMap.Of(PadStyle.Nintendo, PadPhysical.East));
            Assert.Equal(PadButton.Back, PadMap.Of(PadStyle.Nintendo, PadPhysical.South));
            Assert.Equal(PadButton.Toss, PadMap.Of(PadStyle.Nintendo, PadPhysical.West));
            Assert.Equal(PadButton.Pit, PadMap.Of(PadStyle.Nintendo, PadPhysical.North));
            Assert.Equal(PadButton.Aim, PadMap.Of(PadStyle.Xbox, PadPhysical.RightTrigger));
            Assert.Equal(PadButton.Pause, PadMap.Of(PadStyle.Nintendo, PadPhysical.Start));
            // the glyph is the one printed where the button sits
            Assert.Equal("A", PadGlyphs.Of(PadStyle.Nintendo, PadButton.Confirm));
            Assert.Equal("Y", PadGlyphs.Of(PadStyle.Nintendo, PadButton.Toss));
            Assert.Equal("X", PadGlyphs.Of(PadStyle.Xbox, PadButton.Toss));
            Assert.Equal("Cross", PadGlyphs.Of(PadStyle.PlayStation, PadButton.Confirm));
            Assert.Equal("Enter", PadGlyphs.Of(PadStyle.Keyboard, PadButton.Confirm));
            foreach (PadStyle s in Enum.GetValues(typeof(PadStyle)))
                foreach (PadButton b in Enum.GetValues(typeof(PadButton))) Assert.False(string.IsNullOrEmpty(PadGlyphs.Of(s, b)));

            Assert.Equal(PadStyle.Nintendo, PadMap.StyleOf("SwitchProControllerHID", "Pro Controller", "Nintendo", false));
            Assert.Equal(PadStyle.Nintendo, PadMap.StyleOf("NPad", "", "", false));
            Assert.Equal(PadStyle.Nintendo, PadMap.StyleOf("XInputControllerWindows", "", "", true));   // on a Switch, every pad
            Assert.Equal(PadStyle.PlayStation, PadMap.StyleOf("DualSenseGamepadHID", "DualSense Wireless Controller", "Sony", false));
            Assert.Equal(PadStyle.Xbox, PadMap.StyleOf("XInputControllerWindows", "Controller", "", false));
        }

        [Fact]
        public void HeldDirectionRepeatsAfterADelay()
        {
            var rep = new PadRepeat();
            Assert.Equal(PadButton.Right, rep.Step(PadButton.Right, 0.016));
            int presses = 0;
            for (double t = 0; t < 0.3; t += 0.01) if (rep.Step(PadButton.Right, 0.01) != null) presses++;
            Assert.Equal(0, presses);
            for (double t = 0; t < 0.6; t += 0.01) if (rep.Step(PadButton.Right, 0.01) != null) presses++;
            Assert.InRange(presses, 5, 7);
            Assert.Null(rep.Step(null, 0.01));
            Assert.Equal(PadButton.Left, rep.Step(PadButton.Left, 0.01));
            Assert.Equal(PadButton.Up, PadRepeat.Dir(0.1, 0.9));
            Assert.Equal(PadButton.Left, PadRepeat.Dir(-0.8, 0.3));
            Assert.Null(PadRepeat.Dir(0.3, -0.3));
        }

        [Fact]
        public void FocusWalksTheHandAndAPicksThenPlays()
        {
            var g = Green();
            Give(g, "tuck_in", "side_draft", "save_tires", "block");
            var d = g.D;
            // focus starts on the first card shown, and it is the hovered one (the gauge previews it, no slow-mo)
            Assert.Equal(HitKind.Card, g.Pad.Focus.Kind);
            Assert.Equal(g.L.CardBoxes[0].uid, g.Pad.Focus.Arg);
            Assert.Equal(g.Pad.Focus.Arg, d.Hover);
            Assert.Null(g.R.Selected);
            g.Tap(PadButton.Right);
            Assert.Equal(g.L.CardBoxes[1].uid, g.Pad.Focus.Arg);
            g.Tap(PadButton.Right); g.Tap(PadButton.Right); g.Tap(PadButton.Right);   // stops at the last card
            Assert.Equal(g.L.CardBoxes[3].uid, g.Pad.Focus.Arg);
            g.Tap(PadButton.Left);
            int uid = g.Pad.Focus.Arg;
            Assert.Equal(g.L.CardBoxes[2].uid, uid);
            // A picks (slow-mo while Focus lasts), A again plays
            g.Tap(PadButton.Confirm);
            Assert.Equal(uid, g.R.Selected);
            g.Step(30);
            Assert.True(d.Slowing);
            int plays = d.Plays;
            g.Tap(PadButton.Confirm);
            Assert.Equal(plays + 1, d.Plays);
            Assert.DoesNotContain(g.R.Hand, h => h.Uid == uid);
            // the focus goes to the card now in its place
            Assert.Equal(HitKind.Card, g.Pad.Focus.Kind);
            Assert.Equal(g.L.CardBoxes[Math.Min(2, g.L.CardBoxes.Count - 1)].uid, g.Pad.Focus.Arg);
        }

        [Fact]
        public void BackPutsTheCardDownAndXTosses()
        {
            var g = Green();
            Give(g, "tuck_in", "side_draft", "save_tires");
            g.Tap(PadButton.Confirm);
            Assert.NotNull(g.R.Selected);
            g.Tap(PadButton.Back);
            Assert.Null(g.R.Selected);
            int uid = g.Pad.Focus.Arg, tosses = g.D.Tosses;
            g.Tap(PadButton.Toss);
            Assert.Equal(tosses + 1, g.D.Tosses);
            Assert.DoesNotContain(g.R.Hand, h => h.Uid == uid);
        }

        [Fact]
        public void DownReachesTheChipsAndUpComesBack()
        {
            var g = Green();
            Give(g, "tuck_in", "dive_bomb", "save_tires", "block");
            g.Tap(PadButton.Down);
            Assert.Contains(g.Pad.Focus.Kind, new[] { HitKind.Sort, HitKind.AllFamilies, HitKind.Family, HitKind.FadeRisk });
            // walk left to the first chip (Best odds), then one right is Safest
            for (int i = 0; i < 12; i++) g.Tap(PadButton.Left);
            Assert.Equal(new Hit(HitKind.Sort, (int)HandSort.BestOdds).Arg, g.Pad.Focus.Arg);
            Assert.Equal(HitKind.Sort, g.Pad.Focus.Kind);
            g.Tap(PadButton.Right);
            g.Tap(PadButton.Confirm);
            Assert.Equal(HandSort.Safest, g.D.Sort);
            g.Tap(PadButton.Up);
            Assert.Equal(HitKind.Card, g.Pad.Focus.Kind);
            // Back from a chip also comes home to the hand
            g.Tap(PadButton.Down);
            Assert.NotEqual(HitKind.Card, g.Pad.Focus.Kind);
            g.Tap(PadButton.Back);
            Assert.Equal(HitKind.Card, g.Pad.Focus.Kind);
            // the shoulders cycle sort and family filter without moving the focus
            g.Tap(PadButton.Sort);
            Assert.Equal(HandSort.Family, g.D.Sort);
            g.Tap(PadButton.Filter);
            Assert.NotNull(g.D.Filter);
            Assert.All(g.D.Cards.Where(c => !c.Filtered), c => Assert.Equal(g.D.Filter, c.Family));
            for (int i = 0; i < 8 && g.D.Filter != null; i++) g.Tap(PadButton.Filter);
            Assert.Null(g.D.Filter);
        }

        [Fact]
        public void TriggerHoldsToPlayTheFocusedCardAndLeftTriggerCancels()
        {
            var g = Green();
            Give(g, "tuck_in", "side_draft", "save_tires");
            g.Tap(PadButton.Right);
            int uid = g.Pad.Focus.Arg;
            g.Pad.Press(PadButton.Aim, true); g.Lay();
            Assert.NotNull(g.D.Aim);
            Assert.Equal(uid, g.D.Aim.Uid);
            Assert.True(g.D.Aim.Key);
            g.Step(20);
            int plays = g.D.Plays;
            g.Pad.Press(PadButton.Aim, false); g.Lay();
            Assert.Equal(plays + 1, g.D.Plays);
            Assert.Null(g.D.Aim);

            for (int i = 0; i < 60 * 10 && g.R.Attempt != null; i++) g.Step();
            uid = g.Pad.Focus.Arg;
            g.Pad.Press(PadButton.Aim, true); g.Lay();
            g.Pad.Press(PadButton.AimCancel, true);
            g.Pad.Press(PadButton.Aim, false); g.Pad.Press(PadButton.AimCancel, false); g.Lay();
            Assert.Equal(plays + 1, g.D.Plays);
            Assert.Contains(g.R.Hand, h => h.Uid == uid);
        }

        [Fact]
        public void MenuLeavesADrawBeatAndAPlaysThrough()
        {
            var g = Green(beatPause: true);
            for (int i = 0; i < 60 * 60 && !g.D.BeatPaused; i++) g.Step();
            Assert.True(g.D.BeatPaused);
            g.Tap(PadButton.Pause);
            Assert.False(g.D.BeatPaused);
            Assert.False(g.D.Driver.Paused);
            for (int i = 0; i < 60 * 120 && !g.D.BeatPaused; i++) g.Step();
            Assert.True(g.D.BeatPaused);
            g.R.Hand.Clear(); Give(g, "tuck_in");
            g.Tap(PadButton.Confirm); g.Tap(PadButton.Confirm);
            Assert.False(g.D.BeatPaused);
        }

        [Fact]
        public void PitStopPlannedAndReleasedWithButtons()
        {
            var g = Green("carver", 61);
            g.D.Driver.SpeedIndex = 3;
            g.Tap(PadButton.Pit);
            Assert.NotNull(g.R.PitRequest);
            for (int i = 0; i < 60 * 300 && !g.R.Holding; i++) g.Step();
            Assert.True(g.R.Holding);
            // the stall takes the focus: the pit stop hand only
            Assert.Equal(HitKind.PitCard, g.Pad.Focus.Kind);
            int on0 = g.R.PitGame.Hand.Count(c => c.On);
            for (int i = 0; i < 6 && g.R.PitGame.Hand.Count(c => c.On) == on0; i++) { g.Tap(PadButton.Confirm); if (g.R.PitGame.Hand.Count(c => c.On) == on0) g.Tap(PadButton.Right); }
            Assert.Equal(on0 + 1, g.R.PitGame.Hand.Count(c => c.On));
            g.Tap(PadButton.Down);
            Assert.Contains(g.Pad.Focus.Kind, new[] { HitKind.PitAuto, HitKind.PitGo });
            for (int i = 0; i < 4 && g.Pad.Focus.Kind != HitKind.PitGo; i++) g.Tap(PadButton.Right);
            Assert.Equal(HitKind.PitGo, g.Pad.Focus.Kind);
            g.Tap(PadButton.Confirm);
            Assert.Equal(PitGamePhase.Work, g.R.PitGame.Phase);
            Assert.Equal(HitKind.PitRelease, g.Pad.Focus.Kind);
            var pr = PadPrompts.For(g.Pad, g.D, g.L);
            Assert.Equal(("A", "Drop the jack"), pr.Single());
            g.D.Driver.SpeedIndex = 0;
            var pg = g.R.PitGame;
            for (int i = 0; i < 60 * 60 && g.R.T - pg.T0 < pg.Total + pg.Window * 0.4; i++) g.Step();
            g.Tap(PadButton.Confirm);
            Assert.Equal(PitGamePhase.Done, pg.Phase);
            Assert.True(pg.InWindow);
            for (int i = 0; i < 60 * 10 && g.L.StopBoxes.Count > 0; i++) g.Step();
            Assert.NotEqual(HitKind.PitGo, g.Pad.Focus.Kind);    // back on the hand (or nothing, if the hand is empty)
        }

        [Fact]
        public void UpFromTheHandReachesThePitModule()
        {
            var g = Green();
            Give(g, "tuck_in", "side_draft");
            g.Tap(PadButton.Up);
            Assert.Contains(g.Pad.Focus.Kind, new[] { HitKind.PitButton, HitKind.Strategy, HitKind.CautionCall });
            for (int i = 0; i < 3 && g.Pad.Focus.Kind != HitKind.PitButton; i++) g.Tap(PadButton.Up);
            Assert.Equal(HitKind.PitButton, g.Pad.Focus.Kind);
            g.Tap(PadButton.Confirm);
            Assert.NotNull(g.R.PitRequest);
            g.Tap(PadButton.Down);
            Assert.Equal(HitKind.Strategy, g.Pad.Focus.Kind);
        }

        [Fact]
        public void PromptsFollowTheFocusAndThePad()
        {
            var g = Green();
            Give(g, "tuck_in", "side_draft");
            g.Pad.Style = PadStyle.Nintendo;
            var p = PadPrompts.For(g.Pad, g.D, g.L);
            Assert.Contains(("A", "Pick"), p);
            Assert.Contains(("ZR", "Hold to play"), p);
            Assert.Contains(("Y", "Toss"), p);
            Assert.Contains(("X", "PIT"), p);
            g.Tap(PadButton.Confirm);
            p = PadPrompts.For(g.Pad, g.D, g.L);
            Assert.Contains(("A", "Play"), p);
            Assert.Contains(("B", "Put down"), p);
            g.Pad.Style = PadStyle.Keyboard;
            Assert.Contains(("Enter", "Play"), PadPrompts.For(g.Pad, g.D, g.L));
        }

        [Fact]
        public void PointerUseHidesTheFocusAndFirstPressOnlyShowsIt()
        {
            var g = Green();
            Give(g, "tuck_in", "side_draft", "save_tires");
            g.Pad.Active = false;
            g.D.Hover = null; g.Lay();
            Assert.Null(g.D.Hover);                       // the pointer owns hover
            int at = g.Pad.Focus.Arg;
            g.Tap(PadButton.Right);
            Assert.True(g.Pad.Active);
            Assert.Equal(at, g.Pad.Focus.Arg);           // shown where it was, not moved
            Assert.Equal(at, g.D.Hover);
            g.Tap(PadButton.Right);
            Assert.NotEqual(at, g.Pad.Focus.Arg);
        }

        [Fact]
        public void PadPlayerFinishesARaceOnEveryTrack()
        {
            foreach (var t in Tracks.All)
            {
                // step 42: a superspeedway deck is nearly dead on a short track by design, so the short tracks bring the short-track persona
                var (d, bot) = PadPlayer.Run(t, 61, t.Kind == TrackKind.ShortTrack ? "bully" : "drafter");
                var r = d.Race;
                Assert.True(r.Done, t.Id);
                Assert.Equal(0, bot.Lost);
                Assert.True(d.Plays >= 4, $"{t.Id}: {d.Plays} plays");
                Assert.True(bot.Taps > 0 && bot.Holds > 0 && bot.Moves > 0, $"{t.Id}: taps {bot.Taps}, holds {bot.Holds}, moves {bot.Moves}");
                Assert.True(d.BeatStops > 0, t.Id);
                if (!t.Kart) Assert.True(r.CardStats.PitStops > 0 || r.Me.Dnf, $"{t.Id}: no pit stop");
            }
        }

        [Fact]
        public void PadPlayerFinishesOnSwitchHandheldSize()
        {
            foreach (var id in new[] { "carver", "ridge", "hollow" })
            {
                var (d, bot) = PadPlayer.Run(Tracks.ById(id), 62, w: 1280, h: 720, style: PadStyle.Nintendo);
                Assert.True(d.Race.Done, id);
                Assert.Equal(0, bot.Lost);
                Assert.True(d.Plays >= 4, $"{id}: {d.Plays} plays");
            }
        }

        [Fact]
        public void SamePressesGiveTheSameRace()
        {
            var a = PadPlayer.Run(Tracks.ById("cross"), 77).desk.Race;
            var b = PadPlayer.Run(Tracks.ById("cross"), 77).desk.Race;
            Assert.Equal(a.Fingerprint(), b.Fingerprint());
            Assert.Equal(a.CardStats.Plays, b.CardStats.Plays);
        }
    }
}
