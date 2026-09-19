# Fur and parallax examples

`Groomed Fur.nxsg` shows layered shell fur over a Toon base. The sample uses 16 layers, 0.06 length, density 60, thickness 0.45, brown roots, cream tips, and a small groom vector with wind strength 0.2. `Fur Fins.nxsg` enables the optional fin pass with eight shell layers.

`Parallax Tiles.nxsg` sends UV0 through Parallax Occlusion, then into a checker pattern and Toon surface. It starts with a builtin white placeholder. In the Unity editor, select the Parallax Occlusion node and use its height texture picker to assign a height map; the picker updates the resource reference for you.

Fur adds shell passes and draw calls. Fins add one geometry pass with three edge strips per source triangle. Fins approximate grazing silhouettes; they have no fur self-shadowing or mesh adjacency. Expand renderer bounds for long fur and review transparent sorting. LOD settings reduce shell layers at distance, but every active LOD still draws a pass. More layers and fins increase geometry cost.

Parallax Occlusion increases pixel cost with its step count. It needs mesh tangents and changes texture depth only; it does not change the physical silhouette or cast displaced shadows. The sample values are authored examples and are not GPU performance validation.
