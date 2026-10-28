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
        // presentation only (step 30): an AI move that came to nothing, so the view can show it (Other = the car it tried)
        AutoLift, AutoAlongside,
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
        /// <summary>How a card landed (CardResolved: a Tier as int), otherwise -1.</summary>
        public readonly int Tier;

        public RaceEvent(RaceEventKind kind, double time, int car = -1, int other = -1, string text = null, string card = null, int tier = -1)
        { Kind = kind; Time = time; Car = car; Other = other; Text = text; Card = card; Tier = tier; }

        public override string ToString() => $"{Time,8:F2} {Kind}{(Car >= 0 ? " car " + Car : "")}{(Other >= 0 ? " / " + Other : "")}{(Text != null ? ": " + Text : "")}";
    }
}
