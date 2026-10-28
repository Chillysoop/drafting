using System.Collections.Generic;
using GreenFlag.Sim;
using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// The race view's on-screen layer, in immediate-mode GUI so it needs no prefabs or fonts: the scoring pylon, flag banners,
    /// the booth ticker, car numbers, the result card and the controls. The hand and beats HUD (step 31) is HandHud, drawn on
    /// top. Keys are read here too (GUI events work whichever input backend the project uses); with a hand in play the
    /// number keys and Space belong to the cards, so speed moves to [ and ].
    /// </summary>
    public sealed class RaceHud
    {
        readonly RaceView view;
        readonly List<PylonRow> rows = new List<PylonRow>();
        GUIStyle label, labelR, small, bannerStyle, tag, title;
        float scale;

        static readonly Color Panel = new Color(0.035f, 0.05f, 0.094f, 0.86f), Row = new Color(1, 1, 1, 0.04f), MeRow = new Color(0.91f, 0.71f, 0.18f, 0.22f);
        static readonly Color Gold = new Color(0.91f, 0.71f, 0.18f), Dim = new Color(0.62f, 0.66f, 0.74f);

        public RaceHud(RaceView view) { this.view = view; }

        void Styles()
        {
            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
            if (label != null && Mathf.Approximately(s, scale)) return;
            scale = s;
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17 * s), fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            label.normal.textColor = Color.white;
            labelR = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            label.alignment = TextAnchor.MiddleLeft;
            small = new GUIStyle(label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Normal, wordWrap = true };
            bannerStyle = new GUIStyle(label) { fontSize = Mathf.RoundToInt(34 * s), alignment = TextAnchor.MiddleCenter };
            tag = new GUIStyle(label) { fontSize = Mathf.RoundToInt(13 * s), alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(28 * s), alignment = TextAnchor.MiddleCenter };
        }

        public void Draw()
        {
            var d = view.Driver;
            if (d == null) return;
            if (Event.current.type == EventType.KeyDown) Keys(Event.current);
            if (Event.current.type == EventType.Repaint && view.LowRes != null)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), view.LowRes, ScaleMode.StretchToFill, false);
            Styles();
            CarTags(d);
            PylonBox(d.Race);
            Banner(d);
            Booth(d);
            Controls(d);
            if (d.Race.Done) Result(d.Race);
        }

        void Keys(Event e)
        {
            var d = view.Driver;
            bool cards = view.Desk != null;
            switch (e.keyCode)
            {
                case KeyCode.C: view.Rig.Mode = CameraRig.Next(view.Rig.Mode); break;
                case KeyCode.Alpha1 when !cards: d.SpeedIndex = 0; break;
                case KeyCode.Alpha2 when !cards: d.SpeedIndex = 1; break;
                case KeyCode.Alpha3 when !cards: d.SpeedIndex = 2; break;
                case KeyCode.Alpha4 when !cards: d.SpeedIndex = 3; break;
                case KeyCode.Space when !cards: d.Paused = !d.Paused; break;
                case KeyCode.LeftBracket: d.SpeedIndex = Mathf.Max(0, d.SpeedIndex - 1); break;
                case KeyCode.RightBracket: d.SpeedIndex = Mathf.Min(RaceDriver.Speeds.Length - 1, d.SpeedIndex + 1); break;
                case KeyCode.A when cards: view.Autoplay = !view.Autoplay; break;
                case KeyCode.R: view.Restart(true); break;
                case KeyCode.T: view.NextTrack(e.shift ? -1 : 1); break;
                case KeyCode.L: view.NextLook(); break;
                case KeyCode.F: d.Finish(); break;
                default: return;
            }
            e.Use();
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void PylonBox(Race r)
        {
            float s = scale, x = 16 * s, y = 16 * s, w = 270 * s, rh = 26 * s;
            int n = Screen.height < 700 ? 5 : 10;
            Pylon.Rows(r, n, rows);
            var (trk, right, flag) = Pylon.Header(r);
            Fill(new Rect(x, y, w, rh * 1.15f + rows.Count * rh + 8 * s), Panel);
            Color head = flag == PylonFlag.Green ? new Color(0.18f, 0.7f, 0.42f) : flag == PylonFlag.Caution ? Gold : flag == PylonFlag.Final ? Color.white : new Color(0.25f, 0.29f, 0.36f);
            Fill(new Rect(x, y, w, rh * 1.15f), head);
            var hl = new GUIStyle(label); hl.normal.textColor = flag == PylonFlag.Caution || flag == PylonFlag.Final ? Color.black : Color.white;
            var hr = new GUIStyle(labelR); hr.normal.textColor = hl.normal.textColor;
            GUI.Label(new Rect(x + 8 * s, y, w - 16 * s, rh * 1.15f), trk, hl);
            GUI.Label(new Rect(x + 8 * s, y, w - 16 * s, rh * 1.15f), right, hr);
            float ry = y + rh * 1.15f + 4 * s;
            foreach (var row in rows)
            {
                if (row.Pos == 0) { GUI.Label(new Rect(x + 8 * s, ry, w, rh), "···", label); ry += rh; continue; }
                if (row.IsMe) Fill(new Rect(x, ry, w, rh), MeRow); else if (row.Pos % 2 == 0) Fill(new Rect(x, ry, w, rh), Row);
                GUI.Label(new Rect(x + 6 * s, ry, 30 * s, rh), row.Pos.ToString(), labelR);
                Fill(new Rect(x + 44 * s, ry + 5 * s, 6 * s, rh - 10 * s), Kit.C(Looks.Team(row.Team).Body));
                var nl = new GUIStyle(label); nl.normal.textColor = row.Dnf ? Dim : row.IsMe ? Gold : Color.white;
                GUI.Label(new Rect(x + 56 * s, ry, 50 * s, rh), "#" + row.Number, nl);
                GUI.Label(new Rect(x + 104 * s, ry, 100 * s, rh), row.Name, nl);
                var gl = new GUIStyle(labelR) { fontStyle = FontStyle.Normal }; gl.normal.textColor = row.Dnf || row.InPit ? Gold : Dim;
                GUI.Label(new Rect(x + 150 * s, ry, w - 158 * s, rh), row.Gap, gl);
                ry += rh;
            }
        }

        void Banner(RaceDriver d)
        {
            if (!(d.Banner is GreenFlag.View.Banner b)) return;
            float s = scale, w = 420 * s, h = 64 * s;
            var r = new Rect((Screen.width - w) / 2, 70 * s, w, h);
            Fill(r, Kit.C(b.Bg));
            var st = new GUIStyle(bannerStyle); st.normal.textColor = Kit.C(b.Fg);
            GUI.Label(r, b.Text, st);
        }

        void Booth(RaceDriver d)
        {
            if (d.Booth.Count == 0) return;
            float s = scale, w = 520 * s, lh = 22 * s, h = d.Booth.Count * lh + 12 * s;
            var r = new Rect(16 * s, Screen.height - h - 16 * s, w, h);
            if (view.Desk != null) { var b = view.Layout.Booth; w = Mathf.Min(w, (float)b.W); r = new Rect((float)b.X, (float)b.Bottom - h, w, h); }
            Fill(r, Panel);
            for (int i = 0; i < d.Booth.Count; i++)
            {
                var st = new GUIStyle(small) { wordWrap = false }; st.normal.textColor = i == d.Booth.Count - 1 ? Color.white : Dim;
                GUI.Label(new Rect(r.x + 10 * s, r.y + 6 * s + i * lh, w - 20 * s, lh), d.Booth[i], st);
            }
        }

        void CarTags(RaceDriver d)
        {
            var cam = view.Cam;
            if (cam == null) return;
            bool close = view.Rig.Resolve(d.Race.Phase) == CameraMode.Follow;
            float s = scale;
            foreach (var p in d.Poses)
            {
                if (p.IsPace || (!close && !p.IsMe) || (p.Dnf && !p.IsMe)) continue;
                var world = Kit.V(p.P, TrackLayout.CarH * 2.2);
                var sp = cam.WorldToViewportPoint(world);
                if (sp.z <= 0 || sp.x < 0 || sp.x > 1 || sp.y < 0 || sp.y > 1) continue;
                float gx = sp.x * Screen.width, gy = (1 - sp.y) * Screen.height;
                if (close && sp.z > 400) continue;
                var st = new GUIStyle(tag); st.normal.textColor = p.IsMe ? Gold : Color.white;
                string text = p.IsMe ? "YOU" : "#" + NumberOf(d.Race, p.Id);
                GUI.Label(new Rect(gx - 40 * s, gy - 22 * s, 80 * s, 20 * s), text, st);
            }
        }

        static int NumberOf(Race r, int id)
        {
            foreach (var c in r.Cars) if (c.Id == id) return c.Ref.Number;
            return 0;
        }

        void Controls(RaceDriver d)
        {
            float s = scale, w = 230 * s, bh = 30 * s;
            bool cards = view.Desk != null;
            int n = cards ? 8 : 7;
            // with a hand in play the bottom-right corner is the odds gauge, so the controls go to the top right
            var area = cards ? new Rect(Screen.width - w - 16 * s, 16 * s, w, (bh + 4 * s) * n + 12 * s)
                             : new Rect(Screen.width - w - 16 * s, Screen.height - (bh + 4 * s) * n - 24 * s, w, (bh + 4 * s) * n + 12 * s);
            Fill(area, Panel);
            GUILayout.BeginArea(new Rect(area.x + 6 * s, area.y + 6 * s, area.width - 12 * s, area.height - 12 * s));
            var bs = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * s), fixedHeight = bh };
            if (GUILayout.Button("Camera: " + view.Rig.Mode + "  (C)", bs)) view.Rig.Mode = CameraRig.Next(view.Rig.Mode);
            if (GUILayout.Button("Speed: " + d.Speed + "x  (" + (cards ? "[ ]" : "1-4") + ")", bs)) d.SpeedIndex = (d.SpeedIndex + 1) % RaceDriver.Speeds.Length;
            if (GUILayout.Button((d.Paused ? "Resume" : "Pause") + (cards ? "  (P)" : "  (Space)"), bs)) { if (cards) view.Desk.TogglePause(); else d.Paused = !d.Paused; }
            if (cards && GUILayout.Button("Autoplay: " + (view.Autoplay ? "on" : "off") + "  (A)", bs)) view.Autoplay = !view.Autoplay;
            if (GUILayout.Button("Track: " + Pylon.ShortName(d.Race.Track) + "  (T)", bs)) view.NextTrack(1);
            if (GUILayout.Button("Look: " + view.Look.Name + (view.Look.Finished ? "" : "*") + "  (L)", bs)) view.NextLook();
            if (GUILayout.Button("New race  (R)", bs)) view.Restart(true);
            if (GUILayout.Button("Skip to flag  (F)", bs)) d.Finish();
            GUILayout.EndArea();
            var st = new GUIStyle(small) { alignment = TextAnchor.LowerRight, wordWrap = false }; st.normal.textColor = Dim;
            var info = cards ? new Rect(0, area.yMax + 4 * s, Screen.width - 16 * s, 20 * s) : new Rect(0, 0, Screen.width - 16 * s, area.y - 4 * s);
            if (cards) st.alignment = TextAnchor.UpperRight;
            GUI.Label(info, $"{d.Race.Track.Name} · seed {d.Race.Seed} · {d.Race.T:F0} s" + (view.Look.Finished ? "" : " · *look is a preview until step 45"), st);
        }

        Race resultFor;
        RaceResult res;
        string fingerprint;

        void Result(Race r)
        {
            if (resultFor != r) { resultFor = r; res = r.Result(); fingerprint = r.Fingerprint(); }
            float s = scale, w = 420 * s, h = 150 * s;
            var rect = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(rect, Panel);
            GUI.Label(new Rect(rect.x, rect.y + 12 * s, w, 40 * s), res.MyDnf ? "OUT OF THE RACE" : "YOU FINISHED P" + res.MyFinish, title);
            var st = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter }; st.normal.textColor = Dim;
            GUI.Label(new Rect(rect.x, rect.y + 56 * s, w, 50 * s), $"Started P{res.MyStart}, expected P{res.MyExpected} · {res.Cautions} caution(s)\nfingerprint {fingerprint}", st);
            GUI.Label(new Rect(rect.x, rect.y + 108 * s, w, 30 * s), view.Pad != null && view.Pad.Active ? $"{PadGlyphs.Of(view.Pad.Style, PadButton.Confirm)} or {PadGlyphs.Of(view.Pad.Style, PadButton.Pause)} new race · R new race · T next track" : "R new race · T next track", st);
        }
    }
}
