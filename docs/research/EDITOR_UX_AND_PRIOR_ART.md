# Editor UX, integration lifecycle, and prior art

Research date: **2026-09-17**. **Verified** identifies source-backed observations; **Decision** identifies proposed NXSG behavior; **Spike** identifies a question requiring implementation evidence.

## Editor foundation

**Verified:** Unity 2022.3 exposes `UnityEditor.Experimental.GraphView.GraphView`, including typed-port compatibility hooks, node creation requests, graph-change callbacks, and copy/paste callbacks. Unity explicitly marks this API experimental. This supplies useful canvas behavior but does not establish a stable persistence or compiler model. [Unity GraphView API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Experimental.GraphView.GraphView.html)

**Decision, revised after creator feedback:** compare a small custom UI Toolkit canvas with GraphView on **Unity 2022.3.22f1**. Building our own is a valid route; GraphView is an optional accelerator. Both adapt commands to the separate graph model. Do not make serialized `Node`, `Port`, or `VisualElement` objects the portable format. Measure keyboard access, reload recovery, response at the target graph size, and code maintenance before choosing. A Unity package advertised for a newer editor must not be assumed usable on the VRChat baseline. The [custom canvas report](CUSTOM_CANVAS_ON_UNITY_2022.md) covers the version-specific primitives and comparison fixture.

**Verified:** Unity's UI Toolkit editor-window workflow uses `CreateGUI` and addresses hot reload. Asset database refresh can compile scripts, reload the domain, and restart when import callbacks create or change assets. Editor state therefore has to survive more than closing and reopening a window. [Editor windows](https://docs.unity3d.com/2022.3/Documentation/Manual/UIE-HowTo-CreateEditorWindow.html), [asset refresh lifecycle](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetDatabaseRefreshing.html)

**Decision:** Maintain a recoverable editing document with a dirty flag and undo transactions; rebuild the visual tree from it after reload. Saving updates the graph source, while preview recompilation uses a temporary cache. Explicit build publishes generated assets only after successful validation. Guard reimport loops, avoid rewriting unchanged files, and keep Unity asset APIs on the main thread. Background graph analysis can operate on immutable snapshots, with a revision check that discards stale results.

**Spike:** Choose and test the smallest undo bridge: a Unity `ScriptableObject` edit-session wrapper can host serializable state for `Undo`, while the actual saved `.nxsg` remains portable JSON. Test a grouped drag, connection deletion, parameter rename, paste, undo/redo, domain reload, and a simultaneous external file edit. Do not assume Unity Undo automatically tracks arbitrary plain C# objects. [Unity Undo API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Undo.html)

## Discoverability and interaction contract

