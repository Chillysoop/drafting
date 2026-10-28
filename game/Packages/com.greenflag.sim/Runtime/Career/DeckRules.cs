using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>One line of the race deck's rules, as the deck builder lists them.</summary>
    public sealed class DeckRule
    {
        public enum Kind { Size, Min, Cap }
        public Kind K;
        public bool Ok;
        public string Text;
        /// <summary>Cards in the deck that count, and the number the rule asks for (Size: the floor; Cap: the cap).</summary>
        public int Have, Need, Max;
        public CardFamily Family;
        public DeckSlot Slot;
    }

    /// <summary>
    /// The race deck rules (step 15b, board Q19), ported from the slice: you own a collection and take 12 to 16 cards to each
    /// race (stock car; karts 10 to 13, late models 11 to 15), and Aero moves the max (Downforce +1 a notch, Low drag -1).
    /// Each track sets a family minimum, and the tune sets the slot caps: Attack (Passing + High risk) 6 + Handling, Defense
    /// (Defending + Car) 6 - Handling, Drafting 5 + Aero, and the combo focus adds a slot to its group. A minimum or the size
    /// floor relaxes when your collection can't meet it.
    /// </summary>
    public static class DeckRules
    {
        static CardFamily Fam(string id) => CardBook.Get(id).Family;
        public static int Count(IEnumerable<string> deck, string id) { int n = 0; foreach (var x in deck) if (x == id) n++; return n; }
        public static int FamCount(IEnumerable<string> deck, CardFamily f) { int n = 0; foreach (var x in deck) if (Fam(x) == f) n++; return n; }
        public static int SlotUsed(IEnumerable<string> deck, DeckSlot s) { int n = 0; foreach (var x in deck) if (s.Has(Fam(x))) n++; return n; }

        public static (int min, int max) Size(Series s, Tune u)
        {
            var ct = CareerRules.CarTypeOf(s);
            return (ct.Min, ct.Max - u.Aero);
        }

        public static int Cap(DeckSlot s, Tune u)
        {
            int c = s == CareerRules.Attack ? s.Base + u.Hand : s == CareerRules.Defense ? s.Base - u.Hand : s.Base + u.Aero;
            var fs = u.Focus == ComboFocus.Draft ? CareerRules.Drafting : u.Focus == ComboFocus.Bumper ? CareerRules.Attack : u.Focus == ComboFocus.LongRun ? CareerRules.Defense : null;
            return fs == s ? c + 1 : c;
        }

        public static List<DeckRule> Rules(Career c, TrackDef t, IList<string> deck = null, Tune u = null)
        {
            deck = deck ?? c.Deck; u = u ?? c.Tune;
            var coll = c.Collection;
            var outp = new List<DeckRule>();
            int reach = 0;
            foreach (var s in CareerRules.Slots) reach += Math.Min(Cap(s, u), SlotUsed(coll, s));
            foreach (var id in coll) if (CareerRules.SlotOf(Fam(id)) == null) reach++;
            var (min, max) = Size(c.Series, u);
            int lo = Math.Min(min, reach);
            outp.Add(new DeckRule
            {
                K = DeckRule.Kind.Size, Ok = deck.Count >= lo && deck.Count <= max, Have = deck.Count, Need = lo, Max = max,
                Text = $"{CareerRules.CarTypeOf(c.Series).Name}: {lo}–{max} cards{(lo < min ? $" (your cards fit only {lo} with this tune)" : "")}{(u.Aero != 0 ? " (Aero moves the max)" : "")}",
            });
            foreach (var (f, n) in CareerRules.TrackMin(t))
            {
                int need = Math.Min(n, FamCount(coll, f));
                outp.Add(new DeckRule { K = DeckRule.Kind.Min, Family = f, Ok = FamCount(deck, f) >= need, Have = FamCount(deck, f), Need = need, Text = $"{ShortName(t)}: at least {need} {CardRules.FamilyName(f)}{(need < n ? $" (you own {need})" : "")}" });
            }
            foreach (var s in CareerRules.Slots)
                outp.Add(new DeckRule { K = DeckRule.Kind.Cap, Slot = s, Ok = SlotUsed(deck, s) <= Cap(s, u), Have = SlotUsed(deck, s), Need = Cap(s, u), Text = $"{s.Name} slots ({string.Join(" + ", s.Families.Select(CardRules.FamilyName))})" });
            return outp;
        }

        public static bool Ok(Career c, TrackDef t, IList<string> deck = null, Tune u = null) => Rules(c, t, deck, u).All(r => r.Ok);

        /// <summary>Why a card can't go into the deck, or null if it can.</summary>
        public static string AddBlock(Career c, string id, TrackDef t, IList<string> deck = null, Tune u = null)
        {
            deck = deck ?? c.Deck; u = u ?? c.Tune;
            if (Count(deck, id) >= Count(c.Collection, id)) return "All your copies are in the deck";
            int mx = Size(c.Series, u).max;
            if (deck.Count >= mx) return $"The deck is full ({mx})";
            var s = CareerRules.SlotOf(Fam(id));
            if (s != null && SlotUsed(deck, s) >= Cap(s, u)) return $"{s.Name} slots are full. Change your tune for more.";
            return null;
        }

        /// <summary>How good a card is for this track and tune: rarity, the track's minimum, the track's shape, the tune's draw
        /// weight, your persona, and your own success with it once you've played it 3 times.</summary>
        public static double Score(Career c, string id, TrackDef t, Tune u = null)
        {
            u = u ?? c.Tune;
            var cd = CardBook.Get(id);
            var f = cd.Family;
            double s = cd.Rarity == Rarity.Uncommon ? 1.6 : cd.Rarity == Rarity.Rare ? 2.2 : cd.Rarity == Rarity.Special ? 1.8 : 1;
            if (CareerRules.TrackMinFor(t, f) > 0) s += 0.8;
            if (f == CardFamily.Draft) s += t.DraftBias;
            if (f == CardFamily.Defend) s += 0.8 * (t.Contact - 1);
            if (f == CardFamily.Car) s += 0.8 * (t.Wear - 0.9);
            if (f == CardFamily.Pass) s += 0.5 * (t.HandlingW - 1) * 0.5;
            s += 0.6 * (u.DrawMul(f) - 1);
            if (Array.IndexOf(Personas.Get(c.Persona).Add, id) >= 0) s += 0.3;
            if (c.Hist.TryGetValue(id, out var h) && h.Plays >= 3) s += (double)h.Succ / h.Plays - 0.5;
            return s;
        }

        /// <summary>Best first; ties by id.</summary>
        static List<string> ByScore(Career c, IEnumerable<string> ids, TrackDef t, Tune u)
        {
            var l = ids.ToList();
            var sc = new Dictionary<string, double>();
            foreach (var id in l) if (!sc.ContainsKey(id)) sc[id] = Score(c, id, t, u);
            return l.OrderByDescending(id => sc[id]).ThenBy(id => id, StringComparer.Ordinal).ToList();
        }

        static string Weakest(Career c, IEnumerable<string> ids, TrackDef t, Tune u)
        {
            var l = ByScore(c, ids, t, u);
            return l.Count > 0 ? l[l.Count - 1] : null;
        }

        static void RemoveLast(List<string> deck, string id) { int i = deck.LastIndexOf(id); if (i >= 0) deck.RemoveAt(i); }

        /// <summary>Keeps what you have where it's legal, then meets the track's minimums, then fills to target (default: the floor).</summary>
        public static List<string> Fix(Career c, TrackDef t, IEnumerable<string> from = null, Tune u = null, int target = 0)
        {
            u = u ?? c.Tune;
            var deck = (from ?? c.Deck).Where(CardBook.Exists).ToList();
            foreach (var id in deck.Distinct().ToList()) while (Count(deck, id) > Count(c.Collection, id)) RemoveLast(deck, id);
            foreach (var s in CareerRules.Slots)
                while (SlotUsed(deck, s) > Cap(s, u)) RemoveLast(deck, Weakest(c, deck.Where(id => s.Has(Fam(id))), t, u));
            List<string> Spare()
            {
                var l = new List<string>();
                foreach (var id in c.Collection.Distinct()) for (int k = Count(deck, id); k < Count(c.Collection, id); k++) l.Add(id);
                return ByScore(c, l, t, u);
            }
            foreach (var r in Rules(c, t, deck, u).Where(r => r.K == DeckRule.Kind.Min))
            {
                for (int guard = 0; FamCount(deck, r.Family) < r.Need && guard < 64; guard++)
                {
                    var pick = Spare().FirstOrDefault(id => Fam(id) == r.Family);
                    if (pick == null) break;
                    if (AddBlock(c, pick, t, deck, u) == null) { deck.Add(pick); continue; }
                    var s = CareerRules.SlotOf(r.Family);
                    bool slotFull = s != null && SlotUsed(deck, s) >= Cap(s, u);
                    var w = Weakest(c, deck.Where(id => Fam(id) != r.Family && (!slotFull || s.Has(Fam(id)))), t, u);
                    if (w == null) break;
                    RemoveLast(deck, w);
                }
            }
            int mx = Size(c.Series, u).max, lo = Rules(c, t, deck, u)[0].Need;
            int want = Math.Max(lo, Math.Min(mx, target > 0 ? target : lo));
            while (deck.Count < want)
            {
                var pick = Spare().FirstOrDefault(id => AddBlock(c, id, t, deck, u) == null);
                if (pick == null) break;
                deck.Add(pick);
            }
            while (deck.Count > mx)
            {
                var w = Weakest(c, deck.Where(id => { int m = CareerRules.TrackMinFor(t, Fam(id)); return !(m > 0 && FamCount(deck, Fam(id)) <= m); }), t, u);
                if (w == null) break;
                RemoveLast(deck, w);
            }
            return deck;
        }

        /// <summary>The best deck for this track and tune, filled to the max (the harness found a full deck ~0.8 places better than 12 cards).</summary>
        public static List<string> Auto(Career c, TrackDef t, Tune u = null) => Fix(c, t, new string[0], u, Size(c.Series, u ?? c.Tune).max);

        /// <summary>What the crew changed when it fixed your deck for a new track, or null.</summary>
        public static string Diff(IList<string> was, IList<string> now, TrackDef t)
        {
            var add = new List<string>(); var outp = new List<string>();
            foreach (var id in now.Distinct()) for (int k = Count(was, id); k < Count(now, id); k++) add.Add(CardBook.Get(id).Name);
            foreach (var id in was.Distinct()) for (int k = Count(now, id); k < Count(was, id); k++) outp.Add(CardBook.Get(id).Name);
            if (add.Count == 0 && outp.Count == 0) return null;
            return $"Your race deck broke a {ShortName(t)} rule, so the crew fixed it{(add.Count > 0 ? ": added " + string.Join(", ", add) : "")}{(outp.Count > 0 ? (add.Count > 0 ? "; " : ": ") + "took out " + string.Join(", ", outp) : "")}. Change it in the deck builder.";
        }

        /// <summary>A track's short name ("Millbrook" for Millbrook Speedway).</summary>
        public static string ShortName(TrackDef t)
        {
            var n = t.Name ?? t.Id;
            int sp = n.IndexOf(' ');
            return sp > 0 ? n.Substring(0, sp) : n;
        }
    }
}
