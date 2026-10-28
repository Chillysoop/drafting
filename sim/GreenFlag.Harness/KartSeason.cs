using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 40's "done when": a kart season passes the harness targets. Two parts, both seeded, so a run is repeatable.
    ///   Races: every kart circuit x the four kart personas x n seeds, played by the browser harness's bots: smart (careful
    ///   play), none (hands-off) and spam. Targets come from the slice's kart rung (step 21, board Q74: karts close to that).
    ///   Seasons: whole kart seasons through the career menus (MenuBot in the garage and at the events, the smart bot in the
    ///   races), one per persona and seed, to the season review. Targets come from the slice's season balance (step 23: careful play about P5 in
    ///   karts, wins about 29% of kart races).
    /// Prints a table and a PASS/FAIL line per target; exits 1 if any target fails (CI).
    /// </summary>
    public static class KartSeason
    {
        public static readonly string[] Personas = { "brawler", "linepainter", "packrat", "latebraker" };

        sealed class Cell
        {
            public int N, Dnf, Plays, Pass, Draft, Combos, Bumps, Covers;
            public double VsExp, Chain;
        }

        sealed class Target
        {
            public string Name, Want;
            public double Got;
            public bool Ok;
        }

        public static int Run(int n, int seasons, uint seed0, bool fieldOn = true, string style = "smart")
        {
            var kart = Tracks.All.Where(t => t.Kart).ToList();
            var styles = new[] { "smart", "none", "spam" };
            var cells = new Dictionary<(string t, string st), Cell>();
            foreach (var t in kart)
                foreach (var st in styles)
                {
                    var c = cells[(t.Id, st)] = new Cell();
                    foreach (var p in Personas)
                        for (int i = 0; i < n; i++)
                        {
                            var (r, row) = Bots.Run(t, seed0 + (uint)i, st, p, x => x.Bal.KartField = fieldOn);
                            var res = r.Result();
                            c.N++;
                            c.VsExp += res.MyExpected - res.MyFinish;
                            if (res.MyDnf) c.Dnf++;
                            c.Plays += r.CardStats.Plays;
                            c.Combos += r.CardStats.Combos;
                            c.Chain += r.CardStats.BestChain;
                            foreach (var kv in r.CardStats.ByCard)
                            {
                                var f = CardBook.Get(kv.Key).Family;
                                int plays = kv.Value[5];
                                if (f == CardFamily.Pass) c.Pass += plays;
                                if (f == CardFamily.Draft) c.Draft += plays;
                            }
                            c.Bumps += r.KartBumps;
                            c.Covers += r.KartCovers;
                        }
                }

            Console.WriteLine($"Kart races: {n} seeds x {Personas.Length} personas per circuit and style (seeds {seed0}+), Normal, kart field {(fieldOn ? "on" : "off (slice)")}");
            Console.WriteLine("circuit   style  vs exp   DNF  plays  pass%  draft%  combos  best  bumps  covers");
            foreach (var kv in cells)
            {
                var c = kv.Value;
                Console.WriteLine($"{kv.Key.t,-9} {kv.Key.st,-5} {c.VsExp / c.N,7:+0.0;-0.0}  {100.0 * c.Dnf / c.N,3:F0}%  {(double)c.Plays / c.N,5:F1}  {Pct(c.Pass, c.Plays),4:F0}%  {Pct(c.Draft, c.Plays),5:F0}%  {(double)c.Combos / c.N,6:F1}  {c.Chain / c.N,4:F1}  {(double)c.Bumps / c.N,5:F2}  {(double)c.Covers / c.N,6:F2}");
            }

            Cell All(string st)
            {
                var a = new Cell();
                foreach (var kv in cells.Where(k => k.Key.st == st))
                {
                    var c = kv.Value;
                    a.N += c.N; a.VsExp += c.VsExp; a.Dnf += c.Dnf; a.Plays += c.Plays; a.Pass += c.Pass; a.Draft += c.Draft; a.Chain += c.Chain;
                }
                return a;
            }
            var smart = All("smart");
            var targets = new List<Target>
            {
                T("careful play vs expected, all circuits (slice karts +10.8)", "+8 to +14", smart.VsExp / smart.N, 8, 14),
                T("hands-off vs expected (slice karts -1.4)", "-4 to +1", All("none").VsExp / All("none").N, -4, 1),
                T("card spam DNF % (slice karts 30%)", "15% to 40%", 100.0 * All("spam").Dnf / All("spam").N, 15, 40),
                T("pass cards, % of careful plays (stock 17%)", "20% or more", Pct(smart.Pass, smart.Plays), 20, 100),
                T("best combo chain a race (capped x3; stock about 5)", "3.5 or less", smart.Chain / smart.N, 0, 3.5),
            };
            foreach (var t in kart)
            {
                var c = cells[(t.Id, "smart")];
                targets.Add(T($"careful play vs expected at {t.Id}", "+6 to +16", c.VsExp / c.N, 6, 16));
            }
            // the circuits ask for different hands: the tow is worth the most on the fast lake and the least in the twisty club
            var byDraft = kart.OrderBy(t => { var c = cells[(t.Id, "smart")]; return Pct(c.Draft, c.Plays); }).Select(t => t.Id).ToList();
            targets.Add(new Target { Name = $"draft cards, fewest at Bramble Hill and most at Lakeshore (low to high: {string.Join(", ", byDraft)})", Want = "bramble first, lake last", Got = double.NaN, Ok = byDraft[0] == "bramble" && byDraft[byDraft.Count - 1] == "lake" });
            if (!fieldOn) targets.Clear();

            if (seasons > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Kart seasons through the menus, {style} bot: {seasons} per persona (seeds {seed0}+), {CareerRules.Calendar(Series.Kart).Count} races on {string.Join(", ", CareerRules.Calendar(Series.Kart))}");
                Console.WriteLine("persona      seed  finishes                avg   wins  rank  pts   money   promote");
                int races = 0, wins = 0, top14 = 0, cs = 0;
                double fin = 0;
                foreach (var p in Personas)
                    for (int i = 0; i < seasons; i++)
                    {
                        uint seed = seed0 + (uint)i;
                        var m = new CareerMenu { PickPersona = p, NewSeed = seed };
                        var bot = new MenuBot();
                        int len = CareerRules.Calendar(Series.Kart).Count;
                        m = MenuBot.Run(m, len, rs => { rs.PitPolicy = PitPolicy.Bot; var race = new Race(rs); Bots.Drive(race, style); return race; }, bot);
                        var k = m.Career;
                        var rs = k.Results.Where(r => r.Season == 1).ToList();
                        var past = k.Past.FirstOrDefault(x => x.Season == 1);
                        int rank = past?.Rank ?? 99;
                        races += rs.Count; wins += rs.Count(r => r.Finish == 1 && !r.Dnf); fin += rs.Sum(r => r.Finish); cs++;
                        if (rank <= 14) top14++;
                        Console.WriteLine($"{p,-12} {seed,4}  {string.Join(" ", rs.Select(r => (r.Dnf ? "DNF" : "P" + r.Finish).PadLeft(3))),-22}  {rs.Average(r => r.Finish),4:F1}  {rs.Count(r => r.Finish == 1 && !r.Dnf),4}  {(rank < 99 ? "P" + rank : "-"),4}  {past?.Pts ?? 0,3}  {k.Money,6:N0}   {(k.Ladder.Contains(Series.Late) ? "signed" : rank <= 14 ? "offered" : "no")}");
                    }
                targets.Add(T("season: careful average finish (slice karts about P5)", "P3 to P8", fin / races, 3, 8));
                targets.Add(T("season: share of kart races won (slice 29%)", "10% to 45%", 100.0 * wins / races, 10, 45));
                targets.Add(T("season: careers in the top 14 (a late model offer)", "75% or more", 100.0 * top14 / cs, 75, 100));
            }

            Console.WriteLine();
            foreach (var t in targets)
                Console.WriteLine($"{(t.Ok ? "PASS" : "FAIL")}  {t.Name}: {(double.IsNaN(t.Got) ? "" : t.Got.ToString("0.0") + " ")}(target {t.Want})");
            int bad = targets.Count(t => !t.Ok);
            Console.WriteLine(bad == 0 ? $"OK: all {targets.Count} kart targets pass." : $"FAILED: {bad} of {targets.Count} kart targets.");
            return bad == 0 ? 0 : 1;
        }

        static double Pct(int a, int b) => b > 0 ? 100.0 * a / b : 0;

        static Target T(string name, string want, double got, double lo, double hi) => new Target { Name = name, Want = want, Got = got, Ok = got >= lo && got <= hi };
    }
}
