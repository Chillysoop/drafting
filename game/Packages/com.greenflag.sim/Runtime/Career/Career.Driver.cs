using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>A season-end Negotiate check on a contract offer (the slice's o.neg): the roll and what it did to the terms.</summary>
    public sealed class NegResult
    {
        public int Kept, Other, Mod, Dc, Total;
        public CheckTier Tier;
        public string Txt;
        public NegResult Clone() => (NegResult)MemberwiseClone();
    }

    // The driver sheet and the off-track events (roadmap step 38), ported from slice v41 (driver-js) and v45 (late-js
    // negotiation): the sheet, Hype and XP from each race, Momentum promised for the next green flag, Grit and Wits on race
    // day, tonight's event after a race (Go racing waits until it's done or skipped), and the d20 Negotiate check on each
    // season-end contract offer. Every roll comes from a generator seeded by the career's seed and where it is in the
    // career, so quitting and reloading can't reroll a check.
    public sealed partial class Career
    {
        public Driver Driver = new Driver();
        /// <summary>Tonight's event (null when no event follows the last race).</summary>
        public OffTrackEvent Event;
        /// <summary>The events held this season, so most come once a season.</summary>
        public List<string> EventsHeld = new List<string>();

        /// <summary>Off-track money is written at stock car pay; it scales with the series' pay like everything else (board Q121).</summary>
        public double MoneyK => CareerRules.Econ(Series) / CareerRules.Econ(Series.Stock);
        static int R50(double v) => (int)(SimMath.RoundJs(v / 50) * 50);

        public OffFx MoneyFx(OffFx f)
        {
            var o = f.Clone();
            double k = MoneyK;
            o.Money = R50(f.Money * k); o.Per = R50(f.Per * k); o.Bonus = R50(f.Bonus * k);
            return o;
        }

        public static string MoneyText(int v) => (v < 0 ? "−$" : "$") + Math.Abs(v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        static CheckCtx CtxFrom(RaceReport r) => r == null ? new CheckCtx() : new CheckCtx
        {
            Fin = r.Finish, Start = r.Start, Exp = r.Expected, Dnf = r.Dnf, Dmg = r.Dmg, Track = r.TrackName, TrackId = r.TrackId, Rival = r.Rival, BeatRival = r.BeatRival,
            Top3 = !r.Dnf && r.Finish <= 3, Big = !r.Dnf && r.Expected - r.Finish >= 3,
        };

        static uint Hash(string s) { uint h = 2166136261u; foreach (char ch in s) { h ^= ch; h *= 16777619u; } return h; }

        // ------------------------------------------------------------------ after a race

        /// <summary>The driver's side of FinishRace: XP for racing, Hype for a ×4 combo or a big finish, the damage for the
        /// next event's context, and the promised Momentum is used up.</summary>
        void DriverAfterRace(Race race, RaceReport res)
        {
            res.Dmg = (int)Math.Round(race.Me.Damage);
            res.DrvMom = Driver.NextMom;
            Driver.NextMom = 0;
            res.HypeWhy = DriverRules.RaceHype(res);
            res.Hype = Math.Min(res.HypeWhy.Count, DriverRules.HypeMax - Driver.Hype);
            Driver.Hype += res.Hype;
            res.Xp = DriverRules.XpRace;
            res.LvlUp = Driver.AddXp(DriverRules.XpRace).Count;
        }

        /// <summary>Puts up tonight's event, if the season has one after this many races: the fixed slots (press, dinner,
        /// contract talk) or a draw from the events that fit the last race.</summary>
        void RollEvent()
        {
            Event = null;
            var slot = OffTrack.Slots(Series).FirstOrDefault(s => s.after == RaceIdx);
            if (slot.after == 0) return;
            var res = SeasonResults.LastOrDefault();
            var ctx = CtxFrom(res);
            var rng = new SimRng(RaceSeed ^ 0x0FF7A3u);
            string id = slot.fixedId ?? OffTrack.Pick(this, ctx, EventsHeld, rng);
            if (id == null || (id == "dinner" && Sponsor == null)) return;
            ctx.Ev = id;
            int rating = SeriesTeams[Team].Rating;
            Event = new OffTrackEvent { Id = id, Season = Season, After = RaceIdx, Ctx = ctx, Ceil = R50(SeasonRules.SeriesPay(Series) * rating * (1.4 + 0.6 * rng.NextDouble())) };
            EventsHeld.Add(id);
        }

        // ------------------------------------------------------------------ the event

        public EvRound EventRound() => Event == null || Event.Done ? null : OffTrack.Round(this, Event);

        /// <summary>Every number on an answer's check right now (the slice's chkBuild): the sheet's lines, the event's
        /// situation, and Hype spent for advantage.</summary>
        public CheckBuild CheckFor(EvOption o, bool hype = false)
        {
            var ev = Event;
            var b = DriverRules.Start(Driver, o.Skill, o.Dc, ev?.Ctx, o.TX, o.TY, o.Sig);
            var s = ev?.Def.Sit?.Invoke(this, ev, o);
            if (s != null) { b.L.AddRange(s.L); b.Dcl.AddRange(s.Dcl); b.Adv.AddRange(s.Adv); b.Dis.AddRange(s.Dis); }
            if (hype) b.Adv.Add("Spent 1 Hype");
            return DriverRules.Finish(b);
        }

        /// <summary>What each tier of an answer would do, at this series' pay.</summary>
        public OffFx[] TierFxFor(EvOption o) => Enum.GetValues(typeof(CheckTier)).Cast<CheckTier>().Select(t => MoneyFx(OffTrack.TierFx(o, t, Event.Def))).ToArray();

        /// <summary>The check's dice: the race seed, the event, the question and the attempt (0, or 1 for a Hype reroll).
        /// Every answer to the same question rolls the same dice.</summary>
        SimRng EvRng(int attempt) => new SimRng(RaceSeed ^ (0x2D20u + Hash(Event.Id) * 31u + (uint)Event.Ri * 977u + (uint)attempt * 7919u));

        /// <summary>Says an answer. A roll waits on the table (Cur) until Next; an auto answer moves straight on. The tone
        /// moves your image after the check is built, so the in-character bonus uses the image you walked in with.
        /// Returns why not, or null.</summary>
        public string EventChoose(string optId, bool hype = false)
        {
            var ev = Event;
            if (ev == null || ev.Done) return "No event tonight";
            if (ev.Cur != null) return "Take the result first";
            var o = EventRound().Opts.FirstOrDefault(x => x.Id == optId);
            if (o == null) return "No such answer";
            hype = hype && !o.Auto && Driver.Hype > 0;
            var b = o.Auto ? null : CheckFor(o, hype);
            var tiers = o.Auto ? null : TierFxFor(o);
            Driver.MoveImage(o.TX, o.TY);
            if (o.Auto)
            {
                ev.Hist.Add(new EvHist { Ri = ev.Ri, Opt = o.Id, Name = o.Name, Auto = true });
                NextRound();
                return null;
            }
            if (hype) Driver.Hype--;
            var R = DriverRules.Roll(b, EvRng(0));
            ev.Cur = new EvPending { Opt = o.Id, Name = o.Name, Say = o.Say, Hit = o.Hit, Miss = o.Miss, TX = o.TX, TY = o.TY, Hype = hype, B = b, R = R, Tiers = tiers, Info = Info };
            return null;
        }

        /// <summary>Spends 1 Hype to reroll a miss before taking it.</summary>
        public bool EventReroll()
        {
            var c = Event?.Cur;
            if (c == null || c.Rerolled || Driver.Hype <= 0 || c.R.Made) return false;
            Driver.Hype--;
            c.R0 = c.R;
            c.R = DriverRules.Roll(c.B, EvRng(1));
            c.Rerolled = true;
            return true;
        }

        public bool CanReroll => Event?.Cur != null && !Event.Cur.Rerolled && Driver.Hype > 0 && !Event.Cur.R.Made;

        /// <summary>Takes the result on the table (its effects, XP and the log line) and goes to the next question, or ends
        /// the event after the last one.</summary>
        public bool EventNext()
        {
            var ev = Event;
            if (ev == null || ev.Done || ev.Cur == null) return false;
            var c = ev.Cur;
            var fx = c.Fx;
            ApplyFx(fx, ev);
            c.Xp = c.R.Made ? DriverRules.XpMade : DriverRules.XpMissed;
            Driver.AddXp(c.Xp);
            Driver.Log.Insert(0, new CheckLog { Ev = ev.Def.Name, Opt = c.Name, Skill = c.B.Skill, Tier = c.R.Tier, Txt = $"{c.R.Kept} {DriverRules.Sgn(c.B.Mod)} = {c.R.Total} vs DC {c.B.Dc}", Season = Season, Race = SeasonResults.Count });
            if (Driver.Log.Count > 8) Driver.Log.RemoveRange(8, Driver.Log.Count - 8);
            ev.Hist.Add(new EvHist { Ri = ev.Ri, Opt = c.Opt, Name = c.Name, Tier = c.R.Tier, Fx = fx });
            ev.Cur = null;
            NextRound();
            return true;
        }

        void NextRound()
        {
            var ev = Event;
            ev.Ri++;
            if (ev.Ri >= ev.Def.Rounds.Count) ev.Done = true;
        }

        /// <summary>Skips the rest of the event at its stated cost (not with a roll on the table).</summary>
        public bool EventWalk()
        {
            var ev = Event;
            if (ev == null || ev.Done || ev.Cur != null) return false;
            ApplyFx(MoneyFx(ev.Def.Walk), ev);
            ev.Walked = ev.Done = true;
            return true;
        }

        /// <summary>What the answer on the table missed by and what would have helped (the slice's chkHelp).</summary>
        public List<string> EventHelp()
        {
            var c = Event?.Cur;
            if (c == null) return new List<string>();
            var others = EventRound().Opts.Where(x => x.Id != c.Opt && !x.Auto).Select(x => (x.Name, CheckFor(x))).ToList();
            return DriverRules.Help(Driver, c.B, c.R, c.Name, c.B.Skill, others, c.TX, c.TY);
        }

        /// <summary>The slice's applyFx: meters clamp (fans and respect 0 to 100, Hype 0 to 3, trust −2 to 2), money can't go
        /// below zero, the sponsor's per-race money never drops under $500 at stock car pay.</summary>
        public void ApplyFx(OffFx fx, OffTrackEvent ev)
        {
            var d = Driver;
            if (fx.Fans != 0) d.Fans = (int)SimMath.Clamp(d.Fans + fx.Fans, 0, 100);
            if (fx.Respect != 0) d.Respect = (int)SimMath.Clamp(d.Respect + fx.Respect, 0, 100);
            if (fx.Hype != 0) d.Hype = (int)SimMath.Clamp(d.Hype + fx.Hype, 0, DriverRules.HypeMax);
            if (fx.Money != 0) Money = Math.Max(0, Money + fx.Money);
            if (Sponsor != null)
            {
                if (fx.Trust != 0) Sponsor.Trust = (int)SimMath.Clamp(Sponsor.Trust + fx.Trust, -2, 2);
                if (fx.Per != 0) Sponsor.Per = Math.Max(R50(500 * MoneyK), Sponsor.Per + fx.Per);
                if (fx.Bonus != 0) Sponsor.Bonus = Math.Max(0, Sponsor.Bonus + fx.Bonus);
            }
            if (fx.Sal != null && ev != null) { Contract.Per = R50(ev.Ceil * fx.Sal.Value); Contract.Since = Season; }
            if (fx.Mom != 0) d.NextMom = Math.Min(DriverRules.MomMax, d.NextMom + fx.Mom);
            if (fx.Clause != null)
            {
                if (fx.Clause == "crewChief") Base.CrewChief = Math.Min(10, Base.CrewChief + 1);
                else if (CareerRules.Stat(Base.Car, fx.Clause) < CareerRules.StatMax) Base.AddStat(fx.Clause, 1);
                Contract.Clauses.Add(fx.Clause);
            }
            if (ev != null && fx.Adv) ev.AdvFor = ev.Ri + 1;
            if (ev != null && fx.Reveal) ev.Revealed = true;
        }

        public static string ClauseName(string k) => k == "power" ? "+1 Power" : k == "handling" ? "+1 Handling" : k == "crewChief" ? "+1 Crew chief" : k;

        // ------------------------------------------------------------------ the sheet

        /// <summary>Spends a level-up point. Grit and Wits reach the race through the loadout.</summary>
        public string RaiseAttr(DAttr a)
        {
            var why = Driver.RaiseAttr(a);
            SyncDriverStats();
            return why;
        }

        void SyncDriverStats() { Base.Grit = Driver.Score(DAttr.Grit); Base.Wits = Driver.Score(DAttr.Wits); }

        // ------------------------------------------------------------------ season-end negotiation

        /// <summary>The d20 Negotiate check on a contract offer (the slice's negBuild): Savvy, proficiency, your trait or flaw,
        /// against DC 10 + half the team's rating, +1 to step up a series.</summary>
        public CheckBuild NegBuild(ContractOffer o)
        {
            var b = DriverRules.Start(Driver, DSkill.Negotiate, 10 + o.Rating / 2 + (o.Kind == OfferKind.Promote ? 1 : 0), null);
            return DriverRules.Finish(b);
        }

        /// <summary>What each tier does to the salary and the signing bonus (the slice's NEG_FX).</summary>
        public static readonly (double k, string txt)[] NegFx =
        {
            (1.3, "Critical: +30% salary and signing bonus."), (1.15, "Success: +15% salary and signing bonus."), (1, "Mixed: they hold firm. Same terms."),
            (0.9, "Failure: you push too hard. −10%."), (0.8, "Fumble: you insult the owner. −20%."),
        };

        /// <summary>Negotiates offer i once, before you sign. Returns the result, or null when it can't.</summary>
        public NegResult Negotiate(int i)
        {
            if (Offers == null || i < 0 || i >= Offers.Count || Offers[i].Neg != null) return null;
            var o = Offers[i];
            var b = NegBuild(o);
            var R = DriverRules.Roll(b, new SimRng(Seed * 2654435761u ^ (uint)(Season * 7919 + i * 131) ^ 0x4E60u));
            var f = NegFx[(int)R.Tier];
            o.Neg = new NegResult { Kept = R.Kept, Other = R.Other, Mod = b.Mod, Dc = b.Dc, Total = R.Total, Tier = R.Tier, Txt = f.txt };
            o.Per = R50(o.Per * f.k); o.Sign = R50(o.Sign * f.k);
            return o.Neg;
        }
    }
}
