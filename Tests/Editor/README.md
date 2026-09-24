# Editor interaction check

`SocketInteractionSmoke.cs` tests the actual UI Toolkit canvas with synthetic pointer and key events. Run only in a disposable Unity 2022.3.22f1 project: it creates a graph window, edits its in-memory graph, and exits Unity.

1. Create an empty project pinned to 2022.3.22f1.
2. Add `dev.nerdrx.nxsg` from this repository as a local package, with `com.unity.nuget.newtonsoft-json` 3.2.1.
3. Copy this C# file into that project's `Assets/Editor` directory.
4. Launch its Editor with a graphics device, `-executeMethod SocketInteractionSmoke.Run`, and an explicit `-logFile`. On Linux, run inside headless Gamescope as documented in `docs/DEVELOPMENT.md`.
5. Require `NXSG SOCKET CHECK PASSED` in the Editor log with no preceding exception or compile error. Gamescope's exit status alone does not establish a pass.

This fixture does not open or change the interactive development project's graph, material, or scene.

`ColorRenderSmoke.cs` uses the same isolated setup with `-executeMethod ColorRenderSmoke.Run`. Require `NXSG COLOR RENDER CHECK PASSED` in its log. It compiles and renders a red constant multiplied by a second color; `Assets/ColorCheck.png` records the render.

`NodePackRenderSmoke.cs` uses `-executeMethod NodePackRenderSmoke.Run` in the same disposable project. Require `NXSG NODE PACK RENDER CHECK PASSED`. It renders a combined node graph at two fixed times with emission and black lighting, writing `Assets/NodePack0.png` and `Assets/NodePack1.png`.

## Creator tools

Copy these files into the same disposable fixture and run each entry independently:

- `CreatorToolsSmoke.Run`: real GPU bake color/UVs, animation rejection, linear texture import, frozen preview clock, darkness emission response, normal green flip, bookmark round-trip/hash isolation.
- `TextureSetEditorSmoke.Run`: new-document isolation, Undo isolation, actual RGBA channel renders.
- `CreatorMaterialSmoke.Run`: embedded group headers in both emitters, preset serialization/reload, multi-material apply and Undo, preview clock isolation.
- `NXSG.Editor.CreatorPlaygroundSmoke.Run`: actual EditorWindow rendering, image snapshot replacement/cleanup and source isolation. Screenshot capture requires headless Gamescope.

Require each explicit PASSED/passed log marker; Unity startup or a process exit alone is insufficient.

## Color controls and responsiveness

- `ColorControlsSmoke.Run`: stable fields while editing, invalid-number recovery, hue-space persistence, reset/Undo, header switching and a real editor capture at `/tmp/nxsg-color-controls.png`.
- `ColorKeySmoke.Run`: GPU checks for Color Mask and Replace Color, including alpha, edited defaults and material-bound inputs.
- `ResponsivenessSmoke.Run`: 150-node socket cache and wire insertion checks, a bounded drag-target timing sample, Undo and deferred scene checks. Timing is informational, with no machine-specific pass threshold.
