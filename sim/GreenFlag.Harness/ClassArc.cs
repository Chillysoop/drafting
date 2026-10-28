using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 43's "done when": a kart rival can show up as your stock car teammate or rival. Two parts, both seeded.
    ///   Arcs: whole careers from karts through the career menus (MenuBot in the garage, at the events and in the inbox, the
    ///   smart bot in the races), one per kart persona and seed, season after season until a stock car season is done in
    ///   which a driver who started in karts with you was your teammate or this week's rival (or maxSeasons). Prints each
    ///   season (series, team, rank, the class drivers in your field with their Grudge) and the silly season's size.
    ///   Grudge: premier stock seasons for the stock personas, careful (smart) and card spam, with the class and without it
    ///   (Career.ClassOff), for paybacks, Warm and Hot rivals, and the cost to careful play.
    /// Prints a PASS/FAIL line per target; exits 1 if any target fails (CI).
    /// </summary>
    public static class ClassArc
    {
        sealed class Target { public string Name, Want; public double Got; public bool Ok; }
        static Target T(string name, string want, double got, double lo, double hi) => new Target { Name = name, Want = want, Got = got, Ok = got >= lo && got <= hi };

        static Race Play(RaceSetup rs, string style)
        {
            rs.PitPolicy = PitPolicy.Bot;
            var race = new Race(rs);
            Bots.Drive(race, style);
            return race;
        }

        public static int Run(int n, uint seed0, int maxSeasons, int grudgeSeasons)
        {
            var targets = new List<Target>();
            var kids = RivalBook.All.Where(d => d.Gen == 0).Select(d => d.Id).ToHashSet();

            // ---------------------------------------------------------------- arcs
            Console.WriteLine($"Arcs: careers from karts through the menus, smart bot, {n} per kart persona (seeds {seed0}+), up to {maxSeasons} seasons");
            int careers = 0, toStock = 0, done = 0, offSeasons = 0, moves = 0, replies = 0, careersReplied = 0;
            var doneAt = new List<int>();
            foreach (var p in KartSeason.Personas)
                for (int i = 0; i < n; i++)
                {
                    uint seed = seed0 + (uint)i;
                    var m = new CareerMenu { PickPersona = p, NewSeed = seed };
                    var bot = new MenuBot();
                    careers++;
                    bool stock = false, hit = false;
                    string how = null;
                    int hitSeason = 0;
                    Console.WriteLine($"{p} seed {seed}:");
                    for (int r = 0; r < 200; r++)
                    {
                        if (m.Career != null && (m.Career.Season > maxSeasons || (hit && m.Career.Season > hitSeason))) break;
                        m = MenuBot.Run(m, 1, rs => Play(rs, "smart"), bot);
                        var c = m.Career;
                        var res = c.Results[c.Results.Count - 1];
                        if (res.RaceNo == 1)
                        {
                            var f = c.InField.Select(d => $"{d.Def.Last}{(c.SeatOf(d)?.Team == c.Team ? " (mate)" : "")} {RivalBook.Num(d.G)}");
                            Console.WriteLine($"  season {res.Season} {SeasonRules.SeriesName(Tracks.ById(res.TrackId).Series),-11} class in field: {string.Join(", ", f)}");
                        }
                        if (c.Series == Series.Stock && Tracks.ById(res.TrackId).Series == Series.Stock)
                        {
                            stock = true;
                            var mate = c.ClassOf(c.Mate);
                            var wk = res.Rivals.FirstOrDefault(l => l.Week);
                            if (!hit && mate != null && kids.Contains(mate.Id)) { hit = true; how = $"{mate.Def.Name} was your teammate in season {res.Season}"; }
                            if (!hit && wk != null && kids.Contains(wk.Rid)) { hit = true; how = $"{wk.Name} was your rival at {res.TrackName} (season {res.Season}, race {res.RaceNo})"; }
                            if (hit && hitSeason == 0) { hitSeason = res.Season; doneAt.Add(res.Season); }
                        }
                        if (c.Review != null && c.Review.Season == res.Season && m.Screen != CareerScreen.Reward)
                        {
                            offSeasons++;
                            moves += c.News.Count;
                            var past = c.Past.Last();
                            Console.WriteLine($"  season {past.Season} over: P{past.Rank} in {SeasonRules.SeriesName(past.Series).ToLowerInvariant()}; silly season {c.News.Count} lines" + (c.News.Count > 0 ? ": " + string.Join(" | ", c.News.Take(4)) + (c.News.Count > 4 ? " | …" : "") : ""));
                        }
                    }
                    if (stock) toStock++;
                    if (hit) done++;
                    replies += bot.Replies;
                    if (bot.Replies > 0) careersReplied++;
                    Console.WriteLine($"  => {(hit ? "DONE: " + how : stock ? "reached stock cars, no kart rival as teammate or rival yet" : "never reached stock cars")} · {bot.Replies} replies · inbox {m.Career.Inbox.Count}");
                }
            if (careers > 0)
            {
                targets.Add(T("arcs: careers from karts that reach stock cars", $"75% or more within {maxSeasons} seasons", 100.0 * toStock / careers, 75, 100));
                targets.Add(T("arcs: of those, a kart rival shows up as your stock car teammate or rival (step 43's done when)", "75% or more", toStock == 0 ? 0 : 100.0 * done / toStock, 75, 100));
                targets.Add(T("arcs: at least one career where it happens", "1 or more", done, 1, 999));
                targets.Add(T("silly season: lines per off-season (moves, step-ups, lost rides, arrivals)", "3 or more", offSeasons == 0 ? 0 : (double)moves / offSeasons, 3, 99));
                targets.Add(T("inbox: careers that answered at least one rival", "75% or more", 100.0 * careersReplied / careers, 75, 100));
            }
            if (doneAt.Count > 0) Console.WriteLine($"done in season {string.Join(", ", doneAt)} (average {doneAt.Average():F1})");

            // ---------------------------------------------------------------- Grudge
            if (grudgeSeasons > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Grudge: premier stock seasons, {grudgeSeasons} per stock persona (seeds {seed0}+), with the class and without it");
                Console.WriteLine("style  class  races  avg fin  DNF%  paybacks/season  Warm+ races  Hot+ races  rival obj met");
                var avg = new Dictionary<(string, bool), double>();
                var stat = new Dictionary<(string, bool), (double pb, double warm, double hot)>();
                foreach (var style in new[] { "smart", "spam" })
                    foreach (bool on in new[] { true, false })
                    {
                        int races = 0, dnf = 0, pb = 0, warm = 0, hot = 0, robj = 0, rn = 0, seasons = 0; double fin = 0;
                        foreach (var p in LateSeason.Personas)
                            for (int i = 0; i < grudgeSeasons; i++)
                            {
                                var m = new CareerMenu { PickPersona = p, NewSeed = seed0 + (uint)i };
                                var bot = new MenuBot();
                                bot.Step(m);
                                if (!on) m.Career.ClassOff();
                                int len = m.Career.SeasonLength;
                                seasons++;
                                for (int k = 0; k < len; k++)
                                {
                                    var c0 = m.Career;
                                    bool w = c0.InField.Any(d => d.G >= 3), h = c0.InField.Any(d => d.G >= 6);
                                    m = MenuBot.Run(m, 1, rs => Play(rs, style), bot);
                                    var res = m.Career.Results.Last();
                                    races++; fin += res.Finish; if (res.Dnf) dnf++;
                                    pb += res.Paybacks; if (w) warm++; if (h) hot++;
                                    foreach (var o in res.Objectives.Where(o => SeasonRules.Objective(o.Id).Rival)) { rn++; if (o.Ok) robj++; }
                                }
                            }
                        avg[(style, on)] = fin / races;
                        stat[(style, on)] = ((double)pb / seasons, 100.0 * warm / races, 100.0 * hot / races);
                        Console.WriteLine($"{style,-6} {(on ? "on" : "off"),-5} {races,5}  {fin / races,7:F1}  {100.0 * dnf / races,4:F0}  {(double)pb / seasons,15:F2}  {100.0 * warm / races,10:F0}%  {100.0 * hot / races,9:F0}%  {(rn > 0 ? $"{100.0 * robj / rn:F0}% of {rn}" : "-")}");
                    }
                targets.Add(T("grudge: careful play, paybacks a season", "0.5 or fewer", stat[("smart", true)].pb, 0, 0.5));
                targets.Add(T("grudge: careful play, races with a Hot rival in the field (%)", "15% or less", stat[("smart", true)].hot, 0, 15));
                targets.Add(T("grudge: card spam makes enemies, races with a Warm rival in the field (%)", "25% or more", stat[("spam", true)].warm, 25, 100));
                targets.Add(T("grudge: card spam takes more paybacks than careful play (a season)", "more", stat[("spam", true)].pb - stat[("smart", true)].pb, 0.01, 99));
                targets.Add(T("balance: careful season finish, class on minus off (places)", "within 1.5", avg[("smart", true)] - avg[("smart", false)], -1.5, 1.5));
            }

            Console.WriteLine();
            foreach (var t in targets)
                Console.WriteLine($"{(t.Ok ? "PASS" : "FAIL")}  {t.Name}: {t.Got:0.0#} (target {t.Want})");
            int bad = targets.Count(t => !t.Ok);
            Console.WriteLine(bad == 0 ? $"OK: all {targets.Count} rival arc targets pass." : $"FAILED: {bad} of {targets.Count} rival arc targets.");
            return bad == 0 ? 0 : 1;
        }
    }
}
