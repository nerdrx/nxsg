// Run in an isolated Unity project with NXSG installed; never on a user's graph.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class SocketInteractionSmoke
{
    static GraphWindow window;
    static int ticks, phase;
    static string previousClipboard, copiedText;
    static int beforePaste;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static VisualElement Canvas => (VisualElement)Field("canvas");
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static VisualElement Socket(string node, string port, bool output)
    {
        foreach (var s in (IEnumerable)Field("sockets"))
        {
            var t = s.GetType();
            if ((string)t.GetField("node").GetValue(s) == node && (string)t.GetField("port").GetValue(s) == port && (bool)t.GetField("output").GetValue(s) == output)
                return (VisualElement)t.GetField("hit").GetValue(s);
        }
        throw new Exception("Missing socket");
    }
    static void Down(VisualElement socket)
    {
        using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = socket.worldBound.center })) socket.SendEvent(e);
    }
    static void Up(Vector2 position)
    {
        using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = position })) Canvas.SendEvent(e);
        using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = position })) Canvas.SendEvent(e);
    }
    static void ClickMenu(string text)
    {
        var menu = (VisualElement)Field("spawnMenu");
        var button = menu.Children().OfType<Button>().First(b => b.text == text);
        var position = button.worldBound.center;
        Down(button);
        using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = position })) button.SendEvent(e);
    }
    static void Key(KeyCode key)
    {
        using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key, modifiers = EventModifiers.Control })) Canvas.SendEvent(e);
    }
    static void RestoreClipboard()
    {
        var current = EditorGUIUtility.systemCopyBuffer;
        if (previousClipboard != null && (current == copiedText || current == "not nxsg")) EditorGUIUtility.systemCopyBuffer = previousClipboard;
    }
    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.position = new Rect(0, 0, 1380, 820);
        typeof(GraphWindow).GetMethod("NewGraph", Private).Invoke(window, null);
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (++ticks % 45 != 0) return;
        try
        {
            if (phase == 0)
            {
                Graph.Connections.RemoveAll(e => e.To.NodeId == "toon");
                Down(Socket("texture", "color", true));
                Up(Socket("toon", "albedo", false).worldBound.center);
                Require(Graph.Connections.Any(e => e.From.NodeId == "texture" && e.To.NodeId == "toon"), "Drag did not connect");
            }
            else if (phase == 1)
            {
                var count = Graph.Connections.Count;
                Down(Socket("uv0", "uv", true)); Up(Socket("toon", "albedo", false).worldBound.center);
                Require(Graph.Connections.Count == count && !Graph.Connections.Any(e => e.From.NodeId == "uv0" && e.To.NodeId == "toon"), "Type mismatch mutated graph");
                Down(Socket("texture", "color", true)); Up(new Vector2(100, 400));
                Require(Field("spawnMenu") != null && !(bool)Field("wiring") && Graph.Connections.Count == count, "Empty drop did not open menu safely");
                using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape })) Canvas.SendEvent(e);
                Require(Field("spawnMenu") == null, "Escape did not dismiss menu");
            }
            else if (phase == 2)
            {
                var socket = Socket("texture", "color", true);
                Down(socket); Up(socket.worldBound.center);
                Require(Field("pendingNode") != null, "Click output did not remain pending");
                Down(Socket("toon", "albedo", false));
                Require(Field("pendingNode") == null && Graph.Connections.Count(e => e.To.NodeId == "toon") == 1, "Click-click did not replace input");
            }
            else if (phase == 3)
            {
                Down(Socket("texture", "color", true));
                var nodes = (System.Collections.Generic.Dictionary<string, VisualElement>)Field("nodes");
                Require(nodes["texture"].style.borderLeftColor.value.a == 1 && nodes["toon"].style.borderLeftColor.value.a == 0, "Selected-node outline did not follow selection");
                using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape })) Canvas.SendEvent(e);
                Require(Field("pendingNode") == null && !(bool)Field("wiring"), "Escape did not cancel");
            }
            else if (phase == 4)
            {
                var nodes = (System.Collections.Generic.Dictionary<string, VisualElement>)Field("nodes");
                var start = nodes["uv0"].worldBound.min - new Vector2(10, 10);
                var end = nodes["texture"].worldBound.max + new Vector2(5, 5);
                using (var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start })) Canvas.SendEvent(e);
                Up(end);
                var selection = (System.Collections.Generic.List<string>)Field("selection");
                Require(selection.Count == 2 && selection.Contains("uv0") && selection.Contains("texture"), "Box did not select two nodes");
            }
            else if (phase == 5)
            {
                var nodes = (System.Collections.Generic.Dictionary<string, VisualElement>)Field("nodes");
                var title = nodes["uv0"][0];
                var start = title.worldBound.center;
                var beforeUv = Graph.Layout.Nodes["uv0"].X;
                var beforeTexture = Graph.Layout.Nodes["texture"].X;
                Down(title);
                using (var e = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = start + new Vector2(40, 20) })) title.SendEvent(e);
                using (var e = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = start + new Vector2(40, 20) })) title.SendEvent(e);
                Require(Graph.Layout.Nodes["uv0"].X == beforeUv + 40 && Graph.Layout.Nodes["texture"].X == beforeTexture + 40, "Group drag failed");
            }
            else if (phase == 6)
            {
                typeof(GraphWindow).GetMethod("DeleteSelection", Private).Invoke(window, null);
                Require(Graph.Nodes.Count == 2 && Graph.Connections.Count == 1, "Group delete failed");
                Undo.PerformUndo();
            }
            else if (phase == 7)
            {
                Require(Graph.Nodes.Count == 4 && Graph.Connections.Count == 3, "Undo group delete failed");
                Down(Socket("toon", "albedo", false)); Up(Socket("texture", "color", true).worldBound.center);
                Require(Graph.Connections.Count(e => e.To.NodeId == "toon" && e.From.NodeId == "texture") == 1, "Reverse drag failed");
            }
            else if (phase == 8)
            {
                Down(Socket("toon", "albedo", false)); Up(new Vector2(150, 400));
                var menu = (VisualElement)Field("spawnMenu");
                Require(menu != null, "Reverse drag did not open menu");
            }
            else if (phase == 9)
            {
                ClickMenu("Color · value");
            }
            else if (phase == 10)
            {
                Require(Graph.Nodes.Count == 5 && Graph.Connections.Any(e => e.To.NodeId == "toon" && Graph.Nodes.Any(n => n.Id == e.From.NodeId && n.Operation == "core.constant")), "Spawn from input did not connect new Color");
                Undo.PerformUndo();
            }
            else if (phase == 11)
            {
                Require(Graph.Nodes.Count == 4 && Graph.Connections.Any(e => e.To.NodeId == "toon" && e.From.NodeId == "texture"), "Spawn undo failed to restore old connection");
                Require(Field("pendingNode") == null, "Pending endpoint survived Undo: " + Field("pendingNode"));
                Down(Socket("texture", "color", true)); Up(new Vector2(150, 400));
                Require(Field("spawnMenu") != null, "Forward menu missing immediately; pending=" + Field("pendingNode") + " wiring=" + Field("wiring") + " canvas=" + Canvas.worldBound + " status=" + ((Label)Field("status")).text + " toonInput=" + Socket("toon", "albedo", false).worldBound + " textureInput=" + Socket("texture", "uv", false).worldBound);
            }
            else if (phase == 12)
            {
                ClickMenu("Multiply · a");
            }
            else if (phase == 13)
            {
                Require(Graph.Nodes.Count == 5 && Graph.Connections.Any(e => e.From.NodeId == "texture" && e.To.PortId == "a"), "Spawn from output failed");
                typeof(GraphWindow).GetMethod("SelectNode", Private).Invoke(window, new object[] { "uv0", false });
                typeof(GraphWindow).GetMethod("SelectNode", Private).Invoke(window, new object[] { "texture", true });
                previousClipboard = EditorGUIUtility.systemCopyBuffer;
                beforePaste = Graph.Nodes.Count;
                Key(KeyCode.C); copiedText = EditorGUIUtility.systemCopyBuffer;
                Require(copiedText.Contains("core.texture2D"), "Copy shortcut failed");
                Key(KeyCode.V);
                Require(Graph.Nodes.Count == beforePaste + 2, "Paste shortcut failed");
                var ids = (System.Collections.Generic.List<string>)Field("selection");
                Require(ids.Count == 2 && Graph.Connections.Count(e => ids.Contains(e.From.NodeId) && ids.Contains(e.To.NodeId)) == 1, "Paste did not preserve internal connection");
                Require(!Graph.Connections.Any(e => ids.Contains(e.From.NodeId) != ids.Contains(e.To.NodeId)), "Paste copied external connection");
            }
            else if (phase == 14)
            {
                Undo.PerformUndo();
            }
            else if (phase == 15)
            {
                Require(Graph.Nodes.Count == beforePaste, "Undo paste failed");
                typeof(GraphWindow).GetMethod("SelectNode", Private).Invoke(window, new object[] { "texture", false });
                Key(KeyCode.D);
                Require(Graph.Nodes.Count == beforePaste + 1 && EditorGUIUtility.systemCopyBuffer == copiedText, "Duplicate shortcut or clipboard preservation failed");
                var textField = ((VisualElement)Field("inspector")).Q<TextField>();
                Require(textField != null, "Search text field unavailable");
                textField.Focus();
                using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.D, modifiers = EventModifiers.Control })) textField.SendEvent(e);
                Require(Graph.Nodes.Count == beforePaste + 1, "Graph shortcut intercepted text editing");
                Canvas.Focus();
                EditorGUIUtility.systemCopyBuffer = "not nxsg";
                Key(KeyCode.V);
                Require(Graph.Nodes.Count == beforePaste + 1, "Invalid clipboard mutated graph");
                RestoreClipboard();
            }
            else
            {
                Debug.Log("NXSG SOCKET CHECK PASSED: wires both directions, connected spawn, selection, group movement, undo, Ctrl+C/V/D, clipboard preservation, invalid paste rejection");
                window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
                EditorApplication.update -= Tick;
            }
            phase++;
        }
        catch (Exception e) { RestoreClipboard(); Debug.LogException(e); EditorApplication.update -= Tick; window.DiscardChanges(); window.Close(); EditorApplication.Exit(1); }
    }
}
