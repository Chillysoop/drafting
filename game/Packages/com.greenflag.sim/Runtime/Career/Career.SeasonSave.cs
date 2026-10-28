using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    // The season's part of the save file (format version 2, roadmap step 37).
    public sealed partial class Career
    {
        static JsonValue Ints(IEnumerable<int> v) { var a = JsonValue.Arr(); foreach (var x in v) a.Add(JsonValue.Of((long)x)); return a; }
        static List<int> IntsFrom(JsonValue j) => j.Items.Select(x => x.Int()).ToList();
        static JsonValue DealJson(SponsorDeal d) => d == null ? JsonValue.Null : JsonValue.Obj().Set("id", d.Id).Set("per", d.Per).Set("bonus", d.Bonus).Set("renew", d.Renew).Set("since", d.Since).Set("trust", d.Trust);
        static SponsorDeal DealFrom(JsonValue j) => j.IsNull ? null : new SponsorDeal { Id = j["id"].Str(), Per = j["per"].Int(), Bonus = j["bonus"].Int(), Renew = j["renew"].Bool(), Since = j["since"].Int(1), Trust = (int)SimMath.Clamp(j["trust"].Int(), -2, 2) };

        static JsonValue StatsJson(CarStats c) => JsonValue.Arr().Add(JsonValue.Of((long)c.Power)).Add(JsonValue.Of((long)c.Handling)).Add(JsonValue.Of((long)c.Aero)).Add(JsonValue.Of((long)c.Durability))
            .Add(JsonValue.Of((long)c.Racecraft)).Add(JsonValue.Of((long)c.Defense)).Add(JsonValue.Of((long)c.Consistency)).Add(JsonValue.Of((long)c.TireMgmt));
        static CarStats StatsFrom(JsonValue j) => new CarStats { Power = j[0].Int(), Handling = j[1].Int(), Aero = j[2].Int(), Durability = j[3].Int(), Racecraft = j[4].Int(), Defense = j[5].Int(), Consistency = j[6].Int(), TireMgmt = j[7].Int() };

        /// <summary>The field as save JSON: your own car's stats are left out (they are the career's car).</summary>
        public static JsonValue FieldJson(IEnumerable<Entrant> field)
        {
            var a = JsonValue.Arr();
            foreach (var e in field)
                a.Add(JsonValue.Obj().Set("id", e.Id).Set("team", e.Team).Set("rating", e.TeamRating).Set("num", e.Number).Set("me", e.IsMe)
                    .Set("name", e.IsMe ? null : e.Name).Set("trait", e.Trait.ToString()).Set("stats", e.IsMe ? JsonValue.Null : StatsJson(e.Stats))
                    .Set("rec", Ints(new[] { e.Starts, e.Wins, e.SeasonWins, e.Top5, e.Best })));
            return a;
        }

        static JsonValue RowJson(StandRow r) => JsonValue.Obj().Set("pos", r.Pos).Set("id", r.Id).Set("name", r.Name).Set("team", r.Team).Set("pts", r.Pts).Set("wins", r.Wins).Set("best", r.Best).Set("me", r.IsMe);
        static StandRow RowFrom(JsonValue j) => new StandRow { Pos = j["pos"].Int(), Id = j["id"].Int(), Name = j["name"].Str(""), Team = j["team"].Str(""), Pts = j["pts"].Int(), Wins = j["wins"].Int(), Best = j["best"].Int(99), IsMe = j["me"].Bool() };

        void SeasonToJson(JsonValue j)
        {
            var pts = JsonValue.Obj();
            foreach (var kv in Pts.OrderBy(kv => kv.Key)) pts.Set(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), kv.Value);
            j.Set("field", FieldJson(Field)).Set("pts", pts)
                .Set("contract", JsonValue.Obj().Set("per", Contract.Per).Set("team", Contract.Team).Set("series", Contract.Series.ToString()).Set("since", Contract.Since).Set("clauses", JsonValue.Of(Contract.Clauses)))
                .Set("sponsor", DealJson(Sponsor));
            if (SponsorOffers == null) j.Set("sponsorOffers", JsonValue.Null);
            else { var a = JsonValue.Arr(); foreach (var d in SponsorOffers) a.Add(DealJson(d)); j.Set("sponsorOffers", a); }
            if (Objectives == null) j.Set("objectives", JsonValue.Null);
            else { var a = JsonValue.Arr(); foreach (var o in Objectives) a.Add(JsonValue.Obj().Set("id", o.Id).Set("ask", o.Ask)); j.Set("objectives", a); }
            j.Set("rival", RivalId);
            var past = JsonValue.Arr();
            foreach (var p in Past) past.Add(JsonValue.Obj().Set("season", p.Season).Set("series", p.Series.ToString()).Set("rank", p.Rank).Set("pts", p.Pts).Set("sponsor", p.Sponsor).Set("met", p.Met));
            j.Set("past", past);
            if (Review == null) j.Set("review", JsonValue.Null);
            else
            {
                var r = Review;
                var t = JsonValue.Arr();
                foreach (var row in r.Table) t.Add(RowJson(row));
                j.Set("review", JsonValue.Obj().Set("season", r.Season).Set("series", r.Series.ToString()).Set("rank", r.Rank).Set("pts", r.Pts).Set("table", t)
                    .Set("sponsor", r.Sponsor).Set("goal", r.Goal).Set("prog", r.Prog).Set("met", r.Met).Set("bonus", r.Bonus).Set("renewPer", r.RenewPer).Set("renewBonus", r.RenewBonus));
            }
            if (Offers == null) j.Set("offers", JsonValue.Null);
            else
            {
                var a = JsonValue.Arr();
                foreach (var o in Offers) a.Add(JsonValue.Obj().Set("kind", o.Kind.ToString()).Set("series", o.Series.ToString()).Set("team", o.Team).Set("name", o.Name).Set("rating", o.Rating).Set("per", o.Per).Set("sign", o.Sign).Set("neg", NegJson(o.Neg)));
                j.Set("offers", a);
            }
            j.Set("ladder", JsonValue.Of(Ladder.Select(x => x.ToString())));
            if (Qual == null) j.Set("qual", JsonValue.Null);
            else
            {
                var q = Qual;
                var times = JsonValue.Arr();
                foreach (var x in q.Times) times.Add(JsonValue.Of(x));
                j.Set("qual", JsonValue.Obj().Set("season", q.Season).Set("race", q.RaceIdx).Set("track", q.TrackId).Set("grid", Ints(q.Grid)).Set("times", times)
                    .Set("pos", q.Pos).Set("time", q.Time).Set("skip", q.Skip).Set("card", q.Card).Set("avg", q.Avg).Set("best", q.Best).Set("counts", Ints(q.Counts)));
            }
            j.Set("qualRunning", QualRunning);
        }

        void SeasonFromJson(JsonValue j)
        {
            Field = new List<Entrant>();
            foreach (var e in j["field"].Items)
            {
                var rec = IntsFrom(e["rec"]);
                if (rec.Count != 5) throw new FormatException("Save file: a car's record needs 5 numbers");
                bool me = e["me"].Bool();
                Field.Add(new Entrant
                {
                    Id = e["id"].Int(), Team = e["team"].Int(), TeamRating = e["rating"].Int(), Number = e["num"].Int(), IsMe = me,
                    Name = me ? Name : e["name"].Str(""), Trait = e["trait"].Enum(Trait.None), Stats = me ? Base.Car : StatsFrom(e["stats"]),
                    Starts = rec[0], Wins = rec[1], SeasonWins = rec[2], Top5 = rec[3], Best = rec[4],
                });
            }
            Pts = new Dictionary<int, int>();
            foreach (var kv in j["pts"].Fields) Pts[int.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture)] = kv.Value.Int();
            var k = j["contract"];
            Contract = new Contract { Per = k["per"].Int(), Team = k["team"].Int(Teams.PlayerTeam), Series = k["series"].Enum(Series), Since = k["since"].Int(1), Clauses = k["clauses"].Strings() };
            Sponsor = DealFrom(j["sponsor"]);
            SponsorOffers = j["sponsorOffers"].IsNull ? null : j["sponsorOffers"].Items.Select(DealFrom).ToList();
            Objectives = j["objectives"].IsNull ? null : j["objectives"].Items.Select(o => new RaceObjective { Id = o["id"].Str(), Ask = o["ask"].Bool() }).ToList();
            RivalId = j["rival"].Int(-1);
            Past = j["past"].Items.Select(p => new PastSeason { Season = p["season"].Int(), Series = p["series"].Enum(Series.Stock), Rank = p["rank"].Int(), Pts = p["pts"].Int(), Sponsor = p["sponsor"].Str(), Met = p["met"].Bool() }).ToList();
            var r = j["review"];
            Review = r.IsNull ? null : new SeasonReview
            {
                Season = r["season"].Int(), Series = r["series"].Enum(Series.Stock), Rank = r["rank"].Int(), Pts = r["pts"].Int(), Table = r["table"].Items.Select(RowFrom).ToList(),
                Sponsor = r["sponsor"].Str(), Goal = r["goal"].Str(), Prog = r["prog"].Str(), Met = r["met"].Bool(), Bonus = r["bonus"].Int(), RenewPer = r["renewPer"].Int(), RenewBonus = r["renewBonus"].Int(),
            };
            Offers = j["offers"].IsNull ? null : j["offers"].Items.Select(o => new ContractOffer
            {
                Kind = o["kind"].Enum(OfferKind.Stay), Series = o["series"].Enum(Series.Stock), Team = o["team"].Int(), Name = o["name"].Str(""), Rating = o["rating"].Int(), Per = o["per"].Int(), Sign = o["sign"].Int(), Neg = NegFrom(o["neg"]),
            }).ToList();
            Ladder = j["ladder"].Strings().Select(x => JsonValue.Of(x).Enum(Series.Stock)).ToList();
            var q = j["qual"];
            Qual = q.IsNull ? null : new QualiResult
            {
                Season = q["season"].Int(), RaceIdx = q["race"].Int(), TrackId = q["track"].Str(), Grid = IntsFrom(q["grid"]), Times = q["times"].Items.Select(x => x.Double()).ToList(),
                Pos = q["pos"].Int(), Time = q["time"].Double(), Skip = q["skip"].Bool(), Card = q["card"].Str(), Avg = q["avg"].Double(), Best = q["best"].Int(), Counts = IntsFrom(q["counts"]).ToArray(),
            };
            if (Qual != null && (Qual.Counts.Length != 5 || Qual.Grid.Count != Field.Count || Qual.Grid.Any(id => !Field.Any(e => e.Id == id))))
                throw new FormatException("Save file: the qualifying grid doesn't match the field");
            QualRunning = j["qualRunning"].Bool();
        }

        /// <summary>
        /// Save format 1 to 2 (step 37): a version 1 career had no season around it. It gets a field for its series (built
        /// from its seed with its own generator, so the career's rolls don't move), no points yet this season, the smallest
        /// team's contract with no salary, and no sponsor: the garage offers sponsors, and you sign one before the next race.
        /// </summary>
        public static JsonValue MigrateV1(JsonValue c)
        {
            var series = c["series"].Enum(Series.Stock);
            var field = FieldBuilder.Build(new SimRng(c["seed"].UInt() ^ 0xF1E1D5u), series, CarStats.Rookie(), c["name"].Str("Rookie"));
            return c.Set("field", FieldJson(field)).Set("pts", JsonValue.Obj())
                .Set("contract", JsonValue.Obj().Set("per", 0).Set("team", Teams.PlayerTeam).Set("series", series.ToString()).Set("since", 1))
                .Set("sponsor", JsonValue.Null).Set("sponsorOffers", JsonValue.Null).Set("objectives", JsonValue.Null).Set("rival", -1)
                .Set("past", JsonValue.Arr()).Set("review", JsonValue.Null).Set("offers", JsonValue.Null)
                .Set("ladder", JsonValue.Arr().Add(JsonValue.Of(series.ToString()))).Set("qual", JsonValue.Null).Set("qualRunning", false);
        }
    }
}
