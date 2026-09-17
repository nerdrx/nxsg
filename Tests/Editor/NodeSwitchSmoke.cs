using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

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
        BuildGraph(); EditorApplication.update += Tick;
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
                Debug.Log("NXSG NODE SWITCH CHECK PASSED: Mix/Add factor-wire undo, property preservation, Invert/Clamp identity and wires");
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
