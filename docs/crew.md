# Crew HQ, the staff market and poaching (roadmap step 39)

Your crew are four people now, not four numbers. You train them, pick their perks, hire better ones, and keep them when a rival team comes calling. Crew HQ is ported from the browser slice (v42, `crew-js`), and the staff market, poaching and staff who follow you to a new team are new.

## The sim: `game/Packages/com.greenflag.sim/Runtime/Career/Crew.cs`

- **Seats**: crew chief, spotter and engineer make the radio calls, and the pit crew works your pit stops. Each `CrewMember` has a name, level (1 to 10), XP, a trait, a wage, a perk per tier, the season they joined and the season you kept them from a rival. `Career.SyncCrew()` copies the seats into the loadout the race reads: each seat's level, plus the perks and traits added up in `CrewPerks`. A new career starts with four volunteers at the persona's levels, with no wage, trait or perks, so a fresh career races exactly as before.
- **Training**: one level at the driver coaching price, $3,000 + $1,200 per level above 3.
- **Perks**: levels 4, 6 and 8 each open a choice of two perks per seat (`CrewHq.Perks`). A pick is for good.
- **XP**: earned from the calls of theirs you take (3, +3 when it works). The pit crew earns 4 per stop and 4 more for a Perfect Stop. A Mentor's trait adds 50%, and each point of the driver's Rapport modifier adds 10%. Level n needs 60 + 20n XP.
- **Wages** come out of every payout. The reward screen shows them, and the total can go negative. Wages scale with the series' pay like other off-track money (stock cars ×1, late models ×0.75, karts ×0.375; board Q125).
- **Hiring**: 3 candidates per race, each at your level plus 0 to 2, with a trait. The fee is $1,200 plus 80% of the training they save you, and the wage is $250 per level above 2. A hire replaces whoever has that seat, and you pick their perks fresh.
- **Staff market (new)**: before race 1 of every season after the first, there are 6 candidates, covering every seat, up to 3 levels better, at a quarter off their fees.
- **Pit deck**: an upgrade turns a card into its + version for $1,500 (+10 odds and a bigger gain, or a Setup that installs at Level II). The dealer swaps Lightning Lugs, Gas Man Pro or Swing Man into your deck for $2,000.
- **Poaching (new)**: after each race, a rival team may offer one of your crew a job. Only crew at level 5 or better get offers. The chance is 8% at level 5, +4% per level above that, and +5% after a top-5 finish. At most 2 offers come in a season, and the season's end rolls one more at double the odds. The crew chief is half again as likely to be the target. The team is your rival's if they race for another one, and otherwise the best team in the series. Their wage is 30% over what the person is worth or paid (whichever is more), plus 5% per team rating point over 5. Go racing waits until you answer:
  - **Match it**: their wage rises to the offer. They can't be poached again this season, and they will follow you to a new team.
  - **Talk them round**: one Lead the Crew (Rapport) check against DC 10 + half the rival's rating. It's −1 if they have been with you more than a season, and +1 if the offer is half again their wage or more, or they were volunteering. Every number shows before you roll. A critical keeps them at their wage, a success keeps them for half the raise, a mixed result leaves the offer standing, a failure makes matching 10% dearer, and a fumble sends them to the rival. The check earns driver XP and goes in the driver sheet's log.
  - **Let them go**: the rival pays you twice the new wage. Their assistant steps up two levels lower, with perks picked fresh.
- **Followers (new)**: when you sign with a new team, some of your crew come with you. Anyone you kept from a rival, or who has been with you two seasons or more, follows you if they're at least as good as the new team's person in that seat (crew level = the team's crew base). Followers keep their level, perks, trait and wage. The rest stay behind. Your pit deck goes with the pit crew, and starts over if they stay. The season-end offers say who would follow.
- Every roll here uses a generator seeded from the career's seed and the race, and none draws from the career's own generator. That means card and part rewards roll as before, and quitting can't reroll a talk.

## Screens: `CareerMenu.Crew.cs` (engine-free)

- **Crew HQ**: the crew on race day (call gap, average call odds, big calls unlocked, wages), then a rival's offer with Match, the whole Lead the Crew check and Let them go. After that come the four seats, each with wage, trait, XP, their calls with odds (locked big calls show the level that opens them; the pit crew shows the release window), the six perks and Train. Then come Hire (or the off-season staff market), the pit deck with upgrades, and the pit card dealer (pick a dealer card, then the card it replaces). A perk, a hire and letting someone go each take a second press.
- **Garage**: a Crew HQ button that says when a perk or an offer is waiting, and a panel for a rival's offer. **Reward**: crew wages in the payout, and crew XP and level-ups. **Season end**: each offer says who follows you.
- Screenshots: `/mnt/project-files/design/crew-hq/` (from `menus`).

## Saves

Format 4 (`Career.CrewSave.cs`) adds the seats, this race's candidates, a rival's offer (with any talk roll) and the season's offer count, and each race report gains wages, crew XP and level-ups. Format 3 saves migrate: each seat gets a volunteer at the level the career raced with, there with you since season 1. Base's crew numbers are rebuilt from the seats on load.

## Tests

`CrewTests.cs` (15 tests): a fresh crew matches the persona and races as before, training, perks reaching the race and the pit game, XP, level-ups and wages, hiring and the staff market, wage scaling, pit cards, and **a rival hiring your crew chief, with Match and Let them go** (the step's done-when). They also cover every talk result against its text (seeds until all five tiers show) and that it can't be rerolled, offer rates and the cap, followers on a move, a save round trip with an offer on the table, the v3 migration, the page's presses, and a 12-race menu-bot career. `MenuBot` picks perks and answers offers, so the career and save harness runs cover Crew HQ. The `career` command prints a crew line.

## Not verified

No Unity editor has run this yet (step 36 waits on the Unity install). The pages render in the harness's HTML and `MenuHud` draws them like the other pages, but nobody has played Crew HQ in the engine. A poached crew member doesn't make the rival's cars faster yet; that can come with rival arcs (step 43).
