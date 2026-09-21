# NXSG alpha.14 — Panosphere filtering fix

Panosphere now corrects longitude derivatives at the projection wrap. This removes
the coarse-mipmap stripe that could appear even with a seamless, repeating texture.
The projection keeps its existing orientation. Update NXSG, reopen the graph and
Build for VRChat to regenerate the shader.

The fix follows Poiyomi's derivative-aware seam handling approach; the package
includes attribution and the MIT notice. See [Panosphere notes](PANOSPHERE.md)
for remaining constraints (including noninteger tiling and spherical poles).

A GPU regression test reproduces the old stripe with a seamless mipmapped texture
and checks the corrected output. VR headset behavior remains untested.
