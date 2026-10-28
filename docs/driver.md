# Driver sheet and off-track events (roadmap step 38)

Between races your driver talks to the press, sponsors, owners, officials and fans. Each answer is a d20 check, and every number in the check is shown before you commit: each bonus with its source, the DC and what raised it, advantage or disadvantage and why, the roll you need, and the exact odds of each result. The rules are ported from the browser slice (v47: `driver-sheet`), plus 9 new events.

## The sim: `game/Packages/com.greenflag.sim/Runtime/Career/`

- `Driver.cs`: the sheet (six attributes, two proficient skills, level, XP, points to spend, Hype, Fans, Respect, the image map and a log of the last 8 checks) and `DriverRules`:
  - **Backgrounds**: Short-Track Hero, Rich Kid, Dirt Grinder, Former Crew Member and Sim Racer. Each has starting attributes, two proficiencies, one trait and one flaw. You pick one on the title screen, and the default comes from your race persona.
  - **Checks**: d20 + attribute mod (floor((score−10)/2)) + proficiency (+2) + image (+1 on your image's two favored skills) + lean (+1 more on a signature answer when you lean all the way in) + trait and flaw lines. You roll against DC = base + context lines (for example a bad finish or a promotion talk). A natural 20 is a critical and a natural 1 is a fumble. Total ≥ DC is a success, missing by 1 to 4 is mixed, and anything lower is a failure. Advantage and disadvantage cancel. The odds are exact, not simulated.
  - **Results** (`OffTrack.TierFx`): a critical doubles the success and adds 1 Hype. Mixed gives the success with the event's cost. A fumble is a failure plus the event's fumble.
  - **Image**: each answer's tone moves you on a ±10 map (Humble ↔ Cocky, Professional ↔ Showman). An image needs at least 2 on both axes, and 5 is a full lean. The images are Cocky Rookie, Humble Champion, Fan Favorite and Cold Assassin.
  - **XP and levels**: 25 XP per race, 30 for a check you make and 20 for one you miss. Level n needs 100×n XP. Each level gives a point that raises an attribute (Grit and Wits feed the race persona's stats).
  - **Hype**: up to 3. You earn it from a ×4 combo or a big finish, and you spend it to reroll a check (keeping the better die).
  - **Momentum**: some answers promise up to 3 Momentum for the next race. It goes into `PlayerLoadout.DriverMom`, and the race starts with that much more.
- `OffTrack.cs`: the 12 events. Press scrum, Sponsor dinner and Contract talk come from the slice. Crew meeting, Officials' hearing, Fan day, Podcast interview, Rival in the garage, Data night, Charity gala, Commercial shoot and Social media storm are new. Each event has 1 to 3 rounds of 2 to 3 answers, a walk-away option with a known cost, a mixed cost and a fumble. Your image adds its own signature answer. The schedule (`Slots`) puts the press scrum, the dinner and the contract talk in fixed race slots, and the rest are a weighted draw without repeats in a season. Karts get 4 events a season, late models 5 and stock cars 6.
- `Career.Driver.cs`:
  - The career side: rolling the event after each race, choosing, rerolling, walking away, applying effects with clamps, contract clauses, and the season-end **negotiation**.
  - Negotiation is a Negotiate check against DC 10 + team rating/2 (+1 for a promotion). It scales the offer's salary by 1.3 / 1.15 / 1 / 0.9 / 0.8 by result.
  - Money from events scales with the series' pay. A $1,500 stock-car fine is smaller in karts. Amounts are rounded to $50.
  - Rolls are seeded from the race seed, the event, the round and the attempt. Quitting and reloading can't reroll, and every answer to the same question sees the same dice.
- Saves: format 3 (`Career.DriverSave.cs`) adds the driver, the pending event and contract clauses. Only the dice are saved, and the math is rebuilt on load. Format 2 saves migrate: they get the background of their persona, their grit and wits, and no pending event.

## Screens: `CareerMenu.Driver.cs` (engine-free)

- **Garage**: a panel for the waiting event (you can't start the next race until it's done or walked away from), plus a Driver sheet button.
- **Event page**: the question, then one card per answer listing the full math and what Success, Mixed and Failure change, then "Say it". After the roll it shows both dice, each line again, the result and what changed, plus a hint saying how close it was.
- **Driver sheet**: attributes with their mods, the skills with their totals, the background's trait and flaw, the image map, level and XP, and the check log. Points are spent here.
- **Reward page**: race XP, Hype earned and the Momentum promised. **Season end**: a Negotiate button on each offer with its odds.
- The Unity IMGUI `MenuHud` draws these pages like the others. They have **not been run in a Unity editor yet**.

## Tests

`DriverTests.cs` (23 tests). `EveryEventShowsItsMathInFull` walks all 12 events in 3 contexts with 5 images. For every answer it checks:
- that each line, the DC, advantage or disadvantage, the roll needed and the odds are shown on the page;
- that every result row matches the effects;
- that a forced roll's shown math adds up and its effects are applied.

Other tests cover:
- the odds against brute force, mods, tiers, traits, the image map, levels, and race XP, Hype and Momentum;
- determinism and reload, Hype rerolls, walking away, and contract talks and clauses;
- money scaling, clamps, help text, season slots, negotiation, and the v2 migration.

`MenuBot` plays events and negotiations, so the career and save harness runs cover them too.
