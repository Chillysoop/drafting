using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>What a controller (or the keyboard without a mouse) asks the HUD to do. The Unity side turns physical buttons
    /// into these through PadMap, so a Nintendo pad confirms with the right-hand button and every other pad with the bottom one.</summary>
    public enum PadButton
    {
        Up, Down, Left, Right,
        /// <summary>A: pick the focused card, play it if it is already picked, or press the focused chip or pit button.</summary>
        Confirm,
        /// <summary>B: put the card down, cancel an aim, or take the focus back to the hand.</summary>
        Back,
        /// <summary>X: toss the focused card (it charges the Crew Board and does its toss effect).</summary>
        Toss,
        /// <summary>Y: the PIT button (on green ask for the stop, under caution switch between staying out and the stop).</summary>
        Pit,
        /// <summary>Right trigger, held: hold to play the focused card; letting go plays it.</summary>
        Aim,
        /// <summary>Left trigger, held while aiming: letting go of the right trigger cancels.</summary>
        AimCancel,
        /// <summary>Menu / +: pause or resume (also leaves a draw beat).</summary>
        Pause,
        /// <summary>View / −: odds on or off.</summary>
        Odds,
        /// <summary>Left shoulder: next sort (Best odds, Safest, Family). While a crew call is on the radio: No thanks.</summary>
        Sort,
        /// <summary>Right shoulder: next family filter (All, then each family in hand). While a risky crew call is on the radio: Try it.</summary>
        Filter,
    }

    /// <summary>The buttons a pad has, by where they sit (Unity's Input System names them the same way).</summary>
    public enum PadPhysical
    {
        DpadUp, DpadDown, DpadLeft, DpadRight, South, East, West, North,
        LeftShoulder, RightShoulder, LeftTrigger, RightTrigger, Start, Select,
    }

    /// <summary>Which prompts to show: the glyphs printed on the pad in hand, or the keyboard keys.</summary>
    public enum PadStyle { Keyboard, Xbox, PlayStation, Nintendo }

    /// <summary>Physical button to HUD action. Nintendo pads (Switch, Pro Controller, Joy-Con) confirm with the right-hand
    /// button (A) and go back with the bottom one (B), as every Switch game does; the others the other way round.</summary>
    public static class PadMap
    {
        public static PadButton? Of(PadStyle style, PadPhysical p)
        {
            bool nin = style == PadStyle.Nintendo;
            switch (p)
            {
                case PadPhysical.DpadUp: return PadButton.Up;
                case PadPhysical.DpadDown: return PadButton.Down;
                case PadPhysical.DpadLeft: return PadButton.Left;
                case PadPhysical.DpadRight: return PadButton.Right;
                case PadPhysical.South: return nin ? PadButton.Back : PadButton.Confirm;
                case PadPhysical.East: return nin ? PadButton.Confirm : PadButton.Back;
                case PadPhysical.West: return PadButton.Toss;
                case PadPhysical.North: return PadButton.Pit;
                case PadPhysical.LeftShoulder: return PadButton.Sort;
                case PadPhysical.RightShoulder: return PadButton.Filter;
                case PadPhysical.LeftTrigger: return PadButton.AimCancel;
                case PadPhysical.RightTrigger: return PadButton.Aim;
                case PadPhysical.Start: return PadButton.Pause;
                case PadPhysical.Select: return PadButton.Odds;
            }
            return null;
        }

        /// <summary>The pad style from what the device calls itself (Unity's layout name, product and manufacturer).</summary>
        public static PadStyle StyleOf(string layout, string product, string manufacturer, bool onSwitch)
        {
            if (onSwitch) return PadStyle.Nintendo;
            string all = ((layout ?? "") + " " + (product ?? "") + " " + (manufacturer ?? "")).ToLowerInvariant();
            if (all.Contains("nintendo") || all.Contains("switch") || all.Contains("joy-con") || all.Contains("joycon") || all.Contains("npad") || all.Contains("pro controller")) return PadStyle.Nintendo;
            if (all.Contains("dualshock") || all.Contains("dualsense") || all.Contains("playstation") || all.Contains("sony")) return PadStyle.PlayStation;
            return PadStyle.Xbox;
        }
    }

    /// <summary>What to print for each action on each pad.</summary>
    public static class PadGlyphs
    {
        public static string Of(PadStyle style, PadButton b)
        {
            switch (style)
            {
                case PadStyle.Keyboard:
                    switch (b)
                    {
                        case PadButton.Confirm: return "Enter";
                        case PadButton.Back: return "Esc";
                        case PadButton.Toss: return "X";
                        case PadButton.Pit: return "B";
                        case PadButton.Aim: return "Space";
                        case PadButton.AimCancel: return "Shift";
                        case PadButton.Pause: return "P";
                        case PadButton.Odds: return "O";
                        case PadButton.Sort: return "Tab";
                        case PadButton.Filter: return "Q";
                        default: return "Arrows";
                    }
                case PadStyle.PlayStation:
                    switch (b)
                    {
                        case PadButton.Confirm: return "Cross";
                        case PadButton.Back: return "Circle";
                        case PadButton.Toss: return "Square";
                        case PadButton.Pit: return "Triangle";
                        case PadButton.Aim: return "R2";
                        case PadButton.AimCancel: return "L2";
                        case PadButton.Pause: return "Options";
                        case PadButton.Odds: return "Create";
                        case PadButton.Sort: return "L1";
                        case PadButton.Filter: return "R1";
                        default: return "D-pad";
                    }
                case PadStyle.Nintendo:
                    switch (b)
                    {
                        case PadButton.Confirm: return "A";
                        case PadButton.Back: return "B";
                        case PadButton.Toss: return "Y";
                        case PadButton.Pit: return "X";
                        case PadButton.Aim: return "ZR";
                        case PadButton.AimCancel: return "ZL";
                        case PadButton.Pause: return "+";
                        case PadButton.Odds: return "−";
                        case PadButton.Sort: return "L";
                        case PadButton.Filter: return "R";
                        default: return "Stick";
                    }
                default:
                    switch (b)
                    {
                        case PadButton.Confirm: return "A";
                        case PadButton.Back: return "B";
                        case PadButton.Toss: return "X";
                        case PadButton.Pit: return "Y";
                        case PadButton.Aim: return "RT";
                        case PadButton.AimCancel: return "LT";
                        case PadButton.Pause: return "Menu";
                        case PadButton.Odds: return "View";
                        case PadButton.Sort: return "LB";
                        case PadButton.Filter: return "RB";
                        default: return "D-pad";
                    }
            }
        }
    }

    /// <summary>
    /// A held direction (d-pad or stick) as presses: one at once, then after 0.35 s one every 0.11 s. The stick counts past
    /// half way, along its bigger axis.
    /// </summary>
    public sealed class PadRepeat
    {
        public const double Delay = 0.35, Every = 0.11, Dead = 0.5;
        PadButton? held;
        double t;

        public static PadButton? Dir(double x, double y)
        {
            if (Math.Abs(x) < Dead && Math.Abs(y) < Dead) return null;
            if (Math.Abs(x) >= Math.Abs(y)) return x > 0 ? PadButton.Right : PadButton.Left;
            return y > 0 ? PadButton.Up : PadButton.Down;   // stick y is up-positive
        }

        /// <summary>The direction held this frame (null = none); returns the press to make, if any.</summary>
        public PadButton? Step(PadButton? dir, double dt)
        {
            if (dir != held) { held = dir; t = 0; return dir; }
            if (dir == null) return null;
            t += dt;
            if (t < Delay) return null;
            t -= Every;
            return dir;
        }
    }

    /// <summary>
    /// Controller and keyboard-only play (roadmap step 32). A focus moves over the HUD's controls (the cards, the sort and
    /// filter chips, the pit module, and the pit stop hand while you sit in the stall) with the d-pad, the stick or the arrow
    /// keys, by where they sit on screen; Confirm does what a click would. Toss, PIT, hold to play, pause, odds, sort and filter
    /// have their own buttons, so nothing needs a pointer. While a pad drives, the focused card is the hovered one (the gauge
    /// previews it, the card lifts, the tip shows). Engine-free and tested: the Unity HUD feeds it, PadPlayer plays whole
    /// races through it.
    /// </summary>
    public sealed class PadInput
    {
        readonly PlayDesk desk;
        readonly HudLayout lay;
        readonly HudInput input;

        /// <summary>A pad or the arrow keys are driving: the focus ring shows and the pointer's hover is ignored. A pointer
        /// move turns it off; the first direction pressed after that only shows the ring where it was.</summary>
        public bool Active;
        public PadStyle Style = PadStyle.Xbox;
        /// <summary>What has the focus (Kind None when there is nothing to focus).</summary>
        public Hit Focus { get; private set; }
        /// <summary>The focus's box this frame (cards at their resting height).</summary>
        public Box? FocusBox { get; private set; }
        /// <summary>Presses that reached the HUD (the pad player's report).</summary>
        public int Presses;

        int cardIndex;
        bool aimHeld, cancelHeld;
        readonly List<(Hit hit, Box box)> items = new List<(Hit, Box)>();

        public PadInput(PlayDesk desk, HudLayout layout, HudInput input) { this.desk = desk; lay = layout; this.input = input; Focus = new Hit(HitKind.None); }

        /// <summary>The controls the focus can land on now: only the pit stop hand while it is up, else the cards, chips and
        /// pit module. Cards are placed at the hand's resting line, so a lifted card doesn't count as "up".</summary>
        public List<(Hit hit, Box box)> Items()
        {
            items.Clear();
            if (lay.StopBoxes.Count > 0)
            {
                foreach (var (h, b, _) in lay.StopBoxes) items.Add((h, b));
                return items;
            }
            foreach (var (u, b) in lay.CardBoxes) items.Add((new Hit(HitKind.Card, u), new Box(b.X, lay.Hand.Y, b.W, b.H)));
            foreach (var (h, b, _) in lay.ChipBoxes) items.Add((h, b));
            foreach (var (h, b, _) in lay.PitBoxes) items.Add((h, b));
            foreach (var (h, b, _) in lay.RadioBoxes) items.Add((h, b));
            return items;
        }

        static bool Same(Hit a, Hit b) => a.Kind == b.Kind && a.Arg == b.Arg;

        int IndexOf(Hit h) { for (int i = 0; i < items.Count; i++) if (Same(items[i].hit, h)) return i; return -1; }

        /// <summary>
        /// Call each frame after the layout: keeps the focus on something that exists (a played card hands it to the card now
        /// in its place, the pit stop hand takes it while it is up and gives it back to the hand), and while a pad drives,
        /// makes the focused card the hovered one.
        /// </summary>
        public void Frame()
        {
            Items();
            int i = IndexOf(Focus);
            if (i < 0)
            {
                if (lay.StopBoxes.Count > 0)
                {
                    // the stall: start on the first pit card (or RELEASE once the crew is working)
                    i = 0;
                }
                else if (lay.CardBoxes.Count > 0)
                {
                    int k = Math.Max(0, Math.Min(cardIndex, lay.CardBoxes.Count - 1));
                    i = IndexOf(new Hit(HitKind.Card, lay.CardBoxes[k].uid));
                }
                else i = items.Count > 0 && Focus.Kind != HitKind.None && Focus.Kind != HitKind.Card ? 0 : -1;
            }
            if (i >= 0) { Focus = items[i].hit; FocusBox = items[i].box; }
            else { Focus = new Hit(HitKind.None); FocusBox = null; }
            if (Focus.Kind == HitKind.Card)
                for (int k = 0; k < lay.CardBoxes.Count; k++) if (lay.CardBoxes[k].uid == Focus.Arg) cardIndex = k;
            if (Active) desk.Hover = Focus.Kind == HitKind.Card && desk.Drag == null ? Focus.Arg : (int?)null;
        }

        /// <summary>A button went down or up. Directions, Confirm, Back, Toss, PIT, pause, odds, sort and filter act on the way
        /// down; the triggers act both ways (hold to play).</summary>
        public void Press(PadButton b, bool down, bool activate = true)
        {
            if (b == PadButton.Aim) { AimButton(down); if (down && activate) Active = true; return; }
            if (b == PadButton.AimCancel) { cancelHeld = down; if (desk.Aim != null && desk.Aim.Key) desk.AimOut(down); return; }
            if (!down) return;
            Frame();
            bool wasActive = Active;
            if (activate) Active = true;
            Presses++;
            switch (b)
            {
                case PadButton.Up: case PadButton.Down: case PadButton.Left: case PadButton.Right:
                    if (wasActive || !activate) Move(b);
                    break;
                case PadButton.Confirm:
                    if (Focus.Kind == HitKind.None) { if (desk.BeatPaused) desk.TogglePause(); break; }
                    input.Activate(Focus);
                    break;
                case PadButton.Back:
                    if (desk.Aim != null || desk.Drag != null || desk.Race.Selected != null) desk.Cancel();
                    else if (Focus.Kind != HitKind.Card && lay.StopBoxes.Count == 0) Focus = new Hit(HitKind.None);
                    break;
                case PadButton.Toss:
                    if (Focus.Kind == HitKind.Card) desk.Toss(Focus.Arg);
                    else if (desk.Race.Selected is int s) desk.Toss(s);
                    break;
                case PadButton.Pit: desk.PressPit(); break;
                case PadButton.Pause: desk.TogglePause(); break;
                case PadButton.Odds: desk.ToggleOdds(); break;
                // a crew call lasts seconds, so the shoulders answer it while it is up instead of walking the focus there
                case PadButton.Sort when desk.Race.Radio != null: desk.PassCall(); break;
                case PadButton.Filter when desk.RiskyCall: desk.TakeCall(); break;
                case PadButton.Sort: desk.SetSort((HandSort)(((int)desk.Sort + 1) % 3)); break;
                case PadButton.Filter:
                {
                    var fams = desk.Families();
                    int at = desk.Filter is CardFamily f ? fams.IndexOf(f) : -1;
                    desk.SetFilter(at + 1 < fams.Count ? fams[at + 1] : (CardFamily?)null);
                    break;
                }
            }
            lay.Relayout(desk);
            Frame();
        }

        /// <summary>Right trigger: aims the focused card if it can be played (else the picked or best card, as Space does); a
        /// draw beat or a pause goes green while you hold; letting go plays it unless the left trigger is held.</summary>
        void AimButton(bool down)
        {
            if (down == aimHeld) return;
            aimHeld = down;
            if (down)
            {
                if (desk.Aim != null) return;
                if (Focus.Kind == HitKind.Card && desk.Face(Focus.Arg) is CardFace f && f.Block == null && desk.Race.Selected != f.Uid) desk.Select(f.Uid);
                if (!desk.AimStart(true) && desk.Driver.Paused) desk.TogglePause();
                if (desk.Aim != null) desk.AimOut(cancelHeld);
            }
            else if (desk.Aim != null && desk.Aim.Key) desk.AimEnd(cancelHeld || desk.Aim.Out);
        }

        /// <summary>Moves the focus to the nearest control that way. Left and right stay on the row (the boxes must overlap
        /// in height); up and down take the closest one above or below, preferring the one straight above or below.</summary>
        void Move(PadButton dir)
        {
            Items();
            if (items.Count == 0) return;
            int from = IndexOf(Focus);
            if (from < 0) { Frame(); from = IndexOf(Focus); if (from < 0) { Focus = items[0].hit; return; } }
            var a = items[from].box;
            double best = double.MaxValue; int pick = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (i == from) continue;
                var c = items[i].box;
                double dx = c.CX - a.CX, dy = c.CY - a.CY, score;
                switch (dir)
                {
                    case PadButton.Left:
                    case PadButton.Right:
                        if (dir == PadButton.Left ? dx >= -1 : dx <= 1) continue;
                        if (Math.Abs(dy) >= (a.H + c.H) / 2) continue;
                        score = Math.Abs(dx) + 4 * Math.Abs(dy);
                        break;
                    default:
                        if (dir == PadButton.Up ? dy >= -1 : dy <= 1) continue;
                        score = Math.Abs(dy) + 2 * Math.Abs(dx);
                        break;
                }
                if (score < best) { best = score; pick = i; }
            }
            if (pick >= 0) Focus = items[pick].hit;
            FocusBox = items[pick >= 0 ? pick : from].box;
        }
    }

    /// <summary>The button prompts to show for what the focus is on now: one line of (glyph, what it does).</summary>
    public static class PadPrompts
    {
        public static List<(string glyph, string label)> For(PadInput pad, PlayDesk d, HudLayout lay)
        {
            var l = new List<(string, string)>();
            var st = pad.Style;
            void Add(PadButton b, string label) => l.Add((PadGlyphs.Of(st, b), label));
            var f = pad.Focus;
            if (d.Race.PitGame != null && d.Race.PitGame.Phase == PitGamePhase.Work && f.Kind == HitKind.PitRelease)
            {
                Add(PadButton.Confirm, "Drop the jack");
                return l;
            }
            if (lay.StopBoxes.Count > 0)
            {
                Add(PadButton.Up, "Move");
                Add(PadButton.Confirm, f.Kind == HitKind.PitCard ? "Use / remove card" : f.Kind == HitKind.PitAuto ? "Crew chief's plan" : f.Kind == HitKind.PitGo ? "GO" : "Select");
                Add(PadButton.Pause, d.Driver.Paused ? "Resume" : "Pause");
                return l;
            }
            Add(PadButton.Up, "Move");
            switch (f.Kind)
            {
                case HitKind.Card:
                    var face = d.Face(f.Arg);
                    Add(PadButton.Confirm, d.Race.Selected == f.Arg ? "Play" : "Pick");
                    if (face != null && face.Block == null) Add(PadButton.Aim, "Hold to play");
                    if (face != null && !face.Special) Add(PadButton.Toss, "Toss");
                    break;
                case HitKind.None: break;
                default:
                    string label = null;
                    foreach (var (h, _, lb) in lay.ChipBoxes) if (h.Kind == f.Kind && h.Arg == f.Arg) label = lb;
                    foreach (var (h, _, lb) in lay.PitBoxes) if (h.Kind == f.Kind && h.Arg == f.Arg) label = lb;
                    foreach (var (h, _, lb) in lay.RadioBoxes) if (h.Kind == f.Kind && h.Arg == f.Arg) label = lb;
                    Add(PadButton.Confirm, label ?? "Select");
                    break;
            }
            if (d.Race.Selected != null || d.Aim != null) Add(PadButton.Back, d.Aim != null ? "Cancel" : "Put down");
            Add(PadButton.Pit, "PIT");
            Add(PadButton.Pause, d.BeatPaused ? "Go green" : d.Driver.Paused ? "Resume" : "Pause");
            var radio = d.Race.Radio;
            if (radio != null && st == PadStyle.Keyboard)
            {
                if (d.RiskyCall) l.Add(("Y", $"Try the call ({radio.Chance}%)"));
                l.Add(("N", d.RiskyCall ? "No thanks" : "Got it"));
            }
            else if (radio != null)
            {
                if (d.RiskyCall) Add(PadButton.Filter, $"Try the call ({radio.Chance}%)");
                Add(PadButton.Sort, d.RiskyCall ? "No thanks" : "Got it");
            }
            else
            {
                Add(PadButton.Sort, "Sort");
                Add(PadButton.Filter, "Filter");
            }
            return l;
        }
    }
}
