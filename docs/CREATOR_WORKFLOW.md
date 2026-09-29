# Creator workflow

This page describes the editor tools currently available in the Linux Unity
2022.3.22f1 package. Open **Tools → NXSG → Open Graph Editor**, then open or
create a graph and save it inside `Assets` before building.

## Tools menu

The graph editor's **Tools** toolbar menu groups its commands by area:

- **Tools → Preview → Material playground and comparison** opens a temporary
  preview lab for the current graph snapshot. Reopen it from the same menu to
  load subsequent graph edits. It renders a sphere or cube with Studio, Dark, or Colored
  Lights, advances a preview clock, and lets you pause, loop, scrub, and change
  speed. **Snapshot A** and **Snapshot B** retain image comparisons.
- **Tools → Preview → Why does the scene look different?** shows whether the
  preview contains unsaved edits, which material is selected, and the current
  build status. It also lists properties differing from shader defaults and
  checks the generated shader hash. Lighting, probes and mesh differences still
  need a visual comparison.
- **Tools → View → Bookmark current view…** and **Tools → View → Jump to
  bookmark…** save and restore graph pan and zoom in the `.nxsg` layout.
  Bookmarks are named and can be deleted.
- **Tools → Material → Bake selected output to texture…** accepts a numeric or color output from a
  UV-local branch at 256, 512, 1024, or 2048 square. It writes a linear,
  non-HDR 8-bit PNG, clamps HDR and negative values, and keeps the source graph
  editable. The baker rejects time, geometry-dependent, and unsupported nodes.

**Tools → Textures → Review selected textures…** and **Tools → Textures → Import
texture set…** open the texture review workflow. You can correct filename
suggestions for albedo, mask, normal, roughness, metallic, ambient occlusion,
height, flow, and emission slots. It previews RGBA or one channel at a time, chooses a
mask channel, supports mask inversion and strength, and creates a mask, flow, or
reviewed graph. Normal previews decode
the normal map; the graph inspector's **Flip green (DirectX/OpenGL)** option
handles green-channel orientation. NXSG does not change Unity importer
settings for you.

**Tools → Textures → Start from selected material…** reads common texture slots
from a material selected in the Project window. Review every assignment before
creating an untitled graph. The window lists properties it cannot map. Colors,
numeric values, render state, animation settings, and shader-specific effects
remain on the original material; recreate them in the graph as needed. The
source material is not modified.

### Poiyomi/lilToon material conversion

Choose **Tools → NXSG → Import Poiyomi or lilToon material…** or the graph
editor's **Tools → Material → Import Poiyomi or lilToon material…**. Select a
material asset and click **Import and replace material**. NXSG builds an
editable `.nxsg` graph, saves a backup `.mat` and an import report beside the
source material, then assigns the generated shader to that same material asset.
Existing scene and avatar material references keep pointing at it.

NXSG chooses Toon or PBR automatically from source settings. Active emission
layers, glitter, AudioLink branches, clear coat, lilToon color layers and
outlines are connected where NXSG has matching nodes. A report lists active
settings and assigned textures that still need manual review. Source-specific
blending, masks, lighting and animation can render differently; compare the
generated graph, material values and avatar before relying on the conversion.
Keep the backup `.mat` until the result is accepted; use it to restore the
pre-conversion material if needed. The existing **Start from selected
material…** texture workflow remains a separate, non-modifying way to seed a
graph from common texture slots.

**Tools → Textures → Pack channels…** combines four chosen texture channels into
one linear PNG. Each output channel can read R, G, B, or A from a separate
source. An empty source becomes white. Set a common resolution, save in
`Assets`, then assign the packed texture in the graph. Inputs are sampled on a
shared 0–1 UV grid and keep their own Unity import settings.

**Tools → Textures → Build atlas from selected images…** packs selected Project
textures into equal cells in path order. Choose columns and cell size, then
save the PNG in `Assets`. Use it with Flipbook, Texture Sheet Animation, or
Surface Particles' atlas columns/rows. Export GIF animation frames as images
before using this builder; Unity does not import the GIF itself here.

## Organizing the canvas

Choose **Tools → Auto-organize graph** to arrange connected branches into
labelled frames. On graphs with several surface inputs, NXSG separates
albedo, emission, roughness and particle controls by what they feed. Inputs
used by several branches get a **Shared controls** frame; surface nodes and
Output stay together. Nodes that do not reach Output go into **Unused branches**.

Small graphs keep a simple left-to-right layout. Short value chains sit near
the nodes they control, rather than joining one tall input column. Frames
use measured node sizes and pack into rows. Complex graphs can still have
crossing wires; organization does not insert reroutes or duplicate nodes.

Automatic frames are refreshed when you organize again. Rename a frame to
keep it as a manual group. Existing manual frames and Patterns retain their
names, membership, notes and collapsed state.

**Tools → Auto-organize selection** arranges two or more selected nodes and
keeps other nodes in place. If the result needs more room, it moves below
nearby nodes. Complete frames and Patterns move together; expanded groups
also get an internal layout, while folded Patterns retain their internal
positions. Selection-only organization does not create automatic frames.

Each organization is one **Undo** step. Save the graph to retain the layout.
Organizing changes positions and frame metadata without changing connections
or shader values.

Enable **View → Grid snapping** to snap drags to the canvas's 24-unit grid.
Hold **Alt** while dragging for free movement. Multiple selected nodes, frames,
and Patterns share one movement offset so their internal spacing stays intact.
Snapping starts off and remembers your preference across editor sessions.

![Organized branches, a frame and a collapsed Pattern in the Unity editor](images/auto-organize.png)

## AudioLink and lighting checks

Select an AudioLink node to enable its graph preview toggle and single value
slider. The material playground has the same single preview value under
**AudioLink preview uniforms**. These controls test one value; they do not
provide live AudioLink spectrum data.

The playground's **Dark** mode changes preview light intensity and background.
The **Darkness Glow** node exposes strength, threshold, and softness. Its
lighting approximation uses ambient spherical harmonics and the main light;
additional pixel lights and LTCGI are not measured. Subsurface similarly approximates main-light wrap
and backlighting.

## Material controls and presets

Expose Float or Color parameters in the graph inspector. Set
**Material group** to a short name to place that stable parameter ID under a
matching group in the generated material inspector. Clearing the field removes
the group. Group data lives in the graph adapter and survives display-name
changes.

In the graph editor's **Tools → Material** submenu, choose **Save preset…** or
**Load preset…**. Presets are Unity assets containing shader-compatible values,
textures, and texture scale/offset. Loading checks the exact shader name,
records Undo for the target material, and leaves incompatible targets
untouched. The underlying utility also supports applying one preset to multiple
compatible materials. Presets do not carry graph topology or shader code.
To apply one preset to several compatible materials, select their assets in
the Project window and choose **Tools → Material → Apply preset to selected
materials…**. Review the compatibility list before applying; the operation
records one Undo step for all selected materials.

## When results mismatch

Use **Why does the scene look different?** first. Save and build before
comparing the generated material. Check material overrides, texture importer
settings, mesh normals/UVs, scene lights, reflection probes, render queue, and
color space. The material playground is a controlled preview, not proof of
VRChat client or headset output. Windows/D3D, stereo headset, and live client
behavior remain unverified; see [GOODIES](GOODIES.md#verification).

### Preview recovery

**Reset preview controls** restores time, playback, lighting, AudioLink and motion
without discarding A/B snapshots. Changing materials invalidates the old live
frame; snapshots wait until the new material has rendered.
