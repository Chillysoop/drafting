using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    public class RaceTests
    {
        public static IEnumerable<object[]> AllTracks() => Tracks.All.Select(t => new object[] { t.Id });

        static Race Run(string track, uint seed)
        {
            var r = new Race(RaceSetup.Quick(Tracks.ById(track), seed)) { Log = new List<RaceEvent>() };
            r.RunToEnd();
            return r;
        }

        [Theory]
        [MemberData(nameof(AllTracks))]
        public void SameSeedSameRace(string track)
        {
            foreach (uint seed in new uint[] { 61, 2026 })
            {
                var a = Run(track, seed);
                var b = Run(track, seed);
                Assert.Equal(a.Fingerprint(), b.Fingerprint());
                Assert.Equal(a.Result().Order.Select(x => x.Id), b.Result().Order.Select(x => x.Id));
                Assert.Equal(a.Log.Select(e => e.ToString()), b.Log.Select(e => e.ToString()));
            }
        }

        [Fact]
        public void LockstepRunsMatchEveryTick()
        {
            var setup = RaceSetup.Quick(Tracks.Talon, 7);
            var a = new Race(setup);
            var b = new Race(RaceSetup.Quick(Tracks.Talon, 7));
            while (!a.Done)
            {
                a.Tick(); b.Tick();
                Assert.Equal(a.Phase, b.Phase);
                for (int i = 0; i < a.Order.Count; i++)
                {
                    Assert.Equal(a.Order[i].Id, b.Order[i].Id);
                    Assert.Equal(a.Order[i].Dist, b.Order[i].Dist);
                }
            }
            Assert.True(b.Done);
        }

        [Fact]
        public void DifferentSeedsGiveDifferentRaces()
        {
            var prints = new HashSet<string>();
            for (uint s = 1; s <= 8; s++) prints.Add(Run("carver", s).Fingerprint());
            Assert.Equal(8, prints.Count);
        }

        // Golden seeds: the exact race these setups produce today. A change that moves them is a sim change: if it was on
        // purpose, run `dotnet run --project sim/GreenFlag.Harness -- race <track> <seed>` and paste the new fingerprint.
        [Theory]
        [InlineData("carver", 61u, "54b9624ae5932056")] // step 42: intermediate tire fall-off (Bal.StockRules) moved it
        [InlineData("mill", 62u, "f59b5524e24a079e")]
        [InlineData("hollow", 63u, "0f56cd0a7c9947e5")] // step 40: the kart field (Bal.KartField) moved it
        [InlineData("cross", 64u, "2df1d989010d6652")]
        public void GoldenSeeds(string track, uint seed, string fingerprint)
        {
            Assert.Equal(fingerprint, Run(track, seed).Fingerprint());
        }

        [Theory]
        [MemberData(nameof(AllTracks))]
        public void EveryRaceFinishesWithTheWholeField(string track)
        {
            var t = Tracks.ById(track);
            var r = Run(track, 99);
            var res = r.Result();
            Assert.True(r.Done);
            Assert.Equal(20, res.Order.Count);
            Assert.Equal(Enumerable.Range(0, 20), res.Order.Select(x => x.Id).OrderBy(x => x));
            Assert.True(r.FinalOrder[0].Dist >= t.Laps);
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.GreenFlag);
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.Checkered);
            // DNFs run at the back
            bool seenDnf = false;
            foreach (var c in res.Order) { if (c.Dnf) seenDnf = true; else Assert.False(seenDnf); }
        }

        [Theory]
        [InlineData("pinek")]
        [InlineData("hollow")]
        public void KartsHaveNoCautionsAndNoPitStops(string track)
        {
            for (uint s = 1; s <= 5; s++)
            {
                var r = Run(track, s);
                Assert.Equal(0, r.Cautions);
                Assert.DoesNotContain(r.Log, e => e.Kind == RaceEventKind.PitIn);
            }
        }

        [Fact]
        public void EveryoneStopsForFuelWhenATankIsShort()
        {
            // Carver: 16 laps on a 10-lap tank
            for (uint s = 1; s <= 5; s++)
            {
                var r = Run("carver", s);
                foreach (var c in r.Cars) if (!c.Dnf) Assert.True(c.PitStops >= 1, $"seed {s}: {c} never pitted");
            }
        }

        [Fact]
        public void CautionsRunInOrder()
        {
            var seen = 0;
            for (uint s = 1; s <= 12 && seen < 3; s++)
            {
                var r = Run("mill", s);
                var flags = r.Log.Where(e => e.Kind == RaceEventKind.Caution || e.Kind == RaceEventKind.PitRoadOpen || e.Kind == RaceEventKind.OneToGo || e.Kind == RaceEventKind.Restart || e.Kind == RaceEventKind.GreenFlag).Select(e => e.Kind).ToList();
                Assert.Equal(RaceEventKind.GreenFlag, flags[0]);
                for (int i = 1; i < flags.Count; i++)
                {
                    if (flags[i] != RaceEventKind.Caution) continue;
                    seen++;
                    // a caution can be the last thing before the flag; otherwise the full sequence follows
                    var rest = flags.Skip(i + 1).Take(4).ToList();
                    if (rest.Count == 4) Assert.Equal(new[] { RaceEventKind.PitRoadOpen, RaceEventKind.OneToGo, RaceEventKind.Restart, RaceEventKind.GreenFlag }, rest);
                }
            }
            Assert.True(seen > 0, "no caution in 12 races at Millbrook");
        }

        [Fact]
        public void HandsOffFinishesNearExpected()
        {
            // a broad sanity band, not balance: hands-off in the slice finishes about where the car's rating says
            double fin = 0, exp = 0, cautions = 0;
            const int n = 40;
            for (uint s = 0; s < n; s++)
            {
                var res = Run("carver", 500 + s).Result();
                fin += res.MyFinish; exp += res.MyExpected; cautions += res.Cautions;
            }
            Assert.InRange(fin / n - exp / n, -3, 3);
            Assert.InRange(cautions / n, 0.3, 3.5);
        }

        [Fact]
        public void PitButtonOnThePlanLapStopsOnce()
        {
            var r = new Race(RaceSetup.Quick(Tracks.Carver, 5)) { Log = new List<RaceEvent>() };
            while (!r.Done)
            {
                r.Tick();
                var pl = r.MyPlan;
                if (r.Phase == RacePhase.Green && r.Me.Pit == null && r.PitRequest == null && pl != null && r.LapOf(r.Me) >= pl.Value.Lap) r.PressPit();
            }
            Assert.True(r.Me.PitStops >= 1);
            Assert.DoesNotContain(r.Log, e => e.Kind == RaceEventKind.OutOfFuel && e.Car == r.Me.Id);
        }
    }

    public class FieldAndTrackTests
    {
        [Fact]
        public void FieldHasTwentyCarsAndYou()
        {
            var f = FieldBuilder.Build(new SimRng(3), Series.Stock, CarStats.Rookie());
            Assert.Equal(20, f.Count);
            Assert.Single(f, e => e.IsMe);
            Assert.Equal(77, f.Single(e => e.IsMe).Number);
            Assert.Equal(20, f.Select(e => e.Number).Distinct().Count());
            Assert.All(f.Where(e => !e.IsMe), e => Assert.InRange(e.Stats.Power, 1, 10));
        }

        [Theory]
        [MemberData(nameof(RaceTests.AllTracks), MemberType = typeof(RaceTests))]
        public void PitRoadSitsAroundTheLine(string track)
        {
            var s = TrackShape.Of(Tracks.ById(track));
            Assert.True(s.Pit.E0 < s.Pit.E1 && s.Pit.E1 < 0 && 0 < s.Pit.X0 && s.Pit.X0 < s.Pit.X1);
            Assert.True(s.MinGap > 0 && s.MinGap < 0.05);
            for (double d = -1; d < 2; d += 0.013) Assert.InRange(s.UOf(d), 0, 1);
        }

        [Fact]
        public void RoadCourseShapesAreRead()
        {
            var pine = TrackShape.Of(Tracks.Pinewood);
            Assert.Equal(2, pine.Segments.Count(x => x.Kind == TurnKind.Hairpin));
            Assert.Contains(pine.Segments, x => x.Kind == TurnKind.Esses);
            var ridge = TrackShape.Of(Tracks.Ridgeback);
            Assert.Contains(ridge.Segments, x => x.Kind == TurnKind.Sweeper && x.Name == "Ridge Sweeper");
            // across a lap a car sees straights, approaches and turns
            var kinds = new HashSet<string>();
            for (double d = 0; d < 1; d += 0.005) { var p = pine.SpotOf(d); kinds.Add(p.Straight ? "s" : p.Approach ? "a" : "t"); }
            Assert.Equal(3, kinds.Count);
        }
    }
}
