# Compatibility

| Area | Current evidence | Boundary |
| --- | --- | --- |
| Unity editor | Unity 2022.3.22f1 on Linux | Other Unity patches are unverified. |
| Graphics | Linux OpenGLCore on the pinned RX 7900 XTX fixture; strict Windows-target D3D11 asset-bundle cross-compilation passed for 16 shaders in Linux Unity | Cross-compilation verifies shader compilation for that bundle; it does not establish native Windows rendering or behavior on other GPUs. |
| Shader target | PC Built-In forward rendering | Mobile/Quest shader support is not provided by this package. |
| Lighting | ForwardBase + ForwardAdd for Toon/PBR base surfaces | Shell/fur overlays retain their existing lighting; additional lights increase draw calls. |
| VRChat SDK | Base/Avatars 3.10.5 fixture imports and builds in the Linux Unity checks | SDK upload and avatar acceptance remain unverified. |
| VRChat client | Creator reports working mirror/headset checks in their setup on 2026-09-25 | Exact client, world and graph set were not recorded; fallback behavior and upload acceptance are not established by that feedback. |
| Stereo/headset | Creator reports working mirror/headset checks in their setup | A reproducible per-eye compatibility matrix remains open. |
| Windows | Windows-target D3D11 shader cross-compilation is covered; no native Windows result is claimed | Native Windows editor/rendering and client validation remain open. |

The current evidence establishes editor workflows and rendered checks in the
Linux OpenGLCore fixture. It does not turn an offscreen render, synthetic input
check, or generated shader compile into proof of Windows, client, or headset
behavior. See [validation records](VALIDATION.md) and [latest checks](GOODIES.md#verification)
for the dated evidence boundary.
