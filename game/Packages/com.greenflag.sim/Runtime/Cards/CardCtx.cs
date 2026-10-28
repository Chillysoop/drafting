using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>
    /// What a card sees when it reads the odds or lands: your car, the cars around it, the gaps and the spot on the track
    /// (the slice's ctxFor), plus the outcome helpers its effects call (pace, slow, overtake, mom, dmg, wreck, buff, draw).
    /// </summary>
    public sealed class CardCtx
    {
        public readonly Race R;
        public readonly RaceCar Car, Ahead, Behind;
        /// <summary>Seconds to the car ahead and to the car behind (infinity when there is none).</summary>
        public readonly double GapA, GapB;
        public readonly int Pos;
        public readonly TrackDef Track;
        public readonly TrackSpot Seg;
        public readonly double Tires;
        public readonly int Momentum;
        /// <summary>The card being resolved (damage zones). Null while reading odds.</summary>
        internal CardDef Card;

        public CardCtx(Race r, RaceCar car)
        {
            R = r; Car = car;
            Ahead = r.AheadOf(car); Behind = r.BehindOf(car);
            GapA = Ahead != null ? r.GapSec(Ahead, car) : double.PositiveInfinity;
            GapB = Behind != null ? r.GapSec(car, Behind) : double.PositiveInfinity;
            Pos = r.IndexOf(car) + 1;
            Track = r.Track;
            Seg = r.SpotOf(car);
            Tires = car.Tires;
            Momentum = r.Momentum;
        }

        // ---- outcome helpers (the slice's pace / slow / overtake / mom / dmg / wreck / buff / draw)
        public void Pace(RaceCar car, double mult, double sec) => R.AddPace(car, mult, sec);
        public void Slow(RaceCar car, double lossSec) => R.Slow(car, lossSec);
        public void WearFx(RaceCar car, double mult, double sec) => R.WearFx(car, mult, sec);
        public void BlockFor(RaceCar car, double sec) => R.BlockFor(car, sec);
        public void Overtake(RaceCar car, int n) => R.StartOvertake(car, n);
        public void Mom(int n) => R.AddMomentum(n);
        public void Buff(int v, string src) => R.SetBuff(v, null, src);
        public void Draw(int n) => R.DrawCards(n);
        public void Wreck(RaceCar car, RaceCar other, string why, bool big = false) => R.Wreck(car, other, why, big);

        /// <summary>Card damage: on your car it lands where the card says, on anyone else in the tail.</summary>
        public void Dmg(RaceCar car, double amt)
        {
            if (car == null) return;
            var z = car.IsMe && Card != null ? Card.Zone : DamageZone.Tail;
            var (n, s, t) = CardRules.Weights(z);
            R.AddDamage(car, amt, n, s, t);
        }
    }

    public readonly struct OddsTag
    {
        public readonly double Value;
        public readonly string Label, Kind;
        public OddsTag(double v, string label, string kind) { Value = v; Label = label; Kind = kind; }
        public override string ToString() => $"{(Value > 0 ? "+" : "")}{Value} {Label}";
    }

    /// <summary>The slice's R object: a running read with labelled factors, and the factors summed by kind for the odds chips.</summary>
    public sealed class OddsBuilder
    {
        public double V;
        public readonly List<OddsTag> Tags = new List<OddsTag>();
        public readonly Dictionary<string, double> Mods = new Dictionary<string, double>();

        /// <summary>Add to the base without a label.</summary>
        public void B(double x) => V += x;

        /// <summary>Add a labelled factor. Zero is ignored.</summary>
        public void A(double x, string label, string kind = null)
        {
            if (x == 0) return;
            V += x;
            Tags.Add(new OddsTag(x, label, kind));
            if (kind != null) Mods[kind] = (Mods.TryGetValue(kind, out var m) ? m : 0) + x;
        }
    }

    /// <summary>A card's odds right now: the read, the five outcome chances and what moved them.</summary>
    public sealed class Odds
    {
        /// <summary>The read on the slice's 0-100 scale and its spread.</summary>
        public double Read, Sd;
        /// <summary>Chance of each outcome, indexed by (int)Tier.</summary>
        public readonly double[] P = new double[5];
        /// <summary>Factors of 3 or more, biggest first.</summary>
        public List<OddsTag> Tags;
        public Dictionary<string, double> Mods;
        /// <summary>How wide the odds range on the card is drawn (Spotter skill and the Spotter Headset narrow it).</summary>
        public double Range;
        /// <summary>The chance of success without the boost, combo, setup, tune, Heat and damage factors.</summary>
        public double BaseSucc;

        public double this[Tier t] => P[(int)t];
        public double Succ => P[(int)Tier.Success] + P[(int)Tier.CritSuccess];
        public double Wreck => P[(int)Tier.CritFail];

        /// <summary>The slice's sampleTier: one roll walked from critical failure up.</summary>
        public Tier Sample(SimRng rng)
        {
            double r = rng.NextDouble();
            for (int i = 0; i < 5; i++) { r -= P[i]; if (r <= 0) return (Tier)i; }
            return Tier.CritSuccess;
        }
    }

    public static class OddsMath
    {
        /// <summary>Abramowitz-Stegun erf, the same formula as the slice so the odds match to the last digit.</summary>
        public static double Erf(double x)
        {
            double s = Math.Sign(x);
            x = Math.Abs(x);
            double t = 1 / (1 + 0.3275911 * x);
            double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return s * y;
        }

        public static double Phi(double z) => 0.5 * (1 + Erf(z / Math.Sqrt(2)));
    }
}
