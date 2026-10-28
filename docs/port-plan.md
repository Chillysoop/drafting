# Port plan: browser slice to Unity 6

This is the plan for moving `prototype/green-flag-deck.html` (slice v35) into Unity 6 with C#. It covers roadmap phase C (steps 25 to 36). Phase B (steps 15 to 24) keeps building the career in the browser first, so this plan will be refreshed when step 24 closes and the slice has grown.

## The one rule

The race sim decides everything. It is a plain C# library with no `UnityEngine` reference, seeded and deterministic, and tested with `dotnet test` in CI. Unity draws what the sim says happened: cars on splines, contact, wrecks, cameras, HUD, cards. Nothing the player sees on screen can change a result.

## Repo layout after phase C

```
game/                                Unity 6 LTS project, URP
  Packages/
    com.greenflag.sim/               The sim, as a local UPM package
      Runtime/                       All sim code (asmdef with noEngineReferences: true)
      package.json
  Assets/
    GreenFlag/
      Presentation/                  Track view, car motion, cameras, effects (reads sim state and events)
      UI/                            Hand, beats, pylon, pit box, garage, reward screens (UI Toolkit)
      Data/                          Card, track, persona and tune definitions as JSON or ScriptableObjects
      Scenes/
sim/
  GreenFlag.Sim.sln
  GreenFlag.Sim/                     csproj that compiles game/Packages/com.greenflag.sim/Runtime/**
  GreenFlag.Sim.Tests/               xUnit tests, golden-seed tests
  GreenFlag.Harness/                 Console app: the balance harness, same play styles as prototype/harness
.github/workflows/
  sim.yml                            dotnet test + harness smoke on every PR
  nightly.yml                        GameCI Windows player build every night (step 25)
tools/
  setup-git.ps1, setup-git.sh         One-time Git LFS + Unity Smart Merge setup
```

The sim source lives once, inside the Unity package. The `sim/` csproj points at the same files, so CI can build and test it fast without a Unity licence, and Unity compiles the same code into the game.

## What the slice has today (v35)

One HTML file, about 4,200 lines and 300 functions. The parts that matter for the port:

| Slice system | Where in the slice | C# home | Step |
|---|---|---|---|
| Race state, tick, phases (start, green, caution, restart, finished) | `startRace`, `update(dt)`, `goGreen`, `startCaution`, `cautionTick` | `Sim/Race.cs`, `Sim/RacePhase.cs` | 26 |
| Field, teams, AI moves and AI pit plans | `buildField`, `autoMove`, `aiPlan`, `aiCautionCall`, `maybeHazard` | `Sim/Field.cs`, `Sim/Ai/` | 26 |
| Track data and shapes (3 ovals, Pinewood, Ridgeback) | `TRACKS`, `TG`, `tgSeg`, `shapeOdds`, `shapeAI` | `Data/tracks.json` + `Sim/Track.cs` (sim needs segments only, not geometry) | 26, 29 |
| Pace, draft, tire wear, fuel, damage zones | `pace`, `slow`, `wearFx`, `dmg`, `fuelRange`, `dz*` | `Sim/CarState.cs`, `Sim/Damage.cs` | 26 |
| Pit road, fuel windows, chief calls | `pitInit`, `fuelWindow`, `myPlan`, `servicePit`, `chiefCall`, `PIT` | `Sim/Pit/` | 26 |
| Cards (41 race cards, 17 pit cards, toss effects) | `CARDS`, `PIT_CARDS`, `TOSS`, `BASE_DECK` | `Data/cards.json` + `Sim/Cards/` | 27 |
| Odds model and five-tier outcomes | `computeOdds`, `sampleTier`, `Phi`/`erf` | `Sim/Odds.cs` | 27 |
| Attempts, combos, Heat, Momentum, beats, Focus | `playCard`, `resolveAttempt`, `comboFor`, `beatPoints`, `riskRefill`, `focusBarTick` | `Sim/Hand.cs`, `Sim/Combo.cs`, `Sim/Beats.cs` | 27 |
| Personas, tunes, setups, relics, shop | `ARCH`, `TUNE`, `FOCUS`, `SETUPS`, `RELICS`, `SHOP` | `Data/` + `Sim/Build/` | 27, 34 |
| Radio and crew calls | `RADIO_CALLS`, `crewCall`, `spawnRadio`, `acceptRadio` | `Sim/Radio.cs` (sim) + `UI/Radio` | 33 |
| Balance dials and presets | `BAL`, `BAL_PRESETS`, `DIFF`, `INFO`, `STAKES` | `Sim/Balance.cs` (one struct, loaded from data) | 26, 28 |
| Motion: outcome moves, wiggle, spin, contact sparks | `stageOutcome`, `motionTick`, `spinOut`, `nudge`, `contactFx` | `Presentation/` only | 30 |
| Canvas render, cameras, looks | `render`, `drawCar`, `updateCamera`, `LOOKS` | `Presentation/` only | 29, 45 |
| HUD, hand, pylon, pit box, tooltips, tutorial | `renderHand`, `renderPylon`, `showPitBox`, `TUT_STEPS` | `UI/` | 31, 50 |
| Career shell, rewards, garage | `newCareer`, `endRace`, `showReward`, `showGarage` | `Sim/Career/` + `UI/` | 34, 37 |
| Test track practice mode | `startPractice`, `pr*` | `Sim/Practice.cs` + `UI/` | after 36 |

