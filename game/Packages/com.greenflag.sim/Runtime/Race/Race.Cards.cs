using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    /// <summary>A card in your hand. Uid is unique for the race; Beat is the draw beat it arrived on (cards go stale after two).</summary>
    public sealed class HandCard
    {
        public int Uid;
        public string Id;
        public int Beat;
        /// <summary>Evade or Reel Them In: dealt by the race, can't be tossed.</summary>
        public bool Special;
        /// <summary>Drawn as a follow-up after a landed card.</summary>
        public bool Follow;
        public CardDef Def => CardBook.Get(Id);
        public override string ToString() => $"{Id}#{Uid}";
    }

    /// <summary>An odds boost waiting for your next card (of one family, or any card when Family is null).</summary>
    public sealed class OddsBuff
    {
        public int V;
        public CardFamily? Family;
        public string Source;
        public double Until = double.PositiveInfinity;
    }

    /// <summary>A played card setting up: it resolves at End with the odds locked when you played it.</summary>
    public sealed class CardAttempt
    {
        public HandCard Card;
        public CardDef Def;
        public double T0, Dur, End;
        public RaceCar Target;
        public List<OddsBuff> Buffs;
        public Odds Odds;
        /// <summary>The combo link it was played on (0 = none).</summary>
        public int Combo;
    }

    public sealed class InstalledSetup
    {
        public string Id;
        public int Lvl;
        public double At;
        public SetupDef Def => PitCards.Setups[Id];
    }

    /// <summary>Per-race card counters, for the harness, the reward screen and tests.</summary>
    public sealed class CardStats
    {
        public int Plays, Succ, Crits, Combos, BestChain, Follow, BoardFires, Expired, Tossed, TossFx, TossLive, RiskRefills, Reels, Returned;
        public int PitStops, Perfect, Setups, Packages, Loose, Repassed, PushBack;
        public double FocusGain;
        public readonly List<double> StopTimes = new List<double>();
        /// <summary>Plays and outcomes per card id.</summary>
        public readonly Dictionary<string, int[]> ByCard = new Dictionary<string, int[]>();
    }

    /// <summary>
    /// The card layer (step 27): your deck, hand and draw beats, odds and five-outcome resolution, Momentum, Heat, combos, the
    /// Crew Board, tosses, Focus, Evade and Reel Them In. A port of the slice's draw / playCard / resolveAttempt / burnCard /
    /// drawBeat / computeOdds. It only runs when the race has a Loadout. Draws, shuffles and outcome rolls use CardRng, so a
    /// hand you never play leaves the race core's rolls untouched.
    /// </summary>
    public sealed partial class Race
    {
        public const int HandMax = 8;

        public readonly PlayerLoadout Loadout;
        public readonly SimRng CardRng;
        /// <summary>This race's copy of your tune (pit Setups nudge it).</summary>
        public Tune Tune;
        public bool CardsOn => Loadout != null;

        public readonly List<HandCard> Hand = new List<HandCard>();
        public readonly List<string> Pile = new List<string>();
        public readonly List<string> Discard = new List<string>();
        public int Momentum;
        /// <summary>Slow-mo budget, 0 to 1.</summary>
        public double FocusMeter = 1;
        public int Beats;
        /// <summary>Crew Board charges (spare cards turn into fresh ones when it fills).</summary>
        public int Board;
        public List<OddsBuff> Buffs = new List<OddsBuff>();
        public readonly List<OddsBuff> TossBuffs = new List<OddsBuff>();
        public int Chain;
        public CardFamily? ChainFam, ChainRoot;
        public string ChainId;
        public double ChainAt;
        public CardAttempt Attempt;
        public readonly List<InstalledSetup> Setups = new List<InstalledSetup>();
        public int? SetupFresh;
        /// <summary>Choose the Lane / Stay Out: odds for restart cards at the next restart, and spots gained (or lost) at the green.</summary>
        public int RestartBuff, StartGain;
        public bool LuckyUsed;
        /// <summary>The card the player has selected (set by the UI). It never goes stale while selected.</summary>
        public int? Selected;
        /// <summary>The car that last passed you, and when (Crossover).</summary>
        public RaceCar LostTo;
        public double LostAt = -99;
        public double NextDraw;
        public readonly CardStats CardStats = new CardStats();
        public HandCard HazardCard;
        /// <summary>Tutorials and tests: the next card resolves with this outcome instead of rolling.</summary>
        public Tier? ForceNextTier;

        int nextUid = 1, lastPos, followN;
        double? lastBeatU, reelFar;
        double lastBeatAt = -99, overAt = -99, reelCd;
        double[] beatU;

        /// <summary>Restart cards can be played on the start and on restarts.</summary>
        public bool RestartWindow => Phase == RacePhase.Start || Phase == RacePhase.Restart;

        static int Rd(double x) => (int)SimMath.RoundJs(x);

        void CardsInit()
        {
            Tune = Loadout.Tune.Clone();
            Loadout.Apply(Mods, Track, Tune);
            Pile.AddRange(Loadout.Deck);
            CardRng.Shuffle(Pile);
            Momentum = (int)Loadout.PartSum(p => p.StartMom);
            NextDraw = 99999;
            if (Track.Kart) Tow = (int)Loadout.PartSum(p => p.StartTow);
            lastPos = Me.Start;
            DrawCards(5);
            beatU = BeatPoints();
        }

        // ---------------------------------------------------------------- reading the race

        public CardCtx Ctx() => new CardCtx(this, Me);

        /// <summary>Why a card can't be played now, or null (the slice's cardBlock).</summary>
        public string CardBlock(CardDef cd, CardCtx c)
        {
            if (c.Car.Dnf) return "Out of the race";
            if (c.Car.Pit != null) return "On pit road";
            if (Phase == RacePhase.Start && cd.Family != CardFamily.Restart) return "Wait for the green flag";
            if (Phase == RacePhase.Caution && !cd.Situational && cd.Id != "choose_lane") return "Under caution";
            if (Phase == RacePhase.Caution && cd.Id == "restart_jump") return "Restarts only";
            if (cd.TireCost > 0 && c.Tires < cd.TireCost * Bal.TireCost + 5) return "Not enough tire left";
            return cd.Can?.Invoke(c);
        }

        public string CardBlock(HandCard h) => CardBlock(h.Def, Ctx());
        public Odds OddsOf(HandCard h) => ComputeOdds(h.Def, Ctx());

        double ComboWin()
        {
            double w = Bal.ComboWin;
            if (ChainFam != null && Tune.Focus != ComboFocus.None)
            {
                var root = ChainRoot ?? ChainFam.Value;
                w += Tune.FocusHas(root) ? (Tune.Freeze ? 0 : Tune.FocusWindow) : -Tune.OffFocusWindow;
            }
            if (Track.Kart) w -= KartWin;
            return w;
        }

        /// <summary>How long the live combo's window is in all (the combo meter's full width).</summary>
        public double ComboWindow => ComboWin();

        /// <summary>Seconds left to land a card that chains off the last one (0 = no live combo).</summary>
        public double ComboLeft => ChainFam != null ? Math.Max(0, ComboWin() - (T - ChainAt)) : 0;

        /// <summary>Does this card chain off the last landed one? Kart cards name their follow-ups; stock cards link by family.</summary>
        public bool LinksTo(CardDef cd)
        {
            var pv = ChainId != null ? CardBook.Get(ChainId) : null;
            if (pv?.Next != null) return Array.IndexOf(pv.Next, cd.Id) >= 0 && (cd.LinkOk == null || cd.LinkOk(this));
            return ChainFam != null && Array.IndexOf(CardRules.Links[ChainFam.Value], cd.Family) >= 0;
        }

        /// <summary>The combo link this card would be played on, or 0.</summary>
        public int ComboFor(CardDef cd) =>
            Bal.ChainDraw > 0 && ChainFam != null && (!Bal.ComboNew || cd.Id != ChainId) && T - ChainAt <= ComboWin() && !(Track.Kart && Chain >= 3) && LinksTo(cd) ? Chain : 0;

        /// <summary>The slice's computeOdds: the card's read plus every modifier, turned into five outcome chances.</summary>
        public Odds ComputeOdds(CardDef cd, CardCtx c)
        {
            var R = new OddsBuilder();
            cd.Read(c, R);
            var st = Me.Stats;
            var f = cd.Family;
            if (f == CardFamily.Pass || f == CardFamily.Draft) R.A((st.Racecraft - 5) * 2, "Racecraft");
            if (f == CardFamily.Defend) R.A(Rd((st.Defense - 5) * 2.5), "Defense");
            if (f == CardFamily.Pass && c.Seg.Turn) R.A(Rd((st.Handling - 5) * 1.5), "Handling");
            R.A(Loadout.Spotter - 5, "Spotter");
            if (Momentum != 0) R.A(Momentum * 2, $"Momentum ×{Momentum}", "mom");
            foreach (var p in Loadout.EquippedParts()) if (p.Read != null) { int v = p.Read(cd, c); if (v != 0) R.A(v, p.Name); }
            foreach (var (v, label) in SetupReads(cd)) R.A(v, label, "setup");
            foreach (var b in Buffs) if (b.Family == null || b.Family == f) R.A(b.V, b.Source, "boost");
            foreach (var b in TossBuffs) if (b.Family == f && T <= b.Until) R.A(b.V, b.Source, "boost");
            int k = ComboFor(cd);
            if (k > 0)
            {
                bool kart = Track.Kart;
                double per = Bal.ComboBonus + (kart ? KartBonus : 0) + (Tune.FocusHas(ChainRoot ?? ChainFam.Value) ? Tune.FocusBonus : 0);
                R.A(Math.Min(Bal.ComboCap + (kart ? KartBonus * 2 : 0), Math.Min(3, k) * per), $"Combo ×{k + 1}", "combo");
            }
            int hv = Tune.HandRead(cd); if (hv != 0) R.A(hv, Tune.Hand > 0 ? "Loose tune" : "Tight tune", "tune");
            int av = Tune.AeroRead(cd, c); if (av != 0) R.A(av, Tune.Aero > 0 ? "Low drag" : "Downforce", "tune");
            int fv = Tune.FocusRead(cd, Track); if (fv != 0) R.A(fv, Tune.FocusName(Track.Kart), "tune");
            DamageOdds(cd, c.Car, R);
            ShapeOdds(cd, c, R);
            if (Heat > 0 && (f == CardFamily.Pass || f == CardFamily.Risk)) R.A(-Heat * 3, $"Heat ×{Heat}", "heat");
            if ((f == CardFamily.Pass || f == CardFamily.Draft) && c.Ahead != null && c.Ahead.HasEffect(EffectKind.Block)) R.A(-22, "He is blocking", "block");
            DriverOdds(cd, c, R);
            if (Bal.OddsAdj != 0) R.B(Bal.OddsAdj);

            var o = new Odds { Read = R.V, Sd = Math.Max(9, 21 - st.Consistency * 1.1), Mods = R.Mods };
            double read = o.Read, sd = o.Sd;
            double F(double x) => OddsMath.Phi((x - read) / sd);
            double cf = F(Bal.CfLine), fl = F(35) - F(Bal.CfLine), n = F(58) - F(35), s = F(90) - F(58), cs = 1 - F(90);
            if (!cd.CanWreck) { fl += cf; cf = 0; }
            if (cd.NoFail) { n += fl; fl = 0; }
            double cfm = 1;
            foreach (var p in Loadout.EquippedParts()) if (p.Cf != null) cfm *= p.Cf(cd);
            if (cfm < 1) { double mv = cf * (1 - cfm); cf -= mv; fl += mv; }
            double sc = s + cs;
            if (Bal.OddsCap < 1 && sc > Bal.OddsCap)
            {
                double kk = Bal.OddsCap / sc, ex = sc - Bal.OddsCap;
                s *= kk; cs *= kk;
                if (cd.NoFail) n += ex; else { fl += ex * 0.6; n += ex * 0.4; }
            }
            o.P[(int)Tier.CritFail] = cf; o.P[(int)Tier.Fail] = fl; o.P[(int)Tier.Neutral] = n; o.P[(int)Tier.Success] = s; o.P[(int)Tier.CritSuccess] = cs;
            o.Range = Math.Max(1, (11 - Loadout.Spotter) * 0.7 * Loadout.PartMul(p => p.Range));
            var tags = R.Tags.FindAll(t => Math.Abs(t.Value) >= 3);
            SimMath.StableSort(tags, (a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            o.Tags = tags;
            double msum = 0; foreach (var kv in R.Mods) msum += kv.Value;
            o.BaseSucc = 1 - F(58 + msum);
            return o;
        }

        // DZ.odds: per point of zone damage, by family; ignored under DZ.min, capped at DZ.cap
        static double DzPer(int zone, CardFamily f)
        {
            switch (zone)
            {
                case 0: return f == CardFamily.Draft ? .45 : f == CardFamily.Restart ? .25 : f == CardFamily.Pass ? .12 : 0;
                case 1: return f == CardFamily.Pass ? .4 : f == CardFamily.Risk ? .3 : f == CardFamily.Draft ? .1 : 0;
                default: return f == CardFamily.Defend ? .45 : f == CardFamily.Car ? .2 : f == CardFamily.Restart ? .1 : 0;
            }
        }

        void DamageOdds(CardDef cd, RaceCar car, OddsBuilder R)
        {
            ReconcileZones(car);
            var z = car.Zones;
            double[] v = { z.Nose, z.Side, z.Tail };
            string[] names = { "Nose", "Side", "Tail" };
            for (int i = 0; i < 3; i++)
            {
                double per = DzPer(i, cd.Family);
                if (per == 0 || v[i] < 4) continue;
                int x = -Math.Min(18, Rd(v[i] * per));
                if (x != 0) R.A(x, names[i] + " damage", "dmg");
            }
        }

        /// <summary>What the shape of the track does to a card's read (non-oval tracks only).</summary>
        static void ShapeOdds(CardDef cd, CardCtx c, OddsBuilder R)
        {
            var s = c.Seg;
            if (!c.Track.IsPath) return;
            var f = cd.Family; var id = cd.Id; var k = s.Kind;
            string w = s.Corner != null ? s.Corner.ToLowerInvariant() : "";
            if (k == TurnKind.Hairpin)
            {
                if (s.Approach) { if (f == CardFamily.Pass) R.A(10, "Hairpin braking zone", "shape"); if (f == CardFamily.Defend) R.A(4, "Cover the inside", "shape"); }
                else { if (id == "bump_run") R.A(8, "Slow hairpin", "shape"); if (f == CardFamily.Draft) R.A(-10, "No draft in a hairpin", "shape"); }
            }
            else if (k == TurnKind.Esses)
            {
                if (id == "crossover") R.A(10, "Cut back through the " + w, "shape"); else if (f == CardFamily.Pass) R.A(-12, "No room in the " + w, "shape");
                if (f == CardFamily.Defend) R.A(8, "Hard to pass in the " + w, "shape");
                if (f == CardFamily.Risk) R.A(-6, "Easy to lose it in the " + w, "shape");
                if (f == CardFamily.Draft) R.A(-8, "No draft in the " + w, "shape");
            }
            else if (k == TurnKind.Sweeper)
            {
                if (f == CardFamily.Pass) R.A(-8, "Fast sweeper", "shape");
                if (id == "push_limit") R.A(8, "Carry speed through the sweeper", "shape");
            }
            else if (k == TurnKind.Straight && f == CardFamily.Draft) R.A(s.Long ? 6 : -8, s.Long ? "Long straight" : "Short straight", "shape");
        }

        /// <summary>
        /// Step 23: the field races back (a pass or draft card reads lower against a car ahead with good Defense, a defend card
        /// against a car behind with good Racecraft), each rung up the field is sharper, and Grit moves every card late on.
        /// </summary>
        void DriverOdds(CardDef cd, CardCtx c, OddsBuilder R)
        {
            double tough = Track.Kart ? 1 : Track.Late ? 1.5 : 2;
            var f = cd.Family;
            var o = f == CardFamily.Pass || f == CardFamily.Draft ? c.Ahead : f == CardFamily.Defend ? c.Behind : null;
            if (o != null && !o.IsMe && !o.IsPace)
            {
                int st = f == CardFamily.Defend ? o.Stats.Racecraft : o.Stats.Defense;
                int v = -Rd(Math.Max(0, st - 4) * tough);
                if (v != 0)
                {
                    string nm = o.Ref?.Name ?? "He"; int sp = nm.LastIndexOf(' ');
                    R.A(v, $"{(sp >= 0 ? nm.Substring(sp + 1) : nm)} {(f == CardFamily.Defend ? "racecraft" : "defense")} {st}", "field");
                }
            }
            int field = Track.Kart ? 0 : Track.Late ? -6 : -12;
            if (field != 0) R.A(field, Track.Late ? "Late models field" : "Stock cars field", "field");
            int g = (int)Math.Floor((Loadout.Grit - 10) / 2.0);
            if (g != 0 && Me.Dist / Track.Laps >= 0.67) R.A(g * 2, $"Grit {Loadout.Grit} · late race", "drv");
        }

        // ---------------------------------------------------------------- drawing

        public void AddMomentum(int n) => Momentum = (int)SimMath.Clamp(Momentum + n, 0, 5);

        /// <summary>Your next card (of a family, or any card) gets +v. Replaces a waiting boost.</summary>
        public void SetBuff(int v, CardFamily? fam, string src) { Buffs = new List<OddsBuff> { new OddsBuff { V = v, Family = fam, Source = src } }; }

        /// <summary>
        /// Draws read the race: cards you can use right now come up far more often than ones you can't, caution and restart
        /// cards mostly arrive with a caution, and after a landed card the cards it sets up are favored.
        /// </summary>
        double DrawWeight(string id, CardCtx c, bool favor)
        {
            var cd = CardBook.Get(id);
            double w;
            if (cd.Situational) w = Phase == RacePhase.Caution || Phase == RacePhase.Restart ? 4 : 0.04;
            else if (CardBlock(cd, c) != null) w = 0.12;
            else
            {
                bool fav = false;
                if (favor)
                {
                    var pv = ChainId != null ? CardBook.Get(ChainId) : null;
                    fav = pv?.Next != null ? Array.IndexOf(pv.Next, id) >= 0 : ChainFam != null && Array.IndexOf(CardRules.Links[ChainFam.Value], cd.Family) >= 0;
                }
                w = (0.5 + ComputeOdds(cd, c).Succ) * (fav ? Bal.LinkW * (Tune.FocusHas(cd.Family) ? 1.5 : 1) : 1) * Tune.DrawMul(cd.Family);
            }
            double k = 1;
            foreach (var s in Setups) if (s.Def.Family == cd.Family) k += 0.25 * s.Lvl;
            return w * k;
        }

        /// <summary>Draw up to n cards (hand max 8), reshuffling the discard pile when the deck runs out.</summary>
        public void DrawCards(int n) => DrawCards(n, false);

        List<HandCard> DrawCards(int n, bool favor)
        {
            var got = new List<HandCard>();
            var c = Ctx();
            for (int i = 0; i < n; i++)
            {
                if (Hand.Count >= HandMax) break;
                if (Pile.Count == 0) { Pile.AddRange(Discard); Discard.Clear(); CardRng.Shuffle(Pile); }
                if (Pile.Count == 0) break;
                int k = Pile.Count - 1;
                if (Phase != RacePhase.Start)
                {
                    var w = new double[Pile.Count];
                    double sum = 0;
                    for (int j = 0; j < w.Length; j++) { w[j] = DrawWeight(Pile[j], c, favor); sum += w[j]; }
                    double x = CardRng.NextDouble() * sum;
                    for (k = 0; k < w.Length - 1; k++) { x -= w[k]; if (x <= 0) break; }
                }
                var id = Pile[k];
                Pile.RemoveAt(k);
                var h = new HandCard { Uid = nextUid++, Id = id, Beat = Beats };
                Hand.Add(h); got.Add(h);
            }
            return got;
        }

        void BoardAdd(int n)
        {
            if (Bal.Board <= 0 || n == 0) return;
            Board += n;
            int k = 0;
            while (Board >= Bal.Board) { Board -= Bal.Board; k++; }
            if (k == 0) return;
            DrawCards(Bal.BoardDraw * k);
            CardStats.BoardFires += k;
            Emit(RaceEventKind.BoardFull, Me, null, $"{Bal.BoardDraw * k} fresh cards");
        }

        /// <summary>Draw beat positions, as track positions (0-1): the braking zones before the straights' ends, or the named path pieces.</summary>
        double[] BeatPoints()
        {
            if (Track.IsPath)
            {
                var u = new List<double>();
                foreach (int i in Track.BeatAt ?? new int[0]) u.Add(Shape.Segments[i].BrakeA / Shape.Perimeter);
                u.Sort();
                return u.ToArray();
            }
            double P = Shape.Perimeter, Tn = Math.PI * Track.R;
            double back = (Track.S + Tn + 0.45 * Track.S) / P, front = 0.45 * Track.S / P;
            return Track.BeatPoints >= 2 ? new[] { front, back } : new[] { back };
        }

        /// <summary>Draw beats per lap on this track (the beat clock's full turn is a lap divided by this).</summary>
        public int BeatsPerLap => beatU?.Length ?? 0;

        /// <summary>Seconds of racing until your next draw beat (green flag only).</summary>
        public double NextBeatIn
        {
            get
            {
                if (beatU == null || beatU.Length == 0) return double.PositiveInfinity;
                double u = Shape.UOf(Me.Dist), du = 1;
                foreach (var p in beatU) { double d = Shape.UGap(u, p); if (d > 0 && d < du) du = d; }
                return du * Track.LapTime;
            }
        }

        /// <summary>A draw beat: stale green-flag cards go back to the deck, fresh ones come in, and Focus refills.</summary>
        void DrawBeat(int n)
        {
            Beats++; followN = 0;
            if (Bal.FocusSec > 0) FocusMeter = Math.Min(1, FocusMeter + Bal.FocusRegen);
            if (Bal.Fleeting > 0)
            {
                var old = Hand.FindAll(h => !h.Special && !h.Def.Situational && Beats - h.Beat >= Bal.Fleeting && h.Uid != Selected);
                if (old.Count > 0)
                {
                    foreach (var h in old) { Hand.Remove(h); Discard.Add(h.Id); }
                    CardStats.Expired += old.Count;
                    Emit(RaceEventKind.CardsExpired, Me, null, string.Join(",", old.ConvertAll(h => h.Id)));
                    BoardAdd(old.Count);
                }
            }
            foreach (var h in DrawCards(n, false)) h.Beat = Beats;
            Emit(RaceEventKind.DrawBeat, Me, null, $"beat {Beats}");
        }

        /// <summary>Put a card straight into your hand (tutorials, qualifying cards, tests). Specials can't be tossed.</summary>
        public HandCard GiveCard(string id)
        {
            var cd = CardBook.Get(id);
            var h = new HandCard { Uid = nextUid++, Id = id, Beat = Beats, Special = cd.Special };
            Hand.Add(h);
            return h;
        }

        // ---------------------------------------------------------------- playing

        /// <summary>
        /// Play a card from your hand. It costs Momentum now, its odds lock now, and it resolves after the family's setup time.
        /// Returns null, or why it can't be played.
        /// </summary>
        public string PlayCard(int uid)
        {
            if (!CardsOn) return "No cards in this race";
            if (Phase == RacePhase.Finished) return "The race is over";
            var h = Hand.Find(x => x.Uid == uid);
            if (h == null) return "Not in your hand";
            var cd = h.Def; var car = Me;
            if (car.Dnf) return "Out of the race";
            if (Attempt != null) return "One move at a time";
            var c = Ctx();
            var why = CardBlock(cd, c);
            if (why != null) return why;
            var o0 = ComputeOdds(cd, c);
            LastPlayAt = T; // tire cost is charged when the move lands (tires never go back up)
            if (cd.MomCost > 0) Momentum -= cd.MomCost;
            Hand.Remove(h);
            if (h.Special)
            {
                if (cd.Id == "evade") { Hazard = null; HazardCard = null; }
                else reelCd = T + 15;
            }
            else Discard.Add(h.Id);
            if (Selected == uid) Selected = null;
            var target = cd.Family == CardFamily.Defend ? c.Behind : cd.Family == CardFamily.Pass || cd.Family == CardFamily.Draft || cd.Family == CardFamily.Risk || cd.Family == CardFamily.Restart ? c.Ahead : null;
            double dur = CardRules.AttemptSeconds(cd.Family);
            Attempt = new CardAttempt { Card = h, Def = cd, T0 = T, Dur = dur, End = T + dur, Target = target, Buffs = Buffs, Odds = o0, Combo = ComboFor(cd) };
            Buffs = new List<OddsBuff>();
            TossBuffs.RemoveAll(b => T > b.Until);
            var mine = TossBuffs.FindAll(b => b.Family == cd.Family);
            if (mine.Count > 0) { Attempt.Buffs = new List<OddsBuff>(Attempt.Buffs); Attempt.Buffs.AddRange(mine); TossBuffs.RemoveAll(mine.Contains); }
            Emit(RaceEventKind.CardPlayed, car, target, $"{cd.Name} {o0.Succ * 100:F0}%", cd.Id);
            return null;
        }

        static readonly string[] GoneReasons = { "No car ahead", "No one behind", "No one close behind" };

        void ResolveAttempt()
        {
            var a = Attempt; Attempt = null;
            var cd = a.Def; var car = Me;
            if (car.Dnf) return;
            var c = Ctx();
            var o = a.Odds;
            // the car you were setting up moved away: the card comes back with its costs refunded, so locked odds are never wasted
            var can = cd.Can?.Invoke(c);
            if (can != null && Array.IndexOf(GoneReasons, can) >= 0)
            {
                int di = Discard.LastIndexOf(cd.Id); if (di >= 0) Discard.RemoveAt(di);
                Hand.Add(new HandCard { Uid = a.Card.Uid, Id = cd.Id, Beat = Beats });
                if (cd.MomCost > 0) Momentum += cd.MomCost;
                if (a.Buffs.Count > 0 && Buffs.Count == 0) Buffs = a.Buffs;
                CardStats.Returned++;
                Emit(RaceEventKind.CardReturned, car, null, cd.Name, cd.Id);
                return;
            }
            var tier = ForceNextTier ?? o.Sample(CardRng);
            bool forced = ForceNextTier != null; ForceNextTier = null;
            if (!forced && (tier == Tier.Fail || tier == Tier.CritFail) && !LuckyUsed && Loadout.PartSum(p => p.Reroll ? 1 : 0) > 0)
            {
                LuckyUsed = true; tier = o.Sample(CardRng);
                Emit(RaceEventKind.CardRerolled, car, null, "Lucky Lug Nut", cd.Id);
            }
            CardStats.Plays++; PlayerPlays++;
            if (!CardStats.ByCard.TryGetValue(cd.Id, out var hist)) CardStats.ByCard[cd.Id] = hist = new int[6];
            hist[5]++; hist[(int)tier]++;
            // Heat: every aggressive move makes the field more willing to block and retaliate
            if (cd.Heats)
            {
                Heat = Math.Min(5, Heat + (tier == Tier.CritFail ? 2 : 1)); HeatAt = T;
                if (Bal.Overdrive && Heat >= 3 && T - overAt > 30) { overAt = T; Emit(RaceEventKind.Overdrive, car, null, "You're overdriving it. Heat's up: they'll block you and come after you."); }
            }
            bool ok = CardRules.Landed(tier);
            double tc = cd.TireCost > 0 ? cd.TireCost * Bal.TireCost * (ok && Bal.Refund != 0 ? 1 - Bal.Refund : 1) : 0;
            if (tc > 0) car.Tires = Math.Max(5, car.Tires - tc);
            if (ok)
            {
                CardStats.Succ++;
                if (tier == Tier.CritSuccess) AddMomentum(1);
                var f = cd.Family;
                if (f == CardFamily.Pass || f == CardFamily.Draft || f == CardFamily.Restart || f == CardFamily.Risk) HoldFor(car, tier == Tier.CritSuccess ? 120 : 75);
            }
            if (tier == Tier.CritFail) CardStats.Crits++;
            double rf = 0;
            if (ok && !(Hazard != null && cd.Id == "evade"))
            {
                rf = Math.Min(RiskRefill(o.Succ, tier), 1 - FocusMeter);
                if (rf > 0.005) { FocusMeter += rf; CardStats.FocusGain += rf; CardStats.RiskRefills++; } else rf = 0;
            }
            c.Card = cd;
            cd.Effect(tier)?.Invoke(c);
            if (Bal.ChainDraw > 0)
            {
                if (ok)
                {
                    Chain = a.Combo > 0 ? a.Combo + 1 : 1;
                    if (a.Combo == 0) ChainRoot = cd.Family;
                    ChainFam = cd.Family; ChainId = cd.Id; ChainAt = T;
                    CardStats.BestChain = Math.Max(CardStats.BestChain, Chain);
                    if (Chain > 1) { CardStats.Combos++; Emit(RaceEventKind.Combo, car, null, $"Combo ×{Chain}", cd.Id); }
                    if (Chain >= Bal.FollowMin && followN < Bal.FollowCap)
                    {
                        followN++;
                        var got = DrawCards(Bal.ChainDraw + (tier == Tier.CritSuccess ? 1 : 0), true);
                        foreach (var x in got) x.Follow = true;
                        CardStats.Follow += got.Count;
                    }
                }
                else { Chain = 0; ChainFam = null; }
            }
            Emit(RaceEventKind.CardResolved, car, a.Target, $"{cd.Name}: {CardRules.TierName(tier)}{(rf > 0 ? $" · Focus +{rf * 100:F0}%" : "")}", cd.Id, (int)tier);
        }

        /// <summary>Focus refill when a risky card lands: the lower the odds you took it at, the more comes back.</summary>
        double RiskRefill(double succ, Tier tier)
        {
            if (!Bal.RiskFocus || Bal.FocusSec <= 0) return 0;
            double v = SimMath.Clamp((0.72 - succ) * 1.2, 0, 0.45);
            return v * (tier == Tier.CritSuccess ? 1.5 : 1);
        }

        // ---------------------------------------------------------------- tossing

        /// <summary>Does the toss do its main thing right now (or fall back to an extra Crew Board charge)?</summary>
        public bool TossLive(CardDef cd)
        {
            if (cd.Toss == TossKind.None || !Bal.TossFx) return false;
            switch (cd.Toss)
            {
                case TossKind.Chain: return ChainFam != null && T - ChainAt <= ComboWin() && LinksTo(cd);
                case TossKind.Cool: return Heat > 0;
                case TossKind.Setup: return Setups.Count > 0;
                case TossKind.Focus: return Bal.FocusSec > 0 && FocusMeter < 1;
                default: return true;
            }
        }

        /// <summary>Toss a card: it goes to the discard pile, charges the Crew Board and does its toss effect. Returns why not, or null.</summary>
        public string TossCard(int uid)
        {
            if (!CardsOn) return "No cards in this race";
            if (Phase == RacePhase.Finished) return "The race is over";
            var h = Hand.Find(x => x.Uid == uid);
            if (h == null) return "Not in your hand";
            if (h.Special) return "Can't toss this one";
            Hand.Remove(h);
            Discard.Add(h.Id);
            NextDraw = Math.Max(T + 1, NextDraw - 10);
            CardStats.Tossed++;
            BoardAdd(1);
            var what = TossEffect(h.Def);
            if (Selected == uid) Selected = null;
            Emit(RaceEventKind.CardTossed, Me, null, what ?? "Crew Board charge", h.Id);
            return null;
        }

        string TossEffect(CardDef cd)
        {
            if (cd.Toss == TossKind.None || !Bal.TossFx) return null;
            CardStats.TossFx++;
            if (!TossLive(cd)) { BoardAdd(1); return "Extra Crew Board charge"; }
            CardStats.TossLive++;
            switch (cd.Toss)
            {
                case TossKind.Chain: ChainFam = cd.Family; ChainId = cd.Id; ChainAt = T; return "Combo kept alive";
                case TossKind.Buff:
                    TossBuffs.RemoveAll(b => b.Family == cd.TossFamily);
                    TossBuffs.Add(new OddsBuff { Family = cd.TossFamily, V = TossBuffV, Source = "Tossed " + cd.Name, Until = T + TossBuffSec });
                    return $"Next {CardRules.FamilyName(cd.TossFamily)} card +{TossBuffV}";
                case TossKind.Charge: BoardAdd(1); return "Extra Crew Board charge";
                case TossKind.Draw: DrawCards(1); return "Drew a card";
                case TossKind.Focus:
                {
                    double add = Math.Min(cd.TossValue, 1 - FocusMeter);
                    FocusMeter += add; CardStats.FocusGain += add;
                    return $"Focus +{add * 100:F0}%";
                }
                case TossKind.Cool: Heat = Math.Max(0, Heat - 1); HeatAt = T; return "Heat -1";
                case TossKind.Setup:
                {
                    int lap = LapOf(Me);
                    SetupFresh = Math.Min(lap, (SetupFresh ?? lap) + 1);
                    return "Setups stay fresh one more lap";
                }
            }
            return null;
        }

        const int TossBuffV = 8;
        const double TossBuffSec = 20;

        // ---------------------------------------------------------------- Focus

        /// <summary>
        /// Slow-mo while a card is selected: returns the race-time factor for this frame and drains Focus by the real seconds
        /// spent. The Evade card's slow-mo is free. The UI calls this every frame and scales the ticks it runs.
        /// </summary>
        public double SlowMo(double realSeconds, bool cardSelected, bool hazardSelected = false)
        {
            if (!cardSelected || Attempt != null || Phase == RacePhase.Finished || Bal.Slowmo >= 1) return 1;
            if (hazardSelected) return Bal.Slowmo;
            if (Bal.FocusSec <= 0) return Bal.Slowmo;
            if (FocusMeter <= 0) return 1;
            FocusMeter = Math.Max(0, FocusMeter - realSeconds / Bal.FocusSec);
            return Bal.Slowmo;
        }

        // ---------------------------------------------------------------- tick hooks (called from Race.Tick)

        /// <summary>Before the clock moves: the free Reel Them In card when you are stranded more than 1.6 s off the car ahead.</summary>
        void CardsPreTick()
        {
            if (!Bal.Reel) return;
            var m = Me;
            var inH = Hand.Find(h => h.Id == "reel_in");
            bool ok = Phase == RacePhase.Green && !m.Dnf && m.Pit == null && Attempt == null;
            var a = AheadOf(m);
            double gap = a != null ? GapSec(a, m) : 0;
            if (inH != null)
            {
                if (Phase != RacePhase.Green || m.Dnf || m.Pit != null || a == null || gap < 1)
                {
                    Hand.Remove(inH); if (Selected == inH.Uid) Selected = null; reelCd = T + 8;
                }
                return;
            }
            if (!ok || a == null || gap < 1.6 || T < reelCd) { reelFar = null; return; }
            reelFar = reelFar ?? T;
            if (T - reelFar.Value < 6) return;
            Hand.Add(new HandCard { Uid = nextUid++, Id = "reel_in", Special = true, Beat = Beats });
            reelFar = null; CardStats.Reels++;
            Emit(RaceEventKind.ReelOffered, m, a);
        }

        /// <summary>After the clock moves: resolve a move whose setup time is up.</summary>
        void CardsClockTick()
        {
            if (Attempt != null && T >= Attempt.End) ResolveAttempt();
        }

        /// <summary>Under yellow with Restart focus a live combo freezes instead of timing out.</summary>
        void CardsFreezeTick(double dt)
        {
            if (ChainFam != null && (Phase == RacePhase.Caution || Phase == RacePhase.Restart) && Tune.Freeze && T - ChainAt <= ComboWin()) ChainAt += dt;
        }

        double HeatCoolK
        {
            get
            {
                if (!CardsOn) return 1;
                double parts = Loadout.PartSum(p => p.HeatCool);
                return Tune.HeatCool * (parts > 0 ? parts : 1);
            }
        }

        double MySetupWear => CardsOn ? SetupWear() : 1;

        /// <summary>After the order is sorted: who just passed you (Crossover).</summary>
        void CardsAfterMove()
        {
            foreach (var e in Events)
                if (e.Kind == RaceEventKind.Pass && e.Other == Me.Id) { LostAt = T; LostTo = Cars.Find(x => x.Id == e.Car); }
            int mp = Position(Me);
            if (mp > lastPos && Phase == RacePhase.Green)
            {
                var na = AheadOf(Me);
                if (na != null && T - LostAt > 0.5) { LostAt = T; LostTo = na; }
            }
            lastPos = mp;
        }

        /// <summary>After the AI moves: timed draws and draw beats.</summary>
        void CardsDrawTick()
        {
            if (Phase != RacePhase.Finished && Phase != RacePhase.Start && T >= NextDraw)
            {
                // only after a caution hands its restart cards back (goGreen pulls NextDraw in); the slice keeps drawing every 30 s from then on
                if (Hand.Count < HandMax) { DrawCards(1); NextDraw += Bal.DrawEvery; } else NextDraw = T + 1;
            }
            if (Phase == RacePhase.Green)
            {
                double u = Shape.UOf(Me.Dist);
                var pu = lastBeatU; lastBeatU = u;
                if (pu != null && beatU != null && T - lastBeatAt > 4 && Me.Pit == null)
                {
                    bool hit = false;
                    foreach (var p in beatU) if (pu <= u ? pu < p && p <= u : p > pu || p <= u) { hit = true; break; }
                    if (hit) { lastBeatAt = T; DrawBeat(Track.BeatDraw); }
                }
            }
            else lastBeatU = null;
        }

        /// <summary>At the green: unplayed caution and restart cards go back into the deck, and Choose the Lane / Stay Out pay out.</summary>
        void CardsGoGreen()
        {
            var back = Hand.FindAll(h => h.Def.Situational);
            if (back.Count > 0)
            {
                foreach (var h in back) { Hand.Remove(h); Pile.Insert((int)Math.Floor(CardRng.NextDouble() * (Pile.Count + 1)), h.Id); }
                NextDraw = Math.Min(NextDraw, T + 3);
            }
            if (StartGain > 0) StartOvertake(Me, StartGain); else if (StartGain < 0) Slow(Me, 0.6);
            StartGain = 0; RestartBuff = 0;
        }

        /// <summary>A caution cancels a move in progress: the card comes back to your hand with its boosts.</summary>
        void CardsCaution()
        {
            if (Attempt == null) return;
            var a = Attempt; Attempt = null;
            if (!a.Card.Special) { int di = Discard.LastIndexOf(a.Def.Id); if (di >= 0) Discard.RemoveAt(di); }
            Hand.Add(new HandCard { Uid = nextUid++, Id = a.Def.Id, Beat = Beats, Special = a.Card.Special });
            Buffs = a.Buffs;
        }

        /// <summary>The Evade card arrives with the hazard and leaves with it.</summary>
        void CardsHazard(bool offered)
        {
            if (offered)
            {
                HazardCard = new HandCard { Uid = nextUid++, Id = "evade", Special = true, Beat = Beats };
                Hand.Add(HazardCard);
                if (Selected == null) Selected = HazardCard.Uid;
            }
            else if (HazardCard != null)
            {
                Hand.Remove(HazardCard);
                if (Selected == HazardCard.Uid) Selected = null;
                HazardCard = null;
            }
        }

        void CardsEndTick(double dt)
        {
            if (Track.Kart) KartTick(dt);
            PitGameTick();
            SetupStaleTick();
        }
    }
}
