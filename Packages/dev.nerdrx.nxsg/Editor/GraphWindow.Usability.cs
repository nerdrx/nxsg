using System;
using System.Linq;
using System.Text.RegularExpressions;
using NXSG.Core;
using NXSG.Backend;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        [SerializeField] float sidebarWidth = 310;
        [SerializeField] int sidebarTab;
        ScrollView libraryPanel, problemsPanel;
        VisualElement sidebar;
        ToolbarButton problemsButton;
        string diagnosticsHash;
        double diagnosticsDue;
        bool diagnosticsPending;

        void AddFileAndEditMenus(Toolbar toolbar)
        {
            var file = new ToolbarMenu { text = "File" };
            file.menu.AppendAction("New graph", _ => NewGraph());
            file.menu.AppendAction("Open graph…", _ => {
                if (!CanDiscard()) return;
                var path = EditorUtility.OpenFilePanel("Open NXSG graph", Application.dataPath, "nxsg");
                if (!string.IsNullOrEmpty(path)) LoadPath(path);
            });
            file.menu.AppendAction("Save as…", _ => SaveCopy());
            file.menu.AppendAction("Recovery / checkpoints…", _ => ShowRecoveryMenu());
            toolbar.Add(file);
            var edit = new ToolbarMenu { text = "Edit" };
            edit.menu.AppendAction("Copy selection  Ctrl+C", _ => CopySelection());
            edit.menu.AppendAction("Paste  Ctrl+V", _ => PasteSelection());
            edit.menu.AppendAction("Duplicate selection  Ctrl+D", _ => DuplicateSelection());
            edit.menu.AppendAction("Delete selection", _ => DeleteSelection());
            toolbar.Add(edit);
        }

        void AddViewMenu(Toolbar toolbar)
        {
            var view = new ToolbarMenu { text = "View" };
            view.menu.AppendAction("Fit graph  Home", _ => FrameNodes(false));
            view.menu.AppendAction("Frame selection  F", _ => FrameNodes(true));
            view.menu.AppendAction("Reset view", _ => { pan = new Vector2(30, 70); zoom = 1; TransformCanvas(); });
            view.menu.AppendAction("Problems", _ => ShowSidebarTab(2));
            toolbar.Add(view);
        }

        VisualElement CreateSidebar()
        {
            sidebar = new VisualElement { name = "nxsg-sidebar", style = { minWidth = 240, backgroundColor = new Color(.10f,.10f,.10f) } };
            sidebar.RegisterCallback<GeometryChangedEvent>(evt => { if (evt.newRect.width > 0) sidebarWidth = evt.newRect.width; });
            var tabs = new Toolbar { style = { minHeight = 30 } };
            tabs.Add(new ToolbarButton(() => ShowSidebarTab(0)) { text = "Inspector" });
            tabs.Add(new ToolbarButton(() => ShowSidebarTab(1)) { text = "Nodes" });
            problemsButton = new ToolbarButton(() => ShowSidebarTab(2)) { text = "Problems" };
            tabs.Add(problemsButton); sidebar.Add(tabs);
            libraryPanel = new ScrollView { name = "node-browser", style = { flexGrow = 1, paddingLeft = 12, paddingRight = 12 } };
            problemsPanel = new ScrollView { name = "graph-problems", style = { flexGrow = 1, paddingLeft = 12, paddingRight = 12 } };
            sidebar.Add(inspector); sidebar.Add(libraryPanel); sidebar.Add(problemsPanel);
            ShowSidebarTab(sidebarTab);
            return sidebar;
        }

        void ShowSidebarTab(int tab)
        {
            sidebarTab = Mathf.Clamp(tab, 0, 2);
            if (libraryPanel == null) return;
            inspector.style.display = sidebarTab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            libraryPanel.style.display = sidebarTab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            problemsPanel.style.display = sidebarTab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            if (sidebarTab == 2) { diagnosticsHash = null; QueueDiagnostics(); }
        }

        // Value changes keep the active field and pointer capture alive.
        void EditValue(string name, Action action)
        {
            if (graph == null) return;
            Undo.RegisterCompleteObjectUndo(session, name);
            action();
            session.json = GraphJson.Serialize(graph, true);
            hasUnsavedChanges = true;
            EditorUtility.SetDirty(session);
            QueueLivePreview();
        }

        void QueueDiagnostics() { diagnosticsPending = true; diagnosticsDue = EditorApplication.timeSinceStartup + .4; }
        void UpdateDiagnostics()
        {
            if (!diagnosticsPending || graph == null || problemsPanel == null || EditorApplication.timeSinceStartup < diagnosticsDue
                || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            diagnosticsPending = false;
            var hash = GraphJson.ComputeSemanticHash(graph);
            if (hash == diagnosticsHash) return;
            diagnosticsHash = hash;
            problemsPanel.Clear();
            try
            {
                var diagnostics = ShaderEmitter.Emit(graph, OptionalIntegrations.Options()).Diagnostics;
                var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                problemsButton.text = diagnostics.Count == 0 ? "Problems" : "Problems · " + diagnostics.Count;
                problemsButton.tooltip = errors + " errors; " + (diagnostics.Count-errors) + " notes/warnings. Click to inspect.";
                if (diagnostics.Count == 0) problemsPanel.Add(new HelpBox("Graph checks passed. Build to validate the shader on this graphics device.", HelpBoxMessageType.Info));
                foreach (var diagnostic in diagnostics.Take(50))
                {
                    var match = Regex.Match(diagnostic.Path, @"\$\.nodes\[(\d+)\]");
                    var index = match.Success ? int.Parse(match.Groups[1].Value) : -1;
                    var node = index >= 0 && index < graph.Nodes.Count ? graph.Nodes[index] : graph.Nodes.FirstOrDefault(n => n.Id == diagnostic.Path);
                    var box = new VisualElement { style = { marginTop = 8, paddingBottom = 8, borderBottomWidth = 1, borderBottomColor = new Color(.25f,.25f,.25f) } };
                    box.Add(new HelpBox(diagnostic.Message, diagnostic.Severity == DiagnosticSeverity.Error ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning));
                    if (node != null) box.Add(new Button(() => { SelectNode(node.Id, false); FrameNodes(true); ShowSidebarTab(0); }) { text = "Show " + Title(node.Operation) });
                    box.tooltip = diagnostic.Code + " · " + diagnostic.Path;
                    problemsPanel.Add(box);
                }
            }
            catch (Exception exception) { problemsPanel.Add(new HelpBox("Cannot check graph: " + exception.Message, HelpBoxMessageType.Error)); }
        }
    }
}
