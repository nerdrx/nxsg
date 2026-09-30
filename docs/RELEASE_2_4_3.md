# NXSG 2.4.3 — correct textures in mobile bakes

Fixed a texture mix-up in **Bake for Mobile**. When a branch was isolated for a snapshot, a different texture could become `_MainTex`. The bake then copied the original material's `_MainTex` by property name, even though that slot belonged to another graph resource. Texture overrides now follow the named graph resource across the full and isolated shaders. Static node baking uses the same fix.

This fixes the reported NixomiBody case: its `Main` albedo was replaced by the `ShimmerMaslk` image. An isolated Unity render using the real Body graph and both source images now produces the purple Body texture. A separate two-texture regression test also checks that the correct resource wins when `_MainTex` changes meaning. The mobile bake smoke, including maps and desktop/mobile material swapping, passed in hidden Unity 2022.3.22f1.

Update NXSG, select each desktop material, and click **Bake for Mobile** again. The existing baked PNGs do not update automatically. Android upload and headset appearance remain unverified. [Mobile baking guide](https://github.com/nerdrx/nxsg/blob/main/docs/MOBILE_BAKING.md).
