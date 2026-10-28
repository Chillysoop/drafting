# Late model series (roadmap step 41)

A full late model season in the engine: two new tracks, an eight-race calendar that is mostly short ovals plus the figure-8 and a road course (board Q35), a late model field that runs its own tire strategies, and a harness check that a season hits the targets. Late models are where pit stops and tire strategy start: karts never pit, and late model races all need at least one stop on fuel.

## The tracks: `Data/TrackDef.cs`

Two tracks join Cedar Bowl, Millbrook (late length) and the Crossroads Figure-8. They go on the end of `Tracks.All`, so the browser harness's track indexes still match.

| Track | Laps | Shape | What it asks for |
|---|---|---|---|
| Cedar Bowl (`cedar`) | 24 | quarter-mile bullring | Defending (min 2), opens the season |
| **Granite Falls Speedway** (`gran`) | 26 | high-banked half-mile, the hardest on tires of any late model track (wear 1.45) | the tire race: fuel range 12, so two stops; at least 2 Car cards (Save the Tires and friends). The finale |
| Millbrook (`milll`) | 22 | short track | Defending (min 2) |
| Crossroads Figure-8 (`cross`) | 21 | figure-8 with the crossover | at least 1 Car card |
| **Ridgeback Road Course** at late length (`ridgel`) | 9 | the stock car road course, same layout | at least 2 Car cards |

The calendar (`CareerRules.Calendar(Series.Late)`): Cedar Bowl, Granite Falls, Millbrook, Crossroads, Cedar Bowl, Ridgeback, Millbrook, Granite Falls. Eight races, as before (board Q82). Qualifying sweeps: Granite Falls 1.4 s, Ridgeback 1.5 s like the stock version.

## The late model field: `Race/Race.Ai.cs`, `Race/Race.Caution.cs`

On late model tracks (`SimBalance.LateField`, on by default) the AI runs tire strategies by trait (`Race.LateStyleOf`, names for screens in `FieldBuilder.LateStyleName`):

- **Tire savers** (Smooth, Veteran) stay out under caution on tires a charger would change (50% or better, no fuel needed, light damage): they keep track position.
- **Chargers** (Aggressive, Closer) take four tires under caution whenever there are more than 3 laps to go and their tires are under 85% (or they need fuel).
- **The fresh-tire charge**: any car passing a car on much older tires gets +0.4 read per point of tire difference past 15, up to +12 (`Race.LateRead`); chargers get twice that. It works against you too, so staying out on old tires has a cost. Your own cards already read tires ("Fresh tires", "Fresher than him", "Worn tires").

So a restart mixes cars on old tires up front with chargers on fresh ones behind. With `LateField` off the races are the slice's, and `parity` (stock tracks only) is unchanged. `Race.AiCalls` counts the AI's caution calls for the harness.

## Starting in late models

`Career.New(..., start: Series.Late)` starts a stock car persona one rung lower, as the slice's "Start in" did. The title screen shows a **Start in** section (Late models / Stock cars) when a stock persona is picked; kart personas always start in karts. Until the first late model race is run, the garage's next-race panel explains the new layer: the crew chief's pit plan, the PIT button, and the stay / fuel / two / four call under caution.

## The harness: `lateseason [n] [seasons] [seed] [on|off]`

`sim/GreenFlag.Harness/LateSeason.cs`, in CI as `lateseason 10 3 61`. Part one plays every late model track with the five stock personas, n seeds each, with the browser harness's bots (smart, none, spam). Part two is the tire strategy check: the smart bot again with its caution calls made four ways (`Bots.CautionPolicy`: its own tire rule, the crew chief's call, always stay out, always four). Part three plays whole late model seasons started in late models through the career menus. It prints a PASS or FAIL line per target and exits 1 if any fail.

| Target | From | Seeds 61 | Seeds 1000 |
|---|---|---|---|
| careful play vs expected, all tracks: +8 to +14 | slice late models +10.6 | +9.4 | +10.6 |
| careful play vs expected on each track: +6 to +16 | | +8.3 to +11.4 | +10.2 to +11.3 |
| hands-off vs expected: −4 to +1 | slice −0.3 | −0.1 | 0.0 |
| card spam DNF: 30% to 65% | slice 42% at step 22; step 23 made fields tougher | 50% | 58% |
| pit stops a careful race: 1 or more | | 1.2 | 1.3 |
| field caution calls that stay out: 20% to 60% | | 56% | 59% |
| field caution calls for four tires: 20% or more | | 35% | 33% |
| crew chief's calls vs always taking four: +2 places or more | | +4.1 | +4.0 |
| always staying out doesn't beat the crew chief: −0.5 or more | | +1.2 | +0.3 |
| Granite Falls has the most pit stops a race | | yes (1.84) | yes |
| season: careful average finish P3 to P8 | slice about P4 | P6.8 | P5.6 |
| season: share of races won 15% to 55% | | 22% | 30% |
| season: careers in the top 14 (a stock car offer): 75% or more | | 100% | 100% |

What the tire strategy numbers say: taking four tires at every caution throws away 3 to 4 places a race. Staying out every time is a real strategy and close to the crew chief's mixed calls (0.3 to 1.2 places behind, most at Granite Falls). That is on the Decision Board (Q133): whether old tires should hurt more.

Two balance levers were tried and not kept: harder late model tire fall-off (TireGrip 0.12 and 0.16 on late tracks) and a stronger fresh-tire charge (0.7 a point, cap 18). Neither made staying out cost more; the gap stayed inside the noise.

## Saves: format 6

No new fields. `Career.MigrateV5`: a late model season that hasn't run a race yet gets the new calendar; one under way keeps the calendar it saved (`Career.LateCalendarV5`). `MigrateV4` now gives a v4 late model season the v5 calendar, so the chain is right for those too. New sample: `v6-whisperer-late-models-race-4` (a career started in late models). CI also runs `saves 9 whisperer 4 standard late`.

## Checks

- `LateSeriesTests.cs` (10 tests): the calendar's shapes and order, track order, Granite Falls as the tire race, Ridgeback sharing the road course, the styles by trait, the fresh-tire read (and none off late model tracks or with the field off), savers and chargers under caution, starting in late models (and the errors for the wrong persona), the title screen's Start in and the garage note, a full late season through the menus, and the save migrations.
- `frames gran` and `frames ridgel` in CI draw the new tracks from the view model. Screenshots are in the project files under `design/late-models/`.

## What is not verified

No Unity editor has run this (step 36 waits on the Unity install). Granite Falls is an oval built from the same view model as the others, and Ridgeback at late length is the same layout as the stock one, so both should draw in the engine, but nobody has seen them there. The title screen's Start in section is drawn by the same IMGUI menu code as the other chips.
