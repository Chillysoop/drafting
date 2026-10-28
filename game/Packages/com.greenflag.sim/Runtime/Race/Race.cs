using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum RacePhase { Start, Green, Caution, Restart, Finished }

    /// <summary>
    /// One race, advanced in fixed ticks of SimInfo.TickSeconds. A port of the slice's race loop (update, autoMove, pit road,
    /// cautions): same rules and numbers, its own seeded roll order. Everything random comes from Rng; nothing here touches
    /// the engine, the UI or the wall clock, so the same setup and seed always give the same race.
    /// The card layer (Race.Cards, Race.Kart, Race.PitGame) and the crew radio (Race.Radio) run on top when the setup has a Loadout.
    /// </summary>
    public sealed partial class Race
    {
        public readonly TrackDef Track;
        public readonly TrackShape Shape;
        public readonly SimRng Rng;
        public readonly Difficulty Diff;
        public readonly SimBalance Bal;
        public readonly PlayerMods Mods;
        public readonly uint Seed;

        public readonly List<RaceCar> Cars = new List<RaceCar>();
        /// <summary>Running order, leader first, DNFs last. Re-sorted every tick.</summary>
        public readonly List<RaceCar> Order = new List<RaceCar>();
        public readonly RaceCar Me;
        public readonly int Expected;

        public double T, PhaseT;
        public RacePhase Phase = RacePhase.Start;
        public bool Done { get; private set; }
        public int Cautions, Wrecks;
        public double LastGreenAt = 5;
        public RaceCar PaceCar;
        public List<RaceCar> FinalOrder;
        public double FinishedAt;

        // player state the AI reads; the card layer writes these
        /// <summary>Heat from aggressive cards: AI drivers race you harder.</summary>
        public int Heat;
        public double HeatAt;
        /// <summary>When you last played a card. Idle seconds turn into a bonus for cars attacking you.</summary>
        public double LastPlayAt;
        public int PlayerPlays;

        /// <summary>This tick's events. Cleared at the start of every tick.</summary>
        public readonly List<RaceEvent> Events = new List<RaceEvent>();
        /// <summary>Optional: every event of the race, for the harness and tests.</summary>
        public List<RaceEvent> Log;

        readonly double minGap, baseV;
        readonly string qualiCard;
        double pendingCautionAt = -1;
        string pendingCautionReason;
        static readonly string[] DebrisReasons = { "Debris on the track", "A car spun in turn 2", "Fluid on the track" };

        public double MinGap => minGap;

        public Race(RaceSetup s)
        {
            Track = s.Track;
            Shape = TrackShape.Of(s.Track);
            Seed = s.Seed;
            Rng = new SimRng(s.Seed);
            Diff = s.Difficulty;
            Bal = s.Balance;
            Mods = s.Loadout != null ? new PlayerMods() : s.Mods;
            Loadout = s.Loadout;
            PitPolicy = s.PitPolicy;
            qualiCard = s.QualiCard;
            if (Loadout != null) { CardRng = new SimRng(s.Seed ^ 0xC4A2D5EBu); RadioRng = new SimRng(s.Seed ^ 0x7AD10CA1u); }
            minGap = Shape.MinGap;
            baseV = 1 / Track.LapTime;

            // qualifying: a given grid, or the slice's roll-off by car rating with a little noise
            List<Entrant> grid;
            if (s.Grid != null)
            {
                grid = new List<Entrant>();
                foreach (int id in s.Grid) grid.Add(s.Field.Find(e => e.Id == id));
            }
            else
            {
                var q = new List<(Entrant e, double q)>();
                foreach (var e in s.Field) q.Add((e, e.Stats.Rating + Rng.Gauss() * 0.003 + (e.IsMe ? -0.002 : 0)));
                SimMath.StableSort(q, (a, b) => b.q.CompareTo(a.q));
                grid = q.ConvertAll(x => x.e);
            }
            for (int i = 0; i < grid.Count; i++)
            {
                var e = grid[i];
                var c = new RaceCar
                {
                    Ref = e, Id = e.Id, IsMe = e.IsMe, Stats = e.Stats, Trait = e.Trait, Team = e.Team, Start = i + 1,
                    Dist = -(i * minGap * 1.4) - 0.004, NextMove = Rng.Rand(6, 20),
                };
                Cars.Add(c);
                if (c.IsMe) Me = c;
            }
            Order.AddRange(Cars);
            if (Me == null) throw new ArgumentException("The field needs one car with IsMe");
            PitInit();

            double mine = Me.Stats.Rating;
            Expected = 1;
            foreach (var c in Cars) if (!c.IsMe && c.Stats.Rating > mine) Expected++;
            if (Loadout != null) CardsInit();
        }

        // ---------------------------------------------------------------- helpers

        public int IndexOf(RaceCar c) => Order.IndexOf(c);
        public RaceCar AheadOf(RaceCar c) { int i = Order.IndexOf(c); return i > 0 ? Order[i - 1] : null; }
        public RaceCar BehindOf(RaceCar c) { int i = Order.IndexOf(c); var b = i >= 0 && i + 1 < Order.Count ? Order[i + 1] : null; return b != null && !b.Dnf ? b : null; }
        public RaceCar Leader => Order[0];
        public double GapSec(RaceCar a, RaceCar b) => (a.Dist - b.Dist) * Track.LapTime;
        public int LapOf(RaceCar c) => (int)SimMath.Clamp(Math.Floor(c.Dist) + 1, 1, Track.Laps);
        public int Position(RaceCar c) => Order.IndexOf(c) + 1;
        public TrackSpot SpotOf(RaceCar c) => Shape.SpotOf(c.Dist);

        void Emit(RaceEventKind k, RaceCar car = null, RaceCar other = null, string text = null, string card = null, int tier = -1)
        {
            var e = new RaceEvent(k, T, car != null && !car.IsPace ? car.Id : -1, other != null && !other.IsPace ? other.Id : -1, text, card, tier);
            Events.Add(e);
            Log?.Add(e);
        }

        // ---------------------------------------------------------------- effects (cards call these)

        public void AddPace(RaceCar car, double mult, double sec) { car?.Effects.Add(new CarEffect { Kind = EffectKind.Pace, Mult = mult, Until = T + sec }); }

        /// <summary>Lose about lossSec seconds: 60% pace until then, and the car yields to the one behind.</summary>
        public void Slow(RaceCar car, double lossSec)
        {
            if (car == null) return;
            const double m = 0.6;
            car.Effects.Add(new CarEffect { Kind = EffectKind.Pace, Mult = m, Until = T + lossSec / (1 - m), Yield = true });
        }

        public void WearFx(RaceCar car, double mult, double sec) { car?.Effects.Add(new CarEffect { Kind = EffectKind.Wear, Mult = mult, Until = T + sec }); }
        public void BlockFor(RaceCar car, double sec) { car?.Effects.Add(new CarEffect { Kind = EffectKind.Block, Until = T + sec }); }
        public void HoldFor(RaceCar car, double sec) { car?.Effects.Add(new CarEffect { Kind = EffectKind.Hold, Until = T + sec }); }

        /// <summary>Start a pass on the car ahead (n cars in a row). Too far back, it becomes a short run at +6% instead.</summary>
        public bool StartOvertake(RaceCar car, int n)
        {
            var a = AheadOf(car);
            if (a == null || car.Dnf) return false;
            double g = GapSec(a, car);
            if (g > 1.0) { AddPace(car, 1.06, Math.Min(6, g * 0.7 / 0.06)); return false; }
            car.Overtake = new Overtake { Target = a, Until = T + 2 + Track.LapTime * 0.25, Left = n - 1 };
            return true;
        }

        /// <summary>Damage after durability, split over the zones by weight (nose, side, tail). 100 ends the car's race.</summary>
        public void AddDamage(RaceCar car, double amt, double wNose, double wSide, double wTail)
        {
            if (car == null) return;
            double m = 1 - (car.Stats.Durability - 5) * 0.06;
            if (car.IsMe) m *= Mods.Damage;
            ReconcileZones(car);
            double before = car.Damage;
            car.Damage = SimMath.Clamp(car.Damage + amt * m, 0, 100);
            if (car.Damage >= 100 && !car.Dnf) SetDnf(car);
            double add = car.Damage - before;
            if (add <= 0) return;
            car.Zones.Nose += add * wNose; car.Zones.Side += add * wSide; car.Zones.Tail += add * wTail;
        }

        // the slice's DZW weights
        internal void DamageWreckA(RaceCar c, double amt) => AddDamage(c, amt, 0.45, 0.35, 0.2);
        internal void DamageWreckB(RaceCar c, double amt) => AddDamage(c, amt, 0.15, 0.4, 0.45);
        internal void DamageSpin(RaceCar c, double amt) => AddDamage(c, amt, 0.2, 0.5, 0.3);

        /// <summary>When damage was repaired, take it off the worst zone first.</summary>
        internal void ReconcileZones(RaceCar car)
        {
            var z = car.Zones;
            double s = z.Sum, d = car.Damage;
            if (d < s - 0.01)
            {
                double fix = s - d;
                while (fix > 0.01)
                {
                    double take;
                    if (z.Nose >= z.Side && z.Nose >= z.Tail) { take = Math.Min(fix, z.Nose); z.Nose -= take; }
                    else if (z.Side >= z.Tail) { take = Math.Min(fix, z.Side); z.Side -= take; }
                    else { take = Math.Min(fix, z.Tail); z.Tail -= take; }
                    if (take <= 0) break;
                    fix -= take;
                }
            }
            else if (d > s + 0.01) z.Side += d - s;
        }

        void SetDnf(RaceCar car)
        {
            car.Dnf = true; car.Effects.Clear(); car.Overtake = null;
            Emit(RaceEventKind.Dnf, car);
        }

        /// <summary>A wreck: the car, maybe the one it hit, and on a big one the cars around it. Usually brings a caution.</summary>
        public void Wreck(RaceCar car, RaceCar other, string why, bool big = false)
        {
            var cars = new List<RaceCar> { car };
            if (other != null && !other.Dnf) cars.Add(other);
            if (big)
            {
                int i = IndexOf(car);
                foreach (int k in new[] { i + 1, i + 2, i - 2 })
                    if (k >= 0 && k < Order.Count && !Order[k].Dnf && !cars.Contains(Order[k])) cars.Add(Order[k]);
            }
            foreach (var c in cars)
            {
                c.Spinning = 2.2;
                Slow(c, Rng.Rand(4, 7));
                double amt = Rng.Rand(30, 55) * Track.Contact * Diff.Wreck * (c == car ? 1 : 0.55) * (c.IsMe ? Bal.WreckDmg : 1);
                if (c == car) DamageWreckA(c, amt); else DamageWreckB(c, amt);
                c.Overtake = null;
            }
            Wrecks++;
            Emit(RaceEventKind.Wreck, car, cars.Count > 1 ? cars[1] : null, why + (cars.Count > 2 ? $" ({cars.Count} cars)" : ""));
            if (Phase == RacePhase.Green && Rng.NextDouble() < 0.85 && pendingCautionAt < 0) { pendingCautionAt = T + 0.9; pendingCautionReason = "Wreck"; }
            if (Bal.Hazard && !car.IsMe) MaybeHazard(cars);
        }

        // ---------------------------------------------------------------- pace

        /// <summary>The slice's paceMult: stats, tires, damage zones, effects, noise and difficulty.</summary>
        public double PaceMult(RaceCar car)
        {
            var s = car.Stats;
            double spec = Track.Kart ? Bal.KartSpec : Track.Late ? Bal.LateSpec : 1;
            double sp = (s.Power - 5) * 0.0032 + (s.Handling - 5) * 0.0024 * Track.HandlingW + (s.Racecraft - 5) * 0.0012;
            double m = 1 + sp * spec;
            m *= 1 - (1 - car.Tires / 100) * Bal.TireGrip;
            m *= RulesPace(car);
            var z = car.Zones;
            m *= 1 - (z.Nose * Bal.DzPaceNose + z.Side * Bal.DzPaceSide + z.Tail * Bal.DzPaceTail) / 100;
            foreach (var e in car.Effects) if (e.Kind == EffectKind.Pace) m *= e.Mult;
            m *= car.Noise * car.PermPace;
            if (car.IsMe) m *= Diff.Me * Mods.Pace; else m *= Diff.Ai;
            return m;
        }

        double DzDraft(RaceCar c) => Math.Max(Bal.DzDraftMin, 1 - c.Zones.Nose * Bal.DzDraft);
        double DzWear(RaceCar c) => 1 + c.Zones.Tail * Bal.DzWear;

        /// <summary>Seconds of green-flag racing since you last played a card turn into a bonus for cars attacking you.</summary>
        public double IdlePressure()
        {
            if (Bal.Pressure == 0) return 0;
            double since = T - Math.Max(LastPlayAt, LastGreenAt);
            return Math.Min(Bal.PressureCap, Math.Max(0, since - 4) * Bal.Pressure);
        }

        // ---------------------------------------------------------------- the tick

        /// <summary>Run the race to the end. Returns the result.</summary>
        public RaceResult RunToEnd(int maxTicks = 20 * 60 * 60)
        {
            for (int i = 0; i < maxTicks && !Done; i++) Tick();
            if (!Done) throw new InvalidOperationException($"Race on {Track.Id} seed {Seed} did not finish in {maxTicks} ticks");
            return Result();
        }

        /// <summary>Advance one fixed tick (SimInfo.TickSeconds).</summary>
        public void Tick()
        {
            if (Done || Holding) return;
            Events.Clear();
            const double dt = SimInfo.TickSeconds;
            if (CardsOn) CardsPreTick();
            T += dt; PhaseT += dt;
            if (CardsOn) CardsClockTick();

            if (Phase == RacePhase.Start && PhaseT >= 5) GoGreen();
            if (Phase == RacePhase.Caution) CautionTick();
            if (Phase == RacePhase.Restart && PhaseT >= 5) GoGreen();
            if (pendingCautionAt >= 0 && T >= pendingCautionAt && !(Hazard != null && T < Hazard.Until))
            {
                pendingCautionAt = -1;
                if (Phase == RacePhase.Green) StartCaution(pendingCautionReason);
            }
            if (Hazard != null && T >= Hazard.Until) EndHazard(false);
            PaceTick(dt);
            if (Phase != RacePhase.Green && Phase != RacePhase.Finished) PhysAhead();

            if (Heat > 0 && T - HeatAt > 25 / HeatCoolK) { Heat--; HeatAt = T; }
            if (CardsOn) CardsFreezeTick(dt);
            foreach (var c in Cars) c.Effects.RemoveAll(e => e.Until <= T);

            var lead = Order[0];
            for (int i = 0; i < Order.Count; i++)
            {
                var c = Order[i];
                if (c.Dnf) continue;
                if (T > c.NoiseAt) { c.Noise = 1 + Rng.Gauss() * 0.003; c.NoiseAt = T + Rng.Rand(2, 4); }
                double v;
                if (Phase == RacePhase.Green || Phase == RacePhase.Finished)
                {
                    v = baseV * PaceMult(c);
                    var a = i > 0 ? Order[i - 1] : null;
                    if (a != null)
                    {
                        double g = GapSec(a, c);
                        if (g < Track.DraftRange)
                        {
                            double dm = Track.Draft * (1 - g / Track.DraftRange) * (0.7 + c.Stats.Aero * 0.06);
                            if (c.Trait == Trait.Drafter) dm *= 1.3;
                            dm *= DzDraft(c);
                            if (c.IsMe) dm *= Mods.Draft;
                            v += baseV * dm;
                        }
                    }
                    double wearRate = 0.24 * Track.Wear * Diff.Wear * (1 - (c.Stats.TireMgmt - 5) * 0.04) * (c.Trait == Trait.Smooth ? 0.85 : 1) * (c.IsMe ? Mods.Wear * MySetupWear : 1);
                    foreach (var e in c.Effects) if (e.Kind == EffectKind.Wear) wearRate *= e.Mult;
                    c.Tires = Math.Max(5, c.Tires - wearRate * DzWear(c) * dt);
                }
                else
                {
                    double ramp = Phase == RacePhase.Restart ? SimMath.Clamp(PhaseT / 2.5, 0, 1) : 0;
                    v = SimMath.Clamp(FollowV(c, minGap * 1.6), 0, baseV * 1.3);
                    if (ramp > 0) v = v * (1 - ramp) + baseV * PaceMult(c) * ramp;
                    if (Phase == RacePhase.Restart && c.IsMe && Mods.Launch && PhaseT < 1.5) v *= 1.04;
                }
                if (c.Overtake != null)
                {
                    var tg = c.Overtake.Target;
                    v += baseV * 0.13;
                    if (tg.Dnf || c.Dist > tg.Dist + minGap * 1.1 || T > c.Overtake.Until)
                    {
                        int left = c.Overtake.Left;
                        c.Overtake = null;
                        if (c.Dist > tg.Dist) { c.Passes++; Emit(RaceEventKind.Pass, c, tg); }
                        if (left > 0)
                        {
                            var na = AheadOf(c);
                            if (na != null && GapSec(na, c) < 0.6) c.Overtake = new Overtake { Target = na, Until = T + 2 + Track.LapTime * 0.25, Left = left - 1 };
                        }
                    }
                }
                if (c.Spinning > 0) { c.Spinning -= dt; v = baseV * 0.12; }
                if (c.Pit != null || c.Fuel <= 0) v = PitV(c, v);
                c.PrevDist = c.Dist; c.Speed = v; c.Dist += v * dt;
                PitStep(c, c.PrevDist);
            }

            // no passing without a move
            for (int i = 1; i < Order.Count; i++)
            {
                RaceCar c = Order[i], a = Order[i - 1];
                if (c.Dnf || a.Dnf) continue;
                if (c.Overtake != null && c.Overtake.Target == a) continue;
                if (a.Overtake != null && a.Overtake.Target == c) continue;
                if (a.Spinning > 0 || c.Pit != null || a.Pit != null) continue;
                bool yields = false;
                foreach (var e in a.Effects) if (e.Yield) { yields = true; break; }
                if (yields) continue;
                double mx = a.Dist - minGap;
                if (c.Dist > mx) c.Dist = mx;
            }
            SortOrder();
            if (CardsOn) CardsAfterMove();

            if (Phase == RacePhase.Green)
            {
                foreach (var c in Order)
                    if (!c.Dnf && c.Pit == null && T >= c.NextMove && c.Spinning <= 0) AutoMove(c);
                if (T - LastGreenAt > 70 && Rng.NextDouble() < 0.0035 * dt) StartCaution(Rng.Pick(DebrisReasons));
            }
            if (CardsOn) { CardsDrawTick(); RadioTick(); }

            if (Phase != RacePhase.Finished && lead.Dist >= Track.Laps)
            {
                Phase = RacePhase.Finished; FinishedAt = T; FinalOrder = new List<RaceCar>(Order);
                Emit(RaceEventKind.Checkered, lead);
            }
            if (Phase == RacePhase.Finished && T - FinishedAt > 3.5)
            {
                if (CardsOn) KartFairingPenalty();
                Done = true; Emit(RaceEventKind.Finished); return;
            }

            if (Track.Kart && Phase != RacePhase.Finished) KartSettle();
            if (Track.Fig8) Fig8Tick();
            if (CardsOn) CardsEndTick(dt);
        }

        void SortOrder() => SimMath.StableSort(Order, (a, b) => a.Dnf != b.Dnf ? (a.Dnf ? 1 : -1) : b.Dist.CompareTo(a.Dist));

        void GoGreen()
        {
            Phase = RacePhase.Green; PhaseT = 0; LastGreenAt = T;
            MyPlans();
            if (CardsOn) CardsGoGreen();
            foreach (var c in Cars) if (c.NextMove < T + 3) c.NextMove = T + Rng.Rand(3, 12);
            Emit(RaceEventKind.GreenFlag);
        }

        /// <summary>Karts run nose to tail: a move that gets you ahead of your target but onto the next kart's bumper is done.</summary>
        void KartSettle()
        {
            foreach (var c in Order)
            {
                var o = c.Overtake;
                if (o == null || c.Dnf) continue;
                var tg = o.Target;
                if (tg.Dnf || IndexOf(c) > IndexOf(tg)) continue;
                var a = AheadOf(c);
                if (a != null && a != tg && a.Dist - c.Dist > minGap * 1.05) continue;
                c.Overtake = null; c.Passes++;
                Emit(RaceEventKind.Pass, c, tg);
                tg.Dist = Math.Min(tg.Dist, c.Dist - minGap);
                if (o.Left > 0)
                {
                    var na = AheadOf(c);
                    if (na != null && GapSec(na, c) < 0.6) c.Overtake = new Overtake { Target = na, Until = T + 2 + Track.LapTime * 0.25, Left = o.Left - 1 };
                }
            }
            SortOrder();
        }

        /// <summary>
        /// A fingerprint of the race so far: the order, every car's distance, tires and damage bit for bit, the RNG state and the
        /// caution count. Two runs of the same setup give the same fingerprint, on any machine, or the sim has a determinism bug.
        /// </summary>
        public string Fingerprint()
        {
            ulong h = 1469598103934665603UL;
            void Mix(ulong x) { for (int i = 0; i < 8; i++) { h ^= (x >> (i * 8)) & 0xFF; h *= 1099511628211UL; } }
            foreach (var c in FinalOrder ?? Order)
            {
                Mix((ulong)c.Id);
                Mix((ulong)BitConverter.DoubleToInt64Bits(c.Dist));
                Mix((ulong)BitConverter.DoubleToInt64Bits(c.Tires));
                Mix((ulong)BitConverter.DoubleToInt64Bits(c.Damage));
            }
            Mix(Rng.State);
            Mix((ulong)Cautions);
            return h.ToString("x16");
        }

        public RaceResult Result()
        {
            var fo = FinalOrder ?? Order;
            var r = new RaceResult { TrackId = Track.Id, Seed = Seed, Cautions = Cautions, Wrecks = Wrecks, Duration = T, MyExpected = Expected, MyStart = Me.Start };
            for (int i = 0; i < fo.Count; i++)
            {
                var c = fo[i];
                r.Order.Add(new CarResult { Id = c.Id, IsMe = c.IsMe, Start = c.Start, Finish = i + 1, Dnf = c.Dnf, Passes = c.Passes, PitStops = c.PitStops, Damage = c.Damage, Tires = c.Tires });
                if (c.Dnf) r.Dnfs++;
                if (c.IsMe) { r.MyFinish = i + 1; r.MyDnf = c.Dnf; }
            }
            return r;
        }
    }
}
