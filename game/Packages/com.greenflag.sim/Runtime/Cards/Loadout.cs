using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>
    /// Pit crew perks from Crew HQ (step 19) that the pit mini-game reads. All zero for a career without Crew HQ upgrades.
    /// </summary>
    public sealed class CrewPerks
    {
        /// <summary>Seconds off the base tire and fuel lanes.</summary>
        public double LaneCut;
        /// <summary>Odds added to every pit card, and to tire-lane or fuel-lane cards only.</summary>
        public int Chance, TireOdds, FuelOdds;
        /// <summary>Extra pit cards in hand, and extra Prep cards.</summary>
        public int PitHand, Prep;
        /// <summary>Seconds added to the release window.</summary>
        public double ReleaseWindow;
        /// <summary>Body men: Fix the Damage is 2 s quicker and fixes 10 more.</summary>
        public bool BodyMen;

        // crew calls (step 33): per seat, and across the whole crew
        public CallFx Chief = new CallFx(), Spotter = new CallFx(), Engineer = new CallFx();
        /// <summary>Odds on every crew call; Momentum when any call works.</summary>
        public int ChanceAll, MomAll;
        /// <summary>Misses on every call are divided by this; the gap between calls is multiplied by Rate.</summary>
        public double SoftAll = 1, Rate = 1;
        /// <summary>Second opinion: once a race a missed call is rolled again.</summary>
        public bool Reroll;
        /// <summary>Extra crew XP (Mentor).</summary>
        public double Xp;

        public CallFx For(CrewRole r) => r == CrewRole.Spotter ? Spotter : r == CrewRole.Engineer ? Engineer : Chief;

        /// <summary>Adds a perk's or trait's effects (the slice's fx keys) for a seat.</summary>
        public void Add(CrewRole role, Dictionary<string, double> fx)
        {
            bool pit = role == CrewRole.PitCrew;
            var c = pit ? null : For(role);
            foreach (var kv in fx)
            {
                double v = kv.Value;
                switch (kv.Key)
                {
                    case "chance": if (pit) Chance += (int)v; else c.Chance += (int)v; break;
                    case "late": if (!pit) c.Late += (int)v; break;
                    case "pay": if (!pit) c.Pay += v; break;
                    case "soft": if (!pit) c.Soft *= v; break;
                    case "weight": if (!pit) c.Weight *= v; break;
                    case "again": if (!pit) c.Again += v; break;
                    case "net": if (!pit) c.Net += v; break;
                    case "fixOnS": if (!pit) c.FixOnS += v; break;
                    case "chanceAll": ChanceAll += (int)v; break;
                    case "momAll": MomAll += (int)v; break;
                    case "softAll": SoftAll *= v; break;
                    case "rate": Rate *= v; break;
                    case "reroll": Reroll = true; break;
                    case "xp": Xp += v; break;
                    case "prep": Prep += (int)v; break;
                    case "tireOdds": TireOdds += (int)v; break;
                    case "fuelOdds": FuelOdds += (int)v; break;
                    case "relWin": ReleaseWindow += v; break;
                    case "pitHand": PitHand += (int)v; break;
                    case "laneCut": LaneCut += v; break;
                    case "bodymen": BodyMen = true; break;
                    default: throw new ArgumentException("Unknown crew effect " + kv.Key);
                }
            }
        }

        public CrewPerks Clone()
        {
            var p = (CrewPerks)MemberwiseClone();
            p.Chief = new CallFx(); p.Spotter = new CallFx(); p.Engineer = new CallFx();
            p.Chief.CopyFrom(Chief); p.Spotter.CopyFrom(Spotter); p.Engineer.CopyFrom(Engineer);
            return p;
        }
    }

    /// <summary>What perks and traits do to one call seat's radio calls (the slice's crewFx with a role).</summary>
    public sealed class CallFx
    {
        /// <summary>Odds on this seat's calls, and extra odds in the last 30% of a race (Clutch).</summary>
        public int Chance, Late;
        /// <summary>Payoff: q is multiplied by 1 + Pay. Misses are divided by Soft. Weight: how often its calls come up.</summary>
        public double Pay, Soft = 1, Weight = 1;
        /// <summary>Second look: chance another call comes 2 s after one works. Safety net: share of misses that cost nothing.</summary>
        public double Again, Net;
        /// <summary>Cool running: damage repaired when a call works.</summary>
        public double FixOnS;

        internal void CopyFrom(CallFx o) { Chance = o.Chance; Late = o.Late; Pay = o.Pay; Soft = o.Soft; Weight = o.Weight; Again = o.Again; Net = o.Net; FixOnS = o.FixOnS; }
    }

    /// <summary>
    /// Everything about you that the card layer reads: the car, the crew, the race deck, the pit deck, equipped parts, the
    /// tune and your driver's Grit. Give one to RaceSetup.Loadout to race with cards; without one the race is the step 26
    /// hands-off core.
    /// </summary>
    public sealed class PlayerLoadout
    {
        public CarStats Car = CarStats.Rookie();
        public int CrewChief = 3, Spotter = 3, Engineer = 3;
        /// <summary>Pit crew chief skill (Crew HQ). 0 = the crew chief runs the pit crew too.</summary>
        public int PitCrew;
        public List<string> Deck = new List<string>();
        public List<string> PitDeck = new List<string>(PitCards.StartingDeck);
        public List<string> Parts = new List<string>();
        public Tune Tune = new Tune();
        /// <summary>Driver Grit (step 18 attribute, 10 = average): late in a race every card reads a little better or worse.</summary>
        public int Grit = 10;
        /// <summary>Driver Wits (step 18 attribute): crew calls stay on the radio 1 s longer per modifier point.</summary>
        public int Wits = 10;
        /// <summary>Crew radio calls on (the slice always has them; tests and old harness runs can turn them off).</summary>
        public bool Radio = true;
        public CrewPerks Crew = new CrewPerks();
        /// <summary>Momentum at the green flag that the driver promised off track (step 38), on top of the parts'.</summary>
        public int DriverMom;

        public int PitSkill => PitCrew > 0 ? PitCrew : CrewChief;

        /// <summary>A seat's level (the slice's G.stats[role]).</summary>
        public int Level(CrewRole r) => r == CrewRole.Spotter ? Spotter : r == CrewRole.Engineer ? Engineer : r == CrewRole.PitCrew ? PitSkill : CrewChief;

        /// <summary>A crew call's chance to work: base + level x per, the Clutch Radio, perks and traits (Clutch only when late
        /// is true: the last 30% of the race). 5 to 95. The race and Crew HQ both read it.</summary>
        public int CallChance(RadioCallDef d, bool late)
        {
            var fx = Crew.For(d.Role);
            double v = d.Base + Level(d.Role) * d.Per + PartSum(p => p.RadioBonus) + fx.Chance + Crew.ChanceAll + (late ? fx.Late : 0);
            return (int)SimMath.Clamp(v, 5, 95);
        }

        /// <summary>A better crew talks more: the gap between calls is 2% shorter per average level above 3, times perks.</summary>
        public double CrewRate()
        {
            double a = (Math.Max(0, CrewChief - 3) + Math.Max(0, Spotter - 3) + Math.Max(0, Engineer - 3)) / 3.0;
            return Math.Max(0.5, (1 - 0.02 * a) * Crew.Rate);
        }

        /// <summary>Lock in a Crew HQ perk: tier 0, 1 or 2 (levels 4, 6, 8), option a or b. Returns why not, or null.</summary>
        public string PickPerk(CrewRole role, int tier, bool b)
        {
            var t = CrewHq.Perks[role][tier];
            var p = b ? t.b : t.a;
            if (Level(role) < p.Lvl) return $"Needs level {p.Lvl}";
            Crew.Add(role, p.Fx);
            return null;
        }

        /// <summary>A hired crew member's trait.</summary>
        public void AddTrait(CrewRole role, string trait) => Crew.Add(role, CrewHq.Traits[trait].Fx);

        public IEnumerable<PartDef> EquippedParts()
        {
            foreach (var id in Parts) yield return GreenFlag.Sim.Parts.Get(id);
        }

        public double PartMul(Func<PartDef, double> f) { double m = 1; foreach (var p in EquippedParts()) m *= f(p); return m; }
        public double PartSum(Func<PartDef, double> f) { double s = 0; foreach (var p in EquippedParts()) s += f(p); return s; }

        /// <summary>The race-core multipliers this loadout gives your car on a track with a given tune (relicMul, tunePace, tuneWear).</summary>
        public void Apply(PlayerMods m, TrackDef track, Tune tune)
        {
            m.Pace = PartMul(p => p.Pace) * tune.Pace(track);
            m.Wear = PartMul(p => p.Wear) * tune.Wear;
            m.Draft = PartMul(p => p.Draft) * tune.DraftMul;
            m.Damage = PartMul(p => p.Dmg);
            m.PitSeconds = PartSum(p => p.Pit);
            m.Launch = PartSum(p => p.Launch ? 1 : 0) > 0;
            m.CrewChief = CrewChief;
        }

        public PlayerLoadout Clone()
        {
            var l = (PlayerLoadout)MemberwiseClone();
            l.Car = Car.Clone(); l.Crew = Crew.Clone(); l.Deck = new List<string>(Deck); l.PitDeck = new List<string>(PitDeck); l.Parts = new List<string>(Parts); l.Tune = Tune.Clone();
            return l;
        }

        /// <summary>A new career's loadout for a persona: the base deck plus the persona's six cards, its part, stat bonus and tune.</summary>
        public static PlayerLoadout ForPersona(string personaId)
        {
            var p = Personas.Get(personaId);
            var l = new PlayerLoadout();
            l.Deck.AddRange(p.Kart ? CardBook.KartBase : CardBook.StockBase);
            l.Deck.AddRange(p.Add);
            l.Parts.Add(p.Part);
            foreach (var kv in p.Bonus) l.AddStat(kv.Key, kv.Value);
            l.Tune = p.Tune.Clone();
            l.Grit = p.Grit;
            l.Wits = p.Wits;
            return l;
        }

        /// <summary>Add to a stat by the slice's name (car stats, crewChief, spotter, engineer).</summary>
        public void AddStat(string stat, int v)
        {
            switch (stat)
            {
                case "power": Car.Power += v; break;
                case "handling": Car.Handling += v; break;
                case "aero": Car.Aero += v; break;
                case "durability": Car.Durability += v; break;
                case "racecraft": Car.Racecraft += v; break;
                case "defense": Car.Defense += v; break;
                case "consistency": Car.Consistency += v; break;
                case "tireMgmt": Car.TireMgmt += v; break;
                case "crewChief": CrewChief += v; break;
                case "spotter": Spotter += v; break;
                case "engineer": Engineer += v; break;
                case "pitCrew": PitCrew = PitSkill + v; break;
                default: throw new ArgumentException("Unknown stat " + stat);
            }
        }
    }

    /// <summary>A starting persona: six extra cards, a part, a stat bonus, a default tune and the driver background a new career gets.</summary>
    public sealed class Persona
    {
        public string Id, Name, Tag;
        public bool Kart;
        public string[] Add;
        public string Part;
        public Dictionary<string, int> Bonus;
        public Tune Tune;
        /// <summary>The driver background a new career with this persona starts with (the slice's ARCH bg), and its Grit.</summary>
        public string Background;
        public int Grit = 10;
        /// <summary>The background's Wits (crew calls stay up longer).</summary>
        public int Wits = 10;
    }

    public static class Personas
    {
        static readonly List<Persona> all = new List<Persona>();
        public static IReadOnlyList<Persona> All => all;

        static Tune T(int hand, int aero, ComboFocus f) => new Tune { Hand = hand, Aero = aero, Focus = f };
        static Dictionary<string, int> B(params (string, int)[] kv) { var d = new Dictionary<string, int>(); foreach (var (k, v) in kv) d[k] = v; return d; }

        static Personas()
        {
            all.Add(new Persona { Id = "drafter", Background = "sim", Grit = 12, Wits = 15, Name = "The Drafter", Tag = "Superspeedway specialist", Add = new[] { "bump_draft", "slingshot", "tuck_in", "side_draft", "push_limit", "dive_bomb" }, Part = "slick_nose", Bonus = B(("aero", 1)), Tune = T(0, 1, ComboFocus.Draft) });
            all.Add(new Persona { Id = "bully", Background = "hero", Grit = 15, Wits = 12, Name = "The Bully", Tag = "Short-track enforcer", Add = new[] { "bump_run", "bump_run", "bump_draft", "block", "dive_bomb", "push_limit" }, Part = "chrome_horn", Bonus = B(("defense", 1), ("durability", 1), ("racecraft", 1)), Tune = T(0, -2, ComboFocus.Bumper) });
            all.Add(new Persona { Id = "whisperer", Background = "dirt", Grit = 15, Wits = 12, Name = "Tire Whisperer", Tag = "Long-run closer", Add = new[] { "save_tires", "high_line", "high_line", "push_limit", "clear_air", "regroup" }, Part = "long_run_tires", Bonus = B(("tireMgmt", 2)), Tune = T(-1, -1, ComboFocus.LongRun) });
            all.Add(new Persona { Id = "restart", Background = "crew", Grit = 12, Wits = 14, Name = "Restart Ace", Tag = "Chaos merchant", Add = new[] { "restart_jump", "restart_jump", "choose_lane", "block", "dive_bomb", "send_it" }, Part = "launch_control", Bonus = B(("racecraft", 1), ("crewChief", 1)), Tune = T(1, 0, ComboFocus.Restart) });
            all.Add(new Persona { Id = "gambler", Background = "rich", Grit = 9, Wits = 13, Name = "Pit Road Gambler", Tag = "Track-position thief", Add = new[] { "stay_out", "stay_out", "choose_lane", "restart_jump", "tuck_in", "fast_stop" }, Part = "pit_clipboard", Bonus = B(("defense", 1), ("crewChief", 1)), Tune = T(-1, 0, ComboFocus.Restart) });
            all.Add(new Persona { Id = "brawler", Background = "hero", Grit = 15, Wits = 12, Kart = true, Name = "The Brawler", Tag = "Club-track scrapper", Add = new[] { "k_push", "k_door", "k_door", "k_switch", "k_smooth", "k_latebrake" }, Part = "nerf_bar", Bonus = B(("durability", 1), ("racecraft", 1), ("defense", 1)), Tune = T(1, -1, ComboFocus.Bumper) });
            all.Add(new Persona { Id = "linepainter", Background = "dirt", Grit = 15, Wits = 12, Kart = true, Name = "The Line Painter", Tag = "Clean and consistent", Add = new[] { "k_outside", "k_owninside", "k_smooth", "k_smooth", "k_limit", "k_cover" }, Part = "chalk_line", Bonus = B(("consistency", 1), ("handling", 1), ("tireMgmt", 1)), Tune = T(-1, 0, ComboFocus.LongRun) });
            all.Add(new Persona { Id = "packrat", Background = "sim", Grit = 12, Wits = 15, Kart = true, Name = "The Pack Rat", Tag = "Lives in the tow", Add = new[] { "k_tow", "k_smooth", "k_push", "k_pullout", "k_pullout", "k_latebrake" }, Part = "tow_hitch", Bonus = B(("aero", 1), ("racecraft", 1)), Tune = T(0, 1, ComboFocus.Draft) });
            all.Add(new Persona { Id = "latebraker", Background = "crew", Grit = 12, Wits = 14, Kart = true, Name = "The Late Braker", Tag = "Hairpin hunter", Add = new[] { "k_latebrake", "k_latebrake", "k_pullout", "k_smooth", "k_cover", "k_limit" }, Part = "big_rotors", Bonus = B(("racecraft", 1), ("handling", 1)), Tune = T(1, -1, ComboFocus.Bumper) });
        }

        public static Persona Get(string id)
        {
            foreach (var p in all) if (p.Id == id) return p;
            throw new KeyNotFoundException("No persona " + id);
        }
    }
}
