# NXSG 2.1.0

## Auto-organize and grid snapping

- **Tools → Auto-organize graph** arranges nodes from inputs to outputs, using actual node sizes and spacing between branches.
- **Tools → Auto-organize selection** tidies selected nodes while leaving other nodes in place.
- Complete frames and Patterns stay together. Expanded groups get an internal layout; collapsed Patterns keep their internal positions.
- **View → Grid snapping** snaps dragged nodes and groups to a 24-unit grid. Hold **Alt** for free movement. The setting starts off and remembers your preference.
- Organization and dragging support Undo. Organization preserves graph connections and shader settings.

## Fixes

- Repeated collapsed Pattern drags no longer use an old starting position.
- Frame members visibly follow the frame when dragged.
- Clicking a node or group without dragging no longer creates a layout edit.
- Expanded frame backgrounds no longer hide their internal wires.
- Long organized graphs have more coordinate space before reaching the canvas safety limit.

## Checks

Unity 2022.3.22f1 on Linux, in an isolated headless Gamescope session: organization of varied node sizes, disconnected branches, cycles, repeated layout, a 500-node chain, preserved connections and groups, selected-only movement, Undo/Redo, snapping at 150% zoom, Alt bypass, repeated group drags, and frame movement. The actual editor capture was reviewed for spacing and visible wires.

These editor checks do not establish native Windows, VRChat client, or headset behavior. This release changes editor layout tools; existing graphs are organized only when requested.

## Update

Refresh the NXSG repository in ALCOM or Creator Companion, then update **NX Shader Graph** to **2.1.0**. This is a stable release; prerelease packages do not need to be enabled.

[Installation help](https://github.com/nerdrx/nxsg/blob/main/docs/VPM.md) · [Canvas tools](https://github.com/nerdrx/nxsg/blob/main/docs/CREATOR_WORKFLOW.md#organizing-the-canvas)
