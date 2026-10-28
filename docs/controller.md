# Controller play (roadmap step 32)

Step 32 lets you race with a controller, or with the keyboard alone, with no mouse. A focus ring moves over the HUD (the cards,
the sort and filter chips, the pit module, and the pit stop hand while you sit in the stall). A does what a click would, and
toss, PIT, hold to play, pause, sort and filter each have their own button. A strip across the top of the screen shows the
buttons for whatever has the focus, with the glyphs of the pad in your hand.

## The buttons

| Does | Xbox | PlayStation | Nintendo (Switch, Pro Controller, Joy-Con) | Keyboard |
|---|---|---|---|---|
| Move the focus | D-pad / left stick | D-pad / left stick | Stick / d-pad | Arrows |
| Pick the focused card, press again to play it; press a chip or pit button | A | Cross | A (right-hand button) | Enter |
| Put the card down, cancel an aim, take the focus back to the hand | B | Circle | B (bottom button) | Esc |
| Toss the focused card | X | Square | Y | X |
| PIT (on green: ask for the stop; under caution: stay out or stop) | Y | Triangle | X | B |
| Hold to play the focused card (let go plays it) | RT | R2 | ZR | Space |
| Hold while letting go of the trigger to cancel | LT | L2 | ZL | Shift |
| Pause / resume / leave a draw beat | Menu | Options | + | P |
| Odds on or off | View | Create | − | O |
| Next sort (Best odds, Safest, Family) | LB | L1 | L | Tab |
| Next family filter (All, then each family in hand) | RB | R1 | R | Q |
| New race on the finish screen | A or Menu | Cross or Options | A or + | R |

Nintendo pads confirm with the right-hand button and go back with the bottom one, as Switch games do; every other pad uses the
bottom button to confirm (board Q107). The number keys, the mouse and every step 31 key still work, and the two mix: moving the
mouse hides the focus ring and hands hover back to the pointer, and the first direction you press after that only shows the ring
where it was.

## How the focus moves

Left and right walk along a row: the hand (in display order, so the best card is first on Best odds), the chips, the pit
buttons, the pit stop cards. Up and down jump to the nearest control above or below: down from the hand reaches the chips, up
from the hand reaches the pit module, down from the PIT button reaches Safe and Gamble. Held directions repeat (after 0.35 s,
every 0.11 s).

- The focused card is the hovered one: it lifts, its tip shows, and the live odds gauge previews it, with no slow-mo. Picking
  it with A starts slow-mo (Focus drains) as a click does.
- When a card is played or tossed, the focus moves to the card now in its place.
- When the car stops in its stall, the focus jumps to the pit stop hand and stays there until the stop is done: walk the pit
  cards and press A to put each in or take it out, or go down to the crew chief's plan and GO. While the crew works, A drops the
  jack (RELEASE is the only thing to focus). Afterwards the focus goes back to the hand.
- Crew calls (step 33): while a call is on the radio, the right shoulder tries it and the left shoulder turns it down (they sort
  and filter again once the radio is quiet), and the radio lane's two buttons are in the focus ring. On the keyboard, Y tries the
  call and N turns it down. See [radio.md](radio.md) (board Q110).
- At a draw beat the race waits, as with the mouse: A twice plays a card and goes green, the trigger aims and goes green while
  you hold, Menu goes green without playing.

A single Joy-Con held sideways has the stick, four face buttons, SL and SR (the shoulders) and one of + or −. That covers
everything except hold to play (A twice plays instead) and one of pause or odds (board Q108).

## How it is built

Engine-free, in `game/Packages/com.greenflag.view`, tested in CI:

| File | Holds |
|---|---|
| `PadInput.cs` | `PadInput`: the focus, spatial navigation over `HudLayout`'s boxes, and what each `PadButton` does (it calls the same `PlayDesk` and `HudInput.Activate` paths a click does). `PadMap`: physical button to action per pad style, and the style from the device's name. `PadGlyphs`: what each pad prints. `PadRepeat`: held-direction repeat and the stick's dead zone. `PadPrompts`: the prompt strip for the current focus. |
| `PadPlayer.cs` | A player that uses only `PadInput.Press`: it walks the focus to a card, taps A twice or holds the trigger, tosses with X at draw beats, presses Y on the plan lap, leaves draw beats with Menu, and in the stall walks to the crew chief's plan and GO and drops the jack with A. Same card rules as the mouse player. |

Unity side, in `game/Assets/GreenFlag/Presentation/Race`:

- `PadReader.cs` reads `Gamepad.current` with the Input System package each frame and hands `PadInput` the changes. It is
  compiled only when the package is installed (`GF_INPUT_SYSTEM`, set by the asmdef's version define) and the Input System is
  an active input backend (`ENABLE_INPUT_SYSTEM`); without them the race still plays with mouse and keyboard.
- `HandHud.cs` draws the focus ring and the prompt strip, sends the arrows, Enter, Esc, X, Tab and Q to `PadInput`, and hides
  the ring when the mouse moves.
- `RaceView.cs` builds the `PadInput` with the desk, reads the pad each frame (not while Autoplay drives), and starts a new race
  from the finish screen on A or Menu.
- `Packages/manifest.json` adds `com.unity.inputsystem` 1.14.2, and `Green Flag/Set Up Project` sets Active Input Handling to
  Both (the editor asks for a restart once).

Tests: `sim/GreenFlag.Sim.Tests/PadTests.cs` (14): the Nintendo A/B swap and glyphs, style detection, held-direction repeat,
walking the hand, A to pick and play, Back, X to toss, down to the chips and back, the shoulders, trigger hold to play and the
left-trigger cancel, leaving a draw beat, a pit stop planned and released with buttons, up to the pit module, prompts per pad,
the mouse taking over, the pad player finishing a race on all ten tracks (and on three at Switch handheld size, 1280x720, with
Nintendo buttons), and the same presses giving the same race.

Without Unity:

```
dotnet run --project sim/GreenFlag.Harness -c Release -- pad all 2                           # races played with a controller only
dotnet run --project sim/GreenFlag.Harness -c Release -- pad carver,ridge 1 drafter 62 nintendo
```

CI prints the controller races under the mouse races in the run summary.

## Not checked yet

- Nothing here has run in a real Unity editor (not installed yet, see step 25). The engine side compiles against Unity's
  engine assemblies, and `PadReader` against a stand-in for the Input System's `Gamepad` API, but no real pad has been read.
- The Input System version (1.14.2) is a guess at what Unity 6.3 offers; if the Package Manager complains, take the version it
  suggests.
- On the Switch itself, pads come through Nintendo's Switch platform package for Unity (under NDA, after the developer
  application in step 14). `PadReader` reads the plain `Gamepad` controls and treats every pad on a Switch as a Nintendo one, so
  it should need nothing more, but how SL and SR on a sideways Joy-Con map is unverified.
- At 1280x720 (Switch handheld) the HUD's smallest text is 8 px; the race plays, but readability needs the art pass (step 45).
- The prompt strip uses words (A, RT, Cross) rather than button icons until the art pass.
