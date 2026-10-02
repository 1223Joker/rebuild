# 11 — Military: Units, Combat, Walls, Siege

System decisions: [ADR 0008](decisions/0008-combat-model.md). Culture-specific units: [10-cultures](10-cultures.md). User requirements ([USER-ANSWERS](handoff/USER-ANSWERS.md), 2026-10-02): many different warriors, big stone walls, direct unit control, gates, wall towers with archers, siege units, wooden palisades. All numbers are **ASSUMPTIONS** (balancing starting values in `data/units.json`).

## 1. Phasing
| Content | First LAN build (Phase A) | Full MVP |
|---|---|---|
| Control | direct control, all commands of §2 except wall commands | all |
| Units | Swordsman, Spearman, Archer (Rivermen) | 5 base units + 7 culture units = **12 unit types** |
| Fortifications | — (castle and guard towers only) | palisade, stone wall, gate, wall tower |
| Siege | — | battering ram, catapult |

## 2. Control & commands
Selection, control groups (Ctrl+1…9) and formations preview are **client-only** UI state; only the resulting commands are sent ([01-architecture §4](01-architecture.md)).

| Command | Payload | Effect |
|---|---|---|
| `Move` | unit ids (≤ 200), target tile | walk; ignore enemies |
| `AttackMove` | unit ids, target tile | walk; engage enemies met on the way |
| `Attack` | unit ids, target entity | attack a unit, building, wall segment or lair |
| `Stop` / `Hold` | unit ids | stop; hold = do not chase beyond 3 tiles |
| `SetStance` | unit ids, aggressive / defensive / passive | auto-engage behaviour |
| `Garrison` / `Ungarrison` | unit ids, building id | enter/leave tower, castle, wall tower |
| `Train` | barracks id, unit type, count | queue training (needs goods + a carrier) |
| `BuildWall` | type (palisade/stone), tile path (≤ 64 tiles) | place wall segments as construction sites |
| `BuildGate` / `SetGateLocked` | segment tile / gate id, bool | convert segment to gate / lock against allies too |

## 3. Unit roster
Classes and attack types from [ADR 0008](decisions/0008-combat-model.md). Speed in tiles/s at 1×; range in tiles; cooldown in ticks (10 ticks = 1 s).

| Unit | Culture | Class | Attack | HP | Dmg | Range | Cooldown | Speed | Special | Training cost |
|---|---|---|---|---|---|---|---|---|---|---|
| Swordsman | all | Heavy | Melee | 120 | 14 | 1 | 12 | 1.0 | — | sword |
| Spearman | all | Light | Melee | 90 | 10 | 1 | 10 | 1.1 | ×2.5 vs Mounted | spear (iron + plank) |
| Archer | all | Light | Pierce | 70 | 9 | 7 | 15 | 1.1 | — | bow (plank) + arrows |
| Battering ram | all | Siege | Crush | 300 | 60 | 1 | 30 | 0.5 | only targets structures | 6 plank + 2 iron, crew 2 |
| Catapult | all | Siege | Crush | 150 | 50 | 12 (min 3) | 60 | 0.4 | 25 % miss | 8 plank + 4 iron + 4 stone, crew 2 |
| Militia | Rivermen | Light | Melee | 70 | 8 | 1 | 10 | 1.1 | cheap, no iron | spear (planks) |
| Pikeman | Rivermen | Heavy | Melee | 110 | 11 | 1 | 12 | 0.9 | ×3 vs Mounted | pike |
| Shieldbearer | Highlanders | Heavy | Melee | 180 | 12 | 1 | 14 | 0.8 | Pierce taken −60 % | sword + heavy armour |
| Longbowman | Woodfolk | Light | Pierce | 70 | 11 | 9 | 16 | 1.0 | — | longbow |
| Ranger | Woodfolk | Light | Pierce | 80 | 7 | 5 | 10 | 1.5 | +3 vision | bow |
| Light rider | Riders | Mounted | Charge | 130 | 13 | 1 | 12 | 2.0 | first hit ×2 | mount kit + sword |
| Horse archer | Riders | Mounted | Pierce | 100 | 8 | 6 | 15 | 1.9 | −30 % melee dmg dealt | mount kit + bow |

