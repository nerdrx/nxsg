using System;
using System.Linq;
using System.IO;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class PerformanceSmoke
{
    static GraphWindow window;
    static int ticks, phase;
    static readonly float[] Widths = { 240, 310, 400 };
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Flags).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Flags).Invoke(window, args);
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static void Run()
    {
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.Focus();
        window.position = new Rect(0, 0, 1280, 800);
        var graph = GraphSamples.CreateDefault();
        graph.Resources[0].Name = "Body albedo with a deliberately long texture name for the sidebar";
        graph.Nodes.Add(new GraphNode { Id = "unused-fur", Operation = "core.fur" });
        typeof(GraphWindow).GetField("graph", Flags).SetValue(window, graph);
        typeof(GraphWindow).GetField("autoScene", Flags).SetValue(window, false);
        typeof(GraphWindow).GetField("livePreview", Flags).SetValue(window, false);
        Invoke("Rebuild");
        Invoke("ShowSidebarTab", 3);
        window.rootVisualElement.Q<TwoPaneSplitView>().fixedPaneInitialDimension = Widths[0];
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (++ticks < 50) return;
        ticks = 0;
        try
        {
            var panel = (VisualElement)Field("performancePanel");
            Require(panel.panel != null, "Performance tab panel is not attached");
            Require(panel.Children().Any(e => e is HelpBox && ((HelpBox)e).text.Contains("not GPU time")), "Static estimate disclaimer missing");
            Require(panel.Query<Button>().ToList().Any(b => b.text.StartsWith("Body albedo")), "Renamed texture is not identified in Cost");
            Require(panel.Children().OfType<Label>().Any(l => l.text == "Performance estimates"), "Performance title missing");
            var sidebar = (VisualElement)Field("sidebar");
            foreach (var button in sidebar.Query<Button>().ToList().Where(b => b.worldBound.height > 0 && b.worldBound.y < window.position.height))
                Require(button.worldBound.xMax <= sidebar.worldBound.xMax + 1, "Sidebar button overflow at width " + Widths[Math.Min(phase,2)]);
            if (phase < 2)
            {
                phase++;
                window.rootVisualElement.Q<TwoPaneSplitView>().fixedPaneInitialDimension = Widths[phase];
                return;
            }
            if (phase == 2)
            {
                Invoke("FrameNodes", false);
                if (File.Exists("/tmp/nxsg-cost-panel.png")) File.Delete("/tmp/nxsg-cost-panel.png");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("gamescopectl", "screenshot /tmp/nxsg-cost-panel.png") {UseShellExecute=false})?.Dispose();
                phase++; return;
            }
            Require(File.Exists("/tmp/nxsg-cost-panel.png"), "Cost screenshot missing");
            var hotspot = panel.Query<Button>().ToList().FirstOrDefault(b => b.text.Contains("Toon Surface"));
            Require(hotspot != null, "Reachable surface hot spot is missing");
            hotspot.Focus();
            using (var down = PointerDownEvent.GetPooled(new Event { type=EventType.MouseDown, button=0, mousePosition=hotspot.worldBound.center })) hotspot.SendEvent(down);
            using (var up = PointerUpEvent.GetPooled(new Event { type=EventType.MouseUp, button=0, mousePosition=hotspot.worldBound.center })) hotspot.SendEvent(up);
            Require((int)Field("sidebarTab") == 0, "Hot spot focus did not return to Inspector");
            Debug.Log("NXSG PERFORMANCE SMOKE PASSED: estimates render and hot spot focus opens Inspector");
            EditorApplication.update -= Tick; window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
        }
        catch (Exception e) { EditorApplication.update -= Tick; Debug.LogException(e); window.DiscardChanges(); window.Close(); EditorApplication.Exit(1); }
    }
}
