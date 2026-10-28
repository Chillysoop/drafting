using System;

namespace GreenFlag.Sim
{
    public enum PitStrategy { Safe, Gamble }

    public readonly struct PitPlan
    {
        public readonly int Lap;
        public readonly PitService Service;
        public PitPlan(int lap, PitService svc) { Lap = lap; Service = svc; }
    }

    /// <summary>The window for the next stop: the last lap you can reach pit entry on fuel, and the first lap one more tank reaches the flag from.</summary>
    public readonly struct FuelWindow
    {
        public readonly int Now, First, Last;
        public readonly bool Short;
        public FuelWindow(int now, int first, int last, bool sh) { Now = now; First = first; Last = last; Short = sh; }
    }

    /// <summary>
    /// Pit road and fuel. Cars really drive pit road: they leave the track at entry, slow to the limit, stop in their stall and
    /// rejoin at exit, so track position falls out of the time lost. Everyone needs at least one stop on fuel (not karts).
    /// </summary>
    public sealed partial class Race
    {
        public PitStrategy Strategy = PitStrategy.Safe;
        public PitPlan? SafePlan, GamblePlan;
        /// <summary>Your green-flag pit request (the PIT button): you come in the next time you reach pit entry.</summary>
        public PitService? PitRequest;
        /// <summary>Crew skill bonus seconds for your next stop (cards).</summary>
        public double PitBonus;

        public PitPlan? MyPlan => Strategy == PitStrategy.Safe ? SafePlan : GamblePlan;
        public int FuelRange => Track.FuelRange > 0 ? Track.FuelRange : (int)Math.Ceiling(Track.Laps * 0.6);

        static double SOf(double d) => d - SimMath.RoundJs(d);
        static bool CrossedAt(double pd, double d, double s0) => Math.Floor(pd - s0) < Math.Floor(d - s0);

        public int LapsDown(RaceCar c, RaceCar lead = null)
        {
            lead = lead ?? Order.Find(x => !x.Dnf);
            return lead != null ? Math.Max(0, (int)Math.Floor(lead.Dist - c.Dist + 1e-6)) : 0;
        }

        void PitInit()
        {
            var g = Shape.Pit;
            double R = FuelRange, span = g.X0 - g.E1;
            foreach (var c in Cars)
            {
                c.Fuel = R;
                c.Burn = c.IsMe ? 1 : 1 + Rng.Gauss() * 0.015;
                c.Pit = null;
                c.Stall = g.X0 - span * (0.06 + 0.86 * (c.Start - 1) / Math.Max(1, Cars.Count - 1));
                c.EarlyStyle = Rng.NextDouble() < 0.5;
                if (!c.IsMe) AiPlan(c);
            }
            MyPlans();
        }

        public FuelWindow? FuelWindowOf(RaceCar c)
        {
            int L = Track.Laps;
            double toGo = L - c.Dist;
            if (c.Fuel * (1 / c.Burn) >= toGo + 0.15) return null;
            int now = LapOf(c), last = Math.Max(now, (int)Math.Floor(c.Dist + c.Fuel / c.Burn + 0.05)), first = (int)Math.Ceiling(L - FuelRange + 0.2);
            return new FuelWindow(now, first, last, toGo - c.Fuel / c.Burn <= FuelRange * 0.35);
        }

        void AiPlan(RaceCar c)
        {
            var ow = FuelWindowOf(c);
            if (ow == null) { c.PlanLap = 0; return; }
            var w = ow.Value;
            if (w.Short)
            {
                c.PlanLap = (int)SimMath.Clamp(w.Last - Math.Floor(Rng.NextDouble() * 3), w.Now, w.Last);
                c.PlanSvc = Rng.NextDouble() < 0.5 ? PitService.Fuel : PitService.Two;
                return;
            }
            int hi = w.Last, lo = w.First <= w.Now ? w.Now + (hi - w.Now) / 2 : Math.Max(w.Now, Math.Min(w.First, w.Last));
            c.PlanLap = c.EarlyStyle ? lo + (int)Math.Floor(Rng.NextDouble() * Math.Max(1, (hi - lo) / 2.0 + 1)) : hi - (int)Math.Floor(Rng.NextDouble() * 2);
            c.PlanLap = (int)SimMath.Clamp(c.PlanLap, w.Now, hi);
            c.PlanSvc = !c.EarlyStyle && Rng.NextDouble() < 0.4 ? PitService.Two : PitService.Four;
        }

