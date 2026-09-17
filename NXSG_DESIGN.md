# NXSG — NX Shader Graph

> A friendly, VRChat focused visual shader authoring tool: simple materials should take simple graphs, while advanced creators can still reach the underlying controls.

**Status:** design proposal. This document describes intended behavior, not shipped features.

**Research update — 2026-09-17:** [Architecture decisions](docs/DECISIONS.md) and the [implementation plan](docs/IMPLEMENTATION_PLAN.md) refine this product vision. Start with PC VRChat, preserve mutable properties during optimization, and verify editor/graphics behavior before expanding scope. See the [research index](docs/RESEARCH_INDEX.md) for source-backed detail and unresolved gates.

**Authoring environment:** Linux is the primary development and test platform. PC VRChat is the shader target, not a requirement to develop on Windows. Keep Windows portability in the design; native Windows verification can follow later.

## Product direction

NXSG helps avatar creators build and animate materials in Unity without writing HLSL or navigating a giant panel of unrelated toggles. Its editor should feel as approachable as Blender's shader nodes: connect a texture to a toon surface, add an effect, preview it on a mesh, and choose **Build for VRChat**. The build produces a Unity shader and material containing only the features the graph uses.

The initial audience is VRChat avatar creators, especially furry avatar creators. The initial target is PC VRChat's Unity Built-In rendering pipeline and HLSL. Mobile avatars need a separate adapter to VRChat-permitted SDK shaders; reducing the cost of a custom shader does not make it allowed there. NXSG should remain useful for ordinary toon materials; fur is a first-class capability rather than a requirement.

Graphlit and ShaderGraphVRC establish useful prior art for visual shader authoring in this space. Poiyomi provides context for feature-rich avatar shaders and stripping unused features. NXSG's proposed distinction is the combination of Blender-like discoverability, avatar-oriented high-level nodes, fur and animation workflows, editable portable graphs, and visible performance costs. These are product goals, not claims that existing tools lack every individual feature.

## Identity and vocabulary

- **NXSG** is the product; **NX Shader Graph** is its expanded name. `.nxsg` is the editable graph file, separate from generated shader output.
- **Nodes** are operations and effects. **Threads** can describe connections in guides or visual language, while the UI should also use the familiar word “connections.”
- **Patterns** are reusable node groups. **Weave** may name the internal graph/compiler engine or library, without obscuring the NXSG product name.

## Creator experience

1. Start from a small template such as `Texture → Toon Surface → Output`.
2. Search to add nodes, or drag a connection into empty space to search for compatible nodes. Fuzzy search should understand intent and aliases: “moving fur,” “audio glow,” or “scrolling texture” should surface relevant nodes and Patterns.
3. Adjust a high-level node directly; expand it or replace it with lower-level math, masks, coordinates, and lighting nodes when needed. **Beginner** and **Advanced** are views of the same capable graph, not incompatible file types.
4. Inspect live node outputs, any connection's intermediate value, and the resulting material on representative avatar meshes. Preview errors should point to the responsible node and socket.
5. Expose a value as a stable, named material property for avatar animation. Build for VRChat should validate the graph, show actionable warnings, then generate the required shader and material.

Graphs should support undo/redo, clear typed sockets, useful defaults, frames/comments, reusable Patterns, and copy/paste of self-contained snippets for sharing. Pasted graphs must identify missing assets or node packs instead of silently changing behavior.

## Architecture

```text
Unity editor ─┐
Blender adapter (future) ─┼─> portable .nxsg graph ─> validator / typed IR
CLI / web viewer (future) ─┘                           │
                                      optimization + cost analysis
                                                       │
                                        backend: VRChat Built-In / HLSL
                                                       │
                                        Unity shader + material + report
```

Keep the **editor**, **graph model**, **compiler**, and **backend** separate. The editor edits a graph; it does not define the source of truth. Compilation validates node and socket types, resolves parameters and resources, lowers nodes into a typed intermediate representation, optimizes it, and emits target-specific code. Generated HLSL is an output artifact; reopening a project must recover the editable graph from `.nxsg`.

### Portable `.nxsg` graph

The format should store graph and schema versions, stable node and socket identifiers, typed values, connections, parameters, referenced assets, Patterns, extension requirements, editor layout, and target hints. Core graph meaning must not depend on serialized Unity objects. Unity-only references belong in a clearly identified adapter section; portable assets need resolvable identifiers or an explicit missing-asset state. Keep stable IDs and deterministic serialization so diffs, snippets, and migrations are reviewable.

Version the graph schema and each node definition. On load, migrate known older versions explicitly, preserve unknown data where feasible, and report unsupported nodes or lossy migrations before saving. A newer graph should fail clearly on an older installation rather than compile to a different look without warning.

### Nodes and extension model

A node contract describes typed inputs and outputs, defaults, parameter exposure, editor metadata, preview behavior, target capabilities, cost estimates, and a compiler implementation. Core and official extension nodes should use the same contract as community packs. Installing a pack should not require patching NXSG itself.

Community content needs a trust boundary. Prefer declarative graph Patterns and constrained node definitions for shareable packs. Arbitrary editor code or raw HLSL requires an explicit trusted installation path, clear provenance, and reviewable permissions. Missing or untrusted packs should leave visible placeholders and block affected builds. A Pattern packages a graph section behind a small interface; examples might include Fur, Anime Eye, Wetness, and Hologram.

