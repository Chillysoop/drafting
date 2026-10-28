using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GreenFlag.Sim;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 28: the C# sim against the browser slice. The baseline (sim/parity/browser-baseline.json) holds the browser
    /// harness's averages per persona, track and play style (prototype/harness/parity-summary.py). This runs the same cells in
    /// C# with the same play styles (Bots) and compares. It fails if any play style's average finish, pooled over every
    /// persona and track, is more than Tol places from the browser's.
    /// </summary>
    public static class Parity
    {
        public const double Tol = 0.5;

        sealed class Cell { public string Key, Persona, Style; public int Track, Bn; public double BFin, BFinSd, BStart, BDnf, BPlays, BLanded, BPerfect, BCalls = -1, BWon, CFin, CStart, CDnf, CPlays, CLanded, CPerfect, CCalls, CWon; public int Cn; }

        public static int Run(string baselinePath, int n, string styleFilter)
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(baselinePath));
            var cells = new List<Cell>();
            foreach (var p in doc.RootElement.GetProperty("cells").EnumerateObject())
            {
                var k = p.Name.Split('|');
                if (styleFilter != "all" && !styleFilter.Split(',').Contains(k[2])) continue;
                var v = p.Value;
                double M(string f) => v.GetProperty(f)[0].GetDouble();
                cells.Add(new Cell
                {
                    Key = p.Name, Persona = k[0], Track = int.Parse(k[1]), Style = k[2], Bn = v.GetProperty("n").GetInt32(),
                    BFin = M("finish"), BFinSd = v.GetProperty("finish")[1].GetDouble(), BStart = M("start"), BDnf = M("dnf"), BPlays = M("plays"),
                    BLanded = v.GetProperty("landed").GetDouble(), BPerfect = v.GetProperty("perfect").GetDouble()
                });
                // crew calls per race and the share that worked (baselines from step 33 on; older ones ran with the radio off)
                if (v.TryGetProperty("calls", out var bc)) { cells[cells.Count - 1].BCalls = bc[0].GetDouble(); cells[cells.Count - 1].BWon = v.GetProperty("callsWon").GetDouble(); }
            }
            System.Threading.Tasks.Parallel.ForEach(cells, c =>
            {
                double fin = 0, start = 0, dnf = 0, pl = 0, su = 0, stops = 0, pf = 0, ca = 0, cw = 0;
                // fixed seeds, different for every persona and track (the same for every style, so styles compare on the same grids)
                uint seed0 = 5000u + (uint)c.Track * 1000u + (uint)c.Persona.Sum(ch => ch) * 10000u;
                for (int i = 0; i < n; i++)
                {
                    // step 42: the premier stock track rules are the engine's own, so parity races the slice's rules
                    var (_, r) = Bots.Run(Tracks.All[c.Track], seed0 + (uint)i, c.Style, c.Persona, x => x.Bal.StockRules = false);
                    fin += r.f; start += r.s; dnf += r.dnf; pl += r.pl; su += r.su; stops += r.stops; pf += r.perfect; ca += r.calls; cw += r.cwon;
                }
                c.Cn = n; c.CFin = fin / n; c.CStart = start / n; c.CDnf = dnf / n; c.CPlays = pl / n; c.CLanded = pl > 0 ? su / pl : 0; c.CPerfect = stops > 0 ? pf / stops : 0; c.CCalls = ca / n; c.CWon = ca > 0 ? cw / ca : 0;
            });

            Console.WriteLine($"Browser baseline {baselinePath} vs C#, {n} races per cell. Finish is the average finishing place (lower is better).");
            Console.WriteLine("cell                        browser n   finish  C# finish   diff   start b/c      DNF b/c    plays b/c   landed b/c  perfect b/c   calls b/c   worked b/c");
            foreach (var c in cells.OrderBy(c => Array.IndexOf(Bots.Styles, c.Style)).ThenBy(c => c.Persona).ThenBy(c => c.Track))
                Console.WriteLine($"{c.Key,-27} {c.Bn,9} {c.BFin,8:F2} {c.CFin,10:F2} {c.CFin - c.BFin,+6:+0.00;-0.00} {c.BStart,6:F1}/{c.CStart,-5:F1} {c.BDnf * 100,4:F0}/{c.CDnf * 100,-4:F0}% {c.BPlays,5:F1}/{c.CPlays,-5:F1} {c.BLanded * 100,4:F0}/{c.CLanded * 100,-4:F0}% {c.BPerfect * 100,4:F0}/{c.CPerfect * 100,-4:F0}% {(c.BCalls >= 0 ? $"{c.BCalls,5:F1}/{c.CCalls,-5:F1} {c.BWon * 100,4:F0}/{c.CWon * 100,-4:F0}%" : $"    -/{c.CCalls,-5:F1}")}");

            Console.WriteLine();
            Console.WriteLine("style    cells  browser finish  C# finish   diff   ±(2 SE)  DNF b/c    calls b/c  worked b/c  result");
            int bad = 0;
            foreach (var g in cells.GroupBy(c => c.Style).OrderBy(g => Array.IndexOf(Bots.Styles, g.Key)))
            {
                double bw = g.Sum(c => c.Bn), b = g.Sum(c => c.BFin * c.Bn) / bw, cf = g.Average(c => c.CFin), d = cf - b;
                double se = Math.Sqrt(g.Sum(c => c.BFinSd * c.BFinSd * c.Bn) / (bw * bw));
                double bd = g.Sum(c => c.BDnf * c.Bn) / bw, cd = g.Average(c => c.CDnf);
                var wc = g.Where(c => c.BCalls >= 0).ToList();
                string calls = wc.Count > 0
                    ? $"{wc.Sum(c => c.BCalls * c.Bn) / wc.Sum(c => c.Bn),5:F2}/{wc.Average(c => c.CCalls),-5:F2} {wc.Sum(c => c.BWon * c.BCalls * c.Bn) / Math.Max(1e-9, wc.Sum(c => c.BCalls * c.Bn)) * 100,4:F0}/{wc.Sum(c => c.CWon * c.CCalls) / Math.Max(1e-9, wc.Sum(c => c.CCalls)) * 100,-4:F0}%"
                    : "     -/-          -/-   ";
                bool ok = Math.Abs(d) <= Tol;
                if (!ok) bad++;
                Console.WriteLine($"{g.Key,-8} {g.Count(),5} {b,15:F2} {cf,10:F2} {d,+6:+0.00;-0.00} {2 * se,8:F2}  {bd * 100,4:F0}/{cd * 100,-4:F0}%  {calls}  {(ok ? "OK" : "OFF")}");
            }
            Console.WriteLine(bad == 0 ? $"PARITY OK: every play style within {Tol} places of the browser slice." : $"PARITY FAILED: {bad} play style(s) more than {Tol} places off.");
            return bad == 0 ? 0 : 1;
        }
    }
}
