# Cards, odds and the pit stop mini-game (roadmap step 27)

Step 27 puts the slice's card layer into the C# sim: every card as data, the odds model, five-outcome resolution, Heat, tire costs, draw beats, Focus, combos, tosses, the Crew Board, the kart rules and the pit stop mini-game. It is a port of `ctxFor`, `computeOdds`, `playCard`, `resolveAttempt`, `burnCard`, the beat and Focus code and the `pg*` pit game in `prototype/green-flag-deck.html` (slice v47), with the same numbers.

## Turning it on

The card layer runs only when the race has a loadout. Without one the race is exactly the step 26 core, so its golden fingerprints did not move.

```csharp
var setup = RaceSetup.Quick(Tracks.Carver, seed: 61, PlayerLoadout.ForPersona("drafter"));
setup.PitPolicy = PitPolicy.Player;          // you plan and release your own stops
var race = new Race(setup);
while (!race.Done)
{
    if (race.Holding) { race.PitAutoPlan(race.PitGame, true); race.PitGo(); }   // the UI does this from the pit hand
    race.Tick();
    var odds = race.OddsOf(race.Hand[0]);    // five chances, the read, the labelled factors
    race.PlayCard(race.Hand[0].Uid);         // returns why not, or null
}
```

Card rolls use their own generator, `race.CardRng`, seeded from the race seed. Holding a hand you never play leaves every race-core roll where it was, which the tests check on every track.

## Files

| File | What it holds | Slice source |
|---|---|---|
| `Cards/CardDef.cs` | Card fields, families, rarities, outcome tiers, toss kinds, damage zones, family links, attempt times | `CARDS` fields, `LINKS`, `attemptT`, `zoneOf` |
| `Cards/StockCards.cs` | The 24 stock cards plus Evade, Reel Them In and Tape the Grille, each with its read and its five outcomes | `CARDS` |
| `Cards/KartCards.cs` | The 13 kart cards and their card-to-card `Next` combos | `KART_CARDS` |
| `Cards/CardBook.cs` | Lookup, the stock and kart base decks, the kart-to-late-model card swap | `BASE_DECK`, `KART_BASE`, `KART_TO_STOCK` |
| `Cards/CardCtx.cs` | What a card sees (your car, gaps, the track spot) and the outcome helpers; the odds builder, `Odds`, `Erf` and `Phi` | `ctxFor`, `R`, `erf` |
| `Cards/Parts.cs` | All 21 parts, kart parts included | `PARTS` |
| `Cards/Tune.cs` | Handling and aero notches, combo focus, the pit adjustment nudge | `TUNE`, `tuneFx` |
| `Cards/Loadout.cs` | `PlayerLoadout` (car, crew, deck, pit deck, parts, tune, Grit, Crew HQ perks) and the nine personas | `ARCH`, `buildLoadout`, `crewFx` |
| `Pit/PitCards.cs` | The 15 pit cards and their upgrades, Setups, Packages, the starting pit deck and shop | `PIT_CARDS`, `SETUPS`, `PACKAGES` |
| `Race/Race.Cards.cs` | Hand, deck and discard, odds, playing and resolving, combos, Heat, beats, Focus, tosses, Crew Board, Evade and Reel Them In | `draw`, `playCard`, `resolveAttempt`, `burnCard`, `beatTick` |
| `Race/Race.Kart.cs` | Tow pips, fairing penalty, repass, partner push, Show / Outside / Own the Inside | `KART`, `kartTick` |
| `Race/Race.PitGame.cs` | The pit hand, lanes, GO, the release window, loose wheels, Perfect Stops, Setups going stale | `pgOpen`, `pgGo`, `pgRelease`, `setupInstall` |

## The odds

A card's read starts from its own `Read` and adds your stats, crew, parts, tune, Setups, the combo bonus, boosts, Heat, damage in the card's zone, and the track shape. The five chances are bands of a normal curve around the read, with a spread of `max(9, 21 − consistency × 1.1)`: critical failure under 14, failure under 35, neutral under 58, success under 90, critical success above. Cards that can't wreck fold critical failure into failure. Success is capped at 90%. `Odds.Tags` lists the factors worth showing on the card, biggest first, and `Odds.Mods` sums them by kind for the chips.

## The pit stop mini-game

When your car stops in the box, the race deals a pit hand from `PitDeck` (plus Prep cards from the Crew Board and Crew HQ). `PitPolicy` says who runs it:

- `Player`: the race holds (`Holding`) while you toggle cards into lanes with `PitToggle`. `PitGo` rolls them, and you drop the jack with `PitRelease`. More than 1 s early is refused, and early inside that risks a loose wheel. Releasing in the window with at least two cards and no failure is a Perfect Stop (+1 Momentum, full Focus). If you never release, the crew chief does it 5 s after the window.
- `Bot`: plans automatically and releases at a random point in the window (the harness, and the slice's hands-off car).
- `Chief`: plans automatically and releases late (assist mode).

The plan timer (`PgPlanSec`) is UI time. The sim simply waits while you plan.

## Where it differs from the slice on purpose

- **Upgraded Setup cards install at Level II.** The slice's step 7b wrapper around `setupInstall` drops the `two` argument, so a `+` adjustment installs at Level I there. Step 19 meant Level II, so the C# does that (board Q95).
- **Separate card generator.** The slice draws cards and race events from one `Math.random`. C# keeps them apart so the race core stays deterministic whether or not you play (board Q97).
- The timed draw after a caution (one card 3 s after the green, then every 30 s) is ported as it is, quirk included.

## Not in the sim yet

- Radio and crew calls (side bets above the hand): step 33.
- Rivals' Grudge, payback and rival objectives: step 37.
- The plan timer, drag-to-play, slow-mo pacing and the odds display: the UI, steps 29 to 31. `SlowMo(realSeconds, cardSelected)` gives the game the time scale and drains Focus.

## Checking it

```
dotnet test sim/GreenFlag.Sim.sln                                              # 219 tests
dotnet run --project sim/GreenFlag.Harness -c Release -- batch 40 carver smart drafter
dotnet run --project sim/GreenFlag.Harness -c Release -- race carver 61 smart bully
```

`CardTests` has a spec for every card, forcing each outcome it has and checking it does what its text says, and a test that every card in the slice exists as data and nothing else does. `CardLayerTests` covers the odds formula, combos, Heat, tire refunds, beats, Focus, tosses, the Crew Board, cautions, Lucky Lug Nut, damage zones, kart tow pips and the fairing penalty, plus determinism with cards on every track. `PitGameTests` covers the pit hand, holds, release timing, loose wheels, Perfect Stops, Setups, Packages and staleness.

Harness policies: `none` (no cards, Bot stops), `hands` (holds a hand, never plays), `smart` (plays the best card at 65% or better with under 5% wreck chance, Evade at once), `spam` (plays the best card whatever the odds).

## Parity with the browser slice

Step 27's first comparison was 2 to 3 places off. Step 28 found why (a slice bug that made every pit card roll a critical success, and the personas' missing driver Grit) and now checks every play style against the slice on every PR: see [parity.md](parity.md).
