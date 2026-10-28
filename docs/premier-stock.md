# Premier stock series (roadmap step 42)

The top stock car season in the engine. Every stock car track is now one of four kinds, superspeedway, intermediate, short track or road course, and each kind has its own rules. The rules move card reads and pace so that each kind wants a different race deck and setup. Three new tracks fill out a ten-race premier calendar, and a harness check shows that each kind of track has a different best deck.

## The four kinds of track: `Data/TrackDef.cs` (`TrackDef.Kind`)

A stock car track's kind comes from its numbers: any non-oval is a road course, an oval with a big draft (`DraftBias` 0.8 or more) is a superspeedway, a tight oval with lots of contact (`Contact` 1.2 or more) is a short track, and the rest are intermediates. Kart and late model tracks have no kind (`TrackKind.None`) and run as before.

## The rules: `Race/Race.Rules.cs`

On by default (`SimBalance.StockRules`). Every read the rules add is tagged `rules` and shows on the card with its reason.

| Kind | Rule | What it does |
|---|---|---|
| Superspeedway | Pack racing | Drafting cards read +6 with a car pushing you (behind, inside the draft). Passing cards read −10 ("single file: pass with the draft"). |
| Intermediate | Clean air and tire fall-off | Passing or drafting a car less than 0.5 s ahead is dirty air, −10, unless your tires are 10 points fresher than his (+6). Save the Tires, Hot Laps and Run the High Line read +8 (long green runs). Every car loses an extra 0.06 of pace per unit of tire wear (`IntFalloff`), so tires fall off harder. Clear Air gets the +6 the slice's card gives at Carver on every intermediate. |
| Short track | Beating and banging | Bump and Run reads +10 and can be played from 1.0 s back instead of 0.7. Block reads +8. Dive Bomb reads −12 (he's protecting the bottom), drafting −8, and a loose setup costs passing and high-risk cards 4 a notch. |
| Road course | Braking zones | Passing cards read +8 into any corner (on top of the hairpin read from step 8c). Drafting reads −14 except on the long straight. Bump and Run reads −8 outside a hairpin, Block −6 (two lines into every corner). Your aero setup moves pace: 0.2% a notch, downforce faster (`RoadAero`). |

The garage's next-race panel and the deck builder show the rules for the next track in one line (`TrackRules.Text`). The deck builder's fit sort and Auto-build weigh them (`TrackRules.CardFit`), and the garage's tune chip counts downforce on road courses and a loose car on short tracks (`TrackRules.SetupFit`).

With `StockRules` off the races are the browser slice's, and `parity` and `json` turn it off, so parity with the slice is unchanged.

## The new tracks and the calendar

Three tracks go on the end of `Tracks.All`, so the browser harness's track indexes still match.

| Track | Kind | Laps | What it asks for |
|---|---|---|---|
| **Harbor Point Speedway** (`harbor`) | Intermediate | 16 | A worn 1.5-mile oval (wear 1.3, less draft than Carver). At least 2 Car cards. The finale. |
| **Ironside Speedway** (`iron`) | Short track | 26 | A flat paperclip half-mile: long straights into tight corners, no draft, lots of contact. At least 2 Defending cards. |
| **Copper Canyon Raceway** (`copper`) | Road course | 10 | A long back straight into the Copper Hairpin, a chicane, the Mesa Sweeper and the Canyon Kink. Three braking zones a lap are draw beats. At least 2 Passing cards. |

The premier calendar (`CareerRules.Calendar(Series.Stock)`): Talon Ridge, Carver, Millbrook, Ridgeback, Harbor Point, Ironside, Talon Ridge, Copper Canyon, Millbrook, Harbor Point. That is two superspeedway races, three intermediates, three short tracks and two road courses (ovals plus 2 or 3 road courses, board Q35). Pinewood Kart Park is off the stock calendar (board Q90); it is still a stock car track for the test track.

## The harness: `stockseason [n] [seasons] [seed] [on|off]`

`sim/GreenFlag.Harness/StockSeason.cs`, in CI as `stockseason 10 3 61`. It prints a PASS or FAIL line per target and exits 1 if any fail. `off` runs the slice's rules for comparison (the targets that need the rules are skipped). For tuning, `STOCK_PARTS=decks,setups` and `STOCK_TRACKS=mill,iron` limit a run, and `STOCK_V=1` prints each deck's plays and landing rate per card.

1. **Races.** Every premier track with the five stock personas, n seeds each, played by the browser harness's bots (smart, none, spam).
2. **Decks** (the "done when"). Four race decks, each the stock car base deck plus six cards and its own setup, raced by the smart bot with the same rookie car and no part, 4n seeds per track:

   | Deck | Built for | Six cards | Setup |
   |---|---|---|---|
   | Draft pack (`draft`) | superspeedway | Bump Draft, Slingshot, Draft Train, Tuck In, Side Draft, Push to the Limit | Handling 0, Aero +2 (low drag), Draft focus |
   | Long run (`longrun`) | intermediate | Save the Tires ×2, High Line ×2, Hot Laps, Clear Air | Handling −1, Aero −1, Long-run focus |
   | Bumper (`bumper`) | short track | Bump and Run ×3, Block ×2, High Line | Handling 0, Aero −2, Bumper focus |
   | Braking zones (`braking`) | road course | Dive Bomb ×2, Crossover ×2, Push to the Limit, Block | Handling +1, Aero −2, Bumper focus |

   The best deck on each kind of track must be the one built for it, by 0.8 places or more, so the four kinds have four different best decks.
3. **Setups.** One all-round deck (one each of Bump Draft, Slingshot, Save the Tires, High Line, Bump and Run, Block) with each of the four setups. The best setup must not be the same everywhere.
4. **Seasons.** Whole premier seasons through the career menus, one per persona and seed (MenuBot in the garage and deck builder, the smart bot in the races).

Places vs expected, by kind of track (seeds 61, 40 races per track):

| Kind | draft | longrun | bumper | braking | best |
|---|---|---|---|---|---|
| Superspeedway | **+12.6** | +3.8 | +4.0 | +5.8 | draft by 6.8 |
| Intermediate | +3.9 | **+8.3** | +3.3 | +4.4 | longrun by 3.8 |
| Short track | +1.0 | +2.1 | **+5.0** | +2.8 | bumper by 2.2 |
| Road course | +5.7 | +5.8 | +7.2 | **+9.9** | braking by 2.7 |

With the slice's rules (`off`) the Draft pack was best on the superspeedway, the intermediate and Ridgeback, and the Long run deck was best nowhere.

| Target | Seeds 61 | Seeds 1000 |
|---|---|---|
| careful play vs expected, all premier tracks: +5 to +14 (slice rules +6.6) | +6.0 | +6.5 |
| careful play vs expected on each track: +2 to +16 | +2.1 (Ironside) to +9.8 (Copper Canyon) | |
| hands-off vs expected: −4 to +1 | −0.6 | +0.1 |
| card spam DNF: 40% to 85% (slice rules 62%) | 69% | 73% |
| each kind's own deck is best, by 0.8 or more | yes, 2.2 to 6.8 | yes, 2.1 to 8.8 |
| four different best decks | yes | yes |
| the best setup differs by kind (3 or more) | draft, longrun, bumper, bumper | draft, longrun, bumper, braking |
| season: careful average finish P5 to P11 (slice rules about P8) | P10.1 | P8.7 |
| season: share of premier races won, 3% to 35% | 4.7% | 7.3% |
| season: careers in the top 10 of the points, 50% or more | 53% | 80% |

What these say: the rules make the premier season about a place to a place and a half harder for the five personas (P8.1 with the slice's rules on seeds 61, four seasons each), because each persona's deck fits one or two kinds of track and not the rest. A Drafter at Ironside gets 1 to 7 plays a race. That is on the Decision Board.

## Saves: format 7

No new fields. `Career.MigrateV6`: a stock car season that hasn't run a race yet gets the premier calendar; one under way keeps the calendar it saved (`Career.StockCalendarV6`, the old one with Pinewood). `MigrateV4` now gives a v4 stock season the v6 calendar, so the chain is right for those too. New sample: `v7-bully-premier-race-4`. CI also runs `saves 10 bully 5 standard`.

## Other changes

- The race view's wide shot leaves 10% more room (Copper Canyon is tall, and its near corner swelled out of frame in perspective). Every track zooms out a touch.
- The pylon's short name drops "Raceway" (Copper Canyon).
- The mouse and controller tests race the Bully on short tracks: the Drafter's superspeedway deck has too few plays at Ironside by design.
- Golden seed `carver 61` changed on purpose (intermediate tire fall-off).

## Checks

- `StockSeriesTests.cs` (11 tests): the premier calendar's kinds and order (no Pinewood), every track's kind, the new tracks on the end, each kind's reads, the switch and other series, the loose-setup rule, the intermediate fall-off and road course aero pace, the deck builder's score and tune chip, the garage and deck builder text, a full premier season through the menus, and the save migrations.
- `frames harbor`, `frames iron` and `frames copper` in CI draw the new tracks from the view model. Screenshots are in the project files under `design/premier-stock/`.

## What is not verified

No Unity editor has run this (step 36 waits on the Unity install). The three new tracks are drawn from the same view model as the others, and Copper Canyon is a closed path like Ridgeback, so they should draw in the engine, but nobody has seen them there. The rules lines in the garage and deck builder are drawn by the same IMGUI menu code as the other text.
