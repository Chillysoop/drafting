using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum CardFamily { Draft, Pass, Defend, Car, Crew, Restart, Pit, Risk }
    public enum Rarity { Common, Uncommon, Rare, Special }

    /// <summary>The five outcomes, worst first (the order the slice samples them in).</summary>
    public enum Tier { CritFail, Fail, Neutral, Success, CritSuccess }

    /// <summary>What a card does when you toss it by hand. Every toss also charges the Crew Board.</summary>
    public enum TossKind { None, Chain, Buff, Charge, Draw, Focus, Cool, Setup }

    public enum DamageZone { Nose, Side, Tail }

    /// <summary>
    /// One card, as data: its odds read, what each outcome does, its cost, what it chains into and its toss effect.
    /// The delegates are the slice's read() and fx functions, line for line; everything they touch goes through CardCtx.
    /// </summary>
    public sealed class CardDef
    {
        public string Id, Name, Text, Real;
        public CardFamily Family;
        public Rarity Rarity;
        public int TireCost, MomCost;
        /// <summary>A kart card. Kart cards chain card to card (Next) instead of family to family.</summary>
        public bool Kart;
        /// <summary>Passing cards add Heat unless this is set (Show the Inside is a feint).</summary>
        public bool NoHeat;
        /// <summary>Kart cards this one sets up. Null = the stock family links (CardRules.Links).</summary>
        public string[] Next;
        /// <summary>Kart: an extra test for a link (Own the Inside only links when the next corner turns the other way).</summary>
        public Func<Race, bool> LinkOk;
        /// <summary>Why the card can't be played right now, or null.</summary>
        public Func<CardCtx, string> Can;
        /// <summary>The card's own read of the situation: a base and the factors that move it.</summary>
        public Action<CardCtx, OddsBuilder> Read;
        public Action<CardCtx> OnCritSuccess, OnSuccess, OnNeutral, OnFail, OnCritFail;
        /// <summary>Where the card's own failure damage lands on your car.</summary>
        public DamageZone Zone = DamageZone.Side;
        public TossKind Toss;
        public CardFamily TossFamily;
        public double TossValue;

        /// <summary>Has a critical failure (a wreck).</summary>
        public bool CanWreck => OnCritFail != null;
        /// <summary>Can't fail at all: the failure share goes to neutral.</summary>
        public bool NoFail => OnFail == null && OnCritFail == null;
        /// <summary>Evade and Reel Them In: dealt by the race, never in your deck, can't be tossed.</summary>
        public bool Special => Rarity == Rarity.Special;
        /// <summary>Caution and restart cards: dealt mostly under yellow, and back to the deck at the green.</summary>
        public bool Situational => Family == CardFamily.Restart || Family == CardFamily.Pit;
        /// <summary>Passing, high-risk and Bump Draft raise Heat.</summary>
        public bool Heats => (Family == CardFamily.Pass || Family == CardFamily.Risk || Id == "bump_draft") && !NoHeat;

        public Action<CardCtx> Effect(Tier t)
        {
            switch (t)
            {
                case Tier.CritSuccess: return OnCritSuccess;
                case Tier.Success: return OnSuccess;
                case Tier.Neutral: return OnNeutral;
                case Tier.Fail: return OnFail;
                default: return OnCritFail;
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>Rules shared by every card: family links, names, setup time, and the outcome helpers.</summary>
    public static class CardRules
    {
        /// <summary>Follow-ups: a landed move sets up the next one. A draft run sets up a pass, a pass leaves you defending...</summary>
        public static readonly IReadOnlyDictionary<CardFamily, CardFamily[]> Links = new Dictionary<CardFamily, CardFamily[]>
        {
            [CardFamily.Draft] = new[] { CardFamily.Pass, CardFamily.Risk, CardFamily.Draft },
            [CardFamily.Pass] = new[] { CardFamily.Defend, CardFamily.Pass, CardFamily.Car },
            [CardFamily.Defend] = new[] { CardFamily.Car, CardFamily.Defend, CardFamily.Draft },
            [CardFamily.Car] = new[] { CardFamily.Draft, CardFamily.Pass },
            [CardFamily.Crew] = new[] { CardFamily.Draft, CardFamily.Pass, CardFamily.Defend, CardFamily.Car, CardFamily.Risk },
            [CardFamily.Risk] = new[] { CardFamily.Defend, CardFamily.Car },
            [CardFamily.Restart] = new[] { CardFamily.Restart, CardFamily.Pass, CardFamily.Defend, CardFamily.Draft },
            [CardFamily.Pit] = new[] { CardFamily.Restart, CardFamily.Defend },
        };

        /// <summary>Seconds your car sets up a move before it resolves.</summary>
        public static double AttemptSeconds(CardFamily f)
        {
            switch (f)
            {
                case CardFamily.Pass: case CardFamily.Draft: return 2.5;
                case CardFamily.Risk: return 3;
                case CardFamily.Defend: return 1.5;
                case CardFamily.Car: case CardFamily.Restart: return 1.2;
                case CardFamily.Crew: return 1;
                case CardFamily.Pit: return 0.6;
                default: return 1.5;
            }
        }

        public static string FamilyName(CardFamily f)
        {
            switch (f)
            {
                case CardFamily.Draft: return "Drafting";
                case CardFamily.Pass: return "Passing";
                case CardFamily.Defend: return "Defending";
                case CardFamily.Car: return "Car";
                case CardFamily.Restart: return "Restart";
                case CardFamily.Pit: return "Caution";
                case CardFamily.Crew: return "Crew";
                default: return "High risk";
            }
        }

        public static string TierName(Tier t)
        {
            switch (t)
            {
                case Tier.CritSuccess: return "Critical success";
                case Tier.Success: return "Success";
                case Tier.Neutral: return "Neutral";
                case Tier.Fail: return "Failure";
                default: return "Critical failure";
            }
        }

        public static bool Landed(Tier t) => t == Tier.Success || t == Tier.CritSuccess;

        /// <summary>Damage-zone weights (nose, side, tail) for one zone.</summary>
        internal static (double n, double s, double t) Weights(DamageZone z) =>
            z == DamageZone.Nose ? (1, 0, 0) : z == DamageZone.Side ? (0, 1, 0) : (0, 0, 1);

        /// <summary>The slice's DZ_FAM: where a family's failure lands when the card names no zone.</summary>
        public static DamageZone ZoneOf(CardFamily f)
        {
            switch (f)
            {
                case CardFamily.Draft: case CardFamily.Restart: case CardFamily.Crew: return DamageZone.Nose;
                case CardFamily.Defend: return DamageZone.Tail;
                default: return DamageZone.Side;
            }
        }
    }
}
