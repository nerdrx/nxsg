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
            Invoke("SelectNode", "core.gem", false);
            var gemInspector = (VisualElement)Field("inspector");
            foreach (var property in new[] { "sparkleStrength", "sparkleDensity", "sparkleSize", "sparkleDepth" })
                Require(gemInspector.Q<FloatField>("node-property-" + property) != null, "Missing Gem sparkle control: " + property);
            gemInspector.Q<FloatField>("node-property-sparkleStrength").value = 2.5f;
            Require((double)graph.Nodes.Single(n => n.Id == "core.gem").Properties["sparkleStrength"] == 2.5, "Gem strength edit did not save");
            var sparkleInput = NodeCatalog.Create("core.value"); sparkleInput.Id = "sparkle-input"; graph.Nodes.Add(sparkleInput);
            graph.Connections.Add(new GraphConnection { Id = "sparkle-edge", From = new GraphPortRef { NodeId = sparkleInput.Id, PortId = "value" }, To = new GraphPortRef { NodeId = "core.gem", PortId = "sparkleStrength" } });
            Invoke("Rebuild"); Invoke("SelectNode", "core.gem", false);
            Require(!((VisualElement)Field("inspector")).Q<FloatField>("node-property-sparkleStrength").enabledInHierarchy, "Connected Gem strength field stays editable");
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
