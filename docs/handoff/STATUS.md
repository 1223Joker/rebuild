# STATUS

**Last updated:** 2026-10-03 — M0 done; **M1 Map generation first pass done**; **M2 step 1 (map in the sim, start assignment, territory) done**; **M2 step 2 (building data, start castle entity, PlaceBuilding/CancelConstruction) done**; **M2 step 3 (goods, castle stock, construction, Demolish) done**; **M2 step 4 (carriers, A* pathfinding, movement) done**; **M2 step 5 (logistics: transport jobs for construction materials) done** (2026-10-03). M1: full pipeline, F1–F11 validation with retries, share codes, `mapgen` CLI with PNG preview, 24 golden map hashes, nightly 1 000-seed workflow; 96 tests.

## Current phase / step
**Implementation, milestone M2 Sim economy (headless)** ([09-roadmap](../09-roadmap.md)) — steps 1 (map in the sim + territory), 2 (building data + placement), 3 (goods + construction), 4 (carriers + A*) and 5 (logistics for construction) done; M0 Foundations and M1 Map generation complete. The user permitted coding on 2026-10-02 and chose "M0 directly, S1 folded in": S1's sim half is covered by M0; S1's Godot .NET export check moves to the start of M3. Process steps: [ORIGINAL-BRIEF.md §6](ORIGINAL-BRIEF.md).

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

## In progress
- Nothing.

## Next steps
1. Keep CI green (`gh run list -R 1223Joker/rebuild`).
2. M1 polish: tune `Dmin`, `Rf`, `Lmin` and the ASSUMPTION thresholds (F5/F6 minimums, fertile share, lair counts) when gameplay exists (spike S4 is done as part of M1; all spikes are allowed, user 2026-10-02).
3. Run spike **S5** (headless logistics + HPA* + combat scale, [09-roadmap §2](../09-roadmap.md)) alongside the start of M2, so the 20 ms/tick budget is checked before the economy design hardens.
4. Continue **M2 Sim economy (headless)** ([06-economy](../06-economy.md)): next step = **production buildings** (input/output piles, work cycles, woodcutter → sawmill → planks, quarry → stone) feeding logistics with output offers, input requests and storehouse overflow, then builders/diggers, player transport priorities, terrain costs + HPA*.
5. Before M3: run S1's Godot part — install the Godot **.NET** edition (the installed `/Applications/Godot.app` 4.7.2 is the standard build without C#) and export a test project for macOS and Windows — and spike S2 (rendering scale). Before M5: spike S3 (ENet transport).

## Blockers / waiting for user approval
- Deleting old remote branches (`claude/amazing-hopper-tq38xc`, `claude/amazing-hopper-s87jxe`, `claude/amazing-hopper-p2ghnv`, all fully merged into `main`) is not possible from the cloud session (git proxy HTTP 403 earlier; on 2026-10-03 the session's permission policy blocked the delete); delete them in the GitHub UI.

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
