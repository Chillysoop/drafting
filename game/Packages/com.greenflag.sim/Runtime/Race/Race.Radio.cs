using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>How the last crew call you took went, for the HUD's result flash and the booth.</summary>
    public sealed class RadioAnswer
    {
        public RadioCallDef Def;
        public bool Ok, Net, Rerolled;
        /// <summary>What happened, in the slice's words (the call's success or failure line).</summary>
        public string Text;
        public double At;
    }

    /// <summary>
    /// Crew radio calls (step 33): a port of the slice's spawnRadio / armCall / acceptRadio / ignoreRadio / adviceLine with the
    /// step 19 Crew HQ hooks (perks, traits, level 6 and 8 calls, XP) and the step 18 Wits countdown. Calls come up on green
    /// every 22-34 s (sooner with a better crew or the Clutch Radio), last 7 s (advice 6 s) and are a risk layer of their own:
    /// they never touch combos. Everything random here comes from RadioRng, so a race where nobody answers the radio rolls
    /// exactly as it did before radio existed; only a call you take changes the race.
    /// </summary>
    public sealed partial class Race
    {
        public readonly SimRng RadioRng;
        /// <summary>The call on the radio now, or null.</summary>
        public RadioCall Radio { get; private set; }
        /// <summary>Race time the next call can come up.</summary>
        public double RadioNext;
        public RadioAnswer LastAnswer { get; private set; }
        /// <summary>Tutorials and tests: the next call you take works (true) or misses (false) instead of rolling.</summary>
        public bool? ForceNextCall;
        public bool RadioOn => CardsOn && Loadout.Radio;

        double radioSoon;
        bool crewReroll;

        const double CallSec = 7, AdviceSec = 6, WitsSec = 1;

        void RadioInit() { RadioNext = RadioRng.Rand(18, 26); }

        /// <summary>After the draws, as in the slice's update: a call comes up when it's due, and one nobody answered times out.</summary>
        void RadioTick()
        {
            if (!RadioOn) return;
            if (Phase == RacePhase.Green && Radio == null && T >= RadioNext) SpawnRadio();
            if (Radio != null && T > Radio.Until)
            {
                var was = Radio;
                Radio = null; RadioNext = T + RadioGap();
                Emit(RaceEventKind.RadioTimedOut, Me, null, was.Def?.Name ?? "Advice");
            }
        }

        /// <summary>A caution takes the call off the radio (the next one can come right at the green).</summary>
        void RadioCaution() { Radio = null; }

        /// <summary>Overdrive: the crew chief's Heat warning takes over the radio for 7 s.</summary>
        void RadioOverdrive(string line)
        {
            if (!RadioOn) return;
            Radio = new RadioCall { Kind = RadioKind.Advice, Who = "Crew chief", Line = "\"" + line + " Let it cool off for a bit.\"", Until = T + 7, Dur = 7 };
        }

        // ---------------------------------------------------------------- crew numbers (the slice's crewQ / armCall / crewRate)

        int Level(CrewRole r) => Loadout.Level(r);
        bool CrewLate => LapOf(Me) >= Track.Laps * 0.7;

        /// <summary>A call's chance to work right now: base + level x per, the Clutch Radio, perks and traits. 5 to 95.</summary>
        public int CallChance(RadioCallDef d)
        {
            var fx = Loadout.Crew.For(d.Role);
            double v = d.Base + Level(d.Role) * d.Per + Loadout.PartSum(p => p.RadioBonus) + fx.Chance + Loadout.Crew.ChanceAll + (CrewLate ? fx.Late : 0);
            return (int)SimMath.Clamp(v, 5, 95);
        }

        /// <summary>Payoff scale: +10% per level above 3, times the seat's pay perks.</summary>
        public double CallQ(RadioCallDef d) => (1 + Math.Max(0, Level(d.Role) - 3) * 0.1) * (1 + Loadout.Crew.For(d.Role).Pay);

        /// <summary>A better crew talks more: the gap between calls is 2% shorter per average level above 3, times perks.</summary>
        public double CrewRate()
        {
            double a = (Math.Max(0, Loadout.CrewChief - 3) + Math.Max(0, Loadout.Spotter - 3) + Math.Max(0, Loadout.Engineer - 3)) / 3.0;
            return Math.Max(0.5, (1 - 0.02 * a) * Loadout.Crew.Rate);
        }

        double RadioGap() => RadioRng.Rand(22, 34) * Loadout.PartMul(p => p.RadioRate) * CrewRate();

        /// <summary>The calls that could come up now, with their weights.</summary>
        public List<(RadioCallDef def, double w)> RadioOptions()
        {
            var c = Ctx();
            var l = new List<(RadioCallDef, double)>();
            foreach (var d in RadioCalls.All)
            {
                if (d.Pit && Track.Kart) continue;
                if (d.Kind == RadioKind.Mech && d.Lvl > 0 && Level(d.Role) < d.Lvl) continue;
                if (d.When != null && !d.When(c)) continue;
                l.Add((d, d.Kind == RadioKind.Mech ? d.W * Loadout.Crew.For(d.Role).Weight : d.W));
            }
            return l;
        }

        void SpawnRadio()
        {
            if (Me.Dnf) return;
            var opts = RadioOptions();
            double tot = 0;
            foreach (var o in opts) tot += o.w;
            double r = RadioRng.NextDouble() * tot;
            var def = opts[opts.Count - 1].def;
            foreach (var o in opts) { r -= o.w; if (r <= 0) { def = o.def; break; } }

            if (def.Kind == RadioKind.Advice)
            {
                var a = AdviceLine(Ctx());
                if (a == null) { RadioNext = T + 10; return; }
                Radio = new RadioCall { Kind = RadioKind.Advice, Who = a.Value.who, Line = a.Value.line, Until = T + AdviceSec, Dur = AdviceSec };
            }
            else Radio = Arm(def, CallSec);
            Emit(RaceEventKind.RadioCall, Me, null, Radio.Line, def.Kind == RadioKind.Mech ? def.Name : null);
        }

        /// <summary>A risky call with its chance and payoff fixed now (the slice's armCall), and Wits on the countdown.</summary>
        RadioCall Arm(RadioCallDef def, double dur)
        {
            var q = CallQ(def);
            var fx = Loadout.Crew.For(def.Role);
            var call = new RadioCall
            {
                Def = def, Kind = RadioKind.Mech, Who = def.Who, Line = def.Line, Until = T + dur, Dur = dur,
                Chance = CallChance(def), Q = q, Qf = q / (fx.Soft * Loadout.Crew.SoftAll),
            };
            int m = (int)Math.Floor((Loadout.Wits - 10) / 2.0);
            if (m != 0)
            {
                double x = Math.Max(-call.Dur + 2, m * WitsSec);
                call.Until += x; call.Dur += x; call.Wits = x;
            }
            return call;
        }

        /// <summary>Tutorials and tests: put a named call on the radio now (the slice's tutorial does this with a 30 s countdown).</summary>
        public RadioCall OfferCall(string name, double dur = CallSec)
        {
            Radio = Arm(RadioCalls.Get(name), dur);
            Emit(RaceEventKind.RadioCall, Me, null, Radio.Line, name);
            return Radio;
        }

        static string LastName(RaceCar c) { var n = c.Ref?.Name ?? ""; int i = n.LastIndexOf(' '); return i >= 0 ? n.Substring(i + 1) : n; }

        static readonly Dictionary<string, string> TrackTips = new Dictionary<string, string>
        {
            ["Short track"] = "\"Use the bumper if you have to. Everybody else will.\"",
            ["Intermediate"] = "\"Clean air is worth a lot here. Get out front if you can.\"",
            ["Superspeedway"] = "\"Stay in line, find help. Nobody wins alone up here.\"",
            ["Kart circuit"] = "\"No draft here. Make your pass on the brakes into the hairpins.\"",
            ["Road course"] = "\"Draft down the back straight, then outbrake them into the hairpin.\"",
        };

        /// <summary>Free information: tires, the best card in hand, the car ahead burning its tires, a car on your bumper, a tip
        /// for the track. (The slice's rival line waits for rivals in the engine career.)</summary>
        (string who, string line)? AdviceLine(CardCtx c)
        {
            var o = new List<(string, string)>();
            if (c.Tires < 45) o.Add(("Crew chief", "\"Tires are going away. Take care of them or plan to pit.\""));
            CardDef best = null; double bs = 0;
            foreach (var h in Hand)
            {
                if (CardBlock(h.Def, c) != null) continue;
                double s = ComputeOdds(h.Def, c).Succ;
                if (best == null || s > bs) { best = h.Def; bs = s; }
            }
            if (best != null && bs > 0.55) o.Add(("Engineer", $"\"The numbers like {best.Name} right now: about {SimMath.RoundJs(bs * 100)}%.\""));
            if (c.Ahead != null && c.Ahead.Tires < c.Tires - 12) o.Add(("Spotter", $"\"{LastName(c.Ahead)} is burning the tires up. You’ll get there.\""));
            if (c.Behind != null && c.GapB < 0.5) o.Add(("Spotter", $"\"{LastName(c.Behind)} is right on your bumper.\""));
            if (Track.Type != null && TrackTips.TryGetValue(Track.Type, out var tip)) o.Add(("Crew chief", tip));
            return o.Count > 0 ? RadioRng.Pick(o) : ((string, string)?)null;
        }

        // ---------------------------------------------------------------- answering

        /// <summary>
        /// Try the call on the radio (A in the slice). It rolls against its chance; Second opinion rerolls one miss a race and
        /// Safety net lets some spotter misses cost nothing. Returns why not, or null; the outcome is in LastAnswer.
        /// </summary>
        public string TakeCall()
        {
            var call = Radio;
            if (call == null) return "No call on the radio";
            if (call.Kind == RadioKind.Advice) return "That's just advice";
            var d = call.Def;
            var fx = Loadout.Crew.For(d.Role);
            bool Roll() { if (ForceNextCall is bool f) { ForceNextCall = null; return f; } return RadioRng.NextDouble() * 100 < call.Chance; }
            bool ok = Roll(), rerolled = false;
            if (!ok && Loadout.Crew.Reroll && !crewReroll) { crewReroll = true; ok = Roll(); rerolled = ok; }
            bool net = !ok && fx.Net > 0 && RadioRng.NextDouble() < fx.Net;
            var car = Me;
            if (ok) d.OnS(this, car, call.Q); else if (!net) d.OnF(this, car, call.Qf);

            // the slice's crewAfterCall
            CardStats.Calls++;
            if (ok) CardStats.CallsWon++;
            CrewXp(d.Role, CrewHq.XpCall + (ok ? CrewHq.XpWon : 0));
            if (ok)
            {
                if (Loadout.Crew.MomAll != 0) AddMomentum(Loadout.Crew.MomAll);
                if (fx.FixOnS > 0 && car.Damage > 0) Repair(car, Math.Min(fx.FixOnS, car.Damage), DamageZone.Nose);
                if (fx.Again > 0 && RadioRng.NextDouble() < fx.Again) radioSoon = 2;
            }
            string text = ok ? d.S + (rerolled ? " Second opinion saved it." : "") : net ? "Safety net: the miss cost nothing." : d.F;
            LastAnswer = new RadioAnswer { Def = d, Ok = ok, Net = net, Rerolled = rerolled, Text = text, At = T };
            Emit(RaceEventKind.RadioAnswered, car, null, text, d.Name, (int)(ok ? Tier.Success : net ? Tier.Neutral : Tier.Fail));
            Radio = null;
            RadioNext = T + (radioSoon > 0 ? radioSoon : RadioGap());
            radioSoon = 0;
            return null;
        }

        /// <summary>Turn the call down (X in the slice), or wave off the advice. The next call comes after the usual gap.</summary>
        public void PassCall()
        {
            if (Radio == null) return;
            var was = Radio;
            Radio = null; RadioNext = T + RadioGap();
            Emit(RaceEventKind.RadioPassed, Me, null, was.Def?.Name ?? "Advice");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Repair damage: amt comes off the named zone first, then the worst zone (the slice's dzFix).</summary>
        public void Repair(RaceCar car, double amt, DamageZone zone)
        {
            var z = car.Zones;
            ReconcileZones(car);
            switch (zone)
            {
                case DamageZone.Nose: z.Nose -= Math.Min(amt, z.Nose); break;
                case DamageZone.Side: z.Side -= Math.Min(amt, z.Side); break;
                default: z.Tail -= Math.Min(amt, z.Tail); break;
            }
            car.Damage = Math.Max(0, car.Damage - amt);
            ReconcileZones(car);
        }

        void CrewXp(CrewRole r, int n)
        {
            CardStats.CrewXp.TryGetValue(r, out int v);
            CardStats.CrewXp[r] = v + n;
        }
    }
}
