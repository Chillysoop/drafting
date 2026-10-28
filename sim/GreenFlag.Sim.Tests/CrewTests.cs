using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 39: Crew HQ in the career (slice v42's crew-js: seats, training, perks, traits, hiring, crew XP, wages and
    /// pit card upgrades), plus the off-season staff market, rival teams' offers for your crew and staff who follow you to a
    /// new team. The step is done when a rival can hire your crew chief and you can match the offer:
    /// A_rival_can_hire_your_crew_chief_and_you_can_match_it.</summary>
    public class CrewTests
    {
        static Race Quick(RaceSetup s)
        {
            s.PitPolicy = PitPolicy.Bot;
            var r = new Race(s);
            for (int i = 0; i < 200000 && !r.Done; i++) r.Tick();
            Assert.True(r.Done);
            return r;
        }

        static Career Ready(string persona = "drafter", uint seed = 3)
        {
            var c = Career.New(persona, seed: seed);
            c.EnterGarage();
            Assert.True(c.SignSponsor(c.SponsorOffers[0].Id));
            return c;
        }

        /// <summary>Every number in a CrewPerks, for comparing two of them.</summary>
        static string Fx(CrewPerks p)
        {
            string F(object o) => string.Join(",", o.GetType().GetFields().Select(f => f.FieldType == typeof(CallFx) ? f.Name + "{" + F(f.GetValue(o)) + "}" : f.Name + "=" + f.GetValue(o)));
            return F(p);
        }

        static CrewMember Chief(Career c) => c.Staff[CrewRole.CrewChief];

        [Fact]
        public void A_new_career_has_four_volunteers_at_the_personas_levels_and_races_as_before()
        {
            foreach (var p in Personas.All)
            {
                var c = Career.New(p.Id, seed: 5);
                var l0 = PlayerLoadout.ForPersona(p.Id);
                Assert.Equal(4, c.Staff.Count);
                foreach (var r in CrewRules.Roles)
                {
                    var m = c.Staff[r];
                    Assert.Equal(l0.Level(r), m.Lvl);
                    Assert.Equal(0, m.Wage);
                    Assert.Null(m.Trait);
                    Assert.All(m.Perks, x => Assert.Equal(0, x));
                    Assert.Equal(l0.Level(r), c.Loadout().Level(r));
                }
                Assert.Equal(4, c.Staff.Values.Select(m => m.Name).Distinct().Count());
                // no perks, no traits: the crew's numbers are a fresh loadout's
                Assert.Equal(Fx(l0.Crew), Fx(c.Base.Crew));
            }
        }

        [Fact]
        public void Training_costs_the_coaching_curve_and_reaches_the_race()
        {
            var c = Ready();
            c.Money = 100000;
            var m = c.Staff[CrewRole.Spotter];
            int lvl = m.Lvl, cost = CrewRules.TrainCost(lvl);
            Assert.Equal(3000 + 1200 * Math.Max(0, lvl - 3), cost);
            Assert.True(c.Train(CrewRole.Spotter));
            Assert.Equal(100000 - cost, c.Money);
            Assert.Equal(lvl + 1, c.NextRace().Loadout.Spotter);
            while (m.Lvl < CrewRules.Max) Assert.True(c.Train(CrewRole.Spotter));
            Assert.Equal("Level 10", c.CantTrain(CrewRole.Spotter));
            c.Money = 0;
            Assert.Equal("Not enough money", c.CantTrain(CrewRole.Engineer));
        }

        [Fact]
        public void Perks_open_at_4_6_8_are_picked_once_and_reach_the_race()
        {
            var c = Ready();
            c.Money = 100000;
            var sp = c.Staff[CrewRole.Spotter];
            while (sp.Lvl < 3) c.Train(CrewRole.Spotter);
            if (sp.Lvl >= 4) { sp.Lvl = 3; c.SyncCrew(); }
            Assert.Equal("Needs level 4", c.PickPerk(CrewRole.Spotter, 0, 1));
            c.Train(CrewRole.Spotter);
            var call = RadioCalls.Get("High line");
            int before = c.Loadout().CallChance(call, false);
            Assert.Null(c.PickPerk(CrewRole.Spotter, 0, 1)); // Eyes up top: +5% on spotter calls
            Assert.Equal("Already picked", c.PickPerk(CrewRole.Spotter, 0, 2));
            Assert.Equal(before + 5, c.Loadout().CallChance(call, false));
            Assert.Equal(5, c.NextRace().Loadout.Crew.Spotter.Chance);
            // the pit crew's perks reach the pit mini-game's numbers
            var pc = c.Staff[CrewRole.PitCrew];
            pc.Lvl = 8; c.SyncCrew();
            Assert.Equal(3, c.PerksWaiting - (c.Staff.Values.Where(x => x.Role != CrewRole.PitCrew).Sum(x => Enumerable.Range(0, 3).Count(t => x.Lvl >= CrewRules.PerkLevel(x.Role, t) && x.Perks[t] == 0))));
            Assert.Null(c.PickPerk(CrewRole.PitCrew, 1, 1)); // Drilled: release window +0.15 s
            Assert.Null(c.PickPerk(CrewRole.PitCrew, 2, 2)); // Body men
            var l = c.Loadout();
            Assert.Equal(0.15, l.Crew.ReleaseWindow, 6);
            Assert.True(l.Crew.BodyMen);
            Assert.Equal(8, l.PitSkill);
        }

        [Fact]
        public void A_race_pays_crew_XP_levels_them_up_and_takes_their_wages()
        {
            var c = Ready();
            c.Staff[CrewRole.Engineer].Wage = 600;
            c.Staff[CrewRole.Engineer].Trait = "mentor"; // the whole crew earns 50% more XP
            c.SyncCrew();
            var m = c.Staff[CrewRole.Spotter];
            int lvl = m.Lvl;
            m.Xp = CrewRules.XpNeed(lvl) - 1;
            var race = Quick(c.NextRace());
            race.CardStats.CrewXp.Clear();
            race.CardStats.CrewXp[CrewRole.Spotter] = 6;
            race.CardStats.CrewXp[CrewRole.PitCrew] = 8;
            int money = c.Money;
            var res = c.FinishRace(race);
            double k = 1.5 + 0.1 * Math.Max(0, c.Driver.ModOf(DAttr.Rapport));
            Assert.Equal((int)SimMath.RoundJs(6 * k), res.CrewXp[CrewRole.Spotter]);
            Assert.Equal((int)SimMath.RoundJs(8 * k), res.CrewXp[CrewRole.PitCrew]);
            Assert.Contains(CrewRole.Spotter, res.CrewUp);
            Assert.Equal(lvl + 1, m.Lvl);
            Assert.Equal(lvl + 1, c.Base.Spotter);
            Assert.Equal(600, res.Wages);
            Assert.Equal(money + res.Total, c.Money);
            int gross = res.Purse + res.Bonus + res.Retainer + res.Salary + res.Objectives.Where(o => o.Ok).Sum(o => o.Pay);
            Assert.Equal(gross - 600, res.Total);
        }

        [Fact]
        public void Hiring_offers_three_between_races_and_a_bigger_market_in_the_off_season()
        {
            var c = Ready();
            c.Money = 100000;
            var h = c.Hires();
            Assert.Equal(3, h.Count);
            Assert.Same(h, c.Hires()); // rolled once per race
            foreach (var x in h)
            {
                int cur = c.Staff[x.Role].Lvl;
                Assert.InRange(x.Lvl, Math.Max(3, cur), Math.Min(10, cur + 2));
                double fee = 1200; for (int l = cur; l < x.Lvl; l++) fee += CrewRules.TrainCost(l) * 0.8;
                Assert.Equal((int)(SimMath.RoundJs(fee / 100) * 100), x.Fee);
                Assert.Equal(CrewRules.BaseWage(x.Lvl, c.MoneyK), x.Wage);
                Assert.Contains(x.Trait, CrewRules.TraitsFor(x.Role));
            }
            var pick = h[0];
            Assert.Null(c.HireStaff(0));
            var m = c.Staff[pick.Role];
            Assert.Equal(pick.Name, m.Name);
            Assert.Equal(pick.Lvl, c.Loadout().Level(pick.Role));
            Assert.Equal(pick.Wage, m.Wage);
            Assert.Equal(2, c.Hires().Count);
            // the trait reaches the loadout
            var fresh = new CrewPerks(); fresh.Add(pick.Role, CrewHq.Traits[pick.Trait].Fx);
            Assert.Equal(Fx(fresh), Fx(c.Base.Crew));

            // the off-season staff market: 6 people, every seat, up to 3 levels better, a quarter off
            var s2 = Career.New("drafter", seed: 9);
            s2.Season = 2; s2.RaceIdx = 0;
            Assert.True(s2.Market);
            var mk = s2.Hires();
            Assert.Equal(6, mk.Count);
            Assert.Equal(4, mk.Select(x => x.Role).Distinct().Count());
            foreach (var x in mk)
            {
                int cur = s2.Staff[x.Role].Lvl;
                Assert.InRange(x.Lvl, Math.Max(3, cur), Math.Min(10, cur + 3));
                double fee = 1200; for (int l = cur; l < x.Lvl; l++) fee += CrewRules.TrainCost(l) * 0.8;
                Assert.Equal((int)(SimMath.RoundJs(fee * 0.75 / 100) * 100), x.Fee);
            }
        }

        [Fact]
        public void Wages_and_hires_scale_with_the_series_pay()
        {
            Assert.Equal(750, CrewRules.BaseWage(5, 1));
            var k = Career.New("latebraker", seed: 2);
            Assert.Equal(Series.Kart, k.Series);
            Assert.All(k.Hires(), x => Assert.Equal(CrewRules.R50(250 * (x.Lvl - 2) * 0.375), x.Wage));
        }

        [Fact]
        public void Pit_cards_upgrade_once_and_dealer_cards_swap_in()
        {
            var c = Ready();
            c.Money = 10000;
            int i = c.Base.PitDeck.IndexOf("quick_jack");
            Assert.True(c.UpgradePit(i));
            Assert.Equal("quick_jack+", c.Base.PitDeck[i]);
            Assert.Equal("Upgraded", c.CantUpgradePit(i));
            Assert.Equal(10000 - CrewRules.PitUpgrade, c.Money);
            Assert.True(c.SwapPit("lightning_lugs", 0));
            Assert.Equal("lightning_lugs", c.Base.PitDeck[0]);
            Assert.False(c.SwapPit("fix", 1)); // only dealer cards
            Assert.Equal("lightning_lugs", c.NextRace().Loadout.PitDeck[0]);
        }

        [Fact]
        public void A_rival_can_hire_your_crew_chief_and_you_can_match_it()
        {
            var c = Ready();
            var ch = Chief(c);
            ch.Lvl = 7; c.SyncCrew();
            var o = c.RollPoach(force: true, role: CrewRole.CrewChief);
            Assert.NotNull(o);
            Assert.Equal(CrewRole.CrewChief, o.Role);
            Assert.NotEqual(c.Team, o.Team);
            Assert.Equal(CrewRules.PoachWage(ch, o.Rating, c.MoneyK), o.Wage);
            Assert.True(o.Wage > ch.Wage);
            Assert.Contains("offer for your crew chief", c.CantRace);
            Assert.Null(c.RollPoach(force: true)); // one at a time
            Assert.True(c.MatchPoach());
            Assert.Equal(o.Wage, ch.Wage);
            Assert.Equal(c.Season, ch.Kept);
            Assert.True(o.Done);
            Assert.Null(c.CantRace);
            // kept: no more offers for them this season
            Assert.Null(c.RollPoach(force: true, role: CrewRole.CrewChief));

            // the other way: let them go, and the rival has your crew chief
            var d = Ready(seed: 4);
            Chief(d).Lvl = 7; Chief(d).Perks[0] = 1; d.SyncCrew();
            var name = Chief(d).Name;
            var p = d.RollPoach(force: true, role: CrewRole.CrewChief);
            int money = d.Money;
            Assert.True(d.LetGo());
            Assert.Equal(money + p.Buyout, d.Money);
            Assert.NotEqual(name, Chief(d).Name);
            Assert.Equal(5, Chief(d).Lvl);
            Assert.All(Chief(d).Perks, x => Assert.Equal(0, x));
            Assert.Equal(5, d.Loadout().CrewChief);
            Assert.Contains(p.TeamName, p.Outcome);
        }

        [Fact]
        public void Talking_them_round_shows_its_math_and_every_result_does_what_it_says()
        {
            var seen = new HashSet<CheckTier>();
            for (uint seed = 1; seed < 400 && seen.Count < 5; seed++)
            {
                var c = Ready(seed: seed);
                var ch = Chief(c);
                ch.Lvl = 6; ch.Wage = 500; c.SyncCrew();
                var o = c.RollPoach(force: true, role: CrewRole.CrewChief);
                var b = c.TalkBuild();
                Assert.Equal(CrewRules.TalkDc + o.Rating / 2 + b.Dcl.Sum(x => x.V), b.Dc);
                Assert.Equal(DSkill.Crew, b.Skill);
                int money = c.Money;
                var n = c.TalkPoach();
                Assert.Null(c.TalkPoach()); // once
                Assert.Equal(n.Kept + n.Mod, n.Total);
                seen.Add(n.Tier);
                Assert.Equal(CrewRules.TalkFx[(int)n.Tier].txt, n.Txt);
                switch (n.Tier)
                {
                    case CheckTier.Crit: Assert.True(o.Done); Assert.Equal(500, Chief(c).Wage); Assert.Equal(c.Season, Chief(c).Kept); break;
                    case CheckTier.Success: Assert.True(o.Done); Assert.Equal(CrewRules.R50(500 + (o.Wage - 500) / 2.0), Chief(c).Wage); break;
                    case CheckTier.Mixed: Assert.False(o.Done); Assert.Equal(o.Wage, o.MatchAt); break;
                    case CheckTier.Fail: Assert.False(o.Done); Assert.Equal(CrewRules.R50(o.Wage * 1.1), o.MatchAt); Assert.True(c.MatchPoach()); Assert.Equal(o.MatchAt, Chief(c).Wage); break;
                    case CheckTier.Fumble: Assert.True(o.Done); Assert.Equal(money + o.Buyout, c.Money); Assert.Equal(4, Chief(c).Lvl); break;
                }
                // the same career talks the same way: quitting can't reroll it
                var again = Ready(seed: seed);
                Chief(again).Lvl = 6; Chief(again).Wage = 500; again.SyncCrew();
                again.RollPoach(force: true, role: CrewRole.CrewChief);
                Assert.Equal(n.Kept, again.TalkPoach().Kept);
            }
            Assert.Equal(5, seen.Count);
        }

        [Fact]
        public void Offers_come_after_races_for_good_crew_only_and_at_most_twice_a_season()
        {
            int offers = 0, low = 0;
            for (uint seed = 1; seed <= 60; seed++)
            {
                var c = Ready(seed: seed);
                foreach (var m in c.Staff.Values) m.Lvl = 4;
                c.SyncCrew();
                for (int r = 0; r < 4; r++) { c.RaceIdx = r; if (c.RollPoach() != null) low++; }
                foreach (var m in c.Staff.Values) m.Lvl = 9;
                c.SyncCrew();
                c.Poached = 0;
                for (int r = 0; r < 8; r++) { c.RaceIdx = r; c.Poach = null; if (c.RollPoach() != null) offers++; }
                Assert.InRange(c.Poached, 0, CrewRules.PoachMax);
            }
            Assert.Equal(0, low);
            Assert.InRange(offers, 20, 120);
        }

        [Fact]
        public void Kept_and_long_serving_staff_follow_you_to_a_new_team()
        {
            var c = Ready();
            c.Money = 100000;
            var ch = Chief(c); ch.Lvl = 7; ch.Kept = 1; ch.Perks[0] = 2;
            var sp = c.Staff[CrewRole.Spotter]; sp.Lvl = 6; sp.Since = 1;
            var pc = c.Staff[CrewRole.PitCrew]; pc.Lvl = 2; pc.Kept = 1;
            var en = c.Staff[CrewRole.Engineer]; en.Since = 1;
            c.SyncCrew();
            c.Base.PitDeck[0] = "quick_jack+";
            c.Season = 3; // the spotter and engineer have two seasons in
            var f = c.Followers(7).ToDictionary(x => x.m.Role);
            Assert.True(f[CrewRole.CrewChief].follows);
            Assert.True(f[CrewRole.Spotter].follows);
            Assert.False(f[CrewRole.PitCrew].follows); // kept, but the new team's is better
            Assert.Equal(en.Lvl >= SeasonRules.CrewBase(7), f[CrewRole.Engineer].follows);

            // sign with a better team: the followers come with their levels, perks and wages
            c.Offers = new List<ContractOffer> { SeasonRules.Offer(OfferKind.Move, c.Series, SeasonRules.TeamAt(c.Series, 7), 1) };
            int cb = SeasonRules.CrewBase(c.Offers[0].Rating);
            Assert.True(c.SignContract(0));
            Assert.Same(ch, Chief(c));
            Assert.Equal(2, Chief(c).Perks[0]);
            Assert.Equal(7, c.Loadout().CrewChief);
            Assert.Equal(6, c.Loadout().Spotter);
            Assert.NotSame(pc, c.Staff[CrewRole.PitCrew]);
            Assert.Equal(cb, c.Loadout().PitSkill);
            Assert.Equal(PitCards.StartingDeck, c.Base.PitDeck); // the pit crew stayed behind with its cards
            Assert.Contains("follow", c.ContractNote);
        }

        [Fact]
        public void Crew_HQ_saves_and_loads_with_an_offer_on_the_table()
        {
            var c = Ready();
            c.Money = 50000;
            Chief(c).Lvl = 7; Chief(c).Trait = "veteran"; Chief(c).Perks[1] = 2; c.SyncCrew();
            c.Hires();
            c.UpgradePit(2);
            var o = c.RollPoach(force: true, role: CrewRole.CrewChief);
            c.TalkPoach();
            var back = Career.FromJson(JsonValue.Parse(c.ToJson().Write()));
            Assert.Equal(c.ToJson().Write(), back.ToJson().Write());
            Assert.Equal(Chief(c).Name, Chief(back).Name);
            Assert.Equal(o.Talk.Kept, back.Poach.Talk.Kept);
            Assert.Equal(c.Hire.Count, back.Hire.Count);
            Assert.Equal(Fx(c.Base.Crew), Fx(back.Base.Crew));
            Assert.Equal(c.CantRace, back.CantRace);
            var copy = c.Clone();
            copy.Staff[CrewRole.Spotter].Lvl = 9;
            Assert.NotEqual(9, c.Staff[CrewRole.Spotter].Lvl);
        }

        [Fact]
        public void A_version_3_save_gets_volunteers_at_its_crew_levels()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Saves", "v3-gambler-hard-season-2.json"));
            var raw = JsonValue.Parse(text);
            var g = CareerSave.Read(text);
            var b = raw["career"]["base"];
            var c = g.Career;
            Assert.Equal(b["crewChief"].Int(), c.Staff[CrewRole.CrewChief].Lvl);
            Assert.Equal(b["spotter"].Int(), c.Staff[CrewRole.Spotter].Lvl);
            Assert.Equal(b["pitCrew"].Int() > 0 ? b["pitCrew"].Int() : b["crewChief"].Int(), c.Staff[CrewRole.PitCrew].Lvl);
            Assert.All(c.Staff.Values, m => { Assert.Equal(0, m.Wage); Assert.Null(m.Trait); Assert.Equal(1, m.Since); });
            Assert.Null(c.Poach);
        }

        [Fact]
        public void The_crew_page_answers_an_offer_and_locks_perks_on_a_second_press()
        {
            var c = Ready();
            c.Money = 100000;
            Chief(c).Lvl = 6; c.SyncCrew();
            c.RollPoach(force: true, role: CrewRole.CrewChief);
            var m = new CareerMenu(c);
            var g = m.Page();
            Assert.False(g.CanPress("go"));
            Assert.Equal("crew", g.Default);
            Assert.True(m.Press("crew"));
            Assert.Equal(CareerScreen.Crew, m.Screen);
            var p = m.Page();
            Assert.Equal("crew.talk", p.Default);
            Assert.NotNull(p.Sections.FirstOrDefault(s => s.Title.StartsWith("Talk them round", StringComparison.Ordinal)));
            Assert.True(m.Press("crew.match"));
            Assert.True(c.Poach.Done);
            // a perk takes two presses
            string id = "crew.perk:CrewChief:0:1";
            Assert.True(m.Page().CanPress(id));
            Assert.True(m.Press(id));
            Assert.Equal(0, Chief(c).Perks[0]);
            Assert.True(m.Press(id));
            Assert.Equal(1, Chief(c).Perks[0]);
            Assert.False(m.Page().CanPress(id));
            Assert.True(m.Press("crew.train:Spotter"));
            Assert.True(m.Press("crew.pitbuy:gas_man"));
            Assert.True(m.Press("crew.pitswap:1"));
            Assert.Equal("gas_man", c.Base.PitDeck[1]);
            Assert.True(m.Press("crew.back"));
            Assert.Equal(CareerScreen.Garage, m.Screen);
            Assert.True(m.Page().CanPress("go"));
        }

        [Fact]
        public void The_menu_bot_runs_a_career_with_Crew_HQ_through_a_season_end()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "linepainter", NewSeed = 21 }, 12, s => Quick(s));
            var c = m.Career;
            Assert.Equal(12, c.Results.Count);
            Assert.All(c.Staff.Values, x => Assert.InRange(x.Lvl, 1, 10));
            Assert.True(c.Poach == null || c.Poach.Done || m.Screen != CareerScreen.Garage || c.CantRace != null);
        }
    }
}
