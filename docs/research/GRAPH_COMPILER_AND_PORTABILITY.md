# NXSG graph compiler and portability research

**Scope.** This report covers the portable graph, compiler, Unity Built-In/HLSL
backend, extension boundary, and later Blender/CLI/web possibilities described
in [`NXSG_DESIGN.md`](../../NXSG_DESIGN.md). It deliberately treats
the product brief as proposed behavior. “Verified” means supported by a primary
source; “recommended” is an NXSG design choice; “unresolved” needs a spike or
an explicit product decision.

## 1. Architecture: a small portable core, adapters around it

**Verified.** Unity Assembly Definitions can restrict an assembly to selected
platforms, and an Editor-only assembly is made by including only the Editor
platform. Unity also supports GUID-based assembly references so an asset rename
does not require changing every reference ([Unity Assembly Definitions, 2022.3
LTS Manual](https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html),
section “Creating an assembly for Editor code”). ShaderLab `Pass` blocks define
GPU state and shader programs, and separate passes can represent different
render states or `LightMode` behavior
([Unity ShaderLab Pass reference, 2022.3
LTS Manual](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-Pass.html),
section “Overview”).

**Recommended.** Implement the graph model, parser, validator, typed IR,
optimizer, diagnostics, deterministic serializer, and a pure ShaderLab/HLSL
emitter in ordinary C# with no `UnityEngine` or `UnityEditor` references. Target
the oldest .NET/C# surface that the supported Unity versions can load; keep
reflection and file watching out of the core. The Unity package then has three
logical pieces: `NXSG.Core` (portable), `NXSG.ShaderLabBackend` (portable code
emitter), and `NXSG.Editor` (asset database, inspectors, previews, build UI,
`Shader`/`Material` asset creation). The CLI can reference Core plus the pure
backend; only the Unity Editor adapter creates or updates Unity material assets.
A web viewer references a read-only graph parser and layout data. This uses
Unity’s actual assembly boundary instead of inventing a large “platform
abstraction” around every Unity API.

The editor is a client of the graph API, never its source of truth. The source
of truth is `.nxsg`; generated `.shader`, `.mat`, reports and previews are
rebuildable outputs. Unity asset GUID/path resolution belongs in an adapter
section of the graph or a project manifest. A portable graph must remain
loadable when Unity is absent, with unresolved resources represented explicitly.

**Recommended baseline.** Pin the first Unity compatibility test to
**Unity 2022.3.22f1** and use the versioned 2022.3 documentation for core
constraints. The exact C# language level and package assembly layout still need
an API spike. The same Core DLL and pure emitter should be exercised in a
standalone .NET test and that pinned Unity Editor; later Unity versions are
compatibility checks, not the MVP baseline.

## 2. `.nxsg`: typed DAG with stable identity

**Recommended minimum document.** Use JSON with an explicit schema version and
canonical ordering:

```json
{
  "format": "nxsg",
  "schema": "1.0",
  "graphId": "stable-id",
  "nodes": [], "connections": [], "parameters": [],
  "resources": [], "patterns": [], "extensions": [],
  "layout": {}, "targetHints": {}, "dependencyLock": []
}
```

Every node, socket, parameter, pattern interface, connection and resource
reference gets a stable ID. Node identity is semantic identity, not array index;
moving a node or reserializing JSON must not change it. Keep `typeId`,
`definitionVersion`, `displayName`, `properties`, and an opaque `unknown`/extra
object. Connections reference `fromNode/fromSocket` and `toNode/toSocket`, not
names that can be localized. Canonical serialization sorts maps and unordered
collections, uses fixed numeric formatting, and retains explicit ordering where
order has meaning. This makes reviewable diffs, reproducible hashes and stable
copy/paste possible.

The executable graph is a directed acyclic graph. The loader must reject a
cycle with a path showing the offending edge. Pattern/group recursion is a
separate feature: permit a Pattern to call another Pattern only after expansion
has a bounded recursion depth and a cycle diagnostic; never let recursive group
expansion silently become an infinite compiler walk. A group’s public interface
has stable socket IDs and declared types, so paste can preserve links even when
internal layout changes.

Each socket should carry at least:

* MVP value kind and shape: bool constants, numeric scalar and `vec2/3/4`
  values, explicit color values, `texture2D`, and a `Surface` output. Treat
  normal and mask values as explicitly annotated numeric values rather than
  inventing a large type zoo. Matrices, integer families, general shader
  closures, texture arrays/cubes and arbitrary target resources are later
  capabilities, not MVP socket types;
