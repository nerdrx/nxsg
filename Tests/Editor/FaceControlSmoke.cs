using System;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Backend;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Run with the hidden editor fixture: -executeMethod FaceControlSmoke.Run.
public static class FaceControlSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static object Field(string key) => typeof(GraphWindow).GetField(key, Private).GetValue(window);
    static void Invoke(string method, params object[] args) => typeof(GraphWindow).GetMethod(method, Private).Invoke(window, args);
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static void Run()
    {
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show(); window.Focus(); window.position = new Rect(0, 0, 1280, 800);
            typeof(GraphWindow).GetField("autoScene", Private).SetValue(window, false);
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            Invoke("NewGraph");
            var graph = (ShaderGraph)Field("graph"); graph.Nodes.Clear(); graph.Connections.Clear(); graph.Layout.Nodes.Clear();
            Add(graph, "front", FaceNodes.FrontFace);
            Add(graph, "red", "core.constant"); Add(graph, "blue", "core.constant");
            Add(graph, "mix", "core.mix"); Add(graph, "surface", "core.unlitSurface"); Add(graph, "output", "core.output");
            graph.Nodes.Single(n => n.Id == "red").Properties["valueType"] = "color";
            graph.Nodes.Single(n => n.Id == "red").Properties["value"] = new Newtonsoft.Json.Linq.JArray(1, 0, 0, 1);
            graph.Nodes.Single(n => n.Id == "blue").Properties["valueType"] = "color";
            graph.Nodes.Single(n => n.Id == "blue").Properties["value"] = new Newtonsoft.Json.Linq.JArray(0, 0, 1, 1);
            Connect(graph, "front", "isFront", "mix", "factor");
            Connect(graph, "red", "value", "mix", "a"); Connect(graph, "blue", "value", "mix", "b");
            Connect(graph, "mix", "value", "surface", "albedo"); Connect(graph, "surface", "surface", "output", "surface");
            var output = graph.Nodes.Single(n => n.Id == "output");
            output.Properties["renderMode"] = 3; output.Properties["twoPassTransparency"] = 1;
            output.Properties["alphaToCoverage"] = 1; output.Properties["alphaEdgeSharpness"] = .4;
            output.Properties["frontPassBlend"] = 2; output.Properties["backPassBlend"] = 1;
            Invoke("Rebuild"); Invoke("SelectNode", "output", false);
            var inspector = (VisualElement)Field("inspector");
            Require(inspector.Query<PopupField<string>>().ToList().Any(f => f.label == "Two-sided transparency"), "Output inspector misses two-sided transparency control");
            var result = ShaderEmitter.Emit(graph);
            Require(result.Succeeded, "Face control graph failed validation or generation: " + string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Require(result.ShaderSource.Contains("SV_IsFrontFace") || result.ShaderSource.Contains("VFACE"), "Generated fragment shader does not receive face orientation");
            Require(result.ShaderSource.Contains("AlphaToMask On"), "Alpha-to-coverage state missing");
            Require(result.ShaderSource.Contains("Cull Front") && result.ShaderSource.Contains("Cull Back"), "Back/front transparency passes are missing");
            Require(result.ShaderSource.Contains("NX_"), "Expected generated graph functions are missing");
            Debug.Log("NXSG FACE CONTROL SMOKE PASSED: inspector, face-driven color mix, alpha coverage, and ordered two-pass states");
            window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (window != null) { window.DiscardChanges(); window.Close(); }
            EditorApplication.Exit(1);
        }
    }

    static void Add(ShaderGraph graph, string id, string operation)
    {
        var node = NodeCatalog.Create(operation); node.Id = id; graph.Nodes.Add(node);
        graph.Layout.Nodes[id] = new GraphNodeLayout { X = graph.Nodes.Count * 180, Y = 100 };
    }
    static void Connect(ShaderGraph graph, string from, string output, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
}
