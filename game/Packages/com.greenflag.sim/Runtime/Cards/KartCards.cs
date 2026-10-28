using System.Collections.Generic;
using static GreenFlag.Sim.CardFamily;

namespace GreenFlag.Sim
{
    /// <summary>
    /// The kart deck from slice v47 (KART_CARDS, step 21). Kart cards chain card to card (Next), spend and earn Tow pips, and a
    /// scruffy contact move pushes your front fairing in (the third push is a 5 s penalty at the flag).
    /// </summary>
    public static class KartCards
    {
        const string NoAhead = "No car ahead";

        public static IEnumerable<CardDef> All()
        {
            yield return new CardDef
            {
                Id = "k_tow", Name = "Tuck In the Tow", Family = Draft, Rarity = Rarity.Common, Kart = true, Toss = TossKind.Chain,
                Next = new[] { "k_pullout", "k_push", "k_latebrake", "k_show" },
                Text = "Slot into the train: +3% speed for 12 s and a Tow pip (2 on a critical).", Real = "Sitting in the slipstream of the kart ahead, nose to his rear bumper.",
                Can = c => c.Ahead != null ? null : NoAhead,
                Read = (c, R) =>
                {
                    R.B(56); double d = c.Track.DraftRange;
                    if (c.GapA < d * 0.5) R.A(18, "Tucked up close"); else if (c.GapA < d) R.A(6, "In the tow"); else if (c.GapA < d * 2.5) R.A(-12, "Edge of the tow"); else R.A(-35, "Too far back");
                    int tr = c.R.KartTrain(c.Car); if (tr > 1) R.A(5 * (tr - 1), $"Train of {tr + 1}");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.04, 15); c.R.AddTow(2); c.Mom(1); },
                OnSuccess = c => { c.Pace(c.Car, 1.03, 12); c.R.AddTow(1); },
                OnNeutral = c => c.Pace(c.Car, 1.01, 6),
                OnFail = c => c.Slow(c.Car, 0.3),
            };
            yield return new CardDef
            {
                Id = "k_push", Name = "Push Your Partner", Family = Draft, Rarity = Rarity.Common, Kart = true, TireCost = 3, Toss = TossKind.Buff, TossFamily = Draft,
                Next = new[] { "k_pullout", "k_door", "k_latebrake" },
                Text = "Shove the kart ahead so you both pull away. He may push you back later. A clean one is fine; a scruffy one pushes your fairing in.",
                Real = "Pushing the kart ahead down a straight so the pair gains on the field.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 0.6 ? "Get to his bumper" : null,
                Read = (c, R) =>
                {
                    R.B(52);
                    if (c.GapA < 0.2) R.A(16, "On his bumper"); else if (c.GapA > 0.4) R.A(-10, "Not close enough");
                    if (c.Seg.Turn) R.A(-24, "Mid-corner"); else if (c.Seg.Straight) R.A(10, "On the straight");
                    if (c.Seg.Long) R.A(6, "Long straight");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.05, 8); c.Pace(c.Ahead, 1.03, 8); c.R.AddTow(1); c.Mom(1); c.R.SetPartner(c.Ahead, 0.85); },
                OnSuccess = c => { c.Pace(c.Car, 1.04, 6); c.Pace(c.Ahead, 1.03, 6); c.R.SetPartner(c.Ahead, 0.55); },
                OnNeutral = c => { c.Pace(c.Car, 1.01, 4); c.R.AddFairing(1); },
                OnFail = c => { c.Slow(c.Car, 0.4); c.Dmg(c.Car, 4); c.R.AddFairing(2); },
            };
            yield return new CardDef
            {
                Id = "k_pullout", Name = "Pull Out", Family = Draft, Rarity = Rarity.Common, Kart = true, Toss = TossKind.Charge,
                Next = new[] { "k_latebrake", "k_owninside", "k_switch" },
                Text = "Spend a Tow pip to pull out of the tow and pass the kart ahead. +6 per pip.", Real = "Breaking the tow at the right moment and using the speed to draw alongside.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 0.9 ? "Get in the tow first" : c.R.Tow < 1 ? "Needs a Tow pip" : null,
                Read = (c, R) =>
                {
                    R.B(50); R.A(6 * c.R.Tow, $"Tow ×{c.R.Tow}");
                    if (c.Seg.Approach) R.A(10, "End of the straight"); else if (c.Seg.Turn) R.A(-14, "Mid-corner");
                    if (c.GapA > 0.5) R.A(-8, "A bit far back");
                },
                OnCritSuccess = c => { c.R.AddTow(-1); c.Overtake(c.Car, 1); c.Mom(2); },
                OnSuccess = c => { c.R.AddTow(-1); c.Overtake(c.Car, 1); c.Mom(1); c.R.KAlong = c.Ahead; c.R.KAlongAt = c.R.T; },
                OnNeutral = c => c.R.AddTow(-1),
                OnFail = c => { c.R.AddTow(-1); c.Slow(c.Car, 0.3); },
            };
            yield return new CardDef
            {
                Id = "k_show", Name = "Show the Inside", Family = Pass, Rarity = Rarity.Common, Kart = true, NoHeat = true, Toss = TossKind.Chain,
                Next = new[] { "k_switch", "k_outside", "k_door" },
                Text = "Fake a dive. The kart ahead covers the inside for 6 s: Switchback +20 and Around the Outside +12 against him.",
                Real = "A feint into the braking zone that makes the driver ahead defend the wrong line.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1.2 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(58);
                    if (c.GapA < 0.5) R.A(8, "In his mirrors");
                    if (c.Seg.Approach) R.A(10, "Braking zone"); else if (c.Seg.Straight) R.A(-8, "Nothing to sell yet");
                    if (c.Ahead == null) return;
                    if (c.Ahead.Trait == Trait.Veteran) R.A(-10, "He has seen it before");
                    if (c.Ahead.Trait == Trait.Rookie) R.A(8, "Rookie bites");
                },
                OnCritSuccess = c => { c.Ahead.CoverUntil = c.R.T + 7; c.Slow(c.Ahead, 0.3); },
                OnSuccess = c => { c.Ahead.CoverUntil = c.R.T + 6; },
                OnNeutral = c => { c.Ahead.CoverUntil = c.R.T + 3; },
                OnFail = c => c.Slow(c.Car, 0.3),
            };
            yield return new CardDef
            {
                Id = "k_switch", Name = "Switchback", Family = Pass, Rarity = Rarity.Common, Kart = true, TireCost = 4, Toss = TossKind.Buff, TossFamily = Pass,
                Next = new[] { "k_cover", "k_smooth", "k_show" },
                Text = "Cross under him on the exit. +20 against a kart that is covering the inside.", Real = "Going in wide, then cutting back under the defender as he runs wide on exit.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(52);
                    if (c.GapA > 0.7) R.A(-8, "Not close yet");
                    if (c.R.Covering(c.Ahead)) R.A(20, "He is covering the inside");
                    if (c.Seg.Turn) R.A(10, "Corner exit"); else if (c.Seg.Straight) R.A(-10, "No corner to cut back");
                    if (c.GapA < 0.4) R.A(8, "Right on him");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 1); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => c.Slow(c.Car, 0.6),
                OnCritFail = c => c.Wreck(c.Car, c.Ahead, "clips his rear bumper on the switchback"),
            };
            yield return new CardDef
            {
                Id = "k_outside", Name = "Around the Outside", Family = Pass, Rarity = Rarity.Common, Kart = true, TireCost = 3, Toss = TossKind.Setup,
                Next = new[] { "k_owninside", "k_switch" },
                Text = "Pass him the long way round a corner. If the next corner turns the other way, the outside line sets up Own the Inside.",
                Real = "Running the outside line through one corner to get the inside for the next.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 0.9 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(48);
                    if (c.R.Covering(c.Ahead)) R.A(12, "He covered the inside: outside is open");
                    if (c.Seg.Turn) R.A(10, "Mid-corner"); else if (c.Seg.Straight) R.A(-14, "No corner to go round");
                    if (c.Seg.Kind == TurnKind.Sweeper) R.A(8, "Fast corner");
                    if (c.Seg.Kind == TurnKind.Hairpin) R.A(-10, "Too tight round the outside");
                    if (c.GapA < 0.4) R.A(6, "Alongside");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.R.SetOutside(c.Seg.Dir != 0 ? c.Seg.Dir : 1); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 1); c.R.SetOutside(c.Seg.Dir != 0 ? c.Seg.Dir : 1); c.Mom(1); },
                OnNeutral = c => { },
                OnFail = c => c.Slow(c.Car, 0.5),
            };
            yield return new CardDef
            {
                Id = "k_owninside", Name = "Own the Inside", Family = Pass, Rarity = Rarity.Uncommon, Kart = true, Toss = TossKind.Charge,
                Next = new[] { "k_cover", "k_tow" },
                LinkOk = r =>
                {
                    if (!r.OutsideFresh) return false;
                    var s = r.SpotOf(r.Me);
                    return s.Dir != 0 && !s.Straight && s.Dir != r.KOutDir;
                },
                Text = "Take the inside line into the next corner. +22 right after Around the Outside if this corner turns the other way.",
                Real = "The payoff of the outside line: the next corner turns back and you are on the inside.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 0.9 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(46);
                    if (c.R.OutsideFresh && c.Seg.Dir != 0 && !c.Seg.Straight)
                    {
                        if (c.Seg.Dir != c.R.KOutDir) R.A(22, "Outside line became the inside"); else R.A(-10, "Same way: no inside line");
                    }
                    if (c.Seg.Approach) R.A(8, "Corner entry");
                    if (c.GapA < 0.4) R.A(6, "Alongside");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.BlockFor(c.Car, 6); c.Mom(2); c.R.ClearOutside(); },
                OnSuccess = c => { c.Overtake(c.Car, 1); c.Mom(1); c.R.ClearOutside(); },
                OnNeutral = c => c.R.ClearOutside(),
                OnFail = c => { c.Slow(c.Car, 0.5); c.R.ClearOutside(); },
            };
            yield return new CardDef
            {
                Id = "k_latebrake", Name = "Late Brake", Family = Risk, Rarity = Rarity.Uncommon, Kart = true, TireCost = 8, Toss = TossKind.Cool,
                Next = new[] { "k_cover", "k_owninside" },
                Text = "The hairpin divebomb. Spends every Tow pip (+5 each). Pass 1, 2 on a critical, but a poor exit can get you crossed back.",
                Real = "Braking later than the kart ahead into a slow corner and diving to the inside.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 1.2 ? "Too far back" : null,
                Read = (c, R) =>
                {
                    R.B(44);
                    if (c.GapA > 0.8) R.A(-8, "Long way to dive");
                    if (c.Seg.Approach && c.Seg.Kind == TurnKind.Hairpin) R.A(18, "Hairpin braking zone"); else if (c.Seg.Approach) R.A(6, "Braking zone"); else R.A(-14, "Nowhere to brake");
                    R.A(5 * c.R.Tow, $"Tow ×{c.R.Tow}");
                    if (c.R.KAlong != null && c.R.KAlong == c.Ahead && c.R.T - c.R.KAlongAt < 6) R.A(10, "Already alongside");
                    if (c.R.Covering(c.Ahead)) R.A(-10, "He covered the inside");
                    if (c.Tires < 45) R.A(-12, "Worn tires");
                },
                OnCritSuccess = c => { c.R.Tow = 0; c.Overtake(c.Car, 2); c.Mom(2); },
                OnSuccess = c => { c.R.Tow = 0; c.Overtake(c.Car, 1); c.Mom(1); if (c.R.CardRng.NextDouble() < 0.45) c.R.SetRepass(c.Ahead); },
                OnNeutral = c => c.R.Tow = 0,
                OnFail = c => { c.R.Tow = 0; c.Slow(c.Car, 0.8); c.Dmg(c.Car, 6); },
                OnCritFail = c => { c.R.Tow = 0; c.Wreck(c.Car, c.Ahead, "locks the fronts and takes him out"); },
            };
            yield return new CardDef
            {
                Id = "k_door", Name = "Door to Door", Family = Pass, Rarity = Rarity.Common, Kart = true, TireCost = 5, Toss = TossKind.Cool,
                Next = new[] { "k_cover", "k_switch" },
                Text = "Lean on him side by side until he gives. Good in the esses. A clean one is fine; a scruffy one pushes your fairing in.",
                Real = "Banging wheels through a sequence of corners until one of you lifts.",
                Can = c => c.Ahead == null ? NoAhead : c.GapA > 0.6 ? "Get alongside first" : null,
                Read = (c, R) =>
                {
                    R.B(48);
                    if (c.GapA < 0.25) R.A(10, "Wheel to wheel");
                    if (c.Seg.Kind == TurnKind.Esses) R.A(20, "Side by side through the esses");
                    if (c.Track.Contact > 1.2) R.A(6, "Tight club track");
                    if (c.Ahead != null && c.Ahead.Trait == Trait.Wall) R.A(-8, "He races hard");
                },
                OnCritSuccess = c => { c.Overtake(c.Car, 1); c.Slow(c.Ahead, 0.6); c.Mom(2); },
                OnSuccess = c => { c.Overtake(c.Car, 1); c.Mom(1); },
                OnNeutral = c => c.R.AddFairing(1),
                OnFail = c => { c.Slow(c.Car, 0.5); c.Dmg(c.Car, 5); c.R.AddFairing(2); },
                OnCritFail = c => { c.R.AddFairing(2); c.Wreck(c.Car, c.Ahead, "gets tangled door to door"); },
            };
            yield return new CardDef
            {
                Id = "k_cover", Name = "Cover the Inside", Family = Defend, Rarity = Rarity.Common, Kart = true, Toss = TossKind.Chain,
                Next = new[] { "k_smooth", "k_tow", "k_show" },
                Text = "Shut the door into the corner. Karts behind barely get by you for 18 s, and it shuts down a cutback after your Late Brake.",
                Real = "Moving to the inside before the braking zone so nobody can dive under you.",
                Can = c => c.Behind == null ? "No one behind" : c.GapB > 1 ? "No one close behind" : null,
                Read = (c, R) =>
                {
                    R.B(54);
                    if (c.GapB < 0.3) R.A(12, "He's on you"); else if (c.GapB < 0.7) R.A(4, "Close behind"); else R.A(-10, "Early");
                    if (c.Seg.Approach) R.A(10, "Corner entry");
                    if (c.R.KRepass != null) R.A(10, "He wants it back");
                },
                OnCritSuccess = c => { c.BlockFor(c.Car, 25); c.R.KRepass = null; c.Mom(1); },
                OnSuccess = c => { c.BlockFor(c.Car, 18); c.R.KRepass = null; },
                OnNeutral = c => c.BlockFor(c.Car, 6),
                OnFail = c => { if (c.Behind != null) c.Overtake(c.Behind, 1); },
            };
            yield return new CardDef
            {
                Id = "k_smooth", Name = "Carry the Speed", Family = Car, Rarity = Rarity.Common, Kart = true, Toss = TossKind.Focus, TossValue = 0.25,
                Next = new[] { "k_tow", "k_outside", "k_show", "k_switch" },
                Text = "Clean lines and no scrubbing: +3% speed for 12 s and tire wear -40%.", Real = "Rolling more speed through the corner instead of sliding the kart.",
                Read = (c, R) =>
                {
                    R.B(60);
                    if (c.Seg.Kind == TurnKind.Sweeper) R.A(10, "Carry speed through the sweeper"); else if (c.Seg.Kind == TurnKind.Hairpin) R.A(-8, "Hairpin: nothing to carry");
                    if (c.Behind != null && c.GapB < 0.35) R.A(-10, "Pressure behind");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.04, 14); c.WearFx(c.Car, 0.5, 20); c.Mom(1); },
                OnSuccess = c => { c.Pace(c.Car, 1.03, 12); c.WearFx(c.Car, 0.6, 15); },
                OnNeutral = c => c.Pace(c.Car, 1.01, 6),
                OnFail = c => c.Pace(c.Car, 0.98, 6),
            };
            yield return new CardDef
            {
                Id = "k_limit", Name = "On the Limit", Family = Car, Rarity = Rarity.Common, Kart = true, TireCost = 8, Toss = TossKind.Chain,
                Next = new[] { "k_outside", "k_latebrake" },
                Text = "Wring its neck: +5% speed for 9 s.", Real = "Driving every corner on the edge of grip.",
                Read = (c, R) =>
                {
                    R.B(56);
                    if (c.Tires > 75) R.A(14, "Fresh tires"); else if (c.Tires < 45) R.A(-22, "Worn tires");
                    if (c.Car.Damage > 20) R.A(-10, "Bent kart");
                },
                OnCritSuccess = c => { c.Pace(c.Car, 1.06, 10); c.Mom(1); },
                OnSuccess = c => c.Pace(c.Car, 1.05, 9),
                OnNeutral = c => c.Pace(c.Car, 1.02, 6),
                OnFail = c => { c.Slow(c.Car, 0.5); c.Dmg(c.Car, 6); },
                OnCritFail = c => c.Wreck(c.Car, null, "spins it into the tire wall"),
            };
            yield return new CardDef
            {
                Id = "k_board", Name = "Read the Board", Family = Crew, Rarity = Rarity.Common, Kart = true, Toss = TossKind.Focus, TossValue = 0.3,
                Next = new[] { "k_show", "k_outside", "k_tow", "k_latebrake", "k_switch" },
                Text = "Your crew hangs the pit board on the fence. Your next card gets +18.", Real = "Gaps and laps chalked on a board held over the fence.",
                Read = (c, R) => { R.B(70); R.A((c.R.Loadout.Spotter - 5) * 2, "Crew skill"); },
                OnCritSuccess = c => { c.Buff(22, "Read the Board"); c.Mom(1); },
                OnSuccess = c => c.Buff(18, "Read the Board"),
                OnNeutral = c => c.Buff(10, "Read the Board"),
            };
        }
    }
}
