# NXSG alpha.20 — opaque material depth

Fixed a shadow/depth pass being omitted when camera-dependent nodes such as
Fresnel or Matcap feed albedo while **Use albedo alpha** is disabled.

Those nodes affect color, not coverage in that configuration. Opaque Toon, PBR
and Unlit surfaces now retain their shadow caster, which Unity Built-In also uses
for camera depth textures. Missing camera depth can let world effects appear over
an otherwise solid avatar.

View-dependent explicit opacity still produces a warning and omits the caster.
The warning now describes that depth limitation and the albedo-alpha setting.

Update NXSG, reopen each affected graph, click **Build for VRChat**, then upload
again. Keep **Opacity 1** and **Use albedo alpha off** for this material.

The missing-pass bug is confirmed in the supplied graph configuration. The exact
VRChat world effect still needs an in-client retest; this release does not claim
all world-specific rendering issues are resolved.
