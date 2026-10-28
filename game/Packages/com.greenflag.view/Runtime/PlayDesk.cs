using System;
using System.Collections.Generic;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    public enum HandSort { BestOdds, Safest, Family }

    /// <summary>A short message over the hand: why a card couldn't be played, how one landed, a toss effect.</summary>
    public readonly struct Toast
    {
        public readonly string Title, Sub;
        public readonly Rgb Bg;
        /// <summary>Real time it was raised (PlayDesk.Clock).</summary>
        public readonly double At;
        public Toast(string title, string sub, Rgb bg, double at) { Title = title; Sub = sub; Bg = bg; At = at; }
    }

    /// <summary>
    /// The hand and beats rules of the slice, without any drawing (roadmap step 31). It owns the race's driver and decides
    /// what the player's input does: picking a card (the race drops to slow-mo while Focus lasts), playing it by clicking it
    /// again, dragging it up out of the hand or holding the Play button / Space, tossing it by dragging it onto the toss bin,
    /// the pause at each draw beat, P to pause, Esc to cancel, sort and filter chips, and the pit stop you plan in the stall.
    /// The Unity HUD draws from it (HudLayout says where) and feeds it pointer and key events (HudInput); the harness and the
    /// tests drive the same calls, so a race played with a mouse is a race CI can play.
    /// </summary>
    public sealed class PlayDesk
    {
        public readonly RaceDriver Driver;
        public Race Race => Driver.Race;

        public HandSort Sort = HandSort.BestOdds;
        /// <summary>Only this family's cards are shown (null = all).</summary>
        public CardFamily? Filter;
        /// <summary>Fade cards with a 5%+ wreck chance.</summary>
        public bool FadeRisk;
        /// <summary>Odds on the cards (O). Hard modes may turn them off; the cards then show "?".</summary>
        public bool ShowOdds = true;
        /// <summary>The race stops at each draw beat until you play a card or press P (the slice's BAL.beatPause).</summary>
        public bool BeatPause = true;
        public bool BeatPaused { get; private set; }

        /// <summary>Real seconds since the desk started (toasts, flashes, the pit plan timer).</summary>
        public double Clock { get; private set; }
        /// <summary>Every card in hand, in display order (filtered-out cards are flagged, not removed).</summary>
        public readonly List<CardFace> Cards = new List<CardFace>();
        /// <summary>The shown cards' uids in order: 1-9 pick these.</summary>
        public readonly List<int> Shown = new List<int>();
        public int? Hover;
        public Toast? Toast { get; private set; }
        public const double ToastSec = 2.4;

        public sealed class DragState
        {
            public int Uid;
            public int? PrevSel;
            public double X0, Y0, X, Y;
            /// <summary>Moved far enough to be a drag (10 px); before that a release is a click.</summary>
            public bool On;
            public bool OverPlay, OverBin;
        }
        public DragState Drag { get; private set; }

        public sealed class AimState
        {
            public int Uid;
            public int? PrevSel;
            public bool Key;
            /// <summary>Shift held, or the pointer slid off the Play button: letting go cancels.</summary>
            public bool Out;
        }
        public AimState Aim { get; private set; }

        /// <summary>Real seconds spent planning the current pit stop.</summary>
        public double PlanT { get; private set; }
        public double PlanLeft => Race.PitGame != null && Race.PitGame.Phase == PitGamePhase.Plan ? Math.Max(0, Race.Bal.PgPlanSec - PlanT) : 0;

        /// <summary>Cards played and tossed through the desk, and draw beats it paused at (the mouse player's report).</summary>
        public int Plays, Tosses, BeatStops;
        /// <summary>Crew calls taken and turned down through the desk.</summary>
        public int CallsTaken, CallsPassed;

        public PlayDesk(RaceDriver driver)
        {
            Driver = driver;
            Driver.EventHook = OnRaceEvent;
            Refresh();
        }

        /// <summary>A race with your deck for the persona (a kart persona on kart tracks) where you plan your own pit stops.</summary>
        public static Race NewRace(TrackDef track, uint seed, string persona = "drafter")
        {
            var rs = RaceSetup.Quick(track, seed, PlayerLoadout.ForPersona(PersonaFor(track, persona)));
            rs.PitPolicy = PitPolicy.Player;
            return new Race(rs);
        }

        static readonly Dictionary<string, string> kartOf = new Dictionary<string, string>
            { ["drafter"] = "packrat", ["bully"] = "brawler", ["whisperer"] = "linepainter", ["restart"] = "latebraker", ["gambler"] = "latebraker" };

        /// <summary>The kart persona that stands in for a stock one on kart tracks, and back.</summary>
        public static string PersonaFor(TrackDef t, string persona)
        {
            var p = Personas.Get(persona);
            if (t.Kart && !p.Kart) return kartOf.TryGetValue(persona, out var k) ? k : "packrat";
            if (!t.Kart && p.Kart) { foreach (var kv in kartOf) if (kv.Value == persona) return kv.Key; return "drafter"; }
            return persona;
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>
        /// One frame of realSeconds: slow-mo while a card is picked (drains Focus), the race holds while you plan a stop (the
        /// crew chief plans it when the plan timer runs out), then the driver runs the ticks and the hand is rebuilt.
        /// </summary>
        public void Frame(double realSeconds)
        {
            var r = Race;
            Clock += realSeconds;
            if (r.Selected is int s && !r.Hand.Exists(h => h.Uid == s)) r.Selected = null;
            if (r.Holding)
            {
                Driver.TimeScale = 0;
                if (r.PitGame.Auto == PitPolicy.Player && !Driver.Paused)
                {
                    PlanT += realSeconds;
                    if (PlanT >= r.Bal.PgPlanSec) { r.PitGo(true); Say("Crew chief made the call", "Plan time ran out", CardStyle.Panel); }
                }
            }
            else
            {
                if (r.PitGame == null) PlanT = 0;
                double k = 1;
                if (!Driver.Paused && !r.Done) k = r.SlowMo(realSeconds, r.Selected != null, HazardPicked);
                Driver.TimeScale = k;
            }
            Driver.Advance(realSeconds);
            if (BeatPaused && (r.Phase != RacePhase.Green || r.Me.Pit != null || r.Done)) Resume();
            Refresh();
            if (Toast is Toast t && Clock - t.At > ToastSec) Toast = null;
        }

        /// <summary>The Evade card is picked: its slow-mo is free.</summary>
        public bool HazardPicked => Race.HazardCard != null && Race.Selected == Race.HazardCard.Uid;

        /// <summary>The race is moving in slow-mo right now.</summary>
        public bool Slowing => !Driver.Paused && !Race.Holding && Race.Selected != null && Race.Attempt == null && Race.Phase != RacePhase.Finished
                               && Race.Bal.Slowmo < 1 && (Race.FocusMeter > 0 || HazardPicked || Race.Bal.FocusSec <= 0);

        void OnRaceEvent(RaceEvent e)
        {
            var r = Race;
            bool mine = e.Car == r.Me.Id;
            switch (e.Kind)
            {
                case RaceEventKind.DrawBeat:
                    if (BeatPause && r.Attempt == null && r.Phase == RacePhase.Green && r.Me.Pit == null && Aim == null)
                    {
                        Driver.Paused = true; BeatPaused = true; BeatStops++;
                    }
                    break;
                case RaceEventKind.CardResolved:
                    if (mine && e.Tier >= 0)
                    {
                        var t = (Tier)e.Tier;
                        Say(e.Text, null, CardStyle.Tier(t).Shade(-0.35f));
                    }
                    break;
                case RaceEventKind.CardTossed: Say("Tossed " + CardBook.Get(e.Card).Name, e.Text, CardStyle.Panel); break;
                case RaceEventKind.BoardFull: Say("Crew Board full", e.Text, Rgb.Hex("#1d3b5c")); break;
                case RaceEventKind.HazardOffered: Say("Wreck ahead!", "Evade is in your hand: free slow-mo", Rgb.Hex("#8a1c20")); break;
                case RaceEventKind.ReelOffered: Say("Reel Them In", "A free card to close the gap", Rgb.Hex("#1d3b5c")); break;
                case RaceEventKind.PerfectStop: Say("Perfect stop!", e.Text, Rgb.Hex("#1f6b40")); break;
                case RaceEventKind.LooseWheel: Say("Loose wheel", e.Text, Rgb.Hex("#8a1c20")); break;
                case RaceEventKind.Overdrive: if (!r.RadioOn) Say("Crew chief", e.Text, Rgb.Hex("#6b3a10")); break;
                case RaceEventKind.RadioAnswered:
                    bool ok = e.Tier == (int)Tier.Success;
                    Say(ok ? "Radio gamble works" : "Radio gamble fails", e.Text, ok ? Rgb.Hex("#1f6b40") : Rgb.Hex("#8a1c20"));
                    break;
            }
        }

        void Say(string title, string sub, Rgb bg) => Toast = new Toast(title, sub, bg, Clock);

        /// <summary>Rebuilds the card faces: sorted (blocked last), filtered, faded, best pick, combo links, 1-9 keys.</summary>
        public void Refresh()
        {
            var r = Race;
            Cards.Clear(); Shown.Clear();
            if (!r.CardsOn) return;
            var c = r.Ctx();
            foreach (var h in r.Hand) Cards.Add(CardFace.Of(r, h, c));
            Cards.Sort((a, b) =>
            {
                if ((a.Block != null) != (b.Block != null)) return a.Block != null ? 1 : -1;
                int k;
                switch (Sort)
                {
                    case HandSort.Safest: k = a.Odds.Wreck.CompareTo(b.Odds.Wreck); if (k == 0) k = b.Odds.Succ.CompareTo(a.Odds.Succ); break;
                    case HandSort.Family: k = CardStyle.OrderOf(a.Family).CompareTo(CardStyle.OrderOf(b.Family)); if (k == 0) k = b.Odds.Succ.CompareTo(a.Odds.Succ); break;
                    default: k = b.Odds.Succ.CompareTo(a.Odds.Succ); break;
                }
                return k != 0 ? k : a.Uid.CompareTo(b.Uid);
            });
            CardFace best = null;
            foreach (var f in Cards)
            {
                f.Filtered = Filter != null && f.Family != Filter;
                f.Faded = f.Block == null && FadeRisk && f.Odds.Wreck >= 0.05;
                f.Selected = r.Selected == f.Uid;
                if (!f.Filtered) { Shown.Add(f.Uid); f.Key = Shown.Count <= 9 ? Shown.Count : 0; }
                if (best == null && f.Block == null && !f.Filtered) best = f;
            }
            if (best != null && best.Odds.Succ >= 0.5 && Sort == HandSort.BestOdds && !best.Faded) best.Best = true;
        }

        public CardFace Face(int uid) => Cards.Find(f => f.Uid == uid);

        /// <summary>Families in hand, in chip order.</summary>
        public List<CardFamily> Families()
        {
            var l = new List<CardFamily>();
            foreach (var f in Cards) if (!l.Contains(f.Family)) l.Add(f.Family);
            l.Sort((a, b) => CardStyle.OrderOf(a).CompareTo(CardStyle.OrderOf(b)));
            return l;
        }

        /// <summary>The card the HUD previews: the one being aimed or dragged, else the picked one, else the hovered one.</summary>
        public CardFace Preview
        {
            get
            {
                int? u = Aim?.Uid ?? (Drag != null && Drag.On ? Drag.Uid : (int?)null) ?? Race.Selected ?? Hover;
                return u is int x ? Face(x) : null;
            }
        }

        // ------------------------------------------------------------------ picking, playing, tossing

        /// <summary>Pick a card (or pick nothing). A picked card runs the race in slow-mo while Focus lasts.</summary>
        public void Select(int? uid)
        {
            var r = Race;
            if (uid is int u && !r.Hand.Exists(h => h.Uid == u)) return;
            r.Selected = uid;
            Refresh();
        }

        /// <summary>Click a card: picks it, or plays it if it is already picked.</summary>
        public string Click(int uid)
        {
            if (Race.Selected == uid) return Play(uid);
            Select(uid);
            return null;
        }

        /// <summary>Play a card now. Returns null, or why not (also shown as a toast). Playing ends a draw-beat pause.</summary>
        public string Play(int uid)
        {
            var r = Race;
            var why = r.PlayCard(uid);
            if (why != null) { Say("Can't play that", why, Rgb.Hex("#5b2a2d")); Refresh(); return why; }
            Plays++;
            if (r.Selected == uid) r.Selected = null;
            if (BeatPaused) Resume();
            Refresh();
            return null;
        }

        /// <summary>Toss a card onto the bin: it charges the Crew Board and does its toss effect.</summary>
        public string Toss(int uid)
        {
            var why = Race.TossCard(uid);
            if (why != null) { Say("Can't toss that", why, Rgb.Hex("#5b2a2d")); Refresh(); return why; }
            Tosses++;
            Refresh();
            return null;
        }

        /// <summary>Esc: cancel an aim or a drag, else put the picked card down.</summary>
        public void Cancel()
        {
            if (Aim != null) { AimEnd(true); return; }
            if (Drag != null) { DragEnd(true); return; }
            Select(null);
        }

        /// <summary>P: pause or resume (also ends a draw-beat pause).</summary>
        public void TogglePause()
        {
            if (Driver.Paused) Resume();
            else Driver.Paused = true;
        }

        void Resume() { Driver.Paused = false; BeatPaused = false; }

        // ------------------------------------------------------------------ drag to play

        /// <summary>Pointer down on a card. Nothing happens until it moves 10 px; a release before that is a click.</summary>
        public void DragStart(int uid, double x, double y)
        {
            if (!Race.Hand.Exists(h => h.Uid == uid)) return;
            Drag = new DragState { Uid = uid, PrevSel = Race.Selected, X0 = x, Y0 = y, X = x, Y = y };
        }

        /// <summary>The pointer moved while dragging. Picking the card up picks it, so the gauge and slow-mo run while you aim.</summary>
        public void DragMove(double x, double y, bool overPlay, bool overBin)
        {
            var d = Drag;
            if (d == null) return;
            d.X = x; d.Y = y;
            if (!d.On)
            {
                if (Math.Sqrt((x - d.X0) * (x - d.X0) + (y - d.Y0) * (y - d.Y0)) < 10) return;
                d.On = true;
                if (Race.Selected != d.Uid) Select(d.Uid);
            }
            d.OverBin = overBin;
            d.OverPlay = overPlay && !overBin;
        }

        /// <summary>
        /// Let go: above the hand plays it, on the toss bin tosses it, back on the hand puts it down (the card picked before
        /// stays picked). A drag that never moved is a click. Returns "played", "tossed", "clicked" or null.
        /// </summary>
        public string DragEnd(bool cancel)
        {
            var d = Drag; Drag = null;
            if (d == null) return null;
            if (!Race.Hand.Exists(h => h.Uid == d.Uid)) return null;
            if (!d.On) { if (!cancel) { Click(d.Uid); return "clicked"; } return null; }
            if (!cancel && d.OverBin) return Toss(d.Uid) == null ? "tossed" : null;
            if (!cancel && d.OverPlay) return Play(d.Uid) == null ? "played" : null;
            Select(d.PrevSel is int p && p != d.Uid && Race.Hand.Exists(h => h.Uid == p) ? p : (int?)null);
            return null;
        }

        // ------------------------------------------------------------------ hold to play

        /// <summary>What hold-to-play aims: the picked card if it can be played, else the best playable one.</summary>
        public int? AimTarget()
        {
            var r = Race;
            if (r.Attempt != null || r.Phase == RacePhase.Finished || !r.CardsOn) return null;
            var c = r.Ctx();
            if (r.Selected is int s) { var h = r.Hand.Find(x => x.Uid == s); if (h != null && r.CardBlock(h.Def, c) == null) return s; }
            HandCard best = null; double bo = -1;
            foreach (var h in r.Hand)
            {
                if (r.CardBlock(h.Def, c) != null) continue;
                double o = r.ComputeOdds(h.Def, c).Succ;
                if (o > bo) { bo = o; best = h; }
            }
            return best?.Uid;
        }

        /// <summary>
        /// Hold Space or the Play button: the race goes green (out of a pause or a draw beat) in slow-mo while you hold, and
        /// letting go plays the card. Returns false when there is nothing to aim.
        /// </summary>
        public bool AimStart(bool key)
        {
            if (Aim != null) return true;
            var uid = AimTarget();
            if (uid == null) return false;
            Aim = new AimState { Uid = uid.Value, PrevSel = Race.Selected, Key = key };
            if (Driver.Paused) Resume();
            Select(uid);
            return true;
        }

        public void AimOut(bool @out) { if (Aim != null) Aim.Out = @out; }

        /// <summary>Let go of Space or the button: plays the aimed card unless cancelled (Shift, slid off, Esc).</summary>
        public string AimEnd(bool cancel)
        {
            var a = Aim; Aim = null;
            if (a == null) return null;
            if (!cancel && !a.Out && Race.Attempt == null && Race.Hand.Exists(h => h.Uid == a.Uid)) return Play(a.Uid) == null ? "played" : null;
            Select(a.PrevSel is int p && Race.Hand.Exists(h => h.Uid == p) ? p : (int?)null);
            return null;
        }

        // ------------------------------------------------------------------ chips

        public void SetSort(HandSort s) { Sort = s; Refresh(); }
        public void SetFilter(CardFamily? f) { Filter = f; Refresh(); }
        public void ToggleFade() { FadeRisk = !FadeRisk; Refresh(); }
        public void ToggleOdds() { ShowOdds = !ShowOdds; }

        /// <summary>1-9: pick (or play, if already picked) the nth shown card.</summary>
        public string Key(int n) => n >= 1 && n <= Shown.Count ? Click(Shown[n - 1]) : null;

        // ------------------------------------------------------------------ the radio

        /// <summary>A risky crew call is on the radio (Try it / No thanks answer it).</summary>
        public bool RiskyCall => Race.Radio != null && Race.Radio.Kind == RadioKind.Mech;

        /// <summary>Try the call on the radio. The result shows as a toast and in the radio lane.</summary>
        public string TakeCall()
        {
            int n = Race.Events.Count;
            var why = Race.TakeCall();
            if (why == null) { CallsTaken++; Driver.Deliver(n); }
            return why;
        }

        /// <summary>Turn the call down (or wave off advice).</summary>
        public void PassCall()
        {
            if (Race.Radio == null) return;
            int n = Race.Events.Count;
            Race.PassCall();
            CallsPassed++;
            Driver.Deliver(n);
        }

        // ------------------------------------------------------------------ pit road

        /// <summary>The PIT button: on green it asks for the plan's stop next time by (press again to cancel); under caution it
        /// switches your call between staying out and the stop.</summary>
        public void PressPit() => Race.PressPit();

        public void SetStrategy(PitStrategy s) => Race.Strategy = s;

        public void CautionCall(PitService s) => Race.SetCautionCall(s);

        public string PitToggle(int i) { var why = Race.PitToggle(i); if (why != null) Say("Can't use that", why, Rgb.Hex("#5b2a2d")); return why; }

        /// <summary>Let the crew chief fill the lanes (the plan stays open so you can change it).</summary>
        public void PitAuto() { if (Race.Holding) Race.PitAutoPlan(Race.PitGame, true); }

        public void PitGo() { if (Race.Holding) Race.PitGo(); }

        public string PitRelease()
        {
            var why = Race.PitRelease();
            if (why != null && why != "Loose wheel") Say("Not yet", why, Rgb.Hex("#5b2a2d"));
            return why;
        }
    }
}
