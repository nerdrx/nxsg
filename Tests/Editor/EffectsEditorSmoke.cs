using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

// Run in an isolated Unity project with NXSG installed.
public static class EffectsEditorSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int phase;
    static int ticks;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.position = new Rect(0, 0, 1100, 700);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (++ticks % 20 != 0) return;
        try
        {
            if (phase == 0) BuildFixture();
            else if (phase == 1) CheckCategoriesAndRamp();
            else if (phase == 2) CheckPreviewClone();
            else if (phase == 3) CheckPatternsAndUndo();
            else
            {
                EditorApplication.update -= Tick;
                window.DiscardChanges(); window.Close();
                Debug.Log("NXSG EFFECTS EDITOR CHECK PASSED");
                EditorApplication.Exit(0);
                return;
            }
            phase++;
        }
        catch (Exception exception)
        {
            EditorApplication.update -= Tick;
            Debug.LogException(exception);
            window.DiscardChanges(); window.Close();
            EditorApplication.Exit(1);
        }
    }

    static void BuildFixture()
    {
        Invoke("NewGraph");
        Graph.Nodes.Clear(); Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        Graph.Nodes.Add(new GraphNode { Id = "value", Operation = "core.value", Properties = new JObject { ["value"] = .25 } });
        Graph.Nodes.Add(new GraphNode { Id = "ramp", Operation = "core.colorRamp", Properties = new JObject
        {
            ["stops"] = new JArray(new JArray(0, 0, 0, 0, 1), new JArray(1, 1, .5, 0, .75))
        }});
        Graph.Nodes.Add(new GraphNode { Id = "unlit", Operation = "core.unlitSurface", Properties = new JObject() });
        Graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output", Properties = new JObject() });
        Graph.Connections.Add(new GraphConnection { Id = "value-ramp", From = new GraphPortRef { NodeId = "value", PortId = "value" }, To = new GraphPortRef { NodeId = "ramp", PortId = "value" } });
        Graph.Connections.Add(new GraphConnection { Id = "ramp-unlit", From = new GraphPortRef { NodeId = "ramp", PortId = "color" }, To = new GraphPortRef { NodeId = "unlit", PortId = "albedo" } });
        Graph.Connections.Add(new GraphConnection { Id = "unlit-output", From = new GraphPortRef { NodeId = "unlit", PortId = "surface" }, To = new GraphPortRef { NodeId = "output", PortId = "surface" } });
        Invoke("Rebuild");
    }

    static void CheckCategoriesAndRamp()
    {
        var inspector = (VisualElement)Field("inspector");
        var library = inspector.Q<VisualElement>("node-library");
        Require(library.Q<Foldout>("category-Color") != null, "Color category missing");
        Require(library.Q<Foldout>("category-Animation") != null, "Animation category missing");
        Invoke("SelectNode", "ramp", false);
        inspector = (VisualElement)Field("inspector");
        var gradient = inspector.Q<GradientField>();
        Require(gradient != null, "ColorRamp GradientField missing");
        Require(gradient.value.colorKeys.Length == 2 && Math.Abs(gradient.value.colorKeys[1].color.g - .5f) < .01f,
            "ColorRamp GradientField did not load stops");
    }

    static void CheckPreviewClone()
    {
        var before = GraphJson.Serialize(Graph);
        Invoke("PreviewNode", Graph.Nodes.Single(node => node.Id == "value"), "value");
        Require(Field("preview") != null, "Selected float preview did not create a temporary material");
        Require(GraphJson.Serialize(Graph) == before, "Selected preview mutated source graph");
        Require(Graph.Nodes.Count(node => node.Operation == "core.output") == 1, "Source output changed during preview");
        foreach(var operation in new[]{"core.uv0","core.normalMap"})
        {
            var node=NodeCatalog.Create(operation);Graph.Nodes.Add(node);
            before=GraphJson.Serialize(Graph);
            Invoke("PreviewNode",node,NodeCatalog.Ports(operation,true).First());
            Require(!(Field("previewMessage") as string).Contains("failed"),operation+" selected preview failed");
            Require(GraphJson.Serialize(Graph)==before,operation+" preview mutated source");
        }
    }

    static void CheckPatternsAndUndo()
    {
        var before = GraphJson.Serialize(Graph);
        typeof(GraphWindow).GetField("selection", Private).SetValue(window, new List<string> { "value", "ramp" });
        Invoke("Edit", "Add pattern", (Action)(() => GraphGroups.Add(Graph, new[] { "value", "ramp" }, "Effects")));
        var group = GraphGroups.All(Graph).Single();
        Invoke("Edit", "Collapse pattern", (Action)(() => group["collapsed"] = true));
        Invoke("Rebuild");
        var card = ((VisualElement)Field("layer")).Q<VisualElement>("pattern-card");
        Require(card != null, "Pattern card missing");
        var nodeViews = (Dictionary<string, VisualElement>)Field("nodes");
        Require(nodeViews["value"].style.display == DisplayStyle.None && nodeViews["ramp"].style.display == DisplayStyle.None,
            "Collapsed pattern did not hide member nodes");
        Require(GraphJson.Serialize(Graph) != before, "Pattern group did not persist");
        Undo.IncrementCurrentGroup();
        Invoke("Edit", "Remove pattern", (Action)(() => GraphGroups.Remove(Graph, group)));
        Require(!GraphGroups.All(Graph).Any(), "Pattern remove did not apply");
        Undo.PerformUndo();
        Require(GraphGroups.All(Graph).Any(), "Pattern Undo did not restore group");
    }
}
