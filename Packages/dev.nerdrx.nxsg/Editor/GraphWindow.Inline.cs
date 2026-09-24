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
        readonly Dictionary<string, NodeThumbnail> inlineThumbnails = new Dictionary<string, NodeThumbnail>();
        bool inlinePreviewQueued;
        double inlinePreviewDue;
        double inlineLastRepaint;

        // Root hooks: call from node creation, semantic-change queue, editor update, and OnDisable.
        void AddInlineControls(GraphNode node, VisualElement nodeBox)
        {
            if (node == null || nodeBox == null) return;
            if (node.Operation == "core.colorRamp") AddInlineGradient(node, nodeBox);
            if (node.Operation == "core.constant" || node.Operation == "core.value") AddInlineValue(node, nodeBox);
            var outputs = Ports(node.Operation, true).Where(p => {
                var type = PortType(node, p);
                return type == "float" || type == "color" || type == "surface" || type == "vector2" || type == "vector3";
            }).ToList();
            if (outputs.Count == 0) return;
            var state = GetThumbnail(node.Id, outputs[0]);
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 3, marginLeft = 6, marginRight = 6 } };
            var toggle = new Toggle("Thumbnail") { value = state.Enabled, tooltip = "Render the first output of this node. Up to four thumbnails; follows Live preview and pauses when unfocused." };
            toggle.style.flexGrow = 1;
            toggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue && !state.Enabled && inlineThumbnails.Values.Count(s => s.Enabled) >= 4)
                {
                    toggle.SetValueWithoutNotify(false);
                    SetStatus("Only four node thumbnails can be active at once.");
                    return;
                }
                state.Enabled = e.newValue;
                if (!e.newValue) DisposeThumbnail(state);
                QueueInlinePreviews();
            });
            row.Add(toggle);
            nodeBox.Add(row);
            state.Toggle = toggle;
            EnsureThumbnailHost(state, nodeBox);
        }

        void AddInlineValue(GraphNode node, VisualElement nodeBox)
        {
            var row = new VisualElement { style = { marginTop = 3, marginLeft = 6, marginRight = 6 } };
            VisualElement field;
            if (node.Operation == "core.constant" && string.Equals((string)node.Properties["valueType"], "color", StringComparison.OrdinalIgnoreCase))
            {
                var values = node.Properties["value"] as JArray;
                var color = values != null && values.Count == 4 ? new Color((float)values[0], (float)values[1], (float)values[2], (float)values[3]) : Color.white;
                var colorField = new ColorField("Color") { value = color, tooltip = "Inline color. Drag the node header to move it." };
                colorField.labelElement.style.minWidth = 40; colorField.labelElement.style.width = 40;
                colorField.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                colorField.RegisterCallback<PointerMoveEvent>(e => e.StopPropagation());
                colorField.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                colorField.RegisterValueChangedCallback(e => EditValue("Change color", () => node.Properties["value"] = new JArray(e.newValue.r, e.newValue.g, e.newValue.b, e.newValue.a)));
                field = colorField;
            }
            else if (node.Operation == "core.value" || node.Operation == "core.constant" && node.Properties["value"] != null && node.Properties["value"].Type != JTokenType.Array)
            {
                var value = node.Properties["value"] != null && (node.Properties["value"].Type == JTokenType.Float || node.Properties["value"].Type == JTokenType.Integer) ? (float)node.Properties["value"] : 0;
                var number = new FloatField("Value") { value = value, isDelayed = true, tooltip = "Inline value. Drag the node header to move it." };
                number.labelElement.style.minWidth = 40; number.labelElement.style.width = 40;
                number.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                number.RegisterCallback<PointerMoveEvent>(e => e.StopPropagation());
                number.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
                number.RegisterValueChangedCallback(e => { if (!float.IsNaN(e.newValue) && !float.IsInfinity(e.newValue)) EditValue("Change value", () => node.Properties["value"] = e.newValue); });
                field = number;
            }
            else return;
            row.Add(field); nodeBox.Add(row);
        }

        void AddInlineGradient(GraphNode node, VisualElement nodeBox)
        {
            var gradient = GradientFromNode(node);
            var field = new GradientField("Ramp") { value = gradient, tooltip = "Edit color ramp stops." };
            field.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            field.RegisterCallback<PointerMoveEvent>(e => e.StopPropagation());
            field.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            field.RegisterValueChangedCallback(e =>
            {
                e.newValue.mode = GradientMode.Blend;
                var positions = e.newValue.colorKeys.Select(k => k.time).Concat(e.newValue.alphaKeys.Select(k => k.time)).Distinct().OrderBy(x => x).ToList();
                if (positions.Count < 2 || positions.Count > 8) { field.SetValueWithoutNotify(gradient); SetStatus("Use at most 8 combined color/alpha stop positions."); return; }
                Undo.RegisterCompleteObjectUndo(session, "Change Color Ramp");
                node.Properties["stops"] = new JArray(positions.Select(position => {
                    var c = e.newValue.Evaluate(position);
                    return new JArray(position, c.r, c.g, c.b, c.a);
                }));
                gradient = e.newValue;
                session.json = GraphJson.Serialize(graph, true);
                hasUnsavedChanges = true;
                EditorUtility.SetDirty(session);
                QueueInlinePreviews();
                QueueLivePreview();
                UpdateIdentity();
            });
            nodeBox.Add(field);
        }

        static Gradient GradientFromNode(GraphNode node)
        {
            var gradient = new Gradient();
            var points = node.Properties == null ? null : node.Properties["stops"] as JArray;
            var valid = points == null ? new List<JArray>() : points.OfType<JArray>().Where(p => p.Count >= 5).ToList();
            gradient.SetKeys(valid.Count == 0
                ? new[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.white, 1) }
                : valid.Select(p => new GradientColorKey(new Color((float)p[1], (float)p[2], (float)p[3], (float)p[4]), Mathf.Clamp01((float)p[0]))).ToArray(),
                valid.Count == 0
                ? new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
                : valid.Select(p => new GradientAlphaKey(Mathf.Clamp01((float)p[4]), Mathf.Clamp01((float)p[0]))).ToArray());
            gradient.mode = GradientMode.Blend;
            return gradient;
        }

        NodeThumbnail GetThumbnail(string nodeId, string port)
        {
            NodeThumbnail state;
            if (!inlineThumbnails.TryGetValue(nodeId, out state)) { state = new NodeThumbnail { NodeId = nodeId, Port = port }; inlineThumbnails[nodeId] = state; }
            state.Port = port;
            return state;
        }

        void EnsureThumbnailHost(NodeThumbnail state, VisualElement nodeBox)
        {
            if (state.Host == null)
                state.Host = new VisualElement { style = { width = 128, height = 96, marginLeft = 6, marginTop = 3, display = DisplayStyle.None, backgroundColor = new Color(.04f, .04f, .04f) } };
            if (state.Error == null)
                state.Error = new Label { style = { color = new Color(.95f, .55f, .45f), fontSize = 10, whiteSpace = WhiteSpace.Normal, display = DisplayStyle.None } };
            if (state.Host.parent != nodeBox) nodeBox.Add(state.Host);
            if (state.Error.parent != nodeBox) nodeBox.Add(state.Error);
        }

        void QueueInlinePreviews()
        {
            inlinePreviewQueued = true;
            inlinePreviewDue = EditorApplication.timeSinceStartup + .25;
            foreach (var state in inlineThumbnails.Values) if (state.Enabled) state.Pending = true;
        }

        void TickInlinePreviews()
        {
            RepaintInlinePreviews();
            if (!livePreview || focusedWindow != this) return;
            if ((!inlinePreviewQueued && !inlineThumbnails.Values.Any(s=>s.Enabled&&s.Pending)) || EditorApplication.timeSinceStartup < inlinePreviewDue || graph == null || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            inlinePreviewDue=EditorApplication.timeSinceStartup+.25;
            inlinePreviewQueued = false;
            // One lookup set avoids rescanning the graph for every thumbnail.
            var nodeIds = new HashSet<string>(graph.Nodes.Select(n => n.Id));
            foreach (var stale in inlineThumbnails.Keys.Where(id => !nodeIds.Contains(id)).ToList())
            { DisposeThumbnail(inlineThumbnails[stale]); inlineThumbnails.Remove(stale); }
            var active = inlineThumbnails.Values.Where(s => s.Enabled).Take(4).ToList();
            foreach (var state in inlineThumbnails.Values.Where(s => s.Enabled).Skip(4).ToList())
            {
                state.Enabled = false;
                state.Toggle?.SetValueWithoutNotify(false);
                DisposeThumbnail(state);
            }
            if (!active.Any(state => state.Pending)) return;
            var graphHash = GraphJson.ComputeSemanticHash(graph);
            foreach (var state in active)
            {
                if (!state.Pending) continue;
                if (!nodes.TryGetValue(state.NodeId, out var nodeBox) || !nodeBox.worldBound.Overlaps(canvas.worldBound)) continue;
                state.Pending = false;
                try
                {
                    var node = graph.Nodes.FirstOrDefault(n => n.Id == state.NodeId);
                    if (node == null) continue;
                    var hash = graphHash + ":" + state.NodeId + ":" + state.Port;
                    if (hash == state.Hash && state.Preview != null) continue;
                    var candidate = GraphPreview.Create(BuildInlinePreviewGraph(node, state.Port), null);
                    DisposeThumbnail(state);
                    state.Preview = candidate;
                    state.Editor = UnityEditor.Editor.CreateEditor(candidate.Material);
                    state.Hash = hash;
                    state.Host.style.display = DisplayStyle.Flex;
                    state.Error.style.display = DisplayStyle.None;
                    state.Host.Clear();
                    state.Host.Add(new IMGUIContainer(() => {
                        if (state.Editor != null) state.Editor.OnPreviewGUI(new Rect(0, 0, 128, 96), GUIStyle.none);
                    }) { style = { width = 128, height = 96 } });
                }
                catch (Exception ex)
                {
                    state.Error.text = "Preview: " + ex.Message;
                    state.Error.style.display = DisplayStyle.Flex;
                    state.Host.style.display = state.Preview == null ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }
        }

        void RepaintInlinePreviews()
        {
            if (!livePreview || focusedWindow != this || graph == null || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var now = EditorApplication.timeSinceStartup;
            if (now - inlineLastRepaint < .1) return;
            inlineLastRepaint = now;
            var bounds = rootVisualElement == null ? new Rect() : rootVisualElement.worldBound;
            foreach (var state in inlineThumbnails.Values)
                if (state.Enabled && state.Preview != null && state.Host != null && state.Host.worldBound.Overlaps(bounds)) state.Host.MarkDirtyRepaint();
            if (inlineThumbnails.Values.Any(s => s.Enabled && s.Preview != null && s.Host != null && s.Host.worldBound.Overlaps(bounds))) Repaint();
        }

        ShaderGraph BuildInlinePreviewGraph(GraphNode source, string port)
        {
            var clone = GraphJson.Parse(GraphJson.Serialize(graph));
            var node = clone.Nodes.First(n => n.Id == source.Id);
            var type = GraphTypes.PortType(clone, node, port, GraphTypes.Infer(clone));
            if (type == null) throw new InvalidOperationException("Output is unavailable.");
            clone.Nodes.RemoveAll(n => n.Operation == "core.output");
            var ids = new HashSet<string>(clone.Nodes.Select(n => n.Id));
            clone.Connections.RemoveAll(e => !ids.Contains(e.From.NodeId) || !ids.Contains(e.To.NodeId));
            Action<string, string, string, string> wire = (from, fromPort, to, toPort) => clone.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
            var current = node; var currentPort = port;
            if (type == "vector2" || type == "vector3") { var converter = NodeCatalog.Create("core.previewVector"); clone.Nodes.Add(converter); wire(current.Id, currentPort, converter.Id, type == "vector2" ? "uv" : "normal"); current = converter; currentPort = "color"; type = "color"; }
            if (type != "surface") { var surface = NodeCatalog.Create("core.unlitSurface"); clone.Nodes.Add(surface); wire(current.Id, currentPort, surface.Id, "albedo"); current = surface; currentPort = "surface"; }
            var output = NodeCatalog.Create("core.output"); clone.Nodes.Add(output); wire(current.Id, currentPort, output.Id, "surface");
            return clone;
        }

        void DisposeInlinePreviews()
        {
            foreach (var state in inlineThumbnails.Values) DisposeThumbnail(state);
            inlineThumbnails.Clear();
            inlinePreviewQueued = false;
        }

        void DisposeThumbnail(NodeThumbnail state)
        {
            if (state == null) return;
            if (state.Editor != null) UnityEngine.Object.DestroyImmediate(state.Editor);
            if (state.Preview != null) state.Preview.Dispose();
            state.Editor = null; state.Preview = null; state.Hash = null;
            if (state.Host != null) { state.Host.Clear(); state.Host.style.display = DisplayStyle.None; }
        }
    }
}
