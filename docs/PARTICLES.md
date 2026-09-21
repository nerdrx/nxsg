# Shader particles

## Emit from the mesh wearing the material

Use **Surface Particles** between your existing surface and Output:

```text
Toon / Unlit / PBR / Shell → Surface Particles → Output
                               ↑
                         particle Color
```

The shader keeps the base material and adds a geometry pass that emits camera-facing soft dots from the same mesh. No extra mesh, Particle System, or runtime script is required. **Assets → NXSGExamples → Surface Sparkles** is the ready-made example. Build it and assign the generated material to a mesh; for an existing graph, insert Surface Particles after the final surface.

Controls: triangle density, emission rate (particles per selected triangle per second), size, lifetime, outward speed, local-Y gravity, velocity randomness, alpha/additive blending, opacity, and emitter mask. Time can be driven by another node. Emitter Mask uses the mesh UVs. By default, particle Albedo/Emission/Opacity use generated sprite UVs.

Enable **Color from mesh UVs** to sample connected Albedo and Emission textures at each particle's spawn point on mesh UV0. Connect your mesh texture to Albedo first; the toggle does not automatically copy the Base surface color. Each particle gets the color at its own source location. Opacity and the soft circular shape keep sprite UVs. Explicit alternate coordinate sources (such as UV1 or world coordinates) retain their selected mapping. Existing graphs keep sprite UVs until you enable the toggle.

Current limits:

- Experimental PC geometry-shader path. Linux OpenGL is the test target; live VRChat/stereo remains unverified.
- Density is the fraction of triangles selected to emit (1 = all). Emission Rate controls births per selected triangle per second; 0 disables emission. Up to four particles are alive per generated subtriangle. When requested rate × lifetime exceeds four, the particle pass automatically tessellates source triangles to increase capacity. The rate is approximate because tessellation subdivides source geometry; it is distributed using a triangle-count estimate. Tessellation is capped at level 64, so extremely large inputs eventually plateau. A requested rate of 10,000/second/source triangle with a one-second lifetime is exercised by the Linux graphics fixture. The cycle is prewarmed, so particles can already be alive when the material appears. Missing rate values in older graphs default to `1 / lifetime`.
- Small and large triangles contribute equally; density is not uniform per surface area. Every generated subtriangle processes four particle slots even when hidden by density/mask. High rates can multiply geometry work dramatically. The high-count path requires hardware tessellation (shader target 4.6); the ordinary path uses target 4.0.
- Loops follow the current mesh pose, including skinning. They do not retain world-space birth positions, simulate collisions, or leave persistent trails. Base shader displacement is not inherited.
- Source renderer bounds are unchanged. Expand SkinnedMeshRenderer local bounds or the source mesh bounds to cover the full trajectory; otherwise offscreen particles may be culled.
- Keep GPU instancing disabled. Alpha particles are not individually depth-sorted. Additive blending is the safer default for overlapping sparks.
- Use at the graph output; nesting Surface Particles is not supported. Its Base can contain Shells.

See [GPU particle research](research/GPU_PARTICLES.md) for the alternatives and source-backed platform distinctions.

## Existing Unity Particle System materials

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

### Slider ranges

Slider tracks provide convenient working ranges. Their adjacent number fields accept finite values beyond those ranges and preserve them through save/reload. Mathematical limits (such as positive lifetime) still apply. Shader semantics also remain: density and opacity saturate, negative particle size hides the particle, and rates above the tessellation capacity plateau.

### Source mesh vertex colors

Surface Particles do not automatically multiply color or opacity by the source
mesh's vertex colors. Some avatars store black or zero-alpha vertex colors on
hair and other material regions. Connect a Vertex Color node to Albedo, Emission
or Mask when that data should explicitly control particles. Particle lifetime
fading is stored separately, so Vertex Color still reads the original mesh data.
