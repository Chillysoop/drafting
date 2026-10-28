using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    // Crew HQ (roadmap step 39, the slice's showCrewHQ) as an engine-free page: the four seats with their level, XP, trait,
    // wage, calls and odds, the three perk tiers (picked for good, so a pick takes two presses), training, hiring and the
    // off-season staff market, the pit deck with upgrades and the pit card dealer, and a rival's offer for one of your crew
    // with its Lead the Crew check shown in full. The garage shows the offer too, and Go racing waits for an answer.
    public sealed partial class CareerMenu
    {
        /// <summary>Where Crew HQ goes back to.</summary>
        CareerScreen crewFrom = CareerScreen.Garage;
        /// <summary>The dealer card waiting for you to pick which pit card it replaces (null = none).</summary>
        public string PitSwap;

        static readonly Dictionary<CrewRole, Rgb> RoleColor = new Dictionary<CrewRole, Rgb>
        {
            [CrewRole.CrewChief] = Rgb.Hex("#e9b52f"), [CrewRole.Spotter] = Rgb.Hex("#6ea8ff"), [CrewRole.Engineer] = Rgb.Hex("#a98bff"), [CrewRole.PitCrew] = Rgb.Hex("#2fb36a"),
        };

        static CrewRole RoleOf(string s) => (CrewRole)Enum.Parse(typeof(CrewRole), s);
        static string RoleLow(CrewRole r) => CrewRules.Name(r).ToLowerInvariant();

        bool DoCrew(string key, string arg, bool again, string id)
        {
            var c = Career;
            switch (key)
            {
                case "crew": if (Screen != CareerScreen.Crew) crewFrom = Screen; PitSwap = null; Screen = CareerScreen.Crew; return true;
                case "crew.back": PitSwap = null; Screen = crewFrom == CareerScreen.Crew ? CareerScreen.Garage : crewFrom; return true;
                case "crew.train":
                {
                    var r = RoleOf(arg);
                    if (!c.Train(r)) return false;
                    Toast = $"{c.Staff[r].Name} trained to level {c.Staff[r].Lvl}.";
                    return true;
                }
                case "crew.perk":
                {
                    var a = arg.Split(':');
                    var r = RoleOf(a[0]); int t = int.Parse(a[1], inv), pick = int.Parse(a[2], inv);
                    var perk = CrewRules.PerkOf(r, t, pick);
                    if (!again) { armed = id; Toast = $"Press {perk.Name} again to lock it in. Perks are for good."; return true; }
                    var why = c.PickPerk(r, t, pick);
                    Toast = why ?? $"{c.Staff[r].Name}: {perk.Name}. {perk.Text}";
                    return why == null;
                }
                case "crew.hire":
                {
                    int i = int.Parse(arg, inv);
                    var h = c.Hires()[i];
                    var was = c.Staff[h.Role];
                    if (!again) { armed = id; Toast = $"Press Hire again: {h.Name} replaces {was.Name} as your {RoleLow(h.Role)}."; return true; }
                    var why = c.HireStaff(i);
                    Toast = why ?? $"{h.Name} is your {RoleLow(h.Role)} now, at level {h.Lvl}. {was.Name} is gone.";
                    return why == null;
                }
                case "crew.pitup":
                {
                    int i = int.Parse(arg, inv);
                    var was = PitCards.Get(c.Base.PitDeck[i]).Name;
                    if (!c.UpgradePit(i)) return false;
                    Toast = $"{was} upgraded to {PitCards.Get(c.Base.PitDeck[i]).Name}.";
                    return true;
                }
                case "crew.pitbuy": PitSwap = arg; return true;
                case "crew.pitcancel": PitSwap = null; return true;
                case "crew.pitswap":
                {
                    int i = int.Parse(arg, inv);
                    var was = PitCards.Get(c.Base.PitDeck[i]).Name;
                    if (PitSwap == null || !c.SwapPit(PitSwap, i)) return false;
                    Toast = $"{PitCards.Get(PitSwap).Name} in, {was} out.";
                    PitSwap = null;
                    return true;
                }
                case "crew.match":
                    if (!c.MatchPoach()) return false;
                    Toast = c.Poach.Outcome;
                    return true;
                case "crew.talk":
                {
                    var n = c.TalkPoach();
                    if (n == null) return false;
                    Toast = $"Lead the Crew: d20 {n.Kept} {DriverRules.Sgn(n.Mod)} = {n.Total} vs DC {n.Dc}. {n.Txt}";
                    return true;
                }
                case "crew.let":
                    if (!again) { armed = id; Toast = $"Press Let them go again: {c.Poach.Name} leaves for {c.Poach.TeamName}."; return true; }
                    if (!c.LetGo()) return false;
                    Toast = c.Poach.Outcome;
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the garage, reward and season pages

        /// <summary>A rival's offer waiting in the garage (Go racing waits for it), or how the last one ended.</summary>
        void PoachPanel(MenuPage p)
        {
            var o = Career.Poach;
            if (o == null) return;
            if (o.Done) { p.Section($"Crew · {o.TeamName}'s offer", 0).Text(o.Outcome, Dim); return; }
            var s = p.Section($"{o.TeamName} wants your {RoleLow(o.Role)}", 0, $"{o.Name} has an offer: {Money(o.Wage)} a race (you pay {(o.Was > 0 ? Money(o.Was) : "nothing")}). Match it, talk them round, or let them go. Go racing waits for your answer.", Amber);
            s.Button("crew", "Answer in Crew HQ", primary: true);
        }

        string CrewButtonLabel()
        {
            var c = Career; int n = c.PerksWaiting;
            return "Crew HQ" + (c.Poach != null && !c.Poach.Done ? " · offer waiting" : n > 0 ? $" · {n} perk{(n > 1 ? "s" : "")} to pick" : "");
        }

        void CrewRewardRows(MenuSection pay, RaceReport res)
        {
            if (res.CrewXp.Count == 0 && res.Wages == 0) return;
            var xs = string.Join(" · ", CrewRules.Roles.Where(r => res.CrewXp.ContainsKey(r)).Select(r => $"{CrewRules.Name(r)} +{res.CrewXp[r]} XP"));
            var ups = string.Join(", ", res.CrewUp.Distinct().Select(r => $"{CrewRules.Name(r)} level {Career.Staff[r].Lvl}"));
            pay.Row("Crew", (res.Calls > 0 ? $"{res.CallsWon} of {res.Calls} calls worked · " : "") + (xs != "" ? xs : "no XP") + (ups != "" ? " · ▲ " + ups : ""), ups != "" ? Amber : (Rgb?)null);
        }

        /// <summary>What happens to your crew if you sign with a team of this rating.</summary>
        string FollowText(int rating)
        {
            var f = Career.Followers(rating);
            int cb = SeasonRules.CrewBase(rating);
            var come = f.Where(x => x.follows).Select(x => $"{RoleLow(x.m.Role)} {x.m.Name}").ToList();
            bool pit = f.Any(x => x.follows && x.m.Role == CrewRole.PitCrew);
            return come.Count == 0 ? $"New crew at level {cb}: perks and pit cards start over."
                : $"Follow{(come.Count == 1 ? "s" : "")} you: {string.Join(", ", come)}. The rest of the crew is the team's, level {cb}{(pit ? "" : "; pit cards start over")}.";
        }

        // ------------------------------------------------------------------ Crew HQ

        MenuPage CrewPage()
        {
            var c = Career; var l = c.Loadout();
            var p = new MenuPage
            {
                Screen = CareerScreen.Crew, Eyebrow = $"Crew HQ · season {c.Season} · before race {c.RaceIdx + 1} of {c.SeasonLength}", Title = "Your crew", Money = Money(c.Money), Notice = Toast,
                Sub = "Crew calls are risky extra plays on the radio, separate from your cards and combos. A better crew offers them more often, lands them more and pays more, and levels 6 and 8 unlock bigger calls. Spending here instead of on driver coaching is a real way to race.",
            };
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "crew.back", Label = "Back", Primary = c.Poach == null || c.Poach.Done });
            var calls = RadioCalls.All.Where(d => d.Kind == RadioKind.Mech && (!d.Pit || !c.Kart)).ToList();
            var open = calls.Where(d => d.Lvl == 0 || l.Level(d.Role) >= d.Lvl).ToList();
            var sum = p.Section("The crew on race day", 0);
            sum.Row("A crew call about every", $"{Math.Round(28 * l.CrewRate()).ToString(inv)} s");
            sum.Row("Average call", $"{Math.Round(open.Average(d => l.CallChance(d, false))).ToString(inv)}% to work");
            sum.Row("Big calls unlocked", $"{calls.Count(d => d.Lvl > 0 && l.Level(d.Role) >= d.Lvl)} of {calls.Count(d => d.Lvl > 0)}");
            sum.Row("Wages", c.Wages > 0 ? Money(c.Wages) + " a race, out of each payout" : "none: volunteers and team staff");

            PoachSection(p);

            int col = 1;
            foreach (var r in CrewRules.Roles) { SeatSection(p, r, l, col); col ^= 1; }
            HireSection(p);
            PitSection(p);

            if (c.Poach != null && !c.Poach.Done) p.Default = c.Poach.Talk == null ? "crew.talk" : "crew.match";
            else
            {
                var perk = p.Pressables().FirstOrDefault(i => i.Id.StartsWith("crew.perk:", StringComparison.Ordinal));
                p.Default = PitSwap != null ? p.Pressables().FirstOrDefault(i => i.Id.StartsWith("crew.pitswap:", StringComparison.Ordinal))?.Id : perk?.Id ?? "crew.back";
            }
            return p;
        }

        void PoachSection(MenuPage p)
        {
            var c = Career; var o = c.Poach;
            if (o == null) return;
            if (o.Done) { p.Section($"{o.TeamName}'s offer", 0).Text(o.Outcome, Dim); return; }
            var m = c.Staff[o.Role];
            var s = p.Section($"{o.TeamName} wants {o.Name}, your {RoleLow(o.Role)}", 0, $"A rating {o.Rating} team, and a level {m.Lvl} {RoleLow(o.Role)} is worth it to them. Answer before you go racing.", Amber);
            s.Row("Their offer", $"{Money(o.Wage)} a race (you pay {(o.Was > 0 ? Money(o.Was) : "nothing, they volunteer")})");
            s.Button("crew.match", $"Match it · {Money(o.MatchAt)} a race from now on", note: o.MatchAt > o.Wage ? "10% over their offer: the talk went badly." : "They stay for good: no more offers this season, and they follow you to a new team.");
            if (o.Talk == null)
            {
                var b = c.TalkBuild();
                var t = p.Section("Talk them round · Lead the Crew", 0, "One try. Every number is shown before you roll.");
                CheckRows(t, b, c.Info == InfoLevel.Full);
                for (int i = 0; i < 5; i++) t.Row(DriverRules.Name((CheckTier)i), CrewRules.TalkFx[i].txt, CheckColor((CheckTier)i));
                t.Button("crew.talk", "Talk to them", primary: true);
            }
            else s.Row($"You talked: d20 {o.Talk.Kept} {DriverRules.Sgn(o.Talk.Mod)} = {o.Talk.Total} vs DC {o.Talk.Dc}", o.Talk.Txt, CheckColor(o.Talk.Tier));
            s.Button("crew.let", armed == "crew.let" ? "Press again to let them go" : $"Let them go · {o.TeamName} pays you {Money(o.Buyout)}", note: $"Their assistant steps up at level {Math.Max(2, m.Lvl - 2)}, with perks picked fresh.");
        }

        void SeatSection(MenuPage p, CrewRole r, PlayerLoadout l, int col)
        {
            var c = Career; var m = c.Staff[r];
            var tr = m.Trait != null ? CrewHq.Traits[m.Trait] : null;
            var s = p.Section($"{CrewRules.Name(r)} · {m.Name} · level {m.Lvl}", col, CrewRules.Text(r), RoleColor[r]);
            s.Row("Wage", m.Wage > 0 ? Money(m.Wage) + " a race" : "Volunteer · no wage");
            if (tr != null) s.Row("Trait: " + tr.Name, tr.Text, Amber);
            s.Row("With you since", $"season {m.Since}{(m.Kept > 0 ? $" · kept from a rival in season {m.Kept}" : "")}");
            if (m.Lvl < CrewRules.Max) s.Row("XP", $"{m.Xp} / {CrewRules.XpNeed(m.Lvl)} to level {m.Lvl + 1} · " + (r == CrewRole.PitCrew ? "earned at every stop, more for a Perfect Stop" : "earned on every call of theirs you take, more when it works"));
            else s.Row("XP", "Maxed out");
            if (r == CrewRole.PitCrew) s.Row("Release window", $"{(0.55 + 0.07 * l.PitSkill + l.Crew.ReleaseWindow).ToString("0.00", inv)} s");
            else
                foreach (var d in RadioCalls.All.Where(d => d.Kind == RadioKind.Mech && d.Role == r && (!d.Pit || !c.Kart)))
                    s.Row((d.Lvl > 0 ? (l.Level(r) >= d.Lvl ? "★ " : "🔒 ") : "") + d.Name, d.Lvl > 0 && l.Level(r) < d.Lvl ? $"level {d.Lvl}" : $"{l.CallChance(d, false)}%", d.Lvl > 0 && l.Level(r) < d.Lvl ? Dim : (Rgb?)null);
            for (int t = 0; t < 3; t++)
            {
                int need = CrewRules.PerkLevel(r, t);
                for (int pick = 1; pick <= 2; pick++)
                {
                    var perk = CrewRules.PerkOf(r, t, pick);
                    string id = $"crew.perk:{r}:{t.ToString(inv)}:{pick.ToString(inv)}";
                    bool on = m.Perks[t] == pick, ready = m.Perks[t] == 0 && m.Lvl >= need;
                    s.Add(new MenuItem
                    {
                        Kind = MenuKind.Chip, Id = id, Label = $"Lv {need} · {(on ? "✓ " : "")}{perk.Name}" + (armed == id ? " · press again" : ""), On = on, Enabled = ready,
                        Note = perk.Text + (m.Lvl < need ? $" Unlocks at level {need}." : m.Perks[t] != 0 && !on ? " Not picked." : ""),
                    });
                }
            }
            var why = c.CantTrain(r);
            s.Button("crew.train:" + r, m.Lvl >= CrewRules.Max ? "Level 10" : $"Train to level {m.Lvl + 1} · {Money(c.TrainCost(r))}", why == null);
        }

        void HireSection(MenuPage p)
        {
            var c = Career; var h = c.Hires();
            var s = p.Section(c.Market ? "Off-season staff market" : "Hire", 0, c.Market
                ? "Contracts are up around the garage: more people, up to 3 levels better than yours, and a quarter off their fees until your first race. A hire replaces the person in that seat, and you pick their perks fresh."
                : "A hire replaces the person in that seat. They arrive at their level with a trait and a wage, and you pick their perks fresh. New faces every race.");
            if (h.Count == 0) { s.Text("Nobody else is looking right now.", Dim); return; }
            for (int i = 0; i < h.Count; i++)
            {
                var x = h[i]; var tr = CrewHq.Traits[x.Trait];
                string id = "crew.hire:" + i.ToString(inv);
                s.Add(new MenuItem
                {
                    Kind = MenuKind.Part, Id = id, Label = $"{CrewRules.Name(x.Role)}: {x.Name}", Value = armed == id ? "Press again to hire" : $"level {x.Lvl} (yours {c.Staff[x.Role].Lvl}) · hire {Money(x.Fee)}",
                    Note = $"{tr.Name}: {tr.Text} {Money(x.Wage)} a race. Replaces {c.Staff[x.Role].Name}.", Enabled = c.Money >= x.Fee && (c.Poach == null || c.Poach.Done || c.Poach.Role != x.Role), Color = RoleColor[x.Role],
                });
            }
        }

        static string PitCardText(PitCardDef d) => d.Rolls ? $"{d.Base}% · {d.Text} · {d.Miss}" : d.IsFix ? $"+{d.FixTime} s · fixes {d.Fix}" : d.Setup != null ? PitCards.Setups[d.Setup].Text(d.Up ? 2 : 1) : d.Text;

        void PitSection(MenuPage p)
        {
            var c = Career; var deck = c.Base.PitDeck;
            var s = p.Section($"Pit deck · {deck.Count} cards", 1, $"You draw from these at every stop. An upgrade (+) adds 10% to the odds and makes the gain bigger, or installs a Setup straight at Level II: {Money(CrewRules.PitUpgrade)}.");
            if (c.Kart) s.Text("Karts don't pit. Your pit deck waits for late models.", Dim);
            if (PitSwap != null)
            {
                s.Text($"Pick the card {PitCards.Get(PitSwap).Name} replaces ({Money(CrewRules.PitSwap)}).", Amber);
                s.Button("crew.pitcancel", "Cancel");
            }
            for (int i = 0; i < deck.Count; i++)
            {
                var d = PitCards.Get(deck[i]);
                string n = i.ToString(inv);
                if (PitSwap != null) { s.Add(new MenuItem { Kind = MenuKind.Part, Id = "crew.pitswap:" + n, Label = d.Name, Value = "replace", Note = PitCardText(d), Enabled = c.Money >= CrewRules.PitSwap }); continue; }
                var why = c.CantUpgradePit(i);
                s.Add(new MenuItem { Kind = MenuKind.Part, Id = why == null || why == "Not enough money" ? "crew.pitup:" + n : null, Label = d.Name, Value = d.Up ? "upgraded" : why == "Can't be upgraded" ? "" : "upgrade · " + Money(CrewRules.PitUpgrade), Note = PitCardText(d), Enabled = why == null, Color = d.Up ? Green : (Rgb?)null });
            }
            var dl = p.Section("Pit card dealer", 1, $"Swap one into your pit deck for {Money(CrewRules.PitSwap)}.");
            foreach (var id in PitCards.Shop)
            {
                var d = PitCards.Get(id);
                dl.Add(new MenuItem { Kind = MenuKind.Part, Id = "crew.pitbuy:" + id, Label = d.Name, Value = PitSwap == id ? "pick a card to replace" : "swap in · " + Money(CrewRules.PitSwap), Note = PitCardText(d), Enabled = c.Money >= CrewRules.PitSwap, On = PitSwap == id });
            }
        }
    }
}
