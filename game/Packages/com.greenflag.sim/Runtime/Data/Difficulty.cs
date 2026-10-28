namespace GreenFlag.Sim
{
    public enum DifficultyLevel { Easy, Normal, Hard, Brutal }

    /// <summary>The slice's DIFF table: how fast and pushy the field is, and how hard wrecks and tire wear hit.</summary>
    public sealed class Difficulty
    {
        public DifficultyLevel Level;
        public double Ai, Me, VsMe, Auto, Aggr, AiBlock, Purse, Wreck, Wear;

        public static Difficulty For(DifficultyLevel l)
        {
            switch (l)
            {
                case DifficultyLevel.Easy: return new Difficulty { Level = l, Ai = 0.994, Me = 1.005, VsMe = -12, Auto = 12, Aggr = 0.8, AiBlock = 0.05, Purse = 1.25, Wreck = 0.7, Wear = 0.85 };
                case DifficultyLevel.Hard: return new Difficulty { Level = l, Ai = 1.004, Me = 1, VsMe = 6, Auto = -5, Aggr = 1.15, AiBlock = 0.3, Purse = 0.85, Wreck = 1.2, Wear = 1.1 };
                case DifficultyLevel.Brutal: return new Difficulty { Level = l, Ai = 1.007, Me = 1, VsMe = 10, Auto = -8, Aggr = 1.3, AiBlock = 0.45, Purse = 0.7, Wreck = 1.4, Wear = 1.2 };
                default: return new Difficulty { Level = DifficultyLevel.Normal, Ai = 1, Me = 1, VsMe = 0, Auto = 0, Aggr = 1, AiBlock = 0.15, Purse = 1, Wreck = 1, Wear = 1 };
            }
        }
    }
}
