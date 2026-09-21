# NXSG alpha.21 — particles on zero-color mesh regions

Surface Particles no longer multiply their color and opacity by source mesh
vertex colors automatically. Black vertex RGB or zero vertex alpha could hide
all particles on a material region even with Emitter mask and Opacity set to 1.
Disconnecting the mask could not bypass that hidden multiplication.

Particle lifetime fading is stored separately. Use an explicit **Vertex Color**
node when vertex colors should control Albedo, Emission or Mask. Existing graphs
that relied on implicit vertex tinting need that explicit connection now.

Update NXSG and click **Build for VRChat** on affected graphs. The texture mask
can remain connected if desired; this fix does not change its intended behavior.

GPU tests compare white vertices with black, zero-alpha vertices and verify that
explicit vertex-color masks still hide particles. Windows-target shader bundle
compilation also passed. The uploaded avatar still needs an in-client retest.
