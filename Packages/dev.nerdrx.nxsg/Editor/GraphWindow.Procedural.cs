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
        static readonly string[] CoordinateKeys = { "uv0", "uv1", "uv2", "uv3", "object", "world", "polar", "panosphere", "matcap" };
        static readonly string[] CoordinateLabels = { "Mesh UV0", "Mesh UV1", "Mesh UV2", "Mesh UV3", "Object XZ (sticks to object)", "World XZ (fixed in world)", "Polar (mesh UV0)", "Panosphere (camera-relative)", "Matcap (camera-relative)" };

        void AddCoordinateChoice(GraphNode node)
        {
            var keys = CoordinateKeys;
            var labels = CoordinateLabels;
            var field = new PopupField<string>(node.Operation == "core.polarUV" ? "Input coordinates" : "Coordinates", labels.ToList(), Math.Max(0, Array.IndexOf(keys, (string)node.Properties["coordinateSource"] ?? "uv0")))
            { tooltip = "A connected UV wire overrides this choice. Mesh UV and Polar do not follow the camera. UV1–UV3 require those channels on your mesh." };
            field.SetEnabled(!graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == "uv"));
            field.RegisterValueChangedCallback(evt => Edit("Change coordinates", () => node.Properties["coordinateSource"] = keys[Array.IndexOf(labels, evt.newValue)]));
            inspector.Add(field);
            if (node.Operation == "core.polarUV")
                inspector.Add(new HelpBox("Polar is applied after these input coordinates. For plain Panosphere, select Panosphere from the node header menu.", HelpBoxMessageType.Info));
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

        void AddDistortionControls(GraphNode node)
        {
            AddCoordinateChoice(node);
            AddIndexedChoice(node,"mode","Distortion",new[]{"Noise / turbulence","Waves","Swirl","Ripple","Flow map (red/green)","Pixelate","Lens / bulge"});
            var mode = (int?)node.Properties["mode"] ?? 0;
            AddNumber(node,"strength",mode == 5 ? "Blend amount (0–1)" : "Strength",.05f,"strength");
            AddBoundedNumber(node,"mask","Mask",0,1,1,"mask");
            AddVector(node,"axes","Axis strength",Vector2.one);
            if (mode == 0 || mode == 1 || mode == 3 || mode == 5) AddNumber(node,"scale",mode == 5 ? "Grid cells" : "Scale",5);
            if (mode == 0 || mode == 1 || mode == 3) AddNumber(node,"speed","Animation speed",1);
            if (mode == 0 || mode == 1) AddVector(node,"direction",mode == 0 ? "Scroll direction" : "Wave direction",Vector2.one);
            if (mode == 0) AddIndexedChoice(node,"detail","Detail layers",Enumerable.Range(1,6).Select(i=>i.ToString()).ToArray(),1,1);
            if (mode == 2 || mode == 3 || mode == 6)
            {
                AddVector(node,"center","Center",new Vector2(.5f,.5f));
                AddNumber(node,"radius","Radius",.5f);
                AddBoundedNumber(node,"falloff","Edge falloff",0,8,1);
            }
            inspector.Add(new Label(mode == 4 ? "Connect a flow texture's Color to Flow. Red/green set direction; 0.5 is neutral. Import flow textures as linear data." : "Mask 0 or Strength 0 leaves UVs unchanged. Chain UV outputs to combine effects.") { style = { whiteSpace = WhiteSpace.Normal } });
            inspector.Add(new Label("Offset output gives the UV displacement for preview or reuse.") { style = { whiteSpace = WhiteSpace.Normal } });
        }

        void AddBoundedNumber(GraphNode node, string property, string label, float min, float max, float fallback, string inputPort = null)
        {
            var value = (float?)node.Properties[property] ?? fallback;
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 5 } };
            var slider = new Slider(label, min, max) { value = Mathf.Clamp(value, min, max), style = { flexGrow = 1, flexShrink = 1, minWidth = 0 } };
            var number = new FloatField { value = value, isDelayed = true, style = { width = 68, flexShrink = 0 },
                tooltip = label + ": type beyond the slider range. Mathematical limits still apply." };
            row.SetEnabled(inputPort == null || !graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == inputPort));
            slider.RegisterValueChangedCallback(evt => {
                var next = Mathf.Clamp(evt.newValue, min, max);
                EditValue("Change " + label, () => node.Properties[property] = next);
                number.SetValueWithoutNotify(next);
            });
            number.RegisterValueChangedCallback(evt =>
            {
                if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue))
                {
                    number.SetValueWithoutNotify((float?)node.Properties[property] ?? fallback);
                    SetStatus("Enter a finite number.");
                    return;
                }
                EditValue("Change " + label, () => node.Properties[property] = evt.newValue);
                slider.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, min, max));
            });
            slider.labelElement.style.display = DisplayStyle.None;
            slider.tooltip = label + ": drag within " + min + "–" + max + ", or type a value on the right.";
            inspector.Add(new Label(label) { tooltip = number.tooltip });
            row.Add(slider);
            row.Add(number);
            inspector.Add(row);
        }
    }
}
