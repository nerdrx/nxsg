using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in an isolated graphics-enabled Unity project with -executeMethod ColorRenderSmoke.Run.
public static class ColorRenderSmoke
{
    const string ShaderPath = "Assets/ColorCheck.shader";

    public static void Run()
    {
        Material material = null;
        GameObject subject = null, cameraObject = null, lightObject = null;
        RenderTexture target = null;
        Texture2D capture = null;
        var oldAmbientMode = RenderSettings.ambientMode;
        var oldAmbientLight = RenderSettings.ambientLight;
        var oldAmbientIntensity = RenderSettings.ambientIntensity;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required for color render smoke.");

            var graph = CreateGraph();
            var emission = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = "NXSG/ColorCheck" });
            if (!emission.Succeeded)
                throw new InvalidOperationException(string.Join("; ", emission.Diagnostics.Select(d => d.Message)));

            File.WriteAllText(Path.Combine(Application.dataPath, "ColorCheck.shader"), emission.ShaderSource);
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("ColorCheck shader failed import or compilation.");

            ShaderUtil.allowAsyncCompilation = false;
            material = new Material(shader) { name = "NXSG Color Check" };
            material.SetColor("_Color", Color.white);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.ambientIntensity = 1f;

            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            subject.GetComponent<Renderer>().sharedMaterial = material;
            lightObject = new GameObject("NXSG Color Check Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

            cameraObject = new GameObject("NXSG Color Check Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0f, 0f, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            capture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            capture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            capture.Apply();
            var center = capture.GetPixel(128, 128);
            if (center.r <= .1f || center.g >= center.r * .15f || center.b >= center.r * .15f)
                throw new InvalidOperationException("Rendered center pixel is not red: " + center);

            File.WriteAllBytes(Path.Combine(Application.dataPath, "ColorCheck.png"), capture.EncodeToPNG());
            Debug.Log("NXSG COLOR RENDER CHECK PASSED: " + center);
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
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbientLight;
            RenderSettings.ambientIntensity = oldAmbientIntensity;
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
            if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            AssetDatabase.DeleteAsset(ShaderPath);
        }
    }

    static ShaderGraph CreateGraph()
    {
        var graph = new ShaderGraph { GraphId = "color-render-check" };
        graph.Nodes.Add(new GraphNode { Id = "red", Operation = "core.constant", Properties = ColorProperties(1, 0, 0, 1) });
        graph.Nodes.Add(new GraphNode { Id = "tint", Operation = "core.constant", Properties = ColorProperties(.5, 1, 1, 1) });
        graph.Nodes.Add(new GraphNode { Id = "multiply", Operation = "core.multiply", Properties = new JObject { ["valueType"] = "color" } });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Connect(graph, "red", "value", "multiply", "a", "red-multiply");
        Connect(graph, "tint", "value", "multiply", "b", "tint-multiply");
        Connect(graph, "multiply", "value", "toon", "albedo", "multiply-toon");
        Connect(graph, "toon", "surface", "output", "surface", "toon-output");
        return graph;
    }

    static JObject ColorProperties(double r, double g, double b, double a)
    {
        return new JObject { ["valueType"] = "color", ["value"] = new JArray(r, g, b, a) };
    }

    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = id,
            From = new GraphPortRef { NodeId = from, PortId = fromPort },
            To = new GraphPortRef { NodeId = to, PortId = toPort }
        });
    }
}
