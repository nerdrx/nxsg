# Tessellation and displacement

Add **Tessellation** from Surface, connect your Toon, Unlit or PBR surface to **Base**, then connect its output to **Output**. Connect a numeric pattern or mask to **Height**. The included **Tessellated Bumps** example uses stationary noise.

```text
Noise / mask ───────── Height
Toon / Unlit / PBR ─── Base [Tessellation] ─── Output
```

For a height map, add **Texture → Split Color**, connect **R** to Height, and disable sRGB on the height texture import. Black/white values are interpreted relative to **Reference height**. Displacement is `(height − reference) × strength` along the interpolated mesh normal, in object units. The surface's existing Displacement input remains additive.

| Control | Meaning |
| --- | --- |
| Tessellation factor | Subdivision detail near the camera; start around 4–12. Range 1–63. |
| Minimum factor | Detail beyond the far distance; 1 gives the original triangles. |
| Near / far distance | Camera-distance interval over which subdivision detail fades. |
| Strength | Size of height displacement. Negative values reverse it. |
| Reference height | The height value that causes no additional displacement; default 0.5. |
| Smoothing | Optional Phong projection toward the mesh's vertex-normal planes; 0 preserves its linear shape. |

Subdivision happens before displacement, so newly generated vertices can reveal details that were absent from a coarse mesh. Fractional odd partitioning lets detail vary continuously; edge factors depend on shared endpoint positions to agree between adjacent triangles. Matching edge factors cannot repair discontinuous UV height values or split normals at mesh seams.

This is a PC GPU feature requiring tessellation support (shader target 4.6). It adds real geometry work and does not change the saved mesh, collider, or renderer bounds. Expand renderer bounds when displacement would leave them. Displaced positions do not automatically regenerate height-map normals; author the Normal input separately when needed. High factors across dense meshes are expensive.

## Supported compositions

Use one Tessellation node directly before Output. It supports Toon, Unlit, PBR and a Shell stack, with matching base shadow displacement. Fur and Surface Particles composition is not supported in this version. Those effects keep their existing rendering paths.

## Sources

Implementation targets the pinned Unity 2022.3.22f1. Consulted 2026-09-19: [Unity tessellation and displacement stages](https://docs.unity3d.com/2022.3/Documentation/Manual/SL-SurfaceShaderTessellation.html), [Microsoft tessellation partition ranges and domain stages](https://learn.microsoft.com/en-us/windows/win32/direct3d11/direct3d-11-advanced-stages-tessellation). No upstream shader source is bundled.
