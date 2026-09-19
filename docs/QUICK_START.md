# Your first NXSG material

1. Add the [VPM listing](https://nerdrx.github.io/nxsg/) to your package manager. Enable pre-release packages and install **NX Shader Graph** in a test project using Unity **2022.3.22f1**, Built-In rendering.
2. Open **Tools → NXSG → Open Graph Editor → File → New graph**. The starter graph already connects a texture to a toon surface and Output.
3. Select **Texture** and pick a project texture in the **Inspector** tab. Use **Nodes** or Space to find more nodes; drag the sidebar divider to resize it.
4. Click **Save**, choose a `.nxsg` file inside **Assets**, then **Build for VRChat**. This creates a local shader and material under **Assets/NXSGGenerated**; it does not upload an avatar.
5. Drag the generated material onto a scene mesh. Its inspector has **Open Shader Graph** to return to the right graph/material context.
6. Edit the graph. **Auto scene** applies saved edits after a short pause; disable it for manual builds. **Problems** explains graph errors and cost warnings. Node-specific errors include a button to show the node.
7. Save, close and reopen the graph. Confirm the material still looks right. Undo/Redo toolbar buttons remain available; File → Recovery contains local snapshots and checkpoints.

## Ready-made materials

In Unity's Package Manager, select NXSG and import **Example Graphs** from Samples. Open the imported `.nxsg` from its location under Assets. Alternatively, copy a package `Samples~` graph into your own Assets folder. `Assets/NXSGExamples` is the repository development-project location, not a folder automatically created by VPM.

Try **Showcase Hologram**, **Showcase Pearl**, or **Showcase Warm Fur**. They need no external textures. Warm Fur uses 24 shell layers and is an appearance study, not a measured performance recommendation.

## Animate your own property

In **Inspector → Parameters**, add a Float or Color parameter, then click **+ node** beside it and connect its output. Material and AnimatedMaterial bindings create conventional shader properties; Constant is compiled as a fixed value. The parameter editor displays the stable shader reference name to use in animation bindings. Renaming the display label preserves that reference.

Choose **Reads** on a selected Parameter node to change its source. A parameter cannot be deleted while nodes still reference it. Existing material overrides are preserved by rebuilds; changing a graph default does not reset those overrides.

## Boundaries

Start in a test project and keep normal project backups. Recovery snapshots live under Library and disappear if Library is cleared. See [compatibility](COMPATIBILITY.md) for verified and unverified targets. Linux editor checks do not establish VRChat client or headset compatibility.
