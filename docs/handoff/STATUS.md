# STATUS

**Last updated:** 2026-10-02 — All open questions answered: ADR 0007/0008 approved, soldier cap 800, cultures & war before Steam, spikes still not permitted.

## Current phase / step
Planning phase; ADRs 0001–0008 **all approved** (2026-10-02). First implementation goal: **LAN Alpha** (phase A, [09-roadmap](../09-roadmap.md)). **Still no code:** the user said "No spikes yet", so implementation and spikes wait for explicit permission. Process steps: [ORIGINAL-BRIEF.md §6](ORIGINAL-BRIEF.md).

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

## In progress
- Nothing. Waiting for the user.

## Next steps
1. Nothing is blocking planning. Remaining items in [open-questions.md](../open-questions.md) are deferred by the user: B1 (spikes — wait until the user signals readiness), F2 (final art — after M3), H7 (soldier-cap fallback — after S5), H8 (scope cuts — after LAN Alpha). Record any answer verbatim in [USER-ANSWERS.md](USER-ANSWERS.md), update [DECISIONS-LOG.md](DECISIONS-LOG.md), commit.
2. Only when spikes are permitted (B1): start with **S1** (Godot .NET export + determinism, [09-roadmap §2](../09-roadmap.md)) in a throwaway folder `spikes/s1-export/`; record results in a new `docs/spikes/S1.md`; commit.
3. Then S2–S5, each with its own result note; adjust ADRs/roadmap with findings.
4. After spikes and ADR approval: start milestone **M0 Foundations** ([09-roadmap §3](../09-roadmap.md)).

## Blockers / waiting for user approval
- Permission for spikes S1–S5 / any implementation — B1 (user: "No spikes yet", 2026-10-02).
- Nothing else; planning is complete and approved.

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
