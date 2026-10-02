# Decisions Log

Chronological one-liners. Format: `date | decision | status | link`. Status: `proposed` (agent) or `approved by user` (record the approval in [USER-ANSWERS.md](USER-ANSWERS.md) too).

| Date | Decision | Status | Link |
|---|---|---|---|
| 2026-10-02 | Release on Steam, commercial, closed source (no GPL code reuse) | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q2 |
| 2026-10-02 | Low-poly 3D perspective | approved by user | [ADR 0005](../decisions/0005-rendering-2d-vs-3d.md) |
| 2026-10-02 | S4-style free-walking carriers, no roads | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q6 |
| 2026-10-02 | MVP = full loop incl. military, AI, monsters | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q7 |
| 2026-10-02 | Scale: ≤ 8 players, ≤ 512×512 tiles, thousands of settlers | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q8 |
| 2026-10-02 | Monsters = escalating waves | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q9 |
| 2026-10-02 | MP extras: save/load MP, AI takeover on disconnect, game speed | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q10 |
| 2026-10-02 | C# (.NET) for sim + client; sim is a Godot-free library | approved by user | [ADR 0001](../decisions/0001-language.md) |
| 2026-10-02 | Domain integers + `Fix` Q48.16 in `long` | approved by user | [ADR 0002](../decisions/0002-fixed-point-format.md) |
| 2026-10-02 | Map gen: integer noise in sim, hash compare in lobby, host transfer as fallback | approved by user | [ADR 0003](../decisions/0003-mapgen-determinism.md) |
| 2026-10-02 | Transport: Steam SDR primary, ENet LAN/direct, optional custom relay | approved by user | [ADR 0004](../decisions/0004-internet-transport.md) |
| 2026-10-02 | Low-poly 3D, MultiMesh, palette texture | approved by user | [ADR 0005](../decisions/0005-rendering-2d-vs-3d.md) |
| 2026-10-02 | 10 Hz tick, 200 ms turn, square 8-neighbour grid, PCG32 per-subsystem streams, XxHash64 | approved by user | [ADR 0006](../decisions/0006-sim-core-conventions.md) |
| 2026-10-02 | Sim state as SoA arrays; data definitions JSON → source-generated C# tables; sim on main thread in MVP | proposed | [01-architecture](../01-architecture.md) |
| 2026-10-02 | Host-sealed turns (host assigns target turn), own redundancy on unreliable channel, adaptive input delay 1–5 turns | proposed | [02-networking](../02-networking.md) |
| 2026-10-02 | Reconnect & desync recovery via one-time snapshot transfer; host loss ends match with local auto-save (no host migration in MVP) | approved by user | [02-networking](../02-networking.md) §6 |
| 2026-10-02 | Map gen "starts first", 4 symmetry modes, fairness metrics F1–F11, ≤ 16 deterministic retries, XL ≤ 1.5 s/attempt | proposed | [03-mapgen](../03-mapgen.md) |
| 2026-10-02 | 8 slots (Open/Closed/Human/AI/Monsters), mode derived from slot table, victory: Conquest / Survival / Lair hunt, monster wave formula | proposed | [04-game-modes](../04-game-modes.md) |
| 2026-10-02 | AI: host-only utility AI emitting commands, deterministic work-unit budget, stateless takeover; monsters = sim state machine | proposed | [05-ai](../05-ai.md) |
| 2026-10-02 | 24 MVP buildings; request/offer logistics with sector search; duel combat; A* + HPA* + flow fields with node-count budgets | proposed | [06-economy](../06-economy.md) |
| 2026-10-02 | Art: fixed-pitch perspective camera with 90° rotation, flat shading, 16-cell palette texture, glTF pipeline, `data/visuals.json` asset mapping | proposed | [07-art-style](../07-art-style.md) |
| 2026-10-02 | Testing: analyzers, FsCheck, golden map hashes + golden replays, save/load equivalence, 4-runner CI incl. macos-15-intel, cross-OS hash comparison job | proposed | [08-testing](../08-testing.md) |
| 2026-10-02 | Roadmap: 5 spikes + M0–M10, ≈ 71.5 weeks at 12 h/week (≈ 21 months with contingency) | proposed | [09-roadmap](../09-roadmap.md) |
| 2026-10-02 | Spikes S1–S5 not permitted yet ("No spikes yet") | approved by user | [09-roadmap](../09-roadmap.md) |
| 2026-10-02 | Keep full MVP scope; playable LAN PvP checkpoint after M5 (≈ 11 months) | approved by user | [09-roadmap](../09-roadmap.md) |
| 2026-10-02 | Host loss ends match with auto-saves; no host migration in MVP | approved by user | [02-networking](../02-networking.md) §6 |
| 2026-10-02 | Allies share vision; visual fog of war in MVP via deterministic `VisibilitySystem` (added to M4, +1.5 w) | approved by user (fog); implementation proposed | [04-game-modes](../04-game-modes.md) §1 |
| 2026-10-02 | Reference machine = developer's Apple Silicon Mac (only owned test machine); Windows/Linux via CI + extra hardware later | approved by user (machines); approach proposed | [01-architecture](../01-architecture.md) §3 |
| 2026-10-02 | Remaining recommendations accepted (C4–C8, D3–D5, E2–E4, F1, F3, G1–G2) | approved by user | [open-questions](../open-questions.md) |
| 2026-10-02 | First LAN build: 1 culture, 2–3 warrior types, no walls; then expand | approved by user | [USER-ANSWERS](USER-ANSWERS.md) |
| 2026-10-02 | 4 cultures, S4-style depth; direct unit control; walls + gates, wall towers, siege, palisades | approved by user | [USER-ANSWERS](USER-ANSWERS.md) |
| 2026-10-02 | Linux: CI determinism tests from day one, Linux release with Steam release or later | approved by user | [USER-ANSWERS](USER-ANSWERS.md) |
| 2026-10-02 | Data-driven culture system (shared core + per-culture data + fixed hook catalogue) | proposed | [ADR 0007](../decisions/0007-culture-system.md) |
| 2026-10-02 | Combat: attack-type × armour-class table, delayed-hit projectiles, tile occupancy, gate-filtered HPA*, 400 soldiers/player cap | proposed | [ADR 0008](../decisions/0008-combat-model.md) |
| 2026-10-02 | Unit roster: 5 base + 7 culture units; damage table; palisade/stone wall/gate/wall tower rules; first LAN build = Swordsman, Spearman, Archer | proposed | [11-military](../11-military.md) |
| 2026-10-02 | 4 cultures proposed: Rivermen (baseline, first LAN build), Highlanders, Woodfolk, Riders | proposed | [10-cultures](../10-cultures.md) |
| 2026-10-02 | Roadmap restructured into phases A (LAN Alpha) → B (Steam) → C (Cultures & war) → D (AI & monsters) → E (Release); ≈ 114 weeks (≈ 33 months with contingency) | proposed | [09-roadmap](../09-roadmap.md) |
| 2026-10-02 | Reference machine = MacBook M5 24 GB; second test machine = Windows gaming PC; Linux CI-only until phase E | approved by user (machines); approach proposed | [01-architecture](../01-architecture.md) §3 |
| 2026-10-02 | ADR 0007 culture system | approved by user | [ADR 0007](../decisions/0007-culture-system.md) |
| 2026-10-02 | ADR 0008 combat model, soldier cap 800/player (combat budget ≤ 10 ms with 6 400 soldiers) | approved by user | [ADR 0008](../decisions/0008-combat-model.md) |
| 2026-10-02 | 4 cultures and unit roster approved as working concepts / balancing starting values | approved by user | [10-cultures](../10-cultures.md), [11-military](../11-military.md) |
| 2026-10-02 | Phase order: A LAN Alpha → B Cultures & war → C Steam → D AI & monsters → E Release; milestones renumbered | approved by user | [09-roadmap](../09-roadmap.md) |
| 2026-10-02 | Spikes still not permitted ("Not yet"); final art decided after M3 | approved by user | [open-questions](../open-questions.md) |
| 2026-10-02 | Implementation permitted; start with M0 Foundations directly, S1's sim half (Fix, Pcg32, analyzers, cross-OS hash) folded into M0; S1's Godot-export check moves to the start of M3 | approved by user | [09-roadmap](../09-roadmap.md) |
| 2026-10-02 | Toolchain: .NET SDK 10 (pinned in `global.json`) building `net8.0` targets, so the libraries stay loadable by Godot .NET | proposed | [ADR 0001](../decisions/0001-language.md) |
| 2026-10-02 | Extra RNG stream `Setup` (resolves random cultures at match start); `Pcg32` is a sealed class so its state cannot be copied by accident | proposed | `src/Rebuild.Sim/Core/Pcg32.cs` |
| 2026-10-02 | `Fix` division by zero throws (identically on all peers); results outside `long` wrap; gameplay commands are no-ops until their systems exist | proposed | `src/Rebuild.Sim/Core/Fix.cs` |
| 2026-10-02 | Culture data generator uses an own minimal JSON reader (integers only — fractional numbers in data are a build error) and an FNV-1a data hash over LF-normalized files; `.gitattributes` forces LF | proposed | `src/Rebuild.Analyzers/CultureDataGenerator.cs` |
| 2026-10-02 | Map generator as built: land corridors between ring-adjacent starts (new step 2b); start-zone template also in Mirror/Rotational modes (teammates equal, not only images); template elevation clamped to the land band; small start jitter (±1/32 sector) | approved by user | [03-mapgen](../03-mapgen.md) §3, §9 |
| 2026-10-02 | F8 measured per team (front line) instead of per start, because inner starts of a team sector are farther from enemies by design; FFA unchanged | approved by user | [03-mapgen](../03-mapgen.md) §4 |
| 2026-10-02 | Small (192²) maps hold at most 6 starts (approved by user); F5/F6 minimum 100 units, fertile share 6 %, lairs = max(starts, 1/2/3 per 128²) — all ASSUMPTION, tune with gameplay | proposed | [03-mapgen](../03-mapgen.md) §2, §4 |
| 2026-10-02 | Golden map hashes live as `map` lines in `tests/golden/hashes.txt` (cases in `mapgen.json`) so the existing cross-OS comparison covers them; CI job `golden-version` enforces version bumps for changed golden values; `GameVersion` 0.2.0 (MapSpec format v2) | proposed | [08-testing](../08-testing.md) §3, `tools/ci/check-golden-version.sh` |
