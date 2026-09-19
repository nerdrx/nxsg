# Risk register

Research baseline: **2026-09-17 (historical)**. The table remains the source
of risk ownership and closure criteria; later validation records supersede its
initial unknowns when they name passing evidence. A risk closes only when its
named evidence exists. Severity is the consequence if ignored: **P0** can
invalidate the target or lose user data; **P1** can cause incorrect output,
major performance problems, or expensive rework; **P2** affects quality or
future scope.

| ID | Severity | Failure mode and trigger | Mitigation / closing evidence | Gate and owner |
| --- | --- | --- | --- | --- |
| R01 | P0 | A cheap custom PC shader is marketed as mobile-avatar compatible. | Separate SDK-permitted mobile material generation from PC shader compilation; target validation must reject forbidden shaders. | S00/S09, integration |
| R02 | P0 | Wrong Unity patch or drifting SDK/dependencies creates broken uploads or compilation. | Pin editor, Base/Avatars and integration tuple; record archive hashes and installed lock files; recheck official support before upgrades. | S00, integration |
| R03 | P1 | Authoring OS, shader compilation, offscreen preview and actual client behavior are confused. | Validate Linux first; record Editor API and Proton/client path if used; distinguish SDK/client/two-eye evidence. Native Windows smoke is later and only gates verified Windows claims. | S05, validation |
| R04 | P1 | Experimental GraphView breaks core workflows, or a custom canvas becomes an oversized editor framework. | Compare both small adapters on 2022.3.22f1; measure interactions/accessibility/code owned; isolate graph model and retain recoverable dirty state. | S03, editor |
| R05 | P0 | Saving, migration, regeneration, or an external edit loses graph/material data. | Recoverable original, explicit conflict handling, stable `.meta`, staged compile/promotion, failure recovery and material-value preservation tests. | S02/S04, core + editor |
| R06 | P1 | Optimizer changes output by folding mutable defaults, merging incompatible expressions, or hoisting nonlinear/derivative work. | Mutability and stage/type/space/sampler identity in IR; conservative passes; compare independently expected and unoptimized results across time and poses. | S02/S04/S06, compiler |
| R07 | P1 | Property rename or optimization silently breaks animation. | Stable shader reference names independent of display labels; exported bindings tracked separately from computational liveness; clip/VRCFury tests after rebuild. | S06, integration |
| R08 | P1 | A property animation unexpectedly affects several material slots, or a shared material edit changes other avatars. | Document renderer-wide property behavior; show shared material usage; avoid mutating shared assets without an explicit build/assignment action; test multi-slot fixtures. | S04/S06, editor + integration |
| R09 | P0 | An imported graph/pack executes code, escapes output paths, or exhausts compiler resources. | Known declarative operations; bounded bytes/nodes/depth/expansion; safe resource paths; inert unknowns; no automatic downloads. Trust code at installation, before Unity loads it. | S02/S08, core |
| R10 | P1 | Color and shadow passes disagree, stereo setup is missing, or a fallback silently changes surface behavior. | One defined surface contract; matching clip/displacement; deliberate tags, pass requirements and fallback semantics; per-eye/mirror/shadow captures. | S04/S05, rendering |
| R11 | P1 | Fur cannot produce shells through the assumed stage, or expanded geometry breaks skinning/blendshapes. | Compare repeated passes and offline expansion; preserve rig/mesh metadata; reject unsupported generation paths; test animated poses and blendshapes. | S07, rendering |
| R12 | P1 | Fur vanishes at frustum edges, scales incorrectly, or grooms slip during movement. | Bound displacement, test skinned bounds, mirrored/nonuniform transforms, tangent handedness, UV seams and direction space. | S07, rendering |
| R13 | P1 | “LOD optimization” clips pixels but retains costly shell submissions, shadows or mirrors. | Count actual draws/passes/vertices and measure GPU distributions with LOD, mirror and shadow variants; label savings by the work removed. | S07, rendering |
| R14 | P1 | Noise loops jump, time loses precision, eyes disagree, or motion assumes unavailable runtime data. | Explicit period/phase/space; seam and long-time tests; periodic noise construction; documented avatar inputs or labeled synthetic motion; no arbitrary avatar MonoBehaviour dependency. | S06/S07, rendering |
| R15 | P1 | AudioLink include is missing at build time, or data is absent at runtime. | Keep package requirement and runtime availability separate; compile no include for unused features; known API version and neutral/user-selected fallback for active nodes. | S06, integration |
| R16 | P1 | VRCFury integration depends on private classes or an unmerged proposal. | Conventional animatable properties first; pin published release; use documented APIs only when deeper integration becomes necessary. | S06, integration |
| R17 | P1 | Keyword combinations create excessive build time, stripped required variants, or shader bloat. | Explicit static/dynamic/variant choices; no keywords for ordinary animations; bound and report variant multiplication; inspect built artifact. | S04/S06, compiler |
| R18 | P1 | Preview compiles freeze Unity or leak render textures, and stale results overwrite new ones. | Debounce, revision checks, bounded selected/visible previews, cleanup, main-thread Unity APIs; large-graph lifecycle/performance fixture. | S03, editor |
| R19 | P1 | Preview looks correct while generated material uses different lighting, sampling or effects. | Shared lowering/implementation; explicit synthetic preview inputs; paired renders with matching time/mesh/color settings. | S03/S04, rendering + editor |
| R20 | P1 | Bake hides dynamic/view/pose dependencies or stores masks with incorrect color handling. | Check bake domain, freeze inputs explicitly, record source/hash/import settings, mark stale dependencies, compare a reference render. | S09, rendering |
| R21 | P1 | A copied source or sample has obligations not reflected by the root license badge. | Inspect exact files and dependency/sample notices at pinned revisions; keep provenance; choose NXSG license before distribution. | Release, maintainer |
| R22 | P1 | Package install/upgrade introduces duplicate dependencies, destroys assets, or requires unavailable credentials. | Clean/reused-project install tests, package payload audit, retained old versions, editor-only boundaries, rollback plan; secrets only in trusted CI contexts. | S00/release, integration |
| R23 | P1 | Static estimates, avatar ranks, or compositor FPS become misleading GPU promises. | Label estimate vs measurement; record hardware/API/resolution/eyes/lights/mirrors/shadows/animation and frame distributions; profile intended target. | S05/S07, validation |
| R24 | P2 | A “Blender-compatible” graph changes Principled lighting, color management, noise, UDIMs, or unsupported semantics. | Exact/approximate/baked/unsupported conversion report and versioned fixtures; preserve original metadata; limited supported subset first. | Future adapter, core |
| R25 | P2 | Broad portability/SDK ambitions delay the first usable material editor. | One backend, small type/node set, local search, simple official Patterns; keep deferred work outside the MVP acceptance gate. | All milestones, maintainer |