## Things in the slice that have to change on the way over

1. **Randomness.** The slice calls `Math.random` in about 40 places, and the harness makes it deterministic by replacing `Math.random` with one seeded generator. That mixes visual noise (`wiggle`, `motionTick`, `spinOut`) with sim rolls, so changing an animation shifts race results. The port uses two generators: `SimRng` (seeded per race, only the sim touches it) and `FxRng` (presentation, free to change). Card uids come from a counter, not `Math.random().toString(36)`.
2. **Fixed tick.** The slice advances by frame time, and the harness by 0.05 s steps. The sim runs a fixed 20 Hz tick (0.05 s, matching the harness), and Unity interpolates between ticks for drawing. Slow-mo and beat pauses change how many ticks run per real second, never the tick length.
3. **UI calls inside rules.** Rule functions call `toast`, `commentate`, `renderHand` and `renderGauges` directly. In C# the sim raises events (`CardResolved`, `CautionStarted`, `PitServiced`, `Commentary`, ...) into a per-tick event list, and the UI and booth read that list. The sim never knows the UI exists.
4. **Cards as closures.** Each slice card has `read`, `can` and `fx` functions. The port keeps names, text, family, rarity, costs and numbers in data, and moves the behaviour to one small C# class per card keyed by id. Most `read` rules are "base plus situational adds", so a shared helper covers them and a card class only holds what is special. Every card gets a test (step 27).
5. **Global state.** `G` (career) and `race` are globals that every function reads. In C# they become `Career` and `Race` objects passed in, so the harness can run many races side by side.
6. **Floating point.** Results must match across PC and Switch. The sim uses `double` throughout and avoids `Math` functions whose results differ by platform in hot paths; `Phi` gets its own polynomial (the slice's `erf` approximation, ported as is). If golden tests ever drift between platforms, the fallback is fixed-point for odds and positions.

## Proving the port is right

- **Golden seeds.** Before step 26 starts, record slice results for fixed seeds (finish order, plays, cautions per race) with `prototype/harness/sim-harness-v7.js`. Seeded runs are already repeatable: two runs of seed 61 give byte-identical output. The C# sim cannot match the slice race for race, because the RNG call order will differ, so the golden tests check C# against itself (same seed, same order, every time) and step 28 checks the distributions against the slice.
- **Harness parity (step 28).** The C# harness plays the same styles as the browser one (`none`, `smart`, `spam`, `human`, `bold`, `hspam`) and must land within half a place of the slice for every play style, persona and track. From then on it runs in CI on every pull request, and a PR that moves balance outside the band fails.
- **Card tests.** Each card has a test for when it can be played, its odds in a set situation, and what each of the five outcomes does.

## Order of work in phase C

25. Unity 6.3 LTS (6000.3.27f1) project in `game/` with URP, Git LFS, UnityYAMLMerge, and GameCI building a Windows player nightly. Built Oct 28 (see `docs/unity-setup.md`): the project rebuilds its URP asset and boot scene from scripts, `SimRng` is ported and tested against the slice, and `sim.yml` runs `dotnet test`. Waits on Conner for the Unity licence secrets and opening the project once.
26. `com.greenflag.sim` package and the `sim/` solution. Port tracks, field, pace, tires, fuel, pit road and cautions. A headless race runs in CI and gives the same order from the same seed every time.
27. Cards, odds, outcomes, combos, Heat, beats, Focus, personas and tunes as data plus card classes.
28. C# balance harness matching the slice within half a place.
29 to 36. Race view, motion, hand UI, controller, radio lane, rewards and garage, saves, then the engine feel check.

## Open questions

These are on the Decision Board as yes/no questions; this section lists the assumed answer used in the plan.

- Phase B career work (steps 15 to 24) happens in `prototype/` in this repo through pull requests, instead of patch scripts in the project folder. Assumed yes.
- The Unity project lives in `game/`, with the sim as a local package inside it. Assumed yes.
