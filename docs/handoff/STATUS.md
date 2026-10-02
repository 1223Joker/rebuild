# STATUS

**Last updated:** 2026-10-02 — Planning deliverables complete; handoff test done; waiting for user approval of ADRs and open questions.

## Current phase / step
Planning phase — Process step 5 reached (summary delivered, **STOPPED, waiting for the user**). Process steps are defined in [ORIGINAL-BRIEF.md §6](ORIGINAL-BRIEF.md). No production code may be written until the user approves the ADRs; no spike without explicit permission.

## Done
- Step 0: git repo, [AGENTS.md](../../AGENTS.md), [CLAUDE.md](../../CLAUDE.md) (pointer), [ORIGINAL-BRIEF.md](ORIGINAL-BRIEF.md), this file.
- Step 1: research summarized in [RESEARCH-NOTES.md](RESEARCH-NOTES.md).
- Step 2: 10 clarifying questions asked and answered → [USER-ANSWERS.md](USER-ANSWERS.md).
- Step 3 deliverables:

| Deliverable | State |
|---|---|
| [decisions/](../decisions/README.md) ADR 0001–0006 | done (proposed) |
| [00-vision.md](../00-vision.md) | done |
| [01-architecture.md](../01-architecture.md) | done |
| [02-networking.md](../02-networking.md) | done |
| [03-mapgen.md](../03-mapgen.md) | done |
| [04-game-modes.md](../04-game-modes.md) | done |
| [05-ai.md](../05-ai.md) | done |
| [06-economy.md](../06-economy.md) | done |
| [07-art-style.md](../07-art-style.md) | done |
| [08-testing.md](../08-testing.md) | done |
| [09-roadmap.md](../09-roadmap.md) | done |
| [open-questions.md](../open-questions.md) | done (living) |
| [GLOSSARY.md](GLOSSARY.md) | done (living) |
| [DECISIONS-LOG.md](DECISIONS-LOG.md) | living |

- Step 4 handoff test (2026-10-02): all relative links resolve; no context-dependent references; gaps fixed: AGENTS.md now links open-questions, research notes and the code layout; this file now contains the post-approval procedure. Not done: Mermaid diagrams were reviewed by eye, not machine-validated (no renderer installed) — validate when tooling exists (e.g. `npx @mermaid-js/mermaid-cli`).

## In progress
- Nothing. Waiting for the user.

## Next steps (after the user replies)
1. Record every answer verbatim in [USER-ANSWERS.md](USER-ANSWERS.md); for each answered item in [open-questions.md](../open-questions.md): move it to "Resolved", update [DECISIONS-LOG.md](DECISIONS-LOG.md), set the ADR status (`approved` in the ADR file and in [decisions/README.md](../decisions/README.md)); revise documents if the user changed something; commit.
2. If spikes are permitted (open question B1): start with **S1** (Godot .NET export + determinism, [09-roadmap §2](../09-roadmap.md)) in a throwaway folder `spikes/s1-export/`; record results in a new `docs/spikes/S1.md`; commit.
3. Then S2–S5, each with its own result note; adjust ADRs/roadmap with findings.
4. After spikes and ADR approval: start milestone **M0 Foundations** ([09-roadmap §3](../09-roadmap.md)).

## Blockers / waiting for user approval
- ADRs 0001–0006 (`proposed`) — open questions A1–A6.
- Permission for spikes S1–S5 — B1.
- Scope decision given ≈ 21-month estimate — C1.
- Snapshot transfer for reconnect/desync recovery under the "commands only" rule — D1.
- All other items in [open-questions.md](../open-questions.md) have recommendations and are non-blocking for spikes.

## Dead ends (tried or rejected, and why)
- Using Godot `FastNoiseLite` for the shared map: floats, not cross-platform deterministic → rejected ([ADR 0003](../decisions/0003-mapgen-determinism.md)); allowed for client cosmetics only.
- Host-only map generation as primary path: seeds would not be portable across OS ([ADR 0003](../decisions/0003-mapgen-determinism.md)).
- Reusing Widelands code: GPL v2+ incompatible with closed-source Steam release; ideas only.
- Godot high-level RPC multiplayer for lockstep: Node-bound, not usable from an engine-independent sim; raw packets over a transport abstraction instead ([ADR 0004](../decisions/0004-internet-transport.md)).
- GDScript for the simulation: no compile-time float ban, slower ([ADR 0001](../decisions/0001-language.md)).
- Time-based (millisecond) budgets for AI/pathfinding: machine-dependent results → replaced by work-unit/node-count budgets ([05-ai §3](../05-ai.md), [06-economy §6](../06-economy.md)).
- Replaying the full command log for reconnect: minutes of catch-up on long games → snapshot proposed ([02-networking §6](../02-networking.md)).
- Research note: widelands.org returned HTTP 502 on 2026-10-02; GitHub repo used instead. The GDC 2024 PDF is image-heavy; its text was extracted manually.
