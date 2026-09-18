# Fur and parallax examples

`Groomed Fur.nxsg` shows layered shell fur over a Toon base. The sample uses 16 layers, 0.06 length, density 60, thickness 0.45, brown roots, cream tips, and a small groom vector with wind strength 0.2.

`Parallax Tiles.nxsg` sends UV0 through Parallax Occlusion, then into a checker pattern and Toon surface. It starts with a builtin white placeholder. In the Unity editor, select the Parallax Occlusion node and use its height texture picker to assign a height map; the picker updates the resource reference for you.

Fur adds shell passes and draw calls. It has no fins, expanded bounds, or fur self-shadowing. LOD settings reduce work at distance, but every active LOD still draws shell passes. More layers increase geometry cost.

Parallax Occlusion increases pixel cost with its step count. It needs mesh tangents and changes texture depth only; it does not change the physical silhouette or cast displaced shadows. The sample values are authored examples and are not GPU performance validation.
