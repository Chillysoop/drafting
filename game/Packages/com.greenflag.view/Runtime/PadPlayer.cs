using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>
    /// Plays a race with nothing but controller buttons, through PadInput, the way the mouse player does with a pointer: it
    /// walks the focus to a card with the d-pad, picks it and presses A again to play it or holds the right trigger, tosses
    /// weak cards with X at draw beats, presses Y on the plan lap, presses Menu to leave a draw beat, and in the stall walks to
    /// the crew chief's plan and GO and drops the jack with A. The card rules are the mouse player's (the harness's human
    /// style). Tests and the harness use it to prove a race is fully playable on a controller.
    /// </summary>
    public sealed class PadPlayer
    {
        readonly PlayDesk d;
        readonly HudLayout lay;
        readonly HudReadouts ro;
        readonly PadInput pad;
        readonly Queue<Func<int>> steps = new Queue<Func<int>>();
        const int Again = 0, Done = 1, Through = 2;
        double nextLook;
        int gesture;

        /// <summary>Gestures made: pick-then-play with A, holds of the trigger, tosses, pit presses, Menu presses to leave a
        /// draw beat, and focus moves.</summary>
        public int Taps, Holds, Tosses, PitPresses, Resumes, Moves;
        /// <summary>Walks to a control that never got the focus (should stay 0).</summary>
        public int Lost;
        public double MinOdds = 0.65, MaxWreck = 0.05;

        public PadPlayer(PlayDesk desk, HudLayout layout, HudReadouts readouts, PadInput pad)
        {
            d = desk; lay = layout; ro = readouts; this.pad = pad;
        }

        /// <summary>One frame: carries on with the gesture in progress (a step runs again next frame, is done, or is done
        /// without pressing anything so the next one runs now), or decides what to do next.</summary>
        public void Frame()
        {
            pad.Frame();
            while (steps.Count > 0)
            {
                int k = steps.Peek()();
                if (k == Again) return;
                steps.Dequeue();
                if (k == Done) return;
            }
            Decide();
        }

        void Do(Action a) => steps.Enqueue(() => { a(); return Done; });
        void Wait(int frames) { for (int i = 0; i < frames; i++) steps.Enqueue(() => Done); }
        void Tap(PadButton b) => Do(() => { pad.Press(b, true); pad.Press(b, false); });

        /// <summary>Presses directions, one a frame, until the focus is on the control the predicate picks (or gives up).</summary>
        void WalkTo(Func<Hit, bool> want)
        {
            int tries = 0;
            bool vertical = false;
            Hit? before = null;
            steps.Enqueue(() =>
            {
                var items = pad.Items();
                int ti = items.FindIndex(x => want(x.hit));
                if (ti < 0) return Through;                     // gone (played, the stall closed): nothing to walk to
                if (want(pad.Focus)) return Through;
                if (++tries > 40) { Lost++; return Through; }
                if (!(pad.FocusBox is Box a)) { pad.Press(PadButton.Right, true); return Again; }
                // a press that didn't move the focus means that way is blocked: try the other axis
                if (before is Hit h && h.Kind == pad.Focus.Kind && h.Arg == pad.Focus.Arg) vertical = !vertical;
                else vertical = false;
                var b = items[ti].box;
                double dx = b.CX - a.CX, dy = b.CY - a.CY;
                bool useY = Math.Abs(dy) > Math.Abs(dx) * 0.5 && Math.Abs(dy) > 1;
                if (vertical) useY = !useY;
                if (useY && Math.Abs(dy) < 1) useY = false;
                if (!useY && Math.Abs(dx) < 1) useY = true;
                before = pad.Focus;
                pad.Press(useY ? (dy < 0 ? PadButton.Up : PadButton.Down) : (dx < 0 ? PadButton.Left : PadButton.Right), true);
                Moves++;
                return Again;
            });
        }

        void WalkTo(HitKind k) => WalkTo(h => h.Kind == k);
        void WalkToCard(int uid) => WalkTo(h => h.Kind == HitKind.Card && h.Arg == uid);

        void Decide()
        {
            var r = d.Race;
            if (r.Done || r.Me.Dnf) return;
            var p = ro.Pit;

            // the stall: walk to the crew chief's plan, take it, walk to GO, then drop the jack as the slowest lane finishes
            if (r.Holding && r.PitGame.Auto == PitPolicy.Player)
            {
                Wait(20);
                WalkTo(HitKind.PitAuto); Tap(PadButton.Confirm);
                WalkTo(HitKind.PitGo); Tap(PadButton.Confirm);
                Wait(2);
                PitPresses += 2;
                return;
            }
            if (p.Stop == PitGamePhase.Work)
            {
                if (p.Elapsed >= p.Total + Math.Min(0.15, p.Window * 0.3)) { WalkTo(HitKind.PitRelease); Tap(PadButton.Confirm); PitPresses++; Wait(10); }
                return;
            }
            if (r.Phase == RacePhase.Green && r.Me.Pit == null && !p.Requested && p.Show && r.MyPlan is PitPlan plan && r.LapOf(r.Me) >= plan.Lap)
            {
                Tap(PadButton.Pit); PitPresses++;
                Wait(2);
                return;
            }

            bool look = d.BeatPaused || r.HazardCard != null || r.T >= nextLook;
            if (!look || r.Attempt != null) return;
            nextLook = r.T + 2.5;

            CardFace pick = null;
            foreach (var f in d.Cards)
            {
                if (f.Filtered || !f.Playable) continue;
                if (f.Special) { pick = f; break; }
                if (f.Odds.Succ >= MinOdds && f.Odds.Wreck < MaxWreck && (pick == null || f.Odds.Succ > pick.Odds.Succ)) pick = f;
            }
            if (pick != null)
            {
                int uid = pick.Uid;
                WalkToCard(uid);
                if (gesture++ % 2 == 0)
                {
                    Do(() => { if (pad.Focus.Kind == HitKind.Card && pad.Focus.Arg == uid) { pad.Press(PadButton.Confirm, true); pad.Press(PadButton.Confirm, false); } });
                    Wait(3);
                    Do(() => { if (d.Race.Selected == uid) { pad.Press(PadButton.Confirm, true); pad.Press(PadButton.Confirm, false); } });
                    Taps++;
                }
                else
                {
                    Do(() => pad.Press(PadButton.Aim, true));
                    Wait(8);
                    Do(() => pad.Press(PadButton.Aim, false));
                    Holds++;
                }
                return;
            }
            if (!d.BeatPaused) return;
            CardFace toss = null;
            foreach (var f in d.Cards)
            {
                if (f.Filtered || f.Special || f.Def.Situational) continue;
                bool dead = f.Block == "Not enough tire left";
                if (dead || (f.TossLive && f.Odds.Succ < 0.55)) { toss = f; break; }
            }
            if (toss != null)
            {
                int uid = toss.Uid;
                WalkToCard(uid);
                Do(() => { if (pad.Focus.Kind == HitKind.Card && pad.Focus.Arg == uid) { pad.Press(PadButton.Toss, true); Tosses++; } });
                Wait(2);
            }
            Do(() => { if (d.BeatPaused) { pad.Press(PadButton.Pause, true); Resumes++; } });
        }

        /// <summary>One race played on a controller at a screen size and playback speed. Returns the desk when it's done.</summary>
        public static (PlayDesk desk, PadPlayer bot) Run(TrackDef track, uint seed, string persona = "drafter", double w = 1920, double h = 1080,
            int speedIndex = 3, double frame = 1 / 30.0, int maxFrames = 400000, PadStyle style = PadStyle.Xbox)
        {
            var desk = new PlayDesk(new RaceDriver(PlayDesk.NewRace(track, seed, persona)));
            desk.Driver.SpeedIndex = speedIndex;
            var ro = new HudReadouts();
            var lay = new HudLayout();
            var input = new HudInput(desk, lay);
            var pad = new PadInput(desk, lay, input) { Style = style, Active = true };
            var bot = new PadPlayer(desk, lay, ro, pad);
            for (int i = 0; i < maxFrames && !desk.Race.Done; i++)
            {
                desk.Frame(frame);
                ro.Update(desk);
                lay.Update(w, h, desk, ro);
                bot.Frame();
            }
            return (desk, bot);
        }
    }
}
