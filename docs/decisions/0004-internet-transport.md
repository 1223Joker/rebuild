# ADR 0004 — Internet transport: Steam vs custom relay vs port forwarding

**Status:** approved by user (2026-10-02; proposed same day). User input: release on Steam, commercial; "Steam is the main plan, but there should be the option for a custom relay" ([USER-ANSWERS](../handoff/USER-ANSWERS.md) Q2, Q3).

## Context
- Lockstep sends only small command packets; bandwidth is tiny, latency consistency matters more than raw latency ([AoE "1500 Archers"](https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond)).
- Topology: host-centred star (see [02-networking](../02-networking.md)).
- Many players sit behind CGNAT; Godot has no built-in NAT traversal ([Godot docs](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html), [ziva.sh](https://ziva.sh/blogs/godot-multiplayer)).
- LAN must work offline and without Steam.

## Options

| Criterion | Steam Networking Sockets + SDR | Custom relay (own UDP relay on a VPS) | Direct + port forwarding (ENet) |
|---|---|---|---|
| Works behind CGNAT | Yes (relayed) | Yes | **No** |
| Cost | Free for Steam partners ([Steamworks SDR docs](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)) | VPS ~5–10 €/month + ops time | Free |
| IP privacy / DDoS protection | Yes (Valve backbone) | Partial (relay IP exposed, players hidden) | No (host IP exposed) |
| Lobby / invites / matchmaking | Steam lobbies + friend invites built in | Must build lobby service | None (manual IP) |
| Works without Steam (other stores, dev builds, LAN parties) | No | Yes | Yes |
| C# binding | [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET), [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks); GDScript: [GodotSteam](https://godotsteam.com/classes/networking_sockets/) | Own code over ENet/UDP | Godot `ENetConnection` |
| Reliability layer | Built in (reliable + unreliable messages) | Ours (or ENet's) | ENet's |
| Effort | Low–medium | Medium | Low |

## Decision
**Pluggable `ITransport` with three implementations, all carrying the same lockstep protocol bytes:**

| Priority | Transport | Use |
|---|---|---|
| 1 | `SteamTransport` — `ISteamNetworkingSockets` P2P (`ConnectP2P`, relayed via SDR) + Steam lobbies, via **Steamworks.NET** | Default for internet play in the Steam build |
| 2 | `EnetTransport` — Godot `ENetConnection` (low-level API, not RPC) | LAN (with UDP broadcast discovery) and direct IP / port forwarding |
| 3 | `RelayTransport` — ENet client to a self-hosted relay (headless .NET console app, no game logic, forwards packets between peers of a room by room code) | Optional; non-Steam builds, Steam outage, CGNAT without Steam. Built after MVP, kept in the abstraction from day one. |

The lockstep layer adds its own redundancy (resend all unacked commands) so it works over any of them ([Gaffer — Deterministic Lockstep](https://gafferongames.com/post/deterministic_lockstep/)).

## Consequences
- + Steam handles NAT, privacy, invites at zero cost; LAN stays Steam-free.
- + Protocol code is transport-agnostic and unit-testable with an in-memory `LoopbackTransport`.
- − Steam Networking needs a Steam AppID for real testing (AppID 480 "Spacewar" for development).
- − Custom relay is a second deployable (Linux VPS); deferred until after MVP, see [09-roadmap](../09-roadmap.md).
- Open: Steamworks.NET vs Facepunch.Steamworks vs GodotSteam final pick → spike S3 in roadmap.
