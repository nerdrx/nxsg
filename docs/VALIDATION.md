# First Linux implementation slice

**Date:** 2026-09-17. This records implementation evidence, not completion of the full design or roadmap.

## Environment

| Component | Exercised configuration |
| --- | --- |
| Authoring OS | CachyOS Linux, kernel 7.2.5-1-cachyos |
| Unity | 2022.3.22f1, revision 887be4894c44 |
| SDK fixture | VRChat Base and Avatars 3.10.5; official archive SHA-256 values verified |
| Unity JSON dependency | `com.unity.nuget.newtonsoft-json` 3.2.1 |
| Portable host | .NET SDK 8.0.425; Newtonsoft.Json 13.0.3; same core/backend sources |
| Graphics | OpenGLCore, AMD Radeon RX 7900 XTX, radeonsi/ACO |
| Hidden display | Gamescope 3.16.25, 1440 × 900, 30 Hz |

Unity needed `libxml2.so.2` on this rolling distribution. A signed Arch `libxml2-legacy` 2.13.9-2 package was extracted into local test tools and provided through the process library path. System libraries were not changed. The bundled licensing helper failed to start; opening the installed Hub supplied a working 1.17.4 helper. No license modification or activation bypass was used. On first open, Unity removed orphan SDK metadata and generated XR settings inside the local fixture. Rerunning setup detects and preserves that changed tree; the archive hashes describe the downloaded originals.

## Passed automated checks

The [portable harness](../Tests/Portable/Program.cs) checks deterministic JSON, layout-independent semantic hashes, unknown-node preservation, unsupported node-version rejection, malformed input, duplicate/trailing JSON, enum/nonfinite validation, resource traversal, cycles, port types, a 4,096-node chain, and emitter behavior. Emitter cases cover constant colors, unused math, mutable color multiplication, property-name collisions and hostile identifiers. Preflight rejects expression depth above 64 and expanded traversal work above 8,192 visits before recursive lowering; identifiers and total generated source are bounded. Adversarial chain and repeated-input DAG tests exercise those limits.

The [Unity harness](../DevProject/Assets/Editor/NxsgSmoke.cs) compiled and ran in the pinned editor. Its [machine-readable result](evidence/2026-09-17-linux-smoke.json) records:

- Matching core roundtrip and semantic hashes using Unity's resolved Newtonsoft assembly.
- SDK manifests and Base/Avatars assemblies present at the pinned versions.
- Generated opaque toon shader imported, supported and rendered to a texture with a visible non-error subject.
- Explicit `Material.SetPass` checks for generated passes with synchronous compilation on the active renderer.
- Build action created shader/material output and retained their GUIDs across rebuilds.
- Material color and texture references survived rebuilding.
- Injected failures after shader promotion and after material promotion restored both original files byte for byte.

![Actual Linux toon rendering](evidence/2026-09-17-toon.png)

The small sphere is an actual Unity camera capture, not a design mockup. It establishes a basic rendering path, not visual parity with other toon shaders.

## Editor checks

The custom UI Toolkit canvas opened in the pinned Linux editor on a hidden display. Pointer dragging moved the node and redrew its edges. Toolbar undo/redo restored the node position. A user-interface Build action updated the material preview. The search query `anime` offered Toon Surface. Add-node and toolbar undo were also exercised. [Actual editor capture](assets/editor-preview.png). Keyboard undo was not reliable in this environment, matching the creator's existing Unity experience; visible Undo/Redo controls remain the supported path. Closing the hidden test editor through its explicit control signal completed with exit code 0.

The hidden-display setup also exposed a Gamescope/libei event failure during one interactive run. Graphics batch checks completed successfully. This distinction matters when reproducing input automation on this distribution.

## What remains open

This is a development prototype: one sampled texture, UV0, color expressions and opaque toon output. The basic Add/search UI supports curated aliases, not general fuzzy intent matching. Preview updates after an explicit successful build. Some backend inputs are available through graph files before they have editor controls.

