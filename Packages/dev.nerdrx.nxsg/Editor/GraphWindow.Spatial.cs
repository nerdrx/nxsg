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
                    InspectorSection(node, "deform.transform", "Transform", () =>
                    {
                        AddDirectionField(node, "translation", "Translation", Vector3.zero);
                        AddDirectionField(node, "rotation", "Rotation (degrees)", Vector3.zero);
                        AddDirectionField(node, "scale", "Scale", Vector3.one);
                        AddDirectionField(node, "pivot", "Pivot", Vector3.zero);
                        AddNumber(node, "mask", "Deformation mask", 1, "mask");
                        AddIndexedChoice(node, "space", "Transform space", new[] { "Object local", "World" });
                    }, true);
                    InspectorSection(node, "deform.snap-warp", "Snap and warp", () =>
                    {
                        AddNumber(node, "snap", "Snap size", 0, "snap");
                        AddNumber(node, "warp", "Warp amount", 0, "warp");
                        AddIndexedChoice(node, "shape", "Warp shape", new[] { "None", "Sphere", "Cylinder" });
                        FeatureNote("Snap 0 disables snapping. Check renderer bounds after large motion.");
                    });
                    InspectorSection(node, "deform.near-camera", "Near camera", () =>
                    {
                        AddBoundedNumber(node, "nearDistance", "Minimum camera distance (m)", 0, 2, 0, "nearDistance", "Push nearby vertices away from the camera. Zero disables this control.");
                        AddBoundedNumber(node, "nearStrength", "Push strength", 0, 1, 1, "nearStrength");
                        FeatureNote("Uses Deformation mask. Expand renderer bounds if the mesh moves beyond its original bounds.");
                    });
                    FeatureNote("Connect a completed mesh Surface to Base. Transform values use the selected space; rotation uses degrees.");
                    return true;
                case "core.infinityParallax":
                    AddCoordinateChoice(node);
                    AddTexturePicker(node, "Interior texture");
                    InspectorSection(node, "parallax.layers", "Layers and depth", () =>
                    {
                        AddIntegerField(node, "steps", "Interior layers", 1, 32, 8);
                        AddIndexedChoice(node, "blend", "Layer blend", new[] { "Composite", "Additive", "Maximum" });
                        AddNumber(node, "depth", "Interior depth", .3f, "depth");
                        AddNumber(node, "strength", "View parallax", .05f, "strength");
                        AddNumber(node, "height", "Height influence", .5f, "height");
                        AddNumber(node, "mask", "Interior mask", 1, "mask");
                    }, true);
                    InspectorSection(node, "parallax.tint-fade", "Tint and fade", () =>
                    {
                        AddColorField(node, "tint", "Deep tint", new Color(.8f, .9f, 1, 1), "tint");
                        AddNumber(node, "fade", "Depth fade", .7f, "fade");
                    });
                    InspectorSection(node, "parallax.help", "Setup and limits", () =>
                        FeatureNote("Layers are capped at 32. Connect tangent-space View for direction and optional Height for layer position. Texture alpha masks interior samples. The mesh silhouette and shadows do not move."));
                    return true;
                default: return false;
            }
        }
    }
}
