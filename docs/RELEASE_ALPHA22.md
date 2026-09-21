# NXSG alpha.22 — numeric particle inputs

Every numeric Surface Particles setting now has a socket: Density, Emission rate,
Size, Lifetime, Speed, Gravity and Spread join the existing Opacity, Mask and Time.
Connect values, math, animation or compatible AudioLink inputs. Unplugged sockets
keep the existing inspector values; connected fields show their input state.

Wired rate/lifetime drive adaptive tessellation up to level 64. High rates remain
expensive, and changes retime the procedural particles rather than preserving
previously emitted particles. Geometry-stage controls cannot use fragment-only
nodes such as derivative-based glitter. Blending and UV color mode remain choices.

Update NXSG, reopen the graph and connect the new sockets. Build for VRChat to
regenerate the shader before upload. Existing graphs keep their stored settings.
