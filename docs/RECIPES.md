# Material recipes

These are starting points in the package's `Samples~` folder. Copy a graph into
your project's `Assets`, open it in NXSG, replace its placeholder colors or
textures, then build. Keep the original graph editable while you tune it.

## Skin

Start with **Shiny Surface** to study its Subsurface node. Set a restrained
Subsurface strength and tint for the skin you are authoring. The effect uses
wrapped and backlighting approximations, not a measured skin tone LUT. Use a
mesh texture and a mask when different body regions need different response.
Check the result under front, side and rear lights before choosing the final
strength. This sample uses an Unlit Surface because Subsurface supplies its
own lighting response.

For a custom two-dimensional skin color LUT, start with the **Skin Tone LUT**
sample. Its horizontal coordinate is base-color luminance; connect Pigment and
a skin-region Mask, then assign your own LUT texture. An unassigned LUT leaves
the base color unchanged. [Setup and texture contract](SKIN_TONE_LUT.md).
NXSG does not ship calibrated human skin tone data.

## Cloth and velvet

**Velvet Fabric** combines Weave with a Layered PBR Surface's sheen controls.
Replace the base color, then change weave scale, sheen weight, sheen color and
roughness. The independent sheen layer is useful for fabric that should remain
matte at normal incidence and brighten at glancing angles. If you use a
texture mask, feed it into the sheen weight branch.

## Hair and fur

**Groomed Fur** shows a Toon base with root/tip colors and groom direction.
**Fur Cards** and **Fur Fins** show the alternate geometry modes. For flat
hair cards without generated geometry, start with a Toon or PBR surface,
connect a strand texture's alpha to Opacity, and tune cutout before adding
glitter or anisotropic highlights. Verify transparent sorting in a mirror.

## Glass

**Refraction Glass** bends a captured screen image through a normal and
applies a tint. It depends on the scene color capture and behaves differently
on transparent objects and in worlds with unusual render order. For a simpler
opaque glossy surface, use **Lacquered Surface** and its coat controls.

These examples are authoring recipes, not calibrated presets for every avatar
or world. Compare them on the target mesh and lighting setup.
