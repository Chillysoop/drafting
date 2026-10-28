using System;
using System.IO;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 41: the late model series (two new tracks, the eight-race calendar, the late model field's tire strategies,
    /// starting a career in late models, save format 6).</summary>
    public class LateSeriesTests
    {
        [Fact]
        public void TheLateCalendarIsMostlyShortOvalsPlusAFigure8AndARoadCourse()
        {
            var cal = CareerRules.Calendar(Series.Late).Select(Tracks.ById).ToList();
            Assert.Equal(8, cal.Count);
            Assert.All(cal, t => Assert.True(t.Late));
            Assert.Equal(6, cal.Count(t => !t.IsPath));
            Assert.Single(cal, t => t.Fig8);
            Assert.Single(cal, t => t.IsPath && !t.Fig8);
            Assert.Equal("cedar", cal[0].Id);
            Assert.Equal("gran", cal[cal.Count - 1].Id);
            Assert.Equal(Tracks.All.Where(t => t.Late).Select(t => t.Id).OrderBy(x => x), cal.Select(t => t.Id).Distinct().OrderBy(x => x));
            foreach (var t in cal) Assert.NotEmpty(CareerRules.TrackMin(t));
        }

        [Fact]
        public void NewTracksGoOnTheEndSoTheSliceIndexesStillMatch()
        {
            var ids = Tracks.All.Select(t => t.Id).ToList();
            Assert.Equal(new[] { "cedar", "milll", "cross" }, ids.Skip(7).Take(3));
            Assert.Equal(new[] { "gran", "ridgel" }, ids.Skip(12));
            Assert.Equal("Granite Falls", Pylon.ShortName(Tracks.GraniteFalls));
        }

        [Fact]
        public void GraniteFallsIsTheTireRace()
        {
            var late = Tracks.All.Where(t => t.Late).ToList();
            var g = Tracks.GraniteFalls;
            Assert.Equal("gran", late.OrderByDescending(t => t.Wear).First().Id);
            // two stops on fuel: one tank doesn't reach halfway
            Assert.True(g.Laps > 2 * g.FuelRange);
            Assert.All(late.Where(t => t != g), t => Assert.True(t.Laps <= 2 * t.FuelRange, t.Id + " needs two stops"));
            // Ridgeback at late model length is the same road course
            Assert.Same(Tracks.Ridgeback.Path, Tracks.RidgebackLate.Path);
            Assert.True(Tracks.RidgebackLate.Laps < Tracks.Ridgeback.Laps);
            Assert.Equal(Series.Late, Tracks.RidgebackLate.Series);
        }

        [Fact]
        public void TheLateFieldsStylesComeFromTraits()
        {
            Assert.Equal(Race.LateStyle.Saver, Race.LateStyleOf(Trait.Smooth));
            Assert.Equal(Race.LateStyle.Saver, Race.LateStyleOf(Trait.Veteran));
            Assert.Equal(Race.LateStyle.Charger, Race.LateStyleOf(Trait.Aggressive));
            Assert.Equal(Race.LateStyle.Charger, Race.LateStyleOf(Trait.Closer));
            Assert.Equal(Race.LateStyle.None, Race.LateStyleOf(Trait.Wall));
            Assert.Equal("Tire saver", FieldBuilder.LateStyleName(Trait.Smooth));
            Assert.Equal("Charger", FieldBuilder.LateStyleName(Trait.Aggressive));
        }

        [Fact]
        public void FresherTiresReadBetterOnAPassOnlyInLateModels()
        {
            var r = Scenario.Green("cedar");
            var a = r.AheadOf(r.Me);
            var b = r.Cars.First(c => !c.IsMe && c != a);
            b.Trait = Trait.Wall; a.Trait = Trait.Wall;
            b.Tires = 95; a.Tires = 60;
            Assert.Equal((95 - 60 - 15) * 0.4, r.LateRead(b, a), 6);
            b.Tires = 70;
            Assert.Equal(0, r.LateRead(b, a));
            b.Tires = 100; a.Tires = 20;
            Assert.Equal(12, r.LateRead(b, a));
            b.Trait = Trait.Aggressive;
            Assert.Equal(24, r.LateRead(b, a));
            // and against you: a car on fresh tires reads better passing you on old ones
            r.Me.Tires = 40; b.Trait = Trait.Wall;
            Assert.True(r.LateRead(b, r.Me) > 0);
            r.Bal.LateField = false;
            Assert.Equal(0, r.LateRead(b, a));
            var s = Scenario.Green("mill");
            Assert.Equal(0, s.LateRead(s.Cars.First(c => !c.IsMe), s.Me));
        }

        /// <summary>A caution early in a late model race, every AI car on 70% tires with fuel to spare: savers stay out, chargers take four.</summary>
        static Race CautionWith(string track, bool on)
        {
            var r = Scenario.Green(track);
            r.Bal.LateField = on;
            var pool = new[] { Trait.Smooth, Trait.Veteran, Trait.Aggressive, Trait.Closer, Trait.Wall };
            int i = 0;
            foreach (var c in r.Cars.Where(c => !c.IsMe)) { c.Trait = pool[i++ % pool.Length]; c.Tires = 70; }
            r.StartCaution("test");
            return r;
        }

        [Fact]
        public void SaversStayOutAndChargersTakeFourUnderCaution()
        {
            var r = CautionWith("cedar", true);
            foreach (var kv in r.Caution.Plans)
            {
                var st = Race.LateStyleOf(kv.Key.Trait);
                if (st == Race.LateStyle.Saver) Assert.Equal(PitService.Stay, kv.Value);
                if (st == Race.LateStyle.Charger) Assert.Equal(PitService.Four, kv.Value);
            }
            Assert.Equal(r.Caution.Plans.Count, r.AiCalls.Sum());
            // off, or on a stock track, the slice's calls: on 70% tires nobody is forced either way
            foreach (var x in new[] { CautionWith("cedar", false), CautionWith("mill", true) })
                Assert.Contains(x.Caution.Plans, kv => Race.LateStyleOf(kv.Key.Trait) == Race.LateStyle.Charger && kv.Value != PitService.Four);
        }

        [Fact]
        public void AStockPersonaCanStartInLateModels()
        {
            var c = Career.New("whisperer", seed: 3, start: Series.Late);
            Assert.Equal(Series.Late, c.Series);
            Assert.Equal(new[] { Series.Late }, c.Ladder);
            Assert.Equal(CareerRules.Calendar(Series.Late), c.Calendar);
            Assert.Equal(Teams.For(Series.Late).Length * 2, c.Field.Count);
            Assert.Throws<ArgumentException>(() => Career.New("brawler", start: Series.Late));
            Assert.Throws<ArgumentException>(() => Career.New("whisperer", start: Series.Kart));
            Assert.Equal(Series.Stock, Career.New("whisperer").Series);
        }

        [Fact]
        public void TheTitleScreenOffersLateModelsToStockPersonas()
        {
            var m = new CareerMenu { PickPersona = "whisperer", NewSeed = 5 };
            Assert.Contains(Items(m.Page()), i => i.Id == "title.series:Late");
            Assert.True(m.Press("title.series:Late"));
            m.Press("title.persona:brawler");
            Assert.DoesNotContain(Items(m.Page()), i => i.Id == "title.series:Late");
            m.Press("title.persona:whisperer");
            Assert.True(m.Press("title.start"));
            Assert.Equal(Series.Late, m.Career.Series);
            // the garage explains the new pit and tire layer until the first late model race is run
            Assert.Contains(Items(m.Page()), i => i.Label != null && i.Label.StartsWith("New in late models"));
            m = MenuBot.Run(m, 1, QuickRace);
            Assert.Equal(CareerScreen.Garage, m.Screen);
            Assert.DoesNotContain(Items(m.Page()), i => i.Label != null && i.Label.StartsWith("New in late models"));
        }

        [Fact]
        public void ALateSeasonRunsTheWholeCalendarThroughTheMenus()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "gambler", PickSeries = Series.Late, NewSeed = 9 }, 8, QuickRace);
            var k = m.Career;
            Assert.Equal(CareerRules.Calendar(Series.Late), k.Results.Where(r => r.Season == 1).Select(r => r.TrackId));
            Assert.Single(k.Past);
            Assert.All(k.Results.Where(r => r.Season == 1), r => Assert.True(r.Order.Count > 0));
        }

        static System.Collections.Generic.IEnumerable<MenuItem> Items(MenuPage p) => p.Sections.SelectMany(x => x.Items).Concat(p.Actions);

        static string SavesDir => Path.Combine(AppContext.BaseDirectory, "Saves");

        [Fact]
        public void ALateSeasonUnderWayKeepsItsOldTracksAfterTheUpdate()
        {
            var g = CareerSave.Read(File.ReadAllText(Path.Combine(SavesDir, "v5-linepainter-promoted-late-models.json")));
            Assert.Equal(5, g.LoadedFrom);
            Assert.Equal(Series.Late, g.Career.Series);
            Assert.True(g.Career.RaceIdx > 0);
            Assert.Equal(Career.LateCalendarV5, g.Career.Calendar);
            // a late model season that hasn't started gets the new calendar; a v4 late season under way keeps the old one
            var fresh = JsonValue.Obj().Set("series", "Late").Set("season", 2).Set("raceIdx", 0).Set("results", JsonValue.Arr()).Set("calendar", JsonValue.Of(Career.LateCalendarV5));
            Assert.Equal(CareerRules.Calendar(Series.Late), Career.MigrateV5(fresh)["calendar"].Strings());
            var v4 = JsonValue.Obj().Set("series", "Late").Set("season", 1).Set("raceIdx", 3).Set("results", JsonValue.Arr()).Set("field", JsonValue.Arr());
            Assert.Equal(Career.LateCalendarV5, Career.MigrateV5(Career.MigrateV4(v4))["calendar"].Strings());
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
