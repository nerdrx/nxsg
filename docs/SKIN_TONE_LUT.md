# Skin Tone LUT

**Skin Tone LUT** maps a base color to an authored 2D color table. The horizontal coordinate is clamped linear RGB luminance (0 dark, 1 bright). The vertical coordinate is **Pigment** (0 to 1). The sampled RGB becomes the target color; **Mask × Strength** blends toward it. The base alpha is preserved. Connect a skin-region mask to avoid recoloring other surfaces.

Author the table with dark-to-light tones from left to right and pigment variants from bottom to top. Use a linear texture, Clamp wrapping, bilinear filtering, no mipmaps, and no lossy compression. A table of at least 64 × 16 pixels is practical. This is an artistic tone lookup, not a physical skin model or a copy of another shader's LUT.

The graph texture picker supplies the table. With no assigned LUT asset, the node returns the base color exactly, even when Strength is 1. Assign the texture in the graph editor and rebuild the material to enable the lookup. The node is fragment-only. `Samples~/Skin Tone LUT.nxsg` shows the neutral setup; assign your own table to see its effect.
