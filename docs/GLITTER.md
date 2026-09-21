# Glitter

Add **Textures → Glitter**. It makes flakes in UV space that brighten as the
viewing angle changes. Time can add independent twinkling.

- **Color → Emission** adds bright colored glitter. HDR brightness can exceed 1.
- **Color → Albedo** colors the surface with the glitter pattern; use Add or Mix
  to combine it with an existing texture.
- **Value → Opacity / Mix factor** uses the 0–1 glitter mask. Brightness affects
  Color only. Use a transparent surface when you want alpha blending.
- Plug textures or colors into Color; plug masks into Mask. The inspector
  provides a white color fallback.

Flake scale controls how many cells fit across the UVs; size and density control
coverage. Sharpness tightens each flake's view-angle highlight. Set View angle
strength to 0 for an even pattern. Twinkle amount 0 freezes blinking; connect
Time for explicit animation control. UV options and incoming UV distortion work
like other texture nodes. Tiny distant flakes fade to reduce aliasing.

`Glitter Fabric.nxsg` is a ready-to-edit example. This node uses a fixed nine-cell
neighborhood search and fragment derivatives: it adds pixel work, no textures or
extra passes. It cannot feed vertex displacement. The effect uses surface
normals and tangents, not a connected normal map or scene-light reflections.

## Color into numeric sockets

Drag a texture's Color directly into Opacity, a mask, or another numeric socket.
NXSG converts RGB brightness on that connection, with weights 0.2126 / 0.7152 /
0.0722. Alpha is ignored; use Split Color → A if you want texture transparency.
Other branches retain the original color. General conversion is not clamped;
individual mask and opacity inputs apply their own limits.

[Poiyomi's Glitter documentation](https://www.poiyomi.com/special-fx/glitter)
provided feature context. NXSG's implementation is independent and does not
reproduce every Poiyomi option.