        /// <summary>Two plans from the crew chief. Safe pits early in the window for four tires; Gamble stretches the fuel and takes two.</summary>
        void MyPlans()
        {
            var ow = FuelWindowOf(Me);
            if (ow == null) { SafePlan = GamblePlan = null; return; }
            var w = ow.Value;
            if (w.Short)
            {
                SafePlan = new PitPlan((int)SimMath.Clamp(Math.Max(w.First, w.Last - 2), w.Now, w.Last), PitService.Two);
                GamblePlan = new PitPlan(w.Last, PitService.Fuel);
                return;
            }
            int safe = w.First <= w.Now ? w.Now + (w.Last - w.Now) / 2 : (int)SimMath.Clamp(w.First, w.Now, w.Last);
            SafePlan = new PitPlan(safe, PitService.Four);
            GamblePlan = new PitPlan(w.Last, PitService.Two);
        }

        /// <summary>The PIT button on green: toggles a request for the plan's service (four tires without a plan).</summary>
        public void PressPit()
        {
            if (Phase == RacePhase.Finished || Me.Dnf || Me.Pit != null || Track.Kart) return;
            if (Phase == RacePhase.Caution && Caution != null && !Caution.Done)
            {
                if (Caution.Mine == PitService.Stay) Caution.Mine = Caution.LastPit ?? PitService.Four;
                else { Caution.LastPit = Caution.Mine; Caution.Mine = PitService.Stay; }
            }
            else if (Phase == RacePhase.Green)
            {
                if (PitRequest != null) PitRequest = null;
                else PitRequest = MyPlan?.Service ?? PitService.Four;
            }
        }

        public double StopTime(RaceCar c, PitService svc, bool expected)
        {
            double R = FuelRange;
            double tire = svc == PitService.Two ? Bal.TireTwo : svc == PitService.Four ? Bal.TireFour : 0;
            double d = Math.Max(tire, Bal.FuelBase + Bal.FuelT * SimMath.Clamp(1 - c.Fuel / R, 0, 1));
            d *= c.IsMe ? 1 - (Mods.CrewChief - 5) * 0.03 : (1 - (c.Ref.TeamRating - 6) * 0.025) * Bal.AiStop;
            if (c.IsMe) d -= PitBonus + Mods.PitSeconds;
            if (!expected)
            {
                d += Rng.Gauss() * 0.35;
                double slipP = c.IsMe ? SimMath.Clamp(0.1 - Mods.CrewChief * 0.01, 0.02, 0.1) : 0.06;
                if (Rng.NextDouble() < slipP) { double x = Rng.Rand(2, 4); d += x; c.PitSlip = x; }
            }
            return Math.Max(2.5, d);
        }

        void EnterPit(RaceCar c, PitService svc)
        {
            c.Pit = new PitVisit { Stage = PitStage.In, Service = svc, T0 = T };
            c.Overtake = null; c.PitSlip = 0;
            if (Phase == RacePhase.Caution)
            {
                c.CPit = Caution.Id;
                if (Caution.Stage == CautionStage.Closed) c.Penal = Caution.Id;
            }
            if (c.IsMe) PitRequest = null;
            Emit(RaceEventKind.PitIn, c, null, svc.ToString());
        }

        void ServicePit(RaceCar c)
        {
            var svc = c.Pit.Service;
            if (svc == PitService.Two) c.Tires = Math.Min(100, c.Tires * 0.35 + 62);
            if (svc == PitService.Four) { c.Tires = 100; c.Damage = Math.Max(0, c.Damage - 12); ReconcileZones(c); }
            c.Fuel = FuelRange; c.LastPitLap = LapOf(c); c.PitStops++;
            Emit(RaceEventKind.PitService, c, null, $"{svc} {c.Pit.Until - c.Pit.T1:F1} s{(c.PitSlip > 0 ? " (slow)" : "")}");
            if (c.IsMe) { PitBonus = 0; MyPlans(); } else AiPlan(c);
        }

