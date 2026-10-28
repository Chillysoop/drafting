using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GreenFlag.Sim;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Headless races on the C# sim.
    ///   race   [track] [seed] [policy] [persona]     one race: grid, event log, finish order, fingerprint
    ///   batch  [n] [tracks] [policy] [persona]       n races per track: average finish, cautions, wrecks, DNFs, length (+ card stats)
    ///   check  [n]                                   every track, n seeds, each policy run twice: fails if any run differs (CI)
    ///   parity [baseline] [n] [styles]               the browser slice's averages vs C# per persona, track and play style (CI)
    ///   json   [n] [personas] [tracks] [styles] [out] [seed]   the browser harness's JSON, for comparing anything else
    ///   frames [track] [seed] [out.html] [n] [style] n top-down snapshots of the race view (step 29) and its pylon; style = a card bot
    ///   wrecks [track] [seed] [out.html] [style] [n] close-ups of the first n wrecks as the view plays them out (step 30)
    ///   career [races] [persona] [seed]              a career through the menus (step 34): MenuBot shops and picks rewards, the mouse bot races
    ///   menus  [persona] [seed] [out.html]           the career screens as HTML, laid out as MenuHud draws them (step 34)
    /// Policies without cards (the step 26 core): none (hands-off: never presses PIT, takes the crew chief's caution call)
    /// and pit (presses PIT on the plan lap and calls cautions by tires and damage, like the browser harness's bots).
    /// Policies with cards (a persona's starting deck, part, tune and driver; default drafter, its kart cousin on kart tracks):
    /// hands (holds a hand but never plays; the crew chief runs the stops) and the browser harness's play styles smart, spam,
    /// human, human2, bold and hspam (see Bots).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string cmd = args.Length > 0 ? args[0] : "check";
            try
            {
                switch (cmd)
                {
                    case "race": return RaceCmd(Arg(args, 1, "carver"), uint.Parse(Arg(args, 2, "61")), Arg(args, 3, "none"), Arg(args, 4, "drafter"));
                    case "batch": return Batch(int.Parse(Arg(args, 1, "100")), Arg(args, 2, "all"), Arg(args, 3, "none"), Arg(args, 4, "drafter"));
                    case "check": return Check(int.Parse(Arg(args, 1, "5")));
                    case "parity": return Parity.Run(Arg(args, 1, "sim/parity/browser-baseline.json"), int.Parse(Arg(args, 2, "60")), Arg(args, 3, "all"));
                    case "frames": return ViewFrames.Run(Arg(args, 1, "carver"), uint.Parse(Arg(args, 2, "61")), Arg(args, 3, "frames.html"), int.Parse(Arg(args, 4, "6")), Arg(args, 5, "none"));
                    case "wrecks": return ViewFrames.Wrecks(Arg(args, 1, "carver"), uint.Parse(Arg(args, 2, "61")), Arg(args, 3, "wrecks.html"), Arg(args, 4, "spam"), int.Parse(Arg(args, 5, "3")));
                    case "mouse": return HudShots.Mouse(Arg(args, 1, "all"), int.Parse(Arg(args, 2, "1")), Arg(args, 3, "drafter"), uint.Parse(Arg(args, 4, "61")));
                    case "pad": return HudShots.Pad(Arg(args, 1, "all"), int.Parse(Arg(args, 2, "1")), Arg(args, 3, "drafter"), uint.Parse(Arg(args, 4, "61")), Arg(args, 5, "xbox"));
                    case "career": return CareerCmd(int.Parse(Arg(args, 1, "10")), Arg(args, 2, "drafter"), uint.Parse(Arg(args, 3, "1")));
                    case "menus": return MenuShots.Run(Arg(args, 1, "drafter"), uint.Parse(Arg(args, 2, "1")), Arg(args, 3, "menus.html"));
                    case "hud": return HudShots.Run(Arg(args, 1, "carver"), uint.Parse(Arg(args, 2, "61")), Arg(args, 3, "hud.html"), int.Parse(Arg(args, 4, "6")));
                    case "json": return Json(int.Parse(Arg(args, 1, "40")), Arg(args, 2, "drafter"), Arg(args, 3, "0,1,2"), Arg(args, 4, "none,smart,spam"), Arg(args, 5, "parity.json"), uint.Parse(Arg(args, 6, "1000")));
                    default: Console.Error.WriteLine("usage: race [track] [seed] [policy] [persona] | batch [n] [tracks] [policy] [persona] | check [n] | parity [baseline] [n] [styles] | json [n] [personas] [tracks] [styles] [out] [seed] | frames [track] [seed] [out.html] [n] [style] | wrecks [track] [seed] [out.html] [style] [n] | mouse [tracks] [n] [persona] [seed] | pad [tracks] [n] [persona] [seed] [xbox|playstation|nintendo|keyboard] | hud [track] [seed] [out.html] [n] | career [races] [persona] [seed] | menus [persona] [seed] [out.html]"); return 2;
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }

        static string Arg(string[] a, int i, string d) => a.Length > i ? a[i] : d;

        static IEnumerable<TrackDef> TracksFor(string spec) => spec == "all" ? Tracks.All : spec.Split(',').Select(Tracks.ById);

        /// <summary>Step 34: race, rewards, shop and the next race, through the career menus' buttons, with every race played by
        /// the mouse bot on the HUD. Fails if a race doesn't finish or the loop stalls.</summary>
        static int CareerCmd(int races, string persona, uint seed)
        {
            var m = new GreenFlag.View.CareerMenu { PickPersona = persona, NewSeed = seed };
            var bot = new GreenFlag.View.MenuBot();
            Console.WriteLine("race track    fin exp start tier          purse bonus total    bank  plays  then: card pick, part pick, cards owned");
            int bad = 0, shown = 0;
            void Show()
            {
                var c = m.Career;
                for (; shown < c.Results.Count; shown++)
                {
                    var r = c.Results[shown];
                    Console.WriteLine($"{shown + 1,4} {r.TrackId,-7} {(r.Dnf ? "DNF" : "P" + r.Finish),4} {r.Expected,3} {r.Start,5} {CareerRules.TierName(r.Tier),-13} {r.Purse,5} {r.Bonus,5} {r.Total,5} {c.Money,7}  {r.Plays,5}");
                }
            }
            for (int done = 0; done < races; done++)
            {
                for (int i = 0; i < 1000 && m.RaceWanted == null && bot.Step(m); i++) { }
                if (m.RaceWanted == null) { Console.Error.WriteLine("the menus stalled on " + m.Screen); return 1; }
                var (desk, _) = GreenFlag.View.MousePlayer.Play(new Race(m.TakeRace()));
                if (!desk.Race.Done) bad++;
                var res = m.RaceFinished(desk.Race);
                var c = m.Career; var R = c.Reward;
                int bought = bot.Bought;
                for (int i = 0; i < 1000 && m.Screen == GreenFlag.View.CareerScreen.Reward && bot.Step(m); i++) { }
                Show();
                Console.WriteLine($"     {"",-62} {(R.Picked == "skip" ? "skip +$1,000" : CardBook.Get(R.Picked).Name),-21} {(R.PartPicked != null ? Parts.Get(R.PartPicked).Name : "-"),-17} {c.Collection.Count,3} cards owned");
            }
            var k = m.Career;
            Console.WriteLine($"after {k.Results.Count} races: {k.Money:N0} in the bank, {k.Collection.Count} cards, parts {string.Join(", ", k.PartsOwned)}, upgrades bought {bot.Bought}, dealer parts {bot.PartsBought}, menu presses {bot.Presses}");
            Console.WriteLine($"car: power {k.Car.Power} handling {k.Car.Handling} aero {k.Car.Aero} durability {k.Car.Durability} racecraft {k.Car.Racecraft} defense {k.Car.Defense} consistency {k.Car.Consistency} tires {k.Car.TireMgmt}");
            Console.WriteLine($"average finish P{k.Results.Average(r => r.Finish):F1} vs expected P{k.Results.Average(r => r.Expected):F1}; tiers {string.Join(", ", k.Results.GroupBy(r => r.Tier).OrderBy(g => g.Key).Select(g => CareerRules.TierName(g.Key) + " " + g.Count()))}");
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Card play styles: hands (holds a hand, never plays; the browser harness's "none") and the browser harness's bots (Bots.Styles).</summary>
        public static bool UsesCards(string policy) => policy == "hands" || (policy != "none" && Bots.IsStyle(policy));

        /// <summary>The persona for a track: stock personas move to their kart cousin on kart tracks (and back).</summary>
        public static string PersonaFor(TrackDef t, string persona)
        {
            var kart = new Dictionary<string, string> { ["drafter"] = "packrat", ["bully"] = "brawler", ["whisperer"] = "linepainter", ["restart"] = "latebraker", ["gambler"] = "latebraker" };
            var p = Personas.Get(persona);
            if (t.Kart && !p.Kart) return kart[persona];
            if (!t.Kart && p.Kart) return kart.First(kv => kv.Value == persona).Key;
            return persona;
        }

        /// <summary>Run one race with a policy driving your car's pit calls (and cards).</summary>
        public static (Race race, RaceResult res) Run(TrackDef t, uint seed, string policy, bool log = false, string persona = "drafter")
        {
            if (UsesCards(policy))
            {
                var (cr, _) = Bots.Run(t, seed, policy == "hands" ? "none" : policy, persona, x => { if (log) x.Log = new List<RaceEvent>(); });
                return (cr, cr.Result());
            }
            var race = new Race(RaceSetup.Quick(t, seed));
            if (log) race.Log = new List<RaceEvent>();
            bool pit = policy == "pit";
            int cautionSet = -1;
            while (!race.Done)
            {
                race.Tick();
                if (!pit) continue;
                var me = race.Me;
                if (me.Dnf) continue;
                if (race.Phase == RacePhase.Green && me.Pit == null && race.PitRequest == null)
                {
                    var pl = race.MyPlan;
                    if (pl != null && race.LapOf(me) >= pl.Value.Lap) race.PressPit();
                }
                var k = race.Caution;
                if (race.Phase == RacePhase.Caution && k != null && !k.Done && cautionSet != k.Id)
                {
                    cautionSet = k.Id;
                    race.SetCautionCall(me.Tires >= 60 && me.Damage < 25 ? PitService.Stay : me.Tires >= 40 ? PitService.Two : PitService.Four);
                }
            }
            return (race, race.Result());
        }

        static int RaceCmd(string track, uint seed, string policy, string persona)
        {
            var t = Tracks.ById(track);
            var (race, res) = Run(t, seed, policy, true, persona);
            Console.WriteLine($"{t.Name} ({t.Id}), {t.Laps} laps, seed {seed}, policy {policy}{(race.CardsOn ? ", persona " + PersonaFor(t, persona) : "")}");
            Console.WriteLine("Grid: " + string.Join(" ", race.Cars.Select(c => (c.IsMe ? "*" : "") + c.Ref.Number)));
            foreach (var e in race.Log) if (e.Kind != RaceEventKind.Pass || e.Car == race.Me.Id || e.Other == race.Me.Id) Console.WriteLine(e);
            if (race.CardsOn)
            {
                var s = race.CardStats;
                Console.WriteLine($"\nCards: {s.Plays} played, {s.Succ} landed, {s.Crits} wrecks, best combo ×{s.BestChain}, {s.Combos} combo links, {s.Tossed} tossed, {s.Expired} expired, board {s.BoardFires}, beats {race.Beats}, reels {s.Reels}");
                if (s.PitStops > 0) Console.WriteLine($"Pit stall: {s.PitStops} stops ({string.Join(", ", s.StopTimes.Select(x => x.ToString("F1")))} s), {s.Perfect} perfect, {s.Setups} setups, {s.Packages} packages");
            }
            Console.WriteLine();
            foreach (var r in res.Order)
            {
                var c = race.Cars.First(x => x.Id == r.Id);
                Console.WriteLine($"P{r.Finish,-3} #{c.Ref.Number,-3}{(r.IsMe ? "*" : " ")}{c.Ref.Name,-18} start P{r.Start,-3} passes {r.Passes,-3} stops {r.PitStops} tires {r.Tires,5:F1} dmg {r.Damage,5:F1}{(r.Dnf ? "  DNF" : "")}");
            }
            Console.WriteLine($"\nYou: start P{res.MyStart}, finish P{res.MyFinish}, expected P{res.MyExpected}{(res.MyDnf ? ", DNF" : "")}. Cautions {res.Cautions}, wrecks {res.Wrecks}, {res.Duration:F1} s.");
            Console.WriteLine("Fingerprint " + race.Fingerprint());
            return 0;
        }

        static int Batch(int n, string tracks, string policy, string persona)
        {
            bool cards = UsesCards(policy);
            Console.WriteLine($"{n} races per track, policy {policy}{(cards ? ", persona " + persona : "")}, Normal");
            Console.WriteLine("track    avg fin  vs exp  start  cautions  wrecks  field DNF  my DNF  length s" + (cards ? "  plays  landed  crits  combos  best  perfect" : ""));
            foreach (var t in TracksFor(tracks))
            {
                double fin = 0, exp = 0, st = 0, cau = 0, wr = 0, dnf = 0, my = 0, dur = 0, pl = 0, su = 0, cr = 0, co = 0, be = 0, pf = 0, stops = 0;
                for (int i = 0; i < n; i++)
                {
                    var (race, r) = Run(t, (uint)(1000 + i), policy, false, persona);
                    fin += r.MyFinish; exp += r.MyExpected; st += r.MyStart; cau += r.Cautions; wr += r.Wrecks; dnf += r.Dnfs; my += r.MyDnf ? 1 : 0; dur += r.Duration;
                    if (cards) { var s = race.CardStats; pl += s.Plays; su += s.Succ; cr += s.Crits; co += s.Combos; be += s.BestChain; pf += s.Perfect; stops += s.PitStops; }
                }
                Console.WriteLine($"{t.Id,-8} {fin / n,7:F1} {(exp - fin) / n,7:F1} {st / n,6:F1} {cau / n,9:F2} {wr / n,7:F2} {dnf / n,10:F2} {my / n * 100,6:F0}% {dur / n,9:F0}"
                    + (cards ? $"  {pl / n,5:F1}  {(pl > 0 ? su / pl * 100 : 0),5:F0}%  {cr / n,5:F2}  {co / n,6:F1}  {be / n,4:F1}  {(stops > 0 ? pf / stops * 100 : 0),6:F0}%" : ""));
            }
            return 0;
        }

        /// <summary>The browser harness's JSON: {"persona|trackIndex|style": [RaceRow, ...]}, n races each, seeds seedBase + i.</summary>
        static int Json(int n, string personas, string tracks, string styles, string outPath, uint seedBase)
        {
            var all = new Dictionary<string, List<RaceRow>>();
            foreach (var a in personas.Split(','))
                foreach (var ti in tracks.Split(','))
                    foreach (var st in styles.Split(','))
                    {
                        var rows = new List<RaceRow>();
                        for (int i = 0; i < n; i++) rows.Add(Bots.Run(Tracks.All[int.Parse(ti)], seedBase + (uint)i, st, a).row);
                        all[$"{a}|{ti}|{st}"] = rows;
                    }
            System.IO.File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(all, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }));
            Console.WriteLine($"wrote {outPath}: {all.Count} cells x {n} races");
            return 0;
        }

        static int Check(int n)
        {
            int bad = 0;
            var sb = new StringBuilder();
            foreach (var t in Tracks.All)
                foreach (var policy in new[] { "none", "pit", "hands", "smart" })
                    for (int i = 0; i < n; i++)
                    {
                        uint seed = (uint)(61 + i * 7919);
                        var (a, ra) = Run(t, seed, policy);
                        var (b, rb) = Run(t, seed, policy);
                        string fa = a.Fingerprint(), fb = b.Fingerprint();
                        bool same = fa == fb && ra.Order.Select(x => x.Id).SequenceEqual(rb.Order.Select(x => x.Id));
                        if (!same) { bad++; Console.WriteLine($"MISMATCH {t.Id} seed {seed} {policy}: {fa} vs {fb}"); }
                        sb.Append($"{t.Id,-7} seed {seed,-6} {policy,-5} P{ra.MyFinish,-3} cautions {ra.Cautions} wrecks {ra.Wrecks} {fa}\n");
                    }
            Console.Write(sb);
            Console.WriteLine(bad == 0 ? $"OK: {Tracks.All.Count * 4 * n} races, each run twice, identical." : $"FAILED: {bad} races differed between runs.");
            return bad == 0 ? 0 : 1;
        }
    }
}
