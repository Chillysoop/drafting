using System;
using System.Collections.Generic;

namespace GreenFlag.Sim
{
    public enum PartSlot { Engine, Aero, Chassis, Tires, Crew }

    /// <summary>
    /// A car part (the slice's relics): race-long multipliers, odds reads for some cards, and a few one-off rules.
    /// Multipliers default to 1 and sums to 0, so a part only names what it changes.
    /// </summary>
    public sealed class PartDef
    {
        public string Id, Name, Text;
        public PartSlot Slot;
        /// <summary>Kart-only part, or stock/late-only part (pit and restart parts).</summary>
        public bool Kart, Stock;
        public double Pace = 1, Wear = 1, Draft = 1, Dmg = 1, Range = 1, RadioRate = 1;
        public double Pit, HeatCool, StartMom, StartTow, FairMax, RadioBonus;
        public bool Launch, Reroll, Gauge2;
        /// <summary>Odds added to a card.</summary>
        public Func<CardDef, CardCtx, int> Read;
        /// <summary>Share of a card's critical failure chance that stays critical (the rest becomes a plain failure).</summary>
        public Func<CardDef, double> Cf;
    }

    public static class Parts
    {
        static readonly Dictionary<string, PartDef> byId = new Dictionary<string, PartDef>();
        static readonly List<PartDef> all = new List<PartDef>();
        public static IReadOnlyList<PartDef> All => all;

        static bool In(string id, params string[] ids) => Array.IndexOf(ids, id) >= 0;

