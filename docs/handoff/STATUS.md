# STATUS

**Last updated:** 2026-10-03 — M0 done; **M1 Map generation first pass done**; **M2 step 1 (map in the sim, start assignment, territory) done**; **M2 step 2 (building data, start castle entity, PlaceBuilding/CancelConstruction) done**; **M2 step 3 (goods, castle stock, construction, Demolish) done**; **M2 step 4 (carriers, A* pathfinding, movement) done**; **M2 step 5 (logistics: transport jobs for construction materials) done**; **M2 step 6 (production: woodcutter, sawmill, stonecutter; piles, cycles, harvesting, input requests, overflow) done**; **M2 step 7 (food chain and smelters: fisher, hunter, farm, waterworks, mill, bakery, pig farm, slaughterhouse, iron/gold smelter) done**; **M2 step 8 (mines: food alternatives, ore deposits with depletion) done**; **M2 step 9 (forester planting trees) done**; **M2 step 10 (toolsmith/weaponsmith with quotas) done** (2026-10-03); **M2 step 11 (production statistics) done** (2026-10-03); **M2 step 12 (workers with tools) done** (2026-10-03); **M2 step 13 (calendar + seasons: season-length lobby option, seasonal production speed) done** (2026-10-03); **M2 step 14 (storage capacity, mines without food) done** (2026-10-03); **M2 step 15 (household food + water, pantries, shortage states) done** (2026-10-03); **M2 step 16 (housing: beds as population cap, workers live and eat in homes, homeless settlers) done** (2026-10-03); **M2 step 17 (castle fetches food and water from other storages) done** (2026-10-03); **needs/seasons/weather designed** (2026-10-03, ADR 0009 proposed). M1: full pipeline, F1–F11 validation with retries, share codes, `mapgen` CLI with PNG preview, 24 golden map hashes, nightly 1 000-seed workflow; 96 tests.

## Current phase / step
**Implementation, milestone M2 Sim economy (headless)** ([09-roadmap](../09-roadmap.md)) — steps 1 (map in the sim + territory), 2 (building data + placement), 3 (goods + construction), 4 (carriers + A*), 5 (logistics for construction), 6 (first production buildings), 7 (food chain + smelters), 8 (mines), 9 (forester), 10 (smiths with quotas), 11 (production statistics), 12 (workers with tools), 13 (calendar + seasons), 14 (storage capacity, mines without food), 15 (household food + water) and 16 (housing) done; M0 Foundations and M1 Map generation complete. The user permitted coding on 2026-10-02 and chose "M0 directly, S1 folded in": S1's sim half is covered by M0; S1's Godot .NET export check moves to the start of M3. Process steps: [ORIGINAL-BRIEF.md §6](ORIGINAL-BRIEF.md).

## Done
- Step 0: git repo, [AGENTS.md](../../AGENTS.md), [CLAUDE.md](../../CLAUDE.md) (pointer), [ORIGINAL-BRIEF.md](ORIGINAL-BRIEF.md), this file.
- Step 1: research summarized in [RESEARCH-NOTES.md](RESEARCH-NOTES.md).
- Step 2: 10 clarifying questions asked and answered → [USER-ANSWERS.md](USER-ANSWERS.md).
- Step 3 deliverables:

| Deliverable | State |
|---|---|
| [decisions/](../decisions/README.md) ADR 0001–0006 | done (approved 2026-10-02) |
| [decisions/](../decisions/README.md) ADR 0007–0008 | done (approved 2026-10-02) |
| [00-vision.md](../00-vision.md) | done |
| [01-architecture.md](../01-architecture.md) | done |
| [02-networking.md](../02-networking.md) | done |
| [03-mapgen.md](../03-mapgen.md) | done |
| [04-game-modes.md](../04-game-modes.md) | done |
| [05-ai.md](../05-ai.md) | done |
| [06-economy.md](../06-economy.md) | done |
| [07-art-style.md](../07-art-style.md) | done |
| [08-testing.md](../08-testing.md) | done |
| [09-roadmap.md](../09-roadmap.md) | done (restructured into phases A–E) |
| [10-cultures.md](../10-cultures.md) | done (new) |
| [11-military.md](../11-military.md) | done (new) |
| [12-needs-seasons-weather.md](../12-needs-seasons-weather.md) + [ADR 0009](../decisions/0009-needs-seasons-weather.md) | done 2026-10-03 (design; ADR proposed, open question H9) |
| [open-questions.md](../open-questions.md) | done (living) |
| [GLOSSARY.md](GLOSSARY.md) | done (living) |
| [DECISIONS-LOG.md](DECISIONS-LOG.md) | living |

- Step 4 handoff test (2026-10-02): all relative links resolve; no context-dependent references; gaps fixed: AGENTS.md now links open-questions, research notes and the code layout; this file now contains the post-approval procedure. Not done: Mermaid diagrams were reviewed by eye, not machine-validated (no renderer installed) — validate when tooling exists (e.g. `npx @mermaid-js/mermaid-cli`).

- Open-questions round (2026-10-02): answers in [USER-ANSWERS.md](USER-ANSWERS.md); resulting changes: visual fog of war + `VisibilitySystem` added (04-game-modes, 01-architecture, 05-ai, roadmap M4 +1.5 w); LAN PvP checkpoint after M5; reference machine = user's Apple Silicon Mac; snapshot transfer and host-loss behaviour confirmed.

