<p align="center">
  <img src="docs/assets/nxsg-banner.svg" width="100%" alt="NX Shader Graph — a visual material editor for VRChat" />
</p>

<p align="center">
  <strong>Your textures. Your effects. Your shader.</strong><br />
  Make VRChat materials in Unity, one connection at a time.
</p>

<p align="center">
  <a href="https://nerdrx.github.io/nxsg/#install"><strong>Install NXSG</strong></a> &nbsp;·&nbsp;
  <a href="docs/QUICK_START.md">First material</a> &nbsp;·&nbsp;
  <a href="docs/NODES.md">Node guide</a> &nbsp;·&nbsp;
  <a href="Packages/dev.nerdrx.nxsg/Samples~/README.md">Example graphs</a> &nbsp;·&nbsp;
  <a href="https://github.com/nerdrx/nxsg/releases">Releases</a>
</p>

<p align="center"><sub>Early alpha · Unity 2022.3.22f1 · Built-In pipeline · PC VRChat target</sub></p>

## Start simple. Keep going.

A texture, a surface, an output. That's enough for a first material. Add a ramp to reshape a mask, let AudioLink drive an effect, or put a layer of fur over the mesh. The graph stays editable as the material grows.

NXSG is built around the way avatar creators work: named texture slots, draggable connections, searchable nodes, and previews of the values between them. **Build for VRChat** creates the shader and material locally. No hand-written shader code needed.

[![NXSG in Unity: type-colored node headers, graphite canvas, violet controls, and a live particle preview](docs/images/editor-ui-pass.png)](docs/UI_POLISH.md)

<p align="center"><sub>Actual Unity editor on Linux. The Particle Lifetime example is included.</sub></p>

## Get it into Unity

