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
        public CrewPerks Crew = new CrewPerks();

        public int PitSkill => PitCrew > 0 ? PitCrew : CrewChief;

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
            l.Car = Car.Clone(); l.Deck = new List<string>(Deck); l.PitDeck = new List<string>(PitDeck); l.Parts = new List<string>(Parts); l.Tune = Tune.Clone();
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
    }

    public static class Personas
    {
        static readonly List<Persona> all = new List<Persona>();
        public static IReadOnlyList<Persona> All => all;

        static Tune T(int hand, int aero, ComboFocus f) => new Tune { Hand = hand, Aero = aero, Focus = f };
        static Dictionary<string, int> B(params (string, int)[] kv) { var d = new Dictionary<string, int>(); foreach (var (k, v) in kv) d[k] = v; return d; }

        static Personas()
        {
            all.Add(new Persona { Id = "drafter", Background = "sim", Grit = 12, Name = "The Drafter", Tag = "Superspeedway specialist", Add = new[] { "bump_draft", "slingshot", "tuck_in", "side_draft", "push_limit", "dive_bomb" }, Part = "slick_nose", Bonus = B(("aero", 1)), Tune = T(0, 1, ComboFocus.Draft) });
            all.Add(new Persona { Id = "bully", Background = "hero", Grit = 15, Name = "The Bully", Tag = "Short-track enforcer", Add = new[] { "bump_run", "bump_run", "bump_draft", "block", "dive_bomb", "push_limit" }, Part = "chrome_horn", Bonus = B(("defense", 1), ("durability", 1), ("racecraft", 1)), Tune = T(0, -2, ComboFocus.Bumper) });
            all.Add(new Persona { Id = "whisperer", Background = "dirt", Grit = 15, Name = "Tire Whisperer", Tag = "Long-run closer", Add = new[] { "save_tires", "high_line", "high_line", "push_limit", "clear_air", "regroup" }, Part = "long_run_tires", Bonus = B(("tireMgmt", 2)), Tune = T(-1, -1, ComboFocus.LongRun) });
            all.Add(new Persona { Id = "restart", Background = "crew", Grit = 12, Name = "Restart Ace", Tag = "Chaos merchant", Add = new[] { "restart_jump", "restart_jump", "choose_lane", "block", "dive_bomb", "send_it" }, Part = "launch_control", Bonus = B(("racecraft", 1), ("crewChief", 1)), Tune = T(1, 0, ComboFocus.Restart) });
            all.Add(new Persona { Id = "gambler", Background = "rich", Grit = 9, Name = "Pit Road Gambler", Tag = "Track-position thief", Add = new[] { "stay_out", "stay_out", "choose_lane", "restart_jump", "tuck_in", "fast_stop" }, Part = "pit_clipboard", Bonus = B(("defense", 1), ("crewChief", 1)), Tune = T(-1, 0, ComboFocus.Restart) });
            all.Add(new Persona { Id = "brawler", Background = "hero", Grit = 15, Kart = true, Name = "The Brawler", Tag = "Club-track scrapper", Add = new[] { "k_push", "k_door", "k_door", "k_switch", "k_smooth", "k_latebrake" }, Part = "nerf_bar", Bonus = B(("durability", 1), ("racecraft", 1), ("defense", 1)), Tune = T(1, -1, ComboFocus.Bumper) });
            all.Add(new Persona { Id = "linepainter", Background = "dirt", Grit = 15, Kart = true, Name = "The Line Painter", Tag = "Clean and consistent", Add = new[] { "k_outside", "k_owninside", "k_smooth", "k_smooth", "k_limit", "k_cover" }, Part = "chalk_line", Bonus = B(("consistency", 1), ("handling", 1), ("tireMgmt", 1)), Tune = T(-1, 0, ComboFocus.LongRun) });
            all.Add(new Persona { Id = "packrat", Background = "sim", Grit = 12, Kart = true, Name = "The Pack Rat", Tag = "Lives in the tow", Add = new[] { "k_tow", "k_smooth", "k_push", "k_pullout", "k_pullout", "k_latebrake" }, Part = "tow_hitch", Bonus = B(("aero", 1), ("racecraft", 1)), Tune = T(0, 1, ComboFocus.Draft) });
            all.Add(new Persona { Id = "latebraker", Background = "crew", Grit = 12, Kart = true, Name = "The Late Braker", Tag = "Hairpin hunter", Add = new[] { "k_latebrake", "k_latebrake", "k_pullout", "k_smooth", "k_cover", "k_limit" }, Part = "big_rotors", Bonus = B(("racecraft", 1), ("handling", 1)), Tune = T(1, -1, ComboFocus.Bumper) });
        }

        public static Persona Get(string id)
        {
            foreach (var p in all) if (p.Id == id) return p;
            throw new KeyNotFoundException("No persona " + id);
        }
    }
}