        /// <summary>What a car does when it reaches pit entry. Null = stays on track.</summary>
        PitService? PitCall(RaceCar c)
        {
            if (Track.Kart) return null;
            double toGo = Track.Laps - c.Dist;
            int lap = LapOf(c);
            bool dry = c.Fuel < 1.02 * c.Burn;
            if (toGo < 0.5) return null;
            if (Phase == RacePhase.Caution)
            {
                var k = Caution;
                if (c.CPass == k.Id) return null;
                if (k.Stage != CautionStage.Open)
                {
                    if (dry) { c.CPass = k.Id; return PitService.Fuel; }
                    return null;
                }
                c.CPass = k.Id;
                PitService ch = c.IsMe ? k.Mine : k.Plans.TryGetValue(c, out var p) ? p : PitService.Stay;
                if (c.IsMe) k.Done = true;
                return ch != PitService.Stay ? ch : (PitService?)null;
            }
            if (Phase != RacePhase.Green) return null;
            if (c.IsMe)
            {
                if (PitRequest != null) return PitRequest;
                if (c.Fuel <= 0.02) return PitService.Four;
                return null;
            }
            if (c.PlanLap > 0 && lap >= c.PlanLap) return c.PlanSvc;
            if (dry) return PitService.Four;
            if (c.Tires < 18 && toGo > 4) return PitService.Four;
            return null;
        }

        /// <summary>Speed on pit road: brake to the limit through the entry, stop in the stall, accelerate out through the exit.</summary>
        double PitV(RaceCar c, double v)
        {
            var g = Shape.Pit;
            var p = c.Pit;
            double s = SOf(c.Dist), lim = baseV * Bal.PitLimit;
            if (p == null) return v * 0.3; // out of fuel on track: coasting to pit road
            if (p.Stage == PitStage.In)
            {
                if (s < g.E1) return Math.Min(v, baseV * (1 - (1 - Bal.PitLimit) * SimMath.Clamp((s - g.E0) / (g.E1 - g.E0), 0, 1)));
                double rem = c.Stall - s;
                if (rem <= 0)
                {
                    c.Dist = SimMath.RoundJs(c.Dist) + c.Stall;
                    p.Stage = PitStage.Box; p.T1 = T; p.Until = T + StopTime(c, p.Service, false);
                    return 0;
                }
                return lim * SimMath.Clamp(rem / 0.012 + 0.12, 0.12, 1);
            }
            if (p.Stage == PitStage.Box)
            {
                if (T >= p.Until) { ServicePit(c); p.Stage = PitStage.Out; }
                return 0;
            }
            if (s < g.X0) return lim * SimMath.Clamp((s - c.Stall) / 0.01 + 0.25, 0.25, 1);
            double f = SimMath.Clamp((s - g.X0) / (g.X1 - g.X0), 0, 1);
            return lim + (Math.Max(v, lim) - lim) * f * f;
        }

        /// <summary>After a car moves: burn fuel, and act at pit entry and exit.</summary>
        void PitStep(RaceCar c, double pd)
        {
            if (c.Dnf) return;
            var g = Shape.Pit;
            c.Fuel = Math.Max(0, c.Fuel - (c.Dist - pd) * c.Burn * (Phase == RacePhase.Caution ? Bal.CautionBurn : 1));
            if (c.Fuel <= 0 && c.DryAt == 0) { c.DryAt = T; Emit(RaceEventKind.OutOfFuel, c); }
            if (c.Fuel > 0) c.DryAt = 0;
            if (c.Pit != null && c.Pit.Stage == PitStage.Out && CrossedAt(pd, c.Dist, g.X1)) { c.Pit = null; Emit(RaceEventKind.PitOut, c); return; }
            if (c.Pit == null && CrossedAt(pd, c.Dist, g.E0))
            {
                var svc = PitCall(c);
                if (svc != null) EnterPit(c, svc.Value);
            }
        }
    }
}
