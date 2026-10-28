using System;
using System.Collections.Generic;
using System.Linq;

namespace GreenFlag.Sim
{
    // A career to and from save-file JSON (roadmap step 35). The layout is the save format: change it only together with
    // CareerSave.Version and a migration (docs/saves.md).
    public sealed partial class Career
    {
        /// <summary>
        /// Every field of the saved types, by type: the saved ones and the ones left out on purpose. SaveTests fails when a
        /// type gains a field that isn't on this list, so a new piece of career state can't silently miss the save file.
        /// </summary>
        public static readonly IReadOnlyDictionary<Type, string[]> SaveCoverage = new Dictionary<Type, string[]>
        {
            [typeof(Career)] = new[] { "Name", "Persona", "Series", "Diff", "Info", "Stakes", "Survive", "Money", "Season", "RaceIdx", "Over", "Base", "Collection", "Deck", "PartsOwned", "Equipped", "Tune", "Hist", "TrackDecks", "Results", "ShopPart", "ShopRerolls", "shopRolled", "Reward", "DeckNote", "deckFor", "Seed", "Rng",
                "Field", "Pts", "Contract", "Sponsor", "SponsorOffers", "Objectives", "RivalId", "Past", "Review", "Offers", "Ladder", "Qual", "QualRunning", "-ContractNote", "Cal",
                "Driver", "Event", "EventsHeld", "Staff", "Hire", "hireFor", "Poach", "Poached",
                "Class", "ClassRng", "Inbox", "msgN", "News", "-RivalsOn" },
            // Deck, Parts and Tune on the career's base loadout are always empty or unused: Loadout() fills them from the career
            [typeof(PlayerLoadout)] = new[] { "Car", "CrewChief", "Spotter", "Engineer", "PitCrew", "PitDeck", "Grit", "Wits", "Radio", "Crew", "-Deck", "-Parts", "-Tune", "-DriverMom" },
            [typeof(CarStats)] = new[] { "Power", "Handling", "Aero", "Durability", "Racecraft", "Defense", "Consistency", "TireMgmt" },
            [typeof(Tune)] = new[] { "Hand", "Aero", "Focus" },
            [typeof(CrewPerks)] = new[] { "LaneCut", "Chance", "TireOdds", "FuelOdds", "PitHand", "Prep", "ReleaseWindow", "BodyMen", "Chief", "Spotter", "Engineer", "ChanceAll", "MomAll", "SoftAll", "Rate", "Reroll", "Xp" },
            [typeof(CallFx)] = new[] { "Chance", "Late", "Pay", "Soft", "Weight", "Again", "Net", "FixOnS" },
            [typeof(CardHistory)] = new[] { "Plays", "Succ" },
            // a finished race's top 10 and play log are only shown on its reward screen, so older results keep the summary
            [typeof(RaceReport)] = new[] { "Season", "RaceNo", "TrackId", "TrackName", "Start", "Finish", "Expected", "Dnf", "Tier", "Mul", "Purse", "Bonus", "Total", "Plays", "Succ", "Crits", "BestChain", "Calls", "CallsWon", "Order", "Log", "Penalty", "LostPart",
                "ObjE", "BestPos", "BeatMate", "BeatRival", "Rival", "Objectives", "ObjMet", "TierFrom", "Sponsor", "Retainer", "Salary", "Pts", "PtsTotal", "Rank", "RankBefore",
                "Dmg", "DrvMom", "Hype", "Xp", "LvlUp", "HypeWhy", "Wages", "CrewXp", "CrewUp", "RvWreck", "RvPaid", "Paybacks", "InboxNew", "RvWeek", "Rivals" },
            [typeof(FinishLine)] = new[] { "Pos", "Id", "Name", "Team", "IsMe", "Dnf" },
            [typeof(RewardOffer)] = new[] { "Res", "Cards", "CardRerolls", "Picked", "InDeck", "Parts", "PartRerolls", "PartPicked" },
            // step 37: the season. Your own entrant's Stats are your car (Base.Car) and aren't written twice.
            // step 43: an entrant's Grudge is copied in from the class for each race
            [typeof(Entrant)] = new[] { "Id", "Team", "TeamRating", "Number", "IsMe", "Name", "Trait", "Stats", "Starts", "Wins", "SeasonWins", "Top5", "Best", "Rival", "-Grudge" },
            [typeof(ObjectiveResult)] = new[] { "Id", "Name", "Ask", "Ok", "Pay" },
            [typeof(RaceObjective)] = new[] { "Id", "Ask" },
            [typeof(SponsorDeal)] = new[] { "Id", "Per", "Bonus", "Renew", "Since", "Trust" },
            [typeof(Contract)] = new[] { "Per", "Team", "Since", "Series", "Clauses" },
            [typeof(ContractOffer)] = new[] { "Kind", "Series", "Team", "Rating", "Per", "Sign", "Name", "Neg" },
            [typeof(StandRow)] = new[] { "Pos", "Id", "Pts", "Wins", "Best", "Name", "Team", "IsMe" },
            [typeof(PastSeason)] = new[] { "Season", "Rank", "Pts", "Series", "Sponsor", "Met" },
            [typeof(SeasonReview)] = new[] { "Season", "Rank", "Pts", "Series", "Table", "Sponsor", "Goal", "Prog", "Met", "Bonus", "RenewPer", "RenewBonus" },
            [typeof(QualiResult)] = new[] { "Season", "RaceIdx", "Pos", "Best", "TrackId", "Card", "Grid", "Times", "Time", "Avg", "Skip", "Counts" },
            // step 38: the driver. A rolled check keeps its dice and its lines; its sums, odds and tier are worked out again on load.
            [typeof(Driver)] = new[] { "Bg", "Attr", "Prof", "Lvl", "Xp", "Pts", "Hype", "Fans", "Respect", "ImgX", "ImgY", "Trail", "Log", "NextMom" },
            [typeof(CheckLog)] = new[] { "Ev", "Opt", "Txt", "Skill", "Tier", "Season", "Race" },
            [typeof(OffTrackEvent)] = new[] { "Id", "Ri", "Season", "After", "Ctx", "Ceil", "Revealed", "AdvFor", "Hist", "Cur", "Done", "Walked" },
            [typeof(CheckCtx)] = new[] { "Ev", "Track", "TrackId", "Rival", "Fin", "Start", "Exp", "Dmg", "Dnf", "Top3", "Big", "BeatRival" },
            [typeof(EvHist)] = new[] { "Ri", "Opt", "Name", "Auto", "Tier", "Fx" },
            [typeof(EvPending)] = new[] { "Opt", "Name", "Say", "Hit", "Miss", "TX", "TY", "Hype", "Rerolled", "B", "R", "R0", "Tiers", "Info", "-Xp" },
            [typeof(CheckBuild)] = new[] { "Skill", "L", "Dcl", "Adv", "Dis", "BaseDc", "-Mod", "-Dc", "-Mode", "-Odds", "-Need" },
            [typeof(CheckRoll)] = new[] { "A", "B", "-Kept", "-Other", "-Total", "-Tier" },
            [typeof(ModLine)] = new[] { "V", "Src", "K" },
            [typeof(OffFx)] = new[] { "Fans", "Respect", "Hype", "Money", "Trust", "Per", "Bonus", "Mom", "Sal", "Clause", "Adv", "Reveal" },
            [typeof(NegResult)] = new[] { "Kept", "Other", "Mod", "Dc", "Total", "Tier", "Txt" },
            // step 39: the crew
            [typeof(CrewMember)] = new[] { "Role", "Name", "Lvl", "Xp", "Trait", "Wage", "Perks", "Since", "Kept" },
            [typeof(StaffCandidate)] = new[] { "Role", "Name", "Trait", "Lvl", "Fee", "Wage" },
            [typeof(PoachOffer)] = new[] { "Role", "Name", "Team", "Rating", "TeamName", "Was", "Wage", "MatchAt", "Buyout", "Season", "After", "Talk", "Done", "Outcome" },
            // step 43: the class of rivals
            [typeof(ClassDriver)] = new[] { "Id", "Series", "Team", "Skill", "G", "Ahead", "Behind", "Starts", "Wins", "Titles", "Seasons", "LastRank", "Retired", "Met", "Hist", "Path" },
            [typeof(RivalHist)] = new[] { "Season", "Race", "Lap", "Kind", "Track" },
            [typeof(RivalMsg)] = new[] { "Id", "Rid", "Kind", "Track", "Subj", "Body", "Season", "Race", "G", "Reply", "ReplyText", "Tier", "Roll" },
            [typeof(RivalLine)] = new[] { "Rid", "Name", "G0", "G1", "Week", "Fin", "Why" },
        };