Remaining S00–S04 work includes the standalone assembly packaging contract, migration/paste flows, full source-map diagnostics, 20/200-node interaction measurements, GraphView comparison, multi-selection, edge hit testing, keyboard/accessibility coverage, richer property editing, incremental previews and crash-recovery tests across actual process interruption. The tested rollback checkpoints are exceptions within one process, not a simulated machine crash.

No VRChat avatar upload, SDK avatar acceptance, live client, headset, mirror/stereo parity, native Windows, mobile device, VRCFury or AudioLink test has been performed. Fur, animation, Patterns, baking and the Blender bridge remain planned. Passing a basic Linux rendering check does not close those gates.

See [development setup](DEVELOPMENT.md) for reproducible commands and [implementation plan](IMPLEMENTATION_PLAN.md) for the remaining acceptance criteria.

## Socket interaction update — 2026-09-17

The custom canvas now uses edge sockets and per-operation header colors. A separate minimal Unity 2022.3.22f1 project ran `Tests/Editor/SocketInteractionSmoke.cs` inside headless Gamescope, sending UI Toolkit pointer/key events to the real canvas. Dragging from either endpoint, click-click connection replacement, mismatched-type rejection, the compatible-node menu on empty drops, spawning and connecting in both directions, Escape cancellation, selected-node outlines, box selection, group dragging/deletion, and Undo passed. These are synthetic editor interaction checks, not a user usability study.

## Clipboard update — 2026-09-17

The portable checks exercise clipboard resource/parameter remapping, cross-graph paste, fresh IDs, layout offsets, oversize/duplicate/unresolved-reference rejection, and unchanged targets on failure. The hidden Unity interaction check also exercises Ctrl+C/V/D, copied internal wires, excluded external wires, paste Undo, clipboard preservation during Duplicate, text-field shortcut isolation, and invalid clipboard rejection. Copy/paste is limited to currently supported core node versions, 1 MiB of clipboard JSON, and 1,024 nodes per snippet.

## Color correctness and editing context — 2026-09-17

A real graphics regression check (`Tests/Editor/ColorRenderSmoke.cs`) renders a red constant through Multiply and Toon. The sampled center pixel is RGBA(1,0,0,1); the previous comma-expression bug produced white. HLSL now uses vector constructors while ShaderLab property defaults retain tuple syntax. Graph previews opened directly use neutral material tint; opening through a material preserves that material as context. The header shows graph/material/shader identity and the window title includes the graph filename. Existing generated shaders must be rebuilt to incorporate the compiler fix.

## Ten-node pack — 2026-09-17

Portable checks cover each new node, default inputs, type rejection, clipboard, and the Animated Palette example. The combined graphics fixture (`NodePackRenderSmoke.Run`) compiles Time/Value, UV Transform/Scroll, a sampled texture, Noise, Mix, Add, Clamp, One Minus, and Emission together. With black albedo and no lighting, red emission remains visible; captures at deterministic times 0 and 1 differ. Both captures are in `docs/evidence`. The existing hidden UI interaction/clipboard check passes after integrating the shared node catalog. This is Unity OpenGL evidence, not a VRChat client or DX11 acceptance result.

## Coordinate nodes — 2026-09-17

Polar UVs, Rotate UVs, Object Planar UVs, and World Planar UVs passed portable validation/emission, typed angle connections, malformed-property rejection, clipboard preservation, disconnected-node elimination, and a 20-node coordinate-chain source-size check. The Polar Palette sample validates and emits. Coordinate helpers keep nested source expressions from expanding exponentially.

`Tests/Editor/CoordinateRenderSmoke.cs` ran in a separate project under headless Gamescope with Unity 2022.3.22f1 and OpenGL. It samples an encoded UV texture through emission on an X/Z quad using a linear floating-point render target. Polar center/cardinal samples, 90-degree rotation, object-space translation invariance, and world-space translation response passed (`NXSG COORDINATE RENDER CHECK PASSED`). This checks Linux rendering, not live VRChat, stereo, skinned meshes, or Windows.
