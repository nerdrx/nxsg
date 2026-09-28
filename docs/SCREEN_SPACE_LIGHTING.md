# Screen Space Lighting

NXSG's built-in screen-space lighting helpers read the Built-In Render Pipeline's camera depth texture in the material fragment shader. They provide an inexpensive approximation for local ambient occlusion and short contact shadows. They do not trace geometry outside the camera image or replace baked lighting, shadow maps, or a full ambient-occlusion pass.

## Backend contract

Include `ScreenDepthShader.Hlsl` after `UnityCG.cginc` has defined Unity's depth and stereo macros. The generated surface fragment must call `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)` before either helper. The helpers require the existing `NXInput.ws` (world position) and `NXInput.n` (world normal) fields. They are fragment-only and must not be lowered into vertex expressions.

| Helper | Signature | Result | Suggested application |
| --- | --- | --- | --- |
| `NX_ScreenSpaceOcclusion` | `(NXInput input, float radius, float strength, float thickness, float bias, int samples)` | Visibility from 0 (occluded) to 1 (visible) | Multiply indirect/ambient light only. |
| `NX_ScreenContactShadow` | `(NXInput input, float3 directionToLight, float distance, float strength, float thickness, float bias, int samples)` | Visibility from 0 (shadowed) to 1 (visible) | Multiply the current light's attenuation, using `UnityWorldSpaceLightDir(input.ws)` for the direction. |

Each helper accepts a compile-time node quality of 4, 8, 16 or 32 samples. The implementation has a fixed 32 iteration maximum and clamps all other values to one of those tiers; it never derives loop work from a live material input. Ambient occlusion compares each sample against the receiver plane extrapolated from screen derivatives, which suppresses false self-occlusion across sloped planes. The depth texture's reversed-Z convention is handled, and orthographic radius projection uses a separate scale. Missing or tiny depth textures, clear/far samples, oblique projections and shadow-caster passes return neutral visibility (`1`). Stereo depth sampling uses Unity's screen-space texture macros. In double-wide single-pass stereo, AO radius is scaled to the active eye viewport and sample UVs outside that eye's rectangle are rejected, preventing cross-eye taps. The generated fragment still needs the standard stereo eye setup. Validate the actual XR runtime separately.

## Suggested node controls

These are authoring defaults for the nodes; each node's values remain live when connected to mutable graph inputs.

| Node | Control | Suggested default | Units / effect |
| --- | --- | ---: | --- |
| Screen Space Occlusion | Radius | `0.3` | World units; projected neighborhood size. |
|  | Strength | `0.65` | Unitless; maximum ambient visibility reduction. |
|  | Thickness | `0.2` | World units; maximum depth separation treated as a local blocker. |
|  | Bias | `0.02` | World units; ignores near-equal depths. |
|  | Samples | `8` | Compile-time tier: `4`, `8`, `16` or `32` samples per fragment. |
| Contact Shadow | Distance | `0.5` | World units; length of the light ray. |
|  | Strength | `0.7` | Unitless; maximum direct-light reduction. |
|  | Thickness | `0.12` | World units; accepts blockers near the sampled ray depth. |
|  | Bias | `0.015` | World units; offsets the ray start from the receiver and rejects self-depth. |
|  | Samples | `8` | Compile-time tier: `4`, `8`, `16` or `32` samples per fragment. |

All distances follow Unity scene units (commonly treated as metres). Clamp strength to 0–1 and distances to non-negative values in shader evaluation. Bias values larger than the sampling radius or ray length suppress the corresponding effect.

## Depth requirements and limits

The camera must provide `DepthTextureMode.Depth`. Unity renders this depth texture with `ShadowCaster` passes and includes opaque-queue objects; transparent objects generally do not contribute. The helpers sample the primary `_CameraDepthTexture` and return neutral visibility when the binding is absent or its dimensions are too small to be useful. They do not request the texture from a camera.

The occlusion helper compares a fixed screen-space neighborhood around the fragment. The contact helper projects fixed steps along the supplied world-space light direction. Both are depth-only approximations: they can miss off-screen blockers, leak at silhouettes, and vary with screen resolution, camera, and geometry. Use a real shadow map or a higher-quality renderer feature when those limits are unacceptable.

The implementation is authored for Unity 2022.3 Built-In HLSL and does not include or derive code from third-party avatar shaders. The portable source checks validate sample bounds, depth guards, stereo sampling and neutral fallback branches. Parent-owned Unity shader compilation and render checks are still required before claiming graphics or headset validation.

## Primary references

Consulted 2026-09-28:

- [Unity 2022.3 Cameras and depth textures](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-CameraDepthTexture.html): depth range/linearization, `DepthTextureMode`, `ShadowCaster` contribution and opaque render-queue requirements.
- [Unity 2022.3 Built-in shader macros](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-BuiltinMacros.html): depth conversion and built-in shader macro context.
- [Unity 2022.3 single-pass instanced rendering and custom shaders](https://docs.unity3d.com/2022.3/Manual/SinglePassInstancing.html): stereo texture declarations/sampling macros and fragment eye-index setup.
