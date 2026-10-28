namespace GreenFlag.Sim
{
    /// <summary>
    /// Step 42: the premier stock series' track rules as the garage and deck builder explain them, and how much they favor a
    /// card or a setup. The rules themselves run in the race (Race.Rules.cs); these numbers only steer the deck builder's "fit"
    /// sort, its auto-build and the garage's tune chip.
    /// </summary>
    public static class TrackRules
    {
        /// <summary>What the rules on a kind of track do, for the garage and the deck builder. Null off the stock series.</summary>
        public static string Text(TrackKind k)
        {
            switch (k)
            {
                case TrackKind.Superspeedway: return "Superspeedway rules: the pack runs single file. Drafting cards read +6 with a car pushing you; passing cards read −10. Low drag is fast here.";
                case TrackKind.Intermediate: return "Intermediate rules: clean air and tire fall-off. Passing or drafting a car right ahead is dirty air (−10) unless your tires are 10 fresher (+6). Save the Tires, Hot Laps and the High Line read +8; worn tires cost more speed.";
                case TrackKind.ShortTrack: return "Short track rules: beating and banging. Bump and Run reads +10 and works from further back, Block +8; Dive Bomb −12 (he protects the bottom), drafting −8, and a loose setup costs passing cards 4 a notch.";
                case TrackKind.RoadCourse: return "Road course rules: braking zones. Passing cards read +8 into any corner; drafting reads −14 off the long straight, Bump and Run −8 outside a hairpin, Block −6. Downforce is fast here.";
                default: return null;
            }
        }

        /// <summary>How much the rules on a track favor a card, for the deck builder's score (about one rarity step at most).</summary>
        public static double CardFit(TrackKind k, CardDef cd)
        {
            var f = cd.Family; var id = cd.Id;
            switch (k)
            {
                case TrackKind.Superspeedway: return f == CardFamily.Draft ? 0.8 : f == CardFamily.Pass ? -0.8 : 0;
                case TrackKind.Intermediate: return id == "save_tires" || id == "hot_laps" || id == "high_line" ? 0.8 : f == CardFamily.Draft || id == "dive_bomb" || id == "bump_run" ? -0.5 : 0;
                case TrackKind.ShortTrack: return id == "bump_run" || id == "block" ? 0.8 : id == "dive_bomb" || f == CardFamily.Draft ? -0.8 : 0;
                case TrackKind.RoadCourse: return id == "dive_bomb" || id == "crossover" ? 0.8 : f == CardFamily.Draft ? -1 : id == "bump_run" || id == "block" ? -0.6 : 0;
                default: return 0;
            }
        }

        /// <summary>How much the rules on a track favor a setup, in the units of Career.TuneFit.</summary>
        public static double SetupFit(TrackKind k, Tune u)
        {
            switch (k)
            {
                case TrackKind.RoadCourse: return 0.2 * -u.Aero;
                case TrackKind.ShortTrack: return u.Hand > 0 ? -0.2 * u.Hand : 0;
                default: return 0;
            }
        }
    }
}
