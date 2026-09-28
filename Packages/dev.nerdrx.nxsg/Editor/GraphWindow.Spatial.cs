using System;
using NXSG.Core;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        bool AddSpatialControls(GraphNode node)
        {
            switch (node.Operation)
            {
                case "core.vertexDeform":
                    AddDirectionField(node, "translation", "Translation", Vector3.zero);
                    AddDirectionField(node, "rotation", "Rotation (degrees)", Vector3.zero);
                    AddDirectionField(node, "scale", "Scale", Vector3.one);
                    AddDirectionField(node, "pivot", "Pivot", Vector3.zero);
                    AddNumber(node, "mask", "Deformation mask", 1, "mask");
                    AddNumber(node, "snap", "Snap size", 0, "snap");
                    AddNumber(node, "warp", "Warp amount", 0, "warp");
                    AddIndexedChoice(node, "space", "Transform space", new[] { "Object local", "World" });
                    AddIndexedChoice(node, "shape", "Warp shape", new[] { "None", "Sphere", "Cylinder" });
                    FeatureNote("Connect a completed mesh Surface to Base. Translation, rotation, scale and pivot use the selected space; rotation uses degrees. Snap 0 disables snapping. Mask 0 preserves the source mesh. Check renderer bounds after large motion.");
                    return true;
                case "core.infinityParallax":
                    AddCoordinateChoice(node);
                    AddTexturePicker(node, "Interior texture");
                    AddIntegerField(node, "steps", "Interior layers", 1, 32, 8);
                    AddIndexedChoice(node, "blend", "Layer blend", new[] { "Composite", "Additive", "Maximum" });
                    AddNumber(node, "depth", "Interior depth", .3f, "depth");
                    AddNumber(node, "strength", "View parallax", .05f, "strength");
                    AddNumber(node, "height", "Height influence", .5f, "height");
                    AddColorField(node, "tint", "Deep tint", new Color(.8f, .9f, 1, 1), "tint");
                    AddNumber(node, "fade", "Depth fade", .7f, "fade");
                    AddNumber(node, "mask", "Interior mask", 1, "mask");
                    FeatureNote("Steps are capped at 32. Connect tangent-space View for direction and optional Height for layer position. Texture alpha masks interior samples. The mesh silhouette and shadows do not move.");
                    return true;
                default: return false;
            }
        }
    }
}
