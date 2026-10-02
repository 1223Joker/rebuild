# ADR 0008 — Combat model: direct control, typed units, walls and siege

**Status:** approved by user (2026-10-02), with the soldier cap set to 800 by the user. Supersedes the duel-at-building combat sketch formerly in [06-economy §5](../06-economy.md).

## Context
- User wants "many different warriors, and big stone walls etc. to have a nice war experience"; chose **direct unit control**; wants stone walls + gates, wall towers with archers, siege units and wooden palisades ([USER-ANSWERS](../handoff/USER-ANSWERS.md), 2026-10-02).
- First LAN build: 1 culture, 2–3 warrior types, no walls. Full roster and fortifications follow.
- Constraints: deterministic integer sim, single-threaded, up to 8 players, thousands of settlers already in the tick budget ([01-architecture §3](../01-architecture.md)).

## Options
| Topic | Options | Decision | Why |
|---|---|---|---|
| Control | Indirect "attack building with N" (S2) / **direct select-move-attack (S4, RTS)** | Direct | User choice; needed for ranged units, walls, sieges |
| Damage model | single HP+damage / **attack type × armour class table** / full physics | Type × class table (integer %) | Readable counters (spear > cavalry, siege > walls) as in AoE II ([armor classes, AoE wiki](https://ageofempires.fandom.com/wiki/Armor_class_(Age_of_Empires_II))); cheap and deterministic |
| Projectiles | simulated ballistics / **delayed hit with integer flight time** | Delayed hit: hit resolved after `distance / projectileSpeed` ticks; miss chance from `Combat` RNG | No float trajectories; client animates arcs |
| Unit spacing | free steering / **tile occupancy (≤ 2 soldiers per tile, settlers ignore it)** | Tile occupancy with deterministic reservation | Simple, deterministic, fits grid pathing |
| Group movement | per-unit A* / **flow field per move order** | Flow field over HPA* corridor ([06-economy §6](../06-economy.md)) | One computation per order, many units |
| Walls in pathfinding | rebuild graph per team / **conditional gate edges** | Wall tiles block; gate tiles marked passable only for owner + allies; HPA* intra-cluster edges that cross a gate carry the gate id and are filtered per query | One abstract graph for all teams |
| Target acquisition | scan all enemies / **sector grid (16×16) neighbourhood scan** | Sector scan, nearest by squared distance, tie → lowest entity id, re-acquire every 5 ticks | Bounded, deterministic |

## Decision
- **Unit stats** (data-driven, per culture overridable, [ADR 0007](0007-culture-system.md)): HP, armour class (`Light`, `Heavy`, `Mounted`, `Structure`, `Siege`), attack type (`Melee`, `Pierce`, `Charge`, `Crush`), damage, range (sub-tile units), cooldown (ticks), speed, vision, cost, training building.
- **Damage** = `damage × table[type][class] / 100` (integer), minus rank bonus; ranks 1–3 via gold as before.
- **Fortifications** are structures on tiles: palisade (wood, cheap, low HP), stone wall (stone, high HP, only siege and `Crush` damage are effective), gate (passable for owner/allies, closable), wall tower (garrisons 2–4 archers, range + armour bonus). Built only in own territory, repaired by builders with materials.
- **Siege**: battering ram (melee `Crush`, slow, weak vs infantry) and catapult (long range `Crush`, minimum range, inaccurate).
- **Territory** still changes only when a military building's garrison is defeated and attackers occupy it.
- **Soldier cap**: **800 per player** (user, 2026-10-02). 8-player worst case = 6 400 soldiers + settlers. Combat + soldier movement budget: ≤ 10 ms per tick with 6 400 soldiers on the reference machine, verified in spike S5. Fallback if S5 fails: a total cap across all players (e.g. 4 000), so 800 per player stays possible in games with ≤ 5 players — needs user approval before applying.
- Unit roster and numbers: [11-military](../11-military.md).

## Consequences
- + Real RTS battles with counters, sieges and defensive play.
- − More commands (move/attack/stance) and larger payloads (unit id lists, capped at 200 ids per command).
- − Pathfinding gains per-team gate filtering; combat adds target-acquisition cost → spike S5 extended to include 6 400 soldiers in combat.
- − AI must handle walls and siege (planned after cultures, [05-ai](../05-ai.md)).