        public JsonValue ToJson()
        {
            var j = JsonValue.Obj()
                .Set("name", Name).Set("persona", Persona).Set("series", Series.ToString())
                .Set("diff", Diff.ToString()).Set("info", Info.ToString()).Set("stakes", Stakes.ToString()).Set("survive", Survive)
                .Set("money", Money).Set("season", Season).Set("raceIdx", RaceIdx).Set("over", Over)
                .Set("seed", (long)Seed).Set("rng", (long)Rng.State)
                .Set("base", LoadoutJson(Base))
                .Set("collection", JsonValue.Of(Collection)).Set("deck", JsonValue.Of(Deck)).Set("partsOwned", JsonValue.Of(PartsOwned));
            var eq = JsonValue.Obj();
            foreach (PartSlot s in Enum.GetValues(typeof(PartSlot))) if (Equipped.TryGetValue(s, out var id)) eq.Set(s.ToString(), id);
            j.Set("equipped", eq).Set("tune", TuneJson(Tune));
            var hist = JsonValue.Obj();
            foreach (var kv in Hist.OrderBy(kv => kv.Key, StringComparer.Ordinal)) hist.Set(kv.Key, JsonValue.Arr().Add(JsonValue.Of(kv.Value.Plays)).Add(JsonValue.Of(kv.Value.Succ)));
            j.Set("hist", hist);
            var td = JsonValue.Obj();
            foreach (var kv in TrackDecks.OrderBy(kv => kv.Key, StringComparer.Ordinal)) td.Set(kv.Key, JsonValue.Of(kv.Value));
            j.Set("trackDecks", td);
            var res = JsonValue.Arr();
            foreach (var r in Results) res.Add(ReportJson(r, false));
            j.Set("results", res)
                .Set("shop", JsonValue.Obj().Set("rolled", shopRolled).Set("part", ShopPart).Set("rerolls", ShopRerolls))
                .Set("deckFor", deckFor).Set("deckNote", DeckNote)
                .Set("reward", Reward == null ? JsonValue.Null : RewardJson(Reward));
            SeasonToJson(j);
            DriverToJson(j);
            CrewToJson(j);
            RivalsToJson(j);
            return j;
        }

