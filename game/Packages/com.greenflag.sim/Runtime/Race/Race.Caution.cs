using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum CautionStage { Closed, Open, Pitting, OneToGo }

    public sealed class Caution
    {
        public int Id;
        public string Reason;
        public CautionStage Stage;
        public double LeadAt, OpenAt, PitAt;
        public readonly Dictionary<RaceCar, PitService> Plans = new Dictionary<RaceCar, PitService>();
        /// <summary>The first lapped car gets the free pass (lucky dog).</summary>
        public RaceCar Dog;
        /// <summary>Your call: the crew chief's pick until you change it with PressPit or SetCautionCall.</summary>
        public PitService Mine, Chief;
        public PitService? LastPit;
        /// <summary>Your call is locked once you pass pit entry.</summary>
        public bool Done;
    }

    /// <summary>
    /// The caution sequence: the field forms up behind the pace car, pit road opens the next time by, cars that stay out keep
    /// track position, and at one to go the free pass and the wave-around send lapped cars to the tail.
    /// Karts have no cautions.
    /// </summary>
    public sealed partial class Race
    {
        public Caution Caution;
        Dictionary<RaceCar, RaceCar> physAhead;

        /// <summary>Choose your call while the caution's pit window is open.</summary>
        public void SetCautionCall(PitService svc)
        {
            if (Phase == RacePhase.Caution && Caution != null && !Caution.Done) Caution.Mine = svc;
        }

        public void StartCaution(string reason)
        {
            if (Phase != RacePhase.Green) return;
            if (Track.Kart) { pendingCautionAt = -1; return; }
            if (CardsOn) { CardsCaution(); RadioCaution(); }
            Phase = RacePhase.Caution; PhaseT = 0; Cautions++; PitBonus = 0;
            CautionInit(reason);
            Emit(RaceEventKind.Caution, null, null, reason);
        }

        double CrewSkill => SimMath.Clamp((Mods.CrewChief - 3) / 5.0, 0, 1);
        static PitService SmartCall(RaceCar c) => c.Tires >= 60 && c.Damage < 25 ? PitService.Stay : c.Tires >= 40 ? PitService.Two : PitService.Four;
        int StopsLeft(double fuel, double toGo) => Math.Max(0, (int)Math.Ceiling((toGo + 0.15 - fuel) / FuelRange));
        bool CheapStop(RaceCar c) { double toGo = Track.Laps - c.Dist; return StopsLeft(c.Fuel / c.Burn, toGo) > StopsLeft(FuelRange, toGo); }

        PitService MyCautionCall()
        {
            var c = Me;
            double toGo = Track.Laps - c.Dist;
            bool needFuel = c.Fuel / c.Burn < toGo + 0.15 && (CheapStop(c) || c.Tires < 60), down = LapsDown(c) > 0;
            if (down && (Caution.Dog == c || !needFuel)) return PitService.Stay;
            if (Bal.ChiefSmart && PlayerPlays > 0) return ChiefCall(c, toGo, needFuel);
            if (needFuel)
            {
                if (Bal.PitAuto == 2) return PitService.Four;
                return Rng.NextDouble() < CrewSkill ? (c.Tires >= 62 ? PitService.Two : PitService.Four) : PitService.Four;
            }
            if (Bal.PitAuto == 3) return Rng.NextDouble() < CrewSkill ? SmartCall(c) : PitService.Four;
            if (Bal.PitAuto == 2) return PitService.Four;
            return c.Tires < 45 || c.Damage > 25 ? PitService.Four : c.Tires < 70 ? PitService.Two : PitService.Stay;
        }

        /// <summary>What a real crew chief calls: stay out on good tires, fuel only when the tank is the problem, tires when they're worn.</summary>
        static PitService ChiefCall(RaceCar c, double toGo, bool needFuel)
        {
            if (c.Damage >= 25 && toGo > 2) return PitService.Four;
            if (needFuel)
            {
                if (c.Tires >= 75 || toGo < 4) return PitService.Fuel;
                return c.Tires >= 50 ? PitService.Two : PitService.Four;
            }
            if (toGo < 3 || c.Tires >= 65) return PitService.Stay;
            return c.Tires >= 45 ? PitService.Two : PitService.Four;
        }

        PitService AiCautionCall(RaceCar c)
        {
            double toGo = Track.Laps - c.Dist;
            bool needFuel = c.Fuel / c.Burn < toGo + 0.15 && (CheapStop(c) || c.Tires < 60), down = LapsDown(c) > 0;
            if (down) return Caution.Dog == c || !needFuel ? PitService.Stay : PitService.Four;
            if (needFuel)
            {
                if (toGo < FuelRange * 0.35 && c.Tires > 55) return Rng.NextDouble() < 0.5 ? PitService.Fuel : PitService.Two;
                return c.Tires < 70 || Rng.NextDouble() < 0.6 ? PitService.Four : PitService.Two;
            }
            if (c.Tires < 45 || c.Damage > 25) return Rng.NextDouble() < 0.85 ? PitService.Four : PitService.Two;
            if (c.Tires < 72) return Rng.Pick(new[] { PitService.Two, PitService.Two, PitService.Four, PitService.Stay });
            return PitService.Stay;
        }

        void CautionInit(string reason)
        {
            var lead = Order.Find(x => !x.Dnf && x.Pit == null) ?? Order[0];
            var k = Caution = new Caution { Id = Cautions, Reason = reason, Stage = CautionStage.Closed, LeadAt = lead.Dist };
            k.Dog = Order.Find(x => !x.Dnf && LapsDown(x, lead) >= 1);
            foreach (var c in Cars)
            {
                c.Overtake = null;
                if (!c.Dnf && !c.IsMe) k.Plans[c] = AiCautionCall(c);
            }
            k.Mine = k.Chief = MyCautionCall();
            PaceCar = new RaceCar { IsPace = true, Id = -1, Dist = lead.Dist + 0.03 };
        }

        void CautionTick()
        {
            var k = Caution;
            var g = Shape.Pit;
            var run = Order.FindAll(x => !x.Dnf);
            var lead = run.Find(x => x.Pit == null) ?? (run.Count > 0 ? run[0] : null);
            if (lead == null) return;
            if (k.Stage == CautionStage.Closed && lead.Dist - k.LeadAt >= Bal.OpenAfter)
            {
                k.Stage = CautionStage.Open; k.OpenAt = lead.Dist;
                Emit(RaceEventKind.PitRoadOpen);
            }
            if (k.Stage == CautionStage.Open && (lead.Dist - k.OpenAt >= 1.05 || run.TrueForAll(x => x.CPass == k.Id || x.Pit != null)))
            {
                k.Stage = CautionStage.Pitting; k.PitAt = T;
            }
            if (k.Stage == CautionStage.Pitting && (!run.Exists(x => x.Pit != null) || T - k.PitAt > 30))
            {
                OneToGo();
                k.Stage = CautionStage.OneToGo; k.LeadAt = lead.Dist;
            }
            if (k.Stage == CautionStage.OneToGo)
            {
                var ld = Order.Find(x => !x.Dnf);
                if (ld != null && ((lead.Dist - k.LeadAt >= 0.15 && CrossedAt(ld.PrevDist, ld.Dist, g.E0 - 0.06)) || PhaseT > Track.LapTime / Bal.PitPace * 5))
                {
                    Phase = RacePhase.Restart; PhaseT = 0;
                    if (PaceCar != null) PaceCar.Leaving = true;
                    Emit(RaceEventKind.Restart);
                }
            }
        }

        /// <summary>Line the field up for the restart: lead lap, then pit-road penalties, the lucky dog, the wave-arounds, the rest.</summary>
        void OneToGo()
        {
            var k = Caution;
            var run = Order.FindAll(x => !x.Dnf);
            if (run.Count == 0) return;
            var lead = run[0];
            var on = run.FindAll(x => LapsDown(x, lead) == 0);
            var down = run.FindAll(x => LapsDown(x, lead) > 0);
            var dog = k.Dog != null && down.Contains(k.Dog) ? k.Dog : null;
            var waves = down.FindAll(x => x != dog && x.CPit != k.Id && x.CPass == k.Id);
            var rest = down.FindAll(x => x != dog && !waves.Contains(x));
            var line = new List<RaceCar>();
            line.AddRange(on.FindAll(x => x.Penal != k.Id));
            line.AddRange(on.FindAll(x => x.Penal == k.Id));
            if (dog != null) line.Add(dog);
            line.AddRange(waves);
            line.AddRange(rest);
            // laps down are measured before anyone moves
            var downBy = new Dictionary<RaceCar, int>();
            foreach (var c in rest) downBy[c] = LapsDown(c, lead);
            double gap = minGap * 1.6, leadDist = lead.Dist;
            for (int i = 0; i < line.Count; i++)
            {
                var c = line[i];
                c.Dist = leadDist - i * gap - (downBy.TryGetValue(c, out int d) ? d : 0);
            }
            var dnfs = Order.FindAll(x => x.Dnf);
            Order.Clear(); Order.AddRange(line); Order.AddRange(dnfs);
            if (PaceCar != null) PaceCar.Dist = leadDist + 0.03;
            k.Done = true;
            Emit(RaceEventKind.OneToGo);
            if (dog != null) Emit(RaceEventKind.LuckyDog, dog);
            foreach (var w in waves) Emit(RaceEventKind.WaveAround, w);
            foreach (var p in on) if (p.Penal == k.Id) Emit(RaceEventKind.PitPenalty, p);
        }

        /// <summary>The pace car leads the field under caution and peels off onto pit road at the restart.</summary>
        void PaceTick(double dt)
        {
            var pc = PaceCar;
            if (pc == null) return;
            var g = Shape.Pit;
            pc.PrevDist = pc.Dist;
            if (!pc.Leaving) { pc.Dist += baseV * Bal.PitPace * dt; pc.Alpha = Math.Min(1, pc.Alpha + dt * 0.8); return; }
            pc.Dist += baseV * 0.6 * dt;
            double s = SOf(pc.Dist);
            if (s > g.E0 && s < g.X1) pc.LeftTrack = true;
            if (pc.LeftTrack) pc.Alpha -= dt * 0.5;
            if (pc.Alpha <= 0 || (Phase == RacePhase.Green && PhaseT > 8)) PaceCar = null;
        }

        /// <summary>Under caution and restarts each car closes up on whatever is physically ahead of it (the pace car, or the tail of the field).</summary>
        void PhysAhead()
        {
            var S = Cars.FindAll(x => !x.Dnf && x.Pit == null);
            var pc = PaceCar != null && !PaceCar.Leaving ? PaceCar : null;
            if (pc != null) S.Add(pc);
            SimMath.StableSort(S, (a, b) => SimMath.Frac(a.Dist).CompareTo(SimMath.Frac(b.Dist)));
            var m = new Dictionary<RaceCar, RaceCar>();
            for (int i = 0; i < S.Count; i++) m[S[i]] = S[(i + 1) % S.Count];
            if (pc == null && S.Count > 0)
            {
                var top = S[0];
                foreach (var x in S) if (x.Dist > top.Dist) top = x;
                m.Remove(top);
            }
            physAhead = m;
        }

        double FollowV(RaceCar c, double tgt)
        {
            if (physAhead == null || !physAhead.TryGetValue(c, out var a) || a == c) return baseV * Bal.PitPace;
            double gap = SimMath.Frac(a.Dist - c.Dist), want = a.IsPace ? tgt * 1.8 : tgt;
            return SimMath.Clamp(baseV * Bal.PitPace + (gap - want) * 2.2, 0, baseV * 1.3);
        }
    }
}
