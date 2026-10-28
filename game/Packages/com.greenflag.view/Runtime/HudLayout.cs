using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    public enum HitKind
    {
        None, Card, Sort, Family, AllFamilies, FadeRisk, PlayButton, TossBin, Pause,
        PitButton, Strategy, CautionCall, PitCard, PitAuto, PitGo, PitRelease,
    }

    /// <summary>What is under the pointer: a kind and its argument (card uid, sort, family, pit card index, pit service...).</summary>
    public readonly struct Hit
    {
        public readonly HitKind Kind;
        public readonly int Arg;
        public Hit(HitKind k, int arg = 0) { Kind = k; Arg = arg; }
        public override string ToString() => $"{Kind}({Arg})";
    }

    /// <summary>
    /// Where everything on the race HUD sits for a screen size, as the slice's wide-screen layout (hud-modules round 2 and the
    /// hud3 console): the car dash in the bottom-left corner; the live odds gauge in the bottom-right with the beat clock and
    /// the pit module stacked above it; up to eight cards between the corners, overlapping when they don't fit, with the sort
    /// and filter chips under them; the console (Focus, combo, toss bin) above the cards with the toss tiles over it; the pit
    /// stop hand above the console while you sit in the stall. Pixels, y down. One layout serves drawing and hit testing.
    /// </summary>
    public sealed class HudLayout
    {
        public double W, H, S;
        public Box Dash, Gauge, PlayButton, Clock, PitDock, PitButton, Hand, Chips, Console, FocusBar, ComboBar, TossBin, Tiles, Booth, Toast, PitStop, PauseBox;
        /// <summary>Let go of a dragged card above this line to play it.</summary>
        public double PlayLine;
        public double CardW, CardH;
        public readonly List<(int uid, Box box)> CardBoxes = new List<(int, Box)>();
        public readonly List<(Hit hit, Box box, string label)> ChipBoxes = new List<(Hit, Box, string)>();
        public readonly List<(Hit hit, Box box, string label)> PitBoxes = new List<(Hit, Box, string)>();
        public readonly List<(Hit hit, Box box, string label)> StopBoxes = new List<(Hit, Box, string)>();

        /// <summary>Lays the HUD out for a w x h screen and the desk's current hand.</summary>
        public void Update(double w, double h, PlayDesk d, HudReadouts ro)
        {
            last = ro;
            W = w; H = h;
            S = Math.Max(0.6, Math.Min(2, h / 1080));
            double s = S, m = 16 * s, side = 300 * s;

            Dash = new Box(m, h - m - 250 * s, side, 250 * s);
            Gauge = new Box(w - m - side, h - m - 250 * s, side, 250 * s);
            PlayButton = new Box(Gauge.X + 12 * s, Gauge.Bottom - 50 * s, Gauge.W - 24 * s, 38 * s);
            Clock = new Box(Gauge.X, Gauge.Y - 8 * s - 104 * s, side, 104 * s);
            PitDock = new Box(Gauge.X, Clock.Y - 8 * s - 150 * s, side, 150 * s);

            double hx = Dash.Right + m, hw = Math.Max(200 * s, Gauge.X - m - hx);
            Chips = new Box(hx, h - m - 30 * s, hw, 30 * s);
            CardH = 196 * s; CardW = 136 * s;
            Hand = new Box(hx, Chips.Y - 8 * s - CardH, hw, CardH);
            PlayLine = Hand.Y - 14 * s;
            double cw = Math.Min(hw, 760 * s);
            Console = new Box(hx + (hw - cw) / 2, PlayLine - 10 * s - 46 * s, cw, 46 * s);
            TossBin = new Box(Console.Right - 124 * s, Console.Y + 4 * s, 120 * s, Console.H - 8 * s);
            FocusBar = new Box(Console.X + 6 * s, Console.Y + 6 * s, cw * 0.38, Console.H - 12 * s);
            ComboBar = new Box(FocusBar.Right + 10 * s, Console.Y + 6 * s, TossBin.X - FocusBar.Right - 20 * s, Console.H - 12 * s);
            Tiles = new Box(Console.X, Console.Y - 30 * s, cw, 26 * s);
            Booth = new Box(Console.X, Tiles.Y - 6 * s - 92 * s, Math.Min(cw, 560 * s), 92 * s);
            Toast = new Box(hx + (hw - Math.Min(hw, 520 * s)) / 2, Booth.Y - 70 * s, Math.Min(hw, 520 * s), 58 * s);
            PitStop = new Box(hx + (hw - Math.Min(hw, 820 * s)) / 2, Console.Y - 290 * s, Math.Min(hw, 820 * s), 280 * s);
            PauseBox = new Box((w - 460 * s) / 2, h * 0.3, 460 * s, 96 * s);

            LayCards(d);
            LayChips(d);
            LayPit(d, ro);
        }

        HudReadouts last;

        /// <summary>Lays out again at the same size after the hand changed mid-frame (a controller press played a card).</summary>
        public void Relayout(PlayDesk d) { if (last != null) Update(W, H, d, last); }

        void LayCards(PlayDesk d)
        {
            CardBoxes.Clear();
            var shown = d.Cards.FindAll(c => !c.Filtered);
            int n = shown.Count;
            if (n == 0) return;
            double gap = 8 * S;
            double step = n > 1 ? Math.Min(CardW + gap, (Hand.W - CardW) / (n - 1)) : 0;
            double total = CardW + step * (n - 1);
            double x0 = Hand.X + (Hand.W - total) / 2;
            for (int i = 0; i < n; i++)
            {
                var f = shown[i];
                double lift = f.Selected ? 18 * S : d.Hover == f.Uid ? 8 * S : 0;
                CardBoxes.Add((f.Uid, new Box(x0 + i * step, Hand.Y - lift, CardW, CardH)));
            }
        }

        void LayChips(PlayDesk d)
        {
            ChipBoxes.Clear();
            double x = Chips.X, s = S, h = Chips.H;
            void Add(Hit hit, string label, double width)
            {
                if (x + width > Chips.Right) return;
                ChipBoxes.Add((hit, new Box(x, Chips.Y, width, h), label));
                x += width + 6 * s;
            }
            Add(new Hit(HitKind.Sort, (int)HandSort.BestOdds), "Best odds", 88 * s);
            Add(new Hit(HitKind.Sort, (int)HandSort.Safest), "Safest", 66 * s);
            Add(new Hit(HitKind.Sort, (int)HandSort.Family), "Family", 66 * s);
            x += 8 * s;
            Add(new Hit(HitKind.AllFamilies), "All", 40 * s);
            foreach (var f in d.Families()) Add(new Hit(HitKind.Family, (int)f), CardRules.FamilyName(f), (CardRules.FamilyName(f).Length * 8 + 18) * s);
            x += 8 * s;
            Add(new Hit(HitKind.FadeRisk), "Fade wreck risk 5%+", 150 * s);
        }

        void LayPit(PlayDesk d, HudReadouts ro)
        {
            PitBoxes.Clear(); StopBoxes.Clear();
            var p = ro.Pit;
            double s = S, x = PitDock.X + 10 * s, y = PitDock.Y + 34 * s, w = PitDock.W - 20 * s;
            PitButton = new Box(x, y, w, 34 * s);
            if (p.Show)
            {
                if (p.Caution && !p.CallLocked)
                {
                    PitServiceRow(y, x, w);
                }
                else if (!p.Caution)
                {
                    PitBoxes.Add((new Hit(HitKind.PitButton), PitButton, p.Button));
                    double bw = (w - 6 * s) / 2, by = y + 40 * s;
                    PitBoxes.Add((new Hit(HitKind.Strategy, (int)PitStrategy.Safe), new Box(x, by, bw, 26 * s), "Safe"));
                    PitBoxes.Add((new Hit(HitKind.Strategy, (int)PitStrategy.Gamble), new Box(x + bw + 6 * s, by, bw, 26 * s), "Gamble"));
                }
            }
            if (p.Stop == PitGamePhase.Plan)
            {
                double cw = (PitStop.W - 24 * s - 8 * s * (Math.Max(1, p.Hand.Count) - 1)) / Math.Max(5, p.Hand.Count);
                for (int i = 0; i < p.Hand.Count; i++)
                    StopBoxes.Add((new Hit(HitKind.PitCard, i), new Box(PitStop.X + 12 * s + i * (cw + 8 * s), PitStop.Y + 40 * s, cw, 150 * s), p.Hand[i].name));
                double by = PitStop.Bottom - 50 * s;
                StopBoxes.Add((new Hit(HitKind.PitAuto), new Box(PitStop.Right - 12 * s - 300 * s, by, 140 * s, 38 * s), "Crew chief's plan"));
                StopBoxes.Add((new Hit(HitKind.PitGo), new Box(PitStop.Right - 12 * s - 150 * s, by, 150 * s, 38 * s), "GO"));
            }
            else if (p.Stop == PitGamePhase.Work)
                StopBoxes.Add((new Hit(HitKind.PitRelease), new Box(PitStop.CX - 130 * s, PitStop.Bottom - 56 * s, 260 * s, 44 * s), "RELEASE"));
        }

        void PitServiceRow(double y, double x, double w)
        {
            var svcs = new[] { PitService.Stay, PitService.Fuel, PitService.Two, PitService.Four };
            string[] names = { "Stay", "Fuel", "Two", "Four" };
            double bw = (w - 3 * 6 * S) / 4;
            for (int i = 0; i < 4; i++)
                PitBoxes.Add((new Hit(HitKind.CautionCall, (int)svcs[i]), new Box(x + i * (bw + 6 * S), y, bw, 34 * S), names[i]));
        }

        public Box? CardBox(int uid)
        {
            foreach (var (u, b) in CardBoxes) if (u == uid) return b;
            return null;
        }

        /// <summary>What a click at (x, y) lands on. The pit stop hand and the corner modules sit over the cards; among the
        /// cards the one drawn last (the right-hand neighbour, or the picked card) wins.</summary>
        public Hit HitTest(double x, double y, PlayDesk d)
        {
            foreach (var (hit, b, _) in StopBoxes) if (b.Contains(x, y)) return hit;
            if (d.Race.Holding && PitStop.Contains(x, y)) return new Hit(HitKind.None);
            foreach (var (hit, b, _) in PitBoxes) if (b.Contains(x, y)) return hit;
            if (PlayButton.Contains(x, y)) return new Hit(HitKind.PlayButton);
            if (TossBin.Contains(x, y)) return new Hit(HitKind.TossBin);
            foreach (var (hit, b, _) in ChipBoxes) if (b.Contains(x, y)) return hit;
            if (d.Race.Selected is int sel && CardBox(sel) is Box sb && sb.Contains(x, y)) return new Hit(HitKind.Card, sel);
            for (int i = CardBoxes.Count - 1; i >= 0; i--) if (CardBoxes[i].box.Contains(x, y)) return new Hit(HitKind.Card, CardBoxes[i].uid);
            if (d.Driver.Paused && PauseBox.Contains(x, y)) return new Hit(HitKind.Pause);
            return new Hit(HitKind.None);
        }
    }

    public enum HudKey { Space, Shift, Escape, Pause, Odds, Pit, Release, Digit }

    /// <summary>
    /// Turns pointer and key events into PlayDesk calls, through the layout's hit test. The Unity HUD forwards the engine's
    /// events here; the mouse player and the tests call the same three pointer methods.
    /// </summary>
    public sealed class HudInput
    {
        readonly PlayDesk desk;
        readonly HudLayout lay;
        bool aimMouse, spaceDown;

        public HudInput(PlayDesk desk, HudLayout layout) { this.desk = desk; lay = layout; }

        public void Down(double x, double y)
        {
            var hit = lay.HitTest(x, y, desk);
            switch (hit.Kind)
            {
                case HitKind.Card: desk.DragStart(hit.Arg, x, y); break;
                case HitKind.PlayButton: aimMouse = desk.AimStart(false); break;
                default: Activate(hit); break;
            }
        }

        /// <summary>Does what a chip, the pause box or a pit button does when clicked (or confirmed on a controller). A card is
        /// clicked (picked, or played if picked).</summary>
        public void Activate(Hit hit)
        {
            switch (hit.Kind)
            {
                case HitKind.Card: desk.Click(hit.Arg); break;
                case HitKind.Sort: desk.SetSort((HandSort)hit.Arg); break;
                case HitKind.AllFamilies: desk.SetFilter(null); break;
                case HitKind.Family: desk.SetFilter(desk.Filter == (CardFamily)hit.Arg ? (CardFamily?)null : (CardFamily)hit.Arg); break;
                case HitKind.FadeRisk: desk.ToggleFade(); break;
                case HitKind.Pause: desk.TogglePause(); break;
                case HitKind.PitButton: desk.PressPit(); break;
                case HitKind.Strategy: desk.SetStrategy((PitStrategy)hit.Arg); break;
                case HitKind.CautionCall: desk.CautionCall((PitService)hit.Arg); break;
                case HitKind.PitCard: desk.PitToggle(hit.Arg); break;
                case HitKind.PitAuto: desk.PitAuto(); break;
                case HitKind.PitGo: desk.PitGo(); break;
                case HitKind.PitRelease: desk.PitRelease(); break;
            }
        }

        public void Move(double x, double y)
        {
            if (desk.Drag != null) desk.DragMove(x, y, y < lay.PlayLine, lay.TossBin.Inflate(10 * lay.S).Contains(x, y));
            if (desk.Aim != null && aimMouse) desk.AimOut(!lay.PlayButton.Inflate(6 * lay.S).Contains(x, y));
            var hit = lay.HitTest(x, y, desk);
            desk.Hover = desk.Drag == null && hit.Kind == HitKind.Card ? hit.Arg : (int?)null;
        }

        public void Up(double x, double y)
        {
            if (desk.Drag != null) { Move(x, y); desk.DragEnd(false); }
            if (desk.Aim != null && aimMouse) { aimMouse = false; desk.AimEnd(desk.Aim.Out); }
        }

        /// <summary>The window lost focus or the pointer was cancelled: drop whatever is held.</summary>
        public void Cancel()
        {
            if (desk.Drag != null) desk.DragEnd(true);
            if (desk.Aim != null) { aimMouse = false; desk.AimEnd(true); }
        }

        /// <summary>Space hold-to-play (Shift held cancels the release), Esc, P pause, O odds, B pit, R release, 1-9 cards.</summary>
        public void Key(HudKey k, bool down, bool shift = false, int digit = 0)
        {
            switch (k)
            {
                case HudKey.Space:
                    if (down && spaceDown) { if (desk.Aim != null && desk.Aim.Key) desk.AimOut(shift); break; }   // key repeat
                    spaceDown = down;
                    if (down)
                    {
                        if (desk.Aim == null && !desk.AimStart(true) && desk.Driver.Paused) desk.TogglePause();
                        if (desk.Aim != null && desk.Aim.Key) desk.AimOut(shift);
                    }
                    else if (desk.Aim != null && desk.Aim.Key) desk.AimEnd(shift || desk.Aim.Out);
                    break;
                case HudKey.Shift: if (desk.Aim != null && desk.Aim.Key) desk.AimOut(down); break;
                case HudKey.Escape: if (down) desk.Cancel(); break;
                case HudKey.Pause: if (down) desk.TogglePause(); break;
                case HudKey.Odds: if (down) desk.ToggleOdds(); break;
                case HudKey.Pit: if (down) desk.PressPit(); break;
                case HudKey.Release: if (down) desk.PitRelease(); break;
                case HudKey.Digit: if (down) desk.Key(digit); break;
            }
        }
    }
}
