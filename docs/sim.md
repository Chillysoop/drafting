# The race sim (roadmap step 26)

`game/Packages/com.greenflag.sim/Runtime/` is the race, as plain C# with no engine reference. The same files compile into the Unity game and into `sim/GreenFlag.Sim` for CI. It is a port of the slice's race loop (`update`, `autoMove`, pit road and cautions in `prototype/green-flag-deck.html`, slice v47) with the same rules and numbers. Cards, odds, combos, beats, Focus and radio calls come in step 27 and plug into the effect methods listed below.

## How a race runs

```csharp
var race = new Race(RaceSetup.Quick(Tracks.Carver, seed: 61));
while (!race.Done)
{
    race.Tick();                       // one fixed 0.05 s tick (SimInfo.TickHz = 20)
    foreach (var e in race.Events) { } // what happened this tick: flags, passes, wrecks, pit stops
}
RaceResult result = race.Result();
```

Every random roll comes from `race.Rng` (a `SimRng` seeded from `RaceSetup.Seed`). Nothing reads the clock, the engine or a global, so the same setup and seed give the same race on every machine. `race.Fingerprint()` hashes the final order, every car's distance, tires and damage bit for bit, and the RNG state. The game shows it on the boot screen and CI compares it across runs.

Slow-mo and beat pauses (step 27 and 31) change how many ticks run per real second, never the tick length.

## Files

| File | What it holds | Slice source |
|---|---|---|
| `Data/TrackDef.cs` | The ten tracks: Millbrook, Carver, Talon Ridge, Pinewood, Ridgeback, the two kart circuits, Cedar Bowl, Millbrook (late model length), Crossroads Figure-8 | `TRACKS`, `PIT.fuelRange` |
| `Data/Field.cs` | Car stats, traits, teams per series, the 20-car field builder | `TEAMS`, `SERIES_TEAMS`, `TRAITS`, `buildField` |
| `Data/Difficulty.cs` | Easy to Brutal | `DIFF` |
| `Data/SimBalance.cs` | The race-core dials of the `engine` preset, plus pit, damage-zone, kart and late model constants | `BAL_PRESETS.engine`, `PIT`, `DZ`, `KART`, `LATE` |
| `Track/TrackShape.cs` | Lap length, turn kinds (hairpin, esses, sweeper), the speed profile of non-ovals, pit road, minimum car gap | `perim`, `TG`, `segOf`, `uOf`, `pitGeom` |
| `Race/Race.cs` | Setup, the tick, pace, draft, tire wear, damage, wrecks, the no-pass clamp, the finish | `startRace`, `update`, `paceMult`, `dmg`, `wreck`, `overtake` |
| `Race/Race.Ai.cs` | The AI move every 15 to 20 s, wreck hazards and the Evade window, the figure-8 crossover | `autoMove`, `maybeHazard`, `endHazard`, `fig8Tick` |
| `Race/Race.Pit.cs` | Fuel windows, AI and crew chief plans, the PIT button, stop times, driving pit road | `fuelWindow`, `aiPlan`, `myPlans`, `stopTime`, `pitV`, `pitStep` |
| `Race/Race.Caution.cs` | The caution sequence, crew chief calls, lucky dog, wave-around, pit-road-closed penalty, pace car | `startCaution`, `cautionTick`, `oneToGo`, `aiCautionCall`, `physAhead` |
| `Race/RaceSetup.cs` | `RaceSetup`, `PlayerMods`, `RaceResult` | |

## What your car can do in step 26

Your car races with no cards: AI moves still run for it (it works the draft and saves tires but never passes on autopilot outside Easy, as in the slice). The player-side API so far:

- `PressPit()` is the PIT button: on green it toggles a stop for the plan's service, under caution it toggles stay out / pit.
- `SetCautionCall(PitService)` picks your caution call. Left alone, the crew chief decides (`Caution.Chief`).
- `Strategy` picks the Safe or Gamble plan (`SafePlan`, `GamblePlan`, `MyPlan`).
- `EvadeHazard()` dodges a wreck-ahead or crossover hazard. Step 27 replaces it with the Evade card and its odds.
- `PlayerMods` carries what your build does to the race (pace, wear, draft, damage, pit seconds, Launch Control, crew chief). It is all 1s for now; step 27 fills it from the persona, tune, setup and parts.

The effect methods cards will call: `AddPace`, `Slow`, `WearFx`, `BlockFor`, `HoldFor`, `StartOvertake`, `AddDamage`, `Wreck`. The AI already reads `Heat`, `LastPlayAt` (idle pressure) and `PlayerPlays` (the crew chief's smarter calls once you have played a card).

## Not in the sim yet

- Cards, odds, outcomes, combos, Heat gain, beats, Focus, toss effects, the reel-in card, kart tow pips and the partner push: step 27.
- The pit stop mini-game: your stops use the crew chief's stop time for now (step 27 or 33).
- Radio and crew calls: step 33.
- Rivals' Grudge, payback and blocking: step 37 (career).
- Practice mode: after step 36.
- Visual-only rolls (side-by-side moments, wiggles, spin direction, smoke) are not in the sim at all. They belong to the presentation's own random generator.

## Checking it

```
dotnet test sim/GreenFlag.Sim.sln                                    # 51 tests
dotnet run --project sim/GreenFlag.Harness -c Release -- race carver 61   # one race: event log, result, fingerprint
dotnet run --project sim/GreenFlag.Harness -c Release -- batch 100        # hands-off averages per track
dotnet run --project sim/GreenFlag.Harness -c Release -- check 5          # every track, 5 seeds, run twice (CI)
```

The golden-seed tests pin four exact races. A change that moves them is a sim change; if it was on purpose, paste the new fingerprints from `race <track> <seed>` into `RaceTests.GoldenSeeds`.

## First comparison with the browser slice

Hands-off on Normal, engine preset. Slice: `prototype/harness/sim-harness-v9.js` with `_p: engine`, policy `none`, 30 races each for Drafter and Tire Whisperer. C#: `batch 100`, no persona (the slice's hands-off car carries its persona's stat bonus and part).

| Track | Cautions, slice | Cautions, C# | Race length s, slice | C# | Finish vs expected, slice | C# |
|---|---|---|---|---|---|---|
| Millbrook | 1.7 to 2.2 | 1.8 | 501 to 512 | 502 | +0.2 to +0.7 | +0.1 |
| Carver | 1.1 to 1.2 | 1.4 | 472 to 479 | 484 | −0.9 to −1.0 | +0.6 |
| Talon Ridge | 1.1 to 1.3 | 1.3 | 491 to 505 | 505 | +0.2 to −1.1 | +0.2 |
| Pinewood | 1.8 to 1.9 | 1.8 | 490 to 494 | 495 | −0.7 to 0 | +0.2 |
| Ridgeback | 1.4 to 1.6 | 1.4 | 504 to 505 | 504 | −0.8 to +0.2 | +0.5 |

The race core lines up. The C# car finishes slightly better at Carver and Ridgeback, probably because its stops use the crew chief's stop time instead of the pit mini-game's auto release (not checked yet). Step 28 makes this a proper parity check, with cards, per persona and per play style, within half a place.
