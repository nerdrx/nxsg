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
        ScrollView libraryPanel, problemsPanel, performancePanel;
        VisualElement sidebar;
        ToolbarButton problemsButton;
        ToolbarButton[] sidebarTabs;
        string diagnosticsHash;
        double diagnosticsDue;
        bool diagnosticsPending;
        string performanceHash;
        double performanceDue;
        bool performancePending;

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
            view.menu.AppendAction("Performance estimates", _ => ShowSidebarTab(3));
            toolbar.Add(view);
        }

        VisualElement CreateSidebar()
        {
            sidebar = new VisualElement { name = "nxsg-sidebar", style = { minWidth = 240, backgroundColor = new Color(.153f,.165f,.18f) } };
            sidebar.RegisterCallback<GeometryChangedEvent>(evt => { if (evt.newRect.width > 0) sidebarWidth = evt.newRect.width; });
            var tabs = new Toolbar { style = { minHeight = 30 } };
            var inspectorTab = new ToolbarButton(() => ShowSidebarTab(0)) { text = "Inspector" };
            var nodesTab = new ToolbarButton(() => ShowSidebarTab(1)) { text = "Nodes" };
            var performanceTab = new ToolbarButton(() => ShowSidebarTab(3)) { text = "Cost", tooltip = "Performance estimates" };
            tabs.Add(inspectorTab); tabs.Add(nodesTab);
            problemsButton = new ToolbarButton(() => ShowSidebarTab(2)) { text = "Problems" };
            tabs.Add(problemsButton); tabs.Add(performanceTab); sidebar.Add(tabs);
            sidebarTabs = new[] { inspectorTab, nodesTab, problemsButton, performanceTab };
            foreach (var button in sidebarTabs) button.AddToClassList("nxsg-tab");
            libraryPanel = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, name = "node-browser", style = { flexGrow = 1, paddingLeft = 12, paddingRight = 12 } };
            problemsPanel = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, name = "graph-problems", style = { flexGrow = 1, paddingLeft = 12, paddingRight = 12 } };
            performancePanel = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, name = "graph-performance", style = { flexGrow = 1, paddingLeft = 12, paddingRight = 12 } };
            sidebar.Add(inspector); sidebar.Add(libraryPanel); sidebar.Add(problemsPanel); sidebar.Add(performancePanel);
            ShowSidebarTab(sidebarTab);
            return sidebar;
        }

        void ShowSidebarTab(int tab)
        {
            sidebarTab = Mathf.Clamp(tab, 0, 3);
            if (sidebarTabs != null)
                for (var i = 0; i < sidebarTabs.Length; i++)
                {
                    sidebarTabs[i].EnableInClassList("nxsg-tab-selected", i == sidebarTab);
                    sidebarTabs[i].style.borderBottomWidth = 2;
                    sidebarTabs[i].style.borderBottomColor = i == sidebarTab ? new Color(.66f, .43f, 1f) : Color.clear;
                }
            if (libraryPanel == null) return;
            inspector.style.display = sidebarTab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            libraryPanel.style.display = sidebarTab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            problemsPanel.style.display = sidebarTab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            performancePanel.style.display = sidebarTab == 3 ? DisplayStyle.Flex : DisplayStyle.None;
            if (sidebarTab == 2) { diagnosticsHash = null; QueueDiagnostics(); }
            if (sidebarTab == 3) { performanceHash = null; QueuePerformance(); }
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
            QueuePerformance();
        }

        void QueuePerformance() { performancePending = true; performanceDue = EditorApplication.timeSinceStartup + .25; }
        void UpdatePerformance()
        {
            if (!performancePending || graph == null || performancePanel == null || sidebarTab != 3 || EditorApplication.timeSinceStartup < performanceDue || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            performancePending = false;
            var hash = GraphJson.ComputeSemanticHash(graph);
            if (hash == performanceHash) return;
            performanceHash = hash;
            performancePanel.Clear();
            var report = GraphPerformance.Analyze(graph);
            performancePanel.Add(new Label("Performance estimates") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            performancePanel.Add(new HelpBox("These are graph and backend budgets, not GPU time or FPS. Connected runtime values, rendered coverage, lights and target hardware change actual work.", HelpBoxMessageType.Info));
            Metric("Reachable nodes", report.ReachableNodes.Count.ToString());
            Metric("Texture sample sites", report.TextureSampleSites.ToString());
            Metric("Static passes", report.StaticPassBudget.ToString());
            if (!string.IsNullOrEmpty(report.PassNote)) performancePanel.Add(new Label(report.PassNote) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10, marginBottom = 6 } });
            if (report.LoopBudgets.Count > 0)
            {
                performancePanel.Add(new Label("Loop and geometry budgets") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
                foreach (var line in report.LoopBudgets) performancePanel.Add(new HelpBox(line, HelpBoxMessageType.Warning));
            }
            if (report.HotSpots.Count > 0)
            {
                performancePanel.Add(new Label("Where cost comes from") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
                foreach (var item in report.HotSpots)
                {
                    var box = new VisualElement { style = { marginTop = 5, paddingBottom = 6, borderBottomWidth = 1, borderBottomColor = new Color(.25f,.25f,.25f) } };
                    box.Add(new Button(() => { SelectNode(item.NodeId, false); FrameNodes(true); ShowSidebarTab(0); }) { text = item.Label + " · focus", tooltip = item.Label + " · " + item.NodeId + "\nFocus this node on the graph", style = { minWidth = 0, whiteSpace = WhiteSpace.NoWrap, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } });
                    box.Add(new Label(item.Estimate) { style = { whiteSpace = WhiteSpace.Normal, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } });
                    box.Add(new Label(item.Explanation) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10 } });
                    performancePanel.Add(box);
                }
            }
            else performancePanel.Add(new HelpBox("No modeled hot spots on the connected Output path.", HelpBoxMessageType.Info));
            if (report.DynamicUnknowns.Count > 0)
            {
                performancePanel.Add(new Label("Dynamic or unresolved") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
                foreach (var line in report.DynamicUnknowns) performancePanel.Add(new HelpBox(line, HelpBoxMessageType.Info));
            }
        }
        void Metric(string name, string value)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginTop = 4 } };
            row.Add(new Label(name) { style = { flexGrow = 1, whiteSpace = WhiteSpace.Normal } });
            row.Add(new Label(value) { style = { unityFontStyleAndWeight = FontStyle.Bold, minWidth = 24, unityTextAlign = TextAnchor.MiddleRight } });
            performancePanel.Add(row);
        }

        void QueueDiagnostics() { diagnosticsPending = true; diagnosticsDue = EditorApplication.timeSinceStartup + .4; QueuePerformance(); }
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
                    var hint = DiagnosticHint(diagnostic);
                    if(hint!=null)box.Add(new HelpBox(hint,HelpBoxMessageType.Info));
                    box.tooltip = diagnostic.Code + " · " + diagnostic.Path;
                    problemsPanel.Add(box);
                }
            }
            catch (Exception exception) { problemsPanel.Add(new HelpBox("Cannot check graph: " + exception.Message, HelpBoxMessageType.Error)); }
        }
        static string DiagnosticHint(Diagnostic diagnostic)
        {
            var code=diagnostic.Code??"";
            if(code.IndexOf("cycle",StringComparison.OrdinalIgnoreCase)>=0)return "Disconnect one wire in the loop. Graph outputs must flow forward without feeding themselves.";
            if(code.IndexOf("symbol-collision",StringComparison.OrdinalIgnoreCase)>=0)return "Give the colliding parameters different stable IDs; punctuation becomes underscores in shader property names.";
            if(code.IndexOf("resource",StringComparison.OrdinalIgnoreCase)>=0)return "Select the texture node and assign a texture from this Unity project, then rebuild.";
            if(code.IndexOf("unsupported",StringComparison.OrdinalIgnoreCase)>=0)return "Check the selected output/backend and replace the unsupported node or binding. Your saved graph is preserved.";
            return null;
        }
    }
}