        /// <summary>A career from save JSON in the current format (CareerSave migrates older ones first). Throws FormatException
        /// when the file is broken or names a card, part, persona or track this build doesn't have.</summary>
        public static Career FromJson(JsonValue j)
        {
            try
            {
                var c = new Career
                {
                    Name = j["name"].Str("Rookie"), Persona = j["persona"].Str(), Series = j["series"].Enum(Series.Stock),
                    Diff = j["diff"].Enum(DifficultyLevel.Normal), Info = j["info"].Enum(InfoLevel.Full), Stakes = j["stakes"].Enum(Stakes.Standard),
                    Survive = j["survive"].Int(), Money = j["money"].Int(), Season = j["season"].Int(1), RaceIdx = j["raceIdx"].Int(), Over = j["over"].Bool(),
                    Seed = j["seed"].UInt(), Rng = new SimRng(j["rng"].UInt()), Base = LoadoutFrom(j["base"]),
                    Collection = j["collection"].Strings(), Deck = j["deck"].Strings(), PartsOwned = j["partsOwned"].Strings(), Tune = TuneFrom(j["tune"]),
                };
                Personas.Get(c.Persona);
                foreach (var kv in j["equipped"].Fields)
                {
                    var slot = JsonValue.Of(kv.Key).Enum(PartSlot.Engine);
                    var id = kv.Value.Str();
                    if (id != null && (!c.PartsOwned.Contains(id) || Parts.Get(id).Slot != slot)) throw new FormatException($"Save file: {id} is fitted in the {slot} slot but isn't an owned {slot} part");
                    c.Equipped[slot] = id;
                }
                foreach (var kv in j["hist"].Fields) c.Hist[kv.Key] = new CardHistory { Plays = kv.Value[0].Int(), Succ = kv.Value[1].Int() };
                foreach (var kv in j["trackDecks"].Fields) { Tracks.ById(kv.Key); c.TrackDecks[kv.Key] = kv.Value.Strings(); }
                foreach (var r in j["results"].Items) c.Results.Add(ReportFrom(r));
                var shop = j["shop"];
                c.shopRolled = shop["rolled"].Bool(); c.ShopPart = shop["part"].Str(); c.ShopRerolls = shop["rerolls"].Int();
                c.deckFor = j["deckFor"].Int(-1); c.DeckNote = j["deckNote"].Str();
                if (!j["reward"].IsNull)
                {
                    c.Reward = RewardFrom(j["reward"]);
                    // the reward's race is the last result: share the one object, as FinishRace does
                    if (c.Results.Count > 0) c.Results[c.Results.Count - 1] = c.Reward.Res;
                    else c.Results.Add(c.Reward.Res);
                }
                c.SeasonFromJson(j);
                c.DriverFromJson(j);
                c.CrewFromJson(j);
                c.RivalsFromJson(j);
                c.Check();
                return c;
            }
            catch (KeyNotFoundException e) { throw new FormatException("Save file: " + e.Message, e); }
        }