* numeric precision (`f16`, `f32`, or backend default), color encoding/working
  space (linear, sRGB, data), coordinate space (UV/object/world/view/tangent),
  and evaluation stage/frequency (constant, per-material, per-draw, vertex,
  fragment, audio/frame varying);
* interpolation and derivative requirements, texture sampling policy, and
  whether the value is a shader input or a plain data value.

Do not equate “color” with `float3`: color has transfer-function semantics, while
normal and mask data should not receive color conversion. Do not equate a vector
with a coordinate: object-space position and tangent-space direction need
different transforms. Implicit conversions should be a small, versioned table
(scalar splat, vector truncation/extension, color/data conversion only when
declared), materialized as explicit IR conversion nodes. Ambiguous conversions,
lossy precision changes, and coordinate-space changes should be warnings or
errors with a fix suggestion. A graph must never gain a hidden sRGB conversion
because a UI label said “Color.”

Assets should use a project-relative canonical URI plus importer/type metadata,
content hash, optional Unity GUID, and the original display path. Hash the
resolved bytes and relevant import settings; a path alone is not a dependency
identity. `dependencyLock` records node-pack IDs, definition versions, backend
version, and asset hashes used for a build. Missing assets remain visible
placeholders and block a release build unless the node explicitly defines a
documented fallback.

Known schema migrations should be explicit, one version at a time, and produce
a migration report. Preserve unknown fields and unknown node payloads on load and
save. An unknown node may be displayed and copied, but cannot compile; bypassing
it or substituting a constant must be an explicit user action recorded in the
report. Older software should reject a newer schema before saving. Snippets
should carry a mini dependency lock and namespace remapping table; generated
IDs must be deterministic for a paste operation but never collide with existing
IDs. These choices are recommendations, not claims that the current proposal
already defines them.

## 3. Parameters and evaluation semantics

Represent one parameter value with a binding mode: `constant`, `material`,
`animatedMaterial`, `global`, or `audioLink`. Store type, default, range, stable
property name, color/space metadata, and a versioned animation hint. `material`
means uniform per material instance; `global` means supplied by the runtime and
may change between draws; `animatedMaterial` is intentionally time-varying;
`audioLink` is frame-varying external input. A parameter’s binding mode is part
of IR identity.

Constant folding is legal only for literals and expressions proven independent
of all runtime bindings, texture contents, derivatives, and side effects. Never
fold an animated, global, or AudioLink input merely because its preview value is
currently zero. The MVP never folds material, animated, global, or AudioLink
bindings. A future explicit, reversible locking feature could specialize a
material only after proving the relevant values cannot change; this is not
available in the MVP and must never be inferred from defaults. Preserve the original binding in the report when baking a
branch. This prevents the common failure where a static-looking preview turns a
VRChat animation property into a dead constant.

## 4. IR and optimizer correctness

Lower the graph to a typed, stage-aware IR before backend code generation. Each
IR value carries source node/socket IDs, precision, color/coordinate spaces,
evaluation frequency, derivative/texture constraints, and effect flags.

* **DCE:** trace from pass outputs, explicit exported/bound parameters,
  diagnostics requested by the user, and explicit preview outputs. Treat
  material declarations as metadata until they are explicitly exported or bound
  to the generated shader; a declaration alone is not a computational root.
  Pure texture reads are removable when their result is dead. Derivative and
  explicit-LOD dependencies must be represented in the live expression’s IR
  semantics, so DCE preserves them when needed without pretending that sampling
  itself has a side effect. Keep only genuine effectful nodes such as discard,
  clip, ordering-sensitive operations, or backend-declared side effects.
* **CSE:** merge only pure expressions with equal type, precision, space,
  stage/frequency and sampler state. Do not merge across different texture
  filtering/LOD policies or across effect boundaries.
* **Static branches:** specialize only on immutable build constants. Runtime
  material, global, animated and AudioLink choices must remain a uniform/dynamic
  branch or a deliberately declared variant. Unity documents that `shader_feature`
  variants can be stripped based on build materials, while `multi_compile` keeps
  runtime combinations; dynamic branching creates no variants but can cost GPU
  time ([Unity shader keywords, 2022.3 LTS Manual](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-keywords.html),
  sections on definition type and stage-specific keywords). Therefore NXSG must label each switch as `specialize`,
  `variant`, or `dynamic`, and show variant multiplication before build.
