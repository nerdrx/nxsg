# NXSG 2.4.5 — mobile bake handles desktop-only surface controls

**Bake for Mobile** now completes when the desktop surface has opacity, albedo alpha, cutout, or displacement controls. The mobile material uses opaque albedo and omits displaced geometry, while the desktop graph and material stay unchanged. The bake result names these approximations instead of asking you to disconnect nodes.

The focused Unity 2022.3.22f1 smoke passed in hidden Gamescope. It checked the mobile material swap, transparent source pixels becoming opaque without losing color, reported omissions, and unchanged desktop graph data. Android upload and headset appearance remain unverified. Update NXSG and rebake affected materials. [Mobile baking guide](MOBILE_BAKING.md).
