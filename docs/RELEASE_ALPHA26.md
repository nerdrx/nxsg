# NXSG 0.1.0-alpha.26

## Raymarching

- Volume Surface with volumetric integration and solid SDF surface tracing.
- Ray Position, SDF Sphere, SDF Box, SDF Torus and SDF Blend (smooth union, subtraction, intersection).
- Existing 3D/4D noise, ramps, colors and AudioLink can drive the march graph.
- Bounded 8–128 steps, maximum travel, local box bounds, opacity early exit, and opt-in camera depth clipping.
- Three editable gallery examples and actual Unity-rendered showcase pictures.

Use a closed cube proxy with matching local bounds. This does not infer an arbitrary avatar mesh's interior or provide whole-avatar self-shadowing. Solid mode uses ambient and the main directional light; it has no shadow/depth-writing pass. Expensive examples use 128 steps; start with 32 for everyday use. Depth clipping needs a valid camera depth texture. Client/headset and mirror validation remain separate.

See [the volume guide](https://github.com/nerdrx/nxsg/blob/main/docs/VOLUMES.md).

## Development

Automated Unity compile/smoke checks now have a timeout and process cleanup. Interactive editor sessions remain untimed.
