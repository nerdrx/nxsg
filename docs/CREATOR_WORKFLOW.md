# Creator workflow

This page describes the editor tools currently available in the Linux Unity
2022.3.22f1 package. Open **Tools → NXSG → Open Graph Editor**, then open or
create a graph and save it inside `Assets` before building.

## Create menu

The graph editor's **Create** menu collects small creator tools:

- **Material playground / compare** opens a temporary preview lab for the
  current graph snapshot. Reopen it from Create to load subsequent graph edits. It renders a sphere or cube with Studio, Dark, or Colored
  Lights, advances a preview clock, and lets you pause, loop, scrub, and change
  speed. **Snapshot A** and **Snapshot B** retain image comparisons.
- **Why does the scene look different?** shows whether the preview contains
  unsaved edits, which material is selected, and the current build status. It also lists properties differing from shader defaults and checks the generated shader hash. Lighting, probes and mesh differences still need a visual comparison.
- **Bookmark current view…** and **Jump to bookmark…** save and restore graph
  pan and zoom in the `.nxsg` layout. Bookmarks are named and can be deleted.
- **Bake selected output to texture…** accepts a numeric or color output from a
  UV-local branch at 256, 512, 1024, or 2048 square. It writes a linear,
  non-HDR 8-bit PNG, clamps HDR and negative values, and keeps the source graph
  editable. The baker rejects time, geometry-dependent, and unsupported nodes.

The same menu contains **Review selected textures…** and **Import texture
set…**. Review lets you correct filename suggestions for albedo, mask, normal,
roughness, metallic, ambient occlusion, height, and flow slots. It previews RGBA or one
channel at a time, chooses a mask channel, supports mask inversion and
strength, and creates a mask, flow, or reviewed graph. Normal previews decode
the normal map; the graph inspector's **Flip green (DirectX/OpenGL)** option
handles green-channel orientation. NXSG does not change Unity importer
settings for you.

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

In the graph material menu, choose **Material → Save preset…** or **Material →
Load preset…**. Presets are Unity assets containing shader-compatible values,
textures, and texture scale/offset. Loading checks the exact shader name,
records Undo for the target material, and leaves incompatible targets
untouched. The underlying utility also supports applying one preset to multiple
compatible materials. Presets do not carry graph topology or shader code.

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
