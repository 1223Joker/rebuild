# ADR 0007 — Culture system: data-driven cultures on a shared core

**Status:** approved by user (2026-10-02)

## Context
- The user wants "different cultures with different mechanics … different advantages and disadvantages, like need more wood, or more stone" ([USER-ANSWERS](../handoff/USER-ANSWERS.md), 2026-10-02).
- Answers: **S4-style** depth (shared core economy, different costs, 2–4 unique buildings/goods, own special units, one clear strength/weakness each); **4 cultures** in the MVP; the **first LAN build has 1 culture**, but the architecture must support cultures from day one.
- Constraints: deterministic sim, solo part-time developer, data definitions compiled into C# tables ([01-architecture §9](../01-architecture.md)).
- Genre references: *The Settlers IV* has four peoples with distinct buildings and units ([S4 manual](https://files.replacementdocs.com/The_Settlers_IV_-_Manual_-_PC.pdf)); *Age of Empires II* differentiates civilizations mostly by bonuses and unique units on a shared unit/armour-class system ([AoE II armour classes, Age of Empires wiki](https://ageofempires.fandom.com/wiki/Armor_class_(Age_of_Empires_II))).

## Options
| Criterion | (A) Separate code per culture | (B) Pure parameter differences | **(C) Data-driven: shared core + per-culture data + small fixed set of behaviour hooks** |
|---|---|---|---|
| Matches requested depth (unique parts) | Yes | No (no unique buildings/units) | Yes |
| Code paths to keep deterministic | 4× | 1× | 1× + hooks |
| Adding culture 5 later | new code | data only | data (+ hook only if a new mechanic is needed) |
| Balancing effort | High | Low | Medium (tables, AI soak) |
| Risk of desync bugs | Highest | Lowest | Low |

## Decision
**(C).** All cultures run the same systems (construction, production, logistics, combat). A culture is a data package:

```text
data/cultures/<id>/culture.json
  buildings:   base building ids it may build (+ unique ones), each with cost overrides
  goods:       unique goods (e.g. "ashlar", "horse")
  recipes:     unique production recipes
  units:       base units available + unique units, stat overrides
  modifiers:   integer percentages on named parameters
               (e.g. build_cost.wood = 50, build_cost.stone = 150, wall_hp = 125, carrier_speed = 115)
  hooks:       ids from a small fixed catalogue implemented once in the sim
               (e.g. "forest_cover_bonus", "mounted_units", "cheap_palisades")
```
- Every modifier is an **integer percent** applied with floor rounding ([ADR 0002](0002-fixed-point-format.md)); modifiers are resolved at match start into flat per-player tables, so the hot path has no lookups by name.
- Data is compiled to C# tables by the source generator; the culture data hash is part of `GameVersion`.
- Slot table gains `culture` (or `Random`, resolved with the match RNG at start → deterministic).
- First LAN build ships exactly one culture, but already loads it through this system (no hard-coded "default culture").

## Consequences
- + One code path; cultures are content, testable by AI-vs-AI soak per matchup ([08-testing](../08-testing.md)).
- + Asymmetric balance is tunable without code changes.
- − Hook catalogue needs discipline: a new hook = code + tests; keep ≤ ~12 hooks for 4 cultures.
- − 4 cultures × (unique buildings + units + art mapping + balance) is the single biggest scope item; see [09-roadmap](../09-roadmap.md).
- Culture content: [10-cultures](../10-cultures.md).
