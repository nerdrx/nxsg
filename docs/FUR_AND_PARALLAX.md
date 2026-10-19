# Fur and parallax examples

`Groomed Fur.nxsg` shows layered shell fur over a Toon base. The sample uses 16 layers, 0.06 length, density 60, thickness 0.45, brown roots, cream tips, and a small groom vector with wind strength 0.2. `Fur Fins.nxsg` enables the optional fin pass with eight shell layers.

`Parallax Tiles.nxsg` sends UV0 through Parallax Occlusion, then into a checker pattern and Toon surface. It starts with a builtin white placeholder. In the Unity editor, select the Parallax Occlusion node and use its height texture picker to assign a height map; the picker updates the resource reference for you.

Fur adds shell passes and draw calls. Shell strand density counts cells per UV0 axis, so cells per UV tile grow with the square of the value. Apparent density depends on the mesh UV layout; it is not strands per square metre. Zero density, length, or mask hides fur. When an unconnected authored value is zero, fur passes are omitted at build time. Zero fin/card opacity also omits the fin/card pass. Connected values keep their passes so they can change at runtime. Zero density makes the Fur Strand Mask output zero. Animated negative length is treated as zero.

Fins add one geometry pass with three edge strips per source triangle. The fin pattern is unchanged; fins approximate grazing silhouettes and have no mesh adjacency. Expand renderer bounds for long fur and review transparent sorting. Distance LOD fades outer shells and redistributes the remaining shells smoothly, but still submits every shell pass. More layers and fins increase geometry cost.

Fins require shader target 4.5 with geometry support. Shell-only fur keeps target 3.5.

## Fur shadows

Fur receives main directional scene shadows by default. The base mesh is the only fur surface that casts scene shadows. Additional pixel lights do not light fur shell or fin overlays.

The optional local self-shadow is a straight strand-volume approximation along the main light direction. It does not match individual strands or produce exact cross-body or bent-fur shadows. Set `Self-shadow samples` to `Off` (default), `Low · 4`, `Medium · 8`, or `High · 16`. `Self-shadow strength` defaults to 1 and `Self-shadow bias` defaults to 0.03. Higher sample counts cost more; this cost is multiplied by active shell overdraw.

## Parallax limits

Parallax Occlusion increases pixel cost with its step count. It needs mesh tangents and changes texture depth only; it does not change the physical silhouette or cast displaced shadows. The sample values are authored examples and are not GPU performance validation.

## Cards-only fur

On Fur, choose **Fur geometry → Cards only**. Three cards are generated from each
selected source triangle's edges, with no fur shells. Coverage follows mesh
topology, and shared edges can overlap. This is a PC geometry pass, not an exported
card mesh. Existing color, length, mask, grooming and wind inputs apply. Card
opacity controls transparency; at zero, the card pass is omitted. Density 100
selects all triangles; larger values change strand pattern density but do not
generate more cards.

Shell LOD controls do not affect cards and are hidden. Root-to-tip color blends linearly along both cards and shells; tip taper changes strand width. Expand renderer bounds; transparent sorting can remain visible. Local self-shadowing is an approximation.
