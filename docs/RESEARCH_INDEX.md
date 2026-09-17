# NXSG research index

Baseline: 2026-09-17. This index maps design ideas to decisions, reports, and
implementation spikes. “Research-backed” means
the cited sources support the direction or identify a constraint. It does not
mean NXSG has been implemented or tested. Gates are in
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

The [decision log](DECISIONS.md) is authoritative when an exploratory report offers several alternatives. The [independent review](research/RESEARCH_REVIEW.md) records the corrections made during synthesis and the remaining validation work.

## Coverage matrix

| Design area | Decision(s) | Research and direction | Next evidence / status |
|---|---|---|---|
| Product language: NXSG, **Threads**, **Weave**, connections, and **Patterns** | D07, D14 | [Editor UX](research/EDITOR_UX_AND_PRIOR_ART.md) supports familiar graph language and progressive disclosure. Weave remains optional internal terminology; Patterns are bounded declarative subgraphs (Fur, Anime Eye, Wetness), not executable plug-ins. | S03 validates discoverability; S08 validates Pattern schemas. Naming is product design, not runtime-verified. |
| Beginner/Advanced views, search-to-add, typed sockets, keyboard access, previews, undo, and stale-preview handling | D06–D08 | UX research selects deterministic local search, shared graph semantics, recoverable editor state, and preview cancellation by revision. [Custom canvas research](research/CUSTOM_CANVAS_ON_UNITY_2022.md) compares our own UI Toolkit canvas with optional GraphView on Unity 2022.3.22f1. | S03: small candidate comparison, then lifecycle, accessibility, 20/200-node fixtures, preview disposal and response evidence. |
| Portable `.nxsg` JSON, stable IDs, resource adapters, migrations, unknown nodes, paste, cycles, and size limits | D02–D04, D14 | [Graph compiler](research/GRAPH_COMPILER_AND_PORTABILITY.md) selects a typed DAG, invariant serialization, semantic/layout hashes, recoverable migrations, inert unknown nodes, bounded expansion, and no code execution on open. | S01–S02: schema fixtures, parser behavior, migration and safety tests pending. |
| Compiler IR, validation, diagnostics, DCE, folding, CSE, stage constraints, and mutable bindings | D04, D05, D10 | Research supports conservative lowering and dead-code/literal folding first. CSE requires semantic, stage, sampler, and mutability identity. Mutable properties must survive optimization even at zero defaults. | S01–S02: contract/tests pending; S04 checks node-linked HLSL errors. Aggressive hoisting, precision reduction, fast math, and approximate noise remain unverified. |
| Built-In toon backend, lighting, shadows, stereo, passes, output rollback, and stable assets | D01, D04, D09 | [Platform](research/PLATFORM_AND_INTEGRATIONS.md) and [rendering](research/RENDERING_FUR_AND_PERFORMANCE.md) define a PC Built-In forward baseline with intentional passes, matching shadow caster, stereo-safe code, and separate mobile policy. | S00/S04/S05: Unity 2022.3.22f1, SDK 3.10.5, Windows DX11, import/compile, Frame Debugger, VR, mirror, fallback, and transaction tests pending. |
| Fur: shells, fins, hybrid, fixed passes, offline expansion, skinning metadata, tangents, masks, gravity, normals, bounds, shadows | D12 | Rendering research rejects “vertex displacement creates copies.” Shells, fins, and geometry are alternatives with different silhouette, overdraw, shadow, mirror, and bounds costs; no universal shell budget is claimed. | S07: paired avatar/rig experiments and target GPU timings pending. Keep plain toon as control; platform policy is a separate gate. |
| Fur motion, wind, movement, scrolling/evolving noise, precision, looping, UV flipbooks, gradients, dissolves, glitches, and vertex animation | D13 | Sampled flow/noise ships before expensive evolving noise. Period, phase, space, seed, displacement bounds, and seam behavior must be explicit; wrapped phase alone does not make arbitrary noise seamless. | S06/S07: full-loop stereo captures, bounds checks, and measured variants pending. No arbitrary avatar script velocity is assumed. |
| LOD, alpha clipping/blending, overdraw, outlines, mirrors, shadows, stereo, static estimates, and GPU timing | D10, D12 | Reports separate pass/shell/sample/variant estimates from live GPU cost. Distance clipping can retain draw setup; blending can hurt depth behavior; mirror/probe views add camera work. | S05/S07: target-device GPU frame-time distributions and worst mirror/shadow cases pending. Do not substitute VRChat static ranks for shader timing. |
| AudioLink, ordinary animatable properties, VRCFury, and parameter mutability | D05, D11 | [Platform research](research/PLATFORM_AND_INTEGRATIONS.md) treats AudioLink as optional texture data with explicit unavailable fallback. VRCFury integration is conventional Material Property naming/workflow; no private API. | S06: installed/absent/runtime-missing AudioLink and renamed/shared-property animation tests pending. Exact client behavior remains an integration gate. |
| Baking static branches, color space, view/time/pose dependence, debug previews, and rebake invalidation | D08, D13 | Bake only frozen, declared inputs; preserve the source graph and record resolution, color space, mesh/UV dependencies, hashes, and triggers. Time, view, pose, world position, lighting, and AudioLink stay live or require an explicit snapshot approximation. | S09: bake fixtures, stale detection, debug output, and mobile mapping pending. No bake is currently validated in Unity. |
| Snippets, pack trust, security, provenance, licenses, and prior-art reuse | D03, D14, D15 | Declarative Patterns are the first extension boundary. Opening/pasting cannot download or execute code; raw HLSL/C# is trusted content. [Validation research](research/VALIDATION_AND_RELEASE.md) requires notices, exact-file license review, deterministic payloads, and release evidence. | S02/S08 plus release review: traversal/cycle/size tests, notices, provenance, and package checks pending. |
| Blender, MaterialX/glTF, CLI, web viewer, additional backends, and future packaging | D02, D16 | Portable IDs and semantics are reserved, but translation must label exact, approximate, baked, or unsupported behavior. Unity compilation and VRChat acceptance cannot be inferred from a CLI or viewer. | Deferred after S09; separate capability fixtures required. Package/release details remain subject to the [risk register](RISK_REGISTER.md). |

