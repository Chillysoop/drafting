namespace GreenFlag.Sim
{
    /// <summary>
    /// The dials from the slice's BAL "engine" preset (the default the slice ships with), plus the PIT, DZ, KART, LATE and pit
    /// mini-game (PG) constants. One instance per race, so the harness can sweep them.
    /// </summary>
    public sealed class SimBalance
    {
        // BAL (engine preset)
        public double AiWreck = 0.2;
        public double TireGrip = 0.08;
        public bool Hazard = true;
        public double HazardWin = 2.5;
        public double HazardCatch = 0.6;
        public double Pressure = 0.6;
        public double PressureCap = 18;
        public double WreckDmg = 1.3;
        public double AiStop = 0.9;
        public bool TrackWreck = true;
        public bool ChiefSmart = true;
        public int PitAuto = 3;

        // PIT
        public double PitLimit = 0.42;
        public double PitPace = 0.55;
        public double TireTwo = 6.5, TireFour = 11.5;
        public double FuelT = 6, FuelBase = 1.5;
        public double CautionBurn = 0.35;
        public double OpenAfter = 0.3;

        // DZ (damage zones)
        public double DzPaceNose = 0.12, DzPaceSide = 0.05, DzPaceTail = 0.06;
        public double DzDraft = 0.012, DzDraftMin = 0.5, DzWear = 0.012;

        // KART and LATE: how much car and driver stats move pace, as a share of a stock car
        public double KartSpec = 0.35;
        public double LateSpec = 0.6;
        public double LateCrossMe = 0.45, LateCrossAi = 0.004, LateCrossCd = 14, LateCrossWin = 0.6;

        // BAL: cards, beats, combos and Focus (only used when the race has a card layer, RaceSetup.Loadout)
        /// <summary>Seconds of slow-mo in a full Focus bar, and how much of the bar each draw beat refills.</summary>
        public double FocusSec = 8, FocusRegen = 0.5;
        /// <summary>Race speed while a card is selected and Focus lasts.</summary>
        public double Slowmo = 0.1;
        /// <summary>Crew chief warns you at Heat 3.</summary>
        public bool Overdrive = true;
        /// <summary>Unplayed green-flag cards go back to the deck after this many beats.</summary>
        public int Fleeting = 2;
        /// <summary>Share of a card's tire cost you pay, and how much of it a success refunds (1 = all).</summary>
        public double TireCost = 0.5, Refund = 1;
        /// <summary>Timed draws (only after a caution hands its restart cards back; draws are beats otherwise).</summary>
        public double DrawEvery = 30;
        /// <summary>Combos: cards drawn after a landed card (once per beat), weight of linked cards in that draw.</summary>
        public int ChainDraw = 1, FollowMin = 1, FollowCap = 1;
        public double LinkW = 3;
        public double ComboWin = 8, ComboBonus = 6, ComboCap = 12;
        /// <summary>A card can't combo off itself.</summary>
        public bool ComboNew = true;
        /// <summary>Crew Board: charges to fill, and cards drawn when it fills.</summary>
        public int Board = 3, BoardDraw = 1;
        public bool TossFx = true, RiskFocus = true, Reel = true;
        public double OddsAdj = -5, CfLine = 14, OddsCap = 0.9;
        /// <summary>The crew chief waves you out this long after the release window closes.</summary>
        public double ChiefLate = 5;
        /// <summary>Your stops are the pit mini-game (off = the step 26 timed stop).</summary>
        public bool PitGame = true;

        // PG (pit mini-game)
        public double PgPlanSec = 12, PgAdjT = 4, PgFixT = 4, PgEarly = 1, PgLooseK = 0.6, PgLooseT = 3, PgStaleK = 1;
        public int PgHand = 5, PgPrepMax = 3, PgMaxAdj = 3;

        public static SimBalance Engine() => new SimBalance();
    }
}