### Parameter model

Represent a value once, with a binding mode: **constant**, **material property**, **animated material property**, **global**, or **AudioLink**. An exposed parameter has a stable generated name, type, default, range, and animation guidance. This keeps avatar animations and VRCFury workflows predictable. VRCFury compatibility should center on ordinary, cleanly named animatable shader properties and material variants; deeper integration can later automate setup where its APIs and supported workflows permit it. Avoid requiring a custom runtime graph on the avatar.

## Core capabilities

### Surface, inputs, and previews

The baseline node set covers textures, colors, UVs, math, masks, gradients, time, normals, toon lighting, shadows, specular, rim, matcap, emission, and Output. Geometry-aware inputs include UV sets, vertex colors, normals, tangents, object/world position, view direction, and relevant bone or movement signals where the target provides them. Inputs should state their coordinate space and whether they work in vertex or fragment stages.

Preview a node or connection without rewiring the final material. Offer texture, value, normal, mask, and lighting views, plus material preview on a sphere and avatar-relevant meshes. Shader diagnostics should map generated-code errors back to graph nodes when possible.

### Fur and furry materials

A high-level **Fur** Pattern should expose length, density, direction/gravity, masking, color layers, strand or shell breakup, and rim-lit fluff. Explore shell, fins, and hybrid rendering as separate techniques with measured costs and target constraints. Let creators drive fur direction and length from textures, vertex data, procedural fields, and animation. Optional wind, motion response, and noise displacement should have stable controls and a low-cost fallback. Distance-based LOD and simpler mirror/VR modes should be deliberate quality options, with visible appearance changes and cost estimates.

Other avatar-oriented Patterns may cover paw pads, anisotropic highlights, wet fur, eye effects, iridescence, glitter, and emission patterns. Ship them only when a small graph cannot express the same result clearly.

### Animation and AudioLink

Animation nodes should cover UV scrolling and transforms, flipbooks, emission pulses, hue and gradient changes, dissolves, glitches, vertex motion, and procedural noise. Noise modes should include scrolling, time-evolving or 4D-like variation, directional flow, AudioLink response, and parameter-driven speed, phase, scale, direction, and strength. Expensive procedural noise needs cheap alternatives such as sampled or baked noise; the UI should explain the visual and performance tradeoff. AudioLink values should behave like graph inputs and feed any compatible effect rather than require a special shader family.

## Build and performance

**Build for VRChat** validates target support, required packs and assets, property naming, pass count, and known expensive constructs. It then emits a shader/material, concise build report, and warnings tied to specific nodes. An invalid graph should not overwrite the last known good build.

Optimization should include dead-node elimination, constant folding, common-subexpression elimination, static switches for genuinely fixed choices, merged operations, vertex-stage placement when interpolation preserves the intended look, and specialized passes only for used features. Variant growth and compile time are also costs: avoid multiplying variants for every exposed option. The compiler should explain important transformations in its report, especially when they alter precision or visual output.

Show estimated texture samples, passes, procedural iterations, fur shells, variants, and likely hot spots on the graph. Separate **estimates** from measured profiling; report hardware, scene, and quality settings for any measured result. Offer measured quality budgets for desktop VR, with warnings for expensive choices and hard validation errors for unsupported targets. Creators should be able to bake an expensive static branch to a texture, preserve its source graph, and understand when rebaking is required. Keep a cheap PC quality mode for expensive effects, distinguish it from VRChat's safety fallback, and treat mobile shader/material conversion as a separate workflow.

## Roadmap

| Phase | Deliverable | Exit criterion |
| --- | --- | --- |
| **MVP** | Unity editor, portable graph schema, typed sockets, search-to-add, Texture/Color/Math/UV/Toon/Output nodes, material preview, Built-In/HLSL build | `Texture → Toon → Output` produces an editable, working VRChat material with useful validation errors. |
| **Avatar workflow** | Stable animatable properties, emission, rim/matcap, basic animation and AudioLink nodes, snippet sharing, initial cost report | A creator can build and animate a material without editing generated HLSL. |
| **Fur and extensions** | Measured fur approaches, masks/direction/LOD, cheaper fallbacks, Patterns, versioned node pack contract, baking and richer profiling | Fur looks and costs are compared on representative avatar meshes and VR targets. |
| **Portable tooling** | Blender adapter, CLI compiler, web viewer, additional backends where justified | Supported Blender nodes round-trip through `.nxsg` with explicit conversion limits. |

The Blender bridge is a later adapter: translate supported Blender nodes to `.nxsg`, flag unsupported Cycles or render-specific behavior, and suggest substitutes or baking. Importing into Unity should retain the editable graph. True round-tripping and multiple backends should follow only after the graph schema and first backend have proven stable.

## Design rules

- Start with a small, reliable graph and expand from actual avatar use cases.
- Keep the graph editable and independent of generated code or Unity serialization.
- Make simple tasks short; expose detail when creators ask for it.
- Show visual and performance consequences near the choice that causes them.
- Treat community content, migrations, and unsupported targets as explicit, visible states.
- Validate appearance and frame cost on representative avatars before calling an optimization or fur technique successful.

## Implementation evidence

The design above describes the intended product. See [current validation](docs/VALIDATION.md) and [development setup](docs/DEVELOPMENT.md) for the first Linux implementation slice and remaining limits.
