<p align="center">
  <img src="docs/assets/nxsg-banner.svg" width="100%" alt="NXSG — NX Shader Graph. Visual materials for VRChat. Early Linux prototype." />
</p>

<p align="center">
  <strong>Visual shader authoring for avatar creators. Furry-first by design.</strong>
</p>

<p align="center">
  <a href="docs/DEVELOPMENT.md">Try it</a> ·
  <a href="docs/VALIDATION.md">Validation</a> ·
  <a href="NXSG_DESIGN.md">Design</a> ·
  <a href="docs/RESEARCH_INDEX.md">Research</a> ·
  <a href="docs/DECISIONS.md">Decisions</a> ·
  <a href="docs/IMPLEMENTATION_PLAN.md">Roadmap</a> ·
  <a href="docs/RISK_REGISTER.md">Known risks</a>
</p>

> **Early prototype · Linux tested · 2026-09-18**
> The custom graph editor and Built-In compiler run in Unity **2022.3.22f1**. Real Linux rendering and build-recovery checks pass. This is a development package; VRChat client/headset acceptance remain separate validation gates. The banner is a concept illustration.

## Try NXSG

1. Clone the repository and run `python3 scripts/setup-vrchat-fixture.py` to prepare the isolated SDK fixture.
2. Open **DevProject** in Unity Hub with **2022.3.22f1**.
3. Choose **Tools → NXSG → Open Graph Editor**, then **New**.
4. Save the graph inside `Assets`. **Auto scene** saves and builds edits after a short pause; **Build for VRChat** applies them immediately.
5. Find its material under `Assets/NXSGGenerated` and assign it to a mesh.

The custom canvas has search/add, click-to-connect ports, drag, pan/zoom, undo/redo and save. Drag a connected input to detach its wire and reconnect it elsewhere; Undo restores the connection. Live preview shows edits in a temporary material. **Auto scene** is enabled by default: after 0.65 seconds without another shader edit, it saves and builds the graph, updating materials that use its generated shader. The persistent scene status shows pending updates, successful builds, or errors; a failed build keeps the last working shader. Disable Auto scene to save/build manually. Moving nodes does not trigger a shader build. Builds never upload an avatar. [Linux setup and checks](docs/DEVELOPMENT.md)

![NXSG running in Unity on Linux](docs/assets/editor-preview.png)

*Actual prototype, including its built-material preview. Undo/Redo buttons are the supported editing controls; keyboard undo remains a known Unity/Linux gap.*

**Verified so far:** bounded graph parsing, deterministic serialization and semantic hashes, mutable color expressions, generated-symbol collision checks, opaque toon rendering, stable output GUIDs, material tint/texture preservation, and rollback after two injected failures. [Evidence and remaining limits](docs/VALIDATION.md)

## From a graph to your avatar

NXSG aims to make material creation feel direct: add a texture, shape the lighting, preview it on your mesh, expose a value for animation, and **Build for VRChat**.

```text
Texture ───── Toon Surface ───── Output
                   ▲
             Color / Rim / Emission
```

| Create | Preview | Refine |
| :--- | :--- | :--- |
| Search by name or intent. Start with compact effect nodes; open reusable Patterns when you need detail. | Inspect intermediate values and the material on a mesh. Keep the last good preview while editing. | Animate clean properties, add AudioLink, and understand the cost of passes, noise, and fur. |

**89 visible nodes to build with.** New: shape masks, spirals, rays, brick/honeycomb patterns, triplanar/matcap textures, mesh/view inputs, height/slope/distance masks, and actual triangle wireframes. 1D–4D Noise, Musgrave, Voronoi, Checkerboard and Waves; selectable UV channels and coordinate spaces. Seven distortion modes, Gradient, UV Tile/Mirror and Posterize. Same-mesh GPU surface particles; Toon, Unlit, PBR and particle surfaces; particle color, AudioLink, color ramps, rim glow, masked layers, stickers, dissolve, flipbooks, UV distortion, vertex motion and up to eight nested transparent shells. Fold reusable Patterns and preview intermediate values. Stacked fur, fins, LOD and baking remain future work.

## Built around the actual target

| Boundary | Research baseline |
| :--- | :--- |
| Unity | **2022.3.22f1** — the VRChat-supported patch |
| VRChat SDK | **Base + Avatars 3.10.5** |
| Development and testing | **Linux** — native Unity Editor and local checks |
| Shader target | **PC VRChat · Built-In forward**; validate the client graphics path separately |
| Graph format | Portable, versioned **`.nxsg`**; Unity asset references live in an adapter |
| Editor canvas | Custom UI Toolkit prototype running on the pinned editor; full S03 comparison remains open |
| Integrations | Standalone AudioLink shader contract and conventional material properties; live provider/VRCFury validation remains open |

