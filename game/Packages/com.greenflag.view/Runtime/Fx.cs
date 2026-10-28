using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>A spark: a hot streak that flies off a contact point, falls and fades. Track units, Y up.</summary>
    public struct Spark
    {
        public double X, Y, Z, Vx, Vy, Vz, T, Life;
    }

    public enum PuffKind
    {
        /// <summary>Pale tire smoke from a slide or a spin.</summary>
        Tire,
        /// <summary>Dark smoke from a hurt engine or a bent radiator.</summary>
        Engine,
        /// <summary>Brown dust thrown up off the grass.</summary>
        Dust,
    }

    /// <summary>A puff of smoke: rises, grows and fades.</summary>
    public struct Puff
    {
        public double X, Z, T, Life, R, A;
        public PuffKind Kind;
        /// <summary>Height right now (track units).</summary>
        public double Y => T * RiseRate;
        /// <summary>Radius right now (track units).</summary>
        public double Radius => R + T * 0.03;
        /// <summary>Opacity right now, 0 to 1.</summary>
        public double Alpha => A * (1 - T / Life);
        public const double RiseRate = 0.02;
    }

    /// <summary>A rubber mark on the track from a sliding tire. Fades over SkidLife seconds.</summary>
    public struct Skid
    {
        public Vec2 A, B;
        public double At;
    }

    /// <summary>
    /// The race's particles: the slice's race.sparks, race.smoke and race.skids. CarMotion emits them from its own SimRng and steps
    /// them with the motion, so they never touch the sim. The engine only draws what is here.
    /// </summary>
    public sealed class Fx
    {
        public const int MaxSparks = 320, MaxPuffs = 600, MaxSkids = 700;
        public const double SkidLife = 30;
        /// <summary>Gravity in track units (9.8 m/s at 60 m a unit), for sparks and hops.</summary>
        public const double G = 9.81 / 60;

        public readonly List<Spark> Sparks = new List<Spark>();
        public readonly List<Puff> Puffs = new List<Puff>();
        public readonly List<Skid> Skids = new List<Skid>();
        /// <summary>Sparks thrown so far this race (tests and the frames page).</summary>
        public int SparksThrown { get; private set; }

        readonly SimRng rng;
        public Fx(SimRng rng) { this.rng = rng; }

        /// <summary>The slice's spark(): n sparks from a point, carried along dir (unit) at about speed, sprayed every way and up.</summary>
        public void Burst(Vec2 at, int n, Vec2 dir, double speed, double y = 0.012)
        {
            for (int k = 0; k < n; k++)
            {
                double a = rng.Rand(0, Math.PI * 2), m = rng.Rand(0.05, 0.22);
                Sparks.Add(new Spark
                {
                    X = at.X, Y = y, Z = at.Z,
                    Vx = dir.X * speed * rng.Rand(0.4, 0.8) + Math.Cos(a) * m,
                    Vz = dir.Z * speed * rng.Rand(0.4, 0.8) + Math.Sin(a) * m,
                    Vy = rng.Rand(0.02, 0.12),
                    Life = rng.Rand(0.2, 0.55),
                });
            }
            SparksThrown += Math.Max(0, n);
            if (Sparks.Count > MaxSparks) Sparks.RemoveRange(0, Sparks.Count - MaxSparks);
        }

        public void Smoke(Vec2 at, PuffKind kind, double life, double r, double a = 1)
        {
            Puffs.Add(new Puff { X = at.X + rng.Rand(-0.01, 0.01), Z = at.Z + rng.Rand(-0.01, 0.01), Life = life, R = r, A = a, Kind = kind });
            if (Puffs.Count > MaxPuffs) Puffs.RemoveRange(0, Puffs.Count - MaxPuffs);
        }

        public void Mark(Vec2 a, Vec2 b, double now)
        {
            Skids.Add(new Skid { A = a, B = b, At = now });
            if (Skids.Count > MaxSkids) Skids.RemoveRange(0, Skids.Count - MaxSkids);
        }

        public void Step(double dt, double now)
        {
            for (int i = 0; i < Sparks.Count; i++)
            {
                var k = Sparks[i];
                k.T += dt; k.X += k.Vx * dt; k.Z += k.Vz * dt; k.Y += k.Vy * dt;
                k.Vx *= 1 - dt * 2; k.Vz *= 1 - dt * 2; k.Vy -= G * dt;
                if (k.Y < 0) { k.Y = 0; k.Vy = -k.Vy * 0.3; }
                Sparks[i] = k;
            }
            Sparks.RemoveAll(k => k.T >= k.Life);
            for (int i = 0; i < Puffs.Count; i++) { var p = Puffs[i]; p.T += dt; Puffs[i] = p; }
            Puffs.RemoveAll(p => p.T >= p.Life);
            if (Skids.Count > 0 && now - Skids[0].At > SkidLife) Skids.RemoveAll(s => now - s.At > SkidLife);
        }

        /// <summary>How dark a skid mark still is, 0 to 1 (the slice's 0.55 fading over 30 s).</summary>
        public static double SkidAlpha(Skid s, double now) => 0.55 * Math.Max(0, 1 - (now - s.At) / SkidLife);
    }
}
