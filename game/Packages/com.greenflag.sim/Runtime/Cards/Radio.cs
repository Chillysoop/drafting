using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>Crew HQ seats. The crew chief, spotter and engineer make radio calls; the pit crew works the pit stop.</summary>
    public enum CrewRole { CrewChief, Spotter, Engineer, PitCrew }

    public enum RadioKind { Advice, Mech }

    /// <summary>
    /// One crew call (the slice's RADIO_CALLS): who makes it, when it can come up, its odds (base + stat x per) and what it does
    /// when it works or misses. q scales the payoff with the crew member's level and perks; a miss is divided by the soft factor.
    /// Calls are a risk layer of their own, separate from cards and combos (Conner, Oct 27).
    /// </summary>
    public sealed class RadioCallDef
    {
        public string Name, Who, Line, S, F;
        public CrewRole Role;
        public RadioKind Kind = RadioKind.Mech;
        public int Base, Per, Lvl;
        public double W = 1;
        /// <summary>Pit-related: never offered in a kart race.</summary>
        public bool Pit;
        public Func<CardCtx, bool> When;
        public Action<Race, RaceCar, double> OnS, OnF;
    }

    /// <summary>A call on the radio right now. Advice has no buttons; a risky call has its chance and payoff fixed when it comes up.</summary>
    public sealed class RadioCall
    {
        public RadioCallDef Def;
        public RadioKind Kind;
        public string Who, Line;
        public double Until, Dur;
        public int Chance;
        public double Q = 1, Qf = 1;
        /// <summary>Seconds Wits added (or took off) the countdown.</summary>
        public double Wits;
        public double Left(double t) => Math.Max(0, Until - t);
    }

    public static class RadioCalls
    {
        static readonly List<RadioCallDef> all = new List<RadioCallDef>();
        public static IReadOnlyList<RadioCallDef> All => all;

        public static string Who(CrewRole r) => r == CrewRole.Spotter ? "Spotter" : r == CrewRole.Engineer ? "Engineer" : r == CrewRole.PitCrew ? "Pit crew" : "Crew chief";

        static void Heat(Race r) { r.Heat = Math.Min(5, r.Heat + 1); r.HeatAt = r.T; }
        // the slice's dmg with no card: DZW.other (side .5, nose .25, tail .25)
        static void Dmg(Race r, RaceCar c, double amt) => r.AddDamage(c, amt, 0.25, 0.5, 0.25);
        static void DmgNose(Race r, RaceCar c, double amt) => r.AddDamage(c, amt, 1, 0, 0);

        static void Add(CrewRole role, string name, int w, string line, int b, int per, string s, string f, Action<Race, RaceCar, double> onS, Action<Race, RaceCar, double> onF,
                        Func<CardCtx, bool> when = null, int lvl = 0, bool pit = false) =>
            all.Add(new RadioCallDef { Role = role, Who = Who(role), Name = name, W = w, Line = line, Base = b, Per = per, S = s, F = f, OnS = onS, OnF = onF, When = when, Lvl = lvl, Pit = pit });

        static RadioCalls()
        {
            const CrewRole SP = CrewRole.Spotter, CC = CrewRole.CrewChief, EN = CrewRole.Engineer;
            Add(SP, "High line", 3, "\"Clear high! Want to throw it up there?\"", 38, 6, "It stuck up high: +2% speed for 12 s.", "Pinched off up top: −0.6 s.",
                (r, c, q) => r.AddPace(c, 1 + 0.02 * q, 12), (r, c, q) => r.Slow(c, 0.6 / q), x => x.Ahead != null && x.GapA < 1.2);
            Add(SP, "Chop him off", 2, "\"Car low, he’s got a run on you. Chop him off?\"", 40, 6, "He had to lift: he loses half a second.", "He got under you anyway: −0.5 s and +1 Heat.",
                (r, c, q) => { var b = r.BehindOf(c); if (b != null) r.Slow(b, 0.5 * q); }, (r, c, q) => { r.Slow(c, 0.5 / q); Heat(r); }, x => x.Behind != null && x.GapB < 0.7);
            Add(CC, "Save tires", 2, "\"Lift early and save some rubber this run? It pays off at the end.\"", 45, 6, "Tires saved: wear −30% for 40 s.", "Too slow saving them: −1.5% speed for 15 s.",
                (r, c, q) => r.WearFx(c, Math.Max(0.4, 1 - 0.3 * q), 40), (r, c, q) => r.AddPace(c, 1 - 0.015 / q, 15));
            Add(EN, "Run it tight", 2, "\"The car’s good in dirty air. Run it in tight?\"", 40, 6, "Tucked in the tow: +2.5% speed for 10 s.", "Overheated in the dirty air: body −4.",
                (r, c, q) => r.AddPace(c, 1 + 0.025 * q, 10), (r, c, q) => DmgNose(r, c, 4 / q), x => x.Ahead != null && x.GapA < x.Track.DraftRange);
            Add(EN, "Brake bias", 2, "\"We can take a turn of brake bias. Want it?\"", 40, 6, "Brake bias dialed in: +0.8% speed for the rest of the race.", "Too much. The rear is loose: −2% speed for 20 s.",
                (r, c, q) => c.PermPace *= 1.008, (r, c, q) => r.AddPace(c, 1 - 0.02 / q, 20));
            Add(EN, "Bleed pressures", 2, "\"Pressures are running high. Bleed a pound off?\"", 45, 5, "Pressures fixed: tire wear −25% for 60 s.", "Wrong call: tire wear +25% for 40 s.",
                (r, c, q) => r.WearFx(c, 0.75, 60), (r, c, q) => r.WearFx(c, 1 + 0.25 / q, 40));
            Add(CC, "Cool the motor", 1, "\"Water temp is climbing. Get some air on the grille?\"", 45, 6, "Temps are down: 8 damage repaired.", "You lost the draft doing it: −0.4 s.",
                (r, c, q) => r.Repair(c, 8, DamageZone.Nose), (r, c, q) => r.Slow(c, 0.4 / q), x => x.Car.Damage > 0);
            Add(EN, "Short-shift", 1, "\"Try short-shifting off turn 4. Might find speed.\"", 35, 6, "Found it: +1 Momentum and +1.5% speed for 20 s.", "Bogged down off the corner: −0.5 s.",
                (r, c, q) => { r.AddMomentum(1); r.AddPace(c, 1.015, 20); }, (r, c, q) => r.Slow(c, 0.5 / q));
            // the bigger calls a levelled crew member unlocks (Q13); Lvl = the level that unlocks it
            Add(SP, "Bump and run", 2, "\"He’s loose off two. Move him. He’d do it to you.\"", 24, 6, "Moved him: you take the spot, he loses time.", "He didn’t budge: −0.6 s, body −6 and +1 Heat.",
                (r, c, q) => { var a = r.AheadOf(c); r.StartOvertake(c, 1); if (a != null) r.Slow(a, 0.6 * q); r.AddMomentum(1); },
                (r, c, q) => { r.Slow(c, 0.6 / q); Dmg(r, c, 6 / q); Heat(r); }, x => x.Ahead != null && x.GapA < 0.5, lvl: 6);
            Add(SP, "Three-wide hole", 2, "\"Hole opening three-wide! Go now and you clear two of them.\"", 12, 6, "Through the hole: two spots in one move.", "It closed on you: −1 s and body −5.",
                (r, c, q) => { r.StartOvertake(c, 2); r.AddMomentum(1); }, (r, c, q) => { r.Slow(c, 1 / q); Dmg(r, c, 5 / q); }, x => x.Ahead != null && x.GapA < 0.9 && x.Pos > 2, lvl: 8);
            Add(CC, "Fuel stretch", 2, "\"Save fuel this run and we can short the next stop.\"", 24, 6, "Fuel saved: your next stop is 1.5 s shorter.", "Saved nothing, lost speed: −1.5% for 15 s.",
                (r, c, q) => r.PitBonus += 1.5 * q, (r, c, q) => r.AddPace(c, 1 - 0.015 / q, 15), lvl: 6, pit: true);
            Add(CC, "All in", 3, "\"This is it. Use everything the car has left.\"", 12, 6, "All in: +3% speed for 25 s and +1 Momentum.", "Cooked the tires: wear +50% for 30 s.",
                (r, c, q) => { r.AddPace(c, 1 + 0.03 * q, 25); r.AddMomentum(1); }, (r, c, q) => r.WearFx(c, 1 + 0.5 / q, 30), x => x.R.LapOf(x.Car) >= x.Track.Laps * 0.7, lvl: 8);
            Add(EN, "Engine map", 2, "\"I can turn the motor up for a few laps. It’ll run hot.\"", 24, 6, "Motor turned up: +4% speed for 12 s.", "It ran hot: body −8.",
                (r, c, q) => r.AddPace(c, 1 + 0.04 * q, 12), (r, c, q) => Dmg(r, c, 8 / q), lvl: 6);
            Add(EN, "Setup trick", 1, "\"Found something in the data. Want to try it live?\"", 12, 6, "It works: +1.2% speed for the rest of the race.", "Wrong direction: −3% speed for 20 s.",
                (r, c, q) => c.PermPace *= 1 + 0.012 * q, (r, c, q) => r.AddPace(c, 1 - 0.03 / q, 20), lvl: 8);
            all.Add(new RadioCallDef { Kind = RadioKind.Advice, W = 3, Name = "Advice" });
        }

        public static RadioCallDef Get(string name)
        {
            foreach (var c in all) if (c.Name == name) return c;
            throw new KeyNotFoundException("No radio call " + name);
        }
    }

    /// <summary>
    /// Crew HQ perks and traits (the slice's PERKS and CTRAITS). Levels 4, 6 and 8 each open a two-way perk choice per seat;
    /// a hired member can bring a trait. Their effects add up into the loadout's CrewPerks, which the radio and the pit
    /// mini-game read. Keys are the slice's: chance, chanceAll, late, pay, again, net, fixOnS, momAll, reroll and the pit keys
    /// add; rate, soft, softAll and weight multiply.
    /// </summary>
    public static class CrewHq
    {
        public sealed class Perk
        {
            public string Id, Name, Text;
            public int Lvl;
            public Dictionary<string, double> Fx;
        }

        public sealed class Trait
        {
            public string Id, Name, Text;
            public Dictionary<string, double> Fx;
            /// <summary>Only for call seats, or only for the pit crew.</summary>
            public bool Calls, Pit;
        }

        static Dictionary<string, double> F(params (string k, double v)[] kv) { var d = new Dictionary<string, double>(); foreach (var (k, v) in kv) d[k] = v; return d; }
        static Perk P(int lvl, string id, string n, string t, params (string, double)[] fx) => new Perk { Lvl = lvl, Id = id, Name = n, Text = t, Fx = F(fx) };

        /// <summary>Per seat, three tiers of (a, b).</summary>
        public static readonly Dictionary<CrewRole, (Perk a, Perk b)[]> Perks = new Dictionary<CrewRole, (Perk, Perk)[]>
        {
            [CrewRole.CrewChief] = new[]
            {
                (P(4, "tires", "Tire whisperer", "Crew chief calls pay 25% more.", ("pay", 0.25)), P(4, "calm", "Calm voice", "+3% on every crew call.", ("chanceAll", 3))),
                (P(6, "strategist", "Strategist", "+1 Prep card at every pit stop.", ("prep", 1)), P(6, "motivator", "Motivator", "Any crew call that works also gives +1 Momentum.", ("momAll", 1))),
                (P(8, "bigpic", "Big picture", "Crew calls come 15% more often.", ("rate", 0.85)), P(8, "safehands", "Safe hands", "Every missed crew call costs half.", ("softAll", 2))),
            },
            [CrewRole.Spotter] = new[]
            {
                (P(4, "eyes", "Eyes up top", "+5% on spotter calls.", ("chance", 5)), P(4, "chatter", "Chatter", "Spotter calls come up twice as often.", ("weight", 2))),
                (P(6, "door", "Door to door", "Spotter calls pay 30% more.", ("pay", 0.3)), P(6, "clean", "Clean racer", "Missed spotter calls cost 60% less.", ("soft", 2.5))),
                (P(8, "again", "Second look", "After a spotter call works, 35% chance another call comes right away.", ("again", 0.35)), P(8, "net", "Safety net", "Half of your missed spotter calls cost nothing.", ("net", 0.5))),
            },
            [CrewRole.Engineer] = new[]
            {
                (P(4, "data", "Data nerd", "+5% on engineer calls.", ("chance", 5)), P(4, "margins", "Safe margins", "Missed engineer calls cost half.", ("soft", 2))),
                (P(6, "overdrive", "Overdrive", "Engineer calls pay 30% more.", ("pay", 0.3)), P(6, "cool", "Cool running", "An engineer call that works also repairs 4 damage.", ("fixOnS", 4))),
                (P(8, "opinion", "Second opinion", "Once a race, a missed crew call is rolled again.", ("reroll", 1)), P(8, "telemetry", "Telemetry", "Engineer calls come up 60% more often.", ("weight", 1.6))),
            },
            [CrewRole.PitCrew] = new[]
            {
                (P(4, "hands", "Quick hands", "Tire cards +6%.", ("tireOdds", 6)), P(4, "gas", "Gas men", "Fuel and any-lane cards +6%.", ("fuelOdds", 6))),
                (P(6, "drilled", "Drilled", "Release window +0.15 s.", ("relWin", 0.15)), P(6, "bench", "Deep bench", "+1 card in every pit hand.", ("pitHand", 1))),
                (P(8, "pitperfect", "Pit perfect", "Tire and fuel lanes start 0.4 s quicker.", ("laneCut", 0.4)), P(8, "bodymen", "Body men", "Fix the Damage takes 2 s less and fixes 10 more.", ("bodymen", 1))),
            },
        };

        public static readonly Dictionary<string, Trait> Traits = new Dictionary<string, Trait>
        {
            ["steady"] = new Trait { Id = "steady", Name = "Steady", Text = "+5% on their calls (pit crew: pit cards).", Fx = F(("chance", 5)) },
            ["gambler"] = new Trait { Id = "gambler", Name = "Gambler", Text = "Their calls pay 25% more but land 5% less often.", Fx = F(("pay", 0.25), ("chance", -5)), Calls = true },
            ["veteran"] = new Trait { Id = "veteran", Name = "Veteran", Text = "Their missed calls cost 30% less.", Fx = F(("soft", 1.43)), Calls = true },
            ["chatty"] = new Trait { Id = "chatty", Name = "Chatty", Text = "Their calls come up 50% more often.", Fx = F(("weight", 1.5)), Calls = true },
            ["clutch"] = new Trait { Id = "clutch", Name = "Clutch", Text = "+12% on their calls in the last 30% of a race.", Fx = F(("late", 12)), Calls = true },
            ["mentor"] = new Trait { Id = "mentor", Name = "Mentor", Text = "The whole crew earns 50% more XP.", Fx = F(("xp", 0.5)) },
            ["lightning"] = new Trait { Id = "lightning", Name = "Lightning", Text = "Pit cards +5% and release window +0.1 s.", Fx = F(("tireOdds", 5), ("fuelOdds", 5), ("relWin", 0.1)), Pit = true },
        };

        /// <summary>XP a crew member earns (the slice's CXP): a call taken, a call that works, a stop, a Perfect Stop.</summary>
        public const int XpCall = 3, XpWon = 3, XpStop = 4, XpPerfect = 4;
    }
}
