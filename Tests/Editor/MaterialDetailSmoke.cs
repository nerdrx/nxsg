using System;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Run in an isolated Unity project with NXSG installed.
public static class MaterialDetailSmoke
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static void Run()
    {
        try
        {
            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 1100, 800);
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            Invoke("NewGraph");
            var graph = (ShaderGraph)Field("graph"); graph.Nodes.Clear(); graph.Connections.Clear();
            foreach (var operation in new[] { "core.sdfFaceShadow", "core.depthRim", "core.gem" })
            {
                var node = NodeCatalog.Create(operation); Require(node != null, "Missing " + operation);
                node.Id = operation; graph.Nodes.Add(node);
            }
            Invoke("Rebuild");
            foreach (var operation in new[] { "core.sdfFaceShadow", "core.depthRim", "core.gem" })
            {
                Invoke("SelectNode", operation, false);
                var inspector = (VisualElement)Field("inspector");
                Require(inspector.Query<Label>().ToList().Any(label => label.text == NodeCatalog.Description(operation)), operation + " description missing");
                Require(inspector.Query<FloatField>().ToList().Count > 0, operation + " numeric controls missing");
                Require(inspector.Query<Button>().ToList().Any(button => button.text == "Preview selected node"), operation + " preview action missing");
            }
            Invoke("SelectNode", "core.sdfFaceShadow", false);
            var face = (VisualElement)Field("inspector");
            Require(face.Query<PopupField<string>>().ToList().Any(field => field.label == "Basis"), "Face basis selector missing");
            Debug.Log("NXSG MATERIAL DETAIL EDITOR SMOKE PASSED");
            window.DiscardChanges(); window.Close(); EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            if (window != null) { window.DiscardChanges(); window.Close(); }
            Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
