namespace GreenFlag.Sim
{
    /// <summary>Fixed facts about the sim that the game, the tests and the harness all share.</summary>
    public static class SimInfo
    {
        public const string Version = "0.1.0";

        /// <summary>The sim always advances in fixed ticks. 0.05 s matches the browser harness step.</summary>
        public const int TickHz = 20;
        public const double TickSeconds = 1.0 / TickHz;
    }
}