- New-requirements round (2026-10-02): cultures, many warriors, walls/siege, LAN-first priority, machines → ADR 0007/0008, docs 10/11, phased roadmap (≈ 114 w), updates to 00, 01, 04, 05, 06, 07, 08, glossary, AGENTS.

- Final question round (2026-10-02): H1–H6, B1, F2 answered → ADR 0007/0008 approved; soldier cap 800 (combat budget 10 ms for 6 400 soldiers); roadmap order now A LAN Alpha → B Cultures & war → C Steam → D AI & monsters → E Release (milestones renumbered); spikes still "Not yet".

- M0 Foundations, first pass (2026-10-02) — see [README.md](../../README.md) for layout and commands:

| M0 item ([09-roadmap §3](../09-roadmap.md)) | State |
|---|---|
| Repo layout, `Rebuild.sln`, `global.json` (SDK 10 → `net8.0`), `Directory.Build.props` (warnings = errors, no overflow checks, no unsafe) | done |
| Analyzers: BannedApiAnalyzers ([src/BannedSymbols.txt](../../src/BannedSymbols.txt)) + `Rebuild.Analyzers` RB0001 float, RB0002 unordered enumeration, RB0003 two RNG draws per statement, RB0004 async | done, tested |
| `Fix` Q48.16, `IntMath` (floor div, exact isqrt), `Pcg32` (reference-vector tested), `SplitMix64`, RNG streams | done |
| Canonical writer/reader, XxHash64 state hash, savegame format, save/load equivalence test | done |
| `Command`, `CommandType` catalogue, `TurnBundle`, meta commands + validation, `.rblog` format, `Replay` | done |
| Culture data loader: `data/cultures/rivermen/culture.json` → source generator → `CultureCatalog` + per-player tables; data hash in `GameVersion` | done (modifiers + hooks only; buildings/goods/units come with M2/M4) |
| `Rebuild.Tools`: `version`, `probe`, `replay`, `sample-log`, `hashes` | done |
| Golden values [tests/golden/hashes.txt](../../tests/golden/hashes.txt): 1 M-step probe + scripted 3 000-turn replay | done; identical in Debug and Release on the MacBook M5 |
| CI ([.github/workflows/ci.yml](../../.github/workflows/ci.yml)): 4 runners × Debug/Release, float-ban check, cross-OS hash comparison | **green** on https://github.com/1223Joker/rebuild (run 37069100671, 2026-10-02): all 8 jobs pass, `cross-os-hashes` confirms identical probe + replay hashes on Linux x64, Windows x64, macOS arm64, macOS x64 in Debug and Release |

- M0 leftovers done (2026-10-02): CI actions bumped to the Node 24 majors (`checkout@v5`, `setup-dotnet@v5`, `upload-artifact@v6`, `download-artifact@v6` — tag existence not verifiable from the container, confirm on the first CI run); new CI job `golden-version` runs [tools/ci/check-golden-version.sh](../../tools/ci/check-golden-version.sh): a changed probe/replay value needs a changed `version` line, a changed map value a changed `generator` (or `version`) line.

- M1 Map generation, first pass (2026-10-02) — details and measurements: [03-mapgen §9](../03-mapgen.md):

| M1 item ([09-roadmap §3](../09-roadmap.md)) | State |
|---|---|
| `MapSpec` v2 (teams, terrain mix, resources, symmetry, monsters, generator version) + `Check()`; `GameVersion` → 0.2.0 | done |
| Pipeline steps 1–12 incl. land corridors (new step 2b), symmetry modes None/Mirror/Rotational/Equalized | done |
| Validation F1–F11 + deterministic retry (16 attempts, `MapGenResult` with report on failure) | done |
| Share code `RB-…` (Crockford base32 + checksum) | done |
| `rebuild-tools mapgen` (report, `--png`, `--stats`, `--min-first-pass`) | done |
| 24 golden map hashes (`tests/golden/mapgen.json` → `map` lines in `hashes.txt`) | done; **identical on all 4 runners × Debug/Release** (CI run 37073166996 green incl. `cross-os-hashes` and `golden-version`) |
| Nightly 1 000 seeds/size ([.github/workflows/nightly.yml](../../.github/workflows/nightly.yml)) | added; local run: 99.5–100 % first-attempt pass |
| Perf: XL attempt ≤ 1.5 s, p99 ≤ 4 s | MacBook M5: 48 ms mean / 56 ms p99 (XL, 8 starts, 100 seeds) |
| `.rbmap` save files, lobby preview & hash check | not started (belongs with the M5 lobby) |

| M2 step 1 — map in the sim + territory (2026-10-02) | State |
|---|---|
| `Simulation.Create` generates the map from `MatchSetup.Map` with the M1 generator (unchanged); `MapHash` in the state hash; load regenerates the map and rejects a hash mismatch (save format 2) | done |
| `World/StartAssignment`: k-th Human/AI slot → start k, team must equal `MapSpec.TeamOf(k)`; invalid setups throw | done |
| `World/Territory`: castle claims r = 16 (ASSUMPTION constant until building data exists), older claim wins, incremental add/remove = full rebuild (brute-force tests), owner grid hashed + saved | done |
| `Simulation.StartOf`, `AreAllies` (monsters have no allies) | done |
| `GameVersion` 0.3.0; golden replay `m0-meta.rblog` regenerated (its setup now matches its map: 3 starts, teams 0/1/1, monsters Low) | done |
| Code review (`/code-review`, medium): 2 low findings fixed (radius > ushort, `NextClaimId` < 1 on load) | done |
| Tests / CI | 110 pass in Debug and Release locally; **CI run 37077142734 green** on `main` (06fc05f): 4 runners × Debug/Release, `cross-os-hashes` (new replay hash identical everywhere) and `golden-version` pass |

