using System;
using System.IO;
using System.Linq;
using GreenFlag.Sim;
using GreenFlag.View;

namespace GreenFlag.Harness
{
    /// <summary>
    /// Step 35 save checks.
    ///   saves [races] [persona] [seed] [stakes]  a career through the menus twice: once straight, once closing and reopening
    ///                                            the game after every press and race. Fails unless both end the same.
    ///   savefixtures [dir]                       writes this build's sample saves (one set per format version, never deleted)
    /// </summary>
    public static class SaveCmds
    {
        static readonly DateTime Pinned = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        static string Text(CareerMenu m) => CareerSave.Write(new SaveGame { Career = m.Career, Screen = m.Snapshot().Screen, SavedAt = Pinned, Build = "harness" });

        public static int Saves(int races, string persona, uint seed, string stakes, string start = "auto")
        {
            var st = (Stakes)Enum.Parse(typeof(Stakes), stakes, true);
            var a = MenuBot.Run(new CareerMenu { PickPersona = persona, NewSeed = seed, PickStakes = st, PickSeries = start == "late" ? Series.Late : Series.Stock }, races);
            var store = new MemorySaveStore();
            var slots = new SaveSlots(store) { Now = () => Pinned, Build = "harness" };
            int reloads = 0; long biggest = 0;
            var b = MenuBot.RunReloading(new CareerMenu(slots) { PickPersona = persona, NewSeed = seed, PickStakes = st, PickSeries = start == "late" ? Series.Late : Series.Stock }, races, null, null, n =>
            {
                reloads++;
                biggest = Math.Max(biggest, store.Files[SaveSlots.FileName(n.Slot)].Length);
            });
            string ta = Text(a), tb = Text(b);
            Console.WriteLine($"{persona}, {st}, seed {seed}: {a.Career.Results.Count} races straight vs {b.Career.Results.Count} with the game closed and reopened {reloads} times ({store.Writes} saves written, biggest {biggest / 1024.0:F1} KB)");
            Console.WriteLine($"  straight:  bank {a.Career.Money:N0}, {a.Career.Collection.Count} cards, finishes {string.Join(" ", a.Career.Results.Select(r => r.Dnf ? "DNF" : "P" + r.Finish))}");
            Console.WriteLine($"  reloading: bank {b.Career.Money:N0}, {b.Career.Collection.Count} cards, finishes {string.Join(" ", b.Career.Results.Select(r => r.Dnf ? "DNF" : "P" + r.Finish))}");
            if (ta != tb)
            {
                var la = ta.Split('\n'); var lb = tb.Split('\n');
                int i = 0; while (i < la.Length && i < lb.Length && la[i] == lb[i]) i++;
                Console.Error.WriteLine($"  DIFFERENT from line {i + 1}: {(i < la.Length ? la[i].Trim() : "(end)")}  vs  {(i < lb.Length ? lb[i].Trim() : "(end)")}");
                return 1;
            }
            Console.WriteLine("  identical: nothing that matters lives outside the save file");
            return 0;
        }

