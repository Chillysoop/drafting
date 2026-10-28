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
| `CarMotion.cs` | Where each car sits across the track: wandering lines, tucking into the draft, double file on starts and restarts, passer low and defender high, side by side instead of overlapping, pit road, spins, wrecked cars parking on the infield, yaw from sliding | `motionTick` (minus the card outcome moves, sparks and smoke, which step 30 brings) |
| `RaceDriver.cs` | Runs the sim's fixed 20 Hz tick from the frame clock at 1x to 8x, interpolates between ticks, smooths jumps (the caution line-up, pit stall snaps, lucky dogs), turns events into flag banners and booth lines | `update`, `easeFrom`, `flag`, `commentate` |
| `CameraRig.cs` | Wide shot (fits the whole track from the grandstand side) and follow shot (behind and above your car); Auto picks like the slice; switching blends the two | `updateCamera` |
| `Pylon.cs` | The scoring pylon as data: header, laps to go, flag colour, rows with gaps, PIT and OUT, your row always shown | `renderPylon` |
| `Look.cs` | The looks (palette, pixel size) and the team liveries | `LOOKS`, `PAL0`, `TEAMS` |

The view reads the race and never writes to it. Its few random rolls (line wander, spin direction) come from its own `SimRng`,
so a race drawn on screen ends with the same fingerprint as the same race run headless; a test checks this on all ten tracks
at 8x with uneven frame times.

**`game/Assets/GreenFlag/Presentation/Race`** turns that into GameObjects: `TrackBuilder` (generated meshes for grounds,
infield, apron, asphalt, lane line, wall, pit road, start/finish, a grandstand), `CarRig` (cars from boxes in team paint, brake
lights, the pace car's light bar, a marker over your car), `RaceView` (the scene script) and `RaceHud` (pylon, banners,
booth, car tags, result card, controls, in immediate-mode GUI so it needs no prefabs; step 31 replaces it).

One track unit is 60 m in the engine (`Kit.S`), so a stock car is about 5 m long.

## Looks

The look is still benched (board Q37/Q38), so the build starts in **Clean**. `Looks.Default` is the one line to change once it
is picked. Pixel, low-poly and TV exist as previews: their palettes and, for pixel (1/3) and low-poly (1/2), a low-resolution
render scaled up with no filtering. Their outlines, dither, faceting and scanlines come with the retro art in step 45.

## Looking at it without Unity

`dotnet run --project sim/GreenFlag.Harness -c Release -- frames carver 61 frames.html 6` plays a race through the same
driver and motion code and writes top-down SVG snapshots with the pylon beside each. CI uploads Carver, Ridgeback and
Crossroads as the `race-view-frames` artifact on every sim PR.
