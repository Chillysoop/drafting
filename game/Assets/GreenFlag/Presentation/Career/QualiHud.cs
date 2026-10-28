using GreenFlag.Sim;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// The qualifying run screen (roadmap step 37), in immediate-mode GUI like the menus: the real track's outline with your car
    /// and the corners on it, the brake bar with its OK, Good and Perfect zones and the sweeping needle, the corner and what to
    /// do, the last mark, your chain, Focus and lap times, and a BRAKE button. Brake is Space, Enter, the controller's A, or the
    /// BRAKE button held with the mouse; CareerFlow reads them into Held and ticks the run. QualiPanel (engine-free) has the
    /// numbers.
    /// </summary>
    public sealed class QualiHud
    {
        readonly CareerFlow flow;
        QualiPanel panel;
        System.Collections.Generic.List<(double x, double y)> outline;
        /// <summary>Brake held with the mouse on the BRAKE button, or with Space or Enter.</summary>
        public bool MouseHeld, KeyHeld;
        GUIStyle big, mid, small, pop;
        float scale = -1;

        static readonly Color Bg = new Color(0.035f, 0.05f, 0.094f), Panel = new Color(1, 1, 1, 0.05f), Ink = new Color(0.93f, 0.95f, 0.98f), Dim = new Color(0.55f, 0.58f, 0.65f), Amber = new Color(0.91f, 0.71f, 0.18f);

        public QualiHud(CareerFlow flow) { this.flow = flow; }

        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }

        void Styles()
        {
            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
            if (Mathf.Approximately(s, scale)) return;
            scale = s;
            big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big.normal.textColor = Ink;
            mid = new GUIStyle(big) { fontSize = Mathf.RoundToInt(20 * s), fontStyle = FontStyle.Normal };
            small = new GUIStyle(mid) { fontSize = Mathf.RoundToInt(14 * s), alignment = TextAnchor.MiddleLeft };
            small.normal.textColor = Dim;
            pop = new GUIStyle(big) { fontSize = Mathf.RoundToInt(28 * s) };
        }

        public void Draw()
        {
            var run = flow.Menu.Quali;
            if (run == null) return;
            Styles();
            float s = scale, W = Screen.width, H = Screen.height, pad = 24 * s;
            if (panel == null || panel.Track != run.Plan.Track) { panel = new QualiPanel(run.Plan.Track); outline = panel.Outline(); }
            Keys(Event.current);
            Fill(new Rect(0, 0, W, H), Bg);

            GUI.Label(new Rect(pad, pad, W - 2 * pad, 40 * s), $"Qualifying · {run.Plan.Track.Name}", mid);

            // the map
            float ms = Mathf.Min(W * 0.5f, H * 0.45f);
            var map = new Rect((W - ms) / 2, pad + 50 * s, ms, ms);
            Fill(map, Panel);
            foreach (var p in outline) Fill(new Rect(map.x + (float)p.x * ms - 2 * s, map.y + (float)p.y * ms - 2 * s, 4 * s, 4 * s), Dim);
            foreach (var c in run.Plan.Checks)
            {
                if (c.Lap != (run.Current?.Lap ?? run.Plan.Laps - 1)) continue;
                var q = panel.At(c.U);
                var col = c.Res is QualiMark m ? Kit.C(QualiPanel.MarkColor(m)) : (c == run.Current ? Amber : Ink);
                Fill(new Rect(map.x + (float)q.x * ms - 5 * s, map.y + (float)q.y * ms - 5 * s, 10 * s, 10 * s), col);
            }
            var car = panel.At(run.CarU);
            Fill(new Rect(map.x + (float)car.x * ms - 7 * s, map.y + (float)car.y * ms - 7 * s, 14 * s, 14 * s), new Color(0.18f, 0.7f, 0.42f));

            // the brake bar
            float by = map.yMax + 30 * s, bw = Mathf.Min(W - 2 * pad, 900 * s), bx = (W - bw) / 2, bh = 46 * s;
            var bar = new Rect(bx, by, bw, bh);
            Fill(bar, new Color(1, 1, 1, 0.08f));
            var cur = run.Current;
            if (cur != null)
            {
                var z = QualiPanel.Zones(cur);
                Color[] zc = { new Color(0.62f, 0.82f, 1f, 0.35f), new Color(0.18f, 0.7f, 0.42f, 0.6f), new Color(0.91f, 0.71f, 0.18f, 0.9f) };
                for (int i = 0; i < z.Length; i++) Fill(new Rect(bx + (float)z[i].a * bw, by, (float)(z[i].b - z[i].a) * bw, bh), zc[i]);
                if (run.InSweep) Fill(new Rect(bx + (float)run.Needle * bw - 2 * s, by - 8 * s, 4 * s, bh + 16 * s), Ink);
            }
            GUI.Label(new Rect(pad, by + bh + 8 * s, W - 2 * pad, 44 * s), QualiPanel.Prompt(run), big);
            var pp = QualiPanel.Pop(run);
            if (pp != null)
            {
                var ps = new GUIStyle(pop); ps.normal.textColor = Kit.C(QualiPanel.MarkColor(run.Last.Saved ? QualiMark.Ok : run.Last.Res.Value));
                GUI.Label(new Rect(pad, by - 50 * s, W - 2 * pad, 40 * s), pp, ps);
            }

            // chain, Focus, laps
            var sc = run.Score;
            float iy = by + bh + 60 * s;
            GUI.Label(new Rect(bx, iy, bw / 3, 26 * s), $"Chain ×{sc.Chain} · {run.Plan.Checks.FindAll(c => c.Res != null).Count} of {run.Plan.Checks.Count} corners", small);
            Fill(new Rect(bx + bw / 3, iy + 8 * s, bw / 3 * (float)System.Math.Min(1.5, run.Focus) / 1.5f, 10 * s), run.Slowed ? Amber : new Color(0.62f, 0.82f, 1f));
            GUI.Label(new Rect(bx + bw / 3, iy + 18 * s, bw / 3, 22 * s), "Focus (hold for slow-mo)", small);
            string laps = "";
            for (int i = 0; i < run.LapTimes.Count; i++) laps += $"L{i + 1} {run.LapTimes[i]:0.000}   ";
            GUI.Label(new Rect(bx + 2 * bw / 3, iy, bw / 3, 26 * s), laps, small);

            // BRAKE: hold with the mouse
            var brake = new Rect((W - 260 * s) / 2, H - 110 * s, 260 * s, 70 * s);
            Fill(brake, MouseHeld ? Amber : new Color(0.9f, 0.28f, 0.3f, 0.85f));
            GUI.Label(brake, "BRAKE", big);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && brake.Contains(e.mousePosition)) { MouseHeld = true; e.Use(); }
            if (e.type == EventType.MouseUp && e.button == 0 && MouseHeld) { MouseHeld = false; e.Use(); }
            GUI.Label(new Rect(pad, H - 34 * s, W - 2 * pad, 24 * s), flow.PadActive ? $"Hold {PadGlyphs.Of(flow.PadStyle, PadButton.Confirm)}, let go in the zone · {PadGlyphs.Of(flow.PadStyle, PadButton.Back)} gives up (average lap)" : "Hold Space, let go in the zone · Esc gives up (average lap)", small);
        }

        void Keys(Event e)
        {
            if (e.type != EventType.KeyDown && e.type != EventType.KeyUp) return;
            bool down = e.type == EventType.KeyDown;
            switch (e.keyCode)
            {
                case KeyCode.Space: case KeyCode.Return: case KeyCode.KeypadEnter: KeyHeld = down; flow.PadActive = false; e.Use(); break;
                case KeyCode.Escape: if (down) flow.Click("quali.giveup"); e.Use(); break;
            }
        }
    }
}