## Research status and limits

Research is complete enough to start S00–S01 and then follow the dependency gates for S02–S09. The reports do not establish a
working Unity project, generated shader import, editor canvas,
VR headset result, VRChat upload, AudioLink runtime, VRCFury behavior, fur GPU
budget, or mobile material equivalence. [COMPATIBILITY_SNAPSHOT.json](research/COMPATIBILITY_SNAPSHOT.json)
records the available SDK/package evidence; the fixture is still missing.

The most material unresolved questions are:

1. Can the pinned Unity/VRChat fixture be created and licensed on the required
   Windows DX11 and headset setup, and are the pins still accepted?
2. Does the chosen editor adapter meet lifecycle, accessibility, and large-graph
   responsiveness gates without contaminating the portable model?
3. Which fixed-pass or offline-expanded fur path preserves skinned poses,
   blendshapes, bounds, shadows, mirrors, and stereo within measured budgets?
4. What exact AudioLink and VRCFury property behavior is available in the
   target client, including missing data and shared-material edge cases?
5. Which bake domains and mobile shader mappings preserve enough appearance and
   animation meaning to be useful rather than silently lossy?
6. What package/license/provenance checks are required for community Patterns,
   snippets, generated output, and future adapters?
7. Which measured GPU timings justify quality defaults for the target headset,
   mirror configuration, and avatar population?

These questions are implementation or platform gates, not silently resolved
claims. The root risk register tracks their ownership and escalation.

## Document verification

Checked on 2026-09-17: all internal Markdown/HTML navigation and asset links resolve; 123 distinct linked source URLs responded successfully; the compatibility snapshot parses as JSON; GitHub's Markdown renderer accepts the README's banner, navigation and tables. Link reachability does not independently establish a source's technical claims. The banner was rendered and visually inspected, with its embedded NX wordmark paths compared against the canonical source. No Unity or graphics-runtime validation is claimed by these checks.
