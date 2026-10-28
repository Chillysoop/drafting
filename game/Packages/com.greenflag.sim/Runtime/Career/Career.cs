using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    /// <summary>Your plays and successes with one card over the career (the slice's G.hist): the deck builder sorts and scores by it.</summary>
    public sealed class CardHistory { public int Plays, Succ; }

    /// <summary>One line of the race's finishing order, with names, for the reward screen.</summary>
    public sealed class FinishLine
    {
        public int Pos, Id;
        public string Name, Team;
        public bool IsMe, Dnf;
    }

    /// <summary>What a race paid and how it went (the slice's res): the top of the reward screen.</summary>
    public sealed class RaceReport
    {
        public int Season, RaceNo;
        public string TrackId, TrackName;
        public int Start, Finish, Expected;
        public bool Dnf;
        public RewardTier Tier;
        public double Mul;
        public int Purse, Bonus, Total;
        public int Plays, Succ, Crits, BestChain, Calls, CallsWon;
        /// <summary>Top 10, plus you if you finished outside it.</summary>
        public List<FinishLine> Order = new List<FinishLine>();
        /// <summary>"Your calls": every card you played, at the odds you played it, and what moved them.</summary>
        public List<PlayLogEntry> Log = new List<PlayLogEntry>();
        /// <summary>Stakes: null, "ironman", "survive" (Permadeath token used) or "dead" (Permadeath: the run is over).</summary>
        public string Penalty;
        public string LostPart;
        public int BeatBy => Math.Max(0, Expected - Finish);

        // the season (step 37)
        /// <summary>Expected finish the objectives were scaled by, and your best running position during the race.</summary>
        public int ObjE, BestPos = 99;
        public bool BeatMate, BeatRival;
        public string Rival;
        public List<ObjectiveResult> Objectives = new List<ObjectiveResult>();
        public int ObjMet;
        /// <summary>The tier before meeting every objective moved it up one, or null.</summary>
        public RewardTier? TierFrom;
        public string Sponsor;
        public int Retainer, Salary;
        /// <summary>Points this race, your season total after it, and your points position before and after.</summary>
        public int Pts, PtsTotal, Rank, RankBefore;

        // the driver (step 38)
        /// <summary>Your car's damage at the flag, Momentum the driver brought to the green flag, Hype earned (and why),
        /// XP for racing and the levels it reached.</summary>
        public int Dmg, DrvMom, Hype, Xp, LvlUp;
        public List<string> HypeWhy = new List<string>();

        // the crew (step 39)
        /// <summary>Crew wages taken from the payout, XP each seat earned, and the seats that went up a level.</summary>
        public int Wages;
        public Dictionary<CrewRole, int> CrewXp = new Dictionary<CrewRole, int>();
        public List<CrewRole> CrewUp = new List<CrewRole>();
    }

    /// <summary>The reward screen's choices: a card for the collection (or $1,000), and a part after a strong run. Both reroll for money.</summary>
    public sealed class RewardOffer
    {
        public RaceReport Res;
        public List<string> Cards = new List<string>();
        public int CardRerolls;
        /// <summary>Null until you choose: a card id, or "skip".</summary>
        public string Picked;
        /// <summary>The picked card also went into the race deck (it had room).</summary>
        public bool InDeck;
        /// <summary>Null when the run earned no part.</summary>
        public List<string> Parts;
        public int PartRerolls;
        public string PartPicked;
        public bool Done => Picked != null && (Parts == null || PartPicked != null);
    }

    /// <summary>
    /// A career between races (roadmap step 34): money, the card collection and race deck, parts owned and fitted, the car tune,
    /// the shop and the reward screen, as in slice v47. It makes the next race's RaceSetup and takes the finished race back to
    /// pay out and roll rewards, so race, rewards, shop and the next race loop. Every roll comes from the career's own
    /// generator, so a career replays the same from its seed.
    /// The season around it (field, points, sponsors, objectives, qualifying, the season wrap) is in Career.Season.cs (step 37).
    /// The driver sheet and off-track events are in Career.Driver.cs (step 38), Crew HQ in Crew.cs (step 39).
    /// </summary>
    public sealed partial class Career
    {
        public string Name = "Rookie", Persona = "drafter";
        public Series Series = Series.Stock;
        public DifficultyLevel Diff = DifficultyLevel.Normal;
        public InfoLevel Info = InfoLevel.Full;
        public Stakes Stakes = Stakes.Standard;
        /// <summary>Permadeath: Survive tokens left (one per run).</summary>
        public int Survive;
        public int Money = CareerRules.StartMoney;
        public int Season = 1, RaceIdx;
        public bool Over;
        /// <summary>Car stats, crew levels, crew perks, pit deck, Grit and Wits. Deck, parts and tune come from the career.</summary>
        public PlayerLoadout Base;
        public List<string> Collection = new List<string>();
        public List<string> Deck = new List<string>();
        public List<string> PartsOwned = new List<string>();
        public Dictionary<PartSlot, string> Equipped = new Dictionary<PartSlot, string>();
        public Tune Tune = new Tune();
        public Dictionary<string, CardHistory> Hist = new Dictionary<string, CardHistory>();
        /// <summary>The deck you raced at each track last time.</summary>
        public Dictionary<string, List<string>> TrackDecks = new Dictionary<string, List<string>>();
        public List<RaceReport> Results = new List<RaceReport>();
        /// <summary>The parts dealer's part (null = sold out) and rerolls this visit.</summary>
        public string ShopPart;
        public int ShopRerolls;
        bool shopRolled;
        /// <summary>The reward screen, while it is up.</summary>
        public RewardOffer Reward;
        /// <summary>What the crew changed when it fixed your deck for the next track.</summary>
        public string DeckNote;
        int deckFor = -1;
        public uint Seed;
        public SimRng Rng;

        public Difficulty Difficulty => Difficulty.For(Diff);
        public CarStats Car => Base.Car;
        public bool Kart => Series == Series.Kart;
        public IReadOnlyList<string> Calendar => CareerRules.Calendar(Series);
        public int SeasonLength => Calendar.Count;
        public TrackDef NextTrack => Tracks.ById(Calendar[RaceIdx % Calendar.Count]);
        public double PayMul => CareerRules.PayMul(Difficulty, Info, Stakes, Series);
        public int RerollCost(int rerolls) => CareerRules.RerollCost(Diff, rerolls);

        /// <summary>A new career for a persona: its deck (which is also the whole collection), its part fitted, its stat bonus,
        /// tune and driver, and $6,000. A kart persona starts in karts. The driver's background defaults to the persona's.</summary>
        public static Career New(string persona, DifficultyLevel diff = DifficultyLevel.Normal, Stakes stakes = Stakes.Standard, uint seed = 1, string name = "Rookie", InfoLevel info = InfoLevel.Full, string background = null)
        {
            var p = Personas.Get(persona);
            var l = PlayerLoadout.ForPersona(persona);
            var c = new Career
            {
                Name = name, Persona = persona, Series = p.Kart ? Series.Kart : Series.Stock, Diff = diff, Info = info, Stakes = stakes,
                Survive = stakes == Stakes.Permadeath ? 1 : 0, Seed = seed, Rng = new SimRng(seed ^ 0xC0FFEEu), Base = l, Tune = l.Tune.Clone(),
            };
            c.Deck.AddRange(l.Deck);
            c.Collection.AddRange(l.Deck);
            c.AddPart(p.Part);
            l.Deck = new List<string>(); l.Parts = new List<string>();
            c.Driver = Driver.New(background ?? DriverRules.BgFor(persona));
            c.SyncDriverStats();
            c.NewStaff();
            c.Contract.Series = c.Series;
            c.Ladder.Add(c.Series);
            c.BuildField();
            return c;
        }

        // ------------------------------------------------------------------ before the race

        /// <summary>Opens the garage for the next race: stocks the parts dealer and, the first time for each race, fixes a deck
        /// that breaks the new track's rules (DeckNote says what changed).</summary>
        public void EnterGarage()
        {
            if (Offers == null && Sponsor == null && SponsorOffers == null)
                SponsorOffers = SeasonRules.RollSponsorOffers(Rng, Past.Count > 0 ? Past[Past.Count - 1].Rank : 99, null, CareerRules.Econ(Series));
            if (Sponsor != null && Objectives == null) RollObjectives();
            // the game closed in the middle of a qualifying run: that counts as giving up (an average lap)
            if (QualRunning && !QualiDone) FinishQuali(null);
            QualRunning = false;
            if (!shopRolled) { shopRolled = true; ShopPart = RollParts(1).FirstOrDefault(); }
            if (deckFor != RaceIdx + Season * 1000)
            {
                deckFor = RaceIdx + Season * 1000;
                var t = NextTrack;
                if (!DeckOk(t)) { var was = new List<string>(Deck); Deck = DeckRules.Fix(this, t); DeckNote = DeckRules.Diff(was, Deck, t); }
                else DeckNote = null;
            }
        }

        public bool DeckOk(TrackDef t = null) => DeckRules.Ok(this, t ?? NextTrack);
        /// <summary>Why "Go racing" is off, or null.</summary>
        public string CantRace => Over ? "The run is over" : Reward != null ? "Finish the reward screen first" : Offers != null ? "Sign a contract for next season first"
            : Event != null && !Event.Done ? $"Finish tonight's {Event.Def.Name.ToLowerInvariant()} first, or skip it"
            : Poach != null && !Poach.Done ? $"Answer {Poach.TeamName}'s offer for your {CrewRules.Name(Poach.Role).ToLowerInvariant()} first (Crew HQ)"
            : Sponsor == null ? "Sign a sponsor for the season first" : !DeckOk() ? $"Your race deck breaks a rule for {DeckRules.ShortName(NextTrack)}" : null;

        /// <summary>The loadout the next race uses: your car and crew with the race deck, the fitted parts and the tune.</summary>
        public PlayerLoadout Loadout()
        {
            var l = Base.Clone();
            l.Deck = new List<string>(Deck);
            l.Parts = Equipped.Values.Where(x => x != null).ToList();
            l.Tune = Tune.Clone();
            l.Grit = Driver.Score(DAttr.Grit);
            l.Wits = Driver.Score(DAttr.Wits);
            l.DriverMom = Driver.NextMom;
            return l;
        }

        /// <summary>This race's seed: the career's seed and the race's place in the career.</summary>
        public uint RaceSeed => Seed * 2654435761u + (uint)(Season * 100 + RaceIdx) * 40503u + 1;

        /// <summary>The next race, ready to run, with you playing your own pit stops: the season's field, on the grid qualifying
        /// set (the old roll-off when there was none). Remembers this deck for the track.</summary>
        public RaceSetup NextRace(uint? seed = null)
        {
            var t = NextTrack;
            var l = Loadout();
            var s = new RaceSetup { Track = t, Seed = seed ?? RaceSeed, Loadout = l, Field = RaceField(l.Car) };
            if (QualiDone) { s.Grid = new List<int>(Qual.Grid); s.QualiCard = Qual.Card; }
            s.Difficulty = Difficulty;
            s.PitPolicy = PitPolicy.Player;
            TrackDecks[t.Id] = new List<string>(Deck);
            return s;
        }

        // ------------------------------------------------------------------ the shop

        public int StatLevel(string stat) => CareerRules.Stat(Car, stat);
        public int StatCost(string stat) => CareerRules.ShopStatOf(stat).Cost(StatLevel(stat));
        public string CantBuyStat(string stat) => StatLevel(stat) >= CareerRules.StatMax ? "Maxed out" : Money < StatCost(stat) ? "Not enough money" : null;

        public bool BuyStat(string stat)
        {
            if (CantBuyStat(stat) != null) return false;
            Money -= StatCost(stat);
            Base.AddStat(stat, 1);
            return true;
        }

        public bool BuyShopPart()
        {
            if (ShopPart == null || Money < CareerRules.PartPrice) return false;
            Money -= CareerRules.PartPrice;
            AddPart(ShopPart);
            ShopPart = null;
            return true;
        }

        public int ShopRerollCost => RerollCost(ShopRerolls);

        public bool RerollShopPart()
        {
            if (Money < ShopRerollCost) return false;
            Money -= ShopRerollCost;
            ShopRerolls++;
            ShopPart = RollParts(1).FirstOrDefault();
            return true;
        }

        /// <summary>Adds a part to your inventory and fits it if its slot is empty.</summary>
        public void AddPart(string id)
        {
            if (!PartsOwned.Contains(id)) PartsOwned.Add(id);
            var s = Parts.Get(id).Slot;
            if (!Equipped.TryGetValue(s, out var cur) || cur == null) Equipped[s] = id;
        }

        public void Equip(string id) { if (PartsOwned.Contains(id)) Equipped[Parts.Get(id).Slot] = id; }
        public void Unequip(PartSlot s) => Equipped[s] = null;
        public string EquippedIn(PartSlot s) => Equipped.TryGetValue(s, out var id) ? id : null;
        public IEnumerable<string> Spares => PartsOwned.Where(id => !Equipped.ContainsValue(id));

        // ------------------------------------------------------------------ the race deck and tune

        public string AddBlock(string id, TrackDef t = null) => DeckRules.AddBlock(this, id, t ?? NextTrack);
        public bool AddToDeck(string id) { if (AddBlock(id) != null) return false; Deck.Add(id); return true; }
        public bool RemoveFromDeck(string id) { int i = Deck.LastIndexOf(id); if (i < 0) return false; Deck.RemoveAt(i); return true; }
        public void AutoDeck() => Deck = DeckRules.Auto(this, NextTrack);
        public void FixDeck() => Deck = DeckRules.Fix(this, NextTrack);
        public void FillDeck() => Deck = DeckRules.Fix(this, NextTrack, Deck, null, DeckRules.Size(Series, Tune).max);
        public bool LastDeck() { if (!TrackDecks.TryGetValue(NextTrack.Id, out var d)) return false; Deck = DeckRules.Fix(this, NextTrack, d); return true; }
        public void ClearDeck() => Deck.Clear();

        /// <summary>Sets a dial (-2 to 2) or the combo focus. Restart focus isn't offered in karts.</summary>
        public void SetTune(int? hand = null, int? aero = null, ComboFocus? focus = null)
        {
            if (hand.HasValue) Tune.Hand = Math.Max(-2, Math.Min(2, hand.Value));
            if (aero.HasValue) Tune.Aero = Math.Max(-2, Math.Min(2, aero.Value));
            if (focus.HasValue && !(Kart && focus.Value == ComboFocus.Restart)) Tune.Focus = focus.Value;
        }

        public void ResetTune() => Tune = Personas.Get(Persona).Tune.Clone();
        public bool PersonaTune { get { var p = Personas.Get(Persona).Tune; return p.Hand == Tune.Hand && p.Aero == Tune.Aero && p.Focus == Tune.Focus; } }

        /// <summary>How well the tune suits a track (the garage's chip): speed from Aero plus what the combo focus likes here.</summary>
        public static (int verdict, string text) TuneFit(TrackDef t, Tune u)
        {
            double fit = (u.Pace(t) - 1) * 100;
            if (u.Focus == ComboFocus.Draft) fit += 0.4 * t.DraftBias;
            if (u.Focus == ComboFocus.Bumper) fit += 0.3 * (t.Contact - 1.1) * 2;
            if (u.Focus == ComboFocus.LongRun) fit += 0.4 * (t.Wear - 0.8);
            return fit > 0.15 ? (1, "Good fit here") : fit < -0.15 ? (-1, "Poor fit here") : (0, "Fine here");
        }

        // ------------------------------------------------------------------ after the race

        /// <summary>Pays out a finished race and puts up the reward screen (the slice's endRace): purse, bonus, the season's
        /// objectives, sponsor money, salary and points (step 37), and the driver's XP and Hype (step 38).</summary>
        public RaceReport FinishRace(Race race)
        {
            if (!race.Done) throw new InvalidOperationException("The race isn't over");
            var rr = race.Result();
            var res = new RaceReport
            {
                Season = Season, RaceNo = RaceIdx + 1, TrackId = race.Track.Id, TrackName = race.Track.Name,
                Start = rr.MyStart, Finish = rr.MyFinish, Expected = rr.MyExpected, Dnf = rr.MyDnf,
                Plays = race.CardStats.Plays, Succ = race.CardStats.Succ, Crits = race.CardStats.Crits, BestChain = race.CardStats.BestChain,
                Calls = race.CardStats.Calls, CallsWon = race.CardStats.CallsWon,
            };
            res.Tier = CareerRules.TierFor(res.Finish, res.Expected, res.Dnf);
            res.Mul = PayMul;
            res.Purse = CareerRules.Purse(res.Finish, res.Mul);
            res.Bonus = CareerRules.Bonus(res.Finish, res.Expected, res.Mul);
            res.Total = res.Purse + res.Bonus;
            res.ObjE = rr.MyExpected;
            res.BestPos = race.CardStats.BestPos;
            res.Log.AddRange(race.PlayLog);
            var teams = Teams.For(race.Track.Series);
            var byId = race.Cars.ToDictionary(c => c.Id);
            foreach (var o in rr.Order)
            {
                if (o.Finish > 10 && !o.IsMe) continue;
                var e = byId[o.Id].Ref;
                res.Order.Add(new FinishLine { Pos = o.Finish, Id = o.Id, IsMe = o.IsMe, Dnf = o.Dnf, Name = o.IsMe ? Name : e?.Name ?? "Car " + o.Id, Team = e != null && e.Team >= 0 && e.Team < teams.Length ? teams[e.Team].Name : "" });
            }
            foreach (var kv in race.CardStats.ByCard)
            {
                if (!Hist.TryGetValue(kv.Key, out var h)) Hist[kv.Key] = h = new CardHistory();
                h.Plays += kv.Value[5];
                h.Succ += kv.Value[(int)Tier.Success] + kv.Value[(int)Tier.CritSuccess];
            }
            ScoreSeason(race, res);
            DriverAfterRace(race, res);
            CrewAfterRace(race, res);
            Money += res.Total;
            Results.Add(res);
            ApplyStakes(res);
            Reward = new RewardOffer { Res = res, Cards = RollCards(res.Tier), Parts = CareerRules.PartPick(res.Tier) ? RollParts(2) : null };
            if (Reward.Parts != null && Reward.Parts.Count == 0) Reward.Parts = null;
            return res;
        }

        /// <summary>What a DNF costs on Ironman and Permadeath (the slice's stakesPenalty).</summary>
        public void ApplyStakes(RaceReport res)
        {
            if (!res.Dnf || Stakes == Stakes.Standard) return;
            if (Stakes == Stakes.Permadeath && Survive > 0) { Survive--; res.Penalty = "survive"; }
            else if (Stakes == Stakes.Permadeath) { res.Penalty = "dead"; Over = true; return; }
            Money = Math.Max(0, Money - CareerRules.DnfFine);
            var spare = Spares.ToList();
            if (spare.Count > 0) { var lost = spare[Rng.Range(0, spare.Count)]; PartsOwned.Remove(lost); res.LostPart = lost; }
            res.Penalty = res.Penalty ?? "ironman";
        }

        /// <summary>A Standard-stakes career can rerun a race from the reward screen: take a snapshot before the race.</summary>
        public bool CanRetry => Stakes == Stakes.Standard;

        public bool PickCard(string id)
        {
            var R = Reward;
            if (R == null || R.Picked != null || !R.Cards.Contains(id)) return false;
            Collection.Add(id);
            R.InDeck = AddBlock(id) == null;
            if (R.InDeck) Deck.Add(id);
            R.Picked = id;
            return true;
        }

        public bool SkipCard()
        {
            var R = Reward;
            if (R == null || R.Picked != null) return false;
            Money += CareerRules.SkipCash;
            R.Picked = "skip";
            return true;
        }

        public bool RerollCards()
        {
            var R = Reward;
            if (R == null || R.Picked != null || Money < RerollCost(R.CardRerolls)) return false;
            Money -= RerollCost(R.CardRerolls);
            R.CardRerolls++;
            R.Cards = RollCards(R.Res.Tier);
            return true;
        }

        public bool PickPart(string id)
        {
            var R = Reward;
            if (R == null || R.Parts == null || R.PartPicked != null || !R.Parts.Contains(id)) return false;
            AddPart(id);
            R.PartPicked = id;
            return true;
        }

        public bool RerollParts()
        {
            var R = Reward;
            if (R == null || R.Parts == null || R.PartPicked != null || Money < RerollCost(R.PartRerolls)) return false;
            Money -= RerollCost(R.PartRerolls);
            R.PartRerolls++;
            var p = RollParts(2);
            if (p.Count > 0) R.Parts = p;
            return true;
        }

        /// <summary>Leaves the reward screen for the next race's garage (a fresh parts dealer). An unpicked card or part is
        /// forfeited, as in the slice. After the last race of the calendar the season wraps (WrapSeason): the season-end
        /// screen and contract offers come before the next garage.</summary>
        public void Continue()
        {
            if (Reward == null || Over) return;
            Reward = null;
            Qual = null;
            RaceIdx++;
            if (Poach != null && Poach.Done) Poach = null;
            if (RaceIdx >= SeasonLength) WrapSeason();
            else { RollEvent(); RollPoach(); }
            shopRolled = false; ShopPart = null; ShopRerolls = 0;
            EnterGarage();
        }

        // ------------------------------------------------------------------ rolls

        /// <summary>Card offers: 2 after a rough day, 3 otherwise. Persona cards are 1.8x as likely, victory doubles anything
        /// above common, a rough day makes rares rare, and a strong run or victory always shows one uncommon or rare.</summary>
        public List<string> RollCards(RewardTier tier)
        {
            var add = Personas.Get(Persona).Add;
            var pool = CardBook.All.Where(cd => !cd.Special && cd.Kart == Kart).ToList();
            var w = pool.Select(cd => (cd.Rarity == Rarity.Common ? 6 : cd.Rarity == Rarity.Uncommon ? 3 : 1.2)
                * (Array.IndexOf(add, cd.Id) >= 0 ? 1.8 : 1) * (tier == RewardTier.Victory && cd.Rarity != Rarity.Common ? 2 : 1)
                * (tier == RewardTier.Rough && cd.Rarity == Rarity.Rare ? 0.2 : 1)).ToList();
            double tot = w.Sum();
            int n = tier == RewardTier.Rough ? 2 : 3;
            var outp = new List<string>();
            for (int guard = 0; outp.Count < n && guard < 100; guard++)
            {
                double x = Rng.NextDouble() * tot; int i = 0;
                while (i < w.Count - 1 && (x -= w[i]) > 0) i++;
                if (!outp.Contains(pool[i].Id)) outp.Add(pool[i].Id);
            }
            if (tier >= RewardTier.Strong && !outp.Any(id => CardBook.Get(id).Rarity != Rarity.Common))
            {
                var rs = pool.Where(cd => cd.Rarity != Rarity.Common && !outp.Contains(cd.Id)).ToList();
                if (rs.Count > 0) outp[0] = rs[Rng.Range(0, rs.Count)].Id;
            }
            return outp;
        }

        /// <summary>Up to n parts you don't own, for this series (kart parts in karts; pit and restart parts outside karts).</summary>
        public List<string> RollParts(int n)
        {
            var pool = Parts.All.Where(p => !PartsOwned.Contains(p.Id) && !(Kart ? p.Stock : p.Kart)).Select(p => p.Id).ToList();
            for (int i = pool.Count - 1; i > 0; i--) { int j = Rng.Range(0, i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            return pool.Take(n).ToList();
        }

        // ------------------------------------------------------------------ copies

        /// <summary>A deep copy (the retry snapshot, and tests).</summary>
        public Career Clone()
        {
            var c = (Career)MemberwiseClone();
            c.Base = Base.Clone();
            c.Collection = new List<string>(Collection); c.Deck = new List<string>(Deck); c.PartsOwned = new List<string>(PartsOwned);
            c.Tune = Tune.Clone();
            c.Hist = Hist.ToDictionary(kv => kv.Key, kv => new CardHistory { Plays = kv.Value.Plays, Succ = kv.Value.Succ });
            c.TrackDecks = TrackDecks.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value));
            c.Results = new List<RaceReport>(Results);
            c.Rng = new SimRng(Rng.State);
            c.Reward = null;
            c.Equipped = new Dictionary<PartSlot, string>(Equipped);
            c.Field = Field.Select(e => e.Clone()).ToList();
            c.Relink();
            c.Pts = new Dictionary<int, int>(Pts);
            c.Contract = Contract.Clone();
            c.Sponsor = Sponsor?.Clone();
            c.SponsorOffers = SponsorOffers?.Select(o => o.Clone()).ToList();
            c.Objectives = Objectives?.ToList();
            c.Past = new List<PastSeason>(Past);
            c.Ladder = new List<Series>(Ladder);
            c.Offers = Offers?.Select(o => o.Clone()).ToList();
            c.Driver = Driver.Clone();
            c.Event = Event == null ? null : EventFrom(EventJson(Event));
            c.EventsHeld = new List<string>(EventsHeld);
            c.Staff = Staff.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
            c.Hire = Hire.Select(h => h.Clone()).ToList();
            c.Poach = Poach?.Clone();
            return c;
        }

        /// <summary>Your entrant in the field is your car: same stats object and name.</summary>
        void Relink()
        {
            var me = Field.FirstOrDefault(e => e.IsMe);
            if (me != null) { me.Stats = Base.Car; me.Name = Name; }
        }
    }
}
