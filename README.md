# Green Flag Deck

A real-time racing game driven by deckbuilding. You race a fictional stock car (and later karts, late models, open-wheel and world-series cars) by playing cards from a hand that refills on a beat clock. Every card shows its odds across five outcomes, from critical success to critical failure, and those odds move with your track position, tires, speed and car condition. Around the races sits a career: seasons, a Crew HQ, a D&D-style driver sheet with d20 checks for press and contracts, and a ladder from karts to a Grand Slam across three top disciplines.

Targets: PC first (closed beta on Steam Playtest), then Nintendo Switch. Engine: Unity 6.3 LTS with C# and URP.

## What's in this repo

```
prototype/            The browser slice (v36): the playable reference for every rule in the game
  green-flag-deck.html  Open it in a browser to play. One file, no build step.
  harness/              Headless balance harness (Node + Playwright) and summary scripts
docs/
  port-plan.md          How the slice maps onto the Unity project, module by module
  unity-setup.md        Installing Unity, opening the project, CI licence secrets, nightly builds
game/                 Unity 6.3 LTS project (URP); the sim is the local package com.greenflag.sim
sim/                  .NET 8 solution that builds and tests the same sim files outside Unity
tools/                One-time git setup (LFS + Unity Smart Merge)
```

The browser slice stays the source of truth until the engine reaches parity. Career features in roadmap steps 15 to 24 are prototyped there first, because they take hours in the browser and days in an engine. The Unity port starts at step 25.

## Play the prototype

Open `prototype/green-flag-deck.html` in Chrome, Edge or Firefox. The title screen has the career, the first-lap tutorial, the test track and the look picker (press L in a race to cycle looks).

## Run the balance harness

Needs Node 18+ and Playwright with Chromium (`npm i -D playwright && npx playwright install chromium`).

```bash
cd prototype/harness
# quick check: one headless race per track, fails on any page error
node smoke.js "$PWD/../green-flag-deck.html"
# 5 personas x 5 tracks, 40 races each, three play styles, seeded
bash rp.sh "$PWD/../green-flag-deck.html" base 40 none,human,spam ',"seed":61'
python3 sum6.py base          # finish vs expected per play style
python3 pp.py base human      # persona x track table for one play style
```

The same seed and settings give identical results, so compare two builds on the same seed. These numbers are what the C# harness has to match in step 28 (within half a place per play style).

## Unity project and sim tests

See `docs/unity-setup.md`. Sim tests run without Unity: `dotnet test sim/GreenFlag.Sim.sln`. The `nightly` workflow builds a Windows player once the Unity licence secrets are set. How the race and the cards work: `docs/sim.md` and `docs/cards.md`. How the C# sim is kept in line with the browser slice: `docs/parity.md`.

## Working rules

- The race sim decides every outcome. Physics, motion and camera work only play out what the sim already decided, so results stay fair and testable.
- The sim is deterministic: seeded RNGs for the race and the cards, a separate one for visuals, a fixed tick, and no engine types inside it.
- Art, audio and fonts go through Git LFS (see `.gitattributes`). Run `tools/setup-git.ps1` (or `tools/setup-git.sh`) once after cloning.
- Work happens on branches and lands through pull requests.

## Roadmap

The 57-step plan to the closed beta is kept in the project's roadmap. In short: phase A (steps 1 to 14) finished the browser race, phase B (15 to 24) proves the career in the browser, phase C (25 to 36) builds the Unity foundation, phase D (37 to 44) ports the career, phase E (45 to 50) does art, broadcast and audio, and phase F (51 to 57) ships the closed PC beta.
