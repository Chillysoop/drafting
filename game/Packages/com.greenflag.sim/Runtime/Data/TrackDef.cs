using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum Series { Kart, Late, Stock }

    /// <summary>One piece of a non-oval layout: a straight of length S, or a turn of A degrees (positive = left) at radius R.</summary>
    public readonly struct PathPiece
    {
        public readonly double S, A, R;
        public readonly string Name;
        public bool IsStraight => R == 0;

        PathPiece(double s, double a, double r, string name) { S = s; A = a; R = r; Name = name; }
        public static PathPiece Straight(double s) => new PathPiece(s, 0, 0, null);
        public static PathPiece Turn(double a, double r, string name = null) => new PathPiece(0, a, r, name);
    }

    /// <summary>A track as the sim needs it: the slice's TRACKS entry (plus its PIT.fuelRange), without blurbs or art.</summary>
    public sealed class TrackDef
    {
        public string Id, Name, Type;
        public int Laps;
        public double LapTime;
        /// <summary>Oval straight length and turn radius. Path tracks get S from their geometry.</summary>
        public double S, R;
        public double Draft, DraftRange, Wear, Contact, PassDiff, HandlingW;
        /// <summary>Per-track AI wreck scale (BAL.trackWreck); null = 1.</summary>
        public double? AiWreckK;
        public bool Kart, Late, Fig8;
        /// <summary>How much the draft matters here (-1 none to 1 superspeedway). Cards read it.</summary>
        public double DraftBias;
        /// <summary>Draw beats: how many per lap on an oval (1 = back straight, 2 = both), and cards drawn at each.</summary>
        public int BeatPoints = 1, BeatDraw = 2;
        /// <summary>Path tracks: the path pieces whose braking zones are draw beats.</summary>
        public int[] BeatAt;
        public int FuelRange;
        /// <summary>Null for an oval.</summary>
        public PathPiece[] Path;

        public bool IsPath => Path != null;
        public Series Series => Kart ? Series.Kart : Late ? Series.Late : Series.Stock;

        public TrackDef With(string id, System.Action<TrackDef> change)
        {
            var t = (TrackDef)MemberwiseClone();
            t.Id = id;
            change(t);
            return t;
        }
    }

    /// <summary>Every track in slice v47 (three stock ovals, two stock non-ovals, two kart circuits and three late model tracks), plus
    /// the two kart circuits from step 40 and the two late model tracks from step 41. New tracks go on the end so the browser harness's track indexes still match.</summary>
    public static class Tracks
    {
        static PathPiece St(double s) => PathPiece.Straight(s);
        static PathPiece Tn(double a, double r, string n = null) => PathPiece.Turn(a, r, n);

        public static readonly TrackDef Millbrook = new TrackDef { Id = "mill", DraftBias = -0.5, Name = "Millbrook Speedway", Type = "Short track", Laps = 24, LapTime = 18, S = 1.15, R = 0.85, Draft = 0.006, DraftRange = 0.35, Wear = 1.1, Contact = 1.35, PassDiff = 1.12, HandlingW = 1.6, FuelRange = 14 };
        public static readonly TrackDef Carver = new TrackDef { Id = "carver", DraftBias = 0.3, BeatPoints = 2, Name = "Carver Motor Speedway", Type = "Intermediate", Laps = 16, LapTime = 26, S = 1.9, R = 1.0, Draft = 0.012, DraftRange = 0.6, Wear = 1.0, Contact = 1.0, PassDiff = 1.0, HandlingW = 1.0, FuelRange = 10 };
        public static readonly TrackDef Talon = new TrackDef { Id = "talon", DraftBias = 1, BeatPoints = 2, BeatDraw = 3, Name = "Talon Ridge Superspeedway", Type = "Superspeedway", Laps = 10, LapTime = 42, S = 2.8, R = 1.15, Draft = 0.03, DraftRange = 1.1, Wear = 0.6, Contact = 1.25, PassDiff = 0.85, HandlingW = 0.5, FuelRange = 6 };

        public static readonly TrackDef Pinewood = new TrackDef
        {
            Id = "pine", DraftBias = -1, BeatPoints = 2, BeatAt = new[] { 8, 17 }, Name = "Pinewood Kart Park", Type = "Kart circuit", Laps = 15, LapTime = 28, R = 0.5, Draft = 0.002, DraftRange = 0.3, Wear = 1.2, Contact = 1.4, PassDiff = 1.12, HandlingW = 1.8, AiWreckK = 0.7, FuelRange = 10,
            Path = new[] { St(1.9982), Tn(90, 0.5), St(1.0482), Tn(-35, 0.4), Tn(35, 0.4), St(0.35), Tn(90, 0.45), St(0.4), Tn(90, 0.4), St(1), Tn(-180, 0.4, "Bottom Hairpin"), St(1), Tn(180, 0.4, "Top Hairpin"), St(0.9), Tn(-45, 0.5), Tn(45, 0.5), St(0.3), Tn(90, 0.5) },
        };

        public static readonly TrackDef Ridgeback = new TrackDef
        {
            Id = "ridge", DraftBias = 0.2, BeatPoints = 3, BeatAt = new[] { 6, 10, 19 }, Name = "Ridgeback Road Course", Type = "Road course", Laps = 10, LapTime = 42, R = 0.5, Draft = 0.014, DraftRange = 0.65, Wear = 1.1, Contact = 1.1, PassDiff = 1.06, HandlingW = 1.5, FuelRange = 6,
            Path = new[] { St(1.9126), Tn(90, 0.6), St(0.7813), Tn(-50, 1.1, "Ridge Sweeper"), Tn(50, 1.1, "Ridge Sweeper"), St(0.5), Tn(90, 0.5), St(4.2), Tn(180, 0.42), St(1), Tn(-35, 0.35), Tn(35, 0.35), St(0.6), Tn(-90, 0.5), St(0.4), Tn(-90, 0.5), St(0.6), Tn(90, 0.7), St(0.3), Tn(90, 0.7) },
        };

        public static readonly TrackDef PinewoodKart = Pinewood.With("pinek", t => { t.Kart = true; t.Laps = 13; t.Draft = 0.008; t.DraftRange = 0.45; t.Wear = 0.6; t.FuelRange = 99; t.DraftBias = -0.6; });

        public static readonly TrackDef HollowCreek = new TrackDef
        {
            Id = "hollow", DraftBias = 0.2, BeatPoints = 2, BeatAt = new[] { 1, 9 }, Name = "Hollow Creek Kartway", Type = "Kart circuit", Kart = true, Laps = 14, LapTime = 25, R = 0.5, Draft = 0.014, DraftRange = 0.55, Wear = 0.6, Contact = 1.3, PassDiff = 1.05, HandlingW = 1.6, AiWreckK = 0.7, FuelRange = 99,
            Path = new[] { St(1.8), Tn(90, 0.5), St(0.7588), Tn(-90, 0.45), St(0.5), Tn(90, 0.45), St(0.6), Tn(90, 0.5), St(6.3698), Tn(180, 0.38, "Creek Hairpin"), St(0.6), Tn(-50, 0.4), Tn(50, 0.4), St(0.5), Tn(-100, 0.55), St(0.4), Tn(100, 1, "Hollow Sweeper") },
        };

        /// <summary>Step 40: a tight, twisty kart club. Two hairpins, a chicane and esses, short straights and almost no tow.</summary>
        public static readonly TrackDef BrambleHill = new TrackDef
        {
            Id = "bramble", DraftBias = -0.8, BeatPoints = 2, BeatAt = new[] { 6, 13 }, Name = "Bramble Hill Kart Club", Type = "Kart circuit", Kart = true, Laps = 15, LapTime = 23, R = 0.5, Draft = 0.004, DraftRange = 0.35, Wear = 0.6, Contact = 1.45, PassDiff = 1.15, HandlingW = 1.9, AiWreckK = 0.7, FuelRange = 99,
            Path = new[] { St(1.636396), Tn(90, 0.45), St(0.6), Tn(60, 0.4), Tn(-60, 0.4), St(0.5), Tn(180, 0.4, "Bramble Hairpin"), St(0.856424), Tn(-90, 0.45), St(0.9), Tn(-45, 0.45), Tn(45, 0.45), St(0.5), Tn(180, 0.6, "Thicket Hairpin"), St(1.6) },
        };

        /// <summary>Step 40: a fast lakeside kartway. Long straights, big sweepers and one hairpin at the end of the back straight: tow trains.</summary>
        public static readonly TrackDef Lakeshore = new TrackDef
        {
            Id = "lake", DraftBias = 0.6, BeatPoints = 2, BeatAt = new[] { 1, 6 }, Name = "Lakeshore Kartway", Type = "Kart circuit", Kart = true, Laps = 12, LapTime = 29, R = 0.5, Draft = 0.02, DraftRange = 0.7, Wear = 0.6, Contact = 1.2, PassDiff = 1.0, HandlingW = 1.3, AiWreckK = 0.7, FuelRange = 99,
            Path = new[] { St(3), Tn(60, 1.2, "Lakeshore Sweeper"), Tn(30, 1), St(0.1), Tn(90, 0.9), St(6.405256), Tn(180, 0.55, "Boathouse Hairpin"), St(0.8), Tn(-60, 1, "Dune Kink"), Tn(60, 1, "Dune Kink"), St(0.6) },
        };

        public static readonly TrackDef CedarBowl = new TrackDef { Id = "cedar", DraftBias = -0.9, Name = "Cedar Bowl", Type = "Bullring", Late = true, Laps = 24, LapTime = 16, S = 0.6, R = 0.72, Draft = 0.003, DraftRange = 0.3, Wear = 1.25, Contact = 1.5, PassDiff = 1.15, HandlingW = 1.7, FuelRange = 14 };
        public static readonly TrackDef MillbrookLate = Millbrook.With("milll", t => { t.Late = true; t.Laps = 22; t.FuelRange = 13; });

        public static readonly TrackDef Crossroads = new TrackDef
        {
            Id = "cross", DraftBias = -0.5, BeatAt = new[] { 1 }, Name = "Crossroads Figure-8", Type = "Figure-8", Late = true, Fig8 = true, Laps = 21, LapTime = 18, R = 0.5, Draft = 0.005, DraftRange = 0.35, Wear = 1.1, Contact = 1.4, PassDiff = 1.1, HandlingW = 1.5, AiWreckK = 0.8, FuelRange = 12,
            Path = new[] { St(1.2), Tn(135, 0.5), St(2.697056), Tn(-135, 0.5), St(1.2), Tn(-135, 0.5), St(2.697056), Tn(135, 0.5) },
        };

        /// <summary>Step 41: a high-banked half-mile that eats tires. Two stops on fuel, and a long green run turns the race into a
        /// tire race: four fresh tires late are worth a lot, staying out is worth track position.</summary>
        public static readonly TrackDef GraniteFalls = new TrackDef { Id = "gran", DraftBias = -0.2, Name = "Granite Falls Speedway", Type = "Half-mile", Late = true, Laps = 26, LapTime = 17, S = 0.9, R = 0.8, Draft = 0.008, DraftRange = 0.45, Wear = 1.45, Contact = 1.25, PassDiff = 1.05, HandlingW = 1.4, FuelRange = 12 };

        /// <summary>Step 41: Ridgeback at late model length, so the late model calendar has a road course too (board Q35).</summary>
        public static readonly TrackDef RidgebackLate = Ridgeback.With("ridgel", t => { t.Late = true; t.Laps = 9; t.FuelRange = 6; });

        public static readonly IReadOnlyList<TrackDef> All = new[] { Millbrook, Carver, Talon, Pinewood, Ridgeback, PinewoodKart, HollowCreek, CedarBowl, MillbrookLate, Crossroads, BrambleHill, Lakeshore, GraniteFalls, RidgebackLate };

        public static TrackDef ById(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            throw new KeyNotFoundException("No track with id " + id);
        }
    }
}
