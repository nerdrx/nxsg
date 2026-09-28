# VRC Light Volumes

NXSG's optional VRC Light Volumes receiver targets the `red.sim.lightvolumes`
package and calls its v3 shader API. NXSG does not bundle the package or its
shader implementation.

Install VRC Light Volumes in the Unity project before building a graph that
uses this integration. The generated shader includes
`Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc`; without the package
that include cannot compile. The compiler should report the missing optional
dependency before emitting a shader that references it.

The helper returns a diffuse contribution and a specular contribution
separately. Diffuse applies albedo and the metallic energy reduction. Specular
comes from `LightVolumeSHSpecular` and is already colored with the material's
specular response; add it once and do not multiply it by albedo again. The
helper does not apply a graph-level AO/shadow scalar, so the surface lighting
caller can apply its own controls. VRC Light Volumes' own point, spot and area
light shadows are included by its sampler.

At runtime, `LightVolumeSHSpecular` uses VRC Light Volumes when the scene has a
supported system and falls back to Unity probe SH plus an approximate
highlight when it does not. The contribution does not include Reflection Probe
cubemaps. The helper requires shader target 3.5 or newer and `UnityCG.cginc`
must be included first. Evaluate it in the fragment stage for ordinary
surfaces.

## Official API and license

Research checked 2026-09-28 against VRC Light Volumes `3.0.0-dev.20`, commit
[`da8ca8b0a3fbb99aa60cd62ee3f36189e94bd42e`](https://github.com/REDSIM/VRCLightVolumes/commit/da8ca8b0a3fbb99aa60cd62ee3f36189e94bd42e).
This is a prerelease API and can change before a stable v3 release.

- [Official shader integration guide](https://github.com/REDSIM/VRCLightVolumes/blob/v.3.0.0-dev.20/Documentation/ForDevelopers.md)
- [Official shader function reference](https://github.com/REDSIM/VRCLightVolumes/blob/v.3.0.0-dev.20/Documentation/ShaderFunctions.md)
- [Official `LightVolumes.cginc`](https://github.com/REDSIM/VRCLightVolumes/blob/v.3.0.0-dev.20/Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc)
- [Package manifest](https://github.com/REDSIM/VRCLightVolumes/blob/v.3.0.0-dev.20/Packages/red.sim.lightvolumes/package.json) declares package name `red.sim.lightvolumes` and MIT licensing.
- [Upstream MIT license](https://github.com/REDSIM/VRCLightVolumes/blob/v.3.0.0-dev.20/LICENSE)

The integration only calls the package's public shader functions; it does not
copy or redistribute upstream implementation code. Follow the upstream MIT
terms if any package files are redistributed separately.
