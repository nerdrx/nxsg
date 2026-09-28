# AudioLink data nodes

Research checked 2026-09-28 against AudioLink `master` revision
[`5dafeb36927ba6b0c94d782996e2b63285fab737`](https://github.com/llealloo/audiolink/tree/5dafeb36927ba6b0c94d782996e2b63285fab737).
The official [shader guide](https://github.com/llealloo/audiolink/blob/5dafeb36927ba6b0c94d782996e2b63285fab737/Docs/README.md)
and [`AudioLink.cginc`](https://github.com/llealloo/audiolink/blob/5dafeb36927ba6b0c94d782996e2b63285fab737/Packages/com.llealloo.audiolink/Runtime/Shaders/AudioLink.cginc)
document the texture layout and value formats. AudioLink's
[`MIT license`](https://github.com/llealloo/audiolink/blob/5dafeb36927ba6b0c94d782996e2b63285fab737/LICENSE)
was reviewed. NXSG samples the documented layout using independently written
helpers; it does not include AudioLink or third-party shader code.

The node definitions, shader helpers, graph catalog, validation, emitter, and
editor controls are integrated. Nodes:

| Node | Inputs and behavior |
| --- | --- |
| Audio Spectrum Bars | UV, logarithmic frequency range, bar count, gap, gain, and horizontal/radial layout. Output is a scalar mask suitable for color ramps, opacity, or emission. |
| Audio Spectrum | Frequency in hertz; reads one of 240 DFT bins at 24 bins per octave starting at 13.75 Hz. Fractional bins interpolate. |
| Audio Spectrum Bin | Reads a DFT bin from 0 to 239; fractional bins interpolate. |
| AudioLink Chronotensity | Selects one of eight accumulated-time motion modes and one of four frequency bands. Speed scales the returned time; optional wrapping returns `[0,1)`. |
| AudioLink Theme Color | Selects one of the four theme colors stored by the world. |

DFT channel choices are raw magnitude, EQ magnitude, and ColorChord-filtered
magnitude. The fourth channel is phase and is not exposed as an amplitude.
Frequency input spans 13.75–14080 Hz.

Chronotensity mode labels follow AudioLink's documented modes:

| Index | Motion |
| ---: | --- |
| 0 | Moves forward as the selected band's intensity increases. |
| 1 | Same motion using filtered band intensity. |
| 2 | Moves back and forth with band intensity. |
| 3 | Same motion using filtered band intensity. |
| 4 | Advances at a fixed rate while the band is quiet; stationary while loud. |
| 5 | Same motion using filtered band intensity. |
| 6 | Advances at a fixed rate while quiet and reverses at a fixed rate while loud. |
| 7 | Same motion using filtered band intensity. |

Band indices 0–3 are Bass, Low mids, High mids, and Treble. Chronotensity data
is decoded as AudioLink's base-1024 packed unsigned integer and scaled by
`1/100000`, matching `AudioLinkGetChronoTime`. Wrapped mode applies a fractional
wrap after speed scaling.

Audio Spectrum checks for a 128×6 texture, Chronotensity for 24×32, and Theme
Color for 4×24. Undersized or missing providers return each node's fallback.
Temporary node previews use `_NXSG_AudioDataPreview` and separate spectrum,
chronotensity, and theme-color preview values. Texture dimensions confirm a
compatible texture is bound; they do not verify that an AudioLink producer is
live or updating.

The portable data contract test runs with:

```sh
dotnet run --project Tests/AudioDataContract/AudioData.Contract.csproj
```

The Unity render smoke test runs in an isolated graphics-enabled editor project
with `-executeMethod AudioDataRenderSmoke.Run`.
