# Browser prototype

`green-flag-deck.html` is the playable browser slice, version 35 (Oct 27, 2026). It is one self-contained file: open it in a browser to play. It is the reference for every race rule until the Unity port reaches parity (see `../docs/port-plan.md`).

What v35 has: 20 cars and 10 teams; five tracks (Millbrook short track, Carver intermediate, Talon Ridge superspeedway, Pinewood kart circuit, Ridgeback road course); 12 s draw beats with slow-mo and drag-to-play; five-tier odds on every card; combos, Heat, Momentum and Focus; crew radio calls; real pit road with fuel windows, cautions and the pit stop card game; car setup and tunes that shift the deck; lasting damage; five personas; a three-race career arc with rewards and a shop; test track practice mode; the first-lap tutorial; and four looks to compare (press L).

## Changing it

Edit the HTML directly and open a pull request. Before merging a rule or balance change:

1. `node harness/smoke.js "$PWD/green-flag-deck.html"` must pass with 0 page errors.
2. Run the harness on the same seed before and after, and put both summaries in the PR:
   `cd harness && bash rp.sh "$PWD/../green-flag-deck.html" after 40 none,human,spam ',"seed":61'` then `python3 sum6.py after`.

The page exposes `window.__gfd` (sim functions and state) and honours `window.__gfdHeadless` so the harness can run races without drawing.

## Harness files

- `sim-harness-v7.js`: runs N headless races per persona and track for each play style (`none` hands-off, `smart`, `spam`, `human`, `human2`, `bold`, `hspam`). `seed` makes runs repeatable.
- `rp.sh`: one process per persona over all five tracks.
- `sum6.py`, `pp.py`, `tsum.py`: summaries (finish vs expected by play style, persona x track table, tune sweeps).
- `smoke.js`: loads the page and runs one race per track.
