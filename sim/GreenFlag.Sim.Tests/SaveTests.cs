using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GreenFlag.Sim;
using GreenFlag.View;
using Xunit;

namespace GreenFlag.Sim.Tests
{
    /// <summary>Step 35: the save format, its migrations, the three slots, autosave checkpoints, and old saves loading in a
    /// newer build (every committed sample in Saves/).</summary>
    public class SaveTests
    {
        static readonly DateTime Pinned = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        static SaveSlots Slots(ISaveStore store = null) => new SaveSlots(store ?? new MemorySaveStore()) { Now = () => Pinned, Build = "test" };

        /// <summary>A quick race with no HUD (CareerTests.RunRace), so careers of several races stay fast.</summary>
        static Race Quick(RaceSetup s)
        {
            s.PitPolicy = PitPolicy.Bot;
            var r = new Race(s);
            double next = 4;
            for (int i = 0; i < 200000 && !r.Done; i++)
            {
                r.Tick();
                if (r.T >= next && r.Attempt == null && r.Hand.Count > 0)
                {
                    next = r.T + 4;
                    foreach (var h in r.Hand.ToList()) if (r.PlayCard(h.Uid) == null) break;
                }
            }
            Assert.True(r.Done);
            return r;
        }

        static string Text(CareerMenu m) => CareerSave.Write(new SaveGame { Career = m.Career, Screen = m.Snapshot().Screen, SavedAt = Pinned, Build = "test" });

        // ================================================================== JSON

        [Fact]
        public void JsonRoundTripsStringsNumbersAndOrder()
        {
            var j = JsonValue.Obj().Set("z", "quote \" slash \\ tab \t line \n ctrl \u0001 é ✓").Set("a", 0.1 + 0.2).Set("big", 4294967295L).Set("neg", -7)
                .Set("list", JsonValue.Arr().Add(JsonValue.Of("x")).Add(JsonValue.Null).Add(JsonValue.Of(true)))
                .Set("nest", JsonValue.Arr().Add(JsonValue.Obj().Set("k", 1.5e-9)));
            var text = j.Write();
            var back = JsonValue.Parse(text);
            Assert.Equal(text, back.Write());
            Assert.Equal(j.Write(true), back.Write(true));
            Assert.Equal("quote \" slash \\ tab \t line \n ctrl \u0001 é ✓", back["z"].Str());
            Assert.Equal(0.1 + 0.2, back["a"].Double());
            Assert.Equal(4294967295u, back["big"].UInt());
            Assert.Equal(1.5e-9, back["nest"][0]["k"].Double());
            Assert.Equal(new[] { "z", "a", "big", "neg", "list", "nest" }, back.Fields.Select(f => f.Key));
            Assert.True(back["missing"].IsNull);
            Assert.Equal(5, back["missing"].Int(5));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\":1,}")]
        [InlineData("[1 2]")]
        [InlineData("\"open")]
        [InlineData("{\"a\":1} x")]
        [InlineData("01x")]
        [InlineData("{\"a\":\"\\q\"}")]
        public void JsonRejectsBrokenText(string t) => Assert.Throws<FormatException>(() => JsonValue.Parse(t));

        // ================================================================== the format

