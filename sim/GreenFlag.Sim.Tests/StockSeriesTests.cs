using System;
using System.IO;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 42: the premier stock series (the four kinds of stock car track and their rules, three new tracks, the
    /// ten-race premier calendar, save format 7).</summary>
    public class StockSeriesTests
    {
        [Fact]
        public void ThePremierCalendarHasEveryKindOfTrackAndNoKartPark()
        {
            var cal = CareerRules.Calendar(Series.Stock).Select(Tracks.ById).ToList();
            Assert.Equal(10, cal.Count);
            Assert.All(cal, t => Assert.Equal(Series.Stock, t.Series));
            Assert.Equal(2, cal.Count(t => t.Kind == TrackKind.Superspeedway));
            Assert.Equal(3, cal.Count(t => t.Kind == TrackKind.Intermediate));
            Assert.Equal(3, cal.Count(t => t.Kind == TrackKind.ShortTrack));
            Assert.Equal(2, cal.Count(t => t.Kind == TrackKind.RoadCourse));   // ovals plus 2 or 3 road courses (Q35)
            Assert.DoesNotContain(cal, t => t.Id == "pine");                    // Q90: Pinewood is off the stock calendar
            Assert.Equal("talon", cal[0].Id);
            Assert.Equal("harbor", cal[cal.Count - 1].Id);
            foreach (var t in cal) Assert.NotEmpty(CareerRules.TrackMin(t));
        }

        [Fact]
        public void EveryStockTrackHasAKindAndOtherSeriesHaveNone()
        {
            Assert.Equal(TrackKind.Superspeedway, Tracks.Talon.Kind);
            Assert.Equal(TrackKind.Intermediate, Tracks.Carver.Kind);
            Assert.Equal(TrackKind.Intermediate, Tracks.HarborPoint.Kind);
            Assert.Equal(TrackKind.ShortTrack, Tracks.Millbrook.Kind);
            Assert.Equal(TrackKind.ShortTrack, Tracks.Ironside.Kind);
            Assert.Equal(TrackKind.RoadCourse, Tracks.Ridgeback.Kind);
            Assert.Equal(TrackKind.RoadCourse, Tracks.CopperCanyon.Kind);
            Assert.Equal(TrackKind.RoadCourse, Tracks.Pinewood.Kind);
            Assert.All(Tracks.All.Where(t => t.Series != Series.Stock), t => Assert.Equal(TrackKind.None, t.Kind));
            foreach (var k in new[] { TrackKind.Superspeedway, TrackKind.Intermediate, TrackKind.ShortTrack, TrackKind.RoadCourse })
                Assert.StartsWith(TrackDef.KindName(k) + " rules", TrackRules.Text(k));
            Assert.Null(TrackRules.Text(TrackKind.None));
        }

        [Fact]
        public void NewTracksGoOnTheEnd()
        {
            var ids = Tracks.All.Select(t => t.Id).ToList();
            Assert.Equal(new[] { "harbor", "iron", "copper" }, ids.Skip(14));
            Assert.Equal("Harbor Point", Pylon.ShortName(Tracks.HarborPoint));
        }

        static double Rules(Race r, string card)
        {
            var o = r.ComputeOdds(CardBook.Get(card), r.Ctx());
            return o.Mods.TryGetValue("rules", out var v) ? v : 0;
        }

        [Fact]
        public void EachKindOfTrackMovesTheReadsItsOwnWay()
        {
            var ss = Scenario.Green("talon");
            Assert.NotNull(ss.AheadOf(ss.Me));
            Assert.Equal(-10, Rules(ss, "dive_bomb"));
            Assert.True(Rules(ss, "tuck_in") >= 0);

            var st = Scenario.Green("mill");
            Assert.Equal(10, Rules(st, "bump_run"));
            Assert.Equal(-12, Rules(st, "dive_bomb"));
            Assert.Equal(-8, Rules(st, "tuck_in"));
            Scenario.CarBehind(st);
            Assert.Equal(8, Rules(st, "block"));

            var it = Scenario.Green("harbor");
            Assert.Equal(8, Rules(it, "save_tires"));
            var gap = it.GapSec(it.AheadOf(it.Me), it.Me);
            Assert.Equal(gap < 0.5 ? -10 : 0, Rules(it, "dive_bomb"));

            var rc = Scenario.Green("ridge");
            Scenario.CarBehind(rc);
            Assert.Equal(-6, Rules(rc, "block"));
            var seg = rc.Ctx().Seg;
            Assert.Equal(seg.Straight && seg.Long ? 0 : -14, Rules(rc, "tuck_in"));
        }

        [Fact]
        public void TheRulesAreOffWithTheSwitchAndOffTheStockSeries()
        {
            var r = Scenario.Green("mill");
            r.Bal.StockRules = false;
            foreach (var id in new[] { "bump_run", "dive_bomb", "tuck_in", "save_tires" }) Assert.Equal(0, Rules(r, id));
            Assert.Equal(0, Rules(Scenario.Green("milll"), "bump_run"));   // Millbrook at late model length runs the slice's reads
        }

        [Fact]
        public void ALooseSetupCostsPassingCardsOnAShortTrack()
        {
            var l = Scenario.Plain(false); l.Tune = new Tune { Hand = 2 };
            var r = Scenario.Green("iron", l);
            Assert.Equal(-12 - 8, Rules(r, "dive_bomb"));
            Assert.Equal(0, Rules(r, "save_tires"));
        }

        [Fact]
        public void TiresFallOffHarderOnAnIntermediateAndDownforceIsFastOnARoadCourse()
        {
            var r = Scenario.Green("carver");
            var me = r.Me;
            me.Tires = 50;
            double on = r.PaceMult(me);
            r.Bal.StockRules = false;
            double off = r.PaceMult(me);
            Assert.Equal(1 - 0.5 * r.Bal.IntFalloff, on / off, 9);

            var down = Scenario.Plain(false); down.Tune = new Tune { Aero = -2 };
            var drag = Scenario.Plain(false); drag.Tune = new Tune { Aero = 2 };
            var a = Scenario.Green("copper", down); var b = Scenario.Green("copper", drag);
            double ra = a.PaceMult(a.Me) / a.Mods.Pace, rb = b.PaceMult(b.Me) / b.Mods.Pace;
            a.Bal.StockRules = false; b.Bal.StockRules = false;
            Assert.Equal(1 + 2 * a.Bal.RoadAero, ra / (a.PaceMult(a.Me) / a.Mods.Pace), 9);
            Assert.Equal(1 - 2 * b.Bal.RoadAero, rb / (b.PaceMult(b.Me) / b.Mods.Pace), 9);
        }

        [Fact]
        public void TheDeckBuilderAndTuneChipFollowTheRules()
        {
            var c = Career.New("drafter");
            Assert.True(DeckRules.Score(c, "tuck_in", Tracks.Talon) > DeckRules.Score(c, "tuck_in", Tracks.Ridgeback));
            Assert.True(DeckRules.Score(c, "dive_bomb", Tracks.CopperCanyon) > DeckRules.Score(c, "dive_bomb", Tracks.Ironside));
            Assert.True(DeckRules.Score(c, "save_tires", Tracks.HarborPoint) > DeckRules.Score(c, "save_tires", Tracks.Talon));
            Assert.True(Career.TuneFit(Tracks.CopperCanyon, new Tune { Aero = -2 }).verdict > Career.TuneFit(Tracks.CopperCanyon, new Tune { Aero = 2 }).verdict);
        }

        [Fact]
        public void TheGarageAndDeckBuilderExplainTheRules()
        {
            var m = new CareerMenu { PickPersona = "bully", NewSeed = 3 };
            m.Press("title.start");
            Assert.Equal(CareerScreen.Garage, m.Screen);
            Assert.Contains(Items(m.Page()), i => i.Label != null && i.Label.StartsWith("Superspeedway rules"));
            m.Press("deck");
            Assert.Equal(CareerScreen.DeckBuilder, m.Screen);
            Assert.Contains(Items(m.Page()), i => i.Label != null && i.Label.StartsWith("Superspeedway rules"));
        }

        [Fact]
        public void APremierSeasonRunsTheWholeCalendarThroughTheMenus()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "restart", NewSeed = 9 }, 10, QuickRace);
            var k = m.Career;
            Assert.Equal(CareerRules.Calendar(Series.Stock), k.Results.Where(r => r.Season == 1).Select(r => r.TrackId));
            Assert.Single(k.Past);
        }

        static System.Collections.Generic.IEnumerable<MenuItem> Items(MenuPage p) => p.Sections.SelectMany(x => x.Items).Concat(p.Actions);

        static string SavesDir => Path.Combine(AppContext.BaseDirectory, "Saves");

        [Fact]
        public void AStockSeasonUnderWayKeepsItsOldTracksAfterTheUpdate()
        {
            var g = CareerSave.Read(File.ReadAllText(Path.Combine(SavesDir, "v6-drafter-garage.json")));
            Assert.Equal(6, g.LoadedFrom);
            Assert.Equal(Series.Stock, g.Career.Series);
            Assert.True(g.Career.RaceIdx > 0);
            Assert.Equal(Career.StockCalendarV6, g.Career.Calendar);
            // a stock season that hasn't started gets the new calendar; a v4 stock season under way keeps the old one
            var fresh = JsonValue.Obj().Set("series", "Stock").Set("season", 2).Set("raceIdx", 0).Set("results", JsonValue.Arr()).Set("calendar", JsonValue.Of(Career.StockCalendarV6));
            Assert.Equal(CareerRules.Calendar(Series.Stock), Career.MigrateV6(fresh)["calendar"].Strings());
            var v4 = JsonValue.Obj().Set("series", "Stock").Set("season", 1).Set("raceIdx", 3).Set("results", JsonValue.Arr()).Set("field", JsonValue.Arr());
            Assert.Equal(Career.StockCalendarV6, Career.MigrateV6(Career.MigrateV5(Career.MigrateV4(v4)))["calendar"].Strings());
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
