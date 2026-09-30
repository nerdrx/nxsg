# NXSG 2.4.4 — black fallback for mobile albedo

**Bake for Mobile** now uses opaque black when the surface has no connected albedo input. It writes a 1×1 black texture and continues baking any other connected channels instead of stopping with “Connect a color branch to the surface albedo before baking.”

The hidden Unity 2022.3.22f1 smoke passed for the disconnected-albedo case, including black RGB and full alpha, alongside the existing mobile-map and material-swap checks. Android upload and headset appearance remain unverified. Update NXSG and rebake the affected material. [Mobile baking guide](https://github.com/nerdrx/nxsg/blob/main/docs/MOBILE_BAKING.md).
