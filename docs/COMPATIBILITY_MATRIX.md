# Rendering comparison matrix

Use this matrix when comparing an NXSG build with another avatar shader or
when deciding whether a feature is ready for release. Record the exact graph,
material, avatar mesh, texture set, Unity version, graphics API, GPU, VRChat
client version, world, camera, and date with each result. Capture both the
left and right eye where stereo matters.

| Scene | Required observations | Current evidence |
| --- | --- | --- |
| Plain Toon | Main light, extra pixel light, shadow receive, mirror, fallback | Linux editor render and D3D11 cross-compile; native Windows/client matrix open |
| Shiny hair | Normal, rim, anisotropy, backlight, transparency order | Linux editor samples; native mirror and live-world comparison open |
| Glitter and particles | Particle shape/atlas, sorting, alpha/additive, mirror, bounds at near/far camera distance | Linux editor renders; live stereo and mirror comparison open |
| Fur | Shell, fin, card silhouette, local self-shadow, movement and bounds | Linux editor renders; live stereo and mirror comparison open |
| Audio reactive | AudioLink present/absent, silence fallback, animated material property | Linux editor synthetic input; real provider/client comparison open |
| World lighting | LTCGI and Light Volumes present/absent, provider version, scene fallback | Shader compile and editor checks; provider-world runtime comparison open |

For each scene, keep the mesh, UVs, textures, lights, probes, camera, resolution,
color space, and post processing fixed. First match the intended appearance;
then record cold/warm build time, emitted passes and variants, GPU frame time,
and memory. A checkbox count or cross-compile alone does not measure visual
parity or frame cost.

Failure cases to capture: oblique mirrors, each stereo eye, worlds without
camera depth, transparent hair overlap, large displacement and bounds,
additional pixel lights, near-camera clipping, shader fallback, and low/high
camera distance. Mark a case **pass**, **fail**, or **not run**, with a link to
the exact screenshot/log. Do not infer a pass from another row.
