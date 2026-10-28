using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    // The driver's part of the save file (format version 3, roadmap step 38): the sheet, tonight's event with any answer on
    // the table, and the events held this season.
    public sealed partial class Career
    {
        static JsonValue LineJson(ModLine l) => JsonValue.Arr().Add(JsonValue.Of((long)l.V)).Add(JsonValue.Of(l.Src)).Add(JsonValue.Of(l.K));
        static ModLine LineFrom(JsonValue j) => new ModLine(j[0].Int(), j[1].Str(""), j[2].Str("sit"));
        static JsonValue Lines(IEnumerable<ModLine> v) { var a = JsonValue.Arr(); foreach (var l in v) a.Add(LineJson(l)); return a; }

        public static JsonValue DriverJson(Driver d)
        {
            var attr = JsonValue.Obj();
            foreach (DAttr a in Enum.GetValues(typeof(DAttr))) attr.Set(a.ToString(), d.Score(a));
            var trail = JsonValue.Arr();
            foreach (var p in d.Trail) trail.Add(Ints(p));
            var log = JsonValue.Arr();
            foreach (var l in d.Log) log.Add(JsonValue.Obj().Set("ev", l.Ev).Set("opt", l.Opt).Set("skill", l.Skill.ToString()).Set("tier", l.Tier.ToString()).Set("txt", l.Txt).Set("s", l.Season).Set("r", l.Race));
            return JsonValue.Obj().Set("bg", d.Bg).Set("attr", attr).Set("prof", JsonValue.Of(d.Prof.Select(x => x.ToString())))
                .Set("lvl", d.Lvl).Set("xp", d.Xp).Set("pts", d.Pts).Set("hype", d.Hype).Set("fans", d.Fans).Set("respect", d.Respect)
                .Set("img", Ints(new[] { d.ImgX, d.ImgY })).Set("trail", trail).Set("log", log).Set("nextMom", d.NextMom);
        }

        public static Driver DriverFrom(JsonValue j)
        {
            var d = new Driver { Bg = j["bg"].Str("hero") };
            DriverRules.Bg(d.Bg);
            foreach (DAttr a in Enum.GetValues(typeof(DAttr)))
            {
                int v = j["attr"][a.ToString()].Int(10);
                if (v < 1 || v > DriverRules.AttrMax) throw new FormatException($"Save file: {a} {v} is outside 1 to {DriverRules.AttrMax}");
                d.Attr[(int)a] = v;
            }
            d.Prof = j["prof"].Strings().Select(s => JsonValue.Of(s).Enum(DSkill.Media)).ToList();
            d.Lvl = Math.Max(1, j["lvl"].Int(1)); d.Xp = j["xp"].Int(); d.Pts = j["pts"].Int();
            d.Hype = (int)SimMath.Clamp(j["hype"].Int(), 0, DriverRules.HypeMax); d.Fans = j["fans"].Int(35); d.Respect = j["respect"].Int(35);
            var img = IntsFrom(j["img"]);
            if (img.Count == 2) { d.ImgX = img[0]; d.ImgY = img[1]; }
            d.Trail = j["trail"].Items.Select(p => IntsFrom(p).ToArray()).Where(p => p.Length == 2).ToList();
            d.Log = j["log"].Items.Select(l => new CheckLog { Ev = l["ev"].Str(""), Opt = l["opt"].Str(""), Skill = l["skill"].Enum(DSkill.Media), Tier = l["tier"].Enum(CheckTier.Fail), Txt = l["txt"].Str(""), Season = l["s"].Int(), Race = l["r"].Int() }).ToList();
            d.NextMom = (int)SimMath.Clamp(j["nextMom"].Int(), 0, DriverRules.MomMax);
            return d;
        }

        static JsonValue OtFxJson(OffFx f)
        {
            if (f == null) return JsonValue.Null;
            var j = JsonValue.Obj();
            if (f.Fans != 0) j.Set("fans", f.Fans);
            if (f.Respect != 0) j.Set("respect", f.Respect);
            if (f.Hype != 0) j.Set("hype", f.Hype);
            if (f.Money != 0) j.Set("money", f.Money);
            if (f.Trust != 0) j.Set("trust", f.Trust);
            if (f.Per != 0) j.Set("per", f.Per);
            if (f.Bonus != 0) j.Set("bonus", f.Bonus);
            if (f.Mom != 0) j.Set("mom", f.Mom);
            if (f.Sal != null) j.Set("sal", f.Sal.Value);
            if (f.Clause != null) j.Set("clause", f.Clause);
            if (f.Adv) j.Set("adv", true);
            if (f.Reveal) j.Set("reveal", true);
            return j;
        }

        static OffFx OtFxFrom(JsonValue j) => j.IsNull ? null : new OffFx
        {
            Fans = j["fans"].Int(), Respect = j["respect"].Int(), Hype = j["hype"].Int(), Money = j["money"].Int(), Trust = j["trust"].Int(), Per = j["per"].Int(), Bonus = j["bonus"].Int(), Mom = j["mom"].Int(),
            Sal = j["sal"].IsNull ? (double?)null : j["sal"].Double(), Clause = j["clause"].Str(), Adv = j["adv"].Bool(), Reveal = j["reveal"].Bool(),
        };

        static JsonValue BuildJson(CheckBuild b) => JsonValue.Obj().Set("skill", b.Skill.ToString()).Set("l", Lines(b.L)).Set("dcl", Lines(b.Dcl))
            .Set("adv", JsonValue.Of(b.Adv)).Set("dis", JsonValue.Of(b.Dis)).Set("dc", b.BaseDc);

        /// <summary>A build from the save: its lines, re-summed (Finish), so the numbers can't disagree with their sources.</summary>
        static CheckBuild BuildFrom(JsonValue j) => DriverRules.Finish(new CheckBuild
        {
            Skill = j["skill"].Enum(DSkill.Media), L = j["l"].Items.Select(LineFrom).ToList(), Dcl = j["dcl"].Items.Select(LineFrom).ToList(),
            Adv = j["adv"].Strings(), Dis = j["dis"].Strings(), BaseDc = j["dc"].Int(10),
        });

        static JsonValue RollJson(CheckRoll r) => r == null ? JsonValue.Null : Ints(new[] { r.A, r.B });

        static CheckRoll RollFrom(JsonValue j, CheckBuild b)
        {
            if (j.IsNull) return null;
            var d = IntsFrom(j);
            if (d.Count != 2 || d.Any(x => x < 1 || x > 20)) throw new FormatException("Save file: a check needs two dice from 1 to 20");
            int kept = b.Mode == RollMode.Adv ? Math.Max(d[0], d[1]) : b.Mode == RollMode.Dis ? Math.Min(d[0], d[1]) : d[0];
            return new CheckRoll { A = d[0], B = d[1], Kept = kept, Other = kept == d[0] ? d[1] : d[0], Total = kept + b.Mod, Tier = DriverRules.Tier(kept, b.Mod, b.Dc) };
        }

        static JsonValue CtxJson(CheckCtx x) => JsonValue.Obj().Set("ev", x.Ev).Set("track", x.Track).Set("trackId", x.TrackId).Set("rival", x.Rival)
            .Set("fin", x.Fin).Set("start", x.Start).Set("exp", x.Exp).Set("dmg", x.Dmg).Set("dnf", x.Dnf).Set("top3", x.Top3).Set("big", x.Big).Set("beatRival", x.BeatRival);

        static CheckCtx CtxFromJson(JsonValue j) => new CheckCtx
        {
            Ev = j["ev"].Str(), Track = j["track"].Str(""), TrackId = j["trackId"].Str(), Rival = j["rival"].Str(), Fin = j["fin"].Int(), Start = j["start"].Int(), Exp = j["exp"].Int(), Dmg = j["dmg"].Int(),
            Dnf = j["dnf"].Bool(), Top3 = j["top3"].Bool(), Big = j["big"].Bool(), BeatRival = j["beatRival"].Bool(),
        };

        public static JsonValue EventJson(OffTrackEvent ev)
        {
            if (ev == null) return JsonValue.Null;
            var hist = JsonValue.Arr();
            foreach (var h in ev.Hist) hist.Add(JsonValue.Obj().Set("ri", h.Ri).Set("opt", h.Opt).Set("n", h.Name).Set("auto", h.Auto).Set("tier", h.Tier.ToString()).Set("fx", OtFxJson(h.Fx)));
            var j = JsonValue.Obj().Set("id", ev.Id).Set("ri", ev.Ri).Set("season", ev.Season).Set("after", ev.After).Set("ctx", CtxJson(ev.Ctx)).Set("ceil", ev.Ceil)
                .Set("revealed", ev.Revealed).Set("advFor", ev.AdvFor).Set("hist", hist).Set("done", ev.Done).Set("walked", ev.Walked);
            var c = ev.Cur;
            if (c == null) return j.Set("cur", JsonValue.Null);
            var tiers = JsonValue.Arr();
            foreach (var f in c.Tiers) tiers.Add(OtFxJson(f));
            return j.Set("cur", JsonValue.Obj().Set("opt", c.Opt).Set("n", c.Name).Set("say", c.Say).Set("hit", c.Hit).Set("miss", c.Miss).Set("tone", Ints(new[] { c.TX, c.TY }))
                .Set("hype", c.Hype).Set("rerolled", c.Rerolled).Set("b", BuildJson(c.B)).Set("r", RollJson(c.R)).Set("r0", RollJson(c.R0)).Set("tiers", tiers).Set("info", c.Info.ToString()));
        }

        public static OffTrackEvent EventFrom(JsonValue j)
        {
            if (j.IsNull) return null;
            var ev = new OffTrackEvent
            {
                Id = j["id"].Str(), Ri = j["ri"].Int(), Season = j["season"].Int(1), After = j["after"].Int(), Ctx = CtxFromJson(j["ctx"]), Ceil = j["ceil"].Int(),
                Revealed = j["revealed"].Bool(), AdvFor = j["advFor"].Int(-1), Done = j["done"].Bool(), Walked = j["walked"].Bool(),
                Hist = j["hist"].Items.Select(h => new EvHist { Ri = h["ri"].Int(), Opt = h["opt"].Str(), Name = h["n"].Str(""), Auto = h["auto"].Bool(), Tier = h["tier"].Enum(CheckTier.Fail), Fx = OtFxFrom(h["fx"]) }).ToList(),
            };
            var E = OffTrack.Get(ev.Id);
            if (ev.Ri < 0 || ev.Ri > E.Rounds.Count || (!ev.Done && ev.Ri >= E.Rounds.Count)) throw new FormatException($"Save file: the {E.Name} has no question {ev.Ri + 1}");
            var c = j["cur"];
            if (!c.IsNull)
            {
                var b = BuildFrom(c["b"]);
                var tone = IntsFrom(c["tone"]);
                ev.Cur = new EvPending
                {
                    Opt = c["opt"].Str(), Name = c["n"].Str(""), Say = c["say"].Str(""), Hit = c["hit"].Str(""), Miss = c["miss"].Str(""), TX = tone.Count == 2 ? tone[0] : 0, TY = tone.Count == 2 ? tone[1] : 0,
                    Hype = c["hype"].Bool(), Rerolled = c["rerolled"].Bool(), B = b, R = RollFrom(c["r"], b), R0 = RollFrom(c["r0"], b),
                    Tiers = c["tiers"].Items.Select(f => OtFxFrom(f) ?? new OffFx()).ToArray(), Info = c["info"].Enum(InfoLevel.Full),
                };
                if (ev.Cur.R == null || ev.Cur.Tiers.Length != 5) throw new FormatException("Save file: the answer on the table needs a roll and five results");
            }
            return ev;
        }

        static JsonValue NegJson(NegResult n) => n == null ? JsonValue.Null : JsonValue.Obj().Set("kept", n.Kept).Set("other", n.Other).Set("mod", n.Mod).Set("dc", n.Dc).Set("total", n.Total).Set("tier", n.Tier.ToString()).Set("txt", n.Txt);
        static NegResult NegFrom(JsonValue j) => j.IsNull ? null : new NegResult { Kept = j["kept"].Int(), Other = j["other"].Int(), Mod = j["mod"].Int(), Dc = j["dc"].Int(), Total = j["total"].Int(), Tier = j["tier"].Enum(CheckTier.Mixed), Txt = j["txt"].Str("") };

        void DriverToJson(JsonValue j) => j.Set("driver", DriverJson(Driver)).Set("event", EventJson(Event)).Set("eventsHeld", JsonValue.Of(EventsHeld));

        void DriverFromJson(JsonValue j)
        {
            Driver = DriverFrom(j["driver"]);
            Event = EventFrom(j["event"]);
            EventsHeld = j["eventsHeld"].Strings();
            foreach (var id in EventsHeld) OffTrack.Get(id);
        }

        /// <summary>
        /// Save format 2 to 3 (step 38): a version 2 career had no driver sheet. It gets the driver its persona starts with
        /// (the persona's background, with the Grit and Wits the career already raced with), no event tonight, no events
        /// held this season, sponsor trust 0, no contract clauses, and offers not yet negotiated.
        /// </summary>
        public static JsonValue MigrateV2(JsonValue c)
        {
            var pid = c["persona"].Str("drafter");
            var d = Driver.New(Personas.All.FirstOrDefault(x => x.Id == pid)?.Background ?? "hero");
            var b = c["base"];
            d.Attr[(int)DAttr.Grit] = b["grit"].Int(d.Score(DAttr.Grit));
            d.Attr[(int)DAttr.Wits] = b["wits"].Int(d.Score(DAttr.Wits));
            c.Set("driver", DriverJson(d)).Set("event", JsonValue.Null).Set("eventsHeld", JsonValue.Arr());
            c["contract"].Set("clauses", JsonValue.Arr());
            return c;
        }
    }
}