        /// <summary>Every id the career names exists in this build, and the race it is on is on its calendar.</summary>
        void Check()
        {
            foreach (var id in Collection.Concat(Deck).Concat(Hist.Keys).Concat(TrackDecks.Values.SelectMany(d => d))) CardBook.Get(id);
            foreach (var id in PartsOwned) Parts.Get(id);
            if (ShopPart != null) Parts.Get(ShopPart);
            foreach (var id in Base.PitDeck) PitCards.Get(id);
            if (Reward != null)
            {
                foreach (var id in Reward.Cards) CardBook.Get(id);
                if (Reward.Parts != null) foreach (var id in Reward.Parts) Parts.Get(id);
            }
            if (Season < 1 || RaceIdx < 0 || RaceIdx >= SeasonLength) throw new FormatException($"Save file: race {RaceIdx + 1} of season {Season} isn't on the {Series} calendar");
            if (Money < 0) throw new FormatException("Save file: negative money");
            if (Field.Count(e => e.IsMe) != 1) throw new FormatException("Save file: the field needs exactly one car that is you");
            if (Field.Select(e => e.Id).Distinct().Count() != Field.Count) throw new FormatException("Save file: two cars in the field share an id");
            foreach (var id in Pts.Keys) if (!Field.Any(e => e.Id == id)) throw new FormatException($"Save file: points for car {id}, which isn't in the field");
            if (Contract.Team < 0 || Contract.Team >= Teams.For(Series).Length) throw new FormatException($"Save file: no team {Contract.Team}");
            if (Sponsor != null) SeasonRules.Sponsor(Sponsor.Id);
            if (SponsorOffers != null) foreach (var o in SponsorOffers) SeasonRules.Sponsor(o.Id);
            if (Objectives != null) foreach (var o in Objectives) SeasonRules.Objective(o.Id);
            if (Qual != null && Qual.Card != null) CardBook.Get(Qual.Card);
        }

        static JsonValue TuneJson(Tune u) => JsonValue.Obj().Set("hand", u.Hand).Set("aero", u.Aero).Set("focus", u.Focus.ToString());
        static Tune TuneFrom(JsonValue j) => new Tune { Hand = Math.Max(-2, Math.Min(2, j["hand"].Int())), Aero = Math.Max(-2, Math.Min(2, j["aero"].Int())), Focus = j["focus"].Enum(ComboFocus.None) };

