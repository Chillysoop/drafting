using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>A point on the ground plane. X is right, Z is forward (away from the default camera), Y is up.</summary>
    public readonly struct Vec2
    {
        public readonly double X, Z;
        public Vec2(double x, double z) { X = x; Z = z; }
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
        public static Vec2 operator *(Vec2 a, double k) => new Vec2(a.X * k, a.Z * k);
        public double Length => Math.Sqrt(X * X + Z * Z);
        public override string ToString() => $"({X:F3}, {Z:F3})";
    }

    /// <summary>Where a lane position is on the ground, with the unit tangent (direction of travel) and normal (toward lane +, the outside wall).</summary>
    public readonly struct TrackPoint
    {
        public readonly Vec2 P, T, N;
        public TrackPoint(Vec2 p, Vec2 t, Vec2 n) { P = p; T = t; N = n; }
        /// <summary>Heading in radians about the up axis, 0 = +Z, clockwise seen from above (an engine's yaw).</summary>
        public double Yaw => Math.Atan2(T.X, T.Z);
    }

    /// <summary>
    /// The track drawn in world space: the slice's worldPos, TG geometry, cornerLine, brakeZone and pit road lanes.
    /// Units are the slice's (a stock car is 0.085 long, a lane 0.075 wide); the engine scales them.
    /// The slice draws z toward the viewer; here Z points away, so the picture is the same seen from the default camera.
    /// Lane 0 is the middle of the racing surface, + is toward the outside wall, - toward the apron and pit road.
    /// </summary>
    public sealed class TrackLayout
    {
        public const double CarL = 0.085, CarW = 0.042, CarH = 0.032, LaneW = 0.075, TrackHW = 0.17;
        /// <summary>Lanes of the edges: outer edge (wall), inner edge, apron edge, the lane line just inside the inner edge.</summary>
        public const double OuterLane = TrackHW / LaneW, InnerLane = -TrackHW / LaneW, ApronLane = -(TrackHW + 0.07) / LaneW, LaneLine = (-TrackHW + 0.01) / LaneW;
        /// <summary>Pit road lanes (the slice's pitGeom): the lane cars drive down, the stall line, where they rejoin.</summary>
        public const double PitLane = -(TrackHW + 0.2) / LaneW, StallLane = -(TrackHW + 0.27) / LaneW, PitExitLane = -1.6;
        /// <summary>The pit road surface either side of the stalls, as lanes.</summary>
        public const double PitPadIn = -(TrackHW + 0.16) / LaneW, PitPadOut = -(TrackHW + 0.3) / LaneW;
        /// <summary>The motion model's limits: the wall a car can touch and where wrecked cars park on the infield.</summary>
        public const double WallLane = 1.99, DnfLane = -2.6;

        sealed class Seg
        {
            public bool Turn;
            public double A0, Len, X0, Z0, Th0, Kap, Deg;
            public int Dir;
            public double BrakeA;
        }

        public readonly TrackDef Track;
        public readonly TrackShape Shape;
        public readonly double Perimeter;
        /// <summary>Length of the first straight (the front stretch, where the start/finish line and pit road are).</summary>
        public readonly double FrontStraight;
        /// <summary>Bounding box of the centre line.</summary>
        public readonly double MinX, MaxX, MinZ, MaxZ;

        readonly List<Seg> segs = new List<Seg>();

        static readonly Dictionary<TrackDef, TrackLayout> Cache = new Dictionary<TrackDef, TrackLayout>();

        public static TrackLayout Of(TrackDef t)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(t, out var l)) Cache[t] = l = new TrackLayout(t);
                return l;
            }
        }

        TrackLayout(TrackDef t)
        {
            Track = t;
            Shape = TrackShape.Of(t);
            Perimeter = Shape.Perimeter;
            if (t.IsPath)
            {
                // the slice's TG(): walk the pieces like a turtle, then centre the bounding box on the origin
                double x = 0, z = 0, th = 0, arc = 0;
                int k = 0;
                foreach (var p in t.Path)
                {
                    if (p.IsStraight)
                    {
                        segs.Add(new Seg { A0 = arc, Len = p.S, X0 = x, Z0 = z, Th0 = th });
                        x += Math.Cos(th) * p.S; z += Math.Sin(th) * p.S; arc += p.S;
                    }
                    else
                    {
                        double len = p.R * Math.Abs(p.A) * Math.PI / 180, kap = -Math.Sign(p.A) / p.R;
                        segs.Add(new Seg { Turn = true, A0 = arc, Len = len, X0 = x, Z0 = z, Th0 = th, Kap = kap, Deg = Math.Abs(p.A), Dir = Math.Sign(p.A), BrakeA = Shape.Segments[k].BrakeA });
                        double t1 = th + kap * len;
                        x += (Math.Sin(t1) - Math.Sin(th)) / kap; z -= (Math.Cos(t1) - Math.Cos(th)) / kap; th = t1; arc += len;
                    }
                    k++;
                }
                double bx0 = 1e9, bx1 = -1e9, bz0 = 1e9, bz1 = -1e9;
                foreach (var sg in segs)
                    for (double s = 0; s <= sg.Len; s += 0.05)
                    {
                        RawXY(sg, s, out double qx, out double qz, out _);
                        bx0 = Math.Min(bx0, qx); bx1 = Math.Max(bx1, qx); bz0 = Math.Min(bz0, qz); bz1 = Math.Max(bz1, qz);
                    }
                double cx = (bx0 + bx1) / 2, cz = (bz0 + bz1) / 2;
                foreach (var sg in segs) { sg.X0 -= cx; sg.Z0 -= cz; }
                FrontStraight = segs[0].Len;
            }
            else FrontStraight = t.S;

            double mnx = 1e9, mxx = -1e9, mnz = 1e9, mxz = -1e9;
            for (int i = 0; i < 400; i++)
            {
                var q = At(i / 400.0, 0).P;
                mnx = Math.Min(mnx, q.X); mxx = Math.Max(mxx, q.X); mnz = Math.Min(mnz, q.Z); mxz = Math.Max(mxz, q.Z);
            }
            MinX = mnx; MaxX = mxx; MinZ = mnz; MaxZ = mxz;
        }

        static void RawXY(Seg sg, double s, out double x, out double z, out double th)
        {
            if (!sg.Turn) { x = sg.X0 + Math.Cos(sg.Th0) * s; z = sg.Z0 + Math.Sin(sg.Th0) * s; th = sg.Th0; return; }
            th = sg.Th0 + sg.Kap * s;
            x = sg.X0 + (Math.Sin(th) - Math.Sin(sg.Th0)) / sg.Kap;
            z = sg.Z0 - (Math.Cos(th) - Math.Cos(sg.Th0)) / sg.Kap;
        }

        int SegIndex(double a)
        {
            int lo = 0, hi = segs.Count - 1;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (segs[m].A0 <= a) lo = m; else hi = m - 1; }
            return lo;
        }

        /// <summary>The slice's worldPos(t, u, lane): u is a fraction of track length from the start of the front straight.</summary>
        public TrackPoint At(double u, double lane)
        {
            u = SimMath.Frac(u);
            double x, z, nx, nz;
            if (!Track.IsPath)
            {
                double S = Track.S, R = Track.R, d = u * Perimeter;
                if (d < S) { x = -S / 2 + d; z = R; nx = 0; nz = 1; }
                else if (d < S + Math.PI * R) { double th = Math.PI / 2 - (d - S) / R; x = S / 2 + R * Math.Cos(th); z = R * Math.Sin(th); nx = Math.Cos(th); nz = Math.Sin(th); }
                else if (d < 2 * S + Math.PI * R) { x = S / 2 - (d - S - Math.PI * R); z = -R; nx = 0; nz = -1; }
                else { double th = -Math.PI / 2 - (d - 2 * S - Math.PI * R) / R; x = -S / 2 + R * Math.Cos(th); z = R * Math.Sin(th); nx = Math.Cos(th); nz = Math.Sin(th); }
            }
            else
            {
                double a = u * Perimeter;
                var sg = segs[SegIndex(a)];
                RawXY(sg, a - sg.A0, out x, out z, out double th);
                nx = -Math.Sin(th); nz = Math.Cos(th);
            }
            double off = lane * LaneW;
            // slice space -> world space: flip z. The tangent is the slice normal turned a quarter (nz, -nx), flipped too.
            return new TrackPoint(new Vec2(x + nx * off, -(z + nz * off)), new Vec2(nz, nx), new Vec2(nx, -nz));
        }

        /// <summary>Where a car at race distance dist is (dist in laps, as the sim keeps it).</summary>
        public TrackPoint AtDist(double dist, double lane) => At(Shape.UOf(dist), lane);

        /// <summary>The start/finish line: the middle of the front straight.</summary>
        public double StartFinishU => FrontStraight / 2 / Perimeter;

        /// <summary>u of a point a given arc length along the front straight.</summary>
        public double FrontU(double arc) => arc / Perimeter;

        /// <summary>Samples one lane all the way round, closed (the last point repeats the first).</summary>
        public Vec2[] Ring(double lane, int n = 0)
        {
            if (n <= 0) n = Track.IsPath ? 560 : 160;
            var pts = new Vec2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = At((double)i / n, lane).P;
            return pts;
        }

        /// <summary>
        /// The racing line as a lane offset (the slice's cornerLine): enter wide, apex on the inside, out wide.
        /// Path tracks mirror it for right-handers and scale it by the turn angle.
        /// </summary>
        public double CornerLine(double u)
        {
            double d = SimMath.Frac(u) * Perimeter;
            if (!Track.IsPath)
            {
                double S = Track.S, T = Math.PI * Track.R, f = -1;
                if (d >= S && d < S + T) f = (d - S) / T; else if (d >= 2 * S + T) f = (d - 2 * S - T) / T;
                if (f < 0) { double q = (d < S ? d : d - S - T) / S; return q > 0.7 ? (q - 0.7) / 0.3 * 0.3 : q < 0.3 ? (0.3 - q) / 0.3 * 0.3 : 0; }
                return 0.3 - 0.75 * Math.Sin(f * Math.PI);
            }
            int i = SegIndex(d), n = segs.Count;
            var sg = segs[i];
            double Amp(Seg s) => s.Dir * Math.Min(1, s.Deg / 90);
            if (sg.Turn) return Amp(sg) * (0.3 - 0.75 * Math.Sin((d - sg.A0) / sg.Len * Math.PI));
            Seg pv = segs[(i - 1 + n) % n], nx = segs[(i + 1) % n];
            double qq = (d - sg.A0) / sg.Len, l = 0;
            if (qq > 0.7 && nx.Turn) l += Amp(nx) * 0.3 * (qq - 0.7) / 0.3;
            if (qq < 0.3 && pv.Turn) l += Amp(pv) * 0.3 * (0.3 - qq) / 0.3;
            return l;
        }

        /// <summary>Brake lights on: the end of a straight into a turn and the first part of the turn (the slice's brakeZone).</summary>
        public bool BrakeZone(double u)
        {
            double d = SimMath.Frac(u) * Perimeter;
            if (!Track.IsPath)
            {
                double S = Track.S, T = Math.PI * Track.R;
                if (d < S) return d / S > 0.82;
                if (d < S + T) return (d - S) / T < 0.14;
                if (d < 2 * S + T) return (d - S - T) / S > 0.82;
                return (d - 2 * S - T) / T < 0.14;
            }
            var sg = segs[SegIndex(d)];
            if (sg.Turn) return (d - sg.A0) / sg.Len < 0.14;
            // braking for the next turn starts where the speed profile starts to fall
            int j = (SegIndex(d) + 1) % segs.Count;
            var nx = segs[j];
            return nx.Turn && nx.BrakeA >= sg.A0 && d >= nx.BrakeA;
        }

        /// <summary>Signed distance in laps from the nearest start/finish crossing (the slice's sOf), the frame pit road works in.</summary>
        public static double SOf(double dist) => dist - SimMath.RoundJs(dist);

        /// <summary>The lane pit road paints from entry to exit (the slice draws it from -2.3 down to the pit lane and back).</summary>
        public double PitRoadLane(double s)
        {
            var g = Shape.Pit;
            if (s < g.E1) return -2.3 + (PitLane + 2.3) * Smooth((s - g.E0) / (g.E1 - g.E0));
            if (s > g.X0) return PitLane + (-2.3 - PitLane) * Smooth((s - g.X0) / (g.X1 - g.X0));
            return PitLane;
        }

        /// <summary>The figure-8's crossover points as u.</summary>
        public double[] CrossoverU() => Track.Fig8 ? Shape.CrossoverU() : Array.Empty<double>();

        public static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
    }
}
