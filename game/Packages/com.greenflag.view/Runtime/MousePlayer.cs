using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>
    /// Plays a race with nothing but a pointer and the P key, through the same layout and input path a person uses: it drags
    /// cards up to play them, clicks a picked card to play it, holds the Play button, drags weak cards onto the toss bin at
    /// draw beats, presses PIT on the plan lap and plans and releases its own stops. Its card rules are the harness's
    /// human style (looks every 2.5 s of race time and at every draw beat; plays Evade and Reel Them In at once, else the best
    /// card at 65%+ with under 5% wreck chance). Tests and the harness use it to prove a race is playable with a mouse; the
    /// Unity race view runs it as Autoplay (A).
    /// </summary>
    public sealed class MousePlayer
    {
        readonly PlayDesk d;
        readonly HudLayout lay;
        readonly HudReadouts ro;
        readonly HudInput input;
        readonly Queue<Action> steps = new Queue<Action>();
        double nextLook;
        int gesture;

        /// <summary>Gestures made: drag-to-play, click-to-play (pick, then click again), hold the Play button, drag to the toss
        /// bin, pit clicks, and P presses to leave a draw beat.</summary>
        public int Drags, Clicks, Holds, TossDrags, PitClicks, Resumes;
        public double MinOdds = 0.65, MaxWreck = 0.05;

        public MousePlayer(PlayDesk desk, HudLayout layout, HudReadouts readouts, HudInput input)
        {
            d = desk; lay = layout; ro = readouts; this.input = input;
        }

        /// <summary>One frame: carries on with the gesture in progress, or decides what to do next.</summary>
        public void Frame()
        {
            if (steps.Count > 0) { steps.Dequeue()(); return; }
            Decide();
        }

        void Wait(int frames) { for (int i = 0; i < frames; i++) steps.Enqueue(() => { }); }

        void ClickBox(Func<Box?> box)
        {
            steps.Enqueue(() => { if (box() is Box b) { input.Move(b.CX, b.CY); input.Down(b.CX, b.CY); } });
            steps.Enqueue(() => { if (box() is Box b) input.Up(b.CX, b.CY); else input.Cancel(); });
        }

        Box? StopBox(HitKind k) { foreach (var (h, b, _) in lay.StopBoxes) if (h.Kind == k) return b; return null; }
        Box? PitBox(HitKind k) { foreach (var (h, b, _) in lay.PitBoxes) if (h.Kind == k) return b; return null; }

        /// <summary>Press on a card, slide to (tx, ty) over a few frames, let go.</summary>
        void DragCard(int uid, Func<(double x, double y)> to)
        {
            double sx = 0, sy = 0;
            steps.Enqueue(() =>
            {
                if (!(lay.CardBox(uid) is Box b)) return;
                sx = b.CX; sy = b.CY;
                input.Move(sx, sy); input.Down(sx, sy);
            });
            const int n = 6;
            for (int i = 1; i <= n; i++)
            {
                int k = i;
                steps.Enqueue(() =>
                {
                    if (d.Drag == null) return;
                    var (tx, ty) = to();
                    input.Move(sx + (tx - sx) * k / n, sy + (ty - sy) * k / n);
                });
            }
            steps.Enqueue(() => { if (d.Drag == null) return; var (tx, ty) = to(); input.Up(tx, ty); });
        }

        void Decide()
        {
            var r = d.Race;
            if (r.Done || r.Me.Dnf) return;
            var p = ro.Pit;

            // the stall: take the crew chief's plan, GO, then drop the jack as the slowest lane finishes
            if (r.Holding && r.PitGame.Auto == PitPolicy.Player)
            {
                Wait(20);
                ClickBox(() => StopBox(HitKind.PitAuto));
                ClickBox(() => StopBox(HitKind.PitGo));
                PitClicks += 2;
                return;
            }
            if (p.Stop == PitGamePhase.Work)
            {
                if (p.Elapsed >= p.Total + Math.Min(0.15, p.Window * 0.3)) { ClickBox(() => StopBox(HitKind.PitRelease)); PitClicks++; Wait(10); }
                return;
            }
            // green: PIT on the plan lap (or when the tank is nearly dry)
            if (r.Phase == RacePhase.Green && r.Me.Pit == null && !p.Requested && p.Show && r.MyPlan is PitPlan plan && r.LapOf(r.Me) >= plan.Lap)
            {
                ClickBox(() => PitBox(HitKind.PitButton)); PitClicks++;
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
                switch (gesture++ % 3)
                {
                    case 0:
                        DragCard(uid, () => (lay.Hand.CX, lay.PlayLine - 80 * lay.S));
                        Drags++;
                        break;
                    case 1:
                        ClickBox(() => lay.CardBox(uid));
                        Wait(3);
                        ClickBox(() => lay.CardBox(uid));
                        Clicks++;
                        break;
                    default:
                        ClickBox(() => lay.CardBox(uid));
                        steps.Enqueue(() => { var b = lay.PlayButton; input.Move(b.CX, b.CY); input.Down(b.CX, b.CY); });
                        Wait(8);
                        steps.Enqueue(() => { var b = lay.PlayButton; input.Up(b.CX, b.CY); });
                        Holds++;
                        break;
                }
                return;
            }
            if (!d.BeatPaused) return;
            // a draw beat with nothing worth playing: toss a dead card, or a weak one whose toss does something now, then go green
            CardFace toss = null;
            foreach (var f in d.Cards)
            {
                if (f.Filtered || f.Special || f.Def.Situational) continue;
                bool dead = f.Block == "Not enough tire left";
                if (dead || (f.TossLive && f.Odds.Succ < 0.55)) { toss = f; break; }
            }
            if (toss != null)
            {
                DragCard(toss.Uid, () => (lay.TossBin.CX, lay.TossBin.CY));
                TossDrags++;
                Wait(2);
            }
            steps.Enqueue(() => { if (d.BeatPaused) { input.Key(HudKey.Pause, true); Resumes++; } });
        }

        /// <summary>One race played by the mouse player at a screen size and playback speed. Returns the desk when it's done.</summary>
        public static (PlayDesk desk, MousePlayer bot) Run(TrackDef track, uint seed, string persona = "drafter", double w = 1920, double h = 1080,
            int speedIndex = 3, double frame = 1 / 30.0, int maxFrames = 400000)
        {
            var desk = new PlayDesk(new RaceDriver(PlayDesk.NewRace(track, seed, persona)));
            desk.Driver.SpeedIndex = speedIndex;
            var ro = new HudReadouts();
            var lay = new HudLayout();
            var input = new HudInput(desk, lay);
            var bot = new MousePlayer(desk, lay, ro, input);
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
