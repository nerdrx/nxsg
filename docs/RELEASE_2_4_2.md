# NXSG 2.4.2 — LTCGI mobile passthrough

Mobile snapshots now pass through an LTCGI node's connected input color while omitting the world lighting calculation. If no color is connected, the node contributes black. This keeps texture detail supplied to LTCGI without requiring the LTCGI package during mobile baking.

This builds on [2.4.1](https://github.com/nerdrx/nxsg/releases/tag/v2.4.1), which introduced timed snapshots of connected albedo, emission, normal, metallic, roughness and occlusion branches. [Mobile baking guide](https://github.com/nerdrx/nxsg/blob/main/docs/MOBILE_BAKING.md).

Validation: hidden Unity 2022.3.22f1 smoke for LTCGI color passthrough, mobile maps and build-target material swapping, plus isolated rendering of the real NixomiBody albedo, emission and roughness branches. Android upload and headset appearance remain unverified.
