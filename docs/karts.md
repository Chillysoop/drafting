# Kart series (roadmap step 40)

A full kart season in the engine: its own deck (the slice's 12-card kart deck from step 27), four kart circuits of different shapes, a kart field that races like karts, and a harness check that a season hits the targets the browser slice set.

## The circuits: `Data/TrackDef.cs`

Two new circuits join Pinewood (kart length) and Hollow Creek. Both are built from straights and turns like the slice's path tracks, and the pieces close on themselves (`KartSeriesTests.KartCircuitsCloseOnThemselves`). They go on the end of `Tracks.All`, so the browser harness's track indexes still match.

| Circuit | Laps | Shape | Tow | What it asks for |
|---|---|---|---|---|
| Pinewood Kart Park (`pinek`) | 13 | zig-zags, a chicane, esses, two hairpins | little | a bit of everything (opens the season) |
| **Bramble Hill Kart Club** (`bramble`) | 15 | tight: two hairpins (Bramble, Thicket), a chicane, esses, no long straight | least of any kart track | Show the Inside, Switchback, hairpin dives. At least 3 Passing cards in the deck |
| Hollow Creek Kartway (`hollow`) | 14 | a long straight into the Creek Hairpin, a sweeper | medium | the tow, then the hairpin |
| **Lakeshore Kartway** (`lake`) | 12 | fast: two long straights, the Lakeshore Sweeper, the Boathouse Hairpin, the Dune Kink | most of any kart track | Tuck In the Tow, Push Your Partner, Pull Out. At least 2 Drafting cards |

The kart calendar (`CareerRules.Calendar`) visits all four: Pinewood, Bramble Hill, Hollow Creek, Lakeshore, Bramble Hill, Lakeshore (the finale). Six races, as before (board Q82). Qualifying sweeps: Bramble Hill 1.5 s like the other tight tracks, Lakeshore 1.35 s.

## The kart field: `Race/Race.Ai.cs`, `Data/Field.cs`

Karts have no restarts, so a kart field rolls Rookies where a stock field rolls Restarters (same rolls, so the field is otherwise the same car for car). On kart tracks the AI styles race like karts (`SimBalance.KartField`, on by default):

- **Train rats** (Drafter): +8 on a pass when they sit in a train of karts.
- **Late brakers** (Closer): +8 on a pass at a hairpin.
- **Bumpers** (Aggressive): a stalled move ends with a shove on the kart ahead (35%). On you it pushes your front fairing in one notch (the third notch is the slice's +5 s at the flag). Event `KartBump`.
- **Blockers** and anyone else who blocks you also **cover the inside** for 6 s, so Switchback (+20) and Around the Outside (+12) read better against them. Event `KartCover`.
- **Rookies** get flustered (−6) passing a kart that covers the inside.

`FieldBuilder.KartStyle` names the styles for screens (Bumper, Tidy, Blocker, Train rat, Late braker, Rookie, Old hand); no screen shows them yet. With `KartField` off the races are the slice's, so `parity` (stock tracks only) is unchanged. The golden seed for Hollow Creek moved on purpose.

## The harness: `kartseason [n] [seasons] [seed] [on|off]`

`sim/GreenFlag.Harness/KartSeason.cs`. Part one plays every kart circuit with each of the four kart personas, n seeds each, with the browser harness's bots (smart = careful play, none = hands-off, spam). Part two plays whole kart seasons through the career menus (MenuBot in the garage and at the off-track events, the smart bot in the races), one per persona and seed, to the season review. It prints a PASS or FAIL line per target and exits 1 if any fail. CI runs `kartseason 10 3 61`.

| Target | From | Seeds 61 | Seeds 1000 |
|---|---|---|---|
| careful play vs expected, all circuits: +8 to +14 | slice karts +10.8 | +9.7 | +11.3 |
| careful play vs expected on each circuit: +6 to +16 | | +9.3 to +10.0 | |
| hands-off vs expected: −4 to +1 | slice −1.4 | −1.3 | −0.5 |
| card spam DNF: 15% to 40% | slice 30% | 33% | 29% |
| pass cards, share of careful plays: 20% or more | stock 17% | 27% | 27% |
| best combo chain a race: 3.5 or less | kart cap ×3 | 3.0 | 3.0 |
| draft cards fewest at Bramble Hill, most at Lakeshore | | yes | yes |
| season: careful average finish P3 to P8 | slice karts about P5 | P4.2 | P5.3 |
| season: share of kart races won 10% to 45% | slice 29% | 40% | 32% |
| season: careers in the top 14 (a late model offer): 75% or more | | 100% | 100% |

The kart field costs careful play about a place and a half a race (with it off, the same seeds give +11.2). The mouse bot (human timing, used by `career`) finishes kart seasons around P9; it was about the same before this step.

## Saves: format 5

The season's calendar goes in the save (`calendar` in the season block, `Career.Cal`), so a season that is under way keeps its tracks when a later build changes the calendar. `Career.MigrateV4`: a kart season that already ran a race keeps the old Pinewood and Hollow Creek calendar until it ends; one that hasn't started gets the new one; a kart field's Restarters become Rookies. The calendar resets at the season wrap and on promotion.

Two older save bugs turned up in the reload runs (`saves 9 linepainter 2`) and are fixed: the "+1 Crew chief" contract clause from step 38 only bumped the loadout, so the next crew sync or a reload dropped it (it now levels up your crew chief), and parts went into the race in the order you fitted them but in slot order after a reload (now always slot order). CI now also runs that reload check.

## Checks

- `KartSeriesTests.cs` (12 tests, 15 cases): the circuits close, their shapes and names, the calendar, track order, the kart field's rolls, bumps and covers (and none off kart tracks or with the field off), Switchback reading a cover, a v4 kart save mid-season keeping its old tracks and the next season getting the new ones, a kart season through the menus on all four circuits, and the two save fixes.
- `frames bramble` and `frames lake` in CI draw the new circuits from the view model. Screenshots of both are in the project files under `design/kart-series/`.

## What is not verified

No Unity editor has run this (step 36 waits on the Unity install). The new circuits come from the same view model as the others (`TrackLayout`), so they should draw in the engine, but nobody has seen them there.
