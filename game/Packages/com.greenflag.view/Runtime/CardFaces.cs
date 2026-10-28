using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>A screen rectangle in pixels, y down (the engine's GUI space).</summary>
    public readonly struct Box
    {
        public readonly double X, Y, W, H;
        public Box(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
        public double Right => X + W;
        public double Bottom => Y + H;
        public double CX => X + W / 2;
        public double CY => Y + H / 2;
        public bool Contains(double x, double y) => x >= X && x <= X + W && y >= Y && y <= Y + H;
        public Box Inflate(double d) => new Box(X - d, Y - d, W + 2 * d, H + 2 * d);
        public Box Move(double dx, double dy) => new Box(X + dx, Y + dy, W, H);
        public override string ToString() => $"[{X:F0},{Y:F0} {W:F0}x{H:F0}]";
    }

    /// <summary>The slice's card colours and words: family tiles, outcome bands, odds and car condition colours, toss text.</summary>
    public static class CardStyle
    {
        /// <summary>The family order chips and the Family sort use (the slice's FAM_ORDER).</summary>
        public static readonly CardFamily[] Order =
            { CardFamily.Draft, CardFamily.Pass, CardFamily.Defend, CardFamily.Car, CardFamily.Restart, CardFamily.Pit, CardFamily.Crew, CardFamily.Risk };

        static readonly Dictionary<CardFamily, Rgb> fam = new Dictionary<CardFamily, Rgb>
        {
            [CardFamily.Draft] = Rgb.Hex("#2f6fe0"), [CardFamily.Pass] = Rgb.Hex("#1f9d55"), [CardFamily.Defend] = Rgb.Hex("#7c4fe0"),
            [CardFamily.Car] = Rgb.Hex("#e07b1f"), [CardFamily.Restart] = Rgb.Hex("#d9a400"), [CardFamily.Pit] = Rgb.Hex("#d33a3a"),
            [CardFamily.Crew] = Rgb.Hex("#5b6b80"), [CardFamily.Risk] = Rgb.Hex("#121722"),
        };

        /// <summary>The family tile colour (FAM[f].c).</summary>
        public static Rgb Family(CardFamily f) => fam[f];

        /// <summary>The colour the family is written in on dark panels (High risk's tile is near black, so its chip is red).</summary>
        public static Rgb FamilyInk(CardFamily f) => f == CardFamily.Risk ? Rgb.Hex("#ff8a8a") : fam[f];

        public static int OrderOf(CardFamily f) => Array.IndexOf(Order, f);

        public static readonly Rgb Good = Rgb.Hex("#2fb36a"), Mid = Rgb.Hex("#e9b52f"), Bad = Rgb.Hex("#e5484d"), Gold = Rgb.Hex("#e9b52f"),
            Ink = Rgb.Hex("#0b0f18"), Panel = Rgb.Hex("#0b1220", 0.88f), Dim = Rgb.Hex("#9aa3b5"), Combo = Rgb.Hex("#4aa8ff"), Focus = Rgb.Hex("#7fd4ff");

        /// <summary>Outcome band colours, critical failure to critical success.</summary>
        public static Rgb Tier(Tier t)
        {
            switch (t)
            {
                case Sim.Tier.CritSuccess: return Rgb.Hex("#7ee2a8");
                case Sim.Tier.Success: return Rgb.Hex("#2fb36a");
                case Sim.Tier.Neutral: return Rgb.Hex("#8a93a6");
                case Sim.Tier.Fail: return Rgb.Hex("#f08a24");
                default: return Rgb.Hex("#e5484d");
            }
        }

        /// <summary>The big odds number: green at 70% and up, amber from 40%, red below (pc hi/md/lo).</summary>
        public static Rgb Odds(double succ) => succ >= 0.695 ? Good : succ >= 0.395 ? Mid : Bad;

        /// <summary>Car condition: 100% green, then yellow, orange, red at 0% (csColor).</summary>
        public static Rgb Condition(double pct)
        {
            double[] at = { 0, 30, 60, 100 };
            Rgb[] c = { Rgb.Hex("#e5484d"), Rgb.Hex("#f08a24"), Rgb.Hex("#e9d22f"), Rgb.Hex("#2fb36a") };
            pct = Math.Max(0, Math.Min(100, pct));
            for (int i = 1; i < at.Length; i++)
                if (pct <= at[i])
                {
                    float k = (float)((pct - at[i - 1]) / (at[i] - at[i - 1]));
                    Rgb a = c[i - 1], b = c[i];
                    return new Rgb(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k);
                }
            return c[3];
        }

        /// <summary>What tossing this card does (the slice's tossTxt). Every toss also charges the Crew Board.</summary>
        public static string TossText(CardDef cd)
        {
            switch (cd.Toss)
            {
                case TossKind.Chain: return "Keeps a live combo going if it links";
                case TossKind.Buff: return $"Next {CardRules.FamilyName(cd.TossFamily)} card +8 for 20 s";
                case TossKind.Charge: return "An extra Crew Board charge";
                case TossKind.Draw: return "Draws a card";
                case TossKind.Focus: return $"Focus +{cd.TossValue * 100:F0}%";
                case TossKind.Cool: return "Heat -1";
                case TossKind.Setup: return "Setups stay fresh one more lap";
                default: return "Charges the Crew Board";
            }
        }

        /// <summary>Odds changer names for the chips (MOD_INFO).</summary>
        public static string ModName(string kind)
        {
            switch (kind)
            {
                case "shape": return "Track shape";
                case "combo": return "Combo";
                case "boost": return "Boost";
                case "mom": return "Momentum";
                case "setup": return "Setup";
                case "tune": return "Tune";
                case "heat": return "Heat";
                case "block": return "Blocked";
                case "dmg": return "Damage";
                case "field": return "Field";
                case "drv": return "Driver";
                default: return kind;
            }
        }
    }

    /// <summary>One odds changer chip on a card: what it is and how many points it moves the read.</summary>
    public readonly struct ModChip
    {
        public readonly string Kind;
        public readonly int Value;
        public ModChip(string kind, int value) { Kind = kind; Value = value; }
        public string Name => CardStyle.ModName(Kind);
        public override string ToString() => $"{Name} {(Value > 0 ? "+" : "")}{Value}";
    }

    /// <summary>
    /// Everything a card face shows, odds first (the slice's cardFace + updateHand): the success chance as the big number and
    /// the five outcome bands under it, wreck risk, the three biggest odds changers, costs, the combo it would extend, a
    /// waiting boost, what tossing it does, and why it can't be played.
    /// </summary>
    public sealed class CardFace
    {
        public int Uid;
        public CardDef Def;
        public string Name => Def.Name;
        public string Text => Def.Text;
        public CardFamily Family => Def.Family;
        public Rgb Color => CardStyle.Family(Def.Family);
        public Odds Odds;
        /// <summary>Success chance in whole percent (success + critical success).</summary>
        public int Succ;
        /// <summary>Critical failure chance in whole percent, shown at 5% and up on cards that can wreck.</summary>
        public int Wreck;
        /// <summary>Success chance without the odds changers, for the tick on the band (shown when 3 points or more away).</summary>
        public int BaseSucc;
        /// <summary>Why it can't be played right now, or null.</summary>
        public string Block;
        public int TireCost, MomCost;
        public bool Heats;
        public readonly List<ModChip> Chips = new List<ModChip>();
        /// <summary>The combo link it would be played on (0 = none); the card shows COMBO x(Combo+1).</summary>
        public int Combo;
        /// <summary>A banked boost that applies to it (0 = none).</summary>
        public int Boost;
        /// <summary>Families (or, for kart cards, the cards) it sets up.</summary>
        public readonly List<CardFamily> SetsUp = new List<CardFamily>();
        public string TossText;
        /// <summary>The toss does its main thing right now (the bin tile glows).</summary>
        public bool TossLive;
        public bool Special, Follow;
        /// <summary>1-9 shortcut on the card, 0 = none.</summary>
        public int Key;
        public bool Selected, Best, Faded, Filtered;
        /// <summary>Greyed out: blocked, or another move is already in progress.</summary>
        public bool Off;
        /// <summary>The odds changers add up to +5 or more (green edge) or -5 or less (red edge), combo aside. The field's
        /// strength is left out too: it is the same on every stock car card, so it would paint the whole hand red.</summary>
        public int Net;

        public bool Playable => Block == null && !Off;
        public override string ToString() => $"{Name} {Succ}%{(Block != null ? " (" + Block + ")" : "")}";

        public static CardFace Of(Race r, HandCard h, CardCtx c)
        {
            var cd = h.Def;
            var f = new CardFace { Uid = h.Uid, Def = cd, Special = h.Special, Follow = h.Follow };
            f.Block = r.CardBlock(cd, c);
            f.Odds = r.ComputeOdds(cd, c);
            f.Succ = (int)Math.Round(f.Odds.Succ * 100);
            f.Wreck = cd.CanWreck ? (int)Math.Round(f.Odds.Wreck * 100) : 0;
            f.BaseSucc = (int)Math.Round(f.Odds.BaseSucc * 100);
            f.TireCost = cd.TireCost > 0 ? (int)Math.Round(cd.TireCost * r.Bal.TireCost) : 0;
            f.MomCost = cd.MomCost;
            f.Heats = cd.Heats;
            if (f.Block == null && f.Odds.Mods != null)
            {
                var keys = new List<string>(f.Odds.Mods.Keys);
                keys.RemoveAll(k => Math.Abs(f.Odds.Mods[k]) < 1);
                keys.Sort((a, b) => Math.Abs(f.Odds.Mods[b]).CompareTo(Math.Abs(f.Odds.Mods[a])));
                for (int i = 0; i < keys.Count && i < 3; i++) f.Chips.Add(new ModChip(keys[i], (int)Math.Round(f.Odds.Mods[keys[i]])));
                foreach (var kv in f.Odds.Mods) if (kv.Key != "combo" && kv.Key != "field") f.Net += (int)Math.Round(kv.Value);
            }
            f.Off = f.Block != null || r.Attempt != null;
            f.Combo = f.Block == null && r.Attempt == null ? r.ComboFor(cd) : 0;
            foreach (var b in r.Buffs) if (b.Family == null || b.Family == cd.Family) { f.Boost = b.V; break; }
            if (cd.Next != null) { foreach (var id in cd.Next) { var nf = CardBook.Get(id).Family; if (!f.SetsUp.Contains(nf)) f.SetsUp.Add(nf); } }
            else if (CardRules.Links.TryGetValue(cd.Family, out var links)) f.SetsUp.AddRange(links);
            f.TossText = CardStyle.TossText(cd);
            f.TossLive = cd.Toss != TossKind.None && cd.Toss != TossKind.Charge && r.TossLive(cd);
            return f;
        }
    }
}
