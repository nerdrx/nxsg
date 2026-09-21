using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod LightingInfluenceSmoke.Run.
public static class LightingInfluenceSmoke
{
    const int Size = 96;
    static Camera camera;
    static RenderTexture target;
    static GameObject subject;

    public static void Run()
    {
        var oldReflection = RenderSettings.reflectionIntensity;
        var oldProbe = RenderSettings.ambientProbe;
        var oldAmbientMode = RenderSettings.ambientMode;
        var oldAmbientLight = RenderSettings.ambientLight;
        var oldAmbientIntensity = RenderSettings.ambientIntensity;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            RenderSettings.ambientProbe = new SphericalHarmonicsL2();
            RenderSettings.reflectionIntensity = 0;
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var cameraObject = new GameObject("NXSG Lighting Influence Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.targetTexture = target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();

            foreach (var operation in new[] { "core.toonSurface", "core.pbrSurface" })
            {
                CheckSaturation(operation);
                CheckMinimum(operation);
                CheckMaximum(operation);
                CheckEmission(operation);
            }
            Debug.Log("NXSG LIGHTING INFLUENCE SMOKE PASSED");
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
            RenderSettings.reflectionIntensity = oldReflection;
            RenderSettings.ambientProbe = oldProbe;
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbientLight;
            RenderSettings.ambientIntensity = oldAmbientIntensity;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
        }
    }

    static void CheckSaturation(string operation)
    {
        var light = AddDirectional(new Color(1f, .22f, .06f), 2f);
        try
        {
            var neutral = Render(operation, 0, 0, 0);
            var colored = Render(operation, 0, 0, 1);
            Require(Luminance(neutral) > .02f, operation + " saturation baseline is black: " + neutral);
            Require(Mathf.Max(neutral.r, Mathf.Max(neutral.g, neutral.b)) - Mathf.Min(neutral.r, Mathf.Min(neutral.g, neutral.b)) < .1f,
                operation + " saturation 0 did not neutralize light: " + neutral);
            Require(colored.r > colored.g + .04f || colored.r > colored.b + .04f,
                operation + " saturation 1 lost light color: " + colored);
        }
        finally { UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckMinimum(string operation)
    {
        var zero = Render(operation, 0, 0, 1);
        var clamped = Render(operation, .3f, 0, 1);
        Require(Luminance(zero) < .03f, operation + " no-light baseline was not dark: " + zero);
        Require(Luminance(clamped) > Luminance(zero) + .015f && Luminance(clamped) > .03f,
            operation + " lighting minimum did not lift dark surface: " + zero + " / " + clamped);
        if(operation=="core.pbrSurface")
        {
            var metalDark=Render(operation,0,0,1,null,1);
            var metalLift=Render(operation,.3,0,1,null,1);
            Require(Luminance(metalLift)>Luminance(metalDark)+.015f,"Metallic minimum has no effect");
        }
    }

    static void CheckMaximum(string operation)
    {
        var light = AddDirectional(Color.white, 8f);
        try
        {
            var unlimited = Render(operation, 0, 0, 1);
            var limited = Render(operation, 0, .1f, 1);
            Require(Luminance(unlimited) > .05f, operation + " high-light baseline is black: " + unlimited);
            Require(Luminance(limited) < Luminance(unlimited) * .85f,
                operation + " lighting maximum did not dim high light: " + unlimited + " / " + limited);
        }
        finally { UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckEmission(string operation)
    {
        var first = Render(operation, 0, 0, 0, new Color(.8f, .2f, .05f, 1));
        var second = Render(operation, 0, 0, 1, new Color(.8f, .2f, .05f, 1));
        Require(Luminance(first) > .03f && Vector4.Distance(first, second) < .04f,
            operation + " emission changed with lighting saturation: " + first + " / " + second);
    }

    static Color Render(string operation, double minimum, double maximum, double saturation, Color? emission = null, double metallic = 0)
    {
        var graph = Surface(operation, minimum, maximum, saturation, emission, metallic);
        using (var preview = GraphPreview.Create(graph, null))
        {
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            var result = image.GetPixel(Size / 2, Size / 2);
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = null;
            return result;
        }
    }

    static ShaderGraph Surface(string operation, double minimum, double maximum, double saturation, Color? emission, double metallic)
    {
        var graph = new ShaderGraph { GraphId = "lighting-influence-" + operation };
        var surface = NodeCatalog.Create(operation); surface.Id = "surface";
        surface.Properties["lightingMin"] = minimum;
        surface.Properties["lightingMax"] = maximum;
        surface.Properties["lightingSaturation"] = saturation;
        if (operation == "core.pbrSurface") { surface.Properties["metallic"] = metallic; surface.Properties["roughness"] = .9; }
        graph.Nodes.Add(surface);
        Add(graph, ValueColor("albedo", Color.white));
        if (emission.HasValue) Add(graph, ValueColor("emission", emission.Value));
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; Add(graph, output);
        Connect(graph, "albedo", "value", "surface", "albedo", "albedo");
        if (emission.HasValue) Connect(graph, "emission", "value", "surface", "emission", "emission");
        Connect(graph, "surface", "surface", "output", "surface", "output");
        return graph;
    }

    static GraphNode ValueColor(string id, Color value)
    {
        return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject {
            ["valueType"] = "color", ["value"] = new JArray(value.r, value.g, value.b, value.a) } };
    }

    static Light AddDirectional(Color color, float intensity)
    {
        var light = new GameObject("NXSG Lighting Influence Light").AddComponent<Light>();
        light.type = LightType.Directional; light.color = color; light.intensity = intensity;
        light.renderMode = LightRenderMode.ForcePixel; light.transform.rotation = Quaternion.Euler(25, -25, 0);
        return light;
    }

    static float Luminance(Color color) { return color.r * .2126f + color.g * .7152f + color.b * .0722f; }
    static void Add(ShaderGraph graph, GraphNode node) { graph.Nodes.Add(node); }
    static void Connect(ShaderGraph graph, string from, string port, string to, string input, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
