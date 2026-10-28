namespace GreenFlag.Sim
{
    /// <summary>
    /// The race-core dials from the slice's BAL "engine" preset (the default the slice ships with), plus the PIT, DZ, KART and
    /// LATE constants. Card, beat, combo and Focus dials join in step 27. One instance per race, so the harness can sweep them.
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

        public static SimBalance Engine() => new SimBalance();
    }
}
