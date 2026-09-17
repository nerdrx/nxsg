<p align="center">
  <img src="docs/assets/nxsg-banner.svg" width="100%" alt="NXSG — NX Shader Graph. Visual materials for VRChat. Design and research phase." />
</p>

<p align="center">
  <strong>Visual shader authoring for avatar creators. Furry materials included.</strong>
</p>

<p align="center">
  <a href="NXSG_DESIGN.md">Design</a> ·
  <a href="docs/RESEARCH_INDEX.md">Research</a> ·
  <a href="docs/DECISIONS.md">Decisions</a> ·
  <a href="docs/IMPLEMENTATION_PLAN.md">Roadmap</a> ·
  <a href="docs/RISK_REGISTER.md">Known risks</a>
</p>

> **Design + research · 2026-09-17**  
> The specification and research are ready. The editor, compiler, and shaders are planned; there is no installable package yet. The banner is a concept illustration.

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

**Simple graphs first.** A reliable toon surface comes before the larger library: animated noise, flipbooks, dissolve, fur masks and direction, shells/fins, wind, LOD, and cheaper quality modes.

## Built around the actual target

| Boundary | Research baseline |
| :--- | :--- |
| Unity | **2022.3.22f1** — the VRChat-supported patch |
| VRChat SDK | **Base + Avatars 3.10.5** |
| First renderer | **PC · Built-In forward · Windows DX11** |
| Graph format | Portable, versioned **`.nxsg`**; Unity asset references live in an adapter |
| Editor canvas | Custom UI Toolkit canvas and GraphView evaluated on the pinned editor |
| Integrations | Optional AudioLink; ordinary animatable properties for VRCFury |

Mobile avatars require VRChat-permitted SDK shaders. A future mobile adapter can map or bake supported material inputs; a cheaper custom PC shader is not automatically allowed. [Compatibility details and sources →](docs/research/PLATFORM_AND_INTEGRATIONS.md)

## Research before implementation

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

| Stage | Deliverable | Status |
| :--- | :--- | :--- |
| **01 · Foundation** | Pinned test project, portable graph, canvas comparison, toon shader | Next |
| **02 · Avatar workflow** | Live previews, stable builds, animation, AudioLink, VRCFury compatibility | Planned |
| **03 · Fur + Patterns** | Measured fur techniques, direction/masks/LOD, reusable declarative groups | Planned |
| **04 · More places to create** | Baking, mobile material mapping, Blender bridge, CLI, viewer, other backends | Later |

Unity imports, actual shader-variant compilation, Windows rendering, SDK validation, and headset measurements are separate gates. Research and a pretty diagram do not replace those checks.

---

**Context, with credit.** Graphlit, ShaderGraphVRC, and Poiyomi are prior art; their code and designs are not bundled. The [research](docs/research/EDITOR_UX_AND_PRIOR_ART.md) records exact revisions and licensing considerations. The project license remains a software-release decision.

<p align="center">
  Part of NX · <a href="docs/assets/README.md">Brand artwork and provenance</a>
</p>