* **Stage placement:** moving work from fragment to vertex is legal only when
  interpolation preserves the intended result. HLSL vertex outputs are normally
  interpolated, and `nointerpolation`, `noperspective`, `centroid` and `sample`
  alter that behavior ([Microsoft HLSL struct/interpolation reference](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-struct)).
  `ddx`/`ddy` are pixel-shader-only operations ([Microsoft `ddx` reference](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-ddx)).
  Texture sampling moved to the vertex stage also changes available derivatives
  and therefore filtering/LOD. Mark nodes as vertex-safe, fragment-only,
  derivative-dependent or interpolation-sensitive; reject unsafe hoists.
* **Precision and color:** an optimization that lowers precision or moves a
  conversion must be visible in the build report and covered by image-diff
  tolerances. Color conversions happen at typed boundaries, never as a generic
  numeric simplification.

**Unresolved.** Exact floating-point equivalence policy, fast-math allowance,
half precision targets, and whether texture sampling nodes may carry explicit
LOD/gradient inputs need backend spikes. Start conservative: no fast math and no
cross-stage texture movement until paired renders prove an acceptable error.

## 5. ShaderLab/HLSL emission and diagnostics

The Built-In backend should build a small, deterministic ShaderLab envelope:
properties and stable names; one forward/base pass first; then only requested
shadow, depth, meta or additive passes; fixed tags/render state; generated HLSL
programs and includes; the Unity Editor adapter then creates the material asset
and report. Pass construction should be
data-driven from the IR’s required capabilities, not from a monolithic template
with many disabled branches. Unity’s Pass reference confirms that Passes carry
render setup and shader code blocks; the backend must still spike exact Built-In
tags, stereo macros, shadow semantics and VRChat constraints in a real Editor.

Emit a source map from generated ranges to graph node/socket IDs. HLSL’s `#line`
directive deliberately exists for program generators and makes compiler errors
refer to original source locations ([Microsoft `#line` reference](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-appendix-pre-line),
updated 2019-10-24). Put stable synthetic filenames and line markers around each
node implementation; parse Unity compiler output back through the map. A
diagnostic should include severity, node/socket, generated filename/line, likely
fix, and whether it came from validation, lowering, Unity compilation or a target
limit. Never overwrite the last known-good build on failure.

Preview and build must share Core validation, lowering and the same backend
implementation. A preview may use a deliberately marked preview mesh or CPU
fallback, but it must not silently replace unsupported target behavior. Add a
parity mode that renders the same generated shader/material in an offscreen
Unity scene and compares it with the editor preview within documented tolerances.

## 6. Node SDK and trust boundary

The node contract should be declarative first: typed ports, defaults, metadata,
capabilities, cost estimates, preview recipe and a portable Pattern graph.
Official nodes may provide trusted compiler implementations. A community pack
containing only declarative graphs can be inspected, copied and compiled without
executing code. Raw HLSL, custom C# editor code, native plugins, file/network
access, and process execution are optional future trusted capabilities, with
clear provenance, permission display and a “not portable” capability. NXSG does
not need a mandatory signed-package ecosystem for the inert declaration MVP;
the trust boundary is that executable extensions are never run by the CLI or
web viewer unless a later explicit trust mechanism is enabled.

Do not claim that loading arbitrary C# or HLSL is sandboxed; it is not a safe
security boundary in a Unity editor. Untrusted packs should load as inert unknown
nodes and block builds that depend on them. The compiler should never execute
pack code in the CLI or web viewer. A first SDK spike can support only JSON
declarations and Pattern expansion, then add trusted backend implementations.

## 7. Blender, MaterialX and glTF: context, not the MVP schema

