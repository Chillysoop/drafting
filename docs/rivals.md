# Rival arcs and silly season (roadmap step 43)

Twelve named drivers, "the class", have careers that run alongside yours. They race, change teams and step up a series every off-season, and each one keeps a Grudge with you that follows them from karts to stock cars. This step also ports the browser slice's step 20 Grudge rivals, which the C# sim did not have yet.

Done when: a kart rival can show up as your stock car teammate or rival. The `classarc` harness checks it (see below).

## The class: `Career/Rivals.cs` (`RivalBook`)

| Gen | Driver | Nickname | How they hold a Grudge |
|---|---|---|---|
| 0 | Nina Valdez #7 | The Prodigy | climbs fast, proud, forgets a hit quickly (×0.8, cools 1.5) |
| 0 | Jet Alvarez #51 | The Hothead | gains ×1.6, cools fast, payback ×1.2; DC +2 to talk down when Hot |
| 0 | Skye Rennick #88 | The Media Darling | goes to the press (fan swings on replies doubled), proud |
| 0 | Brock Hollister #10 | The Heir | family money: always a big ride, steps up from P12 |
| 0 | Tommy Kessler #42 | The Grinder | gains ×0.7, Respect twice as fast |
| 1 | Cole Mercer #3 | The Enforcer | payback ×1.5, a payback settles 5 |
| 1 | Vance Pruitt #13 | The Grudge | cools 0.25 a race, a payback settles 1; DC +3 to apologise |
| 1 | Dakota Lane #24 | The Local Hero | goes to the press |
| 1 | Rusty Calhoun #57 | The Journeyman | blocks ×1.3, capped at late models, a new team every winter |
| 2 | Hank Doyle #6 | The Old Pro | gains ×0.6, Respect twice as fast, retires after 6 seasons |
| 2 | Marty Krause #31 | The Wall | blocks ×2 |
| 2 | Mika Sorensen #9 | The Ice | gains ×0.5, payback ×0.4 |

Gen 0 is your generation and starts where you start. Gen 1 starts a rung above you and gen 2 two rungs above (capped at stock cars). A stock-start career finds its own generation in late models, a rung below, on the way up. Each driver's `RivalDef` sets how fast they gain Grudge (`Mul`), cool (`Cool` a race), pay back (`Pay`), block (`Block`), how much a payback settles (`Settle`), and whether they're proud (stung by a close loss) or go to the press.

Class drivers take real seats in the field (`Entrant.Rival`), one per team while there's a team without one, and only one on your team. The class has its own generator (`Career.ClassRng`), so turning it off (`Career.ClassOff`) leaves the career's dice where they were.

## Grudge in the race: `Race/Race.Rivals.cs`

On by default (`SimBalance.Rivals`). Grudge runs from −3 to 10 in quarters. Tiers: Respect (−2 or less), Cool, Warm (3+), Hot (6+), Feud (9+).