| M2 step 2 — building data + placement (2026-10-02) | State |
|---|---|
| `data/buildings.json`: 25 shared-core types (castle, storehouse, …, guard towers) with size, placement (land/mountain), territory radius, plank/stone cost (ASSUMPTION) | done |
| `BuildingDataGenerator` (source generator, RB0101 on bad data) → `BuildingCatalog`, `BuildingIds`; data hash folded into `GameVersion.DataHash` | done |
| `World/BuildingRegistry`: buildings (id, type, owner, top-left tile, rotation, state, claim) + occupancy grid; footprints S 2², M 3², L 4² keep a 1-tile margin; state hashed + saved (format 3), load validates ids/types/owners/overlap/claims | done |
| Start castle = complete castle entity centred on the start, owns the r = 16 claim (radius now from data; `Simulation.CastleTerritoryRadius` removed) | done |
| `PlaceBuilding` (u16 type, u16 x, u16 y, u8 rotation) → construction site, validated by `World/BuildingPlacement` (own territory, terrain, margin; castle not placeable); `CancelConstruction` (i32 id) removes an own site | done |
| Golden replays: `m0-meta.rblog` regenerated, new `m2-build.rblog` (600 turns of valid/invalid placements and cancels, `sample-log --script build`); `GameVersion` 0.4.0 | done |
| Code review (`/code-review`, medium): 1 finding fixed — data generators crashed (instead of RB0100/RB0101) on `null` values or out-of-range numbers | done |
| Tests / CI | 127 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 from Ubuntu apt); **CI run 37078721964 green** on `main` (3d29b33): 4 runners × Debug/Release, `cross-os-hashes` (both replay hashes identical everywhere) and `golden-version` pass |

| M2 step 3 — goods, castle stock, construction (2026-10-03) | State |
|---|---|
| `data/goods.json`: 28 goods (18 shared goods + one per tool kind) with start-castle stock (ASSUMPTION) → `GoodDataGenerator` (RB0102 on bad data) → `GoodCatalog`, `GoodIds`; data hash folded into `GameVersion.DataHash` | done |
| `"storage": true` for castle and storehouse; complete storage buildings own a per-good stock; start castles get the start stock | done |
| `World/Construction` (first system in `StepTick`): placeholder supply 1 unit/site/s from the owner's lowest-id storage (planks, then stone; older sites first), 20 build ticks per unit, completion adds the territory claim of military buildings and an empty stock to storehouses | done |
| `CancelConstruction` refunds delivered materials; new `Demolish` (i32 id; own complete building, not the castle) removes building, stock and claim | done |
| Save format 4 (progress + stocks, load validates consistency); `GameVersion` 0.5.0; both golden replays regenerated, `m2-build.rblog` now also demolishes and completes buildings (open valid sites capped at 4 per player) | done |
| Code review (`/code-review`, medium) | no findings |
| Tests / CI | 137 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112); float-ban, golden-version and hash comparison scripts pass locally; **CI run 37080692692 green** on `main` (c40b5cd): 4 runners × Debug/Release, `cross-os-hashes` (both new replay hashes identical everywhere) and `golden-version` pass |

| M2 step 4 — carriers, A* pathfinding, movement (2026-10-03) | State |
|---|---|
| `"carriers"` in `data/buildings.json` (castle 30, residence 10; ASSUMPTION) → `BuildingDefinition.Carriers`; a complete building keeps that many carriers, refilled 1 per 60 s at its door; the start castle starts full | done |
| `World/Pathfinder`: A* on the tile grid, 8 neighbours, costs 10/14, no corner cutting, heap tie-break `(f, h, tile)`, expansion limit per search (2 048) and per tick (60 000) | done; cost equals a brute-force Dijkstra on 200 random grids |
| `World/Settlers` (second system in `StepTick`): settler entities (owner, home, tile, idle/walking, sub-tile progress, path); walk only on walkable, building-free tiles of own territory at 2.5 tiles/s (diagonal 14/10); blocked walkers stop and re-plan; settlers covered by a new building are put at its door; idle carriers wander within 6 tiles of their home door (placeholder until logistics) | done |
| Save format 5 (settlers + paths, load validates ids, owners, homes, tiles, path chains, state/progress); `GameVersion` 0.6.0; both golden replays regenerated | done |
| Code review (`/code-review`, medium): 2 findings fixed — a carrier covered by a newly placed building, or spawned at the footprint centre when no door tile was free, was stuck forever | done |
| Tests / CI | 148 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112); float-ban, golden-version and hash comparison pass locally; **CI run 37084956265 green** on `main` (dba6f46): 4 runners × Debug/Release, `cross-os-hashes` (both new replay hashes identical everywhere) and `golden-version` pass |