        static JsonValue LoadoutJson(PlayerLoadout l)
        {
            var c = l.Car;
            var car = JsonValue.Obj().Set("power", c.Power).Set("handling", c.Handling).Set("aero", c.Aero).Set("durability", c.Durability)
                .Set("racecraft", c.Racecraft).Set("defense", c.Defense).Set("consistency", c.Consistency).Set("tireMgmt", c.TireMgmt);
            var p = l.Crew;
            var crew = JsonValue.Obj().Set("laneCut", p.LaneCut).Set("chance", p.Chance).Set("tireOdds", p.TireOdds).Set("fuelOdds", p.FuelOdds)
                .Set("pitHand", p.PitHand).Set("prep", p.Prep).Set("releaseWindow", p.ReleaseWindow).Set("bodyMen", p.BodyMen)
                .Set("chief", FxJson(p.Chief)).Set("spotter", FxJson(p.Spotter)).Set("engineer", FxJson(p.Engineer))
                .Set("chanceAll", p.ChanceAll).Set("momAll", p.MomAll).Set("softAll", p.SoftAll).Set("rate", p.Rate).Set("reroll", p.Reroll).Set("xp", p.Xp);
            return JsonValue.Obj().Set("car", car).Set("crewChief", l.CrewChief).Set("spotter", l.Spotter).Set("engineer", l.Engineer).Set("pitCrew", l.PitCrew)
                .Set("pitDeck", JsonValue.Of(l.PitDeck)).Set("grit", l.Grit).Set("wits", l.Wits).Set("radio", l.Radio).Set("crew", crew);
        }

        static PlayerLoadout LoadoutFrom(JsonValue j)
        {
            var c = j["car"];
            var l = new PlayerLoadout
            {
                Car = new CarStats { Power = c["power"].Int(), Handling = c["handling"].Int(), Aero = c["aero"].Int(), Durability = c["durability"].Int(), Racecraft = c["racecraft"].Int(), Defense = c["defense"].Int(), Consistency = c["consistency"].Int(), TireMgmt = c["tireMgmt"].Int() },
                CrewChief = j["crewChief"].Int(3), Spotter = j["spotter"].Int(3), Engineer = j["engineer"].Int(3), PitCrew = j["pitCrew"].Int(),
                PitDeck = j["pitDeck"].Strings(), Grit = j["grit"].Int(10), Wits = j["wits"].Int(10), Radio = j["radio"].Bool(true),
            };
            var p = j["crew"];
            var k = l.Crew;
            k.LaneCut = p["laneCut"].Double(); k.Chance = p["chance"].Int(); k.TireOdds = p["tireOdds"].Int(); k.FuelOdds = p["fuelOdds"].Int();
            k.PitHand = p["pitHand"].Int(); k.Prep = p["prep"].Int(); k.ReleaseWindow = p["releaseWindow"].Double(); k.BodyMen = p["bodyMen"].Bool();
            FxFrom(p["chief"], k.Chief); FxFrom(p["spotter"], k.Spotter); FxFrom(p["engineer"], k.Engineer);
            k.ChanceAll = p["chanceAll"].Int(); k.MomAll = p["momAll"].Int(); k.SoftAll = p["softAll"].Double(1); k.Rate = p["rate"].Double(1); k.Reroll = p["reroll"].Bool(); k.Xp = p["xp"].Double();
            return l;
        }

        static JsonValue FxJson(CallFx f) => JsonValue.Obj().Set("chance", f.Chance).Set("late", f.Late).Set("pay", f.Pay).Set("soft", f.Soft)
            .Set("weight", f.Weight).Set("again", f.Again).Set("net", f.Net).Set("fixOnS", f.FixOnS);

