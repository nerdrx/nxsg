using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static class InspectorPolishSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string ScreenshotPath = "/tmp/nxsg-inspector-polish.png";
    static GraphWindow window;
    static int phase;
    static double nextCheck;
    static bool screenshotRequested;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static object Call(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static VisualElement Inspector => (VisualElement)Field("inspector");
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void Run()
    {
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 840, 640);
            window.CreateGUI();
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            Invoke("NewGraph");
            Graph.Nodes.Clear(); Graph.Connections.Clear();
            var toon = NodeCatalog.Create("core.toonSurface"); toon.Id = "toon"; Graph.Nodes.Add(toon);
            Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout> { [toon.Id] = new GraphNodeLayout() } };
            ((GraphSession)Field("session")).json = GraphJson.Serialize(Graph, true);
            Undo.ClearUndo((GraphSession)Field("session"));
            Invoke("SelectNode", toon.Id, false);
            Invoke("Rebuild");
            phase = 0; nextCheck = EditorApplication.timeSinceStartup + .6;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + .6;
        try
        {
            if (phase == 0)
            {
                var minimum = Inspector.Q<FloatField>("node-property-lightingMin");
                Require(minimum != null, "Toon minimum-brightness field is missing");
                Require(!minimum.label.Contains("*"), "Fresh default value is marked as changed");
                var lighting = Section("lighting influence");
                var direction = Section("direction override");
                Require(lighting != null && direction != null, "Lighting influence or direction override section is missing");
                lighting.value = true; direction.value = true;
                minimum.value = .35f;
                phase = 1;
            }
            else if (phase == 1)
            {
                var minimum = Inspector.Q<FloatField>("node-property-lightingMin");
                var lighting = Section("lighting influence");
                var direction = Section("direction override");
                Require(minimum != null && minimum.label.Contains("*"), "Edited property has no change marker");
                Require(lighting != null && lighting.text.Contains("*"), "Edited lighting section has no change marker");
                Require(direction != null && direction.value, "Direction override did not stay expanded");
                CheckVectorBounds();
                ((ScrollView)Inspector).scrollOffset = new Vector2(0, lighting.worldBound.yMin - ((ScrollView)Inspector).contentContainer.worldBound.yMin - 8);
                phase = 9;
            }
            else if (phase == 9)
            {
                CaptureScreenshot();
                phase = 10;
            }
            else if (phase == 10)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY")))
                    Require(File.Exists(ScreenshotPath), "Inspector screenshot is missing");
                var lighting = Section("lighting influence");
                lighting.value = false;
                Invoke("RebuildInspector");
                phase = 2;
            }
            else if (phase == 2)
            {
                if (screenshotRequested) Require(File.Exists(ScreenshotPath), "Gamescope screenshot was requested but file is missing");
                var lighting = Section("lighting influence");
                Require(lighting != null && !lighting.value, "Collapsed inspector section did not survive rebuild");
                Undo.PerformUndo();
                phase = 3;
            }
            else if (phase == 3)
            {
                var minimum = Inspector.Q<FloatField>("node-property-lightingMin");
                var lighting = Section("lighting influence");
                Require(minimum != null && Mathf.Abs(minimum.value) < .0001f, "Undo did not restore default value");
                Require(!minimum.label.Contains("*") && lighting != null && !lighting.text.Contains("*"), "Undo left stale change markers");
                CheckNodeInspectors();
                Debug.Log("NXSG INSPECTOR POLISH SMOKE PASSED"); Finish(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    static Foldout Section(string titlePart) => Inspector.Query<Foldout>().ToList().FirstOrDefault(f =>
        f.name.StartsWith("inspector-section-", StringComparison.Ordinal)
        && f.text.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0);

    static void CheckVectorBounds()
    {
        var vector = Inspector.Query<VisualElement>().ToList().FirstOrDefault(e => e.ClassListContains("nxsg-vector-field"));
        Require(vector != null, "Direction vector field is missing");
        Require(vector.worldBound.width > 0, "Direction vector field has no layout geometry");
        var components = vector.Query<FloatField>().ToList();
        Require(components.Count == 3, "Direction vector field does not expose X, Y and Z components");
        foreach (var field in components)
            Require(field.worldBound.width > 0 && field.worldBound.xMin >= Inspector.worldBound.xMin - 1
                && field.worldBound.xMax <= Inspector.worldBound.xMax + 1, "Vector component is clipped at narrow inspector width");
    }

    static void CaptureScreenshot()
    {
        screenshotRequested = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"));
        if (!screenshotRequested) return;
        if (File.Exists(ScreenshotPath)) File.Delete(ScreenshotPath);
        using (var process = Process.Start(new ProcessStartInfo("gamescopectl", "screenshot " + ScreenshotPath) { UseShellExecute = false }))
            Require(process != null && process.WaitForExit(5000) && process.ExitCode == 0, "Gamescope screenshot request failed");
    }

    static void CheckNodeInspectors()
    {
        var count = 0;
        foreach (var operation in NodeCatalog.All.Distinct().OrderBy(value => value, StringComparer.Ordinal))
        {
            var node = (GraphNode)Call("CreateNode", operation, Vector2.zero);
            Require(node != null, "Node catalog cannot create " + operation);
            Invoke("SelectNode", node.Id, false);
            Require(Inspector.Q<Label>("nxsg-node-description") != null, "Inspector failed for " + operation);
            foreach (var binding in (System.Collections.IEnumerable)Field("inspectorProperties"))
            {
                var changed = (Func<bool>)binding.GetType().GetField("changed").GetValue(binding);
                Require(!changed(), "Fresh node has a changed-default marker: " + operation + " / " + ((VisualElement)binding.GetType().GetField("element").GetValue(binding)).name);
            }
            count++;
        }
        Debug.Log("NXSG inspector catalog coverage: " + count + " operations");
    }

    static void Fail(Exception e) { Debug.LogException(e); Finish(1); }
    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        if (window != null) UnityEngine.Object.DestroyImmediate(window);
        EditorApplication.Exit(code);
    }
}
