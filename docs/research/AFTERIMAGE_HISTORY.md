# Delayed skinned-pose afterimages

Research baseline: 2026-09-28. Target: Unity 2022.3.22f1, Built-In Render
Pipeline, PC VRChat avatars. Sources were checked on 2026-09-28. This report
separates a shader-recording experiment from a supported avatar runtime path.

## Finding

A material shader can render the current deformed skinned mesh. It cannot
preserve that pose across frames by itself. A delayed mesh needs both a pose
capture pass and persistent history storage, plus a way to replay stored vertex
positions. Unity's Custom Render Texture (CRT) can feed an update shader the
texture from its previous update, but its update input is a texture; it does not
receive a `SkinnedMeshRenderer` or its deformed vertex stream. A separate camera
or render command must first rasterize the current mesh into a pose texture.

The Unity APIs that expose current and previous skinned vertex buffers are
`SkinnedMeshRenderer.GetVertexBuffer()` and
`GetPreviousVertexBuffer()`. Unity documents using them from a `ComputeShader`,
and configuring the renderer buffer for compute access. These are script-side
APIs; they do not make historical vertex buffers readable from an ordinary
avatar material. The previous buffer covers one frame only, not a history ring.
([Unity 2022.3 `GetVertexBuffer`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SkinnedMeshRenderer.GetVertexBuffer.html),
[Unity 2022.3 `GetPreviousVertexBuffer`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SkinnedMeshRenderer.GetPreviousVertexBuffer.html),
[Unity 2022.3 `vertexBufferTarget`](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SkinnedMeshRenderer-vertexBufferTarget.html))

Two apparent shader-only alternatives do not close that gap:

- `GrabPass` captures the screen contents where an object is about to draw, for
  later passes in that render. It does not preserve a prior frame or expose
  arbitrary skinned vertex positions. ([Unity 2022.3 ShaderLab commands](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-shaderlab-commands.html))
- `DepthTextureMode.MotionVectors` produces a camera-owned, per-pixel,
  screen-space velocity texture for current-to-previous-frame motion. Unity
  documents it for image effects such as motion blur and TAA; its availability
  depends on camera configuration and renderer support. It is one-frame screen
  motion, not a world-space mesh pose ring. A material pass cannot make the
  camera generate or retain this texture by itself. ([Unity 2022.3 Motion Vectors](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/DepthTextureMode.MotionVectors.html),
  [Unity 2022.3 skinned motion vectors](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SkinnedMeshRenderer-skinnedMotionVectors.html))

An editor-orchestrated UV-space capture/replay experiment was drafted, but is
not shipped or used as validation: it requires a capture camera and C# texture
copies, so it does not meet the material-only requirement.

## VRChat avatar boundary

VRChat allows `SkinnedMeshRenderer`, `Camera`, and VRChat Constraints on avatars,
but a remote user's avatar cameras are disabled when loaded, except for the
friend/Show Avatar cases documented by VRChat. A camera-based capture setup
therefore cannot run consistently on other users' clients. Custom avatar
scripts are outside the allowed component contract, so an automatic ring copy,
camera setup, or compute dispatch cannot be added as a normal avatar runtime
script. ([VRChat Allowed Avatar Components](https://creators.vrchat.com/avatars/whitelisted-avatar-components/whitelisted-avatar-components/))

Thus the exact requested combination—arbitrary multi-frame skinned-pose
afterimages, shader-driven, visible for remote VRChat users, with no avatar
runtime C#—has no supported capture/update path in the documented Unity/VRChat
contract. Do not ship current-pose mesh copies or analytic motion as delayed
afterimages.

This finding is limited to the material-only request. It does not evaluate
constraint-based avatar rigs or other component-driven approximations.

## Options

| Path | Captures actual past mesh pose? | Remote avatar support | Limit |
| --- | --- | --- | --- |
| Material shader alone | No | Yes | No persistent state or mesh-to-history write target. |
| CRT feedback alone | Only texture state, not current skinned mesh | Unverified | Needs a separate mesh capture pass; CRT alone cannot source the renderer pose. Double buffering exposes the prior texture update. |
| Editor fixture with capture camera and texture ring | Yes, for frames explicitly captured | Editor only | Requires orchestration and texture storage; UV overlaps and UV seams make this prototype unsuitable as a general avatar recorder. |
| Runtime camera + ring-copy script | Yes | No consistent remote behavior | Requires custom runtime orchestration, outside supported avatar scripts; remote avatar cameras are disabled in ordinary cases. |
| `TrailRenderer` or particles parented to bones | Stores points/particles, not mesh poses | Allowed component families | Useful motion trails, not whole-avatar silhouette afterimages. |
| Pre-authored animation of a duplicate rig | Stores authored poses | Yes | Cannot follow arbitrary live avatar motion. |

## Material-only capability contract

NXSG should not define or advertise an avatar afterimage node until a
material-only implementation passes all of these checks in the pinned
Unity/VRChat target:

1. Capture actual deformed skinned vertices into persistent history without
   editor/runtime C#, helper cameras, Udon, or externally updated textures.
2. Retain at least two distinct historical poses, then replay a selected pose
   after the source avatar moves again.
3. Preserve mesh connectivity and world pose, including mirrored stereo views,
   with no UV overlap or atlas seam corruption.
4. Update correctly for remote users under ordinary avatar visibility and
   shader fallback settings.

Unity documents CRT feedback and a one-frame skinned vertex buffer, but neither
alone meets this contract. The check is falsifiable: a Unity graphics smoke
must render one known animated skinned mesh, capture several fixed poses using
only the avatar material path, and compare replayed vertex positions and
silhouette against recorded frame references. A rendering difference caused
only by current-pose copies, `_Time` motion, or screen-space distortion fails.

