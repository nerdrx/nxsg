# LTCGI Lighting

Optional receiver integration for [LTCGI by _pi_](https://github.com/PiMaker/ltcgi). NXSG does not bundle or automatically install LTCGI.

Use Linear color space as recommended by LTCGI.

1. Install `at.pimaker.ltcgi` using the [official guide](https://ltcgi.dev/).
2. Add **LTCGI Lighting** from the Surface category.
3. Connect its **Color** output to your surface's **Emission**. Use Add to combine it with existing emission.
4. Connect the same albedo and tangent-space normal used by your surface; match roughness/metallic where appropriate. Strength controls the contribution.
5. Rebuild the graph. The generated material advertises the `LTCGI=ALWAYS` receiver tag.

The world needs an active LTCGI controller and emitters configured to affect avatars. The node receives lighting; it does not turn your avatar into an LTCGI emitter. An empty scene/controller with no sources provides no light.

The first integration uses LTCGI's compatibility diffuse/specular API in avatar mode. It computes dynamic area lighting rather than baked world lightmaps, with a simple metallic tint weighting. It is an additive emission contribution, not a replacement for the surface BRDF or Unity lights. Normal is tangent-space, converted to world-space internally. Do not connect this fragment-only node to displacement or particle emission geometry. The contribution is excluded from ForwardAdd and ShadowCaster to avoid repeated lighting.

Missing package errors are actionable in build/preview; graphs without a reachable LTCGI node have no LTCGI include or dependency. Removing LTCGI while retaining generated LTCGI shaders can still break those shaders: remove the node and rebuild first.

API reference: [For Shader Authors](https://ltcgi.dev/Advanced/Shader_Authors), checked 2026-09-19. Source tested against LTCGI **1.7.3**, commit `b2014d6c6e76c551c30084973e54687941265d68`. NXSG calls the installed package include; no LTCGI algorithm/source/assets are redistributed. Follow LTCGI's own licensing and attribution requirements when using it.

Live VRChat, headset/stereo and Windows validation are separate from Linux Unity tests.
