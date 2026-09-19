# NXSG alpha.8 — fur shadows

- Fur shells and fins receive scene shadows from the main directional light. Toggle this in Fur → Shadows.
- Optional self-shadowing samples the local fur volume along the light direction. Choose Off, Low (4 samples), Medium (8) or High (16), with strength and bias controls.
- Fur lighting respects the main light color and colored ambient lighting. Ambient and rim light remain outside the shadow multiplier.
- Existing graphs default to scene-shadow reception on and self-shadowing off.

Self-shadowing approximates a straight local fur volume; it does not trace bent strands or fur elsewhere on the body. The base mesh casts scene shadows. Additional pixel lights still affect the base surface only. Fin strand patterns are unchanged. Fins use shader target 4.5 plus geometry support; shell-only fur keeps target 3.5.

Validated with portable checks and hidden Linux Unity 2022.3.22f1/OpenGLCore renders: separate shells/fins, hard and soft/cascaded scene shadows, all self-shadow quality levels, and zero-strength equivalence. Windows, headset and live VRChat checks remain open.
