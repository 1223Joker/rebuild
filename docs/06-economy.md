# 06 — Economy, Settlers, Logistics, Military, Pathfinding

Related: [00-vision MVP](00-vision.md) · [01-architecture](01-architecture.md) · [05-ai](05-ai.md). User decisions: free-walking carriers, no roads ([USER-ANSWERS](handoff/USER-ANSWERS.md) Q6); ≤ 8 players, 512² tiles, thousands of settlers (Q8).

Reference for the genre's rules: *The Settlers IV* manual — carriers "automatically go wherever they are needed", carriers stay inside the own territory ([S4 manual, replacementdocs](https://files.replacementdocs.com/The_Settlers_IV_-_Manual_-_PC.pdf)). Values below are ASSUMPTIONS to be tuned in playtests; they live in `data/*.json`, not in code.

## 1. MVP buildings (24)
| Group | Building | Size | Worker (tool) | Inputs → Output | Notes |
|---|---|---|---|---|---|
| Core | Castle | L | — | — | start building; storage; territory r=16; houses soldiers |
| Core | Storehouse | M | — | — | storage hub, reduces carry distances |
| Core | Residence | S | — | → carriers | +1 carrier / 60 s up to +10 per residence |
| Construction | Woodcutter | S | woodcutter (axe) | tree → log | work radius 8 |
| Construction | Forester | S | forester (shovel) | → plants tree | |
| Construction | Sawmill | M | sawyer (saw) | log → plank | |
| Construction | Stonecutter | S | stonecutter (pickaxe) | rock → stone | |
| Food | Fisher | S | fisher (rod) | fish (water) → fish | |
| Food | Hunter | S | hunter (bow) | game → meat | |
| Food | Farm | M | farmer (scythe) | → grain | sows + harvests fields in radius 4 |
| Food | Waterworks | S | water carrier (bucket) | → water | |
| Food | Mill | M | miller | grain → flour | |
| Food | Bakery | M | baker | flour + water → bread | |
| Food | Pig farm | M | pig farmer | grain + water → pig | |
| Food | Slaughterhouse | M | butcher (cleaver) | pig → meat | |
| Mining | Coal mine | S (on mountain) | miner (pickaxe) | 1 food (fish/meat/bread) → coal | depletes deposit |
| Mining | Iron mine | S | miner (pickaxe) | 1 food → iron ore | |
| Mining | Gold mine | S | miner (pickaxe) | 1 food → gold ore | |
| Metal | Iron smelter | M | smelter | iron ore + coal → iron | |
| Metal | Gold smelter | M | smelter | gold ore + coal → gold | |
| Metal | Toolsmith | M | smith (hammer) | iron + plank → tool (by quota) | |
| Metal | Weaponsmith | M | smith (hammer) | iron + coal → sword | |
| Military | Barracks | M | — | carrier + sword → soldier (rank 1) | |
| Military | Guard tower small / large | S / M | soldiers 1–3 / 1–6 | gold → rank-up of a garrisoned soldier | territory r=8 / r=12 |

## 2. Production chains
```mermaid
flowchart LR
  tree((Tree)) --> WC[Woodcutter] -->|log| SM[Sawmill] -->|plank| CON[Construction sites]
  FO[Forester] --> tree
  rock((Rock)) --> SC[Stonecutter] -->|stone| CON
  fishw((Water)) --> FI[Fisher] -->|fish| FOOD{{Mine food}}
  game((Game)) --> HU[Hunter] -->|meat| FOOD
  FA[Farm] -->|grain| MI[Mill] -->|flour| BA[Bakery]
  WW[Waterworks] -->|water| BA
  BA -->|bread| FOOD
  FA -->|grain| PF[Pig farm]
  WW -->|water| PF
  PF -->|pig| SH[Slaughterhouse] -->|meat| FOOD
  FOOD --> CM[Coal mine] & IM[Iron mine] & GM[Gold mine]
  IM -->|iron ore| IS[Iron smelter]
  CM -->|coal| IS & GS[Gold smelter] & WS[Weaponsmith]
  GM -->|gold ore| GS
  IS -->|iron| TS[Toolsmith] & WS
  SM -->|plank| TS
  TS -->|tools| SET[Specialists]
  WS -->|sword| BK[Barracks] -->|soldier| MIL[Towers / Castle]
  GS -->|gold| MIL
```
Goods (19): log, plank, stone, fish, meat, grain, flour, water, bread, pig, coal, iron ore, gold ore, iron, gold, sword, + tools (axe, saw, pickaxe, shovel, hammer, scythe, rod, bow, cleaver, bucket → tracked as one "tool" family with sub-type).

## 3. Settlers
| Role | Becomes one by | Behaviour |
|---|---|---|
| Carrier | spawned by castle/residence | executes transport jobs; idle carriers wait near storehouses |
| Builder (hammer) | carrier picks up tool | constructs sites step by step as materials arrive |
| Digger (shovel) | carrier picks up tool | levels the site's terrain before building |
| Specialist | carrier + tool when a finished building needs a worker | bound to its building; walks to resources in work radius |
| Soldier | barracks: carrier + sword | garrisons, attacks, defends |

Population cap: carriers ≤ residence capacity. Initial stock: castle with tools, planks, stone, food, 30 carriers, 10 soldiers (Normal start resources).

