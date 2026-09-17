# Built-in node pack

The canvas now offers 20 nodes. Socket color indicates data type: yellow color, gray scalar, blue UV coordinates, green surface. Drag from either end; compatible-node menus and clipboard operations use the same core catalog.

## New nodes

| Node | Inputs → output | Controls/defaults |
|---|---|---|
| Value | scalar output | Value, initially 0 |
| Time | scalar output | `_Time.y × speed + offset`; speed 1, offset 0 |
| UV Transform | UV → UV | Tiling (1,1), offset (0,0); unconnected UV uses UV0 |
| Polar UVs | UV → UV | Center (0.5,0.5), radial scale 1, angular repeats 1; U = twice distance from center, V = angle in turns + 0.5. At the center, V is 0.5. |
| Rotate UVs | UV, scalar angle → UV | Center (0.5,0.5), angle 0 degrees; connected angle overrides the control. Positive angles rotate coordinates counterclockwise, so the sampled image rotates clockwise. |
| Object Planar UVs | UV output | Local mesh X/Z coordinates; follows object transforms, not an undeformed skin bind pose. |
| World Planar UVs | UV output | World X/Z coordinates; objects move through the pattern. |
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

Try **Assets/NXSGExamples/Polar Palette.nxsg** in the development project for rotating radial noise. The distributable copy is in `Samples~/Polar Palette.nxsg`.

## Coordinate recipes

- Rings/radial patterns: **Polar UVs → Texture UV**. Use a repeating stripe texture; U runs outward, V runs around the center.
- Spin: **Time → Rotate UVs angle**, then **Rotate UVs → Texture UV**. Time speed is degrees per second here.
- World projection: **World Planar UVs → UV Transform → Texture UV**. Tiling controls repeats per world unit.
- Object projection: use **Object Planar UVs** for a projection that follows object movement.

Polar UVs have an angular seam and a singular center; texture filtering can reveal these. Use repeat wrapping for angular repetition. Planar projection uses X/Z only and stretches on side-facing surfaces; this is not triplanar mapping. UV operations also work with Noise. Unconnected Polar/Rotate UV inputs use UV0.

## Switch math operations

Click a math node's title (marked ▾) to switch between **Add, Multiply, and Mix**, or between **Invert and Clamp**. Drag the same header to move it. Keyboard users can focus the header and press Enter or Space. Node identity, position, compatible wires, and settings are retained. Inputs absent from the new operation are disconnected; the status message reports this, and Undo restores the operation and wires together. Switching back retains the previous Mix factor value, but does not automatically reconnect a removed factor wire.
