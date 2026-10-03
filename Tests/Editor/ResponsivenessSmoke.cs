using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Run in the hidden editor fixture with -executeMethod ResponsivenessSmoke.Run.
public static class ResponsivenessSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int ticks, phase;
    static Vector2 initialWireStart;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Set(string name, object value) => typeof(GraphWindow).GetField(name, Private).SetValue(window, value);
    static object Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void Run()
    {
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 1280, 800); window.CreateGUI();
            Set("livePreview", false); Set("autoScene", false);
            var graph = CreateGraph(); Set("graph", graph);
            Set("session", ScriptableObject.CreateInstance<GraphSession>());
            ((GraphSession)Field("session")).json = GraphJson.Serialize(graph, true);
            Invoke("Rebuild");

            EditorApplication.update += Tick;
        }
        catch (Exception exception) { UnityEngine.Debug.LogException(exception); Finish(1); }
    }

    static void Tick()
    {
        if (++ticks < 30) return; ticks = 0;
        try
        {
            if (phase++ == 0)
            {
                CheckSocketLookup();
                Set("selection", new List<string> { "insert" });
                var sockets = ((System.Collections.IEnumerable)Field("sockets")).Cast<object>();
                Func<object, string, object> get = (socket, key) => socket.GetType().GetField(key).GetValue(socket);
                var from = sockets.Single(socket => (string)get(socket, "node") == "source" && (bool)get(socket,"output"));
                var to = sockets.Single(socket => (string)get(socket, "node") == "target" && !(bool)get(socket,"output") && (string)get(socket,"port") == "albedo");
                var layer = (VisualElement)Field("layer");
                var midpoint = layer.WorldToLocal((((VisualElement)get(from,"hit")).worldBound.center + ((VisualElement)get(to,"hit")).worldBound.center) * .5f);
                var box = ((Dictionary<string, VisualElement>)Field("nodes"))["insert"];
                var position = (Vector2)Invoke("Position", "insert") + midpoint - layer.WorldToLocal(box.worldBound.center);
                Invoke("SetPosition", "insert", position); box.style.left = position.x; box.style.top = position.y;
                return;
            }
            if (phase == 4)
            {
                CheckInsertionGeometryInvalidation();
                CheckSocketLookup();
                CheckDeferredSceneHash();
                CheckSelectionAndDiagnostics();
                CheckWireVisibility();
                UnityEngine.Debug.Log("NXSG RESPONSIVENESS SMOKE PASSED");
                Finish(0);
                return;
            }
            if (phase == 6)
            {
                BeginInsertionGeometryInvalidation();
                return;
            }
            Invoke("UpdateInsertionTarget", "insert");
            Require((string)Field("insertionEdge") == "wire", "Insertion target did not find the known nearby wire");
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++) Invoke("UpdateInsertionTarget", "insert");
            watch.Stop();
            UnityEngine.Debug.Log("NXSG responsiveness insertion target: 1000 cached updates in " + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms");

            Require((bool)Invoke("InsertOnHighlightedWire", "insert"), "Insertion edit failed");
            Require(Graph.Connections.Count(e => e.From.NodeId == "source" && e.To.NodeId == "insert") == 1, "Inserted source wire missing");
            Require(Graph.Connections.Count(e => e.From.NodeId == "insert" && e.To.NodeId == "target") == 1, "Inserted target wire missing");
            Undo.PerformUndo();
            Require(Graph.Connections.Count(e => e.Id == "wire") == 1 && !Graph.Connections.Any(e => e.From.NodeId == "insert"), "Undo did not restore the original wire");

            Invoke("Rebuild");
            phase = 5;
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
            Finish(1);
        }
    }

    static void BeginInsertionGeometryInvalidation()
    {
        Set("selection", new List<string> { "insert" });
        Set("selected", "insert");
        Invoke("UpdateInsertionTarget", "insert");
        initialWireStart = CandidateWireStart();
        var source = ((Dictionary<string, VisualElement>)Field("nodes"))["source"];
        var old = (Vector2)Invoke("Position", "source");
        var moved = old + new Vector2(24, 0);
        Invoke("SetPosition", "source", moved);
        source.style.left = moved.x;
        source.style.top = moved.y;
        phase = 3;
    }

    static void CheckInsertionGeometryInvalidation()
    {
        var title = ((Dictionary<string, VisualElement>)Field("nodes"))["insert"].Children().First();
        using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = title.worldBound.center })) title.SendEvent(down);
        Require(Field("insertionNodeId") == null, "Starting a new node drag retained insertion geometry from the previous gesture");
        Require(Field("insertionEdge") == null, "Starting a new node drag retained the previous wire highlight");
        using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = title.worldBound.center })) title.SendEvent(up);
        Invoke("UpdateInsertionTarget", "insert");
        var movedWireStart = CandidateWireStart();
        var delta = movedWireStart - initialWireStart;
        Require(Mathf.Abs(delta.x - 24) < 2 && Mathf.Abs(delta.y) < 2, "New drag did not rebuild insertion geometry after the source endpoint moved 24 canvas units");
    }

    static Vector2 CandidateWireStart()
    {
        var candidates = (System.Collections.IEnumerable)Field("insertionCandidates");
        foreach (var candidate in candidates)
        {
            var entry = (ValueTuple<GraphConnection, Vector2, Vector2, float, Vector2[]>)candidate;
            if (entry.Item1.Id == "wire") return entry.Item2;
        }
        throw new InvalidOperationException("Known wire is missing from insertion candidates");
    }

    static ShaderGraph CreateGraph()
    {
        var graph = new ShaderGraph { GraphId = "responsiveness-smoke" };
        graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        Add(graph, "source", "core.constant", 100, 100);
        Add(graph, "insert", "core.hueShift", 300, 100);
        Add(graph, "target", "core.unlitSurface", 500, 100);
        graph.Nodes.Single(n => n.Id == "source").Properties["valueType"] = "color";
        graph.Nodes.Single(n => n.Id == "source").Properties["value"] = new JArray(.2, .4, .8, 1);
        graph.Connections.Add(new GraphConnection { Id = "wire", From = new GraphPortRef { NodeId = "source", PortId = "value" }, To = new GraphPortRef { NodeId = "target", PortId = "albedo" } });
        for (var i = 0; i < 147; i++)
        {
            Add(graph, "loose-" + i, "core.multiply", 40 + (i % 10) * 230, 500 + (i / 10) * 220);
            graph.Connections.Add(new GraphConnection { Id = "extra-" + i, From = new GraphPortRef { NodeId = i == 0 ? "source" : "loose-" + (i-1), PortId = "value" }, To = new GraphPortRef { NodeId = "loose-" + i, PortId = "a" } });
        }
        return graph;
    }

    static void Add(ShaderGraph graph, string id, string operation, float x, float y)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; graph.Nodes.Add(node);
        graph.Layout.Nodes[id] = new GraphNodeLayout { X = x, Y = y };
    }

    static void CheckSocketLookup()
    {
        var sockets = (System.Collections.IEnumerable)Field("sockets");
        var lookup = (System.Collections.IDictionary)Field("socketLookup");
        var count = 0;
        foreach (var item in sockets)
        {
            var type = item.GetType();
            var node = (string)type.GetField("node").GetValue(item);
            var port = (string)type.GetField("port").GetValue(item);
            var output = (bool)type.GetField("output").GetValue(item);
            Require(lookup.Contains(new ValueTuple<string, string, bool>(node, port, output)), "Socket lookup missed " + node + "." + port);
            count++;
        }
        Require(count == lookup.Count, "Socket lookup count diverged from socket list");
    }

    static void CheckDeferredSceneHash()
    {
        Set("sourcePath", System.IO.Path.Combine(Application.dataPath, "ResponsivenessSmoke.nxsg"));
        Set("sceneBuiltHash", null); Set("sceneQueuedHash", null); Set("scenePending", false); Set("sceneHashPending", false);
        Invoke("QueueSceneUpdate");
        Require((bool)Field("scenePending") && (bool)Field("sceneHashPending"), "Scene queue did not defer its hash");
        Require(Field("sceneQueuedHash") == null, "Scene queue computed its hash eagerly");
        Set("sceneDue", 0d); Invoke("UpdateScene");
        Require(!(bool)Field("scenePending") && ((Label)Field("sceneStatus")).text.IndexOf("changes not applied", StringComparison.OrdinalIgnoreCase) >= 0,
            "Deferred scene hash did not preserve Auto scene off behavior");
    }

    static void CheckSelectionAndDiagnostics()
    {
        Invoke("ShowSidebarTab", 0);
        Invoke("SelectNode", "source", false);
        var previewHost = Field("previewHost");
        var library = Field("addNodeLibrary");
        Invoke("SelectNode", "source", false);
        Require(ReferenceEquals(previewHost, Field("previewHost")), "Reselecting the same node rebuilt its inspector");
        Invoke("SelectNode", "target", false);
        Require(ReferenceEquals(library, Field("addNodeLibrary")), "Selecting another node rebuilt the Add Nodes library");

        Set("previewHash", "cached-preview");
        Invoke("OnFocus");
        Require((string)Field("previewHash") == "cached-preview", "Focus discarded an unchanged preview");

        Invoke("QueueDiagnostics");
        Set("diagnosticsDue", 0d);
        Invoke("UpdateDiagnostics");
        Require((bool)Field("diagnosticsPending"), "Hidden Problems tab ran diagnostics");
        Invoke("ShowSidebarTab", 2);
        Set("diagnosticsDue", 0d);
        Invoke("UpdateDiagnostics");
        Require(!(bool)Field("diagnosticsPending"), "Opening Problems did not run diagnostics");
    }

    static void CheckWireVisibility()
    {
        var method = typeof(GraphWindow).GetMethod("WireIntersects", BindingFlags.Static | BindingFlags.NonPublic);
        bool Visible(Rect bounds, Vector2 a, Vector2 b) => (bool)method.Invoke(null, new object[] { bounds, a, b });
        Require(Visible(new Rect(95, -5, 10, 10), new Vector2(100, 0), Vector2.zero), "Visible wire endpoint was culled");
        Require(Visible(new Rect(105, -5, 2, 10), new Vector2(100, 0), Vector2.zero), "Reverse wire bend was culled");
        Require(!Visible(new Rect(500, 500, 10, 10), new Vector2(100, 0), Vector2.zero), "Distant wire was not culled");
    }

    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        if (window != null) { window.DiscardChanges(); window.Close(); }
        EditorApplication.Exit(code);
    }
}
