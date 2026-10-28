using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 43: rival arcs and silly season. The class of twelve, their seats in the field, the Grudge on track and
    /// after a race, the inbox and its d20 replies, silly season, the ladder (a kart rival turns up as your stock car teammate
    /// or rival), save format 8.</summary>
    public class RivalTests
    {
        static Race HandsOff(RaceSetup s)
        {
            var r = new Race(s);
            for (int i = 0; i < 400000 && !r.Done; i++) r.Tick();
            Assert.True(r.Done);
            return r;
        }

        /// <summary>A career race to the flag without the HUD (as SeasonTests): pit bot, a card every 4 s.</summary>
        static Race Quick(RaceSetup s)
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

        /// <summary>The field is a legal one: 20 cars, one of them you, unique ids and numbers, two cars a team, every class
        /// seat matching its driver's series and team, and at most one class driver beside you.</summary>
        static void FieldOk(Career c)
        {
            Assert.Equal(20, c.Field.Count);
            Assert.Single(c.Field, e => e.IsMe);
            Assert.Equal(20, c.Field.Select(e => e.Id).Distinct().Count());
            Assert.Equal(20, c.Field.Select(e => e.Number).Distinct().Count());
            Assert.All(c.Field.GroupBy(e => e.Team), g => Assert.Equal(2, g.Count()));
            foreach (var e in c.Field.Where(x => x.Rival != null))
            {
                var d = c.ClassOf(e);
                Assert.False(d.Retired);
                Assert.Equal(c.Series, d.Series);
                Assert.Equal(d.Team, e.Team);
                Assert.Equal(d.Def.Name, e.Name);
                Assert.Equal(d.Def.Number, e.Number);
            }
            Assert.All(c.Class.Where(d => !d.Retired && d.Series == c.Series), d => Assert.NotNull(c.SeatOf(d)));
            Assert.True(c.Field.Count(e => e.Team == c.Team && e.Rival != null) <= 1);
        }

        /// <summary>Ends the season with the given finishing order on points (ids first, then everyone else as they stand).</summary>
        static void EndSeason(Career c, params int[] first)
        {
            var order = first.Concat(c.Field.Select(e => e.Id).Where(id => !first.Contains(id))).ToList();
            c.Pts.Clear();
            for (int i = 0; i < order.Count; i++) c.Pts[order[i]] = 400 - i * 10;
            c.WrapSeason();
        }

        static int Promote(Career c) => c.Offers.FindIndex(o => o.Kind == OfferKind.Promote);

        [Fact]
        public void TheClassIsTwelveDriversWithTheSlicesSixAmongThem()
        {
            Assert.Equal(12, RivalBook.All.Count);
            Assert.Equal(12, RivalBook.All.Select(d => d.Number).Distinct().Count());
            Assert.DoesNotContain(RivalBook.All, d => d.Number == 77);
            Assert.Equal(5, RivalBook.All.Count(d => d.Gen == 0));
            Assert.Equal(4, RivalBook.All.Count(d => d.Gen == 1));
            Assert.Equal(3, RivalBook.All.Count(d => d.Gen == 2));
            // the slice's rivals keep their numbers
            var pro = RivalBook.Get("pro"); Assert.Equal((0.6, 2.0, 0.5, 0.8, 4.0), (pro.Mul, pro.Cool, pro.Pay, pro.Block, pro.Settle));
            var g = RivalBook.Get("grudge"); Assert.Equal((1.1, 0.25, 1.0, 1.0, 1.0), (g.Mul, g.Cool, g.Pay, g.Block, g.Settle)); Assert.True(g.Proud);
            Assert.Equal(1.6, RivalBook.Get("hothead").Mul);
            Assert.Equal(2.0, RivalBook.Get("wall").Block);
            Assert.True(RivalBook.Get("talker").Press);
            Assert.Equal(new[] { "Respect", "Cool", "Warm", "Hot", "Feud" }, new[] { -2, 2.75, 3, 6, 9 }.Select(RivalBook.Tier));
            Assert.Equal(0, RivalBook.PaybackChance(RivalBook.Get("enforcer"), 5.75));
            Assert.Equal(0.6, RivalBook.PaybackChance(RivalBook.Get("enforcer"), 9), 6);
            Assert.Equal(1, RivalBook.BlockK(RivalBook.Get("wall"), 0));
            foreach (var k in RivalBook.Kinds) foreach (var d in RivalBook.All) Assert.False(string.IsNullOrEmpty(RivalBook.Message(k, d.Id).body));
        }

        [Theory]
        [InlineData("brawler", Series.Kart, 5)]
        [InlineData("drafter", Series.Stock, 6)]
        public void ANewCareerSeatsItsSeriesClassDrivers(string persona, Series s, int inField)
        {
            var c = Career.New(persona, seed: 7);
            Assert.Equal(s, c.Series);
            Assert.Equal(12, c.Class.Count);
            Assert.Equal(inField, c.InField.Count());
            FieldOk(c);
            // your generation starts with you in karts; a stock car rookie finds them in late models
            Assert.All(c.Class.Where(d => d.Def.Gen == 0), d => Assert.Equal(s == Series.Kart ? Series.Kart : Series.Late, d.Series));
            Assert.All(c.Class.Where(d => d.Def.Gen == 2), d => Assert.Equal(Series.Stock, d.Series));
            Assert.Equal(Series.Late, c.ClassById("journey").Series);
            Assert.True(Teams.For(c.ClassById("heir").Series)[c.ClassById("heir").Team].Rating >= 7);
            Assert.All(c.Class, d => Assert.Equal(0, d.G));
            c.EnterGarage();
            c.SignSponsor(c.SponsorOffers[0].Id);
            // this week's rival is a class driver
            Assert.NotNull(c.ClassOf(c.Field.First(e => e.Id == c.RivalId)));
        }

        [Fact]
        public void TheClassNeverMovesTheCareersOwnRolls()
        {
            var a = Career.New("brawler", seed: 9);
            var b = Career.New("brawler", seed: 9);
            b.ClassOff();
            Assert.Equal(a.Rng.State, b.Rng.State);
            Assert.Empty(b.Class);
            Assert.DoesNotContain(b.Field, e => e.Rival != null);
            FieldOk(b);
        }

        [Fact]
        public void AFieldWithNoGrudgeRacesExactlyAsBefore()
        {
            foreach (uint seed in new uint[] { 61, 62 })
            {
                var plain = HandsOff(RaceSetup.Quick(Tracks.ById("carver"), seed));
                var s = RaceSetup.Quick(Tracks.ById("carver"), seed);
                foreach (var e in s.Field.Where(x => !x.IsMe).Take(6)) e.Rival = RivalBook.All[e.Id % 12].Id;
                var tagged = HandsOff(s);
                Assert.Equal(plain.Result().Order.Select(o => o.Id), tagged.Result().Order.Select(o => o.Id));
                Assert.Equal(plain.T, tagged.T);
                Assert.Equal(0, tagged.Paybacks);
            }
        }

        [Fact]
        public void AFeudingRivalOnYourBumperPaysYouBackOnceAndSettles()
        {
            int paid = 0;
            for (uint seed = 61; seed < 71; seed++)
            {
                var s = RaceSetup.Quick(Tracks.ById("mill"), seed);
                foreach (var e in s.Field.Where(x => !x.IsMe)) { e.Rival = "enforcer"; e.Grudge = 9.5; }
                var r = HandsOff(s);
                Assert.True(r.Paybacks <= s.Field.Count - 1);
                foreach (var g in r.RivalInc.Where(x => x.Kind == "payback").GroupBy(x => x.Car)) Assert.Single(g);
                foreach (var x in r.RivalInc.Where(x => x.Kind == "payback"))
                {
                    var car = r.Cars.First(c => c.Id == x.Car);
                    Assert.True(r.PaidBack(car));
                    Assert.Equal(9.5 - 5, r.GrudgeOf(car), 6);
                    Assert.Contains(r.RivalWhy[car.Id], w => w.why == "payback");
                }
                paid += r.Paybacks;
            }
            Assert.True(paid > 0, "no payback in ten races with a whole field at Feud");
        }

        [Fact]
        public void BeingPaidBackHurtsSoAFeudingFieldCostsYouPlaces()
        {
            double plain = 0, feud = 0;
            for (uint seed = 61; seed < 69; seed++)
            {
                plain += HandsOff(RaceSetup.Quick(Tracks.ById("mill"), seed)).Result().MyFinish;
                var s = RaceSetup.Quick(Tracks.ById("mill"), seed);
                foreach (var e in s.Field.Where(x => !x.IsMe)) { e.Rival = "hothead"; e.Grudge = 10; }
                feud += HandsOff(s).Result().MyFinish;
            }
            Assert.True(feud > plain, $"feud {feud / 8:F1} vs plain {plain / 8:F1}");
        }

        [Fact]
        public void RepliesAreD20ChecksThatMoveTheGrudgeOnce()
        {
            var c = Career.New("drafter", seed: 5);
            var d = c.ClassById("hothead");
            d.G = 4;
            c.PostMsg(d, "wrecked", "Millbrook Speedway", 12, 3);
            var m = c.Inbox[0];
            Assert.Contains("DUMPED", m.Body);
            Assert.Equal(1, c.Unanswered);
            var opt = RivalBook.RepliesFor("wrecked")[0];
            var b = c.ReplyCheck(m, opt);
            Assert.Equal(DSkill.Cool, b.Skill);
            Assert.Equal(12, b.BaseDc);
            var copy = c.Clone();
            Assert.Null(c.Reply(m.Id, opt.Id));
            Assert.NotNull(m.Tier);
            var fx = c.ReplyFx(m, opt, m.Tier.Value);
            Assert.Equal(RivalBook.Q(4 + (fx.g > 0 ? fx.g * 1.6 : fx.g)), d.G);
            Assert.Equal("Already answered", c.Reply(m.Id, opt.Id));
            Assert.Equal(0, c.Unanswered);
            // the same reply rolls the same die
            Assert.Null(copy.Reply(m.Id, opt.Id));
            Assert.Equal(m.Roll, copy.Inbox[0].Roll);
            // an apology to The Grudge is harder
            var g = c.ClassById("grudge");
            c.PostMsg(g, "wrecked", "Ironside", 3, 4);
            Assert.Equal(15, c.ReplyCheck(c.Inbox[0], opt).BaseDc);
            // a press darling's fan swings double
            var t = c.ClassById("talker");
            c.PostMsg(t, "gloat", "Ironside", 0, 4);
            var trash = RivalBook.RepliesFor("gloat").First(o => o.Id == "trash");
            Assert.Equal(12, c.ReplyFx(c.Inbox[0], trash, CheckTier.Success).fx.Fans);
            for (int i = 0; i < 20; i++) c.PostMsg(d, "beat", "Ironside", 0, 9);
            Assert.Equal(RivalBook.InboxMax, c.Inbox.Count);
        }

        [Fact]
        public void TheRivalObjectiveFollowsTheGrudge()
        {
            var c = Career.New("drafter", seed: 3);
            c.EnterGarage();
            c.SignSponsor(c.SponsorOffers[0].Id);
            var wk = c.ClassOf(c.Field.First(e => e.Id == c.RivalId));
            wk.G = 7;
            c.RollObjectives();
            for (int i = 0; i < 40 && (c.Objectives.All(o => !SeasonRules.Objective(o.Id).Rival)); i++) c.RollObjectives();
            var o = c.Objectives.First(x => SeasonRules.Objective(x.Id).Rival);
            Assert.Equal("rsurv", o.Id);
            Assert.Contains(c.ClassOf(c.Field.First(e => e.Id == c.RivalId)).Def.Name, c.ObjectiveText(o));
            Assert.Equal(c.RivalId, c.Field.Where(e => e.Rival != null).OrderByDescending(e => c.GrudgeOf(e.Rival)).First().Id);
            Assert.False(SeasonRules.Objective("rclean").Drawn);
            var res = new RaceReport { BeatRival = true, RvWreck = true };
            Assert.False(SeasonRules.Objective("rclean").Test(res, false));
            res.RvWreck = false;
            Assert.True(SeasonRules.Objective("rclean").Test(res, false));
        }

        [Fact]
        public void ARaceMovesTheGrudgeAndThePaidBackRivalWrites()
        {
            var c = Career.New("bully", seed: 21);
            c.EnterGarage();
            c.SignSponsor(c.SponsorOffers[0].Id);
            foreach (var d in c.InField) d.G = 9.5;
            var r = Quick(c.NextRace());
            var res = c.FinishRace(r);
            Assert.NotEmpty(res.Rivals);
            foreach (var l in res.Rivals)
            {
                var d = c.ClassById(l.Rid);
                Assert.Equal(d.G, l.G1);
                Assert.Equal(c.Season, d.Met);
            }
            // between races a Grudge cools
            Assert.Contains(res.Rivals, l => l.Why.Any(w => w.why == "cooled off"));
            Assert.All(c.InField, d => Assert.InRange(d.Ahead + d.Behind, 0, 1));
            if (r.Paybacks > 0) { Assert.Contains(c.Inbox, m => m.Kind == "payback"); Assert.Equal(r.Paybacks, res.Paybacks); }
            Assert.InRange(res.InboxNew, 0, 2);
        }

        [Fact]
        public void SillySeasonReshufflesTheFieldAndTheClassStepsUp()
        {
            var c = Career.New("brawler", seed: 4);
            var nina = c.ClassById("prodigy");
            var ninaCar = c.SeatOf(nina);
            int me = c.MeEntrant.Id;
            var names = c.Field.Select(e => e.Name).ToList();
            EndSeason(c, ninaCar.Id, me);
            Assert.Equal(Series.Late, nina.Series);
            Assert.Null(c.SeatOf(nina));
            Assert.Contains(c.News, n => n.StartsWith("Nina Valdez (P1 in karts) steps up to late models"));
            Assert.Contains(c.News, n => n.Contains("Rookie"));
            Assert.True(c.Field.Count(e => !names.Contains(e.Name)) >= 3, "at least three new faces");
            Assert.Equal(1, nina.Titles);
            Assert.Equal(2, nina.Path.Count);
            Assert.StartsWith("Signed for season 2", nina.Path[1]);
            FieldOk(c);
            // you step up too, and she is in your late model field
            Assert.True(c.SignContract(Promote(c)));
            Assert.Equal(Series.Late, c.Series);
            FieldOk(c);
            Assert.NotNull(c.SeatOf(nina));
            nina.Met = 1;
        }

        /// <summary>The roadmap's "done when": a kart rival can show up as your stock car teammate or rival. Three seasons up the
        /// ladder with the prodigy, then you sign with her stock car team.</summary>
        [Fact]
        public void AKartRivalShowsUpAsYourStockCarTeammateOrRival()
        {
            var c = Career.New("packrat", seed: 8);
            var nina = c.ClassById("prodigy");
            Assert.Equal(Series.Kart, nina.Series);
            nina.Met = 1;
            EndSeason(c, c.MeEntrant.Id, c.SeatOf(nina).Id);
            Assert.True(c.SignContract(Promote(c)));
            Assert.Equal(Series.Late, c.Series);
            Assert.NotNull(c.SeatOf(nina));
            Assert.Contains(c.Inbox, m => m.Rid == "prodigy" && (m.Kind == "arrive" || m.Kind == "mate"));
            EndSeason(c, c.MeEntrant.Id, c.SeatOf(nina).Id);
            Assert.Equal(Series.Stock, nina.Series);
            Assert.True(c.SignContract(Promote(c)));
            Assert.Equal(Series.Stock, c.Series);
            FieldOk(c);
            var car = c.SeatOf(nina);
            Assert.NotNull(car);
            // her stock car team calls: sign there and she is your teammate
            c.EnterGarage();
            EndSeason(c, c.MeEntrant.Id);
            var o = c.Offers.FindIndex(x => x.Kind == OfferKind.Stay);
            c.Offers[o] = SeasonRules.Offer(OfferKind.Move, Series.Stock, nina.Team, 1);
            Assert.True(c.SignContract(o));
            FieldOk(c);
            Assert.Equal("prodigy", c.Mate.Rival);
            Assert.Contains(c.Inbox, m => m.Rid == "prodigy" && m.Kind == "mate");
            // or, with a Grudge, she is this week's rival
            nina.G = 4;
            c.EnterGarage();
            if (c.Sponsor == null) c.SignSponsor(c.SponsorOffers[0].Id);
            c.RollObjectives();
            Assert.Equal(c.SeatOf(nina).Id, c.RivalId);
            var m = new CareerMenu(c);
            Assert.Contains(m.Page().Sections, x => x.Title == "Rivals");
            Assert.True(m.Press("rivals"));
            Assert.Equal(CareerScreen.Rivals, m.Screen);
            Assert.True(m.Press("rv.who:prodigy"));
            Assert.Contains(m.Page().Sections.SelectMany(x => x.Items), i => i.Label != null && i.Label.StartsWith("Season 1: Karts"));
        }

        [Fact]
        public void VeteransRetireAndTheJourneymanNeverLeavesLateModels()
        {
            var c = Career.New("drafter", seed: 2);
            for (int s = 0; s < 7; s++)
            {
                c.EnterGarage();
                EndSeason(c);
                Assert.True(c.SignContract(0));
                FieldOk(c);
                Assert.Equal(Series.Late, c.ClassById("journey").Series);
            }
            var pro = c.ClassById("pro");
            Assert.True(pro.Retired);
            Assert.Equal(6, pro.Seasons);
            Assert.Null(c.SeatOf(pro));
            Assert.Contains(c.Past, p => p.Season == 6);
        }

        [Fact]
        public void TheClassSavesAndAnOldSaveBuildsOne()
        {
            var c = Career.New("linepainter", seed: 6);
            var d = c.ClassById("hothead"); d.G = 5.25; d.Hist.Add(new RivalHist { Season = 1, Race = 2, Lap = 9, Kind = "wreck", Track = "Bramble Hill" });
            c.PostMsg(d, "wrecked", "Bramble Hill", 9, 4);
            Assert.Null(c.Reply(c.Inbox[0].Id, "fire"));
            c.PostMsg(d, "beat", "Lakeshore", 0, 6);
            EndSeason(c, c.MeEntrant.Id);
            var j = c.ToJson().Write();
            var back = Career.FromJson(JsonValue.Parse(j));
            Assert.Equal(j, back.ToJson().Write());
            Assert.Equal(c.News, back.News);
            Assert.Equal(c.Inbox.Count, back.Inbox.Count);
            Assert.Equal(d.G, back.ClassById("hothead").G);
            Assert.Equal(c.ClassRng.State, back.ClassRng.State);
            // a save from step 42 (format 7) has no class: it gets one on load, seated in its field
            var root = JsonValue.Parse(j);
            root.Set("class", JsonValue.Null);
            foreach (var e in root["field"].Items) e.Set("rv", JsonValue.Null);
            var old = Career.FromJson(root);
            Assert.Equal(12, old.Class.Count);
            Assert.Empty(old.Inbox);
            Assert.All(old.Class, x => Assert.Equal(0, x.G));
            Assert.Equal(1, old.Class.Max(x => x.Seasons));
            FieldOk(old);
        }
    }
}