| M2 step 5 — logistics: transport jobs for construction materials (2026-10-03) | State |
|---|---|
| `World/Logistics` (runs every tick between construction and movement): sites request missing planks/stone (minus units on the way), storage stocks offer them; sites in id order, planks first; nearest storage by 16×16-sector distance, then nearest idle carrier, ties by id; ≤ 200 jobs/tick; matched units leave the stock at once | done |
| `TransportJob` (id, owner, carrier, good, source, destination, `ToPickup`/`Carrying`); carriers walk to the source door, pick up, walk to the site door, hand over; job searches up to 16 384 expansions | done |
| Failure handling: pickup for a vanished site or from an unreachable source → unit back to the stock; carried unit for a vanished/unreachable site → nearest storage (lost if that fails too, ASSUMPTION until ground piles); a building a carrier failed to reach is skipped for 30 s (found as a carrier churn loop in the build script) | done |
| Placeholder supply (1 unit/site/s straight from storage) and the planks-before-stone rule removed; idle carriers without a job still wander | done |
| Save format 6 (settler `JobId`, jobs, back-offs; load validates ids, owners, goods, live sources being storages, one-to-one carrier links, no over-delivery); `GameVersion` 0.7.0; both golden replays regenerated; `m2-build.rblog` placements beyond 4 open sites now use the invalid rotation 4 | done |
| Code review (`/code-review`, medium): 1 finding fixed — a save whose job pointed at a non-storage source loaded and later crashed with a `NullReferenceException` | done |
| Tests / CI | 153 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112); float-ban and golden-version checks pass locally; **CI run 37088928969 green** on `main` (e8429d6): 4 runners × Debug/Release, `cross-os-hashes` (both new replay hashes identical everywhere) and `golden-version` pass |

| M2 step 6 — production buildings (2026-10-03) | State |
|---|---|
| `"production"` in `data/buildings.json` (inputs ≤ 2, optional harvest tree/stone + radius, output, cycle ticks; RB0101 on bad data, unknown goods fail the build) → `ProductionDefinition`; woodcutter (tree → log, r 8, 15 s), sawmill (1 log → 1 plank, 6 s), stonecutter (stone → stone, r 8, 15 s), all ASSUMPTION | done |
| `World/Production` (runs after construction, before logistics): complete production buildings own input piles + an output pile; a cycle starts with inputs present, a harvest object in reach on own territory and room in the output pile (pile + reserved + 1 ≤ 8), inputs taken at the start; at the end the nearest object loses a unit and one output unit is piled; no workers yet (ASSUMPTION) | done |
| `World/MapChanges`: harvested tiles (tile, object, amount) as sim state, applied to the regenerated map on load (validated: was tree/stone, now empty or fewer stone units); freed tiles become buildable; `MapGenerator.TileFlagsAt` extracted (map hashes unchanged) | done |
| Logistics: three passes per tick in building id order — site materials, production inputs (refill to 4 incl. units on the way), overflow (output pile → nearest storage); offers = storage stocks + output piles, never the requester itself; a carried unit whose destination is unreachable goes to the nearest storage not marked unreachable | done |
| Save format 7 (building cycle + piles, map changes; load validates piles, cycle < cycle ticks, input pile + on the way ≤ 4, output pile + reserved + running cycle ≤ 8, live job sources = storage or matching producer); `GameVersion` 0.8.0; both golden replays regenerated; `m2-build.rblog` now places woodcutters/stonecutters with resources in reach so production runs | done |
| Code review (`/code-review`, medium): 1 finding fixed — overflow carried to a storage that had become unreachable was dropped instead of going to another storage (regression test added) | done |
| Tests / CI | 162 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112); float-ban and golden-version checks pass locally; **CI run 37092501976 green** on `main` (f90bde2): 4 runners × Debug/Release, `cross-os-hashes` (both new replay hashes identical everywhere) and `golden-version` pass |

| M2 step 7 — food chain and smelters (2026-10-03) | State |
|---|---|
| `HarvestSource` (tree, stone, game, fish, water, fertile) replaces the tree/stone object harvest; objects and the fish resource lose one unit per cycle, water and fertile terrain are only required in reach on own territory; `"harvest"` in `data/buildings.json` accepts the new kinds (RB0101 otherwise) | done |
| Production data (ASSUMPTIONS): fisher (fish r 6, 15 s), hunter (game r 10, 20 s), farm (fertile r 4, 30 s; fields abstracted), waterworks (water r 4, 9 s), mill (grain → flour), bakery (flour + water → bread), pig farm (grain + water → pig), slaughterhouse (pig → meat), iron/gold smelter (ore + coal → metal) | done |
| `World/MapChanges` records the resource layer (fished tiles); load validation: object gone/reduced with resource untouched, or fish gone/reduced on a tile without object; save format 8; `GameVersion` 0.9.0; both golden replays regenerated; `m2-build.rblog` now also places fishers, hunters, farms and waterworks with their source in reach | done |
| Code review (`/code-review`, medium): no code findings; docs (06-economy §4, STATUS, DECISIONS-LOG) brought up to date as it asked | done |
| Tests / CI | 170 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112); float-ban, golden-version and hash comparison pass locally; **CI run 37095410902 green** on `main` (15f3c5b): 4 runners × Debug/Release, `cross-os-hashes` (new build replay hash identical everywhere) and `golden-version` pass | done |

