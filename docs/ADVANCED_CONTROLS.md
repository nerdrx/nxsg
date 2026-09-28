# Advanced Controls

This guide covers the added surface, lighting, timing, and texture controls. The node inspectors show the same controls and explain their socket behavior.

The inspector keeps common surface controls open and groups optional settings into sections that start collapsed. Section state is remembered per graph, node, and section through inspector rebuilds. A `*` beside a field means its value differs from a new node's default; a section `*` means it contains a changed value. These markers are separate from the graph's unsaved-edits indicator. XYZ vector fields use full-width component rows.

Toon and PBR cards group optional canvas sockets by purpose; surface-particle sockets use Emission timing, Size & edges, and Motion groups. Expand a group to reveal unconnected inputs. Connected sockets stay visible outside the foldout so existing wires remain apparent. Layered PBR groups optional sockets into lighting, clearcoat, and sheen.

## 1. Face inputs and two-sided rendering

Use **Front Face** to read rasterized face orientation. `Is Front` is 1 for front-facing fragments and 0 for back-facing fragments; connect it to a `Mix` factor to choose front and back colors or UV branches. `Normal World` outputs a geometric world-space normal. It does not replace the tangent-space normal expected by surface normal inputs. The material must render both sides: set **Output → Visible faces → Both**.

Output keeps **Surface mode** and **Visible faces** visible. Depth and render order, Coverage and transparency, and Stencil are separate collapsed sections. Defaults preserve previous behavior: Visible faces is Front, Flip back-face normals is Off, Alpha to coverage is Off, Two-sided transparency is Off, and Alpha edge sharpness is 0. Flip back-face normals when a double-sided lit surface should light consistently from each side. Alpha edge sharpness reshapes coverage around the surface cutoff using pixel derivatives. Coverage and sharpening affect the base mesh and its additional-light pass. Shadow maps retain the surface cutoff; fur, shells and outline overlays keep their own alpha rules.

**Alpha to coverage** maps output alpha to multisample coverage. It is intended for MSAA, especially cutout edges; Unity warns that using it without MSAA can be unpredictable across graphics APIs and GPUs. [Unity 2022.3 AlphaToMask reference](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-AlphaToMask.html)

For thin translucent meshes, set **Two-sided transparency** to **Back then front**. This emits a back-face pass followed by a front-face pass. Each pass has its own **Blend** choice (Follow Output, Alpha, Additive, or Premultiplied) and **Depth write** choice (Automatic, On, or Off). It is supported with Output rendering set to Automatic, Alpha blend, or Additive; it is rejected for Particle and Volume surfaces. Transparent object sorting still applies, so this does not solve intersections between separately sorted renderers.

Example: `Front Face.Is Front → Mix.Factor`; connect front and back color branches to the two Mix inputs, then connect Mix to the surface Albedo. Set Output Visible faces to Both.

## 2. Toon shading, rim, and layered shadows

**Toon Surface → Shading** selects Threshold (default), Multiple bands, Texture ramp, or Layered shadows. This expanded section contains the mode-specific core settings first. Multiple bands has 2–8 bands (default 3). Texture ramp uses ramp X from shadow to light; set the ramp texture wrap mode to Clamp. Layered shadows has its own section with 1–3 ordered layer sub-sections; each layer has its own tint, border, blur, strength/mask, shade-map value, normal influence, and scene-shadow response. Later layers blend over earlier ones, and inactive layer wires remain stored but are ignored. Integrated rim shading and Shadow border tint are optional sections below the core settings.

The integrated rim defaults to Strength 0, Width 0.2, Softness 0.05, and Light alignment 1. Increase Strength to enable it; Width and Softness shape the camera-facing edge, while Light alignment makes it follow the main light. Shadow border tint also defaults off (Tint strength 0). It is separate from the rim.

Example: choose Layered shadows, set **Shadow layers** to 2, give layer 1 a broad dark tint and layer 2 a lighter narrow tint, then connect an animated scalar to `threshold2` to move the second boundary.

## 3. Surface lighting controls and indirect lighting

Toon and PBR surface inspectors expose **Ambient visibility** and **Direct light visibility** under **Light visibility**. Both default to 1 (fully visible). Connect **Contact Shadows → Visibility** to Direct light visibility/Shadow to attenuate direct lighting. Emission is unaffected by that socket.

