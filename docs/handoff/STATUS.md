# STATUS

**Last updated:** 2026-10-02 — Implementation started (user permission); M0 Foundations core in place: sim library, analyzers, culture data generator, tools CLI, 71 tests, CI workflow.

## Current phase / step
**Implementation, milestone M0 Foundations** ([09-roadmap](../09-roadmap.md)). The user permitted coding on 2026-10-02 and chose "M0 directly, S1 folded in": S1's sim half is covered by M0; S1's Godot .NET export check moves to the start of M3. Process steps: [ORIGINAL-BRIEF.md §6](ORIGINAL-BRIEF.md).

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
| CI ([.github/workflows/ci.yml](../../.github/workflows/ci.yml)): 4 runners × Debug/Release, float-ban check, cross-OS hash comparison | pushed to https://github.com/1223Joker/rebuild on 2026-10-02; first run in progress |

## In progress
- First CI run on https://github.com/1223Joker/rebuild (M0 acceptance: "CI green on 4 runners" + cross-OS hash job).

## Next steps
1. Check that all 8 CI jobs and `cross-os-hashes` pass on https://github.com/1223Joker/rebuild (`gh run list -R 1223Joker/rebuild`); fix anything that differs per OS.
2. M0 leftovers: CI rule "golden changes need a `GameVersion` bump" (not yet enforced); nightly workflow with more property cases.
3. Start **M1 Map generation** ([03-mapgen](../03-mapgen.md)): expand `MapSpec` (bump its `FormatVersion`), integer noise, start placement, validation F1–F11, share code, `mapgen` CLI with PNG preview, 24 golden map hashes.
4. Before M3: run S1's Godot part — install the Godot **.NET** edition (the installed `/Applications/Godot.app` 4.7.2 is the standard build without C#) and export a test project for macOS and Windows.

## Blockers / waiting for user approval
- None.

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