| M2 step 8 — mines (2026-10-03) | State |
|---|---|
| Input piles with alternative goods: `"fish|meat|bread": 1` in `data/buildings.json` → `ProductionDefinition.Alternatives` (generator splits on `|`, RB0101 on a good used twice); `InputIndexOf` finds any alternative | done |
| Logistics: a pile with alternatives takes the nearest offer of any of its goods; a storage gives the one it holds most of (ties: data order) | done |
| `HarvestSource` coal / iron_ore / gold_ore = ore deposit (resource on a mountain tile without object); coal, iron and gold mine (S, mountain): 1 food → 1 coal / iron ore (15 s) / gold ore (20 s), deposit within r 3 loses one unit per cycle, an exhausted mine idles (all ASSUMPTIONS) | done |
| `World/MapChanges` accepts dug-out/reduced deposits on load (save layout unchanged, format 8); `GameVersion` 0.10.0; both golden replays regenerated (`m0-meta` hash unchanged), `m2-build.rblog` now also places coal/iron/gold mines with their deposit in reach | done |
| Code review (`/code-review`, medium) | no findings |
| Tests / CI | 175 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime); float-ban and golden-version checks pass locally; **CI run 37098731514 green** on `main` (0a77c94): 4 runners × Debug/Release, `cross-os-hashes` (new build replay hash identical everywhere) and `golden-version` pass | done |

| M2 step 9 — forester (2026-10-03) | State |
|---|---|
| `"plant": "tree"` production in `data/buildings.json` → `ProductionDefinition.Plant` (no inputs, `Output` = `NoOutput`); generator RB0101 on `plant` with `output`/`harvest`, without `radius`, or a kind other than `tree` | done |
| `Production.FindPlantSite`: nearest free tile within r 6 on own territory — buildable land without resource, not on/next to a building footprint, no tree among its 8 neighbours; forester cycle 12 s plants one full-grown tree (`MapChanges.Plant`), idles when no tile is free (all ASSUMPTIONS) | done |
| Load validation: planted (or planted-and-felled) trees accepted on tiles without resource that are buildable once cleared and whose generated object was none/tree/stone/game; a forester's output pile must be empty; save layout unchanged (format 8); `GameVersion` 0.11.0; both golden replays regenerated (`m0-meta` hash unchanged), `m2-build.rblog` now also places foresters (a tree gets planted) | done |
| Code review (`/code-review`, medium): 2 findings fixed — a save could give a forester a non-empty output pile (would carry the non-good `NoOutput`), and a planted tree on a slope too steep to plant on loaded (regression cases added) | done |
| Tests / CI | 180 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime); float-ban and golden-version checks pass locally; **CI run 37101970567 green** on `main` (ccc9381): 4 runners × Debug/Release, `cross-os-hashes` (new build replay hash identical everywhere) and `golden-version` pass | done |

| M2 step 10 — toolsmith/weaponsmith with quotas (2026-10-03) | State |
|---|---|
| `"outputs"` production in `data/buildings.json` → `ProductionDefinition.Outputs`/`HasChoice`/`OutputIndexOf`; one output pile per output good, cap 8 over all output piles; generator RB0101 on fewer than 2 / more than 16 outputs, duplicates, or `outputs` with `output`/`plant` | done |
| Toolsmith: iron + plank → one of 10 tools, 9 s; weaponsmith: iron + coal → sword/spear/bow, 12 s (ASSUMPTIONS; one input set for all weapons) | done |
| `World/ProductionQuotas`: per-player weight 0..10 (default 1) + credit per quota good; output picked at cycle start by smooth weighted round robin (ties data order), all weights 0 → idle; `Building.Choice` holds the running cycle's pick | done |
| Command `SetToolProductionQuota` (201; payload u16 good, u8 weight; `EconomyCommands`), validated (quota good, weight ≤ 10, payload length, acting slot); a change resets the player's credits | done |
| Logistics: overflow per output pile, offers/reservations/returns use the good's own output pile | done |
| Save format 9 (`Choice` per building; weights + credits per player; load checks choice/cycle consistency, weights, credit 0 at weight 0, credit range, credits balanced per smith); `GameVersion` 0.12.0; both golden replays regenerated; `m2-build.rblog` now also places a toolsmith and a weaponsmith per slot at turn 350 and sends quota commands every 40 turns (some invalid) | done |
| Code review (`/code-review`, medium): 1 finding fixed — a save with unbalanced quota credits loaded and broke the next save/load round trip (balance check + regression case added) | done |
| Tests / CI | 187 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime); float-ban, golden-version and hash comparison pass locally; **CI run 37105619781 green** on `main` (0a28fb0): 4 runners × Debug/Release, `cross-os-hashes` (both new replay hashes identical everywhere) and `golden-version` pass | done |

| M2 step 11 — production statistics (2026-10-03) | State |
|---|---|
| `World/ProductionStatistics`: per player and good, produced/consumed units per game minute (600 ticks) in a 60-minute ring buffer (ASSUMPTION) + 64-bit match totals; slot of a minute cleared right after the tick that starts it; queries `Produced`/`Consumed(player, good, minutesAgo)`, `TotalProduced`/`TotalConsumed` | done |
| Produced = output unit piled at the end of a cycle (`Production`); consumed = unit handed over to an input pile or a construction site (`Logistics.Advance`; ASSUMPTION: at hand-over, alternative piles lose the good's identity) | done |
| Save format 10 (only the slots in use, running minute from the tick; load rejects negative counts and totals below their per-minute counts); `GameVersion` 0.13.0; both golden replays regenerated | done |
| Code review (`/code-review`, medium): 1 finding fixed — a save with a negative tick loaded and crashed on the next statistics update (load now rejects it; regression test added) | done |
| Tests / CI | 194 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime); float-ban and golden-version checks pass locally; **CI run 37108426652 green** on `main` (1e03ac5): 4 runners × Debug/Release, `cross-os-hashes` (both new replay hashes identical everywhere) and `golden-version` pass | done |

