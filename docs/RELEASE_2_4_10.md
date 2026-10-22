# NXSG 2.4.10

Fur refinement, less repeated compiler work, and safer graph editing and mobile baking.

## Fur

- Shell distance LOD moves continuously and fades the departing outer layer. Vertex and fragment LOD use the same undisplaced position.
- Zero length, density, or mask hides fur. Unconnected authored zero values remove unused fur passes; connected controls remain available for animation.
- Fin and card displacement uses unit normals. Root-to-tip color matches the shell ramp.
- Cost reporting distinguishes shell passes, cards, and enabled self-shadow sampling.

## Compiler and editor

- A build reuses its validation and type-inference results without retaining stale analysis between graph edits.
- Texture slot labels no longer allocate and sort the resource list for every label.
- New graphs leave enough space between sockets for reliable pointer hits.
- Reverse wire drops reconnect correctly. Escape closes the connected-node menu while its search field has focus.
- Group edits preserve unknown imported metadata. Wire insertion rejects ambiguous node or connection IDs.
- Malformed numeric values produce diagnostics instead of conversion exceptions. Saves reject non-finite numbers; failed open/recovery leaves the current session intact.
- Numeric Text clamps very large finite values before converting them to digits. Slope Mask handles a zero normal without undefined normalization.
- Preview failure paths release their temporary textures and restore the previous render target.

## Mobile baking

A caught write or import failure restores the previous maps, material, registry, metadata, and unsaved edits to the existing baked material. Failed first bakes remove their newly created outputs. Existing Unity asset identities are retained. This covers handled failures; process interruption during file promotion is not crash-atomic.

## Validation

The release record is in [VALIDATION.md](VALIDATION.md#2410-quality-and-release-checks--2026-10-22). Checks distinguish portable CPU emission, Unity editor interaction, graphics rendering, and D3D11 cross-compilation. Two Linux batch starts hit native Mono assertions; successful retries and the failed attempts are documented in that record. These checks do not establish live VRChat, headset, or mirror behavior for this update.

Existing graphs do not need migration. Update **NX Shader Graph** to **2.4.10** through the [NXSG VPM repository](https://nerdrx.github.io/nxsg/).
