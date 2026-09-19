# Compatibility

| Area | Current evidence | Boundary |
| --- | --- | --- |
| Unity editor | Unity 2022.3.22f1 on Linux | Other Unity patches are unverified. |
| Graphics | Linux OpenGLCore on the pinned RX 7900 XTX fixture | This does not establish Windows/D3D or other GPU behavior. |
| Shader target | PC Built-In forward rendering | Mobile/Quest shader support is not provided by this package. |
| Lighting | ForwardBase + ForwardAdd for Toon/PBR base surfaces | Shell/fur overlays retain their existing lighting; additional lights increase draw calls. |
| VRChat SDK | Base/Avatars 3.10.5 fixture imports and builds in the Linux Unity checks | SDK upload and avatar acceptance remain unverified. |
| VRChat client | No live client result is claimed | Client rendering, fallback behavior, and upload acceptance remain open. |
| Stereo/headset | No headset or per-eye result is claimed | Mirror and stereo parity require a separate integration check. |
| Windows | No native Windows/D3D result is claimed | Windows editor and client validation remain open. |

The current evidence establishes editor workflows and rendered checks in the
Linux OpenGLCore fixture. It does not turn an offscreen render, synthetic input
check, or generated shader compile into proof of Windows, client, or headset
behavior. See [validation records](VALIDATION.md) and [latest checks](GOODIES.md#verification)
for the dated evidence boundary.
