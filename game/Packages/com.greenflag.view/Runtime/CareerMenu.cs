using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    /// <summary>The career's screens. Qualifying (step 37) is three: pick a card, the timed run, the grid. Season is the
    /// season-end review with its contract offers. Event is tonight's off-track event and Driver the driver sheet (step 38).
    /// Crew is Crew HQ (step 39).</summary>
    public enum CareerScreen { Title, Garage, DeckBuilder, Reward, Over, Quali, QualiRun, QualiDone, Season, Event, Driver, Crew }

    /// <summary>What a menu line is: plain text, a label and value row, a button, a small toggle chip, a card or part tile.</summary>
    public enum MenuKind { Text, Row, Button, Chip, Card, Part }

    /// <summary>One line or control on a menu page. Anything with an Id can be pressed (CareerMenu.Press).</summary>
    public sealed class MenuItem
    {
        public MenuKind Kind;
        public string Id, Label, Value, Note;
        public bool Enabled = true, On, Primary;
        /// <summary>A second button on the same line (a card's minus in the deck builder).</summary>
        public string AltId, AltLabel;
        public bool AltEnabled = true;
        /// <summary>Text or accent colour (a card's family, a tier, a result), or null for the default ink.</summary>
        public Rgb? Color;
        public bool Pressable => Id != null && Enabled;
    }

    public sealed class MenuSection
    {
        public string Title, Note;
        /// <summary>0 = the main column, 1 = the side column (one column on narrow screens).</summary>
        public int Column;
        public Rgb? Border;
        public readonly List<MenuItem> Items = new List<MenuItem>();
        public MenuSection(string title, int column = 0, string note = null, Rgb? border = null) { Title = title; Column = column; Note = note; Border = border; }
        public MenuItem Add(MenuItem i) { Items.Add(i); return i; }
        public void Text(string t, Rgb? c = null) => Items.Add(new MenuItem { Kind = MenuKind.Text, Label = t, Color = c });
        public void Row(string l, string v, Rgb? c = null) => Items.Add(new MenuItem { Kind = MenuKind.Row, Label = l, Value = v, Color = c });
        public MenuItem Button(string id, string label, bool enabled = true, string note = null, bool primary = false) => Add(new MenuItem { Kind = MenuKind.Button, Id = id, Label = label, Enabled = enabled, Note = note, Primary = primary });
        public MenuItem Chip(string id, string label, bool on, bool enabled = true, string note = null) => Add(new MenuItem { Kind = MenuKind.Chip, Id = id, Label = label, On = on, Enabled = enabled, Note = note });
    }

    /// <summary>A whole screen: a header, sections, and the action bar along the bottom.</summary>
    public sealed class MenuPage
    {
        public CareerScreen Screen;
        public string Eyebrow, Title, Sub, Money, Notice;
        /// <summary>Where the controller focus starts on this page (null = the first control).</summary>
        public string Default;
        public Rgb? TitleColor;
        public readonly List<MenuSection> Sections = new List<MenuSection>();
        public readonly List<MenuItem> Actions = new List<MenuItem>();
        public MenuSection Section(string title, int column = 0, string note = null, Rgb? border = null) { var s = new MenuSection(title, column, note, border); Sections.Add(s); return s; }

        /// <summary>Every pressable control in reading order: the action bar first, then the sections (the controller focus walks this).</summary>
        public IEnumerable<MenuItem> Pressables()
        {
            foreach (var a in Actions) if (a.Pressable) yield return a;
            foreach (var s in Sections) foreach (var i in s.Items) if (i.Pressable) yield return i;
        }

        public MenuItem Find(string id)
        {
            foreach (var a in Actions) if (a.Id == id || a.AltId == id) return a;
            foreach (var s in Sections) foreach (var i in s.Items) if (i.Id == id || i.AltId == id) return i;
            return null;
        }

        /// <summary>Is this id on the page and pressable (an alt button counts when its own flag is on)?</summary>
        public bool CanPress(string id)
        {
            var i = Find(id);
            if (i == null) return false;
            return i.Id == id ? i.Enabled : i.AltEnabled;
        }
    }

    /// <summary>
    /// The career screens between races (roadmap step 34) as engine-free pages: the new-career title, the garage (next race,
    /// car tune, shop upgrades, the parts dealer, equipment and the race deck), the race deck builder and the reward screen
    /// (payout, top 10, "Your calls", the card pick and the strong-run part pick, rerolls, retry). The Unity CareerFlow draws
    /// Page() and calls Press(id) for clicks, Enter or the A button; MenuBot drives the same buttons in tests and the harness.
    /// When you press Go racing, RaceWanted holds the race to run; hand the finished race back with RaceFinished.
    /// </summary>
    public sealed partial class CareerMenu
    {
        public Career Career { get; private set; }
        public CareerScreen Screen { get; private set; } = CareerScreen.Title;
        /// <summary>Set by Go racing (and Retry): the race the engine should run now. The flow clears it when it starts the race.</summary>
        public RaceSetup RaceWanted;
        /// <summary>A short line for the top of the page (what the last press did, or why it didn't).</summary>
        public string Toast;
        /// <summary>The career as it was just before this race, for Retry this race (Standard stakes).</summary>
        Career snap;
        uint snapSeed;

        // the title screen's picks
        public string PickPersona = "drafter";
        /// <summary>The driver background (null = the persona's own).</summary>
        public string PickBg;
        public DifficultyLevel PickDiff = DifficultyLevel.Normal;
        public Stakes PickStakes = Stakes.Standard;
        public uint NewSeed = 1;

        // the deck builder's sort and family tab
        public string DeckSort = "fam";
        public CardFamily? DeckTab;

        // saves (step 35): null = no saving (tests and the harness that don't care)
        /// <summary>The three save slots, or null to play without saving.</summary>
        public SaveSlots Saves;
        /// <summary>The slot this career saves to (0 = none).</summary>
        public int Slot;
        /// <summary>The slot a new career goes into.</summary>
        public int PickSlot = 1;
        /// <summary>A change since the last save that no checkpoint has written yet (garage buys, tune, equipment).</summary>
        public bool Dirty;
        /// <summary>A press that has to be pressed twice (delete a slot, replace a slot): the id that is waiting.</summary>
        string armed;
        List<SlotInfo> slots;

        public CareerMenu() { }
        public CareerMenu(Career c) { Career = c; Career.EnterGarage(); Screen = c.Offers != null ? CareerScreen.Season : CareerScreen.Garage; }

        /// <summary>The qualifying run being played (Screen QualiRun). The engine calls QualiTick every frame.</summary>
        public QualiRun Quali;
        /// <summary>The card picked for qualifying (null = none).</summary>
        public string QualiCard;
        public CareerMenu(SaveSlots saves) { Saves = saves; RefreshSlots(); }

        /// <summary>Re-reads the slots (the title screen shows them; reading files every frame would be wasteful).</summary>
        public IReadOnlyList<SlotInfo> RefreshSlots()
        {
            if (Saves == null) return slots = new List<SlotInfo>();
            slots = Saves.All();
            var empty = slots.Find(i => !i.Used);
            if (Slot == 0) PickSlot = empty != null ? empty.Slot : PickSlot;
            return slots;
        }

        public IReadOnlyList<SlotInfo> Slots => slots ?? RefreshSlots();

        /// <summary>What a save of the career right now holds: the screen it is on and, on a Standard reward screen, the
        /// pre-race career for Retry this race.</summary>
        public SaveGame Snapshot() => new SaveGame
        {
            Career = Career,
            Screen = Screen == CareerScreen.Reward ? SaveGame.Reward : Screen == CareerScreen.Event ? SaveGame.Event : Screen == CareerScreen.Over || (Career != null && Career.Over && Career.Reward == null) ? SaveGame.Over : SaveGame.Garage,
            Retry = Screen == CareerScreen.Reward && Career.CanRetry ? snap : null,
            RetrySeed = snapSeed,
        };

        /// <summary>Writes the career to its slot now (a checkpoint). Returns false when there is nothing to save or the write
        /// failed (the toast says so; the game goes on).</summary>
        public bool SaveNow()
        {
            if (Saves == null || Slot == 0 || Career == null || Screen == CareerScreen.Title) return false;
            if (!Saves.Save(Slot, Snapshot())) { Toast = "Couldn't save: " + Saves.LastError; return false; }
            Dirty = false;
            slots = null;
            return true;
        }

        /// <summary>Saves only if something changed since the last checkpoint (the engine calls this when the game is paused,
        /// put in the background or closed).</summary>
        public bool Flush() => Dirty && SaveNow();

        /// <summary>Picks up a saved career from a slot.</summary>
        public bool Load(int slot)
        {
            if (Saves == null) return false;
            var info = Saves.Info(slot);
            if (info.State != SlotState.Ok) { Toast = info.State == SlotState.Empty ? $"Slot {slot} is empty." : info.Error; return false; }
            var g = info.Game;
            Career = g.Career; Slot = slot; snap = g.Retry; snapSeed = g.RetrySeed; RaceWanted = null; Dirty = false;
            Screen = g.Screen == SaveGame.Reward ? CareerScreen.Reward : g.Screen == SaveGame.Over ? CareerScreen.Over : g.Screen == SaveGame.Event ? CareerScreen.Event : CareerScreen.Garage;
            Quali = null; QualiCard = null;
            if (Screen == CareerScreen.Garage || Screen == CareerScreen.Event) { Career.EnterGarage(); if (Career.Offers != null) Screen = CareerScreen.Season; }
            Toast = info.FromBackup ? $"Slot {slot}'s newest save was damaged, so this is the one before it." : g.LoadedFrom < CareerSave.Version ? $"Save from an older version, updated." : null;
            return true;
        }

        static readonly CultureInfo inv = CultureInfo.InvariantCulture;
        public static string Money(int v) => (v < 0 ? "−$" : "$") + Math.Abs(v).ToString("N0", inv);

        static readonly Rgb Green = Rgb.Hex("#2fb36a"), Amber = Rgb.Hex("#e9b52f"), Red = Rgb.Hex("#e5484d"), Dim = Rgb.Hex("#8a93a6"), Blue = Rgb.Hex("#9fd0ff");

        public static Rgb TierColor(RewardTier t) => t == RewardTier.Victory ? Amber : t == RewardTier.Strong ? Green : t == RewardTier.Solid ? Blue : Red;

        // ================================================================== pressing

        /// <summary>Presses a button by id. Returns false if it isn't on the page or is off.</summary>
        public bool Press(string id)
        {
            var page = Page();
            if (!page.CanPress(id)) { Toast = null; return false; }
            Toast = null;
            bool again = armed == id;
            armed = null;
            var before = Screen;
            bool ok = Do(id, again);
            if (ok) Checkpoint(id, before);
            return ok;
        }

        /// <summary>Which presses write the save at once: anything that starts, ends or reruns a race, every press on the reward
        /// screen (so rerolls can't be undone by quitting) and leaving the deck builder. Other garage changes mark it dirty and
        /// ride along with the next checkpoint (or Flush when the game closes).</summary>
        void Checkpoint(string id, CareerScreen before)
        {
            if (Career == null || Screen == CareerScreen.Title || id.StartsWith("title.", StringComparison.Ordinal)) return;
            bool now = id == "go" || id == "quit" || id == "deck.done" || before == CareerScreen.Reward || id.StartsWith("quali.", StringComparison.Ordinal)
                || id.StartsWith("offer:", StringComparison.Ordinal) || id.StartsWith("sponsor:", StringComparison.Ordinal)
                || id.StartsWith("ev.", StringComparison.Ordinal) || id.StartsWith("neg:", StringComparison.Ordinal) || id.StartsWith("drv.up:", StringComparison.Ordinal)
                || (id.StartsWith("crew.", StringComparison.Ordinal) && id != "crew.back" && !id.StartsWith("crew.pitbuy:", StringComparison.Ordinal) && id != "crew.pitcancel");
            if (now) SaveNow();
            else Dirty = true;
        }

        bool Do(string id, bool again)
        {
            string arg = null;
            int colon = id.IndexOf(':');
            string key = id;
            if (colon > 0) { key = id.Substring(0, colon); arg = id.Substring(colon + 1); }
            var c = Career;
            switch (key)
            {
                // title
                case "title.persona": PickPersona = arg; return true;
                case "title.bg": PickBg = arg == "auto" ? null : arg; return true;
                case "title.diff": PickDiff = (DifficultyLevel)Enum.Parse(typeof(DifficultyLevel), arg); return true;
                case "title.stakes": PickStakes = (Stakes)Enum.Parse(typeof(Stakes), arg); return true;
                case "title.start":
                {
                    var into = Saves != null ? Slots.FirstOrDefault(i => i.Slot == PickSlot) : null;
                    if (into != null && into.Used && !again) { armed = id; Toast = $"Slot {PickSlot} already has a career. Press Start career again to replace it."; return true; }
                    Career = Career.New(PickPersona, PickDiff, PickStakes, NewSeed, background: PickBg);
                    Career.EnterGarage();
                    Screen = CareerScreen.Garage;
                    snap = null;
                    if (Saves != null) { Slot = PickSlot; SaveNow(); }
                    return true;
                }
                case "title.slot": PickSlot = int.Parse(arg, inv); return true;
                case "title.continue": return Load(Saves.MostRecent());
                case "title.load": return Load(int.Parse(arg, inv));
                case "title.delete":
                {
                    int n = int.Parse(arg, inv);
                    if (!again) { armed = id; Toast = $"Press Delete again to erase slot {n}. This can't be undone."; return true; }
                    Saves.Delete(n);
                    if (Slot == n) Slot = 0;
                    RefreshSlots();
                    Toast = $"Slot {n} erased.";
                    return true;
                }
                case "quit":
                    SaveNow();
                    Career = null; Slot = 0; snap = null;
                    Screen = CareerScreen.Title;
                    RefreshSlots();
                    return true;
                // garage
                // the race weekend: Go racing opens qualifying, and its grid screen starts the race
                case "go":
                    QualiCard = null;
                    Screen = c.QualiDone ? CareerScreen.QualiDone : CareerScreen.Quali;
                    return true;
                case "quali.card": QualiCard = arg == "none" ? null : arg; return true;
                case "quali.back": Screen = CareerScreen.Garage; return true;
                case "quali.start":
                    Quali = c.StartQuali(QualiCard);
                    Screen = CareerScreen.QualiRun;
                    return true;
                case "quali.skip": case "quali.giveup":
                    Quali = null;
                    c.FinishQuali(null);
                    Screen = CareerScreen.QualiDone;
                    return true;
                case "quali.race":
                    snap = c.Clone(); snapSeed = c.RaceSeed;
                    RaceWanted = c.NextRace();
                    Screen = CareerScreen.Garage;
                    return true;
                case "sponsor": if (c.SignSponsor(arg)) Toast = $"Signed {SeasonRules.Sponsor(arg).Name} for season {c.Season}."; return true;
                case "offer":
                    if (!c.SignContract(int.Parse(arg, inv))) return false;
                    Toast = c.ContractNote;
                    Screen = CareerScreen.Garage;
                    return true;
                case "deck": Screen = CareerScreen.DeckBuilder; return true;
                case "buy": if (c.BuyStat(arg)) Toast = $"{CareerRules.ShopStatOf(arg).Name} up to {c.StatLevel(arg)}."; return true;
                case "part.buy": { var p = c.ShopPart; if (c.BuyShopPart()) Toast = $"{Parts.Get(p).Name} bought{(c.EquippedIn(Parts.Get(p).Slot) == p ? " and fitted" : ". It's in your spares")}."; return true; }
                case "part.reroll": c.RerollShopPart(); return true;
                case "equip": c.Equip(arg); return true;
                case "unequip": c.Unequip((PartSlot)Enum.Parse(typeof(PartSlot), arg)); return true;
                case "tune.hand": c.SetTune(hand: int.Parse(arg, inv)); return true;
                case "tune.aero": c.SetTune(aero: int.Parse(arg, inv)); return true;
                case "tune.focus": c.SetTune(focus: (ComboFocus)Enum.Parse(typeof(ComboFocus), arg)); return true;
                case "tune.reset": c.ResetTune(); return true;
                case "deck.fix": c.FixDeck(); return true;
                // deck builder
                case "deck.in": c.AddToDeck(arg); return true;
                case "deck.out": c.RemoveFromDeck(arg); return true;
                case "deck.sort": DeckSort = arg; return true;
                case "deck.tab": DeckTab = arg == "all" ? (CardFamily?)null : (CardFamily)Enum.Parse(typeof(CardFamily), arg); return true;
                case "deck.auto": c.AutoDeck(); return true;
                case "deck.last": c.LastDeck(); return true;
                case "deck.fill": c.FillDeck(); return true;
                case "deck.clear": c.ClearDeck(); return true;
                case "deck.done": c.DeckNote = null; Screen = CareerScreen.Garage; return true;
                // reward
                case "reward.card": c.PickCard(arg); return true;
                case "reward.skip": c.SkipCard(); return true;
                case "reward.reroll": c.RerollCards(); return true;
                case "reward.part": c.PickPart(arg); return true;
                case "reward.partreroll": c.RerollParts(); return true;
                case "reward.retry":
                    Career = snap.Clone();
                    RaceWanted = Career.NextRace(snapSeed + 7919u * (uint)(c.Results.Count + 1));
                    Screen = CareerScreen.Garage;
                    return true;
                case "reward.go":
                    if (c.Over) { Screen = CareerScreen.Over; return true; }
                    c.Continue();
                    Screen = c.Offers != null ? CareerScreen.Season : CareerScreen.Garage;
                    return true;
                case "over.new": Career = null; Slot = 0; snap = null; Screen = CareerScreen.Title; RefreshSlots(); return true;
            }
            if (key == "crew" || key.StartsWith("crew.", StringComparison.Ordinal)) return DoCrew(key, arg, again, id);
            return DoDriver(key, arg);
        }

        /// <summary>Advances the qualifying run by dt seconds with brake held or not (Space, A or the BRAKE button). When the
        /// car takes the flag the grid is set, saved, and the grid screen comes up. Returns true while the run goes on.</summary>
        public bool QualiTick(double dt, bool held)
        {
            if (Screen != CareerScreen.QualiRun || Quali == null) return false;
            Quali.Tick(dt, held);
            if (!Quali.Done) return true;
            Career.FinishQuali(Quali);
            Screen = CareerScreen.QualiDone;
            SaveNow();
            return false;
        }

        /// <summary>The engine started RaceWanted: clear it.</summary>
        public RaceSetup TakeRace() { var r = RaceWanted; RaceWanted = null; return r; }

        /// <summary>The race is over: pay out and show the reward screen.</summary>
        public RaceReport RaceFinished(Race race)
        {
            var res = Career.FinishRace(race);
            Screen = CareerScreen.Reward;
            SaveNow();
            return res;
        }

        // ================================================================== pages

        public MenuPage Page()
        {
            switch (Screen)
            {
                case CareerScreen.Garage: return GaragePage();
                case CareerScreen.DeckBuilder: return DeckPage();
                case CareerScreen.Reward: return RewardPage();
                case CareerScreen.Over: return OverPage();
                case CareerScreen.Quali: return QualiPage();
                case CareerScreen.QualiRun: return QualiRunPage();
                case CareerScreen.QualiDone: return QualiDonePage();
                case CareerScreen.Season: return SeasonPage();
                case CareerScreen.Event: return EventPage();
                case CareerScreen.Driver: return DriverPage();
                case CareerScreen.Crew: return CrewPage();
                default: return TitlePage();
            }
        }

        static string TuneLabel(Tune u, bool kart)
        {
            string d(int v, string lo, string hi) => v == 0 ? "Neutral" : (v < 0 ? lo : hi) + (Math.Abs(v) > 1 ? " ++" : " +");
            return $"{d(u.Hand, "Tight", "Loose")} · {d(u.Aero, "Downforce", "Low drag")} · {u.FocusName(kart)}";
        }

        /// <summary>A slot's one-line description on the title screen.</summary>
        public static string SlotLine(SlotInfo i)
        {
            switch (i.State)
            {
                case SlotState.Empty: return "Empty";
                case SlotState.TooNew: return "Saved by a newer version of the game";
                case SlotState.Broken: return "Damaged: " + i.Error;
            }
            var g = i.Game; var c = g.Career;
            string where = c.Over ? "Run over" : g.Screen == SaveGame.Reward ? $"Results of race {c.Reward.Res.RaceNo}" : c.Offers != null ? $"Season {c.Review?.Season} over, P{c.Review?.Rank}: contract offers" : $"Before race {c.RaceIdx + 1} of {c.SeasonLength}, {c.NextTrack.Name}";
            return $"{c.Name} · {Personas.Get(c.Persona).Name} · {c.Series} season {c.Season} · {where} · {Money(c.Money)} · saved {g.SavedAt.ToString("MMM d HH:mm", inv)} UTC{(i.FromBackup ? " (backup)" : "")}";
        }

        void SlotsSection(MenuPage p)
        {
            var s = p.Section("Saved careers", 0, "Three slots. Your career saves itself when you go racing, at the results and when you leave the deck builder.");
            foreach (var i in Slots)
            {
                string n = i.Slot.ToString(inv);
                s.Add(new MenuItem { Kind = MenuKind.Part, Id = "title.load:" + n, Label = $"Slot {i.Slot}", Value = i.State == SlotState.Ok ? "Load" : "", Note = SlotLine(i), Enabled = i.State == SlotState.Ok, Color = i.State == SlotState.Broken || i.State == SlotState.TooNew ? Red : i.Used ? Green : Dim });
                if (i.Used) s.Button("title.delete:" + n, armed == "title.delete:" + n ? $"Press again to erase slot {i.Slot}" : $"Delete slot {i.Slot}");
            }
        }

        MenuPage TitlePage()
        {
            var p = new MenuPage { Screen = CareerScreen.Title, Eyebrow = "New career", Title = "Green Flag Deck", Sub = "A racing career you drive with a hand of cards. Pick a persona: it sets your starting cards, one part and a default car tune. Card rewards and the garage can take you anywhere.", Notice = Toast };
            bool cont = Saves != null && Slots.Any(i => i.State == SlotState.Ok);
            if (Saves != null)
            {
                if (cont) { p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "title.continue", Label = "Continue", Primary = true, Note = "The career you saved last" }); p.Default = "title.continue"; }
                SlotsSection(p);
            }
            var kart = p.Section("Karts (the ladder starts here)", 0, "A kart persona starts in the kart series.");
            var stock = p.Section("Stock cars", 1, "Skip the ladder and start in stock cars.");
            foreach (var pr in Personas.All)
            {
                var part = Parts.Get(pr.Part);
                (pr.Kart ? kart : stock).Add(new MenuItem { Kind = MenuKind.Chip, Id = "title.persona:" + pr.Id, Label = pr.Name, Value = pr.Tag, On = PickPersona == pr.Id, Note = $"Part: {part.Name}. {part.Text} Tune: {TuneLabel(pr.Tune, pr.Kart)}. Cards: {string.Join(", ", pr.Add.Select(id => CardBook.Get(id).Name))}." });
            }
            BackgroundSection(p);
            var d = p.Section("AI skill", 0, "Harder fields pay less and cost more to reroll.");
            foreach (DifficultyLevel l in Enum.GetValues(typeof(DifficultyLevel)))
                d.Chip("title.diff:" + l, l.ToString(), PickDiff == l, note: $"Purse ×{Difficulty.For(l).Purse.ToString("0.##", inv)} · rerolls from {Money(CareerRules.RerollBase(l))}");
            var s = p.Section("Stakes", 1, "Harder stakes pay bigger purses.");
            s.Chip("title.stakes:Standard", "Standard", PickStakes == Stakes.Standard, note: "Retry any race from the results screen.");
            s.Chip("title.stakes:Ironman", "Ironman +10%", PickStakes == Stakes.Ironman, note: "No retries. A DNF costs $3,000 and a random spare part.");
            s.Chip("title.stakes:Permadeath", "Permadeath +20%", PickStakes == Stakes.Permadeath, note: "No retries. A DNF ends the run, but one Survive token turns the first DNF into an Ironman penalty.");
            if (Saves != null)
            {
                var into = p.Section("Start the new career in", 1);
                foreach (var i in Slots) into.Chip("title.slot:" + i.Slot.ToString(inv), $"Slot {i.Slot}{(i.Used ? " (replaces it)" : "")}", PickSlot == i.Slot, i.State != SlotState.TooNew);
            }
            var pick = Slots.FirstOrDefault(i => i.Slot == PickSlot);
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "title.start", Label = armed == "title.start" ? $"Replace slot {PickSlot}" : "Start career", Primary = !cont, Note = Saves != null && pick != null && pick.Used ? $"Replaces the career in slot {PickSlot}" : null });
            return p;
        }

        void TuneSection(MenuPage p, TrackDef t, int column)
        {
            var c = Career; var u = c.Tune;
            var fit = Career.TuneFit(t, u);
            var s = p.Section("Car tune", column, "Your tune shapes your deck for this race: which families you draw more of, a few odds points, and one combo type that chains longer. Every choice has a cost.");
            s.Text(fit.text + ": " + DeckRules.ShortName(t) + (c.PersonaTune ? "  (persona tune)" : ""), fit.verdict > 0 ? Green : fit.verdict < 0 ? Red : Dim);
            string lab(int v, string lo, string hi) => v == 0 ? "Neutral" : (v < 0 ? lo : hi) + (Math.Abs(v) > 1 ? " ++" : "");
            for (int v = -2; v <= 2; v++) s.Chip("tune.hand:" + v.ToString(inv), "Handling: " + lab(v, "Tight", "Loose"), u.Hand == v);
            for (int v = -2; v <= 2; v++) s.Chip("tune.aero:" + v.ToString(inv), "Aero: " + lab(v, "Downforce", "Low drag"), u.Aero == v);
            foreach (ComboFocus f in Enum.GetValues(typeof(ComboFocus)))
            {
                if (f == ComboFocus.None || (c.Kart && f == ComboFocus.Restart)) continue;
                s.Chip("tune.focus:" + f, new Tune { Focus = f }.FocusName(c.Kart), u.Focus == f);
            }
            if (!c.PersonaTune) s.Button("tune.reset", "Reset to persona tune");
            foreach (var (k, v) in TuneFx(u, t)) s.Row(k, v);
        }

        /// <summary>What the tune does, in words (the slice's tuneFx, for the next track only).</summary>
        public static List<(string, string)> TuneFx(Tune u, TrackDef t)
        {
            var o = new List<(string, string)>();
            string pct(double x) => (x >= 1 ? "+" : "−") + Math.Round(Math.Abs(x - 1) * 100).ToString(inv) + "%";
            var dr = new[] { CardFamily.Draft, CardFamily.Pass, CardFamily.Defend, CardFamily.Car, CardFamily.Risk }.Select(f => (f, m: u.DrawMul(f))).Where(x => Math.Abs(x.m - 1) > 0.01).ToList();
            if (dr.Count > 0) o.Add(("Draws", string.Join(", ", dr.Select(x => CardRules.FamilyName(x.f) + " " + pct(x.m)))));
            var od = new List<string>();
            if (u.Hand > 0) od.Add($"Passing & High risk +{2 * u.Hand}");
            if (u.Hand < 0) od.Add($"Defending & Car +{-3 * u.Hand}, Passing {u.Hand}");
            if (u.Aero > 0) od.Add($"Drafting +{2 * u.Aero}");
            if (u.Aero < 0) od.Add($"Passing in corners +{-3 * u.Aero}");
            if (od.Count > 0) o.Add(("Odds", string.Join(", ", od)));
            double w = u.Wear;
            if (Math.Abs(w - 1) > 0.005) o.Add(("Tire wear", pct(w)));
            if (u.Aero != 0) { double sp = (u.Pace(t) - 1) * 100; o.Add(("Speed here", (sp >= 0 ? "+" : "") + sp.ToString("0.0", inv) + "%")); }
            return o;
        }

        static string FamStrip(IEnumerable<string> deck) =>
            string.Join("  ", CardStyle.Order.Select(f => (f, n: DeckRules.FamCount(deck, f))).Where(x => x.n > 0).Select(x => CardRules.FamilyName(x.f) + " " + x.n));

        void RulesRows(MenuSection s, TrackDef t)
        {
            foreach (var r in DeckRules.Rules(Career, t))
                s.Row((r.Ok ? "✓ " : "✗ ") + r.Text, r.K == DeckRule.Kind.Cap ? $"{r.Have} / {r.Need}" : r.Have.ToString(inv), r.Ok ? (Rgb?)null : Red);
        }

        MenuPage GaragePage()
        {
            var c = Career; var t = c.NextTrack;
            var pr = Personas.Get(c.Persona);
            var p = new MenuPage
            {
                Screen = CareerScreen.Garage, Eyebrow = $"Garage · season {c.Season} · before race {c.RaceIdx + 1} of {c.SeasonLength} · {c.Diff} AI · {c.Stakes}{(c.Stakes == Stakes.Permadeath ? $" · Survive tokens: {c.Survive}" : "")}",
                Title = $"{c.Name} · {pr.Name}", Money = Money(c.Money), Notice = Toast ?? c.DeckNote,
            };
            var why = c.CantRace;
            var nx = p.Section($"Next race · {c.RaceIdx + 1} of {c.SeasonLength} · {SeasonRules.SeriesName(c.Series)}", 0, null, Green);
            nx.Row(t.Name, $"{t.Type} · {t.Laps} laps");
            nx.Row("Expected finish (car ratings)", "P" + c.ExpectedFinish().ToString(inv));
            nx.Row("Points", c.SeasonResults.Count > 0 ? $"P{c.MyRank()} · {c.MyPoints} pts" : "Season starts here");
            nx.Row("Team", $"{c.TeamName} · rating {c.SeriesTeams[c.Team].Rating}{(c.Contract.Per > 0 ? " · " + Money(c.Contract.Per) + " a race" : " · no salary yet")}");
            if (why != null) nx.Text(why + (c.Sponsor == null ? ": pick one below." : c.DeckOk() ? "." : ". Fix it in the deck builder."), Red);
            EventPanel(p);
            PoachPanel(p);
            SponsorSection(p);
            ObjectivesSection(p);
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "go", Label = "Go racing", Enabled = why == null, Primary = true, Note = why });
            p.Default = why == null ? "go" : c.Event != null && !c.Event.Done ? "ev.open" : c.Poach != null && !c.Poach.Done ? "crew" : c.Sponsor == null && c.SponsorOffers != null && c.SponsorOffers.Count > 0 ? "sponsor:" + c.SponsorOffers[0].Id : "deck";
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "deck", Label = "Build race deck" });
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "drv", Label = "Driver sheet" + (c.Driver.Pts > 0 ? $" · {c.Driver.Pts} point{(c.Driver.Pts > 1 ? "s" : "")} to spend" : ""), Note = "Attributes, skills, image, Hype and recent checks" });
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "crew", Label = CrewButtonLabel(), Note = "Hire and train staff, pick perks, upgrade pit cards" });
            if (Saves != null && Slot > 0) p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "quit", Label = "Save and quit", Note = $"Saves to slot {Slot} and goes back to the title screen" });

            TuneSection(p, t, 0);

            var shop = p.Section("Shop upgrades", 0, "One level at a time, up to 10. Driver coaching follows you to a new team; the car stays with the team.");
            foreach (var st in CareerRules.Shop)
            {
                int lvl = c.StatLevel(st.Id); var no = c.CantBuyStat(st.Id);
                shop.Add(new MenuItem { Kind = MenuKind.Button, Id = "buy:" + st.Id, Label = $"{st.Group}: {st.Name} {lvl}", Value = lvl >= CareerRules.StatMax ? "Max" : "+1 · " + Money(c.StatCost(st.Id)), Enabled = no == null, Note = st.Text });
            }

            var dealer = p.Section("Parts dealer", 1);
            if (c.ShopPart != null)
            {
                var part = Parts.Get(c.ShopPart);
                dealer.Add(new MenuItem { Kind = MenuKind.Part, Label = part.Name, Value = CareerRules.SlotName(part.Slot), Note = part.Text });
                dealer.Button("part.buy", "Buy · " + Money(CareerRules.PartPrice), c.Money >= CareerRules.PartPrice);
            }
            else dealer.Text("Sold out.", Dim);
            dealer.Button("part.reroll", "Reroll · " + Money(c.ShopRerollCost), c.Money >= c.ShopRerollCost);

            var eq = p.Section("Equipment", 1, "Click a fitted part to take it off, or a spare to fit it in its slot.");
            foreach (PartSlot sl in Enum.GetValues(typeof(PartSlot)))
            {
                var id = c.EquippedIn(sl);
                if (id != null) { var part = Parts.Get(id); eq.Add(new MenuItem { Kind = MenuKind.Part, Id = "unequip:" + sl, Label = part.Name, Value = CareerRules.SlotName(sl), Note = part.Text, On = true }); }
                else eq.Add(new MenuItem { Kind = MenuKind.Part, Label = "Empty slot", Value = CareerRules.SlotName(sl), Color = Dim });
            }
            foreach (var id in c.Spares) { var part = Parts.Get(id); eq.Add(new MenuItem { Kind = MenuKind.Part, Id = "equip:" + id, Label = part.Name, Value = "spare · " + CareerRules.SlotName(part.Slot), Note = part.Text }); }

            StandingsSection(p, 1, $"Season {c.Season} standings", c.StandingsTop(10), c.SeasonResults.Count == 0 ? "No races yet. A win pays 40 points, 2nd 35, then one less per place." : null);

            bool ok = c.DeckOk(t);
            var dk = p.Section($"Race deck ({c.Deck.Count} of {c.Collection.Count} cards owned)", 1, "You keep every card in your collection. Before each race you pick which to take. The track sets a minimum, and your tune sets how many attack, defense and drafting cards fit.", ok ? (Rgb?)null : Red);
            dk.Text(FamStrip(c.Deck));
            RulesRows(dk, t);
            dk.Button("deck", "Build race deck", primary: true);
            if (!ok) dk.Button("deck.fix", "Fix it for me");
            return p;
        }

        static readonly string[][] sorts = { new[] { "fam", "Family" }, new[] { "fit", "Best here" }, new[] { "succ", "Your success" }, new[] { "rar", "Rarity" }, new[] { "name", "Name" } };

        /// <summary>The card's big number: your success rate once you've played it 3 times, otherwise none yet.</summary>
        public string CardRate(string id) => Career.Hist.TryGetValue(id, out var h) && h.Plays >= 3 ? $"{Math.Round(100.0 * h.Succ / h.Plays).ToString(inv)}% yours" : null;

        MenuPage DeckPage()
        {
            var c = Career; var t = c.NextTrack;
            var (_, max) = DeckRules.Size(c.Series, c.Tune);
            bool ok = c.DeckOk(t);
            var p = new MenuPage { Screen = CareerScreen.DeckBuilder, Eyebrow = $"Deck builder · race {c.RaceIdx + 1} of {c.SeasonLength}", Title = "Race deck for " + t.Name, Sub = $"{t.Type} · {c.Deck.Count} / {max} cards", Money = Money(c.Money), Notice = Toast };
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "deck.done", Label = "Done", Enabled = ok, Primary = true, Note = ok ? null : "The deck breaks a rule" });
            p.Default = ok ? "deck.done" : "deck.auto";

            var own = new Dictionary<string, int>();
            foreach (var id in c.Collection) own[id] = own.TryGetValue(id, out var n) ? n + 1 : 1;
            double rate(string id) => c.Hist.TryGetValue(id, out var h) && h.Plays > 0 ? (double)h.Succ / h.Plays : -1;
            int fam(string id) => CardStyle.OrderOf(CardBook.Get(id).Family);
            var ids = own.Keys.Where(id => DeckTab == null || CardBook.Get(id).Family == DeckTab).ToList();
            IOrderedEnumerable<string> q;
            switch (DeckSort)
            {
                case "fit": q = ids.OrderByDescending(id => DeckRules.Score(c, id, t)); break;
                case "succ": q = ids.OrderByDescending(rate); break;
                case "rar": q = ids.OrderByDescending(id => (int)CardBook.Get(id).Rarity); break;
                case "name": q = ids.OrderBy(id => CardBook.Get(id).Name, StringComparer.Ordinal); break;
                default: q = ids.OrderBy(fam); break;
            }
            ids = q.ThenBy(fam).ThenBy(id => CardBook.Get(id).Name, StringComparer.Ordinal).ToList();

            var col = p.Section($"Collection ({c.Collection.Count})", 0, "Press a card or + to put one in the race deck, − to take one out. Lit cards are in the deck.");
            foreach (var s in sorts) col.Chip("deck.sort:" + s[0], "Sort: " + s[1], DeckSort == s[0]);
            col.Chip("deck.tab:all", $"All {c.Collection.Count}", DeckTab == null);
            foreach (var f in CardStyle.Order)
            {
                int n = DeckRules.FamCount(c.Collection, f);
                if (n > 0) col.Chip("deck.tab:" + f, $"{CardRules.FamilyName(f)} {n}", DeckTab == f);
            }
            foreach (var id in ids)
            {
                var cd = CardBook.Get(id);
                int inD = DeckRules.Count(c.Deck, id);
                var why = c.AddBlock(id, t);
                string tags = (cd.CanWreck ? "Can wreck · " : cd.NoFail ? "Never fails · " : "") + cd.Rarity.ToString().ToLowerInvariant();
                col.Add(new MenuItem
                {
                    Kind = MenuKind.Card, Id = "deck.in:" + id, Label = cd.Name, Value = $"{inD} / {own[id]}", On = inD > 0, Enabled = why == null,
                    Note = $"{CardRules.FamilyName(cd.Family)} · {tags}{(CardRate(id) != null ? " · " + CardRate(id) : "")}. {cd.Text}{(why != null ? $" ({why})" : "")}",
                    AltId = "deck.out:" + id, AltLabel = "−", AltEnabled = inD > 0, Color = CardStyle.Family(cd.Family),
                });
            }

            var side = p.Section($"Race deck · {c.Deck.Count} cards", 1, null, ok ? (Rgb?)null : Red);
            side.Text(FamStrip(c.Deck));
            RulesRows(side, t);
            side.Button("deck.auto", "Auto-build for " + DeckRules.ShortName(t), note: "Picks the cards that fit this track and your tune best");
            if (!ok) side.Button("deck.fix", "Fix rules");
            if (c.TrackDecks.ContainsKey(t.Id)) side.Button("deck.last", $"Last {DeckRules.ShortName(t)} deck", note: "The deck you raced here last time");
            side.Button("deck.fill", "Fill to " + max);
            side.Button("deck.clear", "Clear");
            foreach (var g in c.Deck.GroupBy(x => x).OrderBy(g => fam(g.Key)).ThenBy(g => CardBook.Get(g.Key).Name, StringComparer.Ordinal))
            {
                var cd = CardBook.Get(g.Key);
                side.Add(new MenuItem { Kind = MenuKind.Button, Id = "deck.out:" + g.Key, Label = cd.Name + (g.Count() > 1 ? " ×" + g.Count() : ""), Note = "Take one out", Color = CardStyle.Family(cd.Family) });
            }
            TuneSection(p, t, 1);
            return p;
        }

        MenuPage RewardPage()
        {
            var c = Career; var R = c.Reward; var res = R.Res;
            bool last = res.RaceNo >= c.SeasonLength;
            var p = new MenuPage
            {
                Screen = CareerScreen.Reward, Eyebrow = $"Race {res.RaceNo} of {c.SeasonLength} · {res.TrackName}",
                Title = $"P{res.Finish}{(res.Dnf ? " · DNF" : "")}", Sub = $"{CareerRules.TierName(res.Tier)} · started P{res.Start} · expected P{res.Expected} · {res.Succ} of {res.Plays} cards landed{(res.Calls > 0 ? $" · {res.CallsWon} of {res.Calls} crew calls worked" : "")}",
                TitleColor = TierColor(res.Tier), Money = Money(c.Money), Notice = Toast,
            };
            var pay = p.Section("Payout", 0);
            pay.Row($"Purse for P{res.Finish}", Money(res.Purse));
            pay.Row($"Beat expectation by {res.BeatBy}", Money(res.Bonus));
            foreach (var o in res.Objectives.Where(o => o.Ok)) pay.Row((o.Ask ? "Sponsor ask: " : "Objective: ") + o.Name, Money(o.Pay));
            if (res.Sponsor != null) pay.Row(SeasonRules.Sponsor(res.Sponsor).Name + " (per race)", Money(res.Retainer));
            if (res.Salary > 0) pay.Row("Team salary", Money(res.Salary));
            if (res.Wages > 0) pay.Row("Crew wages", "−" + Money(res.Wages), Red);
            pay.Row("Total", (res.Total >= 0 ? "+" : "") + Money(res.Total), res.Total >= 0 ? Green : Red);
            pay.Row($"Points +{res.Pts}", $"P{res.Rank} in points · {res.PtsTotal} pts{(res.RankBefore != res.Rank && res.RaceNo > 1 ? $" (was P{res.RankBefore})" : "")}", res.Rank < res.RankBefore ? Green : (Rgb?)null);
            pay.Row("Bank", Money(c.Money));
            if (res.Penalty == "survive") pay.Text($"Survive token used: the DNF would have ended your run. It still cost {Money(CareerRules.DnfFine)}{(res.LostPart != null ? " and your spare " + Parts.Get(res.LostPart).Name : "")}. No tokens left.", Amber);
            else if (res.Penalty == "ironman") pay.Text($"Ironman penalty: the DNF cost {Money(CareerRules.DnfFine)}{(res.LostPart != null ? " and your spare " + Parts.Get(res.LostPart).Name : "")}.", Red);
            else if (res.Penalty == "dead") pay.Text("Permadeath: the DNF ends the run.", Red);

            DriverRewardRows(pay, res);
            CrewRewardRows(pay, res);

            if (res.Objectives.Count > 0)
            {
                var ob = p.Section($"Objectives · {res.ObjMet} of {res.Objectives.Count}", 0, res.TierFrom != null ? $"All met: rewards moved up from {CareerRules.TierName(res.TierFrom.Value)} to {CareerRules.TierName(res.Tier)}." : "Meet them all and finish, and the rewards move up a tier.", res.TierFrom != null ? Green : (Rgb?)null);
                foreach (var o in res.Objectives) ob.Row((o.Ok ? "✓ " : "✗ ") + (o.Ask ? "Sponsor ask: " : "") + o.Name, o.Ok ? "+" + Money(o.Pay) : Money(o.Pay), o.Ok ? Green : Dim);
            }
            var top = p.Section("Top 10 this race", 1);
            foreach (var o in res.Order) top.Row($"{o.Pos}. {(o.IsMe ? o.Name + " (you)" : o.Name)}", (o.Dnf ? "DNF · " : "") + o.Team, o.IsMe ? Green : (Rgb?)null);

            if (res.Penalty != "dead")
            {
                var pick = p.Section(R.Picked == null ? "Pick a card for your collection" : "Card added", 0, null);
                if (R.Picked == null)
                {
                    foreach (var id in R.Cards)
                    {
                        var cd = CardBook.Get(id);
                        string cost = cd.TireCost > 0 ? $"{cd.TireCost}% tire" : cd.MomCost > 0 ? $"{cd.MomCost} Momentum" : "Free";
                        pick.Add(new MenuItem { Kind = MenuKind.Card, Id = "reward.card:" + id, Label = cd.Name, Value = $"{CardRules.FamilyName(cd.Family)} · {cd.Rarity.ToString().ToLowerInvariant()}", Note = $"{cd.Text} {cost} · {(cd.CanWreck ? "Can wreck" : cd.NoFail ? "Never fails" : "Safe")}.", Color = CardStyle.Family(cd.Family) });
                    }
                    int rc = c.RerollCost(R.CardRerolls);
                    pick.Button("reward.reroll", "Reroll · " + Money(rc), c.Money >= rc);
                    pick.Button("reward.skip", "Skip · +" + Money(CareerRules.SkipCash));
                }
                else pick.Text(R.Picked == "skip" ? "You skipped the card and took $1,000." : CardBook.Get(R.Picked).Name + (R.InDeck ? " is in your collection and your race deck." : " is in your collection. Your race deck had no room, so add it in the deck builder."));

                if (R.Parts != null)
                {
                    var pp = p.Section(R.PartPicked == null ? "Strong run bonus: pick a part" : "Part installed", 0, null, Amber);
                    if (R.PartPicked == null)
                    {
                        foreach (var id in R.Parts) { var part = Parts.Get(id); pp.Add(new MenuItem { Kind = MenuKind.Part, Id = "reward.part:" + id, Label = part.Name, Value = CareerRules.SlotName(part.Slot), Note = part.Text }); }
                        int rc = c.RerollCost(R.PartRerolls);
                        pp.Button("reward.partreroll", "Reroll · " + Money(rc), c.Money >= rc);
                    }
                    else pp.Text($"{Parts.Get(R.PartPicked).Name} added to your inventory{(c.EquippedIn(Parts.Get(R.PartPicked).Slot) == R.PartPicked ? " and fitted" : "")}.");
                }
            }

            if (res.Log.Count > 0)
            {
                var calls = p.Section("Your calls", 1, "Every card you played, the real odds at that moment, and what moved them. Use it to learn what a good moment looks like.");
                foreach (var l in res.Log)
                {
                    var moved = l.Tags.Length == 0 ? "—" : string.Join(" · ", l.Tags.Select(g => (g.Value > 0 ? "▲ " : "▼ ") + g.Label + " " + (g.Value > 0 ? "+" : "") + g.Value.ToString("0.#", inv)));
                    calls.Row($"Lap {l.Lap} · {CardBook.Get(l.Card).Name} · {Math.Round(l.Succ * 100).ToString(inv)}% · {CardRules.TierName(l.Tier)}", moved, CardStyle.Tier(l.Tier));
                }
            }

            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "reward.go", Label = res.Penalty == "dead" ? "See how the run ended" : last ? "Finish the season" : "Back to the garage", Primary = true, Note = R.Done ? null : "An unpicked reward is forfeited" });
            // a quick A at the flag lands on the first card, not on leaving the screen
            p.Default = R.Picked == null && R.Cards.Count > 0 ? "reward.card:" + R.Cards[0] : R.Parts != null && R.PartPicked == null ? "reward.part:" + R.Parts[0] : "reward.go";
            if (c.CanRetry && snap != null) p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "reward.retry", Label = "Retry this race" });
            return p;
        }

        // ================================================================== the season (step 37)

        static string GoalLine(SponsorDef sp, int seasonLength) => SeasonRules.Goal(sp.Goal).Name(seasonLength);

        public static Rgb GoalColor(GoalState st) => st == GoalState.Met || st == GoalState.On ? Green : st == GoalState.Missed ? Red : st == GoalState.Behind ? Amber : Dim;

        void SponsorSection(MenuPage p)
        {
            var c = Career;
            int e = c.ExpectedFinish();
            if (c.Sponsor != null)
            {
                var sp = SeasonRules.Sponsor(c.Sponsor.Id);
                var gs = c.GoalStatus();
                var s = p.Section("Sponsor · " + sp.Name, 0, null, Rgb.Hex(sp.Color));
                s.Row("Per race", Money(c.Sponsor.Per));
                s.Row("Season goal: " + GoalLine(sp, c.SeasonLength), $"{gs.prog} · {SeasonRules.GoalStateName(gs.st)}", GoalColor(gs.st));
                s.Row("Goal bonus at season end", Money(c.Sponsor.Bonus));
                return;
            }
            var o = p.Section($"Sign a sponsor for season {c.Season}", 0, "One sponsor a season. They pay every race, ask for one race objective each week (it pays 1.5×), and pay a bonus if you meet their season goal. Meet it and they renew with a 20% raise; miss it and they walk.", Amber);
            foreach (var d in c.SponsorOffers ?? new List<SponsorDeal>())
            {
                var sp = SeasonRules.Sponsor(d.Id);
                o.Add(new MenuItem
                {
                    Kind = MenuKind.Part, Id = "sponsor:" + d.Id, Label = sp.Name + (d.Renew ? " · renewal +20%" : ""), Value = Money(d.Per) + " a race", Color = Rgb.Hex(sp.Color),
                    Note = $"{sp.Blurb} Season goal: {GoalLine(sp, c.SeasonLength)} (bonus {Money(d.Bonus)}). Asks: {string.Join(" · ", sp.Asks.Select(a => SeasonRules.Objective(a).Name(e, c.Kart)))}.",
                });
            }
        }

        void ObjectivesSection(MenuPage p)
        {
            var c = Career;
            if (c.Objectives == null) return;
            var s = p.Section("Race objectives", 1, "Meet them all and finish, and the rewards move up a tier. Targets follow your expected finish.");
            foreach (var o in c.Objectives) s.Row((o.Ask ? "Sponsor ask: " : "") + c.ObjectiveText(o), Money(c.ObjectivePay(o, c.PayMul)), o.Ask ? Amber : (Rgb?)null);
        }

        void StandingsSection(MenuPage p, int column, string title, List<StandRow> rows, string note = null)
        {
            var s = p.Section(title, column, note);
            int lead = rows.Count > 0 ? rows[0].Pts : 0;
            foreach (var r in rows)
                s.Row($"{r.Pos}. {(r.IsMe ? r.Name + " (you)" : r.Name)}", $"{r.Pts} pts{(r.Wins > 0 ? $" · {r.Wins} win{(r.Wins > 1 ? "s" : "")}" : "")}{(r.Pos > 1 && lead > 0 ? $" · −{lead - r.Pts}" : "")} · {r.Team}", r.IsMe ? Green : (Rgb?)null);
        }

        MenuPage QualiPage()
        {
            var c = Career; var t = c.NextTrack;
            var st = c.Base.Car;
            var pl = Qualifying.Plan(t, st, null, new SimRng(1));
            var p = new MenuPage
            {
                Screen = CareerScreen.Quali, Eyebrow = $"Qualifying · race {c.RaceIdx + 1} of {c.SeasonLength}", Title = t.Name, Money = Money(c.Money), Notice = Toast,
                Sub = $"A rolling start, then {pl.Laps} timed laps ({pl.Checks.Count} corners). At every corner a needle sweeps the brake bar: let go of Space (A on a controller, or the BRAKE button) when it is in the green zone, gold is Perfect. Hold it down early for slow-mo while your Focus lasts. Clean corners in a row build a chain up to ×4. Your time sets where you start.",
            };
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "quali.start", Label = "Start the run", Primary = true, Note = QualiCard != null ? CardBook.Get(QualiCard).Name + " starts the race in your discard pile" : null });
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "quali.skip", Label = "Skip: sim an average lap", Note = "The same odds as a grid by car ratings" });
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "quali.back", Label = "Back to the garage" });
            p.Default = "quali.start";
            var car = p.Section("Your car", 0, "Power, Aero, Handling and Racecraft set your base lap, like the AI's. Driver coaching (Racecraft and Consistency) makes every car quicker here.");
            car.Row("Base lap", Qualifying.BaseLap(t, st).ToString("0.00", inv) + " s");
            car.Row($"Good zone (Handling {st.Handling}, Consistency {st.Consistency})", Math.Round(pl.Wg * 200).ToString(inv) + "% of the bar");
            car.Row($"Perfect zone (Racecraft {st.Racecraft})", Math.Round(pl.Wp * 200).ToString(inv) + "% of the bar");
            var cards = p.Section("Use a card? (optional)", 1, "One card from your race deck can trim the run. It then starts the race in your discard pile.");
            cards.Chip("quali.card:none", "No card", QualiCard == null);
            foreach (var id in c.QualiOffer())
            {
                var cd = CardBook.Get(id);
                var fx = Qualifying.CardFx(cd.Family, t);
                cards.Add(new MenuItem { Kind = MenuKind.Card, Id = "quali.card:" + id, Label = cd.Name, Value = fx.name, Note = fx.text, On = QualiCard == id, Color = CardStyle.Family(cd.Family) });
            }
            return p;
        }

        MenuPage QualiRunPage()
        {
            var c = Career; var q = Quali;
            var p = new MenuPage { Screen = CareerScreen.QualiRun, Eyebrow = "Qualifying · on the clock", Title = c.NextTrack.Name, Money = Money(c.Money) };
            if (q != null)
            {
                var cur = q.Current;
                var sc = q.Score;
                p.Sub = cur == null ? "Take the flag" : q.Time < Qualifying.Roll ? "Rolling start · timing at the line" : $"{cur.Name} · {cur.Action}";
                var s = p.Section("Run", 0);
                s.Row("Corners", $"{q.Plan.Checks.Count(x => x.Res != null)} of {q.Plan.Checks.Count}");
                s.Row("Chain", "×" + sc.Chain.ToString(inv));
                s.Row("Focus", Math.Round(q.Focus * 100).ToString(inv) + "%");
                for (int i = 0; i < q.LapTimes.Count; i++) s.Row("Lap " + (i + 1).ToString(inv), q.LapTimes[i].ToString("0.000", inv));
            }
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "quali.giveup", Label = "Give up: take an average lap" });
            return p;
        }

        MenuPage QualiDonePage()
        {
            var c = Career; var q = c.Qual; var t = c.NextTrack;
            var avgRef = Qualifying.BaseLap(t, c.Base.Car);
            string how = q.Skip ? "Simmed an average lap." : string.Join(" · ", Enumerable.Range(0, 5).Where(i => q.Counts[i] > 0).Select(i => $"{q.Counts[i]} {Qualifying.MarkName((QualiMark)i)}")) + $" · best chain ×{q.Best}. "
                + (q.Time < avgRef - 0.0005 ? $"{(avgRef - q.Time).ToString("0.000", inv)} s quicker than an average lap." : q.Time > avgRef + 0.0005 ? $"{(q.Time - avgRef).ToString("0.000", inv)} s slower than an average lap." : "About an average lap.");
            var p = new MenuPage
            {
                Screen = CareerScreen.QualiDone, Eyebrow = "Qualifying results · " + t.Name, Title = $"You start P{q.Pos} · {q.Time.ToString("0.000", inv)}",
                Sub = how + (q.Card != null ? $" {CardBook.Get(q.Card).Name} starts the race in your discard pile." : ""), Money = Money(c.Money), Notice = Toast,
            };
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "quali.race", Label = "Go racing", Primary = true });
            p.Default = "quali.race";
            var g = p.Section("Starting grid", 0);
            var teams = c.SeriesTeams;
            for (int i = 0; i < q.Grid.Count; i++)
            {
                var e = c.Field.First(x => x.Id == q.Grid[i]);
                g.Row($"P{i + 1}. #{e.Number} {(e.IsMe ? c.Name + " (you)" : e.Name)}", $"{q.Times[i].ToString("0.000", inv)} · {teams[e.Team].Name}", e.IsMe ? Green : (Rgb?)null);
            }
            ObjectivesSection(p);
            return p;
        }

        MenuPage SeasonPage()
        {
            var c = Career; var rv = c.Review;
            var champ = rv.Champion;
            var p = new MenuPage
            {
                Screen = CareerScreen.Season, Eyebrow = $"Season {rv.Season} complete · {SeasonRules.SeriesName(rv.Series)}", Title = rv.Rank == 1 ? "Champion!" : $"P{rv.Rank} in the final standings",
                TitleColor = rv.Rank == 1 ? Amber : (Rgb?)null, Money = Money(c.Money), Notice = Toast,
                Sub = rv.Rank == 1 ? $"You win the title with {rv.Pts} points." : $"{champ.Name} ({champ.Team}) wins the title with {champ.Pts} points, {champ.Pts - rv.Pts} ahead of you.",
            };
            StandingsSection(p, 0, "Final standings", rv.Table);
            if (rv.Sponsor != null)
            {
                var sp = SeasonRules.Sponsor(rv.Sponsor);
                var s = p.Section("Sponsor review · " + sp.Name + (rv.Met ? " renews" : " walks"), 0, null, rv.Met ? Green : Red);
                s.Row("Season goal: " + rv.Goal, rv.Prog + " · " + (rv.Met ? "Met" : "Missed"), rv.Met ? Green : Red);
                s.Text(rv.Met ? $"Goal bonus paid: {Money(rv.Bonus)}. They offer to renew at {Money(rv.RenewPer)} a race with a {Money(rv.RenewBonus)} goal bonus; you'll see it with the other sponsor offers in the garage." : "No bonus. They won't sponsor you next season, so you'll pick from new offers in the garage.");
            }
            var res = p.Section("Results", 1);
            foreach (var r in c.Results.Where(r => r.Season == rv.Season)) res.Row($"{r.RaceNo}. {r.TrackName}", $"P{r.Finish}{(r.Dnf ? " (DNF)" : "")} from P{r.Start}, expected P{r.Expected} · {CareerRules.TierName(r.Tier)}", TierColor(r.Tier));
            var nx = SeasonRules.Next(rv.Series);
            int ups = c.Offers.Count(o => o.Kind == OfferKind.Promote);
            var off = p.Section("Contract offers for next season", 1, nx == null ? "Stay or move to a better team." : ups > 0 ? $"{ups} {SeasonRules.SeriesOne(nx.Value)} team{(ups > 1 ? "s" : "")} want{(ups > 1 ? "" : "s")} you." : $"No {SeasonRules.SeriesOne(nx.Value)} team called: the top 14 in points get step-up offers.", Amber);
            var teams = c.SeriesTeams;
            for (int i = 0; i < c.Offers.Count; i++)
            {
                var o = c.Offers[i];
                string what = o.Kind == OfferKind.Stay ? "Same car, same crew, same series."
                    : $"New car: power, handling and aero {o.Rating} (yours now {c.Base.Car.Power}/{c.Base.Car.Handling}/{c.Base.Car.Aero}). {FollowText(o.Rating)} You keep your driver coaching, money, cards and parts.";
                if (o.Kind == OfferKind.Promote) what += c.Series == Series.Kart ? " Kart cards turn into late model cards and your persona grows into its stock car self." : "";
                if (o.Kind == OfferKind.Promote) what += o.Series == Series.Late ? " Late models: pit stops and cautions, an 11 to 15 card deck." : " Stock cars: the full schedule, 12 to 16 cards.";
                string kind = o.Kind == OfferKind.Stay ? "Stay" : o.Kind == OfferKind.Move ? "Better team" : "Step up · " + SeasonRules.SeriesName(o.Series);
                off.Add(new MenuItem { Kind = MenuKind.Part, Id = "offer:" + i.ToString(inv), Label = $"{kind}: {o.Name}", Value = $"rating {o.Rating} · {Money(o.Per)} a race{(o.Sign > 0 ? " · " + Money(o.Sign) + " to sign" : "")}", Note = what, Color = o.Kind == OfferKind.Promote ? Amber : o.Kind == OfferKind.Move ? Green : (Rgb?)null });
                NegRows(off, o, i);
            }
            off.Text("Negotiate before you sign: one d20 Negotiate (Savvy) check per offer. A success raises the salary and signing bonus, a failure lowers them, a mixed result changes nothing.", Dim);
            int best = c.Offers.FindIndex(o => o.Kind == OfferKind.Promote);
            if (best < 0) best = c.Offers.FindIndex(o => o.Kind == OfferKind.Move);
            p.Default = "offer:" + Math.Max(0, best).ToString(inv);
            if (Saves != null && Slot > 0) p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "quit", Label = "Save and quit" });
            return p;
        }

        MenuPage OverPage()
        {
            var c = Career;
            var p = new MenuPage { Screen = CareerScreen.Over, Eyebrow = "Run over: permadeath", Title = c.Name, Sub = $"Out after {c.Results.Count} races · average finish P{(c.Results.Count > 0 ? c.Results.Average(r => r.Finish) : 0).ToString("0.0", inv)}", Money = Money(c.Money) };
            var s = p.Section("Results");
            foreach (var r in c.Results) s.Row($"{r.RaceNo}. {r.TrackName}", $"P{r.Finish}{(r.Dnf ? " (DNF)" : "")} from P{r.Start}, expected P{r.Expected} · {CareerRules.TierName(r.Tier)}", TierColor(r.Tier));
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "over.new", Label = "New career", Primary = true });
            return p;
        }
    }

    /// <summary>
    /// Walks the menu with a controller or the keyboard: up and down (or the stick) move a focus over every pressable control in
    /// reading order, A or Enter presses it, B or Esc backs out of the deck builder. The focus follows the control's id, so it
    /// stays put when a press rebuilds the page.
    /// </summary>
    public sealed class MenuFocus
    {
        public string Id;

        public MenuItem Current(MenuPage p)
        {
            var all = p.Pressables().ToList();
            if (all.Count == 0) return null;
            var hit = all.FirstOrDefault(i => i.Id == Id) ?? all.FirstOrDefault(i => i.Id == p.Default) ?? all[0];
            Id = hit.Id;
            return hit;
        }

        public void Move(MenuPage p, int dir)
        {
            var all = p.Pressables().ToList();
            if (all.Count == 0) { Id = null; return; }
            int i = all.FindIndex(x => x.Id == Id);
            if (i < 0) { Id = Current(p).Id; return; }
            i = ((i + dir) % all.Count + all.Count) % all.Count;
            Id = all[i].Id;
        }

        public bool Confirm(CareerMenu m)
        {
            var cur = Current(m.Page());
            return cur != null && m.Press(cur.Id);
        }

        /// <summary>B / Esc: leave the deck builder if the deck is legal, the driver sheet, the event (it waits in the garage),
        /// or qualifying's card pick for the garage.</summary>
        public bool Back(CareerMenu m) => m.Screen == CareerScreen.DeckBuilder ? m.Press("deck.done") : m.Screen == CareerScreen.Driver ? m.Press("drv.back")
            : m.Screen == CareerScreen.Event ? m.Press("ev.back") : m.Screen == CareerScreen.Quali && m.Press("quali.back");
    }
}
