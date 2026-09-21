# Panosphere

Panosphere projects camera-to-surface world directions onto a sphere. It does not
use the mesh UV layout. Connect it to Texture UV; UV Transform and UV Scroll
can control the sampled pattern.

The atan2 longitude boundary needs special filtering treatment even with a
seamless image. Fragment evaluation now selects between two equivalent longitude
charts using their derivatives, avoiding a false coarse mip at that boundary.
Vertex evaluation retains raw coordinates because fragment derivatives do not
exist there. Sampling should use Repeat wrapping and a horizontally seamless
texture. Noninteger horizontal tiling, nonperiodic procedural patterns, Clamp
wrapping and the spherical poles can still introduce discontinuities; this is
not a seamless projection for arbitrary data.

The derivative approach follows [Poiyomi 10.0.22 source](https://github.com/poiyomi/PoiyomiToonShader/blob/5ef04e1fc03dea566f891906ac37f1ee160bfaaf/_PoiyomiShaders/Shaders/10.0/Toon/Poiyomi%20Toon%20World.shader),
checked 2026-09-21. NXSG retains its existing orientation and scale. The package
includes the upstream MIT notice. Separate stereo panorama controls are not
implemented; headset behavior is unverified.

The chart comparison includes a tolerance: without it, rounding differences in
otherwise equal derivatives can select different UV charts in adjacent pixels.
This caused speckling in alpha.14–15 and is fixed in alpha.16. A separate GPU
regression covers fractional tiling away from the wrap.

On **Polar UVs**, Input coordinates chooses what enters the polar transform.
It does not replace the operation. Use the node header to select plain Panosphere.
