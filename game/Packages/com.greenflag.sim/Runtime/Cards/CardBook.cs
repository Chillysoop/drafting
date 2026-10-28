using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>Every card in the game by id, and the starting decks.</summary>
    public static class CardBook
    {
        static readonly Dictionary<string, CardDef> byId = new Dictionary<string, CardDef>();
        static readonly List<CardDef> all = new List<CardDef>();

        public static IReadOnlyList<CardDef> All => all;

        /// <summary>The six cards every stock car or late model career starts with (the persona adds six more).</summary>
        public static readonly IReadOnlyList<string> StockBase = new[] { "tuck_in", "side_draft", "dive_bomb", "block", "save_tires", "spotter_focus" };
        public static readonly IReadOnlyList<string> KartBase = new[] { "k_tow", "k_show", "k_switch", "k_outside", "k_cover", "k_board" };

        /// <summary>What each kart card becomes when a career moves up to late models.</summary>
        public static readonly IReadOnlyDictionary<string, string> KartToStock = new Dictionary<string, string>
        {
            ["k_tow"] = "tuck_in", ["k_push"] = "bump_draft", ["k_pullout"] = "side_draft", ["k_show"] = "dive_bomb", ["k_switch"] = "crossover",
            ["k_outside"] = "high_line", ["k_owninside"] = "high_line", ["k_latebrake"] = "send_it", ["k_door"] = "bump_run", ["k_cover"] = "block",
            ["k_smooth"] = "save_tires", ["k_limit"] = "push_limit", ["k_board"] = "spotter_focus",
        };

        static CardBook()
        {
            foreach (var c in StockCards.All()) Add(c);
            foreach (var c in KartCards.All()) { c.Zone = CardRules.ZoneOf(c.Family); Add(c); }
        }

        static void Add(CardDef c)
        {
            if (byId.ContainsKey(c.Id)) throw new InvalidOperationException("Duplicate card " + c.Id);
            byId[c.Id] = c; all.Add(c);
        }

        public static CardDef Get(string id)
        {
            if (byId.TryGetValue(id, out var c)) return c;
            throw new KeyNotFoundException("No card with id " + id);
        }

        public static bool Exists(string id) => byId.ContainsKey(id);
    }
}
