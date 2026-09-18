# NXSG architecture decisions

Research baseline: **2026-09-17**. These decisions guide the first implementation; they do not establish runtime validation. **Selected** means the default direction is settled for planning. **Provisional** means the named spike must pass before committing to the approach. The source-backed reports are linked at each decision.

## D01 — PC avatar backend first · Selected

Develop and test on **Linux**, using Unity **2022.3.22f1** and VRChat Base/Avatars **3.10.5**. Target PC VRChat's Built-In shader contract. The authoring OS, the Linux Editor graphics API, generated target code and the VRChat client's graphics path are separate choices. Record the actual Linux Vulkan/OpenGL path; establish VRChat/VR testing on the available Linux setup, including Proton details if used. A Windows machine is not required for coding or Linux milestones. Native Windows portability is plausible, not verified; add a Windows smoke test later when claiming that environment is supported.

Recheck the official supported editor before creating the fixture. Android/iOS avatars require SDK-permitted shaders; a future mobile material adapter can bake/map compatible values into those shaders. It cannot promise arbitrary NXSG shader execution. Safety fallback, mobile output, and a low-cost PC quality mode are three distinct mechanisms. [Platform evidence](research/PLATFORM_AND_INTEGRATIONS.md)

## D02 — Portable core, one concrete backend · Selected

Use ordinary C# for the graph, validation, deterministic serialization, diagnostics, and initial ShaderLab/HLSL text generation. Keep Unity asset access, material creation, and canvas objects in an editor adapter. Start with a core assembly and editor assembly; a separate backend assembly is justified when ownership or independent testing requires it. Namespaces and clear inputs are sufficient to separate a single backend initially.

Unity 2022.3 uses a C# 9 compiler with documented feature restrictions and supports .NET Standard 2.1. A conservative C# 8 subset is our source policy, not Unity's language ceiling. Confirm the actual assembly in Linux Unity and the standalone .NET runner in S00; these are two runtimes, not two required operating systems. [Unity compiler](https://docs.unity3d.com/2022.3/Documentation/Manual/CSharpCompiler.html), [API profile](https://docs.unity3d.com/2022.3/Documentation/Manual/dotnetProfileSupport.html)

**Serializer choice is provisional:** prefer the Newtonsoft JSON dependency already declared by the SDK (`com.unity.nuget.newtonsoft-json` 3.2.1) over a second serializer. Record the resolved Unity package/assembly identity and a compatible upstream package for the portable host; a rolling documentation page for package 3.2.2 is not proof of the 3.2.1 binary. Compare serialized fixtures on both hosts in S00/S02. Do not add a general serializer interface unless a second implementation is actually needed. [Compiler research](research/GRAPH_COMPILER_AND_PORTABILITY.md), [SDK package snapshot](research/COMPATIBILITY_SNAPSHOT.json)

## D03 — `.nxsg` JSON is the source of truth · Selected

Store schema/node versions, stable identities, typed connections, parameters, resource references, dependency versions, and optional layout. Persist Unity GUID/subasset mapping in an adapter section; never require Unity objects to read the core graph. Separate semantic hashing from layout hashing so moving a node does not recompile a shader. Canonical saves use invariant numeric formatting, stable order, and finite numeric values.

Migrations create a recoverable original and a change report. Unknown nodes stay visible and retain their original payload; they block affected compilation. Paste allocates fresh instance IDs and resolves parameter conflicts explicitly. Expansion rejects Pattern cycles and enforces size limits. No pack download or code execution follows from merely opening or pasting a graph. [Compiler research](research/GRAPH_COMPILER_AND_PORTABILITY.md)

Initial S02 safety defaults, subject to measured adjustment: 4 MiB input JSON, depth 64, 4,096 authored nodes, 16,384 connections, 1,024 resource references, 32 nested Pattern levels, 32,768 nodes after expansion, and 8 MiB generated shader source. These are parser/compiler safety caps, not claims of interactive performance. Inspect counts before expanding/allocating; reject nonfinite values, duplicate IDs, resource path escapes, and unexpected operation IDs. Bound fan-out by the edge cap and variant multiplication by an initial 256 NXSG-controlled combinations per pass; separately enumerate and report additional backend/Unity variants. Each limit error names the input, actual value, limit, and responsible node/path. Cancellation must be checked during long traversals. S02 tests limits at, below, and above each boundary.

## D04 — Small typed DAG, explicit semantics · Selected

MVP supports float/vector math, explicit color/data interpretation, compile-time booleans, UV0, Texture2D sampling, and a toon surface output. Normals and positions have named spaces. Start with exact socket matching plus explicitly documented scalar splats; do not silently reinterpret color as a normal or convert object coordinates to world coordinates. Defer a general closure algebra, matrices, arbitrary shader stages, and a wide integer type system.

An output describes a surface and required pass behavior; it is not merely an RGB value. Clip/displacement/lighting must reach every relevant pass. Stage constraints and parameter mutability belong in the model from the beginning, even while the optimizer is conservative. [Compiler research](research/GRAPH_COMPILER_AND_PORTABILITY.md), [rendering research](research/RENDERING_FUR_AND_PERFORMANCE.md)

