using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>One person in a Crew HQ seat (the slice's G.crew[role] plus G.stats[role]): their level, XP toward the next,
    /// trait, wage, the perk picked at each tier, the season they joined, and the season you kept them from a rival.</summary>
    public sealed class CrewMember
    {
        public CrewRole Role;
        public string Name;
        public int Lvl = 3, Xp;
        /// <summary>A trait id from CrewHq.Traits, or null.</summary>
        public string Trait;
        /// <summary>Paid out of every race's payout (0 = a volunteer, or the team pays them).</summary>
        public int Wage;
        /// <summary>The perk at each tier (levels 4, 6 and 8): 0 = not picked yet, 1 = the first option, 2 = the second.</summary>
        public int[] Perks = new int[3];
        public int Since = 1;
        /// <summary>The season you matched a rival's offer or talked them into staying (0 = never). They can't be poached again
        /// that season, and they follow you to a new team.</summary>
        public int Kept;

        public CrewMember Clone() { var m = (CrewMember)MemberwiseClone(); m.Perks = (int[])Perks.Clone(); return m; }
    }

    /// <summary>Someone you can hire into a seat: their level, trait, the fee to bring them in and their wage.</summary>
    public sealed class StaffCandidate
    {
        public CrewRole Role;
        public string Name, Trait;
        public int Lvl, Fee, Wage;
        public StaffCandidate Clone() => (StaffCandidate)MemberwiseClone();
    }

    /// <summary>
    /// A rival team's offer for one of your crew (roadmap step 39). It waits in the garage until you answer: match their wage,
    /// talk them round (a Lead the Crew check, once), or let them go for a release fee. Go racing waits for it.
    /// </summary>
    public sealed class PoachOffer
    {
        public CrewRole Role;
        public string Name;
        /// <summary>The team that wants them, in your series.</summary>
        public int Team, Rating;
        public string TeamName;
        /// <summary>Their wage now, the rival's offer, and what matching costs (more after a failed talk).</summary>
        public int Was, Wage, MatchAt;
        /// <summary>What the rival pays you if you let them go.</summary>
        public int Buyout;
        /// <summary>When it came: the season and the number of races run that season.</summary>
        public int Season, After;
        /// <summary>The Lead the Crew check, once tried (null before).</summary>
        public NegResult Talk;
        public bool Done;
        /// <summary>How it ended, in words (shown in the garage until the next race).</summary>
        public string Outcome;
        public PoachOffer Clone() { var o = (PoachOffer)MemberwiseClone(); o.Talk = Talk?.Clone(); return o; }
    }

    /// <summary>
    /// Crew HQ's numbers (the slice's crew-js, step 19) and the step 39 staff market and poaching rules.
    /// Training costs the driver coaching curve; XP comes from the calls you take (stops and Perfect Stops for the pit crew).
    /// Wages, like other money off the track, scale with the series' pay (board Q125).
    /// </summary>
    public static class CrewRules
    {
        public const int Max = 10;
        public static readonly CrewRole[] Roles = { CrewRole.CrewChief, CrewRole.Spotter, CrewRole.Engineer, CrewRole.PitCrew };
        public static readonly CrewRole[] CallRoles = { CrewRole.CrewChief, CrewRole.Spotter, CrewRole.Engineer };

        /// <summary>Training one level (the slice's CREW_TRAIN).</summary>
        public static int TrainCost(int lvl) => 3000 + 1200 * Math.Max(0, lvl - 3);
        /// <summary>XP to the next level (the slice's CREW_XP).</summary>
        public static int XpNeed(int lvl) => 60 + 20 * lvl;
        /// <summary>A hire's wage at stock car pay (the slice's 250 per level above 2).</summary>
        public static int BaseWage(int lvl, double k) => R50(250 * Math.Max(0, lvl - 2) * k);
        /// <summary>Pit card prices in Crew HQ (Q23).</summary>
        public const int PitUpgrade = 1500, PitSwap = 2000;
        /// <summary>At most this many poaching offers a season.</summary>
        public const int PoachMax = 2;

        public static int R50(double v) => (int)(SimMath.RoundJs(v / 50) * 50);

        public static readonly string[] Names = { "Dale Pruitt", "Rita Okafor", "Hank Lowery", "Mo Delgado", "Teddy Vance", "Jo Whitlock", "Cal Brennan", "Nia Foster", "Gus Harlan", "Shay Moreno", "Boone Ritter", "Lena Park", "Wes Calloway", "Dot Pickens", "Ray Tillman", "Kit Sorensen" };

        public static string Name(CrewRole r) => RadioCalls.Who(r);
        public static string Text(CrewRole r) =>
            r == CrewRole.Spotter ? "Racing calls: lines, blocks and moves." :
            r == CrewRole.Engineer ? "Car calls: brake bias, pressures, engine maps." :
            r == CrewRole.PitCrew ? "Over the wall: pit card odds and the release window at your stops." :
            "Strategy and tire calls on the radio. Shorter pit lane times.";

        public static CrewHq.Perk PerkOf(CrewRole r, int tier, int pick) => pick == 1 ? CrewHq.Perks[r][tier].a : pick == 2 ? CrewHq.Perks[r][tier].b : null;
        public static int PerkLevel(CrewRole r, int tier) => CrewHq.Perks[r][tier].a.Lvl;

        /// <summary>Traits a seat can have: call traits for the radio seats, pit traits for the pit crew.</summary>
        public static List<string> TraitsFor(CrewRole r) => CrewHq.Traits.Values.Where(t => r == CrewRole.PitCrew ? !t.Calls : !t.Pit).Select(t => t.Id).ToList();

        /// <summary>
        /// Candidates (the slice's rollHire): 3 between races, each at your level plus 0 to 2, with a trait; the fee is $1,200
        /// plus 80% of the training it saves you. The off-season staff market (before race 1 of season 2 on) lists 6, one or
        /// more per seat, up to 3 levels better, and their fees are a quarter off because their contracts are up.
        /// </summary>
        public static List<StaffCandidate> RollHire(SimRng rng, IReadOnlyDictionary<CrewRole, CrewMember> staff, bool market, double k)
        {
            var roles = Roles.ToList();
            rng.Shuffle(roles);
            if (market) roles = Roles.Concat(roles.Take(2)).ToList();
            else roles = roles.Take(3).ToList();
            var used = new HashSet<string>(staff.Values.Select(m => m.Name));
            var o = new List<StaffCandidate>();
            foreach (var role in roles)
            {
                int cur = staff[role].Lvl, lvl = (int)SimMath.Clamp(cur + rng.Range(0, market ? 4 : 3), 3, Max);
                var traits = TraitsFor(role);
                var tr = traits[rng.Range(0, traits.Count)];
                double fee = 1200;
                for (int l = cur; l < lvl; l++) fee += TrainCost(l) * 0.8;
                if (market) fee *= 0.75;
                var names = Names.Where(n => !used.Contains(n)).ToList();
                var name = names.Count > 0 ? names[rng.Range(0, names.Count)] : Names[rng.Range(0, Names.Length)];
                used.Add(name);
                o.Add(new StaffCandidate { Role = role, Lvl = lvl, Trait = tr, Name = name, Fee = (int)(SimMath.RoundJs(fee / 100) * 100), Wage = BaseWage(lvl, k) });
            }
            return o;
        }

        /// <summary>The chance a rival comes for someone after a race: 8% for a level 5, +4% per level above, +5% after a top-5
        /// finish (your crew looks good on TV). Nobody below level 5 gets offers.</summary>
        public static double PoachChance(int lvl, bool top5) => lvl < 5 ? 0 : Math.Min(0.4, 0.08 + 0.04 * (lvl - 5) + (top5 ? 0.05 : 0));

        /// <summary>A rival's wage offer: 30% over what they're worth or paid now (whichever is more), +5% per team rating
        /// point over 5, and at least $100 more than they get.</summary>
        public static int PoachWage(CrewMember m, int rating, double k)
        {
            int worth = Math.Max(m.Wage, BaseWage(m.Lvl, k));
            return Math.Max(m.Wage + 100, R50(worth * (1.3 + 0.05 * Math.Max(0, rating - 5))));
        }

        /// <summary>The Lead the Crew check to keep them: DC 10 + half the rival's rating, −1 if they've been with you more
        /// than a season, +1 if the offer is half again their wage or more.</summary>
        public const int TalkDc = 10;

        public static readonly (string txt, bool keeps)[] TalkFx =
        {
            ("Critical: they stay at their wage, and for good.", true), ("Success: they stay for half the raise.", true),
            ("Mixed: they're torn. Match or let them go.", false), ("Failure: they're insulted. Matching now costs 10% more.", false),
            ("Fumble: they walk out today.", false),
        };
    }

    // Crew HQ in the career (roadmap step 39): the four seats with level, XP, trait, wage and perks; training, hiring and the
    // off-season staff market; pit card upgrades and the pit card dealer; crew XP and wages after each race; rival teams'
    // offers for your crew (match, talk them round, or let them go); and staff who follow you to a new team. Rolls come
    // from generators seeded by the career's seed and where it is in the career, so the career's own generator (and with it
    // the card and part rewards) rolls exactly as before.
    public sealed partial class Career
    {
        /// <summary>The four seats. Base's crew levels and perks are rebuilt from them (SyncCrew).</summary>
        public Dictionary<CrewRole, CrewMember> Staff = new Dictionary<CrewRole, CrewMember>();
        /// <summary>Who you can hire right now, and the race they were rolled for (season × 1000 + race).</summary>
        public List<StaffCandidate> Hire = new List<StaffCandidate>();
        int hireFor = -1;
        /// <summary>A rival's offer for one of your crew, waiting or answered (null when none came after the last race).</summary>
        public PoachOffer Poach;
        /// <summary>Poaching offers this season.</summary>
        public int Poached;

        CrewMember Member(CrewRole r) => Staff[r];
        public bool Market => RaceIdx == 0 && Season > 1;
        public int Wages => Staff.Values.Sum(m => m.Wage);

        /// <summary>A name not on your crew, from the career's seed and a salt (no draw from the career's generator).</summary>
        string FreshName(uint salt)
        {
            var used = new HashSet<string>(Staff.Values.Select(m => m.Name));
            var pool = CrewRules.Names.Where(n => !used.Contains(n)).ToList();
            if (pool.Count == 0) pool = CrewRules.Names.ToList();
            return pool[(int)((Seed * 2654435761u ^ salt) % (uint)pool.Count)];
        }

        /// <summary>A new career's crew (the slice's newCrew): volunteers at the persona's levels, no wages, no perks.</summary>
        void NewStaff()
        {
            int o = (int)(Seed % (uint)CrewRules.Names.Length);
            Staff.Clear();
            for (int i = 0; i < 4; i++)
            {
                var r = CrewRules.Roles[i];
                Staff[r] = new CrewMember { Role = r, Name = CrewRules.Names[(o + i * 5) % CrewRules.Names.Length], Lvl = Base.Level(r), Since = Season };
            }
            SyncCrew();
        }

        /// <summary>Copies the seats into the loadout the race reads: each seat's level, and the perks and traits added up.</summary>
        public void SyncCrew()
        {
            Base.CrewChief = Staff[CrewRole.CrewChief].Lvl;
            Base.Spotter = Staff[CrewRole.Spotter].Lvl;
            Base.Engineer = Staff[CrewRole.Engineer].Lvl;
            Base.PitCrew = Staff[CrewRole.PitCrew].Lvl;
            var p = new CrewPerks();
            foreach (var r in CrewRules.Roles)
            {
                var m = Staff[r];
                for (int t = 0; t < 3; t++)
                    if (m.Perks[t] != 0 && m.Lvl >= CrewRules.PerkLevel(r, t)) p.Add(r, CrewRules.PerkOf(r, t, m.Perks[t]).Fx);
                if (m.Trait != null) p.Add(r, CrewHq.Traits[m.Trait].Fx);
            }
            Base.Crew = p;
        }

        // ------------------------------------------------------------------ training and perks

        public int TrainCost(CrewRole r) => CrewRules.TrainCost(Member(r).Lvl);
        public string CantTrain(CrewRole r) => Member(r).Lvl >= CrewRules.Max ? "Level 10" : Money < TrainCost(r) ? "Not enough money" : null;

        public bool Train(CrewRole r)
        {
            if (CantTrain(r) != null) return false;
            Money -= TrainCost(r);
            Member(r).Lvl++;
            SyncCrew();
            return true;
        }

        /// <summary>Locks in a perk for good: tier 0, 1 or 2 (levels 4, 6, 8), option 1 or 2. Returns why not, or null.</summary>
        public string PickPerk(CrewRole r, int tier, int pick)
        {
            var m = Member(r);
            if (tier < 0 || tier > 2 || (pick != 1 && pick != 2)) return "No such perk";
            if (m.Perks[tier] != 0) return "Already picked";
            if (m.Lvl < CrewRules.PerkLevel(r, tier)) return $"Needs level {CrewRules.PerkLevel(r, tier)}";
            m.Perks[tier] = pick;
            SyncCrew();
            return null;
        }

        /// <summary>Perk choices unlocked and not yet made (the slice's crewReady badge).</summary>
        public int PerksWaiting => CrewRules.Roles.Sum(r => Enumerable.Range(0, 3).Count(t => Member(r).Lvl >= CrewRules.PerkLevel(r, t) && Member(r).Perks[t] == 0));

        // ------------------------------------------------------------------ hiring and the staff market

        /// <summary>This race's candidates (rolled once per race, from the race seed).</summary>
        public List<StaffCandidate> Hires()
        {
            int key = Season * 1000 + RaceIdx;
            if (hireFor != key)
            {
                hireFor = key;
                Hire = CrewRules.RollHire(new SimRng(RaceSeed ^ 0x41E3u), Staff, Market, MoneyK);
            }
            return Hire;
        }

        /// <summary>Hires candidate i: they replace whoever has that seat, at their level, with their trait and wage, and
        /// their perks are picked fresh.</summary>
        public string HireStaff(int i)
        {
            var h = Hires();
            if (i < 0 || i >= h.Count) return "Nobody there";
            var c = h[i];
            if (Money < c.Fee) return "Not enough money";
            if (Poach != null && !Poach.Done && Poach.Role == c.Role) return $"Answer {Poach.TeamName}'s offer first";
            Money -= c.Fee;
            Staff[c.Role] = new CrewMember { Role = c.Role, Name = c.Name, Lvl = c.Lvl, Trait = c.Trait, Wage = c.Wage, Since = Season };
            h.RemoveAt(i);
            SyncCrew();
            return null;
        }

        // ------------------------------------------------------------------ the pit deck

        public string CantUpgradePit(int i)
        {
            if (i < 0 || i >= Base.PitDeck.Count) return "No such card";
            var d = PitCards.Get(Base.PitDeck[i]);
            return d.Up ? "Upgraded" : !PitCards.Exists(d.Id + "+") ? "Can't be upgraded" : Money < CrewRules.PitUpgrade ? "Not enough money" : null;
        }

        /// <summary>Pit card i becomes its + version (+10 odds and bigger gains, or a Setup that installs at Level II).</summary>
        public bool UpgradePit(int i)
        {
            if (CantUpgradePit(i) != null) return false;
            Money -= CrewRules.PitUpgrade;
            Base.PitDeck[i] += "+";
            return true;
        }

        /// <summary>Buys a dealer card in place of pit card i.</summary>
        public bool SwapPit(string id, int i)
        {
            if (!PitCards.Shop.Contains(id) || i < 0 || i >= Base.PitDeck.Count || Money < CrewRules.PitSwap) return false;
            Money -= CrewRules.PitSwap;
            Base.PitDeck[i] = id;
            return true;
        }

        // ------------------------------------------------------------------ after a race

        /// <summary>Crew XP from the race (Mentor and the driver's Rapport add to it), level-ups, and wages out of the payout
        /// (the slice's crewEndRace).</summary>
        void CrewAfterRace(Race race, RaceReport res)
        {
            res.Wages = Wages;
            res.Total -= res.Wages;
            double k = 1 + Base.Crew.Xp + 0.1 * Math.Max(0, Driver.ModOf(DAttr.Rapport));
            foreach (var r in CrewRules.Roles)
            {
                race.CardStats.CrewXp.TryGetValue(r, out int raw);
                int n = (int)SimMath.RoundJs(raw * k);
                if (n == 0) continue;
                res.CrewXp[r] = n;
                var m = Member(r);
                m.Xp += n;
                while (m.Lvl < CrewRules.Max && m.Xp >= CrewRules.XpNeed(m.Lvl)) { m.Xp -= CrewRules.XpNeed(m.Lvl); m.Lvl++; res.CrewUp.Add(r); }
                if (m.Lvl >= CrewRules.Max) m.Xp = Math.Min(m.Xp, CrewRules.XpNeed(CrewRules.Max));
            }
            if (res.CrewUp.Count > 0) SyncCrew();
        }

        // ------------------------------------------------------------------ poaching

        /// <summary>The team that comes for your crew: your rival's team if they're on another one, else the best team in the series.</summary>
        int PoachTeam()
        {
            var riv = RivalId >= 0 ? Field.FirstOrDefault(e => e.Id == RivalId) : null;
            if (riv != null && riv.Team != Team) return riv.Team;
            var t = SeriesTeams;
            return Enumerable.Range(0, t.Length).Where(i => i != Team).OrderByDescending(i => t[i].Rating).ThenBy(i => i).First();
        }

        /// <summary>
        /// After a race (and once more, twice as likely, at the season's end): maybe a rival team offers one of your crew a job.
        /// Who: anyone level 5 or better you haven't already kept this season, weighted by level (the crew chief half again as
        /// likely). At most 2 a season. force (tests) skips the dice and, with role, picks who.
        /// </summary>
        public PoachOffer RollPoach(double boost = 1, bool force = false, CrewRole? role = null)
        {
            if (Poach != null && !Poach.Done) return null;
            if (!force && Poached >= CrewRules.PoachMax) return null;
            var rng = new SimRng(Seed * 2654435761u ^ (uint)(Season * 7919 + RaceIdx * 131) ^ 0xC4E7u);
            var cands = CrewRules.Roles.Select(Member).Where(m => (force || m.Lvl >= 5) && m.Kept != Season && (role == null || m.Role == role)).ToList();
            if (cands.Count == 0) return null;
            var last = Results.Count > 0 ? Results[Results.Count - 1] : null;
            double p = CrewRules.PoachChance(cands.Max(m => m.Lvl), last != null && !last.Dnf && last.Finish <= 5) * boost;
            if (!force && rng.NextDouble() >= p) return null;
            var w = cands.Select(m => Math.Max(1, m.Lvl - 4) * (m.Role == CrewRole.CrewChief ? 1.5 : 1)).ToList();
            double x = rng.NextDouble() * w.Sum(); int i = 0;
            while (i < w.Count - 1 && (x -= w[i]) > 0) i++;
            var who = cands[i];
            int ti = PoachTeam();
            var team = SeriesTeams[ti];
            int wage = CrewRules.PoachWage(who, team.Rating, MoneyK);
            Poach = new PoachOffer
            {
                Role = who.Role, Name = who.Name, Team = ti, TeamName = team.Name, Rating = team.Rating, Was = who.Wage, Wage = wage, MatchAt = wage,
                Buyout = CrewRules.R50(wage * 2), Season = Season, After = SeasonResults.Count,
            };
            Poached++;
            return Poach;
        }

        public string CantMatch => Poach == null || Poach.Done ? "No offer" : null;

        /// <summary>Match the rival: their wage goes up to the offer (more after a failed talk), and they're yours for good.</summary>
        public bool MatchPoach()
        {
            if (CantMatch != null) return false;
            var m = Member(Poach.Role);
            m.Wage = Poach.MatchAt; m.Kept = Season;
            Poach.Done = true;
            Poach.Outcome = $"You matched {Poach.TeamName}: {m.Name} stays for {MoneyText(m.Wage)} a race.";
            return true;
        }

        /// <summary>The Lead the Crew check to keep them without paying the full raise.</summary>
        public CheckBuild TalkBuild()
        {
            var o = Poach; var m = Member(o.Role);
            var b = DriverRules.Start(Driver, DSkill.Crew, CrewRules.TalkDc + o.Rating / 2, null);
            if (Season - m.Since >= 1) b.Dcl.Add(new ModLine(-1, $"with you since season {m.Since}"));
            if (o.Was == 0 || o.Wage >= o.Was * 1.5) b.Dcl.Add(new ModLine(1, o.Was == 0 ? "a paid job after volunteering" : "half again their wage"));
            return DriverRules.Finish(b);
        }

        /// <summary>Talks them round, once. A critical keeps them at their wage, a success for half the raise, a mixed result
        /// leaves the offer standing, a failure makes matching 10% dearer and a fumble sends them to the rival.</summary>
        public NegResult TalkPoach()
        {
            var o = Poach;
            if (o == null || o.Done || o.Talk != null) return null;
            var b = TalkBuild();
            var R = DriverRules.Roll(b, new SimRng(Seed * 2654435761u ^ (uint)(Season * 7919 + RaceIdx * 131) ^ 0x7A1Cu));
            var f = CrewRules.TalkFx[(int)R.Tier];
            o.Talk = new NegResult { Kept = R.Kept, Other = R.Other, Mod = b.Mod, Dc = b.Dc, Total = R.Total, Tier = R.Tier, Txt = f.txt };
            Driver.AddXp(R.Made ? DriverRules.XpMade : DriverRules.XpMissed);
            Driver.Log.Insert(0, new CheckLog { Ev = "Rival's offer", Opt = "Talk them round", Skill = b.Skill, Tier = R.Tier, Txt = $"{R.Kept} {DriverRules.Sgn(b.Mod)} = {R.Total} vs DC {b.Dc}", Season = Season, Race = SeasonResults.Count });
            if (Driver.Log.Count > 8) Driver.Log.RemoveRange(8, Driver.Log.Count - 8);
            var m = Member(o.Role);
            switch (R.Tier)
            {
                case CheckTier.Crit:
                    m.Kept = Season; o.Done = true;
                    o.Outcome = $"{m.Name} turned {o.TeamName} down. Same wage.";
                    break;
                case CheckTier.Success:
                    m.Wage = CrewRules.R50(o.Was + (o.Wage - o.Was) / 2.0); m.Kept = Season; o.Done = true;
                    o.Outcome = $"{m.Name} stays for {MoneyText(m.Wage)} a race, half what {o.TeamName} offered on top.";
                    break;
                case CheckTier.Fail: o.MatchAt = CrewRules.R50(o.Wage * 1.1); break;
                case CheckTier.Fumble: Leave(o); break;
            }
            return o.Talk;
        }

        /// <summary>Lets them go: the rival pays you a release fee, and their assistant steps up two levels lower, perks fresh.</summary>
        public bool LetGo()
        {
            if (Poach == null || Poach.Done) return false;
            Leave(Poach);
            return true;
        }

        void Leave(PoachOffer o)
        {
            var m = Member(o.Role);
            Money += o.Buyout;
            var name = FreshName((uint)(Season * 7919 + RaceIdx * 131) ^ 0xA55Eu);
            Staff[o.Role] = new CrewMember { Role = o.Role, Name = name, Lvl = Math.Max(2, m.Lvl - 2), Since = Season };
            o.Done = true;
            o.Outcome = $"{m.Name} left for {o.TeamName} (they paid you {MoneyText(o.Buyout)}). {name} steps up at level {Staff[o.Role].Lvl}.";
            SyncCrew();
        }

        // ------------------------------------------------------------------ moving teams

        /// <summary>Who comes with you to a team of this rating: anyone you kept from a rival or who has been with you two
        /// seasons or more, if they're at least as good as the new team's own person in that seat. The rest stay behind.</summary>
        public List<(CrewMember m, bool follows, string why)> Followers(int rating)
        {
            int cb = SeasonRules.CrewBase(rating);
            var o = new List<(CrewMember, bool, string)>();
            foreach (var r in CrewRules.Roles)
            {
                var m = Member(r);
                bool loyal = m.Kept > 0 || Season - m.Since >= 2;
                if (!loyal) o.Add((m, false, "not with you long enough"));
                else if (m.Lvl < cb) o.Add((m, false, $"the new team's is level {cb}"));
                else o.Add((m, true, m.Kept > 0 ? "you kept them from a rival" : $"with you since season {m.Since}"));
            }
            return o;
        }

        /// <summary>Seats the new team's crew around the people who follow you. Returns the note for the signing.</summary>
        string MoveCrew(int rating)
        {
            int cb = SeasonRules.CrewBase(rating);
            var f = Followers(rating);
            var come = f.Where(x => x.follows).Select(x => x.m).ToList();
            uint salt = 0;
            foreach (var (m, follows, _) in f)
            {
                if (follows) continue;
                // someone a rival wanted who stays behind anyway: the offer is moot
                if (Poach != null && !Poach.Done && Poach.Role == m.Role) Poach = null;
                Staff[m.Role] = new CrewMember { Role = m.Role, Lvl = cb, Since = Season };
                Staff[m.Role].Name = FreshName(0x7EA4u + ++salt * 977u);
            }
            bool pit = come.Any(m => m.Role == CrewRole.PitCrew);
            if (!pit) Base.PitDeck = new List<string>(PitCards.StartingDeck);
            SyncCrew();
            return come.Count == 0 ? $"Crew level {cb}; perks and pit cards start over."
                : $"{string.Join(", ", come.Select(m => $"{CrewRules.Name(m.Role).ToLowerInvariant()} {m.Name}"))} follow{(come.Count == 1 ? "s" : "")} you; the rest of the crew is the team's, level {cb}{(pit ? "" : ", and pit cards start over")}.";
        }
    }
}
