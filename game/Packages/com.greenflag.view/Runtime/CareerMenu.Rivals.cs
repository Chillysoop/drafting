using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    // Rivals (roadmap step 43) as an engine-free page: the inbox (each message with its replies, every d20 check shown in full
    // before you send it), and the class: twelve drivers with their series, team, Grudge, head to head with you and the path
    // their career has taken. The garage shows this week's rival and your teammate; the reward screen shows how each Grudge
    // moved; the season-end screen shows the silly season.
    public sealed partial class CareerMenu
    {
        CareerScreen rivalsFrom = CareerScreen.Garage;
        /// <summary>The class driver whose card is open on the Rivals page (null = none).</summary>
        public string RivalOpen;

        static Rgb GColor(double g) => Rgb.Hex(RivalBook.TierColor(g));
        static string GText(double g) => $"Grudge {RivalBook.Num(g)} · {RivalBook.Tier(g)}";

        bool DoRivals(string key, string arg)
        {
            var c = Career;
            switch (key)
            {
                case "rivals": if (Screen != CareerScreen.Rivals) rivalsFrom = Screen; Screen = CareerScreen.Rivals; return true;
                case "rv.back": RivalOpen = null; Screen = rivalsFrom == CareerScreen.Rivals ? CareerScreen.Garage : rivalsFrom; return true;
                case "rv.who": RivalOpen = RivalOpen == arg ? null : arg; return true;
                case "rv.reply":
                {
                    var a = arg.Split(':');
                    var why = c.Reply(a[0], a[1]);
                    var m = c.Inbox.FirstOrDefault(x => x.Id == a[0]);
                    Toast = why ?? $"{m.Roll}. {m.ReplyText}";
                    return why == null;
                }
            }
            return false;
        }

        string RivalsButtonLabel() { int n = Career.Unanswered; return "Rivals" + (n > 0 ? $" · {n} message{(n > 1 ? "s" : "")}" : ""); }

        /// <summary>The garage's rival panel: this week's rival, your teammate when they're from the class, and the inbox count.</summary>
        void RivalPanel(MenuPage p)
        {
            var c = Career;
            if (c.Class.Count == 0) return;
            var wk = c.RivalId >= 0 ? c.Field.FirstOrDefault(e => e.Id == c.RivalId) : null;
            var wd = c.ClassOf(wk);
            var mate = c.Mate; var md = c.ClassOf(mate);
            if (wd == null && md == null && c.Unanswered == 0) return;
            var s = p.Section("Rivals", 1, "Twelve drivers climb the ladder with you. Each one keeps a Grudge: Warm (3+) blocks you and cuts your passing odds on them, Hot (6+) can pay you back on track, Respect (−2 or less) gives you room.", wd != null && wd.G >= 6 ? Red : (Rgb?)null);
            if (wd != null) s.Row($"This week: {wd.Def.Name}, {wd.Def.Nick}", $"#{wk.Number} · {c.SeriesTeams[wk.Team].Name} · {GText(wd.G)} · you lead {wd.Ahead}–{wd.Behind}", GColor(wd.G));
            if (md != null) s.Row($"Teammate: {md.Def.Name}, {md.Def.Nick}", $"{GText(md.G)} · you lead {md.Ahead}–{md.Behind}", GColor(md.G));
            foreach (var d in c.InField.Where(d => d != wd && d != md && (d.G >= 3 || d.G <= -2))) s.Row($"{d.Def.Name}, {d.Def.Nick}", GText(d.G), GColor(d.G));
            s.Button("rivals", RivalsButtonLabel(), note: c.Unanswered > 0 ? "Replies are d20 checks from your driver sheet" : null);
        }

        /// <summary>The reward screen's rows: each Grudge that moved, and why.</summary>
        void RivalRewardRows(MenuPage p, RaceReport res)
        {
            if (res.Rivals.Count == 0 && res.InboxNew == 0) return;
            var s = p.Section("Rivals", 1, res.InboxNew > 0 ? $"{res.InboxNew} new message{(res.InboxNew > 1 ? "s" : "")} in your inbox (garage › Rivals)." : null);
            foreach (var l in res.Rivals)
            {
                string why = l.Why.Count == 0 ? "no change" : string.Join(" · ", l.Why.Select(w => $"{w.why} {(w.d > 0 ? "+" : "−")}{RivalBook.Num(Math.Abs(w.d))}"));
                s.Row($"{(l.Week ? "This week's rival: " : "")}{l.Name} · P{l.Fin}", $"{RivalBook.Num(l.G0)} → {RivalBook.Num(l.G1)} ({RivalBook.Tier(l.G1)}) · {why}", GColor(l.G1));
            }
            if (res.Paybacks > 0) s.Text($"You took {res.Paybacks} payback{(res.Paybacks > 1 ? "s" : "")} this race.", Red);
        }

        /// <summary>The season-end screen's silly season.</summary>
        void SillyRows(MenuPage p)
        {
            var c = Career;
            if (c.News.Count == 0) return;
            var s = p.Section("Silly season", 0, "Who moved, who stepped up and who lost the ride. Your contract offers are below.");
            foreach (var n in c.News) s.Text(n);
        }

        MenuPage RivalsPage()
        {
            var c = Career;
            var p = new MenuPage
            {
                Screen = CareerScreen.Rivals, Eyebrow = $"Rivals · season {c.Season} · {SeasonRules.SeriesName(c.Series)}", Title = "The class",
                Sub = "Twelve drivers whose careers run alongside yours. They race, move teams and step up a series every off-season, and each one remembers how you raced them, in every series.",
                Money = Money(c.Money), Notice = Toast,
            };
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "rv.back", Label = "Back", Primary = true });
            p.Default = "rv.back";

            var ib = p.Section($"Inbox · {c.Unanswered} unanswered", 0, "Answer a message with a d20 check from your driver sheet, or leave it. A reply can cool a Grudge, or heat one up for the fans.");
            if (c.Inbox.Count == 0) ib.Text("No messages yet. Wreck someone, beat a proud one by a place or two, or race one clean all day, and you'll hear about it.", Dim);
            foreach (var m in c.Inbox)
            {
                var d = c.ClassById(m.Rid);
                ib.Row($"{d.Def.Name} · {m.Subj}", $"season {m.Season}, after race {m.Race}{(m.Track != null ? " · " + m.Track : "")}", GColor(m.G));
                ib.Text(m.Body);
                if (m.Reply != null) { ib.Text($"You: {RivalBook.RepliesFor(m.Kind).First(o => o.Id == m.Reply).Name}. {m.Roll}. {m.ReplyText}", CheckColor(m.Tier ?? CheckTier.Mixed)); continue; }
                foreach (var o in RivalBook.RepliesFor(m.Kind))
                {
                    var b = c.ReplyCheck(m, o);
                    var win = c.ReplyFx(m, o, CheckTier.Success); var lose = c.ReplyFx(m, o, CheckTier.Fail);
                    string G(double g) => g == 0 ? "" : $", Grudge {(g > 0 ? "+" : "−")}{RivalBook.Num(Math.Abs(g > 0 ? g * d.Def.Mul : g))}";
                    ib.Button($"rv.reply:{m.Id}:{o.Id}", $"{o.Name}: {o.Say}",
                        note: $"{DriverRules.Name(o.Skill)}: {ModsText(b)} vs {DcText(b)}, roll {NeedText(b)} ({DriverRules.Pct(b.Pass)}). Made it: {FxText(win.fx)}{G(win.g)}. Missed: {FxText(lose.fx)}{G(lose.g)}.");
                }
            }

            var cl = p.Section("The class", 1, "Click a driver for their card.");
            foreach (var d in c.Class)
            {
                var def = d.Def;
                var e = c.SeatOf(d);
                string where = d.Retired ? "retired" : $"{SeasonRules.SeriesName(d.Series)} · {Teams.For(d.Series)[d.Team].Name}" + (e != null ? (e.Team == c.Team ? " · your teammate" : e.Id == c.RivalId ? " · this week's rival" : " · in your field") : "");
                cl.Add(new MenuItem { Kind = MenuKind.Part, Id = "rv.who:" + def.Id, Label = $"#{def.Number} {def.Name}, {def.Nick}", Value = $"{where} · {GText(d.G)}", Color = GColor(d.G), On = RivalOpen == def.Id, Note = d.Met > 0 ? $"You lead {d.Ahead}–{d.Behind} head to head." : "You haven't raced yet." });
                if (RivalOpen != def.Id) continue;
                cl.Text(def.Text);
                cl.Row("Career", $"{d.Seasons} season{(d.Seasons == 1 ? "" : "s")} · {d.Wins} win{(d.Wins == 1 ? "" : "s")} · {d.Titles} title{(d.Titles == 1 ? "" : "s")} · skill {Math.Round(d.Skill).ToString(inv)}");
                cl.Row("How they hold a Grudge", $"gains ×{def.Mul.ToString("0.##", inv)} · cools {def.Cool.ToString("0.##", inv)} a race · payback ×{def.Pay.ToString("0.##", inv)} · blocks ×{def.Block.ToString("0.##", inv)}{(def.Proud ? " · proud" : "")}{(def.Press ? " · goes to the press" : "")}");
                foreach (var line in d.Path) cl.Text(line, Dim);
                foreach (var h in d.Hist) cl.Text($"Season {h.Season}, race {h.Race}, lap {h.Lap} at {h.Track}: {(h.Kind == "wreck" ? "you wrecked them" : h.Kind == "hit" ? "they wrecked you" : h.Kind == "payback" ? "payback" : "a hard pass")}", h.Kind == "payback" || h.Kind == "hit" ? Red : Amber);
            }
            return p;
        }
    }
}
