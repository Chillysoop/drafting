using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>A wreck just ahead of you, or traffic at the figure-8 crossover: a short window to play Evade.</summary>
    public sealed class Hazard
    {
        public double Until;
        public bool Crossover;
    }

    public sealed partial class Race
    {
        public Hazard Hazard;
        double f8Cooldown;
        int f8MyK = int.MinValue;

        /// <summary>
        /// The low-stakes move every car makes every 15 to 20 s: try a pass, block you, work the draft or save tires.
        /// Your own car runs this too, but outside Easy it never passes on autopilot: positions come from cards.
        /// </summary>
        void AutoMove(RaceCar car)
        {
            var D = Diff;
            var a = AheadOf(car);
            double g = a != null ? GapSec(a, car) : double.PositiveInfinity;
            car.NextMove = T + Rng.Rand(15, 20) * (car.Trait == Trait.Aggressive ? 0.85 : 1) / (car.IsMe ? 1 : D.Aggr);
            bool autoPass = !car.IsMe || D.Level == DifficultyLevel.Easy;
            if (a != null && a.Pit == null && g < 0.55 && autoPass)
            {
                double read = 46 + (PaceMult(car) - PaceMult(a)) * 900 + (car.Stats.Racecraft - a.Stats.Defense) * 3 - (Track.PassDiff - 1) * 25 + ShapeAi(car);
                if (car.Trait == Trait.Aggressive) read += 8;
                if (!car.IsMe) read += (D.Aggr - 1) * 20;
                if (car.Trait == Trait.Closer && Leader.Dist > Track.Laps * 0.8) read += 10;
                if (car.Trait == Trait.Restarter && T - LastGreenAt < 12) read += 15;
                if (a.Trait == Trait.Wall) read -= 10;
                // defending makes a pass harder, but should not make the attacker more likely to crash
                double def = 0;
                if (a.HasEffect(EffectKind.Block)) def += 35;
                if (a.HasEffect(EffectKind.Hold)) def += 30;
                if (a.IsMe) read += D.VsMe + (Heat >= 3 ? Heat * 4 : 0) + IdlePressure();
                if (car.IsMe) read += D.Auto;
                double sd = Math.Max(8, 16 - car.Stats.Consistency * 0.6) + (car.Trait == Trait.Rookie ? 4 : 0);
                double raw = read + Rng.Gauss() * sd, roll = raw - def;
                double cfT = 6 * Track.Contact;
                if (car.Trait == Trait.Aggressive) cfT += 3;
                if (car.Trait == Trait.Veteran) cfT -= 3;
                if (car.IsMe && D.Level == DifficultyLevel.Easy) cfT = -99;
                double trackK = Bal.TrackWreck && Track.AiWreckK.HasValue ? Track.AiWreckK.Value : 1;
                if (raw < cfT && Rng.NextDouble() < Bal.AiWreck * trackK)
                    Wreck(car, Rng.NextDouble() < (a.IsMe ? 0.3 : 0.5) ? a : null, "gets loose making a move");
                else if (roll < 30) { Slow(car, 0.25); Emit(RaceEventKind.AutoLift, car, a); }
                else if (roll < 55) Emit(RaceEventKind.AutoAlongside, car, a);   // side by side for a moment: presentation only
                else
                {
                    StartOvertake(car, 1);
                    if (roll > 92) AddPace(car, 1.01, 6);
                }
                return;
            }
            if (!car.IsMe)
            {
                var b = BehindOf(car);
                if (b != null && b.IsMe && GapSec(car, b) < 0.6 && Rng.NextDouble() < D.AiBlock * 3 * (1 + Heat * 0.5))
                {
                    BlockFor(car, 8);
                    Emit(RaceEventKind.Block, car, b);
                    return;
                }
            }
            if (a != null && g < Track.DraftRange) { AddPace(car, 1.012, 6); car.Tires = Math.Max(5, car.Tires - 1.5); }
            else if (car.Tires < 55) WearFx(car, 0.7, 15);
        }

        /// <summary>AI cars feel the shape of a road course: they pass in the hairpins and get stuck in the esses.</summary>
        double ShapeAi(RaceCar car)
        {
            if (!Track.IsPath) return 0;
            var s = Shape.SpotOf(car.Dist);
            return s.Kind == TurnKind.Hairpin && s.Approach ? 8 : s.Kind == TurnKind.Esses ? -12 : s.Kind == TurnKind.Sweeper ? -6 : 0;
        }

        /// <summary>A wreck just ahead of you (1 to 3 places, inside 1.6 s) opens an Evade window.</summary>
        void MaybeHazard(List<RaceCar> cars)
        {
            var m = Me;
            if (m.Dnf || Hazard != null || Phase != RacePhase.Green || cars.Contains(m)) return;
            int mi = IndexOf(m);
            bool near = false;
            foreach (var c in cars) { int d = mi - IndexOf(c); if (d >= 1 && d <= 3 && GapSec(c, m) < 1.6) near = true; }
            if (!near) return;
            OfferHazard(false);
        }

        void OfferHazard(bool crossover)
        {
            Hazard = new Hazard { Until = T + Bal.HazardWin, Crossover = crossover };
            Emit(RaceEventKind.HazardOffered, Me, null, crossover ? "Traffic at the crossover" : "Wreck ahead");
            if (CardsOn) CardsHazard(true);
        }

        /// <summary>
        /// React to the hazard. With cards this plays the Evade card (it resolves on its odds); without, you always get slowed in time.
        /// </summary>
        public void EvadeHazard()
        {
            if (Hazard == null) return;
            if (CardsOn && HazardCard != null) PlayCard(HazardCard.Uid);
            else EndHazard(true);
        }

        void EndHazard(bool played)
        {
            Hazard = null;
            if (CardsOn) CardsHazard(false);
            var m = Me;
            if (played) { Emit(RaceEventKind.HazardDodged, m); return; }
            if (!m.Dnf && Rng.NextDouble() < Bal.HazardCatch)
            {
                m.Spinning = 1.4;
                Slow(m, Rng.Rand(2.5, 4));
                DamageSpin(m, Rng.Rand(10, 22) * Track.Contact);
                Emit(RaceEventKind.HazardCollected, m);
            }
            else Emit(RaceEventKind.HazardDodged, m, null, "squeezed through");
        }

        // ---------------------------------------------------------------- figure-8

        (int leg, double s) F8Next(RaceCar car, double[] x)
        {
            double u = Shape.UOf(car.Dist), a = Shape.UGap(u, x[0]) * Track.LapTime, b = Shape.UGap(u, x[1]) * Track.LapTime;
            return a < b ? (0, a) : (1, b);
        }

        /// <summary>Twice a lap the legs cross. You get an Evade window when traffic arrives with you; AI cars rarely T-bone.</summary>
        void Fig8Tick()
        {
            if (Phase != RacePhase.Green) return;
            var x = Shape.CrossoverU();
            var L = new List<(RaceCar c, int leg, double s)>();
            foreach (var c in Cars) if (!c.Dnf && c.Pit == null) { var n = F8Next(c, x); L.Add((c, n.leg, n.s)); }
            int mi = L.FindIndex(q => q.c.IsMe);
            if (mi >= 0 && Bal.Hazard)
            {
                var m = L[mi];
                int k = (int)Math.Floor(m.c.Dist * 2 + 0.5);
                if (m.s > 0.6 && m.s < 1.4 && f8MyK != k && T >= f8Cooldown)
                {
                    f8MyK = k;
                    bool hit = false;
                    foreach (var q in L) if (!q.c.IsMe && q.leg != m.leg && Math.Abs(q.s - m.s) < Bal.LateCrossWin) hit = true;
                    if (hit && Rng.NextDouble() < Bal.LateCrossMe)
                    {
                        f8Cooldown = T + Bal.LateCrossCd;
                        if (!Me.Dnf && Hazard == null) OfferHazard(true);
                    }
                }
            }
            foreach (var q in L)
            {
                if (q.c.IsMe || q.s > 0.12) continue;
                int k = (int)Math.Floor(q.c.Dist * 2 + 0.5);
                if (q.c.F8k == k) continue;
                q.c.F8k = k;
                int yi = L.FindIndex(z => z.c != q.c && !z.c.IsMe && z.leg != q.leg && Math.Abs(z.s - q.s) < 0.12);
                if (yi >= 0 && Rng.NextDouble() < Bal.LateCrossAi * Bal.AiWreck / 0.2) Wreck(q.c, L[yi].c, "gets T-boned at the crossover");
            }
        }
    }
}
