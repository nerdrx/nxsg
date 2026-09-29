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
using Debug = UnityEngine.Debug;

// Run in the isolated Unity project with -executeMethod GraphOrganizeSmoke.Run.
public static class GraphOrganizeSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    const string ScreenshotPath = "/tmp/nxsg-auto-organize.png";
    static GraphWindow window;
    static int phase;
    static double nextCheck;
    static string beforeOrganize, afterOrganize, beforeSelection;
    static Dictionary<string, Vector2> positionsBeforeSelection;
    static bool screenshotRequested;
    static bool screenshotStarted;
    static double screenshotDeadline;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static GraphSession Session => (GraphSession)Field("session");
    static Dictionary<string, Vector2> Positions => Graph.Nodes.ToDictionary(n => n.Id, n => (Vector2)InvokeResult("Position", n.Id));
    static object InvokeResult(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        try
        {
            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 1280, 800); window.CreateGUI();
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            Invoke("NewGraph");
            BuildFixture();
            phase = 0; nextCheck = EditorApplication.timeSinceStartup + .8;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void BuildFixture()
    {
        Graph.Nodes.Clear(); Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        AddNode("src-a", "core.value", 12, 20);
        AddNode("src-b", "core.value", 12, 20);
        AddNode("src-c", "core.value", 12, 20);
        AddNode("branch", "core.mix", 12, 20);
        AddNode("surface", "core.toonSurface", 12, 20);
        AddNode("output", "core.output", 12, 20);
        AddNode("island-a", "core.value", 12, 20);
        AddNode("island-b", "core.add", 12, 20);
        AddNode("collapsed-a", "core.value", 12, 20);
        AddNode("collapsed-b", "core.add", 12, 20);
        AddNode("frame-a", "core.colorRamp", 12, 20);
        AddNode("frame-b", "core.add", 12, 20);
        AddEdge("a-branch", "src-a", "value", "branch", "a");
        AddEdge("b-branch", "src-b", "value", "branch", "b");
        AddEdge("c-branch", "src-c", "value", "branch", "factor");
        AddEdge("branch-surface", "branch", "value", "surface", "albedo");
        AddEdge("surface-output", "surface", "surface", "output", "surface");
        AddEdge("island-edge", "island-a", "value", "island-b", "a");
        AddEdge("collapsed-edge", "collapsed-a", "value", "collapsed-b", "a");
        AddEdge("frame-edge", "frame-a", "color", "frame-b", "a");

        var collapsed = GraphGroups.Add(Graph, new[] { "collapsed-a", "collapsed-b" }, "Collapsed island");
        collapsed["collapsed"] = true;
        var frame = GraphGroups.Add(Graph, new[] { "frame-a", "frame-b" }, "Expanded frame");
        frame["frame"] = true; frame["collapsed"] = false;
        var selection = (List<string>)Field("selection");
        selection.Clear(); selection.AddRange(new[] { "src-a", "src-b" });
        typeof(GraphWindow).GetField("selected", Private).SetValue(window, "src-b");
        Session.json = GraphJson.Serialize(Graph, true);
        Undo.ClearUndo(Session);
        Invoke("Rebuild");
    }

    static void AddNode(string id, string operation, float x, float y)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; Graph.Nodes.Add(node);
        Graph.Layout.Nodes[id] = new GraphNodeLayout { X = x, Y = y };
    }

    static void AddEdge(string id, string from, string fromPort, string to, string toPort)
    {
        Graph.Connections.Add(new GraphConnection { Id = id,
            From = new GraphPortRef { NodeId = from, PortId = fromPort },
            To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + .7;
        try
        {
            if (phase == 0)
            {
                CheckLayoutCore(); CheckGridSnap(); CheckActualNodeSizes();
                beforeOrganize = GraphJson.Serialize(Graph, true);
                var semantic = GraphJson.ComputeSemanticHash(Graph);
                var edges = EdgeSignature(); var groups = GroupSignature(); var selection = Selection();
                var collapsedDelta = GetPosition("collapsed-b") - GetPosition("collapsed-a");
                Invoke("AutoOrganize", false);
                afterOrganize = GraphJson.Serialize(Graph, true);
                Require(afterOrganize != beforeOrganize, "Full graph organization made no layout change");
                Require(GraphJson.ComputeSemanticHash(Graph) == semantic, "Auto-organize changed the semantic hash");
                Require(EdgeSignature() == edges, "Auto-organize changed graph edges");
                Require(GroupSignature() == groups, "Auto-organize changed Pattern or frame metadata");
                Require(Selection().SequenceEqual(selection), "Auto-organize changed the selection");
                var collapsedAfter = GetPosition("collapsed-b") - GetPosition("collapsed-a");
                Require(Vector2.Distance(collapsedAfter, collapsedDelta) < .01f, "Collapsed Pattern members did not move as one block");
                var frameBounds = NodeBounds("frame-a");
                var frameB = NodeBounds("frame-b");
                Require(frameB.xMin >= frameBounds.xMax - 1, "Expanded frame members were not internally organized left-to-right");
                phase = 1;
            }
            else if (phase == 1)
            {
                if (!screenshotStarted)
                {
                    CaptureScreenshot();
                    screenshotStarted = true;
                    screenshotDeadline = EditorApplication.timeSinceStartup + 5;
                }
                if (screenshotRequested && !System.IO.File.Exists(ScreenshotPath))
                {
                    Require(EditorApplication.timeSinceStartup <= screenshotDeadline, "Gamescope screenshot was requested but file was not written within 5 seconds");
                    nextCheck = EditorApplication.timeSinceStartup + .1;
                    return;
                }
                Undo.PerformUndo(); phase = 2;
            }
            else if (phase == 2)
            {
                Require(GraphJson.Serialize(Graph, true) == beforeOrganize, "One Undo did not restore the original full layout");
                Require(Selection().SequenceEqual(new[] { "src-a", "src-b" }), "Undo changed selection");
                Undo.PerformRedo(); phase = 3;
            }
            else if (phase == 3)
            {
                Require(GraphJson.Serialize(Graph, true) == afterOrganize, "One Redo did not restore organized layout");
                CheckSelectedOnly(); phase = 4;
            }
            else if (phase == 4)
            {
                Require(Selection().SequenceEqual(new[] { "src-a", "src-b" }), "Selected-only organization changed selection");
                var now = Positions;
                foreach (var pair in positionsBeforeSelection)
                    if (pair.Key != "src-a" && pair.Key != "src-b")
                        Require(Vector2.Distance(now[pair.Key], pair.Value) < .01f, "Selected-only organization moved unselected node " + pair.Key);
                Require(Vector2.Distance(now["src-a"], positionsBeforeSelection["src-a"]) > 1
                    || Vector2.Distance(now["src-b"], positionsBeforeSelection["src-b"]) > 1,
                    "Selected-only organization did not arrange the overlapping selection");
                Require(GraphJson.ComputeSemanticHash(Graph) == GraphJson.ComputeSemanticHash(GraphJson.Parse(beforeSelection)),
                    "Selected-only organization changed graph semantics");
                Require(EdgeSignature() == EdgeSignature(GraphJson.Parse(beforeSelection)), "Selected-only organization changed graph edges");
                Undo.PerformUndo(); phase = 5;
            }
            else
            {
                Require(GraphJson.Serialize(Graph, true) == beforeSelection, "Selected-only Undo did not restore the previous layout");
                Undo.PerformRedo();
                Debug.Log("NXSG GRAPH ORGANIZE AND GRID SMOKE PASSED; screenshot=" + (screenshotRequested ? ScreenshotPath : "Gamescope unavailable"));
                Finish(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    static void CheckSelectedOnly()
    {
        var selection = (List<string>)Field("selection"); selection.Clear(); selection.AddRange(new[] { "src-a", "src-b" });
        SetPosition("src-a", new Vector2(300, 300)); SetPosition("src-b", new Vector2(300, 300));
        Session.json = GraphJson.Serialize(Graph, true); Undo.ClearUndo(Session);
        beforeSelection = Session.json;
        positionsBeforeSelection = Positions;
        var semantic = GraphJson.ComputeSemanticHash(Graph); var edges = EdgeSignature(); var groups = GroupSignature();
        Invoke("Rebuild"); Invoke("AutoOrganize", true);
        Require(GraphJson.ComputeSemanticHash(Graph) == semantic, "Selected-only organization changed semantic hash");
        Require(EdgeSignature() == edges && GroupSignature() == groups, "Selected-only organization changed edges or groups");
    }

    static void CheckActualNodeSizes()
    {
        var views = (Dictionary<string, VisualElement>)Field("nodes");
        var heights = views.Values.Select(v => v.layout.height).Where(h => h > 0).Distinct().ToArray();
        Require(views.Count == Graph.Nodes.Count && heights.Length >= 2, "Fixture did not produce measured node views with varied heights");
    }

    static void CheckLayoutCore()
    {
        var bounds = new Dictionary<string, Rect>
        {
            ["a"] = new Rect(400, 200, 160, 80), ["b"] = new Rect(30, 80, 175, 220),
            ["c"] = new Rect(200, 120, 190, 110), ["d"] = new Rect(40, 240, 150, 70),
            ["e"] = new Rect(620, 160, 140, 130), ["f"] = new Rect(260, 330, 180, 90),
            ["g"] = new Rect(120, 460, 170, 100), ["h"] = new Rect(480, 450, 150, 150)
        };
        var edges = new List<(string from, string to)> { ("b", "a"), ("d", "a"), ("a", "c"), ("b", "c"), ("g", "h"), ("h", "g") };
        var layoutType = typeof(GraphWindow).Assembly.GetType("NXSG.Editor.GraphAutoLayout");
        var arrange = layoutType?.GetMethod("Arrange", StaticPrivate | BindingFlags.Public);
        Require(arrange != null, "GraphAutoLayout.Arrange is unavailable");
        var timer = Stopwatch.StartNew();
        var first = (Dictionary<string, Vector2>)arrange.Invoke(null, new object[] { bounds, edges });
        timer.Stop();
        var second = (Dictionary<string, Vector2>)arrange.Invoke(null, new object[] { bounds, edges });
        var laidOutBounds = bounds.ToDictionary(p => p.Key, p => new Rect(first[p.Key], p.Value.size));
        var third = (Dictionary<string, Vector2>)arrange.Invoke(null, new object[] { laidOutBounds, edges });
        Require(timer.ElapsedMilliseconds < 1000, "Cyclic graph layout did not terminate promptly");
        Require(first.Count == bounds.Count && first.All(p => p.Value == second[p.Key]), "Repeated layout was not deterministic");
        Require(first.All(p => Vector2.Distance(p.Value, third[p.Key]) < .01f), "Repeated layout was not idempotent");
        foreach (var edge in edges.Where(e => !(e.from == "g" || e.from == "h")))
            Require(first[edge.to].x >= first[edge.from].x + bounds[edge.from].width - .01f, "DAG edge does not point left-to-right: " + edge.from + " → " + edge.to);
        var ids = bounds.Keys.ToArray();
        for (var i = 0; i < ids.Length; i++) for (var j = i + 1; j < ids.Length; j++)
            Require(!new Rect(first[ids[i]], bounds[ids[i]].size).Overlaps(new Rect(first[ids[j]], bounds[ids[j]].size)),
                "Auto-layout overlaps measured node rectangles: " + ids[i] + " and " + ids[j]);
        CheckLongChain(arrange);
    }

    static void CheckLongChain(MethodInfo arrange)
    {
        const int count = 500;
        var bounds = new Dictionary<string, Rect>(count);
        var edges = new List<(string from, string to)>(count - 1);
        for (var i = 0; i < count; i++)
        {
            var id = "chain-" + i.ToString("D4");
            bounds[id] = new Rect(0, 0, 180, 90);
            if (i > 0) edges.Add(("chain-" + (i - 1).ToString("D4"), id));
        }
        var positions = (Dictionary<string, Vector2>)arrange.Invoke(null, new object[] { bounds, edges });
        var last = positions["chain-0499"];
        Require(last.x > 100000, "500-node chain did not exercise layout positions beyond 100,000 units");
        var original = GetPosition("output");
        try
        {
            SetPosition("output", last);
            Require(Vector2.Distance(GetPosition("output"), last) < .01f,
                "GraphWindow Position clamped a valid long-chain layout position");
        }
        finally { SetPosition("output", original); }
    }

    static void CheckGridSnap()
    {
        var method = typeof(GraphWindow).GetMethod("SnapDragDelta", StaticPrivate | BindingFlags.Public);
        Require(method != null, "Grid snap helper is unavailable");
        var anchor = new Vector2(13, 5); var delta = new Vector2(30, 32);
        var snapped = (Vector2)method.Invoke(null, new object[] { anchor, delta, true, false });
        var final = anchor + snapped;
        Require(Mathf.Abs(final.x / 24 - Mathf.Round(final.x / 24)) < .001f && Mathf.Abs(final.y / 24 - Mathf.Round(final.y / 24)) < .001f,
            "Enabled grid snapping did not land on 24-unit coordinates");
        Require((Vector2)method.Invoke(null, new object[] { anchor, delta, false, false }) == delta, "Disabled grid snapping changed drag delta");
        Require((Vector2)method.Invoke(null, new object[] { anchor, delta, true, true }) == delta, "Alt bypass did not preserve free drag delta");
        var group = new Dictionary<string, Vector2> { ["left"] = new Vector2(13, 5), ["right"] = new Vector2(82, 47) };
        var relative = group["right"] - group["left"];
        var firstMove = (Vector2)method.Invoke(null, new object[] { group["left"], new Vector2(20, 12), true, false });
        foreach (var id in group.Keys.ToArray()) group[id] += firstMove;
        var anchorAfterMove = group.Values.Aggregate(Vector2.Min);
        var secondMove = (Vector2)method.Invoke(null, new object[] { anchorAfterMove, new Vector2(31, 28), true, false });
        foreach (var id in group.Keys.ToArray()) group[id] += secondMove;
        Require(Vector2.Distance(group["right"] - group["left"], relative) < .01f, "Consecutive snapped group moves changed relative offsets");
        Require(Mathf.Abs(group["left"].x / 24 - Mathf.Round(group["left"].x / 24)) < .001f
            && Mathf.Abs(group["left"].y / 24 - Mathf.Round(group["left"].y / 24)) < .001f,
            "Recomputed group anchor did not preserve grid alignment after a second drag");
    }

    static string EdgeSignature() => EdgeSignature(Graph);
    static string EdgeSignature(ShaderGraph graph) => string.Join("|", graph.Connections.OrderBy(e => e.Id, StringComparer.Ordinal)
        .Select(e => e.Id + ":" + e.From.NodeId + "/" + e.From.PortId + ">" + e.To.NodeId + "/" + e.To.PortId));
    static string GroupSignature() => string.Join("|", GraphGroups.All(Graph).Select(g => g.ToString(Newtonsoft.Json.Formatting.None)).OrderBy(s => s, StringComparer.Ordinal));
    static List<string> Selection() => ((List<string>)Field("selection")).ToList();
    static Vector2 GetPosition(string id) => (Vector2)InvokeResult("Position", id);
    static void SetPosition(string id, Vector2 position) => Invoke("SetPosition", id, position);
    static Rect NodeBounds(string id)
    {
        var views = (Dictionary<string, VisualElement>)Field("nodes");
        var node = views[id];
        var box = new Vector2(node.layout.width > 0 ? node.layout.width : 175, node.layout.height > 0 ? node.layout.height : 100);
        return new Rect(GetPosition(id), box);
    }

    static void CaptureScreenshot()
    {
        screenshotRequested = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"));
        if (!screenshotRequested) return;
        if (System.IO.File.Exists(ScreenshotPath)) System.IO.File.Delete(ScreenshotPath);
        using (var process = Process.Start(new ProcessStartInfo("gamescopectl", "screenshot " + ScreenshotPath) { UseShellExecute = false }))
            Require(process != null && process.WaitForExit(5000) && process.ExitCode == 0, "Gamescope screenshot request failed");
    }

    static void Fail(Exception e) { Debug.LogException(e); Finish(1); }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        if (window != null) UnityEngine.Object.DestroyImmediate(window);
        EditorApplication.Exit(code);
    }
}
