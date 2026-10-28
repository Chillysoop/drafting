using GreenFlag.View;
using UnityEngine;

namespace GreenFlag.Presentation
{
    /// <summary>
    /// Draws a CareerMenu page in immediate-mode GUI (step 34), in the race HUD's plain dark style until the art pass (step
    /// 45): the header with your bank, the sections in two columns on a wide screen (one on a narrow or Switch-sized one), and
    /// the action bar along the bottom. Cards carry their family colour; the focused control gets an amber ring when the
    /// controller or the arrow keys are in use.
    /// </summary>
    public sealed class MenuHud
    {
        readonly CareerFlow flow;
        GUIStyle eyebrow, title, sub, h3, text, small, row, rowR, btn, btnOn, money, card;
        float scale = -1;
        Vector2 scroll;
        Rect focusRect;
        bool focusSeen;

        static readonly Color Bg = new Color(0.035f, 0.05f, 0.094f), Panel = new Color(1, 1, 1, 0.05f), Line = new Color(1, 1, 1, 0.12f);
        static readonly Color Ink = new Color(0.93f, 0.95f, 0.98f), Dim = new Color(0.55f, 0.58f, 0.65f), Amber = new Color(0.91f, 0.71f, 0.18f), Green = new Color(0.18f, 0.7f, 0.42f);

        public MenuHud(CareerFlow flow) { this.flow = flow; }

