# AGENTS.md — Rebuild

Single source of truth for any agent working in this repository. Tool-specific files (e.g. `CLAUDE.md`) only point here.

## Project summary
"Rebuild" is a real-time city-building strategy game in the style of *The Settlers 4*: economy simulation with production chains and free-walking carriers, territory, 4 asymmetric cultures and RTS-style warfare with walls and siege. It is built with Godot 4.x (C#) for Windows, macOS and Linux. Multiplayer (LAN first, then Steam) uses deterministic lockstep, and every match is played on a seed-based random map. Modes: PvE, PvP, PvPvE with human, AI and neutral-monster slots in free teams. **Current phase: implementation, milestone M0 Foundations; first goal is the "LAN Alpha" ([docs/09-roadmap.md](docs/09-roadmap.md)).**

## Non-negotiable constraints (full text: [docs/handoff/ORIGINAL-BRIEF.md](docs/handoff/ORIGINAL-BRIEF.md))
- Godot 4.x current stable; targets Windows, macOS (ARM + x64), Linux.
- Deterministic lockstep: only player commands go over the network, never unit state.
- Simulation: fixed tick, **no floats** (fixed-point/integer only), own seeded RNG, no threads, no iteration over unordered hash containers.
- Simulation is engine-independent and runs headless; Godot does rendering/input only.
- Random maps: same seed + params + version = bit-identical map on all platforms; validated for fairness.
- Art: small fixed palette, clear silhouettes, swappable; prototype with Kenney (CC0) assets.
- Implementation permitted since 2026-10-02 ([USER-ANSWERS](docs/handoff/USER-ANSWERS.md)); keep the docs and ADRs in sync with the code.

## Conventions
- Language: English for all documents, code identifiers and file names.
- Docs: concise, tables for option comparisons, `ASSUMPTION:` marks assumptions, every technical recommendation cites a source link.
- ADRs: `docs/decisions/NNNN-kebab-title.md` (context, options, decision, consequences, status).
- Every user answer/instruction → `docs/handoff/USER-ANSWERS.md` immediately (date + near-verbatim wording).
- After each completed step: update `docs/handoff/STATUS.md` (+ `DECISIONS-LOG.md` if a decision changed), then commit.
- Never refer to "the chat"; write for a reader with zero context.
- On "HANDOFF": update all handoff files, commit, print a ≤15-line handoff message.
- Code: build/test with `dotnet test` ([README.md](README.md)); sim code must pass the determinism analyzers (never suppress RB0001–RB0004); golden hashes in `tests/golden/` change only together with a `GameVersion` bump.

## Folder structure
```
AGENTS.md                 entry point (this file)
CLAUDE.md                 pointer to AGENTS.md
docs/
  00-vision.md … 11-military.md  planning deliverables (10 cultures, 11 military)
  open-questions.md        decisions the user still has to make
  decisions/               ADRs
  handoff/                 ORIGINAL-BRIEF, STATUS, USER-ANSWERS, DECISIONS-LOG, GLOSSARY, RESEARCH-NOTES
```
Code: `src/` (Rebuild.Sim, Rebuild.Analyzers, Rebuild.Tools), `data/`, `tests/`, `tools/ci/`, `.github/workflows/` — see [README.md](README.md); target layout incl. `client/`: [docs/01-architecture.md §9](docs/01-architecture.md).

## Start here (reading order)
1. [docs/handoff/STATUS.md](docs/handoff/STATUS.md) — where the project is and what to do next.
2. [docs/handoff/USER-ANSWERS.md](docs/handoff/USER-ANSWERS.md) — user decisions that override defaults.
3. [docs/handoff/ORIGINAL-BRIEF.md](docs/handoff/ORIGINAL-BRIEF.md) — the full brief and process.
4. [docs/handoff/DECISIONS-LOG.md](docs/handoff/DECISIONS-LOG.md) and `docs/decisions/` — what is decided/proposed.
5. [docs/open-questions.md](docs/open-questions.md) — everything still waiting for the user.
6. The planning documents `docs/00-vision.md` … `docs/11-military.md` (linked from STATUS.md); [docs/handoff/GLOSSARY.md](docs/handoff/GLOSSARY.md) for terms; [docs/handoff/RESEARCH-NOTES.md](docs/handoff/RESEARCH-NOTES.md) for sources.
