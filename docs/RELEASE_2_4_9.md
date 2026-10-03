# NXSG 2.4.9

This update reduces repeated work in the graph editor, especially when navigating larger graphs.

- Selecting the same node no longer rebuilds its inspector. The Add Nodes library is reused between selections.
- Folded node finder, Parameters, and Frames sections create their controls when opened.
- Problems diagnostics run when the Problems tab is opened. Refocusing the editor retains an unchanged preview shader.
- Box selection avoids rewriting unchanged node outlines. Node dragging skips distant wire segments during insertion checks.
- Wires fully outside the viewport are skipped during canvas redraw.

The portable graph/compiler suite and 11 packaging checks passed. The current Core, Backend, and Editor sources, plus the expanded editor smoke, compiled with Unity 2022.3.22f1 Roslyn. The headless Unity interaction smoke could not start because Unity's licensing client timed out before the project opened. These checks do not establish a measured editor frame-rate improvement or live VRChat behavior.

Update **NX Shader Graph** to **2.4.9** through the [NXSG VPM repository](https://nerdrx.github.io/nxsg/). Existing graph files do not need migration.