**Verified:** Blender documents adding a node by dropping a dragged connection into empty space and searching compatible nodes and sockets. Its familiar pan, zoom, frame-selected, and frame-all actions provide a useful interaction reference. [Blender node editors](https://docs.blender.org/manual/en/5.0/interface/controls/nodes/node_editors.html)

**Decision:** Provide both a visible Add action and a keyboard search action. Rank results using names, aliases, socket compatibility, stage, and target support. Begin with deterministic local matching and a curated synonym list; no model service or embedding dependency is needed for “moving texture” to find UV Scroll. Show why a node is incompatible instead of hiding every unfamiliar capability. Initial intent tests: “audio glow,” “moving fur,” “scroll texture,” “mask,” “toon,” and misspelled “emision.” Record expected top results before tuning the ranking.

Basic mode should prioritize small effect interfaces and explain inputs in creator language. Advanced mode exposes the same graph's types, spaces, math, and compiler details. A built-in atomic node can expose advanced settings without pretending it can be expanded into editable subnodes. Only genuine graph Patterns support expansion; expanding should retain a recoverable relationship to the original Pattern version or explicitly create a local copy.

No graph operation should depend solely on wire color, hover, or a mouse gesture. Provide socket text/type labels, visible focus, keyboard actions for adding/connecting/deleting nodes, readable scaling, and a reduced-motion preview setting. The first accessibility spike must determine which of these GraphView supplies and which NXSG must add. Do not claim screen-reader support until checked in the actual editor.

## Previews without editor stalls

**Decision:** Share lowering and effect semantics between preview and final build. Preview scaffolding may supply a mesh, synthetic lighting, or test AudioLink values; label those substitutions. Pinning one intermediate output should not modify the saved Output connection. Let creators select scalar heatmaps, RGB, normals, or alpha so a valid negative value does not look like a broken black preview.

Debounce structural changes; update mutable uniforms without recompiling when possible. Compile only the most recent document revision, prioritize the selected node and main material, and suspend hidden or paused previews. Cache by semantic graph hash, backend, dependency versions, and preview mode. Keep layout-only edits outside the shader cache key. Dispose temporary materials, render textures, and callbacks on reload/window closure. The preview implementation remains a spike; the existence of an editor API does not prove lifecycle correctness.

**Spike:** Compare a small graph and a representative large graph (proposed fixtures: 20 and 200 nodes). Record search latency, drag response, structural compile latency, preview update rate, and memory after repeated open/close cycles. Proposed UX targets are sub-100 ms search response and responsive dragging while shaders compile; these are design goals, not measured results or promises. A timeout must preserve the last valid preview and identify stale content.

## Asset identity and build behavior

**Verified:** Unity `.meta` files carry asset identities; losing them can break references. `ScriptedImporter` supports importing custom file formats. [Asset metadata](https://docs.unity3d.com/2022.3/Documentation/Manual/AssetMetadata.html), [ScriptedImporter](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetImporters.ScriptedImporter.html)

**Decision:** A `.nxsg` importer should produce a discoverable graph asset; generation of final `.shader` and `.mat` assets should initially happen through an explicit build action, reducing importer complexity. Preserve output paths and `.meta` identity across rebuilds. Rebuilding code should preserve user material values and texture assignments unless a documented migration applies. Graph duplication should create a new graph identity and output namespace; ordinary reimport must not. Failed builds must not replace the current material with an error shader.

Reference textures by stable portable IDs with Unity GUID/subasset mappings in the adapter. Warn on missing dependencies. Reimport tests must cover moving a texture, duplicate names, a renamed graph, material variants, and upgrading the editor package. A “Build for VRChat” button builds local assets; it must not silently upload an avatar or alter the user's animator.

## Prior-art evaluation and provenance

| Reference snapshot | Verified observation | NXSG consequence |
| --- | --- | --- |
| Graphlit `2.7.4`, commit `59a4609b3df6668e34458dc59e926b62957d4871` | Its editor uses GraphView; the package declares Unity `2022.3` and Core RP `14.0.10`. | A relevant comparison for editor lifecycle and Built-In authoring; adopting it would also adopt its architecture and dependency constraints. |
| ShaderGraphVRC `1.1.1`, commit `06f7fe1b209c11e32045883d1a6f54e37dfb5b82` | Its package depends on Shader Graph `14.0.10`; the README describes a forward Built-In target and identifies target code as Unity Companion License. | A useful compatibility reference, with Unity-specific code and API obligations to review before any reuse. |
| Poiyomi, commit `6c8000924c543f5d40f04d28ba8dd6544a071198` | Its README describes Built-In/DX11 focus and a separate ThryEditor dependency for inspection/locking. | Compare creator workflows and material specialization, without copying its interface or assuming cross-API parity. |

Sources: Graphlit [package](https://github.com/z3y/Graphlit/blob/59a4609b3df6668e34458dc59e926b62957d4871/package.json), [canvas source](https://github.com/z3y/Graphlit/blob/59a4609b3df6668e34458dc59e926b62957d4871/Editor/ShaderGraphView.cs), [license](https://github.com/z3y/Graphlit/blob/59a4609b3df6668e34458dc59e926b62957d4871/LICENSE.md); ShaderGraphVRC [README](https://github.com/z3y/ShaderGraphVRC/blob/06f7fe1b209c11e32045883d1a6f54e37dfb5b82/README.md), [package](https://github.com/z3y/ShaderGraphVRC/blob/06f7fe1b209c11e32045883d1a6f54e37dfb5b82/package.json); Poiyomi [README](https://github.com/poiyomi/PoiyomiToonShader/blob/6c8000924c543f5d40f04d28ba8dd6544a071198/README.md).

The GitHub API reports MIT for these repositories, but that badge does not settle dependencies or separately licensed files. Graphlit's root MIT notice and ShaderGraphVRC's explicit target-code notice illustrate why the exact reused files matter. NXSG currently bundles none of them. Keep a small original core for the portable format, while treating upstream code as reference material; evaluate any proposed reuse by concrete saved work and reviewed file-level provenance.

## Required editor checks before feature expansion

1. Create, edit, save, reopen, undo, redo, and recover a graph after domain reload.
2. Prove main material and intermediate preview parity for a known fixture under identical inputs.
3. Rebuild without losing output asset identities, material values, or animation bindings.
4. Paste missing-pack, malformed, cyclic, and oversized snippets safely; never run code on paste.
5. Complete the core material workflow using keyboard actions and readable, non-color-only cues.
6. Measure preview responsiveness and resource cleanup; retain the last good output on failure.

These checks are planned. No NXSG editor implementation or live UX test exists at this research milestone.
