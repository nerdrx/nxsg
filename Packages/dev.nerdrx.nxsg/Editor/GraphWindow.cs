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
        [SerializeField] Material contextMaterial;
        [SerializeField] string selected;
        [SerializeField] List<string> selection = new List<string>();
        readonly Dictionary<string, Vector2> dragOrigins = new Dictionary<string, Vector2>();
        VisualElement marquee;
        bool boxSelecting;
        Vector2 boxStart;
        string[] boxInitial;
        [SerializeField] Vector2 pan = new Vector2(30, 70);
        [SerializeField] float zoom = 1;
        ShaderGraph graph;
        VisualElement canvas, layer, inspector;
        Label status, identity;
        readonly Dictionary<string, VisualElement> nodes = new Dictionary<string, VisualElement>();
        string pendingNode, pendingPort;
        string lastPaste;
        int pasteCount;
        readonly List<SocketView> sockets = new List<SocketView>();
        bool wiring, wireMoved, pendingOutput;
        VisualElement spawnMenu;
        int wirePointer;
        Vector2 wireStart, wirePosition;

        sealed class SocketView
        {
            public string node, port, type;
            public bool output;
            public VisualElement hit, dot;
        }
        Material preview;
        UnityEditor.Editor previewEditor;
        bool dragging, panning;
        Vector2 pointerStart, origin;

        [MenuItem("Tools/NXSG/Open Graph Editor")]
        public static void ShowEditor() { GetWindow<GraphWindow>("NX Shader Graph"); }

        public static void Open(string path, Material material = null)
        {
            var window = GetWindow<GraphWindow>("NX Shader Graph");
            if (!window.CanDiscard()) return;
            window.LoadPath(path);
            if (window.sourcePath == Path.GetFullPath(path)) { window.contextMaterial = material; window.UpdateIdentity(); }
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
            CancelWire();
            Undo.undoRedoPerformed -= Restore;
            ClearPreview();
        }

        void OnFocus() { canvas?.Focus(); }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = new Color(.055f, .055f, .055f);
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
            toolbar.Add(new ToolbarButton(CopySelection) { text = "Copy", tooltip = "Copy selected nodes (Ctrl+C)" });
            toolbar.Add(new ToolbarButton(PasteSelection) { text = "Paste", tooltip = "Paste nodes (Ctrl+V)" });
            toolbar.Add(new ToolbarButton(DuplicateSelection) { text = "Duplicate", tooltip = "Duplicate selected nodes (Ctrl+D)" });
            toolbar.Add(new ToolbarButton(Build) { text = "Build for VRChat" });
            toolbar.Add(new ToolbarButton(() => { pan = new Vector2(30, 70); zoom = 1; TransformCanvas(); }) { text = "Reset view" });
            rootVisualElement.Add(toolbar);
            identity = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 10, paddingTop = 5, paddingBottom = 5 } };
            rootVisualElement.Add(identity);
            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            canvas = new VisualElement { focusable = true, style = { flexGrow = 1, overflow = Overflow.Hidden } };
            layer = new VisualElement { style = { position = UnityEngine.UIElements.Position.Absolute, width = 4000, height = 4000 } };
            layer.generateVisualContent += DrawEdges;
            canvas.Add(layer);
            marquee = new VisualElement { pickingMode = PickingMode.Ignore, style = {
                position = UnityEngine.UIElements.Position.Absolute, display = DisplayStyle.None,
                backgroundColor = new Color(.65f, .75f, .85f, .12f),
                borderLeftWidth = 1, borderRightWidth = 1, borderTopWidth = 1, borderBottomWidth = 1,
                borderLeftColor = Color.white, borderRightColor = Color.white, borderTopColor = Color.white, borderBottomColor = Color.white } };
            canvas.Add(marquee);
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
                if (evt.keyCode == KeyCode.Escape) { CancelBox(true); CancelWire(); SetStatus("Cancelled."); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) DeleteSelection();
                if (evt.actionKey && evt.keyCode == KeyCode.S) { SaveGraph(); evt.StopPropagation(); }
            });
            body.Add(canvas);
            inspector = new VisualElement { style = { width = 270, paddingLeft = 12, paddingRight = 12, paddingTop = 12, backgroundColor = new Color(.10f, .10f, .10f) } };
            body.Add(inspector);
            rootVisualElement.Add(body);
            status = new Label("Create or open a graph. Drag empty space to box-select; Shift adds. Middle-drag pans; wheel zooms. Drag between matching sockets in either direction. Drop on empty space to add a node. Esc cancels.")
            { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 10, paddingTop = 6, paddingBottom = 6 } };
            rootVisualElement.Add(status);
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!evt.actionKey) return;
                var element = evt.target as VisualElement;
                if (IsEditingText(element) || IsEditingText(rootVisualElement.panel?.focusController.focusedElement as VisualElement)) return;
                switch (evt.keyCode)
                {
                    case KeyCode.C: CopySelection(); break;
                    case KeyCode.V: PasteSelection(); break;
                    case KeyCode.D: DuplicateSelection(); break;
                    case KeyCode.S: if (evt.shiftKey) SaveCopy(); else SaveGraph(); break;
                    default: return;
                }
                evt.StopPropagation(); evt.PreventDefault();
            }, TrickleDown.TrickleDown);
            Rebuild();
        }

        void OnSelectionChange()
        {
            var material = Selection.activeObject as Material;
            if (MatchesSource(material)) { contextMaterial = material; ClearPreview(); RebuildInspector(); }
            UpdateIdentity();
        }

        bool MatchesSource(Material material)
        {
            var path = GraphMaterialHeader.SourcePath(material);
            return !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(sourcePath) && Path.GetFullPath(path) == sourcePath;
        }

        void UpdateIdentity()
        {
            var graphName = string.IsNullOrEmpty(sourcePath) ? "Untitled graph" : Path.GetFileName(sourcePath);
            titleContent = new GUIContent(graphName + " — NXSG");
            if (identity == null) return;
            if (contextMaterial != null && !MatchesSource(contextMaterial)) contextMaterial = null;
            if (contextMaterial == null && MatchesSource(Selection.activeObject as Material)) contextMaterial = Selection.activeObject as Material;
            var shader = contextMaterial != null ? contextMaterial.shader : null;
            if (shader == null && !string.IsNullOrEmpty(sourcePath))
            {
                var assetPath = FileUtil.GetProjectRelativePath(sourcePath);
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!string.IsNullOrEmpty(guid)) shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/NXSGGenerated/" + guid + "/Material.shader");
            }
            identity.text = "Graph: " + graphName + "   •   Material: " + (contextMaterial != null ? contextMaterial.name : "none selected (neutral graph preview)")
                + "\nShader: " + (shader != null ? shader.name : "not built yet");
            identity.tooltip = (sourcePath ?? "Save this graph to name it.") + (contextMaterial != null ? "\n" + AssetDatabase.GetAssetPath(contextMaterial) : "");
        }

        static bool IsEditingText(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current is TextField || current.ClassListContains("unity-base-text-field")) return true;
            return false;
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
            sourcePath = null; diskSource = null; contextMaterial = null; selected = null; selection.Clear();
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
                graph = parsed; sourcePath = Path.GetFullPath(path); diskSource = source; contextMaterial = null;
                session.json = GraphJson.Serialize(parsed, true);
                Undo.ClearUndo(session); selected = null; selection.Clear(); hasUnsavedChanges = false;
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
                AssetDatabase.Refresh(); UpdateIdentity(); SetStatus("Saved " + Path.GetFileName(sourcePath));
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
                UpdateIdentity();
                var useContext = contextMaterial != null && contextMaterial.shader == material.shader;
                preview = new Material(useContext ? contextMaterial : material) { hideFlags = HideFlags.HideAndDontSave };
                // A directly opened graph uses neutral tint; a material-opened graph previews that material.
                if (!useContext && preview.HasProperty("_Color")) preview.SetColor("_Color", Color.white);
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
            CancelWire();
            layer.Clear(); nodes.Clear(); sockets.Clear();
            if (graph != null)
                foreach (var node in graph.Nodes)
                {
                    var position = Position(node.Id);
                    var box = new VisualElement { focusable = true, style = { position = UnityEngine.UIElements.Position.Absolute, left = position.x, top = position.y, width = 175, backgroundColor = new Color(.14f, .14f, .14f), borderLeftWidth = 2, borderRightWidth = 2, borderTopWidth = 2, borderBottomWidth = 2, borderTopLeftRadius = 8, borderTopRightRadius = 8, borderBottomLeftRadius = 8, borderBottomRightRadius = 8, paddingBottom = 8 } };
                    var title = new Label(Title(node.Operation)) { style = { paddingLeft = 12, paddingTop = 10, paddingBottom = 10, unityFontStyleAndWeight = FontStyle.Bold, backgroundColor = NodeColor(node.Operation) } };
                    box.Add(title);
                    title.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        SelectNode(node.Id, evt.shiftKey || selection.Contains(node.Id));
                        dragging = true; pointerStart = evt.position;
                        dragOrigins.Clear();
                        foreach (var id in selection) dragOrigins[id] = Position(id);
                        title.CapturePointer(evt.pointerId); RebuildInspector(); evt.StopPropagation();
                    });
                    title.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        if (!dragging || selected != node.Id || !title.HasPointerCapture(evt.pointerId)) return;
                        var delta = ((Vector2)evt.position - pointerStart) / zoom;
                        foreach (var pair in dragOrigins)
                        {
                            var point = pair.Value + delta;
                            SetPosition(pair.Key, point);
                            nodes[pair.Key].style.left = point.x; nodes[pair.Key].style.top = point.y;
                        }
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
                    foreach (var port in Ports(node.Operation, false)) AddSocket(box, node, port, false);
                    foreach (var port in Ports(node.Operation, true)) AddSocket(box, node, port, true);
                    box.RegisterCallback<FocusInEvent>(_ => { if (!selection.Contains(node.Id)) SelectNode(node.Id); });
                    box.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) SelectNode(node.Id, evt.shiftKey || selection.Contains(node.Id)); });
                    layer.Add(box); nodes[node.Id] = box;
                }
            selection.RemoveAll(id => !nodes.ContainsKey(id));
            UpdateSelectionOutline();
            TransformCanvas(); RebuildInspector(); UpdateIdentity();
        }

        void SelectNode(string id, bool additive = false)
        {
            if (!additive) selection.Clear();
            if (id != null && !selection.Contains(id)) selection.Add(id);
            selected = id;
            UpdateSelectionOutline();
            RebuildInspector();
        }

        void UpdateSelectionOutline()
        {
            foreach (var pair in nodes)
            {
                var outline = selection.Contains(pair.Key) ? new Color(.94f, .94f, .94f) : Color.clear;
                pair.Value.style.borderLeftColor = outline;
                pair.Value.style.borderRightColor = outline;
                pair.Value.style.borderTopColor = outline;
                pair.Value.style.borderBottomColor = outline;
            }
        }

        void AddSocket(VisualElement box, GraphNode node, string port, bool output)
        {
            var type = PortType(node, port);
            var row = new VisualElement { style = { height = 28, justifyContent = Justify.Center } };
            row.Add(new Label(port) { pickingMode = PickingMode.Ignore, style = {
                marginLeft = 16, marginRight = 16,
                unityTextAlign = output ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft } });
            var hit = new VisualElement { tooltip = (output ? "Output" : "Input") + ": " + port + " (" + type + ") — drag or click to connect",
                style = { position = UnityEngine.UIElements.Position.Absolute, top = 1, width = 26, height = 26,
                    alignItems = Align.Center, justifyContent = Justify.Center } };
            if (output) hit.style.right = -13; else hit.style.left = -13;
            var dot = new VisualElement { pickingMode = PickingMode.Ignore, style = {
                width = 14, height = 14, backgroundColor = SocketColor(type),
                borderTopLeftRadius = 7, borderTopRightRadius = 7, borderBottomLeftRadius = 7, borderBottomRightRadius = 7,
                borderLeftWidth = 2, borderRightWidth = 2, borderTopWidth = 2, borderBottomWidth = 2,
                borderLeftColor = Color.black, borderRightColor = Color.black, borderTopColor = Color.black, borderBottomColor = Color.black } };
            hit.Add(dot); row.Add(hit); box.Add(row);
            var socket = new SocketView { node = node.Id, port = port, type = type, output = output, hit = hit, dot = dot };
            sockets.Add(socket);
            hit.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                canvas.Focus(); SelectNode(node.Id);
                if (pendingNode != null && pendingOutput != output)
                {
                    Connect(node.Id, port, output);
                    CancelWire();
                }
                else
                {
                    CancelWire();
                    pendingNode = node.Id; pendingPort = port; pendingOutput = output;
                    wiring = true; wireMoved = false; wirePointer = evt.pointerId;
                    wireStart = evt.position; wirePosition = layer.WorldToLocal(evt.position);
                    canvas.CapturePointer(evt.pointerId);
                    foreach (var other in sockets.Where(s => s.output != output))
                        other.dot.style.opacity = other.type == type && other.node != node.Id ? 1f : .3f;
                    layer.MarkDirtyRepaint();
                    SetStatus("Drag to a matching socket, or empty space to add a node. Esc cancels.");
                }
                evt.StopPropagation(); evt.PreventDefault();
            });
            hit.RegisterCallback<GeometryChangedEvent>(_ => layer.MarkDirtyRepaint());
        }

        void CancelWire()
        {
            wiring = false;
            spawnMenu?.RemoveFromHierarchy(); spawnMenu = null;
            if (canvas != null && canvas.HasPointerCapture(wirePointer)) canvas.ReleasePointer(wirePointer);
            pendingNode = null; pendingPort = null;
            foreach (var socket in sockets) socket.dot.style.opacity = 1f;
            layer?.MarkDirtyRepaint();
        }

        static Color NodeColor(string operation)
        {
            switch (operation)
            {
                case "core.uv0": return new Color(.16f, .32f, .52f);
                case "core.texture2D": return new Color(.46f, .25f, .10f);
                case "core.constant": return new Color(.40f, .34f, .10f);
                case "core.multiply": return new Color(.28f, .33f, .38f);
                case "core.toonSurface": return new Color(.13f, .37f, .24f);
                case "core.output": return new Color(.39f, .19f, .20f);
                default: return new Color(.28f, .28f, .28f);
            }
        }

        static Color SocketColor(string type)
        {
            return type == "surface" ? new Color(.35f, .85f, .46f)
                : type == "vector2" ? new Color(.45f, .65f, 1f) : new Color(1f, .78f, .25f);
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
            if (selection.Count > 1)
            {
                inspector.Add(new Label(selection.Count + " nodes selected") { style = { marginTop = 15 } });
                inspector.Add(new Button(DeleteSelection) { text = "Delete selected nodes" });
            }
            var node = selection.Count > 1 ? null : graph?.Nodes.FirstOrDefault(n => n.Id == selected);
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
                inspector.Add(new Label(contextMaterial != null ? "Last build · " + contextMaterial.name : "Last build · neutral material tint"));
                inspector.Add(new IMGUIContainer(() =>
                {
                    if (previewEditor != null) previewEditor.OnPreviewGUI(GUILayoutUtility.GetRect(240, 220), EditorStyles.helpBox);
                }) { style = { height = 220 } });
            }
        }

        GraphNode CreateNode(string operation, Vector2 position)
        {
            var node = new GraphNode { Id = Guid.NewGuid().ToString("N"), Operation = operation };
            if (operation == "core.constant") { node.Properties["valueType"] = "color"; node.Properties["value"] = new JArray(1, 1, 1, 1); }
            if (operation == "core.multiply") node.Properties["valueType"] = "color";
            if (operation == "core.texture2D")
            {
                var resource = new GraphResource { Id = "texture-" + node.Id, Kind = "texture2D", Uri = "builtin://white" };
                graph.Resources.Add(resource); node.Properties["resourceId"] = resource.Id;
            }
            graph.Nodes.Add(node); selected = node.Id; selection.Clear(); selection.Add(node.Id);
            SetPosition(node.Id, position);
            return node;
        }

        void AddNode(string operation)
        {
            Edit("Add " + Title(operation), () => CreateNode(operation, new Vector2(82 + graph.Nodes.Count * 12, 112 + graph.Nodes.Count * 12)));
        }

        void Connect(string target, string port, bool output)
        {
            if (pendingNode == null || pendingOutput == output) return;
            var fromId = pendingOutput ? pendingNode : target;
            var fromPort = pendingOutput ? pendingPort : port;
            var toId = pendingOutput ? target : pendingNode;
            var toPort = pendingOutput ? port : pendingPort;
            var reachable = new HashSet<string> { toId };
            var queue = new Queue<string>(); queue.Enqueue(toId);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var edge in graph.Connections.Where(e => e.From.NodeId == id))
                    if (reachable.Add(edge.To.NodeId)) queue.Enqueue(edge.To.NodeId);
            }
            if (reachable.Contains(fromId)) { SetStatus("That connection would create a cycle."); return; }
            var from = graph.Nodes.First(n => n.Id == fromId);
            var to = graph.Nodes.First(n => n.Id == toId);
            if (PortType(from, fromPort) != PortType(to, toPort)) { SetStatus("Socket types do not match. Use matching color, UV or surface sockets."); return; }
            Edit("Connect nodes", () => AddConnection(fromId, fromPort, toId, toPort));
        }

        void AddConnection(string from, string fromPort, string to, string toPort)
        {
            graph.Connections.RemoveAll(e => e.To.NodeId == to && e.To.PortId == toPort);
            graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"),
                From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
        }

        void ShowSpawnMenu(Vector2 position)
        {
            var endpoint = pendingNode; var endpointPort = pendingPort; var output = pendingOutput;
            var source = graph.Nodes.First(n => n.Id == endpoint);
            var type = PortType(source, endpointPort);
            var graphPosition = layer.WorldToLocal(position);
            var local = canvas.WorldToLocal(position);
            spawnMenu = new VisualElement { style = {
                position = UnityEngine.UIElements.Position.Absolute, width = 240,
                left = Mathf.Clamp(local.x, 0, Mathf.Max(0, canvas.resolvedStyle.width - 240)),
                top = Mathf.Clamp(local.y, 0, Mathf.Max(0, canvas.resolvedStyle.height - 270)),
                backgroundColor = new Color(.18f, .18f, .18f), paddingLeft = 8, paddingRight = 8, paddingTop = 8, paddingBottom = 8 } };
            spawnMenu.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            spawnMenu.Add(new Label("Add connected node · " + type) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 6 } });
            var count = 0;
            foreach (var operation in new[] { "core.uv0", "core.texture2D", "core.constant", "core.multiply", "core.toonSurface", "core.output" })
            {
                if (operation == "core.output" && graph.Nodes.Any(n => n.Operation == operation)) continue;
                var candidate = new GraphNode { Operation = operation };
                foreach (var port in Ports(operation, !output))
                {
                    if (PortType(candidate, port) != type) continue;
                    var op = operation; var compatiblePort = port;
                    spawnMenu.Add(new Button(() => Edit("Add connected " + Title(op), () =>
                    {
                        var node = CreateNode(op, graphPosition - (output ? Vector2.zero : new Vector2(175, 0)));
                        if (output) AddConnection(endpoint, endpointPort, node.Id, compatiblePort);
                        else AddConnection(node.Id, compatiblePort, endpoint, endpointPort);
                    })) { text = Title(op) + " · " + port });
                    count++;
                }
            }
            if (count == 0) spawnMenu.Add(new Label("No compatible nodes available."));
            spawnMenu.Add(new Button(CancelWire) { text = "Cancel" });
            canvas.Add(spawnMenu);
            SetStatus("Choose a compatible node. Existing connections stay until you choose. Esc cancels.");
        }

        void CopySelection()
        {
            if (graph == null || selection.Count == 0) { SetStatus("Select nodes to copy."); return; }
            try
            {
                EditorGUIUtility.systemCopyBuffer = GraphClipboard.Copy(graph, selection);
                lastPaste = null; pasteCount = 0;
                SetStatus("Copied " + selection.Count + " nodes and their internal connections.");
            }
            catch (Exception exception) { SetStatus("Copy failed: " + exception.Message); }
        }

        void PasteSelection()
        {
            if (graph == null) { SetStatus("Create or open a graph before pasting."); return; }
            var text = EditorGUIUtility.systemCopyBuffer;
            var count = text == lastPaste ? pasteCount + 1 : 1;
            if (InsertSnippet(text, 40 * count, "Paste nodes")) { lastPaste = text; pasteCount = count; }
        }

        void DuplicateSelection()
        {
            if (graph == null || selection.Count == 0) { SetStatus("Select nodes to duplicate."); return; }
            try { InsertSnippet(GraphClipboard.Copy(graph, selection), 40, "Duplicate nodes"); }
            catch (Exception exception) { SetStatus("Duplicate failed: " + exception.Message); }
        }

        bool InsertSnippet(string text, double offset, string label)
        {
            try
            {
                // Prepare the whole edit first: invalid clipboard data cannot partially mutate this graph.
                var pasted = GraphClipboard.Paste(graph, text, offset, offset);
                Undo.IncrementCurrentGroup();
                Edit(label, () =>
                {
                    graph = pasted.Graph;
                    selection = pasted.NodeIds.ToList(); selected = selection.LastOrDefault();
                });
                SetStatus(label + ": " + selection.Count + " nodes. Undo restores the previous graph.");
                return true;
            }
            catch (Exception exception) { SetStatus("Paste failed: " + exception.Message); return false; }
        }

        void DeleteSelection()
        {
            if (selection.Count == 0 || graph == null) return;
            var ids = new HashSet<string>(selection);
            Edit("Delete selected nodes", () =>
            {
                graph.Nodes.RemoveAll(n => ids.Contains(n.Id));
                graph.Connections.RemoveAll(e => ids.Contains(e.From.NodeId) || ids.Contains(e.To.NodeId));
                selected = null; selection.Clear(); pendingNode = null;
            });
        }

        void CancelBox(bool restore)
        {
            if (!boxSelecting) return;
            boxSelecting = false;
            if (restore) { selection.Clear(); selection.AddRange(boxInitial); }
            marquee.style.display = DisplayStyle.None;
            if (canvas.HasPointerCapture(boxPointer)) canvas.ReleasePointer(boxPointer);
            selected = selection.LastOrDefault(); UpdateSelectionOutline(); RebuildInspector();
        }
        int boxPointer;

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
                CancelWire(); canvas.Focus();
                boxSelecting = true; boxPointer = evt.pointerId; boxStart = evt.position;
                boxInitial = selection.ToArray();
                if (!evt.shiftKey) selection.Clear();
                boxAdditive = evt.shiftKey;
                canvas.CapturePointer(evt.pointerId);
                UpdateSelectionOutline(); evt.StopPropagation(); return;
            }
            if (evt.button != 2) return;
            panning = true; pointerStart = evt.position; origin = pan; canvas.CapturePointer(evt.pointerId); evt.StopPropagation();
        }
        bool boxAdditive;
        void CanvasMove(PointerMoveEvent evt)
        {
            if (boxSelecting)
            {
                var end = (Vector2)evt.position;
                var bounds = Rect.MinMaxRect(Mathf.Min(boxStart.x, end.x), Mathf.Min(boxStart.y, end.y), Mathf.Max(boxStart.x, end.x), Mathf.Max(boxStart.y, end.y));
                var local = canvas.WorldToLocal(bounds.position);
                marquee.style.display = DisplayStyle.Flex;
                marquee.style.left = local.x; marquee.style.top = local.y;
                marquee.style.width = bounds.width; marquee.style.height = bounds.height;
                selection.Clear();
                if (boxAdditive) selection.AddRange(boxInitial);
                foreach (var pair in nodes)
                    if (bounds.Overlaps(pair.Value.worldBound) && !selection.Contains(pair.Key)) selection.Add(pair.Key);
                UpdateSelectionOutline(); evt.StopPropagation(); return;
            }
            if (pendingNode != null && spawnMenu == null)
            {
                wirePosition = layer.WorldToLocal(evt.position);
                if (wiring && Vector2.Distance(wireStart, evt.position) > 4) wireMoved = true;
                layer.MarkDirtyRepaint();
            }
            if (!panning) return;
            pan = origin + ((Vector2)evt.position - pointerStart); TransformCanvas(); evt.StopPropagation();
        }
        void CanvasUp(PointerUpEvent evt)
        {
            if (boxSelecting && evt.pointerId == boxPointer && evt.button == 0)
            { CancelBox(false); evt.StopPropagation(); return; }
            if (wiring && evt.pointerId == wirePointer && evt.button == 0)
            {
                wiring = false;
                canvas.ReleasePointer(evt.pointerId);
                var target = sockets.FirstOrDefault(s => s.output != pendingOutput && s.hit.worldBound.Contains(evt.position));
                if (target != null) { Connect(target.node, target.port, target.output); CancelWire(); }
                else if (wireMoved)
                {
                    if (canvas.worldBound.Contains(evt.position) && !nodes.Values.Any(n => n.worldBound.Contains(evt.position))) ShowSpawnMenu(evt.position);
                    else { CancelWire(); SetStatus("Connection cancelled. Drop on a socket or empty canvas."); }
                }
                evt.StopPropagation(); return;
            }
            if (!panning) return;
            panning = false; canvas.ReleasePointer(evt.pointerId); evt.StopPropagation();
        }
        void DrawEdges(MeshGenerationContext context)
        {
            if (graph == null) return;
            var painter = context.painter2D; painter.lineWidth = 3;
            foreach (var edge in graph.Connections)
            {
                var from = sockets.FirstOrDefault(s => s.output && s.node == edge.From.NodeId && s.port == edge.From.PortId);
                var to = sockets.FirstOrDefault(s => !s.output && s.node == edge.To.NodeId && s.port == edge.To.PortId);
                if (from == null || to == null) continue;
                painter.strokeColor = SocketColor(from.type);
                DrawWire(painter, layer.WorldToLocal(from.hit.worldBound.center), layer.WorldToLocal(to.hit.worldBound.center));
            }
            var pending = sockets.FirstOrDefault(s => s.output == pendingOutput && s.node == pendingNode && s.port == pendingPort);
            if (pending != null)
            {
                painter.strokeColor = SocketColor(pending.type);
                var socketPosition = layer.WorldToLocal(pending.hit.worldBound.center);
                DrawWire(painter, pendingOutput ? socketPosition : wirePosition, pendingOutput ? wirePosition : socketPosition);
            }
        }

        static void DrawWire(Painter2D painter, Vector2 a, Vector2 b)
        {
            if (float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsNaN(b.x) || float.IsNaN(b.y)) return;
            var bend = Mathf.Max(45, Mathf.Abs(b.x - a.x) * .45f);
            painter.BeginPath(); painter.MoveTo(a);
            painter.BezierCurveTo(a + Vector2.right * bend, b - Vector2.right * bend, b); painter.Stroke();
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
