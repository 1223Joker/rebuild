# 10 — Cultures

System design: [ADR 0007](decisions/0007-culture-system.md). User requirements ([USER-ANSWERS](handoff/USER-ANSWERS.md), 2026-10-02): different mechanics, advantages and disadvantages ("need more wood, or more stone"); S4-style depth; **4 cultures** in the MVP; the **first LAN build has 1 culture**.

All names, numbers and unique content below are **ASSUMPTIONS / first proposals** for the user to approve or rename ([open-questions](open-questions.md) H1–H3). Numbers are integer percentages relative to the baseline culture; they are balancing starting points, not final values.

## 1. Design rules
1. **Shared core**: every culture has the base economy of [06-economy](06-economy.md) (woodcutter, sawmill, stonecutter, food chain, mines, smelters, smiths, barracks, towers) and the base units of [11-military](11-military.md).
2. **One strength, one weakness, one identity**: each culture's advantage comes with a clear cost (e.g. cheap stone walls ↔ slow wood economy).
3. **2–4 unique elements** per culture (buildings, goods, units), defined in data, using only hooks from the catalogue (§4).
4. **Material profile**: build costs shift between wood and stone, so map resources matter differently per culture (map fairness in [03-mapgen §4](03-mapgen.md) guarantees both near every start).
5. **Every culture can fight every way**: weaknesses are "worse at", never "cannot" (e.g. Woodfolk can build stone walls, but they cost +50 %).

## 2. Overview
| | **Rivermen** (baseline) | **Highlanders** | **Woodfolk** | **Riders** |
|---|---|---|---|---|
| Identity | farmers & traders of the river plains | mountain masons | forest clans | horse nomads |
| Strength | strong food economy, cheap numerous infantry | stone buildings & walls, heavy infantry | cheap fast wood buildings, best archers, palisades | mobility, cavalry, fast expansion |
| Weakness | weak mining & metal | slow wood economy, slow units, slow building | fragile buildings, expensive stone works | grain-hungry horses, weak at sieges & walls |
| Build material (wood/stone %) | 100 / 100 | 50 / 150 | 150 / 50 | 120 / 80 |
| Unique goods | — | ashlar (cut stone) | longbow | horse |
| Unique buildings | Granary, Market hall | Masonry, Stone forge | Bowyer, Herbalist | Stud farm, Saddlery |
| Unique units | Militia, Pikeman | Shieldbearer | Longbowman, Ranger | Light rider, Horse archer |
| Playstyle | economy, numbers | turtle, siege-proof fortress | early pressure, skirmish | raids, map control |
| First LAN build | **yes (only culture)** | later | later | later |

Rivermen are the baseline because their rules equal the shared core; they are the only culture in the first LAN build.

## 3. Culture details
### 3.1 Rivermen (baseline)
| Aspect | Rule |
|---|---|
| Advantages | farm, fisher, mill yields +25 %; residence settler production +25 %; Militia: cheap weak infantry (no sword needed, uses a spear made from planks) |
| Disadvantages | mine yields −20 %; iron smelter needs +1 coal per 2 bars |
| Unique buildings | **Granary** (stores food, +15 % mine food efficiency in radius 10) · **Market hall** (converts 3 surplus goods of one type into 1 of another, slow) |
| Unique units | **Militia** (light melee, cheap), **Pikeman** (anti-cavalry, `Charge` resistance) |

### 3.2 Highlanders
| Aspect | Rule |
|---|---|
| Advantages | building costs shift to stone (wood 50 %, stone 150 %); stone walls & wall towers −25 % cost, +25 % HP; stonecutter +25 % yield |
| Disadvantages | woodcutter −20 % yield; building time +20 %; all units −10 % speed |
| Unique goods/buildings | **Masonry** (stone → ashlar; ashlar required for walls and towers, gives the HP bonus) · **Stone forge** (iron + ashlar → heavy armour for Shieldbearers) |
| Unique units | **Shieldbearer** (Heavy armour, very high `Pierce` resistance, slow) |

### 3.3 Woodfolk
| Aspect | Rule |
|---|---|
| Advantages | costs shift to wood (wood 150 %, stone 50 %); building time −20 %; forester +50 % planting; palisades −50 % cost, +50 % HP; archers +1 tile range |
| Disadvantages | buildings −20 % HP; stone walls & wall towers +50 % cost |
| Unique goods/buildings | **Bowyer** (plank → longbow) · **Herbalist** (heals garrisoned soldiers faster, food from forest: berries) |
| Unique units | **Longbowman** (long range `Pierce`), **Ranger** (fast light skirmisher, +vision) |

### 3.4 Riders
| Aspect | Rule |
|---|---|
| Advantages | carriers and units +15 % speed; small guard towers −30 % cost (fast territory expansion); cavalry units |
| Disadvantages | horses need grain + water continuously (Stud farm upkeep); siege units −25 % damage; stone walls +25 % cost |
| Unique goods/buildings | **Stud farm** (grain + water → horse) · **Saddlery** (horse + iron → mount kit for cavalry) |
| Unique units | **Light rider** (Mounted, fast, `Charge` attack), **Horse archer** (Mounted, ranged, weak in melee) |

## 4. Hook catalogue (sim behaviours used by cultures)
Each hook is implemented once in the sim and switched on per culture by data ([ADR 0007](decisions/0007-culture-system.md)).

| Hook | Used by | Effect |
|---|---|---|
| `resource_conversion` (Market hall) | Rivermen | n:1 goods conversion recipe |
| `aura_bonus` (Granary, Herbalist) | Rivermen, Woodfolk | integer % bonus to buildings/units in radius |
| `material_requirement_override` | Highlanders | walls/towers require ashlar instead of stone |
| `mounted_units` | Riders | Mounted class, charge bonus, horse upkeep |
| `upkeep` | Riders | per-minute consumption by a building/unit type |
| `spear_without_iron` | Rivermen | Militia recipe uses planks |

All other differences are plain numeric modifiers.

## 5. Balance process
- Every matchup (4 mirror + 6 cross) is run as headless AI-vs-AI soak once the AI exists (milestone M13 in [09-roadmap](09-roadmap.md)); target: no culture wins > 60 % of a cross matchup at equal AI difficulty ([08-testing](08-testing.md)).
- Before the AI exists: scripted build-order benchmarks per culture (time to first soldier, to first wall, to first siege unit) must stay within ±15 % of the baseline.
- Human playtests on LAN after each culture lands.

## 6. Art identity
Each culture gets its own palette variant and building kit mapping ([07-art-style §6](07-art-style.md)): Highlanders → stone-heavy Castle Kit pieces; Woodfolk → wooden Fantasy Town pieces; Rivermen → plaster/roof town pieces; Riders → tents/wood plus horses (no Kenney 3D horse found → placeholder needed, [open-questions](open-questions.md)).
