# User Answers

Every answer, instruction or preference the user gives, recorded as close to the original wording as possible. Newest entries at the bottom. These override defaults and assumptions in other documents.

| Date | Topic | Question asked | User's answer (near-verbatim) |
|---|---|---|---|
| 2026-10-02 | Initial brief | — | Full brief given; stored verbatim in [ORIGINAL-BRIEF.md](ORIGINAL-BRIEF.md). |
| 2026-10-02 | Q1 Team & time | Who builds this and how much time is available? | "Solo, part-time" — one developer (plus AI agents), ~10–15 h/week. |
| 2026-10-02 | Q2 Distribution | How will the game be released? | "Steam, commercial" — enables Steam relay/lobbies (GodotSteam); closed source. |
| 2026-10-02 | Q3 Server ops | Would you operate a small server (custom relay/lobby) for internet play? | "Steam is the main plan, but there should be the option for a custom relay" |
| 2026-10-02 | Q4 Language | Language preference for simulation and client? | "Let the ADR decide (Recommended)" |
| 2026-10-02 | Q5 Perspective | Visual perspective preference? | "Low-poly 3D" — rotatable camera, easier animation variety. |
| 2026-10-02 | Q6 Logistics | S4 free-walking carriers vs S2/Widelands roads + flags? | "S4-style free walking" — carriers pick up goods anywhere in territory. |
| 2026-10-02 | Q7 MVP scope | What should the first playable MVP include? | "Full loop incl. monsters + AI" — economy, military, AI opponent, monster lairs. |
| 2026-10-02 | Q8 Scale | Scale targets? | "Up to 8 players, ~512×512 tiles" — thousands of settlers per match. |
| 2026-10-02 | Q9 Monsters | How should neutral monsters behave? | "Escalating waves" — pressure increases over time, PvE survival flavor. |
| 2026-10-02 | Q10 MP extras | Which multiplayer comfort features are required beyond pause/disconnect/reconnect? | "Save/load multiplayer games", "AI takes over disconnected player", "Game speed control". (Not selected: spectators / replay viewer.) |
| 2026-10-02 | Execution plan | Agent proposed: commit setup, record answers, write ADRs then docs 00–09, open-questions, glossary, handoff test, summary, stop. | Approved the plan (no changes requested). |
| 2026-10-02 | A1–A6 ADRs | Which ADRs do you approve as proposed? | Selected all: "0001 C# / 0002 fixed point", "0003 map determinism", "0004 transport", "0005 3D / 0006 sim core" → ADRs 0001–0006 approved. |
| 2026-10-02 | B1 Spikes | May I build the throwaway spikes S1–S5? | "No spikes yet" |
| 2026-10-02 | C1 Scope | MVP ~21 months part-time; how to handle scope? | "Keep scope, LAN PvP first (Recommended)" — full MVP, playable LAN PvP build after M5. |
| 2026-10-02 | D1 Snapshot | One-time state snapshot OK for reconnect and desync recovery? | "Yes, allowed (Recommended)" |
| 2026-10-02 | E1 Machines | Which machines do you own for testing? | "Mac Apple Silicon" (only). |
| 2026-10-02 | D2 Host loss | If the host leaves mid-game? | "End match + auto-save (Recommended)" |
| 2026-10-02 | C2/C3 Allies & fog | Alliance & visibility rules? | "Shared vision + visual fog" — allies share vision, carriers may cross allied land, visual fog of war in MVP. |
| 2026-10-02 | Remaining defaults | Accept recommendations for C4–C8, D3–D5, E2–E4, F1, F3, G1–G2? | "Accept all (Recommended)" |
| 2026-10-02 | Cultures (new requirement) | — (unprompted) | "there should be different cultures with different mecheaniks, like in other games. they should have differnt addvantiages and disaddvantages, like need more wood, or more stone," |
| 2026-10-02 | Military depth (new requirement) | — (unprompted) | "there should be many different warieres, and big stone walls etc. to have a nice war experance." |
| 2026-10-02 | E5/E6 Machines | Exact Mac model; Windows/Linux test hardware? | "i have a macbook m5 with 24gb of ram, and a very good gaming pc with windows, linux is not that important," |
| 2026-10-02 | Prototype art | — | "source your art for the prototype form kenney.nl" |
| 2026-10-02 | Priority | — | "first of all its important to have a working version with lan support, after that comes steam and so on" |
| 2026-10-02 | Timing of cultures/war | When should cultures, many warrior types and walls arrive relative to the first LAN version? | "LAN first with 1 culture, then expand (Recommended)" — first LAN build: 1 culture, 2–3 warrior types, no walls; architecture supports cultures from day one. |
| 2026-10-02 | Culture depth | How different should cultures be? | "S4-style: unique parts (Recommended)" — shared core economy, different costs, 2–4 unique buildings/goods, own special units, a strength/weakness each. |
| 2026-10-02 | Culture count | How many cultures for the MVP? | "4 cultures" |
| 2026-10-02 | Combat control | How should war be controlled? | "Direct unit control (Recommended)" — select soldiers, move, attack. |
| 2026-10-02 | Linux | Linux is "not that important"; what should happen? | "Keep in CI, release later (Recommended)" — determinism tests on Linux in CI from day one; Linux builds ship with the Steam release or later; no manual Linux testing before then. |
| 2026-10-02 | Walls & siege | Which wall and siege features? | All selected: "Stone walls + gates", "Wall towers with archers", "Siege units", "Wooden palisades". |
| 2026-10-02 | H1 ADR 0007/0008 | Approve ADR 0007 (cultures) and ADR 0008 (combat)? | "Approve both (Recommended)" |
| 2026-10-02 | H2 Cultures | Rivermen, Highlanders, Woodfolk, Riders OK as working concepts? | "Yes, as working concepts (Recommended)" — names/details may still change. |
| 2026-10-02 | H3 Units | Unit roster and stats as balancing starting values? | "Yes (Recommended)" |
| 2026-10-02 | H4 Phase order | After LAN Alpha: Steam first, then cultures & walls? | "Cultures & walls first" — Steam after the cultures & war phase. |
| 2026-10-02 | H5 Scope | ~114 weeks; accept or cut? | "Accept, decide after LAN Alpha (Recommended)" |
| 2026-10-02 | H6 Unit cap | Maximum soldiers per player? | "800" (offered note: huge battles, may need lower player count). |
| 2026-10-02 | B1 Spikes | May I build spikes S1–S5 now? | "Not yet" |
| 2026-10-02 | F2 Final art | How should final art be made? | "Decide after M3 (Recommended)" |
| 2026-10-02 | Start implementation | — (unprompted) | "start with the Projekt Rebuild, everything is planned and dokumented, start with the coding" — implementation is permitted (supersedes "Not yet" for B1). |
| 2026-10-02 | .NET SDK | May the agent install the .NET SDK to ~/.dotnet via Microsoft's dotnet-install.sh? | "Install .NET 8 + 10 SDK (Recommended)" |
| 2026-10-02 | Start point | Spikes first, or M0 directly with S1's sim half folded in? | "M0 directly, S1 folded in (Recommended)" — Godot export part of S1 is checked later with the client. |
| 2026-10-02 | Repository | — (unprompted) | "dokument it in your dokuments that you shoud use https://github.com/1223Joker/rebuild" — this private GitHub repo is the project remote (`origin`). |
| 2026-10-02 | Next steps | — (unprompted) | "work on the next steps on the Projekt" — continue with the STATUS next steps (M0 leftovers, then M1 Map generation). |
| 2026-10-02 | M1 design choices | Approve land corridors, per-team F8, max 6 starts on Small maps? | "1. is approved" |
| 2026-10-02 | Reference-machine benchmark | Run `mapgen --size XL --players 8 --stats 100` on the MacBook M5 | 100/100 first-attempt pass; attempt mean 48 ms, p50 47, p99 56; map max 73 ms |
| 2026-10-02 | S4 folded into M1 | May S4's map tuning be done as part of M1? | "what do you mean?" — clarification requested |
| 2026-10-02 | Spikes | (after the clarification) | "the \"not yet\" was in the planning phase, that is now over, and all spikes are allowed" — S1–S5 may be built; S4 counts as done within M1. |
| 2026-10-02 | Next step (scheduled task) | — (unprompted) | "Build the next Step, test it and push it. Use the skill code-review. Dokument what you have done/finished! Check CI when finished! Push it to MAIN" |
| 2026-10-02 | M2 step 1 scope | "generating the world is already done????" → explained: M1 generator unchanged, this step wires it into the sim + territory | "ok then go on" — finish the step, review, push to `main`, check CI. |
| 2026-10-02 | Next step (scheduled task, 2nd run) | — (unprompted) | "Use a update to date version von branche MAIN. Build the next Step, test it and push it. Use the skill code-review. Dokument what you have done/finished! Check CI when finished! Push it to MAIN. Delete you old branche." — done as M2 step 2 (building data + placement). |
| 2026-10-03 | Next step (scheduled task, 3rd run) | — (unprompted) | Same prompt as the 2nd run — done as M2 step 3 (goods, castle stock, construction, `Demolish`). |
| 2026-10-03 | Next step (scheduled task, 4th run) | — (unprompted) | Same prompt as the 2nd run — done as M2 step 4 (carriers, A* pathfinding, movement). |
| 2026-10-03 | Next step (scheduled task, 5th run) | — (unprompted) | Same prompt as the 2nd run — done as M2 step 5 (logistics: transport jobs for construction materials). |
| 2026-10-03 | Next step (scheduled task, 6th run) | — (unprompted) | Same prompt as the 2nd run — done as M2 step 6 (production: woodcutter, sawmill, stonecutter). |
| 2026-10-03 | Next step (scheduled task, 7th run) | — (unprompted) | Same prompt as the 2nd run — done as M2 step 7 (food chain and smelters: fisher, hunter, farm, waterworks, mill, bakery, pig farm, slaughterhouse, iron/gold smelter). |
| 2026-10-03 | Next step (scheduled task, 8th run) | — (unprompted) | "Use a update to date version von branche MAIN. Build the next Step, test it and push it. Use the skill code-review. Dokument what you have done/finished! Check CI when finished! Push it to MAIN. Delete you old branche." — done as M2 step 8 (mines: food alternatives, ore deposits with depletion). |
| 2026-10-03 | Next step (scheduled task, 9th run) | — (unprompted) | Same prompt as the 8th run — done as M2 step 9 (forester planting trees). |
| 2026-10-03 | Next step (scheduled task, 10th run) | — (unprompted) | Same prompt as the 8th run — done as M2 step 10 (toolsmith/weaponsmith with quotas). |
| 2026-10-03 | Population needs, seasons, weather | — (unprompted) | "change it that every person needs food and water, not just the mines, and housing, Every house needs a little bit wood or Coal in the winter to heat it. The should be different between the cultures. also add the feature to the list that there a weather events, that make war impossible like a big snow storm, or a thunder storm that can desroy houses and so on. Be creative" — designed in [12-needs-seasons-weather](../12-needs-seasons-weather.md), [ADR 0009](../decisions/0009-needs-seasons-weather.md) (proposed; details → open question H9). |
| 2026-10-03 | Weather events without war | — (clarification of the previous answer) | "in this weather events where war isnt possible, i meant that the player needs to take his units to a shealter otherwise the get dame over time and die" — the truce rule was replaced by exposure damage + shelters ([12 §3.2](../12-needs-seasons-weather.md)). |
| 2026-10-03 | Next step (scheduled task, 11th run) | — (unprompted) | "Use a update to date version von branche MAIN. Build the next Step, test it and push it. Use the skill code-review. Dokument what you have done/finished! Check CI when finished! Push it to MAIN. Delete you old branche." — done as M2 step 11 (production statistics). |
| 2026-10-03 | Next step (scheduled task, 12th run) | — (unprompted) | Same prompt as the 11th run — done as M2 step 12 (workers with tools). |
| 2026-10-03 | Tools of demolished workplaces; Intel macOS | — (review of M2 step 12) | "No the tool should not be lost. And remove the mac os x86 version kompletly, only the macos arm version" — a freed worker now carries its tool back to the nearest storage; Intel macOS (x64) removed as a target and from CI (`macos-15-intel` runner gone); macOS = Apple Silicon only. |
| 2026-10-03 | Next step (scheduled task, 13th run) | — (unprompted) | Same prompt as the 11th run — done as M2 step 13 (calendar + seasons). |
| 2026-10-03 | Production until storage is full; mines without food | — (during M2 step 13) | "make a new task that all Produktion Sites produce until the storage is full or unreachable and their internal little storage is also full, also a new task that mines do not need extra food anymore" — queued as two follow-up tasks (storage capacity + reachable overflow; remove the mine work ration), see STATUS next steps. |
| 2026-10-03 | Save compatibility | — (unprompted) | "dokument for now that you dont have to care about save changes that the old saves cant load, until the full release" — until the full release, save/log format changes need no migration or backward compatibility; old saves and command logs may simply stop loading (they are rejected by format/GameVersion checks). |
| 2026-10-03 | Agent plugin | — (unprompted) | "yeah change it that you agents use ponytail" — the ponytail plugin (DietrichGebert/ponytail) is enabled for all sessions via `.claude/settings.json`; AGENTS.md rules take precedence over it. |
| 2026-10-03 | Next step (scheduled task, 14th run) | — (unprompted) | "Use a update to date version von branche MAIN. Build the next Step, USE ponytail! test it and push it. Use the skill code-review. Dokument what you have done/finished! Check CI when finished! Push it to MAIN." — done as M2 step 14 (storage capacity + mines without food, the two queued requests). |
| 2026-10-03 | Ponytail availability | — (during the 14th run) | "do you use ponytail?" / "can you now acces ponytail?" — answer: the plugin is enabled in `.claude/settings.json` but was not installed in the cloud session (no ponytail skills; cloning the repo was refused by the session's permission policy); its rules were followed by hand. |
