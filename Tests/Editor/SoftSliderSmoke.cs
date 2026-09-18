using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

// Run in an isolated Unity project with NXSG installed; never on a user's graph.
public static class SoftSliderSmoke
{
    static GraphWindow window;
    static int ticks;
    static int phase;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static GraphNode Node(string id) => Graph.Nodes.Single(node => node.Id == id);
    static double Number(string node, string property) => (double)Node(node).Properties[property];

    static void SetBaseline()
    {
        var session = (GraphSession)Field("session");
        session.json = GraphJson.Serialize(Graph, true);
        Undo.ClearUndo(session);
    }

    static void BuildGraph()
    {
        Invoke("NewGraph");
        Graph.Nodes.Clear();
        Graph.Connections.Clear();
        Graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
        var particles = NodeCatalog.Create("core.surfaceParticles");
        particles.Id = "particles";
        Graph.Nodes.Add(particles);
        Graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output", Properties = new JObject() });
        Graph.Connections.Add(Connection("particles-output", "particles", "surface", "output", "surface"));
        SetBaseline();
        Invoke("Rebuild");
        Invoke("SelectNode", "particles", false);
    }

    static GraphConnection Connection(string id, string fromNode, string fromPort, string toNode, string toPort)
    {
        return new GraphConnection { Id = id, From = new GraphPortRef { NodeId = fromNode, PortId = fromPort },
            To = new GraphPortRef { NodeId = toNode, PortId = toPort } };
    }

    static VisualElement Inspector => (VisualElement)Field("inspector");

    static Slider Slider(string label) => Inspector.Query<Slider>().ToList().Single(field => field.label == label);
    static FloatField NumberField(string label)
    {
        var slider = Slider(label);
        return slider.parent.Q<FloatField>();
    }

    static void CheckDensity(float expected, float sliderExpected)
    {
        Require(Mathf.Approximately((float)Number("particles", "density"), expected), "Density graph value changed unexpectedly");
        var field = NumberField("Triangle density");
        Require(Mathf.Approximately(field.value, expected), "Density FloatField value mismatch");
        Require(Mathf.Approximately(Slider("Triangle density").value, sliderExpected), "Density slider value mismatch");
    }

    public static void Run()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>();
        window.Show();
        window.position = new Rect(0, 0, 1100, 700);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        BuildGraph();
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (++ticks % 20 != 0) return;
        try
        {
            if (phase == 0)
            {
                CheckDensity(.1f, .1f);
                NumberField("Triangle density").value = 1000;
                Require(Mathf.Approximately((float)Number("particles", "density"), 1000), "Delayed FloatField did not update graph");
                phase = 1;
            }
            else if (phase == 1)
            {
                CheckDensity(1000, 1);
                var roundTrip = GraphJson.Parse(GraphJson.Serialize(Graph, true));
                Require(Mathf.Approximately((float)roundTrip.Nodes.Single(node => node.Id == "particles").Properties["density"], 1000), "Density did not survive serialization");
                Undo.PerformUndo();
                CheckDensity(.1f, .1f);
                Undo.PerformRedo();
                CheckDensity(1000, 1);
                Slider("Triangle density").value = .5f;
                CheckDensity(.5f, .5f);
                NumberField("Triangle density").value = float.NaN;
                CheckDensity(.5f, .5f);

                var opacitySource = NodeCatalog.Create("core.value");
                opacitySource.Id = "opacitySource";
                Graph.Nodes.Add(opacitySource);
                Graph.Connections.Add(Connection("opacity-input", "opacitySource", "value", "particles", "opacity"));
                Invoke("Rebuild");
                Invoke("SelectNode", "particles", false);
                phase = 2;
            }
            else
            {
                Require(!Slider("Opacity").parent.enabledSelf, "Linked opacity row remained enabled");
                Debug.Log("NXSG SOFT SLIDER CHECK PASSED: delayed out-of-range values, rebuild, persistence, Undo/Redo, slider callback, finite input, and linked-row disable");
                EditorApplication.update -= Tick;
                window.DiscardChanges();
                window.Close();
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
}
