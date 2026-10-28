using GreenFlag.Sim;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    public class SimRngTests
    {
        // First five rolls of the browser harness's seeded Math.random (mulberry32), from node.
        [Theory]
        [InlineData(61u, new[] { 0.54817091953009367, 0.70829046075232327, 0.88076829095371068, 0.73137547192163765, 0.04388630925677717 })]
        [InlineData(0u, new[] { 0.26642920868471265, 0.00032974570058286, 0.22327202744781971, 0.14620214793831110, 0.46732782293111086 })]
        [InlineData(4294967295u, new[] { 0.89642261411063373, 0.18947825673967600, 0.71565267816185951, 0.94405990932136774, 0.84523643157444894 })]
        public void MatchesTheSlicesSeededRandom(uint seed, double[] expected)
        {
            var rng = new SimRng(seed);
            foreach (double e in expected) Assert.Equal(e, rng.NextDouble(), 15);
        }

        [Fact]
        public void SameSeedSameStream()
        {
            var a = new SimRng(12345);
            var b = new SimRng(12345);
            for (int i = 0; i < 10_000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void StateRoundTripsForSaves()
        {
            var a = new SimRng(7);
            for (int i = 0; i < 100; i++) a.NextUInt();
            var b = new SimRng(0) { State = a.State };
            Assert.Equal(a.NextDouble(), b.NextDouble());
        }

        [Fact]
        public void RangeStaysInBounds()
        {
            var rng = new SimRng(99);
            for (int i = 0; i < 10_000; i++)
            {
                int v = rng.Range(-3, 4);
                Assert.InRange(v, -3, 3);
                Assert.InRange(rng.NextDouble(), 0.0, 0.9999999999);
            }
        }

        [Fact]
        public void TickIsTheHarnessStep()
        {
            Assert.Equal(0.05, SimInfo.TickSeconds, 12);
        }
    }
}
