namespace GreenFlag.Sim
{
    public enum RaceEventKind
    {
        GreenFlag, Caution, PitRoadOpen, OneToGo, Restart, Checkered, Finished,
        Pass, Block, Wreck, Dnf, OutOfFuel,
        PitIn, PitService, PitOut,
        LuckyDog, WaveAround, PitPenalty,
        HazardOffered, HazardDodged, HazardCollected,
        // cards (step 27): Card holds the card id
        CardPlayed, CardResolved, CardReturned, CardRerolled, CardTossed, CardsExpired, DrawBeat, Combo, BoardFull, Overdrive, ReelOffered,
        FairingPushed, FairingPenalty, Repassed, PartnerPush,
        PitGameOpen, PitGameGo, LooseWheel, PitGameRelease, PerfectStop, SetupInstalled, PackageComplete, SetupsStale,
    }

    /// <summary>
    /// Something that happened this tick. The sim never calls the UI; the HUD, the booth and the harness read these.
    /// Car and Other are race car ids (-1 = none). Text is a plain description for logs, not the booth's lines.
    /// </summary>
    public readonly struct RaceEvent
    {
        public readonly RaceEventKind Kind;
        public readonly double Time;
        public readonly int Car, Other;
        public readonly string Text;
        /// <summary>The card involved, for card events.</summary>
        public readonly string Card;

        public RaceEvent(RaceEventKind kind, double time, int car = -1, int other = -1, string text = null, string card = null)
        { Kind = kind; Time = time; Car = car; Other = other; Text = text; Card = card; }

        public override string ToString() => $"{Time,8:F2} {Kind}{(Car >= 0 ? " car " + Car : "")}{(Other >= 0 ? " / " + Other : "")}{(Text != null ? ": " + Text : "")}";
    }
}
