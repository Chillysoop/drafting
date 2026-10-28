using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>
    /// What an off-track answer does (the slice's fx): fans, garage respect, Hype, money, sponsor trust, the sponsor's per-race
    /// money and goal bonus, a salary as a share of the team's ceiling, Momentum at the next green flag, a contract clause,
    /// advantage on the next question, and learning the team's budget. Money amounts are written at stock car pay and scale
    /// with the series (Career.MoneyFx).
    /// </summary>
    public sealed class OffFx
    {
        public int Fans, Respect, Hype, Money, Trust, Per, Bonus, Mom;
        public double? Sal;
        public string Clause;
        public bool Adv, Reveal;

        public OffFx Clone() => (OffFx)MemberwiseClone();
        public bool Empty => Fans == 0 && Respect == 0 && Hype == 0 && Money == 0 && Trust == 0 && Per == 0 && Bonus == 0 && Mom == 0 && Sal == null && Clause == null && !Adv && !Reveal;

        /// <summary>Numbers add; a salary, clause or flag in b wins.</summary>
        public static OffFx Merge(OffFx a, OffFx b)
        {
            var o = (a ?? new OffFx()).Clone();
            if (b == null) return o;
            o.Fans += b.Fans; o.Respect += b.Respect; o.Hype += b.Hype; o.Money += b.Money; o.Trust += b.Trust; o.Per += b.Per; o.Bonus += b.Bonus; o.Mom += b.Mom;
            if (b.Sal != null) o.Sal = b.Sal;
            if (b.Clause != null) o.Clause = b.Clause;
            o.Adv |= b.Adv; o.Reveal |= b.Reveal;
            return o;
        }

        /// <summary>A critical doubles every number (not the salary share or the flags).</summary>
        public OffFx Times(int k)
        {
            var o = Clone();
            o.Fans *= k; o.Respect *= k; o.Hype *= k; o.Money *= k; o.Trust *= k; o.Per *= k; o.Bonus *= k; o.Mom *= k;
            return o;
        }
    }

    /// <summary>One answer to an off-track question: what you say, the skill it rolls, its DC, its tone on the image map, and
    /// what each tier does. Out (by CheckTier) replaces Win/Cost/Lose when every tier is its own deal (salary talks).</summary>
    public sealed class EvOption
    {
        public string Id, Name, Say, Hit, Miss;
        /// <summary>A signature answer only this image gets.</summary>
        public string Sig;
        public DSkill Skill;
        public int Dc, TX, TY;
        public OffFx Win = new OffFx(), Lose = new OffFx();
        /// <summary>A mixed result's cost (null = the event's).</summary>
        public OffFx Cost;
        public OffFx[] Out;
        /// <summary>No roll: says yes and moves on.</summary>
        public bool Auto;
    }

    public sealed class EvRound
    {
        public string Q;
        public List<EvOption> Opts = new List<EvOption>();
    }

    /// <summary>What the event's situation adds to one answer's check: lines on the die, lines on the DC, reasons for advantage or disadvantage.</summary>
    public sealed class Situation
    {
        public List<ModLine> L = new List<ModLine>(), Dcl = new List<ModLine>();
        public List<string> Adv = new List<string>(), Dis = new List<string>();
    }

    /// <summary>An off-track event: who you're facing, its rounds of questions, what skipping it costs, the default cost of a
    /// mixed result and what a fumble adds, its situation, and when it comes up after a race.</summary>
    public sealed class EventDef
    {
        public string Id, Name, Go, Blurb, WalkName, FumbleText;
        public Func<Career, OffTrackEvent, string> Who;
        public OffFx Walk = new OffFx(), Cost = new OffFx(), Fumble = new OffFx();
        public Func<Career, OffTrackEvent, EvOption, Situation> Sit;
        public List<Func<Career, OffTrackEvent, EvRound>> Rounds = new List<Func<Career, OffTrackEvent, EvRound>>();
        /// <summary>Signature answers by round, one per image.</summary>
        public Dictionary<int, EvOption[]> Sigs = new Dictionary<int, EvOption[]>();
        /// <summary>How likely it is to fill an open slot after this race (0 = it can't). Null = it only comes on its own slot.</summary>
        public Func<Career, CheckCtx, double> Weight;
        /// <summary>It can come up again in the same season.</summary>
        public bool Repeats;
    }

    /// <summary>One answered question, for the garage's summary and the save.</summary>
    public sealed class EvHist
    {
        public int Ri;
        public string Opt, Name;
        public bool Auto;
        public CheckTier Tier;
        public OffFx Fx;
    }

    /// <summary>A rolled answer that hasn't been taken yet: Hype can still reroll a miss. Its effects apply on Next.</summary>
    public sealed class EvPending
    {
        public string Opt, Name, Say, Hit, Miss;
        public int TX, TY;
        public bool Hype, Rerolled;
        public CheckBuild B;
        public CheckRoll R, R0;
        /// <summary>What each tier would do (at this series' pay), so a reroll needs nothing else.</summary>
        public OffFx[] Tiers;
        /// <summary>XP the answer earned (set when it's taken).</summary>
        public int Xp;
        public OffFx Fx => Tiers[(int)R.Tier];
        /// <summary>Only Reveal mode shows the odds after the roll; Feel mode never does.</summary>
        public InfoLevel Info;
    }

    /// <summary>Tonight's event (the slice's G.event): which one, which question, the race it follows, the team's budget
    /// ceiling (contract talks), what you've answered so far, and the answer on the table.</summary>
    public sealed class OffTrackEvent
    {
        public string Id;
        public int Ri, Season, After;
        public CheckCtx Ctx = new CheckCtx();
        public int Ceil;
        public bool Revealed;
        /// <summary>Advantage on this round (a "read the table" answer earned it), or -1.</summary>
        public int AdvFor = -1;
        public List<EvHist> Hist = new List<EvHist>();
        public EvPending Cur;
        public bool Done, Walked;
        public EventDef Def => OffTrack.Get(Id);
    }

    /// <summary>
    /// The twelve off-track events (roadmap step 38). Three are the slice's (press scrum, sponsor dinner, contract talk, with
    /// their signature answers); nine are new for the engine: crew meeting, officials' hearing, fan day, podcast, rival in the
    /// garage, data night, charity gala, commercial shoot and social media storm. A season has a few slots after its races:
    /// the press after race 1, the sponsor's dinner after race 2 and the owner's contract talk mid-season are fixed, and the
    /// other slots draw from the events that fit the last race (a DNF calls a crew meeting or a hearing, a rival who beat
    /// you comes over), each at most once a season except the press.
    /// </summary>
    public static class OffTrack
    {
        static OffFx F(int fans = 0, int respect = 0, int hype = 0, int money = 0, int trust = 0, int per = 0, int bonus = 0, int mom = 0, double? sal = null, string clause = null, bool adv = false, bool reveal = false)
            => new OffFx { Fans = fans, Respect = respect, Hype = hype, Money = money, Trust = trust, Per = per, Bonus = bonus, Mom = mom, Sal = sal, Clause = clause, Adv = adv, Reveal = reveal };

        static EvOption O(string id, string n, string say, DSkill sk, int dc, int tx, int ty, OffFx win, OffFx lose, string hit, string miss, OffFx cost = null, string sig = null)
            => new EvOption { Id = id, Name = n, Say = say, Skill = sk, Dc = dc, TX = tx, TY = ty, Win = win ?? new OffFx(), Lose = lose ?? new OffFx(), Cost = cost, Hit = hit, Miss = miss, Sig = sig };

        /// <summary>A salary answer: each tier sets the salary share (and maybe more).</summary>
        static EvOption Sal(string id, string n, string say, DSkill sk, int dc, int tx, int ty, OffFx crit, OffFx succ, OffFx mixed, OffFx fail, OffFx fumble, string hit, string miss, string sig = null)
            => new EvOption { Id = id, Name = n, Say = say, Skill = sk, Dc = dc, TX = tx, TY = ty, Out = new[] { crit, succ, mixed, fail, fumble }, Hit = hit, Miss = miss, Sig = sig };

        static EvRound Q(string q, params EvOption[] o) => new EvRound { Q = q, Opts = o.ToList() };

        /// <summary>What a tier does (the slice's tierFx): critical = success doubled + 1 Hype, mixed = success + the cost,
        /// fumble = failure + the event's fumble.</summary>
        public static OffFx TierFx(EvOption o, CheckTier t, EventDef e)
        {
            if (o.Out != null) return (o.Out[(int)t] ?? new OffFx()).Clone();
            switch (t)
            {
                case CheckTier.Crit: return OffFx.Merge(o.Win.Times(2), new OffFx { Hype = 1 });
                case CheckTier.Success: return o.Win.Clone();
                case CheckTier.Mixed: return OffFx.Merge(o.Win, o.Cost ?? e.Cost);
                case CheckTier.Fail: return o.Lose.Clone();
                default: return OffFx.Merge(o.Lose, e.Fumble);
            }
        }

        static readonly List<EventDef> all = new List<EventDef>();
        public static IReadOnlyList<EventDef> All => all;

        public static EventDef Get(string id)
        {
            foreach (var e in all) if (e.Id == id) return e;
            throw new KeyNotFoundException("No off-track event " + id);
        }

        /// <summary>The round's question and answers, with your image's signature answer when the round has one.</summary>
        public static EvRound Round(Career c, OffTrackEvent ev)
        {
            var E = ev.Def;
            var r = E.Rounds[ev.Ri](c, ev);
            var im = c.Driver.Image;
            if (im != null && E.Sigs.TryGetValue(ev.Ri, out var sg)) { var s = sg.FirstOrDefault(x => x.Sig == im); if (s != null) r.Opts.Add(s); }
            return r;
        }

        static string LowCar(Career c) => c.Base.Car.Power <= c.Base.Car.Handling ? "power" : "handling";
        static int Trust(Career c) => c.Sponsor?.Trust ?? 0;
        static string Rival(OffTrackEvent ev) => ev.Ctx.Rival ?? "The 14";

        static OffTrack()
        {
            // ------------------------------------------------------------------ 1. press scrum (slice)
            all.Add(new EventDef
            {
                Id = "press", Name = "Press scrum", Who = (c, ev) => "Reporters at the hauler", Go = "Face the press", Repeats = true,
                Blurb = "The reporters want a quote about your last race.",
                WalkName = "Skip the scrum", Walk = F(fans: -3), Cost = F(respect: -2), Fumble = F(money: -1500, respect: -2), FumbleText = "The series fines you $1,500 for the quote.",
                Sit = (c, ev, o) => { var s = new Situation(); if (ev.Ctx.Top3) s.Adv.Add($"Finished P{ev.Ctx.Fin}: the room is yours"); if (ev.Ctx.Dnf) s.Dis.Add("DNF: the room smells blood"); return s; },
                Weight = (c, x) => 1.5,
                Rounds =
                {
                    (c, ev) =>
                    {
                        var x = ev.Ctx;
                        if (x.Dnf) return Q($"\"Out early at {x.Track}. Whose fault was that?\"",
                            O("own", "Own it", "\"That one's on me. I asked too much of the car.\"", DSkill.Cool, 13, -1, -1, F(respect: 4, trust: 1), F(respect: -2, fans: -2), "The story dies down. The garage respects a driver who owns it.", "You snap at a follow-up. That's the clip they run.", cost: F(fans: -2)),
                            O("blame", "Blame the field", $"\"Ask {Rival(ev)}. He knows what he did.\"", DSkill.Media, 11, 1, 1, F(fans: 5), F(respect: -4, trust: -1), "Your fans eat it up.", "It reads as whining. Your sponsor calls.", cost: F(respect: -3)),
                            O("joke", "Joke it off", "\"Hey, I had the best seat in the house for that one.\"", DSkill.Deflect, 14, -1, 1, F(fans: 7), F(trust: -1, fans: -2), "Biggest laugh of the weekend.", "Looks careless to sponsors."));
                        if (x.Big) return Q($"\"P{x.Fin} from P{x.Start}. Where did that come from?\"",
                            O("credit", "Credit the team", "\"This crew gave me a rocket. I just steered.\"", DSkill.Crew, 12, -1, -1, F(respect: 4, mom: 1), F(respect: -1), "The crew hears it and comes back fired up.", "It sounds rehearsed."),
                            O("shot", "Call your shot", "\"Get used to it. Next week we win.\"", DSkill.Media, 14, 1, 1, F(fans: 8, hype: 1), F(fans: -3, respect: -3), "The clip goes around.", "Now you have to back it up, and nobody believes you.", cost: F(respect: -3)),
                            O("thank", "Thank the fans", "\"Y'all were loud in turn 3. I heard you.\"", DSkill.Fans, 12, -1, 1, F(fans: 6), F(fans: -1), "The stands go nuts on the replay.", "Nobody heard it over the generator."));
                        return Q($"\"P{x.Fin} today. Expected more?\"",
                            O("break", "Break it down", "\"We were tight in the center all day. We'll fix it.\"", DSkill.Read, 12, 0, -1, F(respect: 4), F(respect: -1, fans: -1), "Straight answers. The beat writers like you.", "Too much detail. They tune out."),
                            O("subject", "Change the subject", "\"Ask me after next week.\"", DSkill.Deflect, 12, 1, -1, F(fans: 2, respect: 2), F(fans: -2), "They move on to the next driver.", "\"Evasive\" is the word they use."),
                            O("own2", "Own it", "\"Not good enough. That's on me.\"", DSkill.Cool, 11, -1, -1, F(respect: 3, trust: 1), F(respect: -2), "Calm and honest. Your sponsor likes that.", "Your voice cracks. Not a great look."));
                    },
                },
                Sigs =
                {
                    [0] = new[]
                    {
                        O("trash", "Trash-talk the leader", "\"He's been lucky. Next week I'm in his mirror.\"", DSkill.Media, 15, 1, 1, F(fans: 10, hype: 1), F(fans: -4, respect: -4, trust: -1), "The clip runs all week.", "It reads as sour grapes.", cost: F(respect: -4), sig: "rookie"),
                        O("names", "Thank every crew member by name", "\"Mikey, Dee, Jojo, Big Ron...\"", DSkill.Crew, 11, -1, -1, F(respect: 5, mom: 1), F(respect: -1), "The crew will run through a wall for you next week.", "You forget the tire carrier. He noticed.", sig: "champ"),
                        O("sign", "Sign autographs through the scrum", "\"Who's got a Sharpie?\"", DSkill.Fans, 12, -1, 1, F(fans: 9), F(fans: -2, respect: -1), "Every kid at the fence gets a hat.", "The reporters get nothing and say so.", sig: "fan"),
                        O("nocomment", "\"No comment.\"", "Walk past the cameras without breaking stride.", DSkill.Read, 10, 1, -1, F(respect: 4), F(fans: -3), "The garage nods. Results talk.", "\"Arrogant\" is the headline.", sig: "assassin"),
                    },
                },
            });

            // ------------------------------------------------------------------ 2. sponsor dinner (slice)
            all.Add(new EventDef
            {
                Id = "dinner", Name = "Sponsor dinner", Go = "Go to dinner",
                Who = (c, ev) => c.Sponsor != null ? SeasonRules.Sponsor(c.Sponsor.Id).Name + " · marketing VP" : "Your sponsor",
                Blurb = "Your sponsor is buying. Their mood sets your money for the rest of the season.",
                WalkName = "Send your regrets", Walk = F(trust: -1), Cost = F(money: -500), Fumble = F(per: -300), FumbleText = "You knock a full glass into the CEO's lap. −$300 per race.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (Trust(c) >= 2) s.Adv.Add("Sponsor trust +2: they like you");
                    if (Trust(c) <= -2) s.Dis.Add("Sponsor trust −2: they're wary");
                    if (ev.AdvFor == ev.Ri) s.Adv.Add("You read the table");
                    if (c.Driver.Fans >= 60) s.L.Add(new ModLine(1, $"Fans {c.Driver.Fans}: a name they want"));
                    return s;
                },
                Weight = (c, x) => c.Sponsor != null ? 1 : 0,
                Rounds =
                {
                    (c, ev) => Q("\"So, how's the season feeling?\"",
                        O("table", "Read the table", "Ask what they hope to get out of the year, and listen.", DSkill.Read, 12, 0, -1, F(adv: true), F(), "You catch what they really want. Advantage on the next question.", "You misjudge the mood.", cost: F(money: -500)),
                        O("story", "Tell a garage story", "\"So the jack breaks on lap 140...\"", DSkill.Pitch, 12, -1, 1, F(trust: 1, fans: 2), F(trust: -1), "The whole table is laughing.", "The punchline lands on nobody."),
                        O("late", "Stay till the last toast", "Four hours. Every handshake. Every photo.", DSkill.Endure, 11, -1, -1, F(trust: 1), F(), "They notice who stays.", "You yawn during the CEO's speech.")),
                    (c, ev) => Q("\"We're planning next year's budget. Make your case.\"",
                        O("activate", "Pitch a bigger activation", "\"Hood, fire suit, and a show-car tour. Let's go big.\"", DSkill.Pitch, 14, 1, 1, F(per: 400), F(trust: -1), "They raise the per-race deal.", "Too big, too soon."),
                        O("bonus", "Negotiate the goal bonus", "\"Pay me when I hit the goal. Pay me more.\"", DSkill.Negotiate, 14, 1, -1, F(bonus: 3000), F(trust: -1), "They raise the season goal bonus.", "They don't like being pushed."),
                        O("appear", "Offer fan appearances", "\"I'll do every store opening you've got.\"", DSkill.Fans, 12, -1, 1, F(per: 200, fans: 3), F(), "A little more money and a lot more handshakes.", "They already have a guy for that.")),
                },
                Sigs =
                {
                    [1] = new[]
                    {
                        O("promise", "Promise them a win", "\"Put the logo on the hood. I'll put it in victory lane.\"", DSkill.Media, 15, 1, 1, F(per: 600), F(per: -200, trust: -1), "They love the confidence and pay for it.", "They wonder what else you overpromise.", sig: "rookie"),
                        O("believe", "Thank them for believing in you", "\"None of this happens without you.\"", DSkill.Peace, 11, -1, -1, F(trust: 2), F(trust: -1), "Real loyalty, both ways.", "It comes off as flattery.", sig: "champ"),
                        O("fanclub", "Bring the fan club to dessert", "\"Mind if a few friends join us?\"", DSkill.Fans, 13, -1, 1, F(per: 300, fans: 4), F(trust: -1), "The VP takes forty selfies.", "The restaurant asks you all to leave.", sig: "fan"),
                        O("numbers", "Show them the TV numbers", "Slide a one-page report across the table.", DSkill.Negotiate, 13, 1, -1, F(bonus: 4000), F(trust: -1), "Pure business. They raise the goal bonus.", "They don't like being sold to at dinner.", sig: "assassin"),
                    },
                },
            });

            // ------------------------------------------------------------------ 3. contract talk (slice)
            all.Add(new EventDef
            {
                Id = "contract", Name = "Contract talk", Go = "Sit down with the owner",
                Who = (c, ev) => c.TeamName + " · team owner",
                Blurb = "Your team wants to set your salary. Leverage from your results lowers every DC.",
                WalkName = "Keep your current deal", Walk = F(), Cost = F(respect: -2), Fumble = F(respect: -3), FumbleText = "The owner storms out. −3 Respect.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    var R = c.SeasonResults.Where(r => !r.Dnf).ToList();
                    if (R.Count > 0)
                    {
                        double av = R.Average(r => r.Expected - r.Finish);
                        int lev = (int)SimMath.Clamp(Math.Truncate(av / 2), -3, 3);
                        if (lev != 0) s.Dcl.Add(new ModLine(-lev, $"Leverage: you average {Math.Abs(av).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} spots {(av > 0 ? "better" : "worse")} than expected"));
                    }
                    if (c.Driver.Respect >= 60) s.Dcl.Add(new ModLine(-1, $"Garage respect {c.Driver.Respect}"));
                    if (ev.AdvFor == ev.Ri) s.Adv.Add("You know their number");
                    return s;
                },
                Rounds =
                {
                    (c, ev) => Q("\"Let's talk about the rest of the year, and next year.\"",
                        O("room", "Read the room", "Let them talk first. Watch the spreadsheet.", DSkill.Read, 12, 1, -1, F(reveal: true, adv: true), F(), "You catch their budget ceiling and go in with advantage.", "They give nothing away."),
                        O("shop", "Talk about the shop", "\"The guys have been working nights. That's why we're fast.\"", DSkill.Crew, 11, -1, -1, F(respect: 3), F(), "The owner warms up.", "They want to talk money, not people."),
                        O("fine", "Read the fine print", "Ask for last year's contract and go line by line.", DSkill.Contract, 12, 0, -1, F(reveal: true), F(), "Page 4 shows what they can pay.", "Twelve pages of nothing.")),
                    (c, ev) => Q(ev.Revealed ? $"\"So what do you want a race?\" (Their ceiling: {Career.MoneyText(ev.Ceil)}.)" : "\"So what do you want a race?\"",
                        Sal("ceiling", "Ask for the ceiling", "\"Pay me what the car's worth.\"", DSkill.Negotiate, 15, 1, -1, F(sal: 1.15, hype: 1), F(sal: 1), F(sal: 0.75, respect: -2), F(sal: 0.35, respect: -2), F(sal: 0, respect: -4), "They pay the top of the range.", "They counter low and won't move."),
                        Sal("fair", "Ask for a fair raise", "\"Something that keeps the lights on.\"", DSkill.Negotiate, 11, -1, -1, F(sal: 0.8), F(sal: 0.65), F(sal: 0.5), F(sal: 0.3), F(sal: 0.15), "Fair is fair.", "They hear \"cheap\" and run with it."),
                        Sal("results", "Let the results talk", "Slide the season results across the desk. Say nothing.", DSkill.Cool, 12, 1, -1, F(sal: 0.95, respect: 4), F(sal: 0.7, respect: 3), F(sal: 0.55), F(sal: 0.3), F(sal: 0.2, respect: -2), "The numbers speak.", "You fidget. They notice.")),
                    (c, ev) => Q("\"Anything else before we shake on it?\"",
                        O("car", "Ask for a car upgrade", $"\"Give me a better {(LowCar(c) == "power" ? "engine" : "chassis")} and I'll give you better finishes.\"", DSkill.Pitch, 13, 0, 1, F(clause: LowCar(c)), F(), "Written in: the shop upgrades your car.", "\"Next year, maybe.\""),
                        O("chief", "Bring in a better crew chief", "\"I want the guy from the 14 on my box.\"", DSkill.Crew, 13, -1, -1, F(clause: "crewChief"), F(), "Written in: a better crew chief.", "They like the chief you have."),
                        O("parts", "Ask for a parts budget", "\"Four grand for parts, up front.\"", DSkill.Negotiate, 12, 1, -1, F(money: 4000), F(), "Cash for the parts dealer.", "\"We'll see.\""),
                        new EvOption { Id = "shake", Name = "Shake on it", Say = "No extras. Sign and go racing.", Auto = true, TX = -1, TY = -1 }),
                },
                Sigs =
                {
                    [1] = new[]
                    {
                        Sal("walkout", "Threaten to walk", "\"Three teams called me this week.\"", DSkill.Deflect, 16, 1, 1, F(sal: 1.4, hype: 1), F(sal: 1.25), F(sal: 0.9, respect: -3), F(sal: 0.2, respect: -4), F(sal: 0, respect: -6), "They blink first.", "They call your bluff.", "rookie"),
                        Sal("offer", "Mention another offer", "Leave a rival team's letterhead on the desk.", DSkill.Negotiate, 14, 1, -1, F(sal: 1.3), F(sal: 1.15), F(sal: 0.85), F(sal: 0.4, respect: -2), F(sal: 0.1, respect: -4), "Business is business. They match it.", "They know the letter is old.", "assassin"),
                        Sal("teamfirst", "\"Pay the crew first.\"", "\"Take care of the guys. I'll take what's left.\"", DSkill.Crew, 12, -1, -1, F(sal: 0.9, respect: 6, mom: 1), F(sal: 0.75, respect: 5), F(sal: 0.6, respect: 3), F(sal: 0.4, respect: 2), F(sal: 0.3), "The whole shop hears about it.", "They take you at your word.", "champ"),
                        Sal("merch", "Bring the merch numbers", "\"My shirts outsell the whole team's.\"", DSkill.Fans, 13, -1, 1, F(sal: 1.2, fans: 4), F(sal: 1), F(sal: 0.8), F(sal: 0.45), F(sal: 0.3, fans: -3), "Hard to argue with sales.", "They point out the shirts were on sale.", "fan"),
                    },
                },
            });

            // ------------------------------------------------------------------ 4. crew meeting (new)
            all.Add(new EventDef
            {
                Id = "crew", Name = "Crew meeting", Go = "Hold the meeting",
                Who = (c, ev) => c.TeamName + " · the race shop",
                Blurb = "A rough race. The crew wants to hear from the driver before they start on next week's car.",
                WalkName = "Let the crew chief handle it", Walk = F(respect: -2), Cost = F(respect: -1), Fumble = F(respect: -3), FumbleText = "The tire changer walks out mid-sentence. −3 Respect.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (c.Driver.Respect >= 60) s.L.Add(new ModLine(1, $"Garage respect {c.Driver.Respect}"));
                    if (ev.Ctx.Dnf && o.Skill == DSkill.Media) s.Dis.Add("They just spent all night fixing your wreck");
                    return s;
                },
                Weight = (c, x) => x.Dnf || x.Fin >= x.Exp + 3 ? 3 : 0,
                Rounds =
                {
                    (c, ev) => Q(ev.Ctx.Dnf ? "The car is in pieces in the bay. Nobody is talking." : $"P{ev.Ctx.Fin}, expected P{ev.Ctx.Exp}. The crew wants to know what happened.",
                        O("blame", "Take the blame", "\"That's on me, not the car. You gave me enough.\"", DSkill.Crew, 11, -1, -1, F(respect: 5, mom: 1), F(respect: -1), "Shoulders drop. They go back to work for you.", "It sounds like a speech you've given before."),
                        O("data", "Walk them through the data", "Put the lap traces on the big screen, corner by corner.", DSkill.Read, 12, 0, -1, F(respect: 3, mom: 1), F(respect: -1), "Everyone sees the same problem. Easy fix.", "You lose them at the shock graphs."),
                        O("night", "Stay all night with them", "Grab a wrench. Nobody goes home alone.", DSkill.Endure, 12, -1, 0, F(respect: 4), F(), "At 3 a.m. the car is fixed and you're one of them.", "You fall asleep on the tool chest."),
                        O("fire", "Fire them up", "\"Next week we make 'em pay. Who's with me?\"", DSkill.Media, 14, 1, 1, F(mom: 2, respect: 2), F(respect: -4), "The shop roars. They'll run through a wall.", "Silence. Somebody coughs.", cost: F(respect: -3))),
                },
            });

            // ------------------------------------------------------------------ 5. officials' hearing (new)
            all.Add(new EventDef
            {
                Id = "hearing", Name = "Officials' hearing", Go = "Go to the hauler",
                Who = (c, ev) => "Series officials · the red hauler",
                Blurb = "The officials want to talk about the contact in your last race. A bad answer costs a fine.",
                WalkName = "Accept the fine", Walk = F(money: -1000), Cost = F(money: -500), Fumble = F(money: -1500, respect: -2), FumbleText = "They add a fine for attitude. −$1,500, −2 Respect.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (c.Driver.Respect >= 60) s.L.Add(new ModLine(1, $"Garage respect {c.Driver.Respect}"));
                    if (ev.Ctx.Dmg >= 60) s.Dcl.Add(new ModLine(1, $"Heavy damage ({ev.Ctx.Dmg}%): it looked bad on TV"));
                    return s;
                },
                Weight = (c, x) => x.Dnf || x.Dmg >= 40 ? 3 : 0,
                Rounds =
                {
                    (c, ev) => Q($"\"The tape shows contact at {ev.Ctx.Track}. Explain yourself.\"",
                        O("rule", "Quote the rulebook", "\"Section 10.4: incidental contact. Page 61.\"", DSkill.Contract, 13, 0, -1, F(respect: 2), F(money: -1000), "They check. You're right. Case closed.", "You quote last year's rulebook."),
                        O("calm", "Walk them through it", "Calm, lap by lap, with your spotter's notes.", DSkill.Cool, 12, -1, -1, F(respect: 2), F(money: -1000, respect: -1), "Racing deal. No fine.", "Your voice rises on the replay. Fined."),
                        O("sorry", "Apologize to the other team", "\"I'll go see their crew chief myself.\"", DSkill.Peace, 11, -1, 0, F(respect: 3), F(money: -1000), "The other team speaks up for you.", "They don't buy it either.", cost: F(money: -500)),
                        O("track", "Blame the track", "\"That patch in turn 3 has been loose all year.\"", DSkill.Deflect, 14, 1, 0, F(fans: 3), F(money: -1500, respect: -2), "They send a crew to look at turn 3.", "Fined, and now it's a headline.")),
                },
            });

            // ------------------------------------------------------------------ 6. fan day (new)
            all.Add(new EventDef
            {
                Id = "fanday", Name = "Fan day", Go = "Head to the fan zone",
                Who = (c, ev) => "The fan zone · autograph line",
                Blurb = "The line wraps around the souvenir trailers. Two hours until the gates close.",
                WalkName = "Skip it", Walk = F(fans: -3), Cost = F(fans: -2), Fumble = F(fans: -4), FumbleText = "A clip of you yawning at a kid goes around. −4 Fans.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (ev.Ctx.Top3) s.Adv.Add($"Fresh off P{ev.Ctx.Fin}: they came to see you");
                    if (ev.Ctx.Dnf) s.Dis.Add("DNF: half the line went home");
                    if (c.Driver.Fans >= 60 && o.Skill == DSkill.Fans) s.L.Add(new ModLine(1, $"Fans {c.Driver.Fans}: they know your name"));
                    return s;
                },
                Weight = (c, x) => x.Top3 || x.Big ? 2 : 1,
                Rounds =
                {
                    (c, ev) => Q("Two hundred people, one Sharpie, and a sponsor booth that wants you too.",
                        O("every", "Sign for every last fan", "Nobody leaves without a hat.", DSkill.Endure, 13, -1, 1, F(fans: 8), F(fans: -1), "The last kid in line gets the pen too.", "Security cuts the line at half."),
                        O("stage", "Grab the mic on stage", "\"Who wants to see a burnout?\"", DSkill.Media, 13, 1, 1, F(fans: 6, hype: 1), F(fans: -2), "The whole fan zone chants your number.", "The mic cuts out. Awkward silence.", cost: F(respect: -2)),
                        O("kids", "Run a kids' pit-stop race", "Four tires, plastic wrenches, a stopwatch.", DSkill.Fans, 11, -1, 1, F(fans: 5, respect: 1), F(), "The local news runs it at six.", "A dad argues with the stopwatch."),
                        O("booth", "Work the sponsor's booth", "Hand out samples and say their name a lot.", DSkill.Pitch, 12, 0, -1, F(trust: 1, fans: 2), F(trust: -1), "The VP sees the crowd at their booth.", "You get the slogan wrong twice.")),
                },
            });

            // ------------------------------------------------------------------ 7. podcast (new)
            all.Add(new EventDef
            {
                Id = "podcast", Name = "Podcast interview", Go = "Sit down at the mic",
                Who = (c, ev) => "The Backstretch Pod · two hours, live",
                Blurb = "Two questions, live, no edits. The biggest show in the sport.",
                WalkName = "Cancel the booking", Walk = F(fans: -2), Cost = F(respect: -2), Fumble = F(fans: -3, trust: -1), FumbleText = "You say the quiet part out loud. Your sponsor calls. −3 Fans, trust −1.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (c.Driver.Fans >= 60) s.L.Add(new ModLine(1, $"Fans {c.Driver.Fans}: the host is one of them"));
                    if (ev.AdvFor == ev.Ri) s.Adv.Add("The host is on your side now");
                    return s;
                },
                Weight = (c, x) => 1,
                Rounds =
                {
                    (c, ev) => Q("\"So how did a kid like you end up driving?\"",
                        O("story", "Tell the real story", "The truck, the trailer, the first win.", DSkill.Media, 12, -1, 1, F(fans: 5, adv: true), F(fans: -1), "The host is hooked. Advantage on the next question.", "It runs long. The host checks the clock."),
                        O("joke", "Keep it light", "\"Honestly? I lost a bet.\"", DSkill.Deflect, 11, 1, 1, F(fans: 3), F(), "It's the clip they post.", "Nobody laughs."),
                        O("nerd", "Talk setups for an hour", "Spring rates. Track bar. Cross weight.", DSkill.Read, 12, 0, -1, F(respect: 4), F(fans: -2), "The garage listens. Crew chiefs text you.", "Half the audience leaves.")),
                    (c, ev) => Q("\"Last one. Who's the most overrated driver in the garage?\"",
                        O("names", "Name names", $"\"{Rival(ev)}. Easy.\"", DSkill.Media, 15, 1, 1, F(fans: 8, hype: 1), F(respect: -5, trust: -1), "It's the headline on every site by morning.", "It sounds bitter, and it's everywhere.", cost: F(respect: -4)),
                        O("dodge", "Dodge it", "\"Ask me after the season.\"", DSkill.Deflect, 12, 1, -1, F(respect: 2), F(fans: -2), "Smooth. The garage appreciates it.", "\"Boring answer\" says the chat."),
                        O("praise", "Praise a veteran instead", "\"Nobody's overrated. Hank Doyle taught me half of what I know.\"", DSkill.Peace, 11, -1, -1, F(respect: 4), F(fans: -1), "Hank calls you the next day.", "It sounds like you're dodging.")),
                },
            });

            // ------------------------------------------------------------------ 8. rival in the garage (new)
            all.Add(new EventDef
            {
                Id = "rival", Name = "Rival in the garage", Go = "Hear him out",
                Who = (c, ev) => Rival(ev) + " · your rival last race",
                Blurb = "Your rival finished ahead of you and walks over to your hauler.",
                WalkName = "Walk away", Walk = F(), Cost = F(respect: -1), Fumble = F(respect: -3, fans: -2), FumbleText = "It turns into a shoving match on camera. −3 Respect, −2 Fans.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (c.Driver.Respect >= 60 && (o.Skill == DSkill.Peace || o.Skill == DSkill.Cool)) s.L.Add(new ModLine(1, $"Garage respect {c.Driver.Respect}"));
                    if (ev.Ctx.Dnf && o.Skill == DSkill.Peace) s.Dis.Add("DNF: you're still angry");
                    if (ev.Ctx.Big) s.Adv.Add($"P{ev.Ctx.Fin} from P{ev.Ctx.Start}: he noticed");
                    return s;
                },
                Weight = (c, x) => x.Rival != null && !x.BeatRival ? 2 : 0,
                Rounds =
                {
                    (c, ev) => Q($"{Rival(ev)} leans on your hauler. \"You got something to say about Sunday?\"",
                        O("hand", "Offer a handshake", "\"Good race. Next one's mine.\"", DSkill.Peace, 12, -1, -1, F(respect: 4), F(respect: -1), "He shakes it. The garage saw.", "He leaves you hanging."),
                        O("racing", "Say it's racing", "\"Rubbin's racin'. See you next week.\"", DSkill.Cool, 11, 0, -1, F(respect: 2, mom: 1), F(), "He nods and leaves. You feel looser already.", "He keeps talking. You stop listening."),
                        O("coming", "Tell him you're coming", "\"Check your mirror next week.\"", DSkill.Media, 14, 1, 1, F(fans: 6, mom: 1), F(respect: -3, fans: -1), "The cameras catch all of it. Fans love it.", "He laughs. So does everyone else.", cost: F(respect: -3)),
                        O("ask", "Ask what he saw", "\"Where was I losing you?\"", DSkill.Read, 13, 1, -1, F(mom: 2), F(), "He tells you more than he means to.", "He tells you nothing useful.")),
                },
            });

            // ------------------------------------------------------------------ 9. data night (new)
            all.Add(new EventDef
            {
                Id = "data", Name = "Data night", Go = "Pull up the data",
                Who = (c, ev) => "Your engineer · the sim room",
                Blurb = "The engineer has three hours of data from last week and next week's track on the simulator.",
                WalkName = "Go home and sleep", Walk = F(), Cost = F(respect: -1), Fumble = F(respect: -2), FumbleText = "You fall asleep on the keyboard and wipe the setup file. −2 Respect.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (c.Base.Engineer >= 6) s.L.Add(new ModLine(1, $"Engineer level {c.Base.Engineer}"));
                    return s;
                },
                Weight = (c, x) => 1,
                Rounds =
                {
                    (c, ev) => Q($"Next up: {c.NextTrack.Name}. Where do you start?",
                        O("laps", "Run a hundred sim laps", "Same corner, over and over, until it's boring.", DSkill.Endure, 12, 0, -1, F(mom: 1), F(), "You'll know every bump by the green flag.", "Lap 60 and you're just driving in circles."),
                        O("leaders", "Study what the leaders do", "Overlay your lap on the winner's.", DSkill.Read, 13, 1, -1, F(mom: 2), F(), "There it is: they lift a car length later.", "Too many traces. Nothing stands out."),
                        O("chief", "Bring the crew chief in", "\"Let's build this one together.\"", DSkill.Crew, 12, -1, -1, F(mom: 1, respect: 2), F(respect: -1), "The plan has everyone's fingerprints on it.", "You argue about tire pressures till midnight.")),
                },
            });

            // ------------------------------------------------------------------ 10. charity gala (new)
            all.Add(new EventDef
            {
                Id = "gala", Name = "Charity gala", Go = "Put on the suit",
                Who = (c, ev) => "Children's hospital gala · black tie",
                Blurb = "A room full of sponsors, owners and cameras. They'd love a few words before the auction.",
                WalkName = "Send a signed helmet", Walk = F(fans: -1), Cost = F(money: -500), Fumble = F(fans: -3, trust: -1), FumbleText = "You drop the auction trophy on stage. −3 Fans, sponsor trust −1.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (Trust(c) >= 2 && o.Skill == DSkill.Pitch) s.Adv.Add("Sponsor trust +2: your sponsor bids first");
                    if (c.Driver.Respect >= 60 && o.Skill == DSkill.Peace) s.L.Add(new ModLine(1, $"Garage respect {c.Driver.Respect}"));
                    return s;
                },
                Weight = (c, x) => 1,
                Rounds =
                {
                    (c, ev) => Q("\"Would you say a few words before the auction?\"",
                        O("speech", "Give a heartfelt speech", "About the kid in the hospital bed who sent you a drawing.", DSkill.Media, 13, -1, 1, F(fans: 5, trust: 1), F(fans: -1), "Not a dry eye in the room.", "You lose your place in the notes."),
                        O("auction", "Auction off a ride-along", "\"Three laps at speed. Who's brave?\"", DSkill.Pitch, 12, 1, 1, F(trust: 2), F(trust: -1), "Your sponsor wins the bid and loves the press.", "The bidding stalls at $50."),
                        O("tables", "Work every table", "Every sponsor, every owner, every handshake.", DSkill.Endure, 12, -1, -1, F(trust: 1, respect: 2), F(), "The owners remember your name.", "You run out of small talk by table six."),
                        O("owner", "Sit with a rival team's owner", "\"Mind if I join you?\"", DSkill.Peace, 13, -1, -1, F(respect: 5), F(respect: -1), "By dessert he's talking about next year.", "He talks to his phone all night.")),
                },
            });

            // ------------------------------------------------------------------ 11. commercial shoot (new)
            all.Add(new EventDef
            {
                Id = "shoot", Name = "Commercial shoot", Go = "Go to set",
                Who = (c, ev) => c.Sponsor != null ? SeasonRules.Sponsor(c.Sponsor.Id).Name + " · ad agency" : "Your sponsor's ad agency",
                Blurb = "Your sponsor is shooting a TV spot, and you're the star.",
                WalkName = "Ask to reschedule", Walk = F(trust: -1), Cost = F(fans: -1), Fumble = F(trust: -1, per: -200), FumbleText = "Take 47. The director walks off. −$200 per race.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (Trust(c) >= 2) s.Adv.Add("Sponsor trust +2: they wrote it for you");
                    if (Trust(c) <= -2) s.Dis.Add("Sponsor trust −2: the agency wanted someone else");
                    return s;
                },
                Weight = (c, x) => c.Sponsor != null ? 1 : 0,
                Rounds =
                {
                    (c, ev) => Q("\"Okay, read the line: 'Nothing gets me to victory lane like...'\"",
                        O("line", "Nail the line", "Big smile, one take.", DSkill.Media, 13, 1, 1, F(per: 300), F(trust: -1), "They want you for the whole campaign.", "Eleven takes. They'll fix it in post."),
                        O("improv", "Improvise something better", "\"...a cold one and a fast car.\"", DSkill.Deflect, 14, 1, 1, F(per: 400, fans: 3), F(trust: -1), "The agency rewrites the spot around it.", "Legal says no. Back to the script."),
                        O("takes", "Ask for twenty takes", "Get it exactly right, however long it takes.", DSkill.Endure, 11, -1, -1, F(trust: 1), F(), "The crew stays late and doesn't mind.", "The light goes, and so does the director.")),
                },
            });

            // ------------------------------------------------------------------ 12. social media storm (new)
            all.Add(new EventDef
            {
                Id = "storm", Name = "Social media storm", Go = "Open the app",
                Who = (c, ev) => "Your phone · 4,000 notifications",
                Blurb = "Something you did is all over the internet. Your sponsor is watching how you handle it.",
                WalkName = "Log off for a week", Walk = F(fans: -3), Cost = F(fans: -2), Fumble = F(fans: -5, trust: -1), FumbleText = "You reply at 2 a.m. It's worse. −5 Fans, sponsor trust −1.",
                Sit = (c, ev, o) =>
                {
                    var s = new Situation();
                    if (c.Driver.Fans >= 60 && o.Skill == DSkill.Fans) s.L.Add(new ModLine(1, $"Fans {c.Driver.Fans}: they jump in for you"));
                    if (ev.Ctx.Dnf && o.Skill == DSkill.Cool) s.Dis.Add("The wreck clip is still on loop");
                    return s;
                },
                Weight = (c, x) => x.Dnf ? 2 : 1,
                Rounds =
                {
                    (c, ev) => Q(ev.Ctx.Dnf ? "A clip of your wreck has a million views, and the comments blame you." : "Somebody dug up an old video of you calling the series boring.",
                        O("sorry", "Post an honest apology", "Short, no excuses.", DSkill.Cool, 12, -1, -1, F(fans: 4, trust: 1), F(fans: -2), "It's over by lunch. People respect it.", "\"Fake\" trends for an hour."),
                        O("meme", "Make it a meme", "Post the clip yourself with a better caption.", DSkill.Deflect, 13, 1, 1, F(fans: 7), F(fans: -3), "The internet is on your side now.", "It reads as not caring.", cost: F(trust: -1)),
                        O("live", "Go live with the fans", "Answer every question for an hour.", DSkill.Fans, 12, -1, 1, F(fans: 5), F(fans: -2), "Ten thousand people watch. Most leave happy.", "The trolls show up first."),
                        O("lawyer", "Let the team's PR write it", "A careful statement, run past legal.", DSkill.Contract, 11, 0, -1, F(trust: 1), F(fans: -2), "Boring, safe, and your sponsor loves it.", "It sounds like a robot wrote it.")),
                },
            });
        }

        // ------------------------------------------------------------------ the schedule

        /// <summary>The races after which an event comes (races done this season), and which slots are fixed. Karts (6 races)
        /// get 4, late models (8) 5, stock cars (10) 6, like the slice's six in ten.</summary>
        public static (int after, string fixedId)[] Slots(Series s)
        {
            switch (s)
            {
                case Series.Kart: return new[] { (1, "press"), (2, "dinner"), (3, "contract"), (5, (string)null) };
                case Series.Late: return new[] { (1, "press"), (2, "dinner"), (4, "contract"), (5, (string)null), (7, (string)null) };
                default: return new[] { (1, "press"), (2, "dinner"), (4, "contract"), (6, (string)null), (7, (string)null), (9, (string)null) };
            }
        }

        /// <summary>Which event fills an open slot: the ones that fit the last race, weighted, minus the ones held this season
        /// (the press can come back). Null when nothing fits.</summary>
        public static string Pick(Career c, CheckCtx x, ICollection<string> held, SimRng rng)
        {
            var pool = all.Where(e => e.Weight != null && (e.Repeats || !held.Contains(e.Id))).Select(e => (e.Id, w: e.Weight(c, x))).Where(p => p.w > 0).ToList();
            double tot = pool.Sum(p => p.w);
            if (tot <= 0) return null;
            double r = rng.NextDouble() * tot;
            foreach (var p in pool) { if ((r -= p.w) < 0) return p.Id; }
            return pool[pool.Count - 1].Id;
        }
    }
}
