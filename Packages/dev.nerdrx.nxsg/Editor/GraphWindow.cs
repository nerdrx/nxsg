using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed class GraphWindow : EditorWindow
    {
        [SerializeField] GraphSession session;
        [SerializeField] string sourcePath;
        [SerializeField] string diskSource;
        [SerializeField] string selected;
        [SerializeField] Vector2 pan = new Vector2(30, 70);
        [SerializeField] float zoom = 1;
        ShaderGraph graph;
        VisualElement canvas, layer, inspector;
        Label status;
        readonly Dictionary<string, VisualElement> nodes = new Dictionary<string, VisualElement>();
        string pendingNode, pendingPort;
        Material preview;
        UnityEditor.Editor previewEditor;
        bool dragging, panning;
        Vector2 pointerStart, origin;

        [MenuItem("Tools/NXSG/Open Graph Editor")]
        public static void ShowEditor() { GetWindow<GraphWindow>("NX Shader Graph"); }

        public static void Open(string path)
        {
            var window = GetWindow<GraphWindow>("NX Shader Graph");
            if (!window.CanDiscard()) return;
            window.LoadPath(path);
        }

        void OnEnable()
        {
            minSize = new Vector2(850, 500);
            if (session == null)
            {
                session = CreateInstance<GraphSession>();
                session.hideFlags = HideFlags.HideAndDontSave;
            }
            Undo.undoRedoPerformed += Restore;
            Restore();
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Restore;
            ClearPreview();
        }

        void OnFocus() { canvas?.Focus(); }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = new Color(.035f, .03f, .045f);
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(NewGraph) { text = "New" });
            toolbar.Add(new ToolbarButton(() =>
            {
                if (!CanDiscard()) return;
                var path = EditorUtility.OpenFilePanel("Open NXSG graph", Application.dataPath, "nxsg");
                if (!string.IsNullOrEmpty(path)) LoadPath(path);
            }) { text = "Open" });
            toolbar.Add(new ToolbarButton(() => SaveGraph()) { text = "Save" });
            toolbar.Add(new ToolbarButton(SaveCopy) { text = "Save as" });
            toolbar.Add(new ToolbarButton(Undo.PerformUndo) { text = "Undo" });
            toolbar.Add(new ToolbarButton(Undo.PerformRedo) { text = "Redo" });
            toolbar.Add(new ToolbarButton(Build) { text = "Build for VRChat" });
            toolbar.Add(new ToolbarButton(() => { pan = new Vector2(30, 70); zoom = 1; TransformCanvas(); }) { text = "Reset view" });
            rootVisualElement.Add(toolbar);
            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            canvas = new VisualElement { focusable = true, style = { flexGrow = 1, overflow = Overflow.Hidden } };
            layer = new VisualElement { style = { position = UnityEngine.UIElements.Position.Absolute, width = 4000, height = 4000 } };
            layer.generateVisualContent += DrawEdges;
            canvas.Add(layer);
            canvas.RegisterCallback<PointerDownEvent>(CanvasDown);
            canvas.RegisterCallback<PointerMoveEvent>(CanvasMove);
            canvas.RegisterCallback<PointerUpEvent>(CanvasUp);
            canvas.RegisterCallback<WheelEvent>(evt =>
            {
                var position = canvas.WorldToLocal(evt.mousePosition);
                var previous = zoom;
                zoom = Mathf.Clamp(zoom * Mathf.Exp(-evt.delta.y * .045f), .3f, 1.6f);
                pan = position - (position - pan) * (zoom / previous);
                TransformCanvas();
                evt.StopPropagation();
            });
            canvas.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { pendingNode = null; SetStatus("Connection cancelled."); }
                if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) DeleteSelection();
                if (evt.actionKey && evt.keyCode == KeyCode.S) { SaveGraph(); evt.StopPropagation(); }
            });
            body.Add(canvas);
            inspector = new VisualElement { style = { width = 270, paddingLeft = 12, paddingRight = 12, paddingTop = 12, backgroundColor = new Color(.08f, .065f, .10f) } };
            body.Add(inspector);
            rootVisualElement.Add(body);
            status = new Label("Create or open a graph. Middle-drag pans; wheel zooms. Click output then input to connect.")
            { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 10, paddingTop = 6, paddingBottom = 6 } };
            rootVisualElement.Add(status);
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!evt.actionKey) return;
                var element = evt.target as VisualElement;
                if (element is TextField || element?.GetFirstAncestorOfType<TextField>() != null) return;
                if (evt.keyCode != KeyCode.S) return;
                if (evt.shiftKey) SaveCopy(); else SaveGraph();
                evt.StopPropagation(); evt.PreventDefault();
            }, TrickleDown.TrickleDown);
            Rebuild();
        }

        bool CanDiscard()
        {
            return !hasUnsavedChanges || EditorUtility.DisplayDialog("Unsaved graph", "Discard the unsaved edits and open another graph?", "Discard", "Keep editing");
        }

        void NewGraph()
        {
            if (!CanDiscard()) return;
            graph = GraphSamples.CreateDefault();
            graph.GraphId = Guid.NewGuid().ToString("N");
            sourcePath = null; diskSource = null; selected = null;
            session.json = GraphJson.Serialize(graph, true);
            Undo.ClearUndo(session);
            hasUnsavedChanges = true;
            Rebuild();
        }

        void LoadPath(string path)
        {
            try
            {
                if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Graph exceeds the 4 MiB limit.");
                var source = File.ReadAllText(path);
                var parsed = GraphJson.Parse(source);
                CheckEditableShape(parsed);
                graph = parsed; sourcePath = Path.GetFullPath(path); diskSource = source;
                session.json = GraphJson.Serialize(parsed, true);
                Undo.ClearUndo(session); selected = null; hasUnsavedChanges = false;
                ClearPreview(); Rebuild(); SetStatus(Path.GetFileName(path));
            }
            catch (Exception exception) { SetStatus(exception.Message); }
        }

        void Restore()
        {
            if (session == null || string.IsNullOrEmpty(session.json)) return;
            try { graph = GraphJson.Parse(session.json); Rebuild(); canvas?.Focus(); }
            catch (Exception exception) { SetStatus(exception.Message); }
            hasUnsavedChanges = graph != null && (diskSource == null || GraphJson.Serialize(graph, true) != CanonicalDisk());
        }

        string CanonicalDisk()
        {
            try { return GraphJson.Serialize(GraphJson.Parse(diskSource), true); }
            catch { return diskSource; }
        }

        static void CheckEditableShape(ShaderGraph value)
        {
            if (value.Nodes == null || value.Nodes.Count > 4096 || value.Connections == null || value.Connections.Count > 16384 || value.Resources == null)
                throw new InvalidDataException("Graph collections are missing or exceed the safety limits. The source file is unchanged.");
            if (value.Nodes.Any(n => n == null || string.IsNullOrEmpty(n.Id) || n.Properties == null)
                || value.Connections.Any(e => e == null || e.From == null || e.To == null)
                || value.Resources.Any(r => r == null))
                throw new InvalidDataException("A graph entry is incomplete. Repair the source entry before opening it; the file is unchanged.");
        }

        void Edit(string name, Action action)
        {
            if (graph == null) return;
            Undo.RegisterCompleteObjectUndo(session, name);
            action();
            session.json = GraphJson.Serialize(graph, true);
            hasUnsavedChanges = true;
            EditorUtility.SetDirty(session);
            Rebuild();
            canvas?.Focus();
            SetStatus("Unsaved edits · preview shows the last successful build.");
        }

        public bool SaveGraph()
        {
            if (graph == null) return false;
            if (string.IsNullOrEmpty(sourcePath))
            {
                var path = EditorUtility.SaveFilePanelInProject("Save NXSG graph", "New Material", "nxsg", "Choose where to save your graph.");
                if (string.IsNullOrEmpty(path)) return false;
                sourcePath = Path.GetFullPath(path);
                diskSource = File.Exists(sourcePath) ? File.ReadAllText(sourcePath) : null;
            }
            try
            {
                if (diskSource != null && (!File.Exists(sourcePath) || File.ReadAllText(sourcePath) != diskSource))
                    throw new IOException("The source changed on disk. Reopen it or save a copy before continuing; your edits are still in this window.");
                var text = GraphJson.Serialize(graph, true);
                var temporary = sourcePath + ".saving-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(temporary, text);
                    if (File.Exists(sourcePath)) File.Replace(temporary, sourcePath, null);
                    else File.Move(temporary, sourcePath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                diskSource = text; session.json = text; hasUnsavedChanges = false;
                AssetDatabase.Refresh(); SetStatus("Saved " + Path.GetFileName(sourcePath));
                return true;
            }
            catch (Exception exception) { SetStatus(exception.Message); return false; }
        }

        public override void SaveChanges() { if (SaveGraph()) base.SaveChanges(); }

        void SaveCopy()
        {
            if (graph == null) return;
            var oldPath = sourcePath; var oldDisk = diskSource;
            sourcePath = null; diskSource = null;
            if (!SaveGraph()) { sourcePath = oldPath; diskSource = oldDisk; }
        }

        void Build()
        {
            if (!SaveGraph()) return;
            try
            {
                var material = GraphBuild.Build(graph, sourcePath);
                ClearPreview();
                preview = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                previewEditor = UnityEditor.Editor.CreateEditor(preview);
                RebuildInspector();
                SetStatus("Built local shader + material. Preview updated. Client validation is a separate step.");
            }
            catch (Exception exception) { SetStatus(exception.Message); }
        }

        void ClearPreview()
        {
            if (previewEditor != null) DestroyImmediate(previewEditor);
            if (preview != null) DestroyImmediate(preview);
            previewEditor = null; preview = null;
        }

        void Rebuild()
        {
            if (layer == null) return;
            layer.Clear(); nodes.Clear();
            if (graph != null)
                foreach (var node in graph.Nodes)
                {
                    var position = Position(node.Id);
                    var box = new VisualElement { focusable = true, style = { position = UnityEngine.UIElements.Position.Absolute, left = position.x, top = position.y, width = 175, backgroundColor = node.Id == selected ? new Color(.25f, .08f, .43f) : new Color(.12f, .10f, .16f), borderTopLeftRadius = 8, borderTopRightRadius = 8, borderBottomLeftRadius = 8, borderBottomRightRadius = 8, paddingBottom = 8 } };
                    var title = new Label(Title(node.Operation)) { style = { paddingLeft = 12, paddingTop = 10, paddingBottom = 10, unityFontStyleAndWeight = FontStyle.Bold, backgroundColor = new Color(.30f, .025f, .58f) } };
                    box.Add(title);
                    title.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        selected = node.Id; dragging = true; pointerStart = evt.position; origin = Position(node.Id);
                        title.CapturePointer(evt.pointerId); RebuildInspector(); evt.StopPropagation();
                    });
                    title.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        if (!dragging || selected != node.Id || !title.HasPointerCapture(evt.pointerId)) return;
                        var point = origin + ((Vector2)evt.position - pointerStart) / zoom;
                        SetPosition(node.Id, point); box.style.left = point.x; box.style.top = point.y;
                        layer.MarkDirtyRepaint(); evt.StopPropagation();
                    });
                    title.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (!title.HasPointerCapture(evt.pointerId)) return;
                        dragging = false; title.ReleasePointer(evt.pointerId);
                        Undo.IncrementCurrentGroup();
                        Undo.RegisterCompleteObjectUndo(session, "Move node");
                        session.json = GraphJson.Serialize(graph, true); hasUnsavedChanges = true; EditorUtility.SetDirty(session);
                        canvas.Focus(); evt.StopPropagation();
                    });
                    foreach (var port in Ports(node.Operation, false))
                    {
                        var input = port;
                        box.Add(new Button(() => Connect(node.Id, input)) { text = "●  " + input, tooltip = "Input: " + input });
                    }
                    foreach (var port in Ports(node.Operation, true))
                    {
                        var output = port;
                        box.Add(new Button(() => { pendingNode = node.Id; pendingPort = output; selected = node.Id; RebuildInspector(); SetStatus("Choose an input for " + Title(node.Operation) + "." + output); }) { text = output + "  ●", tooltip = "Output: " + output });
                    }
                    box.RegisterCallback<FocusInEvent>(_ => { selected = node.Id; RebuildInspector(); });
                    layer.Add(box); nodes[node.Id] = box;
                }
            TransformCanvas(); RebuildInspector();
        }

        void RebuildInspector()
        {
            if (inspector == null) return;
            inspector.Clear();
            inspector.Add(new Label("NX SHADER GRAPH") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 16, marginBottom = 12 } });
            var search = new ToolbarSearchField(); inspector.Add(search);
            var choices = new VisualElement(); inspector.Add(choices);
            Action<string> filter = query =>
            {
                choices.Clear();
                foreach (var operation in new[] { "core.uv0", "core.texture2D", "core.constant", "core.multiply", "core.toonSurface", "core.output" })
                    if ((Title(operation) + " " + operation + " " + Aliases(operation)).IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var op = operation;
                        choices.Add(new Button(() => AddNode(op)) { text = "+ " + Title(op) });
                    }
            };
            search.RegisterValueChangedCallback(evt => filter(evt.newValue)); filter("");
            var node = graph?.Nodes.FirstOrDefault(n => n.Id == selected);
            if (node != null)
            {
                inspector.Add(new Label(Title(node.Operation)) { style = { marginTop = 15, unityFontStyleAndWeight = FontStyle.Bold } });
                if (node.Operation == "core.constant")
                {
                    var values = node.Properties["value"] as JArray;
                    var field = new ColorField("Color") { value = values != null && values.Count == 4 ? new Color((float)values[0], (float)values[1], (float)values[2], (float)values[3]) : Color.white };
                    field.RegisterValueChangedCallback(evt => Edit("Change color", () => { node.Properties["valueType"] = "color"; node.Properties["value"] = new JArray(evt.newValue.r, evt.newValue.g, evt.newValue.b, evt.newValue.a); }));
                    inspector.Add(field);
                }
                if (node.Operation == "core.texture2D")
                {
                    var field = new ObjectField("Texture") { objectType = typeof(Texture2D), allowSceneObjects = false };
                    var resourceId = (string)node.Properties["resourceId"];
                    var map = graph.Adapter?["textures"] as JObject;
                    var guid = (string)map?[resourceId ?? ""];
                    if (!string.IsNullOrEmpty(guid)) field.value = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                    field.SetEnabled(!string.IsNullOrEmpty(resourceId) && graph.Resources.Any(r => r.Id == resourceId));
                    field.RegisterValueChangedCallback(evt => Edit("Assign texture", () =>
                    {
                        if (graph.Adapter == null) graph.Adapter = new JObject();
                        if (!(graph.Adapter["textures"] is JObject)) graph.Adapter["textures"] = new JObject();
                        ((JObject)graph.Adapter["textures"])[resourceId] = evt.newValue == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(evt.newValue));
                    }));
                    inspector.Add(field);
                }
                inspector.Add(new Button(() => Edit("Disconnect node", () => graph.Connections.RemoveAll(e => e.From.NodeId == selected || e.To.NodeId == selected))) { text = "Disconnect node" });
                inspector.Add(new Button(DeleteSelection) { text = "Delete node" });
            }
            if (previewEditor != null)
            {
                inspector.Add(new Label("Last successful build"));
                inspector.Add(new IMGUIContainer(() =>
                {
                    if (previewEditor != null) previewEditor.OnPreviewGUI(GUILayoutUtility.GetRect(240, 220), EditorStyles.helpBox);
                }) { style = { height = 220 } });
            }
        }

        void AddNode(string operation)
        {
            Edit("Add " + Title(operation), () =>
            {
                var node = new GraphNode { Id = Guid.NewGuid().ToString("N"), Operation = operation };
                if (operation == "core.constant") { node.Properties["valueType"] = "color"; node.Properties["value"] = new JArray(1, 1, 1, 1); }
                if (operation == "core.multiply") node.Properties["valueType"] = "color";
                if (operation == "core.texture2D")
                {
                    var resource = new GraphResource { Id = "texture-" + node.Id, Kind = "texture2D", Uri = "builtin://white" };
                    graph.Resources.Add(resource); node.Properties["resourceId"] = resource.Id;
                }
                graph.Nodes.Add(node); selected = node.Id;
                SetPosition(node.Id, new Vector2(70 + graph.Nodes.Count * 12, 100 + graph.Nodes.Count * 12));
            });
        }

        void Connect(string target, string port)
        {
            if (pendingNode == null) { selected = target; RebuildInspector(); SetStatus("Select an output first, then this input."); return; }
            var reachable = new HashSet<string> { target };
            var queue = new Queue<string>(); queue.Enqueue(target);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var edge in graph.Connections.Where(e => e.From.NodeId == id))
                    if (reachable.Add(edge.To.NodeId)) queue.Enqueue(edge.To.NodeId);
            }
            if (reachable.Contains(pendingNode)) { SetStatus("That connection would create a cycle."); return; }
            var from = graph.Nodes.First(n => n.Id == pendingNode);
            var to = graph.Nodes.First(n => n.Id == target);
            if (PortType(from, pendingPort) != PortType(to, port)) { SetStatus("Socket types do not match. Use matching color, UV or surface sockets."); return; }
            Edit("Connect nodes", () =>
            {
                graph.Connections.RemoveAll(e => e.To.NodeId == target && e.To.PortId == port);
                graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = new GraphPortRef { NodeId = pendingNode, PortId = pendingPort }, To = new GraphPortRef { NodeId = target, PortId = port } });
                pendingNode = null;
            });
        }

        void DeleteSelection()
        {
            if (selected == null || graph == null) return;
            Edit("Delete node", () => { graph.Nodes.RemoveAll(n => n.Id == selected); graph.Connections.RemoveAll(e => e.From.NodeId == selected || e.To.NodeId == selected); selected = null; pendingNode = null; });
        }

        Vector2 Position(string id)
        {
            if (graph.Layout?.Nodes != null && graph.Layout.Nodes.TryGetValue(id, out var value) && value != null)
                return new Vector2((float)Math.Max(-100000, Math.Min(100000, double.IsNaN(value.X) ? 0 : value.X)), (float)Math.Max(-100000, Math.Min(100000, double.IsNaN(value.Y) ? 0 : value.Y)));
            return new Vector2(graph.Nodes.FindIndex(n => n.Id == id) * 215, 80);
        }
        void SetPosition(string id, Vector2 position)
        {
            if (graph.Layout == null) graph.Layout = new GraphLayout();
            if (graph.Layout.Nodes == null) graph.Layout.Nodes = new Dictionary<string, GraphNodeLayout>();
            graph.Layout.Nodes[id] = new GraphNodeLayout { X = position.x, Y = position.y };
        }
        void TransformCanvas()
        {
            layer.transform.position = pan; layer.transform.scale = new Vector3(zoom, zoom, 1); layer.MarkDirtyRepaint();
        }
        void CanvasDown(PointerDownEvent evt)
        {
            if (evt.button == 0 && (evt.target == canvas || evt.target == layer))
            {
                canvas.Focus(); evt.StopPropagation(); return;
            }
            if (evt.button != 2) return;
            panning = true; pointerStart = evt.position; origin = pan; canvas.CapturePointer(evt.pointerId); evt.StopPropagation();
        }
        void CanvasMove(PointerMoveEvent evt)
        {
            if (!panning) return;
            pan = origin + ((Vector2)evt.position - pointerStart); TransformCanvas(); evt.StopPropagation();
        }
        void CanvasUp(PointerUpEvent evt)
        {
            if (!panning) return;
            panning = false; canvas.ReleasePointer(evt.pointerId); evt.StopPropagation();
        }
        void DrawEdges(MeshGenerationContext context)
        {
            if (graph == null) return;
            var painter = context.painter2D; painter.lineWidth = 3; painter.strokeColor = new Color(.65f, .33f, 1);
            foreach (var edge in graph.Connections)
            {
                if (!nodes.TryGetValue(edge.From.NodeId, out var from) || !nodes.TryGetValue(edge.To.NodeId, out var to)) continue;
                var a = new Vector2(from.resolvedStyle.left + 175, from.resolvedStyle.top + from.resolvedStyle.height - 20);
                var b = new Vector2(to.resolvedStyle.left, to.resolvedStyle.top + 50);
                if (float.IsNaN(a.y) || float.IsNaN(b.y)) continue;
                painter.BeginPath(); painter.MoveTo(a); painter.BezierCurveTo(a + Vector2.right * 70, b - Vector2.right * 70, b); painter.Stroke();
            }
        }
        void SetStatus(string message) { if (status != null) status.text = message; }
        static string Title(string operation)
        {
            switch (operation) { case "core.uv0": return "UV Coordinates"; case "core.texture2D": return "Texture"; case "core.constant": return "Color"; case "core.multiply": return "Multiply"; case "core.toonSurface": return "Toon Surface"; case "core.output": return "Output"; default: return operation + " (unavailable)"; }
        }
        static string Aliases(string operation) { return operation == "core.multiply" ? "tint darken blend" : operation == "core.toonSurface" ? "anime cel cartoon shading" : operation == "core.texture2D" ? "image albedo diffuse" : ""; }
        static string PortType(GraphNode node, string port) { if (port == "surface") return "surface"; if (port == "uv") return "vector2"; return (string)node.Properties?["valueType"] ?? "color"; }
        static string[] Ports(string operation, bool output)
        {
            switch (operation)
            {
                case "core.uv0": return output ? new[] { "uv" } : Array.Empty<string>();
                case "core.texture2D": return output ? new[] { "color" } : new[] { "uv" };
                case "core.constant": return output ? new[] { "value" } : Array.Empty<string>();
                case "core.multiply": return output ? new[] { "value" } : new[] { "a", "b" };
                case "core.toonSurface": return output ? new[] { "surface" } : new[] { "albedo" };
                case "core.output": return output ? Array.Empty<string>() : new[] { "surface" };
                default: return Array.Empty<string>();
            }
        }
    }
}
