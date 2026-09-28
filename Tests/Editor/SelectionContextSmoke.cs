using System;
using System.IO;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Run in the hidden graphics-enabled Editor with -executeMethod SelectionContextSmoke.Run.
public static class SelectionContextSmoke
{
    const string GraphPath = "Assets/SelectionContextSmoke.nxsg";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        GraphWindow window = null;
        Material unrelated = null;
        string generated = null;
        try
        {
            var graph = GraphSamples.CreateDefault();
            File.WriteAllText(GraphPath, GraphJson.Serialize(graph, true));
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var material = GraphBuild.Build(graph, Path.GetFullPath(GraphPath));
            generated = "Assets/NXSGGenerated/" + AssetDatabase.AssetPathToGUID(GraphPath);

            window = ScriptableObject.CreateInstance<GraphWindow>();
            window.Show();
            window.CreateGUI();
            Set(window, "livePreview", true);
            Set(window, "graph", graph);
            Set(window, "sourcePath", Path.GetFullPath(GraphPath));
            Set(window, "contextMaterial", material);
            Invoke(window, "UpdateIdentity");
            var previousPreview = new Material(Shader.Find("Unlit/Color")) { hideFlags = HideFlags.HideAndDontSave };
            Set(window, "preview", previousPreview);

            unrelated = new Material(Shader.Find("Unlit/Color"));
            Selection.activeObject = unrelated;
            Invoke(window, "OnSelectionChange");
            Require(Get(window, "contextMaterial") == material, "unrelated selection discarded the valid graph material context");
            Require(Get(window, "preview") == previousPreview, "unrelated selection needlessly discarded the valid preview");

            Set(window, "previewPending", false);
            material.shader = Shader.Find("Unlit/Color");
            Invoke(window, "OnSelectionChange");
            Require(Get(window, "contextMaterial") == null, "reassigned shader remained linked to the graph");
            Require(Get(window, "preview") == null, "preview still reflects the old graph material context");
            Require((bool)Get(window, "previewPending"), "neutral graph preview was not queued");
            Debug.Log("NXSG SELECTION CONTEXT SMOKE PASSED");
            Finish(window, unrelated, generated, 0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(window, unrelated, generated, 1);
        }
    }

    static object Get(GraphWindow window, string name) => typeof(GraphWindow).GetField(name, Flags).GetValue(window);
    static void Set(GraphWindow window, string name, object value) => typeof(GraphWindow).GetField(name, Flags).SetValue(window, value);
    static void Invoke(GraphWindow window, string name) => typeof(GraphWindow).GetMethod(name, Flags).Invoke(window, null);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Finish(GraphWindow window, Material unrelated, string generated, int exitCode)
    {
        Selection.activeObject = null;
        if (window != null) { window.DiscardChanges(); window.Close(); }
        if (unrelated != null) UnityEngine.Object.DestroyImmediate(unrelated);
        AssetDatabase.DeleteAsset(GraphPath);
        if (!string.IsNullOrEmpty(generated)) AssetDatabase.DeleteAsset(generated);
        AssetDatabase.Refresh();
        EditorApplication.Exit(exitCode);
    }
}
