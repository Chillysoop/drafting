# Rewards, garage and shop (roadmap step 34)

Race, rewards, shop and the next race now loop in the engine build. The rules are a port of slice v47's `endRace`, `showReward`, `showGarage`, `showDeckBuilder` and the deck rules from step 15b; the screens are engine-free pages that Unity draws.

## The sim: `game/Packages/com.greenflag.sim/Runtime/Career/`

- `Career.cs`: a career between races. Money ($6,000 to start), the card collection and the race deck, parts owned and fitted (one per slot: engine, aero, chassis, tires, crew), the car tune, per-card history (plays and successes), the deck you raced at each track last time, results, the parts dealer and the reward screen. `NextRace()` builds the next race's `RaceSetup` (you play your own pit stops); `FinishRace(race)` pays out and rolls the rewards. Every roll uses the career's own generator, so a career replays the same from its seed (tested).
- `CareerRules.cs`: the numbers. Purse `(1500 + (21 − finish) × 350) × mul`, a bonus of `400 × mul` per place beaten under your expected finish, with `mul` = AI skill purse × (1 + Information bonus + Stakes bonus) × the series' pay (karts 15%, late models 30%, stock cars 40%, step 23). Tiers: a DNF or 4+ places behind expected is a rough day, a win or a podium 8+ places ahead is victory lane, 4+ ahead is a strong run, anything else is solid. Rerolls cost $1,000 / $1,500 / $2,000 / $2,500 by AI skill, plus $1,000 for each reroll before it. Shop: car stats cost $3,500 + $1,500 per level above 3, driver coaching $3,000 + $1,200 per level above 4, up to 10; the parts dealer's part is $7,000.
- `DeckRules.cs`: the race deck rules. 12 to 16 cards in a stock car (karts 10 to 13, late models 11 to 15) and Aero moves the max; each track's family minimum; slot caps Attack 6 + Handling, Defense 6 − Handling, Drafting 5 + Aero, plus one for the combo focus's group. `Fix` keeps what's legal, meets the minimums and fills; `Auto` fills to the max with the best cards for the track and tune; `Diff` says what the crew changed when a deck broke a new track's rule.
- `Race.PlayLog` (in `Race.Cards.cs`): every card you played with the leader's lap, the odds you played it at, the outcome and its three biggest factors. It feeds "Your calls". It rolls nothing, so race fingerprints did not move.

The reward screen offers 3 cards (2 after a rough day; persona cards 1.8× as likely, victory doubles anything above common, a rough day makes rares rarer, and a strong run or better always shows an uncommon or rare), or $1,000 to skip. A strong run or victory also offers 2 parts. Both reroll for money. Ironman and Permadeath DNFs cost $3,000 and a random spare part; Permadeath's one Survive token saves the first DNF, the second ends the run. On Standard stakes you can retry the race from the reward screen (the career goes back to how it was before the race and the race runs again with a new seed).

## The screens: `game/Packages/com.greenflag.view/Runtime/CareerMenu.cs`

`CareerMenu` builds a `MenuPage` (header, sections in two columns, an action bar) for each screen and takes button presses by id:

- **New career**: persona (kart personas start in the kart series), AI skill and Stakes. The Information dial isn't offered yet because the engine race always shows the odds; it only sets pay in the sim.
- **Garage**: the next race, the car tune (Handling, Aero, combo focus, the fit chip and what the tune does), shop upgrades, the parts dealer (buy or reroll), equipment (fit and take off parts; the slice keeps this on the car sheet) and the race deck panel with its rules. Go racing is off while the deck breaks a rule.
- **Deck builder**: your collection as cards with + and −, sort (family, best here, your success, rarity, name) and family tabs, the race deck's rules, Auto-build, Fix rules, Last deck here, Fill, Clear, and the tune.
- **Reward screen**: P and tier, payout, top 10, the card pick, the strong-run part pick, rerolls, skip, "Your calls", Back to the garage, Retry this race.

`MenuFocus` walks every control in order for a controller or the arrow keys, starting on each page's main control (Go racing; the first card on the reward screen, so a quick A at the flag can't skip the rewards). `MenuBot` plays the screens through their buttons only.

## Unity

`game/Assets/GreenFlag/Presentation/Career/`: `CareerFlow` (the new Career scene, first in the build) shows the menus and, on Go racing, builds a `RaceView` for the career's race. At the flag, Enter or A (or the Rewards button) hands the race back for the payout and the reward screen and the race view is torn down. `MenuHud` draws the pages in IMGUI in the race HUD's plain dark style until the art pass (step 45). Mouse, keyboard (arrows, Enter, Esc) and controller (d-pad or stick, A, B; `PadReader` now feeds the menus too) all work. In a career race R and T (new race, next track) are off. The single-race Race scene still works as before.

## Checks

- 29 new test cases (`CareerTests.cs`, 348 in total): tiers, pay, reroll and shop prices, deck sizes and caps, auto and fixed decks legal for every persona on every track of its series, add-block reasons, parts per slot and per series, card offers per tier, payout and "Your calls" after a real race, skip, Ironman and Permadeath, the deck fix on entering the garage, the menu loop (3 stock races; 7 kart races into season 2), replay from a seed, Go racing locked by a bad deck, deck builder buttons, retry, the reward page, the controller focus.
- `career [races] [persona] [seed]`: a career through the menus with every race played by the mouse bot on the HUD (CI runs 10 stock and 7 kart races). `menus [persona] [seed] [out.html]`: the screens as HTML, laid out as `MenuHud` draws them (CI artifact).
- Race fingerprints and the parity check are unchanged.

## Not here yet

Race objectives, sponsors, salary, points, standings, qualifying and the season wrap came in step 37 ([season.md](season.md)). Still to come: the driver sheet, XP and Hype (38), Crew HQ, crew wages and pit card upgrades (39). The deck builder shows your own success rate on a card once you've played it 3 times; the slice's "base chance" number isn't shown yet.

## What is not verified

No Unity editor has run this yet (Unity isn't installed; step 25). The presentation code compiles against the UnityEngine 2021.3.33 module stubs (the editor bootstrap's new Career scene does not; it needs the editor), and the pages render in the harness's HTML, but nobody has seen the menus or the race-to-menu hand-off in the engine.