| M2 step 12 — workers with tools (2026-10-03) | State |
|---|---|
| `"tool"` per production in `data/buildings.json` → `ProductionDefinition.Tool` (`NoTool` for mill, bakery, pig farm, smelters); 14 buildings need one (axe, shovel, saw, pickaxe, fishing rod, hunting bow, scythe, bucket, cleaver, hammer) | done |
| `SettlerKind.Worker`: a complete production building only starts cycles with its worker inside (`Settlers.Working`); workers stand at the door and never walk (ASSUMPTION); a demolished workplace frees its worker as an unhomed carrier that carries its tool back to the nearest storage (user 2026-10-03: tools are not lost; `GameVersion` 0.15.0) | done |
| Logistics: worker requests matched first each tick (buildings in id order): nearest storage holding the tool + idle carrier nearest to it, or straight to the building without a tool; `TransportJob.Kind` = `Employ`; tool reserved at once and kept by the worker (not counted as consumed); no tool in stock → the building waits; vanished/unreachable workplace → fetched tool to the nearest storage, unpicked tool back to stock | done |
| Save format 11 (job kind; load validates worker jobs, ≤ 1 worker inside or on the way per building, workers only in complete production buildings, running cycle only with its worker); `GameVersion` 0.14.0; both golden replays regenerated (`m0-meta` hash unchanged); build script now places a forester instead of a woodcutter every 2nd instead of every 3rd harvester turn, so a staffed forester still plants within 600 turns | done |
| Code review (`/code-review`, medium) | no findings |
| Follow-up (user, 2026-10-03): tool return + **Intel macOS dropped** — CI matrix now `ubuntu-latest`, `windows-latest`, `macos-latest` (arm64) × Debug/Release; AGENTS, 00-vision, 08-testing, 09-roadmap, ADR 0001 updated; **CI run 37114440857 green** on `main` (9d367f5): 3 runners × Debug/Release, `cross-os-hashes` and `golden-version` pass | done |
| Tests / CI | 202 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime); float-ban and golden-version checks pass locally; **CI run 37112101439 green** on `main` (5252749): 4 runners × Debug/Release, `cross-os-hashes` (new build replay hash identical everywhere) and `golden-version` pass | done |

| M2 step 13 — calendar + seasons (2026-10-03) | State |
|---|---|
| `MatchSetup.Seasons` lobby option (`SeasonLength` Off / Short 4 min / **Normal 6 min** (default) / Long 9 min per season), written after the slot table; `.rblog` format 2 | done |
| `World/Calendar`: pure function of the tick — `SeasonAt` (match starts on the first tick of spring; Off = eternal summer), `YearAt`, `TicksIntoSeason`, `SeasonTicks`; no state of its own; `Simulation.Season` = season of the next tick | done |
| `"seasons"` per production in `data/buildings.json` → `ProductionDefinition.SeasonSpeed` (percent per season, default 100; 0 or 25..400, generator RB0101 otherwise); `CycleTicksIn(season)` = ticks × 100 / speed rounded up; farm autumn 125 % (240 ticks) and winter 0 (no new cycle; one already running ends after its normal 300 ticks, ASSUMPTION); fisher winter 50 % (300 ticks, ice) | done |
| `Production.Step`: no cycle starts in a season with speed 0; a cycle ends once its elapsed ticks reach the current season's length (a season change shortens/lengthens the running cycle) | done |
| Save format 12 (setup carries the season length; complete-building cycle bound = longest season cycle; load rejects a running cycle at or past the length of the season of the last tick); `GameVersion` 0.16.0; both golden replays regenerated — only the log header changed, replay hashes unchanged (both replays end in the first spring) | done |
| Not yet: winter walking −20 %, housing, pantries, shortage states, heating (next steps) | open |
| Code review (`/code-review`, medium) | no findings |
| Tests / CI | 207 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime; new `SeasonTests`: calendar, data, setup/save round trip, a farm and fisher through a Short-season year, load rejection); float-ban and golden-version checks pass locally; **CI run 37115210539 green** on `main` (9ccc56c): 3 runners × Debug/Release, `cross-os-hashes` and `golden-version` pass | done |

