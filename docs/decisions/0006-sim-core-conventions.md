# ADR 0006 — Simulation core conventions: tick rate, grid, RNG, state hash, collections

**Status:** approved by user (2026-10-02; proposed same day)

## Context
Lockstep needs a fixed tick, a portable RNG, a cheap state hash and strict ordering rules ([ORIGINAL-BRIEF §2](../handoff/ORIGINAL-BRIEF.md)). Desync causes found in shipped games: RNG call order across systems, argument evaluation order, uninitialized memory/padding, unordered containers ([GDC 2024 Pollard](https://media.gdcvault.com/gdc2024/Slides/GDC+slide+presentations/Pollard_Bradley_CrossPlatformDeterminism+2024-03-26+09.34.19.pdf)).

## Decision

| Topic | Options considered | Decision | Why |
|---|---|---|---|
| Sim tick rate | 20 Hz / 10 Hz / 8 Hz | **10 Hz** (100 ms per tick) at 1× speed | Settlers walk ~1 tile/s; 10 Hz is smooth with render interpolation and halves CPU vs 20 Hz. *Realms of Ruin* runs 16 Hz ([Pollard](https://media.gdcvault.com/gdc2024/Slides/GDC+slide+presentations/Pollard_Bradley_CrossPlatformDeterminism+2024-03-26+09.34.19.pdf)) |
| Lockstep turn | 1 tick / 2 ticks | **Turn = 2 ticks (200 ms)**; commands for turn N+2 by default | Matches AoE's 200 ms turns ([1500 Archers](https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond)) |
| Game speed | variable tick length / ticks per turn | Speed `k` = more ticks per wall-clock second (1×, 2×, 3×); turn stays 2 ticks | Sim stays identical; only pacing changes |
| Grid | hex / triangle (S2, Widelands) / square 8-neighbour | **Square tiles, 8-neighbour movement**, octile integer costs (10 straight / 14 diagonal) | Simplest heightmap mesh + pathfinding; S4's own grid is not required ([Red Blob — grids](https://www.redblobgames.com/pathfinding/grids/graphs.html)) |
| RNG algorithm | `System.Random` (impl may change between .NET versions) / Xorshift / PCG32 / SplitMix64 | **PCG32** (64-bit state, 32-bit output) ([O'Neill 2014](https://www.pcg-random.org/paper.html)); SplitMix64 for seed derivation | Small state, good statistics, trivial to implement in integers |
| RNG streams | one global / per system / per entity | **One stream per subsystem** (`MapGen`, `Combat`, `Monsters`, `AI-<slot>`, `Economy`), each seeded by `SplitMix64(matchSeed ^ streamId)` | Adding a call in one system cannot shift others ([Pollard](https://media.gdcvault.com/gdc2024/Slides/GDC+slide+presentations/Pollard_Bradley_CrossPlatformDeterminism+2024-03-26+09.34.19.pdf)) |
| Evaluation order | — | Never call RNG twice in one expression; RNG calls are separate statements (analyzer rule) | C# evaluates left-to-right, but this keeps code portable and reviewable |
| State hash | CRC32 / XxHash64 / FNV | **XxHash64** (`System.IO.Hashing`) ([MS docs](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash64)) over a canonical serialization; full hash every turn of cheap "summary" state, full-state hash every 50 turns (10 s) | Fast, available in BCL |
| Collections | — | Sim uses arrays, `List<T>`, `SortedDictionary`/sorted lists keyed by entity id; `Dictionary`/`HashSet` allowed for **lookup only**, enumeration banned by analyzer ([.NET Dictionary order unspecified](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2)) | Deterministic iteration |
| Entity ids | GUID / incrementing int | Incrementing `int` per match, never reused; iteration in id order | Stable order, cheap hashing |
| Threads | — | None in sim. Client may run sim on a dedicated thread, but sim itself is single-threaded | Brief constraint |

## Consequences
- + Clear, checkable rules; most are enforced by analyzers (see [01-architecture](../01-architecture.md)).
- − Single-threaded sim caps CPU; performance budget per tick defined in [01-architecture](../01-architecture.md) (≤ 20 ms at 8 players / 5 000 settlers on a reference machine).
