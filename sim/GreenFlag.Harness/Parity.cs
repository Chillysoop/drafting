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

        sealed class Cell { public string Key, Persona, Style; public int Track, Bn; public double BFin, BFinSd, BStart, BDnf, BPlays, BLanded, BPerfect, CFin, CStart, CDnf, CPlays, CLanded, CPerfect; public int Cn; }

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
            }
            System.Threading.Tasks.Parallel.ForEach(cells, c =>
            {
                double fin = 0, start = 0, dnf = 0, pl = 0, su = 0, stops = 0, pf = 0;
                for (int i = 0; i < n; i++)
                {
                    var (_, r) = Bots.Run(Tracks.All[c.Track], 5000 + (uint)i, c.Style, c.Persona);
                    fin += r.f; start += r.s; dnf += r.dnf; pl += r.pl; su += r.su; stops += r.stops; pf += r.perfect;
                }
                c.Cn = n; c.CFin = fin / n; c.CStart = start / n; c.CDnf = dnf / n; c.CPlays = pl / n; c.CLanded = pl > 0 ? su / pl : 0; c.CPerfect = stops > 0 ? pf / stops : 0;
            });

            Console.WriteLine($"Browser baseline {baselinePath} vs C#, {n} races per cell. Finish is the average finishing place (lower is better).");
            Console.WriteLine("cell                        browser n   finish  C# finish   diff   start b/c      DNF b/c    plays b/c   landed b/c  perfect b/c");
            foreach (var c in cells.OrderBy(c => Array.IndexOf(Bots.Styles, c.Style)).ThenBy(c => c.Persona).ThenBy(c => c.Track))
                Console.WriteLine($"{c.Key,-27} {c.Bn,9} {c.BFin,8:F2} {c.CFin,10:F2} {c.CFin - c.BFin,+6:+0.00;-0.00} {c.BStart,6:F1}/{c.CStart,-5:F1} {c.BDnf * 100,4:F0}/{c.CDnf * 100,-4:F0}% {c.BPlays,5:F1}/{c.CPlays,-5:F1} {c.BLanded * 100,4:F0}/{c.CLanded * 100,-4:F0}% {c.BPerfect * 100,4:F0}/{c.CPerfect * 100,-4:F0}%");

            Console.WriteLine();
            Console.WriteLine("style    cells  browser finish  C# finish   diff   ±(2 SE)  DNF b/c      result");
            int bad = 0;
            foreach (var g in cells.GroupBy(c => c.Style).OrderBy(g => Array.IndexOf(Bots.Styles, g.Key)))
            {
                double bw = g.Sum(c => c.Bn), b = g.Sum(c => c.BFin * c.Bn) / bw, cf = g.Average(c => c.CFin), d = cf - b;
                double se = Math.Sqrt(g.Sum(c => c.BFinSd * c.BFinSd * c.Bn) / (bw * bw));
                double bd = g.Sum(c => c.BDnf * c.Bn) / bw, cd = g.Average(c => c.CDnf);
                bool ok = Math.Abs(d) <= Tol;
                if (!ok) bad++;
                Console.WriteLine($"{g.Key,-8} {g.Count(),5} {b,15:F2} {cf,10:F2} {d,+6:+0.00;-0.00} {2 * se,8:F2}  {bd * 100,4:F0}/{cd * 100,-4:F0}%    {(ok ? "OK" : "OFF")}");
            }
            Console.WriteLine(bad == 0 ? $"PARITY OK: every play style within {Tol} places of the browser slice." : $"PARITY FAILED: {bad} play style(s) more than {Tol} places off.");
            return bad == 0 ? 0 : 1;
        }
    }
}
