using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

// Run in the isolated Unity fixture with -executeMethod GridDragSmoke.Run.
public static class GridDragSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string GridPref = "NXSG.GraphWindow.GridSnapping";
    static GraphWindow window;
    static int phase;
    static double nextCheck;
    static bool oldGridPref;
    static string clickBefore, nodeBefore, patternBefore, frameBefore;
    static Vector2 patternFirstAnchor;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static GraphSession Session => (GraphSession)Field("session");
    static VisualElement Canvas => (VisualElement)Field("canvas");
    static Dictionary<string, VisualElement> Nodes => (Dictionary<string, VisualElement>)Field("nodes");
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static Vector2 Position(string id) => new Vector2((float)Graph.Layout.Nodes[id].X, (float)Graph.Layout.Nodes[id].Y);
    static void Near(float actual, float expected, string message) => Require(Mathf.Abs(actual - expected) < .1f, message + ": " + actual + " != " + expected);

    public static void Run()
    {
        try
        {
            oldGridPref = EditorPrefs.GetBool(GridPref, false);
            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 1280, 800); window.CreateGUI();
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            Invoke("NewGraph"); BuildFixture();
            EditorPrefs.SetBool(GridPref, true);
            typeof(GraphWindow).GetField("zoom", Private).SetValue(window, 1.5f); Invoke("TransformCanvas");
            phase = 0; nextCheck = EditorApplication.timeSinceStartup + .8;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void BuildFixture()
    {
        Graph.Nodes.Clear(); Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        AddNode("move", "core.value", 42, 350);
        AddNode("pattern-a", "core.value", 25, 20); AddNode("pattern-b", "core.add", 90, 55);
        AddNode("frame-a", "core.value", 150, 80); AddNode("frame-b", "core.add", 240, 130);
        var pattern = GraphGroups.Add(Graph, new[] { "pattern-a", "pattern-b" }, "Collapsed smoke"); pattern["collapsed"] = true;
        var frame = GraphGroups.Add(Graph, new[] { "frame-a", "frame-b" }, "Frame smoke"); frame["frame"] = true; frame["collapsed"] = false;
        Session.json = GraphJson.Serialize(Graph, true); EditorUtility.ClearDirty(Session); Undo.ClearUndo(Session);
        Invoke("Rebuild");
    }

    static void AddNode(string id, string operation, float x, float y)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; Graph.Nodes.Add(node);
        Graph.Layout.Nodes[id] = new GraphNodeLayout { X = x, Y = y };
    }

    static VisualElement Grip(string name)
    {
        var card = ((VisualElement)Field("layer")).Query<VisualElement>(name: name).First();
        return card.Children().OfType<Label>().First(label => label.text.StartsWith("⋮⋮"));
    }

    static void Drag(VisualElement target, Vector2 screenDelta, bool alt = false)
    {
        var start = target.worldBound.center;
        var modifiers = alt ? EventModifiers.Alt : EventModifiers.None;
        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start }))
        {
            target.SendEvent(e);
            Debug.Log("GridDrag down: target=" + target.name + " pointer=" + e.pointerId + " captured=" + target.HasPointerCapture(e.pointerId) + " selected=" + Field("selected") + " move=" + Position("move"));
        }
        using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = start + screenDelta, modifiers = modifiers }))
        {
            Canvas.SendEvent(e);
            Debug.Log("GridDrag move: target=" + target.name + " pointer=" + e.pointerId + " captured=" + target.HasPointerCapture(e.pointerId) + " selected=" + Field("selected") + " move=" + Position("move"));
        }
        using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = start + screenDelta, modifiers = modifiers })) Canvas.SendEvent(e);
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + .65;
        try
        {
            if (phase == 0)
            {
                clickBefore = Session.json; EditorUtility.ClearDirty(Session);
                Drag(Grip("pattern-card"), Vector2.zero);
                phase = 1;
            }
            else if (phase == 1)
            {
                Require(Session.json == clickBefore && !EditorUtility.IsDirty(Session), "Pattern click wrote graph or dirtied session");
                nodeBefore = Session.json; Drag(Nodes["move"][0], new Vector2(30, 30)); phase = 2;
            }
            else if (phase == 2)
            {
                Near(Position("move").x, 72, "Zoomed node drag did not snap X to 24-unit grid");
                Near(Position("move").y, 360, "Zoomed node drag did not snap Y to 24-unit grid");
                Undo.PerformUndo(); phase = 3;
            }
            else if (phase == 3)
            {
                Require(Session.json == nodeBefore, "One Undo did not restore snapped node drag");
                Drag(Nodes["move"][0], new Vector2(30, 30), true); phase = 4;
            }
            else if (phase == 4)
            {
                Near(Position("move").x, 62, "Alt did not bypass snap at zoom 1.5");
                Near(Position("move").y, 370, "Alt bypass changed Y delta");
                Undo.PerformUndo(); phase = 5;
            }
            else if (phase == 5)
            {
                Require(Session.json == nodeBefore, "One Undo did not restore Alt node drag");
                patternBefore = Session.json;
                Drag(Grip("pattern-card"), new Vector2(30, 30)); phase = 6;
            }
            else if (phase == 6)
            {
                patternFirstAnchor = Position("pattern-a");
                Near(patternFirstAnchor.x, 48, "First collapsed Pattern drag did not snap");
                Near(LocalLeft(((VisualElement)Field("layer")).Query<VisualElement>(name: "pattern-card").First()), patternFirstAnchor.x, "Collapsed Pattern card did not follow first drag");
                Drag(Grip("pattern-card"), new Vector2(30, 30)); phase = 7;
            }
            else if (phase == 7)
            {
                var anchor = Position("pattern-a");
                Near(anchor.x, 72, "Second collapsed Pattern drag used stale anchor");
                Near(LocalLeft(((VisualElement)Field("layer")).Query<VisualElement>(name: "pattern-card").First()), anchor.x, "Collapsed Pattern card jumped after second drag");
                var offset = Position("pattern-b") - anchor;
                Require(Vector2.Distance(offset, new Vector2(65, 35)) < .1f, "Pattern members changed relative positions");
                Undo.PerformUndo(); phase = 8;
            }
            else if (phase == 8)
            {
                Near(Position("pattern-a").x, patternFirstAnchor.x, "One Undo did not revert exactly second Pattern drag");
                Undo.PerformUndo(); phase = 9;
            }
            else if (phase == 9)
            {
                Require(Session.json == patternBefore, "Second Undo did not restore original Pattern positions");
                frameBefore = Session.json; Drag(Grip("frame-card"), new Vector2(30, 30)); phase = 10;
            }
            else if (phase == 10)
            {
                Near(Position("frame-a").x, 168, "Frame drag did not snap first member X");
                Near(Position("frame-a").y, 96, "Frame drag did not snap first member Y");
                Near(LocalLeft(Nodes["frame-a"]), Position("frame-a").x, "Frame drag did not move visible first node");
                Near(LocalLeft(Nodes["frame-b"]), Position("frame-b").x, "Frame drag did not move visible second node");
                Undo.PerformUndo(); phase = 11;
            }
            else
            {
                Require(Session.json == frameBefore, "One Undo did not restore Frame drag");
                Debug.Log("NXSG GRID DRAG SMOKE PASSED: click, zoomed snap, Alt bypass, repeated collapsed Pattern drags, frame visuals, single-step Undo");
                Finish(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    static float LocalLeft(VisualElement element) => element.style.left.value.value;

    static void Finish(int code)
    {
        EditorApplication.update -= Tick; EditorPrefs.SetBool(GridPref, oldGridPref);
        if (window != null) { window.DiscardChanges(); window.Close(); }
        EditorApplication.Exit(code);
    }

    static void Fail(Exception e) { Debug.LogException(e); Finish(1); }
}