        void Styles()
        {
            float s = Mathf.Clamp(Screen.height / 1080f, 0.6f, 2f);
            if (Mathf.Approximately(s, scale)) return;
            scale = s;
            int F(float px) => Mathf.RoundToInt(px * s);
            text = new GUIStyle(GUI.skin.label) { fontSize = F(16), wordWrap = true, richText = false };
            text.normal.textColor = Ink;
            small = new GUIStyle(text) { fontSize = F(13) }; small.normal.textColor = Dim;
            eyebrow = new GUIStyle(small) { fontSize = F(13), fontStyle = FontStyle.Bold };
            eyebrow.normal.textColor = Amber;
            title = new GUIStyle(text) { fontSize = F(40), fontStyle = FontStyle.Bold, wordWrap = false };
            sub = new GUIStyle(text) { fontSize = F(15) }; sub.normal.textColor = Dim;
            h3 = new GUIStyle(text) { fontSize = F(18), fontStyle = FontStyle.Bold };
            row = new GUIStyle(text) { fontSize = F(15), wordWrap = true };
            rowR = new GUIStyle(row) { alignment = TextAnchor.UpperRight, wordWrap = false, fontStyle = FontStyle.Bold };
            btn = new GUIStyle(GUI.skin.button) { fontSize = F(15), wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(F(10), F(10), F(6), F(6)) };
            btnOn = new GUIStyle(btn) { fontStyle = FontStyle.Bold };
            btnOn.normal.textColor = btnOn.hover.textColor = Green;
            money = new GUIStyle(text) { fontSize = F(28), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            money.normal.textColor = Green;
            card = new GUIStyle(btn) { alignment = TextAnchor.UpperLeft };
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Frame(Rect r, Color c, float w)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        public void Draw()
        {
            Styles();
            var m = flow.Menu;
            var e = Event.current;
            if (e.type == EventType.KeyDown) Keys(e);
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown) flow.PadActive = false;
            var page = m.Page();
            if (flow.PadActive) flow.Focus.Current(page);
            float s = scale, pad = 24 * s, W = Screen.width, H = Screen.height;
            Fill(new Rect(0, 0, W, H), Bg);

            // header
            float y = pad;
            GUI.Label(new Rect(pad, y, W - 2 * pad, 20 * s), page.Eyebrow ?? "", eyebrow); y += 22 * s;
            var ts = new GUIStyle(title); if (page.TitleColor is Rgb tc) ts.normal.textColor = Kit.C(tc);
            GUI.Label(new Rect(pad, y, W - 2 * pad - 260 * s, 48 * s), page.Title ?? "", ts);
            if (page.Money != null) GUI.Label(new Rect(W - pad - 260 * s, y, 260 * s, 48 * s), page.Money, money);
            y += 50 * s;
            if (!string.IsNullOrEmpty(page.Sub)) { float h = sub.CalcHeight(new GUIContent(page.Sub), W - 2 * pad); GUI.Label(new Rect(pad, y, W - 2 * pad, h), page.Sub, sub); y += h + 4 * s; }
            if (!string.IsNullOrEmpty(page.Notice))
            {
                var ns = new GUIStyle(text); ns.normal.textColor = Amber;
                float h = ns.CalcHeight(new GUIContent(page.Notice), W - 2 * pad - 20 * s) + 12 * s;
                Fill(new Rect(pad, y, W - 2 * pad, h), new Color(0.91f, 0.71f, 0.18f, 0.12f));
                GUI.Label(new Rect(pad + 10 * s, y + 6 * s, W - 2 * pad - 20 * s, h), page.Notice, ns);
                y += h + 6 * s;
            }

            // the action bar
            float barH = 64 * s, barY = H - barH;
            Fill(new Rect(0, barY, W, barH), new Color(0, 0, 0, 0.35f));
            float bx = pad;
            foreach (var a in page.Actions)
            {
                var bs = new GUIStyle(btn) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt((a.Primary ? 18 : 15) * s), fontStyle = a.Primary ? FontStyle.Bold : FontStyle.Normal };
                float bw = Mathf.Max(160 * s, bs.CalcSize(new GUIContent(a.Label)).x + 30 * s);
                var r = new Rect(bx, barY + 10 * s, bw, barH - 20 * s);
                GUI.enabled = a.Pressable;
                if (a.Primary && a.Pressable) Fill(r, new Color(0.18f, 0.7f, 0.42f, 0.35f));
                if (GUI.Button(r, a.Label, bs)) flow.Click(a.Id);
                GUI.enabled = true;
                if (flow.PadActive && a.Id == flow.Focus.Id) Frame(r, Amber, 3 * s);
                bx += bw + 12 * s;
                if (!a.Pressable && !string.IsNullOrEmpty(a.Note)) { var ns = new GUIStyle(small); ns.normal.textColor = Amber; var nw = Mathf.Min(420 * s, W - bx - pad); GUI.Label(new Rect(bx, barY + 10 * s, nw, barH - 20 * s), a.Note, ns); bx += nw; }
            }
            var hint = new GUIStyle(small) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            string keys = flow.PadActive ? $"{PadGlyphs.Of(flow.PadStyle, PadButton.Confirm)} press · {PadGlyphs.Of(flow.PadStyle, PadButton.Back)} back · d-pad move" : "Arrows move · Enter press · Esc back";
            GUI.Label(new Rect(bx, barY, W - bx - pad, barH), keys, hint);

            // sections
            bool two = W >= 1100 * s;
            float colGap = 18 * s, cw = two ? (W - 2 * pad - colGap) / 2 : W - 2 * pad;
            var view = new Rect(0, y, W, barY - y - 6 * s);
            focusSeen = false;
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, W - 20 * s, Mathf.Max(view.height, Height(page, cw, two, s))));
            float[] cy = { 0, 0 };
            foreach (var sec in page.Sections)
            {
                int col = two ? sec.Column : 0;
                float x = pad + col * (cw + colGap);
                cy[col] += DrawSection(sec, x, cy[col], cw, s) + 14 * s;
                if (!two) cy[1] = cy[0];
            }
            GUI.EndScrollView();
            // keep the focused control on screen
            if (flow.PadActive && focusSeen && e.type == EventType.Repaint)
            {
                if (focusRect.y < scroll.y) scroll.y = Mathf.Max(0, focusRect.y - 20 * s);
                else if (focusRect.yMax > scroll.y + view.height) scroll.y = focusRect.yMax - view.height + 20 * s;
            }
        }

        float Height(MenuPage p, float cw, bool two, float s)
        {
            float[] h = { 0, 0 };
            foreach (var sec in p.Sections) { int col = two ? sec.Column : 0; h[col] += SectionHeight(sec, cw, s) + 14 * s; }
            return Mathf.Max(h[0], h[1]) + 20 * s;
        }

        float ItemHeight(MenuItem i, float w, float s)
        {
            switch (i.Kind)
            {
                case MenuKind.Text: return text.CalcHeight(new GUIContent(i.Label), w) + 4 * s;
                case MenuKind.Row: return Mathf.Max(row.CalcHeight(new GUIContent(i.Label), w * 0.62f), rowR.CalcHeight(new GUIContent(i.Value ?? ""), w * 0.38f)) + 4 * s;
                case MenuKind.Card:
                case MenuKind.Part:
                    return 30 * s + (string.IsNullOrEmpty(i.Note) ? 0 : small.CalcHeight(new GUIContent(i.Note), w - 70 * s)) + 10 * s;
                default:
                    return Mathf.Max(34 * s, btn.CalcHeight(new GUIContent(Label(i)), w)) + (string.IsNullOrEmpty(i.Note) || i.Kind == MenuKind.Chip ? 0 : small.CalcHeight(new GUIContent(i.Note), w) + 2 * s) + 4 * s;
            }
        }

        float SectionHeight(MenuSection sec, float w, float s)
        {
            float iw = w - 24 * s, h = 12 * s + 26 * s;
            if (!string.IsNullOrEmpty(sec.Note)) h += small.CalcHeight(new GUIContent(sec.Note), iw) + 6 * s;
            foreach (var i in sec.Items) h += ItemHeight(i, iw, s) + 4 * s;
            return h + 8 * s;
        }

        static string Label(MenuItem i) => i.Value != null ? $"{i.Label}   {i.Value}" : i.Label;

        float DrawSection(MenuSection sec, float x, float y, float w, float s)
        {
            float h = SectionHeight(sec, w, s);
            var box = new Rect(x, y, w, h);
            Fill(box, Panel);
            Frame(box, sec.Border is Rgb b ? Kit.C(b) : Line, 1.5f * s);
            float ix = x + 12 * s, iw = w - 24 * s, iy = y + 10 * s;
            GUI.Label(new Rect(ix, iy, iw, 24 * s), sec.Title, h3); iy += 26 * s;
            if (!string.IsNullOrEmpty(sec.Note)) { float nh = small.CalcHeight(new GUIContent(sec.Note), iw); GUI.Label(new Rect(ix, iy, iw, nh), sec.Note, small); iy += nh + 6 * s; }
            foreach (var i in sec.Items)
            {
                float ih = ItemHeight(i, iw, s);
                DrawItem(i, new Rect(ix, iy, iw, ih), s);
                iy += ih + 4 * s;
            }
            return h;
        }

        void DrawItem(MenuItem i, Rect r, float s)
        {
            Color? c = i.Color is Rgb rgb ? Kit.C(rgb) : (Color?)null;
            switch (i.Kind)
            {
                case MenuKind.Text:
                {
                    var st = new GUIStyle(text); if (c.HasValue) st.normal.textColor = c.Value;
                    GUI.Label(r, i.Label, st);
                    return;
                }
                case MenuKind.Row:
                {
                    var l = new GUIStyle(row); var v = new GUIStyle(rowR);
                    if (c.HasValue) { l.normal.textColor = c.Value; v.normal.textColor = c.Value; }
                    GUI.Label(new Rect(r.x, r.y, r.width * 0.62f, r.height), i.Label, l);
                    GUI.Label(new Rect(r.x + r.width * 0.62f, r.y, r.width * 0.38f, r.height), i.Value ?? "", v);
                    Fill(new Rect(r.x, r.yMax - 1, r.width, 1), Line);
                    return;
                }
                case MenuKind.Card:
                case MenuKind.Part:
                {
                    bool alt = i.AltId != null;
                    var main = new Rect(r.x, r.y, r.width - (alt ? 52 * s : 0), r.height);
                    if (i.On) Fill(main, new Color(0.18f, 0.7f, 0.42f, 0.16f));
                    GUI.enabled = i.Pressable;
                    if (GUI.Button(main, GUIContent.none, card) && i.Id != null) flow.Click(i.Id);
                    GUI.enabled = true;
                    Fill(new Rect(main.x, main.y, 5 * s, main.height), c ?? (i.Kind == MenuKind.Part ? Amber : Dim));
                    var nm = new GUIStyle(text) { fontStyle = FontStyle.Bold, wordWrap = false };
                    if (i.Id != null && !i.Enabled) nm.normal.textColor = Dim;
                    GUI.Label(new Rect(main.x + 14 * s, main.y + 4 * s, main.width * 0.6f, 24 * s), (i.Id != null && i.Kind == MenuKind.Card && i.AltId != null ? "+ " : "") + i.Label, nm);
                    var vs = new GUIStyle(small) { alignment = TextAnchor.UpperRight, wordWrap = false };
                    GUI.Label(new Rect(main.x + main.width * 0.4f, main.y + 6 * s, main.width * 0.6f - 10 * s, 22 * s), i.Value ?? "", vs);
                    if (!string.IsNullOrEmpty(i.Note)) GUI.Label(new Rect(main.x + 14 * s, main.y + 30 * s, main.width - 24 * s, main.height - 34 * s), i.Note, small);
                    if (alt)
                    {
                        GUI.enabled = i.AltEnabled;
                        if (GUI.Button(new Rect(r.xMax - 46 * s, r.y, 46 * s, r.height), i.AltLabel ?? "−", new GUIStyle(btn) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(22 * s) })) flow.Click(i.AltId);
                        GUI.enabled = true;
                    }
                    Focus(i, main, s);
                    return;
                }
                default:
                {
                    float bh = Mathf.Max(34 * s, btn.CalcHeight(new GUIContent(Label(i)), r.width));
                    var br = new Rect(r.x, r.y, r.width, bh);
                    var st = new GUIStyle(i.On ? btnOn : btn); if (c.HasValue && !i.On) st.normal.textColor = c.Value;
                    if (i.Primary) st.fontStyle = FontStyle.Bold;
                    GUI.enabled = i.Pressable;
                    if (GUI.Button(br, (i.On ? "● " : "") + Label(i), st)) flow.Click(i.Id);
                    GUI.enabled = true;
                    if (!string.IsNullOrEmpty(i.Note) && i.Kind != MenuKind.Chip) GUI.Label(new Rect(r.x, r.y + bh + 2 * s, r.width, r.height - bh), i.Note, small);
                    Focus(i, br, s);
                    return;
                }
            }
        }

        void Focus(MenuItem i, Rect r, float s)
        {
            if (!flow.PadActive || i.Id == null || i.Id != flow.Focus.Id) return;
            Frame(r, Amber, 3 * s);
            focusRect = r; focusSeen = true;
        }

        void Keys(Event e)
        {
            PadButton? b = null;
            switch (e.keyCode)
            {
                case KeyCode.UpArrow: b = PadButton.Up; break;
                case KeyCode.DownArrow: b = PadButton.Down; break;
                case KeyCode.LeftArrow: b = PadButton.Left; break;
                case KeyCode.RightArrow: b = PadButton.Right; break;
                case KeyCode.Return: case KeyCode.KeypadEnter: b = PadButton.Confirm; break;
                case KeyCode.Escape: case KeyCode.Backspace: b = PadButton.Back; break;
            }
            if (b == null) return;
            // Enter with no focus yet presses the first control (the main action), as the controller's A does
            flow.OnPad(PadStyle.Keyboard, b.Value, true);
            e.Use();
        }
    }
}
