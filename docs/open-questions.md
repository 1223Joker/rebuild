# Open Questions — decisions the user has to make

Each item names a recommendation. When the user answers: record it verbatim in [handoff/USER-ANSWERS.md](handoff/USER-ANSWERS.md), update [handoff/DECISIONS-LOG.md](handoff/DECISIONS-LOG.md), set the ADR status, and move the item to "Resolved" below.

## A. ADR approvals (blocking all implementation)
| ID | Question | Recommendation | Ref |
|---|---|---|---|
| A1 | Approve C# (.NET) for sim + client, sim as Godot-free library? | Approve | [ADR 0001](decisions/0001-language.md) |
| A2 | Approve domain integers + `Fix` Q48.16? | Approve | [ADR 0002](decisions/0002-fixed-point-format.md) |
| A3 | Approve hybrid map determinism (integer noise primary, host transfer fallback)? | Approve | [ADR 0003](decisions/0003-mapgen-determinism.md) |
| A4 | Approve Steam SDR primary + ENet LAN/direct + post-MVP custom relay? | Approve | [ADR 0004](decisions/0004-internet-transport.md) |
| A5 | Approve low-poly 3D details (camera, palette texture, MultiMesh)? | Approve | [ADR 0005](decisions/0005-rendering-2d-vs-3d.md) |
| A6 | Approve 10 Hz tick, 200 ms turn, square 8-neighbour grid, PCG32 streams, XxHash64? | Approve | [ADR 0006](decisions/0006-sim-core-conventions.md) |

## B. Permissions
| ID | Question | Recommendation | Ref |
|---|---|---|---|
| B1 | May spikes S1–S5 be built (throwaway code, deleted or archived afterwards)? | Yes, S1 first (export + determinism risk) | [09-roadmap §2](09-roadmap.md) |

## C. Scope & product
| ID | Question | Recommendation | Ref |
|---|---|---|---|
| C1 | The MVP is estimated at ≈ 21 months part-time incl. contingency. Keep full scope, or cut? Levers: (a) ship PvP/LAN+Steam first and AI/monsters as update, (b) drop the gold chain (3 buildings), (c) cap at 4 players for MVP | Keep scope but order milestones so a playable LAN PvP build exists after M5 (~10 months) | [09-roadmap](09-roadmap.md) |
| C2 | Allies: shared vision, carriers may cross allied territory, separate economies — OK? | Yes | [04-game-modes §1](04-game-modes.md) |
| C3 | Fog of war in MVP? (lockstep cannot hide data from a hacked client anyway) | No fog in MVP; post-MVP visual-only fog | [05-ai](05-ai.md), [02-networking §12](02-networking.md) |
| C4 | Victory conditions Conquest / Survival / Lair hunt sufficient? | Yes | [04-game-modes §4](04-game-modes.md) |
| C5 | Monster wave numbers (grace 15 min, interval 6→3 min, budget formula) as starting values? | Yes, tune in playtests | [04-game-modes §5](04-game-modes.md) |
| C6 | Pause limits: 3 pauses × 120 s per player, host unlimited? | Yes | [02-networking §5](02-networking.md) |
| C7 | Session length target 45–90 min, no meta-progression? | Yes | [00-vision](00-vision.md) |
| C8 | One tribe, one soldier type, 24 buildings for MVP? | Yes | [06-economy §1](06-economy.md) |

## D. Networking
| ID | Question | Recommendation | Ref |
|---|---|---|---|
| D1 | Is a **one-time snapshot transfer** for reconnect and desync recovery acceptable under "only commands are transmitted"? (Alternative: replay the full command log → minutes of catch-up on long games.) | Yes — it is a file transfer, not continuous state sync | [02-networking §6](02-networking.md) |
| D2 | Host leaves → match ends with local auto-saves (no host migration in MVP)? | Yes; host migration post-MVP | [02-networking §6](02-networking.md) |
| D3 | Steam binding: Steamworks.NET vs Facepunch.Steamworks vs GodotSteam — decide after spike S3? | Steamworks.NET unless S3 shows problems | [ADR 0004](decisions/0004-internet-transport.md) |
| D4 | LAN discovery port 47800 (UDP)? | Yes | [02-networking §10](02-networking.md) |
| D5 | Will the game have a Steam AppID (paid Steam Direct fee) before M8? Development can use AppID 480. | Acquire before M8 | [09-roadmap](09-roadmap.md) |

## E. Technical parameters
| ID | Question | Recommendation | Ref |
|---|---|---|---|
| E1 | Reference machine for performance targets: Apple M1 / Ryzen 5 3600 + GTX 1060 class? Which machines do you own for testing (Win/mac/Linux)? | Yes; please list your machines | [01-architecture §3](01-architecture.md) |
| E2 | Map size presets S 192² / M 256² / L 384² / XL 512²? | Yes | [03-mapgen §2](03-mapgen.md) |
| E3 | Fairness radius 24 tiles and ratio limits 1.10–1.25 as starting values? | Yes, tune in S4 | [03-mapgen §4](03-mapgen.md) |
| E4 | Building footprints S 2×2 / M 3×3 / L 4×4 tiles? | Yes | [07-art-style §5](07-art-style.md) |

## F. Art & audio
| ID | Question | Recommendation | Ref |
|---|---|---|---|
| F1 | Palette and team colors as proposed? | Yes, revisit with first screenshots | [07-art-style §3](07-art-style.md) |
| F2 | Final art: commission an artist, buy asset packs, or own Blender work? (Affects M10 and budget.) | Decide after M3 | [07-art-style](07-art-style.md) |
| F3 | Audio (music/SFX) is not planned in any doc beyond placeholders — source? | CC0 placeholders for MVP, decide final later | [09-roadmap M10](09-roadmap.md) |

## G. Legal / business
| ID | Question | Recommendation | Ref |
|---|---|---|---|
| G1 | Name "Rebuild" — check trademark/Steam store availability? | Check before Steam page | — |
| G2 | Which languages besides English for the UI? | English only for MVP, strings externalized from day one | — |

## Resolved
| ID | Answer | Date |
|---|---|---|
| — | Q1–Q10 of the initial clarification round | see [USER-ANSWERS](handoff/USER-ANSWERS.md), 2026-10-02 |