        static void FxFrom(JsonValue j, CallFx f)
        {
            f.Chance = j["chance"].Int(); f.Late = j["late"].Int(); f.Pay = j["pay"].Double(); f.Soft = j["soft"].Double(1);
            f.Weight = j["weight"].Double(1); f.Again = j["again"].Double(); f.Net = j["net"].Double(); f.FixOnS = j["fixOnS"].Double();
        }

        static JsonValue ReportJson(RaceReport r, bool full)
        {
            var j = JsonValue.Obj().Set("season", r.Season).Set("raceNo", r.RaceNo).Set("track", r.TrackId).Set("trackName", r.TrackName)
                .Set("start", r.Start).Set("finish", r.Finish).Set("expected", r.Expected).Set("dnf", r.Dnf).Set("tier", r.Tier.ToString())
                .Set("mul", r.Mul).Set("purse", r.Purse).Set("bonus", r.Bonus).Set("total", r.Total)
                .Set("plays", r.Plays).Set("succ", r.Succ).Set("crits", r.Crits).Set("bestChain", r.BestChain).Set("calls", r.Calls).Set("callsWon", r.CallsWon)
                .Set("penalty", r.Penalty).Set("lostPart", r.LostPart)
                .Set("objE", r.ObjE).Set("bestPos", r.BestPos).Set("beatMate", r.BeatMate).Set("beatRival", r.BeatRival).Set("rival", r.Rival)
                .Set("objMet", r.ObjMet).Set("tierFrom", r.TierFrom?.ToString()).Set("sponsor", r.Sponsor).Set("retainer", r.Retainer).Set("salary", r.Salary)
                .Set("pts", r.Pts).Set("ptsTotal", r.PtsTotal).Set("rank", r.Rank).Set("rankBefore", r.RankBefore)
                .Set("dmg", r.Dmg).Set("drvMom", r.DrvMom).Set("hype", r.Hype).Set("hypeWhy", JsonValue.Of(r.HypeWhy)).Set("xp", r.Xp).Set("lvlUp", r.LvlUp)
                .Set("wages", r.Wages).Set("crewXp", CrewXpJson(r.CrewXp)).Set("crewUp", JsonValue.Of(r.CrewUp.Select(x => x.ToString())))
                .Set("rvWreck", r.RvWreck).Set("rvPaid", r.RvPaid).Set("paybacks", r.Paybacks).Set("inboxNew", r.InboxNew).Set("rvWeek", r.RvWeek);
            var objs = JsonValue.Arr();
            foreach (var o in r.Objectives) objs.Add(JsonValue.Obj().Set("id", o.Id).Set("name", o.Name).Set("ask", o.Ask).Set("ok", o.Ok).Set("pay", o.Pay));
            j.Set("objectives", objs);
            if (!full) return j;
            j.Set("rivals", RivalLinesJson(r.Rivals));
            var order = JsonValue.Arr();
            foreach (var o in r.Order) order.Add(JsonValue.Obj().Set("pos", o.Pos).Set("id", o.Id).Set("name", o.Name).Set("team", o.Team).Set("me", o.IsMe).Set("dnf", o.Dnf));
            var log = JsonValue.Arr();
            foreach (var l in r.Log)
            {
                var tags = JsonValue.Arr();
                foreach (var t in l.Tags) tags.Add(JsonValue.Arr().Add(JsonValue.Of(t.Value)).Add(JsonValue.Of(t.Label)).Add(JsonValue.Of(t.Kind)));
                log.Add(JsonValue.Obj().Set("lap", l.Lap).Set("card", l.Card).Set("succ", l.Succ).Set("tier", l.Tier.ToString()).Set("tags", tags));
            }
            return j.Set("order", order).Set("log", log);
        }

