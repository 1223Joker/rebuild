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
