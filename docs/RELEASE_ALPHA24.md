# NXSG 0.1.0-alpha.24

## Materials and particles

- Surface Particles uses separate alpha blending so soft particles do not reduce the opacity of an opaque render target. Local opaque/transparent render-target tests pass; the reported VRChat mirror still needs an in-client retest.
- Size, color, and opacity curves shape a particle over its normalized lifetime. Curves multiply existing inputs; default curves leave old graphs unchanged. Scalar curves support 2–16 linear points and the color gradient supports 2–8 combined stops.
- Particle Info outputs normalized age and stable random values for Surface Particles. Use them in appearance, size, or motion branches. Base shading and emission timing/budget inputs cannot use particle lifetime data.
- AudioLink optionally maps gain-scaled, clamped audio between Minimum and Maximum. Existing gain behavior remains the default. Fallback remains a literal output when AudioLink is unavailable.
- Texture nodes expose an explicit Alpha output.

## Graph editor

- Float and Color nodes can be edited directly on the canvas.
- Find Existing Nodes searches this graph separately from the node library.
- Frames organize selected nodes with a name and note.
- Socket tooltips explain ranges, units, and connected defaults.
- Examples opens a small gallery with editable copies of fur, glitter, hologram, audio, and particle graphs. Original package samples are preserved.

Update through ALCOM or Creator Companion with prereleases enabled. Rebuild existing shaders to apply the particle blending fix.
