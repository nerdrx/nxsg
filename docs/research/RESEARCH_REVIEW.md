# NXSG research review

Final review pass: **2026-09-17**. This bounded check covers
`NXSG_DESIGN.md`, `docs/DECISIONS.md`, `docs/IMPLEMENTATION_PLAN.md`,
`docs/RISK_REGISTER.md`, `docs/RESEARCH_INDEX.md`, and the five research
reports. It separates documented design fixes from implementation evidence.
No editor, compiler, Unity import, shader render, headset, or VRChat test has
run in this workspace.

**Subsequent scope clarification:** D06/S03 now treat our own UI Toolkit canvas as a first-class candidate, with GraphView optional. The [Unity 2022.3 canvas report](CUSTOM_CANVAS_ON_UNITY_2022.md) supports a small comparison on 2022.3.22f1; it does not claim either implementation has run. **Platform clarification:** Linux is the primary authoring, Unity-editor, and test platform; Windows is not required for coding, Linux milestones, or package release. VRChat’s Windows-first VCC guidance and `<DX11>` requirement remain factual context for the PC client contract. Native Windows and Proton routes are optional, separately labeled evidence.

## Fixes now addressed in the documents

- **C# and serializer wording:** D02 now states the verified Unity 2022.3
  Roslyn/C# 9 compiler and .NET Standard 2.1 API profile, while C# 8 is an
  intentional conservative Core source policy. Newtonsoft `3.2.1` is clearly
  the SDK-declared dependency, and rolling Unity package `3.2.2` documentation
  is no longer treated as proof of that binary’s compatibility.
- **Graph safety:** D03 now gives initial parser/compiler caps: 4 MiB input,
  depth 64, 4,096 authored nodes, 16,384 edges, 1,024 resources, 32 Pattern
  levels, 32,768 expanded nodes, 8 MiB generated source, and 256 NXSG variant
  combinations per pass. It also specifies nonfinite, path, ID, cancellation,
  and limit error behavior.
- **Mutability and ownership:** D05 separates portable binding kind from Unity
  material/renderer ownership, uses temporary preview materials, warns on
  renderer-wide collisions, and prevents global/material symbol confusion.
  This matches the documented behavior of [`Shader.SetGlobalFloat`](https://docs.unity3d.com/cn/2022.3/ScriptReference/Shader.SetGlobalFloat.html)
  and [`Renderer.sharedMaterial`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-sharedMaterial.html).
- **Build transaction:** D09 now defines revisioned snapshot, validation,
  staging, compile, promotion, final-import validation, commit, recovery
  journal, stale-publication cancellation, and the rule that `.nxsg` import
  never publishes generated assets.
- **Optimizer liveness:** D10 now distinguishes exported property
  declarations/bindings from executable liveness: preserve the declaration and
  binding; keep computation live only when used. This resolves the prior
  ambiguous “all exported properties remain live” wording.
- **Fur accounting:** D12 now separates ShaderLab passes, shell layers,
  estimated submissions, shadow submissions, mirror views, expanded vertices,
  and visible overdraw. The review’s earlier proposed counter split is adopted.

The new implementation plan and risk register consistently map these choices
to S00–S09, owners, and pending evidence. The remaining work is empirical
closure, not a research contradiction.

## Remaining actionable gates

### P1 — Assembly and JSON compatibility still needs S00/S02

The primary Unity sources verify C# 9 and .NET Standard 2.1, but they do not
verify NXSG’s actual Core assembly, Unity asmdef, or Newtonsoft assembly mix.
Unity’s rolling `com.unity.nuget.newtonsoft-json@3.2` page currently identifies
package 3.2.2 and Newtonsoft.Json 13.0.2 ([package documentation](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html));
the project snapshot records SDK 3.10.5 declaring 3.2.1. D02 correctly marks
this provisional. S00/S02 must compile the pinned Unity fixture and standalone
`netstandard2.1` host, record assembly identities, and compare JSON golden
fixtures. This is a planned compatibility spike, not a current research
blocker. Do not claim the portable host works because local .NET 8/9 runtimes
exist.

### Resolved — MVP never implicitly locks a material

`GRAPH_COMPILER_AND_PORTABILITY.md` §3 now explicitly states that MVP never
folds material, animated, global, or AudioLink bindings. Locking is a future,
explicit and reversible feature, unavailable in MVP and never inferred from
defaults. This resolves the final wording ambiguity identified by the review.

### P1 — Execute the already-defined mutation and transaction fixtures

The design now covers shared materials, renderer-wide animation, global inputs,
preview isolation, importer publication, stale revisions, and recovery. S04/S06
still need the concrete fixtures: one renderer with two material slots, one
shared material asset, a global property collision, external source edit during
build, failure before and after promotion, domain reload, and failed shader
import. These tests establish that the design is safe in Unity; source reading
cannot establish that behavior.

### P1 — Validate optimizer and pass counters against generated output

S02/S04/S06 must assert that used mutable computations, per-pass clip and
displacement dependencies, sampler/derivative/interpolation identity, property
layouts, and variant counts survive optimization. An unused exported declaration
must remain in the output manifest without keeping unrelated arithmetic alive.
S07 must compare D12’s fur counters with Frame Debugger output and actual
generated pass structure. Any GPU timing or shell savings remain unverified.

## No remaining major contradiction found

The documents now agree on Unity 2022.3.22f1, PC Built-In/DX11 client contract first, portable
`.nxsg` JSON, a small typed DAG, explicit color/space semantics, mutable
bindings, declarative extensions, staged output publication, and measurement-
gated fur. Platform policy is kept separate from shader compilation, and the
implementation plan explicitly defers Blender, CLI, web, and other backends.

One wording issue remains above (locked-material folding); it is a clarification
before implementation rather than an architectural conflict. No empirical
rendering evidence exists for toon lighting, stereo, mirrors, fur, AudioLink,
VRCFury, or mobile material mapping.

## Source and evidence boundary

Primary sources were checked 2026-09-17. Unity compiler and .NET claims use
versioned 2022.3 documentation: [C# compiler](https://docs.unity.cn/2022.3/Documentation/Manual/CSharpCompiler.html)
and [.NET profile support](https://docs.unity3d.com/2022.3/Documentation/Manual/dotnetProfileSupport.html).
Unity package 3.2 documentation is rolling and is explicitly not the SDK’s
3.2.1 binary lock. Assembly/JSON compatibility, importer recovery, and all
rendering claims remain gated by the implementation plan’s fixtures.
