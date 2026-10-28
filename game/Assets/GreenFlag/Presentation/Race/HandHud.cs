using GreenFlag.Sim;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// The hand and beats HUD (roadmap step 31), in immediate-mode GUI like the rest of the race view: the hand of up to eight
    /// odds-first cards with sort and filter chips under them, the console (Focus bar, combo meter, toss bin) and toss tiles
    /// above, the car dash bottom left, the live odds gauge with its hold-to-play button bottom right, the beat clock and pit
    /// module above it, the pit stop hand while you are in the stall, the draw-beat pause and toasts. Where things sit comes
    /// from HudLayout and what they say from HudReadouts (both engine-free and tested); pointer and key events go to HudInput.
    /// The look follows the slice's dash (dark panels, yellow plates, family colours); the retro art pass (step 45) reskins it.
    /// </summary>
    public sealed class HandHud
    {
        readonly RaceView view;
        GUIStyle txt, txtC, txtR, small, smallC, tiny, tinyC, big, bigC, huge, plate, wrap;
        float sc = -1;
        bool mouseHeld;
        Vector2 lastMouse = new Vector2(-1, -1);
        readonly System.Collections.Generic.HashSet<KeyCode> keysDown = new System.Collections.Generic.HashSet<KeyCode>();

        static readonly Color PanelC = new Color(0.043f, 0.07f, 0.125f, 0.9f), CardC = new Color(0.078f, 0.1f, 0.15f, 0.97f), Line = new Color(1, 1, 1, 0.12f);
        static readonly Color Gold = new Color(0.91f, 0.71f, 0.18f), Dim = new Color(0.6f, 0.64f, 0.71f), GreenC = new Color(0.18f, 0.7f, 0.42f), RedC = new Color(0.9f, 0.28f, 0.3f), Amber = new Color(0.94f, 0.54f, 0.14f), Blue = new Color(0.29f, 0.66f, 1f), FocusC = new Color(0.5f, 0.83f, 1f);

        public HandHud(RaceView view) { this.view = view; }

        static Rect R(Box b) => new Rect((float)b.X, (float)b.Y, (float)b.W, (float)b.H);
        static Color C(Rgb c) => Kit.C(c);

        void Styles(float s)
        {
            if (txt != null && Mathf.Approximately(s, sc)) return;
            sc = s;
            GUIStyle Make(int size, FontStyle fs, TextAnchor a, bool wrapOn = false)
            {
                var g = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size * s)), fontStyle = fs, alignment = a, wordWrap = wrapOn, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
                g.normal.textColor = Color.white;
                return g;
            }
            txt = Make(15, FontStyle.Bold, TextAnchor.MiddleLeft);
            txtC = Make(15, FontStyle.Bold, TextAnchor.MiddleCenter);
            txtR = Make(15, FontStyle.Bold, TextAnchor.MiddleRight);
            small = Make(12, FontStyle.Normal, TextAnchor.MiddleLeft);
            smallC = Make(12, FontStyle.Normal, TextAnchor.MiddleCenter);
            tiny = Make(10, FontStyle.Bold, TextAnchor.MiddleLeft);
            tinyC = Make(10, FontStyle.Bold, TextAnchor.MiddleCenter);
            big = Make(26, FontStyle.Bold, TextAnchor.MiddleLeft);
            bigC = Make(30, FontStyle.Bold, TextAnchor.MiddleCenter);
            huge = Make(44, FontStyle.Bold, TextAnchor.MiddleCenter);
            plate = Make(11, FontStyle.Bold, TextAnchor.MiddleCenter);
            plate.normal.textColor = Color.black;
            wrap = Make(11, FontStyle.Normal, TextAnchor.UpperLeft, true);
        }

        // ------------------------------------------------------------------ drawing helpers

        static void Fill(Rect r, Color c)
        {
            if (c.a <= 0) return;
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Outline(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        static void Text(Rect r, string s, GUIStyle st, Color c)
        {
            var old = st.normal.textColor; st.normal.textColor = c;
            GUI.Label(r, s, st);
            st.normal.textColor = old;
        }

        void Panel(Rect r, string title = null)
        {
            Fill(r, PanelC);
            Outline(r, Line, 1);
            if (title == null) return;
            float s = sc;
            var pr = new Rect(r.x + 8 * s, r.y - 8 * s, Mathf.Min(r.width - 16 * s, (title.Length * 7 + 18) * s), 16 * s);
            Fill(pr, Gold);
            GUI.Label(pr, title, plate);
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        /// <summary>The five outcome bands, critical success on the left as on the slice's cards.</summary>
        void Bands(Rect r, Odds o, int baseSucc = -1)
        {
            Fill(r, new Color(1, 1, 1, 0.06f));
            float x = r.x;
            for (int i = 4; i >= 0; i--)
            {
                float w = (float)(o.P[i]) * r.width;
                if (w <= 0) continue;
                Fill(new Rect(x, r.y, w, r.height), C(CardStyle.Tier((Tier)i)));
                x += w;
            }
            if (baseSucc >= 0 && Mathf.Abs(baseSucc - (float)o.Succ * 100) >= 3)
                Fill(new Rect(r.x + baseSucc / 100f * r.width - 1, r.y - 2 * sc, 2, r.height + 4 * sc), Color.white);
        }

        // ------------------------------------------------------------------ the frame

        public void Draw()
        {
            var desk = view.Desk;
            if (desk == null || !desk.Race.CardsOn) return;
            var lay = view.Layout;
            var ro = view.Readouts;
            lay.Update(Screen.width, Screen.height, desk, ro);
            Styles((float)lay.S);
            Events(desk, lay);
            if (Event.current.type != EventType.Repaint) return;
            var pad = view.Pad;
            var mp = Event.current.mousePosition;
            // a pointer that moves takes over from the pad: the focus ring hides and hover follows the mouse again
            if (lastMouse.x >= 0 && (mp - lastMouse).sqrMagnitude > 9 && pad != null) pad.Active = false;
            lastMouse = mp;
            if (!mouseHeld && !view.Autoplay && (pad == null || !pad.Active)) view.HudInput.Move(mp.x, mp.y);

            if (desk.Slowing) SlowTint();
            Dash(ro.Dash, lay);
            Gauge(ro.Gauge, lay);
            Clock(ro.Clock, lay);
            PitDock(ro.Pit, lay);
            Console(desk, ro, lay);
            Chips(desk, lay);
            Hand(desk, lay);
            Radio(ro.Radio, lay);
            if (ro.Pit.Stop != null && (ro.Pit.Stop != PitGamePhase.Done || ro.Pit.Result != null)) PitStop(ro.Pit, lay);
            Pause(desk, lay);
            ToastBox(desk, lay);
            DragGhost(desk, lay);
            if (pad != null && pad.Active && !view.Autoplay) { FocusRing(desk, pad, lay); Prompts(desk, pad, lay); }
        }

        void Events(PlayDesk desk, HudLayout lay)
        {
            var e = Event.current;
            var inp = view.HudInput;
            if (view.Autoplay) return;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    if (view.Pad != null) view.Pad.Active = false;
                    mouseHeld = true; inp.Move(e.mousePosition.x, e.mousePosition.y); inp.Down(e.mousePosition.x, e.mousePosition.y);
                    if (lay.HitTest(e.mousePosition.x, e.mousePosition.y, desk).Kind != HitKind.None || desk.Drag != null || desk.Aim != null) e.Use();
                    break;
                case EventType.MouseDrag when e.button == 0:
                    inp.Move(e.mousePosition.x, e.mousePosition.y); e.Use(); break;
                case EventType.MouseUp when e.button == 0:
                    mouseHeld = false; inp.Up(e.mousePosition.x, e.mousePosition.y); e.Use(); break;
                case EventType.KeyDown:
                case EventType.KeyUp:
                    bool down = e.type == EventType.KeyDown;
                    var pad = view.Pad;
                    // arrows repeat while held (the OS key repeat); the other keys act once per press
                    void Kb(PadButton b)
                    {
                        if (pad == null) return;
                        e.Use();
                        bool dir = b == PadButton.Up || b == PadButton.Down || b == PadButton.Left || b == PadButton.Right;
                        if (!dir && down && !keysDown.Add(e.keyCode)) return;
                        if (!down) keysDown.Remove(e.keyCode);
                        pad.Style = PadStyle.Keyboard;
                        pad.Press(b, down);
                    }
                    switch (e.keyCode)
                    {
                        // keyboard-only play (step 32): arrows move the focus, Enter does what a click would (it drops the
                        // jack in the stall, where RELEASE is all there is), X tosses, Tab sorts, Q filters
                        case KeyCode.UpArrow: Kb(PadButton.Up); break;
                        case KeyCode.DownArrow: Kb(PadButton.Down); break;
                        case KeyCode.LeftArrow: Kb(PadButton.Left); break;
                        case KeyCode.RightArrow: Kb(PadButton.Right); break;
                        case KeyCode.Return: case KeyCode.KeypadEnter: if (pad != null) Kb(PadButton.Confirm); else { inp.Key(HudKey.Release, down); e.Use(); } break;
                        case KeyCode.X: Kb(PadButton.Toss); break;
                        case KeyCode.Tab: Kb(PadButton.Sort); break;
                        case KeyCode.Q: Kb(PadButton.Filter); break;
                        case KeyCode.Escape: if (pad != null) { pad.Press(PadButton.Back, down, false); e.Use(); } else { inp.Key(HudKey.Escape, down); e.Use(); } break;
                        case KeyCode.Space: inp.Key(HudKey.Space, down, e.shift); e.Use(); break;
                        case KeyCode.LeftShift: case KeyCode.RightShift: inp.Key(HudKey.Shift, down); break;
                        case KeyCode.P: inp.Key(HudKey.Pause, down); e.Use(); break;
                        case KeyCode.O: inp.Key(HudKey.Odds, down); e.Use(); break;
                        case KeyCode.B: inp.Key(HudKey.Pit, down); e.Use(); break;
                        // the slice answers the radio with A / X; here A is autoplay and X tosses, so Y tries the call and N turns it down
                        case KeyCode.Y: inp.Key(HudKey.RadioTake, down); e.Use(); break;
                        case KeyCode.N: inp.Key(HudKey.RadioPass, down); e.Use(); break;
                        default:
                            if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9) { inp.Key(HudKey.Digit, down, false, e.keyCode - KeyCode.Alpha0); e.Use(); }
                            break;
                    }
                    break;
            }
        }

        /// <summary>The window lost focus: drop a held card or a held Play button.</summary>
        public void LostFocus() { mouseHeld = false; keysDown.Clear(); view.HudInput?.Cancel(); }

        // ------------------------------------------------------------------ controller focus and prompts (step 32)

        void FocusRing(PlayDesk desk, PadInput pad, HudLayout lay)
        {
            if (!(pad.FocusBox is Box fb)) return;
            float s = sc;
            // a card is drawn lifted when it is hovered or picked, so ring the card where it is drawn
            var b = pad.Focus.Kind == HitKind.Card && lay.CardBox(pad.Focus.Arg) is Box cb ? cb : fb;
            var r = R(b.Inflate(4 * s));
            float pulse = 0.65f + 0.35f * Mathf.Sin((float)desk.Clock * 6f);
            Outline(r, A(Color.white, pulse), 3 * s);
            Outline(new Rect(r.x - 2 * s, r.y - 2 * s, r.width + 4 * s, r.height + 4 * s), A(FocusC, 0.5f * pulse), 1.5f * s);
        }

        /// <summary>The button prompts for what the focus is on, in a strip across the top (above the race banners).</summary>
        void Prompts(PlayDesk desk, PadInput pad, HudLayout lay)
        {
            float s = sc;
            var list = PadPrompts.For(pad, desk, lay);
            float x = 0, h = 26 * s;
            var widths = new float[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var (g, l) = list[i];
                widths[i] = (Mathf.Max(2, g.Length) * 9 + 14 + l.Length * 7.5f + 22) * s;
                x += widths[i];
            }
            var strip = new Rect((Screen.width - x) / 2 - 8 * s, 18 * s, x + 16 * s, h + 8 * s);
            if (strip.x < (float)lay.Dash.X + 290 * s) strip.x = (float)lay.Dash.X + 290 * s;   // keep clear of the pylon
            Fill(strip, PanelC);
            float cx = strip.x + 8 * s;
            for (int i = 0; i < list.Count; i++)
            {
                var (g, l) = list[i];
                float gw = (Mathf.Max(2, g.Length) * 9 + 8) * s;
                var gr = new Rect(cx, strip.y + 4 * s, gw, h);
                Fill(gr, Gold);
                GUI.Label(gr, g, plate);
                Text(new Rect(gr.xMax + 6 * s, gr.y, widths[i] - gw - 6 * s, h), l, small, Color.white);
                cx += widths[i];
            }
        }

        void SlowTint()
        {
            float s = sc;
            var c = new Color(0.3f, 0.6f, 1f, 0.10f);
            Fill(new Rect(0, 0, Screen.width, 10 * s), c);
            Fill(new Rect(0, Screen.height - 10 * s, Screen.width, 10 * s), c);
            Fill(new Rect(0, 0, 10 * s, Screen.height), c);
            Fill(new Rect(Screen.width - 10 * s, 0, 10 * s, Screen.height), c);
            Text(new Rect(0, 10 * s, Screen.width, 24 * s), "SLOW-MO", txtC, A(FocusC, 0.85f));
        }

        // ------------------------------------------------------------------ the hand

        void Hand(PlayDesk desk, HudLayout lay)
        {
            int? top = desk.Race.Selected;
            foreach (var (uid, box) in lay.CardBoxes) if (uid != top) Card(desk, desk.Face(uid), R(box));
            if (top is int t && lay.CardBox(t) is Box tb) Card(desk, desk.Face(t), R(tb));
            if (desk.Hover is int h && desk.Drag == null && desk.Aim == null && lay.CardBox(h) is Box hb) Tip(desk.Face(h), R(hb));
        }

        /// <summary>One card face. The ghost is the card under the pointer while it is dragged, drawn at full strength (the
        /// copy left in the hand is faded).</summary>
        void Card(PlayDesk desk, CardFace f, Rect r, bool ghost = false)
        {
            if (f == null) return;
            float s = sc;
            bool dragging = !ghost && desk.Drag != null && desk.Drag.On && desk.Drag.Uid == f.Uid;
            float alpha = ghost ? 1 : dragging ? 0.3f : f.Faded ? 0.35f : f.Off ? 0.55f : 1;
            var fam = C(f.Color);
            var ink = C(CardStyle.FamilyInk(f.Family));
            Fill(r, A(CardC, alpha));
            Outline(r, A(f.Selected ? Gold : f.Net >= 5 ? GreenC : f.Net <= -5 ? RedC : ink, alpha), f.Selected ? 3 * s : 1.5f * s);

            // top row: key, family tile, costs
            float x = r.x + 6 * s, y = r.y + 6 * s;
            if (f.Key > 0) Text(new Rect(x, y, 12 * s, 18 * s), f.Key.ToString(), tiny, A(Dim, alpha));
            var tile = new Rect(x + 12 * s, y, 22 * s, 18 * s);
            Fill(tile, A(fam, alpha));
            Text(tile, CardRules.FamilyName(f.Family).Substring(0, 1), tinyC, A(Color.white, alpha));
            string cost = (f.TireCost > 0 ? $"T{f.TireCost}% " : "") + (f.MomCost > 0 ? $"M{f.MomCost} " : "") + (f.Heats ? "H+1" : "");
            Text(new Rect(tile.xMax + 4 * s, y, r.xMax - tile.xMax - 10 * s, 18 * s), cost.Trim(), tiny, A(new Color(1f, 0.8f, 0.5f), alpha));
            if (f.Combo > 0 || f.Boost > 0)
            {
                var badge = new Rect(r.x + 8 * s, r.y - 16 * s, r.width - 16 * s, 16 * s);
                Fill(badge, A(f.Combo > 0 ? Blue : Gold, alpha));
                Text(badge, (f.Combo > 0 ? $"COMBO x{f.Combo + 1}" : "") + (f.Boost > 0 ? $"{(f.Combo > 0 ? "  " : "")}+{f.Boost}" : ""), plate, Color.black);
            }

            // middle: odds first
            y += 22 * s;
            var big0 = new Rect(r.x, y, r.width, 40 * s);
            if (f.Block != null) { Text(new Rect(r.x + 6 * s, y, r.width - 12 * s, 40 * s), f.Block, smallC, A(Dim, alpha)); }
            else if (!desk.ShowOdds) Text(big0, "?", bigC, A(Dim, alpha));
            else
            {
                Text(big0, f.Succ + "%", bigC, A(C(CardStyle.Odds(f.Odds.Succ)), alpha));
                Bands(new Rect(r.x + 8 * s, y + 42 * s, r.width - 16 * s, 8 * s), f.Odds, f.BaseSucc);
                if (f.Wreck >= 5) Text(new Rect(r.x + 8 * s, y + 52 * s, r.width - 16 * s, 14 * s), $"WRECK {f.Wreck}%", tiny, A(RedC, alpha));
                float cx = r.x + 8 * s;
                foreach (var m in f.Chips)
                {
                    var cr = new Rect(cx, y + 66 * s, 38 * s, 14 * s);
                    if (cr.xMax > r.xMax - 4 * s) break;
                    Fill(cr, A(m.Value > 0 ? new Color(0.18f, 0.7f, 0.42f, 0.35f) : new Color(0.9f, 0.28f, 0.3f, 0.35f), alpha));
                    Text(cr, (m.Value > 0 ? "+" : "") + m.Value + " " + Abbrev(m.Kind), tinyC, A(Color.white, alpha));
                    cx += 41 * s;
                }
            }

            // bottom: name, what it does, what it sets up
            y = r.y + 112 * s;
            Text(new Rect(r.x + 6 * s, y, r.width - 12 * s, 18 * s), f.Name, txt, A(Color.white, alpha));
            Text(new Rect(r.x + 6 * s, y + 18 * s, r.width - 12 * s, 48 * s), f.Text ?? "", wrap, A(Dim, alpha));
            float lx = r.x + 6 * s;
            if (f.SetsUp.Count > 0) Text(new Rect(lx, r.yMax - 16 * s, 14 * s, 14 * s), ">", tiny, A(Dim, alpha));
            for (int i = 0; i < f.SetsUp.Count && i < 3; i++)
                Fill(new Rect(lx + (14 + i * 14) * s, r.yMax - 13 * s, 10 * s, 8 * s), A(C(CardStyle.FamilyInk(f.SetsUp[i])), alpha));
            if (f.Best) { var b = new Rect(r.xMax - 40 * s, r.yMax - 16 * s, 36 * s, 13 * s); Fill(b, A(GreenC, alpha)); Text(b, "BEST", tinyC, Color.black); }
            if (f.Follow) { var b = new Rect(r.xMax - 80 * s, r.yMax - 16 * s, 70 * s, 13 * s); Fill(b, A(Blue, alpha)); Text(b, "FOLLOW-UP", tinyC, Color.black); }
        }

        static string Abbrev(string kind)
        {
            switch (kind)
            {
                case "shape": return "TRK";
                case "combo": return "CMB";
                case "boost": return "BST";
                case "mom": return "MOM";
                case "setup": return "SET";
                case "tune": return "TUN";
                case "heat": return "HT";
                case "block": return "BLK";
                case "dmg": return "DMG";
                case "field": return "FLD";
                case "drv": return "DRV";
                default: return kind.ToUpperInvariant();
            }
        }

        void Tip(CardFace f, Rect card)
        {
            if (f == null) return;
            float s = sc, w = 260 * s, h = 92 * s;
            var r = new Rect(Mathf.Clamp(card.center.x - w / 2, 4, Screen.width - w - 4), card.y - h - 26 * s, w, h);
            Panel(r);
            Text(new Rect(r.x + 8 * s, r.y + 4 * s, w - 16 * s, 18 * s), f.Name + "  ·  " + CardRules.FamilyName(f.Family), txt, C(CardStyle.FamilyInk(f.Family)));
            Text(new Rect(r.x + 8 * s, r.y + 22 * s, w - 16 * s, 40 * s), f.Text ?? "", wrap, Color.white);
            Text(new Rect(r.x + 8 * s, r.y + 64 * s, w - 16 * s, 24 * s), f.Special ? "Can't be tossed" : "Toss: " + f.TossText, wrap, f.TossLive ? FocusC : Dim);
        }

        void DragGhost(PlayDesk desk, HudLayout lay)
        {
            var d = desk.Drag;
            if (d == null || !d.On) return;
            var f = desk.Face(d.Uid);
            if (f == null) return;
            float w = (float)lay.CardW, h = (float)lay.CardH;
            var r = new Rect((float)d.X - w / 2, (float)d.Y - h * 0.55f, w, h);
            Card(desk, f, r, true);
            var c = d.OverBin ? Blue : d.OverPlay ? GreenC : Dim;
            Outline(r, c, 3 * sc);
            Text(new Rect(r.x, r.yMax + 2 * sc, w, 18 * sc), d.OverBin ? "Let go to toss" : d.OverPlay ? "Let go to play" : "Drag up to play", smallC, c);
        }

        // ------------------------------------------------------------------ chips

        void Chips(PlayDesk desk, HudLayout lay)
        {
            foreach (var (hit, box, label) in lay.ChipBoxes)
            {
                bool on = hit.Kind == HitKind.Sort ? (int)desk.Sort == hit.Arg
                        : hit.Kind == HitKind.AllFamilies ? desk.Filter == null
                        : hit.Kind == HitKind.Family ? desk.Filter == (CardFamily)hit.Arg
                        : hit.Kind == HitKind.FadeRisk && desk.FadeRisk;
                var r = R(box);
                var c = hit.Kind == HitKind.Family ? C(CardStyle.FamilyInk((CardFamily)hit.Arg)) : Color.white;
                Fill(r, on ? A(c, 0.28f) : new Color(1, 1, 1, 0.06f));
                Outline(r, on ? c : Line, 1);
                Text(r, label, smallC, on ? Color.white : Dim);
            }
        }

        // ------------------------------------------------------------------ the console: Focus, combo, toss bin, tiles

        void Console(PlayDesk desk, HudReadouts ro, HudLayout lay)
        {
            float s = sc;
            Panel(R(lay.Console));
            var fo = ro.Focus;
            var fb = R(lay.FocusBar);
            Text(new Rect(fb.x, fb.y, 52 * s, fb.height), "FOCUS", tiny, FocusC);
            var bar = new Rect(fb.x + 52 * s, fb.y + 4 * s, fb.width - 52 * s, fb.height * 0.45f);
            Fill(bar, new Color(1, 1, 1, 0.08f));
            int cells = 8;
            for (int i = 1; i < cells; i++) Fill(new Rect(bar.x + bar.width * i / cells, bar.y, 1, bar.height), new Color(0, 0, 0, 0.5f));
            var fc = fo.Free ? GreenC : fo.Low ? RedC : fo.Mid ? Amber : FocusC;
            Fill(new Rect(bar.x, bar.y, bar.width * (float)fo.Fill, bar.height), A(fc, fo.Slowing ? 1 : 0.75f));
            if (fo.NextBeat > 0 && !fo.Free) Fill(new Rect(bar.x + bar.width * (float)fo.Fill, bar.y, bar.width * (float)fo.NextBeat, bar.height), new Color(1, 1, 1, 0.12f));
            Text(new Rect(bar.x, bar.yMax, bar.width, fb.yMax - bar.yMax), fo.Pct + "  " + fo.Note, tiny, fo.Refill ? GreenC : Dim);

            var co = ro.Combo;
            var cb = R(lay.ComboBar);
            if (co.Live)
            {
                var fam = co.Family is CardFamily cf ? C(CardStyle.FamilyInk(cf)) : Blue;
                Text(new Rect(cb.x, cb.y, cb.width, cb.height * 0.5f), $"COMBO x{co.Link + 1}  {co.Left:F1} s", tiny, Blue);
                var mr = new Rect(cb.x, cb.y + cb.height * 0.55f, cb.width, cb.height * 0.3f);
                Fill(mr, new Color(1, 1, 1, 0.08f));
                Fill(new Rect(mr.x, mr.y, mr.width * (float)co.Frac, mr.height), fam);
            }
            else Text(new Rect(cb.x, cb.y, cb.width, cb.height * 0.5f), "No combo: land a card, then play one that glows blue", tiny, Dim);
            if (co.BoardMax > 0)
            {
                Text(new Rect(cb.x, cb.y + cb.height * 0.5f, 90 * s, cb.height * 0.5f), co.Live ? "" : "CREW BOARD", tiny, Dim);
                for (int i = 0; i < co.BoardMax && !co.Live; i++)
                    Fill(new Rect(cb.x + (92 + i * 16) * s, cb.y + cb.height * 0.62f, 12 * s, 8 * s), i < co.Board ? Gold : new Color(1, 1, 1, 0.12f));
            }

            var bin = R(lay.TossBin);
            bool over = desk.Drag != null && desk.Drag.On && desk.Drag.OverBin;
            Fill(bin, over ? A(Blue, 0.45f) : co.BinLive && desk.Drag != null ? A(Blue, 0.25f) : new Color(1, 1, 1, 0.05f));
            Outline(bin, over || co.BinLive ? Blue : Line, over ? 2 * s : 1);
            Text(bin, "TOSS", txtC, over ? Color.white : Dim);
            if (desk.Drag != null && desk.Drag.On)
                Text(new Rect((float)lay.Console.X, (float)lay.Console.Bottom + 1 * s, (float)lay.Console.W, 14 * s), co.BinText, tinyC, co.BinLive ? Blue : Dim);

            float tx = (float)lay.Tiles.X;
            foreach (var t in co.Tiles)
            {
                string label = t.Ready ? t.Text + "  READY" : $"{t.Text}  {t.Left:F0} s";
                float w = (label.Length * 7 + 16) * s;
                var tr = new Rect(tx, (float)lay.Tiles.Y, w, (float)lay.Tiles.H);
                Fill(tr, t.Glow ? A(Blue, 0.4f) : PanelC);
                Outline(tr, t.Ready ? Gold : Blue, 1);
                Text(tr, label, tinyC, Color.white);
                tx += w + 6 * s;
            }
        }

        // ------------------------------------------------------------------ dash, gauge, clock

        void Dash(DashReadout d, HudLayout lay)
        {
            float s = sc;
            var r = R(lay.Dash);
            Panel(r, "CAR");
            float x = r.x + 12 * s, y = r.y + 14 * s, w = r.width - 24 * s;
            Meter(new Rect(x, y, w, 30 * s), "TIRES", d.Tires, d.CarThreat, d.TiresAt, d.TiresD);
            Meter(new Rect(x, y + 36 * s, w, 30 * s), "BODY", d.Body, null, d.BodyAt, d.BodyD);
            y += 76 * s;
            Flash(new Rect(x - 4 * s, y - 2 * s, w + 8 * s, 44 * s), d.PosAt, d.PosD, d.PosThreat);
            Text(new Rect(x, y, 90 * s, 40 * s), "P" + d.Pos, big, d.Pos <= d.Expected ? GreenC : Color.white);
            Text(new Rect(x + 86 * s, y, w - 86 * s, 18 * s), $"exp P{d.Expected}   {d.Ahead}", small, Dim);
            Text(new Rect(x + 86 * s, y + 18 * s, w - 86 * s, 18 * s), d.PosThreat is Threat pt ? pt.Tag : d.Behind, small, d.PosThreat is Threat pt2 ? (pt2.Red ? RedC : Amber) : Dim);
            y += 48 * s;
            Pips(new Rect(x, y, w, 20 * s), "MOMENTUM", d.Momentum, 5, Gold, d.MomThreat, d.MomAt, d.MomD);
            Pips(new Rect(x, y + 24 * s, w, 20 * s), "HEAT", d.Heat, 5, RedC, d.HeatThreat, d.HeatAt, d.HeatD);
            y += 50 * s;
            if (d.Kart) Pips(new Rect(x, y, w, 20 * s), "TOW", d.Tow, 3, Blue, null, -9, 0);
            else if (!string.IsNullOrEmpty(d.Banked)) Text(new Rect(x, y, w, 20 * s), d.Banked, tiny, Gold);
        }

        void Flash(Rect r, double at, int delta, Threat? threat)
        {
            var desk = view.Desk;
            double age = desk.Clock - at;
            if (age < 1.2)
            {
                var c = delta > 0 ? GreenC : RedC;
                Outline(r, A(c, (float)(1 - age / 1.2)), 2 * sc);
                Text(new Rect(r.xMax - 50 * sc, r.y - (float)(age * 20 * sc), 46 * sc, 16 * sc), (delta > 0 ? "+" : "") + delta, txtR, A(c, (float)(1 - age / 1.2)));
            }
            else if (threat is Threat t && Mathf.Repeat((float)desk.Clock, 0.8f) < 0.5f) Outline(r, t.Red ? RedC : Amber, 1.5f * sc);
        }

        void Meter(Rect r, string label, int pct, Threat? threat, double at, int delta)
        {
            float s = sc;
            Flash(r, at, delta, threat);
            Text(new Rect(r.x, r.y, 60 * s, r.height), label, tiny, Dim);
            var bar = new Rect(r.x + 60 * s, r.y + r.height * 0.3f, r.width - 120 * s, r.height * 0.4f);
            Fill(bar, new Color(1, 1, 1, 0.08f));
            var c = C(CardStyle.Condition(pct));
            if (pct < 20 && Mathf.Repeat((float)view.Desk.Clock, 0.6f) < 0.3f) c = A(c, 0.4f);
            Fill(new Rect(bar.x, bar.y, bar.width * pct / 100f, bar.height), c);
            Text(new Rect(bar.xMax + 4 * s, r.y, 56 * s, r.height), pct + "%", txtR, c);
            if (threat is Threat t) Text(new Rect(bar.x, r.yMax - 6 * s, bar.width + 60 * s, 12 * s), t.Tag, tiny, t.Red ? RedC : Amber);
        }

        void Pips(Rect r, string label, int n, int max, Color on, Threat? threat, double at, int delta)
        {
            float s = sc;
            Flash(r, at, delta, threat);
            Text(new Rect(r.x, r.y, 90 * s, r.height), label, tiny, Dim);
            for (int i = 0; i < max; i++)
                Fill(new Rect(r.x + 92 * s + i * 22 * s, r.y + 4 * s, 18 * s, r.height - 8 * s), i < n ? (label == "HEAT" && n >= 3 ? RedC : on) : new Color(1, 1, 1, 0.1f));
            if (threat is Threat t) Text(new Rect(r.x + 92 * s + max * 22 * s + 4 * s, r.y, 60 * s, r.height), t.Tag, tiny, t.Red ? RedC : Amber);
        }

        void Gauge(GaugeReadout g, HudLayout lay)
        {
            float s = sc;
            var r = R(lay.Gauge);
            Panel(r, "LIVE ODDS");
            float x = r.x + 12 * s, w = r.width - 24 * s, y = r.y + 12 * s;
            Text(new Rect(x, y, w, 20 * s), g.Title, txt, g.Has ? C(g.Color) : Dim);
            Text(new Rect(x, y, w, 20 * s), g.Status, txtR, g.Armed ? Gold : Dim);
            y += 24 * s;
            if (g.Has && view.Desk.ShowOdds)
            {
                Text(new Rect(x, y, w, 52 * s), g.Succ + "%", huge, C(CardStyle.Odds(g.Odds.Succ)));
                y += 54 * s;
                Bands(new Rect(x, y, w, 12 * s), g.Odds);
                y += 14 * s;
                string[] names = { "crit fail", "fail", "neutral", "success", "crit" };
                float cw = w / 5;
                for (int i = 4; i >= 0; i--) Text(new Rect(x + (4 - i) * cw, y, cw, 14 * s), $"{g.Odds.P[i] * 100:F0}% {names[i]}", tinyC, C(CardStyle.Tier((Tier)i)));
                y += 16 * s;
                foreach (var t in g.Tags) { Text(new Rect(x, y, w, 14 * s), $"{(t.Value > 0 ? "+" : "")}{t.Value:F0}  {t.Label}", tiny, t.Value > 0 ? GreenC : RedC); y += 14 * s; }
            }
            else if (g.Has) Text(new Rect(x, y, w, 52 * s), "?", huge, Dim);
            var pb = R(lay.PlayButton);
            bool aiming = view.Desk.Aim != null;
            Fill(pb, aiming ? (view.Desk.Aim.Out ? A(RedC, 0.5f) : A(GreenC, 0.6f)) : g.CanPlay ? A(GreenC, 0.25f) : new Color(1, 1, 1, 0.05f));
            Outline(pb, g.CanPlay || aiming ? GreenC : Line, 1.5f * s);
            Text(pb, g.PlayLabel, txtC, g.CanPlay || aiming ? Color.white : Dim);
        }

        void Clock(BeatClockReadout c, HudLayout lay)
        {
            float s = sc;
            var r = R(lay.Clock);
            var col = c.State == ClockState.Draw ? Gold : c.State == ClockState.Soon ? (Mathf.Repeat((float)view.Desk.Clock, 0.5f) < 0.25f ? RedC : Amber)
                    : c.State == ClockState.Mid ? Amber : c.State == ClockState.Idle ? Dim : Color.white;
            Panel(r, "BEAT CLOCK");
            if (c.Slow) Outline(r, FocusC, 2 * s);
            Text(new Rect(r.x + 12 * s, r.y + 10 * s, r.width * 0.55f, 56 * s), c.Num, huge, col);
            Text(new Rect(r.x + r.width * 0.55f, r.y + 16 * s, r.width * 0.45f - 12 * s, 20 * s), c.Label, txt, Color.white);
            Text(new Rect(r.x + r.width * 0.55f, r.y + 36 * s, r.width * 0.45f - 12 * s, 18 * s), c.Unit + " " + c.Sub, small, c.Slow ? FocusC : Dim);
            var bar = new Rect(r.x + 12 * s, r.yMax - 22 * s, r.width - 24 * s, 8 * s);
            Fill(bar, new Color(1, 1, 1, 0.08f));
            Fill(new Rect(bar.x, bar.y, bar.width * (float)c.Frac, bar.height), col);
        }

        // ------------------------------------------------------------------ pit road

        void PitDock(PitReadout p, HudLayout lay)
        {
            if (!p.Show) return;
            float s = sc;
            var r = R(lay.PitDock);
            Panel(r, "PIT");
            Text(new Rect(r.x + 12 * s, r.y + 10 * s, r.width - 24 * s, 20 * s), p.Title, txt, p.Caution ? Gold : Color.white);
            foreach (var (hit, box, label) in lay.PitBoxes)
            {
                var b = R(box);
                bool on = hit.Kind == HitKind.Strategy ? (int)p.Strategy == hit.Arg
                        : hit.Kind == HitKind.CautionCall ? (int)p.Mine == hit.Arg
                        : hit.Kind == HitKind.PitButton && p.Requested;
                bool chief = hit.Kind == HitKind.CautionCall && (int)p.Chief == hit.Arg;
                bool live = hit.Kind != HitKind.PitButton || p.CanPress;
                Fill(b, on ? A(Gold, 0.35f) : new Color(1, 1, 1, live ? 0.07f : 0.03f));
                Outline(b, on ? Gold : Line, 1);
                Text(b, label, smallC, live ? Color.white : Dim);
                if (chief) Text(new Rect(b.x, b.yMax, b.width, 12 * s), "chief", tinyC, Dim);
            }
            Text(new Rect(r.x + 12 * s, r.yMax - 46 * s, r.width - 24 * s, 18 * s), p.Plan, small, Dim);
            Text(new Rect(r.x + 12 * s, r.yMax - 26 * s, r.width - 24 * s, 18 * s), p.Fuel, small, Dim);
        }

        void PitStop(PitReadout p, HudLayout lay)
        {
            float s = sc;
            var r = R(lay.PitStop);
            if (p.Stop == PitGamePhase.Done)
            {
                var rr = new Rect(r.center.x - 200 * s, r.yMax - 60 * s, 400 * s, 48 * s);
                Panel(rr);
                Text(rr, p.Result, txtC, p.Result.StartsWith("Perfect") ? GreenC : Color.white);
                return;
            }
            Panel(r, "PIT STOP");
            if (p.Stop == PitGamePhase.Plan)
            {
                Text(new Rect(r.x + 12 * s, r.y + 10 * s, r.width - 24 * s, 22 * s),
                    $"Plan your stop · {p.PlanLeft:F0} s   Tires {p.Tire:F1} s · Fuel {p.FuelLane:F1} s · Adjust {p.Adjust:F1} s  =  {p.Total:F1} s", txt, Color.white);
                int i = 0;
                foreach (var (hit, box, _) in lay.StopBoxes)
                {
                    var b = R(box);
                    if (hit.Kind == HitKind.PitCard)
                    {
                        var c = p.Hand[i++];
                        Fill(b, c.on ? A(GreenC, 0.22f) : CardC);
                        Outline(b, c.on ? GreenC : Line, c.on ? 2 * s : 1);
                        Text(new Rect(b.x + 6 * s, b.y + 4 * s, b.width - 12 * s, 18 * s), c.name, txt, Color.white);
                        Text(new Rect(b.x + 6 * s, b.y + 22 * s, b.width - 12 * s, 14 * s), c.lane + (c.prep ? " · Prep" : ""), tiny, Gold);
                        Text(new Rect(b.x + 6 * s, b.y + 38 * s, b.width - 12 * s, b.height - 60 * s), c.text ?? "", wrap, Dim);
                        Text(new Rect(b.x + 6 * s, b.yMax - 18 * s, b.width - 12 * s, 14 * s), c.on ? "IN" : c.block ?? "Click to use", tiny, c.on ? GreenC : c.block != null ? RedC : Dim);
                    }
                    else
                    {
                        bool go = hit.Kind == HitKind.PitGo;
                        Fill(b, go ? A(GreenC, 0.5f) : new Color(1, 1, 1, 0.08f));
                        Outline(b, go ? GreenC : Line, 1);
                        Text(b, go ? "GO" : "Crew chief's plan", txtC, Color.white);
                    }
                }
            }
            else if (p.Stop == PitGamePhase.Work)
            {
                Text(new Rect(r.x + 12 * s, r.y + 10 * s, r.width - 24 * s, 22 * s), "Drop the jack when the slowest lane is done: early risks a loose wheel, late loses time (Enter)", txt, Color.white);
                double span = p.Total + p.Window * 2.5 + 0.5;
                var bar = new Rect(r.x + 20 * s, r.y + 90 * s, r.width - 40 * s, 26 * s);
                Fill(bar, new Color(1, 1, 1, 0.08f));
                Fill(new Rect(bar.x + bar.width * (float)(p.Total / span), bar.y, bar.width * (float)(p.Window / span), bar.height), A(GreenC, 0.6f));
                Fill(new Rect(bar.x, bar.y + bar.height * 0.35f, bar.width * Mathf.Clamp01((float)(p.Elapsed / span)), bar.height * 0.3f), Color.white);
                Fill(new Rect(bar.x + bar.width * Mathf.Clamp01((float)(p.Elapsed / span)) - 2, bar.y - 4 * s, 4, bar.height + 8 * s), Gold);
                Text(new Rect(bar.x, bar.yMax + 4 * s, bar.width, 18 * s), $"{p.Elapsed:F1} s of {p.Total:F1} s", smallC, Dim);
                foreach (var (hit, box, _) in lay.StopBoxes)
                {
                    var b = R(box);
                    bool inWin = p.Elapsed >= p.Total && p.Elapsed <= p.Total + p.Window;
                    Fill(b, inWin ? A(GreenC, 0.7f) : A(Gold, 0.35f));
                    Outline(b, inWin ? GreenC : Gold, 2 * s);
                    Text(b, "RELEASE", bigC, Color.white);
                }
            }
        }

        // ------------------------------------------------------------------ the radio lane

        static readonly Color AmberBox = new Color(0.91f, 0.71f, 0.18f, 0.97f), AdviceBox = new Color(0.875f, 0.9f, 0.953f, 0.97f), RadioInk = new Color(0.14f, 0.1f, 0f);

        /// <summary>The slice's amber radio box just above the hand: who's talking, the call, Try it / No thanks with the odds,
        /// what it pays and costs, the crew level, and the countdown. Advice is pale blue; the pit call has no buttons.</summary>
        void Radio(RadioReadout rr, HudLayout lay)
        {
            if (!rr.Show) return;
            float s = sc;
            var r = R(lay.Radio);
            Fill(r, rr.Advice || rr.PitCall ? AdviceBox : AmberBox);
            float x = r.x + 12 * s, w = r.width - 24 * s;
            Text(new Rect(x, r.y + 8 * s, w, 18 * s), "RADIO · " + rr.Who.ToUpperInvariant(), small, RadioInk);
            Text(new Rect(x, r.y + 8 * s, w, 18 * s), rr.Tag.ToUpperInvariant() + (rr.Wits != null ? " · " + rr.Wits : "") + (rr.Left != null ? " · " + rr.Left : ""), txtR, RadioInk);
            Text(new Rect(x, r.y + 30 * s, w, 26 * s), rr.Line, txt, RadioInk);
            foreach (var (hit, box, label) in lay.RadioBoxes)
            {
                var b = R(box);
                bool take = hit.Kind == HitKind.RadioTake;
                Fill(b, take ? RadioInk : new Color(0, 0, 0, 0));
                Outline(b, RadioInk, 1);
                Text(b, label + (take ? " (Y)" : " (N)"), smallC, take ? Gold : RadioInk);
            }
            if (rr.Risky)
            {
                var last = lay.RadioBoxes[lay.RadioBoxes.Count - 1].box;
                Text(new Rect((float)last.Right + 10 * s, r.y + 60 * s, r.xMax - (float)last.Right - 22 * s, 32 * s), $"{rr.Win} / risk: {rr.Risk.ToLowerInvariant()}", wrap, RadioInk);
                Text(new Rect(x, r.y + 96 * s, w, 16 * s), rr.Crew, tiny, A(RadioInk, 0.8f));
            }
            if (rr.Frac > 0) Fill(new Rect(r.x, r.yMax - 4 * s, r.width * (float)rr.Frac, 4 * s), RadioInk);
        }

        // ------------------------------------------------------------------ pauses and toasts

        void Pause(PlayDesk desk, HudLayout lay)
        {
            if (!desk.Driver.Paused || desk.Race.Done) return;
            float s = sc;
            var r = R(lay.PauseBox);
            Panel(r);
            Outline(r, desk.BeatPaused ? Gold : Line, 2 * s);
            Text(new Rect(r.x, r.y + 8 * s, r.width, 40 * s), desk.BeatPaused ? "DRAW" : "PAUSED", bigC, desk.BeatPaused ? Gold : Color.white);
            Text(new Rect(r.x + 10 * s, r.y + 50 * s, r.width - 20 * s, 40 * s),
                PauseText(desk), smallC, Dim);
        }

        string PauseText(PlayDesk desk)
        {
            var pad = view.Pad;
            if (pad == null || !pad.Active)
                return desk.BeatPaused ? "Plan your next move. Play a card, hold Space, or press P (or click here) to go green." : "Pick cards and watch their odds. P resumes.";
            string G(PadButton b) => PadGlyphs.Of(pad.Style, b);
            return desk.BeatPaused ? $"Plan your next move. Play a card ({G(PadButton.Confirm)} twice), hold {G(PadButton.Aim)}, or press {G(PadButton.Pause)} to go green."
                                   : $"Pick cards and watch their odds. {G(PadButton.Pause)} resumes.";
        }

        void ToastBox(PlayDesk desk, HudLayout lay)
        {
            if (!(desk.Toast is Toast t)) return;
            float s = sc;
            double age = desk.Clock - t.At;
            float a = (float)Mathf.Clamp01((float)((PlayDesk.ToastSec - age) / 0.4));
            var r = R(lay.Toast);
            Fill(r, A(C(t.Bg), 0.95f * a));
            Text(new Rect(r.x, r.y + 4 * s, r.width, 26 * s), t.Title, txtC, A(Color.white, a));
            if (!string.IsNullOrEmpty(t.Sub)) Text(new Rect(r.x, r.y + 30 * s, r.width, 22 * s), t.Sub, smallC, A(Color.white, 0.85f * a));
        }
    }
}
