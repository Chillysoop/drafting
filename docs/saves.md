# Saves (roadmap step 35)

A career saves to one of **three slots**. Each slot is one JSON file, `career-1.json` to `career-3.json`, in `Application.persistentDataPath/saves` on PC (on Windows that is `%USERPROFILE%\AppData\LocalLow\<company>\<product>\saves`).

## When it saves

The game saves on its own. There is no save button, only **Save and quit** in the garage.

| Moment | What the save holds |
| --- | --- |
| Start career | The new career in the garage |
| Go racing | The garage **before** the race. Quitting mid-race restarts that race on every stakes setting (board Q48). |
| Race finished | The reward screen, its offers and "Your calls". On Standard stakes it also holds the pre-race career, so **Retry this race** still works after a reload. |
| Every press on the reward screen | So a reroll can't be undone by quitting |
| Back to the garage, Retry, leaving the deck builder, Save and quit | |
| The game is paused, loses focus or closes | Anything else changed since (shop buys, tune, parts), only if something changed |

`CareerMenu.Press` decides this (`Checkpoint`). `CareerFlow` calls `Menu.Flush()` from `OnApplicationPause`, `OnApplicationFocus` and `OnApplicationQuit`.

## Safety

- **Atomic writes with a backup.** `FileSaveStore` writes `name.tmp`, then replaces the file and keeps the old one as `name.bak`. If the newest file is cut off or damaged, the slot loads the backup and the game says so.
- **Newer saves are never touched.** A file from a newer build shows on its slot as "Saved by a newer version" and can't be loaded or overwritten by an autosave. It can still be deleted on purpose.
- **Unknown content is rejected.** A save that names a card, part, persona or track this build doesn't have shows as damaged instead of crashing.
- **A failed write never stops the game.** The menu shows "Couldn't save: …" and carries on.
- Deleting a slot or starting a new career over a used slot takes **two presses**.

## The format

```json
{
  "format": "green-flag-deck-career",
  "v": 2,
  "build": "0.1.0",
  "savedAt": "2026-10-28T12:00:00Z",
  "screen": "garage | reward | over",
  "summary": { "name", "persona", "series", "season", "race", "of", "track", "money", ... },
  "career": { ...everything in Career... },
  "retry": null | { "seed": 123, "career": { ... } }
}
```

The JSON is written by hand (`JsonValue` in the sim package, no engine or NuGet references), with keys in a fixed order and doubles written so they read back bit for bit. The same career always writes the same bytes. Past races keep only their summary; the top 10 and the play log are kept for the race on the reward screen. A 10-race career is about 13 KB with the season's field and standings, and up to about 48 KB on a reward screen with a retry.

What is saved is listed in `Career.SaveCoverage`. `SaveTests.EveryCareerFieldIsCovered` fails when a saved class gains a field that isn't on that list, so new career state (the season in step 37, the driver sheet and Crew HQ in steps 38 and 39) can't silently miss the save.

## Changing the format

1. Change `Career.ToJson` / `FromJson` (and `Career.SaveCoverage`).
2. Bump `CareerSave.Version` and add a `SaveMigration { From = old }` to `CareerSave.Migrations`. It edits the parsed JSON of an old file into the new layout. Use `CareerSave.Careers(root)` so the retry career is migrated too.
3. Run `dotnet run --project sim/GreenFlag.Harness -- savefixtures` to add this version's sample saves (`v<N>-*.json`) to `sim/GreenFlag.Sim.Tests/Saves`.
4. **Never delete or regenerate older samples.** They are the saves real players have.

`SaveTests.OldSavesLoadAndPlayOn` loads every sample in CI, migrates it, checks it loads into the menus and plays two more races, then saves it in the current format. Samples in the current format must also write back byte for byte, so a layout change without a version bump fails. `EveryFormatVersionHasSampleSaves` checks that each version has samples and a migration. The real chain has three steps: format 1 to 2 (step 37) adds the season (`Career.MigrateV1`, see [season.md](season.md)), format 2 to 3 (step 38) adds the driver sheet, the pending off-track event and contract clauses (`Career.MigrateV2`, see [driver.md](driver.md)), and format 3 to 4 (step 39) adds the crew as people, hiring candidates and a rival's offer (`Career.MigrateV3`, see [crew.md](crew.md)). The `v1-*`, `v2-*` and `v3-*` samples load through them. `MigrationsRunInOrder` also tests the machinery with a made-up chain.

## Determinism

`MenuBot.RunReloading` plays a career through the menus and closes and reopens the game after every press and every race. `SaveTests.ReloadingAfterEveryPressChangesNothing` and the harness `saves` command (in CI) check that it ends byte for byte where a straight run ends: same races, same rewards, same rolls. The career's random generator state is in the save, so reloading can't reroll anything.

## Switch

The Switch needs its own `ISaveStore` on the NintendoSDK save-data mount (mount, write, commit, with Nintendo's limits on how often a game writes). That needs the Switch developer account (step 14, skipped for now). Until then a Switch build plays without saving and logs a warning. The checkpoints above are already few, about four writes per race.

## Not verified yet

Nothing here has run in a Unity editor or player yet (Unity isn't installed). The sim and menu code is tested in CI with .NET 8. `CareerFlow` compiles against UnityEngine stubs, but the `persistentDataPath` files and the pause and quit flush have not been seen working in a real build.
