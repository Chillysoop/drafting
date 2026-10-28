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
            Shot("New career (title screen with save slots)");
            m.Press("title.start");
            Shot("Garage before race 1");
            m.Press("deck");
            m.Career.Collection.AddRange(new[] { "slingshot", "bump_run", "high_line" });
            Shot("Race deck builder");
            m.Press("deck.done");
            m.Press("go");
            var (desk, _) = MousePlayer.Play(new Race(m.TakeRace()));
            m.RaceFinished(desk.Race);
            Shot("Reward screen after race 1 (played by the mouse bot)");
            var R = m.Career.Reward;
            m.Press("reward.card:" + R.Cards[0]);
            if (R.Parts != null) m.Press("reward.part:" + R.Parts[0]);
            Shot("Reward screen after picking");
            m.Press("reward.go");
            Shot("Garage before race 2");
            m.Press("quit");
            Shot("Title after Save and quit (two careers saved, Continue picks the newest)");
            m.Press("title.delete:2");
            Shot("Delete takes a second press");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"wrote {outPath}");
            return 0;
        }
    }
}
