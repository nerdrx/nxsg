using System;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Backend;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

public static class ParameterAuthoringSmoke
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Get(GraphWindow window, string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(GraphWindow window, string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);

    public static void Run()
    {
        var window = ScriptableObject.CreateInstance<GraphWindow>();
        try
        {
            window.Show();
            Invoke(window, "NewGraph");
            Invoke(window, "AddParameter", GraphValueType.Float);
            var graph = (ShaderGraph)Get(window, "graph");
            if (graph.Parameters.Count != 1 || graph.Parameters[0].Type != GraphValueType.Float) throw new Exception("Float parameter was not created.");
            var id = graph.Parameters[0].Id;
            Invoke(window, "Edit", "Rename smoke parameter", new Action(() => graph.Parameters[0].Name = "Speed"));
            var roundTrip = GraphJson.Parse(((GraphSession)Get(window, "session")).json);
            if (roundTrip.Parameters.Count != 1 || roundTrip.Parameters[0].Id != id || roundTrip.Parameters[0].Name != "Speed") throw new Exception("Parameter identity did not survive serialization.");
            Undo.IncrementCurrentGroup();
            Invoke(window, "AddParameterNode", id);
            if (graph.Nodes.FindAll(n => n.Operation == "core.parameter").Count != 1) throw new Exception("Parameter node was not created.");
            var parameterNode = graph.Nodes.Single(n => n.Operation == "core.parameter");
            if (GraphTypes.PortType(graph, parameterNode, "value") != "float") throw new Exception("Parameter output type was not inferred.");
            graph.Connections.Add(new GraphConnection { Id = "parameter-smoke", From = new GraphPortRef { NodeId = parameterNode.Id, PortId = "value" }, To = new GraphPortRef { NodeId = "toon", PortId = "emission" } });
            var emitted = ShaderEmitter.Emit(graph);
            if (!emitted.Succeeded) throw new Exception("Parameter graph did not emit: " + string.Join("; ", emitted.Diagnostics.Select(d => d.Message)));
            var property = emitted.Properties.SingleOrDefault(p => p.ParameterId == id);
            if (property == null || property.Name != "_NXSG_P_" + id.Replace('-', '_') || property.DisplayName != "Speed") throw new Exception("Generated parameter property mismatch.");
            Invoke(window, "DeleteParameter", graph.Parameters[0]);
            if (graph.Parameters.Count != 1) throw new Exception("Referenced parameter was deleted.");
            Undo.PerformUndo();
            graph = (ShaderGraph)Get(window, "graph");
            if (graph.Nodes.Any(n => n.Operation == "core.parameter")) throw new Exception("Undo did not remove the added parameter node.");
            Undo.PerformRedo();
            graph = (ShaderGraph)Get(window, "graph");
            if (graph.Parameters.Count != 1 || graph.Nodes.All(n => n.Operation != "core.parameter")) throw new Exception("Redo did not restore parameter node state.");
            Debug.Log("NXSG PARAMETER AUTHORING SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally { if (window != null) { window.DiscardChanges(); window.Close(); } }
    }
}
