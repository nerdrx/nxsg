# AudioLink implementation note

Retrieved 2026-09-18 (Europe/Berlin). Source revision: AudioLink `master` at
[`4aeb39503ce94cb250ed7570ee585e1be9cdb3aa`](https://github.com/llealloo/audiolink/tree/4aeb39503ce94cb250ed7570ee585e1be9cdb3aa).

The official AudioLink documentation describes a 128 x 64 RGBA texture. The
current four bands are at `(0, 0..3)`. Filtered AudioLink values occupy a
16 x 4 block at `(0, 28)`: x `0..15` selects smoothing, y `0..3` selects bass,
low-mid, high-mid, and treble. The same layout is defined by
[`AudioLink.cginc`](https://raw.githubusercontent.com/llealloo/audiolink/4aeb39503ce94cb250ed7570ee585e1be9cdb3aa/Packages/com.llealloo.audiolink/Runtime/Shaders/AudioLink.cginc)
and documented in the project's [shader guide](https://github.com/llealloo/audiolink/blob/4aeb39503ce94cb250ed7570ee585e1be9cdb3aa/Docs/README.md).

`Backend/AudioLinkShader.cs` embeds a package-free Built-In shader helper. It
declares `Texture2D<float4> _AudioTexture`, uses `GetDimensions` outside OpenGL and `_AudioTexture_TexelSize` on OpenGL, following the official include’s availability branch (`width > 16`), and reads exact
integer pixels with `Load`. `_NXSG_AudioLinkPreview` and
`_NXSG_AudioLinkValue` provide deterministic editor preview data. The graph
contract supplies normalized smoothing (`0..1`), mapped inversely to
AudioLink's 16 levels (`0..15`): UI value 0 reads the raw current band at `(0, band)`; positive smoothing maps inversely across the filtered block,
and UI value 1 is most smoothed (x=0).

The helper reads the filtered path for positive smoothing. It
requires height 32 or greater because the official filtered block starts at y
28 and contains four rows. Legacy AudioLink textures wider than 16 pixels may
pass the official availability check but cannot satisfy this filtered-node
contract and therefore use the fallback.

The dimensions check cannot prove that a correctly sized texture contains a
live AudioLink producer. A connected but stale or black texture therefore
reads as zero by design. Runtime availability remains the responsibility of
the AudioLink scene/provider.
