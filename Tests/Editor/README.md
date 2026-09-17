# Editor interaction check

`SocketInteractionSmoke.cs` tests the actual UI Toolkit canvas with synthetic pointer and key events. Run only in a disposable Unity 2022.3.22f1 project: it creates a graph window, edits its in-memory graph, and exits Unity.

1. Create an empty project pinned to 2022.3.22f1.
2. Add `dev.nerdrx.nxsg` from this repository as a local package, with `com.unity.nuget.newtonsoft-json` 3.2.1.
3. Copy this C# file into that project's `Assets/Editor` directory.
4. Launch its Editor with a graphics device, `-executeMethod SocketInteractionSmoke.Run`, and an explicit `-logFile`. On Linux, run inside headless Gamescope as documented in `docs/DEVELOPMENT.md`.
5. Require `NXSG SOCKET CHECK PASSED` in the Editor log with no preceding exception or compile error. Gamescope's exit status alone does not establish a pass.

This fixture does not open or change the interactive development project's graph, material, or scene.
