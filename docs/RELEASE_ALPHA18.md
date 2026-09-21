# NXSG alpha.18 — lighting influence

Toon and PBR surfaces now expose Minimum brightness, Maximum brightness and
Lighting saturation.

- Minimum brightness lifts dark lighting. It is not added again for each extra light.
- Maximum brightness limits each lighting contribution; 0 means unlimited.
- Lighting saturation: 0 for neutral light, 1 for unchanged color, above 1 for stronger color.

These controls affect illumination before it is applied to the material. Albedo
and emission colors stay intact. Defaults (0, 0, 1) preserve current lighting.
If minimum exceeds a nonzero maximum, the maximum wins.

Unity's additional lights render in separate passes, so their sum may exceed the
maximum. This is not a final image brightness cap. Unlit surfaces do not expose
lighting controls. Fur overlays retain their own lighting controls.

Update NXSG, select a Toon or PBR surface and expand its inspector. Build for
VRChat to update the material shader.
