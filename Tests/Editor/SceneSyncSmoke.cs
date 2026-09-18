using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;

// Run in an isolated graphics-enabled Unity project with -executeMethod SceneSyncSmoke.Run.
public static class SceneSyncSmoke
{
    const string GraphPath = "Assets/SceneSyncSmoke.nxsg";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        GraphWindow window = null;
        string generated = null;
        try
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/dev.nerdrx.nxsg");
            Require(package != null, "NXSG package was not resolved");
            var sample = Path.Combine(package.resolvedPath, "Samples~/Animated Palette.nxsg");
            File.WriteAllText(Path.Combine(Application.dataPath, "SceneSyncSmoke.nxsg"), File.ReadAllText(sample));
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var graph = GraphJson.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "SceneSyncSmoke.nxsg")));
            var absolute = Path.Combine(Application.dataPath, "SceneSyncSmoke.nxsg");
            var material = GraphBuild.Build(graph, absolute);
            var graphGuid = AssetDatabase.AssetPathToGUID(GraphPath);
            generated = "Assets/NXSGGenerated/" + graphGuid;
            var shaderPath = generated + "/Material.shader";
            var materialPath = generated + "/Material.mat";
            var shaderGuid = AssetDatabase.AssetPathToGUID(shaderPath);
            var materialShaderGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material.shader));
            Require(shaderGuid == materialShaderGuid, "initial material shader identity mismatch");

            GraphWindow.Open(absolute);
            window = Resources.FindObjectsOfTypeAll<GraphWindow>().FirstOrDefault();
            Require(window != null, "GraphWindow did not open");
            window.CreateGUI();
            Set(window, "autoScene", true);
            var liveGraph = (ShaderGraph)Get(window, "graph");
            var constant = liveGraph.Nodes.First(n => n.Operation == "core.constant" &&
                (string)n.Properties["valueType"] == "color");
            constant.Properties["value"] = new JArray(.17f, .63f, .91f, 1f);
            // UI curve keys are floats, unlike JSON values loaded from disk (doubles).
            liveGraph.Nodes.Add(new GraphNode { Id = "float-ramp", Operation = "core.ramp", Properties = new JObject
            {
                ["points"] = new JArray(new JArray(0f, 0f), new JArray(.311698139f, .777249753f), new JArray(1f, .512882233f))
            }});
            Invoke(window, "QueueSceneUpdate");
            Set(window, "sceneDue", 0d);
            Invoke(window, "UpdateScene");

            var expectedHash = GraphJson.ComputeSemanticHash(liveGraph);
            Require(ReadHash(shaderPath) == "// NXSG graph hash: " + expectedHash,
                "scene shader hash does not match queued graph");
            Require(AssetDatabase.AssetPathToGUID(shaderPath) == shaderGuid, "scene rebuild changed shader GUID");
            Require(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(
                AssetDatabase.LoadAssetAtPath<Material>(materialPath).shader)) == shaderGuid,
                "scene material does not use existing shader asset");
            Require(Status(window).IndexOf("up to date", StringComparison.OrdinalIgnoreCase) >= 0,
                "successful scene update did not report up to date");

            liveGraph.Layout.Nodes[liveGraph.Nodes[0].Id].X += 10;
            Invoke(window, "QueueSceneUpdate");
            Require(!(bool)Get(window, "scenePending"), "layout-only edit queued scene rebuild");

            var lastShader = File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + shaderPath));
            constant.Properties["value"] = "invalid-color";
            Invoke(window, "QueueSceneUpdate");
            Set(window, "sceneDue", 0d);
            Invoke(window, "UpdateScene");
            Require(lastShader.SequenceEqual(File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + shaderPath))),
                "invalid scene update replaced last successful shader");
            Require(Status(window).IndexOf("needs attention", StringComparison.OrdinalIgnoreCase) >= 0,
                "invalid scene update did not report needs attention");

            Require(window.SaveGraph(), "retry save failed");
            Require((bool)Get(window, "scenePending"), "Save did not retry failed scene update");
            Set(window, "sceneDue", 0d);
            Invoke(window, "UpdateScene");

            Set(window, "autoScene", false);
            constant.Properties["value"] = new JArray(.91, .22, .14, 1);
            Invoke(window, "QueueSceneUpdate");
            Set(window, "sceneDue", 0d);
            Invoke(window, "UpdateScene");
            Require(lastShader.SequenceEqual(File.ReadAllBytes(Path.Combine(Application.dataPath, "../" + shaderPath))),
                "autoScene=false still rebuilt shader");
            Require(Status(window).IndexOf("up to date", StringComparison.OrdinalIgnoreCase) < 0,
                "autoScene=false reported scene up to date");
            Debug.Log("NXSG SCENE SYNC SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (window != null) window.Close();
            AssetDatabase.DeleteAsset(GraphPath);
            if (generated != null) AssetDatabase.DeleteAsset(generated);
            AssetDatabase.Refresh();
        }
    }

    static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    static string Status(GraphWindow window) => ((Label)Get(window, "sceneStatus")).text;
    static string ReadHash(string assetPath) => File.ReadLines(Path.Combine(Application.dataPath, "../" + assetPath)).First();
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
