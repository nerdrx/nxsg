using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static class SurfaceSocketFoldoutSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string ScreenshotPath = "/tmp/nxsg-surface-foldouts.png";
    static readonly string[] LightingInputs =
    {
        "shadeColor", "shadeMap", "occlusion", "shadow", "threshold", "softness", "shadowStrength", "normalStrength",
        "shadeColor2", "shadeMap2", "threshold2", "softness2", "shadowStrength2", "normalStrength2",
        "shadeColor3", "shadeMap3", "threshold3", "softness3", "shadowStrength3", "normalStrength3",
        "receiveShadow", "layerReceiveShadow", "layerReceiveShadow2", "layerReceiveShadow3"
    };
    static GraphWindow window;
    static int ticks, phase;
    static double nextCheck;
    static bool screenshotRequested;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static string GroupKey(string node, string group) => Graph.GraphId + ":" + node + ":" + group;
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void Run()
    {
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 1100, 700);
            window.CreateGUI();
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            typeof(GraphWindow).GetMethod("NewGraph", Private).Invoke(window, null);
            Graph.Nodes.Clear(); Graph.Connections.Clear();
            var toon = NodeCatalog.Create("core.toonSurface"); toon.Id = "toon"; Graph.Nodes.Add(toon);
            var expandedToon = NodeCatalog.Create("core.toonSurface"); expandedToon.Id = "toon-open"; Graph.Nodes.Add(expandedToon);
            var color = NodeCatalog.Create("core.constant"); color.Id = "color"; Graph.Nodes.Add(color);
            Graph.Connections.Add(new GraphConnection { Id = "connected-shade", From = new GraphPortRef { NodeId = color.Id, PortId = "value" }, To = new GraphPortRef { NodeId = toon.Id, PortId = "shadeColor" } });
            Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>
            {
                ["color"] = new GraphNodeLayout { X = -150, Y = 100 },
                ["toon"] = new GraphNodeLayout { X = 80, Y = 100 },
                ["toon-open"] = new GraphNodeLayout { X = 300, Y = 100 }
            } };
            ((List<string>)Field("expandedSurfaceSocketGroups")).Add(GroupKey("toon-open", "lighting"));
            Invoke("Rebuild");
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void Tick()
    {
        if (++ticks < 12 || EditorApplication.timeSinceStartup < nextCheck) return;
        ticks = 0;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        try
        {
            var nodes = (Dictionary<string, VisualElement>)Field("nodes");
            var card = nodes["toon"];
            var lighting = card.Q<Foldout>("surface-sockets-lighting");
            var lookup = typeof(GraphWindow).GetField("socketLookup", Private).GetValue(window);
            var indexer = lookup.GetType().GetProperty("Item");
            Func<string, string, VisualElement> hit = (node, port) =>
            {
                var entry = indexer.GetValue(lookup, new object[] { (node, port, false) });
                return (VisualElement)entry.GetType().GetField("hit").GetValue(entry);
            };
            if (phase == 0)
            {
                Require(lighting != null && !lighting.value && card.Q<Foldout>("surface-sockets-rim")?.value == false, "Surface groups must start collapsed");
                Require(card.Q<Label>(null).text.StartsWith("Toon Surface", StringComparison.Ordinal), "Toon node title changed");
                foreach (var port in new[] { "albedo", "normal", "emission", "opacity", "displacement" })
                    Require(hit("toon", port) != null && hit("toon", port).parent.parent == card, "Common input is not always visible: " + port);
                Require(hit("toon", "shadeColor").parent.parent == card, "Connected advanced socket is hidden while collapsed");
                Require(hit("toon", "shadeMap").worldBound.height == 0, "Unconnected advanced socket is visible while collapsed");
                var expandedCard = nodes["toon-open"];
                Require(expandedCard.Q<Foldout>("surface-sockets-lighting").value, "Expanded comparison node did not open");
                Require(Mathf.Abs(hit("toon-open", "shadeMap").worldBound.xMin - hit("toon-open", "albedo").worldBound.xMin) < 1, "Expanded sockets do not align with common socket edge");
                var dropCheck = typeof(GraphWindow).GetMethod("IsSocketDropTarget", BindingFlags.Static | BindingFlags.NonPublic);
                Require(!(bool)dropCheck.Invoke(null, new[] { SocketEntry(lookup, indexer, "toon", "shadeMap") }), "Collapsed socket is still a wire drop target");
                Require((bool)dropCheck.Invoke(null, new[] { SocketEntry(lookup, indexer, "toon", "shadeColor") }), "Connected socket is not a wire drop target");
                Invoke("FrameNodes", false);
                phase = 1;
            }
            else if (phase == 1)
            {
                RequestFoldoutScreenshot();
                phase = 2;
            }
            else if (phase == 2)
            {
                if (screenshotRequested) Require(File.Exists(ScreenshotPath), "Gamescope screenshot was requested but file is missing");
                lighting.value = true;
                Require(((List<string>)Field("expandedSurfaceSocketGroups")).Contains(GroupKey("toon", "lighting")), "Foldout state was not recorded under graph identity");
                Invoke("Rebuild"); phase = 3;
            }
            else if (phase == 3)
            {
                card = nodes["toon"]; lighting = card.Q<Foldout>("surface-sockets-lighting");
                Require(lighting.value, "Expanded group state did not survive node rebuild");
                Require(hit("toon", "shadeMap").worldBound.height > 0, "Expanded socket has no layout geometry");
                lighting.value = false;
                Require(!(bool)typeof(GraphWindow).GetMethod("IsSocketDropTarget", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new[] { SocketEntry(lookup, indexer, "toon", "shadeMap") }), "Collapsed socket remained a drop target before layout settled");
                Invoke("Rebuild"); phase = 4;
            }
            else if (phase == 4)
            {
                card = nodes["toon"];
                Require(!card.Q<Foldout>("surface-sockets-lighting").value, "Collapsed state did not survive rebuild");
                Require(hit("toon", "shadeColor").worldBound.height > 0, "Connected socket lost geometry after rebuild");
                Require(hit("toon", "shadeMap").worldBound.height == 0, "Collapsed socket retained visible geometry after rebuild");
                Graph.Connections.RemoveAll(edge => edge.To.NodeId == "toon");
                foreach (var port in LightingInputs)
                    Graph.Connections.Add(new GraphConnection { Id = "connected-all-" + port, From = new GraphPortRef { NodeId = "color", PortId = "value" }, To = new GraphPortRef { NodeId = "toon", PortId = port } });
                var pbr = NodeCatalog.Create("core.pbrSurface"); pbr.Id = "pbr"; Graph.Nodes.Add(pbr);
                Graph.Layout.Nodes["pbr"] = new GraphNodeLayout { X = 550, Y = 100 };
                foreach (var operation in new[] { "core.layeredPbrSurface", "core.surfaceParticles" })
                {
                    var extra = NodeCatalog.Create(operation); extra.Id = operation; Graph.Nodes.Add(extra);
                    Graph.Layout.Nodes[extra.Id] = new GraphNodeLayout { X = 800, Y = 100 };
                }
                Graph.Connections.Add(new GraphConnection { Id = "connected-rate", From = new GraphPortRef { NodeId = "color", PortId = "value" }, To = new GraphPortRef { NodeId = "core.surfaceParticles", PortId = "emissionRate" } });
                Invoke("Rebuild"); phase = 5;
            }
            else
            {
                card = nodes["toon"];
                Require(card.Q<Foldout>("surface-sockets-lighting") == null, "All-connected group shows empty foldout header");
                var pbrCard = nodes["pbr"];
                foreach (var port in new[] { "albedo", "normal", "emission", "opacity", "displacement", "metallic", "roughness" })
                    Require(hit("pbr", port).parent.parent == pbrCard, "PBR common input is not always visible: " + port);
                var layered = nodes["core.layeredPbrSurface"];
                Require(layered.Q<Foldout>("surface-sockets-coat")?.value == false && layered.Q<Foldout>("surface-sockets-sheen")?.value == false, "Layered PBR secondary groups are not collapsed");
                var particles = nodes["core.surfaceParticles"];
                Require(particles.Q<Foldout>("surface-sockets-emission")?.value == false && particles.Q<Foldout>("surface-sockets-motion")?.value == false, "Particle secondary groups are not collapsed");
                Require(hit("core.surfaceParticles", "emissionRate").worldBound.height > 0 && hit("core.surfaceParticles", "emissionRate").parent.parent == particles, "Connected particle rate is hidden");
                Require(hit("core.surfaceParticles", "lifetime").worldBound.height == 0, "Unused particle lifetime is visible while collapsed");
                Debug.Log("NXSG SURFACE SOCKET FOLDOUT SMOKE PASSED"); Finish(0);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    static object SocketEntry(object lookup, System.Reflection.PropertyInfo indexer, string node, string port)
        => indexer.GetValue(lookup, new object[] { (node, port, false) });

    static void RequestFoldoutScreenshot()
    {
        screenshotRequested = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"));
        if (!screenshotRequested) return;
        if (File.Exists(ScreenshotPath)) File.Delete(ScreenshotPath);
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
