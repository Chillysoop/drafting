namespace GreenFlag.Sim
{
    /// <summary>
    /// The sim's only source of randomness. Seeded per race and never touched by presentation code
    /// (visual noise uses its own generator), so changing an animation can never change a result.
    /// The algorithm is mulberry32, the same one the browser harness seeds Math.random with.
    /// </summary>
    public sealed class SimRng
    {
        uint state;

        public SimRng(uint seed) { state = seed; }

        /// <summary>The current state, so a race can be saved and resumed mid-way.</summary>
        public uint State { get => state; set => state = value; }

        public uint NextUInt()
        {
            unchecked
            {
                state += 0x6D2B79F5u;
                uint t = state;
                uint r = (t ^ (t >> 15)) * (1u | t);
                r ^= r + (r ^ (r >> 7)) * (61u | r);
                return r ^ (r >> 14);
            }
        }

        /// <summary>A double in [0, 1), bit for bit the value Math.random gives in the seeded slice.</summary>
        public double NextDouble() => NextUInt() / 4294967296.0;

        /// <summary>An int in [min, max).</summary>
        public int Range(int min, int max) => min + (int)(NextDouble() * (max - min));

        public bool Chance(double p) => NextDouble() < p;
    }
}
