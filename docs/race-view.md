# The race view (roadmap step 29)

The first thing in the Unity build you can watch: a full race of the C# sim on screen, on any of the ten tracks, with wide and
follow cameras and the scoring pylon. Your car runs hands-off (the crew chief takes the caution calls) until the hand and beats
UI arrive in step 31. Open `Assets/GreenFlag/Scenes/Race.unity` (the project bootstrap creates it) and press Play. It is also
the first scene in the player build.

| Key | Does |
|---|---|
| C | Camera: Auto (wide at the start and under caution, follow otherwise, as in the slice), Wide, Follow |
| 1 to 4 | Speed 1x, 2x, 4x, 8x |
| Space | Pause |
| T / Shift+T | Next / previous track (all ten: Millbrook, Carver, Talon Ridge, Pinewood, Ridgeback, the two kart circuits, Cedar Bowl, Millbrook late model, Crossroads Figure-8) |
| L | Next look (Clean, then the three step 11 candidates as previews) |
| R | New race on a new seed |
| F | Skip to the checkered flag |

The same buttons sit bottom right. The result card shows the race fingerprint, which matches
`dotnet run --project sim/GreenFlag.Harness -- race <track> <seed>`.

## Two halves

**`game/Packages/com.greenflag.view`** (assembly `GreenFlag.View`, no engine reference) decides where everything is. CI builds
it with dotnet (`sim/GreenFlag.View`) and tests it (`sim/GreenFlag.Sim.Tests/ViewTests.cs`).

| File | What it holds | Slice source |
|---|---|---|
| `TrackLayout.cs` | Track geometry in world space: ovals and path tracks (road courses, kart circuits, the figure-8), lanes, the racing line, brake zones, pit road lanes, the start/finish line | `worldPos`, `TG`, `tgXY`, `cornerLine`, `brakeZone`, `pitGeom`, `pitLane` |
| `CarMotion.cs` | Where each car sits across the track: wandering lines, tucking into the draft, double file on starts and restarts, passer low and defender high, side by side instead of overlapping, pit road, spins, wrecked cars parking on the infield, yaw from sliding | `motionTick`, plus step 30 below |
| `Fx.cs` | Sparks, smoke puffs (tire, engine, dust) and skid marks, emitted and stepped by `CarMotion` | `race.sparks`, `race.smoke`, `race.skids` |
| `RaceDriver.cs` | Runs the sim's fixed 20 Hz tick from the frame clock at 1x to 8x, interpolates between ticks, smooths jumps (the caution line-up, pit stall snaps, lucky dogs), turns events into flag banners and booth lines | `update`, `easeFrom`, `flag`, `commentate` |
| `CameraRig.cs` | Wide shot (fits the whole track from the grandstand side) and follow shot (behind and above your car); Auto picks like the slice; switching blends the two | `updateCamera` |
| `Pylon.cs` | The scoring pylon as data: header, laps to go, flag colour, rows with gaps, PIT and OUT, your row always shown | `renderPylon` |
| `Look.cs` | The looks (palette, pixel size) and the team liveries | `LOOKS`, `PAL0`, `TEAMS` |

The view reads the race and never writes to it. Its random rolls (line wander, spin direction, sparks and smoke) come from its own `SimRng`,
so a race drawn on screen ends with the same fingerprint as the same race run headless; a test checks this on all ten tracks
at 8x with uneven frame times.

**`game/Assets/GreenFlag/Presentation/Race`** turns that into GameObjects: `TrackBuilder` (generated meshes for grounds,
infield, apron, asphalt, lane line, wall, pit road, start/finish, a grandstand), `CarRig` (cars from boxes in team paint, brake
lights, the pace car's light bar, a marker over your car), `RaceView` (the scene script) and `RaceHud` (pylon, banners,
booth, car tags, result card, controls, in immediate-mode GUI so it needs no prefabs; step 31 replaces it).

One track unit is 60 m in the engine (`Kit.S`), so a stock car is about 5 m long.

## Motion, contact and wrecks (roadmap step 30)

The view now plays out what the sim decided, the way the slice does since step 3, with more weight on top.

- **Card outcomes.** The sim tags `CardResolved` with its tier, and `CarMotion.OnEvent` stages the slice's `stageOutcome`:
  success = speed streaks (and a bumper tap with sparks on Bump Draft / Bump and Run, cover on defend cards), neutral = stalls
  side by side, failure = the rear steps out with smoke and skid marks (Dive Bomb, Send It and Crossover swipe at the other car,
  Push to the Limit and the High Line brush the wall), critical failure = a big fishtail and engine smoke, or a wreck. A glow
  under the car shows the outcome colour for about a second. While your card sets up, your car pulls out to look (the telegraph).
  Kart cards move like the stock card they stand in for.
- **The AI's small moves.** Two new presentation-only events, `AutoLift` and `AutoAlongside`, show the moves that came to nothing
  (a wiggle as he lifts, or a few seconds alongside). Blocks cover the car behind. None of this changes the sim.
- **Body lean.** Every car's body sits on a damped spring: it leans out of turns (about 3 degrees per g), dives under braking,
  squats on the throttle, rocks away from door-to-door contact and bounces after a hop. `CarPose.Roll`, `Pitch` and `Lift`;
  in Unity the body pivots over the wheels.
- **Wrecks.** When the sim wrecks a car (it sets `Spinning` to 2.2 s), the view takes the car off its lane spring and makes it a
  free body: it keeps its speed, spins, slides on its tires (grip across the car is much lower than along it, worse on the grass),
  hits the outside wall with sparks, a bounce and on a hard hit a hop, and the two cars that touched are pushed apart. When the
  sim's spin is over and the car has nearly stopped turning (5.5 s at most), it is handed back: the slide eases out to the
  sim's distance and the heading eases back to straight. A car the wreck put out of the race keeps where it stopped and its angle,
  then is towed to the infield. The physics never feeds back into the sim, so results stay the card sim's (board Q103).
- **Damage.** Cars wear their damage zones: a dark crumple over the nose and tail (deeper with more damage, as in the slice),
  scrapes down both flanks, radiator smoke from a nose past 22, and a tail past 20 steps out now and then.
- **Particles.** Sparks (with gravity, white-hot to orange), smoke puffs that rise and grow, and skid marks that fade over 30 s.
  Unity draws them in `FxView` as three meshes rebuilt each frame with the `GreenFlag/Fx` shader (Resources), so there are no
  particle systems or prefabs to set up.

`dotnet run --project sim/GreenFlag.Harness -c Release -- wrecks mill 61 wrecks.html spam 3` plays a race with the harness's
spam bot and writes close-ups of the first three wrecks at +0.15 s to +7 s; `frames ... 6 spam` shows the card moves on the
full-track frames. CI uploads both. Tests: `sim/GreenFlag.Sim.Tests/MotionTests.cs`.

## Looks

The look is still benched (board Q37/Q38), so the build starts in **Clean**. `Looks.Default` is the one line to change once it
is picked. Pixel, low-poly and TV exist as previews: their palettes and, for pixel (1/3) and low-poly (1/2), a low-resolution
render scaled up with no filtering. Their outlines, dither, faceting and scanlines come with the retro art in step 45.

## Looking at it without Unity

`dotnet run --project sim/GreenFlag.Harness -c Release -- frames carver 61 frames.html 6` plays a race through the same
driver and motion code and writes top-down SVG snapshots with the pylon beside each. CI uploads Carver, Ridgeback and
Crossroads as the `race-view-frames` artifact on every sim PR.
