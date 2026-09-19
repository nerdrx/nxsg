# NXSG alpha.6 — effect handles and motion

- Sticker placement on a mesh preview with UV0 picking, orbit controls, and UV move/resize/rotation handles. Graph Undo is retained.
- Avatar Motion, Motion Response, Motion Sway, and Motion Stretch UVs: four new nodes, 142 visible nodes total.
- Native FX driver builder for VRChat locomotion parameters. Create a standalone controller or explicitly add its layers to an existing FX controller with Undo.
- Material playground velocity controls and a Motion Glow example.

[Setup and limitations](https://github.com/nerdrx/nxsg/blob/main/docs/MOTION_AND_HANDLES.md).

Motion requires the generated FX driver; it measures player locomotion, not bone or PhysBone movement. Mesh picking needs readable UV0 geometry; transformed UV graphs use the UV plane. The generated controller remains a dependency when its layers are added to another FX controller. No painting or runtime script dependency.

Validated in Linux Unity 2022.3.22f1 with OpenGLCore: GPU motion response and texture stretch, shader passes, native Animator material bindings, merge Undo/Redo, and real placement-window mouse interactions. Live VRChat, Windows and headset checks remain open.
