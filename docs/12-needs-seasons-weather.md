# 12 — Population Needs, Seasons and Weather

Design: [ADR 0009](decisions/0009-needs-seasons-weather.md) (proposed). Related: [06-economy](06-economy.md) · [10-cultures](10-cultures.md) · [11-military](11-military.md) · [04-game-modes](04-game-modes.md).

User request (2026-10-03, [USER-ANSWERS](handoff/USER-ANSWERS.md)): "every person needs food and water, not just the mines, and housing. Every house needs a little bit wood or coal in the winter to heat it. [It] should be different between the cultures. … weather events that make war impossible like a big snow storm, or a thunder storm that can destroy houses and so on. Be creative."

All numbers are ASSUMPTIONS (balancing starting points for the Rivermen baseline), stored in `data/*.json`, integer only. 1 s = 10 ticks ([ADR 0006](decisions/0006-sim-core-conventions.md)).

## 1. Population needs
Every settler — carrier, specialist, builder, soldier — needs **a bed, food and water**, and in winter **heat**. Mines keep their per-cycle work ration on top (heavy work, [06-economy §1](06-economy.md)); they are no longer the only food consumers.

### 1.1 Housing
| Rule | Value |
|---|---|
| Home | every settler has exactly one home building with a free bed |
| Beds | castle 30 (carriers) + its soldier slots; residence 10; barracks 10 soldiers; guard towers = their garrison slots |
| Specialists | live in a residence (or the castle) and walk to their workplace; nearest home with a free bed is chosen at hiring |
| Population cap | sum of beds; no bed → no new settler (replaces "carriers ≤ residence capacity") |
| Homeless | a settler whose home is destroyed (fire, lightning, demolition, capture) looks for a free bed for 120 s, then leaves the map |

### 1.2 Pantry (food and water at home)
Needs are accounted **per home building**, not per settler, so 5 000 settlers cost one integer counter per building per tick ([06-economy §6](06-economy.md) budget).

