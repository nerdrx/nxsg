# Additional pixel lights

Toon and PBR base surfaces emit a Built-In `ForwardAdd` pass. Unity runs it for each additional per-pixel light affecting the renderer. No graph switch is needed: rebuild existing graphs to regenerate their shaders.

- The additive pass evaluates direct light only. Ambient light, reflection probes and emission remain in the base pass.
- Point and spot distance/cone attenuation, cookies and realtime shadow variants use Unity's built-in lighting macros.
- Unlit surfaces remain unlit. Extra passes preserve the base geometry, normal mapping and alpha clipping.
- Set a scene light's Render Mode to **Important** to force per-pixel selection, or adjust **Pixel Light Count** for automatic selection. Every additional pixel light adds a draw pass per affected renderer.
- Shell and fur overlay passes keep their existing lighting; only their lit base surface receives this new pass. A compiler diagnostic makes that boundary explicit.
- Native Windows/D3D, VRChat client and stereo/headset validation remain pending.

Reference: [Unity 2022.3 Forward rendering](https://docs.unity3d.com/2022.3/Documentation/Manual/RenderTech-ForwardRendering.html), checked 2026-09-19. The implementation uses the installed Unity 2022.3.22f1 `AutoLight.cginc` macro contract; no upstream shader source was copied.
