using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>The slice's small helpers (rand, randi, pick, shuffle, gauss, clamp), on a SimRng instead of Math.random.</summary>
    public static class SimMath
    {
        public static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));

        /// <summary>JavaScript's Math.round: halves round up, not to even.</summary>
        public static double RoundJs(double v) => Math.Floor(v + 0.5);

        /// <summary>The fractional part, always in [0, 1).</summary>
        public static double Frac(double v) => ((v % 1) + 1) % 1;

        public static double Rand(this SimRng rng, double a, double b) => a + rng.NextDouble() * (b - a);

        public static int RandI(this SimRng rng, int a, int b) => (int)Math.Floor(rng.Rand(a, b + 1));

        public static T Pick<T>(this SimRng rng, IReadOnlyList<T> items) => items[(int)Math.Floor(rng.NextDouble() * items.Count)];

        /// <summary>Box-Muller, as the slice's gauss(). Uses Log, Sqrt and Cos: if platforms ever disagree on a golden seed, this is the first suspect.</summary>
        public static double Gauss(this SimRng rng)
        {
            double u = 0, v = 0;
            while (u == 0) u = rng.NextDouble();
            while (v == 0) v = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
        }

        /// <summary>Fisher-Yates in place, same walk as the slice's shuffle().</summary>
        public static void Shuffle<T>(this SimRng rng, IList<T> a)
        {
            for (int i = a.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(rng.NextDouble() * (i + 1));
                T t = a[i]; a[i] = a[j]; a[j] = t;
            }
        }

        /// <summary>A stable insertion sort. List.Sort is not stable, and the race order is nearly sorted every tick anyway.</summary>
        public static void StableSort<T>(List<T> a, Comparison<T> cmp)
        {
            for (int i = 1; i < a.Count; i++)
            {
                T x = a[i];
                int j = i - 1;
                while (j >= 0 && cmp(a[j], x) > 0) { a[j + 1] = a[j]; j--; }
                a[j + 1] = x;
            }
        }
    }
}
