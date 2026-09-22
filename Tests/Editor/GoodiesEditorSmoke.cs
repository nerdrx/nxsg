using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class GoodiesEditorSmoke
{
    static GraphWindow window; static int ticks;
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Get(string name) => typeof(GraphWindow).GetField(name, Flags).GetValue(window);
    static void Set(string name, object value) => typeof(GraphWindow).GetField(name, Flags).SetValue(window, value);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Flags).Invoke(window, args);
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static void Run()
    {
        window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 1100, 800); window.Focus();
        var graph = new ShaderGraph { GraphId = "goodies-smoke", Layout = new GraphLayout() };
        var value = NodeCatalog.Create("core.value"); value.Id = "value";
        var particles = NodeCatalog.Create("core.surfaceParticles"); particles.Id = "particles";
        graph.Nodes.Add(value); graph.Nodes.Add(particles); Set("graph", graph);
        ((GraphSession)Get("session")).json = GraphJson.Serialize(graph, true);
        Set("selection", new System.Collections.Generic.List<string> { "value", "particles" }); Set("selected", "value"); Invoke("Rebuild");
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (++ticks < 35) return; EditorApplication.update -= Tick;
        try
        {
            var session = (GraphSession)Get("session"); var before = session.json;
            var inline = window.rootVisualElement.Query<FloatField>().ToList().FirstOrDefault(f => f.label == "Value");
            Require(inline != null, "Inline value field missing"); inline.value = .25f; Require(session.json != before, "Inline value did not edit graph");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Require(session.json == before, "Inline value undo did not restore graph");
            Set("selection", new System.Collections.Generic.List<string> { "value", "particles" }); Set("selected", "value"); Invoke("CreateFrame", "Particles", "Tune lifetime first");
            var graph = (ShaderGraph)Get("graph"); var frame = GraphGroups.All(graph).FirstOrDefault(g => (bool?)g["frame"] == true);
            Require(frame != null && (string)frame["note"] == "Tune lifetime first" && (bool?)frame["collapsed"] == false, "Frame metadata missing");
            var roundTrip = GraphJson.Parse(GraphJson.Serialize(graph, true)); Require((string)GraphGroups.All(roundTrip).First(g => (bool?)g["frame"] == true)["note"] == "Tune lifetime first", "Frame note did not round trip");
            Set("selection", new System.Collections.Generic.List<string> { "particles" }); Set("selected", "particles"); Invoke("RebuildInspector");
            var curves = window.rootVisualElement.Query<CurveField>().ToList(); Require(curves.Count >= 2, "Particle identity curves missing on old node"); curves[0].value = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, .4f));
            var particle = ((ShaderGraph)Get("graph")).Nodes.First(n => n.Id == "particles"); Require(particle.Properties["sizeCurve"] is JArray, "Particle curve edit did not persist");
            Invoke("RebuildInspector"); var finder = window.rootVisualElement.Q<ToolbarSearchField>("existing-node-search"); Require(finder != null, "Existing node finder missing"); finder.value = "particles";
            var button = finder.parent.Children().SelectMany(e => e.Children()).OfType<Button>().FirstOrDefault(); Require(button != null, "Existing node finder result missing"); using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit); Require((string)Get("selected") == "particles", "Finder did not select existing node");
            Debug.Log("NXSG GOODIES EDITOR SMOKE PASSED: inline edit/undo, frame note round trip, finder selection, identity particle curves/edit"); window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); window.DiscardChanges(); window.Close(); EditorApplication.Exit(1); }
    }
}
