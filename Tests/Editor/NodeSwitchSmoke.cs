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
public static class NodeSwitchSmoke
{
    static GraphWindow window;
    static int ticks, phase;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static GraphNode Node(string id) => Graph.Nodes.Single(n => n.Id == id);
    static bool Wire(string id, string fromNode, string fromPort, string toNode, string toPort)
    {
        return Graph.Connections.Any(e => e.Id == id && e.From.NodeId == fromNode && e.From.PortId == fromPort
            && e.To.NodeId == toNode && e.To.PortId == toPort);
    }

    static GraphConnection Connection(string id, string fromNode, string fromPort, string toNode, string toPort)
    {
        return new GraphConnection { Id = id, From = new GraphPortRef { NodeId = fromNode, PortId = fromPort },
            To = new GraphPortRef { NodeId = toNode, PortId = toPort } };
    }

    static void SetBaseline()
    {
        var session = (GraphSession)Field("session");
        session.json = GraphJson.Serialize(Graph, true);
        Undo.ClearUndo(session);
    }

    static void BuildGraph()
    {
        Invoke("NewGraph");
        Graph.Nodes.Clear(); Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        foreach (var pair in new[] { new[] { "a", "core.constant" }, new[] { "b", "core.constant" },
            new[] { "factorValue", "core.value" }, new[] { "mix", "core.mix" },
            new[] { "toon", "core.toonSurface" } })
            Graph.Nodes.Add(new GraphNode { Id = pair[0], Operation = pair[1], Properties = pair[0] == "factorValue"
                ? new JObject { ["value"] = .25 } : new JObject { ["valueType"] = "color", ["value"] = new JArray(1, 1, 1, 1) } });
        Node("mix").Properties["factor"] = .5;
        Graph.Layout.Nodes["mix"] = new GraphNodeLayout { X = 321, Y = 177 };
        Graph.Connections.Add(Connection("a-to-mix", "a", "value", "mix", "a"));
        Graph.Connections.Add(Connection("b-to-mix", "b", "value", "mix", "b"));
        Graph.Connections.Add(Connection("factor-to-mix", "factorValue", "value", "mix", "factor"));
        Graph.Connections.Add(Connection("mix-to-toon", "mix", "value", "toon", "albedo"));
        SetBaseline(); Invoke("Rebuild"); Invoke("SelectNode", "mix", false);
    }

    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 1100, 700);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        BuildGraph(); EditorApplication.update += Tick;
    }

    static void ConnectThroughEditor(string from, string fromPort, string to, string toPort)
    {
        typeof(GraphWindow).GetField("pendingNode", Private).SetValue(window, from);
        typeof(GraphWindow).GetField("pendingPort", Private).SetValue(window, fromPort);
        typeof(GraphWindow).GetField("pendingOutput", Private).SetValue(window, true);
        Invoke("Connect", to, toPort, false);
    }

    static void Tick()
    {
        if (++ticks % 30 != 0) return;
        try
        {
            if (phase == 0)
            {
                var before = Graph.Layout.Nodes["mix"];
                Invoke("SwitchOperation", "mix", "core.add");
                Require(Node("mix").Operation == "core.add", "Mix did not switch to Add");
                Require(Wire("a-to-mix", "a", "value", "mix", "a") && Wire("b-to-mix", "b", "value", "mix", "b") && Wire("mix-to-toon", "mix", "value", "toon", "albedo"), "Compatible wires changed");
                Require(!Graph.Connections.Any(e => e.To.NodeId == "mix" && e.To.PortId == "factor"), "Factor wire survived Add switch");
                Require(Graph.Layout.Nodes["mix"].X == before.X && Graph.Layout.Nodes["mix"].Y == before.Y, "Node position changed");
                Require(((List<string>)Field("selection")).SequenceEqual(new[] { "mix" }), "Selection changed");
            }
            else if (phase == 1)
            {
                Undo.PerformUndo();
                Require(Node("mix").Operation == "core.mix" && Wire("factor-to-mix", "factorValue", "value", "mix", "factor"), "One Undo did not restore Mix and factor wire");
            }
            else if (phase == 2)
            {
                Invoke("SwitchOperation", "mix", "core.add");
                Require((double)Node("mix").Properties["factor"] == .5, "Factor property lost on Add switch");
                Invoke("SwitchOperation", "mix", "core.mix");
                Require((double)Node("mix").Properties["factor"] == .5, "Factor property lost switching back to Mix");
                Graph.Nodes.Add(new GraphNode { Id = "invert", Operation = "core.oneMinus", Properties = new JObject() });
                Graph.Layout.Nodes["invert"] = new GraphNodeLayout { X = 555, Y = 222 };
                Graph.Connections.Add(Connection("a-to-invert", "a", "value", "invert", "color"));
                Graph.Connections.Add(Connection("invert-to-toon", "invert", "color", "toon", "emission"));
                SetBaseline(); Invoke("Rebuild"); Invoke("SelectNode", "invert", false);
            }
            else if (phase == 3)
            {
                Invoke("SwitchOperation", "invert", "core.clamp");
                Require(Node("invert").Operation == "core.clamp" && Wire("a-to-invert", "a", "value", "invert", "color") && Wire("invert-to-toon", "invert", "color", "toon", "emission"), "Invert did not switch to Clamp with wires");
                Require(((List<string>)Field("selection")).SequenceEqual(new[] { "invert" }), "Invert selection changed");
            }
            else if (phase == 4)
            {
                Undo.PerformUndo();
                Require(Node("invert").Operation == "core.oneMinus" && Wire("a-to-invert", "a", "value", "invert", "color"), "Undo did not restore Invert");
                foreach (var pair in new[] { new[] { "uv", "core.uv0" }, new[] { "scroll", "core.uvScroll" }, new[] { "tex", "core.texture2D" } })
                { var node = NodeCatalog.Create(pair[1]); node.Id = pair[0]; Graph.Nodes.Add(node); }
                Graph.Connections.Add(Connection("uv-scroll", "uv", "uv", "scroll", "uv"));
                Graph.Connections.Add(Connection("time-scroll", "factorValue", "value", "scroll", "time"));
                Graph.Connections.Add(Connection("scroll-tex", "scroll", "uv", "tex", "uv"));
                SetBaseline(); Invoke("Rebuild");
                Invoke("SwitchOperation", "scroll", "core.polarUV");
                Require(Wire("uv-scroll", "uv", "uv", "scroll", "uv") && Wire("scroll-tex", "scroll", "uv", "tex", "uv"), "UV switch lost compatible wires");
                Require(!Graph.Connections.Any(e => e.Id == "time-scroll"), "UV switch retained unavailable time socket");
                Require(Node("scroll").Properties["center"] is JArray, "Polar defaults missing");
            }
            else if (phase == 5)
            {
                Undo.PerformUndo();
                Require(Node("scroll").Operation == "core.uvScroll" && Wire("time-scroll", "factorValue", "value", "scroll", "time"), "UV switch Undo failed");
                Invoke("SwitchOperation", "scroll", "core.worldUV");
                Require(!Graph.Connections.Any(e => e.To.NodeId == "scroll") && Wire("scroll-tex", "scroll", "uv", "tex", "uv"), "World UV switch did not retain only output");
                var inspector = (VisualElement)Field("inspector");
                var library = (VisualElement)Field("libraryPanel");
                Require(new[] { "Inputs", "Coordinates", "Textures", "Math", "Color", "Animation", "Surface" }.All(category => library.Q<Foldout>("category-" + category) != null), "Node categories missing");
                var math = library.Q<Foldout>("category-Math"); math.value = true;
                Invoke("RebuildInspector");
                inspector = (VisualElement)Field("inspector");
                Require(library.Q<Foldout>("category-Math").value, "Category state lost after rebuild");
                var search = library.Q<ToolbarSearchField>("node-search"); search.value = "polar";
                library = (VisualElement)Field("libraryPanel");
                Require(library.Query<Foldout>().ToList().Count == 1 && library.Q<Foldout>().value && library.Q<Foldout>().Query<Button>().ToList().Any(b => b.text.Contains("Polar UVs")) && !library.Query<Button>().ToList().Any(b => b.text.Contains("Toon Surface")), "Search did not reveal relevant Polar UVs choices");
                search.value = "no-such-node-xyz";
                Require(library.Query<Foldout>().ToList().Count == 0, "Empty search left node choices");
                foreach (var pair in new[] { new[] { "auto", "core.add" }, new[] { "num", "core.value" }, new[] { "ramp", "core.ramp" } })
                { var node = NodeCatalog.Create(pair[1]); node.Id = pair[0]; Graph.Nodes.Add(node); }
                Invoke("Rebuild");
            }
            else if (phase == 6)
            {
                ConnectThroughEditor("num", "value", "auto", "a");
                Require(GraphTypes.Infer(Graph)["auto"] == "float", "Math did not switch to number");
                ConnectThroughEditor("auto", "value", "ramp", "value");
                var count = Graph.Connections.Count;
                ConnectThroughEditor("a", "value", "auto", "b");
                Require(Graph.Connections.Count == count, "Color connection broke a downstream numeric consumer");
                Graph.Connections.RemoveAll(e => e.To.NodeId == "ramp"); Invoke("Rebuild");
                ConnectThroughEditor("a", "value", "auto", "b");
                Require(GraphTypes.Infer(Graph)["auto"] == "color", "Mixed math did not switch to color");
                var gradient = (Gradient)typeof(GraphWindow).GetMethod("WireGradient", Private).Invoke(window, new object[] { "float", "color" });
                Require(gradient.Evaluate(0) != gradient.Evaluate(1), "Wire type gradient has identical endpoints");
                Invoke("SelectNode", "ramp", false);
                Require(((VisualElement)Field("inspector")).Q<CurveField>() != null, "Ramp curve control missing");
                Debug.Log("NXSG NODE SWITCH CHECK PASSED: UV/math switching, Undo, categories, automatic types, compatible connections, gradients, and Ramp controls");
                EditorApplication.update -= Tick; window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
            }
            phase++;
        }
        catch (Exception e)
        {
            Debug.LogException(e); EditorApplication.update -= Tick; window.DiscardChanges(); window.Close(); EditorApplication.Exit(1);
        }
    }
}
