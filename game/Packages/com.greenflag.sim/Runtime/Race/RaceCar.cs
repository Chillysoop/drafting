using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum PitService { Stay, Fuel, Two, Four }
    public enum PitStage { In, Box, Out }

    public enum EffectKind { Pace, Wear, Block, Hold }

    /// <summary>A timed modifier on one car: a pace or tire-wear multiplier, or a defensive block.</summary>
    public struct CarEffect
    {
        public EffectKind Kind;
        public double Mult, Until;
        /// <summary>A slowed car yields: the car behind may pass it without a move.</summary>
        public bool Yield;
    }

    public sealed class Overtake
    {
        public RaceCar Target;
        public double Until;
        public int Left;
    }

    public sealed class PitVisit
    {
        public PitStage Stage;
        public PitService Service;
        public double T0, T1, Until;
    }

    /// <summary>Damage split over three zones (nose, side, tail). The total always matches the car's Damage.</summary>
    public sealed class DamageZones
    {
        public double Nose, Side, Tail;
        public double Sum => Nose + Side + Tail;
    }

    /// <summary>One car in one race. The pace car is a RaceCar too (IsPace), but it is never in Cars or Order.</summary>
    public sealed class RaceCar
    {
        public Entrant Ref;
        public int Id;
        public bool IsMe, IsPace;
        public CarStats Stats;
        public Trait Trait;
        public int Team, Start;

        public double Dist, PrevDist, Speed;
        public double Tires = 100, Damage;
        public readonly DamageZones Zones = new DamageZones();
        public readonly List<CarEffect> Effects = new List<CarEffect>();
        public double Noise = 1, NoiseAt, PermPace = 1;
        public double NextMove;
        public Overtake Overtake;
        public bool Dnf;
        public double Spinning;
        public int Passes;

        // fuel and pit road
        public double Fuel, Burn = 1, Stall, DryAt, PitSlip;
        public PitVisit Pit;
        public bool EarlyStyle;
        public int PlanLap;
        public PitService PlanSvc = PitService.Four;
        public int LastPitLap, PitStops;
        /// <summary>Caution ids: the caution this car passed pit entry in, pitted in, or pitted in with pit road closed. -1 = none.</summary>
        public int CPass = -1, CPit = -1, Penal = -1;

        // pace car
        public bool Leaving, LeftTrack;
        public double Alpha;

        // figure-8
        internal int F8k = int.MinValue;

        public bool HasEffect(EffectKind k)
        {
            foreach (var e in Effects) if (e.Kind == k) return true;
            return false;
        }

        public override string ToString() => IsPace ? "Pace car" : $"#{Ref.Number} {Ref.Name}";
    }
}
