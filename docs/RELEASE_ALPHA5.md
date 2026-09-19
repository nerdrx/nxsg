# NXSG alpha.5 — creator workflow

The graph editor now has a **Create** menu for the full material workflow:

- Material playground with Studio/Dark/Colored lighting, pause/scrub/loop controls, AudioLink preview and persistent A/B image snapshots.
- Reviewed texture-set import, RGBA channel previews, normal-import warnings, mask channels/invert/strength and flow helpers. New imports open as separate untitled graphs.
- Static UV branch baking to linear 8-bit PNG; animated/geometry-dependent branches are rejected.
- Material presets with compatible-shader checks and Undo; parameter groups are embedded in generated shaders.
- Named graph-view bookmarks, material/build mismatch details and actionable diagnostic hints.
- Darkness Glow node and Normal Map green-channel flip.

No painting workflow or new external package dependency. Creator guide: [CREATOR_WORKFLOW.md](CREATOR_WORKFLOW.md).

Validation: portable tests and isolated Linux Unity 2022.3.22f1/OpenGLCore checks for GPU baking, time override, glow response, normal flip, channel previews, preset serialization/Undo, snapshot cleanup and editor layout. Windows, headsets and a live VRChat client remain separate checks. CPU preview timing is measured; GPU timing, when supported, represents the entire editor frame. This Linux test setup reports GPU timing unavailable.

Baking clamps to 0–1 and exports a static UV texture, not an HDR/3D volume. Darkness Glow measures ambient/main-light response, not additional pixel lights or LTCGI. The playground samples existing LTCGI globals when configured; it does not reproduce world geometry.
