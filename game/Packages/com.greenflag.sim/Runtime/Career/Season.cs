using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>One race objective as rolled for the next race: an id, and whether it is the sponsor's ask (pays 1.5x).</summary>
    public sealed class RaceObjective
    {
        public string Id;
        public bool Ask;
    }

    /// <summary>How an objective went, on the reward screen.</summary>
    public sealed class ObjectiveResult
    {
        public string Id, Name;
        public bool Ask, Ok;
        public int Pay;
    }

    /// <summary>A race objective (the slice's OBJECTIVES, step 17). Targets scale with e, your expected finish from car ratings.</summary>
    public sealed class ObjectiveDef
    {
        public string Id;
        public int Pay;
        /// <summary>The objective's text for an expected finish e (karts word the combo one differently).</summary>
        public Func<int, bool, string> Name;
        public Func<RaceReport, bool, bool> Test;
    }

    public enum GoalState { Open, On, Behind, Met, Missed }

    /// <summary>A sponsor's season goal (the slice's SGOALS). Goals that count races scale with the season's length.</summary>
    public sealed class SeasonGoal
    {
        public string Id;
        /// <summary>Count: hit Need of something. Max: no more than Max of something. Net: a running total to reach Need. Rank: finish the season in the top Rank.</summary>
        public enum Kind { Count, Max, Net, Rank }
        public Kind K;
        public int Need, Max, Rank;
        /// <summary>True when Need was set for a 10-race season and scales with the calendar (board Q82).</summary>
        public bool Scales;
        public Func<int, string> Text;
        public Func<IReadOnlyList<RaceReport>, int> Count;

        public int NeedFor(int seasonLength) => Scales ? Math.Max(1, (int)SimMath.RoundJs(Need * seasonLength / 10.0)) : Need;
        public string Name(int seasonLength) => Text(NeedFor(seasonLength));
    }

    /// <summary>A sponsor (the slice's SPONSORS): money every race, a season goal with a bonus, and the race objectives it asks for.</summary>
    public sealed class SponsorDef
    {
        public string Id, Name, Color, Blurb, Goal;
        public int Per, Bonus;
        public string[] Asks;
        /// <summary>Last season's points finish needed for an offer (99 = anyone).</summary>
        public int Need = 99;
    }

    /// <summary>A sponsor's terms: an offer in the garage, or the signed deal.</summary>
    public sealed class SponsorDeal
    {
        public string Id;
        public int Per, Bonus;
        /// <summary>A renewal after a season goal was met (+20%).</summary>
        public bool Renew;
        public int Since;
        /// <summary>How the sponsor feels about you, −2 to 2 (step 38: dinners, shoots and storms move it).</summary>
        public int Trust;
        public SponsorDeal Clone() => (SponsorDeal)MemberwiseClone();
    }

    public enum OfferKind { Stay, Move, Promote }

    /// <summary>A season-end contract offer (the slice's rollOffers, step 22): stay, a better team in the same series, or a step up the ladder.</summary>
    public sealed class ContractOffer
    {
        public OfferKind Kind;
        public Series Series;
        public int Team, Rating, Per, Sign;
        public string Name;
        /// <summary>The Negotiate check on this offer (step 38), or null before you try.</summary>
        public NegResult Neg;
        public ContractOffer Clone() { var o = (ContractOffer)MemberwiseClone(); o.Neg = Neg?.Clone(); return o; }
    }

    /// <summary>Your team contract: salary every race. A new career starts at the series' smallest team with no salary.</summary>
    public sealed class Contract
    {
        public int Per, Team = Teams.PlayerTeam, Since = 1;
        public Series Series;
        /// <summary>Extras written in at a contract talk (step 38): power, handling, crewChief.</summary>
        public List<string> Clauses = new List<string>();
        public Contract Clone() { var c = (Contract)MemberwiseClone(); c.Clauses = new List<string>(Clauses); return c; }
    }

    /// <summary>One line of the points table.</summary>
    public sealed class StandRow
    {
        public int Pos, Id, Pts, Wins, Best;
        public string Name, Team;
        public bool IsMe;
    }

    /// <summary>A season that's over, for the career history.</summary>
    public sealed class PastSeason
    {
        public int Season, Rank, Pts;
        public Series Series;
        public string Sponsor;
        public bool Met;
    }

    /// <summary>The season-end screen: final standings, the champion, and the sponsor review.</summary>
    public sealed class SeasonReview
    {
        public int Season, Rank, Pts;
        public Series Series;
        public List<StandRow> Table = new List<StandRow>();
        public string Sponsor, Goal, Prog;
        public bool Met;
        public int Bonus, RenewPer, RenewBonus;
        public StandRow Champion => Table.Count > 0 ? Table[0] : null;
    }

    /// <summary>
    /// The season rules (roadmap step 37), as in slice v47: points per place, race objectives, sponsors and their season goals,
    /// and the season-end contract offers that move you up the ladder.
    /// </summary>
    public static class SeasonRules
    {
        /// <summary>Points for a finishing place (0 = the winner): 40 for a win, 35 for 2nd, then one less per place (17 for 20th).</summary>
        public static int Points(int i) => i == 0 ? 40 : Math.Max(1, 36 - i);

        /// <summary>Sponsor asks pay this much more than a plain objective.</summary>
        public const double AskMul = 1.5;
        /// <summary>A sponsor whose goal you met renews with this raise.</summary>
        public const double RenewRaise = 1.2;
        /// <summary>The AI field grows a little between races: each AI car has this chance of +1 to one stat.</summary>
        public const double AiGrowth = 0.3;

        public static int FinTop(int e) => Math.Max(1, e - 3);
        public static int RunTop(int e) => Math.Max(3, e - 8);
        public static int R100(double v) => (int)(SimMath.RoundJs(v / 100) * 100);
        static int R50(double v) => (int)(SimMath.RoundJs(v / 50) * 50);

        public static readonly IReadOnlyList<ObjectiveDef> Objectives = new[]
        {
            new ObjectiveDef { Id = "top15", Pay = 3000, Name = (e, k) => $"Finish P{FinTop(e)} or better", Test = (r, k) => !r.Dnf && r.Finish <= FinTop(r.ObjE) },
            new ObjectiveDef { Id = "gain5", Pay = 2500, Name = (e, k) => "Finish 5+ spots ahead of where you started", Test = (r, k) => r.Start - r.Finish >= 5 },
            new ObjectiveDef { Id = "top10", Pay = 2000, Name = (e, k) => $"Run in the top {RunTop(e)} at any point", Test = (r, k) => r.BestPos <= RunTop(r.ObjE) },
            new ObjectiveDef { Id = "clean", Pay = 2000, Name = (e, k) => "No critical failures", Test = (r, k) => r.Crits == 0 },
            new ObjectiveDef { Id = "land4", Pay = 2000, Name = (e, k) => "Land 12 successful cards", Test = (r, k) => r.Succ >= 12 },
            new ObjectiveDef { Id = "mate", Pay = 1500, Name = (e, k) => "Finish ahead of your teammate", Test = (r, k) => r.BeatMate },
            new ObjectiveDef { Id = "rival", Pay = 2500, Name = (e, k) => "Finish ahead of your rival", Test = (r, k) => r.BeatRival },
            new ObjectiveDef { Id = "combo3", Pay = 2500, Name = (e, k) => k ? "Land a ×3 combo" : "Land a ×4 combo", Test = (r, k) => r.BestChain >= (k ? 3 : 4) },
        };

        public static ObjectiveDef Objective(string id)
        {
            foreach (var o in Objectives) if (o.Id == id) return o;
            throw new KeyNotFoundException("No objective " + id);
        }

        static int Finished(IReadOnlyList<RaceReport> R, Func<RaceReport, bool> f) => R.Count(f);

        public static readonly IReadOnlyList<SeasonGoal> Goals = new[]
        {
            new SeasonGoal { Id = "dnf2", K = SeasonGoal.Kind.Max, Max = 2, Text = n => "No more than 2 DNFs all season", Count = R => Finished(R, r => r.Dnf) },
            new SeasonGoal { Id = "beat6", K = SeasonGoal.Kind.Count, Need = 6, Scales = true, Text = n => $"Finish 3+ spots ahead of your expected spot in {n} races", Count = R => Finished(R, r => !r.Dnf && r.Finish <= r.Expected - 3) },
            new SeasonGoal { Id = "gain50", K = SeasonGoal.Kind.Net, Need = 50, Scales = true, Text = n => $"Gain {n} spots over the season, start to finish", Count = R => R.Sum(r => r.Start - r.Finish) },
            new SeasonGoal { Id = "top10x4", K = SeasonGoal.Kind.Count, Need = 4, Scales = true, Text = n => $"{n} top-10 finishes", Count = R => Finished(R, r => !r.Dnf && r.Finish <= 10) },
            new SeasonGoal { Id = "pts10", K = SeasonGoal.Kind.Rank, Rank = 10, Text = n => "Finish the season top 10 in points" },
            new SeasonGoal { Id = "win1", K = SeasonGoal.Kind.Count, Need = 1, Text = n => "Win a race", Count = R => Finished(R, r => r.Finish == 1) },
            new SeasonGoal { Id = "pts5", K = SeasonGoal.Kind.Rank, Rank = 5, Text = n => "Finish the season top 5 in points" },
        };

        public static SeasonGoal Goal(string id)
        {
            foreach (var g in Goals) if (g.Id == id) return g;
            throw new KeyNotFoundException("No season goal " + id);
        }

        public static readonly IReadOnlyList<SponsorDef> Sponsors = new[]
        {
            new SponsorDef { Id = "pinecrest", Name = "Pinecrest Feed & Seed", Color = "#7fbf4d", Blurb = "A family feed store. Wants the car home in one piece.", Per = 1000, Bonus = 6000, Goal = "dnf2", Asks = new[] { "clean", "land4" } },
            new SponsorDef { Id = "cobalt", Name = "Cobalt Insurance", Color = "#4f8ff0", Blurb = "Pays for results it can predict.", Per = 1500, Bonus = 9000, Goal = "beat6", Asks = new[] { "top15", "clean" } },
            new SponsorDef { Id = "bolt", Name = "Bolt Energy", Color = "#f2c94c", Blurb = "Loud can, loud driving. Wants passes on TV.", Per = 1800, Bonus = 10000, Goal = "gain50", Asks = new[] { "gain5", "combo3" } },
            new SponsorDef { Id = "summit", Name = "Summit Credit Union", Color = "#2fb3a0", Blurb = "Wants its name in the points table every week.", Per = 2000, Bonus = 11000, Goal = "pts10", Asks = new[] { "mate", "top10" } },
            new SponsorDef { Id = "hitide", Name = "Hi-Tide Motor Oil", Color = "#ff6b5e", Blurb = "Wants the car near the front of the TV shot.", Per = 2200, Bonus = 12000, Goal = "top10x4", Asks = new[] { "top10", "rival" } },
            new SponsorDef { Id = "redmesa", Name = "Red Mesa Trucking", Color = "#d9824a", Blurb = "Hauls for winners only. Talks to you after a top-12 season.", Per = 2800, Bonus = 15000, Goal = "win1", Asks = new[] { "rival", "gain5" }, Need = 12 },
            new SponsorDef { Id = "keystone", Name = "Keystone Tire Co.", Color = "#a99bff", Blurb = "Title-sponsor money for a title run. Talks to you after a top-8 season.", Per = 3500, Bonus = 20000, Goal = "pts5", Asks = new[] { "top15", "combo3" }, Need = 8 },
        };

        public static SponsorDef Sponsor(string id)
        {
            foreach (var s in Sponsors) if (s.Id == id) return s;
            throw new KeyNotFoundException("No sponsor " + id);
        }

        /// <summary>Where a season goal stands with left races to go (0 = the season is over), and the progress text.</summary>
        public static (string prog, GoalState st) GoalStatus(string goalId, IReadOnlyList<RaceReport> R, int rank, int left, int seasonLength)
        {
            var g = Goal(goalId);
            if (g.K == SeasonGoal.Kind.Rank)
                return (R.Count > 0 ? $"P{rank} in points now" : "no races yet", R.Count == 0 ? GoalState.Open : rank <= g.Rank ? (left > 0 ? GoalState.On : GoalState.Met) : (left > 0 ? GoalState.Behind : GoalState.Missed));
            int c = g.Count(R), need = g.NeedFor(seasonLength);
            if (g.K == SeasonGoal.Kind.Max) return ($"{c} of {g.Max} allowed", c > g.Max ? GoalState.Missed : left > 0 ? GoalState.On : GoalState.Met);
            if (g.K == SeasonGoal.Kind.Net) return ($"{(c > 0 ? "+" : "")}{c} of +{need}", c >= need ? (left > 0 ? GoalState.On : GoalState.Met) : left > 0 ? GoalState.Open : GoalState.Missed);
            return ($"{Math.Min(c, need)} of {need}", c >= need ? GoalState.Met : left > 0 ? GoalState.Open : GoalState.Missed);
        }

        public static string GoalStateName(GoalState s) => s == GoalState.Met ? "Met" : s == GoalState.Missed ? "Missed" : s == GoalState.On ? "On track" : s == GoalState.Behind ? "Behind" : "Open";

        /// <summary>Up to three sponsor offers for a points finish (99 = no season yet), at the series' pay scale. The better
        /// sponsors that need a strong season come first.</summary>
        public static List<SponsorDeal> RollSponsorOffers(SimRng rng, int lastRank, string exclude, double econ)
        {
            var pool = Sponsors.Where(s => lastRank <= s.Need && s.Id != exclude).ToList();
            rng.Shuffle(pool);
            SimMath.StableSort(pool, (a, b) => a.Need.CompareTo(b.Need));
            return pool.Take(3).Select(s => new SponsorDeal { Id = s.Id, Per = R100(s.Per * econ), Bonus = R100(s.Bonus * econ) }).ToList();
        }

        // ------------------------------------------------------------------ the ladder and contracts

        /// <summary>Salary per race at a rating-1 team, by series (the slice's SERIES pay).</summary>
        public static int SeriesPay(Series s) => s == Series.Kart ? 150 : s == Series.Late ? 250 : 400;
        public static Series? Next(Series s) => s == Series.Kart ? Series.Late : s == Series.Late ? (Series?)Series.Stock : null;
        public static int Rung(Series s) => s == Series.Kart ? 1 : s == Series.Late ? 2 : 3;
        public static string SeriesName(Series s) => s == Series.Kart ? "Karts" : s == Series.Late ? "Late models" : "Stock cars";
        public static string SeriesOne(Series s) => s == Series.Kart ? "kart" : s == Series.Late ? "late model" : "stock car";

        /// <summary>Promotion offers by final points position: which team ratings in the next series call (step 23's tiers, board Q83).</summary>
        public static int[] PromotionTiers(int rank) => rank <= 3 ? new[] { 5, 3 } : rank <= 8 ? new[] { 4, 3 } : rank <= 14 ? new[] { 3 } : new int[0];

        /// <summary>Crew level at a team of rating r.</summary>
        public static int CrewBase(int r) => (int)SimMath.Clamp(3 + Math.Floor((r - 3) / 2.0), 2, 8);

        /// <summary>The team in a series whose rating is closest to r (the player team's seat loses ties).</summary>
        public static int TeamAt(Series s, int r)
        {
            var t = Teams.For(s);
            int best = Teams.PlayerTeam; double d = 99;
            for (int i = 0; i < t.Length; i++)
            {
                double q = Math.Abs(t[i].Rating - r) + (i == Teams.PlayerTeam ? 0.1 : 0);
                if (q < d) { d = q; best = i; }
            }
            return best;
        }

        public static ContractOffer Offer(OfferKind kind, Series s, int team, int rank)
        {
            var t = Teams.For(s)[team];
            double pay = SeriesPay(s) * t.Rating * (kind == OfferKind.Stay && rank <= 5 ? 1.15 : 1);
            return new ContractOffer { Kind = kind, Series = s, Team = team, Name = t.Name, Rating = t.Rating, Per = R50(pay), Sign = kind == OfferKind.Stay ? 0 : R50(pay * 4) };
        }

        /// <summary>Season-end offers: always stay; a better team in the same series after a top-10 season; and step-up offers
        /// from the next series' smaller teams after a top-14 season.</summary>
        public static List<ContractOffer> Offers(Series s, int curTeam, int rank)
        {
            var teams = Teams.For(s);
            int curR = teams[curTeam].Rating;
            var o = new List<ContractOffer> { Offer(OfferKind.Stay, s, curTeam, rank) };
            if (rank <= 10)
            {
                int want = Math.Min(9, curR + (rank <= 3 ? 3 : rank <= 6 ? 2 : 1)), ti = TeamAt(s, want);
                if (teams[ti].Rating > curR && ti != curTeam) o.Add(Offer(OfferKind.Move, s, ti, rank));
            }
            var nx = Next(s);
            if (nx != null)
            {
                var used = new HashSet<int>();
                foreach (int r in PromotionTiers(rank))
                {
                    int ti = TeamAt(nx.Value, r);
                    if (used.Add(ti)) o.Add(Offer(OfferKind.Promote, nx.Value, ti, rank));
                }
            }
            return o;
        }

        /// <summary>Kart cards turn into their stock car cousins on promotion out of karts (the slice's K2S).</summary>
        public static readonly IReadOnlyDictionary<string, string> KartToStock = new Dictionary<string, string>
        {
            ["k_tow"] = "tuck_in", ["k_push"] = "bump_draft", ["k_pullout"] = "side_draft", ["k_show"] = "dive_bomb", ["k_switch"] = "crossover",
            ["k_outside"] = "high_line", ["k_owninside"] = "high_line", ["k_latebrake"] = "send_it", ["k_door"] = "bump_run", ["k_cover"] = "block",
            ["k_smooth"] = "save_tires", ["k_limit"] = "push_limit", ["k_board"] = "spotter_focus",
        };

        /// <summary>A kart persona grows into a stock car persona on promotion (the slice's ARCH up).</summary>
        public static string PersonaUp(string kartPersona) =>
            kartPersona == "brawler" ? "bully" : kartPersona == "linepainter" ? "whisperer" : kartPersona == "packrat" ? "drafter" : kartPersona == "latebraker" ? "restart" : null;
    }
}
