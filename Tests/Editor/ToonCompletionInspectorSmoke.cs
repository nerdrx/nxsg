using System;
using System.Collections.Generic;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ToonCompletionInspectorSmoke
{
    public static void Run()
    {
        GraphWindow window = null;
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.CreateGUI();
            var surface = NodeCatalog.Create("core.toonSurface"); surface.Id = "surface"; surface.Properties["lightingMode"] = 3;
            var graph = new ShaderGraph { GraphId = "toon-inspector-smoke" };
            graph.Nodes.Add(surface);
            Set(window, "graph", graph);
            Set(window, "selected", surface.Id);
            Set(window, "selection", new List<string> { surface.Id });
            typeof(GraphWindow).GetMethod("RebuildInspector", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            foreach (var property in new[]
            {
                "layerReceiveShadow", "layerReceiveShadow2", "layerReceiveShadow3",
                "rimStrength", "rimWidth", "rimSoftness", "rimLightAlignment"
            })
                Require(window.rootVisualElement.Q<FloatField>("node-property-" + property) != null, "Inspector is missing numeric control " + property);
            Require(window.rootVisualElement.Q<Label>(null) != null, "Inspector failed to build labels");
            Debug.Log("NXSG TOON COMPLETION INSPECTOR SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
    }

    static void Set(GraphWindow window, string field, object value)
    {
        typeof(GraphWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
    }

    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
