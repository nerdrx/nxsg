# NXSG alpha.13 — coordinate choices in node headers

Click a UV node's header to choose Mesh UV0–3, Object XZ, World XZ, Polar,
Panosphere or Matcap. The header shows the selected coordinate source.
UV Transform, UV Scroll, Rotate UVs and the configurable planar/polar nodes
remain available. Compatible outgoing wires are retained; Undo restores changes.

Panosphere projects textures using the camera-to-surface direction on a sphere.
Connect Panosphere to Texture UV, or add UV Transform / UV Scroll between them
for tiling, offset and animation. This is the same basic projection described in
[Poiyomi's documentation](https://www.poiyomi.com/modifiers/uvs/panosphere-uv),
implemented by NXSG. It does not expose Poiyomi's separate stereo panorama controls.

Validation: portable checks and hidden Unity 2022.3.22f1 menu and rendering tests.
VR headset appearance has not been tested in this release.
