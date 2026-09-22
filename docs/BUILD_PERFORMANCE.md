# Build for VRChat performance

Alpha.27 removes the staging shader import. The final shader is imported and validated once, with the previous shader and material journaled before mutation. An exception restores both assets. Shader and material GUIDs stay stable.

Unchanged emitted source is not rewritten or force-reimported. Unity still checks dependencies and NXSG validates the active renderer passes. Emission runs every time, so compiler changes and optional integrations are not hidden behind a graph-only cache. Material bindings are still applied. This is not a persistent compiled-shader cache.

Saving now imports only the graph instead of refreshing the entire project. Identical graph text is not rewritten. The Build button reports elapsed time including save and preview setup; Console output breaks down generation/validation, shader import/compilation and material saving.

## Measurement

Unity 2022.3.22f1, Linux OpenGL, isolated headless Gamescope project, 2026-09-22. Second consecutive builds using the same graph and existing Unity shader caches:

| Graph | Before | After |
| --- | ---: | ---: |
| Animated Palette | 63 ms | 42 ms |
| Volume Pearl Sculpture | 68 ms | 43 ms |
| Fur Cards | 70 ms | 43 ms |

These samples were 33–39% faster. These are single warm-run observations, not cold-cache, whole-avatar, Windows or VRChat SDK upload benchmarks. The project-wide refresh removal is not included in these direct GraphBuild measurements. First-run baseline staging and final import/validation stages were respectively 106/78, 203/199 and 192/183 ms; the repeated stage is now removed.

Reproduce with `Tests/Editor/BuildTimingSmoke.cs` in an isolated graphics-enabled Unity project. `BuildTransactionSmoke` checks unchanged source, changed builds, rollback at both mutation checkpoints, stable GUIDs, corrupted outputs, missing materials and a real shader compilation failure. `SceneSyncSmoke` exercises editor saving and scene synchronization. Keep Unity runs bounded with a timeout.
