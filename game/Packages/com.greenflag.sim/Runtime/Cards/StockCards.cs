using System;
using System.Collections.Generic;
using static GreenFlag.Sim.CardFamily;

namespace GreenFlag.Sim
{
    /// <summary>
    /// The stock car and late model cards from slice v47 (CARDS), with their toss effects (TOSS) and damage zones (CARD_DZ).
    /// Each read and outcome is a line-for-line port; the numbers are the slice's.
    /// </summary>
    public static class StockCards
    {
        static int Rd(double x) => (int)SimMath.RoundJs(x);
        const string NoAhead = "No car ahead";

        public static IEnumerable<CardDef> All()
        {
            yield return new CardDef
            {
                Id = "tuck_in", Name = "Tuck In", Family = Draft, Rarity = Rarity.Common, Zone = DamageZone.Nose, Toss = TossKind.Chain,
                Text = "Draft the car ahead: +3% speed for 15 s and +1 Momentum.", Real = "Sitting in the draft behind a car to cut drag.",
                Can = c => c.Ahead != null ? null : NoAhead,
                Read = (c, R) =>
                {
                    R.B(56); double d = c.Track.DraftRange;
                    if (c.GapA < d * 0.5) R.A(22, "Tucked up close"); else if (c.GapA < d) R.A(8, "In the draft"); else if (c.GapA < d * 2.5) R.A(-12, "Edge of the draft"); else R.A(-35, "Too far back");
                    R.A(Rd(c.Track.DraftBias * 14), c.Track.Type);
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.04, 20); c.Mom(2); },
                OnSuccess = c => { c.Pace(c.Car, 1.03, 15); c.Mom(1); },
                OnNeutral = c => c.Pace(c.Car, 1.012, 8),
                OnFail = c => c.Slow(c.Car, 0.3),
            };
            yield return new CardDef
            {
                Id = "bump_draft", Name = "Bump Draft", Family = Draft, Rarity = Rarity.Common, TireCost = 4, Zone = DamageZone.Nose, Toss = TossKind.Buff, TossFamily = Draft,
                Text = "Shove the car ahead so you both jump the field. Critical: you pop out and pass him.", Real = "Hitting the rear bumper of the car ahead to push you both.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1.5 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(50);
                    if (c.GapA < 0.25) R.A(18, "On his bumper"); else if (c.GapA < 0.6) R.A(6, "Close"); else R.A(-22, "Not close enough");
                    if (c.Seg.Turn) R.A(-28, "In the corner"); else R.A(10, "On the straight");
                    R.A(Rd(c.Track.DraftBias * 18), c.Track.Type);
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Mom(2); },
                OnSuccess = c => { c.Pace(c.Car, 1.05, 6); c.Pace(c.Ahead, 1.03, 6); c.Mom(1); },
                OnNeutral = c => c.Pace(c.Car, 1.015, 5),
                OnFail = c => { c.Slow(c.Car, 0.5); c.Dmg(c.Car, 5); },
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "turns him in the corner"),
            };
            yield return new CardDef
            {
                Id = "side_draft", Name = "Side Draft", Family = Draft, Rarity = Rarity.Common, Zone = DamageZone.Side, Toss = TossKind.Buff, TossFamily = Pass,
                Text = "Pull alongside and steal his air so he loses time. Critical: you clear him.", Real = "Pulling alongside so your nose disturbs the air on his quarter panel.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1 ? "Get closer first" : null,
                Read = (c, R) =>
                {
                    R.B(54);
                    if (c.GapA < 0.3) R.A(16, "Nose at his quarter"); else if (c.GapA < 0.7) R.A(4, "Close"); else R.A(-20, "Too far back");
                    if (c.Seg.Turn) R.A(-12, "Mid-corner"); else R.A(8, "On the straight");
                    R.A(Rd(c.Track.DraftBias * 10), c.Track.Type);
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Mom(1); },
                OnSuccess = c => { c.Slow(c.Ahead, 0.35); c.Pace(c.Car, 1.02, 4); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => c.Slow(c.Car, 0.3),
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "gets into his door"),
            };
            yield return new CardDef
            {
                Id = "slingshot", Name = "Slingshot", Family = Draft, Rarity = Rarity.Uncommon, MomCost = 2, Zone = DamageZone.Nose, Toss = TossKind.Charge,
                Text = "Whip out of the draft at the end of the straight. Pass 1 car, or 2 on a critical.", Real = "Using the pull of the draft to sling past at the end of a straight.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1.2 ? "Get in the draft first" : c.Momentum < 2 ? "Needs 2 Momentum" : null,
                Read = (c, R) =>
                {
                    R.B(52); double d = c.Track.DraftRange;
                    if (c.GapA < d * 0.5) R.A(18, "Tucked in"); else if (c.GapA < d) R.A(6, "In the draft"); else R.A(-18, "Out of the draft");
                    if (c.Seg.Approach) R.A(18, "End of the straight"); else if (c.Seg.Turn) R.A(-20, "In the corner"); else R.A(-4, "A bit early");
                    R.A(Rd(c.Track.DraftBias * 16), c.Track.Type);
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 2); c.Mom(1); },
                OnSuccess = c => c.Overtake(c.Car, 1),
                OnNeutral = c => c.Mom(1),
                OnFail = c => c.Slow(c.Car, 0.5),
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "swings out and clips him"),
            };
            yield return new CardDef
            {
                Id = "draft_train", Name = "Draft Train", Family = Draft, Rarity = Rarity.Rare, Zone = DamageZone.Nose, Toss = TossKind.Draw,
                Text = "Organize a line and ride it forward. Pass 2 cars, or 3 on a critical.", Real = "Lining up nose to tail so the whole train gains on the field.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > c.Track.DraftRange ? "Get in the draft first" : null,
                Read = (c, R) =>
                {
                    R.B(46); R.A(Rd(c.Track.DraftBias * 22), c.Track.Type);
                    if (!c.Seg.Turn) R.A(8, "On the straight");
                    if (c.Behind != null && c.GapB < c.Track.DraftRange) R.A(8, "Help behind you");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 3); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 2); c.Mom(1); },
                OnNeutral = c => c.Pace(c.Car, 1.02, 8),
                OnFail = c => c.Slow(c.Car, 0.4),
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "triggers the Big One", true),
            };
            yield return new CardDef
            {
                Id = "dive_bomb", Name = "Dive Bomb", Family = Pass, Rarity = Rarity.Common, TireCost = 6, Zone = DamageZone.Side, Toss = TossKind.Chain,
                Text = "Late, deep braking to the inside. Pass the car ahead.", Real = "Diving under a car on corner entry.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1.2 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(46);
                    if (c.GapA < 0.35) R.A(14, "Right on him"); else if (c.GapA < 0.7) R.A(2, "Close"); else R.A(-18, "Too far back");
                    if (c.Seg.Approach) R.A(18, "Corner entry"); else if (c.Seg.Turn) R.A(-6, "Mid-corner"); else R.A(-14, "Too early");
                    if (c.Tires > 75) R.A(10, "Fresh tires"); else if (c.Tires < 45) R.A(-16, "Worn tires");
                    if (c.Track.Contact > 1.2) R.A(6, "Short track");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 1); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => { c.Slow(c.Car, 0.7); c.Dmg(c.Car, 4); },
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "slides up into him"),
            };
            yield return new CardDef
            {
                Id = "high_line", Name = "Run the High Line", Family = Pass, Rarity = Rarity.Common, Zone = DamageZone.Side, Toss = TossKind.Setup,
                Text = "Move up where the rubber is. Late in a run, pass the car ahead.", Real = "Running the high groove when the bottom goes away.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1.5 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(44); if (c.Ahead == null) return;
                    if (c.Ahead.Tires < 60) R.A(16, "His tires are going");
                    if (c.Tires > c.Ahead.Tires + 10) R.A(12, "Fresher than him"); else if (c.Tires < c.Ahead.Tires - 10) R.A(-12, "Older tires");
                    if (c.Seg.Turn) R.A(8, "Mid-corner");
                    if (c.Track.Contact > 1.2) R.A(8, "Short track");
                    if (c.GapA > 0.8) R.A(-10, "Not close yet");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Pace(c.Car, 1.02, 10); },
                OnSuccess = c => c.Overtake(c.Car, 1),
                OnNeutral = c => c.Pace(c.Car, 1.01, 8),
                OnFail = c => c.Slow(c.Car, 0.4),
            };
            yield return new CardDef
            {
                Id = "bump_run", Name = "Bump and Run", Family = Pass, Rarity = Rarity.Common, TireCost = 3, Zone = DamageZone.Nose, Toss = TossKind.Cool,
                Text = "Nudge him up the track and take the line.", Real = "Moving the car ahead with your front bumper on a short track.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > (c.R.RulesOn && c.Track.Kind == TrackKind.ShortTrack ? 1.0 : 0.7) ? "Get to his bumper" : null,
                Read = (c, R) =>
                {
                    R.B(44); if (c.Ahead == null) return;
                    if (c.GapA < 0.25) R.A(16, "On his bumper"); else R.A(-8, "Not quite there");
                    if (c.Seg.Turn) R.A(12, "Middle of the corner");
                    if (c.Track.Contact > 1.2) R.A(14, "Short track");
                    if (c.Track.DraftBias > 0.8) R.A(-18, "Too fast here");
                    if (c.Ahead.Trait == Trait.Wall) R.A(-8, "He races hard");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Slow(c.Ahead, 1.2); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 1); c.Slow(c.Ahead, 0.4); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => { c.Slow(c.Car, 0.6); c.Dmg(c.Car, 6); },
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "dumps him and they both spin"),
            };
            yield return new CardDef
            {
                Id = "crossover", Name = "Crossover", Family = Pass, Rarity = Rarity.Uncommon, Zone = DamageZone.Side, Toss = TossKind.Charge,
                Text = "Cut under the car that just passed you. Only within 6 s of losing a spot.", Real = "Letting a car slide up after a pass, then turning under him on exit.",
                Can = c => c.R.LostTo != null && c.R.T - c.R.LostAt < 6 && c.Ahead == c.R.LostTo ? null : "Only right after losing a spot",
                Read = (c, R) =>
                {
                    R.B(58);
                    if (c.GapA < 0.4) R.A(12, "Still alongside");
                    if (c.Seg.Turn || c.Seg.Approach) R.A(8, "Corner exit");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 1); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => c.Slow(c.Car, 0.4),
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "tags his door on the crossover"),
            };
            yield return new CardDef
            {
                Id = "block", Name = "Block", Family = Defend, Rarity = Rarity.Common, Zone = DamageZone.Tail, Toss = TossKind.Chain,
                Text = "Move into his lane and kill the run. Cars behind barely get by you for 20 s.", Real = "Moving into the lane of a car with a run on you.",
                Can = c => c.Behind == null ? "No one behind" : c.GapB > 1.2 ? "No one close behind" : null,
                Read = (c, R) =>
                {
                    R.B(52);
                    if (c.GapB < 0.3) R.A(14, "He's on you"); else if (c.GapB < 0.7) R.A(4, "Close behind"); else R.A(-10, "Early");
                    if (c.Seg.Approach) R.A(10, "Corner entry");
                    if (c.Track.DraftBias > 0.8) R.A(-6, "Big runs here");
                },
                OnCritSuccess = c => { c.BlockFor(c.Car, 30); c.Mom(1); },
                OnSuccess = c => c.BlockFor(c.Car, 20),
                OnNeutral = c => c.BlockFor(c.Car, 8),
                OnFail = c => { if (c.Behind != null) c.Overtake(c.Behind, 1); },
                OnCritFail = c => c.Wreck(c.Car, c.Behind, "blocks too late and gets turned"),
            };
            yield return new CardDef
            {
                Id = "clear_air", Name = "Clear Air", Family = Defend, Rarity = Rarity.Common, Zone = DamageZone.Tail, Toss = TossKind.Focus, TossValue = 0.25,
                Text = "Use the clean air: +2.5% speed for 25 s. Needs space ahead or the lead.", Real = "Leading in clean air on an intermediate track.",
                Can = c => c.Pos == 1 || c.GapA > 1 ? null : "Needs clean air",
                Read = (c, R) =>
                {
                    R.B(62);
                    if (c.Pos == 1) R.A(16, "Leading"); else if (c.GapA > 2) R.A(8, "Space ahead");
                    if (c.Behind != null && c.GapB < 0.4) R.A(-14, "Pressure behind");
                    if (c.Track.Id == "carver") R.A(6, "Intermediate");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.035, 30); c.Mom(1); },
                OnSuccess = c => c.Pace(c.Car, 1.025, 25),
                OnNeutral = c => c.Pace(c.Car, 1.01, 15),
            };
            yield return new CardDef
            {
                Id = "save_tires", Name = "Save the Tires", Family = Car, Rarity = Rarity.Common, Zone = DamageZone.Side, Toss = TossKind.Cool,
                Text = "Ease off: tire wear halved for 30 s, -1% speed.", Real = "Taking care of the right front in a long run.",
                Read = (c, R) =>
                {
                    R.B(60);
                    if (c.Tires > 70) R.A(14, "Early in the run"); else if (c.Tires < 40) R.A(-20, "Too late");
                    if (c.Behind != null && c.GapB < 0.4) R.A(-12, "Pressure behind");
                },
                OnCritSuccess = c => { c.WearFx(c.Car, 0.4, 40); c.Pace(c.Car, 0.995, 30); },
                OnSuccess = c => { c.WearFx(c.Car, 0.5, 30); c.Pace(c.Car, 0.99, 30); },
                OnNeutral = c => { c.WearFx(c.Car, 0.7, 15); c.Pace(c.Car, 0.99, 15); },
                OnFail = c => c.Pace(c.Car, 0.97, 10),
            };
            yield return new CardDef
            {
                Id = "push_limit", Name = "Push to the Limit", Family = Car, Rarity = Rarity.Common, TireCost = 10, Zone = DamageZone.Side, Toss = TossKind.Chain,
                Text = "Qualifying laps: +5% speed for 10 s.", Real = "Running every lap like a qualifying lap.",
                Read = (c, R) =>
                {
                    R.B(58);
                    if (c.Tires > 75) R.A(16, "Fresh tires"); else if (c.Tires < 45) R.A(-24, "Worn tires");
                    if (c.Car.Damage > 20) R.A(-10, "Damaged car");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.065, 12); c.Mom(1); },
                OnSuccess = c => c.Pace(c.Car, 1.05, 10),
                OnNeutral = c => c.Pace(c.Car, 1.02, 8),
                OnFail = c => { c.Slow(c.Car, 0.5); c.Dmg(c.Car, 8); },
                OnCritFail = c => c.Wreck(c.Car, null, "slaps the wall hard"),
            };
            yield return new CardDef
            {
                Id = "hot_laps", Name = "Hot Laps", Family = Car, Rarity = Rarity.Rare, TireCost = 8, Zone = DamageZone.Side, Toss = TossKind.Draw,
                Text = "String together hot laps: +4% speed for 20 s and draw a card.", Real = "A run of perfect laps when the car comes to you.",
                Read = (c, R) =>
                {
                    R.B(60);
                    if (c.Tires > 65) R.A(10, "Good tires");
                    if (c.Car.Damage > 20) R.A(-10, "Damaged car");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.05, 22); c.Draw(1); c.Mom(1); },
                OnSuccess = c => { c.Pace(c.Car, 1.04, 20); c.Draw(1); },
                OnNeutral = c => c.Pace(c.Car, 1.02, 10),
                OnFail = c => c.Slow(c.Car, 0.3),
            };
            yield return new CardDef
            {
                Id = "spotter_focus", Name = "Eyes Up", Family = Crew, Rarity = Rarity.Common, Zone = DamageZone.Nose, Toss = TossKind.Focus, TossValue = 0.3,
                Text = "Your spotter locks in. Your next card gets +15.", Real = "Spotter and driver getting on the same page.",
                Read = (c, R) => { R.B(70); R.A((c.R.Loadout.Spotter - 5) * 2, "Spotter skill"); },
                OnCritSuccess = c => { c.Buff(20, "Eyes Up"); c.Mom(1); },
                OnSuccess = c => c.Buff(15, "Eyes Up"),
                OnNeutral = c => c.Buff(8, "Eyes Up"),
            };
            yield return new CardDef
            {
                Id = "regroup", Name = "Regroup", Family = Crew, Rarity = Rarity.Common, MomCost = 1, Zone = DamageZone.Nose, Toss = TossKind.Charge,
                Text = "Talk it through with the crew chief: draw 2 cards.", Real = "A calm radio conversation in the middle of a run.",
                Can = c => c.Momentum < 1 ? "Needs 1 Momentum" : null,
                Read = (c, R) => { R.B(72); R.A((c.R.Loadout.CrewChief - 5) * 2, "Crew chief skill"); },
                OnCritSuccess = c => { c.Draw(2); c.Mom(1); },
                OnSuccess = c => c.Draw(2),
                OnNeutral = c => c.Draw(1),
            };
            yield return new CardDef
            {
                Id = "restart_jump", Name = "Jump the Restart", Family = Restart, Rarity = Rarity.Common, Zone = DamageZone.Nose, Toss = TossKind.Setup,
                Text = "Nail the restart. Gain 2 spots, or 3 on a critical. Restarts only.", Real = "Getting the jump when the leader goes in the restart zone.",
                Can = c => c.R.RestartWindow ? null : "Restarts only",
                Read = (c, R) =>
                {
                    R.B(54);
                    if (c.R.RestartBuff != 0) R.A(c.R.RestartBuff, "Chose the right lane");
                    if (c.Pos <= 3) R.A(-10, "Front row: less room");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 3); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 2); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => c.Slow(c.Car, 0.6),
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "stacks up the restart"),
            };
            yield return new CardDef
            {
                Id = "choose_lane", Name = "Choose the Lane", Family = Restart, Rarity = Rarity.Common, Zone = DamageZone.Nose, Toss = TossKind.Charge,
                Text = "Pick the lane with the better car behind. Next restart: restart cards +20 and you gain a spot at the green. Caution only.", Real = "The choose rule at the restart cone.",
                Can = c => c.R.Phase == RacePhase.Caution ? null : "Caution only",
                Read = (c, R) => R.B(64),
                OnCritSuccess = c => { c.R.RestartBuff = 25; c.R.StartGain = 2; },
                OnSuccess = c => { c.R.RestartBuff = 20; c.R.StartGain = 1; },
                OnNeutral = c => { c.R.RestartBuff = 10; },
                OnFail = c => { c.R.StartGain = -1; },
            };
            yield return new CardDef
            {
                Id = "fast_stop", Name = "Fast Stop", Family = Pit, Rarity = Rarity.Common, Zone = DamageZone.Side, Toss = TossKind.Charge,
                Text = "The crew is fired up. If you pit this caution, your stop is 2 s faster (3 s on a critical).", Real = "A pit crew hitting its marks.",
                Can = c => c.R.Phase == RacePhase.Caution && c.R.Caution != null && !c.R.Caution.Done ? null : "Caution only, before your pit call locks",
                Read = (c, R) => { R.B(60); R.A((c.R.Loadout.CrewChief - 5) * 3, "Crew chief skill"); },
                OnCritSuccess = c => c.R.PitBonus = 3,
                OnSuccess = c => c.R.PitBonus = 2,
                OnNeutral = c => c.R.PitBonus = 1,
                OnFail = c => c.R.PitBonus = -1,
            };
            yield return new CardDef
            {
                Id = "send_it", Name = "Send It", Family = Risk, Rarity = Rarity.Uncommon, TireCost = 12, Zone = DamageZone.Side, Toss = TossKind.Cool,
                Text = "An all-in move through traffic. Pass 2 cars, or 3 on a critical.", Real = "The kind of move that wins races or ends them.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(38);
                    if (c.GapA < 0.4) R.A(10, "Close");
                    if (c.Tires > 70) R.A(8, "Fresh tires");
                    if (c.Seg.Approach) R.A(8, "Corner entry");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 3); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 2); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => { c.Slow(c.Car, 1); c.Dmg(c.Car, 10); },
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "sends it and it does not stick"),
            };
            yield return new CardDef
            {
                Id = "stay_out", Name = "Stay Out", Family = Pit, Rarity = Rarity.Common, Zone = DamageZone.Side, Toss = TossKind.Charge,
                Text = "Wave off the stop and keep track position. Success: you also gain a spot at the green (2 on a critical). Caution only, before your pit call locks.",
                Real = "Staying on the track while the leaders pit, trading tires for track position.",
                Can = c => c.R.Phase == RacePhase.Caution && c.R.Caution != null && !c.R.Caution.Done ? null : "Caution only, before your pit call locks",
                Read = (c, R) =>
                {
                    R.B(58);
                    if (c.Tires > 65) R.A(12, "Tires can go"); else if (c.Tires < 40) R.A(-22, "Tires are gone");
                    double left = c.R.Track.Laps - c.Car.Dist;
                    if (c.Car.Burn != 0 && c.Car.Fuel / c.Car.Burn < left + 0.15) R.A(-30, "Not enough fuel");
                    if (c.Pos <= 8) R.A(8, "Running up front");
                },
                OnCritSuccess = c => { c.R.Caution.Mine = PitService.Stay; c.R.StartGain = Math.Max(c.R.StartGain, 2); c.R.RestartBuff = Math.Max(c.R.RestartBuff, 15); },
                OnSuccess = c => { c.R.Caution.Mine = PitService.Stay; c.R.StartGain = Math.Max(c.R.StartGain, 1); c.R.RestartBuff = Math.Max(c.R.RestartBuff, 10); },
                OnNeutral = c => { c.R.Caution.Mine = PitService.Stay; },
                OnFail = c => { c.R.Caution.Mine = PitService.Stay; c.R.StartGain = -1; },
            };
            yield return new CardDef
            {
                Id = "evade", Name = "Evade", Family = Car, Rarity = Rarity.Special, Zone = DamageZone.Side,
                Text = "Wreck ahead! Check up and pick a hole. Ignore it and you may be collected.", Real = "Driving through the smoke on a spotter's call.",
                Read = (c, R) =>
                {
                    R.B(66); R.A(Rd((c.Car.Stats.Handling - 5) * 2), "Handling");
                    if (c.Seg.Turn) R.A(-8, "Mid-corner");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.04, 5); c.Mom(1); },
                OnSuccess = c => c.Pace(c.Car, 1.02, 4),
                OnNeutral = c => c.Slow(c.Car, 0.4),
                OnFail = c => { c.Slow(c.Car, 1.2); c.Dmg(c.Car, 6); },
            };
            yield return new CardDef
            {
                Id = "reel_in", Name = "Reel Them In", Family = Car, Rarity = Rarity.Special, TireCost = 4, Zone = DamageZone.Side,
                Text = "Nobody close enough to race. Put your head down and close the gap: +10% speed for 10 s.", Real = "Running down the car ahead lap after lap.",
                Can = c => c.Ahead == null ? NoAhead : null,
                Read = (c, R) =>
                {
                    R.B(64);
                    if (c.Tires > 70) R.A(10, "Fresh tires"); else if (c.Tires < 45) R.A(-14, "Worn tires");
                    if (c.Car.Damage > 20) R.A(-8, "Damaged car");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.12, 12); c.Mom(1); },
                OnSuccess = c => c.Pace(c.Car, 1.10, 10),
                OnNeutral = c => c.Pace(c.Car, 1.04, 8),
                OnFail = c => { c.Slow(c.Car, 0.4); c.WearFx(c.Car, 1.3, 10); },
            };
            yield return new CardDef
            {
                Id = "tape_grille", Name = "Tape the Grille", Family = Risk, Rarity = Rarity.Uncommon, Zone = DamageZone.Nose, Toss = TossKind.Setup,
                Text = "More tape for downforce: +3.5% speed for 40 s. Risk of overheating.", Real = "Trading engine temperature for speed.",
                Read = (c, R) =>
                {
                    R.B(50);
                    if (c.R.T - c.R.LastGreenAt < 30) R.A(10, "Short run");
                    if (c.Car.Damage > 15) R.A(-10, "Damaged car");
                },
                OnCritSuccess = c => c.Pace(c.Car, 1.045, 45),
                OnSuccess = c => c.Pace(c.Car, 1.035, 40),
                OnNeutral = c => c.Pace(c.Car, 1.015, 20),
                OnFail = c => { c.Dmg(c.Car, 14); c.Slow(c.Car, 0.4); },
                OnCritFail = c => { c.Dmg(c.Car, 40); c.Slow(c.Car, 3); },
            };
        }
    }
}
