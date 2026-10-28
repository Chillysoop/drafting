using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 31's hand and beats HUD without Unity. `mouse [tracks] [n] [persona] [seed]` plays races with the mouse player
    /// (pointer and P key only, through the HUD's layout and input) and prints what it did. `hud [track] [seed] [out] [n]`
    /// plays one and draws the HUD as SVG at moments worth a look (a draw beat, a card in slow-mo, a drag over the toss bin, a
    /// pit stop being planned, the release, the finish).
    /// </summary>
    public static class HudShots
    {
        public static int Mouse(string tracks, int n, string persona, uint seed0)
        {
            var list = tracks == "all" ? Tracks.All.ToList() : tracks.Split(',').Select(Tracks.ById).ToList();
            Console.WriteLine("track   seed  fin exp start  plays drag click hold toss beats stops perfect  focus-out  real-min  ms");
            int bad = 0;
            foreach (var t in list)
                for (int i = 0; i < n; i++)
                {
                    uint seed = seed0 + (uint)i;
                    var sw = Stopwatch.StartNew();
                    var (desk, bot) = MousePlayer.Run(t, seed, persona);
                    var r = desk.Race; var res = r.Result();
                    if (!r.Done) bad++;
                    Console.WriteLine($"{t.Id,-7}{seed,5} {(res.MyDnf ? "DNF" : "P" + res.MyFinish),4} {res.MyExpected,3} {res.MyStart,5}  {desk.Plays,5} {bot.Drags,4} {bot.Clicks,5} {bot.Holds,4} {desk.Tosses,4} {desk.BeatStops,5} {r.CardStats.PitStops,5} {r.CardStats.Perfect,7}  {r.CardStats.Plays,9}  {desk.Clock / 60,8:F1} {sw.ElapsedMilliseconds,5}");
                }
            return bad == 0 ? 0 : 1;
        }

        public static int Run(string track, uint seed, string outPath, int n)
        {
            var t = Tracks.ById(track);
            var desk = new PlayDesk(new RaceDriver(PlayDesk.NewRace(t, seed, "drafter")));
            desk.Driver.SpeedIndex = 1;
            var ro = new HudReadouts();
            var lay = new HudLayout();
            var input = new HudInput(desk, lay);
            var bot = new MousePlayer(desk, lay, ro, input);
            const double W = 1920, H = 1080;
            var want = new List<(string name, Func<bool> when)>
            {
                ("Draw beat: the race waits for you", () => desk.BeatPaused && desk.Race.Hand.Count >= 4),
                ("A card picked: slow-mo, Focus draining, gauge live", () => desk.Slowing && desk.Aim == null && desk.Drag == null && desk.Race.FocusMeter < 0.9),
                ("Dragging a card up to play it", () => desk.Drag != null && desk.Drag.On && desk.Drag.OverPlay),
                ("Dragging a card onto the toss bin", () => desk.Drag != null && desk.Drag.On && desk.Drag.OverBin),
                ("Holding the Play button", () => desk.Aim != null && !desk.Aim.Key),
                ("Move in progress, combo live", () => desk.Race.Attempt != null && desk.Race.ComboLeft > 0),
                ("Pit stop: planning the stop", () => desk.Race.Holding && desk.PlanT > 0.5),
                ("Pit stop: waiting to drop the jack", () => ro.Pit.Stop == PitGamePhase.Work && ro.Pit.Elapsed > ro.Pit.Total * 0.6),
                ("Caution: your pit call", () => ro.Pit.Caution && !ro.Pit.CallLocked),
            };
            var got = new HashSet<string>();
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=utf-8><title>Hand and beats HUD</title><style>body{background:#05070c;color:#dfe6f3;font:14px system-ui;margin:16px}figure{margin:0 0 24px}figcaption{margin:4px 0 6px;font-weight:600}svg{width:100%;max-width:1600px;height:auto;border-radius:6px}</style>");
            sb.Append($"<h2>{t.Name}, seed {seed}: the step 31 HUD at 1920x1080, played by the mouse player</h2>");
            int shots = 0;
            for (int f = 0; f < 400000 && !desk.Race.Done && shots < n; f++)
            {
                desk.Frame(1 / 30.0);
                ro.Update(desk);
                lay.Update(W, H, desk, ro);
                bot.Frame();
                lay.Update(W, H, desk, ro);
                foreach (var (name, when) in want)
                {
                    if (got.Contains(name) || !when()) continue;
                    got.Add(name); shots++;
                    sb.Append($"<figure><figcaption>{Esc(name)} · t {desk.Race.T:F1} s, lap {desk.Race.LapOf(desk.Race.Me)}</figcaption>{Svg(desk, ro, lay, W, H)}</figure>");
                    break;
                }
            }
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"wrote {outPath}: {shots} shots ({string.Join(", ", got)})");
            return 0;
        }

        static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

        /// <summary>The HUD as SVG, drawn the way HandHud draws it (same layout, same readouts), over a close-up of the track.</summary>
        static string Svg(PlayDesk d, HudReadouts ro, HudLayout L, double W, double H)
        {
            var sb = new StringBuilder();
            double s = L.S;
            sb.Append($"<svg viewBox='0 0 {W} {H}' xmlns='http://www.w3.org/2000/svg' font-family='system-ui,sans-serif'>");
            var me = d.Driver.Poses.Find(p => p.IsMe);
            var bg = ViewFrames.Frame(d.Driver, Looks.Default, me.P, 1.6, "");
            int i0 = bg.IndexOf("<svg", StringComparison.Ordinal), i1 = bg.IndexOf("</svg>", StringComparison.Ordinal);
            if (i0 >= 0 && i1 > i0)
            {
                var inner = bg.Substring(i0, i1 + 6 - i0);
                inner = System.Text.RegularExpressions.Regex.Replace(inner, "^<svg width='[^']*' height='[^']*'", $"<svg x='0' y='0' width='{W}' height='{H}' preserveAspectRatio='xMidYMid slice'");
                sb.Append(inner);
            }
            if (d.Slowing) sb.Append($"<rect x='0' y='0' width='{W}' height='{H}' fill='none' stroke='#4c9aff' stroke-opacity='.35' stroke-width='{F(20 * s)}'/><text x='{W / 2}' y='{F(28 * s)}' fill='#7fd4ff' font-size='{F(18 * s)}' text-anchor='middle' font-weight='700'>SLOW-MO</text>");

            void Rect(Box b, string fill, string stroke = null, double sw = 1, double op = 1) =>
                sb.Append($"<rect x='{F(b.X)}' y='{F(b.Y)}' width='{F(b.W)}' height='{F(b.H)}' fill='{fill}' fill-opacity='{op.ToString("F2", CultureInfo.InvariantCulture)}'{(stroke != null ? $" stroke='{stroke}' stroke-width='{F(sw)}'" : "")}/>");
            void Text(double x, double y, string t, string col, double size, string anchor = "start", string weight = "400") =>
                sb.Append($"<text x='{F(x)}' y='{F(y)}' fill='{col}' font-size='{F(size * s)}' text-anchor='{anchor}' font-weight='{weight}'>{Esc(t)}</text>");
            void Wrap(double x, double y, double w, string text, int max)
            {
                var line = ""; int n = 0;
                foreach (var w0 in (text ?? "").Split(' '))
                {
                    if ((line + " " + w0).Length * 5.6 * s > w && line.Length > 0) { if (n++ >= max) return; Text(x, y, line.Trim(), "#9aa3b5", 10); y += 12 * s; line = ""; }
                    line += " " + w0;
                }
                if (n < max && line.Trim().Length > 0) Text(x, y, line.Trim(), "#9aa3b5", 10);
            }
            void Panel(Box b, string title)
            {
                Rect(b, "#0b1220", "#ffffff22", 1, 0.9);
                if (title != null) { Rect(new Box(b.X + 8 * s, b.Y - 8 * s, (title.Length * 7 + 18) * s, 16 * s), "#e9b52f"); Text(b.X + 8 * s + (title.Length * 7 + 18) * s / 2, b.Y + 4 * s, title, "#000", 11, "middle", "700"); }
            }
            void Bands(Box b, Odds o)
            {
                double x = b.X;
                for (int i = 4; i >= 0; i--) { double w = o.P[i] * b.W; if (w > 0) Rect(new Box(x, b.Y, w, b.H), CardStyle.Tier((Tier)i).ToString()); x += w; }
            }

            // dash
            var da = ro.Dash; Panel(L.Dash, "CAR");
            double dx = L.Dash.X + 12 * s, dy = L.Dash.Y + 14 * s, dw = L.Dash.W - 24 * s;
            void Meter(double y, string lab, int pct, Threat? th)
            {
                Text(dx, y + 19 * s, lab, "#9aa3b5", 10, "start", "700");
                var bar = new Box(dx + 60 * s, y + 9 * s, dw - 120 * s, 12 * s);
                Rect(bar, "#ffffff14"); Rect(new Box(bar.X, bar.Y, bar.W * pct / 100.0, bar.H), CardStyle.Condition(pct).ToString());
                Text(bar.Right + 56 * s, y + 20 * s, pct + "%", CardStyle.Condition(pct).ToString(), 15, "end", "700");
                if (th is Threat t) { Rect(new Box(dx - 4 * s, y - 1 * s, dw + 8 * s, 32 * s), "none", t.Red ? "#e5484d" : "#f08a24", 1.5 * s); Text(bar.X, y + 31 * s, t.Tag, t.Red ? "#e5484d" : "#f08a24", 10, "start", "700"); }
            }
            Meter(dy, "TIRES", da.Tires, da.CarThreat); Meter(dy + 36 * s, "BODY", da.Body, null);
            dy += 76 * s;
            Text(dx, dy + 30 * s, "P" + da.Pos, da.Pos <= da.Expected ? "#2fb36a" : "#fff", 26, "start", "700");
            Text(dx + 86 * s, dy + 13 * s, $"exp P{da.Expected}   {da.Ahead}", "#9aa3b5", 12);
            Text(dx + 86 * s, dy + 31 * s, da.PosThreat is Threat pt ? pt.Tag : da.Behind, da.PosThreat is Threat pt2 ? (pt2.Red ? "#e5484d" : "#f08a24") : "#9aa3b5", 12);
            dy += 48 * s;
            void Pips(double y, string lab, int v, int max, string on, Threat? th)
            {
                Text(dx, y + 14 * s, lab, "#9aa3b5", 10, "start", "700");
                for (int k = 0; k < max; k++) Rect(new Box(dx + 92 * s + k * 22 * s, y + 4 * s, 18 * s, 12 * s), k < v ? on : "#ffffff1a");
                if (th is Threat t) Text(dx + 92 * s + max * 22 * s + 4 * s, y + 14 * s, t.Tag, t.Red ? "#e5484d" : "#f08a24", 10, "start", "700");
            }
            Pips(dy, "MOMENTUM", da.Momentum, 5, "#e9b52f", da.MomThreat); Pips(dy + 24 * s, "HEAT", da.Heat, 5, "#e5484d", da.HeatThreat);
            if (!string.IsNullOrEmpty(da.Banked)) Text(dx, dy + 64 * s, da.Banked, "#e9b52f", 10, "start", "700");

            // gauge
            var g = ro.Gauge; Panel(L.Gauge, "LIVE ODDS");
            double gx = L.Gauge.X + 12 * s, gw = L.Gauge.W - 24 * s, gy = L.Gauge.Y + 12 * s;
            Text(gx, gy + 15 * s, g.Title, g.Has ? g.Color.ToString() : "#9aa3b5", 15, "start", "700");
            Text(gx + gw, gy + 15 * s, g.Status, g.Armed ? "#e9b52f" : "#9aa3b5", 13, "end", "700");
            if (g.Has)
            {
                Text(gx + gw / 2, gy + 70 * s, g.Succ + "%", CardStyle.Odds(g.Odds.Succ).ToString(), 44, "middle", "800");
                Bands(new Box(gx, gy + 80 * s, gw, 12 * s), g.Odds);
                double ty = gy + 112 * s;
                foreach (var tg in g.Tags) { Text(gx, ty, $"{(tg.Value > 0 ? "+" : "")}{tg.Value:F0}  {tg.Label}", tg.Value > 0 ? "#2fb36a" : "#e5484d", 10, "start", "700"); ty += 14 * s; }
            }
            bool aiming = d.Aim != null;
            Rect(L.PlayButton, aiming ? "#2fb36a" : g.CanPlay ? "#2fb36a" : "#ffffff", g.CanPlay || aiming ? "#2fb36a" : "#ffffff22", 1.5 * s, aiming ? 0.6 : g.CanPlay ? 0.25 : 0.05);
            Text(L.PlayButton.CX, L.PlayButton.CY + 5 * s, g.PlayLabel, "#fff", 14, "middle", "700");

            // beat clock
            var c = ro.Clock; Panel(L.Clock, "BEAT CLOCK");
            string cc = c.State == ClockState.Draw ? "#e9b52f" : c.State == ClockState.Soon ? "#e5484d" : c.State == ClockState.Mid ? "#f08a24" : c.State == ClockState.Idle ? "#9aa3b5" : "#fff";
            Text(L.Clock.X + L.Clock.W * 0.28, L.Clock.Y + 58 * s, c.Num, cc, 44, "middle", "800");
            Text(L.Clock.X + L.Clock.W * 0.56, L.Clock.Y + 32 * s, c.Label, "#fff", 15, "start", "700");
            Text(L.Clock.X + L.Clock.W * 0.56, L.Clock.Y + 50 * s, c.Unit + " " + c.Sub, c.Slow ? "#7fd4ff" : "#9aa3b5", 12);
            Rect(new Box(L.Clock.X + 12 * s, L.Clock.Bottom - 22 * s, L.Clock.W - 24 * s, 8 * s), "#ffffff14");
            Rect(new Box(L.Clock.X + 12 * s, L.Clock.Bottom - 22 * s, (L.Clock.W - 24 * s) * c.Frac, 8 * s), cc);

            // pit dock
            var p = ro.Pit;
            if (p.Show)
            {
                Panel(L.PitDock, "PIT");
                Text(L.PitDock.X + 12 * s, L.PitDock.Y + 26 * s, p.Title, p.Caution ? "#e9b52f" : "#fff", 15, "start", "700");
                foreach (var (hit, box, label) in L.PitBoxes)
                {
                    bool on = hit.Kind == HitKind.Strategy ? (int)p.Strategy == hit.Arg : hit.Kind == HitKind.CautionCall ? (int)p.Mine == hit.Arg : hit.Kind == HitKind.PitButton && p.Requested;
                    Rect(box, on ? "#e9b52f" : "#ffffff", on ? "#e9b52f" : "#ffffff22", 1, on ? 0.35 : 0.07);
                    Text(box.CX, box.CY + 4 * s, label, "#fff", 12, "middle");
                    if (hit.Kind == HitKind.CautionCall && (int)p.Chief == hit.Arg) Text(box.CX, box.Bottom + 11 * s, "chief", "#9aa3b5", 10, "middle");
                }
                Text(L.PitDock.X + 12 * s, L.PitDock.Bottom - 32 * s, p.Plan, "#9aa3b5", 12);
                Text(L.PitDock.X + 12 * s, L.PitDock.Bottom - 12 * s, p.Fuel, "#9aa3b5", 12);
            }

            // console
            Panel(L.Console, null);
            var fo = ro.Focus; var fb = L.FocusBar;
            Text(fb.X, fb.CY + 4 * s, "FOCUS", "#7fd4ff", 10, "start", "700");
            var bar2 = new Box(fb.X + 52 * s, fb.Y + 4 * s, fb.W - 52 * s, fb.H * 0.45);
            Rect(bar2, "#ffffff14");
            Rect(new Box(bar2.X, bar2.Y, bar2.W * fo.Fill, bar2.H), fo.Free ? "#2fb36a" : fo.Low ? "#e5484d" : fo.Mid ? "#f08a24" : "#7fd4ff");
            for (int k = 1; k < 8; k++) Rect(new Box(bar2.X + bar2.W * k / 8, bar2.Y, 1, bar2.H), "#000", null, 1, 0.5);
            Text(bar2.X, bar2.Bottom + 11 * s, fo.Pct + "  " + fo.Note, fo.Refill ? "#2fb36a" : "#9aa3b5", 10, "start", "700");
            var co = ro.Combo; var cb = L.ComboBar;
            if (co.Live)
            {
                Text(cb.X, cb.Y + 13 * s, $"COMBO x{co.Link + 1}  {co.Left:F1} s", "#4aa8ff", 10, "start", "700");
                Rect(new Box(cb.X, cb.Y + cb.H * 0.55, cb.W, cb.H * 0.3), "#ffffff14");
                Rect(new Box(cb.X, cb.Y + cb.H * 0.55, cb.W * co.Frac, cb.H * 0.3), co.Family is CardFamily cf ? CardStyle.FamilyInk(cf).ToString() : "#4aa8ff");
            }
            else
            {
                Text(cb.X, cb.Y + 13 * s, "No combo: land a card, then play one that glows blue", "#9aa3b5", 10, "start", "700");
                Text(cb.X, cb.Y + 30 * s, "CREW BOARD", "#9aa3b5", 10, "start", "700");
                for (int k = 0; k < co.BoardMax; k++) Rect(new Box(cb.X + (92 + k * 16) * s, cb.Y + cb.H * 0.62, 12 * s, 8 * s), k < co.Board ? "#e9b52f" : "#ffffff1f");
            }
            bool overBin = d.Drag != null && d.Drag.On && d.Drag.OverBin;
            Rect(L.TossBin, "#4aa8ff", overBin || co.BinLive ? "#4aa8ff" : "#ffffff22", overBin ? 2 * s : 1, overBin ? 0.45 : 0.06);
            Text(L.TossBin.CX, L.TossBin.CY + 5 * s, "TOSS", overBin ? "#fff" : "#9aa3b5", 15, "middle", "700");
            if (d.Drag != null && d.Drag.On) Text(L.Console.CX, L.Console.Bottom + 12 * s, co.BinText, co.BinLive ? "#4aa8ff" : "#9aa3b5", 10, "middle", "700");
            double tx = L.Tiles.X;
            foreach (var tl in co.Tiles)
            {
                string lab = tl.Ready ? tl.Text + "  READY" : $"{tl.Text}  {tl.Left:F0} s";
                double w = (lab.Length * 7 + 16) * s;
                Rect(new Box(tx, L.Tiles.Y, w, L.Tiles.H), tl.Glow ? "#4aa8ff" : "#0b1220", tl.Ready ? "#e9b52f" : "#4aa8ff", 1, tl.Glow ? 0.4 : 0.9);
                Text(tx + w / 2, L.Tiles.CY + 4 * s, lab, "#fff", 10, "middle", "700");
                tx += w + 6 * s;
            }
            // booth
            if (d.Driver.Booth.Count > 0)
            {
                double bh = d.Driver.Booth.Count * 22 * s + 12 * s;
                var bb = new Box(L.Booth.X, L.Booth.Bottom - bh, L.Booth.W, bh);
                Rect(bb, "#0b1220", null, 1, 0.86);
                for (int k = 0; k < d.Driver.Booth.Count; k++) Text(bb.X + 10 * s, bb.Y + 22 * s + k * 22 * s, d.Driver.Booth[k], k == d.Driver.Booth.Count - 1 ? "#fff" : "#9aa3b5", 14);
            }

            // chips
            foreach (var (hit, box, label) in L.ChipBoxes)
            {
                bool on = hit.Kind == HitKind.Sort ? (int)d.Sort == hit.Arg : hit.Kind == HitKind.AllFamilies ? d.Filter == null : hit.Kind == HitKind.Family ? d.Filter == (CardFamily)hit.Arg : hit.Kind == HitKind.FadeRisk && d.FadeRisk;
                string col = hit.Kind == HitKind.Family ? CardStyle.FamilyInk((CardFamily)hit.Arg).ToString() : "#ffffff";
                Rect(box, col, on ? col : "#ffffff22", 1, on ? 0.28 : 0.06);
                Text(box.CX, box.CY + 4 * s, label, on ? "#fff" : "#9aa3b5", 12, "middle");
            }

            // cards
            void Card(CardFace f, Box r, bool ghost)
            {
                bool dragging = !ghost && d.Drag != null && d.Drag.On && d.Drag.Uid == f.Uid;
                double a = ghost ? 1 : dragging ? 0.3 : f.Faded ? 0.35 : f.Off ? 0.55 : 1;
                sb.Append($"<g opacity='{a.ToString("F2", CultureInfo.InvariantCulture)}'>");
                string edge = f.Selected ? "#e9b52f" : f.Net >= 5 ? "#2fb36a" : f.Net <= -5 ? "#e5484d" : CardStyle.FamilyInk(f.Family).ToString();
                Rect(r, "#141a26", edge, f.Selected ? 3 * s : 1.5 * s, 0.97);
                double x = r.X + 6 * s, y = r.Y + 6 * s;
                if (f.Key > 0) Text(x, y + 13 * s, f.Key.ToString(), "#9aa3b5", 10, "start", "700");
                Rect(new Box(x + 12 * s, y, 22 * s, 18 * s), f.Color.ToString());
                Text(x + 23 * s, y + 13 * s, CardRules.FamilyName(f.Family).Substring(0, 1), "#fff", 10, "middle", "700");
                string cost = ((f.TireCost > 0 ? $"T{f.TireCost}% " : "") + (f.MomCost > 0 ? $"M{f.MomCost} " : "") + (f.Heats ? "H+1" : "")).Trim();
                Text(x + 38 * s, y + 13 * s, cost, "#ffcc80", 10, "start", "700");
                if (f.Combo > 0 || f.Boost > 0)
                {
                    Rect(new Box(r.X + 8 * s, r.Y - 16 * s, r.W - 16 * s, 16 * s), f.Combo > 0 ? "#4aa8ff" : "#e9b52f");
                    Text(r.CX, r.Y - 4 * s, (f.Combo > 0 ? $"COMBO x{f.Combo + 1}" : "") + (f.Boost > 0 ? $" +{f.Boost}" : ""), "#000", 11, "middle", "700");
                }
                y += 22 * s;
                if (f.Block != null) Text(r.CX, y + 24 * s, f.Block, "#9aa3b5", 11, "middle");
                else if (!d.ShowOdds) Text(r.CX, y + 32 * s, "?", "#9aa3b5", 30, "middle", "800");
                else
                {
                    Text(r.CX, y + 32 * s, f.Succ + "%", CardStyle.Odds(f.Odds.Succ).ToString(), 30, "middle", "800");
                    Bands(new Box(r.X + 8 * s, y + 42 * s, r.W - 16 * s, 8 * s), f.Odds);
                    if (Math.Abs(f.BaseSucc - f.Succ) >= 3) Rect(new Box(r.X + 8 * s + (r.W - 16 * s) * f.BaseSucc / 100.0 - 1, y + 40 * s, 2, 12 * s), "#fff");
                    if (f.Wreck >= 5) Text(r.X + 8 * s, y + 63 * s, $"WRECK {f.Wreck}%", "#e5484d", 10, "start", "700");
                    double cx = r.X + 8 * s;
                    foreach (var m in f.Chips)
                    {
                        if (cx + 38 * s > r.Right - 4 * s) break;
                        Rect(new Box(cx, y + 66 * s, 38 * s, 14 * s), m.Value > 0 ? "#2fb36a" : "#e5484d", null, 1, 0.35);
                        Text(cx + 19 * s, y + 77 * s, (m.Value > 0 ? "+" : "") + m.Value + " " + m.Kind.ToUpperInvariant().Substring(0, Math.Min(3, m.Kind.Length)), "#fff", 9, "middle", "700");
                        cx += 41 * s;
                    }
                }
                y = r.Y + 112 * s;
                Text(r.X + 6 * s, y + 14 * s, f.Name, "#fff", 14, "start", "700");
                Wrap(r.X + 6 * s, y + 32 * s, r.W - 12 * s, f.Text, 4);
                for (int k = 0; k < f.SetsUp.Count && k < 3; k++) Rect(new Box(r.X + (20 + k * 14) * s, r.Bottom - 13 * s, 10 * s, 8 * s), CardStyle.FamilyInk(f.SetsUp[k]).ToString());
                if (f.Best) { Rect(new Box(r.Right - 40 * s, r.Bottom - 16 * s, 36 * s, 13 * s), "#2fb36a"); Text(r.Right - 22 * s, r.Bottom - 6 * s, "BEST", "#000", 10, "middle", "700"); }
                sb.Append("</g>");
            }
            int? top = d.Race.Selected;
            foreach (var (uid, box) in L.CardBoxes) if (uid != top) { var f = d.Face(uid); if (f != null) Card(f, box, false); }
            if (top is int tt && L.CardBox(tt) is Box tb && d.Face(tt) is CardFace tf) Card(tf, tb, false);

            // pit stop hand
            if (p.Stop == PitGamePhase.Plan || p.Stop == PitGamePhase.Work)
            {
                Panel(L.PitStop, "PIT STOP");
                if (p.Stop == PitGamePhase.Plan)
                {
                    Text(L.PitStop.X + 12 * s, L.PitStop.Y + 26 * s, $"Plan your stop · {p.PlanLeft:F0} s   Tires {p.Tire:F1} s · Fuel {p.FuelLane:F1} s · Adjust {p.Adjust:F1} s  =  {p.Total:F1} s", "#fff", 15, "start", "700");
                    int k = 0;
                    foreach (var (hit, box, _) in L.StopBoxes)
                    {
                        if (hit.Kind == HitKind.PitCard)
                        {
                            var pc = p.Hand[k++];
                            Rect(box, pc.on ? "#2fb36a" : "#141a26", pc.on ? "#2fb36a" : "#ffffff22", pc.on ? 2 * s : 1, pc.on ? 0.22 : 0.97);
                            Text(box.X + 6 * s, box.Y + 18 * s, pc.name, "#fff", 14, "start", "700");
                            Text(box.X + 6 * s, box.Y + 34 * s, pc.lane + (pc.prep ? " · Prep" : ""), "#e9b52f", 10, "start", "700");
                            Wrap(box.X + 6 * s, box.Y + 52 * s, box.W - 12 * s, pc.text, 6);
                            Text(box.X + 6 * s, box.Bottom - 8 * s, pc.on ? "IN" : pc.block ?? "Click to use", pc.on ? "#2fb36a" : pc.block != null ? "#e5484d" : "#9aa3b5", 10, "start", "700");
                        }
                        else
                        {
                            bool go = hit.Kind == HitKind.PitGo;
                            Rect(box, go ? "#2fb36a" : "#ffffff", go ? "#2fb36a" : "#ffffff22", 1, go ? 0.5 : 0.08);
                            Text(box.CX, box.CY + 5 * s, go ? "GO" : "Crew chief's plan", "#fff", 15, "middle", "700");
                        }
                    }
                }
                else
                {
                    Text(L.PitStop.X + 12 * s, L.PitStop.Y + 26 * s, "Drop the jack when the slowest lane is done: early risks a loose wheel, late loses time", "#fff", 15, "start", "700");
                    double span = p.Total + p.Window * 2.5 + 0.5;
                    var pb = new Box(L.PitStop.X + 20 * s, L.PitStop.Y + 90 * s, L.PitStop.W - 40 * s, 26 * s);
                    Rect(pb, "#ffffff14");
                    Rect(new Box(pb.X + pb.W * p.Total / span, pb.Y, pb.W * p.Window / span, pb.H), "#2fb36a", null, 1, 0.6);
                    Rect(new Box(pb.X, pb.Y + pb.H * 0.35, pb.W * Math.Min(1, p.Elapsed / span), pb.H * 0.3), "#fff");
                    foreach (var (hit, box, _) in L.StopBoxes) { Rect(box, "#e9b52f", "#e9b52f", 2 * s, 0.35); Text(box.CX, box.CY + 10 * s, "RELEASE", "#fff", 30, "middle", "800"); }
                }
            }
            // pause / draw
            if (d.Driver.Paused)
            {
                Panel(L.PauseBox, null);
                Text(L.PauseBox.CX, L.PauseBox.Y + 40 * s, d.BeatPaused ? "DRAW" : "PAUSED", d.BeatPaused ? "#e9b52f" : "#fff", 30, "middle", "800");
                Text(L.PauseBox.CX, L.PauseBox.Y + 72 * s, d.BeatPaused ? "Plan your next move. Play a card, hold Space, or press P to go green." : "P resumes.", "#9aa3b5", 12, "middle");
            }
            if (d.Toast is Toast to)
            {
                Rect(L.Toast, to.Bg.ToString(), null, 1, 0.95);
                Text(L.Toast.CX, L.Toast.Y + 24 * s, to.Title, "#fff", 15, "middle", "700");
                if (!string.IsNullOrEmpty(to.Sub)) Text(L.Toast.CX, L.Toast.Y + 46 * s, to.Sub, "#ffffffd9", 12, "middle");
            }
            if (d.Drag != null && d.Drag.On && d.Face(d.Drag.Uid) is CardFace gf)
            {
                var gb = new Box(d.Drag.X - L.CardW / 2, d.Drag.Y - L.CardH * 0.55, L.CardW, L.CardH);
                Card(gf, gb, true);
                string gc = d.Drag.OverBin ? "#4aa8ff" : d.Drag.OverPlay ? "#2fb36a" : "#9aa3b5";
                Rect(gb, "none", gc, 3 * s);
                Text(gb.CX, gb.Bottom + 16 * s, d.Drag.OverBin ? "Let go to toss" : d.Drag.OverPlay ? "Let go to play" : "Drag up to play", gc, 12, "middle", "700");
            }
            // pylon stand-in (Unity draws the real one top left)
            var rows = new List<PylonRow>(); Pylon.Rows(d.Race, 10, rows);
            var (trk, right, _) = Pylon.Header(d.Race);
            Rect(new Box(16 * s, 16 * s, 270 * s, 30 * s + rows.Count * 26 * s), "#0b1220", null, 1, 0.86);
            Text(24 * s, 36 * s, trk + "   " + right, "#fff", 15, "start", "700");
            double ry = 46 * s;
            foreach (var row in rows) { Text(24 * s, ry + 18 * s, row.Pos == 0 ? "..." : $"{row.Pos}  #{row.Number} {row.Name}", row.IsMe ? "#e9b52f" : "#fff", 14, "start", row.IsMe ? "700" : "400"); if (row.Pos != 0) Text(280 * s, ry + 18 * s, row.Gap, "#9aa3b5", 13, "end"); ry += 26 * s; }
            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
