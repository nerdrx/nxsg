using System;
using System.Collections.Generic;
using System.Linq;
using NXSG.Core;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        void AddCoordinateChoice(GraphNode node)
        {
            var keys = new[] { "uv0", "uv1", "uv2", "uv3", "object", "world", "polar", "panosphere", "matcap" };
            var labels = new[] { "Mesh UV0", "Mesh UV1", "Mesh UV2", "Mesh UV3", "Object XZ (sticks to object)", "World XZ (fixed in world)", "Polar (mesh UV0)", "Panosphere (camera-relative)", "Matcap (camera-relative)" };
            var field = new PopupField<string>("Coordinates", labels.ToList(), Math.Max(0, Array.IndexOf(keys, (string)node.Properties["coordinateSource"] ?? "uv0")))
            { tooltip = "A connected UV wire overrides this choice. Mesh UV and Polar do not follow the camera. UV1–UV3 require those channels on your mesh." };
            field.SetEnabled(!graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == "uv"));
            field.RegisterValueChangedCallback(evt => Edit("Change coordinates", () => node.Properties["coordinateSource"] = keys[Array.IndexOf(labels, evt.newValue)]));
            inspector.Add(field);
        }

        void AddIndexedChoice(GraphNode node, string property, string label, string[] choices, int fallback = 0, int first = 0)
        {
            var field = new PopupField<string>(label, choices.ToList(), Mathf.Clamp(((int?)node.Properties[property] ?? fallback) - first, 0, choices.Length - 1));
            field.RegisterValueChangedCallback(evt => Edit("Change " + label, () => node.Properties[property] = Array.IndexOf(choices, evt.newValue) + first));
            inspector.Add(field);
        }

        void AddProceduralControls(GraphNode node)
        {
            var noise = node.Operation == "core.noise";
            AddIndexedChoice(node, "dimensions", "Dimensions", noise ? new[] { "1D · line", "2D · UV pattern", "3D · volume", "4D · evolving volume" } : new[] { "2D · UV pattern", "3D · volume" }, 2, noise ? 1 : 2);
            var dimensions = (int?)node.Properties["dimensions"] ?? 2;
            if (dimensions <= 2) AddCoordinateChoice(node);
            else
            {
                var field = new PopupField<string>("Position space", new List<string> { "Object (sticks to mesh)", "World (fixed in world)" }, (string)node.Properties["coordinateSpace"] == "world" ? 1 : 0)
                { tooltip = "Used when Position is unconnected. Object coordinates follow object transforms; World coordinates stay fixed in the scene." };
                field.SetEnabled(!graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == "position"));
                field.RegisterValueChangedCallback(evt => Edit("Change position space", () => node.Properties["coordinateSpace"] = field.index == 1 ? "world" : "object"));
                inspector.Add(field);
            }
            AddNumber(node, "scale", "Scale", 5);
            AddNumber(node, "speed", dimensions == 4 ? "Evolution speed" : "Animation speed", noise ? 1 : 0);
            inspector.Add(new Label(dimensions == 1 ? "Uses X input, or UV.x when X is empty." : dimensions == 2 ? "Uses UV input, or Coordinates when UV is empty." : "Uses Position input, or Position space when empty.") { style = { whiteSpace = WhiteSpace.Normal } });
            inspector.Add(new Label("Speed 0 freezes animation. Time input overrides the clock.") { style = { whiteSpace = WhiteSpace.Normal } });
            if (node.Operation == "core.musgrave")
            {
                AddIndexedChoice(node, "mode", "Pattern", new[] { "Soft fractal", "Ridged", "Turbulence" });
                AddIndexedChoice(node, "octaves", "Detail layers", Enumerable.Range(1, 8).Select(i => i.ToString()).ToArray(), 4, 1);
                AddBoundedNumber(node, "lacunarity", "Detail scale", 1, 4, 2);
                AddBoundedNumber(node, "gain", "Detail strength", 0, 1, .5f);
            }
            if (node.Operation == "core.voronoi") AddBoundedNumber(node, "randomness", "Randomness", 0, 1, 1);
            if (node.Operation == "core.wave")
            {
                AddIndexedChoice(node, "mode", "Pattern", new[] { "Bands", "Rings" });
                AddIndexedChoice(node, "axis", "Direction", new[] { "X", "Y", "Z (3D only)" });
            }
        }

        void AddBoundedNumber(GraphNode node, string property, string label, float min, float max, float fallback)
        {
            var field = new Slider(label, min, max) { value = (float?)node.Properties[property] ?? fallback, showInputField = true };
            field.RegisterValueChangedCallback(evt => Edit("Change " + label, () => node.Properties[property] = Mathf.Clamp(evt.newValue, min, max)));
            inspector.Add(field);
        }
    }
}
