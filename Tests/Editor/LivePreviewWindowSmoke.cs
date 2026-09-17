using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Run in hidden graphics-enabled Unity with -executeMethod LivePreviewWindowSmoke.Run.
public static class LivePreviewWindowSmoke
{
    static GraphWindow window;
    static int phase;
    static double next;
    static Material goodPreview;

    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.Focus(); Invoke("NewGraph");
        Set("graph", CreateGraph(Color.red));
        Set("sourcePath", null);
        ((GraphSession)Get("session")).json = GraphJson.Serialize((ShaderGraph)Get("graph"), true);
        Invoke("Rebuild");
        Invoke("QueueLivePreview");
        phase = 0; next = EditorApplication.timeSinceStartup + .55;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            Invoke("UpdateLivePreview");
            if (phase == 0)
            {
                goodPreview = (Material)Get("preview");
                Check(goodPreview != null, "initial live preview missing");
                Check(Get("sourcePath") == null && window.hasUnsavedChanges, "initial editor state changed");
                Set("graph", new ShaderGraph { GraphId = "invalid-live-preview" });
                Invoke("QueueLivePreview"); Set("previewDue", EditorApplication.timeSinceStartup - 1); phase = 1;
                next = EditorApplication.timeSinceStartup + .05; return;
            }
            if (phase == 1)
            {
                Invoke("UpdateLivePreview");
                Check((Material)Get("preview") == goodPreview, "invalid graph replaced last good preview");
                Check(((string)Get("previewMessage")).Contains("Preview needs attention"), "preview failure status missing");
                Set("graph", CreateGraph(Color.green)); Invoke("QueueLivePreview");
                Set("previewDue", EditorApplication.timeSinceStartup - 1); phase = 2;
                next = EditorApplication.timeSinceStartup + .05; return;
            }
            if (phase == 2)
            {
                Invoke("UpdateLivePreview");
                Check((Material)Get("preview") != null && (Material)Get("preview") != goodPreview, "valid graph did not refresh preview");
                Set("livePreview", false); Set("graph", new ShaderGraph { GraphId = "paused-invalid" });
                Invoke("QueueLivePreview"); Invoke("UpdateLivePreview");
                Check((Material)Get("preview") != null, "paused preview disappeared");
                window.DiscardChanges(); window.Close();
                Check(Get("preview") == null, "closing window did not dispose preview");
                EditorApplication.update -= Tick;
                Debug.Log("NXSG LIVE PREVIEW WINDOW SMOKE PASSED");
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception)
        {
            EditorApplication.update -= Tick;
            if (window != null) { window.DiscardChanges(); window.Close(); }
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static object Get(string name) { return typeof(GraphWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
    static void Set(string name, object value) { typeof(GraphWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value); }
    static void Invoke(string name) { typeof(GraphWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null); }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    static ShaderGraph CreateGraph(Color color)
    {
        var graph = new ShaderGraph { GraphId = color == Color.red ? "window-red" : "window-green" };
        graph.Nodes.Add(new GraphNode { Id = "albedo", Operation = "core.constant", Properties = ColorProperties(Color.black) });
        graph.Nodes.Add(new GraphNode { Id = "emission", Operation = "core.constant", Properties = ColorProperties(color) });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "albedo", "value", "toon", "albedo", "albedo-toon");
        Connect(graph, "emission", "value", "toon", "emission", "emission-toon");
        Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        return graph;
    }

    static JObject ColorProperties(Color color) { return new JObject { ["valueType"] = "color", ["value"] = new JArray(color.r, color.g, color.b, color.a) }; }
    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}
