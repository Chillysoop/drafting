using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>What the renderer needs for one car this frame.</summary>
    public struct CarPose
    {
        public int Id;
        public bool IsMe, IsPace, Dnf, InPit, Braking;
        public int Team;
        /// <summary>World position on the ground and heading (radians, 0 = +Z, clockwise from above), spin included.</summary>
        public Vec2 P;
        public double Yaw;
        /// <summary>Lane across the track (0 = middle, + = outside wall).</summary>
        public double Lane;
        /// <summary>Track position (fraction of track length) the pose was taken at.</summary>
        public double U;
        /// <summary>Pace car fade (1 = solid). Always 1 for race cars.</summary>
        public double Alpha;
        /// <summary>Seconds since this car last touched the wall or another car (big = not recently).</summary>
        public double SinceHit;

        // step 30: the body on its springs, and what the card outcome looks like
        /// <summary>Body roll in radians, + = the right side goes down (leaning out of a left-hand turn).</summary>
        public double Roll;
        /// <summary>Body pitch in radians, + = nose down (braking, a hit from behind).</summary>
        public double Pitch;
        /// <summary>Height of the car off the ground (track units): a hop off a hard wall hit.</summary>
        public double Lift;
        /// <summary>Tires sliding: a spin, a big wiggle or a wreck (tire smoke and skid marks).</summary>
        public bool Sliding;
        /// <summary>The car is a free body in a wreck the sim has already decided.</summary>
        public bool Wrecking;
        /// <summary>Speed streaks behind the car after a clean move, 0 to 1.</summary>
        public double Streak;
        /// <summary>The card outcome glow: colour and strength 0 to 1 (fades over about a second).</summary>
        public Rgb Glow;
        public double GlowA;
        /// <summary>Damage by zone, 0 to 100 (the sim's DamageZones): crumpled nose and tail, scraped flanks.</summary>
        public double Nose, Side, Tail;
    }

    /// <summary>
    /// Where each car sits across the track and how it is turned: the slice's motionTick (v47) with step 3's card outcome moves
    /// (stageOutcome), sparks, smoke and skid marks, plus what the slice never had: body roll and pitch on springs and wrecks
    /// played out as free bodies that slide, spin, hit the wall and hop. Purely visual: it reads the race and never changes it,
    /// and its rolls come from its own SimRng, so drawing a race (or not) leaves the sim's results and fingerprint untouched.
    /// The physics only ever plays out a crash the sim has already decided, and hands the car back to its sim position after.
    /// </summary>
    public sealed class CarMotion
    {
        // the slice's MV dials
        const double K = 7, Damp = 5.3, MaxLv = 1.4, KHit = 16, DampHit = 6.4, MaxLvHit = 2.4;
        const double YawGain = 1.8, YawRate = 3, YawMax = 0.3, Sep = 0.9, EaseRate = 1.3, RowRate = 1.4;
        // the slice's step 3 card lists
        static readonly HashSet<string> NudgeCards = new HashSet<string> { "bump_draft", "bump_run" };
        static readonly HashSet<string> SwipeOnFail = new HashSet<string> { "dive_bomb", "send_it", "crossover" };
        static readonly HashSet<string> BrushOnFail = new HashSet<string> { "push_limit", "high_line" };
        /// <summary>The slice's DZ.looseAt: a tail this crushed steps out now and then.</summary>
        public const double LooseTailAt = 20;
        /// <summary>A bent nose this bad smokes from the radiator (the slice's 22).</summary>
        public const double SmokyNoseAt = 22;

        /// <summary>Body springs: radians of lean per g, stiffness and damping, and the most a car leans while driving.</summary>
        public const double RollPerG = 0.05, PitchPerG = 0.04, BodyK = 140, BodyDamp = 13, MaxDriveLean = 0.1, MaxLean = 0.55;
        /// <summary>Wreck bodies: tire grip across and along the car (per second), spin decay, how the wall gives back.</summary>
        public const double GripSide = 2.2, GripRoll = 0.35, SpinDecay = 1.1, WallBounce = 0.3, GrassGrip = 1.8;
        /// <summary>A wreck body gives the car back when the sim's spin is over and it has nearly stopped turning, or after this long.</summary>
        public const double WreckMax = 5.5;

        public static readonly Rgb GlowCritSuccess = Rgb.Hex("#7dffb0"), GlowSuccess = Rgb.Hex("#58d68d"), GlowNeutral = Rgb.Hex("#ffd666"), GlowFail = Rgb.Hex("#ff7b7f");

        sealed class Wig { public double T0, Amp, Dur, F; public bool Hit; }
        sealed class Sbs { public RaceCar Tg; public double Until, SwipeAt = double.NaN; }
        sealed class Nudge { public RaceCar Tg; public double Until, Str; public bool Hit; }

        sealed class Vis
        {
            public double Lane, LaneT, Lv, Line, LineAt, DOff, Ease, Yaw, HitAt = -9, PrevSpinning, Lane0 = double.NaN;
            public bool Row;
            public double SpinT0 = double.NaN, SpinDur, SpinTotal, SpinLane;
            // step 3 outcome moves
            public double Side = -1, Cover = -9, Brush = -9, Streak = -9, StreakLen = 1, Glow = -9, EngSmoke = -9, SmokeAt, LooseAt;
            public Rgb GlowCol;
            public Wig Wig;
            public Sbs Sbs;
            public Nudge Nudge;
            public bool HaveSkid;
            public Vec2 Skid;
            // body springs
            public double Roll, RollV, Pitch, PitchV, Lift, LiftV, SpeedS = double.NaN;
            // a wreck: free body in the track frame (along offset S and speed Va in track units, lateral speed in Lv, heading Psi)
            public bool Free;
            public double FreeT0, S, Va, Psi, Om, Kick;
            public RaceCar Partner;
            /// <summary>After a wreck: the heading left over, eased back to straight from RecT0 (a parked wreck keeps it).</summary>
            public double YawRec, RecT0 = double.NaN, ParkYaw, Park;
        }

        readonly Race race;
        readonly TrackLayout layout;
        readonly SimRng rng;
        readonly Dictionary<RaceCar, Vis> vis = new Dictionary<RaceCar, Vis>();
        readonly List<(RaceCar c, double u)> list = new List<(RaceCar, double)>();
        readonly Dictionary<RaceCar, RaceCar> being = new Dictionary<RaceCar, RaceCar>();
        readonly Dictionary<int, RaceCar> byId = new Dictionary<int, RaceCar>();
        RaceCar pace;
        double paceLane0 = double.NaN;

        /// <summary>The time the motion has been stepped to (sim seconds).</summary>
        public double T { get; private set; }
        /// <summary>Sparks, smoke and skid marks.</summary>
        public readonly Fx Fx;
        /// <summary>Wrecks played out as free bodies so far (tests and the frames page).</summary>
        public int WreckBodies { get; private set; }
        /// <summary>Each car's interpolated race distance for this frame (the driver sets it; events use it between steps).</summary>
        public Func<RaceCar, double> Dist;

        public CarMotion(Race race, TrackLayout layout)
        {
            this.race = race;
            this.layout = layout;
            rng = new SimRng(race.Seed ^ 0x5EED_F1A6u);
            for (int i = 0; i < race.Cars.Count; i++)
            {
                var c = race.Cars[i];
                // the slice's grid: odd cars on the outside, everyone on a slightly different preferred line
                vis[c] = new Vis { Lane = i % 2 == 1 ? 0.5 : -0.5, Line = rng.Rand(-0.4, 0.4) };
                byId[c.Id] = c;
            }
            Fx = new Fx(new SimRng(race.Seed ^ 0xF1A5_0B05u));
            Dist = c => c.Dist;
            T = race.T;
        }

        Vis V(RaceCar c)
        {
            if (!vis.TryGetValue(c, out var v)) vis[c] = v = new Vis { Line = rng.Rand(-0.4, 0.4) };
            return v;
        }

        RaceCar Car(int id) => id >= 0 && byId.TryGetValue(id, out var c) ? c : null;

        /// <summary>
        /// A car's distance jumped this tick (the caution line-up, pit stall snaps, lucky dogs): absorb the jump into a visual
        /// offset that eases away, so the car slides into its new spot instead of teleporting.
        /// </summary>
        public void Jumped(RaceCar c, double by, bool pit)
        {
            var v = V(c);
            v.DOff -= by;
            v.Ease = T + (pit ? 4 : 2.5);
        }

        /// <summary>Visual race distance: the sim's (interpolated) distance plus the easing offset, and a wreck's slide.</summary>
        public double VisDist(RaceCar c, double dist)
        {
            if (!vis.TryGetValue(c, out var v)) return dist;
            return dist + v.DOff + v.Park + (v.Free ? v.S / layout.Perimeter : 0);
        }

        /// <summary>
        /// A sim event, as it happens: card outcomes stage their move on track (the slice's stagePre and stageOutcome), the AI's
        /// small moves that came to nothing wiggle or pull alongside, blocks cover, and wrecks pair up the cars that touched.
        /// </summary>
        public void OnEvent(RaceEvent e)
        {
            var car = Car(e.Car);
            if (car == null) return;
            var v = V(car);
            switch (e.Kind)
            {
                case RaceEventKind.CardPlayed:
                    v.Side = MoveId(e.Card) == "high_line" ? 1 : -1;
                    break;
                case RaceEventKind.CardReturned:
                    v.Glow = T + 1.1; v.GlowCol = GlowNeutral;
                    break;
                case RaceEventKind.CardResolved:
                    if (e.Tier >= 0 && CardBook.Exists(e.Card)) StageOutcome((Tier)e.Tier, CardBook.Get(e.Card), car);
                    break;
                case RaceEventKind.AutoLift:
                    Wiggle(car, 0.12, 0.9, false);
                    break;
                case RaceEventKind.AutoAlongside:
                    var tg = Car(e.Other);
                    if (tg != null && rng.NextDouble() < 0.6) v.Sbs = new Sbs { Tg = tg, Until = T + 2.4 };
                    break;
                case RaceEventKind.Block:
                    v.Cover = T + 2.5;
                    break;
                case RaceEventKind.Wreck:
                    var o = Car(e.Other);
                    if (o != null) { v.Partner = o; V(o).Partner = car; }
                    break;
            }
        }

        /// <summary>Kart cards move like the stock card they stand in for.</summary>
        static string MoveId(string id) => id != null && CardBook.KartToStock.TryGetValue(id, out var s) ? s : id;

        /// <summary>The slice's stageOutcome: how each card outcome plays out on track. The sim effect has already been applied.</summary>
        void StageOutcome(Tier tier, CardDef cd, RaceCar car)
        {
            var v = V(car);
            string id = MoveId(cd.Id);
            var tg = cd.Family == CardFamily.Defend ? race.BehindOf(car) : race.AheadOf(car);
            v.Glow = T + 1.1;
            v.GlowCol = tier == Tier.CritSuccess ? GlowCritSuccess : tier == Tier.Success ? GlowSuccess : tier == Tier.Neutral ? GlowNeutral : GlowFail;
            bool attack = cd.Family == CardFamily.Pass || cd.Family == CardFamily.Draft || cd.Family == CardFamily.Risk || cd.Family == CardFamily.Restart;
            switch (tier)
            {
                case Tier.CritSuccess:
                case Tier.Success:
                    if (attack || cd.Family == CardFamily.Car) { v.StreakLen = tier == Tier.CritSuccess ? 2.6 : 1.8; v.Streak = T + v.StreakLen; }
                    if (NudgeCards.Contains(id) && tg != null) StartNudge(car, tg, tier == Tier.CritSuccess ? 1 : 0.7);
                    if (cd.Family == CardFamily.Defend && race.BehindOf(car) != null) v.Cover = T + 3;
                    break;
                case Tier.Neutral:
                    if (tg != null && (cd.Family == CardFamily.Pass || cd.Family == CardFamily.Risk)) v.Sbs = new Sbs { Tg = tg, Until = T + 3.2 };
                    else if (cd.Family == CardFamily.Draft && tg != null) { v.StreakLen = 0.8; v.Streak = T + 0.8; }
                    break;
                case Tier.Fail:
                    if (NudgeCards.Contains(id) && tg != null) StartNudge(car, tg, 0.5);
                    else if (SwipeOnFail.Contains(id) && tg != null) v.Sbs = new Sbs { Tg = tg, Until = T + 1.6, SwipeAt = T + 0.6 };
                    if (BrushOnFail.Contains(id)) v.Brush = T + 1.3;
                    Wiggle(car, 0.22, 2.2, false); v.Lv += 0.6;
                    if (id == "tape_grille" || id == "evade") v.EngSmoke = T + 4;
                    break;
                case Tier.CritFail:
                    // a wrecking card has already spun the car (the wreck body plays it); the rest smoke and fishtail
                    if (car.Spinning <= 0) { Wiggle(car, 0.45, 2, false); v.EngSmoke = T + 8; }
                    break;
            }
        }

        void StartNudge(RaceCar c, RaceCar tg, double str) => V(c).Nudge = new Nudge { Tg = tg, Until = T + 0.9, Str = str };

        /// <summary>The slice's wiggle(): the rear steps out and the car catches it (hit = a sharp kick from contact).</summary>
        void Wiggle(RaceCar c, double amp, double dur, bool hit)
        {
            if (c == null || c.Dnf) return;
            var v = V(c);
            if (v.Free) return;
            v.Wig = new Wig { T0 = T, Amp = amp * (rng.NextDouble() < 0.5 ? -1 : 1), Dur = dur, F = hit ? rng.Rand(1.8, 2.4) : rng.Rand(0.9, 1.2), Hit = hit };
        }

        /// <summary>Steps the motion by dt sim seconds. dist(c) gives each car's interpolated race distance for this frame.</summary>
        public void Step(double dt, Func<RaceCar, double> dist)
        {
            if (dt <= 0) return;
            Dist = dist;
            // the springs are integrated explicitly: keep each step short, or a hitch at 8x would fling cars off the track
            int n = (int)Math.Ceiling(dt / MaxStep);
            for (int i = 0; i < n; i++) StepOnce(dt / n, dist);
        }

        /// <summary>The longest single motion step (sim seconds).</summary>
        public const double MaxStep = 1.0 / 60;

        void StepOnce(double dt, Func<RaceCar, double> dist)
        {
            T += dt;
            var r = race;
            double P = layout.Perimeter, t = T;

            being.Clear();
            foreach (var c in r.Cars) if (c.Overtake != null && !c.Dnf && c.Overtake.Target != null) being[c.Overtake.Target] = c;
            bool restart2 = r.Phase == RacePhase.Start || (r.Phase == RacePhase.Restart && r.PhaseT < 3);
            var att = r.Attempt;

            // 1) where each car wants to be across the track
            for (int i = 0; i < r.Order.Count; i++)
            {
                var c = r.Order[i];
                var v = V(c);
                double vd = VisDist(c, dist(c)), u = layout.Shape.UOf(vd);
                if (t > v.LineAt) { v.Line = SimMath.Clamp(v.Line + rng.Rand(-0.35, 0.35), -0.5, 0.5); v.LineAt = t + rng.Rand(7, 14); }
                var a = i > 0 ? r.Order[i - 1] : null;
                double cl = layout.CornerLine(u), lt = v.Line + cl;
                bool tucked = a != null && !a.Dnf && r.GapSec(a, c) < 0.5;
                if (tucked) lt = V(a).Lane;                                   // tuck into the draft, single file
                if (r.Phase == RacePhase.Caution && !tucked) lt = cl * 0.5;
                v.Row = restart2 && i % 2 == 1 && a != null && !a.Dnf;
                if (restart2) lt = i % 2 == 1 ? 0.5 : -0.5;                   // double file at the start and restarts
                foreach (var e in c.Effects) if (e.Yield) { lt = 0.85; break; }
                if (being.TryGetValue(c, out var passer)) lt = -V(passer).Side * 0.45 + cl * 0.3;   // the passer takes one side, this car the other
                if (c.Overtake != null) lt = v.Side * 0.75 + cl * 0.3;
                // telegraph: while your card sets up, the car pulls out to look (or moves over to cover)
                if (c.IsMe && att != null && att.Target != null && !att.Target.Dnf)
                {
                    var f = att.Def.Family;
                    if (f == CardFamily.Defend) lt = V(att.Target).Lane;
                    else if (f == CardFamily.Pass || f == CardFamily.Draft || f == CardFamily.Risk) lt = V(att.Target).Lane + v.Side * 0.5;
                }
                if (v.Cover > t) { var b = r.BehindOf(c); if (b != null) lt = V(b).Lane; }
                if (v.Sbs != null)
                {
                    var tg = v.Sbs.Tg;
                    if (t > v.Sbs.Until || tg.Dnf || c.Dnf) v.Sbs = null;
                    else { lt = V(tg).Lane + v.Side * Sep * 1.05; if (t > v.Sbs.SwipeAt) lt = V(tg).Lane + v.Side * 0.2; }
                }
                if (v.Brush > t) lt = TrackLayout.WallLane;
                // spin: starts when the sim sets Spinning; a wreck's spin is a free body, a lighter one is scripted
                if (c.Spinning > 0 && v.PrevSpinning <= 0 && c.Pit == null)
                {
                    if (c.Spinning >= 2) StartWreck(c, v, dist);
                    else if (!c.Dnf) StartSpin(c, v, c.Spinning);
                }
                v.PrevSpinning = c.Spinning;
                if (!double.IsNaN(v.SpinT0) && t - v.SpinT0 < v.SpinDur) lt = v.SpinLane;
                if (c.Dnf) lt = TrackLayout.DnfLane;
                if (c.Pit != null)
                {
                    if (v.Free) Release(c, v);
                    if (double.IsNaN(v.Lane0)) v.Lane0 = v.Lane;
                    lt = PitLaneOf(c, v.Lane0);
                }
                else v.Lane0 = double.NaN;
                v.LaneT = lt;
            }

            // 2) cars that overlap nose to tail go side by side instead
            list.Clear();
            foreach (var c in r.Cars) if (!c.Dnf && c.Pit == null) list.Add((c, layout.Shape.UOf(VisDist(c, dist(c)))));
            list.Sort((x, y) => x.u.CompareTo(y.u));
            double near = TrackLayout.CarL * 1.05 / P;
            for (int i = 0; i < list.Count; i++)
            {
                var A = list[i].c; var B = list[(i + 1) % list.Count].c;
                double du = list[(i + 1) % list.Count].u - list[i].u; if (du < 0) du += 1;
                var va = V(A); var vb = V(B);
                if (A != B && du < near && Math.Abs(va.LaneT - vb.LaneT) < Sep)
                {
                    double m = (va.LaneT + vb.LaneT) / 2, sg = va.Lane <= vb.Lane ? 1 : -1;
                    va.LaneT = m - sg * Sep / 2; vb.LaneT = m + sg * Sep / 2;
                }
            }

            // 3) integrate: lateral spring (or a wreck's free body), longitudinal easing, yaw, body lean
            foreach (var c in r.Cars)
            {
                var v = V(c);
                double lv0 = v.Lv;
                if (v.Free) StepWreck(c, v, dt, dist);
                else
                {
                    bool spinning = !double.IsNaN(v.SpinT0) && t - v.SpinT0 < v.SpinDur;
                    bool hard = c.Dnf || spinning || t - v.HitAt < 0.6 || (v.Wig != null && v.Wig.Hit) || (v.Nudge != null && !v.Nudge.Hit);
                    double k = hard ? KHit : K, dmp = hard ? DampHit : Damp, ml = hard ? MaxLvHit : MaxLv;
                    // a parked wreck is towed in slowly
                    if (c.Dnf && !double.IsNaN(v.RecT0)) ml = 0.8;
                    double acc = k * (v.LaneT - v.Lane) - dmp * v.Lv;
                    v.Lv = SimMath.Clamp(v.Lv + acc * dt, -ml * (c.Dnf ? 1.5 : 1), ml);
                    v.Lane += v.Lv * dt;
                    if (!c.Dnf)
                    {
                        if (v.Lane > TrackLayout.WallLane)
                        {
                            if (v.Lv > 0.5 && t - v.HitAt > 0.35)
                            {
                                var q = Tan(c, dist);
                                Fx.Burst(Side(q, TrackLayout.CarW / 2), spinning ? 16 : 9, q.T, Speed(c));
                                v.HitAt = t; Wiggle(c, 0.1, 0.6, true);
                                v.RollV -= v.Lv * 0.8;
                            }
                            v.Lane = TrackLayout.WallLane; v.Lv = -Math.Abs(v.Lv) * 0.25;
                        }
                        if (c.Pit != null) { v.Lv = SimMath.Clamp((v.LaneT - v.Lane) / dt * 0.5, -MaxLv, MaxLv); v.Lane = v.LaneT; }
                        else if (v.Lane < -2.2) { v.Lane = -2.2; v.Lv = Math.Max(0, v.Lv); }
                    }
                }
                double dT = 0, rate = RowRate;
                if (v.Ease > t) rate = EaseRate;
                if (v.Row) { var a = r.AheadOf(c); if (a != null) dT = SimMath.Clamp((dist(a) - dist(c)) * 0.95, 0, TrackLayout.CarL * 2 / P); }
                if (v.Sbs != null) dT = SimMath.Clamp(dist(v.Sbs.Tg) - dist(c) - TrackLayout.CarL * 0.45 / P, 0, TrackLayout.CarL * 1.4 / P);
                if (v.Nudge != null) StepNudge(c, v, dist, ref dT, ref rate);
                v.DOff += (dT - v.DOff) * Math.Min(1, dt * rate);
                if (!(v.Ease > t) && Math.Abs(v.DOff) < 1e-6) v.DOff = 0;
                double sp = Math.Max(0.02, c.Speed * P);
                double slip = SimMath.Clamp(Math.Atan2(v.Lv * TrackLayout.LaneW, sp) * YawGain, -YawMax, YawMax);
                v.Yaw += ((v.Free ? 0 : slip) - v.Yaw) * Math.Min(1, dt * YawRate);
                StepBody(c, v, dt, (v.Lv - lv0) / dt, dist);
                Smoke(c, v, dist);
            }

            // 4) contact: cars never overlap; closing hard is a bump, sparks and a bounce
            for (int i = 0; i < list.Count; i++)
                for (int j = 1; j <= 2 && j < list.Count; j++)
                {
                    var A = list[i].c; var B = list[(i + j) % list.Count].c;
                    double du = list[(i + j) % list.Count].u - list[i].u; if (du < 0) du += 1;
                    if (A == B || du * P >= TrackLayout.CarL * 0.98) continue;
                    var va = V(A); var vb = V(B);
                    double dl = vb.Lane - va.Lane, ad = Math.Abs(dl);
                    if (ad >= Sep) continue;
                    double sg = dl >= 0 ? 1 : -1, pf = (A.Spinning > 0 || B.Spinning > 0) ? 1 : (ad < Sep * 0.4 ? 0.45 : 0.3), push = (Sep - ad) / 2 * pf;
                    va.Lane -= sg * push; vb.Lane += sg * push;
                    double closing = (vb.Lv - va.Lv) * sg;
                    if (closing < 0)
                    {
                        double jv = -closing * 0.6; va.Lv -= sg * jv; vb.Lv += sg * jv;
                        if (-closing > 0.45 && t - Math.Max(va.HitAt, vb.HitAt) > 0.5)
                        {
                            Contact(A, B, (int)Math.Round(4 - closing * 5), dist);
                            if (A.Spinning <= 0) Wiggle(A, 0.08, 0.7, true);
                            if (B.Spinning <= 0) Wiggle(B, 0.08, 0.7, true);
                            // door to door: both bodies rock away from the hit
                            va.RollV += sg * closing * 0.5; vb.RollV -= sg * closing * 0.5;
                        }
                    }
                }
            foreach (var (c, _) in list) { var v = V(c); v.Lane = SimMath.Clamp(v.Lane, -2.2, TrackLayout.WallLane); }

            // a crushed tail steps out now and then (the slice checks twice a second under green)
            if (r.Phase == RacePhase.Green)
                foreach (var c in r.Cars)
                {
                    var v = V(c);
                    if (c.Dnf || c.Pit != null || c.Spinning > 0 || v.Free || t < v.LooseAt) continue;
                    v.LooseAt = t + 0.5;
                    double tail = c.Zones.Tail;
                    if (tail >= LooseTailAt && v.Wig == null && double.IsNaN(v.SpinT0) && rng.NextDouble() < 0.06 + tail * 0.002)
                        Wiggle(c, 0.07 + Math.Min(0.12, tail * 0.002), 1.4, false);
                }

            // 5) particles
            Fx.Step(dt, t);

            // the pace car: runs the low groove, then peels down pit road and fades out
            if (r.PaceCar != pace) { pace = r.PaceCar; paceLane0 = double.NaN; }
        }

        void StepNudge(RaceCar c, Vis v, Func<RaceCar, double> dist, ref double dT, ref double rate)
        {
            var n = v.Nudge; var tg = n.Tg; double P = layout.Perimeter;
            if (T > n.Until || tg.Dnf) { v.Nudge = null; return; }
            if (n.Hit) return;
            dT = SimMath.Clamp(dist(tg) - dist(c) - TrackLayout.CarL * 1.0 / P, 0, TrackLayout.CarL * 1.2 / P);
            rate = 6;
            var vt = V(tg);
            if (VisDist(tg, dist(tg)) - VisDist(c, dist(c)) <= TrackLayout.CarL * 1.04 / P && Math.Abs(vt.Lane - v.Lane) < Sep)
            {
                n.Hit = true;
                Contact(c, tg, (int)(6 + n.Str * 8), dist);
                Wiggle(tg, 0.14 * n.Str + 0.04, 1.1, true);
                vt.Lv += (rng.NextDouble() < 0.5 ? -1 : 1) * 0.6 * n.Str;
                // the bumper tap: your nose dips, his tail kicks up
                v.PitchV += 0.6 * n.Str; vt.PitchV -= 0.8 * n.Str;
            }
        }

        // ---------------------------------------------------------------- body lean

        /// <summary>
        /// Roll and pitch on a damped spring toward what the car is doing: leaning out of the turn and into a lane change, diving
        /// under braking and squatting on the throttle. Contact and wall hits kick the springs directly; a hop falls back down.
        /// </summary>
        void StepBody(RaceCar c, Vis v, double dt, double laneAcc, Func<RaceCar, double> dist)
        {
            double P = layout.Perimeter, rollT = 0, pitchT = 0;
            double spd = c.Speed * P;
            if (double.IsNaN(v.SpeedS)) v.SpeedS = spd;
            double s0 = v.SpeedS;
            v.SpeedS += (spd - v.SpeedS) * Math.Min(1, dt * 4);
            if (!c.Dnf && c.Pit == null && !v.Free)
            {
                // the turn: heading change per unit of track at this spot, times speed squared, plus the lane change
                double u = layout.Shape.UOf(VisDist(c, dist(c)));
                double du = 0.004, y0 = layout.At(u - du, v.Lane).Yaw, y1 = layout.At(u + du, v.Lane).Yaw;
                double kap = WrapPi(y1 - y0) / (2 * du * P);
                double latG = (v.SpeedS * v.SpeedS * kap + laneAcc * TrackLayout.LaneW) / Fx.G;
                rollT = SimMath.Clamp(-latG * RollPerG, -MaxDriveLean, MaxDriveLean);
                double lonG = (v.SpeedS - s0) / Math.Max(1e-6, dt) / Fx.G;
                pitchT = SimMath.Clamp(-lonG * PitchPerG, -MaxDriveLean * 0.6, MaxDriveLean * 0.6);
                if (layout.BrakeZone(u)) pitchT += 0.02;
            }
            v.RollV += (BodyK * (rollT - v.Roll) - BodyDamp * v.RollV) * dt;
            v.Roll = SimMath.Clamp(v.Roll + v.RollV * dt, -MaxLean, MaxLean);
            v.PitchV += (BodyK * (pitchT - v.Pitch) - BodyDamp * v.PitchV) * dt;
            v.Pitch = SimMath.Clamp(v.Pitch + v.PitchV * dt, -MaxLean * 0.6, MaxLean * 0.6);
            if (v.Lift > 0 || v.LiftV > 0)
            {
                v.LiftV -= Fx.G * dt;
                v.Lift += v.LiftV * dt;
                if (v.Lift <= 0)
                {
                    // landing: the suspension takes it and the body bounces once
                    v.Lift = 0; v.PitchV += v.LiftV * 4; v.RollV -= v.LiftV * 3 * (v.Roll >= 0 ? 1 : -1);
                    v.LiftV = v.LiftV < -0.02 ? -v.LiftV * 0.25 : 0;
                }
            }
        }

        // ---------------------------------------------------------------- wrecks

        /// <summary>
        /// A wreck the sim has decided (Spinning 2 s or more): the car comes off its lane spring and becomes a free body that keeps
        /// its speed, spins, slides on its tires, hits the wall and bounces, and gets the cars it touched pushed apart.
        /// </summary>
        void StartWreck(RaceCar c, Vis v, Func<RaceCar, double> dist)
        {
            double P = layout.Perimeter, v0 = Math.Max(c.Speed * P, v.SpeedS > 0 ? v.SpeedS : 0);
            if (v0 <= 1e-4) v0 = layout.Perimeter / Math.Max(10, race.Track.LapTime);
            v.Free = true; v.FreeT0 = T; WreckBodies++;
            v.S = 0; v.Va = v0; v.Psi = v.Yaw; v.Wig = null; v.SpinT0 = double.NaN; v.Sbs = null; v.Nudge = null;
            v.Om = (rng.NextDouble() < 0.8 ? -1 : 1) * rng.Rand(3.5, 6.5);
            double kick;
            var o = v.Partner;
            // two cars that touched go opposite ways: the second one to start takes the other side of the first one's kick
            if (o != null && V(o).Free && V(o).Kick != 0) kick = -Math.Sign(V(o).Kick) * rng.Rand(0.25, 0.45);
            else if (o != null) kick = (v.Lane >= V(o).Lane ? 1 : -1) * rng.Rand(0.25, 0.45);
            else kick = rng.NextDouble() < 0.7 ? rng.Rand(0.2, 0.45) : -rng.Rand(0.1, 0.3);   // most single-car spins wash up the track
            v.Kick = kick;
            v.Lv += kick * v0 / TrackLayout.LaneW;
            v.Om += -kick * 6;
            if (o != null && V(o).Free) Contact(c, o, 14, dist);
            v.RollV += -kick * 2.5;
            v.PitchV += 0.8;
        }

        void StepWreck(RaceCar c, Vis v, double dt, Func<RaceCar, double> dist)
        {
            double P = layout.Perimeter, simV = c.Dnf ? 0 : c.Speed * P;
            bool grass = v.Lane < TrackLayout.ApronLane;
            double grip = grass ? GrassGrip : 1;
            double cs = Math.Cos(v.Psi), sn = Math.Sin(v.Psi);
            double vl = v.Lv * TrackLayout.LaneW;
            double fwd = v.Va * cs + vl * sn, side = -v.Va * sn + vl * cs;
            side *= Math.Exp(-GripSide * grip * dt);
            fwd *= Math.Exp(-GripRoll * grip * (1 + 1.5 * Math.Abs(sn)) * dt);
            v.Va = fwd * cs - side * sn; vl = fwd * sn + side * cs;
            v.Om *= Math.Exp(-SpinDecay * grip * dt);
            v.Psi += v.Om * dt;
            v.Lane += vl / TrackLayout.LaneW * dt;
            v.S += (v.Va - simV) * dt;
            // the outside wall
            if (v.Lane > TrackLayout.WallLane && vl > 0)
            {
                double imp = vl;
                v.Lane = TrackLayout.WallLane; vl = -vl * WallBounce; v.Va *= 0.85;
                v.Om += -Math.Sign(Math.Sin(2 * v.Psi) + 1e-9) * imp * 40;
                var q = Tan(c, dist);
                Fx.Burst(Side(q, TrackLayout.CarW / 2), 10 + (int)(imp * 120), q.T, Math.Max(0.1, v.Va));
                v.RollV -= imp * 18;
                if (imp > 0.12 && v.Lift <= 0) v.LiftV = imp * 0.35;
                v.HitAt = T;
            }
            // the inside: the grass slows it hard, the apron edge is as far as it goes
            if (v.Lane < -2.2) { v.Lane = -2.2; vl = Math.Max(0, vl); }
            v.Lv = vl / TrackLayout.LaneW;
            v.S = SimMath.Clamp(v.S, -TrackLayout.CarL * 8, TrackLayout.CarL * 3);
            double age = T - v.FreeT0;
            if (((c.Spinning <= 0 || c.Dnf) && Math.Abs(v.Om) < 1.2 && age > 1.2 && Math.Abs(v.Va - simV) < 0.25 * Math.Max(0.05, simV)) || age > WreckMax
                || (c.Dnf && Math.Abs(v.Om) < 0.4 && Math.Abs(v.Va) < 0.02))
                Release(c, v);
        }

        /// <summary>The wreck is over: hand the car back to its lane spring and sim position, easing out the slide and the heading.</summary>
        void Release(RaceCar c, Vis v)
        {
            if (!v.Free) return;
            v.Free = false;
            double P = layout.Perimeter;
            if (c.Dnf) { v.Park += v.S / P; v.ParkYaw = WrapPi(v.Psi); }
            else { v.DOff += v.S / P; v.Ease = T + 2.5; v.YawRec = WrapPi(v.Psi); }
            v.RecT0 = T;
            v.S = 0; v.Yaw = 0;
            v.Lv = SimMath.Clamp(v.Lv, -MaxLvHit, MaxLvHit);
            v.Partner = null; v.Kick = 0;
        }

        /// <summary>The heading a car still carries from a wreck: eases back to straight in 0.9 s; a parked wreck keeps its angle.</summary>
        double RecoverYaw(RaceCar c, Vis v)
        {
            if (v.Free) return WrapPi(v.Psi);
            if (c.Dnf) return v.ParkYaw;
            if (double.IsNaN(v.RecT0)) return 0;
            double x = (T - v.RecT0) / 0.9;
            if (x >= 1) { v.YawRec = 0; return 0; }
            return v.YawRec * (1 - TrackLayout.Smooth(x));
        }

        // ---------------------------------------------------------------- smoke, skids, sparks

        void Smoke(RaceCar c, Vis v, Func<RaceCar, double> dist)
        {
            if (c.Dnf && !v.Free) { v.HaveSkid = false; return; }
            bool sliding = Sliding(v);
            var q = sliding || v.EngSmoke > T || c.Zones.Nose >= SmokyNoseAt || (v.Free && v.Lane < TrackLayout.ApronLane) ? Tan(c, dist) : default;
            if ((sliding || v.EngSmoke > T) && T > v.SmokeAt)
            {
                v.SmokeAt = T + (sliding ? 0.08 : 0.2);
                var back = q.P - q.T * (TrackLayout.CarL * 0.4);
                if (sliding) Fx.Smoke(back, v.Free && v.Lane < TrackLayout.ApronLane ? PuffKind.Dust : PuffKind.Tire, rng.Rand(0.5, 0.9), rng.Rand(0.01, 0.018), 0.5);
                else Fx.Smoke(back, PuffKind.Engine, rng.Rand(1, 1.6), rng.Rand(0.008, 0.015));
            }
            // radiator smoke off a bent nose (the slice rolls this every frame; here every step, at its 60 fps rate)
            if (c.Zones.Nose >= SmokyNoseAt && c.Pit == null && rng.NextDouble() < Math.Min(0.35, c.Zones.Nose * 0.008))
                Fx.Smoke(q.P + q.T * (TrackLayout.CarL * 0.55), PuffKind.Engine, rng.Rand(0.5, 1.0), rng.Rand(0.006, 0.012));
            if (sliding && c.Pit == null)
            {
                if (v.HaveSkid) Fx.Mark(v.Skid, q.P, T);
                v.Skid = q.P; v.HaveSkid = true;
            }
            else v.HaveSkid = false;
        }

        bool Sliding(Vis v) =>
            v.Free && (Math.Abs(v.Om) > 0.8 || Math.Abs(v.Lv) > 0.4)
            || (!double.IsNaN(v.SpinT0) && T - v.SpinT0 < v.SpinDur)
            || (v.Wig != null && T - v.Wig.T0 < v.Wig.Dur * 0.5 && Math.Abs(v.Wig.Amp) > 0.2);

        /// <summary>The slice's contactFx: sparks between two cars, both marked as just hit.</summary>
        void Contact(RaceCar a, RaceCar b, int n, Func<RaceCar, double> dist)
        {
            if (a == null || b == null) return;
            var qa = Tan(a, dist); var qb = Tan(b, dist);
            Fx.Burst((qa.P + qb.P) * 0.5, Math.Max(1, n), qa.T, Speed(a));
            V(a).HitAt = V(b).HitAt = T;
        }

        TrackPoint Tan(RaceCar c, Func<RaceCar, double> dist) => layout.At(layout.Shape.UOf(VisDist(c, dist(c))), V(c).Lane);
        static Vec2 Side(TrackPoint q, double off) => q.P + q.N * off;
        double Speed(RaceCar c) => layout.Perimeter / Math.Max(1, race.Track.LapTime) * (c.Speed > 0 ? 1 : 0.5);

        void StartSpin(RaceCar c, Vis v, double secs)
        {
            double hard = secs >= 2 ? 1 : 0.5, dir = rng.NextDouble() < 0.8 ? -1 : 1;
            v.SpinT0 = T; v.SpinDur = secs * 0.85;
            v.SpinTotal = dir * rng.Rand(Math.PI * 0.9, Math.PI * 1.9) * (0.6 + hard * 0.4);
            v.SpinLane = hard >= 1 ? TrackLayout.WallLane : rng.Rand(0.2, 1.0);
            v.Lv += 1.2 * hard;
            v.Wig = null;
        }

        /// <summary>The slice's pitLane(c): from the racing lane down to pit road, into the stall, and back out.</summary>
        double PitLaneOf(RaceCar c, double lane0)
        {
            var g = layout.Shape.Pit; var p = c.Pit;
            double s = TrackLayout.SOf(c.Dist);
            if (p.Stage == PitStage.In)
            {
                if (s < g.E1) return lane0 + (TrackLayout.PitLane - lane0) * TrackLayout.Smooth((s - g.E0) / (g.E1 - g.E0));
                return TrackLayout.PitLane + (TrackLayout.StallLane - TrackLayout.PitLane) * TrackLayout.Smooth(1 - (c.Stall - s) / 0.012);
            }
            if (p.Stage == PitStage.Box) return TrackLayout.StallLane;
            if (s < g.X0) return TrackLayout.StallLane + (TrackLayout.PitLane - TrackLayout.StallLane) * TrackLayout.Smooth((s - c.Stall) / 0.012);
            return TrackLayout.PitLane + (TrackLayout.PitExitLane - TrackLayout.PitLane) * TrackLayout.Smooth((s - g.X0) / (g.X1 - g.X0));
        }

        /// <summary>The car's spin angle right now (0 when it is not spinning), with the slice's recovery back to straight.</summary>
        double SpinAngle(Vis v)
        {
            if (double.IsNaN(v.SpinT0)) return 0;
            double age = T - v.SpinT0, x = SimMath.Clamp(age / v.SpinDur, 0, 1);
            double sa = v.SpinTotal * (1 - Math.Pow(1 - x, 2.2));
            if (x >= 1)
            {
                double rec = SimMath.Clamp((age - v.SpinDur) / 0.9, 0, 1), tgt = v.SpinTotal - WrapPi(v.SpinTotal);
                sa = v.SpinTotal + (tgt - v.SpinTotal) * TrackLayout.Smooth(rec);
                if (rec >= 1) { v.SpinT0 = double.NaN; sa = 0; }
            }
            return WrapPi(sa);
        }

        /// <summary>The wiggle's yaw right now: a decaying sway of the rear.</summary>
        double WiggleAngle(Vis v)
        {
            if (v.Wig == null) return 0;
            double age = T - v.Wig.T0;
            if (age > v.Wig.Dur) { v.Wig = null; return 0; }
            return v.Wig.Amp * Math.Exp(-2.6 * age / v.Wig.Dur) * Math.Sin(age * Math.PI * 2 * v.Wig.F);
        }

        static double WrapPi(double a) => a - Math.PI * 2 * Math.Round(a / (Math.PI * 2));

        /// <summary>Every race car's pose (in race order), then the pace car if it is out.</summary>
        public void Poses(Func<RaceCar, double> dist, List<CarPose> into)
        {
            into.Clear();
            foreach (var c in race.Order)
            {
                var v = V(c);
                double vd = VisDist(c, dist(c)), u = layout.Shape.UOf(vd);
                var q = layout.At(u, v.Lane);
                // the heading follows the lane the car is on (pit road bends), plus its slip, a wiggle, a spin or a wreck
                var q2 = layout.At(u + 0.002, v.Lane);
                double yaw = Math.Atan2(q2.P.X - q.P.X, q2.P.Z - q.P.Z) + v.Yaw + WiggleAngle(v) + SpinAngle(v) + RecoverYaw(c, v);
                double glowA = v.Glow > T ? Math.Min(1, (v.Glow - T) / 0.4) : 0;
                into.Add(new CarPose
                {
                    Id = c.Id, IsMe = c.IsMe, Team = c.Team, Dnf = c.Dnf, InPit = c.Pit != null, P = q.P, U = u, Yaw = yaw, Lane = v.Lane, Alpha = 1,
                    Braking = !c.Dnf && (c.Spinning > 0 || v.Free || layout.BrakeZone(u)), SinceHit = T - v.HitAt,
                    Roll = v.Roll, Pitch = v.Pitch, Lift = v.Lift, Sliding = Sliding(v), Wrecking = v.Free,
                    Streak = v.Streak > T ? Math.Min(1, (v.Streak - T) / Math.Max(0.1, v.StreakLen) * 1.5) : 0,
                    Glow = v.GlowCol, GlowA = glowA,
                    Nose = c.Zones.Nose, Side = c.Zones.Side, Tail = c.Zones.Tail,
                });
            }
            var pc = race.PaceCar;
            if (pc != null)
            {
                double lane = -0.3, s = TrackLayout.SOf(pc.Dist);
                var g = layout.Shape.Pit;
                if (pc.Leaving && s > g.E0 && s < g.X1)
                {
                    if (double.IsNaN(paceLane0)) paceLane0 = lane;
                    lane = paceLane0 + (TrackLayout.PitLane - paceLane0) * TrackLayout.Smooth((s - g.E0) / (g.E1 - g.E0));
                }
                double u = layout.Shape.UOf(pc.Dist);
                var q = layout.At(u, lane); var q2 = layout.At(u + 0.002, lane);
                into.Add(new CarPose { Id = -1, IsPace = true, P = q.P, U = u, Yaw = Math.Atan2(q2.P.X - q.P.X, q2.P.Z - q.P.Z), Lane = lane, Alpha = SimMath.Clamp(pc.Alpha, 0, 1), SinceHit = 99 });
            }
        }
    }
}