        static Parts()
        {
            Add(new PartDef { Id = "big_motor", Name = "Big Motor", Slot = PartSlot.Engine, Text = "+0.8% speed. Car cards -5.", Pace = 1.008, Read = (cd, c) => cd.Family == CardFamily.Car ? -5 : 0 });
            Add(new PartDef { Id = "launch_control", Name = "Launch Control", Slot = PartSlot.Engine, Stock = true, Text = "Restart cards +15 and a quick launch on every restart.", Read = (cd, c) => cd.Family == CardFamily.Restart ? 15 : 0, Launch = true });
            Add(new PartDef { Id = "slick_nose", Name = "Slick Nose", Slot = PartSlot.Aero, Text = "Drafting cards +4. Draft pull +15%.", Read = (cd, c) => cd.Family == CardFamily.Draft ? 4 : 0, Draft = 1.15 });
            Add(new PartDef { Id = "low_spoiler", Name = "Low-Drag Spoiler", Slot = PartSlot.Aero, Text = "+0.5% speed. Drafting cards +4.", Pace = 1.005, Read = (cd, c) => cd.Family == CardFamily.Draft ? 4 : 0 });
            Add(new PartDef { Id = "reinforced_bumper", Name = "Reinforced Bumper", Slot = PartSlot.Chassis, Text = "Critical failures on Passing and Defending cards are halved.", Cf = cd => cd.Family == CardFamily.Pass || cd.Family == CardFamily.Defend ? 0.5 : 1 });
            Add(new PartDef { Id = "roll_cage", Name = "Roll Cage", Slot = PartSlot.Chassis, Text = "Damage taken -35%.", Dmg = 0.65 });
            Add(new PartDef { Id = "tight_setup", Name = "Tight Setup", Slot = PartSlot.Chassis, Text = "Defending cards +10.", Read = (cd, c) => cd.Family == CardFamily.Defend ? 10 : 0 });
            Add(new PartDef { Id = "long_run_tires", Name = "Long-Run Tires", Slot = PartSlot.Tires, Text = "Tire wear -25%. Car cards +4.", Wear = 0.75, Read = (cd, c) => cd.Family == CardFamily.Car ? 4 : 0 });
            Add(new PartDef { Id = "sticker_tires", Name = "Sticker Tires", Slot = PartSlot.Tires, Text = "Passing cards +8 while your tires are above 75%.", Read = (cd, c) => cd.Family == CardFamily.Pass && c.Tires > 75 ? 8 : 0 });
            Add(new PartDef
            {
                Id = "chrome_horn", Name = "Chrome Horn", Slot = PartSlot.Chassis, Text = "Bump and Run, Bump Draft and Block +12. Critical failures on them are halved, and Heat cools twice as fast.",
                Read = (cd, c) => In(cd.Id, "bump_run", "bump_draft", "block") ? 12 : 0, Cf = cd => In(cd.Id, "bump_run", "bump_draft", "block") ? 0.5 : 1, HeatCool = 2,
            });
            Add(new PartDef { Id = "pit_clipboard", Name = "Pit Clipboard", Slot = PartSlot.Crew, Stock = true, Text = "Caution and restart cards +10. Every stop is 0.3 s faster.", Read = (cd, c) => cd.Family == CardFamily.Pit || cd.Family == CardFamily.Restart ? 10 : 0, Pit = 0.3 });
            Add(new PartDef { Id = "spotter_headset", Name = "Spotter Headset", Slot = PartSlot.Crew, Text = "Odds ranges half as wide. Passing cards +5.", Range = 0.5, Read = (cd, c) => cd.Family == CardFamily.Pass ? 5 : 0 });
            Add(new PartDef { Id = "pit_monitor", Name = "Pit Box Monitor", Slot = PartSlot.Crew, Text = "A second live odds gauge, so you can track two cards at once.", Gauge2 = true });
            Add(new PartDef { Id = "lucky_lugnut", Name = "Lucky Lug Nut", Slot = PartSlot.Crew, Text = "Once per race, a failed card is rerolled.", Reroll = true });
            Add(new PartDef { Id = "momentum_wheel", Name = "Lucky Steering Wheel", Slot = PartSlot.Crew, Text = "Start each race with 2 Momentum.", StartMom = 2 });
            Add(new PartDef { Id = "fast_gun", Name = "Fast Air Gun", Slot = PartSlot.Crew, Stock = true, Text = "Every pit stop is 1 s faster.", Pit = 1 });
            Add(new PartDef { Id = "clutch_radio", Name = "Clutch Radio", Slot = PartSlot.Crew, Text = "Radio calls come 30% more often and engineer gambles get +15%.", RadioRate = 0.7, RadioBonus = 15 });
            // kart parts (step 21)
            Add(new PartDef { Id = "nerf_bar", Name = "Bent Nerf Bar", Slot = PartSlot.Chassis, Kart = true, Text = "Push Your Partner and Door to Door +10. Your fairing takes 4 pushes before it goes in.", Read = (cd, c) => In(cd.Id, "k_push", "k_door") ? 10 : 0, FairMax = 1 });
            Add(new PartDef { Id = "big_rotors", Name = "Drilled Rotors", Slot = PartSlot.Chassis, Kart = true, Text = "Late Brake +10, and its critical failures are halved.", Read = (cd, c) => cd.Id == "k_latebrake" ? 10 : 0, Cf = cd => cd.Id == "k_latebrake" ? 0.5 : 1 });
            Add(new PartDef { Id = "chalk_line", Name = "Chalk Line", Slot = PartSlot.Tires, Kart = true, Text = "Around the Outside and Own the Inside +10. Tire wear -15%.", Read = (cd, c) => In(cd.Id, "k_outside", "k_owninside") ? 10 : 0, Wear = 0.85 });
            Add(new PartDef { Id = "tow_hitch", Name = "Tow Bar Sticker", Slot = PartSlot.Aero, Kart = true, Text = "Start every race with a Tow pip. Drafting cards +4.", Read = (cd, c) => cd.Family == CardFamily.Draft ? 4 : 0, StartTow = 1 });
        }

        static void Add(PartDef p) { byId[p.Id] = p; all.Add(p); }

        public static PartDef Get(string id)
        {
            if (byId.TryGetValue(id, out var p)) return p;
            throw new KeyNotFoundException("No part with id " + id);
        }
    }
}
