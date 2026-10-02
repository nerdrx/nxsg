using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class AvatarScaleSmoke
{
    public static void Run()
    {
        const string shaderPath = "Assets/SmokeResults/AvatarScale.shader";
        const string controllerPath = "Assets/SmokeResults/AvatarScale.controller";
        GameObject root = null;
        Material material = null;
        try
        {
            var graph = new ShaderGraph { GraphId = "avatar-scale-smoke" };
            var color = NodeCatalog.Create("core.constant"); color.Properties["value"] = new JArray(1, 1, 1, 1);
            var surface = NodeCatalog.Create("core.unlitSurface");
            var particles = NodeCatalog.Create("core.surfaceParticles");
            var scale = NodeCatalog.Create("core.avatarScaleFactor");
            var output = NodeCatalog.Create("core.output");
            graph.Nodes.AddRange(new[] { color, surface, particles, scale, output });
            Connect(graph, color, "value", surface, "albedo");
            Connect(graph, surface, "surface", particles, "base");
            Connect(graph, scale, "value", particles, "opacity");
            Connect(graph, particles, "surface", output, "surface");
            if (NodeCatalog.Category(scale.Operation) != "External") throw new Exception("Scale Factor missing from External nodes.");
            var emitted = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = "NXSG/Smoke/AvatarScale" });
            if (!emitted.Succeeded) throw new Exception(string.Join("\n", emitted.Diagnostics));
            if (!emitted.ShaderSource.Contains("_NXSG_AvatarScaleFactor (\"Avatar scale factor\", Float) = 1") ||
                !emitted.ShaderSource.Contains("float _NXSG_AvatarScaleFactor;")) throw new Exception("Scale Factor shader property missing.");
            Directory.CreateDirectory("Assets/SmokeResults");
            File.WriteAllText(shaderPath, emitted.ShaderSource);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
            if (!material.HasProperty(AvatarScaleSetupWindow.Property) || Math.Abs(material.GetFloat(AvatarScaleSetupWindow.Property) - 1) > .001)
                throw new Exception("Scale Factor did not default to 1.");

            root = new GameObject("Avatar");
            var child = new GameObject("Body"); child.transform.SetParent(root.transform);
            var renderer = child.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMaterial = material;
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var configure = typeof(AvatarScaleSetupWindow).GetMethod("ConfigureController", BindingFlags.Static | BindingFlags.NonPublic);
            configure.Invoke(null, new object[] { controller, root.transform, new[] { renderer }, true });
            configure.Invoke(null, new object[] { controller, root.transform, new[] { renderer }, false });
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceSynchronousImport);
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller.layers.Length != 1 || controller.parameters.Length != 1 || controller.parameters[0].name != "ScaleFactor")
                throw new Exception("Scale Factor controller shape is wrong.");
            var animator = root.AddComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0);
            animator.SetFloat("ScaleFactor", 2); animator.Update(.1f);
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            if (Math.Abs(block.GetFloat(AvatarScaleSetupWindow.Property) - 2) > .02)
                throw new Exception("ScaleFactor did not animate the skinned material: " + block.GetFloat(AvatarScaleSetupWindow.Property));
            Debug.Log("NXSG AVATAR SCALE SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            AssetDatabase.DeleteAsset(controllerPath); AssetDatabase.DeleteAsset(shaderPath);
        }
    }

    static void Connect(ShaderGraph graph, GraphNode from, string fromPort, GraphNode to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"),
            From = new GraphPortRef { NodeId = from.Id, PortId = fromPort }, To = new GraphPortRef { NodeId = to.Id, PortId = toPort } });
    }
}
