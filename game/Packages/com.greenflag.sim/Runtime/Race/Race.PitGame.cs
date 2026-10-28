using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum PitGamePhase { Plan, Work, Done }

    /// <summary>Who plays your pit stops: you (the race holds while you plan), a bot that plans Setups and releases on the drop, or the crew chief.</summary>
    public enum PitPolicy { Player, Bot, Chief }

    public sealed class PitHandCard
    {
        public string Id;
        public bool On, Prep;
        public Tier? Roll;
        public double Delta;
        public PitCardDef Def => PitCards.Get(Id);
    }

    /// <summary>Lane lengths in seconds. The stop takes as long as the slowest.</summary>
    public struct PitLanes
    {
        public double Tire, Fuel, Adjust;
        public double Total => Math.Max(Tire, Math.Max(Fuel, Adjust));
    }

    /// <summary>One stop in your pit stall: plan (pick cards into lanes), work (lanes run, the jack drops), release.</summary>
    public sealed class PitGame
    {
        public PitGamePhase Phase;
        public PitService Service;
        public double BaseTire, BaseFuel;
        public readonly List<PitHandCard> Hand = new List<PitHandCard>();
        public int Prep;
        public PitPolicy Auto;
        /// <summary>The field's average stop for this service, to beat.</summary>
        public double Field;
        public PitLanes Lanes;
        public double Total, T0, Window, BotAt;
        public int Loose;
        public bool Perfect, InWindow, ChiefRelease;
        /// <summary>Seconds early (positive) or late (negative) against the drop.</summary>
        public double ReleaseError;
        public double StopSeconds;
        public readonly List<string> Done = new List<string>();
    }

    /// <summary>
    /// The pit stop mini-game (step 6b, board Q94: it moves into the sim with the cards). When your car stops in its stall you
    /// draw a pit hand from your pit deck (plus Prep from leftover Crew Board charges) and put cards into three crew lanes that
    /// work at the same time. Then the jack drops and you release: early risks a loose wheel, late gives time away.
    /// Adjustment cards install Setups: up to 3 ongoing bonuses that level up, combine into Packages and go stale.
    /// </summary>
    public sealed partial class Race
    {
        public PitGame PitGame;
        /// <summary>The last finished stop, for the HUD after you leave the stall.</summary>
        public PitGame LastPitGame;
        public PitPolicy PitPolicy = PitPolicy.Bot;

        /// <summary>The race holds while you plan a stop (PitPolicy.Player). Tick does nothing until PitGo.</summary>
        public bool Holding => PitGame != null && PitGame.Phase == PitGamePhase.Plan;

        bool PitGameOn => CardsOn && Bal.PitGame;

        /// <summary>Base lane times for this stop (pgBase): crew chief skill, Fast Stop, parts and Crew HQ lane cuts.</summary>
        (double tire, double fuel) PitBase(RaceCar c, PitService svc)
        {
            double R = FuelRange, m = 1 - (Loadout.CrewChief - 5) * 0.03, b = PitBonus + Mods.PitSeconds;
            double tt = svc == PitService.Two ? Bal.TireTwo : svc == PitService.Four ? Bal.TireFour : 0;
            double tire = tt > 0 ? Math.Max(2.5, tt * m - b) : 0;
            double fuel = Math.Max(1, (Bal.FuelBase + Bal.FuelT * SimMath.Clamp(1 - c.Fuel / R, 0, 1)) * m - b);
            double k = Loadout.Crew.LaneCut;
            if (k > 0) { if (tire > 0) tire = Math.Max(2.5, tire - k); fuel = Math.Max(1, fuel - k); }
            return (tire, fuel);
        }

        /// <summary>A pit card's outcome chances: crew skill and Crew HQ perks move the base odds.</summary>
        public double[] PitOdds(PitCardDef d)
        {
            if (!d.Rolls) return null;
            var cr = Loadout.Crew;
            int lane = cr.Chance + (d.Lane == PitLane.Tire ? cr.TireOdds : d.Lane == PitLane.Fuel || d.Lane == PitLane.Any ? cr.FuelOdds : 0);
            double s = SimMath.Clamp((d.Base + (Loadout.PitSkill - 5) * 4 + lane) / 100.0, 0.15, 0.95);
            var p = new double[5];
            p[(int)Tier.CritSuccess] = s * 0.2; p[(int)Tier.Success] = s * 0.8;
            if (d.Safe) p[(int)Tier.Neutral] = 1 - s;
            else { p[(int)Tier.Neutral] = (1 - s) * 0.45; p[(int)Tier.Fail] = (1 - s) * 0.55; }
            return p;
        }

        public double PitExpected(PitCardDef d)
        {
            var p = PitOdds(d); double e = 0;
            for (int i = 0; i < 5; i++) e += p[i] * d.Delta((Tier)i);
            return e;
        }

        double FixTime(PitCardDef d) => d.FixTime - (Loadout.Crew.BodyMen ? 2 : 0);
        double FixAmount(PitCardDef d) => d.Fix + (Loadout.Crew.BodyMen ? 10 : 0);

        /// <summary>Lane lengths: rolled deltas once the stop is running, expected deltas while you plan.</summary>
        public PitLanes PitLanesOf(PitGame pg, bool useRoll)
        {
            var L = new PitLanes { Tire = pg.BaseTire, Fuel = pg.BaseFuel };
            var any = new List<double>();
            foreach (var pc in pg.Hand)
            {
                if (!pc.On) continue;
                var d = pc.Def;
                double dv = useRoll && pc.Roll != null ? pc.Delta : d.Rolls ? PitExpected(d) : d.IsFix ? FixTime(d) : Bal.PgAdjT;
                if (d.Lane == PitLane.Any) any.Add(dv);
                else if (d.Lane == PitLane.Adjust) L.Adjust += d.Rolls ? 0 : dv;
                else if (d.Lane == PitLane.Tire) { if (L.Tire > 0) L.Tire += dv; }
                else if (L.Fuel > 0) L.Fuel += dv;
            }
            foreach (var dv in any) { if (L.Tire >= L.Fuel) L.Tire += dv; else L.Fuel += dv; }
            if (pg.BaseTire > 0) L.Tire = Math.Max(2.5, L.Tire);
            if (pg.BaseFuel > 0) L.Fuel = Math.Max(1, L.Fuel);
            return L;
        }

        public string PitCardBlock(PitGame pg, PitHandCard pc)
        {
            var d = pc.Def;
            if (d.Lane == PitLane.Tire && pg.BaseTire <= 0) return "No tires this stop";
            if (d.Lane == PitLane.Adjust && !pc.On && pg.Hand.FindAll(x => x.On && x.Def.Lane == PitLane.Adjust).Count >= Bal.PgMaxAdj) return $"Max {Bal.PgMaxAdj} adjustments";
            return null;
        }

        public enum SetupPlanKind { New, Up, Fresh, Swap }

        /// <summary>Where a Setup card would go: a level-up, a new slot, already Level II (freshened), or replacing your stalest Setup.</summary>
        public (SetupPlanKind kind, InstalledSetup old) SetupPlan(string id, List<InstalledSetup> list = null)
        {
            var L = list ?? Setups;
            var s = L.Find(x => x.Id == id);
            if (s != null) return (s.Lvl >= 2 ? SetupPlanKind.Fresh : SetupPlanKind.Up, null);
            if (L.Count < 3) return (SetupPlanKind.New, null);
            var sorted = new List<InstalledSetup>(L);
            SimMath.StableSort(sorted, (a, b) => a.Lvl != b.Lvl ? a.Lvl.CompareTo(b.Lvl) : a.At.CompareTo(b.At));
            return (SetupPlanKind.Swap, sorted[0]);
        }

        void OpenPitGame(RaceCar c)
        {
            var svc = c.Pit.Service;
            var cr = Loadout.Crew;
            int xh = cr.PitHand, prep = Math.Min(Bal.PgPrepMax, Bal.Board > 0 ? Board : 0) + cr.Prep;
            Board = 0;
            var (bt, bf) = PitBase(c, svc);
            var pool = new List<string>(Loadout.PitDeck);
            CardRng.Shuffle(pool);
            pool = pool.FindAll(id => !((id == "fix" || id == "fix+") && c.Damage < 8) && !(PitCards.Get(id).Lane == PitLane.Tire && bt <= 0));
            // a damaged car always finds the Fix card in its pit hand
            ReconcileZones(c);
            if (Math.Max(c.Zones.Nose, Math.Max(c.Zones.Side, c.Zones.Tail)) >= 10)
            {
                int i = pool.IndexOf("fix"); if (i < 0) i = pool.IndexOf("fix+");
                if (i > 0) { var f = pool[i]; pool.RemoveAt(i); pool.Insert(0, f); }
            }
            var pg = PitGame = new PitGame { Phase = PitGamePhase.Plan, Service = svc, BaseTire = bt, BaseFuel = bf, Prep = prep, Auto = PitPolicy, Field = PitField(svc) };
            int n = Math.Min(pool.Count, Bal.PgHand + xh + prep);
            for (int i = 0; i < n; i++) pg.Hand.Add(new PitHandCard { Id = pool[i], Prep = i >= Bal.PgHand + xh });
            CardStats.PitStops++;
            Emit(RaceEventKind.PitGameOpen, c, null, $"{pg.Hand.Count} pit cards{(prep > 0 ? $" ({prep} Prep)" : "")}");
            if (PitPolicy != PitPolicy.Player) { PitAutoPlan(pg, PitPolicy == PitPolicy.Bot); PitGo(); }
        }

        double PitField(PitService svc)
        {
            double sum = 0; int n = 0;
            double tt = svc == PitService.Two ? Bal.TireTwo : svc == PitService.Four ? Bal.TireFour : 0;
            foreach (var c in Cars)
            {
                if (c.IsMe || c.Dnf) continue;
                double m = 1 - (c.Ref.TeamRating - 6) * 0.025;
                sum += Math.Max(tt, Bal.FuelBase + Bal.FuelT * 0.85) * m; n++;
            }
            return n > 0 ? sum / n : 10;
        }

        /// <summary>The crew chief's plan (speed cards only) or the bot's plan (also Setups): add each card that makes the stop shorter.</summary>
        public void PitAutoPlan(PitGame pg, bool setups)
        {
            foreach (var pc in pg.Hand) pc.On = false;
            var cands = pg.Hand.FindAll(pc => pc.Def.Rolls && PitCardBlock(pg, pc) == null);
            SimMath.StableSort(cands, (a, b) => PitExpected(a.Def).CompareTo(PitExpected(b.Def)));
            foreach (var pc in cands)
            {
                double before = PitLanesOf(pg, false).Total;
                pc.On = true;
                if (PitLanesOf(pg, false).Total > before - 0.05) pc.On = false;
            }
            var fx = pg.Hand.Find(pc => pc.Id == "fix" || pc.Id == "fix+");
            ReconcileZones(Me);
            if (fx != null && (Me.Damage >= 25 || Math.Max(Me.Zones.Nose, Math.Max(Me.Zones.Side, Me.Zones.Tail)) >= 15)) fx.On = true;
            if (!setups) return;
            int Rank(PitHandCard x) { var k = SetupPlan(x.Def.Setup).kind; return k == SetupPlanKind.Up ? 0 : k == SetupPlanKind.New ? 1 : k == SetupPlanKind.Swap ? 2 : 3; }
            var adj = pg.Hand.FindAll(pc => pc.Def.Setup != null);
            SimMath.StableSort(adj, (a, b) => Rank(a).CompareTo(Rank(b)));
            var seen = new HashSet<string>();
            foreach (var pc in adj)
            {
                if (PitCardBlock(pg, pc) != null) break;
                var id = pc.Def.Setup;
                if (seen.Contains(id) || SetupPlan(id).kind == SetupPlanKind.Fresh) continue;
                double before = PitLanesOf(pg, false).Total;
                pc.On = true;
                if (PitLanesOf(pg, false).Total > before + 0.4) pc.On = false; else seen.Add(id);
            }
        }

        /// <summary>Put a pit card in its lane or take it out (plan phase). Returns why not, or null.</summary>
        public string PitToggle(int index)
        {
            var pg = PitGame;
            if (pg == null || pg.Phase != PitGamePhase.Plan) return "Not planning a stop";
            if (index < 0 || index >= pg.Hand.Count) return "No such card";
            var pc = pg.Hand[index];
            if (!pc.On) { var why = PitCardBlock(pg, pc); if (why != null) return why; }
            pc.On = !pc.On;
            return null;
        }

        /// <summary>GO: every card rolls, the lanes start working, and the release window opens at the end of the slowest lane.</summary>
        public void PitGo(bool timeout = false)
        {
            var pg = PitGame;
            if (pg == null || pg.Phase != PitGamePhase.Plan) return;
            if (timeout) PitAutoPlan(pg, false);
            foreach (var pc in pg.Hand)
            {
                if (!pc.On || !pc.Def.Rolls) continue;
                var p = PitOdds(pc.Def);
                double r = CardRng.NextDouble(); var t = Tier.CritSuccess;
                for (int i = 0; i < 5; i++) { r -= p[i]; if (r <= 0) { t = (Tier)i; break; } }
                pc.Roll = t; pc.Delta = pc.Def.Delta(t);
            }
            pg.Lanes = PitLanesOf(pg, true); pg.Total = pg.Lanes.Total; pg.T0 = T; pg.Phase = PitGamePhase.Work;
            pg.Window = 0.55 + 0.07 * Loadout.PitSkill + Loadout.Crew.ReleaseWindow;
            if (pg.Auto == PitPolicy.Bot) pg.BotAt = pg.Total + CardRng.NextDouble() * pg.Window * 1.3;
            else if (pg.Auto == PitPolicy.Chief) pg.BotAt = pg.Total + pg.Window + Bal.ChiefLate;
            Emit(RaceEventKind.PitGameGo, Me, null, $"{pg.Total:F1} s");
        }

        /// <summary>
        /// Drop the jack. More than 1 s early is refused; early inside that risks a loose wheel (+3 s); late just costs time.
        /// A release in the window with no failed card and at least two cards played is a Perfect Stop. Returns why not, or null.
        /// </summary>
        public string PitRelease(bool chief = false)
        {
            var pg = PitGame;
            if (pg == null || pg.Phase != PitGamePhase.Work) return "No stop running";
            double el = T - pg.T0, e = pg.Total - el;
            if (e > Bal.PgEarly) return "The crew is still working";
            if (e > 0)
            {
                double risk = Math.Min(0.9, e * Bal.PgLooseK * (pg.BaseTire > 0 ? 1 : 0.6));
                if (CardRng.NextDouble() < risk)
                {
                    pg.Total = el + Bal.PgLooseT; pg.Loose++; CardStats.Loose++;
                    Emit(RaceEventKind.LooseWheel, Me, null, pg.BaseTire > 0 ? "Loose wheel: +3 s" : "Unsafe release: +3 s");
                    return "Loose wheel";
                }
            }
            bool inWin = !chief && e <= 0 && -e <= pg.Window;
            int played = pg.Hand.FindAll(x => x.On).Count;
            bool clean = pg.Loose == 0 && !pg.Hand.Exists(x => x.On && x.Roll == Tier.Fail);
            pg.InWindow = inWin; pg.ChiefRelease = chief; pg.ReleaseError = e;
            pg.Perfect = inWin && clean && played >= 2;
            var car = Me;
            foreach (var pc in pg.Hand)
            {
                if (!pc.On) continue;
                var d = pc.Def;
                if (d.IsFix) { double fx = FixAmount(d); car.Damage = Math.Max(0, car.Damage - fx); ReconcileZones(car); pg.Done.Add($"Body +{fx}"); }
                if (d.Setup != null) pg.Done.Add(SetupInstall(d.Setup, d.Up));
            }
            SetupFresh = LapOf(car);
            pg.Phase = PitGamePhase.Done;
            pg.StopSeconds = el;
            car.Pit.Until = T;
            CardStats.StopTimes.Add(Math.Round(el, 2));
            if (pg.Perfect)
            {
                CardStats.Perfect++; AddMomentum(1); FocusMeter = 1;
                Emit(RaceEventKind.PerfectStop, car, null, "+1 Momentum and full Focus");
            }
            Emit(RaceEventKind.PitGameRelease, car, null, $"{el:F1} s{(chief ? " (crew chief)" : inWin ? "" : e > 0 ? " (early)" : " (late)")}");
            LastPitGame = pg;
            return null;
        }

        string SetupInstall(string id, bool two)
        {
            var (kind, old) = SetupPlan(id);
            var d = PitCards.Setups[id];
            CardStats.Setups++;
            string outTxt;
            if (kind == SetupPlanKind.Up) { var s = Setups.Find(x => x.Id == id); s.Lvl = 2; s.At = T; outTxt = $"{d.Short} -> Level II"; }
            else if (kind == SetupPlanKind.Fresh) outTxt = $"{d.Short} freshened";
            else
            {
                if (kind == SetupPlanKind.Swap) Setups.Remove(old);
                // an upgraded (+) Setup card installs at Level II (step 19; the slice's step 7b wrapper drops this, see docs/cards.md)
                Setups.Add(new InstalledSetup { Id = id, Lvl = two ? 2 : 1, At = T });
                foreach (var pk in PitCards.Packages)
                    if (Array.IndexOf(pk.Of, id) >= 0 && Array.TrueForAll(pk.Of, x => Setups.Exists(s => s.Id == x)))
                    { CardStats.Packages++; Emit(RaceEventKind.PackageComplete, Me, null, pk.Name); break; }
                outTxt = kind == SetupPlanKind.Swap ? $"{d.Short} in ({PitCards.Setups[old.Id].Short} out)" : $"{d.Short} installed";
            }
            Emit(RaceEventKind.SetupInstalled, Me, null, outTxt, id);
            // step 7b: a pit adjustment nudges this race's tune one notch
            var nudge = Tune.Nudge(id);
            if (nudge != null) { Loadout.Apply(Mods, Track, Tune); outTxt += " · car " + nudge; }
            return outTxt;
        }

        IEnumerable<PackageDef> ActivePackages()
        {
            foreach (var p in PitCards.Packages)
                if (Array.TrueForAll(p.Of, x => Setups.Exists(s => s.Id == x))) yield return p;
        }

        IEnumerable<(int v, string label)> SetupReads(CardDef cd)
        {
            foreach (var s in Setups)
            {
                var d = s.Def;
                if (d.Family == cd.Family) yield return (d.Odds[s.Lvl - 1], $"{d.Short} {(s.Lvl > 1 ? "II" : "I")}");
            }
            foreach (var p in ActivePackages())
                if (p.Odds.TryGetValue(cd.Family, out var v)) yield return (v, p.Name);
        }

        double SetupWear()
        {
            double m = 1;
            foreach (var s in Setups) if (s.Def.Wear != null) m *= s.Def.Wear[s.Lvl - 1];
            foreach (var p in ActivePackages()) m *= p.Wear;
            return m;
        }

        /// <summary>Green laps before the Setups go stale.</summary>
        public int StaleLaps => Math.Max(3, (int)SimMath.RoundJs(FuelRange * Bal.PgStaleK));

        void SetupStaleTick()
        {
            if (Setups.Count == 0 || Phase == RacePhase.Finished || Me.Pit != null) return;
            int lap = LapOf(Me);
            if (SetupFresh == null) SetupFresh = lap;
            if (Phase == RacePhase.Green && lap - SetupFresh.Value >= StaleLaps)
            {
                SetupFresh = lap;
                foreach (var s in Setups) s.Lvl--;
                var gone = Setups.FindAll(s => s.Lvl <= 0);
                Setups.RemoveAll(s => s.Lvl <= 0);
                Emit(RaceEventKind.SetupsStale, Me, null, gone.Count > 0 ? string.Join(",", gone.ConvertAll(s => s.Id)) + " gone" : "down a level");
            }
        }

        void PitGameTick()
        {
            var pg = PitGame;
            if (pg == null) return;
            var m = Me;
            if (m.Pit == null || m.Pit.Stage != PitStage.Box) { PitGame = null; return; }
            if (pg.Phase != PitGamePhase.Work) return;
            double el = T - pg.T0;
            if (pg.Auto != PitPolicy.Player && el >= pg.BotAt) { PitRelease(pg.Auto == PitPolicy.Chief && el >= pg.Total + pg.Window + Bal.ChiefLate); return; }
            if (el >= pg.Total + pg.Window + Bal.ChiefLate) PitRelease(true);
        }
    }
}
