using System;

namespace GreenFlag.Sim
{
    /// <summary>The combo package on the garage's third dial (karts call them Tow, Brawl and Line focus).</summary>
    public enum ComboFocus { None, Draft, Bumper, LongRun, Restart }

    /// <summary>
    /// The car setup's three dials (step 7b): Handling -2 tight to +2 loose, Aero -2 downforce to +2 low drag, and a combo
    /// focus. They shift which cards you draw, card odds, tire wear, pace on each track and the combo window.
    /// In a race the tune is a copy, so a pit Setup can nudge it for that race only.
    /// </summary>
    public sealed class Tune
    {
        public int Hand, Aero;
        public ComboFocus Focus;

        public const double FocusWindow = 3, OffFocusWindow = 1;
        public const int FocusBonus = 2;

        public Tune Clone() => (Tune)MemberwiseClone();

        static bool In(CardFamily f, CardFamily a, CardFamily b) => f == a || f == b;

        /// <summary>Draw weight for a family (TUNE.hand.draw x TUNE.aero.draw x the focus package's draw).</summary>
        public double DrawMul(CardFamily f)
        {
            double h = 1, v = Hand;
            if (v > 0) h = In(f, CardFamily.Pass, CardFamily.Risk) ? 1 + 0.25 * v : In(f, CardFamily.Defend, CardFamily.Car) ? 1 - 0.12 * v : 1;
            else if (v < 0) h = In(f, CardFamily.Defend, CardFamily.Car) ? 1 - 0.25 * v : In(f, CardFamily.Pass, CardFamily.Risk) ? 1 + 0.12 * v : 1;
            double a = 1, w = Aero;
            if (w > 0) a = f == CardFamily.Draft ? 1 + 0.25 * w : 1;
            else if (w < 0) a = f == CardFamily.Pass ? 1 - 0.1 * w : f == CardFamily.Draft ? 1 + 0.12 * w : 1;
            double p = FocusHas(f) ? FocusDraw(Focus) : 1;
            return h * a * p;
        }

        /// <summary>Odds from the handling dial.</summary>
        public int HandRead(CardDef cd)
        {
            int v = Hand;
            if (v > 0) return In(cd.Family, CardFamily.Pass, CardFamily.Risk) ? 2 * v : 0;
            if (v < 0) return In(cd.Family, CardFamily.Defend, CardFamily.Car) ? -3 * v : In(cd.Family, CardFamily.Pass, CardFamily.Risk) ? v : 0;
            return 0;
        }

        /// <summary>Odds from the aero dial: low drag helps drafting cards, downforce helps passing in and into corners.</summary>
        public int AeroRead(CardDef cd, CardCtx c)
        {
            int v = Aero;
            if (v > 0) return cd.Family == CardFamily.Draft ? 2 * v : 0;
            if (v < 0 && cd.Family == CardFamily.Pass && c != null && (c.Seg.Turn || c.Seg.Approach)) return -3 * v;
            return 0;
        }

        public double Pace(TrackDef t) => 1 + 0.0025 * Aero * t.DraftBias - (Aero < 0 ? 0.001 * -Aero * Math.Max(0, t.DraftBias) : 0);
        public double DraftMul => 1 + 0.1 * Aero;

        public double Wear
        {
            get
            {
                double h = Hand > 0 ? 1 + 0.05 * Hand : 1 + 0.08 * Hand;
                double a = Aero < 0 ? 1 + 0.03 * Aero : 1;
                return h * a * (Focus == ComboFocus.LongRun ? 0.95 : 1);
            }
        }

        public bool FocusHas(CardFamily f) => FocusFamilies(Focus, f);

        /// <summary>The families a combo package favors.</summary>
        public static bool FocusFamilies(ComboFocus p, CardFamily f)
        {
            switch (p)
            {
                case ComboFocus.Draft: return f == CardFamily.Draft;
                case ComboFocus.Bumper: return f == CardFamily.Pass || f == CardFamily.Defend;
                case ComboFocus.LongRun: return f == CardFamily.Car;
                case ComboFocus.Restart: return f == CardFamily.Restart || f == CardFamily.Pit;
                default: return false;
            }
        }

        static double FocusDraw(ComboFocus p) => p == ComboFocus.Draft ? 1.4 : p == ComboFocus.Restart ? 1.5 : 1;

        /// <summary>Draft focus: drafting cards read +1 to +6 where the draft is real.</summary>
        public int FocusRead(CardDef cd, TrackDef t) =>
            Focus == ComboFocus.Draft && cd.Family == CardFamily.Draft && t.DraftBias > 0 ? (int)SimMath.RoundJs(6 * t.DraftBias) : 0;

        /// <summary>Bumper focus cools Heat faster.</summary>
        public double HeatCool => Focus == ComboFocus.Bumper ? 1.5 : 1;

        /// <summary>Restart focus: any live combo freezes under yellow instead of timing out.</summary>
        public bool Freeze => Focus == ComboFocus.Restart;

        public string FocusName(bool kart)
        {
            switch (Focus)
            {
                case ComboFocus.Draft: return kart ? "Tow focus" : "Draft focus";
                case ComboFocus.Bumper: return kart ? "Brawl focus" : "Bumper focus";
                case ComboFocus.LongRun: return kart ? "Line focus" : "Long-run focus";
                case ComboFocus.Restart: return "Restart focus";
                default: return "No combo focus";
            }
        }

        /// <summary>A pit Setup nudges this race's tune one notch (step 7b): Track bar tighter, Spring rubber looser, Wedge less drag.</summary>
        public string Nudge(string setupId)
        {
            int was;
            switch (setupId)
            {
                case "trackbar": was = Hand; Hand = Math.Max(-2, Math.Min(2, Hand - 1)); return Hand != was ? "tighter" : null;
                case "spring": was = Hand; Hand = Math.Max(-2, Math.Min(2, Hand + 1)); return Hand != was ? "looser" : null;
                case "wedge": was = Aero; Aero = Math.Max(-2, Math.Min(2, Aero + 1)); return Aero != was ? "less drag" : null;
                default: return null;
            }
        }
    }
}