## Open unknowns requiring a spike

- **Environment:** record any Linux client/headset validation path separately from the established Unity OpenGL fixture. Native Windows is not a prerequisite for editor work, but remains required for Windows claims.
- **Editor:** compare custom UI Toolkit canvas and GraphView; prove undo/reload recovery, keyboard interactions and responsive previews on the exact patch.
- **Asset publication:** determine a recoverable shader/material promotion strategy that preserves Unity identities even if import fails after file writing.
- **Shader contract:** verify actual forward/shadow/stereo/fallback behavior in Unity and VRChat, including global inputs that preview scaffolding may synthesize.
- **Fur:** choose generation technique from paired measurements; hardware cost cannot be settled from a paper or source inspection.

These unknowns need not stop independent portable-core work. They do block claims about an installable, VR-ready product.

## Evidence and review

Technical rationale and primary citations live in [platform](research/PLATFORM_AND_INTEGRATIONS.md), [compiler](research/GRAPH_COMPILER_AND_PORTABILITY.md), [editor](research/EDITOR_UX_AND_PRIOR_ART.md), [rendering](research/RENDERING_FUR_AND_PERFORMANCE.md), and [release](research/VALIDATION_AND_RELEASE.md) research. [Decisions](DECISIONS.md) name the selected mitigations; [implementation gates](IMPLEMENTATION_PLAN.md) define what will test them. Revisit a risk when its dependency version, supported target, or graph semantics change; do not restart unrelated research after every small edit.
