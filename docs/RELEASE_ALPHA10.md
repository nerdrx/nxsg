# NXSG alpha.10 — glitter and automatic grayscale

- New Glitter node with UV-stable flakes, view-angle sparkle, time-driven twinkle, tint, HDR brightness and a separate 0–1 Value mask.
- Connect its Color to Emission/Albedo, or Value to Opacity/Mix. Includes a Glitter Fabric example.
- Color outputs now connect to numeric sockets automatically using RGB luminance. Other connections retain the original color. Use Split Color → A for alpha instead.
- Compatible connections use the existing socket menus and wire color gradients.

Validated with portable/packaging checks and hidden Linux Unity 2022.3.22f1/OpenGLCore pixels: zero mask/density/size, HDR scaling, independent mask range, viewing-angle response, animation and RGB luminance. Glitter is fragment-only and approximates decorative flakes; it does not evaluate scene-light reflections. Windows and live VRChat remain unverified.
