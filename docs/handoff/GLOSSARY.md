# Glossary

| Term | Definition |
|---|---|
| ADR | Architecture Decision Record in `docs/decisions/`; states context, options, decision, consequences, status. |
| AI takeover | Host-side AI controls a disconnected human's slot until they reconnect. |
| Bundle (`TurnBundle`) | All commands of one turn, sealed and broadcast by the host. |
| Canonical serialization | Fixed field order, little-endian, no padding; basis for saves, hashes, snapshots. |
| Carrier | Generic settler that transports goods; walks freely inside own territory (no roads). |
| CGNAT | Carrier-grade NAT; prevents incoming connections, so direct hosting fails without a relay. |
| Command | Small serialized player intent (e.g. `PlaceBuilding`); the only gameplay data sent over the network. |
| Command log (`.rblog`) | Header + sequence of turn bundles; replays a match exactly. |
| Desync | Peers' sim states diverge; detected by comparing state hashes. |
| Desync dump | Text dump of the full state at the first mismatching turn, compared by `desync-diff`. |
| Deterministic lockstep | All peers run the same sim on the same commands in the same turns; state is never sent. |
| Fix | Q48.16 fixed-point number stored in a 64-bit integer ([ADR 0002](../decisions/0002-fixed-point-format.md)). |
| Flow field | Per-tile direction grid toward a target; lets many units share one path computation. |
| Golden hash | Expected hash checked into the repo (map or replay); CI fails if any OS produces a different value. |
| HPA* | Hierarchical Path-Finding A*: plans on a cluster graph, then refines locally. |
| Host | Lobby creator; runs the sim like everyone, plus AI, turn sealing and hash comparison. |
| Input delay (`D`) | Number of turns between sealing a command and executing it; adaptive 1–5. |
| Lair | Monster spawn building in a neutral zone; sends escalating waves. |
| `MapSpec` | Seed + all map parameters + generator version; fully determines a generated map. |
| `MapHash` | XxHash64 of the canonical map data; compared in the lobby. |
| Meta command | Command issued by the host (slot 255) such as `Pause`, `SetSpeed`, `AiTakeover`. |
| MultiMesh | Godot GPU instancing node; draws many copies of one mesh in one draw call. |
| PCG32 | Permuted congruential generator; the sim's RNG. |
| Region | Connected component of walkable land; used for reachability checks. |
| Relay | Server forwarding packets between peers that cannot connect directly (Steam SDR or custom). |
| SDR | Steam Datagram Relay; Valve's free relay network for Steam games. |
| Sector | 16×16-tile block used to find nearby offers/carriers quickly. |
| Settler | Any worker unit (carrier, builder, digger, specialist, soldier). |
| Share code | Base32 string encoding a `MapSpec`, copyable in the lobby. |
| Sim | `Rebuild.Sim`: the deterministic, engine-independent game simulation library. |
| Slot | One of 8 player positions: Open, Closed, Human, AI, Monsters. |
| Snapshot | Savegame-format copy of the full sim state; used for reconnect and MP load. |
| Spike | Throwaway prototype answering one technical question; needs user approval. |
| State hash | XxHash64 of the canonical sim state after a turn. |
| Sub-tile unit | Position unit: 1 tile = 256 units (integer). |
| Territory | Tiles owned by a player via military buildings; civilian building and carrier movement allowed only there. |
| Tick | One fixed sim step: 100 ms at 1× speed. |
| Turn | Lockstep unit: 2 ticks (200 ms at 1×); commands execute at turn boundaries. |
| Wave | Group of monsters spawned by a lair at scheduled times with growing strength. |
