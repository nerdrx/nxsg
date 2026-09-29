using System;
using System.IO;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;

// Run in a hidden graphics-enabled Unity 2022.3 editor.
public static class ConstellationPathingRenderSmoke
{
    const int Size = 128;
    static Camera camera;
    static Renderer quad;
    static RenderTexture target;

    public static void Run()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
            camera = new GameObject("NXSG path effect camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -2);
            camera.orthographic = true;
            camera.orthographicSize = .5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;

            var sampleRoot = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShaderEmitter).Assembly).resolvedPath, "Samples~");
            CheckConstellation(GraphJson.Parse(File.ReadAllText(Path.Combine(sampleRoot, "Constellation.nxsg"))));
            CheckPathing(GraphJson.Parse(File.ReadAllText(Path.Combine(sampleRoot, "Pathing.nxsg"))));
            Debug.Log("NXSG CONSTELLATION PATHING RENDER PASSED: visible output, time response, audio and mask controls");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (quad != null) UnityEngine.Object.DestroyImmediate(quad.gameObject);
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }

    static void CheckConstellation(ShaderGraph graph)
    {
        var node = graph.Nodes.Single(n => n.Operation == ConstellationNodes.Operation);
        node.Properties["scale"] = 8;
        node.Properties["pointSize"] = .18;
        node.Properties["lineWidth"] = .04;
        node.Properties["linkChance"] = 1;
        node.Properties["twinkle"] = 0;
        var clock = AddClock(graph, node);
        node.Properties["audio"] = 0;
        var silent = Render(graph);
        node.Properties["audio"] = 1;
        var visible = Render(graph);
        Require(Difference(visible, silent) > .01f, "Constellation points and links were not visible");
        node.Properties["twinkle"] = 1;
        clock.Properties["value"] = 0;
        var before = Render(graph);
        clock.Properties["value"] = 2;
        Require(Difference(before, Render(graph)) > .002f, "Constellation did not animate with Time");
    }

    static void CheckPathing(ShaderGraph graph)
    {
        var node = graph.Nodes.Single(n => n.Operation == PathingNodes.Operation);
        var clock = AddClock(graph, node);
        node.Properties["mask"] = 0;
        var masked = Render(graph);
        node.Properties["mask"] = 1;
        node.Properties["travel"] = 0;
        var full = Render(graph);
        Require(Difference(full, masked) > .01f, "Pathing lanes were not visible");
        node.Properties["travel"] = 1;
        clock.Properties["value"] = 0;
        var moving = Render(graph);
        Require(Difference(full, moving) > .003f, "Pathing Travel did not change the lanes");
        clock.Properties["value"] = 1.3;
        Require(Difference(moving, Render(graph)) > .002f, "Pathing did not animate with Time");
        node.Properties["audio"] = 0;
        Require(Difference(Render(graph), masked) < .002f, "Pathing Audio zero did not suppress the effect");
    }

    static GraphNode AddClock(ShaderGraph graph, GraphNode effect)
    {
        var clock = NodeCatalog.Create("core.value");
        clock.Id = "render-clock";
        clock.Properties["value"] = 0;
        graph.Nodes.Add(clock);
        graph.Connections.Add(new GraphConnection { Id = "render-clock-time",
            From = new GraphPortRef { NodeId = clock.Id, PortId = "value" },
            To = new GraphPortRef { NodeId = effect.Id, PortId = "time" } });
        return clock;
    }

    static Color[] Render(ShaderGraph graph)
    {
        var emitted = ShaderEmitter.Emit(graph);
        Require(emitted.Succeeded, string.Join(";", emitted.Diagnostics.Select(d => d.Message)));
        var shader = ShaderUtil.CreateShaderAsset(emitted.ShaderSource, true);
        var material = new Material(shader);
        quad.sharedMaterial = material;
        try
        {
            camera.Render(); camera.Render();
            Require(shader.isSupported && !ShaderUtil.ShaderHasError(shader),
                string.Join(";", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try
            {
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply();
                var pixels = image.GetPixels();
                Require(pixels.All(p => !float.IsNaN(p.r) && !float.IsInfinity(p.r)), "Rendered pixels are not finite");
                return pixels;
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }
        finally { UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(shader); }
    }

    static float Difference(Color[] first, Color[] second) => first.Zip(second,
        (a, b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)).Average();
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