## D05 — Mutable means mutable · Selected

Constants may specialize. Material properties, animated properties, globals, and AudioLink values remain live even if their current defaults are zero. “Animated” is useful authoring metadata; an ordinary exposed material property is also mutable. Preserve exported property declarations and bindings separately from calculation liveness. Built-in names used for fallback compatibility must retain matching semantics; other properties use a stable NXSG namespace.

Keep graph-level binding kind distinct from Unity ownership metadata: material asset/instance identity, renderer/material-slot usage, renderer-wide animation scope, or a documented global provider. Preview uses temporary material instances. Never silently edit shared material assets, and warn when a renderer-wide animation matches several materials. Global inputs must not accidentally become ShaderLab material properties that override the global value; validate symbol collisions. Arbitrary global writers are not assumed available on VRChat avatars. [Global property behavior](https://docs.unity3d.com/cn/2022.3/ScriptReference/Shader.SetGlobalFloat.html), [shared material behavior](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-sharedMaterial.html)

Offer static switches only for immutable build choices. Do not expose a shader keyword as though it were an ordinary float animation. A future locking feature requires an explicit, reversible contract; it is unnecessary for the first compiler. [Compiler binding rules](research/GRAPH_COMPILER_AND_PORTABILITY.md), [VRCFury behavior](research/PLATFORM_AND_INTEGRATIONS.md)

## D06 — Own the canvas contract; compare native implementations · Provisional, S03

A custom node canvas is a first-class candidate. Compare a small canvas built from Unity 2022.3 UI Toolkit primitives with an Experimental.GraphView adapter on **2022.3.22f1**. GraphView is an optional accelerator, not a required dependency. No Unity 6 Graph Toolkit or later-only APIs enter the baseline. Use the same 20/200-node fixture, interactions and reload checks to measure implementation effort, responsiveness, accessibility and maintenance exposure. Prefer the custom canvas if it meets those requirements with manageable code; the spike determines the choice.

Either implementation adapts to the same graph commands. Canvas objects never define `.nxsg`; the editor maintains recoverable state, a dirty indicator, undo transactions and external-change detection. Replacing the canvas must not redesign the graph or compiler. [Editor evidence](research/EDITOR_UX_AND_PRIOR_ART.md), [custom canvas research](research/CUSTOM_CANVAS_ON_UNITY_2022.md)

Linux is where the custom UI is built and used daily. Prefer Unity UI Toolkit drawing, events and clipboard APIs over platform-specific integrations; keep asset/include path casing exact, normalize serialized separators and check DPI, focus and pointer capture on the Linux desktop. This exercises the primary platform directly while preserving a straightforward later Windows port.

## D07 — Deterministic local search and progressive disclosure · Selected

Names, curated aliases, socket compatibility, and stage/target constraints are enough for initial intent-aware search. Basic and Advanced modes share the same graph. High-level effects expose compact controls; real Patterns can expand into editable subgraphs. Atomic built-in nodes need not pretend they are Patterns. Keyboard access and non-color-only socket labels are core requirements. [Editor research](research/EDITOR_UX_AND_PRIOR_ART.md)

## D08 — Shared preview semantics, bounded work · Selected

Preview and production compilation share the graph lowering and effect implementation. Temporary preview lighting/mesh/time are labeled inputs. Recompile on structural changes, update properties directly when safe, cancel obsolete requests logically by revision, and pause hidden previews. Never change the user's output connection merely to preview an intermediate value. Cache state is expendable; source state and the last successful build are not. [Editor research](research/EDITOR_UX_AND_PRIOR_ART.md)

## D09 — Explicit local build with stable assets · Provisional, S04

Import `.nxsg` as an editable source asset; initially generate final `.shader`/`.mat` through the Build action. Stage and compile output before promoting it, preserve existing output `.meta` identities, and retain material values/texture assignments on rebuild. Capture and restore the previous output if a later Unity import fails. The exact transaction strategy must be tested across editor reload/crash boundaries.

Build for VRChat prepares and validates local assets. Uploading, signing into VRChat, and avatar publication remain separate user actions. [Editor lifecycle](research/EDITOR_UX_AND_PRIOR_ART.md), [validation and release](research/VALIDATION_AND_RELEASE.md)

Build-state contract: `snapshot revision → validate → stage → compile staged output → promote files → validate final import → commit`, with `restore previous output` on failure. A small recovery journal records source revision/hash, output paths, prior hashes/identities and completed promotion steps. A changed source revision cancels stale publication. `.nxsg` import may parse, validate, and index; it never publishes shaders/materials or writes the source again. Imported output callbacks may report results, never start another build. Per-file filesystem replacement is not a multi-file Unity transaction. S04 must inject failure before/after promotion and recover after domain reload.

An error-free import is insufficient: Unity initially performs only minimal shader processing and compiles variants when needed. S04 must explicitly cause and record compilation of the required target variants through a controlled render/build path, then check diagnostics. [Unity shader compilation](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-compilation.html)

## D10 — Optimization follows equivalence evidence · Selected

Start with dead-code elimination and literal constant folding. Add pure-expression CSE after semantic/stage/sampler identity is represented. Preserve clip and displacement behavior in applicable passes. Defer automatic stage hoisting, precision reduction, approximate noise, fast math, and aggressive pass specialization until paired image tests define acceptable changes. A static operation count is not a GPU cost model; texture sample count must be qualified by stage, active branch, pass, and loop count. [Compiler research](research/GRAPH_COMPILER_AND_PORTABILITY.md)

Optimizer fixtures must separately assert exported property declarations/bindings, used mutable values, per-pass clip/displacement dependencies, and sampler/derivative/interpolation identity. An exported but unused declaration need not keep an unrelated computation alive. Check generated text and property layouts before adding paired render comparisons.

Reuse the platform shader compiler for low-level optimization. NXSG's own passes should primarily remove unused graph branches/resources/passes and specialize explicit build choices; do not build a second general HLSL optimizer. Unity's compilation pipeline and Microsoft compiler optimization flags provide the downstream context. [Unity shader compilation](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-compilation.html), [HLSL compiler options](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/d3dcompile-constants)

## D11 — AudioLink optional; VRCFury property compatibility first · Selected

Graphs without AudioLink build without its package or include. NXSG's current implementation uses a standalone shader-text contract: `_AudioTexture` is sampled at the official AudioLink layout, with `_NXSG_AudioLinkPreview` and `_NXSG_AudioLinkValue` for deterministic preview and an explicit fallback when data is unavailable. It does not install or include the AudioLink package. Runtime provider and live music behavior remain unvalidated. Initial VRCFury integration consists of conventional properties and a documented Material Property workflow. No private reflection or unreleased API dependency. Remember renderer-wide property animation can affect multiple materials sharing the same property name. [AudioLink implementation](research/AUDIOLINK_IMPLEMENTATION.md) · [Integration research](research/PLATFORM_AND_INTEGRATIONS.md)

## D12 — Fur is a measured follow-on · Provisional, S07

Prove the base toon material before adding shell rendering. Prototype fixed repeated passes and/or offline expanded skinned geometry as explicit alternatives; vertex displacement alone cannot duplicate geometry. Preserve bone weights, bind poses, blendshapes, UVs, bounds, and shadow coverage when a geometry path needs them. Geometry shaders, fins, and hybrid quality tiers follow evidence, not assumed superiority.

Distance or mirror clipping may reduce fragment work while retaining submissions; call that out. No fixed universal shell budget, no automatic claim of physical motion, and no arbitrary avatar script dependency. [Rendering research](research/RENDERING_FUR_AND_PERFORMANCE.md)

Cost reports separate declared ShaderLab passes, shell layers, estimated submissions per view, shadow submissions, extra mirror/camera views, expanded vertices and visible overdraw. These are different quantities; only an actual capture can establish the frame's draw count and GPU cost.

## D13 — Animation and baking preserve meaning · Selected

Ship UV scroll and sampled noise before expensive evolving noise. Declare period, phase, space, and seed behavior; a wrapped phase alone does not make a nonperiodic function seamless. Bake only a branch with explicit frozen inputs and an appropriate domain. Time-, view-, pose-, world-position-, or AudioLink-dependent content requires a deliberate snapshot approximation or a live path. Record texture color space, resolution, mesh/UV dependency, and rebake triggers. [Rendering research](research/RENDERING_FUR_AND_PERFORMANCE.md)

## D14 — Declarative extensions first · Selected

First shared extensions are bounded JSON Patterns over known node operations. Pack schemas declare types, version, capability needs, and provenance. Raw HLSL or editor C# is trusted executable content, not a safe declaration. Do not introduce arbitrary package loading, a marketplace, or a signing service to deliver the MVP. Official compiled nodes may use the same metadata contract without accepting untrusted code. [Compiler research](research/GRAPH_COMPILER_AND_PORTABILITY.md)

## D15 — Prior art informs; reuse is a separate decision · Selected

No Graphlit, ShaderGraphVRC, or Poiyomi code is bundled. A root MIT badge does not settle Unity-specific source or sample licenses. Review the exact files before adopting any code, retain notices, and keep Unity-restricted implementation out of a supposedly portable core. The project license remains a release decision. [Revision and license review](research/EDITOR_UX_AND_PRIOR_ART.md)

## D16 — Future adapters are capability-limited · Selected

Reserve portable resource IDs and node semantics now. Defer Blender, CLI, web viewer, MaterialX/glTF adapters, and additional rendering backends. A future CLI can emit shader text independently; that does not prove Unity compilation or VRChat acceptance. A Blender bridge must distinguish exact translation, approximate substitution, baked output, and unsupported behavior. Round-tripping requires preserving original semantics and identities, not just matching node names. [Portability research](research/GRAPH_COMPILER_AND_PORTABILITY.md)
