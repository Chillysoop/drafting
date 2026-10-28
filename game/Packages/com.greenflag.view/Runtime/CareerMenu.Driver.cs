using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    // The driver sheet, tonight's off-track event and the season-end negotiation (roadmap step 38), as engine-free pages.
    // Every check shows each number by its source, the DC and what moves it, advantage or disadvantage and why, the roll
    // needed and the five-tier odds (Full info; Reveal shows the odds after the roll, Feel never), and what each tier does.
    // After the roll the page shows both dice, the sum, the tier, what changed, and what would have helped.
    public sealed partial class CareerMenu
    {
        /// <summary>Spend 1 Hype for advantage on the next answer (a toggle on the event page).</summary>
        public bool HypeOn;
        /// <summary>Where the driver sheet goes back to.</summary>
        CareerScreen sheetFrom = CareerScreen.Garage;

        static readonly Rgb Violet = Rgb.Hex("#c9bfff"), White = Rgb.Hex("#ffffff"), Pink = Rgb.Hex("#ff7b7f");

        public static Rgb CheckColor(CheckTier t) => t == CheckTier.Crit ? White : t == CheckTier.Success ? Green : t == CheckTier.Mixed ? Amber : t == CheckTier.Fail ? Red : Pink;

        bool DoDriver(string key, string arg)
        {
            var c = Career;
            switch (key)
            {
                case "drv": sheetFrom = Screen; Screen = CareerScreen.Driver; return true;
                case "drv.back": Screen = sheetFrom == CareerScreen.Driver ? CareerScreen.Garage : sheetFrom; return true;
                case "drv.up":
                {
                    var a = (DAttr)Enum.Parse(typeof(DAttr), arg);
                    var why = c.RaiseAttr(a);
                    Toast = why ?? $"{DriverRules.Name(a)} up to {c.Driver.Score(a)}.";
                    return true;
                }
                case "ev.open": HypeOn = false; Screen = CareerScreen.Event; return true;
                case "ev.back": Screen = CareerScreen.Garage; return true;
                case "ev.hype": HypeOn = !HypeOn; return true;
                case "ev.opt":
                {
                    var why = c.EventChoose(arg, HypeOn);
                    HypeOn = false;
                    if (why != null) { Toast = why; return false; }
                    if (c.Event.Done) { Screen = CareerScreen.Garage; Toast = $"{c.Event.Def.Name} done."; }
                    return true;
                }
                case "ev.reroll": return c.EventReroll();
                case "ev.next":
                    if (!c.EventNext()) return false;
                    if (c.Event.Done) { Screen = CareerScreen.Garage; Toast = $"{c.Event.Def.Name} done."; }
                    return true;
                case "ev.walk":
                {
                    var name = c.Event.Def.WalkName;
                    if (!c.EventWalk()) return false;
                    Screen = CareerScreen.Garage;
                    Toast = name + ".";
                    return true;
                }
                case "neg":
                {
                    int i = int.Parse(arg, inv);
                    var n = c.Negotiate(i);
                    if (n == null) return false;
                    Toast = $"{c.Offers[i].Name}: d20 {n.Kept} {DriverRules.Sgn(n.Mod)} = {n.Total} vs DC {n.Dc}. {n.Txt}";
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ text helpers

        /// <summary>What an effect does, in words (the slice's fxChips), or "nothing changes".</summary>
        public static string FxText(OffFx f, OffTrackEvent ev = null)
        {
            if (f == null || f.Empty) return "nothing changes";
            var o = new List<string>();
            string sg(int v) => DriverRules.Sgn(v);
            if (f.Fans != 0) o.Add($"{sg(f.Fans)} Fans");
            if (f.Respect != 0) o.Add($"{sg(f.Respect)} Respect");
            if (f.Hype != 0) o.Add($"{sg(f.Hype)} Hype");
            if (f.Money != 0) o.Add((f.Money > 0 ? "+" : "") + Money(f.Money));
            if (f.Trust != 0) o.Add($"Sponsor trust {sg(f.Trust)}");
            if (f.Per != 0) o.Add((f.Per > 0 ? "+" : "") + Money(f.Per) + " per race (sponsor)");
            if (f.Bonus != 0) o.Add((f.Bonus > 0 ? "+" : "") + Money(f.Bonus) + " goal bonus");
            if (f.Sal != null) o.Add(ev != null && ev.Revealed ? $"salary {Money((int)(SimMath.RoundJs(ev.Ceil * f.Sal.Value / 50) * 50))} a race" : $"salary {Math.Round(f.Sal.Value * 100).ToString(inv)}% of their ceiling");
            if (f.Mom != 0) o.Add($"+{f.Mom} Momentum next green flag");
            if (f.Clause != null) o.Add("Clause: " + Career.ClauseName(f.Clause));
            if (f.Adv) o.Add("Advantage on the next question");
            if (f.Reveal) o.Add("Learn their budget");
            return string.Join(" · ", o);
        }

        static string ModsText(CheckBuild b) => "d20 " + string.Join(" ", b.L.Select(l => $"{DriverRules.Sgn(l.V)} {l.Src}"));
        static string DcText(CheckBuild b) => $"DC {b.Dc}" + (b.Dcl.Count > 0 ? $" (base {b.BaseDc}{string.Concat(b.Dcl.Select(l => $", {DriverRules.Sgn(l.V)} {l.Src}"))})" : "");
        static string ModeText(CheckBuild b) => b.Mode == RollMode.Adv ? "Advantage: roll two dice, keep the higher" : b.Mode == RollMode.Dis ? "Disadvantage: roll two dice, keep the lower" : b.Adv.Count > 0 ? "Advantage and disadvantage cancel: one die" : null;
        public static string OddsText(double[] o) => string.Join(" · ", Enum.GetValues(typeof(CheckTier)).Cast<CheckTier>().Select(t => $"{DriverRules.Name(t)} {DriverRules.Pct(o[(int)t])}"));
        static string NeedText(CheckBuild b) => b.Need >= 20 ? "20 (natural 20 only)" : b.Need + "+";

        /// <summary>All the math of one check, line by line, in a section (the slice's mathHTML, advHTML and oddsHTML).</summary>
        void CheckRows(MenuSection s, CheckBuild b, bool showOdds)
        {
            s.Row(DriverRules.Name(b.Skill), DriverRules.AttrAbbr[(int)DriverRules.AttrOf(b.Skill)] + " check");
            s.Row("d20", "1 to 20");
            foreach (var l in b.L) s.Row("+ " + l.Src, DriverRules.Sgn(l.V), l.V < 0 ? Red : l.K != "attr" && l.V > 0 ? Green : (Rgb?)null);
            s.Row("vs DC", b.BaseDc.ToString(inv));
            foreach (var l in b.Dcl) s.Row("DC " + l.Src, DriverRules.Sgn(l.V), l.V < 0 ? Green : Red);
            foreach (var a in b.Adv) s.Text("▲ " + a, Green);
            foreach (var a in b.Dis) s.Text("▼ " + a, Red);
            var m = ModeText(b);
            if (m != null) s.Text(m, Dim);
            if (showOdds)
            {
                s.Row($"Total {DriverRules.Sgn(b.Mod)} vs DC {b.Dc}: roll needed", NeedText(b));
                s.Row("Gets it (success or mixed)", DriverRules.Pct(b.GetsIt));
                s.Text(OddsText(b.Odds));
            }
            else s.Row($"Total {DriverRules.Sgn(b.Mod)} vs DC {b.Dc}", Career.Info == InfoLevel.Feel ? "Feel mode: odds hidden" : "Odds show once you roll");
        }

        // ------------------------------------------------------------------ garage panel

        void EventPanel(MenuPage p)
        {
            var c = Career; var ev = c.Event;
            if (ev == null) return;
            var E = ev.Def;
            if (ev.Done)
            {
                var s = p.Section("Off track · " + E.Name, 0);
                if (ev.Walked) s.Text($"You skipped it: {FxText(c.MoneyFx(E.Walk), ev)}.", Dim);
                foreach (var h in ev.Hist) s.Row(h.Name, h.Auto ? "no roll" : $"{DriverRules.Name(h.Tier)} · {FxText(h.Fx, ev)}", h.Auto ? Dim : CheckColor(h.Tier));
                return;
            }
            var o = p.Section($"Off track tonight · {E.Name}: {E.Who(c, ev)}", 0, $"{E.Blurb} {E.Rounds.Count} question{(E.Rounds.Count > 1 ? "s" : "")}, each a d20 check. Every answer shows its odds and where each bonus comes from.", Amber);
            if (ev.Ri > 0 || ev.Cur != null) o.Text($"Question {ev.Ri + 1} of {E.Rounds.Count}{(ev.Cur != null ? ": a roll is waiting" : "")}.", Dim);
            o.Button("ev.open", ev.Ri > 0 || ev.Cur != null ? "Back to the " + E.Name.ToLowerInvariant() : E.Go, primary: true);
            var walk = c.MoneyFx(E.Walk);
            if (ev.Cur == null) o.Button("ev.walk", E.WalkName + (walk.Empty ? "" : " · " + FxText(walk, ev)));
        }

        // ------------------------------------------------------------------ the event page

        MenuPage EventPage()
        {
            var c = Career; var ev = c.Event; var d = c.Driver;
            if (ev == null || ev.Done) { Screen = CareerScreen.Garage; return GaragePage(); }
            var E = ev.Def; var x = ev.Ctx;
            var r = c.EventRound();
            var im = d.Image;
            var p = new MenuPage
            {
                Screen = CareerScreen.Event, Eyebrow = $"Off track · {E.Name} · question {ev.Ri + 1} of {E.Rounds.Count}", Title = E.Who(c, ev), Money = Money(c.Money), Notice = Toast,
                Sub = (x.Fin > 0 ? $"After P{x.Fin}{(x.Dnf ? " (DNF)" : "")} at {x.Track} · expected P{x.Exp} · " : "") + $"{c.Name} · {d.Background.Name} · {(im != null ? DriverRules.Image(im).Name : "Fresh face")} · Hype {d.Hype} of {DriverRules.HypeMax}",
            };
            var q = p.Section(r.Q, 0, null, Amber);
            bool full = c.Info == InfoLevel.Full;
            if (ev.Cur == null)
            {
                q.Text("You pick what you say; the answer picks the skill. Every bonus is labeled by where it comes from. A natural 20 is always a critical, a natural 1 always a fumble. Miss by 1 to 4 and you still get it, with a cost. Each answer's tone moves your image on the driver sheet.", Dim);
                if (d.Hype > 0) q.Chip("ev.hype", HypeOn ? "✓ Spending 1 Hype: advantage on this answer" : "Spend 1 Hype for advantage", HypeOn, note: "Or keep it to reroll a miss.");
                int col = 0;
                foreach (var o in r.Opts)
                {
                    var tone = DriverRules.ToneName(o.TX, o.TY);
                    if (o.Auto)
                    {
                        var a = p.Section(o.Name, col, o.Say + (tone != "" ? " · " + tone : ""));
                        a.Text("No roll. Nothing else changes.", Dim);
                        a.Button("ev.opt:" + o.Id, o.Name);
                        col ^= 1;
                        continue;
                    }
                    var b = c.CheckFor(o, HypeOn && d.Hype > 0);
                    var s = p.Section(o.Name + (o.Sig != null ? $" · {DriverRules.Image(o.Sig).Name} only" : ""), col, o.Say + (tone != "" ? " · " + tone : ""), o.Sig != null ? Violet : (Rgb?)null);
                    CheckRows(s, b, full);
                    var t = c.TierFxFor(o);
                    s.Row("Success", FxText(t[(int)CheckTier.Success], ev), Green);
                    s.Row("Mixed", FxText(t[(int)CheckTier.Mixed], ev), Amber);
                    s.Row("Failure", FxText(t[(int)CheckTier.Fail], ev), Red);
                    s.Text(o.Out != null ? $"Critical: {FxText(t[(int)CheckTier.Crit], ev)}. Fumble: {FxText(t[(int)CheckTier.Fumble], ev)}." : $"Critical doubles a success and adds 1 Hype. A fumble is a failure plus: {E.FumbleText}", Dim);
                    s.Button("ev.opt:" + o.Id, "Say it", primary: true);
                    col ^= 1;
                }
                p.Default = "ev.opt:" + r.Opts.Where(o => !o.Auto).Select(o => (o.Id, pass: c.CheckFor(o).Pass)).OrderByDescending(z => z.pass).Select(z => z.Id).DefaultIfEmpty(r.Opts[0].Id).First();
                var walk = c.MoneyFx(E.Walk);
                p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "ev.walk", Label = E.WalkName + (walk.Empty ? "" : " · " + FxText(walk, ev)), Note = "Ends the event here" });
            }
            else RollSection(p, q);
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "drv", Label = "Driver sheet" });
            if (ev.Cur == null) p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "ev.back", Label = "Back to the garage", Note = "The event waits; Go racing waits for it" });
            return p;
        }

        void RollSection(MenuPage p, MenuSection q)
        {
            var c = Career; var ev = c.Event; var cur = ev.Cur; var b = cur.B; var R = cur.R;
            bool last = ev.Ri >= ev.Def.Rounds.Count - 1;
            q.Text(cur.Say, Dim);
            var s = p.Section($"{cur.Name} · {DriverRules.Name(b.Skill)}{(cur.Rerolled ? " · rerolled with Hype" : "")}", 0, null, CheckColor(R.Tier));
            if (b.Mode == RollMode.Flat) s.Row("Your die", $"{R.Kept}   (second die {R.Other}: what advantage would have kept)");
            else s.Row(b.Mode == RollMode.Adv ? "Two dice, keep the higher" : "Two dice, keep the lower", $"{R.A} and {R.B} → {R.Kept}");
            s.Row("d20", R.Kept.ToString(inv));
            foreach (var l in b.L) s.Row("+ " + l.Src, DriverRules.Sgn(l.V), l.V < 0 ? Red : l.K != "attr" && l.V > 0 ? Green : (Rgb?)null);
            s.Row("= Total vs " + DcText(b), $"{R.Total} vs {b.Dc}");
            foreach (var a in b.Adv) s.Text("▲ " + a, Green);
            foreach (var a in b.Dis) s.Text("▼ " + a, Red);
            s.Row(DriverRules.Name(R.Tier) + (R.Kept == 20 ? " · natural 20" : R.Kept == 1 ? " · natural 1" : ""), R.Made ? cur.Hit : R.Tier == CheckTier.Mixed ? cur.Hit + " But it costs you." : cur.Miss + (R.Tier == CheckTier.Fumble ? " " + ev.Def.FumbleText : ""), CheckColor(R.Tier));
            int xp = R.Made ? DriverRules.XpMade : DriverRules.XpMissed;
            s.Row("What changes", FxText(cur.Fx, ev) + $" · +{xp} XP");
            if (c.Driver.Xp + xp >= c.Driver.XpNeed) s.Text($"Level {c.Driver.Lvl + 1}! +1 attribute point on the driver sheet.", Amber);
            if (cur.Info == InfoLevel.Reveal) s.Text("The odds were: " + OddsText(b.Odds), Dim);
            if (cur.R0 != null) s.Text($"First roll: {cur.R0.Kept} ({DriverRules.Name(cur.R0.Tier)})", Dim);
            var h = p.Section(R.Made ? "How it went" : "What would have helped", 1);
            foreach (var line in c.EventHelp()) h.Text(line);
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "ev.next", Label = last ? "Back to the garage" : "Next question", Primary = true });
            p.Default = "ev.next";
            if (c.CanReroll) p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "ev.reroll", Label = "Spend 1 Hype: reroll", Note = $"Hype {c.Driver.Hype} of {DriverRules.HypeMax}" });
        }

        // ------------------------------------------------------------------ the driver sheet

        MenuPage DriverPage()
        {
            var c = Career; var d = c.Driver; var B = d.Background; var im = d.Image;
            var p = new MenuPage
            {
                Screen = CareerScreen.Driver, Eyebrow = "Driver sheet · off track", Title = $"Level {d.Lvl} · {B.Name}", Money = Money(c.Money), Notice = Toast,
                Sub = $"{c.Name} · Proficiency +{DriverRules.Prof} · {(im != null ? DriverRules.Image(im).Name : "Fresh face")} · Hype {d.Hype} of {DriverRules.HypeMax}. {B.Blurb}",
            };
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "drv.back", Label = "Back", Primary = d.Pts == 0 });
            var top = p.Section("Level and standing", 0);
            top.Row("XP", $"{d.Xp} / {d.XpNeed} to level {d.Lvl + 1}");
            top.Text($"+{DriverRules.XpRace} per race, +{DriverRules.XpMade} per check made, +{DriverRules.XpMissed} per check missed. Each level gives 1 attribute point.", Dim);
            if (d.Pts > 0) top.Text($"{d.Pts} point{(d.Pts > 1 ? "s" : "")} to spend below.", Amber);
            top.Row("Hype", $"{d.Hype} of {DriverRules.HypeMax}");
            top.Text("Earn Hype on track with a ×4 combo or a big finish (top 3, or 5+ spots over expected). Spend it off track for advantage or a reroll.", Dim);
            top.Row("Fans", d.Fans.ToString(inv));
            top.Row("Garage respect", d.Respect.ToString(inv));
            top.Row("Team salary", c.Contract.Per > 0 ? Money(c.Contract.Per) + " a race" : "none yet");
            if (c.Contract.Clauses.Count > 0) top.Row("Contract clauses", string.Join(", ", c.Contract.Clauses.Select(Career.ClauseName)));
            if (c.Sponsor != null) top.Row("Sponsor trust", DriverRules.Sgn(c.Sponsor.Trust));
            if (d.NextMom > 0) top.Row("Next race", $"+{d.NextMom} Momentum at the green flag", Green);

            var at = p.Section("Attributes", 0, "Scores 8 to 20. Modifier = (score − 10) ÷ 2, rounded down.");
            foreach (DAttr a in Enum.GetValues(typeof(DAttr)))
            {
                int sc = d.Score(a);
                var skills = string.Join(", ", DriverRules.SkillsOf(a).Select(DriverRules.Name));
                at.Row($"{DriverRules.AttrAbbr[(int)a]} {DriverRules.Name(a)} · {DriverRules.AttrText[(int)a]} · {skills}", $"{sc} ({DriverRules.Sgn(d.ModOf(a))})");
                if (d.Pts > 0) at.Button("drv.up:" + a, $"+1 {DriverRules.AttrAbbr[(int)a]}", sc < DriverRules.AttrMax, AttrPreview(a));
            }

            var sk = p.Section("Skills", 1, $"◆ proficient (+{DriverRules.Prof}) · ★ favored by your image (+1 when you answer in character). Right: chance to make a DC 12 check with one die. Typical DCs: 10 easy, 12 to 13 normal, 15 hard.");
            foreach (DSkill k in Enum.GetValues(typeof(DSkill)))
            {
                int v = d.SkillTotal(k);
                bool fav = im != null && DriverRules.Image(im).Fav.Contains(k);
                var o = DriverRules.Odds(v, 12, RollMode.Flat);
                sk.Row($"{(d.Prof.Contains(k) ? "◆" : "◇")} {DriverRules.Name(k)}{(fav ? " ★" : "")} · {DriverRules.AttrAbbr[(int)DriverRules.AttrOf(k)]}", $"{DriverRules.Sgn(v)} · {DriverRules.Pct(o[0] + o[1])}", v <= 0 ? Red : (Rgb?)null);
            }

            var tf = p.Section("Trait and flaw", 1);
            tf.Row(B.Trait.Name, B.Trait.Text, Amber);
            tf.Row(B.Flaw.Name, B.Flaw.Text, Pink);

            var ims = p.Section("Image", 1, "How the world sees you off track. Your deck persona is separate. Across: Humble (−) to Cocky (+). Up: Professional (−) to Showman (+).");
            ims.Row("Where you are", $"{DriverRules.Sgn(d.ImgX)} across, {DriverRules.Sgn(d.ImgY)} up{(d.Trail.Count > 1 ? $" · came from {string.Join(" → ", d.Trail.Skip(Math.Max(0, d.Trail.Count - 4)).Select(t => $"({t[0]},{t[1]})"))}" : "")}");
            if (im != null)
            {
                var I = DriverRules.Image(im);
                ims.Row(I.Name, I.Text, Rgb.Hex(I.Color));
                ims.Text($"Favored: {string.Join(", ", I.Fav.Select(DriverRules.Name))} (+1 in character). Unlocks a signature answer in the press scrum, sponsor dinner and contract talk{(d.LeansIn ? ", now +1 more because you lean all the way in" : $", +1 more once you lean in to {DriverRules.ImgLean} on both axes")}.");
            }
            else ims.Text($"Fresh face. Every answer has a tone. {DriverRules.ImgAt} steps the same way on both axes sets your image; each image favors two skills and unlocks signature answers.", Dim);
            foreach (var I in DriverRules.Images) ims.Row($"{(I.X < 0 ? "Humble" : "Cocky")} + {(I.Y < 0 ? "Professional" : "Showman")}", I.Name + (I.Id == im ? " (you)" : ""), Rgb.Hex(I.Color));

            var rd = p.Section("On race day", 0, "Two attributes reach the track. Each uses its modifier, so 10 or 11 does nothing.");
            int g = d.ModOf(DAttr.Grit), w = d.ModOf(DAttr.Wits);
            rd.Row($"Grit {d.Score(DAttr.Grit)} ({DriverRules.Sgn(g)})", g != 0 ? $"{DriverRules.Sgn(g * 2)} on every card in the last third of the race" : "no late-race effect");
            rd.Row($"Wits {d.Score(DAttr.Wits)} ({DriverRules.Sgn(w)})", w != 0 ? $"crew calls stay up {DriverRules.Sgn(w)} s" : "no radio effect");

            if (d.Log.Count > 0)
            {
                var lg = p.Section("Recent checks", 0);
                foreach (var l in d.Log) lg.Row($"S{l.Season} R{l.Race} · {l.Ev} · {l.Opt} ({DriverRules.Name(l.Skill)})", $"{l.Txt} · {DriverRules.Name(l.Tier)}", CheckColor(l.Tier));
            }
            if (d.Pts > 0) p.Default = "drv.up:" + Enum.GetValues(typeof(DAttr)).Cast<DAttr>().First(a => d.Score(a) < DriverRules.AttrMax);
            return p;
        }

        string AttrPreview(DAttr a)
        {
            var d = Career.Driver; int s = d.Score(a);
            if (s >= DriverRules.AttrMax) return "Maxed at 20.";
            int m0 = DriverRules.Mod(s), m1 = DriverRules.Mod(s + 1);
            if (m0 == m1) return $"+1 → {s + 1}: modifier stays {DriverRules.Sgn(m0)} (next step at {s + 2}).";
            return $"+1 → {s + 1}: {DriverRules.Sgn(m0)} → {DriverRules.Sgn(m1)} on {string.Join(" and ", DriverRules.SkillsOf(a).Select(DriverRules.Name))}. Each is +5% to pass a check.";
        }

        // ------------------------------------------------------------------ title, reward, season

        void BackgroundSection(MenuPage p)
        {
            string on = PickBg ?? DriverRules.BgFor(PickPersona);
            var s = p.Section("Driver background · off track: press, sponsors, contracts", 1, "Your driver sheet for d20 checks off track. A background sets your six attributes, two proficient skills, a trait and a flaw. It doesn't change your cards or car (but Grit and Wits reach race day)." + (PickBg == null ? " Picked to match your persona until you choose one." : ""));
            foreach (var B in DriverRules.Backgrounds)
                s.Add(new MenuItem
                {
                    Kind = MenuKind.Chip, Id = "title.bg:" + B.Id, Label = B.Name, On = on == B.Id, Value = string.Join(" · ", Enum.GetValues(typeof(DAttr)).Cast<DAttr>().Select(a => DriverRules.AttrAbbr[(int)a] + " " + B.Attr[(int)a])),
                    Note = $"{B.Blurb} Proficient: {string.Join(", ", B.Prof.Select(DriverRules.Name))}. {B.Trait.Name}: {B.Trait.Text} {B.Flaw.Name}: {B.Flaw.Text}",
                });
        }

        void DriverRewardRows(MenuSection pay, RaceReport res)
        {
            if (res.Xp == 0) return;
            pay.Row("Driver", $"+{res.Xp} XP{(res.Hype > 0 ? $" · +{res.Hype} Hype ({string.Join(", ", res.HypeWhy)})" : res.HypeWhy.Count > 0 ? " · Hype full" : "")}{(res.LvlUp > 0 ? $" · level {Career.Driver.Lvl}! A point to spend on the driver sheet" : "")}", res.LvlUp > 0 || res.Hype > 0 ? Amber : (Rgb?)null);
            if (res.DrvMom > 0) pay.Text($"You started with +{res.DrvMom} Momentum from off track.", Dim);
        }

        void NegRows(MenuSection s, ContractOffer o, int i)
        {
            if (o.Neg != null) { s.Row($"Negotiated: d20 {o.Neg.Kept} {DriverRules.Sgn(o.Neg.Mod)} = {o.Neg.Total} vs DC {o.Neg.Dc}", o.Neg.Txt, CheckColor(o.Neg.Tier)); return; }
            var b = Career.NegBuild(o);
            double good = b.Pass, bad = b.Odds[(int)CheckTier.Fail] + b.Odds[(int)CheckTier.Fumble];
            s.Button("neg:" + i.ToString(inv), "Negotiate", note: $"{ModsText(b)} vs {DcText(b)}. Roll needed {NeedText(b)}. Better terms {DriverRules.Pct(good)}, same {DriverRules.Pct(b.Odds[(int)CheckTier.Mixed])}, worse {DriverRules.Pct(bad)}.");
        }
    }
}
