using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>
    /// What the player's build does to the race core. All 1 for a car with no parts or tune; with a Loadout the race fills
    /// them from the equipped parts and the tune (the slice's relicMul / tunePace / tuneWear) and keeps them current when a
    /// pit Setup nudges the tune. Setup tire wear is applied on top, live.
    /// </summary>
    public sealed class PlayerMods
    {
        public double Pace = 1, Wear = 1, Draft = 1, Damage = 1;
        /// <summary>Seconds off every pit stop (parts).</summary>
        public double PitSeconds;
        /// <summary>Launch Control: +4% for the first 1.5 s of a restart.</summary>
        public bool Launch;
        /// <summary>Crew chief stat: stop times, slip chance and caution calls.</summary>
        public int CrewChief = 3;
    }

    public sealed class RaceSetup
    {
        public TrackDef Track;
        public List<Entrant> Field;
        public uint Seed;
        public Difficulty Difficulty = Difficulty.For(DifficultyLevel.Normal);
        public SimBalance Balance = SimBalance.Engine();
        public PlayerMods Mods = new PlayerMods();
        /// <summary>Entrant ids in grid order (from qualifying). Null = the slice's roll-off by car rating.</summary>
        public List<int> Grid;
        /// <summary>Your deck, parts, tune and crew. Null = no cards: the step 26 hands-off race core.</summary>
        public PlayerLoadout Loadout;
        /// <summary>Who plays your pit stops when the race has cards.</summary>
        public PitPolicy PitPolicy = PitPolicy.Bot;

        /// <summary>A race on a fresh field for the track's series, everything rolled from one seed.</summary>
        public static RaceSetup Quick(TrackDef track, uint seed, CarStats myStats = null)
        {
            var fieldRng = new SimRng(seed ^ 0x9E3779B9u);
            return new RaceSetup { Track = track, Seed = seed, Field = FieldBuilder.Build(fieldRng, track.Series, myStats ?? CarStats.Rookie()) };
        }

        /// <summary>A race with cards: the same field as Quick, your car from the loadout.</summary>
        public static RaceSetup Quick(TrackDef track, uint seed, PlayerLoadout loadout)
        {
            var s = Quick(track, seed, loadout.Car);
            s.Loadout = loadout;
            return s;
        }
    }

    public sealed class CarResult
    {
        public int Id, Start, Finish, Passes, PitStops;
        public bool IsMe, Dnf;
        public double Damage, Tires;
    }

    public sealed class RaceResult
    {
        public string TrackId;
        public uint Seed;
        /// <summary>Every car in finishing order (DNFs at the back, in the order they ran when the leader took the flag).</summary>
        public List<CarResult> Order = new List<CarResult>();
        public int MyStart, MyFinish, MyExpected, Cautions, Wrecks, Dnfs;
        public bool MyDnf;
        public double Duration;
    }
}
