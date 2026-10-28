using System;
using System.Collections.Generic;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 38: the driver sheet, the d20 check, the twelve off-track events and the season-end negotiation, against
    /// slice v41's driver-js rules. The step is done when each event's math is shown in full and covered by a test:
    /// EveryEventShowsItsMathInFull walks every question of every event, in every race context and with every image.</summary>
    public class DriverTests
    {
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

        static Career Ready(string persona = "drafter", uint seed = 3, string bg = null)
        {
            var c = Career.New(persona, seed: seed, background: bg);
            c.EnterGarage();
            Assert.True(c.SignSponsor(c.SponsorOffers[0].Id));
            return c;
        }

        static CheckCtx Ctx(string kind, string ev) => kind == "dnf"
            ? new CheckCtx { Ev = ev, Fin = 31, Start = 12, Exp = 18, Dnf = true, Dmg = 70, Track = "Millbrook Speedway", TrackId = "mill", Rival = "Cole Mercer" }
            : kind == "big" ? new CheckCtx { Ev = ev, Fin = 2, Start = 15, Exp = 9, Top3 = true, Big = true, Track = "Carver Motor Speedway", TrackId = "carver", Rival = "Cole Mercer", BeatRival = true }
            : new CheckCtx { Ev = ev, Fin = 14, Start = 13, Exp = 12, Dmg = 45, Track = "Talon Ridge Superspeedway", TrackId = "ridge", Rival = "Cole Mercer" };

        static int TotalXp(Driver d) => d.Xp + Enumerable.Range(1, d.Lvl - 1).Sum(DriverRules.XpNeed);

        static OffTrackEvent Put(Career c, string id, CheckCtx x) => c.Event = new OffTrackEvent { Id = id, Season = c.Season, After = c.RaceIdx, Ctx = x, Ceil = 1200 };

        // ================================================================== the check

        [Fact]
        public void Modifiers_are_score_minus_10_halved_rounded_down()
        {
            Assert.Equal(-1, DriverRules.Mod(8));
            Assert.Equal(-1, DriverRules.Mod(9));
            Assert.Equal(0, DriverRules.Mod(10));
            Assert.Equal(0, DriverRules.Mod(11));
            Assert.Equal(1, DriverRules.Mod(12));
            Assert.Equal(2, DriverRules.Mod(15));
            Assert.Equal(5, DriverRules.Mod(20));
        }

        [Fact]
        public void Natural_20_and_1_and_the_mixed_band()
        {
            Assert.Equal(CheckTier.Crit, DriverRules.Tier(20, -10, 40));
            Assert.Equal(CheckTier.Fumble, DriverRules.Tier(1, 15, 2));
            Assert.Equal(CheckTier.Success, DriverRules.Tier(10, 2, 12));
            Assert.Equal(CheckTier.Mixed, DriverRules.Tier(9, 2, 12));   // 11: missed by 1
            Assert.Equal(CheckTier.Mixed, DriverRules.Tier(6, 2, 12));   // 8: missed by 4
            Assert.Equal(CheckTier.Fail, DriverRules.Tier(5, 2, 12));    // 7: missed by 5
        }

        [Fact]
        public void Odds_match_every_pair_of_dice()
        {
            foreach (int mod in new[] { -3, 0, 2, 5, 9 })
                foreach (int dc in new[] { 8, 11, 12, 15, 19, 25 })
                    foreach (RollMode mode in Enum.GetValues(typeof(RollMode)))
                    {
                        var want = new double[5];
                        for (int a = 1; a <= 20; a++)
                            for (int b = 1; b <= 20; b++)
                            {
                                int k = mode == RollMode.Adv ? Math.Max(a, b) : mode == RollMode.Dis ? Math.Min(a, b) : a;
                                want[(int)DriverRules.Tier(k, mod, dc)] += 1 / 400.0;
                            }
                        var got = DriverRules.Odds(mod, dc, mode);
                        for (int t = 0; t < 5; t++) Assert.Equal(want[t], got[t], 12);
                        Assert.Equal(1, got.Sum(), 12);
                    }
            // the plain DC 12 check with no modifier: 5% critical, 40% success, 20% mixed, 30% failure, 5% fumble
            Assert.Equal(new[] { 0.05, 0.40, 0.20, 0.30, 0.05 }, DriverRules.Odds(0, 12, RollMode.Flat).Select(x => Math.Round(x, 4)));
        }

        [Fact]
        public void A_check_lists_every_number_by_its_source()
        {
            var d = Driver.New("rich");
            var b = DriverRules.Finish(DriverRules.Start(d, DSkill.Pitch, 12, null));
            Assert.Equal(new[] { "Presence 15", "Proficiency", "Old Money (trait)" }, b.L.Select(l => l.Src));
            Assert.Equal(new[] { 2, 2, 2 }, b.L.Select(l => l.V));
            Assert.Equal(6, b.Mod);
            Assert.Equal(12, b.Dc);
            Assert.Equal(6, b.Need);
            Assert.Equal(RollMode.Flat, b.Mode);
            // a flaw subtracts, labeled
            var m = DriverRules.Finish(DriverRules.Start(d, DSkill.Media, 12, null));
            Assert.Contains(m.L, l => l.Src == "Silver Spoon (flaw)" && l.V == -1);
            // Need clamps to 2..20: a 1 never makes it, a 20 always does
            Assert.Equal(2, DriverRules.Finish(new CheckBuild { BaseDc = 5, L = { new ModLine(9, "x") } }).Need);
            Assert.Equal(20, DriverRules.Finish(new CheckBuild { BaseDc = 30, L = { new ModLine(0, "x") } }).Need);
        }

        [Fact]
        public void Traits_give_advantage_and_flaws_disadvantage_and_both_cancel()
        {
            var hero = Driver.New("hero");
            var mill = Ctx("dnf", "press");
            var media = DriverRules.Finish(DriverRules.Start(hero, DSkill.Media, 12, mill));
            Assert.Equal(RollMode.Adv, media.Mode);
            Assert.Contains("Hometown Favorite: short-track crowd", media.Adv);
            var cool = DriverRules.Finish(DriverRules.Start(hero, DSkill.Cool, 12, mill));
            Assert.Equal(RollMode.Dis, cool.Mode);
            cool.Adv.Add("Spent 1 Hype");
            Assert.Equal(RollMode.Flat, DriverRules.Finish(cool).Mode);
            // Seen Worse: +2 on Keep Cool after a DNF, Data Brain +1 on reading, Keyboard Warrior at a press scrum
            Assert.Contains(DriverRules.Finish(DriverRules.Start(Driver.New("dirt"), DSkill.Cool, 12, mill)).L, l => l.Src == "Seen Worse (trait)" && l.V == 2);
            Assert.Contains(DriverRules.Finish(DriverRules.Start(Driver.New("sim"), DSkill.Contract, 12, null)).L, l => l.Src == "Data Brain (trait)");
            Assert.Equal(RollMode.Dis, DriverRules.Finish(DriverRules.Start(Driver.New("sim"), DSkill.Cool, 12, Ctx("normal", "press"))).Mode);
            Assert.Equal(RollMode.Adv, DriverRules.Finish(DriverRules.Start(Driver.New("crew"), DSkill.Crew, 12, null)).Mode);
        }

        [Fact]
        public void The_image_map_names_an_image_and_rewards_answering_in_character()
        {
            var d = Driver.New("hero");
            Assert.Null(d.Image);
            d.MoveImage(1, 1);
            Assert.Null(d.Image);
            d.MoveImage(1, 1);
            Assert.Equal("rookie", d.Image);
            Assert.Equal(2, d.Trail.Count);
            // Media is a Cocky Rookie skill: +1 when the answer's tone is cocky or showy, not when it pulls the other way
            Assert.Contains(DriverRules.Start(d, DSkill.Media, 12, null, 1, 1).L, l => l.K == "image" && l.V == 1);
            Assert.Contains(DriverRules.Start(d, DSkill.Media, 12, null, 0, 1).L, l => l.K == "image");
            Assert.DoesNotContain(DriverRules.Start(d, DSkill.Media, 12, null, -1, 1).L, l => l.K == "image");
            Assert.DoesNotContain(DriverRules.Start(d, DSkill.Cool, 12, null, 1, 1).L, l => l.K == "image");
            // a signature answer gets one more once you lean all the way in (5 on both axes)
            Assert.DoesNotContain(DriverRules.Start(d, DSkill.Media, 12, null, 1, 1, "rookie").L, l => l.Src == "Leaning all the way in");
            for (int i = 0; i < 3; i++) d.MoveImage(1, 1);
            Assert.True(d.LeansIn);
            Assert.Contains(DriverRules.Start(d, DSkill.Media, 12, null, 1, 1, "rookie").L, l => l.Src == "Leaning all the way in");
            for (int i = 0; i < 20; i++) d.MoveImage(1, 1);
            Assert.Equal(DriverRules.ImgMax, d.ImgX);
            Assert.Equal(12, d.Trail.Count);
        }

        [Fact]
        public void Levels_give_attribute_points_and_Grit_and_Wits_reach_the_race()
        {
            var c = Ready("bully");
            Assert.Equal("hero", c.Driver.Bg);
            Assert.Equal(c.Driver.Score(DAttr.Grit), c.Loadout().Grit);
            Assert.Equal(new[] { 2 }, c.Driver.AddXp(100));
            Assert.Equal(1, c.Driver.Pts);
            Assert.Equal(new[] { 3, 4 }, c.Driver.AddXp(500));
            Assert.Equal(3, c.Driver.Pts);
            int w = c.Driver.Score(DAttr.Wits);
            Assert.Null(c.RaiseAttr(DAttr.Wits));
            Assert.Equal(w + 1, c.Loadout().Wits);
            Assert.Equal(w + 1, c.Base.Wits);
            c.Driver.Attr[(int)DAttr.Grit] = 20;
            Assert.Equal("Maxed at 20", c.RaiseAttr(DAttr.Grit));
            Assert.Equal(2, c.Driver.Pts);
        }

        [Fact]
        public void A_race_pays_XP_and_Hype_and_spends_the_promised_Momentum()
        {
            var c = Ready();
            c.Driver.NextMom = 2;
            var s = c.NextRace();
            Assert.Equal(2, s.Loadout.DriverMom);
            var r = new Race(s);
            r.Tick();
            Assert.Equal(2, r.Momentum);
            var race = Quick(c.NextRace());
            var res = c.FinishRace(race);
            Assert.Equal(DriverRules.XpRace, res.Xp);
            Assert.Equal(DriverRules.XpRace, c.Driver.Xp);
            Assert.Equal(2, res.DrvMom);
            Assert.Equal(0, c.Driver.NextMom);
            Assert.Equal(DriverRules.RaceHype(res).Count, res.Hype);
            Assert.Equal(res.Hype, c.Driver.Hype);
            Assert.Equal((int)Math.Round(race.Me.Damage), res.Dmg);
            // Hype rules: a x4 combo and a big finish
            Assert.Equal(2, DriverRules.RaceHype(new RaceReport { BestChain = 4, Finish = 2, Expected = 10 }).Count);
            Assert.Equal(new[] { "6 spots over expected" }, DriverRules.RaceHype(new RaceReport { Finish = 8, Expected = 14 }));
            Assert.Empty(DriverRules.RaceHype(new RaceReport { Finish = 2, Expected = 10, Dnf = true }));
        }

        // ================================================================== the events

        [Fact]
        public void There_are_twelve_events_and_each_question_has_answers_with_every_tier_defined()
        {
            Assert.Equal(12, OffTrack.All.Count);
            Assert.Equal(12, OffTrack.All.Select(e => e.Id).Distinct().Count());
            foreach (var E in OffTrack.All)
            {
                Assert.NotEmpty(E.Rounds);
                Assert.False(string.IsNullOrEmpty(E.FumbleText));
                Assert.False(string.IsNullOrEmpty(E.WalkName));
            }
        }

        /// <summary>The step's "done when": for every event, every question, every race context and every image (so every
        /// signature answer), the event page shows each answer's whole check: the skill, every modifier by source and value,
        /// the base DC and what moves it, each advantage and disadvantage reason, the roll needed, the five-tier odds, and what
        /// success, mixed and failure do. Then a roll shows both dice, every line again, the total against the DC, the tier
        /// the numbers give, and what the answer did, and taking it applies exactly that.</summary>
        [Fact]
        public void EveryEventShowsItsMathInFull()
        {
            int checks = 0, rounds = 0;
            var sigs = new HashSet<string>();
            foreach (var E in OffTrack.All)
                foreach (var kind in new[] { "normal", "dnf", "big" })
                    foreach (var image in new[] { null, "rookie", "champ", "fan", "assassin" })
                    {
                        var c = Ready(seed: 7);
                        var m = new CareerMenu(c);
                        if (image != null) { var I = DriverRules.Image(image); c.Driver.ImgX = 3 * I.X; c.Driver.ImgY = 3 * I.Y; }
                        c.Driver.Fans = 64; c.Driver.Respect = 62; c.Sponsor.Trust = 2;
                        var ev = Put(c, E.Id, Ctx(kind, E.Id));
                        Assert.True(m.Press("ev.open"));
                        for (int guard = 0; guard < 6 && !ev.Done; guard++)
                        {
                            var page = m.Page();
                            Assert.Equal(CareerScreen.Event, page.Screen);
                            var r = c.EventRound();
                            Assert.Contains(page.Sections, s => s.Title == r.Q);
                            foreach (var o in r.Opts)
                            {
                                if (o.Sig != null) sigs.Add(E.Id + ":" + o.Id);
                                if (o.Auto) { Assert.True(page.CanPress("ev.opt:" + o.Id)); continue; }
                                var b = c.CheckFor(o);
                                var sec = page.Sections.Single(s => s.Title.StartsWith(o.Name, StringComparison.Ordinal) && s.Items.Any(i => i.Id == "ev.opt:" + o.Id));
                                var rows = sec.Items.Where(i => i.Kind == MenuKind.Row).ToList();
                                var texts = sec.Items.Where(i => i.Kind == MenuKind.Text).Select(i => i.Label).ToList();
                                Assert.Contains(rows, i => i.Label == DriverRules.Name(o.Skill));
                                Assert.Equal("attr", b.L[0].K);
                                foreach (var l in b.L) Assert.Contains(rows, i => i.Label == "+ " + l.Src && i.Value == DriverRules.Sgn(l.V));
                                Assert.Contains(rows, i => i.Label == "vs DC" && i.Value == o.Dc.ToString());
                                foreach (var l in b.Dcl) Assert.Contains(rows, i => i.Label == "DC " + l.Src && i.Value == DriverRules.Sgn(l.V));
                                foreach (var a in b.Adv) Assert.Contains("▲ " + a, texts);
                                foreach (var a in b.Dis) Assert.Contains("▼ " + a, texts);
                                Assert.Equal(b.L.Sum(l => l.V), b.Mod);
                                Assert.Equal(o.Dc + b.Dcl.Sum(l => l.V), b.Dc);
                                Assert.Contains(rows, i => i.Label.EndsWith("roll needed", StringComparison.Ordinal) && i.Value.StartsWith(b.Need.ToString(), StringComparison.Ordinal));
                                Assert.Contains(CareerMenu.OddsText(b.Odds), texts);
                                Assert.Equal(1, b.Odds.Sum(), 9);
                                var t = c.TierFxFor(o);
                                Assert.Contains(rows, i => i.Label == "Success" && i.Value == CareerMenu.FxText(t[(int)CheckTier.Success], ev));
                                Assert.Contains(rows, i => i.Label == "Mixed" && i.Value == CareerMenu.FxText(t[(int)CheckTier.Mixed], ev));
                                Assert.Contains(rows, i => i.Label == "Failure" && i.Value == CareerMenu.FxText(t[(int)CheckTier.Fail], ev));
                                checks++;
                            }
                            rounds++;
                            // answer with the first rolled answer and check the roll's math
                            var pick = r.Opts.First(o => !o.Auto);
                            var before = (fans: c.Driver.Fans, respect: c.Driver.Respect, xp: TotalXp(c.Driver), money: c.Money);
                            var want = c.CheckFor(pick);
                            Assert.True(m.Press("ev.opt:" + pick.Id));
                            var cur = ev.Cur;
                            Assert.NotNull(cur);
                            var R = cur.R;
                            int kept = want.Mode == RollMode.Adv ? Math.Max(R.A, R.B) : want.Mode == RollMode.Dis ? Math.Min(R.A, R.B) : R.A;
                            Assert.Equal(kept, R.Kept);
                            Assert.Equal(R.Kept + want.Mod, R.Total);
                            Assert.Equal(DriverRules.Tier(R.Kept, want.Mod, want.Dc), R.Tier);
                            Assert.Equal(want.Mod, cur.B.Mod);
                            var rp = m.Page();
                            var all = rp.Sections.SelectMany(s => s.Items).ToList();
                            Assert.Contains(all, i => i.Label == "d20" && i.Value == R.Kept.ToString());
                            foreach (var l in cur.B.L) Assert.Contains(all, i => i.Label == "+ " + l.Src && i.Value == DriverRules.Sgn(l.V));
                            Assert.Contains(all, i => i.Value == $"{R.Total} vs {cur.B.Dc}");
                            Assert.Contains(all, i => i.Label.StartsWith(DriverRules.Name(R.Tier), StringComparison.Ordinal));
                            Assert.Contains(all, i => i.Label == "What changes" && i.Value.StartsWith(CareerMenu.FxText(cur.Fx, ev), StringComparison.Ordinal));
                            Assert.NotEmpty(c.EventHelp());
                            var fx = cur.Fx;
                            Assert.Same(fx, cur.Tiers[(int)R.Tier]);
                            Assert.True(m.Press("ev.next"));
                            Assert.Equal(SimMath.Clamp(before.fans + fx.Fans, 0, 100), c.Driver.Fans);
                            Assert.Equal(SimMath.Clamp(before.respect + fx.Respect, 0, 100), c.Driver.Respect);
                            Assert.Equal(Math.Max(0, before.money + fx.Money), c.Money);
                            Assert.Equal(before.xp + (R.Made ? DriverRules.XpMade : DriverRules.XpMissed), TotalXp(c.Driver));
                            Assert.Equal(DriverRules.Name(cur.B.Skill), DriverRules.Name(c.Driver.Log[0].Skill));
                            Assert.Equal(R.Tier, ev.Hist.Last().Tier);
                        }
                        Assert.True(ev.Done);
                        Assert.Equal(CareerScreen.Garage, m.Screen);
                        Assert.Null(c.CantRace);
                    }
            // every signature answer showed up: press round 1, dinner round 2, contract round 2, one per image
            Assert.Equal(12, sigs.Count);
            Assert.True(checks > 500, $"{checks} checks");
            Assert.True(rounds > 200, $"{rounds} rounds");
        }

        [Fact]
        public void The_same_question_rolls_the_same_dice_and_a_reload_keeps_the_roll()
        {
            var c = Ready(seed: 11);
            Put(c, "podcast", Ctx("normal", "podcast"));
            var a = c.Clone(); var b = c.Clone();
            Assert.Null(a.EventChoose("story"));
            Assert.Null(b.EventChoose("nerd"));
            Assert.Equal((a.Event.Cur.R.A, a.Event.Cur.R.B), (b.Event.Cur.R.A, b.Event.Cur.R.B));
            var back = Career.FromJson(JsonValue.Parse(a.ToJson().Write()));
            Assert.Equal(a.Event.Cur.R.Kept, back.Event.Cur.R.Kept);
            Assert.Equal(a.Event.Cur.R.Tier, back.Event.Cur.R.Tier);
            Assert.Equal(a.Event.Cur.B.Mod, back.Event.Cur.B.Mod);
            Assert.Equal(a.ToJson().Write(), back.ToJson().Write());
        }

        [Fact]
        public void Hype_buys_advantage_or_a_reroll_of_a_miss()
        {
            var c = Ready(seed: 5);
            c.Driver.Hype = 2;
            Put(c, "data", Ctx("normal", "data"));
            Assert.Null(c.EventChoose("leaders", hype: true));
            Assert.Equal(1, c.Driver.Hype);
            Assert.Equal(RollMode.Adv, c.Event.Cur.B.Mode);
            Assert.Contains("Spent 1 Hype", c.Event.Cur.B.Adv);
            Assert.Equal(Math.Max(c.Event.Cur.R.A, c.Event.Cur.R.B), c.Event.Cur.R.Kept);
            // force a miss to test the reroll
            var cur = c.Event.Cur;
            cur.R = new CheckRoll { A = 2, B = 2, Kept = 2, Other = 2, Total = 2 + cur.B.Mod, Tier = DriverRules.Tier(2, cur.B.Mod, cur.B.Dc) };
            Assert.False(cur.R.Made);
            Assert.True(c.CanReroll);
            Assert.True(c.EventReroll());
            Assert.Equal(0, c.Driver.Hype);
            Assert.True(cur.Rerolled);
            Assert.Equal(2, cur.R0.Kept);
            Assert.False(c.EventReroll());
            Assert.True(c.EventNext());
            Assert.True(c.Event.Done);
        }

        [Fact]
        public void Go_racing_waits_for_the_event_and_skipping_it_costs_what_it_says()
        {
            var c = Ready(seed: 6);
            int fans = c.Driver.Fans;
            Put(c, "fanday", Ctx("normal", "fanday"));
            Assert.Contains("fan day", c.CantRace);
            var m = new CareerMenu(c);
            Assert.False(m.Page().CanPress("go"));
            Assert.Equal("ev.open", m.Page().Default);
            Assert.True(m.Press("ev.walk"));
            Assert.True(c.Event.Done && c.Event.Walked);
            Assert.Equal(fans - 3, c.Driver.Fans);
            Assert.Null(c.CantRace);
        }

        [Fact]
        public void The_contract_talk_sets_the_salary_from_the_ceiling_and_writes_in_a_clause()
        {
            var c = Ready("drafter", 8);
            var ev = Put(c, "contract", Ctx("normal", "contract"));
            ev.Ceil = 2000;
            // read the room: a success reveals the ceiling and gives advantage on the salary question
            c.ApplyFx(new OffFx { Reveal = true, Adv = true }, ev);
            ev.Ri = 1;
            Assert.Contains("$2,000", c.EventRound().Q);
            Assert.Contains("You know their number", c.CheckFor(c.EventRound().Opts.First(o => o.Id == "ceiling")).Adv);
            c.ApplyFx(new OffFx { Sal = 0.75 }, ev);
            Assert.Equal(1500, c.Contract.Per);
            int hd = c.Base.Car.Handling, pw = c.Base.Car.Power;
            c.ApplyFx(new OffFx { Clause = c.Base.Car.Power <= c.Base.Car.Handling ? "power" : "handling" }, ev);
            Assert.Equal(1, c.Base.Car.Power - pw + c.Base.Car.Handling - hd);
            c.ApplyFx(new OffFx { Clause = "crewChief" }, ev);
            Assert.Equal(2, c.Contract.Clauses.Count);
            // the salary is paid every race
            var res = c.FinishRace(Quick(c.NextRace()));
            Assert.Equal(1500, res.Salary);
            // leverage: averaging 4 spots better than expected takes 2 off every DC
            c.Results.ForEach(r => { r.Expected = r.Finish + 4; r.Dnf = false; });
            ev.Ri = 0;
            var lev = c.CheckFor(c.EventRound().Opts[0]).Dcl;
            Assert.Contains(lev, l => l.V == -2 && l.Src.StartsWith("Leverage", StringComparison.Ordinal));
        }

        [Fact]
        public void Money_on_an_event_scales_with_the_series()
        {
            var k = Ready("brawler", 4);
            Assert.Equal(Series.Kart, k.Series);
            Put(k, "dinner", Ctx("normal", "dinner"));
            k.Event.Ri = 1;
            var act = k.EventRound().Opts.First(o => o.Id == "activate");
            Assert.Equal(150, k.TierFxFor(act)[(int)CheckTier.Success].Per);   // $400 at stock car pay x 0.375
            var s = Ready("drafter", 4);
            Put(s, "dinner", Ctx("normal", "dinner"));
            s.Event.Ri = 1;
            Assert.Equal(400, s.TierFxFor(s.EventRound().Opts.First(o => o.Id == "activate"))[(int)CheckTier.Success].Per);
        }

        [Fact]
        public void Effects_clamp_like_the_slice()
        {
            var c = Ready(seed: 9);
            var ev = Put(c, "press", Ctx("normal", "press"));
            c.ApplyFx(new OffFx { Fans = 500, Respect = -500, Hype = 9, Trust = -9, Money = -99999, Mom = 7 }, ev);
            Assert.Equal(100, c.Driver.Fans);
            Assert.Equal(0, c.Driver.Respect);
            Assert.Equal(DriverRules.HypeMax, c.Driver.Hype);
            Assert.Equal(-2, c.Sponsor.Trust);
            Assert.Equal(0, c.Money);
            Assert.Equal(DriverRules.MomMax, c.Driver.NextMom);
            c.ApplyFx(new OffFx { Per = -99999, Bonus = -99999 }, ev);
            Assert.Equal(500, c.Sponsor.Per);
            Assert.Equal(0, c.Sponsor.Bonus);
            // tier effects: critical doubles a success and adds Hype; mixed adds the cost; fumble adds the event's fumble
            var E = OffTrack.Get("press");
            var o = c.EventRound().Opts.First(x => x.Id == "break");
            Assert.Equal(8, OffTrack.TierFx(o, CheckTier.Crit, E).Respect);
            Assert.Equal(1, OffTrack.TierFx(o, CheckTier.Crit, E).Hype);
            Assert.Equal(2, OffTrack.TierFx(o, CheckTier.Mixed, E).Respect);
            Assert.Equal(-1500, OffTrack.TierFx(o, CheckTier.Fumble, E).Money);
        }

        [Fact]
        public void What_would_have_helped_points_at_the_numbers()
        {
            var d = Driver.New("hero");
            var b = DriverRules.Finish(DriverRules.Start(d, DSkill.Read, 14, null));   // Wits 12: +1
            var R = new CheckRoll { A = 9, B = 15, Kept = 9, Other = 15, Total = 10, Tier = DriverRules.Tier(9, 1, 14) };
            Assert.Equal(CheckTier.Mixed, R.Tier);
            var easy = DriverRules.Finish(DriverRules.Start(d, DSkill.Media, 10, null));
            var h = DriverRules.Help(d, b, R, "Break it down", DSkill.Read, new[] { ("Call your shot", easy) });
            Assert.Equal("Rolled 9 +1 = 10 vs DC 14. Missed by 4.", h[0]);
            Assert.Contains("Wits 12 → 20 (+5) would have made it a success.", h);
            // a failure by 5 is 1 short of a mixed result: proficiency (+2) would have done it
            var b2 = DriverRules.Finish(DriverRules.Start(d, DSkill.Read, 10, null));
            var R3 = new CheckRoll { A = 4, B = 3, Kept = 4, Other = 3, Total = 5, Tier = DriverRules.Tier(4, 1, 10) };
            Assert.Contains("Proficiency in Read the Room (+2) would have made it a mixed result.", DriverRules.Help(d, b2, R3, "x", DSkill.Read, new (string, CheckBuild)[0]));
            Assert.Contains(h, x => x.StartsWith("Advantage would have kept your other die, a 15: success", StringComparison.Ordinal));
            Assert.Contains(h, x => x.StartsWith("Call your shot (Media, needed 6+) would have passed", StringComparison.Ordinal));
            Assert.Contains(h, x => x.StartsWith("Best odds this round: Call your shot", StringComparison.Ordinal));
            Assert.Equal(new[] { "Natural 20. Nothing could beat that." }, DriverRules.Help(d, b, new CheckRoll { Kept = 20, Tier = CheckTier.Crit }, "x", DSkill.Read, new (string, CheckBuild)[0]));
        }

        // ================================================================== the season

        [Fact]
        public void A_season_holds_events_on_its_slots()
        {
            foreach (var (persona, series, slots) in new[] { ("drafter", Series.Stock, new[] { 1, 2, 4, 6, 7, 9 }), ("brawler", Series.Kart, new[] { 1, 2, 3, 5 }) })
            {
                var m = new CareerMenu { PickPersona = persona, NewSeed = 21 };
                var bot = new MenuBot();
                var seen = new List<(int after, string id)>();
                int done = 0, len = CareerRules.Calendar(series).Count;
                for (int i = 0; i < 20000 && done < len; i++)
                {
                    if (m.RaceWanted != null) { m.RaceFinished(Quick(m.TakeRace())); done++; continue; }
                    if (m.Screen == CareerScreen.Event && (seen.Count == 0 || seen[seen.Count - 1].after != m.Career.Event.After)) seen.Add((m.Career.Event.After, m.Career.Event.Id));
                    Assert.True(bot.Step(m));
                }
                Assert.Equal(slots, seen.Select(s => s.after));
                Assert.Equal("press", seen[0].id);
                Assert.Equal("dinner", seen[1].id);
                Assert.Equal("contract", seen[2].id);
                // drawn slots: at most once a season, except the press
                var drawn = seen.Skip(3).Select(s => s.id).Where(id => id != "press").ToList();
                Assert.Equal(drawn.Distinct().Count(), drawn.Count);
                Assert.True(bot.Checks >= slots.Length, $"{bot.Checks} checks");
            }
        }

        [Fact]
        public void A_DNF_calls_a_crew_meeting_or_a_hearing_and_a_win_never_does()
        {
            var c = Ready(seed: 2);
            var dnf = Ctx("dnf", null);
            var win = Ctx("big", null);
            Assert.True(OffTrack.Get("crew").Weight(c, dnf) > 0);
            Assert.True(OffTrack.Get("hearing").Weight(c, dnf) > 0);
            Assert.Equal(0, OffTrack.Get("crew").Weight(c, win));
            Assert.Equal(0, OffTrack.Get("hearing").Weight(c, win));
            Assert.Equal(0, OffTrack.Get("rival").Weight(c, win));   // you beat your rival
            Assert.Null(OffTrack.Get("contract").Weight);              // only on its own slot
            var picks = Enumerable.Range(0, 400).Select(i => OffTrack.Pick(c, dnf, new List<string>(), new SimRng((uint)i + 1))).ToList();
            Assert.Contains("crew", picks);
            Assert.Contains("hearing", picks);
            Assert.DoesNotContain("contract", picks);
            Assert.DoesNotContain("crew", Enumerable.Range(0, 400).Select(i => OffTrack.Pick(c, dnf, new List<string> { "crew" }, new SimRng((uint)i + 1))));
        }

        [Fact]
        public void Season_end_offers_negotiate_once_with_their_math_shown()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "linepainter", NewSeed = 19 }, 5, Quick);
            var bot = new MenuBot();
            for (int i = 0; i < 200 && m.Screen != CareerScreen.Season; i++)
            {
                if (m.RaceWanted != null) { m.RaceFinished(Quick(m.TakeRace())); continue; }
                bot.Step(m);
            }
            Assert.Equal(CareerScreen.Season, m.Screen);
            var c = m.Career;
            var o = c.Offers[0];
            var b = c.NegBuild(o);
            Assert.Equal(10 + o.Rating / 2 + (o.Kind == OfferKind.Promote ? 1 : 0), b.Dc);
            Assert.Equal("Savvy " + c.Driver.Score(DAttr.Savvy), b.L[0].Src);
            var item = m.Page().Find("neg:0");
            Assert.Contains("vs DC " + b.Dc, item.Note);
            Assert.Contains(b.L[0].Src, item.Note);
            int per = o.Per, sign = o.Sign;
            Assert.True(m.Press("neg:0"));
            var n = o.Neg;
            Assert.NotNull(n);
            Assert.Equal(n.Kept + b.Mod, n.Total);
            Assert.Equal(DriverRules.Tier(n.Kept, b.Mod, b.Dc), n.Tier);
            double k = Career.NegFx[(int)n.Tier].k;
            Assert.Equal((int)(SimMath.RoundJs(per * k / 50) * 50), o.Per);
            Assert.Equal((int)(SimMath.RoundJs(sign * k / 50) * 50), o.Sign);
            Assert.False(m.Page().CanPress("neg:0"));
            Assert.Null(c.Negotiate(0));
            Assert.True(m.Press("offer:0"));
            Assert.Equal(o.Per, c.Contract.Per);
            Assert.Empty(c.Contract.Clauses);
        }

        // ================================================================== screens and saves

        [Fact]
        public void The_title_picks_a_background_and_the_sheet_spends_points()
        {
            var m = new CareerMenu { PickPersona = "drafter" };
            Assert.Contains(m.Page().Sections.SelectMany(s => s.Items), i => i.Id == "title.bg:sim" && i.On);
            Assert.True(m.Press("title.bg:rich"));
            Assert.True(m.Press("title.start"));
            var c = m.Career;
            Assert.Equal("rich", c.Driver.Bg);
            Assert.Equal(9, c.Loadout().Grit);
            c.Driver.AddXp(100);
            Assert.True(m.Press("drv"));
            Assert.Equal(CareerScreen.Driver, m.Screen);
            var p = m.Page();
            Assert.Contains(p.Sections, s => s.Title == "Skills");
            Assert.Contains(p.Sections, s => s.Title == "On race day");
            Assert.True(m.Press("drv.up:Savvy"));
            Assert.Equal(15, c.Driver.Score(DAttr.Savvy));
            Assert.False(m.Page().CanPress("drv.up:Grit"));
            Assert.True(m.Press("drv.back"));
            Assert.Equal(CareerScreen.Garage, m.Screen);
        }

        [Fact]
        public void A_version_2_save_gets_its_personas_driver()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Saves", "v2-drafter-garage.json"));
            var g = CareerSave.Read(text);
            Assert.Equal(2, g.LoadedFrom);
            var c = g.Career;
            Assert.Equal("sim", c.Driver.Bg);
            Assert.Equal(c.Base.Grit, c.Driver.Score(DAttr.Grit));
            Assert.Equal(c.Base.Wits, c.Driver.Score(DAttr.Wits));
            Assert.Null(c.Event);
            Assert.Empty(c.EventsHeld);
            Assert.Empty(c.Contract.Clauses);
        }

        [Fact]
        public void Quitting_mid_event_comes_back_to_the_event_with_the_roll_on_the_table()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Saves", "v3-drafter-event-roll-waiting.json"));
            var store = new MemorySaveStore();
            store.Files[SaveSlots.FileName(1)] = text;
            var m = new CareerMenu(new SaveSlots(store));
            Assert.True(m.Load(1));
            Assert.Equal(CareerScreen.Event, m.Screen);
            Assert.NotNull(m.Career.Event.Cur);
            Assert.True(m.Page().CanPress("ev.next"));
        }
    }
}
