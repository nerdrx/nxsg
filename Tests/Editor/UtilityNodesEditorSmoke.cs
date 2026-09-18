using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

// Run in an isolated Unity project with NXSG installed; never on a user's graph.
public static class UtilityNodesEditorSmoke
{
    static readonly string[] Operations =
    {
        "absolute", "power", "sqrt", "sine", "cosine", "fraction", "floor", "ceil", "round", "step",
        "smoothstep", "remap", "pingPong", "splitColor", "combineColor", "luminance", "contrast", "saturation",
        "splitUV", "combineUV",
        "position", "normalDirection", "viewDirection", "vertexColor", "cameraDistance", "screenUV", "circleMask", "boxMask", "polygonMask", "starMask", "radialRays", "spiral", "brick", "hexGrid", "triplanarTexture", "matcapTexture", "rimGlow", "heightMask", "slopeMask", "distanceFade", "wireframe"
    };
    static readonly Dictionary<string, string[]> PreviewPorts = new Dictionary<string, string[]>
    {
        ["absolute"] = new[] { "value" }, ["power"] = new[] { "value" }, ["sqrt"] = new[] { "value" },
        ["sine"] = new[] { "value" }, ["cosine"] = new[] { "value" }, ["fraction"] = new[] { "value" },
        ["floor"] = new[] { "value" }, ["ceil"] = new[] { "value" }, ["round"] = new[] { "value" },
        ["step"] = new[] { "value" }, ["smoothstep"] = new[] { "value" }, ["remap"] = new[] { "value" },
        ["pingPong"] = new[] { "value" }, ["splitColor"] = new[] { "r", "g", "b", "a" },
        ["combineColor"] = new[] { "color" }, ["luminance"] = new[] { "value" }, ["contrast"] = new[] { "color" },
        ["saturation"] = new[] { "color" }, ["splitUV"] = new[] { "u", "v" }, ["combineUV"] = new[] { "uv" }
    };
    static GraphWindow window;
    static int ticks, phase;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static VisualElement Inspector => (VisualElement)Field("inspector");
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static GraphNode Node(string id) => Graph.Nodes.Single(node => node.Id == id);

    static GraphConnection Connection(string id, string fromNode, string fromPort, string toNode, string toPort)
    {
        return new GraphConnection { Id = id, From = new GraphPortRef { NodeId = fromNode, PortId = fromPort },
            To = new GraphPortRef { NodeId = toNode, PortId = toPort } };
    }

    static void BuildGraph()
    {
        Invoke("NewGraph");
        Graph.Nodes.Clear(); Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        var source = NodeCatalog.Create("core.value"); source.Id = "source"; Graph.Nodes.Add(source);
        foreach (var operation in Operations)
        {
            var node = NodeCatalog.Create("core." + operation); node.Id = operation; Graph.Nodes.Add(node);
            if(operation=="triplanarTexture"||operation=="matcapTexture")
            {var resource="texture-"+operation;node.Properties["resourceId"]=resource;Graph.Resources.Add(new GraphResource{Id=resource,Kind="texture2D",Uri="builtin://white"});}
            Graph.Layout.Nodes[node.Id] = new GraphNodeLayout { X = 40 + (Graph.Nodes.Count % 5) * 210, Y = 100 + (Graph.Nodes.Count / 5) * 145 };
        }
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; Graph.Nodes.Add(output);
        Graph.Layout.Nodes["source"] = new GraphNodeLayout { X = 40, Y = 40 };
        Graph.Layout.Nodes["output"] = new GraphNodeLayout { X = 900, Y = 700 };
        Graph.Connections.Add(Connection("source-to-remap", "source", "value", "remap", "value"));
        ((GraphSession)Field("session")).json = GraphJson.Serialize(Graph, true);
        Undo.ClearUndo((GraphSession)Field("session"));
        Invoke("Rebuild"); Invoke("SelectNode", "splitColor", false);
    }

    static Color CategoryColor(string category)
    {
        switch (category)
        {
            case "Math": return new Color(.28f, .33f, .38f);
            case "Color": return new Color(.40f, .34f, .10f);
            case "Coordinates": return new Color(.16f, .32f, .52f);
            case "Inputs": return new Color(.12f,.38f,.40f);
            case "Textures": return new Color(.46f,.25f,.10f);
            default: return new Color(.28f, .28f, .28f);
        }
    }

    static void CheckInspectorAndColors()
    {
        var nodeViews = (Dictionary<string, VisualElement>)Field("nodes");
        foreach (var operation in Operations)
        {
            var id = operation;
            Invoke("SelectNode", id, false);
            var preview = Inspector.Query<PopupField<string>>().ToList().FirstOrDefault();
            Require(preview != null, operation + " preview output selector missing");
            Require(preview.choices.SequenceEqual(PreviewPorts.ContainsKey(operation) ? PreviewPorts[operation] : NodeCatalog.Ports("core."+operation,true)), operation + " preview ports mismatch");
            Require(Inspector.Query<Button>().ToList().Any(button => button.text == "Preview selected node"), operation + " preview button missing");
            if(operation=="triplanarTexture"||operation=="matcapTexture")
                Require(Inspector.Query<ObjectField>().ToList().Count==1,"Exactly one texture picker expected for "+operation);
            Require(!string.IsNullOrWhiteSpace(NodeCatalog.Description("core."+operation)),operation+" missing tooltip");
            var title = nodeViews[id].Q<Label>();
            var expected = CategoryColor(NodeCatalog.Category("core." + operation));
            Require(title != null && title.resolvedStyle.backgroundColor == expected, operation + " category color mismatch");
        }
    }

    static void CheckConnectedMenu()
    {
        typeof(GraphWindow).GetField("pendingNode", Private).SetValue(window, "source");
        typeof(GraphWindow).GetField("pendingPort", Private).SetValue(window, "value");
        typeof(GraphWindow).GetField("pendingOutput", Private).SetValue(window, true);
        var canvas = (VisualElement)Field("canvas");
        Invoke("ShowSpawnMenu", canvas.worldBound.center);
        var menu = (VisualElement)Field("spawnMenu");
        Require(menu != null, "Connected-node menu missing");
        var search = menu.Q<ToolbarSearchField>("connected-node-search");
        Require(search != null, "Connected-node search missing");
        search.value = "remap";
        var foldouts = menu.Query<Foldout>().ToList();
        Require(foldouts.Count == 1 && foldouts[0].text == "Remap", "Remap search did not show one grouped node");
        Require(foldouts[0].Query<Button>().ToList().Count == 5, "Remap group did not expose five compatible inputs");
        search.value = "no-such-utility-node";
        Require(menu.Query<Label>().ToList().Any(label => label.text == "No matching compatible nodes."), "Empty connected-node search message missing");
        Invoke("CancelWire");
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
            if (phase == 0) { CheckInspectorAndColors(); phase = 1; }
            else if (phase == 1) { CheckConnectedMenu(); Invoke("SelectNode","circleMask",false); Debug.Log("NXSG UI READY FOR CAPTURE"); phase = 2; }
            else
            {
                Debug.Log("NXSG UTILITY NODES EDITOR SMOKE PASSED: 41 node inspectors, category colors, preview outputs, connected-node search, grouped ports, and empty search");
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
