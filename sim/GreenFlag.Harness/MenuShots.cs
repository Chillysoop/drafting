using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 34: the career screens as one HTML page (title, garage, deck builder, reward screen after a real race), laid out
    /// the way MenuHud draws them in Unity: header and bank, two columns of sections, the action bar. A look at the screens
    /// without an editor; CI keeps it as an artifact.
    /// </summary>
    public static class MenuShots
    {
        static string H(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        static string Css(Rgb c) => string.Format(CultureInfo.InvariantCulture, "rgb({0:0},{1:0},{2:0})", c.R * 255, c.G * 255, c.B * 255);

        public static int Run(string persona, uint seed, string outPath)
        {
            // step 35: three save slots, one already holding a kart career two races in
            var slots = new SaveSlots(new MemorySaveStore()) { Now = () => new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), Build = "harness" };
            MenuBot.Run(new CareerMenu(slots) { PickPersona = "brawler", NewSeed = seed + 1, PickSlot = 2 }, 2);
            slots.Now = () => new DateTime(2026, 10, 28, 13, 5, 0, DateTimeKind.Utc);
            var m = new CareerMenu(slots) { PickPersona = persona, NewSeed = seed };
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=utf-8><title>Career screens</title><style>body{margin:0;background:#090d18;color:#edf2fa;font:15px system-ui,sans-serif}.pg{width:1600px;margin:0 auto 60px;border:1px solid #333;padding:24px;position:relative}.eb{color:#e9b52f;font-weight:700;font-size:13px}.t{font-size:40px;font-weight:800}.mo{position:absolute;right:24px;top:40px;font-size:28px;font-weight:800;color:#2fb36a}.sub{color:#8c94a6}.no{background:#e9b52f22;color:#e9b52f;padding:6px 10px;margin:6px 0}.cols{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin-top:12px}.sec{background:#ffffff0d;border:1.5px solid #ffffff1f;padding:10px 12px;margin-bottom:14px}.sec h3{margin:0 0 6px;font-size:18px}.n{color:#8c94a6;font-size:13px;margin:2px 0 6px}.row{display:flex;justify-content:space-between;border-bottom:1px solid #ffffff1f;padding:2px 0}.row b{white-space:nowrap;padding-left:12px}.btn,.chip{display:inline-block;background:#2a3142;border:1px solid #444;border-radius:4px;padding:6px 10px;margin:3px 4px 3px 0}.chip.on{color:#2fb36a;font-weight:700}.off{opacity:.45}.card{border:1px solid #444;background:#1c2232;margin:4px 0;padding:6px 10px 6px 14px;border-left:5px solid}.card.on{background:#2fb36a29}.card .v{float:right;color:#8c94a6;font-size:13px}.bar{background:#0006;padding:10px;margin-top:10px}.bar .btn{font-weight:700}.prim{background:#2fb36a59}h2{color:#8c94a6;font-size:14px;margin:30px auto 6px;width:1600px}</style>");
            void Shot(string caption)
            {
                var p = m.Page();
                sb.Append($"<h2>{H(caption)}</h2><div class=pg><div class=eb>{H(p.Eyebrow)}</div><div class=t style=\"{(p.TitleColor is Rgb tc ? "color:" + Css(tc) : "")}\">{H(p.Title)}</div>");
                if (p.Money != null) sb.Append($"<div class=mo>{H(p.Money)}</div>");
                if (p.Sub != null) sb.Append($"<div class=sub>{H(p.Sub)}</div>");
                if (p.Notice != null) sb.Append($"<div class=no>{H(p.Notice)}</div>");
                sb.Append("<div class=cols>");
                for (int col = 0; col < 2; col++)
                {
                    sb.Append("<div>");
                    foreach (var s in p.Sections.Where(s => s.Column == col))
                    {
                        sb.Append($"<div class=sec style=\"{(s.Border is Rgb b ? "border-color:" + Css(b) : "")}\"><h3>{H(s.Title)}</h3>");
                        if (s.Note != null) sb.Append($"<div class=n>{H(s.Note)}</div>");
                        foreach (var i in s.Items)
                        {
                            string col2 = i.Color is Rgb c ? "color:" + Css(c) : "";
                            switch (i.Kind)
                            {
                                case MenuKind.Text: sb.Append($"<div style=\"{col2}\">{H(i.Label)}</div>"); break;
                                case MenuKind.Row: sb.Append($"<div class=row style=\"{col2}\"><span>{H(i.Label)}</span><b>{H(i.Value)}</b></div>"); break;
                                case MenuKind.Card:
                                case MenuKind.Part:
                                    sb.Append($"<div class=\"card{(i.On ? " on" : "")}{(i.Id != null && !i.Enabled ? " off" : "")}\" style=\"border-left-color:{(i.Color is Rgb cc ? Css(cc) : i.Kind == MenuKind.Part ? "#e9b52f" : "#666")}\"><b>{H(i.Label)}</b><span class=v>{H(i.Value)}{(i.AltId != null ? " &nbsp;[−]" : "")}</span><div class=n>{H(i.Note)}</div></div>");
                                    break;
                                default:
                                    sb.Append($"<span class=\"{(i.Kind == MenuKind.Chip ? "chip" : "btn")}{(i.On ? " on" : "")}{(i.Enabled ? "" : " off")}{(i.Primary ? " prim" : "")}\" style=\"{(i.On ? "" : col2)}\" title=\"{H(i.Note)}\">{H(i.Label)}{(i.Value != null ? " &nbsp;" + H(i.Value) : "")}</span>");
                                    if (i.Note != null && i.Kind == MenuKind.Button) sb.Append($"<div class=n>{H(i.Note)}</div>");
                                    break;
                            }
                        }
                        sb.Append("</div>");
                    }
                    sb.Append("</div>");
                }
                sb.Append("</div><div class=bar>");
                foreach (var a in p.Actions) sb.Append($"<span class=\"btn{(a.Primary && a.Enabled ? " prim" : "")}{(a.Enabled ? "" : " off")}\">{H(a.Label)}</span>{(!a.Enabled && a.Note != null ? $"<span class=n> {H(a.Note)}</span>" : "")}");
                sb.Append("</div></div>");
            }
            // step 37: the qualifying run as the Unity QualiHud lays it out (map, brake bar with zones, needle)
            void RunShot(string caption)
            {
                var q = m.Quali;
                var pn = new QualiPanel(q.Plan.Track);
                string P((double x, double y) p) => string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0}", 400 + p.x * 500, 20 + p.y * 500);
                sb.Append($"<h2>{H(caption)}</h2><div class=pg style=\"text-align:center\"><div class=eb>Qualifying · {H(q.Plan.Track.Name)}</div><svg width=1300 height=700 viewBox=\"0 0 1300 700\">");
                sb.Append($"<rect x=400 y=20 width=500 height=500 fill=#ffffff0d /><polyline fill=none stroke=#8c94a6 stroke-width=4 points=\"{string.Join(" ", pn.Outline().Select(P))} {P(pn.At(0))}\"/>");
                var cur = q.Current;
                foreach (var c in q.Plan.Checks.Where(c => c.Lap == (cur?.Lap ?? 0)))
                {
                    var xy = pn.At(c.U).ToTuple();
                    string col = c.Res is QualiMark mk ? Css(QualiPanel.MarkColor(mk)) : c == cur ? "#e9b52f" : "#edf2fa";
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "<circle cx={0:0.0} cy={1:0.0} r=7 fill={2} />", 400 + xy.Item1 * 500, 20 + xy.Item2 * 500, col));
                }
                var car = pn.At(q.CarU);
                sb.Append(string.Format(CultureInfo.InvariantCulture, "<circle cx={0:0.0} cy={1:0.0} r=9 fill=#2fb36a />", 400 + car.x * 500, 20 + car.y * 500));
                double bx = 200, bw = 900, by = 560;
                sb.Append($"<rect x={bx} y={by} width={bw} height=46 fill=#ffffff14 />");
                if (cur != null)
                {
                    string[] zc = { "#9fd0ff59", "#2fb36a99", "#e9b52fe6" };
                    var z = QualiPanel.Zones(cur);
                    for (int i = 0; i < 3; i++) sb.Append(string.Format(CultureInfo.InvariantCulture, "<rect x={0:0.0} y={1} width={2:0.0} height=46 fill={3} />", bx + z[i].a * bw, by, (z[i].b - z[i].a) * bw, zc[i]));
                    if (q.InSweep) sb.Append(string.Format(CultureInfo.InvariantCulture, "<rect x={0:0.0} y={1} width=4 height=62 fill=#edf2fa />", bx + q.Needle * bw - 2, by - 8));
                }
                sb.Append($"<text x=650 y=650 fill=#edf2fa font-size=32 font-weight=700 text-anchor=middle>{H(QualiPanel.Prompt(q))}</text>");
                var pop = QualiPanel.Pop(q);
                if (pop != null) sb.Append($"<text x=650 y=545 fill=#e9b52f font-size=26 font-weight=700 text-anchor=middle>{H(pop)}</text>");
                sb.Append($"<text x=200 y=690 fill=#8c94a6 font-size=15>Chain ×{q.Score.Chain} · Focus {Math.Round(q.Focus * 100)}% · {string.Join("  ", q.LapTimes.Select((t, i) => $"L{i + 1} {t.ToString("0.000", CultureInfo.InvariantCulture)}"))}</text>");
                sb.Append("<rect x=520 y=660 width=0 height=0 /></svg><div><span class=btn style=\"background:#e5484d;font-weight:800;padding:14px 60px\">BRAKE</span></div></div>");
            }

            Shot("New career (title screen with save slots)");
            m.Press("title.start");
            Shot("Garage before race 1: sign a sponsor first (step 37)");
            m.Press("sponsor:" + m.Career.SponsorOffers[0].Id);
            Shot("Garage with the sponsor, race objectives and standings");
            m.Press("deck");
            m.Career.Collection.AddRange(new[] { "slingshot", "bump_run", "high_line" });
            Shot("Race deck builder");
            m.Press("deck.done");
            m.Press("go");
            m.Press("quali.card:" + m.Career.QualiOffer()[0]);
            Shot("Qualifying: pick a card (or none), start the run or skip");
            m.Press("quali.start");
            var qbot = new QualiBot("good", seed);
            while (m.Quali.Time < m.Quali.Plan.Checks[2].S0 + 0.6 * m.Quali.Plan.Sweep) m.QualiTick(1 / 60.0, qbot.Held(m.Quali));
            RunShot("Qualifying run: third corner, needle sweeping (the Unity screen draws this)");
            while (m.QualiTick(1 / 60.0, qbot.Held(m.Quali))) { }
            Shot("Qualifying results: the grid");
            m.Press("quali.race");
            var (desk, _) = MousePlayer.Play(new Race(m.TakeRace()));
            m.RaceFinished(desk.Race);
            Shot("Reward screen after race 1 (played by the mouse bot)");
            var R = m.Career.Reward;
            m.Press("reward.card:" + R.Cards[0]);
            if (R.Parts != null) m.Press("reward.part:" + R.Parts[0]);
            Shot("Reward screen after picking");
            m.Press("reward.go");
            Shot("Garage before race 2: tonight's press scrum comes first (step 38)");
            m.Press("ev.open");
            Shot("Off-track event: every answer's whole d20 check, its odds and what each tier does");
            var opt = m.Career.EventRound().Opts.First(o => !o.Auto);
            m.Press("ev.opt:" + opt.Id);
            Shot("The roll: both dice, the math, the result and what would have helped");
            m.Press("ev.next");
            m.Career.Driver.AddXp(100);
            m.Press("drv");
            Shot("Driver sheet with a level-up point to spend");
            m.Press("drv.back");
            Shot("Garage before race 2");
            m.Press("quit");
            Shot("Title after Save and quit (two careers saved, Continue picks the newest)");
            m.Press("title.delete:2");
            Shot("Delete takes a second press");
            // the season wrap: a kart season of 6 races, then the season-end screen and a promotion
            m = MenuBot.Run(new CareerMenu { PickPersona = "brawler", NewSeed = seed + 2 }, 5, null, new MenuBot { QualiSkill = "great" });
            var sb2 = new MenuBot { QualiSkill = "great" };
            for (int i = 0; i < 2000 && m.Screen != CareerScreen.Season; i++)
            {
                if (m.RaceWanted != null) { m.RaceFinished(MousePlayer.Play(new Race(m.TakeRace())).desk.Race); continue; }
                sb2.Step(m);
            }
            Shot("Season end: final standings, sponsor review and contract offers, each with a Negotiate check");
            m.Press("neg:0");
            Shot("After negotiating the first offer (step 38)");
            int up = m.Career.Offers.FindIndex(o => o.Kind == OfferKind.Promote);
            m.Press("offer:" + Math.Max(0, up));
            Shot("Garage after signing (a promotion when one was offered): next season's sponsor offers");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"wrote {outPath}");
            return 0;
        }
    }
}
