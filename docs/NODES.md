# Built-in node pack

The canvas now offers 16 nodes. Socket color indicates data type: yellow color, gray scalar, blue UV coordinates, green surface. Drag from either end; compatible-node menus and clipboard operations use the same core catalog.

## New nodes

| Node | Inputs → output | Controls/defaults |
|---|---|---|
| Value | scalar output | Value, initially 0 |
| Time | scalar output | `_Time.y × speed + offset`; speed 1, offset 0 |
| UV Transform | UV → UV | Tiling (1,1), offset (0,0); unconnected UV uses UV0 |
| UV Scroll | UV, scalar time → UV | Speed (0.1,0); unconnected time uses shader time |
| Noise | UV, scalar time → grayscale color and scalar value | Scale 5, speed 1; unconnected UV/time use UV0/shader time |
| Add | colors A/B → color | Missing inputs are black |
| Mix | colors A/B, scalar factor → color | Missing A is black, B is white, factor is 0.5; factor clamps to 0–1 |
| Emission | color, scalar strength → color | White and strength 1 by default; connect to Toon Surface's emission socket |
| Invert | color → color | Inverts components; missing input is black |
| Clamp | color → color | Clamps components to 0–1; missing input is black |

The original UV Coordinates, Texture, Color, Multiply, Toon Surface, and Output remain available. Multiply uses white for missing inputs. Connected factor/strength sockets override their inspector defaults.

## Try it

`Packages/dev.nerdrx.nxsg/Samples~/Animated Palette.nxsg` blends warm and cool colors using Noise's scalar output, then adds emission. Copy it into Assets and build. The local development project includes a copy under `Assets/NXSGExamples`.

## Current boundaries

- One **reachable** texture node per compiled graph. It can feed math, Mix, or emission. Extra disconnected texture nodes are ignored; multiple connected textures produce an explicit diagnostic.
- Noise is smooth 2D value noise with a moving sampling position, not true evolving/4D noise.
- Emission adds unlit color. Bloom halos depend on the world's post-processing.
- Build updates the material; editing the graph does not yet rebuild it automatically. Shader Time animates the built shader when the rendering environment advances shader time.
- PC Built-In backend only; VRChat client, headset, and mobile acceptance remain separate checks.