Mobile avatars require VRChat-permitted SDK shaders. A future mobile adapter can map or bake supported material inputs; a cheaper custom PC shader is not automatically allowed. [Compatibility details and sources →](docs/research/PLATFORM_AND_INTEGRATIONS.md)

## A research-backed roadmap

Three Luna researchers worked in parallel, followed by a consistency review and central synthesis. The record includes **16 architecture decisions**, **25 tracked risks**, source revisions, and explicit validation gates.

| Read | Find |
| :--- | :--- |
| [Project design](NXSG_DESIGN.md) | Naming, creator workflow, features, and long-term direction |
| [Research index](docs/RESEARCH_INDEX.md) | Feature coverage and primary-source reports |
| [Decision log](docs/DECISIONS.md) | Selected approaches and provisional choices |
| [Implementation plan](docs/IMPLEMENTATION_PLAN.md) | Dependencies, parallel work lanes, and acceptance criteria |
| [Risk register](docs/RISK_REGISTER.md) | Failure modes, ownership, and evidence needed to close them |
| [Independent review](docs/research/RESEARCH_REVIEW.md) | Corrections made and remaining empirical checks |

## Path to a first release

Live material preview, effect nodes, standalone AudioLink shader text, and flat editable Patterns are available in the current package. Live AudioLink music, fur quality, and VRChat client acceptance remain unverified.

| Stage | Deliverable | Status |
| :--- | :--- | :--- |
| **01 · Foundation** | Pinned fixture, portable graph, custom canvas, toon shader | First working slice; remaining gates open |
| **02 · Avatar workflow** | Live previews, stable builds, animation, AudioLink, VRCFury compatibility | Effect nodes and standalone AudioLink contract available; runtime gates open |
| **03 · Fur + Patterns** | Measured fur techniques, direction/masks/LOD, reusable declarative groups | Flat editable Pattern groups available; measured fur gates open |
| **04 · More places to create** | Baking, mobile material mapping, Blender bridge, CLI, viewer, other backends | Later |

Linux is our primary authoring and validation environment. Record the Linux Editor graphics API and, when testing VRChat through Proton, the client and translation-layer versions. A Windows machine is not a prerequisite for development or Linux milestones. Native Windows compatibility is expected from the portable design but remains unverified until a future smoke test. Unity imports, shader compilation, SDK validation and headset measurements still establish different things.

---

**Context, with credit.** Graphlit, ShaderGraphVRC, and Poiyomi are prior art; their code and designs are not bundled. The [research](docs/research/EDITOR_UX_AND_PRIOR_ART.md) records exact revisions and licensing considerations. The project license remains a software-release decision.

<p align="center">
  Part of NX · <a href="docs/assets/README.md">Brand artwork and provenance</a>
</p>

## Canvas controls

- Drag between matching colored sockets in either direction, or click each endpoint.
- Drop a wire onto empty canvas to add a compatible node with its connection.
- Drag empty canvas to box-select; hold Shift to add. Drag a selected title to move the group.
- Selected nodes have a light outline. Delete removes the selection; toolbar Undo restores it.
- Copy/Paste/Duplicate buttons and Ctrl+C/Ctrl+V/Ctrl+D preserve selected nodes and internal connections. Pasted nodes receive fresh IDs; Duplicate leaves the clipboard unchanged.
- Middle-drag pans, the wheel zooms, and Escape cancels the current connection or selection.

### More nodes

**New: [Neon Wireframe](Packages/dev.nerdrx.nxsg/Samples~/Neon%20Wireframe.nxsg)** — open the example in Unity to get glowing mesh edges immediately. Connected-node menus now include search and grouped socket choices; typed values can exceed slider ranges. Surface Particles has an emission rate and automatic high-count tessellation, with a 10,000/sec/triangle request exercised on Linux.


There are now **89 visible nodes**, plus hidden Parameter and Preview Vector helpers. The pack includes PBR, Unlit and particle surfaces, particle color, Fresnel, color ramps, layers, stickers, dissolve, flipbooks, UV distortion, vertex motion, AudioLink, normal mapping, and shell rendering. Multiple reachable textures receive separate sampler properties. PBR uses Built-In main-light/ambient/reflection-probe lighting in one generated pass; nested shells add up to eight transparent normal-offset mesh passes and warn about cost, bounds and sorting. Patterns are flat editable copies, not linked instances. See the [node guide](docs/NODES.md), [particle quick path](docs/PARTICLES.md), and [Particle Sparkles example](Packages/dev.nerdrx.nxsg/Samples~/Particle%20Sparkles.nxsg).
