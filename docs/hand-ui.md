# The hand and beats HUD (roadmap step 31)

Step 31 hands you the car. The race view now deals your persona's deck (Drafter by default, its kart cousin on kart tracks) and
you play the race with the mouse, the way the browser slice plays: odds-first cards, drag to play, the toss bin, sort and filter
chips, the pause at every draw beat, slow-mo while you aim, the Focus bar, the live odds gauge with hold to play, the beat clock,
the car dash and the pit stop you plan in the stall. Open `Scenes/Race.unity` and press Play.

## What is where

The wide-screen layout of the slice (HUD rounds 2 and 3):

| Where | What |
|---|---|
| Bottom middle | Your hand, up to eight cards. They overlap when they don't fit between the corners. The picked card lifts and gets a gold edge. |
| Under the cards | Sort chips (Best odds, Safest, Family), a chip per family in hand (All shows everything) and Fade wreck risk 5%+. |
| Above the cards | The console: the Focus bar (8 cells = 8 s of slow-mo, a ghost shows what the next beat gives back), the combo meter or the Crew Board charges, and the toss bin. Toss tiles (a toss boost counting down, a banked boost READY) float over it. |
| Above the console | The booth, and toasts (why a card can't be played, how one landed, a toss effect). |
| Bottom left | The car dash: tires and body (green to red, blinking under 20%), position against expected with the gaps, Momentum, Heat (red from 3), the banked boost or, on kart tracks, Tow. |
| Bottom right | The live odds gauge: the picked (or hovered) card's odds moving with the race, the five outcome chances and the factors behind them, or the move in progress with the odds it locked. The Hold to play button sits at its foot. |
| Above the gauge | The beat clock (real seconds to the next draw beat, stretched by slow-mo until Focus runs out; DRAW at a beat) and the pit module (PIT, Safe or Gamble, the fuel window; under caution your call: Stay, Fuel, Two or Four, with the crew chief's pick marked). |
| Top left / top right | The scoring pylon / camera, speed, pause, track, look, autoplay and new-race buttons. |

## Each card

Odds first, as in the slice: the success chance is the big number (green from 70%, amber from 40%, red below), the five outcome
bands sit under it (critical success on the left) with a tick where the odds would be without the odds changers, the wreck chance
shows at 5% and up, and the three biggest odds changers are chips (+ green, − red). The top row has the number key, the family tile
and the costs (T tire %, M Momentum, H+1 Heat). A blue COMBO xN banner means it extends your live combo; a gold +N is a banked
boost that will apply. The bottom has the name, what it does and the families it sets up. BEST marks the best card on Best odds.
A green edge means the odds changers add up to +5 or more, red −5 or less. A card you can't play says why in place of the odds.
Hovering a card shows its text and its toss effect, and warns the dash modules it could hurt (tire cost or wreck chance on the
car, fail chance on position, Momentum spent, +1 Heat).

O hides the odds (cards show ?), for the hard modes.

## Playing

| Do | And |
|---|---|
| Click a card | Picks it. The race drops to 10% speed while Focus lasts (8 real seconds, refilled half at every beat; Evade's slow-mo is free). |
| Click it again | Plays it. |
| Drag a card up past the hand | Plays it when you let go. Dropping it back on the hand puts it down (the card you had picked stays picked). |
| Drag a card onto the toss bin | Tosses it: it charges the Crew Board and does its toss effect. The bin glows when the effect does its main thing now. |
| Hold the Play button, or hold Space | Aims the picked card (or the best one if none is picked). The race runs in slow-mo while you hold, and letting go plays it. Slide off the button, or hold Shift as you let go of Space, to cancel. |
| Esc | Cancels an aim or a drag, or puts the picked card down. |
| 1 to 9 | Pick the nth card shown; press again to play. |
| P | Pause or resume. |
| B | PIT: on green, ask for the plan's stop next time by (again to cancel); under caution, switch between staying out and the stop. |

**Draw beats.** At each braking-zone beat the race stops (the beat clock says DRAW): stale cards go back, fresh ones come in,
Focus refills. Nothing moves until you play a card, hold to play, or press P. Picking cards and reading their odds at a beat is
free.

**Pit stops.** When your car stops in its stall the race holds while you plan: click pit cards into their lanes (tires, fuel,
adjustments; the slowest lane sets the stop), or take the crew chief's plan, then GO. After 12 s the crew chief plans it for you.
Then click RELEASE (or press Enter) as the slowest lane finishes: inside the green window with two cards and no failure is a
Perfect Stop, more than a second early is refused, a little early risks a loose wheel, late gives time away. If you never
release, the crew chief does it.

## How it is built

The rules and the layout are engine-free, in `game/Packages/com.greenflag.view` with the race view, and tested in CI:

| File | Holds |
|---|---|
| `PlayDesk.cs` | The hand and beats rules: picking, click to play, drag to play, the toss bin, hold to play, Esc, P, the beat pause, slow-mo (it scales the driver's time with `Race.SlowMo`, which drains Focus), the pit stop plan timer, toasts. |
| `CardFaces.cs` | `CardFace`, everything a card shows; `CardStyle`, the slice's family, outcome and condition colours and toss wording. |
| `HudReadouts.cs` | What the Focus bar, beat clock, gauge, dash (with change flashes and threat warnings), console and pit module say. |
| `HudLayout.cs` | Where everything sits for a screen size, hit testing, and `HudInput`, which turns pointer and key events into desk calls. |
| `MousePlayer.cs` | A player that uses only the pointer and P, through the same layout and input (the harness's human rules: every 2.5 s and at each beat, Evade and Reel Them In at once, else the best card at 65%+ with under 5% wreck chance; it drags, clicks and holds in turn, tosses dead or weak cards at beats, presses PIT on the plan lap and plans and releases its stops). |

`game/Assets/GreenFlag/Presentation/Race/HandHud.cs` draws it in immediate-mode GUI (no prefabs, fonts or sprites to set up) and
forwards the GUI's mouse and key events to `HudInput`. `RaceView` now builds the race with your loadout and `PitPolicy.Player`,
runs `PlayDesk.Frame` each frame and, with A, lets the mouse player drive (a demo, and a quick way to watch a carded race).

Tests: `sim/GreenFlag.Sim.Tests/HudTests.cs` plays through the HUD with pointer coordinates: sorting and filters, click to play,
slow-mo and Focus, drag to play, drag back, drag to the bin, hold to play with the button and with Space, the beat pause, a pit stop
planned and released with clicks, the plan timer, the layout from 1280x720 to 3840x2160, the mouse player finishing a race on all
ten tracks, and the same clicks giving the same race.

Without Unity:

```
dotnet run --project sim/GreenFlag.Harness -c Release -- mouse all 2          # races played with the mouse only
dotnet run --project sim/GreenFlag.Harness -c Release -- hud carver 61 hud.html 9   # the HUD drawn as SVG at moments worth a look
```

CI prints the mouse races in the run summary and uploads the HUD pictures with the race view frames.

## Where it differs from the slice

- The HUD is drawn with Unity's immediate-mode GUI in a plain dark style; the slice's 90s dash look (bevels, rivets, LCD digits,
  icons) comes with the retro art pass in step 45, once the look is picked (board Q105).
- The gauge is a bar, not the slice's half-dial with a needle.
- The beat pause does not start while you are holding to play (the slice would stop the race under your finger; board Q106).
- The red or green card edge leaves out the field strength odds changer, which is on every stock card and would paint the whole
  hand red.
- Controller and keyboard-only play come in step 32, the radio lane (engineer, spotter and crew calls) in step 33, the reward and
  garage screens in step 34. The slice's first-lap tutorial and keyword tooltips are not ported yet.

## Not checked yet

None of this has been run in a real Unity editor (the editor isn't installed yet, see step 25). The engine side compiles against
Unity's engine assemblies, and the rules, layout and input are tested in CI, but the drawing itself, font sizes and the feel of the
mouse handling need a look in the editor.
