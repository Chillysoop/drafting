# Season framework (roadmap step 37)

A full season now plays start to finish through the career menus: sign a sponsor, qualify, race for points and objectives, read the standings, and at the end take a contract for next season, up the ladder if you earned it. The rules are ported from the browser slice (v47: `season-shell`, `sponsors`, `qualifying`, `late-models`, `season-balance`).

## The sim: `game/Packages/com.greenflag.sim/Runtime/Career/`

- `Season.cs` (`SeasonRules`): points (40 for a win, then 35 for P2 down to 1), the 8 race objectives and their pay, the 7 season goals (counts scale with the series' calendar length), the 7 sponsors and how their offers roll (better sponsors want a better points finish last season), series pay (karts 150, late models 250, stock cars 400 a race at rating 1), contract offers by points finish (step up to the next series from P14 or better, a better team in the same series from P8, always a stay offer), and what a promotion turns over (kart cards to late model cards, persona growth, kart parts).
- `Career.Season.cs`: the season's state on `Career`. The **field** is built once per season with the career's generator and kept; between races each AI car's stats grow toward its team rating (30% of the way). Your seat is a team's; signing with another team swaps you with that team's driver. **Standings** sort by points, then wins, then best finish. `ScoreSeason` runs inside `FinishRace` before the money: objectives (meet them all and finish, and the reward tier goes up one), the sponsor's retainer, the team's salary, points, each car's record. `WrapSeason` runs on Continue after the last race: the season review (champion, your rank, the sponsor goal and its bonus, a renewal at 1.2× if the goal was met), new sponsor offers, the contract offers. `SignContract` takes one: a promotion converts series, cards, persona and parts and builds the new series' field; a new team resets the car to that team's rating and the crew to the team's base (perks and pit cards start over); you keep coaching, money, cards and parts.
- `Qualifying.cs`: the slice's braking mini-game. Two flying laps; each corner is a brake bar with OK, Good and Perfect zones and a sweeping needle; hold to brake, let go in the zone. Perfect and Good chain; Focus (hold) slows time in a sweep. Your qualifying card shapes the zones and what counts (defend, risk, restart, draft and pit cards each score differently) and is discarded at the green. The lap time from the marks plus your car's stats sets your spot in a grid of the field's simulated laps. `QualiRun` is the frame-by-frame run (Unity's tick and the bots' tick are the same call); `QualiBot` plays it at four skill levels for tests and the harness. All rolls are seeded from the race's seed, so quitting can't reroll a lap. Skipping (or giving up) takes an average lap.
- Saves: format 2 adds the season (`Career.SeasonSave.cs`). Version 1 saves migrate on load: they get a field built from their own seed (a separate generator, so the career's rolls don't move), no points yet, the smallest team's contract, and no sponsor (sign one in the garage before the next race).

## Screens: `CareerMenu.cs` (engine-free) and `QualiHud.cs` (Unity)

- **Garage**: next race (expected finish, points, team), sponsor (offers until you sign; you can't race without one), this race's objectives with their pay, standings top 10 and you.
- **Qualifying**: pick a qualifying card or skip; the run (`QualiHud`: the real track outline from `QualiPanel`, the brake bar, the prompt, marks, chain, Focus, lap times, BRAKE held with the mouse, Space/Enter or the controller's A; Esc or B gives up); the grid.
- **Reward**: payout rows for the retainer, salary and objectives, points and rank, the objectives met.
- **Season end**: final standings, results, the sponsor review, and the contract offers. Each offer can be talked up once with a d20 Negotiate check (step 38, see [driver.md](driver.md)) before you sign.

## Not in this step

- **No playoffs.** The slice has a straight points title per series and so does this; a playoff format waits for the Grand Slam endgame (step 42). Board Q118.
- Crew HQ wages (step 39). Negotiation, XP, Hype and off-track events are in since step 38 ([driver.md](driver.md)).

## Checks

- `SeasonTests.cs` (32 tests): points, the field and its growth, sponsor gating and offers by rank, objectives and the tier bump, goals, race payout, season wrap through the menus, contract offers, kart promotion, sponsor rescale, renewal, qualifying checks, judging, scoring, positions, card discard, determinism and a live run, the menu weekend, a kart career climbing to late models through the menus, v1 save migration.
- Harness `career [races] [persona] [seed] [poor|avg|good|great|skip]` prints objectives, points and rank per race and the season lines. CI runs a kart season into late models and a stock car season into season 2.
- `SaveTests.ReloadingAfterEveryPressChangesNothing` still holds with the season and qualifying in the save.

## What is not verified

No Unity editor has run this (Unity isn't installed; step 25, and the engine feel check, step 36, is still open). `QualiHud` and the `CareerFlow` changes compile against UnityEngine 2021.3.33 module stubs, and the screens render in the harness's HTML, but nobody has held the brake in the engine yet, so the bar's feel at 60 fps, the mouse hold and the controller A hold are untested by hand.
