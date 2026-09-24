using System;
using System.Collections;
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

// Isolated editor interaction check. Run with the hidden Gamescope fixture.
public static class ColorControlsSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static int phase, ticks;
    static object Field(string key) => typeof(GraphWindow).GetField(key, Private).GetValue(window);
    static void Invoke(string method, params object[] args) => typeof(GraphWindow).GetMethod(method, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static GraphNode Adjust => Graph.Nodes.Single(n => n.Id == "adjust");
    static VisualElement Inspector => (VisualElement)Field("inspector");
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Run()
    {
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show(); window.Focus(); window.position = new Rect(0, 0, 1280, 800);
        typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        Invoke("NewGraph"); Graph.Nodes.Clear(); Graph.Connections.Clear(); Graph.Layout.Nodes.Clear();
        foreach (var pair in new[] { new[] { "input", "core.constant" }, new[] { "hue", "core.value" }, new[] { "adjust", "core.colorAdjust" }, new[] { "surface", "core.unlitSurface" }, new[] { "output", "core.output" } })
        { var node = NodeCatalog.Create(pair[1]); node.Id = pair[0]; Graph.Nodes.Add(node); }
        Graph.Nodes[0].Properties["value"] = new JArray(.2, .4, .8, 1);
        Connect("input", "value", "adjust", "color"); Connect("adjust", "color", "surface", "albedo"); Connect("surface", "surface", "output", "surface");
        SetPosition("input", 20, 170); SetPosition("hue", 20, 400); SetPosition("adjust", 240, 130); SetPosition("surface", 470, 130); SetPosition("output", 680, 130);
        Baseline(); Invoke("Rebuild"); Invoke("SelectNode", "adjust", false);
        EditorApplication.update += Tick;
    }
    static void SetPosition(string id, float x, float y) => Invoke("SetPosition", id, new Vector2(x, y));
    static void Connect(string from, string port, string to, string input) => Graph.Connections.Add(new GraphConnection { Id = from + to + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    static void Baseline() { var session = (GraphSession)Field("session"); session.json = GraphJson.Serialize(Graph, true); Undo.ClearUndo(session); }
    static void Tick()
    {
        if (++ticks < 30) return; ticks = 0;
        try
        {
            if (phase == 0)
            {
                var oldLayout = Graph.Layout.Nodes["adjust"]; SetPosition("adjust", 245, 130);
                Require(ReferenceEquals(oldLayout, Graph.Layout.Nodes["adjust"]), "Dragging allocated a replacement layout");
                var hue = Inspector.Q<FloatField>("node-property-hue"); hue.value = .25f;
                Require(ReferenceEquals(hue, Inspector.Q<FloatField>("node-property-hue")), "Hue edit rebuilt inspector");
                var gain = Inspector.Q<FloatField>("node-property-gain"); gain.value = 2;
                Require(ReferenceEquals(gain, Inspector.Q<FloatField>("node-property-gain")), "Gain edit rebuilt inspector");
                gain.value = float.NaN;
                Require(gain.value == 2 && (float)Adjust.Properties["gain"] == 2, "Rejected value remained visible or changed graph");
                Require(gain.tooltip.Contains("Multiplies"), "Color control help missing");
                Inspector.Query<PopupField<string>>().ToList().Single(f => f.label == "Hue space").value = "OKLab";
            }
            else if (phase == 1)
            {
                Require((int)Adjust.Properties["hueSpace"] == 1, "Hue dropdown did not persist");
                Connect("hue", "value", "adjust", "hue"); Baseline(); Invoke("Rebuild");
                Require(!Inspector.Q<FloatField>("node-property-hue").enabledInHierarchy, "Connected input still editable");
                Require(Inspector.Q<Button>("reset-color-adjustments") != null, "Reset button missing");
                Invoke("ResetColorAdjustment", Adjust);
                Require((float)Adjust.Properties["gain"] == 1 && (float)Adjust.Properties["hue"] == .25f && (int)Adjust.Properties["hueSpace"] == 1 && Graph.Connections.Count == 4, "Reset changed connected input or hue space");
                Undo.PerformUndo();
            }
            else if (phase == 2)
            {
                Require((float)Adjust.Properties["gain"] == 2, "Reset undo lost value");
                var parsed = GraphJson.Parse(((GraphSession)Field("session")).json);
                Require((int)parsed.Nodes.Single(n => n.Id == "adjust").Properties["hueSpace"] == 1, "Session lost hue space");
                Invoke("SwitchOperation", "adjust", "core.hueShift");
                Require(Adjust.Operation == "core.hueShift" && Graph.Connections.Count == 4, "Hue header switch lost compatible wires");
                Invoke("SwitchOperation", "adjust", "core.colorAdjust");
                Require((int)Adjust.Properties["hueSpace"] == 1, "Header switch lost hue space");
                Invoke("SelectNode", "adjust", false);
            }
            else if (phase == 3)
            {
                var fields = Inspector.Query<FloatField>().ToList();
                Require(fields.All(f => f.worldBound.xMax <= Inspector.worldBound.xMax + 1), "Color field overflowed inspector");
                Require(Inspector.Q<Button>("reset-color-adjustments").worldBound.yMax < window.position.height, "Reset control is clipped");
                if (File.Exists("/tmp/nxsg-color-controls.png")) File.Delete("/tmp/nxsg-color-controls.png");
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("gamescopectl", "screenshot /tmp/nxsg-color-controls.png") { UseShellExecute = false });
                if (process != null) process.Dispose();
            }
            else
            {
                Require(File.Exists("/tmp/nxsg-color-controls.png"), "Editor screenshot missing");
                Debug.Log("NXSG COLOR CONTROLS PASSED: field identity, invalid input recovery, dropdown persistence, reset/Undo, header switching, layout bounds");
                Finish(0); return;
            }
            phase++;
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }
    static void Finish(int code) { EditorApplication.update -= Tick; window.DiscardChanges(); window.Close(); EditorApplication.Exit(code); }
}