- **Gains.** Wrecking them +4 (+2 if they were only collected in a big one), a hard pass +0.5, each times their `Mul`.
- **Warm.** They block you more (×1 + 0.25 a point × `Block`) and your passing cards on them read lower ("won't give an inch"). Respect halves their blocks and gives you +8 ("gives you room").
- **Hot.** On their AI move they may go for your bumper instead: chance 0.12 × (Grudge − 5) × `Pay`, at most 60%. A payback spins and damages you, settles their Grudge by `Settle`, and at Feud brings out the caution. The card odds show "wants payback" and the radio warns you.
- **After the race** (`Career.RivalAfterRace`, the slice's rivalEndRace): head to head, cooling between races unless you wrecked them today, Respect from a clean race side by side, proud drivers stung by a close loss, history, and up to two inbox messages.

Parity with the browser slice is unchanged: Quick fields have no class drivers and nothing rolls the race's dice below Grudge 6.

## Rival objectives

The weekly rival is a class driver when one is in your field (`ClassRivalPick`): a Warm one first, else the nearest by car, pulled 4 places toward your own generation and up to 2 more toward the ones you've raced most, and nobody twice in a short spell (the last up to 3 races; `RaceReport.RvWeek` keeps it in the save). The rival objective scales with their Grudge: beat them; at Warm, beat them with no wreck between you (`rclean`, $3,000); at Hot, beat them and dodge the payback (`rsurv`, $3,500).

## Inbox: `Career.Reply`

Messages by kind: wrecked, payback, hit, respect, gloat, beat, arrive, mate. Each has two or three replies, and each reply is a d20 check from your driver sheet with the full math shown before you send it (skill, mods vs DC, the roll you need, the odds, and what making or missing it does to the Grudge, hype and the rest). Replies are seeded by the message id, so reloading doesn't change the roll.

## Silly season: `Career.SillySeason` (in `WrapSeason`, before you sign)

Every class driver's season goes in their path. Off your field they get a simulated finish from skill and team (no two class drivers on the same spot in a series). They grow toward their peak, and veterans may retire. Each one may step up a series (P3 or better, P10 or better with skill 5+, the heir from P12), move to a bigger team (P4 or better) or lose the ride (P15 or worse). The journeyman moves every winter. Then your field reshuffles: class drivers who left give their seats to rookies, arrivals take the worst seat on their team, the best three field drivers on small teams move up, and the two worst lose their rides. A Grudge with someone who didn't race you this season cools by twice their cooling. Arrivals and new teammates send a message (`Greet`), also after you sign with a new team.

## Menus: `CareerMenu.Rivals.cs`

- Garage: a Rivals panel (this week's rival, your teammate from the class, Warm or Respect drivers in your field) and a Rivals button with the unanswered count.
- Rivals page: the inbox with every reply's check, then the class; click a driver for their card (bio, career, how they hold a Grudge, path, history with you).
- Reward screen: each Grudge that moved and why, paybacks taken.
- Season end: the silly season lines above your contract offers.

`MenuBot` opens the inbox when messages wait and sends the best-odds reply that cools the Grudge.

## Saves

Format 8. The class (`"class"`: generator state, drivers, inbox, news), field seats (`"rv"`) and report fields (`rvWreck`, `rvPaid`, `paybacks`, `inboxNew`, `rvWeek`, full reports `rivals`). A format 7 or older save builds its class from its own seed on load (`ClassFromOldSave`): the class starts where the career started, the off-seasons already played run abstractly, and the class drivers of your current series take seats in the field. No Grudge, no messages yet. New sample saves include `v8-brawler-late-models-rivals-inbox.json`.

## The harness: `classarc [n] [seed] [maxSeasons] [grudgeSeasons]`

`sim/GreenFlag.Harness/ClassArc.cs`, in CI as `classarc 2 61 6 2`. Exits 1 if any target fails.

1. **Arcs.** Whole careers from karts through the menus (MenuBot, smart bot), n per kart persona, until a stock car season in which a driver who started in karts with you was your teammate or the weekly rival. CI run: 8 of 8 careers, in season 3 to 5 (average 4.1). About 12 silly season lines per off-season.
2. **Grudge.** Premier stock seasons for the stock personas, careful (smart) and card spam, class on and off:

| Style | Class | Avg finish | DNF | Paybacks/season | Warm+ races | Hot+ races | Rival objective met |
|---|---|---|---|---|---|---|---|
| Smart | on | P9.5 | 0% | 0.00 | 11% | 4% | 67% |
| Smart | off | P8.6 | 0% | 0 | 0% | 0% | 75% |
| Spam | on | P19.0 | 79% | 0.20 | 52% | 20% | 8% |
| Spam | off | P18.6 | 70% | 0 | 0% | 0% | 11% |

Card spam makes enemies and takes paybacks; careful play rarely meets a Hot rival and costs under a place. The slice had about 0.8 paybacks a season for spam; it's lower here because spam DNFs most races.

## What's not verified

- Nothing here has run in the Unity editor. The menus are the engine-free view model, checked by MenuBot and the tests. Step 36 (the engine feel check) still waits on Conner's Unity install.
- No kart rival became your stock car **teammate** in the CI run (all eight were the weekly rival). The smart bot climbs faster than the class (P1 in late models in season 2), so the kids arrive in stock cars a season or two after you. Normal being too easy (board Q88) is step 44's job.
