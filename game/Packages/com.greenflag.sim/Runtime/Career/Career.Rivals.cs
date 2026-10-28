using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    // Rival arcs and silly season (roadmap step 43). "The class" is twelve named drivers whose careers run alongside yours:
    // five start where you start, four a rung ahead, three veterans wait in stock cars. Each season they race (in your field
    // when they share your series, simulated from skill and team when they don't), grow, move teams and step up the ladder,
    // so a kart rival can turn up seasons later as your stock car teammate or this week's rival. Each one keeps a Grudge toward
    // you across every series (the slice's step 20 rules, Race.Rivals.cs on track). After a race they send messages to your
    // inbox, and a reply is a d20 check from the driver sheet. In the off-season, silly season reshuffles your field: class
    // drivers come and go, the field's best drivers move to bigger teams, the worst lose their rides to rookies.
    // The class has its own generator (ClassRng), so its rolls never move the career's.
    public sealed partial class Career
    {
        public List<ClassDriver> Class = new List<ClassDriver>();
        public SimRng ClassRng;
        public List<RivalMsg> Inbox = new List<RivalMsg>();
        int msgN;
        /// <summary>The last off-season's silly season, line by line (the season-end screen).</summary>
        public List<string> News = new List<string>();
        /// <summary>Off = no class in the field (harness comparisons). Not saved.</summary>
        public bool RivalsOn = true;

        static readonly string[] RookieNames =
        {
            "Gage Whitfield", "Theo Marchetti", "Jonah Pike", "Ricky Delgado", "Owen Strand", "Cy Harlow", "Mateo Rios", "Beau Tillman", "Kai Brandt", "Lars Engel",
            "Dmitri Volkov", "Sam Okonjo", "Eli Fontaine", "Hugo Lemaire", "Rafa Quintero", "Zane Archer", "Niko Varga", "Ty Bowen", "Ash Corrigan", "Jules Moreno",
            "Pete Lindgren", "Omar Haddad", "Wes Calloway", "Felix Brandon", "Dante Russo", "Cody Marsh", "Ike Nwosu", "Reid Sumner", "Milo Sato", "Abe Kowal",
        };
        static readonly Trait[] RookieTraits = { Trait.Rookie, Trait.Rookie, Trait.Aggressive, Trait.Smooth, Trait.Drafter, Trait.Closer, Trait.Wall, Trait.Restarter };

        public ClassDriver ClassById(string id) => Class.FirstOrDefault(d => d.Id == id);
        public ClassDriver ClassOf(Entrant e) => e?.Rival != null ? ClassById(e.Rival) : null;
        public Entrant SeatOf(ClassDriver d) => Field.FirstOrDefault(e => e.Rival == d.Id);
        /// <summary>Class drivers with a seat in this season's field.</summary>
        public IEnumerable<ClassDriver> InField => Class.Where(d => SeatOf(d) != null);
        public Entrant Mate => Field.FirstOrDefault(e => !e.IsMe && e.Team == Team);
        public double GrudgeOf(string id) => ClassById(id)?.G ?? 0;

        // ------------------------------------------------------------------ the class

        /// <summary>The class at the start of a career: each driver in the series their generation puts them in (a stock car
        /// rookie's own generation is still in late models), on a team that suits their skill (a family team for the heir).
        /// start = your first series.</summary>
        void NewClass(Series start)
        {
            ClassRng = new SimRng(Seed * 2246822519u ^ 0x5EA5C1A5u);
            Class = new List<ClassDriver>();
            foreach (var def in RivalBook.All)
            {
                // your generation starts where you start, except that a stock car rookie finds them a rung below, on the way up
                int you = SeasonRules.Rung(start);
                int rung = Math.Min(Math.Min(def.Gen == 0 ? Math.Min(you, 2) : you + def.Gen, 3), SeasonRules.Rung(def.Cap));
                var s = rung == 1 ? Series.Kart : rung == 2 ? Series.Late : Series.Stock;
                var d = new ClassDriver { Id = def.Id, Series = s, Skill = def.Skill };
                Class.Add(d);
                int want = (int)SimMath.Clamp(Math.Round(def.Skill - 0.5 + ClassRng.RandI(-1, 1)), 3, 9);
                if (def.Money) want = Math.Max(want, 7);
                d.Team = PlaceTeam(d, s, want, -1);
            }
        }

        /// <summary>A team in series s close to rating want with room for d: one class driver a team while there's a team
        /// without one (two at most, and only one on your team, since you have the other seat). In your series, a team one
        /// notch from want can be yours (half the time).</summary>
        int PlaceTeam(ClassDriver d, Series s, int want, int exclude)
        {
            var t = Teams.For(s);
            bool mine = s == Series;
            int Room(int ti, int cap) => (mine && ti == Team ? 1 : cap) - Class.Count(x => x != d && !x.Retired && x.Series == s && x.Team == ti);
            List<int> Order(int cap) => Enumerable.Range(0, t.Length).Where(i => i != exclude && Room(i, cap) > 0).OrderBy(i => Math.Abs(t[i].Rating - want) + i * 0.001).ToList();
            var order = Order(1);
            if (order.Count == 0) order = Order(2);
            if (order.Count == 0) return d.Team;
            if (mine && order.Contains(Team) && Team != exclude && Math.Abs(t[Team].Rating - want) <= 1 && ClassRng.Chance(0.5)) return Team;
            return order[0];
        }

        /// <summary>Puts every class driver of your series in a seat on their team, in place of a field driver.</summary>
        void SeatClass()
        {
            if (!RivalsOn) return;
            foreach (var d in Class.Where(x => !x.Retired && x.Series == Series && SeatOf(x) == null).ToList()) SeatOne(d, null);
        }

        /// <summary>One class driver into the field: the worst-ranked field driver on their team gives up the seat (to another team
        /// with room when theirs has none). Returns the driver who left, or null.</summary>
        string SeatOne(ClassDriver d, List<StandRow> st)
        {
            var t = SeriesTeams;
            Entrant Free(int ti) => Field.Where(e => !e.IsMe && e.Rival == null && e.Team == ti).OrderBy(e => st == null ? -e.Id : -(st.FirstOrDefault(r => r.Id == e.Id)?.Pos ?? 0)).FirstOrDefault();
            var seat = Free(d.Team);
            if (seat == null)
            {
                int ti = PlaceTeam(d, Series, t[d.Team].Rating, d.Team);
                seat = Free(ti);
                if (seat == null) return null;
                d.Team = ti;
            }
            string was = seat.Name;
            Become(seat, d);
            return was;
        }

        /// <summary>An entrant becomes a class driver: name, trait, number, and a car for their team with their skill.</summary>
        void Become(Entrant e, ClassDriver d)
        {
            var def = d.Def;
            e.Name = def.Name; e.Rival = d.Id; e.Trait = def.Trait;
            var clash = Field.FirstOrDefault(x => x != e && x.Number == def.Number);
            if (clash != null) clash.Number = e.Number;
            e.Number = def.Number;
            e.Starts = e.Wins = e.SeasonWins = e.Top5 = 0; e.Best = 99;
            CarFor(e, SeriesTeams[e.Team].Rating, (int)Math.Round(d.Skill));
        }

        /// <summary>A field driver's car for a team (the slice's buildField rolls), with driver skill k.</summary>
        void CarFor(Entrant e, int rating, int k)
        {
            int R(int b) => (int)SimMath.Clamp(b + ClassRng.RandI(-1, 1), 1, 10);
            e.TeamRating = rating;
            e.Stats = new CarStats { Power = R(rating), Handling = R(rating), Aero = R(rating), Durability = R(5), Racecraft = R(k), Defense = R(k), Consistency = R(k), TireMgmt = R(k) };
        }

        /// <summary>A rookie takes over a seat: a new name and number, and a car rolled for the team.</summary>
        void RookieIn(Entrant e)
        {
            var used = new HashSet<string>(Field.Select(x => x.Name));
            var names = RookieNames.Where(n => !used.Contains(n)).ToList();
            e.Name = names.Count > 0 ? names[ClassRng.Range(0, names.Count)] : "Rookie " + e.Id;
            e.Rival = null;
            e.Trait = Kart && ClassRng.Chance(0.15) ? Trait.Rookie : ClassRng.Pick(RookieTraits);
            if (Kart && e.Trait == Trait.Restarter) e.Trait = Trait.Rookie;
            var nums = new HashSet<int>(Field.Select(x => x.Number));
            int n = 2 + ClassRng.Range(0, 97);
            for (int i = 0; i < 100 && (nums.Contains(n) || n == 77); i++) n = n >= 99 ? 2 : n + 1;
            e.Number = n;
            e.Starts = e.Wins = e.SeasonWins = e.Top5 = 0; e.Best = 99;
            int rating = SeriesTeams[e.Team].Rating;
            CarFor(e, rating, (int)SimMath.Clamp(SimMath.RoundJs(rating * 0.6 + ClassRng.Rand(1, 4)) - 1, 2, 9));
        }

        /// <summary>The field for a race carries each class driver's Grudge toward you.</summary>
        void FieldGrudge(List<Entrant> f) { foreach (var e in f) e.Grudge = e.Rival != null ? GrudgeOf(e.Rival) : 0; }

        /// <summary>This week's rival (the slice's rivalPickId): the class driver in the field with the biggest Grudge at 3 or
        /// more, else the one closest to your expected finish (a little ahead beats behind). -1 = no class driver in the field.</summary>
        int ClassRivalPick()
        {
            var ids = Field.Where(e => e.Rival != null && !e.IsMe).ToList();
            if (ids.Count == 0) return -1;
            var hot = ids.Where(e => GrudgeOf(e.Rival) >= 3).OrderByDescending(e => GrudgeOf(e.Rival)).ThenBy(e => e.Id).ToList();
            if (hot.Count > 0) return hot[0].Id;
            var ord = Field.ToList();
            SimMath.StableSort(ord, (a, b) => b.Stats.Rating != a.Stats.Rating ? b.Stats.Rating.CompareTo(a.Stats.Rating) : a.Id.CompareTo(b.Id));
            int mi = ord.FindIndex(e => e.IsMe);
            // nearest by car, pulled toward your own generation (4 places) and the ones you've raced longest (up to 2 more), and
            // nobody twice in a short spell
            double Dist(Entrant e) { int k = ord.IndexOf(e) - mi; var d = ClassById(e.Rival); return (k < 0 ? -k : k + 0.5) - (d.Def.Gen == 0 ? 4 : 0) - Math.Min(d.Ahead + d.Behind, 16) * 0.125; }
            int spell = Math.Min(3, ids.Count - 1);
            var recent = new HashSet<string>(SeasonResults.Skip(Math.Max(0, SeasonResults.Count - spell)).Select(r => r.RvWeek).Where(x => x != null));
            var pool = ids.Where(e => !recent.Contains(e.Rival)).ToList();
            if (pool.Count == 0) pool = ids;
            return pool.OrderBy(Dist).ThenBy(e => e.Id).First().Id;
        }

        /// <summary>The rival objective for this week's rival: beat them, beat them clean (Warm), or beat them and dodge the payback (Hot).</summary>
        string RivalObjective(string id)
        {
            if (id != "rival" || RivalId < 0) return id;
            var e = Field.FirstOrDefault(x => x.Id == RivalId);
            if (e?.Rival == null) return id;
            double g = GrudgeOf(e.Rival);
            return g >= 6 ? "rsurv" : g >= 3 ? "rclean" : "rival";
        }

        // ------------------------------------------------------------------ after a race

        /// <summary>The slice's rivalEndRace: Grudge from the race, head to head, the between-races cooling (unless you wrecked
        /// them today), Respect from a clean race side by side, the proud ones stung by a close loss, their history with you,
        /// and up to two messages for the inbox.</summary>
        void RivalAfterRace(Race race, RaceReport res)
        {
            if (Class.Count == 0) return;
            var fo = race.FinalOrder ?? race.Order;
            var m = race.Me;
            int mi = fo.IndexOf(m);
            var wk = RivalId >= 0 ? race.Cars.FirstOrDefault(c => c.Id == RivalId && c.Ref?.Rival != null) : null;
            bool Hit(RivalIncident x) => x.Kind == "wreck" || x.Kind == "hit" || x.Kind == "payback";
            res.RvWeek = wk?.Ref?.Rival;
            res.RvWreck = wk != null && race.RivalInc.Any(x => x.Car == wk.Id && Hit(x));
            res.RvPaid = wk != null && race.RivalInc.Any(x => x.Car == wk.Id && x.Kind == "payback");
            res.Paybacks = race.Paybacks;
            var msgs = new List<(string kind, ClassDriver d, RivalIncident ev, int pri, int fin)>();
            foreach (var c in race.Cars)
            {
                var d = ClassOf(c.Ref);
                if (d == null || c.IsMe) continue;
                var def = d.Def;
                d.Met = Season;
                var inc = race.RivalInc.Where(x => x.Car == c.Id).ToList();
                int ci = fo.IndexOf(c);
                double g0 = race.RivalG0.TryGetValue(c.Id, out var gg) ? gg : d.G;
                var why = race.RivalWhy.TryGetValue(c.Id, out var w) ? new List<(string, double)>(w) : new List<(string, double)>();
                double g = race.RivalG.TryGetValue(c.Id, out var gl) ? gl : d.G;
                bool both = !m.Dnf && !c.Dnf;
                if (both) { if (mi < ci) d.Ahead++; else d.Behind++; }
                bool wrecked = inc.Any(x => x.Kind == "wreck"), clean = !inc.Any(Hit), close = both && Math.Abs(mi - ci) <= 3;
                if (g > 0 && !wrecked) { double dd = -Math.Min(g, def.Cool); g += dd; why.Add(("cooled off", dd)); }
                bool stung = def.Proud && both && mi < ci && ci - mi <= 3;
                if (clean && close && !stung) { double dd = Math.Max(RivalBook.GMin - g, def.RespectK > 1 ? -1 : -0.5); if (dd != 0) { g += dd; why.Add(("clean race side by side", dd)); } }
                else if (g < 0 && !close) g = Math.Min(0, g + 0.25);
                if (stung) { double dd = Math.Min(RivalBook.GMax - g, def.Mul); g += dd; why.Add(("beaten in a close finish", dd)); }
                d.G = RivalBook.Q(g);
                var ev = inc.FirstOrDefault(x => x.Kind == "wreck") ?? inc.FirstOrDefault(x => x.Kind == "payback") ?? inc.FirstOrDefault(x => x.Kind == "hit");
                if (ev != null) { d.Hist.Insert(0, new RivalHist { Season = Season, Race = RaceIdx + 1, Kind = ev.Kind, Lap = ev.Lap, Track = race.Track.Name }); if (d.Hist.Count > 6) d.Hist.RemoveRange(6, d.Hist.Count - 6); }
                if (g0 != d.G || c.Id == RivalId || inc.Count > 0)
                {
                    var sum = new List<(string why, double d)>();
                    foreach (var (k, v) in why) { int i = sum.FindIndex(s => s.why == k); if (i >= 0) sum[i] = (k, sum[i].d + v); else sum.Add((k, v)); }
                    res.Rivals.Add(new RivalLine { Rid = d.Id, Name = def.Name, G0 = g0, G1 = d.G, Week = c.Id == RivalId, Fin = ci + 1, Why = sum.Where(s => s.d != 0).ToList() });
                }
                string kind = wrecked ? "wrecked" : inc.Any(x => x.Kind == "payback") ? "payback" : inc.Any(x => x.Kind == "hit") ? "hit" : d.G <= -2 && g0 > -2 ? "respect" : null;
                if (kind == null && c.Id == RivalId && both) { if (ci < mi && ci < 10) kind = "gloat"; else if (mi < ci && mi < 5) kind = "beat"; }
                if (kind != null) msgs.Add((kind, d, ev, Array.IndexOf(RivalBook.Kinds, kind), ci + 1));
            }
            foreach (var x in msgs.OrderBy(x => x.pri).ThenBy(x => Array.FindIndex(RivalBook.All.ToArray(), r => r.Id == x.d.Id)).Take(2))
            {
                PostMsg(x.d, x.kind, race.Track.Name, x.ev?.Lap ?? 0, x.fin);
                res.InboxNew++;
            }
        }

        /// <summary>A message from d in your inbox (kind: one of RivalBook.Kinds), newest first; the inbox keeps twelve.</summary>
        public void PostMsg(ClassDriver d, string kind, string track, int lap, int them)
        {
            var (subj, body) = RivalBook.Message(kind, d.Id);
            string Fill(string s) => s.Replace("{lap}", lap.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("{track}", track ?? "")
                .Replace("{me}", Name).Replace("{them}", them.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (track == null && kind == "arrive") { var h = d.Hist.FirstOrDefault(); track = h?.Track ?? "the old days"; }
            Inbox.Insert(0, new RivalMsg { Id = "m" + (++msgN).ToString(System.Globalization.CultureInfo.InvariantCulture), Rid = d.Id, Kind = kind, Season = Season, Race = SeasonResults.Count, Track = track, Subj = Fill(subj), Body = Fill(body), G = d.G });
            if (Inbox.Count > RivalBook.InboxMax) Inbox.RemoveRange(RivalBook.InboxMax, Inbox.Count - RivalBook.InboxMax);
        }

        public int Unanswered => Inbox.Count(x => x.Reply == null);

        // ------------------------------------------------------------------ replies

        /// <summary>The d20 check on a reply: the driver sheet's lines for its skill against its DC (harder for some drivers).</summary>
        public CheckBuild ReplyCheck(RivalMsg msg, RivalReply o)
        {
            var d = ClassById(msg.Rid);
            var ctx = new CheckCtx { Ev = "Rival's message", Rival = d.Def.Name, Track = msg.Track };
            var b = DriverRules.Start(Driver, o.Skill, RivalBook.DcFor(o, d.Def, d.G), ctx, o.TX, o.TY);
            return DriverRules.Finish(b);
        }

        /// <summary>What each tier of a reply does: a critical doubles the success, a mixed result only moves the Grudge, a
        /// fumble doubles the miss. Fans double for drivers who take it to the press. Money at this series' pay.</summary>
        public (OffFx fx, double g) ReplyFx(RivalMsg msg, RivalReply o, CheckTier t)
        {
            var def = ClassById(msg.Rid).Def;
            OffFx fx; double g;
            switch (t)
            {
                case CheckTier.Crit: fx = o.Win.Times(2); g = o.GWin * 2; break;
                case CheckTier.Success: fx = o.Win.Clone(); g = o.GWin; break;
                case CheckTier.Mixed: fx = new OffFx(); g = o.GWin; break;
                case CheckTier.Fail: fx = o.Lose.Clone(); g = o.GLose; break;
                default: fx = o.Lose.Times(2); g = o.GLose * 2; break;
            }
            if (def.Press) fx.Fans *= 2;
            return (MoneyFx(fx), g);
        }

        /// <summary>Sends a reply to a message: the check rolls (seeded by the career and the message), its effects land, the
        /// Grudge moves (gains scaled by how touchy they are), and the driver gets XP and a log line. Returns why not, or null.</summary>
        public string Reply(string msgId, string optId)
        {
            var msg = Inbox.FirstOrDefault(x => x.Id == msgId);
            if (msg == null) return "No such message";
            if (msg.Reply != null) return "Already answered";
            var d = ClassById(msg.Rid);
            var o = RivalBook.RepliesFor(msg.Kind).FirstOrDefault(x => x.Id == optId);
            if (o == null) return "No such answer";
            var b = ReplyCheck(msg, o);
            Driver.MoveImage(o.TX, o.TY);
            var R = DriverRules.Roll(b, new SimRng(Seed * 2654435761u ^ (0x9E91u + Hash(msg.Id) * 31u)));
            var (fx, g) = ReplyFx(msg, o, R.Tier);
            ApplyFx(fx, null);
            if (g != 0) d.G = RivalBook.Q(d.G + (g > 0 ? g * d.Def.Mul : g));
            int xp = R.Made ? DriverRules.XpMade : DriverRules.XpMissed;
            Driver.AddXp(xp);
            Driver.Log.Insert(0, new CheckLog { Ev = "Reply to " + d.Def.Last, Opt = o.Name, Skill = b.Skill, Tier = R.Tier, Txt = $"{R.Kept} {DriverRules.Sgn(b.Mod)} = {R.Total} vs DC {b.Dc}", Season = Season, Race = SeasonResults.Count });
            if (Driver.Log.Count > 8) Driver.Log.RemoveRange(8, Driver.Log.Count - 8);
            msg.Reply = o.Id; msg.Tier = R.Tier;
            msg.Roll = $"{DriverRules.Name(o.Skill)}: d20 {R.Kept} {DriverRules.Sgn(b.Mod)} = {R.Total} vs DC {b.Dc}, {DriverRules.Name(R.Tier)}";
            msg.ReplyText = (R.Made ? o.Hit : R.Tier == CheckTier.Mixed ? "It lands, just about." : o.Miss) + (g != 0 ? $" Grudge {(g > 0 ? "+" : "−")}{RivalBook.Num(Math.Abs(g > 0 ? g * d.Def.Mul : g))}, now {RivalBook.Num(d.G)} ({RivalBook.Tier(d.G)})." : "");
            return null;
        }

        // ------------------------------------------------------------------ silly season

        /// <summary>A class driver's season in a series you aren't in: a finish from skill and team, with luck.</summary>
        int SimRank(ClassDriver d)
        {
            int rating = Teams.For(d.Series)[d.Team].Rating;
            return (int)SimMath.Clamp(Math.Round(10.5 - (d.Skill - 5) * 2 - (rating - 6) * 1.4 + ClassRng.Gauss() * 3), 1, 20);
        }

        /// <summary>
        /// The off-season (run by WrapSeason, before you sign): every class driver's season goes in their path, they grow, the
        /// veterans may retire, and each one may step up a series (P3 or better; P10 or better with skill 5+; the heir from
        /// P12), move to a bigger team (P4 or better) or lose the ride to a smaller one (P15 or worse). The journeyman moves
        /// every winter. Then your field reshuffles: class drivers who left give their seats to rookies, class drivers who
        /// arrived take the worst seat on their team, the best three field drivers on small teams move up, and the two worst
        /// lose their rides. A Grudge with someone who didn't race you this season cools by twice their cooling.
        /// </summary>
        void SillySeason(List<StandRow> st)
        {
            News = new List<string>();
            if (!RivalsOn || Class.Count == 0) return;
            var before = new HashSet<string>(InField.Select(d => d.Id));
            var mateWas = Mate?.Rival;
            var ranks = new Dictionary<string, int>();
            // the seasons away from your field, one finish each with no two class drivers on the same spot in a series
            var away = new Dictionary<string, int>();
            foreach (var g in Class.Where(x => !x.Retired && (x.Series != Series || SeatOf(x) == null)).GroupBy(x => x.Series))
            {
                var taken = new HashSet<int>();
                foreach (var (d, raw) in g.Select(d => (d, SimRank(d))).OrderBy(x => x.Item2).ThenByDescending(x => x.d.Skill).ThenBy(x => x.d.Id).ToList())
                {
                    int r = raw;
                    while (taken.Contains(r) && r < 20) r++;
                    taken.Add(r); away[d.Id] = r;
                }
            }
            foreach (var d in Class.Where(x => !x.Retired))
            {
                var def = d.Def;
                var e = d.Series == Series ? SeatOf(d) : null;
                int rank, wins;
                if (e != null) { rank = st.First(r => r.Id == e.Id).Pos; wins = e.SeasonWins; d.Starts += SeasonResults.Count; }
                else
                {
                    rank = away[d.Id];
                    wins = rank == 1 ? 1 + ClassRng.Range(0, 3) : rank <= 3 && ClassRng.Chance(0.5) ? 1 : 0;
                    d.Starts += CareerRules.Calendar(d.Series).Count;
                }
                ranks[d.Id] = rank;
                d.Wins += wins; d.Seasons++; d.LastRank = rank;
                if (rank == 1) d.Titles++;
                d.Path.Add($"Season {Season}: {SeasonRules.SeriesName(d.Series)}, P{rank} for {Teams.For(d.Series)[d.Team].Name}{(wins > 0 ? $", {wins} win{(wins > 1 ? "s" : "")}" : "")}{(rank == 1 ? " · champion" : "")}");
                if (rank == 1 && d.Series != Series) News.Add($"{def.Name} wins the {SeasonRules.SeriesOne(d.Series)} title.");
                if (d.Met != Season && d.G > 0) d.G = RivalBook.Q(Math.Max(0, d.G - def.Cool * 2));
                d.Skill = Math.Max(1, Math.Min(Math.Max(def.Peak, d.Skill), d.Skill + def.Grow));
            }
            foreach (var d in Class.Where(x => !x.Retired && x.Def.Retire > 0 && x.Seasons >= x.Def.Retire))
            {
                d.Retired = true;
                d.Path.Add($"Retired after season {Season}");
                News.Add($"{d.Def.Name} retires after {d.Seasons} seasons, {d.Wins} wins and {d.Titles} title{(d.Titles == 1 ? "" : "s")}.");
            }
            foreach (var d in Class.Where(x => !x.Retired))
            {
                var def = d.Def; int rank = ranks[d.Id];
                var s0 = d.Series; int t0 = d.Team;
                var t = Teams.For(d.Series);
                int r = t[d.Team].Rating;
                var nx = SeasonRules.Next(d.Series);
                bool up = nx != null && SeasonRules.Rung(nx.Value) <= SeasonRules.Rung(def.Cap) && (rank <= 3 || (rank <= 10 && d.Skill >= 5) || (def.Money && rank <= 12));
                if (up)
                {
                    int want = rank <= 3 ? 5 : 4;
                    if (def.Money) want = Math.Max(want, 7);
                    d.Series = nx.Value;
                    d.Team = PlaceTeam(d, nx.Value, want, -1);
                    News.Add($"{def.Name} (P{rank} in {SeasonRules.SeriesName(s0).ToLowerInvariant()}) steps up to {SeasonRules.SeriesName(nx.Value).ToLowerInvariant()} with {Teams.For(nx.Value)[d.Team].Name}.");
                }
                else
                {
                    int want = r;
                    if (def.Id == "journey") want = r + ClassRng.RandI(-1, 1);
                    else if (rank <= 4 && r < 9) want = Math.Min(9, r + 2);
                    else if (rank >= 15 && r > 3 && !def.Money) want = Math.Max(3, r - 2);
                    if (want != r || def.Id == "journey")
                    {
                        int ti = PlaceTeam(d, d.Series, want, d.Team);
                        if (ti != d.Team)
                        {
                            d.Team = ti;
                            News.Add($"{def.Name} {(t[ti].Rating > r ? "moves up to" : t[ti].Rating < r ? "loses the ride and drops to" : "moves to")} {t[ti].Name}{(d.Series != Series ? $" ({SeasonRules.SeriesName(d.Series).ToLowerInvariant()})" : "")}.");
                        }
                    }
                }
                if (s0 != d.Series || t0 != d.Team) d.Path.Add($"Signed for season {Season + 1}: {Teams.For(d.Series)[d.Team].Name}, {SeasonRules.SeriesName(d.Series).ToLowerInvariant()}");
            }

            // your field
            var teams = SeriesTeams;
            int Pos(Entrant e) => st.FirstOrDefault(x => x.Id == e.Id)?.Pos ?? 21;
            var moved = new HashSet<Entrant>();
            foreach (var e in Field.Where(x => x.Rival != null).ToList())
            {
                var d = ClassOf(e);
                if (d.Retired || d.Series != Series)
                {
                    string who = e.Name;
                    RookieIn(e);
                    moved.Add(e);
                    News.Add($"Rookie {e.Name} takes {who}'s seat at {teams[e.Team].Name}.");
                }
                else if (d.Team != e.Team)
                {
                    var g = Field.Where(x => !x.IsMe && x.Rival == null && x.Team == d.Team).OrderByDescending(Pos).FirstOrDefault();
                    if (g == null) { d.Team = e.Team; continue; }
                    g.Team = e.Team; CarFor(g, teams[g.Team].Rating, g.Stats.Racecraft);
                    e.Team = d.Team; CarFor(e, teams[e.Team].Rating, (int)Math.Round(d.Skill));
                    moved.Add(g); moved.Add(e);
                }
            }
            // the field's best on small teams move up
            var gens = Field.Where(x => !x.IsMe && x.Rival == null && !moved.Contains(x)).OrderBy(Pos).ToList();
            foreach (var e in gens.Where(x => teams[x.Team].Rating <= 6).Take(3).ToList())
            {
                int ti = SeasonRules.TeamAt(Series, Math.Min(9, teams[e.Team].Rating + 2));
                if (ti == e.Team) continue;
                var g = Field.Where(x => !x.IsMe && x.Rival == null && x.Team == ti && !moved.Contains(x)).OrderByDescending(Pos).FirstOrDefault();
                if (g == null) continue;
                News.Add($"{e.Name} (P{Pos(e)}) moves from {teams[e.Team].Name} to {teams[ti].Name}; {g.Name} goes the other way.");
                int from = e.Team;
                e.Team = ti; CarFor(e, teams[ti].Rating, e.Stats.Racecraft);
                g.Team = from; CarFor(g, teams[from].Rating, g.Stats.Racecraft);
                moved.Add(e); moved.Add(g);
            }
            // the two worst lose their rides
            foreach (var e in Field.Where(x => !x.IsMe && x.Rival == null && !moved.Contains(x)).OrderByDescending(Pos).Take(2).ToList())
            {
                string who = e.Name; int p = Pos(e);
                RookieIn(e);
                moved.Add(e);
                News.Add($"{teams[e.Team].Name} drops {who} (P{p}). Rookie {e.Name} gets the seat.");
            }
            // class drivers who arrive in your series
            foreach (var d in Class.Where(x => !x.Retired && x.Series == Series && SeatOf(x) == null).ToList())
            {
                string was = SeatOne(d, st);
                if (was != null) News.Add($"{d.Def.Name} arrives at {teams[d.Team].Name}, taking {was}'s seat.");
            }
            Greet(before, mateWas);
        }

        /// <summary>Messages from old faces: a class driver who raced you before and just arrived in your field, and one who just
        /// became your teammate.</summary>
        void Greet(HashSet<string> before, string mateWas)
        {
            var mate = Mate?.Rival;
            foreach (var d in InField.ToList())
            {
                if (d.Id == mate && mate != mateWas) { PostMsg(d, "mate", null, 0, 0); News.Add($"{d.Def.Name} is your teammate at {TeamName}."); }
                else if (!before.Contains(d.Id) && d.Met > 0) PostMsg(d, "arrive", null, 0, 0);
            }
        }

        /// <summary>Takes the class out of this career (harness comparisons): their seats go to rookies and nothing about rivals
        /// happens again. The rival objective falls back to the old pick.</summary>
        public void ClassOff()
        {
            foreach (var e in Field.Where(x => x.Rival != null)) RookieIn(e);
            Class.Clear(); Inbox.Clear(); News.Clear();
            RivalsOn = false;
            if (Objectives != null) RollObjectives();
        }

        // ------------------------------------------------------------------ copies

        void CloneRivals(Career c)
        {
            c.Class = Class.Select(d => d.Clone()).ToList();
            c.ClassRng = ClassRng == null ? null : new SimRng(ClassRng.State);
            c.Inbox = Inbox.Select(m => m.Clone()).ToList();
            c.News = new List<string>(News);
        }
    }
}
