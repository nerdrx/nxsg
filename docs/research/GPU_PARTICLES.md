# GPU-driven particles for VRChat (Unity 2022.3 Built-In)

Research baseline: 2026-09-18. Target: PC VRChat avatars and worlds built with
Unity 2022.3.22f1 and the Built-In Render Pipeline. This is source-backed
feasibility research, not a claim of live VRChat or headset validation.

## Short recommendation

For an **avatar**, the selected prototype is an additional geometry pass in the
same material: each source triangle emits one camera-facing quad while the
existing base surface remains. The geometry stage derives a stable phase from
primitive ID, chooses a deterministic barycentric spawn point, then applies
outward-normal motion, gravity, spread, and looped fade from `_Time`. It adds no
mesh asset and no runtime script. Unity documents geometry stages as requiring
shader target 4.0; this must be compiled and tested per target graphics API.
([Unity, Shader compilation targets, 2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-ShaderCompileTargets.html))

This is animated geometry, not a true particle simulator: particle count is
source triangle density, every frame starts from the current skinned pose, and
there are no historical spawn positions, collisions, persistent state, or
script-driven lifecycle. The source renderer bounds are not expanded, so
outward motion can clip; stereo behavior and live VRChat client behavior remain
unverified.

For a **world**, use Unity's built-in `ParticleSystem` first. It is supported by
VRChat world workflows and can be controlled by Udon; Unity's GPU instancing
option reduces the rendering cost of mesh particles. This still does not make
the simulation GPU-driven. Keep compute and stateful Custom Render Texture
designs as unvalidated experiments until the exact VRChat runtime path is
documented and tested.

## Feasibility by target

| Approach | Avatar | World | What it actually moves to the GPU | Main limits |
| --- | --- | --- | --- | --- |
| Unity `ParticleSystem` + billboard renderer | Feasible as a whitelisted component, subject to avatar particle limits | Feasible | Rendering can be GPU-instanced only for the documented mesh-particle path | The ParticleSystem still owns its simulation; GPU instancing is not a compute simulator. Avatar CPU/component/particle budgets remain. |
| Unity `ParticleSystem` + mesh renderer + GPU instancing | PC-feasible in principle; validate shader/fallback and avatar budgets | Recommended first prototype | Repeated mesh drawing and per-particle instance data | Unity requires Mesh mode, an instancing-capable shader, a supported platform, and Enable GPU Instancing. Default billboard mode is a different path. |
| Custom compute shader driving buffers/indirect draws | Unverified and outside the avatar baseline | Unverified from the cited VRChat world docs | Particle state update and/or draw data | Compute shaders run outside normal rendering and need runtime dispatch/buffer orchestration. VRChat documents Udon graphics calls such as `DrawMeshInstanced`, but this report found no official Udon `ComputeShader.Dispatch` path. |
| Custom Render Texture (CRT) stateful simulation | **Unknown/unvalidated** | Possible Unity technique; VRChat client behavior needs a world test | Texture update and feedback state | Realtime updates happen each frame; OnDemand requires a script. Double buffering reads the previous result but copies on each swap. Initialization/update happens at the next frame, so lifecycle and ordering need tests. |
| Same-source geometry pass + analytic quads | **Selected prototype** | Feasible in Unity; VRChat avatar/client path unverified | Geometry stage emits a quad per source triangle; vertex/fragment stages animate it | Triangle density controls count. Current pose only; no historical state/collisions. Requires target 4.0 geometry support. Bounds remain unchanged and may clip. |
| Baked billboard mesh + analytic vertex motion | Feasible fallback | Feasible | Vertex displacement and billboard math | Requires an extra mesh asset. Static topology only. No true particle lifecycle, collisions, per-particle random state, or simulation feedback. `_Time` follows Unity time and is not a network-sync contract. |

## Evidence and interpretation

### ParticleSystem and GPU instancing

Unity 2022.3 documents GPU instancing specifically as a rendering feature for
ParticleSystem mesh particles: set Renderer mode to Mesh, use an instancing
shader, run on a supported platform, and enable GPU instancing. Unity's built-in
compatible shader is `Particles/Standard Surface`; custom shaders need the
documented instancing setup and at least shader target 4.5.
([Unity, Particle System GPU Instancing, 2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/PartSysInstancing.html))