| M2 step 14 — storage capacity + mines without food (2026-10-03, the two queued user requests) | State |
|---|---|
| Mines (coal, iron, gold) no longer take `fish|meat|bread`; they only need the miner and a deposit in reach (12 §1, ADR 0009, 06-economy updated). The alternative-goods pile (`"a|b|c"`) stays in the code for the planned home fuel pile but no building uses it now | done |
| `"storage"` in `data/buildings.json` is a capacity (castle 500, storehouse 300; ASSUMPTIONS; generator range 1..100 000) → `BuildingDefinition.StorageCapacity` | done |
| Overflow goes only to the nearest reachable storage with room (capacity − stock − units on the way); none → the output pile fills to 8 and the building pauses until room appears. Returned units (refunds, tools, retargeted units) may exceed the capacity (ASSUMPTION: never lost) | done |
| No new sim state (save format 12 unchanged); `GameVersion` 0.17.0; `m2-build` golden replay regenerated (`m0-meta` hash unchanged) | done |
| Code review (`/code-review`, medium) | no code findings; flagged stale docs (mine food in 06-economy) → fixed |
| Ponytail: `.claude/settings.json` enables the plugin, but in this cloud session it was **not installed** (`~/.claude/plugins/installed_plugins.json` empty, no ponytail skills; cloning its repo was refused by the session's permission policy). Its rules (YAGNI, stdlib, smallest diff) were followed by hand per AGENTS.md | note |
| .NET in the cloud session: `builds.dotnet.microsoft.com` is blocked by the egress proxy; installed instead from Ubuntu's archive (`apt-get install dotnet-sdk-10.0 dotnet-runtime-8.0`, SDK 10.0.112) | note |
| Tests / CI | 209 pass in Debug and Release locally (Linux x64; new: storage capacity data, a woodcutter filling its pile when the castle is full and resuming when room appears, a mine digging without food); float-ban and golden-version checks pass locally; **CI run 37116308974 green** on `main` (ecc6360): 3 runners × Debug/Release, `cross-os-hashes` and `golden-version` pass | done |

| M2 step 15 — household food + water (2026-10-03, scheduled run) | State |
|---|---|
| `World/Households` (runs after production, before logistics): homes = complete buildings with `carriers` (castle, residence); occupants = carriers homed there (ASSUMPTION: workers eat nothing until housing); per home 4 counters (food/water due, food/water unpaid); 1 food per settler per 6 000 ticks, 1 water per 4 800 ticks | done |
| Castle eats from its own stock (food good held most, ties fish → meat → bread); residences own 2 pantry piles (food = fish\|meat\|bread, water) refilled by logistics to 2 (pass 1 with production inputs); consumption counted when eaten from a stock or handed over to a pantry | done |
| Shortage states Supplied / Short (unpaid 60 s food, 30 s water) / Crisis (+180 s / +120 s): Short blocks carrier spawns, Crisis removes the highest-id job-less carrier of the home on entry and every 60 s; one unit eaten → Supplied at once | done |
| Start stock +30 water (ASSUMPTION); save format 13 (pantry piles ≤ 2 + on the way, need counters: due ≤ period, unpaid only while due); `GameVersion` 0.18.0; both golden replays regenerated | done |
| Not built: work/walk slowdowns while Short, specialists living in homes, beds as population cap, homeless settlers, variety bonus, heating; castle cannot receive food from storehouses (known limitation, see next steps) | open |
| Ponytail: plugin still not installed in the cloud session (`installed_plugins.json` empty); its repo was cloned to the scratchpad and `skills/ponytail/SKILL.md` (full) followed by hand — fixed pantry target 2 and a fixed food-goods list instead of new data fields | note |
| Code review (`/code-review`, medium) | no bugs; flagged the castle-starves-while-storehouse-has-food limitation → documented |
| Tests / CI | 213 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime; new `HouseholdTests`: castle eating order, Short → Crisis → departures → recovery, residence pantry, load rejection); 8 existing tests adjusted for the eating castle; float-ban and golden-version checks pass locally; **CI run 37118739837 green** on `main` (2804ae6): 3 runners × Debug/Release, `cross-os-hashes` (both new replay hashes identical everywhere) and `golden-version` pass | done |

| M2 step 16 — housing (2026-10-03, scheduled run) | State |
|---|---|
| Data field `"carriers"` → `"beds"` (`BuildingDefinition.Beds`; castle 30, residence 10) | done |
| `Settler.HomeId` = the home whose bed a settler has (carriers and workers); new `Settler.WorkplaceId` (a worker's production building, 0 for carriers) and `HomelessTicks`; a carrier hired as worker keeps its bed | done |
| Beds cap the population: a home spawns a carrier only while settlers homed there < beds; workers count as occupants and eat at home (`Households`) | done |
| Homeless (`Households.Step`): a settler whose home is gone takes a free bed in its owner's nearest home (sector distance, ties lower id) every tick; after 120 s a worker leaves its workplace as a carrier bringing its tool back (`Logistics.ReleaseWorkerAt`), a job-less carrier leaves the map; homeless carriers are not hired | done |
| Crisis with only workers left in a home: the highest-id worker leaves its workplace (found by code review) | done |
| Load: settler workplace ⇔ worker, homeless ticks ≤ 120 s; a carried job's source may be any building (a released worker's tool from a standing workplace — found by the new tests) | done |
| Save format 14; `GameVersion` 0.19.0; both golden replays regenerated | done |
| Not built (ASSUMPTIONS / next): specialists choose the nearest free bed at hiring (they stay in the home that spawned them), workers walking from home to work, castle requesting food from storehouses (done in step 17), Short-state slowdowns | open |
| Ponytail: plugin still not installed in the cloud session (`installed_plugins.json` empty); its repo was cloned to the scratchpad and `skills/ponytail/SKILL.md` followed by hand — one bed field reused as the population cap, no new building data or systems | note |
| Code review (`/code-review`, medium) | 2 findings fixed: a home in Crisis with only workers never lost an occupant; a homeless carrier could be hired and released again the next tick (homeless carriers are no longer hired); stale `$comment`/docs updated |
| Tests / CI | 216 pass in Debug and Release locally (Linux x64, .NET SDK 10.0 + .NET 8 runtime from Ubuntu apt; new `HousingTests`: re-housing after demolition, leaving after 120 s, worker beds and homeless worker release, Crisis with only a worker left); float-ban and golden-version checks pass locally; **CI run 37121955045 green** on `main` (d76c109): 3 runners × Debug/Release, `cross-os-hashes` and `golden-version` pass | done |

