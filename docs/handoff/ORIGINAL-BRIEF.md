# Original Brief (verbatim)

> Copied verbatim from the user's first message on 2026-10-02. Do not edit. Later changes and clarifications live in `USER-ANSWERS.md`.

---

Planning Brief: Settlers-4-style RTS (Godot 4) name: Rebuild
1. Role & Task
You are the lead engineer for a real-time city-building strategy game in the style of "The Settlers 4" (economy simulation with production chains, territory, military).
We are in the PLANNING PHASE.

* Do NOT write production code.
* Allowed: documents and Mermaid diagrams.
* Small throwaway spikes are allowed only after asking me first.

2. Fixed Constraints (non-negotiable)

* Engine: Godot 4.x (current stable)
* Platforms: Windows, macOS (Apple Silicon + Intel), Linux
* Game modes: PvE, PvP, PvPvE. Slot types: human, AI, neutral monsters. Teams freely configurable.
* Multiplayer: over LAN and internet
* Network model: deterministic lockstep. Only commands are transmitted, never unit state.
* Simulation:
   * deterministic, fixed tick
   * NO floats (fixed-point/integer only)
   * own seeded RNG
   * no threads inside the simulation
   * no iteration over unordered hash containers
* Separation: simulation (engine-independent, runs headless) ↔ rendering/input (Godot), strictly separated
* Maps: random map generation (see section 3)
* Art: timeless and simple (small fixed palette, clear silhouettes), swappable. Prototype with Kenney assets (CC0).

3. Random Map Generation – Requirements

* Seed-based: same seed + same parameters + same game version = bit-identical map on all platforms.
* Determinism: Godot's Noise/FastNoiseLite uses floats and must not be used unverified for the shared map. Evaluate at least these two options in an ADR and recommend one:
   * (a) custom integer-based noise implementation
   * (b) host generates the map and transmits map data + hash to clients
* Parameters: map size, player count, team layout, terrain mix (water, mountains, forest, plains), resource density, symmetric/asymmetric, monster density (PvPvE)
* Fairness:
   * start positions with minimum distance between them
   * equivalent resources within a radius of each start (buildable land, wood, stone, ore, food)
   * all starts reachable by land (connectivity check)
* Validation: check each generated map automatically. On failure, regenerate deterministically with a derived seed.
* PvPvE: monster lairs in neutral zones with minimum distance to starts
* Lobby: map preview, seed copyable and shareable, map can be saved
* Tests: golden-hash tests (seed → expected map hash) on all three operating systems
* Performance: define a generation-time target for the largest map size

4. Deliverables (all under `docs/`)

1. `00-vision.md` – game concept, core loop, MVP scope, explicit non-goals
2. `01-architecture.md` – module structure, tick loop, command model, fixed-point strategy, RNG, data flow simulation → rendering (Mermaid), repo folder structure
3. `02-networking.md` – lockstep flow (host star topology), input delay, desync detection via state hash, pause/disconnect/reconnect, transport abstraction (ENet for LAN/direct; Steam relay or custom relay for internet/CGNAT), LAN discovery, lobby
4. `03-mapgen.md` – generation pipeline step by step, parameters, fairness metrics, validation, performance
5. `04-game-modes.md` – slot/team model, PvE/PvP/PvPvE, victory conditions
6. `05-ai.md` – AI runs only on the host and sends commands like a player; difficulty levels; monster behavior as part of the deterministic simulation
7. `06-economy.md` – MVP buildings, production chains, settler/carrier logic, pathfinding (algorithm + scaling to thousands of units)
8. `07-art-style.md` – perspective (isometric 2D vs. low-poly 3D), palette, resolution, asset pipeline
9. `08-testing.md` – determinism tests (same command log → same hash), cross-platform CI (Windows, macOS ARM, Linux), replay tests
10. `09-roadmap.md` – milestones with tasks, each with measurable acceptance criteria and a rough effort estimate
11. `decisions/` – one ADR per significant decision (context, options, decision, consequences). Required ADRs:
   * Language: GDScript vs. C# (including fixed-point libraries and export to all 3 platforms)
   * Fixed-point format
   * Map generation determinism
   * Internet transport: Steam vs. custom relay vs. port forwarding
   * Rendering: 2D vs. 3D
12. `open-questions.md` – every point I need to decide

5. Knowledge Persistence & Handoff
Goal: a new agent with zero chat history must be able to continue this project at any time, using only the repository. The chat is not memory. Anything not written to the repo is lost.
Files

1. `AGENTS.md` (repo root) – short, stable entry point (max. ~1 page):
   * project summary in 3–5 sentences
   * non-negotiable constraints (short list, link to the full brief)
   * conventions (language, naming, folder structure)
   * "Start here" section: reading order for a new agent
If your tool uses its own instruction file (e.g. `CLAUDE.md`, `.cursor/rules`), create it as a one-line pointer to `AGENTS.md`. `AGENTS.md` is the single source of truth.
2. `docs/handoff/ORIGINAL-BRIEF.md` – this prompt, copied verbatim
3. `docs/handoff/STATUS.md` – living project state:
   * current phase and step
   * done (with links to documents)
   * in progress
   * next 3–5 concrete steps
   * blockers and items waiting for my approval
   * dead ends: approaches tried or rejected, and why
   * last updated (date + one-line summary)
4. `docs/handoff/USER-ANSWERS.md` – every answer, instruction or preference I give in chat, with date, recorded as close to my wording as possible
5. `docs/handoff/DECISIONS-LOG.md` – chronological one-liners: `date | decision | status (proposed / approved by me) | link to ADR`
6. `docs/handoff/GLOSSARY.md` – game and technical terms with one-line definitions (e.g. tick, command, carrier, territory, desync)

Rules

* Record each of my answers in `USER-ANSWERS.md` immediately.
* Update `STATUS.md` after every completed step, and always before you stop to wait for me.
* Never keep relevant information only in chat. If it matters, it goes into a file.
* Write for a reader with no context. No references like "as discussed above" or "see chat".
* Commit after each completed step with a meaningful commit message.
* When I write "HANDOFF":
   1. bring all handoff files up to date
   2. commit
   3. output a handoff message (max. 15 lines) that I can paste into a new agent

6. Process

0. Setup: initialize a git repo if none exists. Create `docs/handoff/ORIGINAL-BRIEF.md`, `AGENTS.md` and `docs/handoff/STATUS.md`. Commit.
1. Research: read the references in section 8.
2. Questions: BEFORE writing any planning document, ask me up to 10 clarifying questions in one batch about unclear requirements. Wait for my answers and record them in `USER-ANSWERS.md`.
3. Write: create the deliverables from section 4.
   * Mark assumptions with `ASSUMPTION:`.
   * Cite a source (link) for every technical recommendation.
   * After each document: update `STATUS.md` and `DECISIONS-LOG.md`, then commit.
4. Handoff test: check whether `AGENTS.md` → `STATUS.md` → linked docs is enough to continue without me. List any gaps and fix them.
5. Finish: give a short summary and the list of ADRs that need my approval. Then STOP. No implementation before approval.

7. Style

* All documents, code identifiers and file names in English
* Concise and technically precise
* Use tables for option comparisons

8. References

* https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond
* https://gafferongames.com/post/deterministic_lockstep/
* https://gafferongames.com/post/floating_point_determinism
* https://media.gdcvault.com/gdc2024/Slides/GDC+slide+presentations/Pollard_Bradley_CrossPlatformDeterminism+2024-03-26+09.34.19.pdf
* https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html
* https://ziva.sh/blogs/godot-multiplayer
* https://www.widelands.org (open-source reference game)
* https://kenney.nl (CC0 assets)
