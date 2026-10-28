# Radio lane and crew calls (roadmap step 33)

Your spotter, engineer and crew chief come on the radio during green-flag running with risky extra moves. They are a layer of their own, separate from cards and combos (Conner, Oct 27): a better crew offers more of them, lands them more often and pays more, so spending on Crew HQ is a real alternative to spending on the deck.

## The sim

`game/Packages/com.greenflag.sim/Runtime/Race/Race.Radio.cs` is a port of the slice's `spawnRadio`, `armCall`, `acceptRadio`, `ignoreRadio` and `adviceLine` with the step 19 Crew HQ hooks and the step 18 Wits countdown. The calls themselves, and Crew HQ's perks and traits, are data in `Runtime/Cards/Radio.cs`.

- The first call can come 18 to 26 s into the race, then every 22 to 34 s after one ends, on green only. The gap is shorter with a better crew (2% per average level above 3 across the three call seats, floor half), the Big picture perk (15%) and the Clutch Radio part (30%).
- A risky call stays up 7 s, advice 6 s. Wits adds or takes 1 s per modifier point (Drafter's Sim Racer background, Wits 15: +2 s). A caution takes the call off the radio; the crew chief's Heat warning (Overdrive) takes it over for 7 s.
- 14 calls, as in the slice: High line, Chop him off, Save tires, Run it tight, Brake bias, Bleed pressures, Cool the motor and Short-shift for everyone; Bump and run, Fuel stretch and Engine map at level 6 of their seat; Three-wide hole, All in and Setup trick at level 8. Each has a condition (a car close ahead, one on your bumper, damage to fix, the last 30% of the race). Fuel stretch never comes up in a kart race. Advice is free info (worn tires, the best card in hand, the car ahead burning its tires, a car on your bumper, a tip for the track).
- Chance = base + seat level x per + the Clutch Radio's 15 + perks and traits (Eyes up top, Data nerd, Calm voice, Steady, Gambler, Clutch late in the race), held between 5 and 95. It is fixed when the call comes up.
- Payoff q = 1 + 10% per level above 3, times pay perks (Tire whisperer, Door to door, Overdrive, Gambler). A miss is divided by the soft perks (Clean racer, Safe margins, Safe hands, Veteran). Second opinion rerolls one miss a race; Safety net lets half the spotter's misses cost nothing; Motivator gives Momentum, Cool running repairs 4 damage and Second look brings another call 2 s later when one works.
- Taking a call: `Race.TakeCall()` (result in `LastAnswer`); turning it down: `Race.PassCall()`. Both are free to call between ticks. `CardStats.Calls`, `CallsWon` and `CrewXp` (3 per call, 3 more when it works, 4 per pit stop and 4 more for a Perfect Stop) feed the reward screen and Crew HQ in steps 34 and 37+.
- Everything random in the radio comes from its own generator, `RadioRng`. A race where nobody answers the radio rolls exactly as it did before step 33 (tested on four tracks), so the step 26 to 32 fingerprints and the hands-off parity cells did not move. `PlayerLoadout.Radio = false` turns calls off.
- Crew HQ perks go into a loadout with `PickPerk(role, tier, b)` (it refuses a tier the seat hasn't reached) and traits with `AddTrait(role, id)`.

Not ported yet: the slice's rival advice line ("Your rival is running P7") waits for rivals in the engine career (steps 37+).

## The radio lane

`game/Packages/com.greenflag.view/Runtime`, engine-free and tested:

- `RadioReadout` (in `HudReadouts.cs`): who is talking, the line, the countdown, the odds, what the call pays and what a miss costs, the crew level behind it and Wits. When the radio is quiet it carries **the pit call**: the crew chief's caution call until your call locks ("Bring it in. Four tires and fix what we can.", or "You've got two tires. I'd have gone four. Your call."), and on green "Pit next lap", "Box this lap" and "You're past the window!" around your planned stop. The pit call has no buttons of its own; the pit module has them.
- `HudLayout.Radio`: the slice's amber box just above the console, centred over the hand, with the booth moved up above it while it shows. Try it and No thanks are hit boxes (`HitKind.RadioTake`, `RadioPass`); advice has one Got it button.
- `PlayDesk.TakeCall()` / `PassCall()`: the result shows as a toast ("Radio gamble works" in green, "fails" in red) and a booth line, and flashes in the lane.
- Keys: **Y** tries the call, **N** turns it down. The slice uses A and X, but in the Unity build A is autoplay and X tosses (board Q110).
- Controller: while a call is up, the **right shoulder** tries it and the **left shoulder** turns it down (they go back to filter and sort when the radio is quiet), and the two buttons are in the focus ring too. The prompt strip shows "RB Try the call (63%)" and "LB No thanks" while a call is up (board Q110).
- `MousePlayer` clicks Try it on calls at 55% or better; `PadPlayer` takes them alternately with the shoulder and by walking the focus to Try it. Both are the browser harness's rule.
- Unity: `HandHud.Radio` draws the box (IMGUI, amber, pale blue for advice and the pit call) until the art pass in step 45.

## Parity

The browser harness takes every risky call at 55% or better for every style but hands-off; `Bots.cs` now does the same. The browser baseline (`sim/parity/browser-baseline.json`) was rebuilt with crew calls on (7,000 slice races), and the `parity` check also prints calls per race and the share that worked, per cell and per style. See [parity.md](parity.md) for the result.

## What is not verified

No Unity editor or controller has run this yet (Unity isn't installed; step 25). The presentation code compiles against the UnityEngine 2021.3.33 module stubs and the radio lane draws in the harness's SVG HUD shots (`hud` command), but nobody has seen it in the engine.
