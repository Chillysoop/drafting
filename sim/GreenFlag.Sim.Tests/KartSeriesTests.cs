using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 40: the kart series (two new circuits, the four-circuit calendar, the kart field's styles, save format 5).</summary>
    public class KartSeriesTests
    {
        static readonly string[] KartIds = { "pinek", "hollow", "bramble", "lake" };

        /// <summary>A path track's pieces end where they start, pointing the same way.</summary>
        [Theory]
        [InlineData("bramble")]
        [InlineData("lake")]
        [InlineData("pinek")]
        [InlineData("hollow")]
        public void KartCircuitsCloseOnThemselves(string id)
        {
            double x = 0, y = 0, h = 0;
            foreach (var p in Tracks.ById(id).Path)
            {
                if (p.IsStraight) { x += Math.Cos(h) * p.S; y += Math.Sin(h) * p.S; continue; }
                double a = p.A * Math.PI / 180, s = Math.Sign(a);
                double cx = x - s * p.R * Math.Sin(h), cy = y + s * p.R * Math.Cos(h);
                h += a;
                x = cx + s * p.R * Math.Sin(h); y = cy - s * p.R * Math.Cos(h);
            }
            Assert.Equal(0, x, 3);
            Assert.Equal(0, y, 3);
            Assert.Equal(2 * Math.PI, h, 6);
        }

        static List<TrackShape.Segment> Turns(string id) => TrackShape.Of(Tracks.ById(id)).Segments.Where(s => s.IsTurn).ToList();

        [Fact]
        public void TheNewCircuitsHaveTheirOwnShapes()
        {
            // Bramble Hill: two hairpins, a chicane and esses, no straight long enough for the tow
            var b = TrackShape.Of(Tracks.BrambleHill);
            Assert.Equal(2, Turns("bramble").Count(s => s.Kind == TurnKind.Hairpin));
            Assert.True(Turns("bramble").Count(s => s.Kind == TurnKind.Esses) >= 4);
            Assert.DoesNotContain(b.Segments, s => !s.IsTurn && s.Len >= 1.8);
            Assert.Equal(new[] { "Bramble Hairpin", "Thicket Hairpin" }, Turns("bramble").Where(s => s.Kind == TurnKind.Hairpin).Select(s => s.Name));
            // Lakeshore: long straights, sweepers and one hairpin at the end of the back straight
            var l = TrackShape.Of(Tracks.Lakeshore);
            Assert.Equal(2, l.Segments.Count(s => !s.IsTurn && s.Len >= 1.8));
            Assert.Single(Turns("lake"), s => s.Kind == TurnKind.Hairpin);
            Assert.True(Turns("lake").Count(s => s.Kind == TurnKind.Sweeper) >= 4);
            Assert.Equal("Boathouse Hairpin", Turns("lake").Single(s => s.Kind == TurnKind.Hairpin).Name);
            // the tow: Lakeshore pulls the most of any kart circuit, Bramble Hill the least
            var kart = Tracks.All.Where(t => t.Kart).ToList();
            Assert.Equal("lake", kart.OrderByDescending(t => t.Draft).First().Id);
            Assert.Equal("bramble", kart.OrderBy(t => t.Draft).First().Id);
            // every beat is a braking zone into a real turn
            foreach (var t in new[] { Tracks.BrambleHill, Tracks.Lakeshore })
                foreach (int i in t.BeatAt) Assert.False(t.Path[i].IsStraight);
        }

        [Fact]
        public void TheKartCalendarVisitsEveryKartCircuit()
        {
            var cal = CareerRules.Calendar(Series.Kart);
            Assert.Equal(6, cal.Count);
            Assert.Equal("pinek", cal[0]);
            Assert.Equal("lake", cal[cal.Count - 1]);
            Assert.Equal(KartIds.OrderBy(x => x), cal.Distinct().OrderBy(x => x));
            Assert.All(cal, id => Assert.True(Tracks.ById(id).Kart));
            foreach (var id in KartIds) Assert.NotEmpty(CareerRules.TrackMin(Tracks.ById(id)));
        }

        [Fact]
        public void NewTracksGoOnTheEndSoTheSliceIndexesStillMatch()
        {
            var ids = Tracks.All.Select(t => t.Id).ToList();
            Assert.Equal(new[] { "mill", "carver", "talon", "pine", "ridge", "pinek", "hollow", "cedar", "milll", "cross" }, ids.Take(10));
            Assert.Equal(new[] { "bramble", "lake" }, ids.Skip(10).Take(2));
            Assert.Equal("Bramble Hill", Pylon.ShortName(Tracks.BrambleHill));
            Assert.Equal("Lakeshore", Pylon.ShortName(Tracks.Lakeshore));
        }

        [Fact]
        public void KartFieldsHaveRookiesInsteadOfRestartersWithTheSameRolls()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                var k = FieldBuilder.Build(new SimRng(seed), Series.Kart, CarStats.Rookie());
                var s = FieldBuilder.Build(new SimRng(seed), Series.Stock, CarStats.Rookie());
                Assert.DoesNotContain(k, e => e.Trait == Trait.Restarter);
                for (int i = 0; i < k.Count; i++)
                {
                    Assert.Equal(s[i].Name, k[i].Name);
                    Assert.Equal(s[i].Trait == Trait.Restarter ? Trait.Rookie : s[i].Trait, k[i].Trait);
                }
            }
            Assert.Equal("Train rat", FieldBuilder.KartStyle(Trait.Drafter));
        }

        /// <summary>Kart races on many seeds, every AI kart a Bumper, you playing a card every 4 s: the field's bumps and
        /// covers, and what they do.</summary>
        static (int bumps, int covers, int fairing, List<Race> races) Field(bool on, string track = "bramble", int n = 12)
        {
            int b = 0, c = 0, f = 0;
            var races = new List<Race>();
            for (uint s = 1; s <= n; s++)
            {
                var r = Scenario.Make(track, seed: s);
                r.Bal.KartField = on;
                foreach (var car in r.Cars) if (!car.IsMe) car.Trait = Trait.Aggressive;
                bool covered = false;
                double next = 4;
                while (!r.Done)
                {
                    int was = r.Log.Count;
                    r.Tick();
                    // play a card every 4 s (as QuickRace does), so you pass karts and have some behind you
                    if (r.T >= next && r.Attempt == null && r.Hand.Count > 0)
                    {
                        next = r.T + 4;
                        foreach (var h in r.Hand.ToList()) if (r.PlayCard(h.Uid) == null) break;
                    }
                    for (int i = was; i < r.Log.Count; i++)
                        if (r.Log[i].Kind == RaceEventKind.KartCover)
                        {
                            var car = r.Cars.First(x => x.Id == r.Log[i].Car);
                            Assert.True(r.Covering(car));
                            Assert.Equal(r.Me.Id, r.Log[i].Other);
                            covered = true;
                        }
                }
                b += r.KartBumps; c += r.KartCovers; f += r.Fairing;
                // every bump on you pushes your fairing in (up to the most it can take)
                Assert.True(r.Fairing >= Math.Min(r.KartBumps, r.FairingMax));
                if (covered || r.KartBumps > 0) races.Add(r);
            }
            return (b, c, f, races);
        }

        [Fact]
        public void TheKartFieldBumpsAndCovers()
        {
            var on = Field(true);
            Assert.True(on.bumps > 0, "no bumps in 12 races");
            Assert.True(on.covers > 0, "no covers in 12 races");
            Assert.True(on.fairing > 0);
            var off = Field(false);
            Assert.Equal(0, off.bumps);
            Assert.Equal(0, off.covers);
            // only Bumpers bump you
            foreach (var r in on.races)
                foreach (var e in r.Log.Where(e => e.Kind == RaceEventKind.KartBump))
                    Assert.Equal(Trait.Aggressive, r.Cars.First(x => x.Id == e.Car).Trait);
        }

        [Fact]
        public void TheKartFieldStaysOffStockAndLateModelTracks()
        {
            foreach (var t in new[] { "ridge", "cross" })
            {
                var r = Scenario.Make(t, seed: 3);
                while (!r.Done) r.Tick();
                Assert.Equal(0, r.KartBumps + r.KartCovers);
                Assert.DoesNotContain(r.Log, e => e.Kind == RaceEventKind.KartBump || e.Kind == RaceEventKind.KartCover);
            }
        }

        [Fact]
        public void SwitchbackReadsTheCoverTheFieldPutsUp()
        {
            var r = Scenario.Green("bramble");
            var a = r.AheadOf(r.Me);
            var cd = CardBook.Get("k_switch");
            double before = r.ComputeOdds(cd, r.Ctx()).Read;
            a.CoverUntil = r.T + 6;
            Assert.Equal(before + 20, r.ComputeOdds(cd, r.Ctx()).Read, 6);
        }

        static string SavesDir => Path.Combine(AppContext.BaseDirectory, "Saves");

        [Fact]
        public void AKartSeasonUnderWayKeepsItsOldTracksAfterTheUpdate()
        {
            var text = File.ReadAllText(Path.Combine(SavesDir, "v4-brawler-kart-reward.json"));
            var g = CareerSave.Read(text);
            var c = g.Career;
            Assert.Equal(4, g.LoadedFrom);
            Assert.Equal(Series.Kart, c.Series);
            Assert.Equal(Career.KartCalendarV4, c.Calendar);
            Assert.DoesNotContain(c.Field, e => e.Trait == Trait.Restarter);
            Assert.Contains("\"calendar\"", CareerSave.Write(g));
            // the rest of the season runs on the old tracks, and the next season on the new calendar
            var store = new MemorySaveStore();
            store.Files[SaveSlots.FileName(1)] = text;
            var m = new CareerMenu(new SaveSlots(store) { Now = () => new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), Build = "test" });
            Assert.True(m.Load(1), m.Toast);
            int had = m.Career.Results.Count;
            m = MenuBot.Run(m, m.Career.SeasonLength - m.Career.SeasonResults.Count + 1, QuickRace);
            var k = m.Career;
            Assert.Equal(Career.KartCalendarV4.Skip(had), k.Results.Where(r => r.Season == 1).Skip(had).Select(r => r.TrackId));
            Assert.Equal(2, k.Season);
            Assert.Equal(CareerRules.Calendar(k.Series), k.Calendar);
            Assert.Equal(CareerRules.Calendar(k.Series)[0], k.Results.Last().TrackId);
        }

        [Fact]
        public void AStockSaveGetsItsSeriesCalendar()
        {
            var g = CareerSave.Read(File.ReadAllText(Path.Combine(SavesDir, "v4-drafter-garage.json")));
            Assert.Equal(CareerRules.Calendar(Series.Stock), g.Career.Calendar);
        }

        [Fact]
        public void AKartSeasonRunsAllFourCircuitsThroughTheMenus()
        {
            var m = new CareerMenu { PickPersona = "linepainter", NewSeed = 7 };
            m = MenuBot.Run(m, 6, QuickRace);
            var k = m.Career;
            var s1 = k.Results.Where(r => r.Season == 1).ToList();
            Assert.Equal(CareerRules.Calendar(Series.Kart), s1.Select(r => r.TrackId));
            Assert.Single(k.Past);
            Assert.True(k.Past[0].Rank >= 1);
        }

        /// <summary>Two save bugs from step 39 that the step 40 reload runs found: the "+1 Crew chief" contract clause only
        /// bumped the loadout (a reload or the next crew sync dropped it), and parts went into the race in the order they were
        /// fitted in memory but in slot order after a reload.</summary>
        [Fact]
        public void ACrewChiefClauseAndFittedPartsSurviveAReload()
        {
            var c = Career.New("linepainter", seed: 4);
            int was = c.Staff[CrewRole.CrewChief].Lvl;
            c.ApplyFx(new OffFx { Clause = "crewChief" }, null);
            Assert.Equal(was + 1, c.Staff[CrewRole.CrewChief].Lvl);
            Assert.Equal(was + 1, c.Base.CrewChief);
            c.SyncCrew();
            Assert.Equal(was + 1, c.Base.CrewChief);
            // fit parts in reverse slot order: the race still gets them in slot order
            var two = Parts.All.Where(p => p.Kart).GroupBy(p => p.Slot).Select(g => g.First()).OrderByDescending(p => p.Slot).Take(2).ToList();
            foreach (var p in two) { if (!c.PartsOwned.Contains(p.Id)) c.PartsOwned.Add(p.Id); c.Equipped.Remove(p.Slot); }
            foreach (var p in two) c.Equip(p.Id);
            var order = c.Loadout().Parts;
            var g = CareerSave.Read(CareerSave.Write(new SaveGame { Career = c, Screen = SaveGame.Garage }));
            Assert.Equal(order, g.Career.Loadout().Parts);
            Assert.Equal(was + 1, g.Career.Base.CrewChief);
        }

        /// <summary>Board Q89 (Conner, Oct 28): a near-perfect qualifying run in the first kart (team 3/10) reaches the top 8,
        /// while a skipped run still starts near the back.</summary>
        [Fact]
        public void APerfectQualifyingRunInTheFirstKartStartsInTheTopEight()
        {
            double perfect = 0, skip = 0;
            for (uint s = 1; s <= 40; s++)
            {
                var c = Career.New("linepainter", seed: s);
                c.EnterGarage();
                var run = c.StartQuali(null);
                foreach (var ch in run.Plan.Checks) ch.Res = QualiMark.Perfect;
                perfect += c.FinishQuali(run).Pos;
                skip += c.FinishQuali(null).Pos;
            }
            perfect /= 40; skip /= 40;
            Assert.True(perfect <= 8, $"perfect run starts P{perfect:F1}");
            Assert.True(skip > 12, $"skip starts P{skip:F1}");
        }

        static Race QuickRace(RaceSetup s)
        {
            s.PitPolicy = PitPolicy.Bot;
            var r = new Race(s);
            double next = 4;
            for (int i = 0; i < 200000 && !r.Done; i++)
            {
                r.Tick();
                if (r.T >= next && r.Attempt == null && r.Hand.Count > 0)
                {
                    next = r.T + 4;
                    foreach (var h in r.Hand.ToList()) if (r.PlayCard(h.Uid) == null) break;
                }
            }
            Assert.True(r.Done);
            return r;
        }
    }
}
