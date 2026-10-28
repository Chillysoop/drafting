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
    ///   race  [track] [seed] [policy]     one race: grid, event log, finish order, fingerprint
    ///   batch [n] [tracks] [policy]       n races per track: average finish, cautions, wrecks, DNFs, length
    ///   check [n]                         every track, n seeds, each run twice: fails if any run differs (CI)
    /// Policies: none (hands-off: never presses PIT, takes the crew chief's caution call) and pit (presses PIT on the
    /// plan lap and calls cautions by tires and damage, like the browser harness's bots). Cards arrive in step 27.
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
                    case "race": return RaceCmd(Arg(args, 1, "carver"), uint.Parse(Arg(args, 2, "61")), Arg(args, 3, "none"));
                    case "batch": return Batch(int.Parse(Arg(args, 1, "100")), Arg(args, 2, "all"), Arg(args, 3, "none"));
                    case "check": return Check(int.Parse(Arg(args, 1, "5")));
                    default: Console.Error.WriteLine("usage: race [track] [seed] [policy] | batch [n] [tracks] [policy] | check [n]"); return 2;
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }

        static string Arg(string[] a, int i, string d) => a.Length > i ? a[i] : d;

        static IEnumerable<TrackDef> TracksFor(string spec) => spec == "all" ? Tracks.All : spec.Split(',').Select(Tracks.ById);

        /// <summary>Run one race with a policy driving your car's pit calls.</summary>
        public static (Race race, RaceResult res) Run(TrackDef t, uint seed, string policy, bool log = false)
        {
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

        static int RaceCmd(string track, uint seed, string policy)
        {
            var t = Tracks.ById(track);
            var (race, res) = Run(t, seed, policy, true);
            Console.WriteLine($"{t.Name} ({t.Id}), {t.Laps} laps, seed {seed}, policy {policy}");
            Console.WriteLine("Grid: " + string.Join(" ", race.Cars.Select(c => (c.IsMe ? "*" : "") + c.Ref.Number)));
            foreach (var e in race.Log) if (e.Kind != RaceEventKind.Pass || e.Car == race.Me.Id || e.Other == race.Me.Id) Console.WriteLine(e);
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

        static int Batch(int n, string tracks, string policy)
        {
            Console.WriteLine($"{n} races per track, policy {policy}, Normal");
            Console.WriteLine("track    avg fin  vs exp  start  cautions  wrecks  field DNF  my DNF  length s");
            foreach (var t in TracksFor(tracks))
            {
                double fin = 0, exp = 0, st = 0, cau = 0, wr = 0, dnf = 0, my = 0, dur = 0;
                for (int i = 0; i < n; i++)
                {
                    var (_, r) = Run(t, (uint)(1000 + i), policy);
                    fin += r.MyFinish; exp += r.MyExpected; st += r.MyStart; cau += r.Cautions; wr += r.Wrecks; dnf += r.Dnfs; my += r.MyDnf ? 1 : 0; dur += r.Duration;
                }
                Console.WriteLine($"{t.Id,-8} {fin / n,7:F1} {(exp - fin) / n,7:F1} {st / n,6:F1} {cau / n,9:F2} {wr / n,7:F2} {dnf / n,10:F2} {my / n * 100,6:F0}% {dur / n,9:F0}");
            }
            return 0;
        }

        static int Check(int n)
        {
            int bad = 0;
            var sb = new StringBuilder();
            foreach (var t in Tracks.All)
                foreach (var policy in new[] { "none", "pit" })
                    for (int i = 0; i < n; i++)
                    {
                        uint seed = (uint)(61 + i * 7919);
                        var (a, ra) = Run(t, seed, policy);
                        var (b, rb) = Run(t, seed, policy);
                        string fa = a.Fingerprint(), fb = b.Fingerprint();
                        bool same = fa == fb && ra.Order.Select(x => x.Id).SequenceEqual(rb.Order.Select(x => x.Id));
                        if (!same) { bad++; Console.WriteLine($"MISMATCH {t.Id} seed {seed} {policy}: {fa} vs {fb}"); }
                        sb.Append($"{t.Id,-7} seed {seed,-6} {policy,-4} P{ra.MyFinish,-3} cautions {ra.Cautions} wrecks {ra.Wrecks} {fa}\n");
                    }
            Console.Write(sb);
            Console.WriteLine(bad == 0 ? $"OK: {Tracks.All.Count * 2 * n} races, each run twice, identical." : $"FAILED: {bad} races differed between runs.");
            return bad == 0 ? 0 : 1;
        }
    }
}