Optional indirect controls are separated into collapsed **Bent normal** and **Direction override** sections. Bent normal influence defaults to 1 and only matters when a bent normal is connected; Light direction override strength defaults to 0 and leaves real light directions in use. Bent normals are tangent-space inputs that shape ambient and reflection-probe visibility. They do not cast shadows between body parts. The optional light-direction vector is world-space by default, can use object space, and blends toward a target direction. Real light distance, cookies, and cast-shadow maps still come from Unity's light.

**Minimum brightness** defaults to 0; **Maximum brightness** defaults to 0 (unlimited); **Lighting saturation** defaults to 1 (original saturation). These values affect lighting contributions, not Albedo or Emission. The maximum is applied per contribution, so several lights can add above it.

**Light Volumes** samples the optional VRC Light Volumes package. It defaults to Roughness 0.5, Metallic 0, and Strength 1. Its Color output already includes the supplied surface color; connect it to an Unlit Albedo, or use its Diffuse and Specular outputs separately. Adding its Color to a lit surface can count ambient lighting twice. The node requires the external Light Volumes package at build/runtime.

## 4. Camera-depth effects

**Screen Space AO** and **Contact Shadows** use camera depth, so they see only visible depth-buffer geometry. Both default to 8 samples. AO defaults to Radius 0.3 m, Strength 0.65, Thickness 0.2 m, and Bias 0.02 m. Connect **Visibility** to a lit surface's Occlusion input. Contact Shadows defaults to Trace distance 0.5 m, Strength 0.7, Thickness 0.12 m, and Bias 0.015 m. Connect Visibility to Shadow; its optional world-space Direction defaults to the current light direction.

Depth Rim is a separate screen-space edge mask. It defaults to Width 2 pixels, Softness 0.02 m, Bias 0.01 m, and Strength 1. Connect its output to a surface mask or emission branch. It is not a geometric outline.

All three need a valid camera depth texture. They cannot detect hidden or off-screen blockers; Depth Rim returns neutral when depth is missing, and oblique mirror projections can also return neutral. They are limited to ordinary mesh surfaces in the current backend. Check the build diagnostics for the depth requirement.

## 5. XYZ vertex deformation

Place **Vertex Deform** around the completed mesh surface chain: connect the surface or supported geometry wrapper to **Base**, then connect Vertex Deform to Output. Numeric inputs for Translation, Rotation, Scale, Pivot, Mask, Snap size, and Warp amount are connectable. Rotation uses XYZ Euler degrees. **Transform space** defaults to Object local; World applies the axes in world space. **Warp shape** defaults to None; Sphere bends toward a rounded volume and Cylinder rounds the XZ profile. Snap size 0 disables snapping; Mask 0 preserves the input geometry.

The node transforms the current mesh positions and updates normals and tangents. Its vertex stage is used by forward, additive, shadow, and tessellated geometry paths. It can wrap supported fur, surface-particle, outline, and dissolve chains so those generated effects follow the deformed mesh. The SRT-only normal transform is analytic; sphere/cylinder warp and active snapping estimate their local Jacobian from nearby samples. It does not add persistent trails or remember earlier poses. Large offsets may require larger renderer bounds. Transform inputs are evaluated at the original vertex; generated effects and scalar displacement follow the deformation. The local normal estimate does not reconstruct spatial gradients of arbitrary connected masks.

Example: `Texture2D alpha → mask math → Vertex Deform.Mask`; set Translation to `(0, 0.1, 0)`, Rotation to `(0, 0, 15)`, and keep Scale `(1,1,1)` for a masked local tilt. For world-axis motion, switch Transform space to World.

## 6. Infinity Parallax

**Infinity Parallax** is a color/alpha texture node with a fixed, validated **Interior layers** count from 1–32 (default 8). Its other defaults are Composite blending, Depth 0.3, View parallax 0.05, Height influence 0.5, a cool pale Deep tint, Depth fade 0.7, and Interior mask 1. Connect UVs and, optionally, tangent-space View and a scalar Height signal. Texture alpha masks individual layers; the `mask` input controls the blend from the front texture to the interior result. Blend choices are Composite, Additive, and Maximum.

Example: `UV0 → Infinity Parallax.UV`; connect Infinity Parallax Color to Albedo and leave Use albedo alpha enabled. Alternatively, connect Alpha to Opacity and disable Use albedo alpha to avoid multiplying the same alpha twice. Feed a height signal to Height to vary layer positions. Increase Depth or View parallax gradually, then adjust Deep tint and Depth fade. Texture sampling is bounded by the selected layer count.

