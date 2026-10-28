using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 37: the season framework. Points, standings, sponsors, objectives, salary, qualifying, the season wrap,
    /// contract offers and promotion up the ladder, against slice v47's rules.</summary>
    public class SeasonTests
    {
        /// <summary>A race to the flag without the HUD (as CareerTests): pit bot, a card every 4 s.</summary>
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

        /// <summary>A career in the garage with a sponsor signed and qualifying skipped, ready to race.</summary>
        static Career Ready(string persona = "drafter", uint seed = 3)
        {
            var c = Career.New(persona, seed: seed);
            c.EnterGarage();
            Assert.True(c.SignSponsor(c.SponsorOffers[0].Id));
            return c;
        }

        [Fact]
        public void Points_follow_the_slice()
        {
            Assert.Equal(40, SeasonRules.Points(0));
            Assert.Equal(35, SeasonRules.Points(1));
            Assert.Equal(34, SeasonRules.Points(2));
            Assert.Equal(17, SeasonRules.Points(19));
            Assert.Equal(1, SeasonRules.Points(60));
        }

        [Fact]
        public void A_new_career_has_a_season_field_at_the_smallest_team()
        {
            foreach (var p in new[] { "drafter", "brawler" })
            {
                var c = Career.New(p, seed: 9);
                Assert.Equal(20, c.Field.Count);
                Assert.Single(c.Field, e => e.IsMe);
                Assert.Same(c.Base.Car, c.MeEntrant.Stats);
                Assert.Equal(Teams.PlayerTeam, c.Team);
                Assert.Equal(0, c.Contract.Per);
                Assert.Equal(new[] { c.Series }, c.Ladder);
                Assert.Equal(20, c.Field.Select(e => e.Number).Distinct().Count());
                Assert.Equal(2, c.Field.Count(e => e.Team == Teams.PlayerTeam));
            }
        }

        [Fact]
        public void Go_racing_waits_for_a_sponsor_and_signing_rolls_the_objectives()
        {
            var c = Career.New("drafter", seed: 4);
            c.EnterGarage();
            Assert.NotNull(c.CantRace);
            Assert.Contains("sponsor", c.CantRace);
            Assert.Equal(3, c.SponsorOffers.Count);
            // a first season: only the sponsors anyone can get, at stock car pay (40%)
            foreach (var o in c.SponsorOffers)
            {
                var sp = SeasonRules.Sponsor(o.Id);
                Assert.Equal(99, sp.Need);
                Assert.Equal(SeasonRules.R100(sp.Per * 0.4), o.Per);
            }
            var id = c.SponsorOffers[1].Id;
            Assert.True(c.SignSponsor(id));
            Assert.Null(c.CantRace);
            Assert.Null(c.SponsorOffers);
            Assert.False(c.SignSponsor(id));
            Assert.Equal(3, c.Objectives.Count);
            var ask = c.Objectives.Single(o => o.Ask);
            Assert.Contains(ask.Id, SeasonRules.Sponsor(id).Asks);
            Assert.Equal(3, c.Objectives.Select(o => o.Id).Distinct().Count());
            Assert.True(c.RivalId >= 0 && !c.Field.First(e => e.Id == c.RivalId).IsMe);
        }

        [Fact]
        public void Sponsor_offers_open_up_after_a_strong_season()
        {
            var rng = new SimRng(1);
            var top = SeasonRules.RollSponsorOffers(rng, 5, null, 1);
            Assert.Equal("keystone", top[0].Id);   // needs a top-8 season, sorted first
            Assert.Equal("redmesa", top[1].Id);    // needs top 12
            var mid = SeasonRules.RollSponsorOffers(rng, 10, null, 1);
            Assert.Equal("redmesa", mid[0].Id);
            Assert.DoesNotContain(mid, o => o.Id == "keystone");
            Assert.DoesNotContain(SeasonRules.RollSponsorOffers(rng, 99, "pinecrest", 1), o => o.Id == "pinecrest" || o.Id == "redmesa" || o.Id == "keystone");
        }

        [Theory]
        [InlineData("top15", 10, false, 7, 12, 99, true)]
        [InlineData("top15", 10, false, 8, 12, 99, false)]
        [InlineData("top15", 10, true, 1, 12, 99, false)]
        [InlineData("gain5", 10, false, 7, 12, 99, true)]
        [InlineData("gain5", 10, false, 8, 12, 99, false)]
        [InlineData("top10", 12, false, 15, 15, 4, true)]
        [InlineData("top10", 12, false, 15, 15, 5, false)]
        [InlineData("top10", 4, false, 15, 15, 3, true)]
        public void Objectives_scale_with_the_expected_finish(string id, int objE, bool dnf, int finish, int start, int bestPos, bool ok)
        {
            var r = new RaceReport { ObjE = objE, Expected = objE, Dnf = dnf, Finish = finish, Start = start, BestPos = bestPos };
            Assert.Equal(ok, SeasonRules.Objective(id).Test(r, false));
        }

        [Fact]
        public void Season_goals_scale_with_the_calendar_and_report_progress()
        {
            var beat = SeasonRules.Goal("beat6");
            Assert.Equal(6, beat.NeedFor(10));
            Assert.Equal(4, beat.NeedFor(6));
            Assert.Equal(5, beat.NeedFor(8));
            Assert.Equal(30, SeasonRules.Goal("gain50").NeedFor(6));
            var R = new List<RaceReport> { new RaceReport { Finish = 3, Start = 10, Expected = 9 }, new RaceReport { Finish = 20, Start = 18, Expected = 15, Dnf = true } };
            Assert.Equal(("1 of 4", GoalState.Open), SeasonRules.GoalStatus("beat6", R, 5, 4, 6));
            Assert.Equal(("1 of 2 allowed", GoalState.On), SeasonRules.GoalStatus("dnf2", R, 5, 4, 6));
            Assert.Equal(("+5 of +30", GoalState.Missed), SeasonRules.GoalStatus("gain50", R, 5, 0, 6));
            Assert.Equal(GoalState.Behind, SeasonRules.GoalStatus("pts5", R, 6, 3, 6).st);
            Assert.Equal(GoalState.Met, SeasonRules.GoalStatus("pts10", R, 6, 0, 6).st);
            Assert.Equal(GoalState.Missed, SeasonRules.GoalStatus("win1", R, 1, 0, 6).st);
        }

        [Fact]
        public void A_race_pays_objectives_sponsor_and_salary_and_scores_points()
        {
            var c = Ready();
            c.Contract.Per = 700;
            var objs = c.Objectives.ToList();
            int bank = c.Money;
            var race = Quick(c.NextRace());
            var res = c.FinishRace(race);
            Assert.Equal(objs.Count, res.Objectives.Count);
            Assert.Equal(c.Sponsor.Per, res.Retainer);
            Assert.Equal(700, res.Salary);
            int objPay = res.Objectives.Where(o => o.Ok).Sum(o => o.Pay);
            Assert.Equal(res.Purse + res.Bonus + res.Retainer + res.Salary + objPay, res.Total);
            Assert.Equal(bank + res.Total, c.Money);
            Assert.Equal(SeasonRules.Points(res.Finish - 1), res.Pts);
            Assert.Equal(res.Pts, res.PtsTotal);
            // every car scored, the table adds up, the rank matches it
            Assert.Equal(Enumerable.Range(0, 20).Sum(SeasonRules.Points), c.Pts.Values.Sum());
            var st = c.Standings();
            Assert.Equal(res.Rank, st.FindIndex(r => r.IsMe) + 1);
            Assert.True(st.Zip(st.Skip(1), (a, b) => a.Pts >= b.Pts).All(x => x));
            Assert.Equal(1, c.Field.Count(e => e.Wins == 1));
            Assert.All(c.Field, e => Assert.Equal(1, e.Starts));
            Assert.Null(c.Objectives);
            c.PickCard(c.Reward.Cards[0]);
            c.Continue();
            Assert.NotNull(c.Objectives);   // rolled for the next race in the garage
        }

        [Fact]
        public void Meeting_every_objective_moves_the_rewards_up_a_tier()
        {
            string dbg = "";
            for (uint seed = 1; seed < 40; seed++)
            {
                var c = Ready("drafter", seed);
                // two asks a hands-off race can meet (no cards, so no critical failures; the teammate is a coin flip)
                c.Objectives = new List<RaceObjective> { new RaceObjective { Id = "clean" }, new RaceObjective { Id = "mate" } };
                var s = c.NextRace();
                s.PitPolicy = PitPolicy.Bot;
                var race = new Race(s);
                for (int i = 0; i < 200000 && !race.Done; i++) race.Tick();
                var res = c.FinishRace(race);
                dbg += $"{seed}:P{res.Finish} obj{res.ObjMet}/{res.Objectives.Count} dnf{res.Dnf}; ";
                if (res.ObjMet != res.Objectives.Count || res.Dnf) { Assert.Null(res.TierFrom); continue; }
                if (res.TierFrom == null) { Assert.Equal(RewardTier.Victory, res.Tier); continue; }
                Assert.Equal(res.TierFrom.Value + 1, res.Tier);
                Assert.Equal(CareerRules.TierFor(res.Finish, res.Expected, res.Dnf), res.TierFrom.Value);
                return;
            }
            Assert.True(false, "no seed met every objective " + dbg);
        }

        [Fact]
        public void The_field_stays_for_the_season_and_grows()
        {
            var c = Ready();
            var names = c.Field.Select(e => e.Name).ToList();
            int sum0 = c.Field.Where(e => !e.IsMe).Sum(e => e.Stats.Power + e.Stats.Handling + e.Stats.Racecraft + e.Stats.Defense);
            for (int i = 0; i < 3; i++)
            {
                var s = c.NextRace();
                Assert.Equal(c.Field.Select(e => e.Name), s.Field.Select(e => e.IsMe ? c.Field.First(x => x.IsMe).Name : e.Name));
                c.FinishRace(Quick(s));
                c.SkipCard();
                c.Continue();
                if (c.Sponsor == null) c.SignSponsor(c.SponsorOffers[0].Id);
            }
            Assert.Equal(names, c.Field.Select(e => e.Name));
            int sum1 = c.Field.Where(e => !e.IsMe).Sum(e => e.Stats.Power + e.Stats.Handling + e.Stats.Racecraft + e.Stats.Defense);
            Assert.InRange(sum1 - sum0, 6, 30);   // about 0.3 x 19 cars a race
        }

        [Fact]
        public void A_race_can_not_change_the_career_field()
        {
            var c = Ready();
            var s = c.NextRace();
            Assert.DoesNotContain(s.Field, e => c.Field.Contains(e));
            Assert.NotSame(c.Base.Car, s.Field.First(e => e.IsMe).Stats);
        }

        [Fact]
        public void The_season_wraps_into_offers_and_a_new_season()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "brawler", NewSeed = 7 }, 6, Quick);
            var c = m.Career;
            // the bot signed a contract and is in season 2's garage (karts are 6 races)
            Assert.Equal(2, c.Season);
            Assert.Equal(0, c.RaceIdx);
            Assert.Single(c.Past);
            var rv = c.Review;
            Assert.Equal(1, rv.Season);
            Assert.Equal(c.Past[0].Rank, rv.Rank);
            Assert.True(rv.Table.Count >= 10);
            Assert.Equal(rv.Table.Max(r => r.Pts), rv.Champion.Pts);
            Assert.Null(c.Offers);
            Assert.Empty(c.Pts);
            Assert.All(c.Field, e => Assert.Equal(0, e.SeasonWins));
            Assert.Null(c.Sponsor == null ? null : "signed");   // the bot hasn't signed next season's sponsor until it's in the garage...
        }

        [Fact]
        public void Contract_offers_follow_the_points_finish()
        {
            var champ = SeasonRules.Offers(Series.Kart, Teams.PlayerTeam, 1);
            Assert.Equal(new[] { OfferKind.Stay, OfferKind.Move, OfferKind.Promote, OfferKind.Promote }, champ.Select(o => o.Kind));
            Assert.Equal(new[] { 5, 3 }, champ.Where(o => o.Kind == OfferKind.Promote).Select(o => o.Rating));
            Assert.All(champ.Where(o => o.Kind == OfferKind.Promote), o => Assert.Equal(Series.Late, o.Series));
            Assert.Equal(6, champ[1].Rating);                     // rating 3 + 3 for a top-3 season
            Assert.Equal(518 / 50 * 50 + 0, SeasonRules.Offer(OfferKind.Stay, Series.Kart, Teams.PlayerTeam, 4).Per); // 150 x 3 x 1.15 = 517.5 -> 500
            var p12 = SeasonRules.Offers(Series.Late, Teams.PlayerTeam, 12);
            Assert.Equal(new[] { OfferKind.Stay, OfferKind.Promote }, p12.Select(o => o.Kind));
            Assert.Equal(Series.Stock, p12[1].Series);
            Assert.Single(SeasonRules.Offers(Series.Stock, Teams.PlayerTeam, 15));
            Assert.Equal(2, SeasonRules.Offers(Series.Stock, Teams.PlayerTeam, 2).Count);   // no rung above stock cars
            Assert.Equal(1200, SeasonRules.Offer(OfferKind.Promote, Series.Stock, 8, 9).Per);
            Assert.Equal(4800, SeasonRules.Offer(OfferKind.Promote, Series.Stock, 8, 9).Sign);
        }

        [Fact]
        public void A_promotion_out_of_karts_turns_cards_persona_parts_and_field_over()
        {
            var c = Career.New("brawler", seed: 21);
            c.EnterGarage();
            c.Field.First(e => e.IsMe).Starts = 6;
            c.Offers = SeasonRules.Offers(Series.Kart, c.Team, 2);
            int bank = c.Money;
            int pi = c.Offers.FindIndex(o => o.Kind == OfferKind.Promote);
            var o = c.Offers[pi];
            Assert.True(c.SignContract(pi));
            Assert.Equal(Series.Late, c.Series);
            Assert.Equal("bully", c.Persona);
            Assert.Equal(new[] { Series.Kart, Series.Late }, c.Ladder);
            Assert.DoesNotContain(c.Collection, id => CardBook.Get(id).Kart);
            Assert.DoesNotContain(c.Deck, id => CardBook.Get(id).Kart);
            Assert.DoesNotContain(c.PartsOwned, id => Parts.Get(id).Kart);
            Assert.Contains("chrome_horn", c.PartsOwned);
            Assert.Equal(bank + o.Sign, c.Money);
            Assert.Equal(o.Per, c.Contract.Per);
            Assert.Equal(o.Team, c.Team);
            Assert.Equal(o.Team, c.MeEntrant.Team);
            Assert.Equal(6, c.MeEntrant.Starts);
            Assert.Equal(2, c.Field.Count(e => e.Team == o.Team));
            Assert.Equal(o.Rating, c.Car.Power);
            Assert.Equal(SeasonRules.CrewBase(o.Rating), c.Base.CrewChief);
            Assert.Contains(Tracks.ById(CareerRules.Calendar(Series.Late)[0]).Name, c.NextTrack.Name);
            Assert.True(c.DeckOk());
            Assert.NotNull(c.ContractNote);
        }

        [Fact]
        public void Sponsor_offers_rescale_on_promotion()
        {
            var c = Career.New("brawler", seed: 22);
            c.EnterGarage();
            var before = c.SponsorOffers.Select(o => o.Per).ToList();
            c.Offers = SeasonRules.Offers(Series.Kart, c.Team, 1);
            c.SignContract(c.Offers.FindIndex(o => o.Kind == OfferKind.Promote));
            var after = c.SponsorOffers.Select(o => o.Per).ToList();
            for (int i = 0; i < before.Count; i++) Assert.Equal(SeasonRules.R100(before[i] * 2), after[i]);   // karts 15% -> late models 30%
        }

        [Fact]
        public void A_met_goal_pays_its_bonus_and_renews_with_a_raise()
        {
            var c = Ready();
            c.Sponsor = new SponsorDeal { Id = "pinecrest", Per = 400, Bonus = 2400 };
            c.RaceIdx = c.SeasonLength - 1;
            int bank = c.Money;
            var rv = c.WrapSeason();
            Assert.True(rv.Met);   // no DNFs at all
            Assert.Equal(2400, rv.Bonus);
            Assert.Equal(bank + 2400, c.Money);
            Assert.Equal(new SponsorDeal { Id = "pinecrest", Per = 500, Bonus = 2900, Renew = true }.Per, c.SponsorOffers[0].Per);
            Assert.True(c.SponsorOffers[0].Renew);
            Assert.Equal(2900, c.SponsorOffers[0].Bonus);
            Assert.Null(c.Sponsor);
            Assert.Equal(2, c.Season);
            Assert.NotNull(c.Offers);
            Assert.Contains("contract", c.CantRace);
        }

        // ------------------------------------------------------------------ qualifying

        [Fact]
        public void Qualifying_has_a_check_per_corner_per_lap()
        {
            Assert.Equal(8, Qualifying.Plan(Tracks.ById("mill"), CarStats.Rookie(), null, new SimRng(1)).Checks.Count);
            foreach (var t in Tracks.All)
            {
                var pl = Qualifying.Plan(t, CarStats.Rookie(), null, new SimRng(1));
                Assert.Equal(0, pl.Checks.Count % 2);
                Assert.True(pl.Checks.Count >= 4, t.Id);
                Assert.True(pl.Checks.Zip(pl.Checks.Skip(1), (a, b) => b.S0 > a.S1 && b.U > a.U).All(x => x), t.Id);
                Assert.All(pl.Checks, ch => Assert.True(ch.Wp < ch.Wg && ch.Wg < ch.Wo && ch.C >= 0.58 && ch.C <= 0.8));
                Assert.InRange(pl.End, 30, 70);
            }
        }

        [Fact]
        public void Judging_and_scoring_follow_the_slice()
        {
            var ch = new QualiCheck { C = 0.7, Wp = 0.02, Wg = 0.06, Wo = 0.12 };
            Assert.Equal(QualiMark.Perfect, Qualifying.Judge(ch, 0.71));
            Assert.Equal(QualiMark.Good, Qualifying.Judge(ch, 0.65));
            Assert.Equal(QualiMark.Ok, Qualifying.Judge(ch, 0.8));
            Assert.Equal(QualiMark.Early, Qualifying.Judge(ch, 0.5));
            Assert.Equal(QualiMark.Late, Qualifying.Judge(ch, 0.9));
            Assert.Equal(QualiMark.Late, Qualifying.Judge(ch, null));

            QualiPlan Run(string card, params QualiMark[] marks)
            {
                var pl = Qualifying.Plan(Tracks.ById("mill"), CarStats.Rookie(), card, new SimRng(2));
                for (int i = 0; i < marks.Length; i++) pl.Checks[i].Res = marks[i];
                return pl;
            }
            var P = QualiMark.Perfect; var G = QualiMark.Good; var L = QualiMark.Late;
            var sc = Qualifying.Score(Run(null, P, P, G, L));
            Assert.Equal(1.6 + 1.6 * 1.1 + 1 * 1.2 - 1, sc.Tot, 9);
            Assert.Equal(4, sc.Best);
            Assert.Equal(1, sc.Chain);
            // Tidy line (a defend card) turns the first miss of a lap into an OK
            var tidy = Qualifying.Score(Run("block", L, L));
            Assert.Equal(0.25 - 1, tidy.Tot, 9);
            // Send it (risk): Perfect and Good x1.5, a miss x2
            var risk = Qualifying.Score(Run("send_it", P, L));
            Assert.Equal(1.6 * 1.5 - 2, risk.Tot, 9);
        }

        [Fact]
        public void A_good_run_starts_further_up_than_a_skip_and_upgrades_help()
        {
            double Avg(string skill, CarStats st, bool skip)
            {
                double sum = 0;
                for (uint s = 1; s <= 40; s++)
                {
                    var c = Career.New("drafter", seed: s);
                    c.Base.Car = st.Clone(); c.MeEntrant.Stats = c.Base.Car;
                    c.EnterGarage();
                    var q = skip ? c.FinishQuali(null) : c.FinishQuali(new QualiBot(skill, s).Play(c.StartQuali(null)));
                    sum += q.Pos;
                }
                return sum / 40;
            }
            var rookie = CarStats.Rookie();
            double skip = Avg(null, rookie, true), good = Avg("good", rookie, false), poor = Avg("poor", rookie, false);
            Assert.True(good < skip - 1.5, $"good {good} vs skip {skip}");
            Assert.True(poor > good, $"poor {poor} vs good {good}");
            var up = rookie.Clone(); up.Power += 3; up.Handling += 3; up.Racecraft += 3; up.Consistency += 3; up.Aero += 3;
            Assert.True(Avg("good", up, false) < good - 5);
        }

        [Fact]
        public void The_qualifying_card_starts_the_race_in_the_discard_pile_and_sets_the_grid()
        {
            var c = Ready("drafter", 8);
            var card = c.QualiOffer()[0];
            var run = new QualiBot("good", 1).Play(c.StartQuali(card));
            var q = c.FinishQuali(run);
            Assert.Equal(card, q.Card);
            Assert.True(c.QualiDone);
            var s = c.NextRace();
            Assert.Equal(q.Grid, s.Grid);
            var r = new Race(s);
            Assert.Equal(q.Pos, r.Me.Start);
            Assert.Contains(card, r.Discard);
            Assert.Equal(c.Deck.Count, r.Pile.Count + r.Discard.Count + r.Hand.Count);
        }

        [Fact]
        public void Qualifying_rolls_come_from_the_race_so_quitting_cant_reroll_them()
        {
            var c = Ready("drafter", 8);
            Assert.Equal(c.QualiOffer(), c.QualiOffer());
            var a = c.StartQuali(null).Plan.Checks.Select(x => x.C).ToList();
            var b = c.StartQuali(null).Plan.Checks.Select(x => x.C).ToList();
            Assert.Equal(a, b);
            // the game closed mid-run: reopening gives up the run (an average lap)
            c.EnterGarage();
            Assert.True(c.QualiDone);
            Assert.True(c.Qual.Skip);
            Assert.False(c.QualRunning);
        }

        [Fact]
        public void The_live_run_ticks_releases_and_slows_with_Focus()
        {
            var pl = Qualifying.Plan(Tracks.ById("carver"), CarStats.Rookie(), null, new SimRng(3));
            var run = new QualiRun(pl, CarStats.Rookie());
            var ch = pl.Checks[0];
            while (run.Time < ch.S0) run.Tick(1 / 60.0, false);
            // hold through the sweep: time runs at 35% and Focus drains
            double t0 = run.Time;
            run.Tick(0.1, true);
            Assert.True(run.Slowed);
            Assert.Equal(t0 + 0.1 * Qualifying.Slow, run.Time, 9);
            Assert.True(run.Focus < 1);
            while (run.Needle < ch.C) run.Tick(1 / 60.0, true);
            run.Tick(1 / 60.0, false);
            Assert.NotNull(ch.Res);
            Assert.True(ch.Res == QualiMark.Perfect || ch.Res == QualiMark.Good, ch.Res.ToString());
            // never pressing again: every other check runs out Late, and the run ends
            while (!run.Done) run.Tick(1 / 30.0, false);
            Assert.All(pl.Checks.Skip(1), x => Assert.Equal(QualiMark.Late, x.Res));
            Assert.Equal(pl.Laps, run.LapTimes.Count);
        }

        // ------------------------------------------------------------------ the menus

        [Fact]
        public void The_race_weekend_runs_through_the_menus()
        {
            var m = new CareerMenu { PickPersona = "drafter", NewSeed = 6 };
            Assert.True(m.Press("title.start"));
            Assert.False(m.Page().CanPress("go"));
            var sp = m.Career.SponsorOffers[0].Id;
            Assert.Equal("sponsor:" + sp, m.Page().Default);
            Assert.True(m.Press("sponsor:" + sp));
            Assert.True(m.Press("go"));
            Assert.Equal(CareerScreen.Quali, m.Screen);
            var card = m.Career.QualiOffer()[0];
            Assert.True(m.Press("quali.card:" + card));
            Assert.True(m.Press("quali.back"));
            Assert.Equal(CareerScreen.Garage, m.Screen);
            Assert.True(m.Press("go"));
            Assert.True(m.Press("quali.start"));
            Assert.Equal(CareerScreen.QualiRun, m.Screen);
            Assert.NotNull(m.Page());
            var bot = new QualiBot("avg", 1);
            int frames = 0;
            while (m.QualiTick(1 / 60.0, bot.Held(m.Quali))) frames++;
            Assert.InRange(frames / 60.0, 30, 70);
            Assert.Equal(CareerScreen.QualiDone, m.Screen);
            Assert.Equal(20, m.Page().Sections[0].Items.Count);
            // going back to the garage and racing again keeps the grid (no rerolling qualifying)
            var grid = m.Career.Qual.Grid.ToList();
            Assert.True(m.Press("quali.race"));
            Assert.NotNull(m.RaceWanted);
            Assert.Equal(grid, m.RaceWanted.Grid);
            var res = m.RaceFinished(Quick(m.TakeRace()));
            var page = m.Page();
            Assert.Contains(page.Sections, s => s.Title.StartsWith("Objectives", StringComparison.Ordinal));
            Assert.Contains(page.Sections[0].Items, i => i.Label.StartsWith("Points", StringComparison.Ordinal));
        }

        [Fact]
        public void The_season_page_signs_a_contract_and_the_next_season_needs_a_sponsor()
        {
            var bot = new MenuBot { QualiSkill = null };
            var m = new CareerMenu { PickPersona = "packrat", NewSeed = 3 };
            for (int i = 0; i < 5000 && m.Career?.Offers == null; i++)
            {
                if (m.RaceWanted != null) { m.RaceFinished(Quick(m.TakeRace())); continue; }
                Assert.True(bot.Step(m));
            }
            Assert.Equal(CareerScreen.Season, m.Screen);
            var page = m.Page();
            Assert.StartsWith("Season 1 complete", page.Eyebrow);
            Assert.False(page.CanPress("go"));
            Assert.True(page.CanPress(page.Default));
            Assert.True(m.Press("offer:0"));   // stay
            Assert.Equal(CareerScreen.Garage, m.Screen);
            Assert.Equal(Series.Kart, m.Career.Series);
            Assert.NotNull(m.Toast);
            Assert.False(m.Page().CanPress("go"));
            Assert.NotNull(m.Career.SponsorOffers);
        }

        [Fact]
        public void A_kart_career_climbs_to_late_models_through_the_menus()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "brawler", NewSeed = 1 }, 7, null, new MenuBot { QualiSkill = "great" });   // the mouse bot, as the harness (P9 in season 1)
            var c = m.Career;
            Assert.Equal(2, c.Season);
            Assert.Equal(new[] { Series.Kart, Series.Late }, c.Ladder);
            Assert.Equal(Series.Late, c.Series);
            Assert.Equal(7, c.Results.Count);
            Assert.Equal("cedar", c.Results[6].TrackId);
            Assert.True(c.Contract.Per > 0);
        }

        [Fact]
        public void A_v1_save_gets_a_season_on_load()
        {
            var g = CareerSave.Read(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Saves", "v1-gambler-hard-season-2.json")));
            var c = g.Career;
            Assert.Equal(1, g.LoadedFrom);
            Assert.Equal(20, c.Field.Count);
            Assert.Same(c.Base.Car, c.MeEntrant.Stats);
            Assert.Empty(c.Pts);
            Assert.Null(c.Sponsor);
            Assert.Equal(new[] { c.Series }, c.Ladder);
            c.EnterGarage();
            Assert.NotNull(c.SponsorOffers);
            Assert.Contains("sponsor", c.CantRace);
        }
    }
}
