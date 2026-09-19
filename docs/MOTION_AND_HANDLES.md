# Effect handles and motion-aware materials

## Place a sticker without painting

Select a **Sticker** node and open its placement tool in the inspector, or use
**Create → Place selected sticker**. The tool edits the same graph, with Undo.
Use the visible Undo/Redo, Center sticker and Reset view controls. Numeric fields
allow precise placement; Escape cancels the current drag. A failed graph edit
keeps the last successful preview and reports the error.
Use the mesh preview to pick a position from the mesh's UV0, then use the UV
plane handles to move, resize and rotate the sticker. Choose a scene object to
preview its mesh; a skinned mesh uses a snapshot of its current pose.

Mesh picking requires readable geometry with UV0. Overlapping UV islands share
placement. Transformed UV inputs cannot be mapped back automatically: use the
UV plane for those graphs. Position is an offset from UV center `(0.5, 0.5)`;
size is the sticker's UV extent. These are graph edits, not painted textures.

## Motion nodes

| Node | Use |
| --- | --- |
| Avatar Motion | Locomotion speed, signed sideways/vertical/forward speed, and velocity vector |
| Motion Response | Convert speed into a 0–1 effect strength, with start/full-speed thresholds and curve |
| Motion Sway | Procedural flutter along mesh normals, scaled by speed and mask |
| Motion Stretch UVs | Stretch a texture horizontally or vertically as speed increases |

Connect **Avatar Motion → speed** to the other nodes. A Response can control
emission, mix factors, opacity, or particle masks. Connect Sway to a surface's
Displacement input. Bounds must contain the displaced mesh. Sway is procedural
animation, not simulated inertia, cloth, or retained particle history.

Import **Motion Glow** from the package examples for a connected glow/flutter
graph. In **Create → Material playground / compare**, change **Velocity (m/s)**
to test the effect without touching scene materials.

## Feed real avatar motion

The shader needs an Animator driver; installing the shader alone does not
provide motion data. Build a graph containing a connected Avatar Motion node,
assign its generated material, then open **Create → Create avatar motion driver**.
Choose the avatar root and renderer. Generate a standalone controller for an
otherwise empty FX slot, or add motion layers to the existing FX controller.
Alpha.7 copies the generated motion layers, states, trees and clips into the
existing FX controller. New merges are self-contained and support Undo/Redo.
Older alpha.6 merges still need their original generated controller; keep it
unless you replace those motion layers. Ambiguous renderer paths are rejected.
Keep existing avatar FX layers: do not replace a populated controller.

The driver uses native Unity blend trees and constant clips, with no custom
runtime component. It maps VRChat's built-in float parameters to these material
properties:

| Animator parameter | Material property |
| --- | --- |
| VelocityMagnitude | `_NXSG_MotionSpeed` |
| VelocityX | `_NXSG_MotionX` |
| VelocityY | `_NXSG_MotionY` |
| VelocityZ | `_NXSG_MotionZ` |

All values are metres per second. The chosen maximum speed bounds the blend-tree
range; values beyond it saturate. Choose a range covering the intended motion.
Bindings affect all matching material slots on the selected renderer. One driver
covers one renderer. No Expression Parameters entries are needed for these
built-in inputs. FX merging through external tools is optional and not assumed.

This measures player locomotion, not individual bone, hand, PhysBone, or cloth
motion. Standstill defaults are zero. Desktop preview simulation is separate from
live VRChat behaviour; remote-player fidelity depends on VRChat's parameter
updates. Windows/headset/client verification remains open.

References, checked 2026-09-19:
[VRChat Animator parameters](https://creators.vrchat.com/avatars/animator-parameters/),
[playable layers](https://creators.vrchat.com/avatars/playable-layers/),
[Unity 2022.3 AnimationClip.SetCurve](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AnimationClip.SetCurve.html).
