using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 41's "done when": a late model season passes the harness targets. Three parts, all seeded, so a run is repeatable.
    ///   Races: every late model track x the five stock personas x n seeds, played by the browser harness's bots: smart (careful
    ///   play), none (hands-off) and spam. Targets come from the slice's late model rung (step 22: careful +10.6, hands-off -0.3,
    ///   spam DNF 42%).
    ///   Tire strategy: the smart bot again on every late model track, with its caution calls made four ways: reading its tires
    ///   (the browser harness's rule), the crew chief's call, always stay out, always four tires. Pit stops and tire strategy
    ///   unlock in late models, so the calls have to matter, and the field has to mix its own calls so restarts mix tires.
    ///   Seasons: whole late model seasons through the career menus, started in late models (MenuBot in the garage and at the
    ///   events, the smart bot in the races), one per persona and seed, to the season review.
    /// Prints tables and a PASS/FAIL line per target; exits 1 if any target fails (CI).
    /// </summary>
    public static class LateSeason
    {
        public static readonly string[] Personas = { "drafter", "bully", "whisperer", "restart", "gambler" };
        static readonly string[] Calls = { "bot", "chief", "stay", "four" };

        sealed class Cell
        {
            public int N, Dnf, Plays, Stops, Cautions;
            public double VsExp;
            public readonly int[] AiCalls = new int[4];
        }

        sealed class Target
        {
            public string Name, Want;
            public double Got;
            public bool Ok;
        }

        public static int Run(int n, int seasons, uint seed0, bool fieldOn = true)
        {
            var late = Tracks.All.Where(t => t.Late).ToList();
            var styles = new[] { "smart", "none", "spam" };
            var cells = new Dictionary<(string t, string st), Cell>();
            Cell Play(TrackDef t, string st, string call)
            {
                var c = new Cell();
                Bots.CautionPolicy = call;
                try
                {
                    foreach (var p in Personas)
                        for (int i = 0; i < n; i++)
                        {
                            var (r, row) = Bots.Run(t, seed0 + (uint)i, st, p, x => x.Bal.LateField = fieldOn);
                            var res = r.Result();
                            c.N++;
                            c.VsExp += res.MyExpected - res.MyFinish;
                            if (res.MyDnf) c.Dnf++;
                            c.Plays += r.CardStats.Plays;
                            c.Stops += r.Me.PitStops;
                            c.Cautions += res.Cautions;
                            for (int k = 0; k < 4; k++) c.AiCalls[k] += r.AiCalls[k];
                        }
                }
                finally { Bots.CautionPolicy = "bot"; }
                return c;
            }
            foreach (var t in late)
                foreach (var st in styles)
                    cells[(t.Id, st)] = Play(t, st, "bot");

            Console.WriteLine($"Late model races: {n} seeds x {Personas.Length} personas per track and style (seeds {seed0}+), Normal, late model field {(fieldOn ? "on" : "off (slice)")}");
            Console.WriteLine("track    style  vs exp   DNF  plays  stops  cautions  AI calls: stay  fuel   two  four");
            foreach (var kv in cells)
            {
                var c = kv.Value;
                int calls = Math.Max(1, c.AiCalls.Sum());
                Console.WriteLine($"{kv.Key.t,-8} {kv.Key.st,-5} {c.VsExp / c.N,7:+0.0;-0.0}  {100.0 * c.Dnf / c.N,3:F0}%  {(double)c.Plays / c.N,5:F1}  {(double)c.Stops / c.N,5:F2}  {(double)c.Cautions / c.N,8:F2}            {Pct(c.AiCalls[0], calls),3:F0}%  {Pct(c.AiCalls[1], calls),3:F0}%  {Pct(c.AiCalls[2], calls),3:F0}%  {Pct(c.AiCalls[3], calls),3:F0}%");
            }

            Cell All(string st)
            {
                var a = new Cell();
                foreach (var kv in cells.Where(k => k.Key.st == st))
                {
                    var c = kv.Value;
                    a.N += c.N; a.VsExp += c.VsExp; a.Dnf += c.Dnf; a.Plays += c.Plays; a.Stops += c.Stops; a.Cautions += c.Cautions;
                    for (int k = 0; k < 4; k++) a.AiCalls[k] += c.AiCalls[k];
                }
                return a;
            }
            var smart = All("smart");
            int aiCalls = Math.Max(1, smart.AiCalls.Sum());
            var targets = new List<Target>
            {
                T("careful play vs expected, all tracks (slice late models +10.6)", "+8 to +14", smart.VsExp / smart.N, 8, 14),
                T("hands-off vs expected (slice late models -0.3)", "-4 to +1", All("none").VsExp / All("none").N, -4, 1),
                T("card spam DNF % (slice late models 42% before step 23 toughened the fields)", "30% to 65%", 100.0 * All("spam").Dnf / All("spam").N, 30, 65),
                T("pit stops a careful race (everyone stops on fuel)", "1 or more", (double)smart.Stops / smart.N, 1, 99),
            };
            foreach (var t in late)
            {
                var c = cells[(t.Id, "smart")];
                targets.Add(T($"careful play vs expected at {t.Id}", "+6 to +16", c.VsExp / c.N, 6, 16));
            }
            if (fieldOn)
            {
                // the field mixes its calls, so a restart has cars on old tires (track position) and fresh ones (the charge)
                targets.Add(T("field caution calls that stay out, %", "20% to 60%", Pct(smart.AiCalls[0], aiCalls), 20, 60));
                targets.Add(T("field caution calls for four tires, %", "20% or more", Pct(smart.AiCalls[3], aiCalls), 20, 100));
            }
            else targets.Clear();

            if (fieldOn)
            {
                Console.WriteLine();
                Console.WriteLine($"Tire strategy: the smart bot on every late model track, {n} seeds x {Personas.Length} personas, four ways to call a caution");
                Console.WriteLine("track    " + string.Join("  ", Calls.Select(x => $"{x,7}")));
                var by = new Dictionary<(string t, string call), Cell>();
                foreach (var t in late)
                {
                    foreach (var call in Calls) by[(t.Id, call)] = call == "bot" ? cells[(t.Id, "smart")] : Play(t, "smart", call);
                    Console.WriteLine($"{t.Id,-8} " + string.Join("  ", Calls.Select(x => $"{by[(t.Id, x)].VsExp / by[(t.Id, x)].N,7:+0.0;-0.0}")));
                }
                double Avg(string call) => late.Sum(t => by[(t.Id, call)].VsExp) / late.Sum(t => by[(t.Id, call)].N);
                Console.WriteLine($"all      " + string.Join("  ", Calls.Select(x => $"{Avg(x),7:+0.00;-0.00}")));
                // the crew chief reads tires, fuel, damage and laps to go. Pitting for four every time throws track position away;
                // staying out every time is a real strategy (it is close), but it must not beat the crew chief's reads
                targets.Add(T("the crew chief's calls beat always taking four (places)", "+2 or more", Avg("chief") - Avg("four"), 2, 99));
                targets.Add(T("always staying out doesn't beat the crew chief's calls (places)", "-0.5 or more", Avg("chief") - Avg("stay"), -0.5, 99));
                var stops = late.OrderByDescending(t => (double)cells[(t.Id, "smart")].Stops / cells[(t.Id, "smart")].N).First().Id;
                targets.Add(new Target { Name = $"the tire race, Granite Falls, has the most pit stops a race (most: {stops})", Want = "gran", Got = double.NaN, Ok = stops == "gran" });
            }

            if (seasons > 0)
            {
                Console.WriteLine();
                var cal = CareerRules.Calendar(Series.Late);
                Console.WriteLine($"Late model seasons through the menus, smart bot: {seasons} per persona (seeds {seed0}+), started in late models, {cal.Count} races on {string.Join(", ", cal)}");
                Console.WriteLine("persona      seed  finishes                         avg   wins  rank  pts   money   promote");
                int races = 0, wins = 0, top14 = 0, cs = 0;
                double fin = 0;
                foreach (var p in Personas)
                    for (int i = 0; i < seasons; i++)
                    {
                        uint seed = seed0 + (uint)i;
                        var m = new CareerMenu { PickPersona = p, PickSeries = Series.Late, NewSeed = seed };
                        m = MenuBot.Run(m, cal.Count, rs => { rs.PitPolicy = PitPolicy.Bot; var race = new Race(rs); Bots.Drive(race, "smart"); return race; }, new MenuBot());
                        var k = m.Career;
                        var rs = k.Results.Where(r => r.Season == 1).ToList();
                        var past = k.Past.FirstOrDefault(x => x.Season == 1);
                        int rank = past?.Rank ?? 99;
                        races += rs.Count; wins += rs.Count(r => r.Finish == 1 && !r.Dnf); fin += rs.Sum(r => r.Finish); cs++;
                        if (rank <= 14) top14++;
                        Console.WriteLine($"{p,-12} {seed,4}  {string.Join(" ", rs.Select(r => (r.Dnf ? "DNF" : "P" + r.Finish).PadLeft(3))),-31}  {rs.Average(r => r.Finish),4:F1}  {rs.Count(r => r.Finish == 1 && !r.Dnf),4}  {(rank < 99 ? "P" + rank : "-"),4}  {past?.Pts ?? 0,3}  {k.Money,6:N0}   {(k.Ladder.Contains(Series.Stock) ? "signed" : rank <= 14 ? "offered" : "no")}");
                    }
                targets.Add(T("season: careful average finish (slice late models about P4)", "P3 to P8", fin / races, 3, 8));
                targets.Add(T("season: share of late model races won", "15% to 55%", 100.0 * wins / races, 15, 55));
                targets.Add(T("season: careers in the top 14 (a stock car offer)", "75% or more", 100.0 * top14 / cs, 75, 100));
            }

            Console.WriteLine();
            foreach (var t in targets)
                Console.WriteLine($"{(t.Ok ? "PASS" : "FAIL")}  {t.Name}: {(double.IsNaN(t.Got) ? "" : t.Got.ToString("0.0") + " ")}(target {t.Want})");
            int bad = targets.Count(t => !t.Ok);
            Console.WriteLine(bad == 0 ? $"OK: all {targets.Count} late model targets pass." : $"FAILED: {bad} of {targets.Count} late model targets.");
            return bad == 0 ? 0 : 1;
        }

        static double Pct(int a, int b) => b > 0 ? 100.0 * a / b : 0;

        static Target T(string name, string want, double got, double lo, double hi) => new Target { Name = name, Want = want, Got = got, Ok = got >= lo && got <= hi };
    }
}
