# Open Questions — decisions the user has to make

Each item names a recommendation. When the user answers: record it verbatim in [handoff/USER-ANSWERS.md](handoff/USER-ANSWERS.md), update [handoff/DECISIONS-LOG.md](handoff/DECISIONS-LOG.md), set any ADR status, and move the item to "Resolved" below.

## Open
| ID | Question | Recommendation | Needed before | Ref |
|---|---|---|---|---|
| B1 | May spikes S1–S5 be built? User said "No spikes yet" on 2026-10-02 — **ask again** when planning is considered finished. | Yes, S1 first (export + determinism risk) | any implementation | [09-roadmap §2](09-roadmap.md) |
| H1 | Approve [ADR 0007](decisions/0007-culture-system.md) (data-driven cultures) and [ADR 0008](decisions/0008-combat-model.md) (direct control, typed units, walls, siege)? | Approve | M0 (culture loader), M4 (combat) | ADRs |
| H2 | Approve the 4 proposed cultures — Rivermen (baseline, food/economy), Highlanders (stone/walls), Woodfolk (wood/archers), Riders (horses/mobility) — or rename/replace any? | Approve as working concepts; names can change any time | M9 | [10-cultures](10-cultures.md) |
| H3 | Unit roster (5 shared + 7 culture units) and numbers OK as balancing starting values? | Yes | M4 | [11-military §3](11-military.md) |
| H4 | Phase order: Steam (phase B) **before** cultures & walls (phase C), based on "after that comes steam and so on". Correct, or cultures/walls first? | Steam first: online playtests with friends help balance cultures | M6 | [09-roadmap §1](09-roadmap.md) |
| H5 | The MVP is now ≈ 114 weeks (≈ 26 months, ≈ 33 with contingency). Accept, or use a lever (e.g. release with 2 cultures, add 2 as updates: −11 w)? | Accept; decide on levers after LAN Alpha | phase C | [09-roadmap §4](09-roadmap.md) |
| H6 | Soldier cap 400 per player? | Yes, revisit after spike S5 | M4 | [ADR 0008](decisions/0008-combat-model.md) |
| F2 | Final art: commission an artist, buy asset packs, or own Blender work? (Prototype art: Kenney only — decided.) | Decide after M3 | M17 | [07-art-style](07-art-style.md) |

## Action items from accepted recommendations (not questions)
| ID | Action | When |
|---|---|---|
| D5 | Acquire a Steam AppID (Steam Direct fee); use AppID 480 until then | before M6 |
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
| E1 | Test machines | Mac with Apple Silicon → follow-ups E5, E6 |
| E5 | Exact Mac model | MacBook M5, 24 GB RAM (reference machine) |
| E6 | Windows/Linux manual testing | own Windows gaming PC; "linux is not that important" → Linux CI-only until release |
| — | Cultures | 4 cultures, S4-style depth (unique parts); first LAN build 1 culture |
| — | Combat control | direct unit control |
| — | Walls & siege | stone walls + gates, wall towers with archers, siege units, wooden palisades |
| — | Linux | keep in CI, release later |
| — | Prototype art | Kenney only |
| E2 | Map sizes 192/256/384/512 | accepted |
| E3 | Fairness radius 24, ratio limits 1.10–1.25 | accepted, tune later |
| E4 | Footprints 2×2 / 3×3 / 4×4 | accepted |
| F1 | Palette & team colors | accepted |
| F3 | CC0 placeholder audio for MVP | accepted |
| G1 | Check name trademark | accepted (now action item) |
| G2 | English-only UI, strings externalized | accepted |
