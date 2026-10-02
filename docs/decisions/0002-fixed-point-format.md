# ADR 0002 — Fixed-point number format

**Status:** approved by user (2026-10-02; proposed same day)

## Context
- The simulation may not use floats ([ORIGINAL-BRIEF §2](../handoff/ORIGINAL-BRIEF.md)); float results differ across compilers/architectures ([Gaffer — Floating Point Determinism](https://gafferongames.com/post/floating_point_determinism/)). *Realms of Ruin* shipped cross-platform lockstep with fixed point and blocked float types at compile time ([GDC 2024 Pollard](https://media.gdcvault.com/gdc2024/Slides/GDC+slide+presentations/Pollard_Bradley_CrossPlatformDeterminism+2024-03-26+09.34.19.pdf)).
- Language: C# ([ADR 0001](0001-language.md)): `int`/`long` have fixed sizes, wraparound is defined, `Int128` and `Math.BigMul` exist.
- Value ranges: map ≤ 512×512 tiles (≤ 1024 for headroom); stock counts ≤ 10⁶; timers in ticks; rates/probabilities/combat modifiers need fractions.
- Most game logic in a Settlers-style economy is naturally integer (tiles, ticks, item counts). Fractions are rare.

## Options

| Option | Storage | Range / resolution | Mul/div cost | Notes |
|---|---|---|---|---|
| Q16.16 | `int` | ±32 768 / 1.5·10⁻⁵ | 64-bit intermediate | Classic; range too tight for squared distances on 1024-tile maps in sub-tile units |
| Q32.32 | `long` | ±2.1·10⁹ / 2.3·10⁻¹⁰ | needs 128-bit intermediate | [FixedMath.NET](https://github.com/asik/FixedMath.Net) (Q31.32), [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) |
| **Q48.16** | `long` | ±1.4·10¹⁴ / 1.5·10⁻⁵ | 128-bit intermediate only for large products | Used by [mas-bandwidth/fixed](https://github.com/mas-bandwidth/fixed) (Glenn Fiedler, author of the Gaffer lockstep articles) |
| Plain integers in domain units (no generic fixed type) | `int`/`long` | per use | trivial | e.g. position in 1/256 tile, probability in 1/65 536 |

## Decision
Two layers:
1. **Domain integers first.** Positions = `int` sub-tile units with **1 tile = 256 units** (`TilePos` = `(short x, short y)`, `SubPos` = `(int x, int y)`). Time = `int` ticks. Probabilities/percentages = `int` in parts-per-65 536. Most code never touches a fractional type.
2. **`Fix` = Q48.16 in `long`** for the remaining fractional math (rates, combat formulas, AI scoring, mapgen falloff curves).
   - Add/sub: plain `long` (wraparound defined, guarded by debug asserts).
   - Mul: `(a * b) >> 16`, always computed in `Int128` (`Math.BigMul`) to avoid a range-dependent branch; **rounding = floor (arithmetic shift)** everywhere.
   - Div: `(a << 16) / b` via `Int128`; C# integer division truncates toward zero — documented and tested.
   - Sqrt: integer Newton iteration, deterministic iteration count. Trig: not needed in sim (grid directions); if needed, 1024-entry lookup table generated at build time and checked in.
   - No implicit conversion from/to `float`/`double`; the only float conversion lives in the **client** (`Rebuild.Client`), as `ToRenderFloat()` extension outside the sim assembly.

## Consequences
- + Same bits everywhere; trivially serializable and hashable.
- + Q48.16 avoids overflow traps for squared distances (1024 tiles × 256 units = 2¹⁸ → square 2³⁶, fits).
- − Precision 1/65 536 is enough for gameplay but not for physics (none planned).
- − Own `Fix` type (~300 LOC) needs a property-based test suite against a `BigInteger` reference oracle (see [08-testing](../08-testing.md)).