| M2 step 17 — castle food and water from storehouses (2026-10-03, scheduled run) | State |
|---|---|
| Logistics pass 1: a storage home (castle) requests food and water like a pantry, counting the need's goods in its own stock, up to `Households.StockTarget` = max(2, beds / 4) = 7 per need; sources = the owner's other storages and output piles (never itself), units on the way count; residences keep `PantryTarget` 2 | done |
| No new state (save format 14); `GameVersion` 0.20.0; both golden replays regenerated (only the header changed, replay hashes unchanged) | done |
| Not built / ASSUMPTIONS: requests may exceed storage capacity (≤ 7 per need); no guard against two storage homes of one owner trading units back and forth (one castle per player today) | open |
| Ponytail: plugin still not installed in the cloud session (`installed_plugins.json` empty); its repo was cloned to the scratchpad and `skills/ponytail/SKILL.md` followed by hand — the existing pantry request reused for storage homes, no new system or data field | note |
| Code review (`/code-review`, medium) | 1 finding fixed: the first draft used the beds/4 target for pantries too, while save/load still caps pantry piles at 2 (would break for a home with ≥ 12 beds) → beds/4 now only for storage homes |
| Tests / CI | 217 pass in Debug and Release locally (Linux x64, .NET SDK 10.0.112 + .NET 8 runtime from Ubuntu apt; new `HouseholdTests.The_castle_fetches_food_and_water_from_a_storehouse`, fails without the change); float-ban and golden-version checks pass locally; CI: see below | done |

- Design round 2026-10-03 (user request): population needs (every settler needs a bed, food, water; winter heating with log/coal; culture differences) and seasons/weather events (blizzard/thunderstorm: shelter or die, lightning, fire, floods, …) → [12-needs-seasons-weather](../12-needs-seasons-weather.md), [ADR 0009](../decisions/0009-needs-seasons-weather.md) (proposed, H9), roadmap M2 +3 w and new M7b (+4 w, total ≈ 121 w). No code changed yet.

## In progress
- Nothing.

## Next steps
1. Keep CI green (`gh run list -R 1223Joker/rebuild`).
2. M1 polish: tune `Dmin`, `Rf`, `Lmin` and the ASSUMPTION thresholds (F5/F6 minimums, fertile share, lair counts) when gameplay exists (spike S4 is done as part of M1; all spikes are allowed, user 2026-10-02).
3. Run spike **S5** (headless logistics + HPA* + combat scale, [09-roadmap §2](../09-roadmap.md)) alongside the start of M2, so the 20 ms/tick budget is checked before the economy design hardens.
4. Continue **M2 Sim economy (headless)** ([06-economy](../06-economy.md)): next step = **Short-state slowdowns, then winter heating (fuel piles, cold states) and winter walking −20 %** ([12-needs-seasons-weather](../12-needs-seasons-weather.md), user request 2026-10-03; mines eat no work ration since step 14), builders/diggers (construction workers with hammer/shovel), workers walking to their resources, player transport priorities, terrain costs + HPA*.
5. Queued user requests: none (both 2026-10-03 requests done in M2 step 14).
6. Before M3: run S1's Godot part — install the Godot **.NET** edition (the installed `/Applications/Godot.app` 4.7.2 is the standard build without C#) and export a test project for macOS and Windows — and spike S2 (rendering scale). Before M5: spike S3 (ENet transport).

## Blockers / waiting for user approval
- None. Old remote branches: on 2026-10-03 (after M2 step 13) the remote still lists `claude/amazing-hopper-btzuh2`, `claude/amazing-hopper-ttuac1` and `claude/trusting-sagan-y5e49d` — all fully merged into `main`. Deleting remote branches from the agent session is refused by the git proxy (`git push --delete` → "remote end hung up"); delete them in the GitHub UI (Branches page).

## Dead ends (tried or rejected, and why)
- Using Godot `FastNoiseLite` for the shared map: floats, not cross-platform deterministic → rejected ([ADR 0003](../decisions/0003-mapgen-determinism.md)); allowed for client cosmetics only.
- Host-only map generation as primary path: seeds would not be portable across OS ([ADR 0003](../decisions/0003-mapgen-determinism.md)).
- Reusing Widelands code: GPL v2+ incompatible with closed-source Steam release; ideas only.
- Godot high-level RPC multiplayer for lockstep: Node-bound, not usable from an engine-independent sim; raw packets over a transport abstraction instead ([ADR 0004](../decisions/0004-internet-transport.md)).
- GDScript for the simulation: no compile-time float ban, slower ([ADR 0001](../decisions/0001-language.md)).
- Time-based (millisecond) budgets for AI/pathfinding: machine-dependent results → replaced by work-unit/node-count budgets ([05-ai §3](../05-ai.md), [06-economy §6](../06-economy.md)).
- Replaying the full command log for reconnect: minutes of catch-up on long games → snapshot proposed ([02-networking §6](../02-networking.md)).
- Settlers-2-style duel-at-building combat: superseded by direct-control typed combat after the user asked for many warriors and walls ([ADR 0008](../decisions/0008-combat-model.md)).
- Single-tribe MVP: replaced by 4 cultures on user request ([ADR 0007](../decisions/0007-culture-system.md)).
- Research note: widelands.org returned HTTP 502 on 2026-10-02; GitHub repo used instead. The GDC 2024 PDF is image-heavy; its text was extracted manually.
