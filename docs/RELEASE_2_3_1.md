# NXSG 2.3.1

## Material import

The Poiyomi and lilToon importer now creates connected branches for enabled emission layers, glitter, and supported masks. It also maps Poiyomi AudioLink modulation, Panosphere emission coordinates, clear coat, and lilToon secondary color layers, normals, MatCaps, and outlines where the source material uses them. NXSG chooses Toon or PBR from source settings; the PBR checkbox is gone. Imported texture slots have names taken from their source roles.

Import still changes the selected material asset in place after creating a backup. The report now lists active settings and assigned textures that were not mapped, rather than dumping every shader property. Check it and compare the avatar with the backup before uploading: source-specific blending, lighting, glitter shapes, and animation do not all have exact NXSG equivalents.

Validation: Unity 2022.3.22f1 headless import and shader build for synthetic Poiyomi and lilToon fixtures, plus an isolated copy of a real locked Poiyomi 9.2 material. Portable and VPM packaging tests passed. This does not establish visual parity in VRChat.
