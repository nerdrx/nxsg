<p align="center">
  <img src="docs/assets/nxsg-banner.svg" width="100%" alt="NXSG — NX Shader Graph. Make your avatar impossible to ignore." />
</p>

<h3 align="center">Fluffy. Glowy. Holographic. Unmistakably yours.</h3>

<p align="center">
  A visual shader playground for VRChat creators.<br />
  Build materials from connected nodes, see your changes, and keep experimenting.
</p>

<p align="center">
  <img alt="136 visible nodes" src="https://img.shields.io/badge/nodes-136-7700FF?style=flat-square" />
  <img alt="Unity development version 2022.3.22f1" src="https://img.shields.io/badge/Unity-2022.3.22f1-222222?style=flat-square" />
  <img alt="Tested on Linux" src="https://img.shields.io/badge/tested_on-Linux-222222?style=flat-square" />
  <img alt="Early development" src="https://img.shields.io/badge/status-early_development-7700FF?style=flat-square" />
</p>

<p align="center">
  <a href="https://nerdrx.github.io/nxsg/"><strong>Install with VPM</strong></a> ·
  <a href="#try-nxsg">Try NXSG</a> ·
  <a href="docs/NODES.md">Explore the nodes</a> ·
  <a href="Packages/dev.nerdrx.nxsg/Samples~/README.md">Example graphs</a> ·
  <a href="docs/GOODIES.md">Latest goodies</a> ·
  <a href="#where-were-going">Roadmap</a>
</p>

## Your avatar deserves a little extra

Rim-lit fluff. Shifting pearl colors. A hologram floating above your skin. Particles spilling from the mesh. Music-driven emission. A tiny adjustment that becomes an entirely new look.

**NXSG — NX Shader Graph — puts those ingredients on one canvas.** Connect textures, masks, animation and surfaces to create your own material. Start with a simple toon shader; keep adding personality as you learn.

**Furry-first. Linux-developed. Built for experimentation.**

**Current status (2026-09-19):** Linux Unity 2022.3.22f1/OpenGLCore editor
and rendering checks are the supported evidence baseline. Windows/D3D, headset
stereo, and live VRChat client behavior remain unverified; see the
[compatibility record](docs/COMPATIBILITY.md).

