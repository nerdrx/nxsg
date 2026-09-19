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

// Run in an isolated Unity project with NXSG installed; never on a user's graph.
public static class FeatureNodesEditorSmoke
{
    static readonly string[] Operations =
    {
        "tessellation", "fur", "parallaxUV", "parallaxOcclusion", "furMask", "flowMapUV", "ditherMask", "truchet", "weave", "scales", "dots",
        "scratches", "cracks", "woodRings", "marble", "clouds", "sparkleMask", "scanlines", "glitchUV", "pixelateUV", "kaleidoscopeUV",
        "swapUV", "spherizeUV", "pinchUV", "barrelUV", "chromaticTexture", "normalBlend", "normalStrength", "normalFromHeight",
        "reflectionDirection", "objectScale", "objectOrigin", "objectRandom", "distanceToPoint", "sphereMask", "boxVolumeMask",
        "capsuleMask", "stripes3D", "snowMask", "wetnessColor", "anisotropicHighlight"
    };
    static GraphWindow window;
    static int phase, ticks;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static VisualElement Inspector => (VisualElement)Field("inspector");
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);

    static void BuildGraph()
    {
        Invoke("NewGraph"); Graph.Nodes.Clear(); Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        var source = NodeCatalog.Create("core.value"); source.Id = "source"; Graph.Nodes.Add(source);
        foreach (var operation in Operations)
        {
            var node = NodeCatalog.Create("core." + operation);
            Require(node != null, "Missing feature node: " + operation);
            node.Id = operation;
            if (operation == "parallaxOcclusion" || operation == "chromaticTexture")
            {
                node.Properties["resourceId"] = "resource-" + operation;
                Graph.Resources.Add(new GraphResource { Id = "resource-" + operation, Kind = "texture2D", Uri = "builtin://white" });
            }
            Graph.Nodes.Add(node);
            Graph.Layout.Nodes[node.Id] = new GraphNodeLayout { X = 80 + (Graph.Nodes.Count % 6) * 220, Y = 80 + (Graph.Nodes.Count / 6) * 150 };
        }
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; Graph.Nodes.Add(output);
        Graph.Layout.Nodes["source"] = new GraphNodeLayout { X = 40, Y = 40 };
        Graph.Layout.Nodes["output"] = new GraphNodeLayout { X = 1600, Y = 900 };
        Graph.Connections.Add(new GraphConnection { Id = "source-height", From = new GraphPortRef { NodeId = "source", PortId = "value" }, To = new GraphPortRef { NodeId = "parallaxUV", PortId = "height" } });
        Graph.Connections.Add(new GraphConnection { Id = "source-tess-height", From = new GraphPortRef { NodeId = "source", PortId = "value" }, To = new GraphPortRef { NodeId = "tessellation", PortId = "height" } });
        ((GraphSession)Field("session")).json = GraphJson.Serialize(Graph, true);
        Undo.ClearUndo((GraphSession)Field("session"));
        Invoke("Rebuild");
    }

    static void CheckNodeInspector(string operation)
    {
        Invoke("SelectNode", operation, false);
        var description = NodeCatalog.Description("core." + operation);
        Require(Inspector.Query<Label>().ToList().Any(label => label.text == description), operation + " description missing");
        Require(Inspector.Query<Button>().ToList().Any(button => button.text == "Preview selected node"), operation + " preview action missing");
    }

    static void CheckFeatures()
    {
        foreach (var operation in Operations) CheckNodeInspector(operation);
        Invoke("SelectNode", "fur", false);
        var sections = Inspector.Query<Foldout>().ToList().Select(foldout => foldout.text).ToList();
        Require(sections.Contains("Shells") && sections.Contains("Strands") && sections.Contains("Grooming & wind") && sections.Contains("Distance LOD"), "Fur sections missing");
        var layers = Inspector.Query<IntegerField>().ToList().Single(field => field.label == "Shell layers");
        Require(layers.value == 16, "Fur layers default missing");
        Require(Inspector.Query<IntegerField>().ToList().Any(field => field.label == "Minimum LOD layers"), "Fur minimum LOD control missing");

        foreach (var operation in new[] { "parallaxOcclusion", "chromaticTexture" })
        {
            Invoke("SelectNode", operation, false);
            Require(Inspector.Query<ObjectField>().ToList().Count == 1, operation + " texture picker missing");
        }
        Invoke("SelectNode", "parallaxOcclusion", false);
        Require(Inspector.Query<IntegerField>().ToList().Single(field => field.label == "Ray steps").value == 16, "POM steps default missing");
        Invoke("SelectNode", "parallaxUV", false);
        var height = Inspector.Query<FloatField>().ToList().Single(field => field.label == "Height");
        Require(!height.enabledSelf, "Connected parallax height control stayed enabled");

        Invoke("SelectNode", "tessellation", false);
        Require(Inspector.Query<IntegerField>().ToList().Single(field => field.label == "Tessellation factor").value == 8, "Tessellation factor default missing");
        Require(!Inspector.Query<FloatField>().ToList().Single(field => field.label == "Height").enabledSelf, "Connected tessellation height stayed enabled");
        var library = Inspector.Q<VisualElement>("node-library");
        var search = library.Q<ToolbarSearchField>("node-search");
        search.value = "parallax";
        Require(library.Query<Button>().ToList().Count >= 2, "Feature search metadata did not find both parallax nodes");
    }

    static void StartFramingCheck()
    {
        var toolbar = window.rootVisualElement.Query<Button>().ToList();
        Require(toolbar.Any(button => button.text == "Fit graph") && toolbar.Any(button => button.text == "Frame selected") && toolbar.Any(button => button.text == "Add node"), "Framing/search toolbar actions missing");
        Invoke("FrameNodes", false);
    }

    static void CheckWholeGraphFraming()
    {
        var canvas = (VisualElement)Field("canvas");
        var nodeViews = (Dictionary<string, VisualElement>)Field("nodes");
        Require(nodeViews.Values.All(node => canvas.worldBound.Contains(node.worldBound.center)), "Fit graph left node outside viewport");
        Invoke("SelectNode", "objectOrigin", false);
        Invoke("FrameNodes", true);
    }

    static void CheckSelectedFramingAndSearchFocus()
    {
        var canvas = (VisualElement)Field("canvas");
        var nodeViews = (Dictionary<string, VisualElement>)Field("nodes");
        Require(canvas.worldBound.Contains(nodeViews["objectOrigin"].worldBound.center), "Frame selected missed selected node");
        Invoke("FocusNodeSearch");
        var search = Inspector.Q<ToolbarSearchField>("node-search");
        var focused = search.panel?.focusController?.focusedElement as VisualElement;
        var insideSearch = false;
        for (var current = focused; current != null; current = current.parent)
            if (current == search) { insideSearch = true; break; }
        Require(insideSearch, "FocusNodeSearch did not focus node search");
    }

    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 1100, 800);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        BuildGraph(); EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (++ticks % 30 != 0) return;
        try
        {
            if (phase == 0) { CheckFeatures(); StartFramingCheck(); phase = 1; }
            else if (phase == 1) { CheckWholeGraphFraming(); phase = 2; }
            else if (phase == 2) { CheckSelectedFramingAndSearchFocus(); phase = 3; }
            else
            {
                Debug.Log("NXSG FEATURE NODES EDITOR SMOKE PASSED: 41 node inspectors, descriptions, texture controls, defaults, connected disable, and search metadata");
                EditorApplication.update -= Tick; window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            EditorApplication.update -= Tick;
            if (window != null) { window.DiscardChanges(); window.Close(); }
            Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
