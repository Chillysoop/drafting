using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 42's "done when": each kind of premier stock car track has a different best deck in the harness. Four parts, all
    /// seeded, so a run is repeatable.
    ///   Races: every stock car track x the five stock personas x n seeds, played by the browser harness's bots: smart (careful
    ///   play), none (hands-off) and spam, so the track rules don't throw the series off its targets.
    ///   Decks: four race decks, each built for one kind of track with its own setup (Packs), raced by the smart bot on every
    ///   stock car track with the same rookie car and no part. The best deck on each kind of track must be the one built for it.
    ///   Setups: one all-round deck with each of the four setups, to show the setup matters on its own.
    ///   Seasons: whole premier seasons through the career menus (MenuBot in the garage, the smart bot in the races).
    /// Prints tables and a PASS/FAIL line per target; exits 1 if any target fails (CI). off = the browser slice's stock races
    /// (no track rules), for comparing; the targets that need the rules are skipped.
    /// </summary>
    public static class StockSeason
    {
        /// <summary>A race deck (the stock car base deck plus six cards) and the setup it is built around.</summary>
        public sealed class Pack
        {
            public string Id, Name;
            public TrackKind For;
            public string[] Add;
            public Tune Tune;

            public List<string> Deck() { var d = new List<string>(CardBook.StockBase); d.AddRange(Add); return d; }

            public PlayerLoadout Loadout(Tune tune = null)
            {
                var l = new PlayerLoadout();
                l.Deck.AddRange(Deck());
                l.Tune = (tune ?? Tune).Clone();
                return l;
            }
        }

        static Tune T(int h, int a, ComboFocus f) => new Tune { Hand = h, Aero = a, Focus = f };

        public static readonly Pack[] Packs =
        {
            new Pack { Id = "draft", Name = "Draft pack", For = TrackKind.Superspeedway, Add = new[] { "bump_draft", "slingshot", "draft_train", "tuck_in", "side_draft", "push_limit" }, Tune = T(0, 2, ComboFocus.Draft) },
            new Pack { Id = "longrun", Name = "Long run", For = TrackKind.Intermediate, Add = new[] { "save_tires", "save_tires", "high_line", "high_line", "hot_laps", "clear_air" }, Tune = T(-1, -1, ComboFocus.LongRun) },
            new Pack { Id = "bumper", Name = "Bumper", For = TrackKind.ShortTrack, Add = new[] { "bump_run", "bump_run", "bump_run", "block", "block", "high_line" }, Tune = T(0, -2, ComboFocus.Bumper) },
            new Pack { Id = "braking", Name = "Braking zones", For = TrackKind.RoadCourse, Add = new[] { "dive_bomb", "dive_bomb", "crossover", "crossover", "push_limit", "block" }, Tune = T(1, -2, ComboFocus.Bumper) },
        };

        /// <summary>The setup check's deck: one of everything.</summary>
        public static readonly string[] AllRound = { "bump_draft", "slingshot", "save_tires", "high_line", "bump_run", "block" };

        public static readonly TrackKind[] Kinds = { TrackKind.Superspeedway, TrackKind.Intermediate, TrackKind.ShortTrack, TrackKind.RoadCourse };

        sealed class Cell { public int N, Dnf, Plays; public double VsExp; public readonly Dictionary<string, int[]> ByCard = new Dictionary<string, int[]>(); }

        /// <summary>STOCK_V=1 prints each deck's plays per race and landing rate per card.</summary>
        static readonly bool Verbose = Environment.GetEnvironmentVariable("STOCK_V") == "1";

        /// <summary>STOCK_PARTS=decks,setups (say) runs only those parts; STOCK_TRACKS=mill,iron only those tracks. For tuning.</summary>
        static bool Part(string p) { var e = Environment.GetEnvironmentVariable("STOCK_PARTS"); return string.IsNullOrEmpty(e) || e.Split(',').Contains(p); }
        static bool Only(TrackDef t) { var e = Environment.GetEnvironmentVariable("STOCK_TRACKS"); return string.IsNullOrEmpty(e) || e.Split(',').Contains(t.Id); }

        sealed class Target { public string Name, Want; public double Got; public bool Ok; }

        static Cell Race(TrackDef t, uint seed0, int n, bool rulesOn, Func<uint, (Race r, string style)> make)
        {
            var c = new Cell();
            for (int i = 0; i < n; i++)
            {
                var (r, st) = make(seed0 + (uint)i);
                r.Bal.StockRules = rulesOn;
                Bots.Drive(r, st);
                var res = r.Result();
                c.N++; c.VsExp += res.MyExpected - res.MyFinish; c.Plays += r.CardStats.Plays;
                if (res.MyDnf) c.Dnf++;
                foreach (var kv in r.CardStats.ByCard)
                {
                    if (!c.ByCard.TryGetValue(kv.Key, out var h)) c.ByCard[kv.Key] = h = new int[6];
                    for (int q = 0; q < 6; q++) h[q] += kv.Value[q];
                }
            }
            if (Verbose) Console.WriteLine($"    {t.Id}: " + string.Join(", ", c.ByCard.OrderByDescending(kv => kv.Value[5]).Select(kv => $"{kv.Key} {(double)kv.Value[5] / n:F1} ({100.0 * (kv.Value[(int)Tier.Success] + kv.Value[(int)Tier.CritSuccess]) / Math.Max(1, kv.Value[5]):F0}%)")));
            return c;
        }

        static Race PackRace(TrackDef t, uint seed, PlayerLoadout lo)
        {
            var rs = RaceSetup.Quick(t, seed, lo);
            rs.PitPolicy = PitPolicy.Bot;
            return new Race(rs);
        }

        public static int Run(int n, int seasons, uint seed0, bool rulesOn = true)
        {
            // the premier calendar's tracks (Pinewood is a stock car track for the test track only, board Q90)
            var stock = CareerRules.Calendar(Series.Stock).Distinct().Select(Tracks.ById).ToList();
            var targets = new List<Target>();

            // ---------------------------------------------------------------- races
            var styles = new[] { "smart", "none", "spam" };
            var cells = new Dictionary<(string t, string st), Cell>();
            foreach (var t in stock.Where(_ => Part("races")))
                foreach (var st in styles)
                {
                    var a = new Cell();
                    foreach (var p in LateSeason.Personas)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            var (r, _) = Bots.Run(t, seed0 + (uint)i, st, p, x => x.Bal.StockRules = rulesOn);
                            var res = r.Result();
                            a.N++; a.VsExp += res.MyExpected - res.MyFinish; a.Plays += r.CardStats.Plays;
                            if (res.MyDnf) a.Dnf++;
                        }
                    }
                    cells[(t.Id, st)] = a;
                }
            Console.WriteLine($"Premier stock car races: {n} seeds x {LateSeason.Personas.Length} personas per track and style (seeds {seed0}+), Normal, track rules {(rulesOn ? "on" : "off (slice)")}");
            Console.WriteLine("track    kind           style  vs exp   DNF  plays");
            foreach (var kv in cells)
            {
                var c = kv.Value;
                Console.WriteLine($"{kv.Key.t,-8} {TrackDef.KindName(Tracks.ById(kv.Key.t).Kind),-14} {kv.Key.st,-5} {c.VsExp / c.N,7:+0.0;-0.0}  {100.0 * c.Dnf / c.N,3:F0}%  {(double)c.Plays / c.N,5:F1}");
            }
            Cell All(string st)
            {
                var a = new Cell();
                foreach (var kv in cells.Where(k => k.Key.st == st)) { a.N += kv.Value.N; a.VsExp += kv.Value.VsExp; a.Dnf += kv.Value.Dnf; a.Plays += kv.Value.Plays; }
                return a;
            }
            if (Part("races")) targets.Add(T("careful play vs expected, all stock tracks (slice rules +6.6)", "+5 to +14", All("smart").VsExp / All("smart").N, 6, 14));
            if (Part("races")) targets.Add(T("hands-off vs expected", "-4 to +1", All("none").VsExp / All("none").N, -4, 1));
            if (Part("races")) targets.Add(T("card spam DNF % (slice rules 62%)", "40% to 85%", 100.0 * All("spam").Dnf / All("spam").N, 40, 85));
            foreach (var t in stock.Where(_ => Part("races")))
            {
                var c = cells[(t.Id, "smart")];
                targets.Add(T($"careful play vs expected at {t.Id}", "+2 to +16", c.VsExp / c.N, 2, 16));
            }

            // ---------------------------------------------------------------- decks
            int nd = n * 4;
            Console.WriteLine();
            Console.WriteLine($"Race decks: each deck with its own setup, smart bot, rookie car, no part, {nd} seeds per track (seeds {seed0}+)");
            foreach (var p in Packs) Console.WriteLine($"  {p.Id,-8} {p.Name} for the {TrackDef.KindName(p.For).ToLowerInvariant()}: base + {string.Join(", ", p.Add)}; setup {TuneText(p.Tune)}");
            Console.WriteLine("track    kind           " + string.Join(" ", Packs.Select(p => $"{p.Id,8}")) + "   best");
            var deck = new Dictionary<(string t, string p), Cell>();
            foreach (var t in stock.Where(x => Part("decks") && Only(x)))
            {
                foreach (var p in Packs) deck[(t.Id, p.Id)] = Race(t, seed0, nd, rulesOn, s => (PackRace(t, s, p.Loadout()), "smart"));
                Console.WriteLine($"{t.Id,-8} {TrackDef.KindName(t.Kind),-14} " + string.Join(" ", Packs.Select(p => $"{deck[(t.Id, p.Id)].VsExp / nd,8:+0.0;-0.0}")) + "   " + Best(Packs.Select(p => p.Id), id => deck[(t.Id, id)].VsExp));
            }
            var bestBy = new Dictionary<TrackKind, string>();
            foreach (var k in Kinds.Where(k => Part("decks") && stock.Any(t => t.Kind == k && Only(t))))
            {
                var ts = stock.Where(t => t.Kind == k && Only(t)).ToList();
                double Avg(string id) => ts.Sum(t => deck[(t.Id, id)].VsExp) / (ts.Count * nd);
                var order = Packs.OrderByDescending(p => Avg(p.Id)).ToList();
                bestBy[k] = order[0].Id;
                Console.WriteLine($"{TrackDef.KindName(k),-23}" + string.Join(" ", Packs.Select(p => $"{Avg(p.Id),8:+0.0;-0.0}")) + $"   {order[0].Id} by {Avg(order[0].Id) - Avg(order[1].Id):F1}");
                if (!rulesOn) continue;
                var own = Packs.First(p => p.For == k);
                targets.Add(new Target { Name = $"{TrackDef.KindName(k).ToLowerInvariant()}: the best deck is the {own.Name.ToLowerInvariant()} deck built for it (best: {order[0].Id})", Want = own.Id, Got = double.NaN, Ok = order[0] == own });
                targets.Add(T($"{TrackDef.KindName(k).ToLowerInvariant()}: the {own.Name.ToLowerInvariant()} deck beats the next best by (places)", "0.8 or more", Avg(own.Id) - Packs.Where(p => p != own).Max(p => Avg(p.Id)), 0.8, 99));
            }
            if (rulesOn && bestBy.Count == Kinds.Length)
                targets.Add(new Target { Name = $"each kind of track has a different best deck ({string.Join(", ", Kinds.Select(k => bestBy[k]))})", Want = "4 different", Got = double.NaN, Ok = bestBy.Values.Distinct().Count() == Kinds.Length });

            // ---------------------------------------------------------------- setups
            Console.WriteLine();
            Console.WriteLine($"Setups: the all-round deck (base + {string.Join(", ", AllRound)}) with each deck's setup, smart bot, {nd} seeds per track");
            Console.WriteLine("track    kind           " + string.Join(" ", Packs.Select(p => $"{p.Id,8}")) + "   best");
            var setup = new Dictionary<(string t, string p), Cell>();
            foreach (var t in stock.Where(x => Part("setups") && Only(x)))
            {
                foreach (var p in Packs)
                {
                    var lo = new PlayerLoadout(); lo.Deck.AddRange(CardBook.StockBase); lo.Deck.AddRange(AllRound); lo.Tune = p.Tune.Clone();
                    setup[(t.Id, p.Id)] = Race(t, seed0, nd, rulesOn, s => (PackRace(t, s, lo.Clone()), "smart"));
                }
                Console.WriteLine($"{t.Id,-8} {TrackDef.KindName(t.Kind),-14} " + string.Join(" ", Packs.Select(p => $"{setup[(t.Id, p.Id)].VsExp / nd,8:+0.0;-0.0}")) + "   " + Best(Packs.Select(p => p.Id), id => setup[(t.Id, id)].VsExp));
            }
            var setupBy = new Dictionary<TrackKind, string>();
            foreach (var k in Kinds.Where(k => Part("setups") && stock.Any(t => t.Kind == k && Only(t))))
            {
                var ts = stock.Where(t => t.Kind == k && Only(t)).ToList();
                double Avg(string id) => ts.Sum(t => setup[(t.Id, id)].VsExp) / (ts.Count * nd);
                setupBy[k] = Packs.OrderByDescending(p => Avg(p.Id)).First().Id;
                Console.WriteLine($"{TrackDef.KindName(k),-23}" + string.Join(" ", Packs.Select(p => $"{Avg(p.Id),8:+0.0;-0.0}")) + $"   {setupBy[k]}");
            }
            if (rulesOn && setupBy.Count == Kinds.Length)
                targets.Add(new Target { Name = $"the best setup isn't the same everywhere ({string.Join(", ", Kinds.Select(k => setupBy[k]))})", Want = "3 or more different", Got = double.NaN, Ok = setupBy.Values.Distinct().Count() >= 3 });

            // ---------------------------------------------------------------- seasons
            if (seasons > 0 && Part("seasons"))
            {
                Console.WriteLine();
                var cal = CareerRules.Calendar(Series.Stock);
                Console.WriteLine($"Premier stock seasons through the menus, smart bot: {seasons} per persona (seeds {seed0}+), {cal.Count} races on {string.Join(", ", cal)}");
                Console.WriteLine("persona      seed  finishes                                 avg   wins  rank  pts   money");
                int races = 0, wins = 0, top10 = 0, cs = 0;
                double fin = 0;
                foreach (var p in LateSeason.Personas)
                    for (int i = 0; i < seasons; i++)
                    {
                        uint seed = seed0 + (uint)i;
                        var m = new CareerMenu { PickPersona = p, NewSeed = seed };
                        m = MenuBot.Run(m, cal.Count, rs => { rs.PitPolicy = PitPolicy.Bot; var race = new Race(rs); race.Bal.StockRules = rulesOn; Bots.Drive(race, "smart"); return race; }, new MenuBot());
                        var k = m.Career;
                        var rs = k.Results.Where(r => r.Season == 1).ToList();
                        var past = k.Past.FirstOrDefault(x => x.Season == 1);
                        int rank = past?.Rank ?? 99;
                        races += rs.Count; wins += rs.Count(r => r.Finish == 1 && !r.Dnf); fin += rs.Sum(r => r.Finish); cs++;
                        if (rank <= 10) top10++;
                        Console.WriteLine($"{p,-12} {seed,4}  {string.Join(" ", rs.Select(r => (r.Dnf ? "DNF" : "P" + r.Finish).PadLeft(3))),-39}  {rs.Average(r => r.Finish),4:F1}  {rs.Count(r => r.Finish == 1 && !r.Dnf),4}  {(rank < 99 ? "P" + rank : "-"),4}  {past?.Pts ?? 0,3}  {k.Money,6:N0}");
                    }
                targets.Add(T("season: careful average finish (slice rules about P8)", "P5 to P11", fin / races, 5, 11));
                targets.Add(T("season: share of premier races won (a rookie in the top series)", "3% to 35%", 100.0 * wins / races, 3, 35));
                targets.Add(T("season: careers in the top 10 of the points", "50% or more", 100.0 * top10 / cs, 50, 100));
            }

            Console.WriteLine();
            foreach (var t in targets)
                Console.WriteLine($"{(t.Ok ? "PASS" : "FAIL")}  {t.Name}: {(double.IsNaN(t.Got) ? "" : t.Got.ToString("0.0") + " ")}(target {t.Want})");
            int bad = targets.Count(t => !t.Ok);
            Console.WriteLine(bad == 0 ? $"OK: all {targets.Count} premier stock targets pass." : $"FAILED: {bad} of {targets.Count} premier stock targets.");
            return bad == 0 ? 0 : 1;
        }

        static string Best(IEnumerable<string> ids, Func<string, double> v) => ids.OrderByDescending(v).First();

        static string TuneText(Tune u) => $"Handling {u.Hand:+0;-0;0}, Aero {u.Aero:+0;-0;0}, {u.FocusName(false)}";

        static Target T(string name, string want, double got, double lo, double hi) => new Target { Name = name, Want = want, Got = got, Ok = got >= lo && got <= hi };
    }
}
