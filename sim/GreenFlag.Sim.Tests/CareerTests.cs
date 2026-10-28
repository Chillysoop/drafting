using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 34: the reward screen, the garage shop, parts, the race deck rules and the race to rewards to shop loop.</summary>
    public class CareerTests
    {
        /// <summary>Runs a career's next race to the flag without the HUD: the pit bot plays the stops and a card goes in every 4 s.</summary>
        static Race RunRace(RaceSetup s)
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

        /// <summary>The race weekend through the menus (step 37): sign a sponsor if there is none, Go racing, skip qualifying,
        /// and start the race.</summary>
        internal static void GoRacing(CareerMenu m)
        {
            if (m.Career.Sponsor == null) Assert.True(m.Press("sponsor:" + m.Career.SponsorOffers[0].Id));
            Assert.True(m.Press("go"));
            if (m.Screen == CareerScreen.Quali) Assert.True(m.Press("quali.skip"));
            Assert.True(m.Press("quali.race"));
        }

        [Theory]
        [InlineData(1, 10, false, RewardTier.Victory)]
        [InlineData(3, 11, false, RewardTier.Victory)]
        [InlineData(3, 10, false, RewardTier.Strong)]
        [InlineData(6, 10, false, RewardTier.Strong)]
        [InlineData(7, 10, false, RewardTier.Solid)]
        [InlineData(13, 10, false, RewardTier.Solid)]
        [InlineData(14, 10, false, RewardTier.Rough)]
        [InlineData(2, 10, true, RewardTier.Rough)]
        public void Reward_tiers_follow_the_slice(int finish, int expected, bool dnf, RewardTier tier) =>
            Assert.Equal(tier, CareerRules.TierFor(finish, expected, dnf));

        [Fact]
        public void Pay_scales_by_skill_dials_and_series()
        {
            var normal = Difficulty.For(DifficultyLevel.Normal);
            double stock = CareerRules.PayMul(normal, InfoLevel.Full, Stakes.Standard, Series.Stock);
            Assert.Equal(0.4, stock, 9);
            Assert.Equal(3400, CareerRules.Purse(1, stock));       // (1500 + 20 x 350) x 0.4
            Assert.Equal(1850, CareerRules.Purse(20, 1));
            Assert.Equal(800, CareerRules.Bonus(8, 13, stock));    // 5 places x 400 x 0.4
            Assert.Equal(0, CareerRules.Bonus(12, 10, stock));
            double kart = CareerRules.PayMul(Difficulty.For(DifficultyLevel.Easy), InfoLevel.Feel, Stakes.Permadeath, Series.Kart);
            Assert.Equal(1.25 * 1.45 * 0.15, kart, 9);
            Assert.Equal(1500, CareerRules.RerollCost(DifficultyLevel.Normal, 0));
            Assert.Equal(4500, CareerRules.RerollCost(DifficultyLevel.Brutal, 2));
        }

        [Fact]
        public void A_new_career_owns_its_persona_deck_and_part()
        {
            var c = Career.New("bully");
            Assert.Equal(Series.Stock, c.Series);
            Assert.Equal(12, c.Deck.Count);
            Assert.Equal(c.Deck, c.Collection);
            Assert.Equal("chrome_horn", c.EquippedIn(PartSlot.Chassis));
            Assert.Equal(CareerRules.StartMoney, c.Money);
            Assert.Equal(5, c.Car.Defense);
            var l = c.Loadout();
            Assert.Equal(new[] { "chrome_horn" }, l.Parts);
            Assert.Equal(c.Deck, l.Deck);
            var k = Career.New("packrat");
            Assert.Equal(Series.Kart, k.Series);
            Assert.Equal("pinek", k.NextTrack.Id);
            Assert.Equal(6, k.SeasonLength);
        }

        [Fact]
        public void Deck_size_and_slot_caps_move_with_the_tune()
        {
            var u = new Tune { Hand = 1, Aero = -2, Focus = ComboFocus.Draft };
            Assert.Equal((12, 18), DeckRules.Size(Series.Stock, u));
            Assert.Equal((10, 13), DeckRules.Size(Series.Kart, new Tune()));
            Assert.Equal(7, DeckRules.Cap(CareerRules.Attack, u));
            Assert.Equal(5, DeckRules.Cap(CareerRules.Defense, u));
            Assert.Equal(4, DeckRules.Cap(CareerRules.Drafting, u)); // 5 - 2 + 1 for draft focus
        }

        [Fact]
        public void Auto_and_fixed_decks_meet_every_tracks_rules()
        {
            foreach (var persona in Personas.All.Select(p => p.Id))
            {
                var c = Career.New(persona);
                // a bigger collection: every card of the series once more
                foreach (var cd in CardBook.All.Where(cd => !cd.Special && cd.Kart == c.Kart)) c.Collection.Add(cd.Id);
                foreach (var t in Tracks.All.Where(t => t.Series == c.Series || (c.Series == Series.Stock && t.Series == Series.Stock)))
                {
                    var auto = DeckRules.Auto(c, t);
                    Assert.True(DeckRules.Ok(c, t, auto), $"{persona} auto deck at {t.Id}: " + string.Join("; ", DeckRules.Rules(c, t, auto).Where(r => !r.Ok).Select(r => r.Text)));
                    Assert.Equal(DeckRules.Size(c.Series, c.Tune).max, auto.Count);
                    // a deck stuffed with one family gets fixed
                    var bad = Enumerable.Repeat(c.Collection.First(id => CardBook.Get(id).Family == CardFamily.Pass), 2).Concat(c.Collection.Where(id => CardBook.Get(id).Family == CardFamily.Pass)).ToList();
                    var fixd = DeckRules.Fix(c, t, bad);
                    Assert.True(DeckRules.Ok(c, t, fixd), $"{persona} fixed deck at {t.Id}");
                }
            }
        }

        [Fact]
        public void Add_block_says_why_a_card_cant_go_in()
        {
            var c = Career.New("drafter");
            var t = Tracks.Carver;
            string own = c.Deck[0];
            Assert.Equal("All your copies are in the deck", c.AddBlock(own, t));
            c.Collection.Add("slingshot"); c.Collection.Add("slingshot"); c.Collection.Add("slingshot"); c.Collection.Add("bump_draft");
            c.SetTune(aero: -2, focus: ComboFocus.LongRun); // drafting slots 3
            c.Deck = c.Deck.Where(id => CardBook.Get(id).Family != CardFamily.Draft).ToList();
            for (int i = 0; i < 3; i++) Assert.True(c.AddToDeck("slingshot"));
            Assert.StartsWith("Drafting slots are full", c.AddBlock("bump_draft", t));
        }

        [Fact]
        public void The_shop_sells_upgrades_and_parts_and_rerolls_cost_more_each_time()
        {
            var c = Career.New("drafter", seed: 5);
            c.Money = 50000;
            c.EnterGarage();
            Assert.NotNull(c.ShopPart);
            int lvl = c.StatLevel("power"), cost = c.StatCost("power");
            Assert.Equal(3500, cost);
            Assert.True(c.BuyStat("power"));
            Assert.Equal(lvl + 1, c.Car.Power);
            Assert.Equal(50000 - 3500, c.Money);
            Assert.Equal(5000, c.StatCost("power"));
            Assert.Equal(3000, c.StatCost("defense")); // coaching: 3000 + 1200 per level above 4
            int m = c.Money;
            Assert.True(c.RerollShopPart()); Assert.True(c.RerollShopPart());
            Assert.Equal(m - 1500 - 2500, c.Money);
            var part = c.ShopPart;
            Assert.True(c.BuyShopPart());
            Assert.Contains(part, c.PartsOwned);
            Assert.Null(c.ShopPart);
            c.Car.Power = 10;
            Assert.Equal("Maxed out", c.CantBuyStat("power"));
            c.Money = 0;
            Assert.False(c.BuyStat("handling"));
        }

        [Fact]
        public void Parts_fit_one_per_slot_and_spares_swap_in()
        {
            var c = Career.New("drafter");
            c.AddPart("low_spoiler"); // aero slot already has the Slick Nose
            Assert.Equal("slick_nose", c.EquippedIn(PartSlot.Aero));
            Assert.Contains("low_spoiler", c.Spares);
            c.Equip("low_spoiler");
            Assert.Equal("low_spoiler", c.EquippedIn(PartSlot.Aero));
            Assert.Equal(new[] { "low_spoiler" }, c.Loadout().Parts);
            c.Unequip(PartSlot.Aero);
            Assert.Empty(c.Loadout().Parts);
            var kart = Career.New("brawler");
            Assert.All(kart.RollParts(30), id => Assert.False(Parts.Get(id).Stock));
            Assert.All(c.RollParts(30), id => Assert.False(Parts.Get(id).Kart));
        }

        [Fact]
        public void Card_offers_follow_the_tier()
        {
            var c = Career.New("whisperer", seed: 9);
            for (int i = 0; i < 40; i++)
            {
                Assert.Equal(2, c.RollCards(RewardTier.Rough).Count);
                var s = c.RollCards(RewardTier.Strong);
                Assert.Equal(3, s.Distinct().Count());
                Assert.Contains(s, id => CardBook.Get(id).Rarity != Rarity.Common);
                Assert.All(s, id => { Assert.False(CardBook.Get(id).Kart); Assert.False(CardBook.Get(id).Special); });
            }
            var k = Career.New("linepainter", seed: 9);
            Assert.All(k.RollCards(RewardTier.Victory), id => Assert.True(CardBook.Get(id).Kart));
        }

        [Fact]
        public void A_finished_race_pays_out_logs_your_calls_and_offers_rewards()
        {
            var c = Career.New("drafter", seed: 3);
            c.EnterGarage();
            int before = c.Money;
            var r = RunRace(c.NextRace());
            var res = c.FinishRace(r);
            Assert.Equal(before + res.Total, c.Money);
            Assert.Equal(res.Purse + res.Bonus, res.Total);
            Assert.Equal(CareerRules.Purse(res.Finish, 0.4), res.Purse);
            Assert.Equal(r.CardStats.Plays, res.Log.Count);
            Assert.True(res.Plays > 0);
            Assert.All(res.Log, l => Assert.InRange(l.Lap, 1, r.Track.Laps));
            Assert.True(res.Order.Count >= 10 && res.Order.Count <= 11);
            Assert.Contains(res.Order, o => o.IsMe);
            Assert.Equal(res.Plays, c.Hist.Values.Sum(h => h.Plays));
            var R = c.Reward;
            Assert.Equal(res.Tier == RewardTier.Rough ? 2 : 3, R.Cards.Count);
            Assert.Equal(CareerRules.PartPick(res.Tier), R.Parts != null);

            int m = c.Money;
            Assert.True(c.RerollCards());
            Assert.Equal(m - 1500, c.Money);
            var pick = R.Cards[0];
            int owned = DeckRules.Count(c.Collection, pick);
            Assert.True(c.PickCard(pick));
            Assert.Equal(owned + 1, DeckRules.Count(c.Collection, pick));
            Assert.False(c.SkipCard());
            c.Continue();
            Assert.Null(c.Reward);
            Assert.Equal(1, c.RaceIdx);
            Assert.Equal("pine", c.NextTrack.Id);
            Assert.True(c.DeckOk());
        }

        [Fact]
        public void Skipping_the_card_pays_1000()
        {
            var c = Career.New("restart", seed: 4);
            c.EnterGarage();
            c.FinishRace(RunRace(c.NextRace()));
            int m = c.Money;
            Assert.True(c.SkipCard());
            Assert.Equal(m + 1000, c.Money);
            Assert.False(c.PickCard(c.Reward.Cards[0]));
        }

        [Fact]
        public void Ironman_and_permadeath_make_a_dnf_cost()
        {
            var c = Career.New("drafter", stakes: Stakes.Ironman);
            c.Money = 10000; c.AddPart("roll_cage"); c.Unequip(PartSlot.Chassis);
            var res = new RaceReport { Dnf = true };
            c.ApplyStakes(res);
            Assert.Equal("ironman", res.Penalty);
            Assert.Equal(7000, c.Money);
            Assert.Equal("roll_cage", res.LostPart);
            Assert.DoesNotContain("roll_cage", c.PartsOwned);

            var p = Career.New("drafter", stakes: Stakes.Permadeath);
            var r1 = new RaceReport { Dnf = true };
            p.ApplyStakes(r1);
            Assert.Equal("survive", r1.Penalty);
            Assert.False(p.Over);
            var r2 = new RaceReport { Dnf = true };
            p.ApplyStakes(r2);
            Assert.Equal("dead", r2.Penalty);
            Assert.True(p.Over);

            var s = Career.New("drafter");
            var r3 = new RaceReport { Dnf = true };
            s.ApplyStakes(r3);
            Assert.Null(r3.Penalty);
        }

        [Fact]
        public void A_track_rule_the_deck_breaks_gets_fixed_on_the_way_into_the_garage()
        {
            var c = Career.New("whisperer");
            c.Deck = c.Deck.Where(id => CardBook.Get(id).Family != CardFamily.Defend).ToList(); // Millbrook wants 2 Defending
            Assert.False(c.DeckOk(Tracks.Millbrook));
            c.EnterGarage();
            Assert.True(c.DeckOk());
            Assert.Contains("Millbrook rule", c.DeckNote);
        }

        // ------------------------------------------------------------------ the menus

        [Fact]
        public void The_menu_loops_race_rewards_shop_and_the_next_race()
        {
            var m = new CareerMenu { PickPersona = "drafter", NewSeed = 11 };
            var bot = new MenuBot();
            MenuBot.Run(m, 3, RunRace, bot);
            var c = m.Career;
            Assert.Equal(CareerScreen.Garage, m.Screen);
            Assert.Equal(3, c.Results.Count);
            Assert.Equal(3, c.RaceIdx);
            Assert.True(c.Collection.Count >= 12 + 2, "a card from each race unless skipped");
            Assert.True(bot.Bought > 0, "the bot spends money in the shop");
            Assert.Equal(new[] { "mill", "pine", "carver" }, c.Results.Select(r => r.TrackId));
            Assert.True(c.DeckOk());
        }

        [Fact]
        public void The_kart_menu_loop_wraps_into_season_two()
        {
            var m = new CareerMenu { PickPersona = "brawler", NewSeed = 2 };
            MenuBot.Run(m, 7, RunRace);
            Assert.Equal(2, m.Career.Season);
            Assert.Equal(1, m.Career.RaceIdx);
            Assert.All(m.Career.Collection, id => Assert.True(CardBook.Get(id).Kart));
        }

        [Fact]
        public void A_career_replays_the_same_from_its_seed()
        {
            string Run()
            {
                var m = new CareerMenu { PickPersona = "gambler", NewSeed = 77 };
                MenuBot.Run(m, 2, RunRace);
                var c = m.Career;
                return $"{c.Money}|{string.Join(",", c.Collection)}|{string.Join(",", c.PartsOwned)}|{string.Join(",", c.Results.Select(r => r.Finish))}";
            }
            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void Go_racing_is_off_while_the_deck_breaks_a_rule()
        {
            var m = new CareerMenu(Career.New("drafter"));
            m.Press("sponsor:" + m.Career.SponsorOffers[0].Id);
            m.Career.ClearDeck();
            Assert.False(m.Page().CanPress("go"));
            Assert.False(m.Press("go"));
            Assert.Null(m.RaceWanted);
            Assert.True(m.Press("deck"));
            Assert.Equal(CareerScreen.DeckBuilder, m.Screen);
            Assert.False(m.Page().CanPress("deck.done"));
            Assert.True(m.Press("deck.auto"));
            Assert.True(m.Press("deck.done"));
            GoRacing(m);
            Assert.NotNull(m.TakeRace());
        }

        [Fact]
        public void Deck_builder_buttons_add_and_take_out_cards()
        {
            var m = new CareerMenu(Career.New("drafter"));
            m.Career.Collection.Add("slingshot");
            m.Press("deck");
            int n = m.Career.Deck.Count;
            Assert.True(m.Press("deck.in:slingshot"));
            Assert.Equal(n + 1, m.Career.Deck.Count);
            Assert.True(m.Press("deck.out:slingshot"));
            Assert.True(m.Press("deck.out:slingshot"));
            Assert.Equal(n - 1, m.Career.Deck.Count);
            Assert.False(m.Press("deck.out:slingshot")); // none left in the deck
            Assert.True(m.Press("deck.sort:fit"));
            Assert.True(m.Press("deck.tab:Draft"));
            Assert.All(m.Page().Sections[0].Items.Where(i => i.Kind == MenuKind.Card), i => Assert.Equal(CardStyle.Family(CardFamily.Draft).R, i.Color.Value.R));
            Assert.True(m.Press("tune.aero:2"));
            Assert.Equal(2, m.Career.Tune.Aero);
        }

        [Fact]
        public void Retry_puts_the_career_back_as_it_was_before_the_race()
        {
            var m = new CareerMenu(Career.New("drafter", seed: 8));
            m.Press("sponsor:" + m.Career.SponsorOffers[0].Id);
            int money = m.Career.Money;
            GoRacing(m);
            m.RaceFinished(RunRace(m.TakeRace()));
            Assert.NotEqual(money, m.Career.Money);
            Assert.True(m.Press("reward.retry"));
            Assert.Equal(money, m.Career.Money);
            Assert.Empty(m.Career.Results);
            Assert.NotNull(m.TakeRace());

            var iron = new CareerMenu(Career.New("drafter", stakes: Stakes.Ironman));
            GoRacing(iron);
            iron.RaceFinished(RunRace(iron.TakeRace()));
            Assert.False(iron.Page().CanPress("reward.retry"));
        }

        [Fact]
        public void Reward_page_shows_payout_top10_and_your_calls()
        {
            var m = new CareerMenu(Career.New("drafter", seed: 21));
            GoRacing(m);
            var res = m.RaceFinished(RunRace(m.TakeRace()));
            var p = m.Page();
            Assert.Equal($"P{res.Finish}" + (res.Dnf ? " · DNF" : ""), p.Title);
            Assert.Contains(p.Sections, s => s.Title == "Payout" && s.Items.Any(i => i.Value == "+" + CareerMenu.Money(res.Total)));
            Assert.Contains(p.Sections, s => s.Title == "Top 10 this race");
            Assert.Equal(res.Log.Count, p.Sections.First(s => s.Title == "Your calls").Items.Count);
            Assert.Equal(m.Career.Reward.Cards.Count, p.Sections.First(s => s.Title.StartsWith("Pick a card")).Items.Count(i => i.Kind == MenuKind.Card));
        }

        [Fact]
        public void The_focus_walks_every_button_and_presses_with_confirm()
        {
            var m = new CareerMenu();
            var f = new MenuFocus();
            var p = m.Page();
            Assert.Equal("title.start", f.Current(p).Id);
            int n = p.Pressables().Count();
            var seen = new HashSet<string>();
            for (int i = 0; i < n; i++) { seen.Add(f.Current(p).Id); f.Move(p, 1); }
            Assert.Equal(n, seen.Count);
            Assert.Equal("title.start", f.Current(p).Id);
            f.Move(p, -1);
            f.Move(p, 1);
            Assert.True(f.Confirm(m));
            Assert.Equal(CareerScreen.Garage, m.Screen);
            m.Press("deck");
            Assert.True(f.Back(m));
            Assert.Equal(CareerScreen.Garage, m.Screen);
        }

        [Fact]
        public void Money_formats_like_the_slice() => Assert.Equal("$12,345", CareerMenu.Money(12345));
    }
}
