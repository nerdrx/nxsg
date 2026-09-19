<p align="center">
  <img src="docs/assets/nxsg-banner.svg" width="100%" alt="NXSG — NX Shader Graph, a node editor for VRChat materials" />
</p>

<p align="center">
  <a href="https://nerdrx.github.io/nxsg/"><strong>Install</strong></a> ·
  <a href="docs/QUICK_START.md">Getting started</a> ·
  <a href="docs/NODES.md">Nodes</a> ·
  <a href="Packages/dev.nerdrx.nxsg/Samples~/README.md">Examples</a> ·
  <a href="https://github.com/nerdrx/nxsg/releases">Releases</a>
</p>

## NX Shader Graph

NXSG is a shader node editor for Unity, focused on VRChat avatars. Connect textures, masks and effects, then build a material you can put on your mesh. The goal is to make custom shaders easier to work with, especially if you're used to Blender's nodes.

There's a lot of fur here: shells, fins, root/tip colors, grooming, wind and distance-based detail. You can also make toon or PBR materials, layer holograms over a mesh, add AudioLink effects, or emit particles directly from the surface.

**It's still an alpha.** Development and testing happen on Linux with Unity **2022.3.22f1**, targeting PC Built-In shaders. Windows/D3D, headsets and the live VRChat client haven't been verified yet. Quest/mobile avatars aren't supported. Use a test project for now. [Testing details](docs/VALIDATION.md).

<table>
  <tr>
    <td align="center" width="33%"><img src="docs/assets/hologram-study.png" alt="Violet and cyan scanlines floating over dark PBR sphere and capsule" /><br /><strong>Layered hologram</strong></td>
    <td align="center" width="33%"><img src="docs/assets/pearl-study.png" alt="Soft iridescent pearl PBR finish on a sphere and capsule" /><br /><strong>Pearl finish</strong></td>
    <td align="center" width="33%"><img src="docs/assets/fur-study.png" alt="Dense warm short fur on a sphere and capsule" /><br /><strong>Short plush fur</strong></td>
  </tr>
</table>

These are Unity renders of the included example graphs. The fur example uses 24 shells, so check its cost before putting it on an avatar. [Hologram video](https://nerdrx.github.io/nxsg/#material-studies) · [Example graphs](Packages/dev.nerdrx.nxsg/Samples~/README.md).

## Try NXSG

[Add the VPM listing](https://nerdrx.github.io/nxsg/), enable **Show Pre-Release Packages**, and install **NX Shader Graph**. On Linux, use the same URL in your VPM-compatible package manager:

```text
https://nerdrx.github.io/nxsg/index.json
```

1. Open **Tools → NXSG → Open Graph Editor** in Unity.
2. Create a graph, or import **Example Graphs** from NXSG's Package Manager entry.
3. Save the graph inside **Assets**, then click **Build for VRChat**.
4. Assign the generated material from **Assets/NXSGGenerated** to your mesh.

Build for VRChat creates the shader and material locally; it doesn't upload an avatar. **Auto scene** applies later edits after a short pause. Turn it off if you prefer to build manually.

[Install help](docs/VPM.md) · [First material walkthrough](docs/QUICK_START.md)

## What's in it

There are **142 nodes** so far. The [node guide](docs/NODES.md) covers their inputs and settings.

| Area | Features |
| :--- | :--- |
| Surfaces and lighting | Toon, Unlit, PBR, additional pixel lights, matcaps, rim lighting, iridescence and optional LTCGI |
| Fur | Shells, optional fins, direction, masks, root/tip colors, wind and LOD |
| Layers and depth | Nested shells, stickers, wireframes, parallax, parallax occlusion and tessellation |
| Procedural textures | 1D–4D noise, Musgrave-style fractals, Voronoi, waves, gradients, texture bombing and distortion |
| Animation | UV scrolling, flipbooks, dissolve, vertex animation, AudioLink and shader-driven surface particles |
| Motion | Speed-driven glow, flutter and UV stretching through a generated FX Animator driver |

The motion nodes read avatar locomotion, not individual bones or PhysBones. [Motion setup](docs/MOTION_AND_HANDLES.md).

For editing, you get live previews, intermediate node previews, search, box selection, copy/paste, operation dropdowns and reusable node groups called **Patterns**. Drag a wire into empty space to add a compatible node. You can drag from inputs too.

Sticker placement has a mesh preview and UV handles. The material playground lets you scrub time and compare A/B snapshots. Texture-set import, material presets and static texture baking are also included. [Workflow guide](docs/CREATOR_WORKFLOW.md).

<details>
<summary>Editor screenshot and controls</summary>

![NXSG running in Unity on Linux](docs/assets/editor-preview.png)

| Action | Control |
| :--- | :--- |
| Connect nodes | Drag between sockets, or click both endpoints |
| Detach a wire | Pull from its connected input |
| Select a group | Drag empty canvas; Shift adds to selection |
| Pan / zoom | Middle-drag / scroll wheel |
| Find a node | Space on the canvas |
| Fit graph / frame selection | Home / F |
| Copy / paste / duplicate | Toolbar or Ctrl+C / Ctrl+V / Ctrl+D |
| Undo / redo | Toolbar buttons; keyboard undo is still unreliable on Linux/Unity |

Failed builds keep the last working shader. Local recovery snapshots live under `Library/NXSG/Recovery`; deleting Library removes them too.

</details>

## A few limitations

- Fur, shells, particles and tessellation can get expensive quickly. The cost warnings are estimates, not GPU measurements.
- Additional pixel lights affect Toon/PBR base surfaces. Fur and shell overlays have more limited lighting.
- Refraction samples the screen. Interiors and subsurface lighting are approximations. Transparency sorting and renderer bounds still need attention.
- AudioLink inputs and animatable material properties are implemented; live AudioLink and VRCFury integration testing is still pending.
- Patterns are editable copies of nodes, rather than linked instances of another graph.

[Fur and parallax](docs/FUR_AND_PARALLAX.md) · [Particles](docs/PARTICLES.md) · [Tessellation](docs/TESSELLATION.md) · [LTCGI](docs/LTCGI.md) · [Compatibility](docs/COMPATIBILITY.md)

## Working on NXSG

The graph model, compiler and Unity editor are separate. `.nxsg` files store the editable graph; ShaderLab/HLSL is generated from it. Unity asset references live in an adapter section.

To open the development project, you'll need Git, Python 3, Unity Hub and Unity **2022.3.22f1**:

```bash
git clone https://github.com/nerdrx/nxsg.git
cd nxsg
python3 scripts/setup-vrchat-fixture.py
```

The script downloads and verifies the pinned VRChat Base/Avatars **3.10.5** packages. Open **DevProject** through Unity Hub afterward. [Development setup](docs/DEVELOPMENT.md).

### Where we're going

Next up is more testing on actual avatars, in VRChat and in stereo, along with fur performance and usability work. A community node SDK, Blender bridge, CLI and web viewer are longer-term plans.

[Design document](NXSG_DESIGN.md) · [Implementation plan](docs/IMPLEMENTATION_PLAN.md)

## Bugs and feedback

If something breaks, [open an issue](https://github.com/nerdrx/nxsg/issues). A small graph that reproduces it, your Unity version, and a screenshot help a lot. Confusing controls count as bugs worth reporting too.

Graphlit, ShaderGraphVRC and Poiyomi are references from the research, not bundled code or designs. [Attribution notes](docs/research/EDITOR_UX_AND_PRIOR_ART.md).

No project-wide open-source license has been selected yet. The source is public, but that doesn't grant an open-source license. [Artwork credits](docs/assets/README.md).
