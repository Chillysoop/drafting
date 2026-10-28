using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum TurnKind { None, Straight, Corner, Hairpin, Esses, Sweeper }

    /// <summary>Where a car is on the lap, as the slice's segOf() reports it.</summary>
    public readonly struct TrackSpot
    {
        public readonly bool Turn, Approach, Straight, Long;
        /// <summary>Path tracks only: the kind of the turn the car is in or braking for (None on an oval).</summary>
        public readonly TurnKind Kind;
        /// <summary>+1 left, -1 right, 0 on a straight or an oval.</summary>
        public readonly int Dir;
        public readonly string Corner;

        public TrackSpot(bool turn, bool approach, bool straight, TurnKind kind = TurnKind.None, int dir = 0, string corner = null, bool lng = false)
        { Turn = turn; Approach = approach; Straight = straight; Kind = kind; Dir = dir; Corner = corner; Long = lng; }
    }

    /// <summary>Pit road in lap fractions around the start/finish line: entry starts at E0, the stalls run E1..X0, exit ends at X1.</summary>
    public readonly struct PitGeometry
    {
        public readonly double E0, E1, X0, X1;
        public PitGeometry(double e0, double e1, double x0, double x1) { E0 = e0; E1 = e1; X0 = x0; X1 = x1; }
    }

    /// <summary>
    /// The geometry the sim needs: lap length, segment kinds, the time-based mapping from race distance to track position,
    /// pit road and the minimum car gap. Drawing the track (x/z, lanes) is presentation work in step 29.
    /// Race distance is in laps. On an oval it is plain arc length; on a path track it is lap time, because cars slow for
    /// corners (the slice's TG speed profile) and dist stays linear in time.
    /// </summary>
    public sealed class TrackShape
    {
        public const double CarLength = 0.085;
        const double Ds = 0.01, Brake = 0.6, Accel = 0.35;

        public sealed class Segment
        {
            public bool IsTurn;
            public double A0, Len, R, Deg, BrakeA;
            public int Dir;
            public TurnKind Kind = TurnKind.Straight;
            public string Name;
        }

        public readonly TrackDef Track;
        public readonly double Perimeter;
        public readonly double MinGap;
        public readonly PitGeometry Pit;
        public readonly IReadOnlyList<Segment> Segments;

        // path speed profile
        readonly double[] v, tm;
        readonly int n;
        readonly double tt, tau0, firstS;

        static readonly Dictionary<TrackDef, TrackShape> Cache = new Dictionary<TrackDef, TrackShape>();

        public static TrackShape Of(TrackDef t)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(t, out var s)) Cache[t] = s = new TrackShape(t);
                return s;
            }
        }

        TrackShape(TrackDef t)
        {
            Track = t;
            if (!t.IsPath)
            {
                Perimeter = 2 * t.S + 2 * Math.PI * t.R;
                Segments = Array.Empty<Segment>();
                MinGap = CarLength * 1.18 / Perimeter;
                double h = Math.Min(t.S / 2 - 0.12, 0.8), P = Perimeter;
                Pit = new PitGeometry(-(h + 0.45) / P, -h / P, h / P, (h + 0.5) / P);
                return;
            }

            var segs = new List<Segment>();
            double arc = 0;
            foreach (var p in t.Path)
            {
                if (p.IsStraight) { segs.Add(new Segment { A0 = arc, Len = p.S }); arc += p.S; }
                else
                {
                    double len = p.R * Math.Abs(p.A) * Math.PI / 180;
                    segs.Add(new Segment { IsTurn = true, A0 = arc, Len = len, R = p.R, Dir = Math.Sign(p.A), Deg = Math.Abs(p.A), Name = p.Name });
                    arc += len;
                }
            }
            Perimeter = arc;
            Segments = segs;
            int sn = segs.Count, tn = 0;
            Segment NearTurn(int i, int d)
            {
                double gap = 0;
                for (int k = 1; k < sn; k++)
                {
                    var q = segs[((i + d * k) % sn + sn) % sn];
                    if (q.IsTurn) return q;
                    gap += q.Len;
                    if (gap > 0.6) return null;
                }
                return null;
            }
            for (int i = 0; i < sn; i++)
            {
                var sg = segs[i];
                if (!sg.IsTurn) continue;
                tn++;
                bool esses = false;
                foreach (var q in new[] { NearTurn(i, -1), NearTurn(i, 1) })
                    if (q != null && q.Dir != sg.Dir && q.Deg <= 60) esses = true;
                sg.Kind = sg.Deg >= 150 ? TurnKind.Hairpin : sg.R >= 0.9 ? TurnKind.Sweeper : (sg.Deg <= 60 && esses) ? TurnKind.Esses : TurnKind.Corner;
                sg.Name = sg.Name ?? (sg.Kind == TurnKind.Hairpin ? "Hairpin" : sg.Kind == TurnKind.Esses ? (sg.R <= 0.45 ? "Chicane" : "Esses") : sg.Kind == TurnKind.Sweeper ? "Sweeper" : "Turn " + tn);
            }

            // speed profile: target speed by turn radius and angle, then braking and acceleration limits (laps wrap)
            n = (int)Math.Ceiling(arc / Ds);
            v = new double[n];
            int si = 0;
            for (int i = 0; i < n; i++)
            {
                double a = i * Ds;
                while (si < sn - 1 && segs[si + 1].A0 <= a) si++;
                var sg = segs[si];
                v[i] = sg.IsTurn ? Math.Min(1, 0.38 + 0.55 * sg.R + 0.25 * Math.Max(0, 1 - sg.Deg / 90)) : 1;
            }
            for (int pass = 0; pass < 2; pass++)
                for (int i = n - 1; i >= 0; i--) { double q = v[(i + 1) % n]; v[i] = Math.Min(v[i], Math.Sqrt(q * q + 2 * Brake * Ds)); }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++) { double q = v[(i - 1 + n) % n]; v[i] = Math.Min(v[i], Math.Sqrt(q * q + 2 * Accel * Ds)); }
            tm = new double[n + 1];
            var brk = new bool[n];
            double vmin = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                tm[i + 1] = tm[i] + Math.Min(Ds, arc - i * Ds) / v[i];
                brk[i] = v[(i + 1) % n] < v[i] - 1e-6;
                vmin = Math.Min(vmin, v[i]);
            }
            tt = tm[n];
            firstS = segs[0].Len;
            foreach (var sg in segs)
            {
                if (!sg.IsTurn) continue;
                int i = (int)Math.Floor(sg.A0 / Ds) % n, k = 0;
                while (k < n && brk[((i - 1) % n + n) % n]) { i = ((i - 1) % n + n) % n; k++; }
                sg.BrakeA = i * Ds;
            }
            tau0 = Tau(firstS / 2);
            MinGap = CarLength * 1.18 / (Math.Max(vmin, 0.62) * tt);

            double hh = Math.Min(firstS / 2 - 0.12, 0.8);
            double D(double x)
            {
                double tau = Tau(firstS / 2 + x) - tau0;
                if (x < 0 && tau > 0) tau -= tt;
                if (x > 0 && tau < 0) tau += tt;
                return tau / tt;
            }
            Pit = new PitGeometry(D(-(hh + 0.45)), D(-hh), D(hh), D(hh + 0.5));
        }

        double Tau(double a)
        {
            a = ((a % Perimeter) + Perimeter) % Perimeter;
            int i = Math.Min(n - 1, (int)Math.Floor(a / Ds));
            return tm[i] + (a - i * Ds) / v[i];
        }

        double Arc(double tau)
        {
            tau = ((tau % tt) + tt) % tt;
            int lo = 0, hi = n - 1;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (tm[m] <= tau) lo = m; else hi = m - 1; }
            return Math.Min(Perimeter, lo * Ds + (tau - tm[lo]) * v[lo]);
        }

        int SegIndex(double a)
        {
            int lo = 0, hi = Segments.Count - 1;
            while (lo < hi) { int m = (lo + hi + 1) >> 1; if (Segments[m].A0 <= a) lo = m; else hi = m - 1; }
            return lo;
        }

        /// <summary>Track position (fraction of track length, 0 = start of the first straight) for a race distance.</summary>
        public double UOf(double dist)
        {
            if (!Track.IsPath) return SimMath.Frac(dist + Track.S / 2 / Perimeter);
            return Arc(SimMath.Frac(dist) * tt + tau0) / Perimeter;
        }

        /// <summary>Share of a lap's time from position u to position p, going forward.</summary>
        public double UGap(double u, double p)
        {
            if (!Track.IsPath) return SimMath.Frac(p - u);
            return SimMath.Frac((Tau(p * Perimeter) - Tau(u * Perimeter)) / tt);
        }

        public TrackSpot SpotOf(double dist)
        {
            double a = UOf(dist) * Perimeter;
            if (!Track.IsPath)
            {
                double S = Track.S, T = Math.PI * Track.R, pos = 0, tf = 0;
                bool turn = false;
                if (a < S) pos = a / S;
                else if (a < S + T) { turn = true; tf = (a - S) / T; }
                else if (a < 2 * S + T) pos = (a - S - T) / S;
                else { turn = true; tf = (a - 2 * S - T) / T; }
                // corner entry spans the back half of the straight and the first fifth of the turn
                bool approach = false;
                if (!turn && pos > 0.45) approach = true;
                if (turn && tf < 0.2) { turn = false; approach = true; }
                return new TrackSpot(turn, approach, !turn && !approach);
            }
            int i = SegIndex(a);
            var sg = Segments[i];
            if (sg.IsTurn)
            {
                bool ap = (a - sg.A0) / sg.Len < 0.2;
                return new TrackSpot(!ap, ap, false, sg.Kind, sg.Dir, sg.Name);
            }
            int j = i, k = 0, cnt = Segments.Count;
            do { j = (j + 1) % cnt; } while (!Segments[j].IsTurn && ++k < cnt);
            var nx = Segments[j];
            bool app = (a - sg.A0) / sg.Len > 0.45 || (nx.BrakeA >= sg.A0 && a >= nx.BrakeA);
            return app ? new TrackSpot(false, true, false, nx.Kind, nx.Dir, nx.Name)
                       : new TrackSpot(false, false, true, TurnKind.Straight, 0, null, sg.Len >= 1.8);
        }

        /// <summary>The figure-8 crossover points: the middle of path pieces 2 and 6, as track positions.</summary>
        public double[] CrossoverU()
        {
            double A(int i) => (Segments[i].A0 + Segments[i].Len / 2) / Perimeter;
            return new[] { A(2), A(6) };
        }
    }
}
