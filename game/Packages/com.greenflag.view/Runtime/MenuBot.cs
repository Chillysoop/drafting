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
    /// </summary>
    public sealed class MenuBot
    {
        /// <summary>Money it keeps in the bank after a purchase.</summary>
        public int Reserve = 3000;
        public int Presses, Bought, PartsBought, Rerolls, Skips;
        int builtFor = -1;

        /// <summary>Presses one button. Returns false when it has nothing to press (a race is waiting to be run).</summary>
        public bool Step(CareerMenu m)
        {
            if (m.RaceWanted != null) return false;
            var p = m.Page();
            string id = Choose(m, p);
            if (id == null) return false;
            if (!m.Press(id)) throw new InvalidOperationException($"MenuBot pressed {id} on the {m.Screen} screen and nothing happened");
            Presses++;
            if (id.StartsWith("buy:", StringComparison.Ordinal)) Bought++;
            if (id == "part.buy") PartsBought++;
            if (id == "reward.skip") Skips++;
            return true;
        }

        string Choose(CareerMenu m, MenuPage p)
        {
            var c = m.Career;
            switch (m.Screen)
            {
                case CareerScreen.Title: return "title.start";
                case CareerScreen.Over: return null;
                case CareerScreen.DeckBuilder:
                    if (builtFor != Key(c)) { builtFor = Key(c); return "deck.auto"; }
                    return "deck.done";
                case CareerScreen.Garage:
                {
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
            // leave the last reward screen so the loop ends back in the garage
            for (int i = 0; i < 20 && m.Screen == CareerScreen.Reward && bot.Step(m); i++) { }
            return m;
        }
    }
}