        static RaceReport ReportFrom(JsonValue j)
        {
            var r = new RaceReport
            {
                Season = j["season"].Int(), RaceNo = j["raceNo"].Int(), TrackId = j["track"].Str(), TrackName = j["trackName"].Str(),
                Start = j["start"].Int(), Finish = j["finish"].Int(), Expected = j["expected"].Int(), Dnf = j["dnf"].Bool(), Tier = j["tier"].Enum(RewardTier.Solid),
                Mul = j["mul"].Double(), Purse = j["purse"].Int(), Bonus = j["bonus"].Int(), Total = j["total"].Int(),
                Plays = j["plays"].Int(), Succ = j["succ"].Int(), Crits = j["crits"].Int(), BestChain = j["bestChain"].Int(), Calls = j["calls"].Int(), CallsWon = j["callsWon"].Int(),
                Penalty = j["penalty"].Str(), LostPart = j["lostPart"].Str(),
                ObjE = j["objE"].Int(), BestPos = j["bestPos"].Int(99), BeatMate = j["beatMate"].Bool(), BeatRival = j["beatRival"].Bool(), Rival = j["rival"].Str(),
                ObjMet = j["objMet"].Int(), TierFrom = j["tierFrom"].IsNull ? (RewardTier?)null : j["tierFrom"].Enum(RewardTier.Solid), Sponsor = j["sponsor"].Str(),
                Retainer = j["retainer"].Int(), Salary = j["salary"].Int(), Pts = j["pts"].Int(), PtsTotal = j["ptsTotal"].Int(), Rank = j["rank"].Int(), RankBefore = j["rankBefore"].Int(),
                Dmg = j["dmg"].Int(), DrvMom = j["drvMom"].Int(), Hype = j["hype"].Int(), HypeWhy = j["hypeWhy"].Strings(), Xp = j["xp"].Int(), LvlUp = j["lvlUp"].Int(),
                Wages = j["wages"].Int(), CrewXp = CrewXpFrom(j["crewXp"]), CrewUp = j["crewUp"].IsNull ? new List<CrewRole>() : j["crewUp"].Strings().Select(s => JsonValue.Of(s).Enum(CrewRole.CrewChief)).ToList(),
                RvWreck = j["rvWreck"].Bool(), RvPaid = j["rvPaid"].Bool(), Paybacks = j["paybacks"].Int(), InboxNew = j["inboxNew"].Int(), RvWeek = j["rvWeek"].Str(), Rivals = RivalLinesFrom(j["rivals"]),
            };
            foreach (var o in j["objectives"].Items)
                r.Objectives.Add(new ObjectiveResult { Id = o["id"].Str(), Name = o["name"].Str(""), Ask = o["ask"].Bool(), Ok = o["ok"].Bool(), Pay = o["pay"].Int() });
            foreach (var o in j["order"].Items)
                r.Order.Add(new FinishLine { Pos = o["pos"].Int(), Id = o["id"].Int(), Name = o["name"].Str(), Team = o["team"].Str(""), IsMe = o["me"].Bool(), Dnf = o["dnf"].Bool() });
            foreach (var l in j["log"].Items)
            {
                var tags = l["tags"].Items.Select(t => new OddsTag(t[0].Double(), t[1].Str(), t[2].Str())).ToArray();
                var card = l["card"].Str();
                CardBook.Get(card);
                r.Log.Add(new PlayLogEntry(l["lap"].Int(), card, l["succ"].Double(), l["tier"].Enum(Tier.Neutral), tags));
            }
            return r;
        }

        static JsonValue RewardJson(RewardOffer R) => JsonValue.Obj()
            .Set("res", ReportJson(R.Res, true)).Set("cards", JsonValue.Of(R.Cards)).Set("cardRerolls", R.CardRerolls).Set("picked", R.Picked).Set("inDeck", R.InDeck)
            .Set("parts", JsonValue.Of(R.Parts)).Set("partRerolls", R.PartRerolls).Set("partPicked", R.PartPicked);

        static RewardOffer RewardFrom(JsonValue j) => new RewardOffer
        {
            Res = ReportFrom(j["res"]), Cards = j["cards"].Strings(), CardRerolls = j["cardRerolls"].Int(), Picked = j["picked"].Str(), InDeck = j["inDeck"].Bool(),
            Parts = j["parts"].IsNull ? null : j["parts"].Strings(), PartRerolls = j["partRerolls"].Int(), PartPicked = j["partPicked"].Str(),
        };
    }
}
