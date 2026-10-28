using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.Harness
{
    /// <summary>One race's numbers, with the same names as the browser harness (prototype/harness/sim-harness-v10.js) writes.</summary>
    public sealed class RaceRow
    {
        public int s, f, e, dnf, pl, su, cr, beats, hz, calls, cwon, ndnf, fol, combos, chain, board, exp, toss, stops, perfect, setups, pk, cautions, tfx, tlive, rref, rep, push;
        public double slow, dur, hand, dead, idle, empty, dry, stopT, fgain, odds;
        public List<StopRow> sd = new List<StopRow>();
    }

    /// <summary>One pit stall visit: cards played, failed rolls, release error (+ early), window, lane total, loose wheels, Perfect, crew chief release.</summary>
    public sealed class StopRow
    {
        public int n, f, lo, p, ch;
        public double e, w, T;
    }

    /// <summary>
    /// The browser harness's play styles, move for move (sim-harness-v10.js runRace). Every tick: the race moves 0.05 s, then
    /// the bot presses PIT on the plan lap, takes crew calls at 55%+ (all but none), calls cautions by tires and damage, and plays
    /// (or tosses) cards.
    ///   none    hands-off: holds a hand, never plays, never presses PIT, the crew chief runs the stops and caution calls
    ///   smart   instant bot: plays Evade or Reel Them In at once, else the best card at 65%+ with under 5% wreck
    ///   spam    instant bot: always plays the best card
    ///   human   smart's rules with human timing: looks every 2.5 s, reacts in 1.5 s (0.15 s of race time in slow-mo while
    ///           Focus lasts), Evade after 2 s (0.2 s in slow-mo); at each draw beat the race pauses, so it tosses dead cards,
    ///           tosses a weak card (under 55%) when its toss effect is live and plays its pick at once
    ///   human2  human, but tosses only dead or under 40% cards
    ///   bold    human timing, plays 50%+ with under 8% wreck
    ///   hspam   human timing, always plays the best card, never tosses for effect
    /// The pit mini-game is played by PitPolicy.Bot (Setups, release at a random point up to 1.3 windows after the drop) for
    /// every style but none.
    /// </summary>
    public static class Bots
    {
        public static readonly string[] Styles = { "none", "smart", "spam", "human", "human2", "bold", "hspam" };
        public static bool IsStyle(string p) => Array.IndexOf(Styles, p) >= 0;
        static bool Human(string p) => p == "human" || p == "human2" || p == "bold" || p == "hspam";

        /// <summary>The slice pauses at each draw beat (BAL.beatPause defaults to true and the engine preset keeps it).</summary>
        public const bool BeatPause = true;

        /// <summary>The lowest call chance the bots take (the browser harness's bal.callMin, default 55).</summary>
        public static int CallMin = 55;

        /// <summary>Step 41's tire strategy check: how every style but none calls a caution. "bot" = the browser harness's tire rule
        /// (stay on 60%+ tires, two on 40%+, else four), "chief" = take the crew chief's call, or a fixed "stay", "two" or "four".</summary>
        public static string CautionPolicy = "bot";

        sealed class Pending { public int Uid; public double At; public bool Hazard; }

        public static (Race race, RaceRow row) Run(TrackDef track, uint seed, string style, string persona, Action<Race> setup = null)
        {
            var lo = PlayerLoadout.ForPersona(Program.PersonaFor(track, persona));
            var rs = RaceSetup.Quick(track, seed, lo);
            rs.PitPolicy = style == "none" ? PitPolicy.Chief : PitPolicy.Bot;
            var r = new Race(rs);
            setup?.Invoke(r);
            return (r, Drive(r, style));
        }

        /// <summary>Plays a race that is already set up (a career's, say) to the flag with one of the styles.</summary>
        public static RaceRow Drive(Race r, string style)
        {
            var row = new RaceRow();
            var bal = r.Bal;
            string pol = style == "human" || style == "human2" ? "smart" : style;
            bool hum = Human(style);
            double tcut = style == "human2" ? 0.4 : 0.55;
            const double dt = 0.05;
            double t = 0, nextLook = 2.5, oddsSum = 0;
            int ticks = 0, lastBeat = 0, samples = 0, oddsN = 0, cautionSet = -1, hzSeen = -1;
            double handS = 0, deadS = 0, emptyS = 0;
            Pending pend = null;
            PitGame pgSeen = null;

            void Play(HandCard h, Odds o) { oddsSum += o.Succ; oddsN++; r.PlayCard(h.Uid); }

            while (!r.Done)
            {
                r.Tick(); t += dt; ticks++;
                if (r.Done) break;
                var pg = r.LastPitGame;
                if (pg != null && pg != pgSeen)
                {
                    pgSeen = pg;
                    row.sd.Add(new StopRow { n = pg.Hand.FindAll(x => x.On).Count, f = pg.Hand.FindAll(x => x.On && x.Roll == Tier.Fail).Count, e = Math.Round(pg.ReleaseError, 3), w = Math.Round(pg.Window, 3), T = Math.Round(pg.Total, 3), lo = pg.Loose, p = pg.Perfect ? 1 : 0, ch = pg.ChiefRelease ? 1 : 0 });
                }
                var me = r.Me;
                if (me.Dnf) continue;
                if (me.Fuel <= 0) row.dry += dt;
                if (style != "none" && r.Phase == RacePhase.Green && me.Pit == null && r.PitRequest == null)
                {
                    var pl = r.MyPlan;
                    if (pl != null && r.LapOf(me) >= pl.Value.Lap) r.PressPit();
                }
                // crew calls: every style but hands-off takes a risky call at CallMin% or better and lets the rest time out
                if (style != "none" && r.Radio != null && r.Radio.Kind == RadioKind.Mech && r.Radio.Chance >= CallMin) r.TakeCall();
                var c = r.Ctx();
                if (r.HazardCard != null && hzSeen != r.HazardCard.Uid) { hzSeen = r.HazardCard.Uid; row.hz++; }
                if (ticks % 20 == 0 && r.Phase == RacePhase.Green)
                {
                    samples++; handS += r.Hand.Count;
                    if (!r.Hand.Exists(h => r.CardBlock(h.Def, c) == null)) deadS++;
                    if (r.Hand.Count == 0) emptyS++;
                    row.idle = Math.Max(row.idle, r.IdlePressure());
                }
                var k = r.Caution;
                if (style != "none" && r.Phase == RacePhase.Caution && k != null && !k.Done && cautionSet != k.Id)
                {
                    cautionSet = k.Id;
                    var call = CautionPolicy == "stay" ? PitService.Stay : CautionPolicy == "two" ? PitService.Two : CautionPolicy == "four" ? PitService.Four
                        : me.Tires >= 60 && me.Damage < 25 ? PitService.Stay : me.Tires >= 40 ? PitService.Two : PitService.Four;
                    if (CautionPolicy != "chief") r.SetCautionCall(call);
                }
                if (style == "none" || r.Attempt != null) continue;

                var hz = r.Hand.Find(h => h.Special);
                if (hz != null && style == "smart") { r.PlayCard(hz.Uid); continue; }
                if (hz != null && hum && (pend == null || pend.Uid != hz.Uid)) pend = new Pending { Uid = hz.Uid, At = t + 2.0 * (bal.Slowmo < 1 ? bal.Slowmo : 1), Hazard = true };
                if (!hum)
                {
                    var pk = Want(r, c, pol);
                    if (pk.h != null) Play(pk.h, pk.o);
                    continue;
                }
                bool beatNow = r.Beats > lastBeat && hz == null; lastBeat = r.Beats;
                if (beatNow && bal.Board > 0 && r.Phase == RacePhase.Green)
                    foreach (var x in r.Hand.ToArray())
                        if (!x.Special && !x.Def.Situational && x.Beat < r.Beats && r.CardBlock(x.Def, c) != null) r.TossCard(x.Uid);
                void TossNow()
                {
                    if (!bal.TossFx || r.Phase != RacePhase.Green || style == "hspam") return;
                    var xs = r.Hand.FindAll(x =>
                    {
                        var cd = x.Def;
                        if (x.Special || cd.Toss == TossKind.None || cd.Toss == TossKind.Charge || cd.Situational || !r.TossLive(cd)) return false;
                        if (cd.Toss == TossKind.Focus && r.FocusMeter > 0.6) return false;
                        return r.CardBlock(cd, c) != null || r.ComputeOdds(cd, c).Succ < tcut;
                    });
                    if (xs.Count > 0 && r.Hand.Count >= 2) { r.TossCard(xs[0].Uid); row.tfx++; }
                }
                if (beatNow) TossNow();
                // the slice pauses the race at every draw beat (BAL.beatPause), so a human picks there with no reaction time
                if (beatNow && BeatPause && r.Phase == RacePhase.Green)
                {
                    row.beats++;
                    var pk = Want(r, c, pol);
                    if (pk.h != null) { Play(pk.h, pk.o); pend = null; }
                    continue;
                }
                if (pend != null && hz == null && pend.Hazard) pend = null;
                if (pend != null)
                {
                    if (t >= pend.At)
                    {
                        var h = r.Hand.Find(x => x.Uid == pend.Uid);
                        pend = null;
                        if (h != null && r.CardBlock(h.Def, c) == null) Play(h, r.ComputeOdds(h.Def, c));
                    }
                    continue;
                }
                if (t >= nextLook)
                {
                    nextLook = t + 2.5;
                    TossNow();
                    var pk = Want(r, c, pol);
                    if (pk.h != null)
                    {
                        double delay = 1.5 * bal.Slowmo;
                        if (bal.FocusSec > 0 && bal.Slowmo < 1)
                        {
                            double cost = 1.5 / bal.FocusSec;
                            if (r.FocusMeter >= cost) r.FocusMeter -= cost; else delay = 1.5;
                        }
                        row.slow += 1.5;
                        pend = new Pending { Uid = pk.h.Uid, At = t + delay };
                    }
                }
            }

            var res = r.Result();
            var s = r.CardStats;
            row.s = res.MyStart; row.f = res.MyFinish; row.e = res.MyExpected; row.dnf = res.MyDnf ? 1 : 0;
            row.pl = s.Plays; row.su = s.Succ; row.cr = s.Crits; row.dur = t;
            row.hand = handS / Math.Max(1, samples); row.dead = deadS / Math.Max(1, samples); row.empty = emptyS / Math.Max(1, samples);
            row.ndnf = res.Dnfs; row.fol = s.Follow; row.combos = s.Combos; row.chain = s.BestChain; row.board = s.BoardFires; row.exp = s.Expired; row.toss = s.Tossed;
            row.stops = s.PitStops; row.perfect = s.Perfect; row.setups = s.Setups; row.pk = s.Packages;
            double st = 0; foreach (var x in s.StopTimes) st += x; row.stopT = st / Math.Max(1, s.StopTimes.Count);
            row.cautions = res.Cautions; row.tlive = s.TossLive; row.rref = s.RiskRefills; row.fgain = s.FocusGain; row.rep = s.Repassed; row.push = s.PushBack;
            row.calls = s.Calls; row.cwon = s.CallsWon;
            row.odds = oddsN > 0 ? oddsSum / oddsN : 0;
            return row;
        }

        /// <summary>The browser harness's want(): the playable card with the best success chance, if the style would play it.</summary>
        public static (HandCard h, Odds o) Want(Race r, CardCtx c, string pol)
        {
            var cands = new List<(HandCard h, Odds o)>();
            foreach (var h in r.Hand) if (r.CardBlock(h.Def, c) == null) cands.Add((h, r.ComputeOdds(h.Def, c)));
            if (cands.Count == 0) return (null, null);
            SimMath.StableSort(cands, (a, b) => b.o.Succ.CompareTo(a.o.Succ));
            if (pol == "spam" || pol == "hspam") return cands[0];
            double min = pol == "bold" ? 0.5 : 0.65, wr = pol == "bold" ? 0.08 : 0.05;
            foreach (var x in cands) if (x.o.Succ >= min && x.o.Wreck < wr) return x;
            return (null, null);
        }
    }
}
