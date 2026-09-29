using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

// Run in the isolated Unity project with -executeMethod BranchOrganizeSmoke.Run.
// The graph is supplied at runtime through NXSG_ORGANIZE_INPUT; user data is never embedded here.
public static class BranchOrganizeSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string OriginalCapture = "/tmp/nxsg-organize-original.png";
    const string OrganizedCapture = "/tmp/nxsg-organize-branches.png";
    static GraphWindow window;
    static int phase;
    static double nextCheck, captureDeadline;
    static string before, after;
    static bool captureRequested;
    static string capturePath;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static object Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static GraphSession Session => (GraphSession)Field("session");
    static Dictionary<string, Vector2> Positions => Graph.Nodes.ToDictionary(n => n.Id, n => (Vector2)Invoke("Position", n.Id));

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        try
        {
            var input = Environment.GetEnvironmentVariable("NXSG_ORGANIZE_INPUT");
            Require(!string.IsNullOrWhiteSpace(input), "Set NXSG_ORGANIZE_INPUT to the supplied .nxsg graph path.");
            Require(File.Exists(input), "NXSG_ORGANIZE_INPUT does not exist: " + input);
            var parsed = GraphJson.Parse(File.ReadAllText(input));
            Require(parsed.Nodes.Count == 44 && parsed.Connections.Count == 50,
                "Expected the supplied 44-node, 50-connection graph; got " + parsed.Nodes.Count + " nodes and " + parsed.Connections.Count + " connections.");

            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 1920, 1080); window.CreateGUI();
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            Invoke("NewGraph");
            typeof(GraphWindow).GetField("graph", Private).SetValue(window, parsed);
            Session.json = GraphJson.Serialize(parsed, true);
            Undo.ClearUndo(Session);
            Invoke("Rebuild");
            phase = 0; nextCheck = EditorApplication.timeSinceStartup + 1;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + .8;
        try
        {
            if (phase == 0)
            {
                CheckRenderedGraph();
                Invoke("FrameNodes", false);
                phase = 6;
                nextCheck = EditorApplication.timeSinceStartup + .8;
            }
            else if (phase == 6)
            {
                CheckRenderedGraph();
                before = GraphJson.Serialize(Graph, true);
                LogMetrics("before");
                BeginCapture(OriginalCapture);
                phase = 1;
            }
            else if (phase == 1)
            {
                if (!FinishCapture()) return;
                Invoke("AutoOrganize", false);
                after = GraphJson.Serialize(Graph, true);
                phase = 2;
            }
            else if (phase == 2)
            {
                CheckPreserved(before, after);
                CheckRenderedGraph();
                CheckNoOverlaps();
                CheckOrganizedFrameHeaders();
                LogMetrics("after");
                SaveOrganizedSource();
                BeginCapture(OrganizedCapture);
                phase = 7;
            }
            else if (phase == 7)
            {
                if (!FinishCapture()) return;
                var firstGroups = GroupSignature(Graph);
                var organizedPositions = Positions;
                Invoke("AutoOrganize", false);
                var repeated = Positions;
                foreach (var pair in organizedPositions)
                    Require(Vector2.Distance(pair.Value, repeated[pair.Key]) < .01f,
                        "Repeated organize changed position for node " + pair.Key + ": " + pair.Value + " -> " + repeated[pair.Key]);
                Require(firstGroups == GroupSignature(Graph), "Repeated organize changed generated or preserved group metadata.");
                after = GraphJson.Serialize(Graph, true);
                phase = 3;
            }
            else if (phase == 3)
            {
                if (!FinishCapture()) return;
                Undo.PerformUndo(); phase = 4;
            }
            else if (phase == 4)
            {
                Require(GraphJson.Serialize(Graph, true) == before, "One Undo did not restore the original graph and layout.");
                Undo.PerformRedo(); phase = 5;
            }
            else
            {
                Require(GraphJson.Serialize(Graph, true) == after, "One Redo did not restore the organized graph and layout.");
                Debug.Log("NXSG USER GRAPH ORGANIZE SMOKE PASSED; original=" + CaptureStatus(OriginalCapture) + "; organized=" + CaptureStatus(OrganizedCapture));
                Finish(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    static void CheckPreserved(string original, string organized)
    {
        var first = JObject.Parse(original); var second = JObject.Parse(organized);
        var originalGraph = GraphJson.Parse(original);
        Require(GraphJson.ComputeSemanticHash(Graph) == GraphJson.ComputeSemanticHash(originalGraph), "Auto-organize changed graph semantics.");
        Require(JToken.DeepEquals(first["nodes"], second["nodes"]), "Auto-organize changed node IDs, operations, versions, or properties.");
        Require(JToken.DeepEquals(first["connections"], second["connections"]), "Auto-organize changed connections.");
        var newGroups = GraphGroups.All(Graph).ToDictionary(g => (string)g["id"], g => g);
        foreach (var group in GraphGroups.All(originalGraph))
        {
            var id = (string)group["id"];
            var kept = id != null && newGroups.TryGetValue(id, out var byId) && JToken.DeepEquals(group, byId)
                || id == null && GraphGroups.All(Graph).Any(candidate => JToken.DeepEquals(group, candidate));
            Require(kept, "Auto-organize lost or changed existing group metadata " + (id ?? "(no id)") + ".");
        }
    }

    static void CheckRenderedGraph()
    {
        var views = (Dictionary<string, VisualElement>)Field("nodes");
        var ids = new HashSet<string>(Graph.Nodes.Select(n => n.Id), StringComparer.Ordinal);
        Require(views.Count == ids.Count && ids.All(views.ContainsKey), "A graph node is missing from the rendered graph window.");
        Require(views.Values.Where(v => v.resolvedStyle.display != DisplayStyle.None).All(v => v.layout.width > 0 && v.layout.height > 0),
            "Node layout has not settled yet.");
        var layer = (VisualElement)Field("layer");
        var renderedKeys = new HashSet<string>(layer.Children().Where(e => e.name == "frame-card" && e.userData is JObject)
            .Select(e => (string)((JObject)e.userData)["organizeKey"]).Where(k => !string.IsNullOrEmpty(k)), StringComparer.Ordinal);
        foreach (var group in GraphGroups.All(Graph).Where(g => g["organizeKey"]?.Type == JTokenType.String))
            Require(renderedKeys.Contains((string)group["organizeKey"]), "An inferred group frame is missing from the graph window: " + group["organizeKey"]);
    }

    static void CheckOrganizedFrameHeaders()
    {
        var layer = (VisualElement)Field("layer");
        var headers = layer.Children().Where(e => e.name == "frame-card" && e.userData is JObject group &&
            group["organizeKey"]?.Type == JTokenType.String).Select(card =>
        {
            var header = card.Children().FirstOrDefault();
            Require(header != null && card.layout.width > 0 && header.layout.width > 0 && header.layout.height > 0,
                "An inferred frame header has no measured bounds.");
            return new Rect(card.layout.position + header.layout.position, header.layout.size);
        }).ToArray();
        for (var i = 0; i < headers.Length; i++)
        for (var j = i + 1; j < headers.Length; j++)
            Require(!headers[i].Overlaps(headers[j]), "Inferred group frame headers overlap.");
    }

    static void CheckNoOverlaps()
    {
        var views = (Dictionary<string, VisualElement>)Field("nodes");
        var positions = Positions;
        var visible = Graph.Nodes.Where(n => views[n.Id].resolvedStyle.display != DisplayStyle.None).ToArray();
        for (var i = 0; i < visible.Length; i++)
        for (var j = i + 1; j < visible.Length; j++)
        {
            var a = views[visible[i].Id].layout;
            var b = views[visible[j].Id].layout;
            var ar = new Rect(positions[visible[i].Id], a.size);
            var br = new Rect(positions[visible[j].Id], b.size);
            Require(!ar.Overlaps(br), "Organized nodes overlap: " + visible[i].Id + " and " + visible[j].Id + ".");
        }
    }

    static void LogMetrics(string label)
    {
        var canvas = (VisualElement)Field("canvas");
        var positions = Positions;
        var views = (Dictionary<string, VisualElement>)Field("nodes");
        var visible = Graph.Nodes.Where(n => views[n.Id].resolvedStyle.display != DisplayStyle.None).ToArray();
        var bounds = new Rect(positions[visible[0].Id], views[visible[0].Id].layout.size);
        foreach (var node in visible.Skip(1))
        {
            var rect = new Rect(positions[node.Id], views[node.Id].layout.size);
            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin),
                Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
        }
        var fit = Mathf.Min(canvas.layout.width / Mathf.Max(1, bounds.width), canvas.layout.height / Mathf.Max(1, bounds.height));
        Debug.Log("NXSG ORGANIZE METRICS " + label + ": canvas=" + canvas.layout.width + "x" + canvas.layout.height +
            ", graphBounds=" + bounds.width + "x" + bounds.height + ", zoom=" + Field("zoom") + ", fitZoom=" + fit +
            ", nodes=" + Graph.Nodes.Count + ", connections=" + Graph.Connections.Count +
            ", frames=" + GraphGroups.All(Graph).Count(g => g["frame"]?.Type == JTokenType.Boolean && (bool)g["frame"]));
    }

    static void SaveOrganizedSource()
    {
        var output = Environment.GetEnvironmentVariable("NXSG_ORGANIZE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var fullPath = Path.GetFullPath(output);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, GraphJson.Serialize(Graph, true));
        Debug.Log("NXSG organized source saved to " + fullPath);
    }

    static void BeginCapture(string path)
    {
        captureRequested = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"));
        capturePath = path;
        if (!captureRequested) return;
        if (File.Exists(path)) File.Delete(path);
        using (var process = Process.Start(new ProcessStartInfo("gamescopectl", "screenshot " + path) { UseShellExecute = false }))
            Require(process != null && process.WaitForExit(5000) && process.ExitCode == 0, "Gamescope screenshot request failed: " + path);
        captureDeadline = EditorApplication.timeSinceStartup + 5;
    }

    static bool FinishCapture()
    {
        if (!captureRequested || File.Exists(capturePath)) return true;
        Require(EditorApplication.timeSinceStartup <= captureDeadline, "Gamescope did not write screenshot: " + capturePath);
        nextCheck = EditorApplication.timeSinceStartup + .1;
        return false;
    }

    static string CaptureStatus(string path) => captureRequested && File.Exists(path) ? path : "Gamescope unavailable";
    static string GroupSignature(ShaderGraph graph) => string.Join("|", GraphGroups.All(graph)
        .Select(g => g.ToString(Newtonsoft.Json.Formatting.None)).OrderBy(s => s, StringComparer.Ordinal));
    static void Fail(Exception e) { Debug.LogException(e); Finish(1); }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        if (window != null) UnityEngine.Object.DestroyImmediate(window);
        EditorApplication.Exit(code);
    }
}
