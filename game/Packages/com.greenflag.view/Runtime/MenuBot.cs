using System;
using System.Linq;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>
    /// Plays the career screens through their buttons only, like a player who knows what they want: starts a career, buys the
    /// cheapest upgrade while it keeps a cushion of cash, buys the dealer's part when it can spare $7,000, auto-builds the race
    /// deck for each track, goes racing, then takes the best card on offer (rarest, then best for the next track) and the
    /// first part. Tests and the harness use it to prove race, rewards, shop and the next race loop (roadmap step 34).
    /// Step 37: it signs the sponsor paying most per race (a renewal first), qualifies with QualiBot at QualiSkill (null =
    /// skip qualifying), and at the season end signs the best step-up offer, else a better team, else stays.
    /// </summary>
    public sealed class MenuBot
    {
        /// <summary>Money it keeps in the bank after a purchase.</summary>
        public int Reserve = 3000;
        public int Presses, Bought, PartsBought, Rerolls, Skips, QualiRuns, Contracts, Checks, Negotiations, Points;
        /// <summary>Skip every off-track event instead of answering (tests that only care about racing).</summary>
        public bool SkipEvents;
        /// <summary>How well it qualifies: poor, avg, good, great; null skips qualifying.</summary>
        public string QualiSkill = "avg";
        public uint QualiSeed = 1;
        int builtFor = -1;

        /// <summary>Presses one button. Returns false when it has nothing to press (a race is waiting to be run).</summary>
        public bool Step(CareerMenu m)
        {
            if (m.RaceWanted != null) return false;
            if (m.Screen == CareerScreen.QualiRun)
            {
                // the run, at 60 frames a second, through the same tick the engine calls
                var qb = new QualiBot(QualiSkill ?? "avg", QualiSeed + (uint)(m.Career.Season * 100 + m.Career.RaceIdx));
                for (int i = 0; i < 200000 && m.QualiTick(1 / 60.0, qb.Held(m.Quali)); i++) { }
                QualiRuns++; Presses++;
                return true;
            }
            var p = m.Page();
            string id = Choose(m, p);
            if (id == null) return false;
            if (!m.Press(id)) throw new InvalidOperationException($"MenuBot pressed {id} on the {m.Screen} screen and nothing happened");
            Presses++;
            if (id.StartsWith("buy:", StringComparison.Ordinal)) Bought++;
            if (id == "part.buy") PartsBought++;
            if (id == "reward.skip") Skips++;
            if (id.StartsWith("offer:", StringComparison.Ordinal)) Contracts++;
            if (id.StartsWith("ev.opt:", StringComparison.Ordinal)) Checks++;
            if (id.StartsWith("neg:", StringComparison.Ordinal)) Negotiations++;
            if (id.StartsWith("drv.up:", StringComparison.Ordinal)) Points++;
            return true;
        }

        string Choose(CareerMenu m, MenuPage p)
        {
            var c = m.Career;
            switch (m.Screen)
            {
                case CareerScreen.Title: return "title.start";
                case CareerScreen.Over: return null;
                case CareerScreen.Quali: return QualiSkill == null ? "quali.skip" : "quali.start";
                case CareerScreen.QualiDone: return "quali.race";
                case CareerScreen.Season:
                {
                    var o = c.Offers;
                    int i = o.FindIndex(x => x.Kind == OfferKind.Promote);
                    if (i < 0) i = o.FindIndex(x => x.Kind == OfferKind.Move);
                    i = Math.Max(0, i);
                    // negotiate when better terms are at least as likely as worse ones
                    if (o[i].Neg == null) { var b = c.NegBuild(o[i]); if (b.Pass >= b.Odds[(int)CheckTier.Fail] + b.Odds[(int)CheckTier.Fumble]) return "neg:" + i; }
                    return "offer:" + i;
                }
                case CareerScreen.Event:
                {
                    var ev = c.Event;
                    if (ev.Cur != null) return "ev.next";
                    if (SkipEvents) return "ev.walk";
                    // the answer most likely to pass (ties: the first)
                    var r = c.EventRound();
                    var best = r.Opts.Where(x => !x.Auto).Select(x => (x.Id, pass: c.CheckFor(x).Pass)).OrderByDescending(x => x.pass).FirstOrDefault();
                    return "ev.opt:" + (best.Id ?? r.Opts[0].Id);
                }
                case CareerScreen.Driver:
                {
                    var d = c.Driver;
                    if (d.Pts == 0) return "drv.back";
                    // Savvy for contracts, then Grit for race day, then Composure
                    foreach (var a in new[] { DAttr.Savvy, DAttr.Grit, DAttr.Composure, DAttr.Wits, DAttr.Presence, DAttr.Rapport })
                        if (d.Score(a) < (a == DAttr.Savvy || a == DAttr.Grit ? 16 : DriverRules.AttrMax)) return "drv.up:" + a;
                    return "drv.back";
                }
                case CareerScreen.DeckBuilder:
                    if (builtFor != Key(c)) { builtFor = Key(c); return "deck.auto"; }
                    return "deck.done";
                case CareerScreen.Garage:
                {
                    if (c.Event != null && !c.Event.Done) return "ev.open";
                    if (c.Driver.Pts > 0) return "drv";
                    if (c.Sponsor == null && c.SponsorOffers != null && c.SponsorOffers.Count > 0)
                        return "sponsor:" + c.SponsorOffers.OrderByDescending(d => d.Renew).ThenByDescending(d => d.Per).ThenBy(d => d.Id, StringComparer.Ordinal).First().Id;
                    var buy = p.Pressables().Where(i => i.Id.StartsWith("buy:", StringComparison.Ordinal))
                        .Select(i => (i.Id, cost: c.StatCost(i.Id.Substring(4)))).Where(x => c.Money - x.cost >= Reserve).OrderBy(x => x.cost).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (buy.Id != null) return buy.Id;
                    if (p.CanPress("part.buy") && c.Money - CareerRules.PartPrice >= Reserve) return "part.buy";
                    if (builtFor != Key(c)) return "deck";
                    return p.CanPress("go") ? "go" : "deck";
                }
                case CareerScreen.Reward:
                {
                    var R = c.Reward;
                    if (R.Res.Penalty == "dead") return "reward.go"; // Permadeath: no rewards, the run is over
                    if (R.Picked == null)
                    {
                        if (R.Cards.Count == 0) return "reward.skip";
                        var t = c.NextTrack;
                        var best = R.Cards.OrderByDescending(id => (int)CardBook.Get(id).Rarity).ThenByDescending(id => DeckRules.Score(c, id, t)).ThenBy(id => id, StringComparer.Ordinal).First();
                        return "reward.card:" + best;
                    }
                    if (R.Parts != null && R.PartPicked == null) return "reward.part:" + R.Parts[0];
                    return "reward.go";
                }
            }
            return null;
        }

        static int Key(Career c) => c.Season * 1000 + c.RaceIdx;

        /// <summary>Runs a career for n races through the menus, playing each race with play (the mouse bot by default).</summary>
        public static CareerMenu Run(CareerMenu m, int races, Func<RaceSetup, Race> play = null, MenuBot bot = null, int maxPresses = 100000)
        {
            bot = bot ?? new MenuBot();
            play = play ?? (s => MousePlayer.Play(new Race(s)).desk.Race);
            int done = 0;
            for (int i = 0; i < maxPresses && done < races; i++)
            {
                if (m.RaceWanted != null)
                {
                    var race = play(m.TakeRace());
                    m.RaceFinished(race);
                    done++;
                    continue;
                }
                if (!bot.Step(m)) break;
            }
            // leave the last reward screen (and a season's contract offers) so the loop ends back in the garage
            for (int i = 0; i < 20 && (m.Screen == CareerScreen.Reward || m.Screen == CareerScreen.Season) && bot.Step(m); i++) { }
            return m;
        }

        /// <summary>
        /// Run, but after every press and every race the game is closed and opened again: the menu flushes its save (as the
        /// engine does when the game quits) and a fresh menu loads the slot back (step 35). Presses on the deck builder and in
        /// qualifying (its card pick, the run and its result) aren't reloaded in between, since they reopen in the garage (a mid-run quit is tested on its
        /// own: it gives the run up). Reload says how often it did it.
        /// </summary>
        public static CareerMenu RunReloading(CareerMenu m, int races, Func<RaceSetup, Race> play = null, MenuBot bot = null, Action<CareerMenu> reloaded = null, int maxPresses = 100000)
        {
            if (m.Saves == null) throw new ArgumentException("RunReloading needs a menu with save slots");
            bot = bot ?? new MenuBot();
            play = play ?? (s => MousePlayer.Play(new Race(s)).desk.Race);
            int done = 0;
            CareerMenu Reload(CareerMenu x)
            {
                if (x.Career == null || x.Screen == CareerScreen.DeckBuilder || x.Screen == CareerScreen.Driver || x.Screen == CareerScreen.Quali || x.Screen == CareerScreen.QualiRun || x.Screen == CareerScreen.QualiDone) return x;
                x.Flush();
                var n = new CareerMenu(x.Saves) { NewSeed = x.NewSeed, PickPersona = x.PickPersona, PickDiff = x.PickDiff, PickStakes = x.PickStakes, PickSlot = x.PickSlot };
                if (!n.Load(x.Slot)) throw new InvalidOperationException("Reload failed: " + n.Toast);
                reloaded?.Invoke(n);
                return n;
            }
            for (int i = 0; i < maxPresses && done < races; i++)
            {
                if (m.RaceWanted != null)
                {
                    var race = play(m.TakeRace());
                    m.RaceFinished(race);
                    done++;
                    m = Reload(m);
                    continue;
                }
                if (!bot.Step(m)) break;
                if (m.RaceWanted == null) m = Reload(m);
            }
            for (int i = 0; i < 20 && (m.Screen == CareerScreen.Reward || m.Screen == CareerScreen.Season) && bot.Step(m); i++) m = Reload(m);
            return m;
        }
    }
}
