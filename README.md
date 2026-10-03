# Rebuild

Real-time city-building strategy game in the style of *The Settlers 4* — Godot 4 (C#), deterministic lockstep multiplayer, seed-based random maps. Start with [AGENTS.md](AGENTS.md).

Repository: https://github.com/1223Joker/rebuild

## Build & test
Requires the .NET SDK pinned in [global.json](global.json) (SDK 10, building `net8.0` targets) plus the .NET 8 runtime.

```bash
dotnet test
```

```bash
dotnet run --project src/Rebuild.Tools -- probe
dotnet run --project src/Rebuild.Tools -c Release -- mapgen --seed 7 --size L --players 4 --teams 0,0,1,1 --symmetry Mirror --png map.png
dotnet run --project src/Rebuild.Tools -c Release -- mapgen --code RB-… --stats 200
```

| Path | Contents |
|---|---|
| `src/Rebuild.Sim` | deterministic simulation (no Godot, no floats, no threads) |
| `src/Rebuild.Analyzers` | determinism analyzers (RB0001–RB0004) + culture, building and good data source generators |
| `src/Rebuild.Sim/MapGen` | seed-based map generator + fairness validation ([docs/03-mapgen.md](docs/03-mapgen.md)) |
| `src/Rebuild.Tools` | headless CLI: `probe`, `replay`, `sample-log`, `hashes`, `mapgen` |
| `data/cultures/<id>/culture.json` | culture data, compiled into C# at build time |
| `data/buildings.json` | building types (size, placement, territory, storage, cost), compiled into C# at build time |
| `data/goods.json` | good types and the start-castle stock, compiled into C# at build time |
| `tests/` | xUnit/FsCheck tests and golden hashes/replays/map cases (`tests/golden/`) |
| `tools/ci/` | CI helper scripts |
