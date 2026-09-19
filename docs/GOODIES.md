# Surface effects and editor comforts

For the complete creator-tool walkthrough, see [Creator workflow](CREATOR_WORKFLOW.md).

## Try the effects

Open the examples from `Assets/NXSGExamples` in the development project, or copy them from the package's `Samples~` folder into your project's Assets folder.

| Example | What to try |
|---|---|
| Fur Fins | Select Fur and toggle **Fur fins**. Fins supplement the shell layers with grazing edge strips. |
| Shiny Surface | Adjust Iridescence thickness/phase and Subsurface strength. |
| Refraction Glass | Put something behind the mesh; adjust refraction strength and index of refraction. |
| Interior Bomb | Assign textures to Interior Mapping and Texture Bomb; move the camera to inspect virtual room depth. |

These are artistic approximations. Fins use triangle edges without adjacency and have no separate distance LOD; the existing LOD controls shell coverage. Refraction captures screen pixels, uses transparent ordering, and cannot see off-screen geometry. Interior Mapping samples a room atlas without creating geometry. Subsurface approximates main-light wrap/backlighting. Texture Bomb blends four transformed samples, with a fifth plain sample when needed.

## Editor controls

- **Inline Color Ramp:** edit its Gradient field on the node card. Up to eight combined color/alpha stop positions, with Undo.
- **Node thumbnails:** enable **Thumbnail** on up to four cards. They show the first supported output, follow Live preview, and pause while unfocused. The saved graph output stays intact.
- **Insert on wire:** drag a compatible node by its header until its center overlaps a wire. The wire thickens; release to insert. Undo restores the connection and move.
- **Recovery:** automatic snapshots run at most every five seconds after graph changes. **Recovery → Create checkpoint** saves a manual copy. Restore opens an unsaved copy with Auto scene paused; use **Save as** to choose a destination.

Snapshots live in `Library/NXSG/Recovery`: up to 30 automatic copies and 30 checkpoints per project. Deleting Library deletes recovery history; keep normal graph backups.

## Verification

Validated on Linux with Unity **2022.3.22f1**, OpenGLCore, in hidden Gamescope:

- `ShinyRenderSmoke`: rendered effects, finite pixels, refraction Build transaction, and all four sample shaders.
- `FurFinRenderSmoke`: fins on/off, zero mask, programmable passes and filtered-strand rendering.
- `InlineGoodiesSmoke`: gradient edit/Undo, thumbnail graph isolation, rebuild attachment and disposal.
- `RecoverySmoke`: snapshot round-trip, corrupt-file rejection, blank-window restore, material isolation and wire insertion Undo.
- Portable checks: graph validation, insertion, emission and example graphs.

Windows/D3D, headset stereo and the VRChat client remain unverified. These checks do not establish a performance budget or power-loss recovery guarantee.