        /// <summary>Plays careers to a few different spots (garage, reward screen with a retry, a part pick, season 2) and writes
        /// them as sample saves named v{version}-*.json.</summary>
        public static int Fixtures(string dir)
        {
            Directory.CreateDirectory(dir);
            int v = CareerSave.Version;
            void Write(string name, CareerMenu m)
            {
                var store = new MemorySaveStore();
                var slots = new SaveSlots(store) { Now = () => Pinned, Build = $"sample save (format {v})" };
                m.Saves = slots; m.Slot = 1;
                if (!m.SaveNow()) throw new InvalidOperationException("save failed: " + slots.LastError);
                var path = Path.Combine(dir, $"v{v}-{name}.json");
                File.WriteAllText(path, store.Files[SaveSlots.FileName(1)]);
                var g = CareerSave.Read(store.Files[SaveSlots.FileName(1)]);
                Console.WriteLine($"{path}: {CareerMenu.SlotLine(new SlotInfo { Slot = 1, State = SlotState.Ok, Game = g })} ({new FileInfo(path).Length / 1024.0:F1} KB)");
            }
            CareerMenu ToReward(CareerMenu m, int races, Func<RewardOffer, bool> want = null, int cap = 12)
            {
                var bot = new MenuBot();
                for (int done = 0; done < cap; done++)
                {
                    for (int i = 0; i < 1000 && m.RaceWanted == null && bot.Step(m); i++) { }
                    m.RaceFinished(MousePlayer.Play(new Race(m.TakeRace())).desk.Race);
                    if (done + 1 >= races && (want == null || want(m.Career.Reward))) return m;
                    for (int i = 0; i < 1000 && m.Screen == CareerScreen.Reward && bot.Step(m); i++) { }
                }
                return m;
            }

            Write("drafter-garage", MenuBot.Run(new CareerMenu { PickPersona = "drafter", NewSeed = 11 }, 4));
            var k = ToReward(new CareerMenu { PickPersona = "brawler", NewSeed = 12 }, 3);
            k.Press("reward.reroll");
            Write("brawler-kart-reward", k);
            Write("bully-ironman-part-pick", ToReward(new CareerMenu { PickPersona = "bully", NewSeed = 13, PickStakes = Stakes.Ironman }, 1, R => R.Parts != null));
            Write("gambler-hard-season-2", MenuBot.Run(new CareerMenu { PickPersona = "gambler", NewSeed = 14, PickDiff = DifficultyLevel.Hard }, 11));
            // step 37 (format 2): the season-end screen with contract offers waiting, a promoted career, and a qualified grid
            CareerMenu ToSeasonEnd(CareerMenu m)
            {
                m = ToReward(m, 6);
                var bot = new MenuBot();
                for (int i = 0; i < 20 && m.Screen == CareerScreen.Reward; i++) bot.Step(m);
                if (m.Screen != CareerScreen.Season) throw new InvalidOperationException("not at the season end");
                return m;
            }
            Write("brawler-season-end-offers", ToSeasonEnd(new CareerMenu { PickPersona = "brawler", NewSeed = 15 }));
            Write("linepainter-promoted-late-models", MenuBot.Run(new CareerMenu { PickPersona = "linepainter", NewSeed = 16 }, 7, null, new MenuBot { QualiSkill = "great" }));
            var q = MenuBot.Run(new CareerMenu { PickPersona = "restart", NewSeed = 17 }, 2);
            var qb = new MenuBot();
            for (int i = 0; i < 100 && q.Screen != CareerScreen.QualiDone; i++) qb.Step(q);
            Write("restart-qualified", q);
            // step 38 (format 3): an off-track event with a roll on the table, a driver with a point to spend, and a
            // negotiated season-end offer
            var e = MenuBot.Run(new CareerMenu { PickPersona = "drafter", NewSeed = 18 }, 1);
            var eb = new MenuBot();
            for (int i = 0; i < 100 && e.Screen != CareerScreen.Event; i++) eb.Step(e);
            eb.Step(e);
            if (e.Career.Event?.Cur == null) throw new InvalidOperationException("no roll on the table");
            Write("drafter-event-roll-waiting", e);
            var n = ToSeasonEnd(new CareerMenu { PickPersona = "linepainter", NewSeed = 19 });
            if (!n.Press("neg:0")) throw new InvalidOperationException("couldn't negotiate");
            Write("linepainter-season-end-negotiated", n);
            // step 39 (format 4): Crew HQ built up (training, a perk, a hire on a wage, an upgraded pit card) and a rival's
            // offer for the crew chief waiting after a talk
            var w = MenuBot.Run(new CareerMenu { PickPersona = "gambler", NewSeed = 20 }, 4);
            w.Press("crew");
            for (int i = 0; i < 4 && w.Career.Staff[CrewRole.CrewChief].Lvl < 6; i++) w.Press("crew.train:CrewChief");
            w.Press("crew.perk:CrewChief:0:2"); w.Press("crew.perk:CrewChief:0:2");
            var hi = w.Career.Hires().FindIndex(h => h.Role != CrewRole.CrewChief && h.Fee <= w.Career.Money);
            if (hi >= 0) { w.Press("crew.hire:" + hi); w.Press("crew.hire:" + hi); }
            w.Press("crew.pitup:0");
            if (w.Career.RollPoach(force: true, role: CrewRole.CrewChief) == null) throw new InvalidOperationException("no offer");
            w.Press("crew.talk");
            Write("gambler-crew-offer-waiting", w);
            var x = MenuBot.Run(new CareerMenu { PickPersona = "drafter", NewSeed = 22 }, 6);
            Write("drafter-crew-xp", x);
            // step 41 (format 6): a career started in late models, three races into the new calendar
            Write("whisperer-late-models-race-4", MenuBot.Run(new CareerMenu { PickPersona = "whisperer", PickSeries = Series.Late, NewSeed = 23 }, 3));
            // step 42 (format 7): a premier stock career three races into the new calendar, Ridgeback road course next
            Write("bully-premier-race-4", MenuBot.Run(new CareerMenu { PickPersona = "bully", NewSeed = 24 }, 3));
            // step 43 (format 8): a kart career promoted to late models with the class around it, a silly season behind it
            // and rivals' messages waiting unanswered
            var rv = MenuBot.Run(new CareerMenu { PickPersona = "brawler", NewSeed = 25 }, 8, null, new MenuBot { SkipRivals = true });
            if (rv.Career.Unanswered == 0 || rv.Career.Series != Series.Late) throw new InvalidOperationException("no unanswered message in late models");
            Write("brawler-late-models-rivals-inbox", rv);
            return 0;
        }
    }
}
