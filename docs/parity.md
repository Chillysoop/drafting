# Parity with the browser slice (roadmap step 28)

The browser slice (`prototype/green-flag-deck.html`) is where the game's balance was tuned. The C# sim has to play the same game, so step 28 runs the browser harness's play styles in C# and checks, on every PR, that each style finishes within half a place of the slice.

## How it works

- `prototype/harness/sim-harness-v10.js` runs races in the slice (engine preset, Normal, crew calls on since step 33) for each persona, track and play style and writes every race's numbers.
- `prototype/harness/parity-summary.py` boils that down to averages per cell: `sim/parity/browser-baseline.json`.
- `sim/GreenFlag.Harness/Bots.cs` is the same seven play styles in C#, move for move: what it looks at every 0.05 s tick, when it presses PIT, how it calls a caution, when it plays, tosses or waits, and how it plays the pit stop.
- `dotnet run --project sim/GreenFlag.Harness -c Release -- parity` runs every baseline cell in C# (60 races each, fixed seeds, all cores, about 3 minutes) and fails if any play style's average finish, pooled over its 25 cells, is more than 0.5 places off. `sim.yml` runs it on every PR that touches the sim and puts the table in the run summary.

The play styles:

| Style | Plays like |
|---|---|
| none | Hands-off. Holds a hand, never plays, the crew chief runs the stops and caution calls. |
| smart | Instant bot: Evade or Reel Them In at once, otherwise the best card at 65%+ with under 5% wreck. |
| spam | Instant bot: always the best card. |
| human | smart's rules with human timing: looks every 2.5 s and reacts in 1.5 s (0.15 s of race time in slow-mo while Focus lasts), Evade after 2 s; at each draw beat the race pauses, so it tosses dead cards, tosses a weak card for its toss effect and plays its pick at once. |
| human2 | human, but tosses only dead cards or cards under 40%. |
| bold | human timing, plays 50%+ with under 8% wreck. |
| hspam | human timing, always the best card, no tossing for effect. |

All but hands-off press PIT on the plan lap, call cautions by tires and damage (stay out at 60%+ tires and under 25 damage, two tires at 40%+, else four) and play the pit stop as a bot.

## Result

Baseline: 7,000 browser races (5 personas × 5 tracks × 7 styles × 40). C#: 60 races per cell.

With crew calls on (step 33; the bots take every risky call at 55% or better, as the browser harness does):

| Style | Slice finish | C# finish | Difference | Slice DNF | C# DNF | Calls a race (slice / C#) | Calls that worked |
|---|---|---|---|---|---|---|---|
| none | 18.89 | 18.76 | −0.13 | 0% | 0% | 0 / 0 | |
| smart | 10.81 | 11.12 | +0.31 | 0% | 0% | 7.75 / 7.63 | 60% / 60% |
| spam | 18.68 | 18.36 | −0.31 | 65% | 58% | 4.10 / 4.33 | 59% / 61% |
| human | 11.43 | 11.84 | +0.41 | 0% | 0% | 8.22 / 8.27 | 60% / 59% |
| human2 | 11.61 | 11.86 | +0.25 | 0% | 0% | 8.24 / 8.17 | 60% / 59% |
| bold | 11.56 | 11.56 | 0.00 | 0% | 0% | 8.12 / 8.11 | 60% / 60% |
| hspam | 18.20 | 18.14 | −0.06 | 58% | 56% | 4.48 / 4.56 | 61% / 61% |

Step 28's table (crew calls off on both sides) had every style within 0.44 as well; the calls move finishes by less than the noise.

Plays per race, landed rates and caution counts line up per cell as well. A single cell (40 browser races) is noisy, about ±1.5 places, because a race's finish has a spread of 4 to 6 places; that is why the check pools each style over its 25 cells (about ±0.3).

## What was different, and what changed

Step 27's first comparison was 2 to 3 places off. Matching the harness numbers race by race found:

1. **A slice bug: every pit card rolled a critical success.** `pcOdds` returned chances without a `cf` key, so `sampleTier`'s running total went NaN on the first step and fell through to `cs` every time. Pit cards never failed, lanes were always the fastest, and Perfect Stops came easy (75% of stops, against 50% in C#). Fixed in the slice by adding `cf:0` (board Q98). With real rolls a careful player finishes about 2 places worse on Millbrook and Carver than before; Talon barely moves.
2. **Personas had no driver.** A new career's driver comes from the persona's background (Drafter: Sim Racer, Grit 12; Bully: Short-Track Hero, 15; Whisperer: Dirt Grinder, 15; Restart: Former Crew Member, 12; Gambler: Rich Kid, 9; kart personas the same as their stock cousins). C# personas raced with Grit 10, so they missed the late-race odds bonus. `Persona.Grit` and `Persona.Background` now carry it.
3. **The human styles pause at draw beats.** The slice's `BAL.beatPause` is on, so the browser harness's human picks a card at every beat with no reaction delay. The C# styles do the same.
4. **The smart bot plays Reel Them In at once**, like Evade (any special card).

Step 28 ran with crew radio calls off on both sides (`noRadio`, board Q99). Step 33 ported them ([radio.md](radio.md)) and rebuilt the baseline with them on; `noRadio` is still in the browser harness for comparisons.

## Refreshing the baseline

When the slice's balance changes on purpose:

```
cd prototype/harness
for a in drafter bully whisperer restart gambler; do for t in 0 1 2 3 4; do
  node sim-harness-v10.js 40 "$PWD/../green-flag-deck.html" "{\"_p\":\"engine\",\"archs\":[\"$a\"],\"trks\":[$t],\"seed\":$((${#a}*13+t*101))}" c_${a}_$t.json none,smart,spam,human,human2,bold,hspam
done; done
python3 parity-summary.py ../../sim/parity/browser-baseline.json c_*.json
```

That is about 45 minutes on 4 cores if you run four cells at a time (the committed baseline is two 20-race batches per cell on different seeds). When the C# sim changes on purpose, run `parity` and check the table before updating anything.

To compare any other number (tosses, Focus, pit stop timing), `json` writes the C# side in the browser harness's format:

```
dotnet run --project sim/GreenFlag.Harness -c Release -- json 100 drafter 0,3 human out.json 5000
```