- Each home owns three need piles: **food** (any of the culture's food goods — fish, meat, bread, …), **water**, **fuel** (§2).
- Need counters: every tick `foodNeed += occupants × foodRate`; when `foodNeed ≥ 1 000 000` one food unit is taken from the pile and the counter drops by 1 000 000. Same for water. Rates (baseline): **1 food per settler per 10 min**, **1 water per settler per 8 min** (≈ 100 settlers eat 10 food and drink 12–13 water per minute ≈ 3 fishers/bakeries + 2 waterworks).
- Logistics refills need piles like production inputs ([06-economy §4](06-economy.md)); target = max(2, occupants / 4) units per pile; a pile with alternatives (food) takes the nearest offer of any of its goods.
- Soldiers outside own/allied territory eat nothing for 5 min (they carry a ration), then count as *Hungry* until they are back home — long campaigns need supply.

### 1.3 Shortage states
Evaluated per home building and per need; the worst state counts.

| State | Enters when | Effect on occupants |
|---|---|---|
| Supplied | pile could pay the last due unit | normal |
| Short | a due unit could not be paid for 60 s (water: 30 s) | work speed −25 %, walking −15 %, home spawns no new settlers |
| Crisis | Short for 180 s more (water: 120 s) | work stops; soldiers −25 % damage; every 60 s one occupant leaves the map (highest id first, deterministic) |
| Recovery | a unit arrives | back to Supplied immediately |

Variety bonus (Rivermen only, §4): a home that ate ≥ 2 different food goods within the last 5 min gives its occupants +10 % work speed.

## 2. Seasons and heating
### 2.1 Calendar
A match starts on the first day of spring. Season length is a lobby option ([04-game-modes](04-game-modes.md)): **Off** (eternal summer, no heating), Short 4 min, **Normal 6 min** (year = 24 min ≈ 2–4 winters per 45–90 min match), Long 9 min. The calendar is a pure function of the tick, so it needs no state of its own.

As built (M2 step 13): `MatchSetup.Seasons` (`SeasonLength`, default Normal) and `src/Rebuild.Sim/World/Calendar.cs`; season effects on production are data (`"seasons"` per production in `data/buildings.json`, work speed in percent per season: farm autumn 125 / winter 0, fisher winter 50). A cycle ends when its elapsed ticks reach the current season's cycle length; with speed 0 no cycle starts, but one already running ends after its normal length (ASSUMPTION). Winter walking, the flood/drought hooks and heating are not built yet.

| Season | Economy effects (baseline) |
|---|---|
| Spring | farms grow (normal yield); snowmelt can bring floods (§3) |
| Summer | farm, fisher, hunter normal; droughts and thunderstorms possible |
| Autumn | **harvest**: farm +25 %; heating piles start to fill (stockpiling) |
| Winter | farms produce nothing; fisher −50 % (ice); walking −20 % on snow; **heating** needed |

### 2.2 Heating
- From the first tick of autumn every building with a `heat` value fills its fuel pile (target = heat need of one winter month); in winter it burns fuel: `heatNeed += heat` per tick, one heat point per 1 000 000.
- Heat per building (baseline): **S 1 heat per 2 min, M 1 per 90 s, L 1 per 60 s** — a residence burns ~3 logs per winter. Residences, the castle, barracks, towers **and** workplaces (smiths and smelters are self-heating: heat 0) are heated.
- Fuel goods and their heat value are culture data: baseline **log = 1, coal = 2**. A fuel pile takes any fuel good; storages hand out the one they hold most of (same rule as mine food).
- **Cold**: an unheated home or workplace in winter goes Short → Crisis like §1.3 (Short after 60 s: work −50 %, no spawns; Crisis after 180 s: work stops, occupants leave one per 90 s). Soldiers in an unheated tower lose 1 HP per 10 s down to 50 %.

## 3. Weather events
**Lobby option** "Weather": **Off** (competitive), **Mild** (default), **Harsh** (more, longer events, every event possible every year).

**Determinism & fairness**
- The whole match's event schedule is rolled at match start from a new RNG stream `Weather` (`SplitMix64(matchSeed ^ streamId)`, [ADR 0006](decisions/0006-sim-core-conventions.md)); no float, no per-client randomness. Clients only render (snow, rain, lightning flashes).
- **Forecast**: every event is announced 60 s before it starts (HUD icon + countdown, AI sees the same) so nobody is surprised mid-attack.
- **Map-wide** events hit every player equally. **Local** effects (lightning strikes, toppled trees, flooded tiles) are rolled **per player in equal numbers inside that player's own territory**, so no player is luckier than another; Monsters get none.
- At most one event at a time; ≥ 90 s calm between events; no event in the first 10 min (Mild) / 6 min (Harsh).

### 3.1 Event catalogue
| Event | Season | Duration | Effects | War |
|---|---|---|---|---|
| **Blizzard** | winter | 60–120 s | walking −50 %, vision −50 %, heating ×2, construction and fishers pause | **Shelter or die** (§3.2): unsheltered units lose 2 % max HP/s |
| **Thunderstorm** | spring, summer | 60–120 s | per player 1–3 (Harsh 2–5) **lightning strikes** on random tiles of own territory: a hit building loses 50 % HP and catches fire 50 % of the time, a small wooden building (S) is **destroyed outright** 25 % of the time; a hit tree burns; a strike in the open kills every unsheltered unit within 1 tile. Rain puts out every fire after 20 s. | **Shelter or die** (§3.2): unsheltered units lose 1.5 % max HP/s |
| **Great gale** | autumn | 30–60 s | 5–15 trees per player **topple into log piles** on the ground (free wood for carriers); mills and L buildings pause; carriers −25 % speed | no ranged attacks, siege engines cannot fire |
| **Flood** | spring | 90–180 s | snowmelt: plains/fertile tiles in the lowest elevation band next to rivers/lakes become impassable water; buildings on them pause and lose 25 % HP; afterwards the tiles are **silt**: farm yield +50 % until the next spring | units cannot cross flooded tiles |
| **Drought** | summer | 120–240 s | water need ×2, waterworks −50 %, farms −50 %, fires spread ×3 faster; Harsh: one **dry lightning** per player starts a wildfire (no rain to stop it) | normal |
| **Thick fog** | autumn | 60–120 s | vision −75 % for everyone (also towers); archers −50 % range | normal — attacks out of the fog get +15 % damage for 5 s (ambush) |
| **Hailstorm** | spring, summer | 20–40 s | farms lose their running cycle; units and carriers outside buildings take 1 damage per 5 s; carriers seek shelter (pause) | melee only |
| **Deep frost** | winter | 120–180 s | heating ×3; waterworks −75 % unless a heated building stands within r 4; rivers freeze → **frozen water tiles are walkable** (new paths, also for attackers!) | normal |
| **Golden autumn** / **Mild spring** | autumn / spring | 180 s | positive: farm, fisher, hunter +25 %; heating 0 (spring) | normal |

### 3.2 Shelter or die (Blizzard, Thunderstorm)
War is not forbidden by a rule — it becomes impossible because nobody survives outside. The player has to bring the units into shelter in time (user, 2026-10-03).
- **Exposure**: after a 10 s grace period every unit that is not in a shelter loses HP each second — Blizzard 2 % of max HP/s, Thunderstorm 1.5 %/s (plus lightning strikes in the open). A unit caught outside for the whole event dies; there is no automatic retreat for soldiers.
- **Shelters** (own or allied, complete, with free shelter slots): castle 40, barracks 20, storehouse 15, large tower 6, small tower 3, wall tower 3, residence 5, and the new cheap **Bivouac** (S, 4 planks, own territory only, 12 slots). A sheltered unit is inside the building, cannot be attacked or attack, and leaves it when the event ends (or on command). Full shelters turn units away — slots are the real constraint for big armies.
- **Command** `SeekShelter(unit ids)`: each selected unit walks to the nearest own/allied shelter with a free slot (slots reserved in unit id order); plain `Move`/`Garrison` work too. The 60 s forecast is the time to march home — an army deep in enemy land has to retreat, build a bivouac on captured land, or accept losses.
- **Combat** keeps its rules for units still outside, but towers and wall towers do not shoot (shutters closed), siege engines cannot operate (they take no exposure damage) and capture is impossible.
- **Civilians** (carriers, specialists, builders) shelter automatically in the nearest building of their owner and pause their work; they take no exposure damage, so the economy just stops for the event.
- **Monsters** retreat to their lairs; waves are delayed until the event ends.
- Afterwards, damaged units heal at the normal rate — a storm is a cheap way for a defender to win if the attacker misjudged the forecast.

### 3.3 Fire
Introduced by lightning (and later by fire arrows, out of scope here).
- A burning building loses 2 % max HP per s; at 0 HP it becomes rubble, occupants become homeless (§1.1), stock/piles are lost.
- Spread: each 5 s a burning building ignites every wooden building / tree within 2 tiles with 20 % chance (Drought: 60 %). Stone buildings (Highlanders, walls) never catch fire from spread.
- Extinguishing: rain (thunderstorm) ends all fires after 20 s; otherwise carriers from the nearest waterworks within r 10 form a **bucket chain** — every delivered water unit removes 10 s of burning; a fire with no water for 60 s burns down.

## 4. Culture differences
Numbers are integer percentages on the baseline (plain modifiers, [ADR 0007](decisions/0007-culture-system.md)); special rules use the hooks in [10-cultures §4](10-cultures.md).

| | **Rivermen** | **Highlanders** | **Woodfolk** | **Riders** |
|---|---|---|---|---|
| Food need | 100 % | 125 % (heavy mountain work) | 100 % | 100 % |
| Food goods | fish, meat, bread; **variety bonus** +10 % work (≥ 2 kinds) | meat counts 1.5 food; fish 0.5 | + **berries** (Herbalist) | meat counts 1.5; **kumis** (Stud farm byproduct) counts as food *and* water |
| Water need | **75 %** (wells in every home) | 100 % | 100 % | 125 % |
| Heating need | 100 % | **50 %** (thick stone houses) | **150 %** (drafty timber halls) | 75 % (felt yurts) |
| Fuel (heat per unit) | log 1, coal 2 | log 1, **coal 3** | **log 2** (seasoned firewood), coal 1 | log 1, coal 2, **dung 1** (Stud farm byproduct) |
| Housing | residence 10 beds | **stone house** 12 beds, +50 % stone cost | lodge 8 beds, −30 % cost, built 30 % faster | **yurt** 6 beds, cheap, demolish refunds 100 % (move camp) |
| Weather strength | **flood-proof stilt houses**: flood damage −75 %; silt bonus +75 %; residences shelter 10 | lightning damage −50 %, stone buildings never catch fire; blizzard walking only −25 %, blizzard exposure −50 % | Rangers keep full vision in fog; units next to a tree take half exposure; foresters replant burned land 2× faster | Great gale and fog do not slow mounted units |
| Weather weakness | Deep frost: fisher-based food stops completely | Drought: water need ×2.5 instead of ×2 | **fire damage +50 %**, spread chance +50 % | Blizzard: horses eat double grain; Drought hits hardest (water 125 %) |

## 5. Implementation notes
- New sim systems (order in `StepTick`, after production, before logistics): `Calendar` (pure function of tick), `Weather` (schedule + active event + local effects), `Households` (need counters, shortage states), `Fire`. All bounded work, id order, no floats.
- New state (hashed + saved): per home building occupants, need counters, need piles, shortage timers; fire state per building; flooded/silt tiles and toppled-tree log piles as map changes; the event schedule is regenerated from the seed on load and only the progress index is saved.
- Data: `"beds"`, `"heat"`, `"needs"` in `data/buildings.json`; `"food"`/`"fuel"` values per good in culture data; weather tables in `data/weather.json` (event weights per season and lobby setting).
- AI ([05-ai](05-ai.md)): build residences before beds run out, keep food/water production above the need curve, stockpile fuel in autumn, plan shelter slots for the army, order `SeekShelter` when a Blizzard/Thunderstorm is forecast and never start an attack it cannot finish before one, repair after storms.
- Statistics: needs consumption appears in the per-good consumption ring buffer ([06-economy §4](06-economy.md)).
- Tests: golden replays with Weather = Harsh; unit tests for exposure damage (unsheltered unit dies, sheltered unit untouched, slot reservation order), equal lightning count per player, fire spread order, shortage state transitions.

## 6. Delivery plan ([09-roadmap](09-roadmap.md))
| Part | Milestone |
|---|---|
| Housing, pantries (food + water), shortage states, calendar + seasons, winter heating with log/coal | M2 (Sim economy) |
| Seasons/needs visuals and HUD (snow, need icons, calendar) | M3 (client) |
| Weather events, forecast, shelter & exposure, fire, floods, lobby option | **M7b Weather & seasons events** (phase B) |
| Culture-specific needs, fuels, housing and weather traits | with each culture (M8–M10) |