        /// <summary>Every field of every saved type is either written or listed as left out on purpose: a new piece of career
        /// state fails this test until someone decides where it goes in the save (and bumps the version if the layout changes).</summary>
        [Fact]
        public void EveryCareerFieldIsCovered()
        {
            foreach (var kv in Career.SaveCoverage)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | (kv.Key == typeof(Career) ? BindingFlags.NonPublic : 0);
                var fields = kv.Key.GetFields(flags).Where(f => !f.Name.Contains("k__BackingField")).Select(f => f.Name).ToList();
                var listed = kv.Value.Select(n => n.TrimStart('-')).ToList();
                foreach (var f in fields) Assert.True(listed.Contains(f), $"{kv.Key.Name}.{f} is not in Career.SaveCoverage: save it (and bump CareerSave.Version) or list it as \"-{f}\"");
                foreach (var f in listed) Assert.True(fields.Contains(f), $"Career.SaveCoverage lists {kv.Key.Name}.{f}, which no longer exists");
            }
        }

        [Fact]
        public void ACareerMidSeasonRoundTripsByteForByte()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "drafter", NewSeed = 5 }, 3, Quick);
            var text = Text(m);
            var g = CareerSave.Read(text);
            Assert.Equal(text, CareerSave.Write(g));
            var c = g.Career;
            Assert.Equal(m.Career.Money, c.Money);
            Assert.Equal(m.Career.RaceIdx, c.RaceIdx);
            Assert.Equal(m.Career.Rng.State, c.Rng.State);
            Assert.Equal(m.Career.Collection, c.Collection);
            Assert.Equal(m.Career.Deck, c.Deck);
            Assert.Equal(m.Career.ShopPart, c.ShopPart);
            Assert.Equal(m.Career.Results.Select(r => r.Finish), c.Results.Select(r => r.Finish));
            // the next race is the same race
            Assert.Equal(m.Career.RaceSeed, c.RaceSeed);
            Assert.Equal(Quick(m.Career.NextRace()).Result().MyFinish, Quick(c.NextRace()).Result().MyFinish);
        }

        [Fact]
        public void TheRewardScreenSavesWithItsOffersLogAndRetry()
        {
            var slots = Slots();
            var m = new CareerMenu(slots) { PickPersona = "brawler", NewSeed = 9 };
            Assert.True(m.Press("title.start"));
            var bot = new MenuBot();
            while (m.RaceWanted == null) Assert.True(bot.Step(m));
            m.RaceFinished(Quick(m.TakeRace()));
            Assert.Equal(CareerScreen.Reward, m.Screen);

            var n = new CareerMenu(slots);
            Assert.True(n.Load(1));
            Assert.Equal(CareerScreen.Reward, n.Screen);
            var a = m.Career.Reward; var b = n.Career.Reward;
            Assert.Equal(a.Cards, b.Cards);
            Assert.Equal(a.Parts, b.Parts);
            Assert.Equal(a.Res.Log.Count, b.Res.Log.Count);
            Assert.Equal(a.Res.Order.Select(o => o.Name), b.Res.Order.Select(o => o.Name));
            Assert.Same(n.Career.Reward.Res, n.Career.Results.Last());
            // Standard stakes: Retry this race still works after the reload, and reruns from the same pre-race career
            Assert.True(n.Page().CanPress("reward.retry"));
            Assert.True(m.Press("reward.retry")); Assert.True(n.Press("reward.retry"));
            Assert.Equal(m.RaceWanted.Seed, n.RaceWanted.Seed);
            Assert.Equal(m.Career.Money, n.Career.Money);
        }

        [Fact]
        public void RerollsOnTheRewardScreenCantBeUndoneByQuitting()
        {
            var slots = Slots();
            var m = new CareerMenu(slots) { PickPersona = "drafter", NewSeed = 3 };
            m.Press("title.start");
            m.Career.Money = 50000;
            var bot = new MenuBot();
            while (m.RaceWanted == null) Assert.True(bot.Step(m));
            m.RaceFinished(Quick(m.TakeRace()));
            var first = m.Career.Reward.Cards.ToList();
            Assert.True(m.Press("reward.reroll"));
            var rolled = m.Career.Reward.Cards.ToList();
            // the game is killed right after the reroll, without a flush
            var n = new CareerMenu(slots);
            n.Load(1);
            Assert.Equal(rolled, n.Career.Reward.Cards);
            Assert.Equal(1, n.Career.Reward.CardRerolls);
            Assert.Equal(m.Career.Money, n.Career.Money);
            Assert.NotNull(first);
        }

        /// <summary>The career plays out the same whether or not the game is closed and reopened after every single press and
        /// race: nothing that matters lives outside the save.</summary>
        [Theory]
        [InlineData("drafter", 4u, Stakes.Standard, 5)]
        [InlineData("brawler", 8u, Stakes.Ironman, 7)]
        [InlineData("bully", 2u, Stakes.Permadeath, 4)]
        public void ReloadingAfterEveryPressChangesNothing(string persona, uint seed, Stakes stakes, int races)
        {
            var plain = MenuBot.Run(new CareerMenu(Slots()) { PickPersona = persona, NewSeed = seed, PickStakes = stakes }, races, Quick);
            int reloads = 0;
            var slots = Slots();
            var re = MenuBot.RunReloading(new CareerMenu(slots) { PickPersona = persona, NewSeed = seed, PickStakes = stakes }, races, Quick, null, _ => reloads++);
            Assert.True(reloads > re.Career.Results.Count * 3, $"only {reloads} reloads");
            Assert.Equal(plain.Career.Results.Count, re.Career.Results.Count);
            Assert.Equal(Text(plain), Text(re));
        }

        [Fact]
        public void GarageChangesWaitForTheNextCheckpointOrAFlush()
        {
            var slots = Slots();
            var m = new CareerMenu(slots) { PickPersona = "drafter", NewSeed = 6 };
            m.Press("title.start");
            int saves = slots.Saves;
            Assert.True(m.Press("tune.aero:2"));
            Assert.True(m.Dirty);
            Assert.Equal(saves, slots.Saves);
            Assert.Equal(0, slots.Load(1).Career.Tune.Aero == 2 ? 1 : 0);
            Assert.True(m.Flush());
            Assert.False(m.Dirty);
            Assert.Equal(2, slots.Load(1).Career.Tune.Aero);
            Assert.False(m.Flush());
            // Go racing always writes, and so does every qualifying press; the save is the garage before the race (with the
            // grid from qualifying): quitting mid-race restarts it
            Assert.True(m.Press("sponsor:" + m.Career.SponsorOffers[0].Id));
            Assert.True(m.Press("go"));
            Assert.False(m.Dirty);
            Assert.True(m.Press("quali.skip"));
            Assert.True(m.Press("quali.race"));
            var g = slots.Load(1);
            Assert.True(g.Career.QualiDone);
            Assert.Equal(m.RaceWanted.Grid, g.Career.NextRace().Grid);
            Assert.Equal(SaveGame.Garage, g.Screen);
            Assert.Equal(0, g.Career.Results.Count);
            Assert.Equal(m.RaceWanted.Seed, g.Career.NextRace().Seed);
        }

        // ================================================================== slots

        [Fact]
        public void ThreeSlotsContinueReplaceAndDelete()
        {
            var slots = Slots();
            var m = new CareerMenu(slots) { PickPersona = "drafter" };
            Assert.False(m.Page().CanPress("title.continue"));
            Assert.Equal(1, m.PickSlot);
            m.Press("title.start");
            m.Press("quit");
            Assert.Equal(CareerScreen.Title, m.Screen);
            Assert.Equal(2, m.PickSlot);                       // the next empty slot
            m.PickPersona = "brawler";
            m.Press("title.start");
            m.Press("quit");
            Assert.Equal(3, m.PickSlot);
            Assert.True(m.Page().CanPress("title.continue"));
            var all = slots.All();
            Assert.Equal(new[] { SlotState.Ok, SlotState.Ok, SlotState.Empty }, all.Select(i => i.State));
            Assert.Contains("Brawler", CareerMenu.SlotLine(all[1]));

            // a newer save time wins Continue
            slots.Now = () => Pinned.AddHours(1);
            m.Press("title.load:1"); m.Press("quit");
            Assert.Equal(1, slots.MostRecent());
            Assert.True(m.Press("title.continue"));
            Assert.Equal("drafter", m.Career.Persona);
            m.Press("quit");

            // replacing a used slot takes two presses
            m.Press("title.slot:2");
            m.PickPersona = "bully";
            Assert.True(m.Press("title.start"));
            Assert.Equal(CareerScreen.Title, m.Screen);
            Assert.Contains("already has a career", m.Toast);
            Assert.True(m.Press("title.start"));
            Assert.Equal(CareerScreen.Garage, m.Screen);
            Assert.Equal("bully", slots.Load(2).Career.Persona);
            m.Press("quit");

            // so does deleting, and any other press in between disarms it
            Assert.True(m.Press("title.delete:1"));
            Assert.True(m.Press("title.slot:3"));
            Assert.True(m.Press("title.delete:1"));
            Assert.Equal(SlotState.Ok, slots.Info(1).State);
            Assert.True(m.Press("title.delete:1"));
            Assert.Equal(SlotState.Empty, slots.Info(1).State);
            Assert.False(m.Page().CanPress("title.load:1"));
            Assert.False(m.Page().CanPress("title.delete:1"));
        }

        [Fact]
        public void ABrokenSaveFallsBackToTheOneBeforeIt()
        {
            var store = new MemorySaveStore();
            var slots = Slots(store);
            var m = new CareerMenu(slots) { PickPersona = "drafter" };
            m.Press("title.start");
            m.Career.Money = 1234; m.SaveNow();
            m.Career.Money = 5678; m.SaveNow();
            store.Files["career-1.json"] = store.Files["career-1.json"].Substring(0, 300); // a write cut off half way
            var i = slots.Info(1);
            Assert.Equal(SlotState.Ok, i.State);
            Assert.True(i.FromBackup);
            Assert.Equal(1234, i.Game.Career.Money);
            var n = new CareerMenu(slots);
            Assert.True(n.Load(1));
            Assert.Contains("damaged", n.Toast);

            store.Files["career-1.json.bak"] = "{\"format\":\"green-flag-deck-career\",\"v\":1,\"career\":{\"persona\":\"nobody\"}}";
            i = slots.Info(1);
            Assert.Equal(SlotState.Broken, i.State);
            Assert.Contains("Damaged", CareerMenu.SlotLine(i));
            Assert.False(new CareerMenu(slots).Load(1));
        }

        [Fact]
        public void ASaveFromANewerBuildIsShownButNeverLoadedOrOverwritten()
        {
            var store = new MemorySaveStore();
            var slots = Slots(store);
            var m = new CareerMenu(slots) { PickPersona = "drafter" };
            m.Press("title.start");
            var newer = store.Files["career-1.json"].Replace($"\"v\": {CareerSave.Version},", $"\"v\": {CareerSave.Version + 1},");
            store.Files["career-1.json"] = newer;
            Assert.Equal(SlotState.TooNew, slots.Info(1).State);
            Assert.Throws<SaveTooNewException>(() => CareerSave.Read(newer));
            Assert.False(m.SaveNow());
            Assert.Contains("newer version", m.Toast);
            Assert.Equal(newer, store.Files["career-1.json"]);
            var t = new CareerMenu(slots);
            Assert.False(t.Page().CanPress("title.load:1"));
            Assert.False(t.Page().CanPress("title.slot:1"));
        }

        [Fact]
        public void SavesThatNameThingsThisBuildDoesntHaveAreRejected()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "drafter", NewSeed = 2 }, 1, Quick);
            var text = Text(m);
            var card = m.Career.Collection[0];
            Assert.Throws<FormatException>(() => CareerSave.Read(text.Replace($"\"{card}\"", "\"no-such-card\"")));
            Assert.Throws<FormatException>(() => CareerSave.Read(text.Replace("\"persona\": \"drafter\"", "\"persona\": \"ghost\"")));
            Assert.Throws<FormatException>(() => CareerSave.Read(text.Replace("\"stakes\": \"Standard\"", "\"stakes\": \"7\"")));
            Assert.Throws<FormatException>(() => CareerSave.Read(text.Replace("green-flag-deck-career", "something-else")));
            Assert.Throws<FormatException>(() => CareerSave.Read(text.Replace($"\"v\": {CareerSave.Version},", "\"v\": 0,")));
            Assert.Throws<FormatException>(() => CareerSave.Read(text.Replace($"\"{m.Career.Sponsor.Id}\"", "\"no-such-sponsor\"")));
        }

        [Fact]
        public void ARunThatEndedLoadsOnItsLastScreen()
        {
            var slots = Slots();
            var m = new CareerMenu(slots) { PickPersona = "drafter", PickStakes = Stakes.Permadeath };
            m.Press("title.start");
            var bot = new MenuBot();
            while (m.RaceWanted == null) Assert.True(bot.Step(m));
            var res = m.RaceFinished(Quick(m.TakeRace()));
            // the run ends as a Permadeath DNF with no Survive token ends it
            res.Penalty = "dead"; m.Career.Over = true;
            Assert.True(m.Career.Over);
            m.Press("reward.go");
            Assert.Equal(CareerScreen.Over, m.Screen);
            m.SaveNow();
            var n = new CareerMenu(slots);
            Assert.True(n.Load(1));
            Assert.Equal(CareerScreen.Over, n.Screen);
            Assert.Contains("Run over", CareerMenu.SlotLine(slots.Info(1)));
        }

        [Fact]
        public void FileStoreKeepsABackupAndLeavesNoTempFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gfd-saves-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileSaveStore(dir);
                var slots = Slots(store);
                var m = new CareerMenu(slots) { PickPersona = "drafter" };
                m.Press("title.start");
                m.Career.Money = 111; m.SaveNow();
                Assert.True(File.Exists(Path.Combine(dir, "career-1.json")));
                Assert.True(File.Exists(Path.Combine(dir, "career-1.json.bak")));
                Assert.False(File.Exists(Path.Combine(dir, "career-1.json.tmp")));
                Assert.Equal(111, slots.Load(1).Career.Money);
                File.WriteAllText(Path.Combine(dir, "career-1.json"), "");
                Assert.True(slots.Info(1).FromBackup);
                slots.Delete(1);
                Assert.Empty(Directory.GetFiles(dir));
                Assert.Equal(SlotState.Empty, slots.Info(1).State);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        // ================================================================== migrations and old saves

        /// <summary>The migration machinery with a made-up chain: each step runs once, in order, on the career and the retry career, and the version follows.</summary>
        [Fact]
        public void MigrationsRunInOrder()
        {
            var m = MenuBot.Run(new CareerMenu { PickPersona = "drafter", NewSeed = 2 }, 1, Quick);
            var text = CareerSave.Write(new SaveGame { Career = m.Career, Retry = m.Career.Clone(), SavedAt = Pinned });
            var root = JsonValue.Parse(text);
            root.Set("v", 1);
            text = root.Write();
            var ran = new List<int>();
            var steps = new[]
            {
                new SaveMigration { From = 2, What = "money in cents", Up = j => { ran.Add(2); foreach (var c in CareerSave.Careers(j)) c.Set("cents", c["money"].Long() * 100); return j; } },
                new SaveMigration { From = 1, What = "rename money", Up = j => { ran.Add(1); foreach (var c in CareerSave.Careers(j)) { c.Set("cash", c["money"]); } return j; } },
            };
            var up = CareerSave.Migrate(root, steps, 3);
            Assert.Equal(new[] { 1, 2 }, ran);
            Assert.Equal(3, up["v"].Int());
            foreach (var c in CareerSave.Careers(up)) Assert.Equal(m.Career.Money * 100L, c["cents"].Long());
            Assert.Equal(2, CareerSave.Careers(up).Count());
            Assert.Throws<FormatException>(() => CareerSave.Migrate(JsonValue.Parse("{\"v\":1}"), steps.Take(1).ToArray(), 3));
            Assert.Throws<SaveTooNewException>(() => CareerSave.Migrate(JsonValue.Parse("{\"v\":4}"), steps, 3));
            Assert.Equal(1, CareerSave.Read(text, steps, 3).LoadedFrom);
        }

        static string SavesDir => Path.Combine(AppContext.BaseDirectory, "Saves");

        public static IEnumerable<object[]> SampleSaves() =>
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Saves"), "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => new object[] { Path.GetFileName(f) });

        /// <summary>Every format version this build reads has sample saves in Saves/ (old ones are never deleted), and the
        /// real migration chain covers every version below the current one.</summary>
        [Fact]
        public void EveryFormatVersionHasSampleSaves()
        {
            var versions = Directory.GetFiles(SavesDir, "*.json").Select(f => JsonValue.Parse(File.ReadAllText(f))["v"].Int()).ToList();
            for (int v = 1; v <= CareerSave.Version; v++) Assert.True(versions.Contains(v), $"No sample save for format {v} in sim/GreenFlag.Sim.Tests/Saves (run the harness: savefixtures)");
            for (int v = 1; v < CareerSave.Version; v++) Assert.Contains(CareerSave.Migrations, s => s.From == v);
            Assert.All(versions, v => Assert.InRange(v, 1, CareerSave.Version));
        }

        /// <summary>The roadmap's "done when": a save written by an older build loads in this one and the career plays on
        /// through the menus. Samples in the current format must also write back byte for byte, so a layout change without a
        /// version bump fails here.</summary>
        [Theory]
        [MemberData(nameof(SampleSaves))]
        public void OldSavesLoadAndPlayOn(string file)
        {
            var text = File.ReadAllText(Path.Combine(SavesDir, file));
            var g = CareerSave.Read(text);
            Assert.NotNull(g.Career);
            if (g.LoadedFrom == CareerSave.Version) Assert.Equal(text, CareerSave.Write(g));

            var store = new MemorySaveStore();
            store.Files[SaveSlots.FileName(2)] = text;
            var slots = Slots(store);
            var m = new CareerMenu(slots);
            Assert.True(m.Load(2), m.Toast);
            if (m.Screen == CareerScreen.Over) { Assert.True(m.Career.Over); return; }
            int had = m.Career.Results.Count;
            m = MenuBot.Run(m, 2, Quick);
            Assert.True(m.Career.Results.Count >= had + 1);
            Assert.True(m.SaveNow());
            Assert.Equal(CareerSave.Version, JsonValue.Parse(store.Files[SaveSlots.FileName(2)])["v"].Int());
        }
    }
}
