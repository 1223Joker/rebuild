# Open Questions — decisions the user has to make

Each item names a recommendation. When the user answers: record it verbatim in [handoff/USER-ANSWERS.md](handoff/USER-ANSWERS.md), update [handoff/DECISIONS-LOG.md](handoff/DECISIONS-LOG.md), set any ADR status, and move the item to "Resolved" below.

## Open
| ID | Question | Recommendation | Needed before | Ref |
|---|---|---|---|---|
| B1 | May spikes S1–S5 be built? User said "No spikes yet" on 2026-10-02 — **ask again** when planning is considered finished. | Yes, S1 first (export + determinism risk) | any implementation | [09-roadmap §2](09-roadmap.md) |
| E5 | Exact model of the Apple Silicon Mac (chip, RAM) — it is the reference machine for all performance targets. | Record model, e.g. "MacBook Air M2, 16 GB" | S2/S5 | [01-architecture §3](01-architecture.md) |
| E6 | How to test Windows/Linux by hand (only a Mac is owned)? Options: (a) cheap used x64 mini PC dual-booting Windows + Linux, (b) cloud VMs with GPU for short sessions, (c) playtesters/friends, (d) Windows 11 ARM VM on the Mac (runs x64 via emulation — not a real x64 test). CI covers determinism on all OS regardless. | (a) — a ~200–300 € used mini PC before M5 | M5 | [08-testing §2](08-testing.md) |
| F2 | Final art: commission an artist, buy asset packs, or own Blender work? | Decide after M3 | M10 | [07-art-style](07-art-style.md) |

## Action items from accepted recommendations (not questions)
| ID | Action | When |
|---|---|---|
| D5 | Acquire a Steam AppID (Steam Direct fee); use AppID 480 until then | before M8 |
| G1 | Check the name "Rebuild" for trademark/Steam store conflicts | before creating the Steam page |
| D3 | Pick Steam binding (default Steamworks.NET) after spike S3 | S3 |

## Resolved
| ID | Question | Answer (2026-10-02, see [USER-ANSWERS](handoff/USER-ANSWERS.md)) |
|---|---|---|
| Q1–Q10 | Initial clarification round | see USER-ANSWERS |
| A1–A6 | Approve ADRs 0001–0006 | all approved |
| C1 | Scope vs ~21-month estimate | keep scope, playable LAN PvP after M5 |
| C2 | Allies: shared vision, carriers cross allied land, separate economies | yes |
| C3 | Fog of war in MVP | **visual fog in MVP** (changed from recommendation "no fog") |
| C4 | Victory: Conquest / Survival / Lair hunt | accepted |
| C5 | Monster wave starting numbers | accepted, tune in playtests |
| C6 | Pause limits 3 × 120 s, host unlimited | accepted |
| C7 | Session 45–90 min, no meta-progression | accepted |
| C8 | One tribe, one soldier type, 24 buildings | accepted |
| D1 | One-time snapshot for reconnect/desync recovery | allowed |
| D2 | Host loss → match ends with auto-saves, no host migration in MVP | accepted |
| D3 | Steam binding decided after S3 (default Steamworks.NET) | accepted (now action item) |
| D4 | LAN discovery UDP port 47800 | accepted |
| D5 | Steam AppID before M8 | accepted (now action item) |
| E1 | Test machines | only a Mac with Apple Silicon → follow-ups E5, E6 |
| E2 | Map sizes 192/256/384/512 | accepted |
| E3 | Fairness radius 24, ratio limits 1.10–1.25 | accepted, tune later |
| E4 | Footprints 2×2 / 3×3 / 4×4 | accepted |
| F1 | Palette & team colors | accepted |
| F3 | CC0 placeholder audio for MVP | accepted |
| G1 | Check name trademark | accepted (now action item) |
| G2 | English-only UI, strings externalized | accepted |
