# NXSG rendering, fur, animation, and performance research

Status: engineering research for `NXSG_DESIGN.md`. This report describes a
conservative Built-In/HLSL contract. The platform agent must reconcile it with
the exact Unity SDK, upload rules, shader policy, and fallback behavior.

## Short conclusion

The MVP should emit one opaque or alpha-cutout forward material with a small
toon lighting function, an optional shadow caster, and optional rim/specular /
emission branches. Fur should begin as a shell-only cutout Pattern with a
direction mask, tangent-aware strand bend, conservative bounds, and distance
quality tiers. Add fins only after a paired silhouette test shows that shells
alone fail; add geometry strands or tessellation only as an explicitly
unsupported research branch. The generator must make pass count, shell count,
texture samples, alpha mode, shadow mode, and stereo mode visible estimates.

Animated fur should use a sampled flow/noise texture. A procedural noise node
may offer scrolling/evolving variation, but should make precision,
looping, and sample/ALU costs explicit. Do not infer velocity
from a skinned mesh when no velocity signal is actually available: expose
artist-supplied movement, a material parameter, or a texture fallback.

Performance claims require a built player on the target headset/GPU, with
matched camera, mirror, shadow, stereo, and resolution. Editor estimates,
static VRChat performance ranks, shader
instruction counts, and a successful compile are useful gates; none is a GPU
frame-time result.

## 1. Minimal Built-In toon contract

