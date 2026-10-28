using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GreenFlag.Sim;

namespace GreenFlag.View
{
    public enum CareerScreen { Title, Garage, DeckBuilder, Reward, Over }

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
    public sealed class CareerMenu
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
        public DifficultyLevel PickDiff = DifficultyLevel.Normal;
        public Stakes PickStakes = Stakes.Standard;
        public uint NewSeed = 1;

        // the deck builder's sort and family tab
        public string DeckSort = "fam";
        public CardFamily? DeckTab;

        public CareerMenu() { }
        public CareerMenu(Career c) { Career = c; Career.EnterGarage(); Screen = CareerScreen.Garage; }

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
            string arg = null;
            int colon = id.IndexOf(':');
            string key = id;
            if (colon > 0) { key = id.Substring(0, colon); arg = id.Substring(colon + 1); }
            var c = Career;
            switch (key)
            {
                // title
                case "title.persona": PickPersona = arg; return true;
                case "title.diff": PickDiff = (DifficultyLevel)Enum.Parse(typeof(DifficultyLevel), arg); return true;
                case "title.stakes": PickStakes = (Stakes)Enum.Parse(typeof(Stakes), arg); return true;
                case "title.start":
                    Career = Career.New(PickPersona, PickDiff, PickStakes, NewSeed);
                    Career.EnterGarage();
                    Screen = CareerScreen.Garage;
                    return true;
                // garage
                case "go":
                    snap = c.Clone(); snapSeed = c.RaceSeed;
                    RaceWanted = c.NextRace();
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
                    Screen = CareerScreen.Garage;
                    return true;
                case "over.new": Career = null; Screen = CareerScreen.Title; return true;
            }
            return false;
        }

        /// <summary>The engine started RaceWanted: clear it.</summary>
        public RaceSetup TakeRace() { var r = RaceWanted; RaceWanted = null; return r; }

        /// <summary>The race is over: pay out and show the reward screen.</summary>
        public RaceReport RaceFinished(Race race)
        {
            var res = Career.FinishRace(race);
            Screen = CareerScreen.Reward;
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
                default: return TitlePage();
            }
        }

        static string TuneLabel(Tune u, bool kart)
        {
            string d(int v, string lo, string hi) => v == 0 ? "Neutral" : (v < 0 ? lo : hi) + (Math.Abs(v) > 1 ? " ++" : " +");
            return $"{d(u.Hand, "Tight", "Loose")} · {d(u.Aero, "Downforce", "Low drag")} · {u.FocusName(kart)}";
        }

        MenuPage TitlePage()
        {
            var p = new MenuPage { Screen = CareerScreen.Title, Eyebrow = "New career", Title = "Green Flag Deck", Sub = "A racing career you drive with a hand of cards. Pick a persona: it sets your starting cards, one part and a default car tune. Card rewards and the garage can take you anywhere." };
            var kart = p.Section("Karts (the ladder starts here)", 0, "A kart persona starts in the kart series.");
            var stock = p.Section("Stock cars", 1, "Skip the ladder and start in stock cars.");
            foreach (var pr in Personas.All)
            {
                var part = Parts.Get(pr.Part);
                (pr.Kart ? kart : stock).Add(new MenuItem { Kind = MenuKind.Chip, Id = "title.persona:" + pr.Id, Label = pr.Name, Value = pr.Tag, On = PickPersona == pr.Id, Note = $"Part: {part.Name}. {part.Text} Tune: {TuneLabel(pr.Tune, pr.Kart)}. Cards: {string.Join(", ", pr.Add.Select(id => CardBook.Get(id).Name))}." });
            }
            var d = p.Section("AI skill", 0, "Harder fields pay less and cost more to reroll.");
            foreach (DifficultyLevel l in Enum.GetValues(typeof(DifficultyLevel)))
                d.Chip("title.diff:" + l, l.ToString(), PickDiff == l, note: $"Purse ×{Difficulty.For(l).Purse.ToString("0.##", inv)} · rerolls from {Money(CareerRules.RerollBase(l))}");
            var s = p.Section("Stakes", 1, "Harder stakes pay bigger purses.");
            s.Chip("title.stakes:Standard", "Standard", PickStakes == Stakes.Standard, note: "Retry any race from the results screen.");
            s.Chip("title.stakes:Ironman", "Ironman +10%", PickStakes == Stakes.Ironman, note: "No retries. A DNF costs $3,000 and a random spare part.");
            s.Chip("title.stakes:Permadeath", "Permadeath +20%", PickStakes == Stakes.Permadeath, note: "No retries. A DNF ends the run, but one Survive token turns the first DNF into an Ironman penalty.");
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "title.start", Label = "Start career", Primary = true });
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
            var nx = p.Section($"Next race · {c.RaceIdx + 1} of {c.SeasonLength}", 0, null, Green);
            nx.Row(t.Name, $"{t.Type} · {t.Laps} laps");
            if (why != null) nx.Text(why + ". Fix it in the deck builder.", Red);
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "go", Label = "Go racing", Enabled = why == null, Primary = true, Note = why });
            p.Default = why == null ? "go" : "deck";
            p.Actions.Add(new MenuItem { Kind = MenuKind.Button, Id = "deck", Label = "Build race deck" });

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
            pay.Row("Total", "+" + Money(res.Total), Green);
            pay.Row("Bank", Money(c.Money));
            if (res.Penalty == "survive") pay.Text($"Survive token used: the DNF would have ended your run. It still cost {Money(CareerRules.DnfFine)}{(res.LostPart != null ? " and your spare " + Parts.Get(res.LostPart).Name : "")}. No tokens left.", Amber);
            else if (res.Penalty == "ironman") pay.Text($"Ironman penalty: the DNF cost {Money(CareerRules.DnfFine)}{(res.LostPart != null ? " and your spare " + Parts.Get(res.LostPart).Name : "")}.", Red);
            else if (res.Penalty == "dead") pay.Text("Permadeath: the DNF ends the run.", Red);

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

        /// <summary>B / Esc: leave the deck builder if the deck is legal.</summary>
        public bool Back(CareerMenu m) => m.Screen == CareerScreen.DeckBuilder && m.Press("deck.done");
    }
}
