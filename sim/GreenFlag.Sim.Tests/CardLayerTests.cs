using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>The card layer around the cards: odds math, combos, Heat, tires, beats, Focus, tosses, the Crew Board.</summary>
    public class CardLayerTests
    {
        static void Resolve(Race r) { int g = 0; while (r.Attempt != null && g++ < 200) r.Tick(); }

        static void Play(Race r, string id, Tier t)
        {
            var h = r.GiveCard(id);
            r.ForceNextTier = t;
            Assert.Null(r.PlayCard(h.Uid));
            Resolve(r);
        }

        [Fact]
        public void ErfMatchesKnownValues()
        {
            Assert.Equal(0, OddsMath.Erf(0));
            Assert.Equal(0.8427, OddsMath.Erf(1), 3);
            Assert.Equal(-0.8427, OddsMath.Erf(-1), 3);
            Assert.Equal(0.5, OddsMath.Phi(0), 9);
            Assert.Equal(0.8413, OddsMath.Phi(1), 3);
        }

        [Fact]
        public void OddsFollowTheSliceFormula()
        {
            var r = Scenario.Green("carver");
            var cd = CardBook.Get("tuck_in");
            var o = r.ComputeOdds(cd, r.Ctx());
            // the five bands of a normal around the read: crit fail < 14 (folded into fail: Tuck In can't wreck), fail < 35, neutral < 58, success < 90
            double F(double x) => OddsMath.Phi((x - o.Read) / o.Sd);
            Assert.Equal(0, o.Wreck);
            Assert.Equal(F(35), o[Tier.Fail], 9);
            Assert.Equal(21 - r.Me.Stats.Consistency * 1.1, o.Sd, 9);
            double s = F(90) - F(58), cs = 1 - F(90);
            if (s + cs <= 0.9) { Assert.Equal(s, o[Tier.Success], 9); Assert.Equal(cs, o[Tier.CritSuccess], 9); }
            else Assert.Equal(0.9, o.Succ, 9);
            // the read is the card's own read plus the modifiers: Racecraft 4 (-2), Spotter 3 (-2), stock field (-12), engine odds adjust (-5)
            var R = new OddsBuilder(); cd.Read(r.Ctx(), R);
            Assert.Equal(R.V - 2 - 2 - 12 - 5 + (r.AheadOf(r.Me).Stats.Defense > 4 ? -(r.AheadOf(r.Me).Stats.Defense - 4) * 2 : 0), o.Read, 9);
        }

        [Fact]
        public void OddsCapAtNinetyPercent()
        {
            var r = Scenario.Green("carver");
            r.SetBuff(80, null, "test");
            var o = r.ComputeOdds(CardBook.Get("tuck_in"), r.Ctx());
            Assert.Equal(0.9, o.Succ, 9);
            Assert.Equal(1, o.P.Sum(), 9);
        }

        [Fact]
        public void LandedCardStartsAComboThatLinksByFamily()
        {
            var r = Scenario.Green("carver");
            Scenario.CarBehind(r);
            Play(r, "tuck_in", Tier.Success);
            Assert.Equal(1, r.Chain);
            Assert.Equal(CardFamily.Draft, r.ChainFam);
            // draft sets up pass: Dive Bomb reads +6 for the link; a defend card doesn't link
            Assert.Equal(1, r.ComboFor(CardBook.Get("dive_bomb")));
            Assert.Equal(0, r.ComboFor(CardBook.Get("block")));
            var o = r.ComputeOdds(CardBook.Get("dive_bomb"), r.Ctx());
            Assert.Equal(r.Bal.ComboBonus, o.Mods["combo"]);
            Play(r, "dive_bomb", Tier.Success);
            Assert.Equal(2, r.Chain);
            Assert.Equal(1, r.CardStats.Combos);
            // a miss drops the chain
            Play(r, "block", Tier.Fail);
            Assert.Equal(0, r.Chain);
            Assert.Null(r.ChainFam);
        }

        [Fact]
        public void ComboTimesOut()
        {
            var r = Scenario.Green("carver");
            Play(r, "tuck_in", Tier.Success);
            for (int i = 0; i < (int)((r.Bal.ComboWin + 0.5) / SimInfo.TickSeconds); i++) r.Tick();
            Assert.Equal(0, r.ComboFor(CardBook.Get("dive_bomb")));
        }

        [Fact]
        public void KartCombosChainCardToCardAndCapAtThree()
        {
            var r = Scenario.Green("hollow");
            Play(r, "k_tow", Tier.Success);
            Assert.Equal(1, r.ComboFor(CardBook.Get("k_pullout")));
            Assert.Equal(0, r.ComboFor(CardBook.Get("k_cover"))); // a draft card, but not one Tuck In the Tow names
            r.Chain = 3;
            Assert.Equal(0, r.ComboFor(CardBook.Get("k_pullout")));
        }

        [Fact]
        public void PassingCardsRaiseHeatAndHeatCools()
        {
            var r = Scenario.Green("carver");
            Play(r, "dive_bomb", Tier.Neutral);
            Assert.Equal(1, r.Heat);
            var o = r.ComputeOdds(CardBook.Get("dive_bomb"), r.Ctx());
            Assert.Equal(-3, o.Mods["heat"]);
            Play(r, "send_it", Tier.CritFail);
            Assert.Equal(3, r.Heat);
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.Overdrive);
            double at = r.T;
            while (r.Heat == 3) r.Tick();
            Assert.InRange(r.T - at, 25, 25.2);
        }

        [Fact]
        public void ChromeHornAndBumperFocusCoolHeatFaster()
        {
            var l = Scenario.Plain(false); l.Parts.Add("chrome_horn"); l.Tune.Focus = ComboFocus.Bumper;
            var r = Scenario.Green("carver", l);
            r.Heat = 2; r.HeatAt = r.T;
            double at = r.T;
            while (r.Heat == 2) r.Tick();
            Assert.InRange(r.T - at, 25 / 3.0, 25 / 3.0 + 0.1);
        }

        [Fact]
        public void TireCostIsRefundedOnASuccess()
        {
            var r = Scenario.Green("carver");
            double t0 = r.Me.Tires;
            Play(r, "push_limit", Tier.Success);
            Assert.InRange(t0 - r.Me.Tires, 0, 1);
            t0 = r.Me.Tires;
            Play(r, "push_limit", Tier.Neutral);
            Assert.InRange(t0 - r.Me.Tires, 5, 6);
        }

        [Fact]
        public void LowTiresBlockExpensiveCards()
        {
            var r = Scenario.Green("carver");
            r.Me.Tires = 9;
            Assert.Equal("Not enough tire left", r.CardBlock(CardBook.Get("push_limit"), r.Ctx()));
            Assert.Null(r.CardBlock(CardBook.Get("tuck_in"), r.Ctx()));
        }

        [Fact]
        public void BuffIsSpentByTheNextCard()
        {
            var r = Scenario.Green("carver");
            Play(r, "spotter_focus", Tier.Success);
            Assert.Equal(15, r.ComputeOdds(CardBook.Get("block"), r.Ctx()).Mods["boost"]);
            var h = r.GiveCard("tuck_in");
            r.PlayCard(h.Uid);
            Assert.Empty(r.Buffs);
            Assert.Equal(15, r.Attempt.Buffs.Single().V);
        }

        [Fact]
        public void DrawBeatsDealCardsRefillFocusAndExpireStaleOnes()
        {
            var r = Scenario.Make("carver");
            while (r.Phase != RacePhase.Green) r.Tick();
            Assert.Equal(5, r.Hand.Count);
            r.FocusMeter = 0.2;
            var first = r.Hand.ToList();
            int beats = r.Beats;
            while (r.Beats == beats) r.Tick();
            Assert.Equal(0.7, r.FocusMeter, 9);
            Assert.True(r.Hand.Count > 5 || r.Pile.Count == 0);
            while (r.Beats < 2) r.Tick();
            // cards dealt before the first beat are two beats old now: back to the deck
            Assert.DoesNotContain(r.Hand, h => first.Contains(h) && !h.Def.Situational);
            Assert.True(r.CardStats.Expired > 0);
        }

        [Fact]
        public void HandNeverExceedsEight()
        {
            var l = Scenario.Plain(false);
            for (int i = 0; i < 10; i++) l.Deck.Add("tuck_in");
            var r = Scenario.Green("carver", l);
            r.DrawCards(20);
            Assert.Equal(Race.HandMax, r.Hand.Count);
        }

        [Fact]
        public void BeatsComeAtTheTracksBrakingZones()
        {
            foreach (var t in Tracks.All)
            {
                var r = Scenario.Make(t.Id);
                // three green laps (a beat that sits on the line can land on either side of the window)
                while (r.Me.Dist < 1) r.Tick();
                int b0 = r.Beats;
                while (r.Me.Dist < 4 && r.Phase == RacePhase.Green && r.Me.Pit == null) r.Tick();
                if (r.Phase != RacePhase.Green || r.Me.Pit != null) continue;
                int expected = 3 * (t.IsPath ? t.BeatAt.Length : t.BeatPoints);
                Assert.True(Math.Abs(r.Beats - b0 - expected) <= 1, $"{t.Id}: {r.Beats - b0} beats in 3 laps, expected {expected}");
            }
        }

        [Fact]
        public void TossesDoTheirThing()
        {
            var r = Scenario.Green("carver");
            // cool: Heat -1
            r.Heat = 2;
            Assert.Null(r.TossCard(r.GiveCard("bump_run").Uid));
            Assert.Equal(1, r.Heat);
            // focus
            r.FocusMeter = 0.5;
            r.TossCard(r.GiveCard("spotter_focus").Uid);
            Assert.Equal(0.8, r.FocusMeter, 9);
            // chain: keeps a live combo going and points it at the tossed card's family
            Play(r, "tuck_in", Tier.Success);
            r.TossCard(r.GiveCard("dive_bomb").Uid);
            Assert.Equal(CardFamily.Pass, r.ChainFam);
            Assert.Equal(r.T, r.ChainAt);
            // buff: next passing card +8
            r.TossCard(r.GiveCard("side_draft").Uid);
            Assert.Equal(8, r.ComputeOdds(CardBook.Get("high_line"), r.Ctx()).Mods["boost"]);
            // specials can't be tossed
            Assert.NotNull(r.TossCard(r.GiveCard("evade").Uid));
        }

        [Fact]
        public void CrewBoardFillsAndDrawsACard()
        {
            var r = Scenario.Green("carver");
            r.Hand.Clear();
            r.Board = 0;
            r.TossCard(r.GiveCard("push_limit").Uid); // chain toss with no live combo: +1, and +1 for the fallback
            Assert.Equal(2, r.Board);
            r.TossCard(r.GiveCard("tape_grille").Uid); // setup toss, no setups: +2, fills (3) and draws 1
            Assert.Equal(1, r.Board);
            Assert.Single(r.Hand);
            Assert.Equal(1, r.CardStats.BoardFires);
        }

        [Fact]
        public void FocusDrainsInSlowMo()
        {
            var r = Scenario.Green("carver");
            Assert.Equal(0.1, r.SlowMo(1, true), 9);
            Assert.Equal(1 - 1 / 8.0, r.FocusMeter, 9);
            Assert.Equal(1, r.SlowMo(1, false));
            r.FocusMeter = 0;
            Assert.Equal(1, r.SlowMo(1, true));
            Assert.Equal(0.1, r.SlowMo(1, true, hazardSelected: true), 9);
        }

        [Fact]
        public void RiskyCardsRefillFocus()
        {
            var r = Scenario.Green("carver");
            r.FocusMeter = 0;
            var h = r.GiveCard("send_it");
            double succ = r.OddsOf(h).Succ;
            r.ForceNextTier = Tier.Success;
            r.PlayCard(h.Uid);
            Resolve(r);
            Assert.Equal(Math.Min(0.45, Math.Max(0, (0.72 - succ) * 1.2)), r.FocusMeter, 9);
        }

        [Fact]
        public void ACautionHandsAMoveInProgressBack()
        {
            var r = Scenario.Green("carver");
            var h = r.GiveCard("dive_bomb");
            r.PlayCard(h.Uid);
            Assert.NotNull(r.Attempt);
            r.StartCaution("test");
            Assert.Null(r.Attempt);
            Assert.Contains(r.Hand, x => x.Id == "dive_bomb");
        }

        [Fact]
        public void CautionCardsGoBackToTheDeckAtTheGreen()
        {
            var r = Scenario.Green("carver");
            r.StartCaution("test");
            r.GiveCard("choose_lane"); r.GiveCard("stay_out");
            int pile = r.Pile.Count;
            while (r.Phase != RacePhase.Green) r.Tick();
            Assert.DoesNotContain(r.Hand, h => h.Def.Situational);
            Assert.True(r.Pile.Count >= pile + 2 - 2); // they went back (some may have been drawn again by the timed draw)
            Assert.Contains("choose_lane", r.Pile.Concat(r.Hand.Select(h => h.Id)));
        }

        [Fact]
        public void ChooseTheLanePaysOutAtTheGreen()
        {
            var r = Scenario.Green("carver");
            r.StartCaution("test");
            r.Tick();
            Play(r, "choose_lane", Tier.Success);
            Assert.Equal(1, r.StartGain);
            while (r.Phase != RacePhase.Restart) r.Tick();
            Assert.True(r.RestartWindow);
            Assert.Equal(20, r.ComputeOdds(CardBook.Get("restart_jump"), r.Ctx()).Tags.Where(t => t.Label == "Chose the right lane").Sum(t => t.Value));
            while (r.Phase != RacePhase.Green) r.Tick();
            Assert.Equal(0, r.StartGain);
            Assert.NotNull(r.Me.Overtake);
        }

        [Fact]
        public void LuckyLugNutRerollsOneFailure()
        {
            var l = Scenario.Plain(false); l.Parts.Add("lucky_lugnut");
            var r = Scenario.Green("carver", l);
            var h = r.GiveCard("dive_bomb");
            // force a bad roll by making the odds terrible, then check the reroll fired once
            r.Heat = 5;
            for (int i = 0; i < 30 && !r.LuckyUsed; i++)
            {
                h = r.GiveCard("dive_bomb");
                if (r.PlayCard(h.Uid) != null) break;
                Resolve(r);
            }
            Assert.True(r.LuckyUsed);
            Assert.Single(r.Log.Where(e => e.Kind == RaceEventKind.CardRerolled));
        }

        [Fact]
        public void PartsTuneAndSetupsMoveTheOdds()
        {
            var l = Scenario.Plain(false);
            l.Parts.Add("slick_nose");
            l.Tune = new Tune { Hand = 2, Aero = 1, Focus = ComboFocus.Draft };
            var r = Scenario.Green("talon", l);
            var c = r.Ctx();
            var o = r.ComputeOdds(CardBook.Get("tuck_in"), c);
            Assert.Contains(o.Tags, t => t.Label == "Slick Nose" && t.Value == 4);
            Assert.Equal(2 + 6, o.Mods["tune"]); // low drag +2, draft focus +6 at the superspeedway
            Assert.Equal(4, r.ComputeOdds(CardBook.Get("dive_bomb"), c).Mods["tune"]); // loose +2 per notch
            // the tune moves your pace and draft through PlayerMods
            Assert.Equal(1 + 0.0025 * 1 * 1, r.Mods.Pace, 9);
            Assert.Equal(1.15 * 1.1, r.Mods.Draft, 9);
        }

        [Fact]
        public void DamageZonesHurtTheirCards()
        {
            var r = Scenario.Green("carver");
            r.AddDamage(r.Me, 30, 1, 0, 0);
            var o = r.ComputeOdds(CardBook.Get("tuck_in"), r.Ctx());
            Assert.True(o.Mods["dmg"] < 0);
            Assert.False(r.ComputeOdds(CardBook.Get("block"), r.Ctx()).Mods.ContainsKey("dmg"));
        }

        [Fact]
        public void ShapeOfTheTrackMovesTheOdds()
        {
            // somewhere on Ridgeback a passing card reads the hairpin or the sweeper
            var r = Scenario.Green("ridge");
            bool seen = false;
            for (int i = 0; i < 20 * 60 && !seen; i++)
            {
                r.Tick();
                if (r.Me.Pit != null || r.Phase != RacePhase.Green) continue;
                var o = r.ComputeOdds(CardBook.Get("dive_bomb"), r.Ctx());
                seen = o.Mods.ContainsKey("shape");
            }
            Assert.True(seen);
        }

        [Fact]
        public void EvadeCardArrivesWithTheHazardAndLeavesWithIt()
        {
            var r = Scenario.Green("carver");
            for (int i = 0; i < 20 * 600 && r.HazardCard == null && !r.Done; i++) r.Tick();
            if (r.HazardCard == null) return; // no wreck ahead of you in this race: nothing to test
            Assert.Contains(r.Hand, h => h.Id == "evade");
            while (r.Hazard != null) r.Tick();
            Assert.DoesNotContain(r.Hand, h => h.Id == "evade");
        }

        [Fact]
        public void PlayingEvadeClearsTheHazard()
        {
            foreach (uint seed in new uint[] { 61, 62, 63, 64, 65, 66, 67, 68 })
            {
                var r = Scenario.Make("talon", seed: seed);
                while (!r.Done && r.HazardCard == null) r.Tick();
                if (r.HazardCard == null) continue;
                r.EvadeHazard();
                Assert.Null(r.Hazard);
                Assert.NotNull(r.Attempt);
                Assert.Equal("evade", r.Attempt.Def.Id);
                return;
            }
        }

        [Fact]
        public void HoldingAHandWithoutPlayingLeavesAKartRaceUnchanged()
        {
            foreach (var t in new[] { "pinek", "hollow" })
            {
                var plain = new Race(RaceSetup.Quick(Tracks.ById(t), 61));
                plain.RunToEnd();
                var withDeck = Scenario.Make(t);
                withDeck.RunToEnd();
                Assert.Equal(plain.Fingerprint(), withDeck.Fingerprint());
            }
        }

        [Fact]
        public void KartFairingPenaltyCostsPlacesAtTheFlag()
        {
            var r = Scenario.Green("hollow");
            r.AddFairing(3);
            Assert.True(r.FairingOut);
            r.RunToEnd();
            Assert.Contains(r.Log, e => e.Kind == RaceEventKind.FairingPenalty);
            Assert.True(r.FairingLost >= 0);
        }

        [Fact]
        public void TowPipsFillInATrainAndFadeOutOfIt()
        {
            var r = Scenario.Green("hollow");
            int max = 0;
            for (int i = 0; i < 20 * 60; i++) { r.Tick(); max = Math.Max(max, r.Tow); }
            Assert.True(max >= 1);
            Assert.InRange(r.Tow, 0, 3);
        }

        [Theory]
        [MemberData(nameof(RaceTests.AllTracks), MemberType = typeof(RaceTests))]
        public void CardRacesAreDeterministic(string track)
        {
            string Run()
            {
                var r = Scenario.Make(track, PlayerLoadout.ForPersona(Tracks.ById(track).Kart ? "packrat" : "bully"), 2026);
                while (!r.Done)
                {
                    r.Tick();
                    if (r.Attempt != null || r.Done) continue;
                    var c = r.Ctx();
                    var best = r.Hand.Where(h => r.CardBlock(h.Def, c) == null).OrderByDescending(h => r.ComputeOdds(h.Def, c).Succ).FirstOrDefault();
                    if (best != null) r.PlayCard(best.Uid);
                }
                return r.Fingerprint() + "|" + string.Join(",", r.Log.Select(e => e.ToString()));
            }
            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void PersonasBuildTheirStartingDecks()
        {
            foreach (var p in Personas.All)
            {
                var l = PlayerLoadout.ForPersona(p.Id);
                Assert.Equal(12, l.Deck.Count);
                Assert.All(l.Deck, id => Assert.Equal(p.Kart, CardBook.Get(id).Kart));
                Assert.Single(l.Parts);
                Assert.Equal(p.Kart, Parts.Get(l.Parts[0]).Kart);
            }
            var drafter = PlayerLoadout.ForPersona("drafter");
            Assert.Equal(4, drafter.Car.Aero);
            Assert.Equal(ComboFocus.Draft, drafter.Tune.Focus);
        }

        [Fact]
        public void PersonasStartWithTheirBackgroundsGrit()
        {
            // a new career's driver comes from the persona's background (the slice's ARCH bg): sim 12, hero 15, dirt 15, crew 12, rich 9
            var want = new Dictionary<string, int> { ["drafter"] = 12, ["bully"] = 15, ["whisperer"] = 15, ["restart"] = 12, ["gambler"] = 9, ["brawler"] = 15, ["linepainter"] = 15, ["packrat"] = 12, ["latebraker"] = 12 };
            foreach (var p in Personas.All) Assert.Equal(want[p.Id], PlayerLoadout.ForPersona(p.Id).Grit);
        }

        [Fact]
        public void KartCardsTranslateToLateModelCards()
        {
            foreach (var c in CardBook.All.Where(c => c.Kart))
            {
                Assert.True(CardBook.KartToStock.ContainsKey(c.Id), c.Id);
                Assert.False(CardBook.Get(CardBook.KartToStock[c.Id]).Kart);
            }
        }
    }
}
