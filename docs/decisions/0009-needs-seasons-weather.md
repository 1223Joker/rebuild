# ADR 0009 — Population needs, seasons and weather events

**Status:** proposed (2026-10-03) — requested by the user, details written by an agent and awaiting confirmation ([open-questions](../open-questions.md) H9)

## Context
- The user asked (2026-10-03, [USER-ANSWERS](../handoff/USER-ANSWERS.md)): every person needs food and water, not just the mines; every person needs housing; every house needs a little wood or coal in winter; this should differ between cultures; plus weather events that make war impossible (big snowstorm) or destroy houses (thunderstorm), "be creative".
- Until now only mines consumed food ([06-economy §1](../06-economy.md)); there were no seasons and no weather.
- Constraints: deterministic lockstep, integer-only sim, bounded work per tick with thousands of settlers ([ADR 0006](0006-sim-core-conventions.md)); fair random maps ([03-mapgen](../03-mapgen.md)); data-driven cultures ([ADR 0007](0007-culture-system.md)).
- Genre references: *Banished* — citizens need food, firewood keeps homes warm in winter, disasters such as fire and tornadoes ([Banished wiki — Firewood](https://banished.fandom.com/wiki/Firewood), [Banished wiki — Disasters](https://banished.fandom.com/wiki/Disasters)); *The Settlers II/IV* — only mines and some workers consume food ([S4 manual](https://files.replacementdocs.com/The_Settlers_IV_-_Manual_-_PC.pdf)).

## Options
| Criterion | (A) Per-settler needs | **(B) Per-home-building needs (household counters)** | (C) Global per-player upkeep |
|---|---|---|---|
| Visible on the map (which houses starve/freeze) | yes | yes | no |
| Cost with 5 000 settlers | 5 000 counters/tick | ~300 counters/tick | 1 counter |
| Logistics fit | new request kind per settler | need piles = production input piles (existing code) | none (instant deduction) |
| Culture variation | yes | yes | yes |

| Criterion | (W1) Live random weather each tick | **(W2) Schedule rolled at match start + 60 s forecast, local effects in equal numbers per player** | (W3) No weather |
|---|---|---|---|
| Deterministic | yes (own stream) | yes (own stream) | — |
| Fair between players | luck decides | equal hits per player | — |
| Players can plan (no surprise mid-attack) | no | yes | — |

## Decision
**(B) + (W2)**, specified in [12-needs-seasons-weather](../12-needs-seasons-weather.md):
- Every settler has a home bed; homes hold food, water and fuel piles refilled by logistics; integer need counters per home; shortage states Short/Crisis reduce work and finally make settlers leave. Mines eat no work ration (user 2026-10-03, superseding the earlier "mines keep their work ration").
- A tick-derived calendar with four seasons (lobby: Off/Short/Normal/Long); winter heating burns log/coal per building size.
- Weather events from a new RNG stream `Weather` (extends the stream list of ADR 0006 without changing existing streams), forecast 60 s ahead; Blizzard and Thunderstorm make war impossible through **exposure**: units outside a shelter lose HP every second until they die, so the player must bring armies into shelters in time (user clarification 2026-10-03; new command `SeekShelter`, new building Bivouac); lightning, fire, floods, gales, droughts, fog, hail, deep frost and two positive events. Lobby: Weather Off/Mild/Harsh.
- Cultures differ by plain numeric modifiers plus food/fuel values per good and two byproducts (Riders' kumis, dung).

## Consequences
- M2 grows by ~3 w (housing, pantries, calendar, heating); new milestone **M7b Weather & season events** (~4 w) in phase B; total ≈ 121 w ([09-roadmap](../09-roadmap.md)).
- Food and water production must scale with population, not only with mines — the economy gets a second consumer curve; balancing and AI must follow it.
- New sim state (households, fire, flooded tiles) → save format and `GameVersion` bumps when implemented; golden replays with Weather = Harsh.
- Weather = Off keeps a pure economy/war mode for competitive play.
