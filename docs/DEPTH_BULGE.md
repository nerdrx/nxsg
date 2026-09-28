# Depth Bulge

Depth Bulge makes a mesh dent or bulge when other geometry is nearby in the camera's depth texture. It is a visual deformation along the mesh normals, similar to Poiyomi's Depth Bulge effect.

## Connect it

1. Add **Surface → Depth Bulge**.
2. Connect **Displacement → your surface's Displacement** input.
3. Start with **Height −0.03** and **Touch distance 0.1**. Negative height presses inward; positive height pushes outward.
4. Move an opaque object close to the mesh, with a camera that provides a depth texture.

**Touch Dent** in the Example Gallery is a ready-connected PBR graph. Its standalone sphere preview has no touching object, so an unchanged preview is expected.

| Control | Meaning |
|---|---|
| Height | Signed displacement in object units. Object scale changes its size in the scene. |
| Touch distance | Maximum camera depth separation in metres. This is separation along the view, not a 3D collision radius. |
| Falloff | Shapes the response. Higher values concentrate the effect near the touching depth. |
| Self-depth bias | Ignores nearly equal depths to reduce the mesh reacting to itself. Raise slightly if the untouched mesh ripples. Too much bias suppresses small contacts. |
| Mask | Where the effect applies; evaluated from 0 to 1. Connect a texture or procedural mask. |

All five controls accept numeric connections. Slider tracks offer convenient ranges; typed finite values can exceed them. Negative distance becomes zero, falloff has a small positive floor, and mask saturates to 0–1. **Touch** outputs the masked proximity signal without multiplying by Height, useful for a color mix or emission effect.

## Depth and mesh requirements

The camera must provide a depth texture. In Unity, a camera can request `DepthTextureMode.Depth`. For a VRChat avatar, availability depends on the world/camera; Poiyomi documents its separate **DepthGet** prefab as one way to request depth. NXSG does not add lights, cameras or avatar helpers automatically. Missing depth leaves the mesh unchanged.

The touching object normally needs an opaque or cutout material with a ShadowCaster pass. Transparent objects generally do not appear in Unity's camera depth texture. The effect cannot detect an object hidden behind the nearest recorded surface, and it changes with viewing direction. It does not provide colliders, VRChat Contacts, stored deformation or physical touch detection.

Deformation needs enough vertices. A coarse mesh produces coarse dents; **Tessellation** can supply additional vertices on PC. Expand renderer bounds when using displacement to prevent culling at screen edges. Shading normals are adjusted around the deformation while preserving the original smooth normals away from it.

## Supported surfaces and limits

- Toon, Unlit, PBR, Layered PBR, Shell and Tessellation support the node. Surface Particles can use it on the **Base** surface; generated particles still originate from their existing mesh pose.
- Fur, Volume Surface and Particle Surface combinations are rejected. Depth Bulge cannot drive generated-particle inputs.
- Mirrors and other oblique camera projections disable the effect. Shadow/depth passes keep the original mesh shape to avoid depth feedback. Shadows, depth-based fog and other screen effects can therefore disagree with the visible dent.
- Driving surface opacity or alpha-bearing albedo from Touch omits the shadow pass, as with other camera-dependent alpha branches. Use the displacement input for deformation and emission for a contact glow when you need the original shadow.
- The self-depth bias is a precision filter, not object identification. Nearby parts of the same mesh can still react to each other.
- The node does not change the material's blending mode or render queue. Camera depth sampling adds work in the stages that use the node; mesh density and tessellation determine vertex cost.

Desktop rendering and shader compilation are tested separately from VRChat and headset behavior; see [validation](VALIDATION.md).

## References

Consulted 2026-09-28:

- [Poiyomi Depth Bulge documentation](https://www.poiyomi.com/special-fx/depth-bulge), version 10.0.
- [Poiyomi public implementation](https://github.com/poiyomi/PoiyomiToonShader/blob/5ef04e1fc03dea566f891906ac37f1ee160bfaaf/_PoiyomiShaders/ModularShader/Editor/Poi_FeatureModules/Toon/Poi_DepthBulge/VRLTC_PoiDepthBulge.poiTemplateCollection), revision `5ef04e1fc03dea566f891906ac37f1ee160bfaaf`, for depth sampling, signed height and pass restrictions.
- [Unity 2022.3 camera depth textures](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-CameraDepthTexture.html), for depth contributors and camera setup.

NXSG implements the effect independently; no upstream shader source is bundled.