This suggests a repeated internal texture stack. It does not change the silhouette, cast displaced shadows, trace actual hidden geometry, or replace Parallax Occlusion or room mapping. Use those separate nodes when their height-ray or box-interior behavior is the intended effect.

## 7. Network Clock

**Network Clock** defaults to Unity time. Its **Clock** selector can use VRChat network time instead; Period defaults to 1 second and Offset to 0. Outputs are `Seconds`, `Phase` (0–1 within each period), and `Cycle` (the period counter). Unity time follows `_Time.y` and is scaled by Unity time scale. Network time uses VRChat's `_VRChatTimeNetworkMs` uint millisecond counter; VRChat documents that it is for synchronization and offsets, has no meaningful absolute epoch, and can wrap. The counter wraps about every 49.7 days. Network periods are evaluated in integer milliseconds, from 1 ms to 24 hours; connected values are clamped to that range. The Seconds output loses precision over long uptimes; Phase performs the modulo before float conversion. NXSG previews use the preview timeline for either clock source. In an ordinary Unity scene outside VRChat, the network global remains zero: its outputs reflect a fixed counter plus Offset and do not advance. Even in VRChat, observers can differ slightly because of network correction and frame timing. [VRChat Shader Globals](https://creators.vrchat.com/worlds/udon/vrc-graphics/vrchat-shader-globals/), [VRChat SDK 3.10.2 release](https://creators.vrchat.com/releases/release-3-10-2/)

Example: `Network Clock.Phase → Mix.Factor` or `Network Clock.Seconds → Numeric Text.Value`. Choose a shared period and offset for a synchronized repeating effect; do not use the absolute seconds output as a real-world timestamp.

## 8. Viewer Stats

**Viewer Stats** exposes Render FPS, Delta Seconds, World Position, Camera Distance, Unity Seconds, and Network Seconds. All are observer-local. Render FPS is `1 / unity_DeltaTime.x`, a reciprocal of Unity's render-frame delta, not a headset refresh rate or compositor FPS. World Position is the shaded point; Camera Distance uses the active viewer camera. Unity Seconds follows Unity time; Network Seconds follows the Network Clock contract above. Unity documents its built-in time and delta-time shader variables in the [2022.3 shader variable reference](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-UnityShaderVariables.html).

Example: connect Render FPS or Camera Distance to **Numeric Text → Value** to display a viewer-side diagnostic. Values can differ between people viewing the same avatar and should not be treated as authoritative gameplay or network state.

## 9. MSDF decals and Numeric Text

**MSDF Decal** samples an RGB multi-channel signed-distance atlas. Connect a real MSDF atlas; ordinary color images and single-channel SDF images are different encodings. The default Fill is white, Outline black, Outline width 0, Softness 0 pixels, and Distance range 4 texels. It outputs Color and Alpha. The inspector recommends linear import, mipmaps and compression off, bilinear filtering, and Clamp wrap. Keep atlas distance range consistent with how the atlas was generated.

**Numeric Text** draws digits 0–9, minus, and decimal point from the generated single-channel numeric SDF atlas. The default tint is white, Maximum integer digits 6, Decimal places 1, Text scale 1, and Character spacing 0.08. Supported settings are 1–8 integer digits and 0–4 decimal places; shader sampling clamps scale to 0.001–64 and spacing to 0–0.8. Click **Create numeric SDF atlas…** to make the atlas asset and assign it to the node. Connect UVs, then connect a scalar (for example Viewer Stats Render FPS or a Clock output) to Value. It formats a fixed-width field and does not render arbitrary words. Shader floats provide roughly seven significant decimal digits, regardless of the field width. For an emission-only display, multiply Color by Alpha before connecting to Emission. For a cutout surface, connect Color to Albedo and keep Use albedo alpha enabled; if you connect Alpha to Opacity instead, disable Use albedo alpha to avoid applying it twice.

## Validation and verification

Enum controls and fixed iteration counts are validated before shader generation; numeric sockets remain ordinary graph inputs. The added editor checks render a neutral and deformed mesh and compile generated surface variants. That verifies the local Unity shader path only. It does not establish native Windows, VRChat client, headset compositor, or cross-viewer visual equivalence.

Technical references above were checked on 2026-09-28. Unity's AlphaToMask behavior and variable names come from its 2022.3 manual; VRChat clock semantics come from its shader-global documentation and SDK release notes.
