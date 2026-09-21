# NXSG alpha.19 — D3D vertex output budget

Advanced shaders now explicitly require 32 interpolators in every generated pass.
Their shared vertex payload can exceed the Shader Model 4.0 output limit once
lighting, shadows and stereo fields are included. This could report X4571 in
`vertAdd` on D3D11 even for a small PBR or Toon graph.

The change covers base lighting, additional lights, shells, fur, particles,
geometry and tessellation passes. The compact basic Toon backend is unchanged.
This declares the GPU capability required by the existing layout; it does not
add material effects or change their appearance. Hardware must support that
larger output budget.

## Updating existing materials

Update NX Shader Graph in ALCOM/Creator Companion (pre-release packages enabled).
Open each affected graph and click **Build for VRChat** to regenerate its shader,
then rebuild/upload the avatar. Installing the package alone does not rewrite
already-generated shaders.

A successful upload does not establish that every shader pass compiled. The
reported error affects additional-light variants and should not be ignored.

See [the validation record](VALIDATION.md) for the checks and their limits.
