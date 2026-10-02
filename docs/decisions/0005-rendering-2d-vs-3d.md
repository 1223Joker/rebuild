# ADR 0005 — Rendering: isometric 2D vs low-poly 3D

**Status:** proposed (2026-10-02). User preference: **"Low-poly 3D"** ([USER-ANSWERS](../handoff/USER-ANSWERS.md) Q5). This ADR records the trade-offs and confirms that choice.

## Context
- Art goals: timeless, simple, small fixed palette, clear silhouettes, swappable; prototype with Kenney CC0 assets ([ORIGINAL-BRIEF §2](../handoff/ORIGINAL-BRIEF.md)).
- Scale: thousands of settlers, 512×512 tiles visible in parts.
- Solo part-time developer: animation and asset production effort dominate.
- Rendering is client-only; the sim is unaffected by this choice.

## Options

| Criterion | Isometric 2D (sprites) | Low-poly 3D |
|---|---|---|
| Settlers-4 look | Closest | Different but genre-typical (e.g. *Against the Storm*, *Foundation*) |
| Animation cost | Every animation × 8 directions × frames, rendered per palette variant | Skeletal/vertex animation once; any direction free |
| Camera | Fixed; rotation needs 4× sprites | Free zoom/rotation steps cheap |
| Terrain height | Faked; hard with height-based gameplay | Native heightmap mesh |
| Many units | `MultiMeshInstance2D` / batching | `MultiMeshInstance3D` — one draw call per mesh type ([Godot MultiMesh](https://docs.godotengine.org/en/stable/classes/class_multimesh.html)) |
| Palette swap / team colors | Palette shader on sprites | One shared palette texture (Kenney 3D kits use a colormap texture) or vertex colors |
| Kenney prototype assets | [Medieval RTS](https://opengameart.org/content/medieval-rts-120), isometric miniature packs | [Retro Medieval Kit](https://kenney-assets.itch.io/retro-medieval-kit), Castle/Nature/Survival kits (3D) |
| GPU requirements | Very low | Low (Godot Compatibility or Mobile renderer suffices) |
| Asset swap later | Re-render all sprites | Replace meshes with same pivot/scale conventions |

## Decision (proposed)
**Low-poly 3D**, flat-shaded, fixed-pitch perspective camera (~50°) with zoom and 90° rotation steps.
- Renderer: Godot **Forward+** on desktop, with Compatibility renderer as fallback for old GPUs (decide after perf spike).
- Units/buildings drawn through `MultiMeshInstance3D` per mesh type; terrain as chunked heightmap meshes (32×32 tiles per chunk).
- Palette: one shared 16-color palette texture; models UV-map onto palette cells → full restyle by swapping one texture. Team color via per-instance custom data.

## Consequences
- + Animation and direction handling far cheaper for a solo developer; camera rotation helps readability in dense cities.
- − Not a pixel-perfect S4 look; accepted by the user.
- − Need a small asset pipeline (Blender → glTF) with conventions; see [07-art-style](../07-art-style.md).
- Performance target and validation: see [07-art-style](../07-art-style.md) and spike S2 in [09-roadmap](../09-roadmap.md).
