using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>How a qualifying corner went, best first.</summary>
    public enum QualiMark { Perfect, Good, Ok, Early, Late }

    /// <summary>One timing check: a corner of one lap. The needle sweeps the brake bar from S0 to S1 (run seconds); release
    /// inside the zone around C. Wp, Wg and Wo are the Perfect, Good and OK half-widths, as a share of the bar.</summary>
    public sealed class QualiCheck
    {
        public int Lap;
        public string Name, Action;
        public TurnKind Kind;
        /// <summary>Track position of the corner and of the check before it (fractions of a lap, counting up across laps).</summary>
        public double U, U0;
        public double T0, S0, S1, C, Wp, Wg, Wo;
        public QualiMark? Res;
        /// <summary>Where the needle was on release (0 to 1), or null for never released.</summary>
        public double? X;
        public double Pts;
        public int Chain;
        /// <summary>A Tidy line card turned this miss into an OK.</summary>
        public bool Saved;
    }

    /// <summary>A whole run: every check with its zone and timing.</summary>
    public sealed class QualiPlan
    {
        public TrackDef Track;
        public int Laps;
        public List<QualiCheck> Checks = new List<QualiCheck>();
        public double Sweep, Gap, End, U0, UEnd;
        public double Wp, Wg, Wo, Draft;
        public CardFamily? Fam;
        public string Card;
    }

    public sealed class QualiScore
    {
        public double Tot, Avg;
        public int N, Chain, Best;
        public readonly Dictionary<int, double> LapPts = new Dictionary<int, double>();
        public readonly Dictionary<int, int> LapN = new Dictionary<int, int>();
    }

    /// <summary>Qualifying's outcome: the grid (car ids, pole first), lap times, your spot, and the card that starts the race in
    /// your discard pile. Season and RaceIdx say which race it is for.</summary>
    public sealed class QualiResult
    {
        public int Season, RaceIdx, Pos, Best;
        public string TrackId, Card;
        public List<int> Grid = new List<int>();
        public List<double> Times = new List<double>();
        public double Time, Avg;
        /// <summary>Simmed an average lap (Skip), or gave up (quit mid-run).</summary>
        public bool Skip;
        /// <summary>How many checks got each mark, Perfect first.</summary>
        public int[] Counts = new int[5];
    }

    /// <summary>
    /// Qualifying (roadmap step 37, a port of the slice's step 16 mini-game): a rolling start, then two timed laps. At every
    /// corner a needle sweeps the brake bar; release inside the zone. Perfect / Good / OK / Early / Late score points, clean
    /// corners in a row build a chain up to ×4, and one card from the race deck can trim the run (it then starts the race in
    /// the discard pile). Power, Aero, Handling and Racecraft set the base lap (car rating, like the AI); Handling and
    /// Consistency widen the Good zone, Racecraft widens Perfect. Skip runs an average lap, the old roll-off.
    /// </summary>
    public static class Qualifying
    {
        public const int Laps = 2;
        public const double Roll = 3.4, RollU = 0.22, GapMax = 2.2, StatK = 0.0009, RunSec = 44, Slow = 0.35, FocusSec = 3, PerfFocus = 0.05;
        public const double ChainStep = 0.1, K = 0.03, SkipPts = 1.15, SkipQ = -0.002, Noise = 0.003, PlayNoise = 0.0015;
        public const int ChainMax = 4;

        public static double MarkPts(QualiMark m) => m == QualiMark.Perfect ? 1.6 : m == QualiMark.Good ? 1 : m == QualiMark.Ok ? 0.25 : m == QualiMark.Early ? -0.5 : -1;
        public static string MarkName(QualiMark m) => m == QualiMark.Perfect ? "Perfect" : m == QualiMark.Good ? "Good" : m == QualiMark.Ok ? "OK" : m == QualiMark.Early ? "Early" : "Late";

        /// <summary>Seconds the needle takes to sweep the bar on each track.</summary>
        public static double SweepOf(TrackDef t)
        {
            switch (t.Id)
            {
                case "carver": return 1.4;
                case "talon": return 1.2;
                case "pine": case "ridge": case "pinek": case "cedar": return 1.5;
                default: return 1.45;
            }
        }

        /// <summary>What a card of each family does in qualifying.</summary>
        public static (string name, string text) CardFx(CardFamily f, TrackDef t)
        {
            switch (f)
            {
                case CardFamily.Draft: return ("Slipstream", t.DraftBias > 0.5 ? "+0.25 on every corner here: the tow is worth a lot on this track." : t.DraftBias > 0 ? "+0.15 on every corner: a little tow down the straights." : "+0.05 on every corner: there is barely any tow here.");
                case CardFamily.Pass: return ("Attack the lap", "Perfect zone 60% wider.");
                case CardFamily.Defend: return ("Tidy line", "Your first miss each lap counts as OK.");
                case CardFamily.Car: return ("Grip setup", "Good zone 30% wider.");
                case CardFamily.Risk: return ("Send it", "Perfect and Good score 50% more, but a miss costs double.");
                case CardFamily.Crew: return ("Spotter count-in", "Start with 150% Focus.");
                case CardFamily.Restart: return ("Hot launch", "Your chain starts at ×2.");
                default: return ("Fresh stickers", "Every zone 25% wider on the first lap.");
            }
        }

        /// <summary>The corners of one lap from the line, as track fractions: four on an oval, one per corner group on a circuit.</summary>
        public static List<(double u, string name, string action, TurnKind kind)> Corners(TrackDef t)
        {
            var sh = TrackShape.Of(t);
            var o = new List<(double, string, string, TurnKind)>();
            if (!t.IsPath)
            {
                double P = sh.Perimeter, S = t.S, T = Math.PI * t.R;
                bool ss = t.Id == "talon";
                o.Add((S / P, "Turn 1", ss ? "Stay low" : "Lift", TurnKind.Corner));
                o.Add(((S + T) / P, "Turn 2", "Gas", TurnKind.Corner));
                o.Add(((2 * S + T) / P, "Turn 3", ss ? "Stay low" : "Lift", TurnKind.Corner));
                o.Add(((2 * S + 2 * T) / P, "Turn 4", "Gas", TurnKind.Corner));
                return o;
            }
            bool prevT = false;
            foreach (var sg in sh.Segments)
            {
                if (!sg.IsTurn) { if (sg.Len > 0.05) prevT = false; continue; }
                if (prevT) continue;
                prevT = true;
                o.Add((sg.BrakeA / sh.Perimeter, sg.Name, "Brake", sg.Kind));
            }
            return o;
        }

        /// <summary>A run's plan for your car and card. Zone centres come from rng.</summary>
        public static QualiPlan Plan(TrackDef t, CarStats s, string card, SimRng rng)
        {
            CardFamily? fam = card != null ? CardBook.Get(card).Family : (CardFamily?)null;
            double wg = SimMath.Clamp(0.05 + 0.009 * (s.Handling - 3) + 0.009 * (s.Consistency - 4), 0.035, 0.16);
            double wp = SimMath.Clamp(0.015 + 0.005 * (s.Racecraft - 4), 0.01, 0.05);
            if (fam == CardFamily.Car) wg *= 1.3;
            if (fam == CardFamily.Pass) wp *= 1.6;
            wp = Math.Min(wp, wg * 0.8);
            var pl = new QualiPlan
            {
                Track = t, Laps = Laps, Card = card, Fam = fam, Wp = wp, Wg = wg, Wo = wg + 0.06,
                Draft = fam == CardFamily.Draft ? (t.DraftBias > 0.5 ? 0.25 : t.DraftBias > 0 ? 0.15 : 0.05) : 0,
                Sweep = SweepOf(t),
            };
            var cs = Corners(t);
            int n = Laps * cs.Count;
            pl.Gap = SimMath.Clamp(RunSec / n - pl.Sweep, 0.75, GapMax);
            var sh = TrackShape.Of(t);
            pl.U0 = sh.UOf(0);
            double at = Roll, uPrev = pl.U0;
            for (int l = 0; l < Laps; l++)
                foreach (var c in cs)
                {
                    double u = c.u + l;
                    while (u <= uPrev) u += 1;
                    double k = c.kind == TurnKind.Hairpin ? 0.8 : c.kind == TurnKind.Sweeper ? 1.2 : t.Id == "talon" ? 1.25 : 1;
                    double w1 = fam == CardFamily.Pit && l == 0 ? 1.25 : 1;
                    pl.Checks.Add(new QualiCheck
                    {
                        Lap = l, Name = c.name, Action = c.action, Kind = c.kind, U = u, U0 = uPrev, T0 = at, S0 = at + pl.Gap, S1 = at + pl.Gap + pl.Sweep,
                        C = 0.58 + rng.NextDouble() * 0.22, Wp = wp * k * w1, Wg = wg * k * w1, Wo = pl.Wo * k * w1,
                    });
                    uPrev = u;
                    at += pl.Gap + pl.Sweep;
                }
            pl.End = at + 1.2;
            pl.UEnd = Math.Ceiling(uPrev - pl.U0 + 1e-9) + pl.U0;
            return pl;
        }

        /// <summary>The mark for a release at needle x (null = never released).</summary>
        public static QualiMark Judge(QualiCheck ch, double? x)
        {
            if (x == null) return QualiMark.Late;
            double d = Math.Abs(x.Value - ch.C);
            return d <= ch.Wp ? QualiMark.Perfect : d <= ch.Wg ? QualiMark.Good : d <= ch.Wo ? QualiMark.Ok : x < ch.C ? QualiMark.Early : QualiMark.Late;
        }

        /// <summary>Scores the run so far (also live): points per check with the chain and the card's rule.</summary>
        public static QualiScore Score(QualiPlan pl)
        {
            var sc = new QualiScore();
            int chain = pl.Fam == CardFamily.Restart ? 2 : 1, best = chain;
            var missUsed = new HashSet<int>();
            foreach (var ch in pl.Checks)
            {
                if (ch.Res == null) break;
                var r = ch.Res.Value;
                ch.Saved = false;
                if (pl.Fam == CardFamily.Defend && (r == QualiMark.Early || r == QualiMark.Late) && missUsed.Add(ch.Lap)) { r = QualiMark.Ok; ch.Saved = true; }
                double p = MarkPts(r);
                if (r == QualiMark.Perfect || r == QualiMark.Good)
                {
                    p *= 1 + ChainStep * (chain - 1);
                    if (pl.Fam == CardFamily.Risk) p *= 1.5;
                    chain = Math.Min(ChainMax, chain + 1);
                }
                else if (r != QualiMark.Ok) { if (pl.Fam == CardFamily.Risk) p *= 2; chain = 1; }
                p += pl.Draft;
                best = Math.Max(best, chain);
                ch.Pts = p; ch.Chain = chain;
                sc.LapPts[ch.Lap] = (sc.LapPts.TryGetValue(ch.Lap, out var lp) ? lp : 0) + p;
                sc.LapN[ch.Lap] = (sc.LapN.TryGetValue(ch.Lap, out var ln) ? ln : 0) + 1;
                sc.Tot += p; sc.N++;
            }
            sc.Avg = sc.N > 0 ? sc.Tot / sc.N : 0;
            sc.Chain = chain; sc.Best = best;
            return sc;
        }

        /// <summary>Pace (a fraction, higher = faster) your run adds from its average points per corner.</summary>
        public static double Delta(double avg) => SkipQ + K * (avg - SkipPts);
        /// <summary>The driver term every car adds, yours and the AI's (step 23: Racecraft and Consistency against 5).</summary>
        public static double Up(CarStats s) => StatK * ((s.Racecraft - 5) + (s.Consistency - 5));
        public static double Lap(TrackDef t, double q) => t.LapTime * (1 - q);
        /// <summary>Your car's lap with an average run.</summary>
        public static double BaseLap(TrackDef t, CarStats s) => Lap(t, s.Rating + Up(s) + SkipQ);

        /// <summary>The grid: every car's car rating and driver term with a little noise, and yours from the run (or the old
        /// roll-off when skipped).</summary>
        public static List<(int id, double time)> Grid(IReadOnlyList<Entrant> field, TrackDef t, double avg, bool skip, SimRng rng)
        {
            var rows = new List<(int id, double q)>();
            foreach (var e in field)
            {
                double q = e.Stats.Rating + Up(e.Stats);
                if (e.IsMe) q += skip ? SkipQ + rng.Gauss() * Noise : Delta(avg) + rng.Gauss() * PlayNoise;
                else q += rng.Gauss() * Noise;
                rows.Add((e.Id, q));
            }
            SimMath.StableSort(rows, (a, b) => b.q.CompareTo(a.q));
            return rows.Select(r => (r.id, Lap(t, r.q))).ToList();
        }

        /// <summary>Up to three different cards from the race deck to pick from.</summary>
        public static List<string> Offer(IReadOnlyList<string> deck, SimRng rng)
        {
            var d = new List<string>(deck);
            rng.Shuffle(d);
            return d.Distinct().Take(3).ToList();
        }

        public static QualiResult Finish(int season, int raceIdx, TrackDef t, IReadOnlyList<Entrant> field, QualiPlan pl, bool skip, SimRng rng)
        {
            var sc = skip ? null : Score(pl);
            var rows = Grid(field, t, sc?.Avg ?? 0, skip, rng);
            int mi = rows.FindIndex(r => field.First(e => e.Id == r.id).IsMe);
            var q = new QualiResult
            {
                Season = season, RaceIdx = raceIdx, TrackId = t.Id, Grid = rows.Select(r => r.id).ToList(), Times = rows.Select(r => Math.Round(r.time, 3)).ToList(),
                Pos = mi + 1, Time = rows[mi].time, Skip = skip, Card = skip ? null : pl?.Card, Avg = sc != null ? Math.Round(sc.Avg, 3) : 0, Best = sc?.Best ?? 0,
            };
            if (!skip) foreach (var ch in pl.Checks) if (ch.Res != null) q.Counts[(int)ch.Res.Value]++;
            return q;
        }
    }

    /// <summary>
    /// A qualifying run being played: call Tick every frame with how long it was and whether brake (Space, A or the BRAKE
    /// button) is held. A release inside a sweep is judged where the needle is; holding through a sweep slows time to 35% for as
    /// long as there is Focus (3 s of it); a sweep that runs out is Late. Done when the car takes the flag.
    /// </summary>
    public sealed class QualiRun
    {
        public readonly QualiPlan Plan;
        public double Time, Focus;
        public bool Held, Done;
        /// <summary>True on the frame time ran slowed.</summary>
        public bool Slowed;
        /// <summary>The last check judged (for the HUD's mark pop).</summary>
        public QualiCheck Last;
        public double LastAt = -9;
        /// <summary>Lap times as laps finish.</summary>
        public readonly List<double> LapTimes = new List<double>();
        readonly CarStats stats;

        public QualiRun(QualiPlan plan, CarStats stats)
        {
            Plan = plan;
            this.stats = stats;
            Focus = plan.Fam == CardFamily.Crew ? 1.5 : 1;
        }

        /// <summary>The check whose needle is next (or sweeping now), or null after the last one.</summary>
        public QualiCheck Current => Plan.Checks.FirstOrDefault(c => c.Res == null && Time < c.S1);
        public bool InSweep { get { var c = Current; return c != null && Time >= c.S0; } }
        /// <summary>The needle's place on the bar (0 to 1) during a sweep.</summary>
        public double Needle { get { var c = Current; return c != null && Time >= c.S0 ? (Time - c.S0) / (c.S1 - c.S0) : 0; } }
        public QualiScore Score => Qualifying.Score(Plan);

        public void Tick(double dt, bool held)
        {
            if (Done) return;
            if (Held && !held)
            {
                var ch = Plan.Checks.FirstOrDefault(c => c.Res == null && Time >= c.S0 && Time < c.S1);
                if (ch != null) { ch.X = (Time - ch.S0) / (ch.S1 - ch.S0); Judged(ch, Qualifying.Judge(ch, ch.X)); }
            }
            Held = held;
            double k = 1;
            Slowed = false;
            if (Held && InSweep && Focus > 0) { k = Qualifying.Slow; Focus = Math.Max(0, Focus - dt / Qualifying.FocusSec); Slowed = true; }
            Time += dt * k;
            foreach (var c in Plan.Checks) if (c.Res == null && Time >= c.S1) { c.X = null; Judged(c, QualiMark.Late); }
            if (Time >= Plan.End) Done = true;
        }

        void Judged(QualiCheck ch, QualiMark m)
        {
            ch.Res = m;
            if (m == QualiMark.Perfect) Focus = Math.Min(1.5, Focus + Qualifying.PerfFocus);
            var sc = Qualifying.Score(Plan);
            Last = ch; LastAt = Time;
            if (Plan.Checks.Where(c => c.Lap == ch.Lap).All(c => c.Res != null))
                LapTimes.Add(Qualifying.Lap(Plan.Track, stats.Rating + Qualifying.Up(stats) + Qualifying.Delta(sc.LapPts[ch.Lap] / sc.LapN[ch.Lap])));
        }

        /// <summary>Where your car is on the track (a lap fraction, counting up) for the map.</summary>
        public double CarU
        {
            get
            {
                var pl = Plan;
                if (Time < Qualifying.Roll) return pl.U0 - Qualifying.RollU * (1 - Time / Qualifying.Roll);
                var c = pl.Checks.FirstOrDefault(x => Time < x.S1);
                if (c == null) { var l = pl.Checks[pl.Checks.Count - 1]; return l.U + (pl.UEnd - l.U) * SimMath.Clamp((Time - l.S1) / (pl.End - l.S1), 0, 1); }
                return c.U0 + (c.U - c.U0) * SimMath.Clamp((Time - c.T0) / (c.S1 - c.T0), 0, 1);
            }
        }
    }

    /// <summary>
    /// Plays a qualifying run through QualiRun.Tick like a person would (tests, the harness and the menu bot): it aims each
    /// release at the zone centre with a timing error of Sigma bar widths, and holds through the sweep for slow-mo (half the
    /// error) while it has Focus for it. Skill names as in the slice's harness: poor, avg, good, great.
    /// </summary>
    public sealed class QualiBot
    {
        public double Sigma;
        readonly SimRng rng;
        QualiCheck aim;
        double target;
        bool slow;

        public QualiBot(string skill, uint seed)
        {
            Sigma = skill == "poor" ? 0.07 : skill == "good" ? 0.028 : skill == "great" ? 0.018 : 0.045;
            rng = new SimRng(seed ^ 0xB07B07u);
        }

        /// <summary>Is brake held this frame?</summary>
        public bool Held(QualiRun run)
        {
            var c = run.Current;
            if (c == null) return false;
            if (aim != c)
            {
                aim = c;
                slow = run.Focus >= 0.6 / Qualifying.FocusSec;
                target = c.C + rng.Gauss() * Sigma * (slow ? 0.5 : 1);
            }
            // hold from the start of the sweep (slow-mo while there is Focus) until the needle reaches the target, then let go
            return run.InSweep && run.Needle < target;
        }

        /// <summary>Runs a whole qualifying lap at 60 frames a second.</summary>
        public QualiRun Play(QualiRun run)
        {
            for (int i = 0; i < 200000 && !run.Done; i++) run.Tick(1 / 60.0, Held(run));
            return run;
        }
    }
}
