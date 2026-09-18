# Particle materials

NXSG particle graphs target Unity's standard `ParticleSystemRenderer` streams
on the PC Built-In backend. The default streams are Position, Normal, Color,
and UV. Create the particle system in Unity; NXSG supplies the material graph.

## Quick path

1. In the development project, open **Assets → NXSGExamples → Particle Sparkles**.
2. In another project, copy `Samples~/Particle Sparkles.nxsg` from the package into `Assets`.
3. Open the graph in Unity 2022.3.22f1 and choose **Build for VRChat**.
4. Create a Unity **Particle System**. Keep **Renderer → Render Mode** at **Billboard**.
5. Assign generated graph material in **Particle System → Renderer → Material**.
6. Enable **Color over Lifetime** or **Color by Speed** when desired. Their RGBA
   values arrive through renderer `COLOR` stream.

`Particle Sparkles` uses `Gradient` radial mode, then `Invert`, then `Ramp` for
a soft dot opacity mask. Bright `Color` feeds Particle Surface albedo. Particle
Surface applies renderer color and alpha automatically. Do not add a second
multiply with Particle Color.

Particle Surface defaults to opacity `1` and soft distance `0` (disabled). Set
**Blending → Additive** for glowing sparks, or **Alpha** for smoke. Set soft distance above zero only when camera
depth texture is available; this fades intersections against scene depth.

## Animation and integrations

Use Unity **Texture Sheet Animation** for per-particle flipbook UVs. NXSG's
Flipbook node uses global shader time and does not know each particle's age; do
not animate same atlas twice. NXSG does not add flipbook blending.

AudioLink and UV Distortion remain reusable graph nodes. They can feed particle
color, emission, or UVs using same connections as other surfaces.

This path is PC Built-In Unity 2022.3.22f1. It does not provide GPU particle
simulation, VFX Graph integration, mobile support, or custom VR validation.
Unity's [vertex stream documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/PartSysVertexStreams.html)
and VRChat's [shader fallback list](https://creators.vrchat.com/avatars/shader-fallback-system/)
were checked 2026-09-18. VRChat client and stereo behavior remain unverified. Keep mesh-particle GPU instancing disabled; this shader does not implement Unity's procedural particle instancing buffers.
