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
    public sealed partial class GraphWindow : EditorWindow
    {
        [SerializeField] GraphSession session;
        [SerializeField] string sourcePath;
        [SerializeField] string diskSource;
        [SerializeField] Material contextMaterial;
        [SerializeField] string selected;
        [SerializeField] List<string> selection = new List<string>();
        [SerializeField] string nodeSearch = "";
        [SerializeField] List<string> expandedNodeCategories = new List<string> { "Coordinates" };
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
        Dictionary<string, string> inferredTypes = new Dictionary<string, string>();
        readonly Dictionary<string, Gradient> wireGradients = new Dictionary<string, Gradient>();
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
        [SerializeField] bool livePreview = true;
        [SerializeField] string previewNodeId, previewNodePort;
        [SerializeField] bool audioPreviewEnabled;
        [SerializeField] float audioPreviewValue = .5f;
        GraphPreview livePreviewResources;
        VisualElement previewHost;
        bool previewPending;
        double previewDue, lastPreviewRepaint;
        string previewHash, previewMessage;
        string selectedPreviewPort;
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
            EditorApplication.update += UpdateLivePreview;
            EditorApplication.update += UpdateScene;
            EditorApplication.update += TickRecovery;
            EditorApplication.update += TickInlinePreviews;
            Restore();
        }

        void OnDisable()
        {
            CancelWire();
            Undo.undoRedoPerformed -= Restore;
            EditorApplication.update -= UpdateLivePreview;
            EditorApplication.update -= UpdateScene;
            EditorApplication.update -= TickRecovery;
            EditorApplication.update -= TickInlinePreviews;
            recoveryDue=0; TickRecovery(); DisposeInlinePreviews();
            ClearPreview();
        }

        void OnFocus() { canvas?.Focus(); previewHash = null; QueueLivePreview(); }

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
            toolbar.Add(new ToolbarButton(ShowRecoveryMenu) { text = "Recovery", tooltip = "Create checkpoints or recover local graph snapshots." });
            toolbar.Add(new ToolbarButton(Undo.PerformUndo) { text = "Undo" });
            toolbar.Add(new ToolbarButton(Undo.PerformRedo) { text = "Redo" });
            toolbar.Add(new ToolbarButton(CopySelection) { text = "Copy", tooltip = "Copy selected nodes (Ctrl+C)" });
            toolbar.Add(new ToolbarButton(PasteSelection) { text = "Paste", tooltip = "Paste nodes (Ctrl+V)" });
            toolbar.Add(new ToolbarButton(DuplicateSelection) { text = "Duplicate", tooltip = "Duplicate selected nodes (Ctrl+D)" });
            toolbar.Add(new ToolbarButton(Build) { text = "Build for VRChat" });
            var liveToggle = new ToolbarToggle { text = "Live preview", value = livePreview,
                tooltip = "Preview unsaved edits without changing your material. Pauses while this window is unfocused." };
            liveToggle.RegisterValueChangedCallback(evt =>
            {
                livePreview = evt.newValue;
                if (livePreview) { previewHash = null; QueueLivePreview(); }
                else { previewPending = false; previewMessage = "Preview paused"; RefreshPreviewPanel(); }
            });
            toolbar.Add(liveToggle);
            AddSceneToggle(toolbar);
            toolbar.Add(new ToolbarButton(() => { pan = new Vector2(30, 70); zoom = 1; TransformCanvas(); }) { text = "Reset view" });
            toolbar.Add(new ToolbarButton(() => FrameNodes(false)) { text = "Fit graph", tooltip = "Frame the whole graph (Home)." });
            toolbar.Add(new ToolbarButton(() => FrameNodes(true)) { text = "Frame selected", tooltip = "Focus the selected nodes (F)." });
            toolbar.Add(new ToolbarButton(FocusNodeSearch) { text = "Add node", tooltip = "Search nodes (Space on canvas)." });
            AddPatternToolbar(toolbar);
            rootVisualElement.Add(toolbar);
            identity = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 10, paddingTop = 5, paddingBottom = 5 } };
            rootVisualElement.Add(identity);
            sceneStatus = new Label { style = { whiteSpace = WhiteSpace.Normal, paddingLeft = 10, paddingBottom = 5 } };
            rootVisualElement.Add(sceneStatus);
            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            canvas = new VisualElement { focusable = true, style = { flexGrow = 1, overflow = Overflow.Hidden } };
            layer = new VisualElement { style = { position = UnityEngine.UIElements.Position.Absolute, width = 4000, height = 4000 } };
            layer.style.transformOrigin = new TransformOrigin(0, 0, 0);
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
                zoom = Mathf.Clamp(zoom * Mathf.Exp(-evt.delta.y * .045f), .1f, 1.6f);
                pan = position - (position - pan) * (zoom / previous);
                TransformCanvas();
                evt.StopPropagation();
            });
            canvas.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (IsEditingText(evt.target as VisualElement)) return;
                if (evt.keyCode == KeyCode.F) { FrameNodes(true); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.Home) { FrameNodes(false); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.Space) { FocusNodeSearch(); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.Escape) { CancelBox(true); CancelWire(); SetStatus("Cancelled."); evt.StopPropagation(); }
                if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) DeleteSelection();
                if (evt.actionKey && evt.keyCode == KeyCode.S) { SaveGraph(); evt.StopPropagation(); }
            });
            body.Add(canvas);
            inspector = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, style = { width = 270, paddingLeft = 12, paddingRight = 12, paddingTop = 12, backgroundColor = new Color(.10f, .10f, .10f) } };
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
            if (MatchesSource(material)) { contextMaterial = material; ClearPreview(); QueueLivePreview(); RebuildInspector(); }
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
            ClearPreview();
            graph = GraphSamples.CreateDefault();
            graph.GraphId = Guid.NewGuid().ToString("N");
            sourcePath = null; diskSource = null; contextMaterial = null; selected = null; selection.Clear(); previewNodeId = previewNodePort = null;
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
                previewNodeId = previewNodePort = null;
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
            SetStatus(livePreview ? "Unsaved edits · live preview updates after a short pause." : "Unsaved edits · preview shows the last successful build.");
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
                AssetDatabase.Refresh(); UpdateIdentity(); sceneQueuedHash = null; QueueSceneUpdate(); SetStatus("Saved " + Path.GetFileName(sourcePath));
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
                SceneBuildSucceeded();
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
            catch (Exception exception) { SceneBuildFailed(exception); SetStatus(exception.Message); }
        }

        void ClearPreview()
        {
            if (previewEditor != null) DestroyImmediate(previewEditor);
            if (livePreviewResources != null) livePreviewResources.Dispose();
            else if (preview != null) DestroyImmediate(preview);
            livePreviewResources = null;
            previewEditor = null; preview = null; previewHash = null; previewMessage = null;
        }

        void QueueLivePreview()
        {
            QueueSceneUpdate();
            QueueInlinePreviews();
            if (!livePreview || graph == null) return;
            previewPending = true;
            previewDue = EditorApplication.timeSinceStartup + .45;
        }

        void UpdateLivePreview()
        {
            if (!livePreview || graph == null || focusedWindow != this || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var now = EditorApplication.timeSinceStartup;
            if (previewPending && now >= previewDue)
            {
                previewPending = false;
                GraphPreview candidate = null;
                UnityEditor.Editor candidateEditor = null;
                try
                {
                    var previewGraph = PreparePreviewGraph();
                    var hash = GraphJson.ComputeSemanticHash(graph) + ":" + previewNodeId + ":" + previewNodePort;
                    if (hash != previewHash)
                    {
                        previewHash = hash;
                        candidate = GraphPreview.Create(previewGraph, string.IsNullOrEmpty(previewNodeId) ? contextMaterial : null);
                        candidateEditor = UnityEditor.Editor.CreateEditor(candidate.Material);
                        ClearPreview();
                        livePreviewResources = candidate; preview = candidate.Material; previewEditor = candidateEditor;
                        ApplyAudioLinkPreview();
                        candidate = null; candidateEditor = null; previewHash = hash;
                        previewMessage = string.IsNullOrEmpty(previewNodeId) ? "Live preview · " + (contextMaterial != null ? contextMaterial.name : "neutral material tint") : "Live node preview · " + previewNodePort;
                        RefreshPreviewPanel();
                    }
                }
                catch (Exception exception)
                {
                    if (candidateEditor != null) DestroyImmediate(candidateEditor);
                    candidate?.Dispose();
                    previewMessage = "Preview needs attention: " + exception.Message + (preview != null ? "\nShowing the last successful preview." : "");
                    RefreshPreviewPanel();
                }
            }
            if (previewEditor != null && now - lastPreviewRepaint > .05)
            {
                lastPreviewRepaint = now;
                previewHost?.MarkDirtyRepaint(); Repaint();
            }
        }

        void RefreshPreviewPanel()
        {
            if (previewHost == null) return;
            previewHost.Clear();
            if (!string.IsNullOrEmpty(previewNodeId)) previewHost.Add(new Button(() => { previewNodeId = previewNodePort = null; previewHash = null; QueueLivePreview(); RefreshPreviewPanel(); }) { text = "Back to material preview" });
            previewHost.Add(new Label(previewMessage ?? (previewEditor != null
                ? (contextMaterial != null ? "Last build · " + contextMaterial.name : "Last build · neutral material tint")
                : livePreview ? "Live preview waiting for a complete graph…" : "Enable Live preview to see unsaved edits."))
                { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } });
            if (previewEditor != null)
                previewHost.Add(new IMGUIContainer(() =>
                {
                    if (previewEditor != null) previewEditor.OnPreviewGUI(GUILayoutUtility.GetRect(240, 220), EditorStyles.helpBox);
                }) { style = { height = 220 } });
        }

        void Rebuild()
        {
            if (layer == null) return;
            CancelWire();
            layer.Clear(); nodes.Clear(); sockets.Clear();
            inferredTypes = GraphTypes.Infer(graph);
            if (graph != null)
                foreach (var node in graph.Nodes)
                {
                    var position = Position(node.Id);
                    var box = new VisualElement { focusable = true, style = { position = UnityEngine.UIElements.Position.Absolute, left = position.x, top = position.y, width = 175, backgroundColor = new Color(.14f, .14f, .14f), borderLeftWidth = 2, borderRightWidth = 2, borderTopWidth = 2, borderBottomWidth = 2, borderTopLeftRadius = 8, borderTopRightRadius = 8, borderBottomLeftRadius = 8, borderBottomRightRadius = 8, paddingBottom = 8 } };
                    var title = new Label(Title(node.Operation)) { style = { paddingLeft = 12, paddingTop = 10, paddingBottom = 10, unityFontStyleAndWeight = FontStyle.Bold, backgroundColor = NodeColor(node.Operation) } };
                    var alternatives = OperationAlternatives(node.Operation);
                    if (alternatives.Length > 0)
                    {
                        title.text += " ▾";
                        title.focusable = true;
                        title.tooltip = "Click to change operation. Drag to move this node. Compatible wires are kept; unavailable inputs are disconnected. Undo restores them.";
                        title.RegisterCallback<KeyDownEvent>(evt =>
                        {
                            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
                            ShowOperationMenu(node.Id); evt.StopPropagation();
                        });
                    }
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
                        UpdateInsertionTarget(node.Id); layer.MarkDirtyRepaint(); evt.StopPropagation();
                    });
                    title.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (!title.HasPointerCapture(evt.pointerId)) return;
                        dragging = false; title.ReleasePointer(evt.pointerId);
                        if (alternatives.Length > 0 && ((Vector2)evt.position - pointerStart).sqrMagnitude < 16)
                        {
                            foreach (var pair in dragOrigins)
                            {
                                SetPosition(pair.Key, pair.Value);
                                nodes[pair.Key].style.left = pair.Value.x; nodes[pair.Key].style.top = pair.Value.y;
                            }
                            layer.MarkDirtyRepaint();
                            ShowOperationMenu(node.Id); evt.StopPropagation(); return;
                        }
                        Undo.IncrementCurrentGroup();
                        if (InsertOnHighlightedWire(node.Id)) { evt.StopPropagation(); return; }
                        Undo.RegisterCompleteObjectUndo(session, "Move node");
                        session.json = GraphJson.Serialize(graph, true); hasUnsavedChanges = true; EditorUtility.SetDirty(session);
                        canvas.Focus(); evt.StopPropagation();
                    });
                    foreach (var port in Ports(node.Operation, false)) AddSocket(box, node, port, false);
                    foreach (var port in Ports(node.Operation, true)) AddSocket(box, node, port, true);
                    AddInlineControls(node, box);
                    box.RegisterCallback<FocusInEvent>(_ => { if (!selection.Contains(node.Id)) SelectNode(node.Id); });
                    box.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) SelectNode(node.Id, evt.shiftKey || selection.Contains(node.Id)); });
                    layer.Add(box); nodes[node.Id] = box;
                }
            DrawPatternGroups();
            selection.RemoveAll(id => !nodes.ContainsKey(id));
            UpdateSelectionOutline();
            TransformCanvas(); RebuildInspector(); UpdateIdentity(); QueueLivePreview();
        }

        static string[] OperationAlternatives(string operation)
        {
            switch (operation)
            {
                case "core.power": case "core.add": case "core.multiply": case "core.mix": case "core.subtract": case "core.divide": case "core.minimum": case "core.maximum":
                    return new[] { "core.add", "core.subtract", "core.multiply", "core.divide", "core.minimum", "core.maximum", "core.power", "core.mix" };
                case "core.absolute": case "core.sqrt": case "core.sine": case "core.cosine":
                case "core.fraction": case "core.floor": case "core.ceil": case "core.round":
                    return new[] { "core.absolute", "core.sqrt", "core.sine", "core.cosine", "core.fraction", "core.floor", "core.ceil", "core.round" };
                case "core.oneMinus": case "core.clamp":
                    return new[] { "core.oneMinus", "core.clamp" };
                case "core.uv0": case "core.objectUV": case "core.worldUV": case "core.uvTransform":
                case "core.uvScroll": case "core.uvRotate": case "core.polarUV":
                    return new[] { "core.uv0", "core.objectUV", "core.worldUV", "core.uvTransform", "core.uvScroll", "core.uvRotate", "core.polarUV" };
                case "core.toonSurface": case "core.unlitSurface": case "core.pbrSurface": case "core.particleSurface":
                    return new[] { "core.toonSurface", "core.unlitSurface", "core.pbrSurface", "core.particleSurface" };
                default: return Array.Empty<string>();
            }
        }

        void ShowOperationMenu(string nodeId)
        {
            var node = graph?.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null) return;
            var menu = new GenericMenu();
            foreach (var operation in OperationAlternatives(node.Operation))
            {
                var op = operation;
                menu.AddItem(new GUIContent(Title(op), NodeCatalog.Description(op)), op == node.Operation,
                    () => SwitchOperation(nodeId, op));
            }
            menu.ShowAsContext();
        }

        void SwitchOperation(string nodeId, string operation)
        {
            var node = graph?.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null || node.Operation == operation || !OperationAlternatives(node.Operation).Contains(operation)) return;
            Undo.IncrementCurrentGroup();
            var removed = 0;
            Edit("Change node operation", () =>
            {
                node.Operation = operation;
                // Preserve dormant controls so switching back restores the user's settings.
                foreach (var property in NodeCatalog.Create(operation).Properties.Properties())
                    if (node.Properties[property.Name] == null) node.Properties[property.Name] = property.Value.DeepClone();
                inferredTypes = GraphTypes.Infer(graph);
                removed = graph.Connections.RemoveAll(edge =>
                {
                    if (edge.From.NodeId != nodeId && edge.To.NodeId != nodeId) return false;
                    var from = graph.Nodes.FirstOrDefault(n => n.Id == edge.From.NodeId);
                    var to = graph.Nodes.FirstOrDefault(n => n.Id == edge.To.NodeId);
                    return from == null || to == null || !Ports(from.Operation, true).Contains(edge.From.PortId)
                        || !Ports(to.Operation, false).Contains(edge.To.PortId)
                        || !GraphTypes.Compatible(PortType(from, edge.From.PortId), PortType(to, edge.To.PortId));
                });
            });
            if (removed > 0) SetStatus("Changed to " + Title(operation) + " · disconnected " + removed + " incompatible wire(s). Undo restores them.");
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

        static string PortLabel(string port)
        {
            switch(port)
            {
                case "uv": return "UV"; case "rootColor": return "Root color"; case "tipColor": return "Tip color";
                case "inMin": return "Input min"; case "inMax": return "Input max";
                case "outMin": return "Output min"; case "outMax": return "Output max";
                case "groom": return "Groom direction"; default: return port;
            }
        }

        void FocusNodeSearch()
        {
            var search=inspector?.Q<ToolbarSearchField>("node-search");
            if(search==null)return;
            (inspector as ScrollView)?.ScrollTo(search);
            search.Focus();
        }

        void FrameNodes(bool selectedOnly)
        {
            if(graph==null||canvas==null)return;
            var ids=selectedOnly && selection.Count>0?selection:graph.Nodes.Select(n=>n.Id).ToList();
            var visible=ids.Where(id=>nodes.ContainsKey(id)&&graph.Layout.Nodes.ContainsKey(id)).ToList();
            if(visible.Count==0){SetStatus("Add a node to frame the graph.");return;}
            var min=new Vector2(float.MaxValue,float.MaxValue);var max=new Vector2(float.MinValue,float.MinValue);
            foreach(var id in visible)
            {
                var layout=graph.Layout.Nodes[id];var position=new Vector2((float)layout.X,(float)layout.Y);
                var height=nodes[id].resolvedStyle.height;if(float.IsNaN(height)||height<40)height=120;
                min=Vector2.Min(min,position);max=Vector2.Max(max,position+new Vector2(175,height));
            }
            var viewport=new Vector2(canvas.resolvedStyle.width,canvas.resolvedStyle.height);
            if(viewport.x<1||viewport.y<1)return;
            var size=max-min+Vector2.one*80;
            zoom=Mathf.Clamp(Mathf.Min(viewport.x/size.x,viewport.y/size.y),.1f,1.6f);
            pan=viewport*.5f-(min+max)*.5f*zoom;
            TransformCanvas();
        }

        void AddSocket(VisualElement box, GraphNode node, string port, bool output)
        {
            var type = PortType(node, port);
            var row = new VisualElement { style = { height = 28, justifyContent = Justify.Center } };
            row.Add(new Label(PortLabel(port)) { pickingMode = PickingMode.Ignore, style = {
                marginLeft = 16, marginRight = 16,
                unityTextAlign = output ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft } });
            var hit = new VisualElement { tooltip = (output ? "Output" : "Input") + ": " + port + " (" + type + ") — drag or click to connect. Drag a connected input to detach its wire.",
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
                    {
                        var otherNode = graph.Nodes.First(n => n.Id == other.node);
                        var compatible = output ? CanOffer(node, port, otherNode, other.port) : CanOffer(otherNode, other.port, node, port);
                        other.dot.style.opacity = compatible && other.node != node.Id ? 1f : .3f;
                    }
                    layer.MarkDirtyRepaint();
                    SetStatus("Drag to a matching socket, or empty space to add a node. Esc cancels.");
                }
                evt.StopPropagation(); evt.PreventDefault();
            });
            hit.RegisterCallback<GeometryChangedEvent>(_ => layer.MarkDirtyRepaint());
        }

        void DetachInputWire()
        {
            if (pendingOutput) return;
            var edge = graph.Connections.FirstOrDefault(e => e.To.NodeId == pendingNode && e.To.PortId == pendingPort);
            if (edge == null) return;
            var pointer = wirePointer;
            var start = wireStart;
            var position = wirePosition;
            Edit("Detach input wire", () => graph.Connections.Remove(edge));
            // Rebuild clears the old gesture. Continue with the original output's loose wire.
            pendingNode = edge.From.NodeId; pendingPort = edge.From.PortId; pendingOutput = true;
            wiring = true; wireMoved = true; wirePointer = pointer; wireStart = start; wirePosition = position;
            canvas.CapturePointer(pointer);
            layer.MarkDirtyRepaint();
            SetStatus("Wire detached. Drop onto an input to reconnect, or empty space to add a node. Undo restores it.");
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
                case "core.polarUV": case "core.uvRotate": case "core.objectUV": case "core.worldUV":
                case "core.uvTransform": case "core.uvScroll": case "core.uv0": return new Color(.16f, .32f, .52f);
                case "core.noise": case "core.musgrave": case "core.voronoi": case "core.checker": case "core.wave": case "core.texture2D": return new Color(.46f, .25f, .10f);
                case "core.particleColor": case "core.constant": return new Color(.40f, .34f, .10f);
                case "core.subtract": case "core.divide": case "core.minimum": case "core.maximum":
                case "core.ramp": case "core.value": case "core.time": case "core.add": case "core.mix": case "core.oneMinus": case "core.clamp": case "core.multiply": return new Color(.28f, .33f, .38f);
                case "core.emission": case "core.toonSurface": return new Color(.13f, .37f, .24f);
                case "core.surfaceParticles": case "core.particleSurface": case "core.unlitSurface": case "core.pbrSurface": case "core.shell": return new Color(.13f,.37f,.24f);
                case "core.gradient": case "core.posterize": case "core.fresnel": case "core.colorRamp": case "core.layer": case "core.dissolve": return new Color(.40f,.34f,.10f);
                case "core.sticker": return new Color(.46f,.25f,.10f);
                case "core.uvTile": case "core.flipbook": case "core.uvDistort": return new Color(.16f,.32f,.52f);
                case "core.vertexMotion": case "core.normalMap": return new Color(.12f,.38f,.40f);
                case "core.audioLink": return new Color(.44f,.22f,.29f);
                case "core.output": return new Color(.39f, .19f, .20f);
                default:
                    switch (NodeCatalog.Category(operation))
                    {
                        case "Math": return new Color(.28f,.33f,.38f);
                        case "Color": return new Color(.40f,.34f,.10f);
                        case "Coordinates": return new Color(.16f,.32f,.52f);
                        case "Textures": return new Color(.46f,.25f,.10f);
                        case "Inputs": return new Color(.12f,.38f,.40f);
                        default: return new Color(.28f,.28f,.28f);
                    }
            }
        }

        static Color SocketColor(string type)
        {
            return type == "surface" ? new Color(.35f, .85f, .46f)
                : type == "float" ? new Color(.80f, .82f, .85f)
                : type == "vector2" ? new Color(.45f, .65f, 1f) : type == "vector3" ? new Color(.25f,.85f,.85f) : new Color(1f, .78f, .25f);
        }

        void RebuildInspector()
        {
            if (inspector == null) return;
            inspector.Clear();
            previewHost = new VisualElement { style = { marginBottom = 10 } };
            inspector.Add(previewHost); RefreshPreviewPanel();
            var library = new VisualElement { name = "node-library", style = { marginTop = 14 } };
            library.Add(new Label("ADD NODES") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginBottom = 8 } });
            var search = new ToolbarSearchField { name = "node-search", tooltip = "Search nodes, descriptions, or categories" };
            search.SetValueWithoutNotify(nodeSearch); library.Add(search);
            var choices = new VisualElement(); library.Add(choices);
            Action<string> filter = query =>
            {
                choices.Clear();
                var searching = !string.IsNullOrWhiteSpace(query);
                var matches = NodeCatalog.All.Where(op => op != "core.parameter" && op != "core.previewVector" &&
                    MatchesNodeSearch(op, query)).ToList();
                foreach (var category in new[] { "Inputs", "Coordinates", "Textures", "Math", "Color", "Animation", "Surface" })
                {
                    var operations = matches.Where(op => NodeCatalog.Category(op) == category).ToList();
                    if (operations.Count == 0) continue;
                    var group = new Foldout { name = "category-" + category, text = category + "  ·  " + operations.Count,
                        value = searching || expandedNodeCategories.Contains(category), style = {
                            marginTop = 6, paddingTop = 4, paddingBottom = 4, paddingRight = 4,
                            borderLeftWidth = 3, borderLeftColor = NodeColor(operations[0]),
                            backgroundColor = new Color(.14f, .14f, .14f), borderTopRightRadius = 4, borderBottomRightRadius = 4 } };
                    group.RegisterValueChangedCallback(evt =>
                    {
                        if (!string.IsNullOrWhiteSpace(nodeSearch)) return;
                        expandedNodeCategories.Remove(category);
                        if (evt.newValue) expandedNodeCategories.Add(category);
                    });
                    foreach (var operation in operations)
                    {
                        var op = operation;
                        group.Add(new Button(() => AddNode(op)) { text = "+  " + Title(op), tooltip = NodeCatalog.Description(op),
                            style = { height = 25, unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 8, marginTop = 2, marginBottom = 2 } });
                    }
                    choices.Add(group);
                }
                if (matches.Count == 0) choices.Add(new Label("No matching nodes. Try ‘UV’, ‘blend’, or ‘color’.")
                    { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } });
            };
            search.RegisterValueChangedCallback(evt => { nodeSearch = evt.newValue; filter(nodeSearch); }); filter(nodeSearch);
            if (selection.Count > 1)
            {
                inspector.Add(new Label(selection.Count + " nodes selected") { style = { marginTop = 15 } });
                inspector.Add(new Button(DeleteSelection) { text = "Delete selected nodes" });
            }
            var node = selection.Count > 1 ? null : graph?.Nodes.FirstOrDefault(n => n.Id == selected);
            if (node != null)
            {
                inspector.Add(new Label(Title(node.Operation)) { style = { marginTop = 15, unityFontStyleAndWeight = FontStyle.Bold } });
                if (GraphTypes.IsDynamic(node.Operation)) inspector.Add(new Label("Automatic type: " + (inferredTypes.TryGetValue(node.Id, out var inferred) && inferred == "float" ? "Number" : "Color"))
                    { style = { color = new Color(.7f, .8f, .9f), marginBottom = 4 } });
                inspector.Add(new Label(NodeCatalog.Description(node.Operation)) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 6 } });
                AddNodePreviewControls(node);
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
                AddVisualControls(node);
                AddFeatureControls(node);
                switch (node.Operation)
                {
                    case "core.absolute": case "core.sqrt": case "core.sine": case "core.cosine":
                    case "core.fraction": case "core.floor": case "core.ceil": case "core.round":
                        AddNumber(node,"a","Value",.5f,"a"); break;
                    case "core.power": AddNumber(node,"a","Base",.5f,"a"); AddNumber(node,"b","Exponent",2,"b"); break;
                    case "core.step": AddNumber(node,"a","Threshold",.5f,"a"); AddNumber(node,"b","Value",0,"b"); break;
                    case "core.smoothstep": AddNumber(node,"value","Value",0,"value"); AddNumber(node,"low","Low edge",0,"low"); AddNumber(node,"high","High edge",1,"high"); break;
                    case "core.remap": AddNumber(node,"value","Value",0,"value"); AddNumber(node,"inMin","Input minimum",0,"inMin"); AddNumber(node,"inMax","Input maximum",1,"inMax"); AddNumber(node,"outMin","Output minimum",0,"outMin"); AddNumber(node,"outMax","Output maximum",1,"outMax"); break;
                    case "core.pingPong": AddNumber(node,"value","Value / time",0,"value"); AddNumber(node,"length","Peak",1,"length"); break;
                    case "core.combineColor": AddNumber(node,"r","Red",0,"r"); AddNumber(node,"g","Green",0,"g"); AddNumber(node,"b","Blue",0,"b"); AddNumber(node,"a","Alpha",1,"a"); break;
                    case "core.contrast": AddNumber(node,"amount","Contrast",1,"amount"); AddNumber(node,"pivot","Pivot",.5f,"pivot"); break;
                    case "core.saturation": AddNumber(node,"amount","Saturation",1,"amount"); break;
                    case "core.combineUV": AddNumber(node,"u","U / horizontal",0,"u"); AddNumber(node,"v","V / vertical",0,"v"); break;
                    case "core.ramp": AddNumber(node, "blackPoint", "Black point", 0); AddNumber(node, "whitePoint", "White point", 1); AddNumber(node, "smoothness", "Smoothing (0–1)", 0); AddRampCurve(node); break;
                    case "core.colorRamp": AddColorRamp(node); break;
                    case "core.value": AddNumber(node, "value", "Value", 0); break;
                    case "core.time": AddNumber(node, "speed", "Speed", 1); AddNumber(node, "offset", "Offset", 0); break;
                    case "core.uvTransform": AddVector(node, "tiling", "Tiling", Vector2.one); AddVector(node, "offset", "Offset", Vector2.zero); break;
                    case "core.uv0": case "core.texture2D": AddCoordinateChoice(node); break;
                    case "core.polarUV": AddCoordinateChoice(node); AddVector(node, "center", "Center", new Vector2(.5f, .5f)); AddNumber(node, "radialScale", "Radial scale", 1); AddNumber(node, "angleScale", "Angular repeats", 1); break;
                    case "core.uvRotate": AddVector(node, "center", "Center", new Vector2(.5f, .5f)); AddNumber(node, "angle", "Angle (degrees)", 0, "angle"); break;
                    case "core.uvScroll": AddVector(node, "speed", "Scroll speed", new Vector2(.1f, 0)); break;
                    case "core.noise": case "core.musgrave": case "core.voronoi": case "core.checker": case "core.wave": AddProceduralControls(node); break;
                    case "core.mix": AddFactor(node); break;
                    case "core.emission": AddNumber(node, "strength", "Strength", 1, "strength"); break;
                    case "core.fresnel": AddNumber(node, "power", "Power", 5, "power"); break;
                    case "core.layer": AddNumber(node, "mask", "Mask", 1, "mask"); break;
                    case "core.pbrSurface": AddNumber(node, "opacity", "Opacity", 1, "opacity"); AddNumber(node, "cutoff", "Cutoff", .001f); AddNumber(node, "displacement", "Displacement", 0, "displacement"); AddNumber(node, "metallic", "Metallic", 0, "metallic"); AddNumber(node, "roughness", "Roughness", .5f, "roughness"); break;
                    case "core.toonSurface": AddNumber(node, "opacity", "Opacity", 1, "opacity"); AddNumber(node, "cutoff", "Cutoff", .001f); AddNumber(node, "displacement", "Displacement", 0, "displacement"); break;
                    case "core.surfaceParticles":
                        var sourceUvToggle = new Toggle("Color from mesh UVs") { value = (int?)node.Properties["sourceUV"] == 1,
                            tooltip = "On: particle Albedo and Emission sample the connected texture at the spawn point on mesh UV0. Off: each particle displays the texture using its own sprite UVs. Opacity keeps sprite UVs." };
                        sourceUvToggle.RegisterValueChangedCallback(evt => Edit("Change particle color UVs", () => node.Properties["sourceUV"] = evt.newValue ? 1 : 0));
                        inspector.Add(sourceUvToggle);
                        AddIndexedChoice(node,"blendMode","Blending",new[]{"Alpha","Additive"},1);
                        AddBoundedNumber(node,"density","Triangle density",0,1,.1f);
                        AddBoundedNumber(node,"emissionRate","Rate / triangle / sec",0,4,1 / Mathf.Max(.001f, (float?)node.Properties["lifetime"] ?? 2));
                        AddBoundedNumber(node,"size","Particle size",.0001f,1,.03f);
                        AddBoundedNumber(node,"lifetime","Lifetime (seconds)",.05f,30,2);
                        AddNumber(node,"speed","Outward speed",.2f);
                        AddNumber(node,"gravity","Gravity (local Y)",0);
                        AddBoundedNumber(node,"spread","Velocity randomness",0,5,.05f);
                        AddBoundedNumber(node,"opacity","Opacity",0,1,1,"opacity");
                        AddBoundedNumber(node,"mask","Emitter mask",0,1,1,"mask");
                        inspector.Add(new Label("Connect your surface to Base, then this node to Output. Emits from the same mesh: Density selects source triangles (1 = all). Rate requests births per source triangle per second. High rates automatically tessellate the mesh in this pass; the resulting rate is approximate. Tessellation stops at level 64. Mask uses mesh UVs. Color from mesh UVs samples particle color at its spawn point; otherwise it uses sprite UVs. Particles follow the current pose. Expand renderer bounds if particles disappear near screen edges.") { style = { whiteSpace = WhiteSpace.Normal } });
                        break;
                    case "core.particleSurface":
                        AddIndexedChoice(node, "blendMode", "Blending", new[] { "Alpha · smoke / fluff", "Additive · sparks / glow" });
                        AddBoundedNumber(node, "opacity", "Opacity", 0, 1, 1, "opacity");
                        AddBoundedNumber(node, "softDistance", "Soft intersection distance", 0, 5, 0);
                        inspector.Add(new Label("Particle color and lifetime alpha apply automatically. Use Renderer streams Position, Normal, Color, UV. Soft distance 0 disables depth fading; positive values need camera depth.") { style = { whiteSpace = WhiteSpace.Normal } });
                        break;
                    case "core.unlitSurface": AddNumber(node, "opacity", "Opacity", 1, "opacity"); AddNumber(node, "cutoff", "Cutoff", .001f); AddNumber(node, "displacement", "Displacement", 0, "displacement"); break;
                    case "core.sticker": AddTexturePicker(node, "Sticker texture"); AddVector(node, "position", "Position", Vector2.zero); AddVector(node, "size", "Size", Vector2.one); AddNumber(node, "rotation", "Rotation", 0); AddNumber(node, "mask", "Mask", 1, "mask"); break;
                    case "core.dissolve": AddNumber(node, "threshold", "Threshold", .5f, "threshold"); AddNumber(node, "edgeWidth", "Edge width", .05f); break;
                    case "core.flipbook": AddNumber(node, "columns", "Columns", 1); AddNumber(node, "rows", "Rows", 1); AddNumber(node, "speed", "Speed", 1); break;
                    case "core.uvDistort": AddDistortionControls(node); break;
                    case "core.gradient": AddCoordinateChoice(node); AddIndexedChoice(node,"mode","Shape",new[]{"Linear","Radial","Angular"}); AddVector(node,"center","Center",new Vector2(.5f,.5f)); AddNumber(node,"angle","Angle (degrees)",0); AddNumber(node,"radius","Radius",.5f); break;
                    case "core.uvTile": AddCoordinateChoice(node); AddIndexedChoice(node,"mode","Wrapping",new[]{"Repeat","Mirror","Clamp"}); AddVector(node,"tiling","Tiling",Vector2.one); AddVector(node,"offset","Offset",Vector2.zero); break;
                    case "core.posterize": AddBoundedNumber(node,"levels","Steps",2,256,4,"levels"); break;
                    case "core.vertexMotion": AddNumber(node, "strength", "Strength", .02f, "strength"); AddNumber(node, "speed", "Speed", 1); AddNumber(node, "frequency", "Frequency", 2); break;
                    case "core.shell": AddNumber(node, "offset", "Shell offset", .02f, "offset"); break;
                    case "core.normalMap": AddNumber(node, "strength", "Strength", 1); break;
                    case "core.audioLink": AddAudioLinkControls(node); break;
                }
                inspector.Add(new Button(() => Edit("Disconnect node", () => graph.Connections.RemoveAll(e => e.From.NodeId == selected || e.To.NodeId == selected))) { text = "Disconnect node" });
                inspector.Add(new Button(DeleteSelection) { text = "Delete node" });
            }
            inspector.Add(library);
        }

        void AddNodePreviewControls(GraphNode node)
        {
            var ports = Ports(node.Operation, true).Where(port =>
            {
                var type = PortType(node, port);
                return type == "float" || type == "color" || type == "surface" || type == "vector2" || type == "vector3";
            }).ToList();
            if (ports.Count == 0) return;
            if (string.IsNullOrEmpty(selectedPreviewPort) || !ports.Contains(selectedPreviewPort)) selectedPreviewPort = ports[0];
            var field = new PopupField<string>("Preview output", ports, Math.Max(0, ports.IndexOf(selectedPreviewPort)));
            field.tooltip = "Preview this node output without changing graph connections.";
            field.RegisterValueChangedCallback(evt => selectedPreviewPort = evt.newValue);
            inspector.Add(field);
            inspector.Add(new Button(() => PreviewNode(node, selectedPreviewPort)) { text = "Preview selected node", tooltip = "Build a temporary preview graph from this output. Original graph stays unchanged." });
        }

        ShaderGraph PreparePreviewGraph()
        {
            if (string.IsNullOrEmpty(previewNodeId) || !graph.Nodes.Any(n => n.Id == previewNodeId)) { previewNodeId = previewNodePort = null; return graph; }
            var clone = GraphJson.Parse(GraphJson.Serialize(graph));
            var node = clone.Nodes.First(n => n.Id == previewNodeId);
            var type = GraphTypes.PortType(clone, node, previewNodePort, GraphTypes.Infer(clone));
            if (type == null) { previewNodeId = previewNodePort = null; return graph; }
            clone.Nodes.RemoveAll(n => n.Operation == "core.output");
            var ids = new HashSet<string>(clone.Nodes.Select(n => n.Id));
            clone.Connections.RemoveAll(e => !ids.Contains(e.From.NodeId) || !ids.Contains(e.To.NodeId));
            Action<string,string,string,string> wire = (from,fromPort,to,toPort) => clone.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"),
                From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
            var port = previewNodePort;
            if (type == "vector2" || type == "vector3")
            {
                var converter = NodeCatalog.Create("core.previewVector"); clone.Nodes.Add(converter);
                wire(node.Id,port,converter.Id,type == "vector2" ? "uv" : "normal"); node = converter; port = "color"; type = "color";
            }
            if (type != "surface")
            {
                var surface = NodeCatalog.Create("core.unlitSurface"); clone.Nodes.Add(surface);
                wire(node.Id,port,surface.Id,"albedo"); node = surface; port = "surface";
            }
            var output = NodeCatalog.Create("core.output"); clone.Nodes.Add(output); wire(node.Id,port,output.Id,"surface");
            return clone;
        }

        void PreviewNode(GraphNode node, string port)
        {
            if (graph == null || node == null || string.IsNullOrEmpty(port)) return;
            GraphPreview candidate = null; UnityEditor.Editor candidateEditor = null;
            var oldNode = previewNodeId; var oldPort = previewNodePort;
            try
            {
                previewNodeId = node.Id; previewNodePort = port;
                candidate = GraphPreview.Create(PreparePreviewGraph(), null);
                candidateEditor = UnityEditor.Editor.CreateEditor(candidate.Material);
                ClearPreview();
                livePreviewResources = candidate; preview = candidate.Material; previewEditor = candidateEditor;
                candidate = null; candidateEditor = null; ApplyAudioLinkPreview(); previewPending = false;
                previewMessage = "Live node preview · " + Title(node.Operation) + " · " + port;
                RefreshPreviewPanel();
            }
            catch (Exception exception)
            {
                if (candidateEditor != null) DestroyImmediate(candidateEditor); candidate?.Dispose();
                previewNodeId = oldNode; previewNodePort = oldPort;
                previewMessage = "Selected node preview failed: " + exception.Message; RefreshPreviewPanel();
            }
        }

        void AddColorRamp(GraphNode node)
        {
            var gradient = new Gradient();
            var points = node.Properties["stops"] as JArray;
            var validPoints = points == null ? new List<JArray>() : points.OfType<JArray>().Where(point => point.Count >= 5).ToList();
            var colors = validPoints.Count == 0 ? new[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.white, 1) }
                : validPoints.Select(point => new GradientColorKey(new Color((float)point[1], (float)point[2], (float)point[3], (float)point[4]), Mathf.Clamp01((float)point[0]))).ToArray();
            var alpha = validPoints.Count == 0 ? new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
                : validPoints.Select(point => new GradientAlphaKey(Mathf.Clamp01((float)point[4]), Mathf.Clamp01((float)point[0]))).ToArray();
            gradient.SetKeys(colors, alpha);
            var field = new GradientField("Color stops") { value = gradient, tooltip = "Edit up to 8 combined color/alpha stops. Interpolation is linear." };
            field.RegisterValueChangedCallback(evt =>
            {
                evt.newValue.mode = GradientMode.Blend;
                var positions = evt.newValue.colorKeys.Select(key => key.time).Concat(evt.newValue.alphaKeys.Select(key => key.time)).Distinct().OrderBy(value => value).ToList();
                if (positions.Count < 2 || positions.Count > 8) { field.SetValueWithoutNotify(gradient); SetStatus("Use at most 8 combined color/alpha stop positions."); return; }
                Undo.RegisterCompleteObjectUndo(session, "Change Color Ramp");
                node.Properties["stops"] = new JArray(positions.Select(position =>
                {
                    var color = evt.newValue.Evaluate(position);
                    return new JArray(position, color.r, color.g, color.b, color.a);
                }));
                gradient = evt.newValue; field.SetValueWithoutNotify(gradient);
                session.json = GraphJson.Serialize(graph, true); hasUnsavedChanges = true; EditorUtility.SetDirty(session); QueueLivePreview();
            });
            inspector.Add(field);
        }

        void AddTexturePicker(GraphNode node, string label)
        {
            var field = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false,
                tooltip = "Pick a project texture resource." };
            var resourceId = (string)node.Properties["resourceId"];
            var map = graph.Adapter?["textures"] as JObject;
            var guid = (string)map?[resourceId ?? ""];
            if (!string.IsNullOrEmpty(guid)) field.value = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            field.SetEnabled(!string.IsNullOrEmpty(resourceId) && graph.Resources.Any(resource => resource != null && resource.Id == resourceId));
            field.RegisterValueChangedCallback(evt => Edit("Assign texture", () =>
            {
                if (graph.Adapter == null) graph.Adapter = new JObject();
                if (!(graph.Adapter["textures"] is JObject)) graph.Adapter["textures"] = new JObject();
                ((JObject)graph.Adapter["textures"])[resourceId ?? ""] = evt.newValue == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(evt.newValue));
            }));
            inspector.Add(field);
        }

        void AddAudioLinkControls(GraphNode node)
        {
            var bandValue = node.Properties["band"] != null ? (int)node.Properties["band"] : 0;
            var band = new PopupField<string>("Band", new List<string> { "Bass", "Low mids", "High mids", "Treble" }, Mathf.Clamp(bandValue, 0, 3));
            band.RegisterValueChangedCallback(evt => Edit("Change AudioLink band", () => node.Properties["band"] = band.index));
            inspector.Add(band);
            AddNumber(node, "gain", "Gain", 1);
            AddUnitNumber(node, "smoothing", "Smoothing", .5f);
            AddNumber(node, "fallback", "Fallback", 0);
            var toggle = new Toggle("Preview AudioLink") { value = audioPreviewEnabled, tooltip = "Simulate AudioLink only in temporary preview material." };
            var slider = new Slider("Preview value", 0, 1) { value = audioPreviewValue, showInputField = true, tooltip = "Simulate the same input amplitude for all AudioLink nodes; each node keeps its gain." };
            toggle.RegisterValueChangedCallback(evt => { audioPreviewEnabled = evt.newValue; ApplyAudioLinkPreview(); });
            slider.RegisterValueChangedCallback(evt => { audioPreviewValue = Mathf.Clamp01(evt.newValue); ApplyAudioLinkPreview(); });
            inspector.Add(toggle); inspector.Add(slider);
        }

        void SetAudioLinkPreview(bool enabled, float value)
        {
            audioPreviewEnabled = enabled; audioPreviewValue = Mathf.Clamp01(value); ApplyAudioLinkPreview();
        }

        void ApplyAudioLinkPreview()
        {
            if (preview == null) return;
            preview.SetFloat("_NXSG_AudioLinkPreview", audioPreviewEnabled ? 1 : 0);
            preview.SetFloat("_NXSG_AudioLinkValue", Mathf.Clamp01(audioPreviewValue));
            previewHost?.MarkDirtyRepaint();
        }

        void AddRampCurve(GraphNode node)
        {
            var points = node.Properties["points"] as JArray ?? new JArray(new JArray(0, 0), new JArray(1, 1));
            var curve = new AnimationCurve(points.Select(point => new Keyframe((float)point[0], (float)point[1])).ToArray());
            for (var i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            var field = new CurveField("Ramp curve") { value = curve, ranges = new Rect(0, 0, 1, 1),
                tooltip = "Click to move/add curve points. 2–16 points, input/output 0–1. Segments are linear; the Smoothing control rounds transitions." };
            field.RegisterValueChangedCallback(evt =>
            {
                var keys = evt.newValue?.keys;
                if (keys == null || keys.Length < 2 || keys.Length > 16 || keys.Any(k => float.IsNaN(k.time) || float.IsNaN(k.value)
                    || float.IsInfinity(k.time) || float.IsInfinity(k.value) || k.time < 0 || k.time > 1 || k.value < 0 || k.value > 1)
                    || keys.Zip(keys.Skip(1), (a, b) => b.time - a.time).Any(gap => gap < .000001f))
                { field.SetValueWithoutNotify(curve); SetStatus("Use 2–16 distinct curve points inside the 0–1 square."); return; }
                Undo.RegisterCompleteObjectUndo(session, "Change Ramp curve");
                node.Properties["points"] = new JArray(keys.Select(k => new JArray(k.time, k.value)));
                curve = new AnimationCurve(keys);
                for (var i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                field.SetValueWithoutNotify(curve);
                session.json = GraphJson.Serialize(graph, true); hasUnsavedChanges = true; EditorUtility.SetDirty(session);
                QueueLivePreview();
                SetStatus("Unsaved Ramp curve · preview updates after a short pause.");
            });
            inspector.Add(field);
            inspector.Add(new Button(() => Edit("Reset Ramp curve", () => node.Properties["points"] = new JArray(new JArray(0, 0), new JArray(1, 1)))) { text = "Reset curve" });
        }

        void AddFactor(GraphNode node)
        {
            var token = node.Properties["factor"];
            var initial = token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) ? (float)token : .5f;
            var field = new Slider("Factor", 0, 1) { showInputField = true, value = Mathf.Clamp01(initial),
                tooltip = "Blend amount: 0 = A, 1 = B. Connected factor values are also clamped to this range." };
            field.SetEnabled(!graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == "factor"));
            field.RegisterValueChangedCallback(evt =>
            {
                if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue))
                { field.SetValueWithoutNotify(Mathf.Clamp01((float?)node.Properties["factor"] ?? .5f)); return; }
                var value = Mathf.Clamp01(evt.newValue);
                field.SetValueWithoutNotify(value);
                Undo.RegisterCompleteObjectUndo(session, "Change Factor");
                node.Properties["factor"] = value;
                session.json = GraphJson.Serialize(graph, true);
                hasUnsavedChanges = true; EditorUtility.SetDirty(session);
                // Keep the active slider alive while dragging; no graph structure changed.
                QueueLivePreview();
                SetStatus(livePreview ? "Unsaved edits · live preview updates after a short pause." : "Unsaved edits · preview shows the last successful build.");
            });
            inspector.Add(field);
        }

        void AddUnitNumber(GraphNode node, string property, string label, float fallback)
        {
            var field = new Slider(label,0,1) { value = Mathf.Clamp01((float?)node.Properties[property] ?? fallback), showInputField = true };
            field.RegisterValueChangedCallback(evt =>
            {
                if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue)) return;
                Undo.RegisterCompleteObjectUndo(session,"Change " + label);
                node.Properties[property] = Mathf.Clamp01(evt.newValue); field.SetValueWithoutNotify((float)node.Properties[property]);
                session.json = GraphJson.Serialize(graph,true); hasUnsavedChanges = true; EditorUtility.SetDirty(session); QueueLivePreview();
            });
            inspector.Add(field);
        }

        void AddNumber(GraphNode node, string property, string label, float fallback, string input = null)
        {
            var token = node.Properties[property];
            var field = new FloatField(label) { isDelayed = true, value = token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) ? (float)token : fallback };
            if (input != null)
            {
                field.tooltip = "Used when the " + input + " socket is unconnected.";
                field.SetEnabled(!graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == input));
            }
            field.RegisterValueChangedCallback(evt =>
            {
                if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue)) { SetStatus("Enter a finite number."); return; }
                Edit("Change " + label, () => node.Properties[property] = evt.newValue);
            });
            inspector.Add(field);
        }

        void AddVector(GraphNode node, string property, string label, Vector2 fallback)
        {
            var values = node.Properties[property] as JArray;
            var field = new Vector2Field(label) { value = values != null && values.Count == 2 ? new Vector2((float)values[0], (float)values[1]) : fallback };
            field.RegisterValueChangedCallback(evt =>
            {
                var value = evt.newValue;
                if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y)) { SetStatus("Enter finite vector values."); return; }
                Edit("Change " + label, () => node.Properties[property] = new JArray(value.x, value.y));
            });
            inspector.Add(field);
        }

        GraphNode CreateNode(string operation, Vector2 position)
        {
            var node = NodeCatalog.Create(operation);
            if (operation == "core.texture2D" || operation == "core.sticker" || operation == "core.triplanarTexture" || operation == "core.matcapTexture" || operation == "core.parallaxOcclusion" || operation == "core.chromaticTexture" || operation == "core.interiorMapping" || operation == "core.textureBomb")
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
            if (!CanConnectTypes(from, fromPort, to, toPort)) { SetStatus("That connection would make incompatible types. Numbers can become colors; colors cannot become numbers automatically."); return; }
            Edit("Connect nodes", () => AddConnection(fromId, fromPort, toId, toPort));
        }

        bool CanOffer(GraphNode from, string fromPort, GraphNode to, string toPort)
        {
            var fromType = PortType(from, fromPort); var toType = PortType(to, toPort);
            return GraphTypes.Compatible(fromType, toType)
                || (GraphTypes.IsDynamic(to.Operation) && toPort != "factor" && (fromType == "float" || fromType == "color"))
                || (GraphTypes.IsDynamic(from.Operation) && (toType == "float" || toType == "color"));
        }

        bool CanConnectTypes(GraphNode from, string fromPort, GraphNode to, string toPort)
        {
            var trial = new ShaderGraph { Nodes = graph.Nodes, Parameters = graph.Parameters,
                Connections = graph.Connections.Where(e => e.To.NodeId != to.Id || e.To.PortId != toPort).ToList() };
            trial.Connections.Add(new GraphConnection { From = new GraphPortRef { NodeId = from.Id, PortId = fromPort },
                To = new GraphPortRef { NodeId = to.Id, PortId = toPort } });
            var types = GraphTypes.Infer(trial);
            if (!GraphTypes.Compatible(GraphTypes.PortType(trial, from, fromPort, types), GraphTypes.PortType(trial, to, toPort, types))) return false;
            foreach (var edge in graph.Connections)
            {
                if (edge.To.NodeId == to.Id && edge.To.PortId == toPort) continue;
                var source = graph.Nodes.FirstOrDefault(n => n.Id == edge.From.NodeId);
                var target = graph.Nodes.FirstOrDefault(n => n.Id == edge.To.NodeId);
                if (source == null || target == null) continue;
                if (GraphTypes.Compatible(PortType(source, edge.From.PortId), PortType(target, edge.To.PortId))
                    && !GraphTypes.Compatible(GraphTypes.PortType(trial, source, edge.From.PortId, types), GraphTypes.PortType(trial, target, edge.To.PortId, types))) return false;
            }
            return true;
        }

        void AddConnection(string from, string fromPort, string to, string toPort)
        {
            graph.Connections.RemoveAll(e => e.To.NodeId == to && e.To.PortId == toPort);
            graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"),
                From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
        }

        static bool MatchesNodeSearch(string operation, string query)
        {
            return string.IsNullOrWhiteSpace(query) || (Title(operation) + " " + operation + " " + Aliases(operation) + " " + NodeCatalog.Category(operation) + " " + NodeCatalog.Description(operation))
                .IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
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
            var menuOptions = new ScrollView(ScrollViewMode.Vertical) { style = { maxHeight = 220 } };
            spawnMenu.Add(menuOptions);
            var search = new ToolbarSearchField { name = "connected-node-search", tooltip = "Search compatible nodes by name or purpose." };
            spawnMenu.Insert(1, search);
            void RefreshOptions(string query)
            {
                menuOptions.Clear();
                var count = 0;
                foreach (var operation in NodeCatalog.All.Where(op => op != "core.parameter" && op != "core.previewVector"))
                {
                    if (operation == "core.output" && graph.Nodes.Any(n => n.Operation == operation)) continue;
                    if (!MatchesNodeSearch(operation, query)) continue;
                    var candidate = NodeCatalog.Create(operation);
                    var compatible = Ports(operation, !output).Where(port => output ? CanOffer(source, endpointPort, candidate, port) : CanOffer(candidate, port, source, endpointPort)).ToArray();
                    if (compatible.Length == 0) continue;
                    VisualElement group = menuOptions;
                    if (compatible.Length > 1)
                    {
                        var fold = new Foldout { text = Title(operation), value = !string.IsNullOrWhiteSpace(query), tooltip = NodeCatalog.Description(operation) };
                        menuOptions.Add(fold); group = fold;
                    }
                    foreach (var port in compatible)
                    {
                        var op = operation; var compatiblePort = port;
                        group.Add(new Button(() => Edit("Add connected " + Title(op), () =>
                        {
                            var node = CreateNode(op, graphPosition - (output ? Vector2.zero : new Vector2(175, 0)));
                            if (output) AddConnection(endpoint, endpointPort, node.Id, compatiblePort);
                            else AddConnection(node.Id, compatiblePort, endpoint, endpointPort);
                        })) { text = compatible.Length > 1 ? "Connect to " + port : Title(op) + " · " + port, tooltip = NodeCatalog.Description(op), style = { minHeight = 24 } });
                    }
                    count++;
                }
                if (count == 0) menuOptions.Add(new Label("No matching compatible nodes."));
            }
            search.RegisterValueChangedCallback(evt => RefreshOptions(evt.newValue));
            RefreshOptions("");
            spawnMenu.Add(new Button(CancelWire) { text = "Cancel" });
            canvas.Add(spawnMenu);
            search.Focus();
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
                if (wiring && !wireMoved && Vector2.Distance(wireStart, evt.position) > 4)
                {
                    wireMoved = true;
                    DetachInputWire();
                }
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
                painter.lineWidth = edge.Id == insertionEdge ? 7 : 3;
                painter.strokeGradient = WireGradient(from.type, to.type);
                DrawWire(painter, layer.WorldToLocal(from.hit.worldBound.center), layer.WorldToLocal(to.hit.worldBound.center));
            }
            var pending = sockets.FirstOrDefault(s => s.output == pendingOutput && s.node == pendingNode && s.port == pendingPort);
            if (pending != null)
            {
                var target = sockets.FirstOrDefault(s => s.output != pendingOutput && s.hit.worldBound.Contains(layer.LocalToWorld(wirePosition)));
                painter.strokeGradient = pendingOutput ? WireGradient(pending.type, target?.type ?? pending.type)
                    : WireGradient(target?.type ?? pending.type, pending.type);
                var socketPosition = layer.WorldToLocal(pending.hit.worldBound.center);
                DrawWire(painter, pendingOutput ? socketPosition : wirePosition, pendingOutput ? wirePosition : socketPosition);
            }
        }

        Gradient WireGradient(string from, string to)
        {
            var key = from + ":" + to;
            if (!wireGradients.TryGetValue(key, out var gradient))
            {
                gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(SocketColor(from), 0), new GradientColorKey(SocketColor(to), 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
                wireGradients[key] = gradient;
            }
            return gradient;
        }

        static void DrawWire(Painter2D painter, Vector2 a, Vector2 b)
        {
            if (float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsNaN(b.x) || float.IsNaN(b.y)) return;
            var bend = Mathf.Max(45, Mathf.Abs(b.x - a.x) * .45f);
            painter.BeginPath(); painter.MoveTo(a);
            painter.BezierCurveTo(a + Vector2.right * bend, b - Vector2.right * bend, b); painter.Stroke();
        }
        void SetStatus(string message) { if (status != null) status.text = message; }
        static string Title(string operation) { return NodeCatalog.Title(operation); }
        static string Aliases(string operation) { return NodeCatalog.Aliases(operation); }
        string PortType(GraphNode node, string port) { return GraphTypes.PortType(graph, node, port, inferredTypes); }
        static string[] Ports(string operation, bool output)
        {
            return NodeCatalog.Ports(operation, output);
        }
    }
}