Blender’s current manual describes shader nodes with distinct value, vector,
color and shader socket classes, and states that its shader output describes
lighting interaction ([Blender 5.2 node introduction](https://docs.blender.org/manual/en/latest/render/shader_nodes/introduction.html)).
The Principled BSDF is a broad physically based node, while NXSG’s Toon Surface
is a target-specific stylized model. A Principled-to-Toon import can map base
color, roughness/specular and emission approximately, but it cannot promise
lighting-equivalent roundtrip. Blender’s Noise Texture has 1D–4D dimensions and
detail/roughness/lacunarity/distortion controls ([Blender Noise Texture](https://docs.blender.org/manual/en/4.0/render/shader_nodes/textures/noise.html));
the same name does not prove the same algorithm, seed, interpolation or output
range in HLSL. Import should preserve the source node as metadata and mark a
translated substitute or baked texture.

Color management is a hard boundary: Blender’s working space affects shader and
compositing results and conversions can require manual fixes
([Blender color spaces, 5.2](https://docs.blender.org/manual/es/latest/render/color_management/color_spaces.html)).
UDIM is a tiled image set using tokens such as `<UDIM>` and starts at tile 1001
([Blender UDIM manual](https://docs.blender.org/manual/id/dev/modeling/meshes/uv/workflows/udims.html));
the first Unity Built-In backend should either import a resolved tile set or
produce an explicit unsupported-resource diagnostic. Blender procedural noise,
Cycles-only nodes, render-engine light paths, geometry displacement, arbitrary
drivers, AOVs and view transforms cannot be genuine roundtripped semantics.

MaterialX is useful context, not a reason to adopt its full file format. Its
current specification is v1.39 (dated March 15, 2025), defines typed node graphs
as DAGs, and supports target-specific implementations ([MaterialX 1.39 specification](https://github.com/AcademySoftwareFoundation/MaterialX/blob/main/documents/Specification/MaterialX.Specification.md)).
glTF 2.0 explicitly says it is not an authoring format and uses a declarative
metallic-roughness PBR material model; its core supports static 2D textures
([glTF 2.0 specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html),
lines 619–624 and 1735–1739; texture/image sections describe static 2D inputs).
Thus MaterialX can inform typed graphs, colors and target selection, while glTF
is a material/asset interchange target. Neither captures NXSG’s VRChat animation,
AudioLink, fur-shell passes, Unity property naming or editable stylized intent.
The minimum useful format is the small `.nxsg` contract above plus explicit
import/export adapters; adopting either large standard as the source of truth
would add semantics NXSG cannot guarantee.

## 8. Later tooling and concrete spikes

The CLI should validate, migrate, compile and emit a report without opening
Unity. A web viewer should parse only portable JSON, render layout/metadata and
show unknown nodes; it must never execute C# or raw HLSL. The CLI may emit pure
ShaderLab/HLSL, but Unity material asset creation remains an Editor adapter.
Additional backends
should implement the same typed IR and declare capability matrices rather than
adding backend conditionals to Core.

Minimum acceptance spikes:

1. **Portable determinism:** serialize/parse/serialize a fixture on .NET and
   Unity; bytes and dependency hash match. Reorder JSON object keys and node
   layout, then confirm graph meaning and IDs remain unchanged.
2. **Graph safety:** reject direct and Pattern-recursive cycles; reject wrong
   socket spaces/types; insert and display explicit approved conversions; retain
   unknown nodes through save/load.
3. **Binding safety:** animate a property and vary AudioLink/global inputs in a
   paired render; prove optimizer output changes every frame and contains no
   folded literal for those values.
4. **Optimizer equivalence:** compare unoptimized and optimized shaders across
   vertex/fragment-sensitive, derivative, explicit-LOD, color-space and precision
   fixtures. Fail on disallowed pixel error or silhouette change.
5. **Unity backend:** build `Texture → Toon Surface → Output` in the minimum
   supported Unity Editor, render an avatar-like mesh in stereo, and assert
   generated properties, pass count, shader compilation and material assignment.
6. **Diagnostics:** inject a failing node implementation and verify Unity’s
   error maps to the `.nxsg` node/socket using `#line`; confirm failed builds
   preserve the last known-good output.
7. **Preview parity:** render the same graph through preview and generated
   ShaderLab in an offscreen scene; record tolerances, shader keywords, samples,
   pass count and backend version.
8. **Blender import limits:** import Principled, color-managed image, 4D noise,
   UDIM and one Cycles-only fixture; each either maps with a report or remains a
   visible unsupported/baked node. No “successful” silent approximation.

## Source and version ledger

Accessed 2026-09-17. Primary sources used: Unity 2022.3 LTS Manual pages for
assembly boundaries, Pass construction, and keyword variant/dynamic-branch
semantics; the project’s pinned Editor target is Unity 2022.3.22f1. Unity’s
versioned pages are preferred for implementation constraints; any unversioned
Unity links should be treated as rolling documentation. Microsoft Learn HLSL
references (`ddx`, interpolation, `#line`) are linked with their page update
dates where supplied; these are language references rather than Unity-version
claims. Blender 5.2 LTS Manual plus the Noise/UDIM pages, MaterialX
Specification v1.39 (dated March 15, 2025), and the Khronos glTF 2.0 registry
specification (registry copy dated 2023-07-29) provide the interchange context.
Unity version support, VRChat shader restrictions, AudioLink runtime behavior,
and exact Blender exporter behavior remain version-sensitive and must be
rechecked in implementation spikes.
