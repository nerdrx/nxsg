# NXSG alpha.17 — optional albedo transparency

Toon, Unlit and PBR surfaces now have **Use albedo alpha**.

- On: multiply albedo texture/color alpha by Opacity and material Tint alpha.
- Off: ignore albedo alpha; Opacity and material Tint alpha still apply.

The switch applies to the base, additional-light, shell and shadow-caster paths.
It defaults on to preserve existing advanced-surface behavior. Stored graphs
without the property retain their existing backend selection.

Update, select the surface node, disable Use albedo alpha, then Build for VRChat.
With Opacity 1 and opaque material Tint, transparent albedo pixels become solid.
