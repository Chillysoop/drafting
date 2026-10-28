using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum Trait { None, Aggressive, Smooth, Wall, Drafter, Closer, Rookie, Veteran, Restarter }

    /// <summary>A car's eight on-track stats (1 to 10). Crew stats live on the player setup.</summary>
    public sealed class CarStats
    {
        public int Power, Handling, Aero, Durability, Racecraft, Defense, Consistency, TireMgmt;

        /// <summary>A new career's car before persona bonuses (the slice's newCareer stats).</summary>
        public static CarStats Rookie() => new CarStats { Power = 3, Handling = 3, Aero = 3, Durability = 4, Racecraft = 4, Defense = 4, Consistency = 4, TireMgmt = 4 };

        public CarStats Clone() => (CarStats)MemberwiseClone();

        /// <summary>The slice's carRating: the qualifying and expected-finish yardstick.</summary>
        public double Rating => (Power - 5) * 0.0032 + (Handling - 5) * 0.0024 + (Racecraft - 5) * 0.0012 + (Aero - 5) * 0.001;
    }

    /// <summary>One car and driver in the season's field.</summary>
    public sealed class Entrant
    {
        public int Id, Team, TeamRating, Number;
        public bool IsMe;
        public string Name;
        public Trait Trait;
        public CarStats Stats;
        /// <summary>Career record (step 37): starts, wins, wins this season, top 5s and best finish (99 = none yet).</summary>
        public int Starts, Wins, SeasonWins, Top5, Best = 99;

        public Entrant Clone() { var e = (Entrant)MemberwiseClone(); e.Stats = Stats?.Clone(); return e; }
    }

    public readonly struct TeamDef
    {
        public readonly string Name;
        public readonly int Rating;
        public readonly bool Player;
        public TeamDef(string name, int rating, bool player = false) { Name = name; Rating = rating; Player = player; }
    }

    /// <summary>The ten teams (two cars each). Each series renames them; the ratings stay the same, as in the slice.</summary>
    public static class Teams
    {
        static readonly int[] Ratings = { 9, 8, 8, 7, 6, 6, 5, 4, 3, 3 };
        static readonly string[] Stock = { "Apex Motorsports", "Harlan-Reed Racing", "Northline Racing", "Vantage Performance", "Copperhead Motorsports", "Bluewater Racing", "Ridgeway Racing", "Granite State Motorsports", "Dusty Road Racing", "Lowline Motorsports" };
        static readonly string[] Kart = { "Velocity Kart Works", "Brickyard Juniors", "Tri-County Karting", "Hotshoe Karts", "Kingpin Kart Club", "Rattlesnake Karts", "Sawmill Kart Shop", "Burnout Brothers", "Gravel Lot Karting", "Garage 77 Karting" };
        static readonly string[] Late = { "Delmar Late Models", "Cobalt Speed Shop", "Ironside Racing", "Big Sky Late Models", "Rusty Gate Motorsports", "Bayou Bullring Racing", "Two Rivers Racing", "Mason-Dix Motorsports", "Backstretch Garage", "Lowline Late Models" };
        public const int PlayerTeam = 9;

        public static TeamDef[] For(Series s)
        {
            var names = s == Series.Kart ? Kart : s == Series.Late ? Late : Stock;
            var t = new TeamDef[names.Length];
            for (int i = 0; i < names.Length; i++) t[i] = new TeamDef(names[i], Ratings[i], i == PlayerTeam);
            return t;
        }
    }

    /// <summary>The slice's buildField: 20 cars, two per team, AI stats rolled around the team rating.</summary>
    public static class FieldBuilder
    {
        static readonly string[] Names = { "Diego Reyes", "Tobi Okafor", "Erik Lindqvist", "Cal Bram", "Marco Castellano", "Jace Whitaker", "Ren Nakamura", "Luc Dubois", "Wade Haskins", "Remy Moreau", "Ivan Petrov", "Femi Adeyemi", "Shane Sullivan", "Kasey Kowalski", "Bruno Ferreira", "Kenta Tanaka", "Rory McAllister", "Liam Brennan", "Ana Ruiz" };
        static readonly Trait[] TraitPool = { Trait.Aggressive, Trait.Smooth, Trait.Wall, Trait.Drafter, Trait.Closer, Trait.Rookie, Trait.Veteran, Trait.Restarter };
        /// <summary>Step 40: karts have no restarts, so a kart field has more rookies instead (same pool size, same rolls).</summary>
        static readonly Trait[] KartPool = { Trait.Aggressive, Trait.Smooth, Trait.Wall, Trait.Drafter, Trait.Closer, Trait.Rookie, Trait.Veteran, Trait.Rookie };

        /// <summary>What each AI style is called in a kart field (step 40), for screens and the booth.</summary>
        public static string KartStyle(Trait t) =>
            t == Trait.Aggressive ? "Bumper" : t == Trait.Smooth ? "Tidy" : t == Trait.Wall ? "Blocker" : t == Trait.Drafter ? "Train rat"
            : t == Trait.Closer ? "Late braker" : t == Trait.Rookie ? "Rookie" : t == Trait.Veteran ? "Old hand" : "";

        /// <summary>What each AI style is called in a late model field (step 41): the tire strategy it runs.</summary>
        public static string LateStyleName(Trait t) =>
            t == Trait.Smooth ? "Tire saver" : t == Trait.Veteran ? "Old hand (saver)" : t == Trait.Aggressive ? "Charger" : t == Trait.Closer ? "Closer (charger)"
            : t == Trait.Wall ? "Blocker" : t == Trait.Drafter ? "Pusher" : t == Trait.Rookie ? "Rookie" : t == Trait.Restarter ? "Restarter" : "";

        public static List<Entrant> Build(SimRng rng, Series series, CarStats myStats, string myName = "Rookie")
        {
            var names = new List<string>(Names);
            rng.Shuffle(names);
            var nums = new List<int>();
            for (int n = 2; n < 100; n++) if (n != 77) nums.Add(n);
            rng.Shuffle(nums);
            int ni = 0;
            var teams = Teams.For(series);
            var cars = new List<Entrant>();
            for (int ti = 0; ti < teams.Length; ti++)
            {
                var t = teams[ti];
                for (int k = 0; k < 2; k++)
                {
                    bool isMe = t.Player && k == 0;
                    var e = new Entrant { Id = cars.Count, Team = ti, TeamRating = t.Rating, IsMe = isMe };
                    if (isMe)
                    {
                        e.Number = 77; e.Name = myName; e.Trait = Trait.None; e.Stats = myStats;
                    }
                    else
                    {
                        int d = (int)SimMath.Clamp(SimMath.RoundJs(t.Rating * 0.6 + rng.Rand(1, 4)), 2, 9);
                        int R(int b) => (int)SimMath.Clamp(b + rng.RandI(-1, 1), 1, 10);
                        e.Number = nums[ni++];
                        e.Name = names[names.Count - 1];
                        names.RemoveAt(names.Count - 1);
                        e.Trait = rng.Pick(series == Series.Kart ? KartPool : TraitPool);
                        e.Stats = new CarStats { Power = R(t.Rating), Handling = R(t.Rating), Aero = R(t.Rating), Durability = R(5), Racecraft = R(d), Defense = R(d), Consistency = R(d), TireMgmt = R(d) };
                    }
                    cars.Add(e);
                }
            }
            return cars;
        }
    }
}
