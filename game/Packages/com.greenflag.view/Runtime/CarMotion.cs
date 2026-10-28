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
    }

    /// <summary>
    /// Where each car sits across the track and how it is turned: the slice's motionTick (v47), minus the card outcome moves,
    /// sparks and smoke, which step 30 carries over. Purely visual: it reads the race and never changes it, and its rolls come
    /// from its own SimRng, so drawing a race (or not) leaves the sim's results and fingerprint untouched.
    /// </summary>
    public sealed class CarMotion
    {
        // the slice's MV dials
        const double K = 7, Damp = 5.3, MaxLv = 1.4, KHit = 16, DampHit = 6.4, MaxLvHit = 2.4;
        const double YawGain = 1.8, YawRate = 3, YawMax = 0.3, Sep = 0.9, EaseRate = 1.3, RowRate = 1.4;

        sealed class Vis
        {
            public double Lane, LaneT, Lv, Line, LineAt, DOff, Ease, Yaw, HitAt = -9, PrevSpinning, Lane0 = double.NaN;
            public bool Row;
            public double SpinT0 = double.NaN, SpinDur, SpinTotal, SpinLane;
        }

        readonly Race race;
        readonly TrackLayout layout;
        readonly SimRng rng;
        readonly Dictionary<RaceCar, Vis> vis = new Dictionary<RaceCar, Vis>();
        readonly List<(RaceCar c, double u)> list = new List<(RaceCar, double)>();
        readonly Dictionary<RaceCar, RaceCar> being = new Dictionary<RaceCar, RaceCar>();
        RaceCar pace;
        double paceLane0 = double.NaN;

        /// <summary>The time the motion has been stepped to (sim seconds).</summary>
        public double T { get; private set; }

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
            }
            T = race.T;
        }

        Vis V(RaceCar c)
        {
            if (!vis.TryGetValue(c, out var v)) vis[c] = v = new Vis { Line = rng.Rand(-0.4, 0.4) };
            return v;
        }

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

        /// <summary>Visual race distance: the sim's (interpolated) distance plus the easing offset.</summary>
        public double VisDist(RaceCar c, double dist) => dist + (vis.TryGetValue(c, out var v) ? v.DOff : 0);

        /// <summary>Steps the motion by dt sim seconds. dist(c) gives each car's interpolated race distance for this frame.</summary>
        public void Step(double dt, Func<RaceCar, double> dist)
        {
            if (dt <= 0) return;
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
                if (being.ContainsKey(c)) lt = 0.45 + cl * 0.3;   // the passer goes low, this car holds high
                if (c.Overtake != null) lt = -0.75 + cl * 0.3;
                // spin: starts when the sim sets Spinning
                if (c.Spinning > 0 && v.PrevSpinning <= 0 && !c.Dnf) StartSpin(c, v, c.Spinning);
                v.PrevSpinning = c.Spinning;
                if (!double.IsNaN(v.SpinT0) && t - v.SpinT0 < v.SpinDur) lt = v.SpinLane;
                if (c.Dnf) lt = TrackLayout.DnfLane;
                if (c.Pit != null)
                {
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

            // 3) integrate: lateral spring, longitudinal easing, yaw
            foreach (var c in r.Cars)
            {
                var v = V(c);
                bool spinning = !double.IsNaN(v.SpinT0) && t - v.SpinT0 < v.SpinDur;
                bool hard = c.Dnf || spinning || t - v.HitAt < 0.6;
                double k = hard ? KHit : K, dmp = hard ? DampHit : Damp, ml = hard ? MaxLvHit : MaxLv;
                double acc = k * (v.LaneT - v.Lane) - dmp * v.Lv;
                v.Lv = SimMath.Clamp(v.Lv + acc * dt, -ml * (c.Dnf ? 1.5 : 1), ml);
                v.Lane += v.Lv * dt;
                if (!c.Dnf)
                {
                    if (v.Lane > TrackLayout.WallLane) { if (v.Lv > 0.5 && t - v.HitAt > 0.35) v.HitAt = t; v.Lane = TrackLayout.WallLane; v.Lv = -Math.Abs(v.Lv) * 0.25; }
                    if (c.Pit != null) { v.Lv = SimMath.Clamp((v.LaneT - v.Lane) / dt * 0.5, -MaxLv, MaxLv); v.Lane = v.LaneT; }
                    else if (v.Lane < -2.2) { v.Lane = -2.2; v.Lv = Math.Max(0, v.Lv); }
                }
                double dT = 0, rate = RowRate;
                if (v.Ease > t) rate = EaseRate;
                if (v.Row) { var a = r.AheadOf(c); if (a != null) dT = SimMath.Clamp((dist(a) - dist(c)) * 0.95, 0, TrackLayout.CarL * 2 / P); }
                v.DOff += (dT - v.DOff) * Math.Min(1, dt * rate);
                if (!(v.Ease > t) && Math.Abs(v.DOff) < 1e-6) v.DOff = 0;
                double sp = Math.Max(0.02, c.Speed * P);
                double slip = SimMath.Clamp(Math.Atan2(v.Lv * TrackLayout.LaneW, sp) * YawGain, -YawMax, YawMax);
                v.Yaw += (slip - v.Yaw) * Math.Min(1, dt * YawRate);
            }

            // 4) contact: cars never overlap; closing hard is a bump and a bounce
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
                        if (-closing > 0.45 && t - Math.Max(va.HitAt, vb.HitAt) > 0.5) va.HitAt = vb.HitAt = t;
                    }
                }
            foreach (var (c, _) in list) { var v = V(c); v.Lane = SimMath.Clamp(v.Lane, -2.2, TrackLayout.WallLane); }

            // the pace car: runs the low groove, then peels down pit road and fades out
            if (r.PaceCar != pace) { pace = r.PaceCar; paceLane0 = double.NaN; }
        }

        void StartSpin(RaceCar c, Vis v, double secs)
        {
            double hard = secs >= 2 ? 1 : 0.5, dir = rng.NextDouble() < 0.8 ? -1 : 1;
            v.SpinT0 = T; v.SpinDur = secs * 0.85;
            v.SpinTotal = dir * rng.Rand(Math.PI * 0.9, Math.PI * 1.9) * (0.6 + hard * 0.4);
            v.SpinLane = hard >= 1 ? TrackLayout.WallLane : rng.Rand(0.2, 1.0);
            v.Lv += 1.2 * hard;
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
                // the heading follows the lane the car is on (pit road bends), plus its slip and any spin
                var q2 = layout.At(u + 0.002, v.Lane);
                double yaw = Math.Atan2(q2.P.X - q.P.X, q2.P.Z - q.P.Z) + v.Yaw + SpinAngle(v);
                into.Add(new CarPose
                {
                    Id = c.Id, IsMe = c.IsMe, Team = c.Team, Dnf = c.Dnf, InPit = c.Pit != null, P = q.P, U = u, Yaw = yaw, Lane = v.Lane, Alpha = 1,
                    Braking = !c.Dnf && (c.Spinning > 0 || layout.BrakeZone(u)), SinceHit = T - v.HitAt,
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
