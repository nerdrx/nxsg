using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        /// <summary>Editor controls for portable utility nodes. Root GraphWindow feature dispatcher calls this first.</summary>
        bool AddUtilityControls(GraphNode node)
        {
            switch (node.Operation)
            {
                case UtilityNodes.Clock:
                    AddIndexedChoice(node, "source", "Clock", new[] { "Unity time (default)", "VRChat network time" }, 0, 0,
                        "Network mode reads _VRChatTimeNetworkMs, a uint millisecond counter shared by VRChat. It wraps about every 49.7 days; frames and network correction can differ slightly between viewers.");
                    AddNumber(node, "period", "Cycle period (seconds)", 1, "period");
                    AddNumber(node, "offset", "Offset (seconds)", 0, "offset");
                    inspector.Add(new Label("Phase is 0–1. Cycle is the counter's cycle number; network cycle wraps with the uint millisecond source.") { style = { whiteSpace = WhiteSpace.Normal } });
                    return true;
                case UtilityNodes.MsdfDecal:
                    AddTexturePicker(node, "RGB MSDF atlas");
                    AddColorField(node, "color", "Fill", Color.white, "color");
                    InspectorSection(node, "msdf.edges", "Outline and edges", () =>
                    {
                        AddColorField(node, "outlineColor", "Outline", Color.black, "outlineColor");
                        AddNumber(node, "outlineWidth", "Outline width", 0, "outlineWidth");
                        AddNumber(node, "softness", "Edge softness (pixels)", 0, "softness");
                    });
                    InspectorSection(node, "msdf.atlas", "Atlas settings and requirements", () =>
                    {
                        AddNumber(node, "distanceRange", "Atlas distance range (texels)", 4, "distanceRange");
                        inspector.Add(new HelpBox("Assign an RGB multi-channel signed-distance atlas. Import it as linear data with mipmaps and compression disabled, bilinear filtering, and clamp wrap.", HelpBoxMessageType.Info));
                    });
                    return true;
                case UtilityNodes.NumericText:
                    AddTexturePicker(node, "Numeric SDF atlas");
                    AddColorField(node, "color", "Text tint", Color.white, "color");
                    AddNumber(node, "value", "Number", 0, "value");
                    AddNumber(node, "scale", "Text scale", 1, "scale");
                    InspectorSection(node, "numeric-text.format", "Number formatting", () =>
                    {
                        AddIntegerField(node, "digits", "Maximum integer digits", 1, 8, 6);
                        AddIntegerField(node, "decimals", "Decimal places", 0, 4, 1);
                        AddNumber(node, "spacing", "Character spacing", .08f, "spacing");
                    });
                    InspectorSection(node, "numeric-text.atlas-help", "Atlas and connections", () =>
                    {
                        var create = new Button(() => CreateAtlas(node)) { text = "Create numeric SDF atlas…", tooltip = "Creates digits, minus and decimal point as a small single-channel SDF texture asset." };
                        inspector.Add(create);
                        inspector.Add(new HelpBox("Creates an atlas for digits 0–9, minus, and decimal point. Connect Viewer Stats → Render FPS, Camera Distance, World Position component, or a clock output to Value. Render FPS is the current viewer's Unity frame rate, not compositor FPS. Shader values have about 7 significant decimal digits; extra layout digits do not add precision.", HelpBoxMessageType.Info));
                    });
                    return true;
                case UtilityNodes.ViewerStats:
                    inspector.Add(new HelpBox("Viewer-local stats: Render FPS = 1 / Unity delta time; World Position = current shaded point; Camera Distance = distance to the active viewer camera; Unity Time follows _Time; Network Time follows VRChat's wrapping synchronized millisecond counter. These values can differ per viewer.", HelpBoxMessageType.Info));
                    return true;
                default: return false;
            }
        }

        void CreateAtlas(GraphNode node)
        {
            var path = EditorUtility.SaveFilePanelInProject("Create numeric SDF atlas", "NXSG_NumericSDF", "png", "Choose a location under Assets.");
            if (string.IsNullOrEmpty(path)) return;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            try
            {
                TextAtlasUtility.CreateDigitAtlas(path);
                var resourceId = (string)node.Properties["resourceId"];
                Edit("Assign numeric SDF atlas", () =>
                {
                    if (graph.Adapter == null) graph.Adapter = new JObject();
                    if (!(graph.Adapter["textures"] is JObject)) graph.Adapter["textures"] = new JObject();
                    ((JObject)graph.Adapter["textures"])[resourceId ?? ""] = AssetDatabase.AssetPathToGUID(path);
                });
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            catch (Exception exception) { SetStatus("Could not create numeric SDF atlas: " + exception.Message); }
        }
    }
}
