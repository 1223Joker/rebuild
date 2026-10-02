# Rebuild

Real-time city-building strategy game in the style of *The Settlers 4* — Godot 4 (C#), deterministic lockstep multiplayer, seed-based random maps. Start with [AGENTS.md](AGENTS.md).

## Build & test
Requires the .NET SDK pinned in [global.json](global.json) (SDK 10, building `net8.0` targets) plus the .NET 8 runtime.

```bash
dotnet test
```

```bash
dotnet run --project src/Rebuild.Tools -- probe
```

| Path | Contents |
|---|---|
| `src/Rebuild.Sim` | deterministic simulation (no Godot, no floats, no threads) |
| `src/Rebuild.Analyzers` | determinism analyzers (RB0001–RB0004) + culture data source generator |
| `src/Rebuild.Tools` | headless CLI: `probe`, `replay`, `sample-log`, `hashes` |
| `data/cultures/<id>/culture.json` | culture data, compiled into C# at build time |
| `tests/` | xUnit/FsCheck tests and golden hashes/replays |
| `tools/ci/` | CI helper scripts |
