using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    // The class of rivals in the save file (format version 8, roadmap step 43).
    public sealed partial class Career
    {
        static JsonValue RivalLinesJson(List<RivalLine> v)
        {
            var a = JsonValue.Arr();
            foreach (var l in v)
            {
                var why = JsonValue.Arr();
                foreach (var (k, d) in l.Why) why.Add(JsonValue.Arr().Add(JsonValue.Of(k)).Add(JsonValue.Of(d)));
                a.Add(JsonValue.Obj().Set("rid", l.Rid).Set("name", l.Name).Set("g0", l.G0).Set("g1", l.G1).Set("week", l.Week).Set("fin", l.Fin).Set("why", why));
            }
            return a;
        }

        static List<RivalLine> RivalLinesFrom(JsonValue j) => j.Items.Select(l => new RivalLine
        {
            Rid = l["rid"].Str(), Name = l["name"].Str(""), G0 = l["g0"].Double(), G1 = l["g1"].Double(), Week = l["week"].Bool(), Fin = l["fin"].Int(),
            Why = l["why"].Items.Select(w => (w[0].Str(""), w[1].Double())).ToList(),
        }).ToList();

        void RivalsToJson(JsonValue j)
        {
            var cl = JsonValue.Arr();
            foreach (var d in Class)
            {
                var hist = JsonValue.Arr();
                foreach (var h in d.Hist) hist.Add(JsonValue.Obj().Set("season", h.Season).Set("race", h.Race).Set("lap", h.Lap).Set("kind", h.Kind).Set("track", h.Track));
                cl.Add(JsonValue.Obj().Set("id", d.Id).Set("series", d.Series.ToString()).Set("team", d.Team).Set("skill", d.Skill).Set("g", d.G)
                    .Set("rec", Ints(new[] { d.Ahead, d.Behind, d.Starts, d.Wins, d.Titles, d.Seasons, d.LastRank, d.Met }))
                    .Set("retired", d.Retired).Set("hist", hist).Set("path", JsonValue.Of(d.Path)));
            }
            var inbox = JsonValue.Arr();
            foreach (var m in Inbox)
                inbox.Add(JsonValue.Obj().Set("id", m.Id).Set("rid", m.Rid).Set("kind", m.Kind).Set("track", m.Track).Set("subj", m.Subj).Set("body", m.Body)
                    .Set("season", m.Season).Set("race", m.Race).Set("g", m.G).Set("reply", m.Reply).Set("replyText", m.ReplyText).Set("tier", m.Tier?.ToString()).Set("roll", m.Roll));
            j.Set("class", JsonValue.Obj().Set("rng", (long)(ClassRng?.State ?? 0)).Set("drivers", cl).Set("inbox", inbox).Set("msgN", msgN).Set("news", JsonValue.Of(News)));
        }

        void RivalsFromJson(JsonValue j)
        {
            var k = j["class"];
            if (k.IsNull) { ClassFromOldSave(); return; }
            ClassRng = new SimRng(k["rng"].UInt());
            Class = k["drivers"].Items.Select(d =>
            {
                var rec = IntsFrom(d["rec"]);
                if (rec.Count != 8) throw new FormatException("Save file: a class driver's record needs 8 numbers");
                var c = new ClassDriver
                {
                    Id = d["id"].Str(), Series = d["series"].Enum(Series.Stock), Team = d["team"].Int(), Skill = d["skill"].Double(), G = RivalBook.Q(d["g"].Double()),
                    Ahead = rec[0], Behind = rec[1], Starts = rec[2], Wins = rec[3], Titles = rec[4], Seasons = rec[5], LastRank = rec[6], Met = rec[7],
                    Retired = d["retired"].Bool(), Path = d["path"].Strings(),
                    Hist = d["hist"].Items.Select(h => new RivalHist { Season = h["season"].Int(), Race = h["race"].Int(), Lap = h["lap"].Int(), Kind = h["kind"].Str(), Track = h["track"].Str("") }).ToList(),
                };
                RivalBook.Get(c.Id);
                if (c.Team < 0 || c.Team >= Teams.For(c.Series).Length) throw new FormatException($"Save file: class driver {c.Id} on no team {c.Team}");
                return c;
            }).ToList();
            if (Class.Select(d => d.Id).Distinct().Count() != Class.Count) throw new FormatException("Save file: a class driver is listed twice");
            Inbox = k["inbox"].Items.Select(m => new RivalMsg
            {
                Id = m["id"].Str(), Rid = m["rid"].Str(), Kind = m["kind"].Str(), Track = m["track"].Str(), Subj = m["subj"].Str(""), Body = m["body"].Str(""),
                Season = m["season"].Int(), Race = m["race"].Int(), G = m["g"].Double(), Reply = m["reply"].Str(), ReplyText = m["replyText"].Str(),
                Tier = m["tier"].IsNull ? (CheckTier?)null : m["tier"].Enum(CheckTier.Mixed), Roll = m["roll"].Str(),
            }).ToList();
            foreach (var m in Inbox) { if (ClassById(m.Rid) == null) throw new FormatException($"Save file: a message from {m.Rid}, who isn't in the class"); if (Array.IndexOf(RivalBook.Kinds, m.Kind) < 0) throw new FormatException($"Save file: unknown message kind {m.Kind}"); }
            msgN = k["msgN"].Int();
            News = k["news"].Strings();
            foreach (var e in Field.Where(x => x.Rival != null)) if (ClassById(e.Rival) == null) throw new FormatException($"Save file: {e.Name} is class driver {e.Rival}, who isn't in the class");
        }

        /// <summary>
        /// A save from before step 43 (format 7 or older) has no class. It gets one built from its own seed: the drivers start
        /// where the career started (its first series), the off-seasons it has already played run abstractly so the class has
        /// moved on as far as you have, and the class drivers of your current series take seats in the field now (renaming a
        /// field driver each, who keeps the seat's points). No Grudge, no messages yet.
        /// </summary>
        void ClassFromOldSave()
        {
            NewClass(Ladder.Count > 0 ? Ladder[0] : Series);
            for (int s = 1; s < Season; s++) PastSeasonClass(s);
            msgN = 0; Inbox = new List<RivalMsg>(); News = new List<string>();
            SeatClass();
        }

        /// <summary>One off-season for the class, played away from your field (old saves only).</summary>
        void PastSeasonClass(int season)
        {
            foreach (var d in Class.Where(x => !x.Retired))
            {
                var def = d.Def;
                int rank = SimRank(d);
                d.Seasons++; d.LastRank = rank; d.Starts += CareerRules.Calendar(d.Series).Count;
                if (rank == 1) { d.Titles++; d.Wins++; }
                d.Path.Add($"Season {season}: {SeasonRules.SeriesName(d.Series)}, P{rank} for {Teams.For(d.Series)[d.Team].Name}");
                d.Skill = Math.Max(1, Math.Min(Math.Max(def.Peak, d.Skill), d.Skill + def.Grow));
                if (def.Retire > 0 && d.Seasons >= def.Retire) { d.Retired = true; continue; }
                var nx = SeasonRules.Next(d.Series);
                if (nx != null && SeasonRules.Rung(nx.Value) <= SeasonRules.Rung(def.Cap) && (rank <= 3 || (rank <= 10 && d.Skill >= 5) || (def.Money && rank <= 12)))
                {
                    d.Series = nx.Value;
                    d.Team = PlaceTeam(d, nx.Value, def.Money ? 7 : rank <= 3 ? 5 : 4, -1);
                }
            }
        }

        /// <summary>Save format 7 to 8 (step 43): the class of rivals. Nothing in the file changes; a career with no "class"
        /// builds one on load (ClassFromOldSave).</summary>
        public static JsonValue MigrateV7(JsonValue c) => c;
    }
}
