using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod OpaqueDepthSmoke.Run.
public static class OpaqueDepthSmoke
{
    const int Size = 96;
    static Camera camera;
    static RenderTexture sceneTarget;
    static RenderTexture probeTarget;
    static GameObject sphere;
    static GameObject background;
    static Shader probeShader;
    static Material probeMaterial;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required.");

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.transform.position = new Vector3(0, 0, 1);
            background.transform.localScale = Vector3.one * 8;

            var cameraObject = new GameObject("NXSG Opaque Depth Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.farClipPlane = 10;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.targetTexture = sceneTarget = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            sceneTarget.Create();
            probeTarget = new RenderTexture(Size, Size, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            probeTarget.Create();
            probeShader = ShaderUtil.CreateShaderAsset(DepthProbeShader, true);
            probeMaterial = new Material(probeShader);

            Check(useAlbedoAlpha: 0, expectDepth: true);
            Check(useAlbedoAlpha: 1, expectDepth: false);
            Debug.Log("NXSG OPAQUE DEPTH SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            RenderTexture.active = null;
            if (probeMaterial != null) UnityEngine.Object.DestroyImmediate(probeMaterial);
            if (probeShader != null) UnityEngine.Object.DestroyImmediate(probeShader);
            if (sceneTarget != null) { sceneTarget.Release(); UnityEngine.Object.DestroyImmediate(sceneTarget); }
            if (probeTarget != null) { probeTarget.Release(); UnityEngine.Object.DestroyImmediate(probeTarget); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
            if (background != null) UnityEngine.Object.DestroyImmediate(background);
        }
    }

    static void Check(int useAlbedoAlpha, bool expectDepth)
    {
        var graph = Graph(useAlbedoAlpha);
        var emitted = ShaderEmitter.Emit(graph);
        Require(emitted.Succeeded, string.Join("; ", emitted.Diagnostics.Select(d => d.Message)));
        var hasShadowCaster = emitted.ShaderSource.Contains("Name \"ShadowCaster\"");
        Require(hasShadowCaster == expectDepth, "Unexpected ShadowCaster for useAlbedoAlpha=" + useAlbedoAlpha);

        using (var preview = GraphPreview.Create(graph, null))
        {
            sphere.GetComponent<Renderer>().sharedMaterial = preview.Material;
            var probe = new CommandBuffer { name = "NXSG depth sample" };
            probe.Blit(Texture2D.whiteTexture, probeTarget, probeMaterial);
            camera.AddCommandBuffer(CameraEvent.AfterEverything, probe);
            try { camera.Render(); }
            finally { camera.RemoveCommandBuffer(CameraEvent.AfterEverything, probe); probe.Release(); }
            RenderTexture.active = probeTarget;
            var image = new Texture2D(Size, Size, TextureFormat.RFloat, false, true);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            var center = image.GetPixel(Size / 2, Size / 2).r;
            var corner = image.GetPixel(4, 4).r;
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = null;
            if (expectDepth)
                Require(center + .05f < corner, "Sphere did not contribute closer depth: " + center + " / " + corner);
            else
                Require(Mathf.Abs(center - corner) < .05f, "Albedo-alpha shadow caster unexpectedly contributed depth: " + center + " / " + corner);
        }
    }

    static ShaderGraph Graph(int useAlbedoAlpha)
    {
        var graph = new ShaderGraph { GraphId = "opaque-depth-" + useAlbedoAlpha };
        graph.Nodes.Add(ColorNode("a", new JArray(.2, .3, .5, 1)));
        graph.Nodes.Add(ColorNode("b", new JArray(.2, .3, .5, 1)));
        graph.Nodes.Add(new GraphNode { Id = "fresnel", Operation = "core.fresnel", Properties = new JObject { ["power"] = 5 } });
        graph.Nodes.Add(new GraphNode { Id = "mix", Operation = "core.mix" });
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.pbrSurface", Properties = new JObject { ["opacity"] = 1, ["useAlbedoAlpha"] = useAlbedoAlpha } });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "a", "value", "mix", "a"); Edge(graph, "b", "value", "mix", "b");
        Edge(graph, "fresnel", "value", "mix", "factor"); Edge(graph, "mix", "value", "surface", "albedo");
        Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static GraphNode ColorNode(string id, JToken value)
    {
        return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } };
    }

    static void Edge(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    const string DepthProbeShader = @"
Shader ""Hidden/NXSGDepthProbe"" {
 SubShader { Cull Off ZWrite Off ZTest Always
  Pass { CGPROGRAM
   #pragma vertex vert_img
   #pragma fragment frag
   #include ""UnityCG.cginc""
   UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
   fixed4 frag(v2f_img input) : SV_Target {
    return Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, input.uv));
   }
  ENDCG }
 }
}";
}