**Damage multipliers (%)** — `final = dmg × table[attack][class] / 100 × unitBonus / 100`, integer, floor:

| Attack \ Class | Light | Heavy | Mounted | Structure | Siege |
|---|---|---|---|---|---|
| Melee | 100 | 70 | 90 | 20 | 60 |
| Pierce | 120 | 50 | 80 | 5 | 20 |
| Charge | 120 | 80 | 100 | 10 | 50 |
| Crush | 50 | 50 | 50 | 300 | 150 |

Ranks 1–3 (gold-based rank-up in a garrison, [06-economy §5](06-economy.md)): +10 % HP and +10 % damage per rank. Counter triangle: spears/pikes > cavalry > archers > heavy infantry (slow, but strong vs light) > spears; siege > structures; anything melee > siege.

## 4. Fortifications
| Structure | Material (baseline) | HP | Build time | Notes |
|---|---|---|---|---|
| Palisade segment | 2 log | 200 | 6 s | cheap early wall; Woodfolk −50 % cost, +50 % HP |
| Stone wall segment | 3 stone | 1 200 | 20 s | effectively only damaged by `Crush`; Highlanders use ashlar, −25 % cost, +25 % HP |
| Gate | wall cost + 2 plank | as wall | +10 s | passable for owner + allies; lockable |
| Wall tower | 6 stone + 2 plank | 1 500 | 30 s | placed on a wall line; garrison 2–4 archer-type units; +2 range, Pierce taken −50 % for garrison |

Rules:
- Built only in own territory, on walkable tiles with slope ≤ 1; one segment per tile; diagonals connect.
- Placement validation: a wall may not cut a building's door tile from the rest of the territory (flood-fill check within the affected sectors) — otherwise the command is a no-op.
- Damaged segments are repaired by builders with ¼ of the material cost per 25 % HP.
- Destroyed segments leave rubble (passable, slows movement ×0.5) until cleared or rebuilt.
- Walls do not claim territory; they never block settlers of the owner (gates) but do block carriers of enemies (carriers stay in own territory anyway).

## 5. Military buildings & territory
Castle (garrison 10), large guard tower (6), small guard tower (3) as in [06-economy §5](06-economy.md). A building is captured when its garrison is defeated and a melee unit of the attacker enters it; ranged and siege units cannot capture. Siege may also destroy a building (no capture, territory becomes free).

## 6. Combat resolution (per tick, deterministic)
1. Units in `Engaging` state iterate in entity-id order.
2. Target acquisition every 5 ticks via sector-grid scan ([ADR 0008](decisions/0008-combat-model.md)); priority by stance → nearest by squared distance → lowest id.
3. Melee: attack when adjacent and cooldown 0. Ranged: spawn a pending hit with `arrivalTick = now + distance·4/projectileSpeed`; at arrival roll miss chance (`Combat` RNG) and apply damage if the target still exists.
4. Deaths are applied at the end of the tick (order-independent).
5. Movement uses tile occupancy (≤ 2 soldiers per tile); blocked units wait 1 tick, then request a local repath.

## 7. Performance
- Soldier cap 400 per player (ASSUMPTION, [ADR 0008](decisions/0008-combat-model.md)).
- Budget: combat + soldier movement ≤ 6 ms per tick with 3 000 soldiers engaged, on the reference machine (MacBook M5, [01-architecture §3](01-architecture.md)); part of the 20 ms sim budget. Verified in spike S5 (extended) and milestone benchmarks.

## 8. Rendering notes
Kenney [Castle Kit](https://kenney.nl/assets/castle-kit) provides walls, towers, gates and siege weapons for the prototype ([07-art-style §6](07-art-style.md)). Projectiles are client-side arcs interpolated from `(spawnTick, arrivalTick, from, to)` events.
