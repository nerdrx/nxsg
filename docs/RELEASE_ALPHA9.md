# NXSG alpha.9 — negative inputs and small fixes

- Negative numeric inputs compile correctly in HLSL expressions, including Remap, Smoothstep, masks, parallax, tessellation and fur controls. ShaderLab defaults remain plain numbers.
- The basic backend explains when repeated inlined subgraphs exceed its work limit. The limit remains in place to bound generated source growth.
- Every example asset now includes Unity metadata. Existing GUIDs are preserved.
- Nodes added from the library appear in the visible canvas center after pan/zoom.
- The install page calls out the prerelease setting required in ALCOM/Creator Companion.

Validation: portable checks, seven packaging checks, and hidden Unity 2022.3.22f1/OpenGLCore compilation of eleven negative-input graphs. Windows and live VRChat remain unverified.
