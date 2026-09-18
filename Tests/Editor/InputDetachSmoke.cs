using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class InputDetachSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int ticks, phase;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, null);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.position = new Rect(0, 0, 1300, 800);
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (++ticks % 30 != 0) return;
        try
        {
            if (phase == 0)
            {
                Invoke("NewGraph");
                Graph.Nodes.Add(new GraphNode { Id = "extra", Operation = "core.toonSurface" });
                Graph.Connections.Add(new GraphConnection { Id = "fanout", From = new GraphPortRef { NodeId = "texture", PortId = "color" }, To = new GraphPortRef { NodeId = "extra", PortId = "albedo" } });
                var session = Field("session");
                session.GetType().GetField("json").SetValue(session, GraphJson.Serialize(Graph, true));
                Invoke("Rebuild");
            }
            else if (phase == 1)
            {
                VisualElement socket = null;
                foreach (var item in (IEnumerable)Field("sockets"))
                {
                    var type = item.GetType();
                    if ((string)type.GetField("node").GetValue(item) == "toon" && (string)type.GetField("port").GetValue(item) == "albedo") socket = (VisualElement)type.GetField("hit").GetValue(item);
                }
                Require(socket != null, "input socket missing");
                var canvas = (VisualElement)Field("canvas");
                var position = socket.worldBound.center;
                var before = Graph.Connections.Count;
                using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = position })) socket.SendEvent(e);
                Require(Graph.Connections.Count == before, "click detached connection");
                using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position + new Vector2(25, 0) })) canvas.SendEvent(e);
                Require(!Graph.Connections.Any(e => e.To.NodeId == "toon") && Graph.Connections.Count == before - 1, "drag did not detach input");
                Require(Graph.Connections.Any(e => e.Id == "fanout"), "drag removed fanout");
                Require((string)Field("pendingNode") == "texture" && (string)Field("pendingPort") == "color" && (bool)Field("pendingOutput") && (bool)Field("wiring"), "loose wire lost source");
                Undo.PerformUndo();
            }
            else
            {
                Require(Graph.Connections.Any(e => e.To.NodeId == "toon" && e.From.NodeId == "texture"), "Undo did not restore wire");
                Require(Graph.Connections.Any(e => e.Id == "fanout"), "Undo lost fanout");
                Debug.Log("NXSG INPUT DETACH SMOKE PASSED");
                Finish(0); return;
            }
            phase++;
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        window.DiscardChanges(); window.Close(); EditorApplication.Exit(code);
    }
}
