# NXSG 0.1.0-alpha.3 — additional pixel lights

Toon and PBR base surfaces now receive additional pixel lights through ForwardAdd. Point/spot attenuation, cookies and full-shadow variants use Unity lighting macros. Emission, ambient and reflection probes are not repeated per light; Unlit remains unaffected. Rebuild existing graphs after updating.

Includes a shadow varying collision fix for wireframe graphs and preserved tessellation/displacement in the additive pass. Shell/fur overlays retain their existing lighting with a scoped compiler warning. Each additional light adds rendering work.

Linux Unity 2022.3.22f1/OpenGLCore render checks cover point falloff, spots, spot cookies, colored additive lights, emission isolation and Unlit invariance. Native Windows, live VRChat and stereo/headset validation remain pending. See docs/PIXEL_LIGHTS.md.