That page does not claim that ParticleSystem simulation runs on the GPU. The
separate C# Job System integration page describes applying custom particle
behavior through C# jobs, which is CPU worker-thread work, and says that
`GetParticles`/`SetParticles` run on the main thread. Therefore the honest
description is **CPU-owned simulation with optional GPU-instanced rendering**;
do not label this “GPU simulation.”
([Unity, Particle System C# Job System integration, 2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/particle-system-job-system-integration.html))

VRChat's official avatar component page lists both `ParticleSystem` and
`ParticleSystemRenderer` as allowed Unity components, but says that components
outside the list and custom scripts will not work on avatars or may block
upload. This permits a configured ParticleSystem component, not an arbitrary
MonoBehaviour that dispatches compute or rewrites particle buffers.
([VRChat, Allowed Avatar Components](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components/))

VRChat also exposes ParticleSystem operations such as `Emit`, `Play`, `Pause`,
`Simulate`, and `Stop` to Udon world scripts. That is direct evidence for a
world prototype controlled by Udon, not evidence that those controls are
available to an avatar.
([VRChat, Udon UI Events component access](https://creators.vrchat.com/worlds/udon/ui-events/);
[VRChat, Udon networking particle example](https://creators.vrchat.com/worlds/udon/networking/))

### Compute shaders and avatar restrictions

Unity defines compute shaders as GPU programs outside the normal rendering
pipeline. They are dispatched through the ComputeShader API and commonly use
ComputeBuffers or random-write textures.
([Unity, Compute Shaders, 2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/class-ComputeShader.html);
[Unity, ComputeShader API, 2022.3](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ComputeShader.html))

The missing piece for an avatar is runtime orchestration: a script must dispatch
the kernel and manage buffers/draws. VRChat's avatar whitelist explicitly
rejects custom scripts. Thus a compute-driven avatar particle system is outside
the supported avatar contract even if the shader itself compiles in Unity.
For a world, Udon is the supported programming model: VRChat describes Udon as
the programming language for worlds, with scripts able to interact with scene
objects and players. VRChat documents `VRCGraphics.DrawMeshInstanced` and
shader-global setters, but the cited docs do not document an Udon node/API for
`ComputeShader.Dispatch`. Therefore a compute-driven world particle design is
**unverified**, and must not be presented as a supported Udon path until a
primary VRChat API documents it and a live-client test passes.
([VRChat, Allowed Avatar Components](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components/);
[VRChat, Udon](https://creators.vrchat.com/worlds/udon/);
[VRChat, VRCGraphics](https://creators.vrchat.com/worlds/udon/vrc-graphics/))

### Custom Render Texture lifecycle

CRT supports `OnLoad`, `Realtime`, and `OnDemand` modes. `Realtime` updates each
frame; `OnDemand` updates from script. Double buffering exposes the preceding
result to the update shader, which is the basic feedback mechanism for a
stateful texture simulation. Unity warns that each double-buffer swap currently
copies the texture, and that updates requested by `Update()` or `Initialize()`
occur at the start of the next frame. These details create ordering, first-frame,
reset, and cost risks for a particle state machine.
([Unity, Custom Render Texture, 2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/class-CustomRenderTexture.html))

CRT is an asset/lifecycle feature, so its absence from the avatar component
whitelist does not establish that it is forbidden. The cited VRChat material
does not specify CRT behavior on avatars. Realtime CRT updates need no
MonoBehaviour, while OnDemand and parameter changes do require script control;
avatar lifecycle, reset, visibility, and shader-blocking behavior therefore
remain **unknown and unvalidated**. In a world it can be a shader experiment,
but the report must test initialization, reset, update frequency, double-buffer
cost, visibility when the object is inactive, and client platforms. A static
texture or shader-only analytic effect is much easier to ship. VRChat's
VRCGraphics documentation covers shader globals and `DrawMeshInstanced`, but
does not specify a CRT-specific avatar restriction or lifecycle guarantee; that
absence is an open validation item, not permission or prohibition.
([VRChat, VRCGraphics](https://creators.vrchat.com/worlds/udon/vrc-graphics/))

### Same-source geometry pass with analytic time motion

Unity's Built-In shader variables include `_Time`, documented as elapsed time
values intended for shader animation. A vertex shader can use it with a baked
per-quad phase, direction, scale, and lifetime attribute to produce looping
motion without a script or mutable GPU buffer.
([Unity, Built-in shader variables, 2022.3](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-UnityShaderVariables.html))

The selected approximation keeps the original mesh and base surface: the
geometry stage expands each incoming triangle into one camera-facing quad, with
primitive-ID phase/randomness and barycentric placement. The GPU evaluates
position and opacity from the current frame's skinned triangle and `_Time`. It
cannot react to collisions, maintain feedback between particles, or create
historical particle state. It also inherits shader blocking/fallback behavior,
requires stereo-safe transforms, and leaves source bounds unchanged. VRChat's FX
layer can animate particle systems and shader properties, but ordinary avatar
animation is still a higher-level parameter driver rather than a compute
runtime.
([VRChat, Playable Layers](https://creators.vrchat.com/avatars/playable-layers/);
[VRChat, Shader Blocking and Fallback](https://creators.vrchat.com/avatars/shader-fallback-system/))

## Prototype acceptance checklist

1. **Avatar:** keep the original mesh and base surface, add the experimental
   geometry pass, and compile with an explicit `#pragma target 4.0`. Test source
   triangle density, current skinned poses, primitive-ID phase stability,
   barycentric placement, outward motion/gravity/spread/fade, unchanged bounds,
   shader blocking, desktop and both-eye rendering. Keep a non-geometry fallback
   material.
2. **World:** make a ParticleSystem billboard baseline, then a Mesh-mode
   GPU-instanced variant. Measure CPU simulation, draw calls, overdraw, and
   VRChat client behavior separately.
3. **Do not claim GPU simulation** unless a target-runtime compute/CRT
   experiment demonstrates state updates in the target client with a measured
   comparison.
4. Record Unity 2022.3.22f1, VRChat SDK package versions, graphics API, client
   platform, stereo mode, particle count, texture size, and mirror/shadow state.

## Decision

NXSG's selected experimental prototype is the **same-source geometry pass**:
one camera-facing quad per source triangle, analytic `_Time` motion, and the
existing base surface retained. Keep the baked billboard mesh as a fallback,
and ParticleSystem GPU instancing as a separate world/particle experiment.
Treat compute and CRT stateful simulations as unvalidated until the exact
VRChat runtime path is proven; do not silently classify CRT as forbidden on
avatars.