1. [Add the NXSG repository](https://nerdrx.github.io/nxsg/#install) to **ALCOM or Creator Companion**.
2. Enable **Show Pre-Release Packages**, then add **NX Shader Graph** to your project.
3. Open **Tools → NXSG → Open Graph Editor** in Unity.
4. Create a graph, or import **Example Graphs** from NXSG's Unity Package Manager entry.
5. Save inside **Assets**, choose **Build for VRChat**, and assign the material from **Assets/NXSGGenerated** to your mesh.

<details>
<summary>Adding the repository manually?</summary>

```text
https://nerdrx.github.io/nxsg/index.json
```

[Installation help](docs/VPM.md) · [First material walkthrough](docs/QUICK_START.md)

</details>

**Start in a test project.** NXSG is an early alpha, developed and tested on Linux. Native Windows/D3D, headset and live VRChat validation remain incomplete. Quest/mobile avatars are not supported. Building creates local assets; it does not upload your avatar. [Tested behavior and remaining limits](docs/VALIDATION.md).

## A few things you can make

<table>
  <tr>
    <td width="33%"><a href="Packages/dev.nerdrx.nxsg/Samples~/README.md"><img src="docs/assets/hologram-study.png" alt="Layered violet and cyan hologram rendered on a sphere and capsule" /></a><br /><strong>Layered holograms</strong><br /><sub>Light, scanlines, and a little distance from the mesh.</sub></td>
    <td width="33%"><a href="Packages/dev.nerdrx.nxsg/Samples~/README.md"><img src="docs/assets/pearl-study.png" alt="Iridescent pearl finish rendered on a sphere and capsule" /></a><br /><strong>Pearl and iridescence</strong><br /><sub>Color that changes with the view.</sub></td>
    <td width="33%"><a href="docs/FUR_AND_PARALLAX.md"><img src="docs/assets/fur-study.png" alt="Warm short shell fur rendered on a sphere and capsule" /></a><br /><strong>Soft, short fur</strong><br /><sub>Shape the length, roots, tips, and movement.</sub></td>
  </tr>
</table>

These are Unity renders from included graphs. The fur study uses **24 shells**; it's an appearance study, not a performance target. [Watch the hologram move](https://nerdrx.github.io/nxsg/#material-studies).

### Go beyond the mesh

[![Pearl sculpture with gold bands, formed by a raymarched distance field](docs/images/volume-pearl-sculpture.png)](docs/VOLUMES.md)

This sculpture is drawn inside a cube using distance fields. The same volume tools can make drifting dust and smoke. [Explore the three volume studies](docs/VOLUMES.md)—the graphs are included. Use a closed cube proxy. These Unity renders use presentation bloom and tone mapping; raymarching is an experimental PC effect with a per-pixel cost.

## Pick a direction

| Want to make… | Tools to start with |
| :--- | :--- |
| **An everyday avatar material** | Toon, PBR and Unlit surfaces. Normal maps, matcaps, rims, named texture slots, optional albedo alpha, and lighting brightness/saturation controls. |
| **Fur** | Shells, silhouette fins, or cards generated from mesh edges. Masks, root/tip colors, grooming, wind, local self-shadowing and shell distance LOD. [Fur guide](docs/FUR_AND_PARALLAX.md) |
| **Glitter and glow** | View-dependent glitter, emission, iridescence, additional pixel lights and optional LTCGI. [Glitter](docs/GLITTER.md) · [LTCGI](docs/LTCGI.md) |
| **Layers and surface detail** | Nested shells, stickers, wireframes, parallax, parallax occlusion and tessellation. [Tessellation](docs/TESSELLATION.md) |
| **Procedural patterns** | 1D–4D noise, Voronoi, Musgrave-style fractals, waves, ramps, distortion, texture bombing, Polar and Panosphere coordinates. [Node guide](docs/NODES.md) |
| **Movement** | UV scrolling, flipbooks, dissolve, vertex animation, AudioLink inputs and shader-driven particles emitted from the mesh. [Particles](docs/PARTICLES.md) |

## Made to be worked in

- **Follow the color.** Solid type-colored headers and sockets make a graph easier to scan. Selection gets a distinct outline.
- **Keep connecting.** Drag from either socket direction. Drop on empty space to find a compatible node, or pull from a connected input to detach it.
- **Change your mind.** Switch related operations in the header. Box-select, duplicate, and reuse groups as **Patterns**.
- **See what's happening.** Preview intermediate values, compare A/B material snapshots, and scrub animation time. Collapse the material preview when you need more room.
- **Cut the repetition.** Rename texture slots, import texture sets, use presets, and bake static branches to textures.

**Auto scene** applies saved edits after a short pause. Repeated builds avoid rewriting unchanged shader source, and saving no longer refreshes the entire project. [Build measurements](docs/BUILD_PERFORMANCE.md).

[Creator workflow](docs/CREATOR_WORKFLOW.md) · [Effect handles and motion](docs/MOTION_AND_HANDLES.md) · [Editor UI notes](docs/UI_POLISH.md)

<details>
<summary><strong>Canvas shortcuts and recovery</strong></summary>

| Action | Control |
| :--- | :--- |
| Connect | Drag between sockets, or click both endpoints |
| Detach | Pull from the connected input |
| Select a group | Drag empty canvas; Shift adds to selection |
| Pan / zoom | Middle-drag / scroll wheel |
| Find a node | Space on the canvas |
| Fit graph / frame selection | Home / F |
| Copy / paste / duplicate | Toolbar or Ctrl+C / Ctrl+V / Ctrl+D |
| Undo / redo | Toolbar buttons; keyboard undo remains unreliable on Linux/Unity |

Failed builds keep the last working shader. Recovery snapshots live under `Library/NXSG/Recovery`; deleting Library removes those snapshots too.

</details>

## Before you cover an avatar in it

- **Extra geometry costs extra.** Fur, shells, particles and tessellation can get expensive. Generated cards follow triangle edges and can overlap. Cost warnings are estimates, not GPU measurements.
- **Lighting varies by effect.** Additional pixel lights affect Toon/PBR base surfaces; fur and shell overlays have more limited lighting. Brightness limits apply per contribution, not to the sum of all lights.
- **Some effects approximate the result.** Refraction samples the screen. Fur self-shadowing estimates a local volume. Transparent layers can sort incorrectly, and displaced effects may need larger renderer bounds.
- **Integrations still need live testing.** AudioLink and animatable properties exist; live AudioLink and VRCFury checks remain pending. Motion inputs read avatar speed, not individual bones or PhysBones. Patterns are editable copies, not linked instances.

[Compatibility](docs/COMPATIBILITY.md) · [Validation](docs/VALIDATION.md)

## Found something awkward?

[Open an issue](https://github.com/nerdrx/nxsg/issues). A small `.nxsg` graph, your Unity version, and a screenshot help more than a long description. Controls that are confusing count, too.

Current priorities are testing on actual avatars and in stereo, fur performance, and editor usability. A community node SDK, Blender bridge, CLI and web viewer are longer-term plans.

<details>
<summary><strong>For contributors</strong></summary>

The graph model, compiler and Unity editor are separate. `.nxsg` stores the editable graph; ShaderLab/HLSL is generated from it. Unity asset references live in an adapter section.

With Git, Python 3 and Unity Hub installed:

```bash
git clone https://github.com/nerdrx/nxsg.git
cd nxsg
python3 scripts/setup-vrchat-fixture.py
```

The setup script downloads and verifies pinned VRChat Base/Avatars **3.10.5** packages. Open **DevProject** in Unity **2022.3.22f1** afterward.

[Development setup](docs/DEVELOPMENT.md) · [Design document](NXSG_DESIGN.md) · [Implementation plan](docs/IMPLEMENTATION_PLAN.md)

</details>

### Credits and source

Graphlit, ShaderGraphVRC and Poiyomi informed the research. Panosphere seam handling follows Poiyomi's derivative-aware approach, credited under its MIT license in the [third-party notices](Packages/dev.nerdrx.nxsg/Third%20Party%20Notices.md). [Research notes](docs/research/EDITOR_UX_AND_PRIOR_ART.md) · [Artwork credits](docs/assets/README.md).

NXSG's source is public. **No project-wide open-source license has been selected yet.** Third-party components retain their own licenses.
