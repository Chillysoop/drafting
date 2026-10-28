using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>
    /// What the qualifying run screen draws (roadmap step 37), engine-free: the real track's outline fitted in a box (the race's
    /// own geometry, as Conner asked for the slice's map), where your car and each corner are on it, and the brake bar's zones.
    /// Unity's QualiHud only paints these numbers. Map coordinates are 0 to 1 with y down.
    /// </summary>
    public sealed class QualiPanel
    {
        public readonly TrackDef Track;
        readonly TrackLayout lay;
        readonly double x0, z0, k, ox, oy;

        public QualiPanel(TrackDef t)
        {
            Track = t;
            lay = TrackLayout.Of(t);
            double w = lay.MaxX - lay.MinX, h = lay.MaxZ - lay.MinZ, m = Math.Max(w, h);
            k = 0.9 / m; x0 = lay.MinX; z0 = lay.MinZ;
            ox = (1 - w * k) / 2; oy = (1 - h * k) / 2;
        }

        /// <summary>A point on the map for a track position u (a lap fraction; whole laps wrap).</summary>
        public (double x, double y) At(double u)
        {
            var p = lay.At(SimMath.Frac(u), 0).P;
            return (ox + (p.X - x0) * k, 1 - (oy + (p.Z - z0) * k));
        }

        /// <summary>The outline as n points around the lap.</summary>
        public List<(double x, double y)> Outline(int n = 240)
        {
            var o = new List<(double, double)>(n);
            for (int i = 0; i < n; i++) o.Add(At((double)i / n));
            return o;
        }

        /// <summary>The bar's zones for a check as [from, to] fractions of the bar: OK, Good and Perfect, widest first.</summary>
        public static (double a, double b)[] Zones(QualiCheck c) => new[]
        {
            (Math.Max(0, c.C - c.Wo), Math.Min(1, c.C + c.Wo)),
            (Math.Max(0, c.C - c.Wg), Math.Min(1, c.C + c.Wg)),
            (Math.Max(0, c.C - c.Wp), Math.Min(1, c.C + c.Wp)),
        };

        public static Rgb MarkColor(QualiMark m) => m == QualiMark.Perfect ? Rgb.Hex("#e9b52f") : m == QualiMark.Good ? Rgb.Hex("#2fb36a") : m == QualiMark.Ok ? Rgb.Hex("#9fd0ff") : Rgb.Hex("#e5484d");

        /// <summary>The big line under the bar: the corner and what to do, or the rolling start.</summary>
        public static string Prompt(QualiRun r)
        {
            var c = r.Current;
            if (r.Time < Qualifying.Roll) return "Rolling start · timing at the line";
            if (r.Time < Qualifying.Roll + 0.8) return "Green: on the clock";
            return c == null ? "Take the flag" : $"{c.Name} · {c.Action}";
        }

        /// <summary>The last mark, for about a second after it lands ("Perfect ×3", "Saved: OK").</summary>
        public static string Pop(QualiRun r)
        {
            var c = r.Last;
            if (c == null || r.Time - r.LastAt > 1.1 || c.Res == null) return null;
            if (c.Saved) return "Saved: OK";
            var m = c.Res.Value;
            return Qualifying.MarkName(m) + ((m == QualiMark.Perfect || m == QualiMark.Good) && c.Chain > 1 ? " ×" + c.Chain : "");
        }
    }
}
