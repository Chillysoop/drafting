using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    // The season framework (roadmap step 37), ported from slice v47: one field of named drivers for the whole season, points
    // and standings, race objectives, a sponsor every season, team salary, qualifying before each race, and the season wrap
    // with its sponsor review and contract offers that move you up the ladder (karts, late models, stock cars).
    public sealed partial class Career
    {
        /// <summary>The season's 20 cars (you included, with Stats = your car). AI cars keep their names, numbers and stats
        /// for the season and grow a little between races.</summary>
        public List<Entrant> Field = new List<Entrant>();
        /// <summary>Season points by car id.</summary>
        public Dictionary<int, int> Pts = new Dictionary<int, int>();
        public Contract Contract = new Contract();
        /// <summary>This season's sponsor, or null until you sign one (Go racing waits for it, board Q59).</summary>
        public SponsorDeal Sponsor;
        public List<SponsorDeal> SponsorOffers;
        /// <summary>The next race's objectives (rolled in the garage once a sponsor is signed).</summary>
        public List<RaceObjective> Objectives;
        /// <summary>The next race's rival: a car rated a few spots above you. -1 = none rolled yet.</summary>
        public int RivalId = -1;
        public List<PastSeason> Past = new List<PastSeason>();
        /// <summary>The last season's review (the season-end screen).</summary>
        public SeasonReview Review;
        /// <summary>Contract offers waiting after the season wrap; the next season can't start until you sign one.</summary>
        public List<ContractOffer> Offers;
        /// <summary>Every series you've raced in, in order.</summary>
        public List<Series> Ladder = new List<Series>();
        /// <summary>The next race's qualifying, once run.</summary>
        public QualiResult Qual;
        /// <summary>A qualifying run was started and not finished (the game closed mid-run): it counts as giving up.</summary>
        public bool QualRunning;

        public int Team => Contract.Team;
        public Entrant MeEntrant => Field.First(e => e.IsMe);
        public TeamDef[] SeriesTeams => Teams.For(Series);
        public string TeamName => SeriesTeams[Team].Name;
        /// <summary>This season's races so far.</summary>
        public List<RaceReport> SeasonResults => Results.Where(r => r.Season == Season).ToList();

        /// <summary>A new season's field for the series: you in your team's first seat.</summary>
        void BuildField()
        {
            var keep = Field.FirstOrDefault(e => e.IsMe);
            Field = FieldBuilder.Build(Rng, Series, Base.Car, Name);
            var me = MeEntrant;
            if (keep != null) { me.Starts = keep.Starts; me.Wins = keep.Wins; me.Top5 = keep.Top5; me.Best = keep.Best; }
            Seat(Contract.Team);
        }

        /// <summary>Puts your car in team ti's first seat; that team's driver takes your old seat.</summary>
        void Seat(int ti)
        {
            var me = MeEntrant;
            var t = SeriesTeams;
            var o = Field.FirstOrDefault(e => !e.IsMe && e.Team == ti);
            if (o != null && me.Team != ti) { o.Team = me.Team; o.TeamRating = t[me.Team].Rating; }
            me.Team = ti; me.TeamRating = t[ti].Rating;
            Contract.Team = ti;
        }

        /// <summary>Your expected finish from car ratings alone (ties go your way); objectives and the garage use it.</summary>
        public int ExpectedFinish() { double m = Base.Car.Rating; return 1 + Field.Count(e => !e.IsMe && e.Stats.Rating > m); }

        public List<StandRow> Standings()
        {
            var t = SeriesTeams;
            var rows = Field.Select(e => new StandRow
            {
                Id = e.Id, IsMe = e.IsMe, Name = e.IsMe ? Name : e.Name, Team = e.Team >= 0 && e.Team < t.Length ? t[e.Team].Name : "",
                Pts = Pts.TryGetValue(e.Id, out var p) ? p : 0, Wins = e.SeasonWins, Best = e.Best,
            }).ToList();
            SimMath.StableSort(rows, (a, b) => a.Pts != b.Pts ? b.Pts.CompareTo(a.Pts) : a.Wins != b.Wins ? b.Wins.CompareTo(a.Wins) : a.Best != b.Best ? a.Best.CompareTo(b.Best) : a.Id.CompareTo(b.Id));
            for (int i = 0; i < rows.Count; i++) rows[i].Pos = i + 1;
            return rows;
        }

        public int MyRank() => Standings().FindIndex(r => r.IsMe) + 1;
        public int MyPoints => Pts.TryGetValue(MeEntrant.Id, out var p) ? p : 0;

        /// <summary>The cars a points table shows: the top n, plus you if you're further back.</summary>
        public List<StandRow> StandingsTop(int n)
        {
            var st = Standings();
            var o = st.Take(n).ToList();
            var me = st.First(r => r.IsMe);
            if (me.Pos > n) o.Add(me);
            return o;
        }

        // ------------------------------------------------------------------ sponsors and objectives

        /// <summary>The season so far against your sponsor's goal.</summary>
        public (string prog, GoalState st) GoalStatus()
        {
            if (Sponsor == null) return (null, GoalState.Open);
            var R = SeasonResults;
            return SeasonRules.GoalStatus(SeasonRules.Sponsor(Sponsor.Id).Goal, R, MyRank(), SeasonLength - R.Count, SeasonLength);
        }

        public string CantSign(string id) => Sponsor != null ? "You already have a sponsor this season" : SponsorOffers == null || !SponsorOffers.Any(o => o.Id == id) ? "Not on offer" : null;

        public bool SignSponsor(string id)
        {
            if (CantSign(id) != null) return false;
            var o = SponsorOffers.First(x => x.Id == id);
            Sponsor = o.Clone();
            Sponsor.Since = Season;
            SponsorOffers = null;
            RollObjectives();
            return true;
        }

        /// <summary>Two general objectives, plus the sponsor's ask when you have one, and this week's rival.</summary>
        public void RollObjectives()
        {
            // rival: a car rated one to four spots above you (the slice's race.rival)
            var exp = Field.ToList();
            SimMath.StableSort(exp, (a, b) => b.Stats.Rating.CompareTo(a.Stats.Rating));
            int e = exp.FindIndex(x => x.IsMe) + 1;
            var cands = exp.Skip(Math.Max(0, e - 5)).Take(Math.Max(0, e - 1 - Math.Max(0, e - 5))).Where(x => !x.IsMe).ToList();
            RivalId = cands.Count > 0 ? Rng.Pick(cands).Id : exp[0].Id;

            var sp = Sponsor != null ? SeasonRules.Sponsor(Sponsor.Id) : null;
            var o = new List<RaceObjective>();
            if (sp == null)
            {
                var pool = SeasonRules.Objectives.Where(x => x.Id != "combo3").ToList();
                Rng.Shuffle(pool);
                o.AddRange(pool.Take(2).Select(x => new RaceObjective { Id = x.Id }));
            }
            else
            {
                string ask = Rng.Pick(sp.Asks);
                var pool = SeasonRules.Objectives.Where(x => x.Id != ask).ToList();
                Rng.Shuffle(pool);
                o.AddRange(pool.Take(2).Select(x => new RaceObjective { Id = x.Id }));
                o.Add(new RaceObjective { Id = ask, Ask = true });
            }
            Objectives = o;
        }

        /// <summary>An objective's text for the next race (names the rival).</summary>
        public string ObjectiveText(RaceObjective o, int? expected = null)
        {
            var d = SeasonRules.Objective(o.Id);
            var s = d.Name(expected ?? ExpectedFinish(), Kart);
            if (o.Id == "rival" && RivalId >= 0) { var r = Field.FirstOrDefault(x => x.Id == RivalId); if (r != null) s += $" ({r.Name})"; }
            return s;
        }

        /// <summary>What an objective pays at this career's pay scale.</summary>
        public int ObjectivePay(RaceObjective o, double mul) => SeasonRules.R100(SeasonRules.Objective(o.Id).Pay * (o.Ask ? SeasonRules.AskMul : 1) * mul);

        // ------------------------------------------------------------------ after a race

        /// <summary>The season side of FinishRace: objectives, sponsor and salary money, points, the field's records and growth.</summary>
        void ScoreSeason(Race race, RaceReport res)
        {
            var fo = race.FinalOrder ?? race.Order;
            int Idx(int id) => fo.FindIndex(c => c.Id == id);
            int mi = Idx(race.Me.Id);
            var mate = Field.FirstOrDefault(e => !e.IsMe && e.Team == Team);
            res.BeatMate = mate != null && mi < Idx(mate.Id);
            res.BeatRival = RivalId >= 0 && mi < Idx(RivalId);
            res.Rival = RivalId >= 0 ? Field.FirstOrDefault(e => e.Id == RivalId)?.Name : null;
            foreach (var o in Objectives ?? new List<RaceObjective>())
            {
                var d = SeasonRules.Objective(o.Id);
                res.Objectives.Add(new ObjectiveResult { Id = o.Id, Ask = o.Ask, Name = ObjectiveText(o, res.ObjE), Pay = ObjectivePay(o, res.Mul), Ok = d.Test(res, Kart) });
            }
            res.ObjMet = res.Objectives.Count(o => o.Ok);
            // meet every objective (and finish) and the rewards move up one tier
            if (res.Objectives.Count >= 2 && res.ObjMet == res.Objectives.Count && !res.Dnf && res.Tier != RewardTier.Victory) { res.TierFrom = res.Tier; res.Tier++; }
            if (Sponsor != null) { res.Sponsor = Sponsor.Id; res.Retainer = Sponsor.Per; }
            res.Salary = Contract.Per;
            res.Total += res.Retainer + res.Salary + res.Objectives.Where(o => o.Ok).Sum(o => o.Pay);

            // points and records
            res.RankBefore = MyRank();
            for (int i = 0; i < fo.Count; i++)
            {
                int id = fo[i].Id;
                Pts[id] = (Pts.TryGetValue(id, out var p) ? p : 0) + SeasonRules.Points(i);
                var e = Field.FirstOrDefault(x => x.Id == id);
                if (e == null) continue;
                e.Starts++;
                if (i == 0) { e.Wins++; e.SeasonWins++; }
                if (i < 5) e.Top5++;
                e.Best = Math.Min(e.Best, i + 1);
            }
            res.Pts = SeasonRules.Points(res.Finish - 1);
            res.Rank = MyRank();
            res.PtsTotal = MyPoints;
            Objectives = null;

            // the AI field grows a little between races
            foreach (var e in Field)
            {
                if (e.IsMe || !Rng.Chance(SeasonRules.AiGrowth)) continue;
                switch (Rng.Range(0, 4))
                {
                    case 0: e.Stats.Power = Math.Min(10, e.Stats.Power + 1); break;
                    case 1: e.Stats.Handling = Math.Min(10, e.Stats.Handling + 1); break;
                    case 2: e.Stats.Racecraft = Math.Min(10, e.Stats.Racecraft + 1); break;
                    default: e.Stats.Defense = Math.Min(10, e.Stats.Defense + 1); break;
                }
            }
        }

        // ------------------------------------------------------------------ the season wrap

        /// <summary>The last race is done: pay the sponsor's goal bonus (and offer a renewal) or let them walk, record the season,
        /// roll next season's sponsor offers and the contract offers, and start the next season's calendar. Points start over;
        /// money, car, collection, parts and crew carry on.</summary>
        public SeasonReview WrapSeason()
        {
            int rk = MyRank();
            var rv = new SeasonReview { Season = Season, Series = Series, Rank = rk, Pts = MyPoints, Table = StandingsTop(10) };
            var sp = Sponsor != null ? SeasonRules.Sponsor(Sponsor.Id) : null;
            if (sp != null)
            {
                var gs = SeasonRules.GoalStatus(sp.Goal, SeasonResults, rk, 0, SeasonLength);
                rv.Sponsor = sp.Id; rv.Goal = SeasonRules.Goal(sp.Goal).Name(SeasonLength); rv.Prog = gs.prog; rv.Met = gs.st == GoalState.Met;
                if (rv.Met)
                {
                    Money += Sponsor.Bonus;
                    rv.Bonus = Sponsor.Bonus;
                    rv.RenewPer = SeasonRules.R100(Sponsor.Per * SeasonRules.RenewRaise);
                    rv.RenewBonus = SeasonRules.R100(Sponsor.Bonus * SeasonRules.RenewRaise);
                }
            }
            Past.Add(new PastSeason { Season = Season, Series = Series, Rank = rk, Pts = MyPoints, Sponsor = sp?.Id, Met = rv.Met });
            var offers = new List<SponsorDeal>();
            if (rv.Met) offers.Add(new SponsorDeal { Id = sp.Id, Per = rv.RenewPer, Bonus = rv.RenewBonus, Renew = true });
            offers.AddRange(SeasonRules.RollSponsorOffers(Rng, rk, sp?.Id, CareerRules.Econ(Series)));
            SponsorOffers = offers.Take(3).ToList();
            Sponsor = null;
            Offers = SeasonRules.Offers(Series, Team, rk);
            Review = rv;
            Season++;
            RaceIdx = 0;
            Pts.Clear();
            foreach (var e in Field) e.SeasonWins = 0;
            Objectives = null; Qual = null; QualRunning = false;
            return rv;
        }

        public bool SignContract(int i)
        {
            if (Offers == null || i < 0 || i >= Offers.Count) return false;
            var o = Offers[i];
            var from = Series;
            double econWas = CareerRules.Econ(from);
            Money += o.Sign;
            var notes = new List<string>();
            if (o.Kind == OfferKind.Promote)
            {
                Series = o.Series;
                Ladder.Add(o.Series);
                var up = SeasonRules.PersonaUp(Persona);
                if (from == Series.Kart && up != null)
                {
                    notes.Add($"{Personas.Get(Persona).Name} grows into {Personas.Get(up).Name}.");
                    Persona = up;
                }
                if (from == Series.Kart)
                {
                    int n = Collection.Count(id => CardBook.Get(id).Kart);
                    string K2S(string id) => CardBook.Get(id).Kart ? (SeasonRules.KartToStock.TryGetValue(id, out var s) ? s : "tuck_in") : id;
                    Collection = Collection.Select(K2S).ToList();
                    Deck = Deck.Select(K2S).ToList();
                    Hist = Hist.Where(kv => !CardBook.Get(kv.Key).Kart).ToDictionary(kv => kv.Key, kv => kv.Value);
                    notes.Add($"Your {n} kart cards became their {SeasonRules.SeriesOne(o.Series)} cousins.");
                    PartsOwned = PartsOwned.Where(id => !Parts.Get(id).Kart).ToList();
                    foreach (var s in Equipped.Keys.ToList()) if (Equipped[s] != null && Parts.Get(Equipped[s]).Kart) Equipped[s] = null;
                    AddPart(Personas.Get(Persona).Part);
                }
                TrackDecks.Clear();
                deckFor = -1;
                Contract.Team = Teams.PlayerTeam;
                BuildField();
            }
            if (o.Kind != OfferKind.Stay)
            {
                Seat(o.Team);
                // the car is the new team's car (plus your persona's bonus); the crew is the new team's crew
                var b = Personas.Get(Persona).Bonus;
                int B(string k) => b.TryGetValue(k, out var v) ? v : 0;
                var c = Base.Car;
                c.Power = Clamp10(o.Rating + B("power")); c.Handling = Clamp10(o.Rating + B("handling")); c.Aero = Clamp10(o.Rating + B("aero"));
                c.Durability = Clamp10(4 + B("durability"));
                int cb = SeasonRules.CrewBase(o.Rating);
                Base.CrewChief = cb; Base.Spotter = cb; Base.Engineer = cb; Base.PitCrew = cb;
                Base.Crew = new CrewPerks();
                Base.PitDeck = new List<string>(PitCards.StartingDeck);
                notes.Insert(0, $"Car: power, handling and aero {o.Rating}. Crew level {cb}; perks and pit cards start over.");
            }
            Contract = new Contract { Per = o.Per, Team = o.Team, Series = o.Series, Since = Season };
            Offers = null;
            // sponsor money follows the series
            double k = CareerRules.Econ(Series) / econWas;
            if (k != 1 && SponsorOffers != null)
                foreach (var s in SponsorOffers) { s.Per = SeasonRules.R100(s.Per * k); s.Bonus = SeasonRules.R100(s.Bonus * k); }
            ContractNote = $"{(o.Kind == OfferKind.Stay ? "Re-signed with" : "Signed with")} {o.Name}{(o.Kind == OfferKind.Promote ? " · " + SeasonRules.SeriesName(o.Series) : "")}. {MoneyText(o.Per)} a race{(o.Sign > 0 ? $" and {MoneyText(o.Sign)} to sign" : "")}. {string.Join(" ", notes)}".Trim();
            shopRolled = false; ShopPart = null; ShopRerolls = 0;
            EnterGarage();
            return true;
        }

        /// <summary>What the last contract signing did, for the garage's notice (not saved).</summary>
        public string ContractNote;

        static int Clamp10(int v) => Math.Max(1, Math.Min(10, v));
        static string MoneyText(int v) => "$" + v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The field for the next race: copies, with your car as the race will run it.</summary>
        List<Entrant> RaceField(CarStats mine)
        {
            var f = Field.Select(e => e.Clone()).ToList();
            var me = f.First(e => e.IsMe);
            me.Stats = mine; me.Name = Name;
            return f;
        }

        // ------------------------------------------------------------------ qualifying

        /// <summary>This race's qualifying rolls (card offer, zone centres, grid noise) come from the race seed, so quitting
        /// and coming back can't reroll them.</summary>
        public SimRng QualiRng(uint salt) => new SimRng(RaceSeed ^ salt);
        public List<string> QualiOffer() => Qualifying.Offer(Deck, QualiRng(0x51A11u));
        public bool QualiDone => Qual != null && Qual.Season == Season && Qual.RaceIdx == RaceIdx;

        public QualiRun StartQuali(string card)
        {
            if (card != null && !QualiOffer().Contains(card)) card = null;
            QualRunning = true;
            return new QualiRun(Qualifying.Plan(NextTrack, Base.Car, card, QualiRng(0x9C4Eu)), Base.Car);
        }

        /// <summary>Sets the grid from a finished run, or from an average lap when run is null (Skip, or giving up).</summary>
        public QualiResult FinishQuali(QualiRun run)
        {
            Qual = Qualifying.Finish(Season, RaceIdx, NextTrack, RaceField(Base.Car), run?.Plan, run == null, QualiRng(0x6E1Du));
            QualRunning = false;
            return Qual;
        }
    }
}