The smallest useful generated shader is an opaque `ForwardBase` pass using
position, normal, tangent, UV0, vertex color, main texture, main directional
light, ambient/light-probe contribution, and a stable property block. Unity’s
Built-In pass tags define `ForwardBase` for ambient,
the main directional light, vertex/SH lights, and lightmaps; `ForwardAdd` is a
separate per-additional-light pass, so NXSG should omit it unless the graph
actually asks for per-pixel additional lights ([Unity pass tags,
2022.3](https://docs.unity3d.com/kr/2022.3/Manual/shader-predefined-pass-tags-built-in.html)).

Toon diffuse can be a ramp or threshold function over `NdotL`, with a soft
width and shadow factor. The contract should specify color space, shadow order,
and whether texture alpha is a mask or output opacity. A two-band default is
easier to validate than an unconstrained ramp.
Normal maps belong in tangent space and require a valid tangent basis; a
material with no normal map should not pay for tangent-space reconstruction.

Specular should be a separate, opt-in branch: color, intensity, roughness or
hardness, and an optional toon threshold. Rim should be an opt-in view-space
`1 - saturate(NdotV)` mask with width, color, and intensity. Emission should be
added after lighting, driven by a color and scalar, and optionally multiplied
by a mask, gradient, animation signal, or AudioLink sample. These branches
should not silently add passes; they are fragment work in the base pass unless
the target-specific backend proves a pass is required.

Shadow behavior needs an explicit choice: receive the main shadow, cast through
a `ShadowCaster` pass, both, or neither. Unity uses
`ShadowCaster` for object depth in shadow maps or depth textures, and
`MotionVectors` is a separate predefined pass ([Unity pass tags,
2022.3](https://docs.unity3d.com/kr/2022.3/Manual/shader-predefined-pass-tags-built-in.html)).
If the color pass clips an alpha mask, the shadow caster must use the same mask
and cutoff or the avatar will visibly detach from its shadow. Keep shadow
keyword variants out of the material unless used by the graph.

## 2. Render state and pass contract

Opaque toon surfaces should use `Cull Back`, depth testing, and depth writes.
Two-sided fur shell rendering may use `Cull Off`, but the generator should show
that it may increase visible surfaces/overdraw depending on geometry and can
affect self-occlusion. ShaderLab
exposes `Cull`, `ZTest`, `ZWrite`, `Offset`, `Blend`, and `AlphaToMask` as pass
state ([Unity ShaderLab Pass](https://docs.unity3d.com/es/2018.3/Manual/SL-Pass.html)).

For fur and leaf-like coverage, prefer alpha clipping in the `AlphaTest` queue:
sample coverage, apply `clip(alpha - cutoff)`, and retain depth and shadow
participation. Alpha-to-coverage is an optional MSAA path. Regular alpha
blending should be a deliberate fallback for soft effects because it usually
disables depth writes and creates sorting problems; blending also disables some
early-Z optimizations ([Unity ShaderLab Blend](https://docs.unity3d.com/cn/current/Manual/SL-Blend.html)).

Outlines need a visible cost contract. The common inverted-hull method is a
second pass with front-face culling and an outward offset. It requires another
vertex submission and can inflate bounds; a screen-space outline needs a
depth/normal buffer and is a different backend problem.
NXSG should default to one-pass rim lighting, offer inverted-hull outline as an
explicit second-pass Pattern, and show the extra pass in the report.

Shadows are multiplicative in practice: a fur material may be rendered in the
camera pass, once per relevant shadow map, and again in a mirror or reflection
capture. Shells in a shadow caster can be especially expensive and noisy. MVP
should offer a cutout base shadow and a “fur shadow simplified” mode that uses
the base mesh or a low shell count. Never imply that a material’s visible pass
count is its complete frame cost.

VR custom shaders need stereo-safe transforms and screen-space coordinates.
Unity’s single-pass instanced mode uses one instanced draw for both eyes and can
reduce CPU work, but custom shaders must carry the required stereo setup. It can fall back to
multi-pass when unsupported, and Built-In deferred does not support it in the
documented 2022.3 configuration ([Unity stereo rendering,
2022.3](https://docs.unity3d.com/ja/2022.3/Manual/SinglePassStereoRendering.html)).
Therefore the backend must test multi-pass and single-pass instanced separately;
screen-space noise, outlines, depth reads, and mirror images must be checked per
eye. Do not use a mono screen UV for a stereo-dependent effect.

This is distinct from GPU instancing of repeated meshes and does not make
skinned fur free.

Mirrors and realtime reflection probes are extra camera work. Unity documents
that a cubemap probe renders six faces and that realtime probes can update every
frame or be time-sliced; update frequency, resolution, and culling mask change
the cost ([Unity reflection probe performance](https://docs.unity3d.com/ru/530/Manual/RefProbePerformance.html)).
The benchmark must include mirror off, one mirror, and the worst-case
mirror arrangement.

## 3. Fur approaches and a staged target

The primary shells-and-fins reference renders concentric semi-transparent shell
layers over a surface, then places fins normal to the surface for silhouette
coverage. It supports local fur color, length, and direction control, and
describes local shell shearing for motion ([Lengyel, Praun, Finkelstein, Hoppe,
I3D 2001](https://hhoppe.com/proj/fur/)). Its implementation assumptions are
not a guarantee of modern VRChat support.

**Shells** reuse the skinned base mesh and displace each layer along a direction
field. They are portable with ordinary vertex/fragment programs. Costs scale
with shell count, visible area, and overdraw. Weaknesses are layer banding,
flat silhouettes, alpha overdraw, and repeated shadow/mirror work. Use a
deterministic per-shell offset and alpha mask; do not call a shell count
“cheap” without measuring the target scene.

An ordinary vertex shader cannot create shell copies by itself: use fixed
repeated ShaderLab passes, offline expanded mesh/skinning metadata, or a later
geometry-stage experiment. With fixed passes, discarding distant shells in a
branch does not remove their pass/draw setup cost; LOD must measure the whole
draw path.

**Fins** add camera-facing or surface-normal strips with a baked strand texture.
They improve silhouettes for less volume than many shells, but can pop with
view direction, need careful backface/alpha handling, and can look like cards
in stereo. They remain a texture representation, not per-strand geometry.

**Hybrid** should be the quality tier: fewer shells for volume, fins for
silhouette, and a base cutout for distant LOD. Keep fins out of the shadow
caster by default unless a paired shadow test justifies them. Real hair strands,
compute-generated curves, geometry shader expansion, and tessellation may be
useful research on a specific GPU, but they should not be implied by the
Built-In/HLSL stage contract. VRChat’s current Android avatar policy permits
only SDK-provided shaders, while its optimization guidance warns about excess
passes and tessellation; this is platform policy, separate from what Unity can
compile ([VRChat Android shader limitations](https://creators.vrchat.com/platforms/android/quest-content-limitations/),
[VRChat optimization tips](https://creators.vrchat.com/avatars/avatar-optimizing-tips/)).

## 4. Skinned fur direction, displacement, and normals

Evaluate fur direction in a declared space. Object/local space keeps a groom
attached to the avatar and is appropriate for a gravity vector authored in the
avatar’s coordinates. World space is useful for a global wind field or world
gravity but can make the groom slide when the avatar rotates. A robust Pattern
combines a tangent-space or object-space direction texture with a scalar length
mask, transforms it to world space using the skinned normal/tangent basis, then
adds a clamped gravity or wind vector. Expose direction strength and a
“preserve surface tangent” control so artists can avoid normal inversion.

The vertex displacement should be bounded by a declared fur length. Use a
length/density mask, vertex color, and optionally a flow texture. Keep the
normal treatment explicit: a cheap mode preserves the skinned base normal and
uses a view/rim response; a better mode reconstructs a displaced-shell normal
from neighboring shell offsets or an approximate bend. Reusing an unchanged
normal after a large offset can create implausible lighting and self-shadow
errors. Tangent basis handedness must be preserved when sampling a normal map.

Skinned meshes have a practical visibility trap. Unity warns that imported
bounds may not contain vertices moved by vertex shaders, runtime bone changes,
ragdolls, or other post-import animation; enlarge bounds when the maximum is
known, or enable `Update When Offscreen` when it is not, with a performance
tradeoff ([Unity Skinned Mesh Renderer](https://docs.unity3d.com/cn/2020.3/Manual/class-SkinnedMeshRenderer.html)).
NXSG’s Fur Pattern should emit a maximum displacement estimate and a validation
warning when it exceeds the renderer’s bounds.

Unity’s skinned motion vectors are an optional, separate facility and require
double-buffered GPU mesh data; they are not a free per-vertex velocity input
for an arbitrary color shader ([Unity `SkinnedMeshRenderer.skinnedMotionVectors`](https://docs.unity3d.com/cn/6000.0/ScriptReference/SkinnedMeshRenderer-skinnedMotionVectors.html)).
For wind or movement response, prefer an explicit material parameter or a
direction/strength texture. A script-fed world/object velocity is a standalone
Unity option, not an assumption for a VRChat avatar; use only documented avatar
parameters there. If no signal exists, use stable time plus spatial noise and
label it synthetic. Do not claim bone velocity or true physical response.

## 5. Animation and texture-driven effects

Unity exposes `_Time`, sine/cosine time, and delta time to Built-In shaders
([Unity built-in shader variables](https://docs.unity3d.com/es/530/Manual/SL-UnityShaderVariables.html)).
NXSG can lower a scrolling UV node to `uv + direction * speed * time`, with
phase and repeat controls. A texture-based flow field is the cheap default:
sample one packed direction/noise texture, scroll it, and remap it to bounded
displacement. A procedural evolving or “4D-like” noise mode should be described
as several 2D/3D samples or a compact hash, with explicit sample and ALU
estimates; no visual label should imply a true 4D noise implementation.

For precision and looping, use a bounded phase (`frac(time * cycles + phase)`)
only as an input to a periodic construction, document the loop period, and run
a seam test. Wrapping phase does not recover precision already lost in a large
float `_Time`; avoid unbounded subtraction where it can jitter. Directional
flow should derive from a normalized vector and clamp displacement.

AudioLink is a texture interface, not a magical engine input. Its documented
shader path exposes a 128x64 RGBA `_AudioTexture`, waveform data, four-band
frame data, interpolation helpers, and `AudioLinkIsAvailable()`; the project
also notes a CPU readback penalty and does not recommend readback on Quest
([AudioLink shader documentation](https://github.com/llealloo/audiolink/blob/master/Docs/README.md)).
NXSG should expose AudioLink availability, band, smoothing, gain, and a
fallback value. Use the sampled band as a scalar for emission, fur length,
noise speed, or gradient position. Keep the parameter binding ordinary so an
avatar animation can drive the same socket when AudioLink is absent.

Flipbooks, gradients, dissolves, glitches, and vertex animation should be
composable nodes. A flipbook needs frame count, layout, playback rate,
phase, and wrap mode. An affine UV cell remap may be vertex work when valid,
while texture sampling remains fragment work; avoid per-frame material writes.
A gradient node should
state its coordinate space and clamp/repeat behavior. Dissolve should share its
mask and cutoff with the shadow caster when the material casts shadows.
Glitch should default to UV perturbation with a bounded mask; vertex glitches
need an amplitude limit, stable seed, and bounds warning. Time-evolving effects
must have a stereo test so both eyes see coherent world/object motion.

Baking is valid only for static branches. A branch may be baked when it depends
only on constant/material values, static UV/object data, and a declared view
independent basis. Time, AudioLink, camera direction, screen position, realtime
lighting, skin deformation, and world position make the output time-, view-, or
pose-dependent; those branches must remain live or be baked per known pose/view
with an explicit loss warning. Preserve the source graph and record the bake
inputs, resolution, color space, and invalidation conditions.

## 6. LOD, overdraw, and cost reporting

Fur LOD should be a quality policy with visible transitions: base cutout at far
distance, low-shell fur at medium distance, and shell/fins near the camera.
Avoid a single hard distance switch if it causes stereo-visible popping. Use a
short cross-fade only when its alpha mode and overdraw are affordable; a
cross-fade with blending can temporarily draw both LODs and disable early-Z.
Alpha clipping preserves depth but can shimmer; alpha-to-coverage requires
MSAA and should be tested on the actual headset.

Report pass count, shell/fin counts, estimated texture samples and procedural
iterations, variant count, alpha/shadow/outline modes, and mirror/stereo
multipliers. These are useful
compiler estimates and graph review signals, not GPU timings. VRChat
itself says its avatar Performance Rank is static analysis and does not account
for shaders, texture resolution, or pixel lights ([VRChat Performance Ranks](https://creators.vrchat.com/avatars/avatar-performance-ranking-system/));
the rank cannot validate NXSG’s GPU cost.

## 7. Benchmark method and evidence gates

Create a scene with one skinned furry avatar, opaque/cutout controls, a
directional light, a shadow receiver, and intended headset scale.
Keep mesh, textures, animation, lighting, camera path, quality, and post effects
fixed.
Capture these variants independently: base toon; base plus shadow caster;
one, low, and high shell counts; fins; hybrid; outline; animated noise;
AudioLink active/absent; multi-pass and single-pass instanced; mirror off/on;
and shadow on/off. Include a far-LOD camera and the worst expected mirror.

Use the Unity Profiler GPU module and Frame Debugger to identify pass and draw
contributions, then use target-device captures for real timing. Unity explicitly
states that Editor profiling is only an approximation and that target-device
profiling is required for reliable results ([Unity profiling applications](https://docs.unity3d.com/2022.2/Documentation/Manual/profiler-profiling-applications.html)).
Record headset, GPU, Unity version, API, stereo mode, render scale, refresh
target, warm-up frames, median/high-percentile GPU time, CPU active time,
dropped frames, mirror/shadow state, and temperature where available.

Evidence gates:

1. **Compile gate:** generated shader validates, pass tags are intentional,
   properties are stable, and unused branches/keywords are stripped.
2. **Image gate:** matched screenshots show lighting, fur silhouette, shadows,
   LOD transition, animation phase, and both eyes without depth or UV errors.
3. **Correctness gate:** bounds remain visible under animation; alpha clipping
   agrees between color and shadow; mirrors and fallback materials render.
4. **Timing gate:** target-device GPU captures compare variants under matched
   conditions. Report measured deltas with their capture context; leave unknown
   costs unknown.
5. **Policy gate:** the platform agent verifies the exact VRChat SDK/platform
   shader permissions and upload/fallback behavior. Unity compilation alone is
   not policy approval.

## 8. Recommended stages and prototype tests

**Stage A — Toon baseline.** Implement Texture → Toon → Output with opaque
forward lighting, optional cutout, shadow receive/cast, specular, rim, and
emission. Prototype test: compare a reference material in mono, multi-pass VR,
and single-pass instanced VR; inspect the Frame Debugger for intended passes.

**Stage B — Animation.** Add UV scroll, flipbook, gradient, dissolve, bounded
glitch, sampled flow texture, and AudioLink scalar input with fallback. Prototype
test: a fixed camera and two-eye capture across a full loop, with AudioLink
missing, flat, and active inputs.

**Stage C — Fur shells.** Add direction/length masks, local/world gravity,
skinned displacement, shell alpha clip, simplified shadow caster, bounds
warning, and three quality tiers. Prototype test: rotate and animate a furry
avatar through silhouette, mirror, shadow, and offscreen-boundary cases; record
overdraw and GPU timings without inventing a universal shell budget.

**Stage D — Hybrid fur and LOD.** Add fins only if the silhouette test passes,
then add cross-fade or dithered LOD with explicit overdraw reporting. Prototype
test: compare shell-only, fin-only, and hybrid views in stereo and at the
closest mirror; verify that the simplified shadow does not detach visibly.

**Stage E — Optional advanced branches.** Investigate better displaced normals,
expressive flow, or strand geometry only behind capability checks and measured
evidence. Keep unsupported or policy-blocked branches visible as unavailable.

## Source/version/date ledger

Sources were checked 2026-09-17. Primary references used:

- *ShaderLab: Pass*, 2018.3:
  https://docs.unity3d.com/es/2018.3/Manual/SL-Pass.html
- *ShaderLab: Blend*, current Unity 6:
  https://docs.unity3d.com/cn/current/Manual/SL-Blend.html
- *Predefined Built-In pass tags*, 2022.3:
  https://docs.unity3d.com/kr/2022.3/Manual/shader-predefined-pass-tags-built-in.html
- *Stereo rendering*, 2022.3:
  https://docs.unity3d.com/ja/2022.3/Manual/SinglePassStereoRendering.html
- *Skinned Mesh Renderer*, 2020.3:
  https://docs.unity3d.com/cn/2020.3/Manual/class-SkinnedMeshRenderer.html
- *SkinnedMeshRenderer.skinnedMotionVectors*, Unity 6 API:
  https://docs.unity3d.com/cn/6000.0/ScriptReference/SkinnedMeshRenderer-skinnedMotionVectors.html
- *Built-in shader variables*, legacy reference:
  https://docs.unity3d.com/es/530/Manual/SL-UnityShaderVariables.html
- *Profiling your application*, 2022.2:
  https://docs.unity3d.com/2022.2/Documentation/Manual/profiler-profiling-applications.html
- Unity Technologies, *Reflection Probe Performance and Optimisation*, 5.3
  documentation: https://docs.unity3d.com/ru/530/Manual/RefProbePerformance.html
- Lengyel, Praun, Finkelstein, Hoppe, *Real-time fur over arbitrary surfaces*,
  I3D 2001: https://hhoppe.com/proj/fur/
- llealloo, *AudioLink shader documentation*, repository `master`, accessed
  2026-09-17: https://github.com/llealloo/audiolink/blob/master/Docs/README.md
- VRChat Creation, *Avatar Performance Ranking System*, accessed 2026-09-17:
  https://creators.vrchat.com/avatars/avatar-performance-ranking-system/
- VRChat Creation, *Avatar Optimization Tips*, accessed 2026-09-17:
  https://creators.vrchat.com/avatars/avatar-optimizing-tips/
- VRChat Creation, *Android Content Limitations*, last updated 2025-06-25:
  https://creators.vrchat.com/platforms/android/quest-content-limitations/
