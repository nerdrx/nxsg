using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        [SerializeField] string existingNodeSearch = "";
        [SerializeField] string frameName = "";
        [SerializeField] string frameNote = "";

        void AddFrameToolbar(Toolbar toolbar)
        {
            var menu = new ToolbarMenu { text = "Frames", tooltip = "Create named boxes around selected nodes." };
            menu.menu.AppendAction("Create frame from selection", _ => CreateFrame(frameName, frameNote));
            toolbar.Add(menu);
        }

        void AddNodeFinder(VisualElement parent)
        {
            var section = new Foldout { text = "FIND EXISTING NODES", value = false, style = { marginTop = 10 } };
            var search = new ToolbarSearchField { name = "existing-node-search", tooltip = "Find nodes already in this graph. This does not add nodes." };
            search.SetValueWithoutNotify(existingNodeSearch); section.Add(search);
            var matches = new VisualElement(); section.Add(matches);
            Action<string> refresh = query =>
            {
                matches.Clear();
                var nodesFound = (graph?.Nodes ?? new List<NXSG.Core.GraphNode>()).Where(n => n != null && MatchesNodeSearch(n.Operation, query) ||
                    (n != null && ((NodeTitle(n) + " " + n.Id).IndexOf((query ?? "").Trim(), StringComparison.OrdinalIgnoreCase) >= 0))).Take(30).ToList();
                foreach (var node in nodesFound)
                {
                    var item = node;
                    var button = new Button(() => { RevealNode(item.Id); SelectNode(item.Id); FrameNodes(true); })
                    { text = NodeTitle(item), tooltip = item.Id + " · " + NodeCatalog.Description(item.Operation), style = { unityTextAlign = TextAnchor.MiddleLeft, marginTop = 2 } };
                    matches.Add(button);
                }
                if (nodesFound.Count == 0 && !string.IsNullOrWhiteSpace(query)) matches.Add(new Label("No existing nodes found."));
            };
            search.RegisterValueChangedCallback(e => { existingNodeSearch = e.newValue; refresh(existingNodeSearch); });
            refresh(existingNodeSearch);
            parent.Add(section);
        }

        void RevealNode(string nodeId)
        {
            var group = GraphGroups.All(graph).FirstOrDefault(g => (bool?)g["collapsed"] == true && GraphGroups.Members(graph, g).Contains(nodeId));
            if (group != null) Edit("Reveal found node", () => group["collapsed"] = false);
        }

        void AddFrameControls()
        {
            if (graph == null) return;
            var section = new Foldout { text = "FRAMES", value = false, style = { marginTop = 10 } };
            var name = new TextField("Name") { value = frameName, tooltip = "Frame name." };
            var note = new TextField("Note") { value = frameNote, multiline = true, maxLength = 1000, tooltip = "Optional note for this frame." };
            name.RegisterValueChangedCallback(e => frameName = e.newValue);
            note.RegisterValueChangedCallback(e => frameNote = e.newValue);
            section.Add(name); section.Add(note);
            section.Add(new Button(() => CreateFrame(frameName, frameNote)) { text = "Frame selected nodes" });
            var frames = GraphGroups.All(graph).Where(g => (bool?)g["frame"] == true);
            if (frames != null)
                foreach (var token in frames.OfType<JObject>())
                {
                    var item = token;
                    var label = (string)item["name"] ?? "Frame";
                    var button = new Button(() => { selection = GraphGroups.Members(graph, item).ToList(); selected = selection.LastOrDefault(); UpdateSelectionOutline(); RebuildInspector(); FrameNodes(true); }) { text = "▸ " + label, tooltip = (string)item["note"] ?? "" };
                    section.Add(button);
                }
            inspector.Add(section);
        }

        void CreateFrame(string name, string note)
        {
            var clean = (name ?? "").Trim();
            if (clean.Length == 0) clean = "Frame";
            if (graph == null || selection.Count == 0) { SetStatus("Select nodes before creating a frame."); return; }
            Edit("Save graph frame", () =>
            {
                var frame = GraphGroups.Add(graph, selection, clean);
                frame["frame"] = true; frame["collapsed"] = false; frame["note"] = note ?? "";
            });
            frameName = clean;
        }

        string SocketTooltip(GraphNode node, string port, bool output, string type)
        {
            var connected = output ? graph?.Connections.Count(e => e.From.NodeId == node.Id && e.From.PortId == port) ?? 0
                : graph?.Connections.Count(e => e.To.NodeId == node.Id && e.To.PortId == port) ?? 0;
            var range = SocketRange(node.Operation, port);
            var units = SocketUnits(node.Operation, port);
            var effect = output ? "Feeds " + connected + " connection" + (connected == 1 ? "" : "s") + "." : connected > 0 ? "Connected value drives this input; stored default is ignored." : "Unconnected input uses its stored or built-in default.";
            return (output ? "Output" : "Input") + ": " + PortLabel(port) + " (" + type + ")" + range + units + "\n" + effect + " Drag to connect.";
        }

        static string SocketRange(string operation, string port)
        {
            if (operation == "core.particleInfo" && (port == "age" || port == "random" || port == "alpha")) return " · range 0–1";
            if (port == "opacity" || port == "mask" || port == "factor" || port == "smoothing" || port == "fallback") return " · range 0–1";
            if (port == "alpha" || port == "edgeSharpness" || port == "density") return " · effective range 0–1";
            if (port == "lifetime") return " · positive duration (stored minimum 0.001)";
            if (operation == "core.audioLink" && port == "value") return " · range from AudioLink mode";
            return "";
        }

        static string SocketUnits(string operation, string port)
        {
            if (operation == "core.surfaceParticles" && port == "size") return " · local mesh units";
            if (operation == "core.particleInfo" && port == "random") return " · deterministic per particle";
            if (port == "lifetime") return " · seconds";
            if (port == "emissionRate") return " · births per source triangle per second";
            if (operation == "core.surfaceParticles" && (port == "speed" || port == "spread")) return " · local mesh units per second";
            if (operation == "core.surfaceParticles" && port == "gravity") return " · local mesh units per second squared";
            if (port == "angle") return " · degrees";
            return "";
        }

        void AddParticleCurves(GraphNode node)
        {
            AddParticleCurve(node, "sizeCurve", "Particle size curve");
            AddParticleCurve(node, "opacityCurve", "Particle opacity curve");
            AddParticleColorCurve(node, "colorCurve", "Particle color curve");
        }

        void AddParticleCurve(GraphNode node, string property, string label)
        {
            var points = node.Properties[property] as JArray;
            var valid = points == null ? new List<JArray>() : points.OfType<JArray>().Where(p => p.Count >= 2 && Number(p[0]) && Number(p[1])).Take(16).ToList();
            var keys = valid.Count < 2 ? new[] { new Keyframe(0, 1), new Keyframe(1, 1) } : valid.Select(p => new Keyframe(Mathf.Clamp01((float)p[0]), Mathf.Clamp01((float)p[1]))).ToArray();
            var curve = new AnimationCurve(keys);
            for (var i = 0; i < curve.length; i++) { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear); }
            var field = new CurveField(label) { value = curve, ranges = new Rect(0, 0, 1, 1), tooltip = "Normalized lifetime response. X = normalized age, Y = multiplier." };
            field.RegisterValueChangedCallback(e =>
            {
                var values = e.newValue?.keys;
                if (values == null || values.Length < 2 || values.Length > 16 || values.Any(k => k.time < 0 || k.time > 1 || k.value < 0 || k.value > 1)) { field.SetValueWithoutNotify(curve); SetStatus("Use 2–16 curve points inside the 0–1 square."); return; }
                Edit("Change " + label, () => node.Properties[property] = new JArray(values.Select(k => new JArray(Mathf.Clamp01(k.time), Mathf.Clamp01(k.value)))));
            });
            inspector.Add(field);
        }

        void AddParticleColorCurve(GraphNode node, string property, string label)
        {
            var points = node.Properties[property] as JArray;
            var valid = points == null ? new List<JArray>() : points.OfType<JArray>().Where(p => p.Count >= 5 && Number(p[0]) && Number(p[1]) && Number(p[2]) && Number(p[3]) && Number(p[4])).Take(8).ToList();
            var positions = valid.Count < 2 ? new[] { 0f, 1f } : valid.Select(p => Mathf.Clamp01((float)p[0])).Distinct().OrderBy(v => v).ToArray();
            var gradient = new Gradient();
            gradient.SetKeys(positions.Select(t => new GradientColorKey(valid.Count < 2 ? Color.white : EvaluateColor(valid, t), t)).ToArray(),
                positions.Select(t => new GradientAlphaKey(valid.Count < 2 ? 1 : EvaluateColor(valid, t).a, t)).ToArray());
            var field = new GradientField(label) { value = gradient, tooltip = "Normalized lifetime color. X = normalized age." };
            field.RegisterValueChangedCallback(e =>
            {
                var positions = e.newValue.colorKeys.Select(k => k.time).Concat(e.newValue.alphaKeys.Select(k => k.time)).Distinct().OrderBy(v => v).ToList();
                if (positions.Count < 2 || positions.Count > 8) { field.SetValueWithoutNotify(gradient); SetStatus("Use 2–8 color points inside the 0–1 range."); return; }
                Edit("Change " + label, () => node.Properties[property] = new JArray(positions.Select(t => { var c = e.newValue.Evaluate(t); return new JArray(t, c.r, c.g, c.b, c.a); })));
            });
            inspector.Add(field);
        }

        static Color EvaluateColor(List<JArray> points, float time)
        {
            var point = points.OrderBy(p => Mathf.Abs((float)p[0] - time)).First();
            return new Color((float)point[1], (float)point[2], (float)point[3], (float)point[4]);
        }

        static bool Number(JToken token)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return false;
            var value = (double)token; return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
