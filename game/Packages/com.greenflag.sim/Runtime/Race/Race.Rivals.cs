using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>Something between you and a class driver this race: wreck (you wrecked them), hit (they wrecked you), payback,
    /// or pass (you landed a hard pass on them).</summary>
    public sealed class RivalIncident
    {
        public int Car, Lap;
        public string Kind;
    }

    /// <summary>
    /// Rivals on track (roadmap step 43, the slice's step 20 rivals-js through small hooks in the race): a class driver's
    /// Grudge toward you changes how they race you. Warm (3+) blocks you more, leans on you when attacking, and costs your
    /// passing cards odds against them; Hot (6+) can turn an attack into payback, a hook that spins you, at most once a race,
    /// which settles some of the Grudge; Respect (−2 or less) gives you room. Wrecking a class driver adds +4 (+2 if they were
    /// only collected in a big one) and a landed hard pass on one adds +0.5, scaled by how touchy they are.
    /// A field without class drivers, or with every Grudge at 0, races exactly as before: nothing here rolls the race's dice
    /// below Hot, so parity and the golden races don't move. Bal.Rivals off turns all of it off.
    /// </summary>
    public sealed partial class Race
    {
        /// <summary>Every incident with a class driver this race, in order.</summary>
        public readonly List<RivalIncident> RivalInc = new List<RivalIncident>();
        /// <summary>Live Grudge by car id (only class drivers whose Grudge has moved this race), where it started, and why it moved.</summary>
        public readonly Dictionary<int, double> RivalG = new Dictionary<int, double>(), RivalG0 = new Dictionary<int, double>();
        public readonly Dictionary<int, List<(string why, double d)>> RivalWhy = new Dictionary<int, List<(string, double)>>();
        readonly HashSet<int> rivalPaid = new HashSet<int>(), rivalWarned = new HashSet<int>();
        /// <summary>Paybacks you took this race.</summary>
        public int Paybacks;

        bool RivalsOn => Bal.Rivals;
        static RivalDef RDef(RaceCar c) => c?.Ref?.Rival != null ? RivalBook.Get(c.Ref.Rival) : null;
        public bool IsRival(RaceCar c) => RivalsOn && c?.Ref?.Rival != null && !c.IsMe;
        public double GrudgeOf(RaceCar c) => c == null ? 0 : RivalG.TryGetValue(c.Id, out var g) ? g : c.Ref?.Rival != null ? c.Ref.Grudge : 0;
        public bool PaidBack(RaceCar c) => rivalPaid.Contains(c.Id);

        /// <summary>Moves a class driver's Grudge (gains scaled by their Mul) and says so when it turns Hot.</summary>
        public double AddGrudge(RaceCar c, double v, string why)
        {
            if (!IsRival(c) || v == 0) return 0;
            var d = RDef(c);
            double g0 = GrudgeOf(c);
            if (!RivalG0.ContainsKey(c.Id)) RivalG0[c.Id] = g0;
            double g = RivalBook.Q(g0 + (v > 0 ? v * d.Mul : v));
            RivalG[c.Id] = g;
            if (!RivalWhy.TryGetValue(c.Id, out var w)) RivalWhy[c.Id] = w = new List<(string, double)>();
            w.Add((why, g - g0));
            if (g >= 6 && g0 < 6) Emit(RaceEventKind.RivalHot, c, Me, $"{c.Ref.Name} is {(g >= 9 ? "livid" : "hot")} with you. Watch the mirror.");
            return g - g0;
        }

        void RivalIncidentAdd(RaceCar c, string kind) => RivalInc.Add(new RivalIncident { Car = c.Id, Kind = kind, Lap = LapOf(Leader) });

        /// <summary>A wreck with you in it: class drivers you collected get Grudge; one who collected you is a 'hit'.</summary>
        void RivalWreck(RaceCar car, List<RaceCar> cars, bool big)
        {
            if (!RivalsOn || !cars.Contains(Me)) return;
            if (car.IsMe)
            {
                for (int i = 1; i < cars.Count; i++)
                    if (IsRival(cars[i])) { RivalIncidentAdd(cars[i], "wreck"); AddGrudge(cars[i], big && i > 1 ? 2 : 4, "you wrecked them"); }
            }
            else if (IsRival(car)) RivalIncidentAdd(car, "hit");
        }

        /// <summary>A hard pass (pass, risk or Bump Draft) that lands on a class driver.</summary>
        void RivalPlay(CardDef cd, Tier tier, RaceCar target)
        {
            if (!IsRival(target)) return;
            if ((cd.Family == CardFamily.Pass || cd.Family == CardFamily.Risk || cd.Id == "bump_draft") && CardRules.Landed(tier))
            {
                RivalIncidentAdd(target, "pass");
                AddGrudge(target, 0.5, "hard pass");
            }
        }

        /// <summary>A class driver within striking range of your bumper: payback (null, the move is over), or the read their
        /// Grudge adds to the pass. Only a Hot rival rolls anything.</summary>
        double? RivalAttack(RaceCar car)
        {
            if (!IsRival(car)) return 0;
            double g = GrudgeOf(car);
            var d = RDef(car);
            if (g >= 6 && !rivalPaid.Contains(car.Id) && Me.Pit == null && Phase == RacePhase.Green)
            {
                if (rivalWarned.Add(car.Id)) Emit(RaceEventKind.RivalBumper, car, Me, $"{car.Ref.Name} is right on your bumper, and there's history there. Grudge {RivalBook.Num(g)} ({RivalBook.Tier(g)}): defend, or let them go.");
                if (Rng.NextDouble() < RivalBook.PaybackChance(d, g)) { Payback(car); return null; }
            }
            return RivalBook.Lean(d, g);
        }

        /// <summary>The hook: you spin and take damage (more at Feud), they lose a little and take a scrape, and the Grudge
        /// settles by their Settle. A Feud payback can bring out a caution.</summary>
        void Payback(RaceCar car)
        {
            var m = Me;
            var d = RDef(car);
            bool big = GrudgeOf(car) >= 9;
            rivalPaid.Add(car.Id);
            m.Spinning = big ? 2.2 : 1.4;
            Slow(m, Rng.Rand(big ? 4 : 2.5, big ? 7 : 4.5));
            m.Overtake = null;
            double amt = Rng.Rand(big ? 26 : 14, big ? 42 : 24) * Track.Contact * Diff.Wreck * Bal.WreckDmg;
            if (big) DamageWreckA(m, amt); else DamageSpin(m, amt);
            Slow(car, Rng.Rand(0.5, 1.5));
            DamageSpin(car, Rng.Rand(4, 9));
            RivalIncidentAdd(car, "payback");
            Paybacks++;
            AddGrudge(car, -d.Settle, "payback");
            Emit(RaceEventKind.Payback, car, m, $"{car.Ref.Name} hooks you{(big ? " hard" : "")}! That was payback{(d.Settle >= 4 ? ", and now they call it even" : "")}.");
            if (big && Phase == RacePhase.Green && pendingCautionAt < 0 && Rng.NextDouble() < 0.6) { pendingCautionAt = T + 0.9; pendingCautionReason = "Wreck: you, " + d.Last; }
        }

        /// <summary>How much more often a class driver ahead of you blocks you.</summary>
        double RivalBlock(RaceCar car) => IsRival(car) ? RivalBook.BlockK(RDef(car), GrudgeOf(car)) : 1;

        /// <summary>The odds lines: a Warm rival ahead won't give an inch, a Respectful one gives you room, a Hot one behind
        /// wants payback.</summary>
        void RivalOdds(CardDef cd, CardCtx c, OddsBuilder R)
        {
            if (!RivalsOn) return;
            var f = cd.Family;
            var a = c.Ahead;
            if (IsRival(a) && (f == CardFamily.Pass || f == CardFamily.Draft || f == CardFamily.Risk))
            {
                double g = GrudgeOf(a); var d = RDef(a);
                if (g >= 3) R.A(-Rd((g - 2) * 1.5 * d.Block), $"{d.Last} won't give an inch", "rival");
                else if (g <= -2) R.A(5, $"{d.Last} gives you room", "rival");
            }
            var b = c.Behind;
            if (IsRival(b) && f == CardFamily.Defend && GrudgeOf(b) >= 6) R.A(-4, $"{RDef(b).Last} wants payback", "rival");
        }

        /// <summary>The spotter's word on a class driver next to you with a Grudge (for the radio's advice line).</summary>
        (string, string)? RivalAdvice(CardCtx c)
        {
            if (!RivalsOn) return null;
            if (IsRival(c.Behind) && GrudgeOf(c.Behind) >= 6 && c.GapB < 1) return ("Spotter", $"\"{RDef(c.Behind).Last} hasn't forgotten. Grudge {RivalBook.Num(GrudgeOf(c.Behind))}: expect payback if that car gets to your bumper.\"");
            if (IsRival(c.Ahead) && GrudgeOf(c.Ahead) >= 3) return ("Spotter", $"\"{RDef(c.Ahead).Last} won't give you an inch today. Pick your spot.\"");
            if (IsRival(c.Ahead) && GrudgeOf(c.Ahead) <= -2) return ("Spotter", $"\"{RDef(c.Ahead).Last} will give you room. Race them clean.\"");
            return null;
        }
    }
}
