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
            var curves = window.rootVisualElement.Query<CurveField>().ToList(); Require(curves.Count >= 2, "Particle identity curves missing on old node");
            var sizeCurve = curves[0]; var colorCurve = window.rootVisualElement.Query<GradientField>().ToList().FirstOrDefault();
            Require(colorCurve != null, "Particle color curve missing on old node");
            sizeCurve.value = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, .4f));
            var particle = ((ShaderGraph)Get("graph")).Nodes.First(n => n.Id == "particles"); Require(particle.Properties["sizeCurve"] is JArray, "Particle curve edit did not persist");
            Require(sizeCurve.panel != null && colorCurve.panel != null, "Particle curve fields detached after first edit");
            var firstSize = particle.Properties["sizeCurve"].ToString();
            var firstColor = particle.Properties["colorCurve"]?.ToString();
            colorCurve.value = Gradient(Color.red, Color.white);
            Require(particle.Properties["colorCurve"].ToString() != firstColor, "Particle gradient edit did not persist");
            Require(sizeCurve.panel != null && colorCurve.panel != null, "Particle curve fields detached after gradient edit");
            sizeCurve.value = new AnimationCurve(new Keyframe(0, .2f), new Keyframe(1, .8f));
            colorCurve.value = Gradient(Color.blue, Color.yellow);
            Require(particle.Properties["sizeCurve"].ToString() != firstSize, "Repeated particle curve edit did not persist");
            Require(particle.Properties["colorCurve"].ToString() != firstColor, "Repeated particle gradient edit did not persist");
            Require(Mathf.Abs(sizeCurve.value.Evaluate(0) - .2f) < .001f && Mathf.Abs(sizeCurve.value.Evaluate(1) - .8f) < .001f, "Curve field did not display latest edit");
            Require(colorCurve.value.Evaluate(0) == Color.blue, "Gradient field did not display latest edit");
            sizeCurve.value = new AnimationCurve(new Keyframe(0, -1), new Keyframe(1, 1));
            Require(Mathf.Abs(sizeCurve.value.Evaluate(0) - .2f) < .001f, "Invalid curve did not restore last accepted edit");
            var restored = GraphJson.Parse(session.json).Nodes.First(n => n.Id == "particles");
            Require(Mathf.Abs((float)restored.Properties["sizeCurve"][0][1] - .2f) < .001f, "Saved curve differs from field");
            Invoke("RebuildInspector"); var finder = window.rootVisualElement.Q<ToolbarSearchField>("existing-node-search"); Require(finder != null, "Existing node finder missing"); finder.value = "particles";
            var button = finder.parent.Children().SelectMany(e => e.Children()).OfType<Button>().FirstOrDefault(); Require(button != null, "Existing node finder result missing"); using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit); Require((string)Get("selected") == "particles", "Finder did not select existing node");
            Debug.Log("NXSG GOODIES EDITOR SMOKE PASSED: inline edit/undo, frame note round trip, finder selection, identity particle curves/edit"); window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); window.DiscardChanges(); window.Close(); EditorApplication.Exit(1); }
    }

    static Gradient Gradient(Color first, Color last)
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(first, 0), new GradientColorKey(last, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        return gradient;
    }
}
