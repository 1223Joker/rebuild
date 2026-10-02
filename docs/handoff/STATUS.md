# STATUS

**Last updated:** 2026-10-02 — 00-vision.md written.

## Current phase / step
Planning phase — Process step 3 (Write deliverables). Process steps are defined in [ORIGINAL-BRIEF.md §6](ORIGINAL-BRIEF.md). No production code may be written until the user approves the ADRs.

## Done
- Step 0: git repo, [AGENTS.md](../../AGENTS.md), [CLAUDE.md](../../CLAUDE.md) (pointer), [ORIGINAL-BRIEF.md](ORIGINAL-BRIEF.md), this file.
- Step 1: research summarized in [RESEARCH-NOTES.md](RESEARCH-NOTES.md).
- Step 2: 10 clarifying questions asked and answered → [USER-ANSWERS.md](USER-ANSWERS.md).
- Step 3 deliverables (checklist):

| Deliverable | State |
|---|---|
| [decisions/](../decisions/README.md) ADR 0001–0006 | done (proposed) |
| [00-vision.md](../00-vision.md) | done |
| [01-architecture.md](../01-architecture.md) | todo |
| [02-networking.md](../02-networking.md) | todo |
| [03-mapgen.md](../03-mapgen.md) | todo |
| [04-game-modes.md](../04-game-modes.md) | todo |
| [05-ai.md](../05-ai.md) | todo |
| [06-economy.md](../06-economy.md) | todo |
| [07-art-style.md](../07-art-style.md) | todo |
| [08-testing.md](../08-testing.md) | todo |
| [09-roadmap.md](../09-roadmap.md) | todo |
| [open-questions.md](../open-questions.md) | todo |
| [GLOSSARY.md](GLOSSARY.md) | todo |
| [DECISIONS-LOG.md](DECISIONS-LOG.md) | living |

## In progress
- Step 3: next deliverable in the table above marked `todo`.

## Next steps
1. Write remaining deliverables in table order; after each: update this table + DECISIONS-LOG, commit.
2. Step 4: handoff test (AGENTS → STATUS → links, zero-context read); fix gaps; commit.
3. Step 5: summary + list of ADRs needing approval; then STOP.

## Blockers / waiting for user approval
- ADRs 0001–0006 are `proposed` and need user approval before any implementation or spike.
- Spikes (throwaway prototypes) need explicit user permission first.

## Dead ends (tried or rejected, and why)
- Using Godot `FastNoiseLite` for the shared map: floats, not cross-platform deterministic → rejected ([ADR 0003](../decisions/0003-mapgen-determinism.md)); allowed for client cosmetics only.
- Host-only map generation as primary path: seeds would not be portable across OS ([ADR 0003](../decisions/0003-mapgen-determinism.md)).
- Reusing Widelands code: GPL v2+ incompatible with closed-source Steam release; ideas only.
- Godot high-level RPC multiplayer for lockstep: Node-bound, not usable from an engine-independent sim; raw packets over a transport abstraction instead ([ADR 0004](../decisions/0004-internet-transport.md)).
- GDScript for the simulation: no compile-time float ban, slower ([ADR 0001](../decisions/0001-language.md)).
- Research note: widelands.org returned HTTP 502 on 2026-10-02; GitHub repo used instead.
