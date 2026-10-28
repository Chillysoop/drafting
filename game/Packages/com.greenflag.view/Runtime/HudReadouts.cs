using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>The Focus bar (#focusbar): how much slow-mo is left, and what is happening to it.</summary>
    public sealed class FocusReadout
    {
        /// <summary>0-1 fill (1 while Evade's free slow-mo runs).</summary>
        public double Fill;
        public string Pct, Note;
        public bool Slowing, Free, Low, Mid, Refill;
        /// <summary>Striped ghost: what the next beat gives back.</summary>
        public double NextBeat;

        double prev = -1, refillUntil;

        public void Update(PlayDesk d)
        {
            var r = d.Race;
            double f = Math.Max(0, Math.Min(1, r.FocusMeter));
            if (prev >= 0 && f > prev + 0.001) refillUntil = d.Clock + 1.8;
            prev = f;
            Slowing = d.Slowing;
            Free = d.HazardPicked;
            Refill = !Slowing && d.Clock < refillUntil;
            Fill = Free ? 1 : f;
            Pct = Free ? "FREE" : Math.Ceiling(f * 100) + "%";
            Low = !Free && f < 0.2;
            Mid = !Free && f >= 0.2 && f < 0.5;
            NextBeat = Math.Min(1 - f, r.Bal.FocusRegen);
            Note = Refill ? "Refilled" : Free ? "Slow-mo: Evade costs no Focus" : Slowing ? (f <= 0 ? "Empty: full speed" : "Slow-mo · Esc cancels")
                 : r.Selected != null && f <= 0 ? "Empty: full speed · play or Esc" : "Pick a card for slow-mo";
        }
    }

    public enum ClockState { Idle, Draw, Normal, Mid, Soon }

    /// <summary>The beat clock (#beatclock): real seconds to the next draw beat, stretched by slow-mo until Focus runs out.</summary>
    public sealed class BeatClockReadout
    {
        public ClockState State;
        /// <summary>0-1 of the way round (1 = a full gap to the next beat).</summary>
        public double Frac;
        public string Num, Unit, Label, Sub;
        public bool Slow;

        public void Update(PlayDesk d)
        {
            var r = d.Race;
            double? simLeft = null, real = null;
            double gap = r.BeatsPerLap > 0 ? r.Track.LapTime / r.BeatsPerLap : r.Track.LapTime;
            if (r.Phase == RacePhase.Green && !r.Me.Dnf && r.Me.Pit == null)
            {
                double n = r.NextBeatIn;
                if (!double.IsInfinity(n)) simLeft = n;
            }
            double spd = d.Driver.Speed;
            if (simLeft is double sl)
            {
                if (!d.Slowing) real = sl / spd;
                else
                {
                    double fsec = r.Bal.FocusSec <= 0 || d.HazardPicked ? double.PositiveInfinity : r.FocusMeter * r.Bal.FocusSec;
                    double slowSim = fsec * spd * r.Bal.Slowmo;
                    real = sl <= slowSim ? sl / (spd * r.Bal.Slowmo) : fsec + (sl - slowSim) / spd;
                }
            }
            bool draw = d.BeatPaused;
            Slow = d.Slowing && !draw;
            Frac = draw ? 1 : simLeft is double s2 ? Math.Max(0, Math.Min(1, s2 / gap)) : 0;
            Num = draw ? "DRAW" : real is double rv ? Math.Ceiling(rv).ToString() : "-";
            Unit = draw || real == null ? "" : "SEC";
            Label = draw ? "Draw beat" : real == null ? (r.Phase == RacePhase.Start ? "Beats start" : "Beats resume") : "Next beat";
            Sub = draw ? "Play, or P for green" : real == null ? "at the green" : d.Driver.Paused ? "Paused" : Slow ? "Slow-mo time" : "";
            State = draw ? ClockState.Draw : real == null ? ClockState.Idle : real <= 3 && !d.Driver.Paused ? ClockState.Soon : Frac <= 0.5 ? ClockState.Mid : ClockState.Normal;
        }
    }

    /// <summary>The live odds gauge (#gauges): the picked card's odds moving with the race, or the move in progress.</summary>
    public sealed class GaugeReadout
    {
        public bool Has;
        public string Title, Status;
        public Rgb Color;
        public Odds Odds;
        public int Succ, Wreck;
        public bool Armed;
        /// <summary>The factors moving the odds, biggest first (up to 4).</summary>
        public readonly List<OddsTag> Tags = new List<OddsTag>();
        public bool CanPlay;
        public string PlayLabel;

        public void Update(PlayDesk d)
        {
            var r = d.Race;
            Tags.Clear();
            Armed = false;
            CanPlay = d.AimTarget() != null;
            PlayLabel = d.Aim != null ? (d.Aim.Out ? "Let go to cancel" : d.Aim.Key ? "Release Space to play · Shift cancels" : "Release to play")
                      : "Hold to play (Space)";
            if (r.Attempt != null)
            {
                var a = r.Attempt;
                Has = true; Armed = true;
                Title = a.Def.Name; Color = CardStyle.FamilyInk(a.Def.Family);
                Odds = a.Odds;
                Status = $"Lands in {Math.Max(0, a.End - r.T):F1} s";
            }
            else
            {
                var f = d.Preview;
                Has = f != null;
                if (!Has) { Title = "Live odds"; Status = "Pick a card"; return; }
                Title = f.Name; Color = CardStyle.FamilyInk(f.Family);
                Odds = f.Odds;
                Status = f.Block ?? (f.Combo > 0 ? $"Combo x{f.Combo + 1}" : f.Selected ? "Picked" : "");
            }
            Succ = (int)Math.Round(Odds.Succ * 100);
            Wreck = (int)Math.Round(Odds.Wreck * 100);
            if (Odds.Tags != null) for (int i = 0; i < Odds.Tags.Count && i < 4; i++) Tags.Add(Odds.Tags[i]);
        }
    }

    /// <summary>A warning on a dash module from the card you are looking at (amber, or red for a big one).</summary>
    public readonly struct Threat
    {
        public readonly string Tag;
        public readonly bool Red;
        public Threat(string tag, bool red) { Tag = tag; Red = red; }
    }

    /// <summary>The car dash (bottom left): car condition, position, Momentum, Heat, banked boost, and the threat preview.</summary>
    public sealed class DashReadout
    {
        public int Tires, Body, Pos, Expected, Momentum, Heat, Tow;
        public bool Kart;
        public string Ahead, Behind, Banked;
        public Threat? CarThreat, PosThreat, MomThreat, HeatThreat;
        /// <summary>Real time each module last changed and whether it got better (the green/red flash and floating delta).</summary>
        public double TiresAt = -9, BodyAt = -9, PosAt = -9, MomAt = -9, HeatAt = -9;
        public int TiresD, BodyD, PosD, MomD, HeatD;

        bool first = true;

        public void Update(PlayDesk d)
        {
            var r = d.Race; var me = r.Me;
            int t = (int)Math.Round(me.Tires), b = (int)Math.Round(100 - me.Damage), p = r.Position(me);
            if (!first)
            {
                if (t <= Tires - 6) { TiresAt = d.Clock; TiresD = t - Tires; }
                if (b != Body && Math.Abs(b - Body) >= 3) { BodyAt = d.Clock; BodyD = b - Body; }
                if (p != Pos) { PosAt = d.Clock; PosD = Pos - p; }
                if (r.Momentum != Momentum) { MomAt = d.Clock; MomD = r.Momentum - Momentum; }
                if (r.Heat != Heat) { HeatAt = d.Clock; HeatD = r.Heat - Heat; }
            }
            first = false;
            Tires = t; Body = b; Pos = p; Expected = r.Expected; Momentum = r.Momentum; Heat = r.Heat;
            Kart = r.Track.Kart; Tow = r.Tow;
            var a = r.AheadOf(me); var bh = r.BehindOf(me);
            Ahead = a != null && !me.Dnf ? $"+{r.GapSec(a, me):F1} s to #{a.Ref.Number}" : me.Dnf ? "Out" : "Leading";
            Behind = bh != null ? $"-{r.GapSec(me, bh):F1} s #{bh.Ref.Number}" : "";
            Banked = r.Buffs.Count > 0 ? $"BANKED +{r.Buffs[0].V} {(r.Buffs[0].Family is CardFamily f ? CardRules.FamilyName(f) : "next card")}" : "";

            CarThreat = PosThreat = MomThreat = HeatThreat = null;
            var c = d.Preview;
            if (c == null || c.Block != null) return;
            if (c.Wreck >= 5) CarThreat = new Threat($"{c.Wreck}% wreck", c.Wreck >= 10);
            else if (c.TireCost > 0) CarThreat = new Threat($"-{c.TireCost}% tires if it misses", c.TireCost >= 8);
            int fail = (int)Math.Round((c.Odds[Tier.Fail] + c.Odds[Tier.CritFail]) * 100);
            if (fail >= 10) PosThreat = new Threat($"{fail}% fail", fail >= 35);
            if (c.MomCost > 0) MomThreat = new Threat($"-{c.MomCost}", c.MomCost > r.Momentum);
            if (c.Heats) HeatThreat = new Threat("+1", r.Heat + 1 >= 3);
        }
    }

    /// <summary>A tile over the console: a toss boost counting down, a banked boost READY, a combo kept alive.</summary>
    public readonly struct TossTile
    {
        public readonly string Text;
        public readonly double Left;
        public readonly bool Ready, Glow;
        public TossTile(string text, double left, bool ready, bool glow) { Text = text; Left = left; Ready = ready; Glow = glow; }
    }

    /// <summary>The console (#hudcore) between Focus and the toss bin: the combo meter, the Crew Board and the toss tiles.</summary>
    public sealed class ComboReadout
    {
        public bool Live;
        public int Link;
        public double Left, Frac;
        public CardFamily? Family;
        public int Board, BoardMax;
        public readonly List<TossTile> Tiles = new List<TossTile>();
        /// <summary>The bin glows: the dragged or picked card's toss does its main thing now.</summary>
        public bool BinLive;
        public string BinText;

        public void Update(PlayDesk d)
        {
            var r = d.Race;
            Left = r.ComboLeft;
            Live = Left > 0;
            Link = r.Chain;
            Family = r.ChainFam;
            Frac = Live ? Math.Max(0, Math.Min(1, Left / Math.Max(0.1, r.ComboWindow))) : 0;
            Board = r.Board; BoardMax = r.Bal.Board;
            var c = d.Preview;
            Tiles.Clear();
            foreach (var b in r.TossBuffs)
                if (b.Until > r.T) Tiles.Add(new TossTile($"+{b.V} {(b.Family is CardFamily f ? CardRules.FamilyName(f) : "")}", b.Until - r.T, false, c != null && b.Family == c.Family));
            foreach (var b in r.Buffs)
                Tiles.Add(new TossTile($"+{b.V} {(b.Family is CardFamily f ? CardRules.FamilyName(f) : "next card")}", 0, true, c != null && (b.Family == null || b.Family == c.Family)));
            BinLive = c != null && c.TossLive;
            BinText = c != null && !c.Special ? "Toss: " + c.TossText : "Drag a card here to toss it";
        }
    }

    /// <summary>The pit module (#pitmod) and, while you sit in the stall, the pit stop hand.</summary>
    public sealed class PitReadout
    {
        public bool Show;
        public string Title, Plan, Fuel, Button;
        public bool Requested, CanPress;
        public PitStrategy Strategy;
        /// <summary>Under caution: your call and the crew chief's.</summary>
        public bool Caution;
        public PitService Mine, Chief;
        public bool CallLocked;

        // the stall
        public PitGamePhase? Stop;
        public readonly List<(string name, string lane, string text, bool on, string block, bool prep, Tier? roll)> Hand = new List<(string, string, string, bool, string, bool, Tier?)>();
        public double Tire, FuelLane, Adjust, Total, PlanLeft;
        /// <summary>Work phase: seconds since GO, the drop (Total) and the release window after it.</summary>
        public double Elapsed, Window;
        public string Result;

        public void Update(PlayDesk d)
        {
            var r = d.Race; var me = r.Me;
            Show = !r.Track.Kart && !me.Dnf && r.Phase != RacePhase.Finished;
            Strategy = r.Strategy;
            Caution = r.Phase == RacePhase.Caution && r.Caution != null;
            Requested = r.PitRequest != null;
            var plan = r.MyPlan;
            var w = r.FuelWindowOf(me);
            Fuel = w is FuelWindow fw ? (fw.Short ? $"Fuel short: stop by lap {fw.Last}" : $"Fuel window laps {fw.First}-{fw.Last}") : "Fuel to the end";
            Plan = plan is PitPlan pp ? $"{(r.Strategy == PitStrategy.Safe ? "Safe" : "Gamble")}: {Svc(pp.Service)} on lap {pp.Lap}" : "No stop needed";
            if (Caution)
            {
                var k = r.Caution;
                Mine = k.Mine; Chief = k.Chief; CallLocked = k.Done;
                Title = CallLocked ? "Call locked" : "Caution: your call";
                Button = Mine == PitService.Stay ? "Stay out" : "Pit: " + Svc(Mine);
                CanPress = !CallLocked;
            }
            else
            {
                Title = me.Pit != null ? "On pit road" : Requested ? "Pitting next time by" : "Pit";
                Button = Requested ? "Cancel stop" : "PIT  " + Svc(plan?.Service ?? PitService.Four);
                CanPress = r.Phase == RacePhase.Green && me.Pit == null;
            }

            Hand.Clear();
            var pg = r.PitGame;
            Stop = pg?.Phase;
            Result = null;
            if (pg == null)
            {
                var last = r.LastPitGame;
                if (last != null && me.Pit != null) { Stop = PitGamePhase.Done; Result = StopText(last); }
                return;
            }
            foreach (var pc in pg.Hand)
            {
                var def = pc.Def;
                string lane = def.Lane == PitLane.Tire ? "Tires" : def.Lane == PitLane.Fuel ? "Fuel" : def.Lane == PitLane.Adjust ? "Adjust" : "Any";
                Hand.Add((def.Name, lane, def.Text, pc.On, pc.On ? null : r.PitCardBlock(pg, pc), pc.Prep, pc.Roll));
            }
            var lanes = pg.Phase == PitGamePhase.Plan ? r.PitLanesOf(pg, false) : pg.Lanes;
            Tire = lanes.Tire; FuelLane = lanes.Fuel; Adjust = lanes.Adjust; Total = pg.Phase == PitGamePhase.Plan ? lanes.Total : pg.Total;
            PlanLeft = d.PlanLeft;
            Elapsed = pg.Phase == PitGamePhase.Work ? r.T - pg.T0 : 0;
            Window = pg.Window;
            if (pg.Phase == PitGamePhase.Done) Result = StopText(pg);
        }

        static string StopText(PitGame pg) =>
            pg.Perfect ? $"Perfect stop: {pg.StopSeconds:F1} s" : $"{pg.StopSeconds:F1} s stop{(pg.ChiefRelease ? " (crew chief released)" : pg.Loose > 0 ? " (loose wheel)" : pg.InWindow ? "" : pg.ReleaseError > 0 ? " (early)" : " (late)")}";

        public static string Svc(PitService s) => s == PitService.Four ? "Four tires" : s == PitService.Two ? "Two tires" : s == PitService.Fuel ? "Fuel only" : "Stay out";
    }

    /// <summary>All the readouts the HUD draws, updated once a frame after PlayDesk.Frame.</summary>
    public sealed class HudReadouts
    {
        public readonly FocusReadout Focus = new FocusReadout();
        public readonly BeatClockReadout Clock = new BeatClockReadout();
        public readonly GaugeReadout Gauge = new GaugeReadout();
        public readonly DashReadout Dash = new DashReadout();
        public readonly ComboReadout Combo = new ComboReadout();
        public readonly PitReadout Pit = new PitReadout();

        public void Update(PlayDesk d)
        {
            if (!d.Race.CardsOn) return;
            Focus.Update(d); Clock.Update(d); Gauge.Update(d); Dash.Update(d); Combo.Update(d); Pit.Update(d);
        }
    }
}