> **Available to try today:** a working Unity editor package targeting PC Built-In shaders. Rendering and editor checks run on Linux with Unity 2022.3.22f1. This is early development: Windows/D3D, headset stereo and VRChat client acceptance are still unverified. [What has been tested →](docs/GOODIES.md#verification)

## Pick your kind of extra

| Make it… | Your ingredients |
| :--- | :--- |
| **Fluffy** | Fur shells and optional fins, root/tip colors, grooming, masks, wind and shell LOD. |
| **Shiny** | Toon, Unlit and PBR surfaces; iridescence, matcaps, rim glow and stylized subsurface lighting. |
| **Deep** | Parallax and parallax occlusion, virtual interiors, screen refraction and real tessellated displacement. |
| **Layered** | Nested shells, stickers, hologram scanlines, emission, dissolve and triangle wireframes. |
| **Alive** | Shader-driven particles from the mesh wearing the material, UV motion, flipbooks, vertex animation and AudioLink inputs. |
| **Procedural** | 1D–4D noise, Musgrave-style fractals, Voronoi, waves, texture bombing, distortion, gradients and shape masks. |

**136 visible nodes.** Compact controls for common effects; reusable Patterns when your graph grows. [Full node guide →](docs/NODES.md)

<table>
  <tr>
    <td align="center" width="33%"><img src="docs/assets/hologram-study.png" alt="Violet and cyan scanlines floating over dark PBR sphere and capsule" /><br /><strong>Layered hologram</strong></td>
    <td align="center" width="33%"><img src="docs/assets/pearl-study.png" alt="Soft iridescent pearl PBR finish on a sphere and capsule" /><br /><strong>Pearl finish</strong></td>
    <td align="center" width="33%"><img src="docs/assets/fur-study.png" alt="Dense warm short fur on a sphere and capsule" /><br /><strong>Short plush fur</strong></td>
  </tr>
</table>

**[Watch the hologram animate →](https://nerdrx.github.io/nxsg/#material-studies)** · [Open the example graphs](Packages/dev.nerdrx.nxsg/Samples~/README.md)

<sub>Actual Linux Unity GPU renders of included, texture-free graphs. Simple mesh studies, not avatar/client validation. Fur uses 24 shell layers; measure its cost on your target. The top banner is a concept illustration.</sub>

## A canvas that lets you keep playing

- **Pull a wire, find an idea.** Drag from either socket direction. Drop into empty space to search compatible nodes, or drop a node onto a wire to insert it.
- **See what you are changing.** Live material previews, intermediate output inspection, and up to four optional node thumbnails. Edit Color Ramp gradients right on the card.
- **Expose your own controls.** Author Float/Color parameters with stable shader references for material animation.
- **Stay in the flow.** Box selection, copy/paste, duplicate, category search, operation dropdowns, automatic number/color types and wires that fade between those types.
- **Make the scene catch up.** Auto scene saves and builds after a short pause. Build for VRChat applies edits immediately; failed builds preserve the last working shader.
- **Experiment with a way back.** Undo/Redo buttons, manual checkpoints and local recovery snapshots. Turn a useful selection into a reusable Pattern.

<details>
<summary><strong>See the editor and learn the controls</strong></summary>

![NXSG custom graph editor running in Unity on Linux](docs/assets/editor-preview.png)

*Current Linux editor capture: compact menus, resizable sidebar, separate node browser, diagnostics and material parameters.*

| Action | Control |
| :--- | :--- |
| Connect nodes | Drag between sockets, or click both endpoints |
| Detach a wire | Pull from its connected input |
| Select a group | Drag empty canvas; Shift adds to selection |
| Pan / zoom | Middle-drag / scroll wheel |
| Find a node | Space on the canvas |
| Fit graph / frame selection | Home / F |
| Copy / paste / duplicate | Toolbar or Ctrl+C / Ctrl+V / Ctrl+D |
| Undo / redo | Toolbar buttons; keyboard undo remains a known Linux/Unity gap |

Recovery files live under `Library/NXSG/Recovery`; clearing Library clears those snapshots. Keep normal project backups.

</details>

## Try NXSG

### Install through your package manager

**[Add NXSG to VCC / get the VPM listing →](https://nerdrx.github.io/nxsg/)**

Enable **Show Pre-Release Packages**, then add **NX Shader Graph** to a test project. On Linux, add the same listing URL to your VPM-compatible manager:

```text
https://nerdrx.github.io/nxsg/index.json
```

[Installation help](docs/VPM.md) · [Download the alpha package](https://github.com/nerdrx/nxsg/releases)

### Work from source

You need **Git, Python 3, Unity Hub and Unity 2022.3.22f1**. Start with the included development project.

```bash
git clone https://github.com/nerdrx/nxsg.git
cd nxsg
python3 scripts/setup-vrchat-fixture.py
```

The setup script downloads and verifies the pinned VRChat Base/Avatars **3.10.5** packages.

1. Open **DevProject** through Unity Hub using **2022.3.22f1**.
2. Choose **Tools → NXSG → Open Graph Editor**.
3. Open a graph from **Assets/NXSGExamples**, or choose **New**.
4. Save inside **Assets**, then click **Build for VRChat**.
5. Assign the generated material from **Assets/NXSGGenerated** to a mesh—and start tweaking.

**Build for VRChat generates a local shader and material. It does not upload an avatar.** Auto scene is enabled by default and applies subsequent shader edits after roughly 0.65 seconds of inactivity; switch it off for manual saves/builds.

[First material walkthrough](docs/QUICK_START.md) · [Compatibility table](docs/COMPATIBILITY.md) · [Linux setup and troubleshooting](docs/DEVELOPMENT.md) · [Particle quick start](docs/PARTICLES.md) · [Fur and parallax](docs/FUR_AND_PARALLAX.md) · [Tessellation](docs/TESSELLATION.md)

### Start with a ready-made idea

| Open this example | Then make it yours |
| :--- | :--- |
| **Groomed Fur / Fur Fins** | Tune strand colors, direction, wind and silhouette detail. |
| **Shiny Surface** | Play with iridescence and stylized light scattering. |
| **Refraction Glass / Interior Bomb** | Bend the background or explore texture-driven virtual depth. |
| **Neon Wireframe** | Turn the mesh's own triangles into glowing detail. |
| **Audio Hologram** | Explore AudioLink inputs and layered holographic shading. |
| **Noise Color Ramp** | Turn a procedural signal into your own palette. |

[Browse all example graphs →](Packages/dev.nerdrx.nxsg/Samples~/README.md)

## Made to grow with your ideas

The editor, graph model and compiler are separate. **`.nxsg` stores an editable, versioned graph independent of Unity**, with Unity asset references kept in an adapter. Generated ShaderLab/HLSL is the build output; your graph remains the source.

Today that means portable graph data, deterministic serialization, stable generated materials and reusable graph snippets. Longer term, it makes room for new editors, tools and backends.

### Where we're going

- **Make the avatar workflow dependable:** client/stereo validation, representative avatars, performance measurements and live AudioLink/VRCFury integration checks.
- **Take fluff further:** better fur quality, measured cost and more useful fallbacks.
- **Open the toolbox:** a community node SDK and packs, with clear versioning and trust boundaries.
- **Create in more places:** texture baking, a Blender bridge, CLI tools, a web viewer and additional backends.

These are roadmap items, not promised features of the current package. [Full design](NXSG_DESIGN.md) · [Implementation plan](docs/IMPLEMENTATION_PLAN.md)

<details>
<summary><strong>Compatibility, limits and engineering notes</strong></summary>

- **Target:** PC Built-In forward rendering. Mobile/Quest avatars are not supported by this custom-shader package.
- **Test environment:** Linux Unity 2022.3.22f1/OpenGLCore. Native Windows/D3D and headset/client rendering need separate checks.
- **Lighting:** ForwardBase only; additional per-pixel point/spot lights are not accumulated (no ForwardAdd). The compiler reports this limitation.
- **Integrations:** AudioLink shader inputs and conventional animatable material properties are implemented. Live AudioLink and VRCFury acceptance remain open.
- **Effects:** refraction samples the screen; interiors, iridescence and subsurface are approximations. Fur fins use triangle edges without mesh adjacency. Transparent ordering and expanded renderer bounds matter for shells, fur and particles.
- **Performance:** cost warnings help expose expensive operations; they are not a measured GPU budget. Tessellation, overdraw and many shell passes can become expensive quickly.
- **Patterns:** editable copies of grouped nodes, not linked external instances.
- **Verification:** portable graph tests, hidden Unity GPU renders, build rollback checks and editor interaction checks. Client validation is a separate milestone.

[Validation record](docs/VALIDATION.md) · [Latest feature checks](docs/GOODIES.md#verification) · [Research](docs/RESEARCH_INDEX.md) · [Decisions](docs/DECISIONS.md) · [Risk register](docs/RISK_REGISTER.md)

</details>

## Help shape the playground

Try a graph. Find a confusing control. Show what you made. A small reproducible example of a broken connection or unexpected render is especially useful while NXSG is taking shape.

[Report a bug or suggest an idea](https://github.com/nerdrx/nxsg/issues) · **Star the repo to keep it on your radar.**

---

<sub>Graphlit, ShaderGraphVRC and Poiyomi are prior-art context, not bundled designs or code. See the [research and attribution notes](docs/research/EDITOR_UX_AND_PRIOR_ART.md). No project-wide open-source license has been selected yet; public source availability does not itself grant an open-source license.</sub>

<p align="center"><strong>NXSG · Make something only you would make.</strong><br /><a href="docs/assets/README.md">Artwork and provenance</a></p>
