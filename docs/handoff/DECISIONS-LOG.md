# Decisions Log

Chronological one-liners. Format: `date | decision | status | link`. Status: `proposed` (agent) or `approved by user` (record the approval in [USER-ANSWERS.md](USER-ANSWERS.md) too).

| Date | Decision | Status | Link |
|---|---|---|---|
| 2026-10-02 | Release on Steam, commercial, closed source (no GPL code reuse) | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q2 |
| 2026-10-02 | Low-poly 3D perspective | approved by user (preference); ADR proposed | [ADR 0005](../decisions/0005-rendering-2d-vs-3d.md) |
| 2026-10-02 | S4-style free-walking carriers, no roads | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q6 |
| 2026-10-02 | MVP = full loop incl. military, AI, monsters | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q7 |
| 2026-10-02 | Scale: ≤ 8 players, ≤ 512×512 tiles, thousands of settlers | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q8 |
| 2026-10-02 | Monsters = escalating waves | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q9 |
| 2026-10-02 | MP extras: save/load MP, AI takeover on disconnect, game speed | approved by user | [USER-ANSWERS](USER-ANSWERS.md) Q10 |
| 2026-10-02 | C# (.NET) for sim + client; sim is a Godot-free library | proposed | [ADR 0001](../decisions/0001-language.md) |
| 2026-10-02 | Domain integers + `Fix` Q48.16 in `long` | proposed | [ADR 0002](../decisions/0002-fixed-point-format.md) |
| 2026-10-02 | Map gen: integer noise in sim, hash compare in lobby, host transfer as fallback | proposed | [ADR 0003](../decisions/0003-mapgen-determinism.md) |
| 2026-10-02 | Transport: Steam SDR primary, ENet LAN/direct, optional custom relay | proposed | [ADR 0004](../decisions/0004-internet-transport.md) |
| 2026-10-02 | Low-poly 3D, MultiMesh, palette texture | proposed | [ADR 0005](../decisions/0005-rendering-2d-vs-3d.md) |
| 2026-10-02 | 10 Hz tick, 200 ms turn, square 8-neighbour grid, PCG32 per-subsystem streams, XxHash64 | proposed | [ADR 0006](../decisions/0006-sim-core-conventions.md) |
| 2026-10-02 | Sim state as SoA arrays; data definitions JSON → source-generated C# tables; sim on main thread in MVP | proposed | [01-architecture](../01-architecture.md) |
| 2026-10-02 | Host-sealed turns (host assigns target turn), own redundancy on unreliable channel, adaptive input delay 1–5 turns | proposed | [02-networking](../02-networking.md) |
| 2026-10-02 | Reconnect & desync recovery via one-time snapshot transfer; host loss ends match with local auto-save (no host migration in MVP) | proposed (needs user confirmation) | [02-networking](../02-networking.md) §6 |
| 2026-10-02 | Map gen "starts first", 4 symmetry modes, fairness metrics F1–F11, ≤ 16 deterministic retries, XL ≤ 1.5 s/attempt | proposed | [03-mapgen](../03-mapgen.md) |
| 2026-10-02 | 8 slots (Open/Closed/Human/AI/Monsters), mode derived from slot table, victory: Conquest / Survival / Lair hunt, monster wave formula | proposed | [04-game-modes](../04-game-modes.md) |
