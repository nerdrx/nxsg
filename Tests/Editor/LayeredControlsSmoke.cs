using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class LayeredControlsSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int phase, ticks;
    static object Field(string key) => typeof(GraphWindow).GetField(key, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static GraphNode Surface => Graph.Nodes.Single(n => n.Id == "surface");
    static VisualElement Inspector => (VisualElement)Field("inspector");
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.Focus(); window.position = new Rect(0, 0, 1380, 850);
        typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        Invoke("NewGraph");
        var graph = GraphJson.Parse(File.ReadAllText("Packages/dev.nerdrx.nxsg/Samples~/Lacquered Surface.nxsg"));
        typeof(GraphWindow).GetField("graph", Private).SetValue(window, graph);
        ((GraphSession)Field("session")).json = GraphJson.Serialize(graph, true);
        Invoke("Rebuild"); Invoke("SelectNode", "surface", false); Invoke("FrameNodes", false);
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (++ticks < 30) return; ticks = 0;
        try
        {
            if (phase == 0)
            {
                var before = Graph.Connections.Count; var coatBefore = (double)Surface.Properties["coat"];
                Invoke("SwitchOperation", "surface", "core.pbrSurface");
                Require(Surface.Operation == "core.pbrSurface", "PBR header switch failed");
                var baseWires = Graph.Connections.Count;
                Require(baseWires > 0 && baseWires <= before, "PBR switch removed base wires");
                Invoke("SwitchOperation", "surface", "core.layeredPbrSurface");
                Require(Graph.Connections.Count == baseWires, "Returning to layered PBR changed base wires");
                Require((double)Surface.Properties["coat"] == coatBefore, "Header switch lost dormant coat default");
                var field = Inspector.Q<FloatField>("node-property-coat");
                Require(field != null && field.enabledInHierarchy, "Unconnected coat weight field missing");
                field.value = .6f;
                Require(ReferenceEquals(field, Inspector.Q<FloatField>("node-property-coat")), "Coat edit rebuilt inspector");
                Require(Math.Abs((double)Surface.Properties["coat"] - .6) < .00001, "Coat edit not saved");
                Inspector.Q<ColorField>("node-property-sheenColor").value = new Color(.3f,.5f,1);
                Require((Surface.Properties["sheenColor"] as JArray)?.Count == 4, "Sheen color edit failed");
                Graph.Connections.Add(new GraphConnection { Id="weight-input", From=new GraphPortRef { NodeId="coatWeight",PortId="value" }, To=new GraphPortRef { NodeId="surface",PortId="coat" } });
                Invoke("Rebuild");
            }
            else if (phase == 1)
            {
                Require(!Inspector.Q<FloatField>("node-property-coat").enabledInHierarchy, "Connected coat weight is editable");
                var fields = Inspector.Query<FloatField>().ToList();
                Require(fields.All(f => f.worldBound.xMin >= Inspector.worldBound.xMin-1 && f.worldBound.xMax <= Inspector.worldBound.xMax+1), "Layered controls overflow inspector");
                var labels = window.rootVisualElement.Query<Label>().ToList();
                Require(labels.Any(l => l.text == "Coat normal") && labels.Any(l => l.text == "Sheen roughness"), "Layer socket labels missing");
                if (File.Exists("/tmp/nxsg-layered-controls.png")) File.Delete("/tmp/nxsg-layered-controls.png");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("gamescopectl", "screenshot /tmp/nxsg-layered-controls.png") {UseShellExecute=false})?.Dispose();
            }
            else
            {
                Require(File.Exists("/tmp/nxsg-layered-controls.png"), "Layered inspector screenshot missing");
                Debug.Log("NXSG LAYERED CONTROLS PASSED: header switching, dormant properties, scalar/color edits, connected fields, bounds and socket labels");
                Finish(0); return;
            }
            phase++;
        }
        catch(Exception e) { Debug.LogException(e); Finish(1); }
    }
    static void Finish(int code) { EditorApplication.update -= Tick; window.DiscardChanges(); window.Close(); EditorApplication.Exit(code); }
}
