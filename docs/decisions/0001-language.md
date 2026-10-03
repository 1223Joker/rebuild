# ADR 0001 — Programming language: C# (.NET) vs GDScript

**Status:** approved by user (2026-10-02; proposed same day). User delegated the choice to this ADR ([USER-ANSWERS](../handoff/USER-ANSWERS.md), Q4).

## Context
- The simulation must be deterministic, integer/fixed-point only, single-threaded, engine-independent and run headless (CI, AI tests, custom relay/host tooling). See [ORIGINAL-BRIEF §2](../handoff/ORIGINAL-BRIEF.md).
- Scale: up to 8 players, 512×512 tiles, thousands of settlers ([USER-ANSWERS](../handoff/USER-ANSWERS.md) Q8) → the sim is CPU-heavy (pathfinding, economy, AI).
- Targets: Windows, macOS ARM + x64, Linux. (Update 2026-10-03: macOS ARM only, user decision.) Release on Steam (closed source).
- Solo part-time developer → tooling, testability and refactoring safety matter.

## Options

| Criterion | (A) C# everywhere (sim = plain .NET library, client = Godot .NET) | (B) GDScript everywhere | (C) GDScript client + C# sim | (D) C++ GDExtension sim |
|---|---|---|---|---|
| Engine independence of sim | Strong: sim project has **no** Godot reference; enforced by the compiler | Weak: GDScript only runs inside Godot (headless Godot still needed) | Strong | Strong |
| Headless tests / CI | `dotnet test` on any OS, no Godot install | Godot `--headless` + test addon | `dotnet test` | CMake + native test runner per OS |
| CPU performance (integer-heavy loops) | Several × GDScript ([Godot forum](https://forum.godotengine.org/t/state-of-gdscript-vs-c-performance-in-godot-4-0/5875), [godot#46029](https://github.com/godotengine/godot/issues/46029)) | Slowest | Fast sim, slower client glue | Fastest |
| Ban floats / unordered iteration | Compile-time via Roslyn [BannedApiAnalyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md) | Not enforceable (`float` is a built-in; Dictionary preserves insertion order but no static checks) | Compile-time | Partly (clang-tidy, custom checks) |
| Integer semantics | Fully specified: two's complement, wrap in `unchecked`, `long`=64 bit on all platforms ([C# spec — integral types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types)) | 64-bit int only; no 32-bit type, no 128-bit multiply | Fully specified | Specified since C++20, UB on signed overflow |
| Fixed-point libraries | [FixedMath.NET](https://github.com/asik/FixedMath.Net) (Q31.32), [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) (Q32.32); `Int128`/`Math.BigMul` built in | None mature | Same as A | [mas-bandwidth/fixed](https://github.com/mas-bandwidth/fixed) (Q48.16) |
| Export to Win/macOS ARM+x64/Linux | Supported since Godot 4.2 ([Godot blog](https://godotengine.org/article/platform-state-in-csharp-for-godot-4-2/)); known past issue with macOS arm64 .NET export ([godot#94631](https://github.com/godotengine/godot/issues/94631)) → verify in spike | Trivial | Same as A | Build 4 native binaries per release |
| Steam bindings | [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) / [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) | [GodotSteam](https://godotsteam.com/) | Either | Steamworks SDK directly |
| Web export | Not supported for C# (not a target) | Yes | No | No |
| Solo-dev productivity | High (IDE, refactoring, type safety) | High for small scripts, degrades with size | Two languages | Lowest |

## Decision
**Option A — C# on .NET 8+ (the version Godot .NET currently requires) for everything.**
- `Rebuild.Sim` is a plain `net8.0` class library with zero Godot dependency, compiled with `<Nullable>enable</Nullable>`, `<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>` (explicit, documented wraparound) and BannedApiAnalyzers that forbid `float`, `double`, `decimal`, `System.Math` float overloads, `System.Random`, `DateTime.Now`, `Dictionary<,>`/`HashSet<>` enumeration, `Parallel`, `Task`, `Thread`.
- The Godot client is a Godot .NET project that references `Rebuild.Sim` read-only.
- Fixed-point type: own small `Fix` struct (see [ADR 0002](0002-fixed-point-format.md)); third-party libraries serve only as reference implementations/test oracles to keep full control over rounding.

## Consequences
- + Compile-time determinism guards; `dotnet test` on all 3 OS in CI without Godot.
- + Same language for sim, networking, AI, relay server, tools.
- − Requires the Godot .NET build and .NET SDK; C# cannot target web (irrelevant).
- − Must verify the macOS arm64 .NET export early → spike S1 in [09-roadmap](../09-roadmap.md). (Update 2026-10-03: Intel macOS/x64 is no longer a target, user decision; no universal build needed.)
- − Interop cost Sim↔Godot: keep crossings coarse (one snapshot read per frame, no per-entity calls into Godot).
- If the export spike fails on a platform, fallback is option (D) for the sim only; recorded as a risk.
