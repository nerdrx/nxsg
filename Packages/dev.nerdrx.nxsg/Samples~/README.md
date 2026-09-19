# NXSG examples

Copy `Animated Palette.nxsg` into your project's Assets folder, open it, and choose **Build for VRChat**. The graph blends warm and cool colors using animated noise, with a small emission contribution. The Time node controls animation speed. Emission adds unlit color; a bloom halo depends on the world's post-processing.

Noise currently animates by moving through a smooth noise field. It is not true 4D noise. This is a PC Built-In prototype; mobile and live VRChat validation remain separate.

`Polar Palette.nxsg` adds Rotate UVs and Polar UVs before the noise. The spin Time node uses degrees per second; the other Time node moves the noise. Polar mapping has an angular seam. Both samples are also available in the development project's `Assets/NXSGExamples`.

`Noise Ramp.nxsg` routes Noise through a numeric Ramp before Mix. Adjust the Ramp black/white points, curve, and smoothing to reshape the mask.

`Fur Fins.nxsg` enables the optional Fur fin pass over a short shell stack. Fins add silhouette strips and one geometry pass; inspect bounds, transparency sorting, and cost on the target mesh.

`Shiny Surface.nxsg` chains Iridescence and the Subsurface approximation. Both are artistic Built-In lighting effects. Iridescence changes with view angle; Subsurface responds to main-light direction and thickness.

`Refraction Glass.nxsg` samples the Built-In GrabPass screen texture. It needs a regular mesh surface and transparent sorting; it does not trace scene geometry and is not a VR or Windows acceptance test.

`Interior Bomb.nxsg` combines room-atlas Interior Mapping with seeded Texture Bomb sampling. Assign a tiled atlas in the texture pickers to see room variation. The shipped `builtin://white` resource keeps the graph portable and valid before assignment.

These samples target Unity 2022.3.22f1, PC Built-In shader generation on Linux. They prove graph validation and emission paths when the portable checks pass; they do not prove native Windows, VR headset, VRChat client, or GPU performance behavior.

## Self-contained material studies

- **Showcase Hologram:** animated scanlines above a dark PBR base, tinted violet/cyan.
- **Showcase Pearl:** a restrained iridescent PBR finish with a close shell layer.
- **Showcase Warm Fur:** dense short shell fur with root/tip color and gentle movement. Uses 24 shell layers; measure its cost on your intended avatar and target.

These graphs need no external textures. Render previews use simple meshes; they are not avatar/client validation.
