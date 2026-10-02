# ADR 0003 — Map generation determinism

**Status:** approved by user (2026-10-02; proposed same day)

## Context
- Requirement: same seed + parameters + game version ⇒ **bit-identical map on all platforms**; seeds are copyable/shareable; golden-hash tests run on Windows, macOS, Linux ([ORIGINAL-BRIEF §3](../handoff/ORIGINAL-BRIEF.md)).
- Godot's `FastNoiseLite` uses floats; float results may differ between compilers/CPUs ([Gaffer — Floating Point Determinism](https://gafferongames.com/post/floating_point_determinism/)). The brief forbids using it unverified for the shared map.
- Map size up to 512×512 tiles, 8 players ([USER-ANSWERS](../handoff/USER-ANSWERS.md) Q8). Raw map data ≈ 512² × ~6 bytes (height, terrain, resource, amount, object) ≈ 1.5 MB uncompressed.

## Options

| Criterion | (a) Custom integer noise in `Rebuild.Sim` | (b) Host generates (any method, e.g. FastNoiseLite) and transmits map + hash | (c) Hybrid: (a) + hash comparison, (b) as fallback |
|---|---|---|---|
| Seed reproducible across OS | Yes, by construction | **No** — host OS/CPU determines the map; same seed may give different maps on different hosts | Yes |
| Shareable seed meaningful | Yes | Only "on the same platform" | Yes |
| Golden-hash tests on 3 OS | Meaningful (must match each other) | Only per-OS goldens; cannot detect cross-platform drift | Meaningful |
| Lobby bandwidth | Seed + params (~50 B) | ~200–500 KB compressed per client; slower lobby on relays | Seed + params; full map only on mismatch |
| Implementation effort | Integer value/gradient noise + fBm (~300 LOC), well documented ([Red Blob Games](https://www.redblobgames.com/maps/terrain-from-noise/), [Eiserloh, GDC 2017 Noise-Based RNG](https://www.gdcvault.com/play/1024365/Math-for-Game-Programmers-Noise)) | Lowest | (a) + map transfer path |
| Saved maps / replays | Store seed + params + version (tiny) | Must store full map | Seed (+ full map for edited maps) |
| Risk | Bugs in own noise | Silent cross-platform divergence; float drift in later regeneration | Lowest |

## Decision
**(c) Hybrid, with (a) as the primary path.**
1. The complete generator lives in `Rebuild.Sim/MapGen`, uses only integers/`Fix` ([ADR 0002](0002-fixed-point-format.md)) and the sim RNG ([ADR 0006](0006-sim-core-conventions.md)). Noise: integer hash-based value noise (Squirrel-style hash of `(x, y, seed)`) with integer bilinear interpolation (smoothstep as integer polynomial), summed in octaves (fBm).
2. Every client generates the map locally from `MapSpec` (seed, params, `GeneratorVersion`) and reports `MapHash` (XxHash64 over the canonical map serialization) to the host in the lobby.
3. If any hash differs from the host's, the host transmits its serialized map (compressed) to that client and logs a **determinism bug report** (seed, params, both hashes, OS). Game starts only when all hashes match.
4. Godot `FastNoiseLite` may be used **only** for client-side cosmetics (grass sway, color variation) that never feed back into the sim.

## Consequences
- + Seeds are portable; golden-hash CI catches cross-platform drift before release.
- + Fallback keeps multiplayer working even if a generator bug slips through.
- − Must write and test own noise; any generator change requires bumping `GeneratorVersion` and regenerating golden hashes (old saved seeds then reference the old version → keep a version table; saved maps store the full map too).