## 4. Logistics (S4-style free walking)
No roads. Goods lie at output piles, storehouses or construction sites. Carriers walk freely **inside their own territory** (allied territory: ASSUMPTION allowed, see [04-game-modes](04-game-modes.md)).

**Request/offer matching** (`LogisticsSystem`, every tick, bounded work):
- *Offers*: items in output piles, storehouses (each item has id, good type, location, reserved flag).
- *Requests*: construction sites (exact amounts), production inputs (refill to stock target, default 4), storehouses (absorb overflow from piles).
- Requests processed in deterministic order: `(goodPriority[player], buildingPriority, createdTick, requestId)`. Good priority is player-set ([01-architecture §4](01-architecture.md) `SetTransportPriority`).
- For each request: nearest unreserved offer by **sector distance** (map split into 16×16-tile sectors; ring search outward), then nearest idle carrier to that offer (same sector index). Reserve both; create a `TransportJob`. Max 200 matches per tick (ASSUMPTION; enough for 2 000 jobs/s).
- Ties broken by entity id → deterministic.

```mermaid
stateDiagram-v2
  [*] --> Idle
  Idle --> ToPickup: job assigned
  ToPickup --> Carrying: picked up item
  ToPickup --> Idle: offer vanished (job cancelled, request re-queued)
  Carrying --> Delivering: arrived
  Delivering --> Idle: item handed over
  Carrying --> DropAndIdle: destination destroyed / territory lost
  DropAndIdle --> Idle: item dropped as new offer
```

Construction: site placed → digger levels terrain → builder works while materials arrive (requests for planks/stone) → building finished → worker request.

Production: building cycles `wait inputs → work (N ticks) → place output in pile`; pile cap 8 → building pauses when full. Mines consume one food per cycle; deposits deplete.

Statistics: per player, per good, ring buffer of production/consumption per minute (used by UI and AI, [05-ai](05-ai.md)).

## 5. Territory & military
- Each military building claims a radius (castle 16, large tower 12, small tower 8 tiles). Ownership per tile; on overlap the older claim keeps the tile (S4/S2 convention). Recomputed incrementally only around changed buildings.
- Civilian buildings may only be placed in own territory; enemy civilian buildings inside newly captured territory burn down.
- **Combat** (deterministic duels, `Combat` RNG stream): attackers walk to the target building; defenders come out one at a time; a duel = alternating hit rolls each tick: hit chance and damage per rank (rank 1/2/3: HP 100/130/170, hit 50/55/60 %, damage 10–14/13–17/16–20, all integers). Attacker occupies the building when no defenders remain → territory transfer.
- Rank-up: garrisoned soldier consumes 1 gold → +1 rank (max 3).
- Monsters use the same duel system with their own stats ([05-ai §5](05-ai.md)).

## 6. Pathfinding & movement at scale
Grid: square tiles, 8-neighbour, octile integer costs 10/14 ([ADR 0006](decisions/0006-sim-core-conventions.md)), terrain cost multipliers (plains 1, forest 1.5, hill 2 — as integers ×10). Settlers **do not collide** with each other (as in the Settlers series) → no local avoidance needed for civilians.

| Need | Algorithm | Why |
|---|---|---|
| Short trips (most carrier jobs, < 32 tiles) | A* on tile grid, octile heuristic, binary heap with deterministic tie-break `(f, h, tileIndex)` | Simple, optimal ([Red Blob — A*](https://www.redblobgames.com/pathfinding/a-star/introduction.html)) |
| Long trips (≥ 32 tiles) | **HPA*** with 16×16 clusters: abstract graph of cluster entrances, local refinement ([Botea, Müller, Schaeffer 2004](https://www.semanticscholar.org/paper/Near-Optimal-Hierarchical-Path-Finding-Botea-M%C3%BCller/b0f0432ba69e4d730b93a75e3d19c8e9d811efac)) — up to 10× faster than A*, within ~1 % of optimal | 512² map = 1 024 clusters |
| Many units → one target (army attack, monster wave) | **Flow field** over the cluster corridor from HPA* ([Emerson, "Crowd Pathfinding and Steering Using Flow Field Tiles", Game AI Pro](http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf); [howtorts — flow fields](https://howtorts.github.io/2014/01/04/basic-flow-fields.html)) | one computation for N units |
| Reachability queries (placement, AI, map validation) | Region ids per connected land component + territory ownership | O(1) |

Scaling rules:
- **Path requests are queued** and served in request-id order with a **node-expansion budget per tick** (e.g. 60 000 expansions). A budget counted in nodes (not milliseconds) keeps results identical on every machine.
- Paths must be a pure function of `(grid version, start, goal)`. A **path cache** keyed by `(startCluster, goalCluster)` stores abstract routes; invalidation by per-cluster version counters when buildings/objects change. Cache hits change only speed, never results.
- Blocking changes (new building, tree felled) update only the affected cluster's entrances and intra-cluster edges.
- Movement: entity moves along tile waypoints in sub-tile units (1 tile = 256) at integer speed per tick; diagonal steps take 14/10 longer. Client interpolates.
- Target: 5 000 settlers, average ≤ 60 new path requests/s, pathfinding ≤ 5 ms per tick average on the reference machine (part of the 20 ms sim budget, [01-architecture §3](01-architecture.md)).

## 7. Out of scope for MVP
Roads (speed bonus), donkeys/trade, ships, magic, multiple tribes, wine/beer luxury goods, soldier types beyond one melee type, catapults.
