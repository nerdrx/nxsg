using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// Run in the hidden graphics fixture with -executeMethod IndirectLightingSmoke.Run.
// Exports graphs to work/indirect-lighting-d3d-graphs for D3DCompileSmoke.
public static class IndirectLightingSmoke
{
    const int Size = 96;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static Camera camera;
    static GameObject subject, lightObject;
    static RenderTexture target;
    static GraphWindow window;
    static AmbientMode oldAmbientMode;
    static Color oldAmbientLight;
    static float oldAmbientIntensity, oldReflectionIntensity;
    static SphericalHarmonicsL2 oldAmbientProbe;
    static bool renderSettingsSaved;
    static int exportIndex;

    public static void Run()
    {
        try
        {
            SaveRenderSettings();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            InspectControls();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0;
            RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.reflectionIntensity = 0;
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var cameraObject = new GameObject("NXSG Indirect Lighting Camera"); camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.allowHDR = true;
            camera.transform.position = new Vector3(0, 0, -3.5f); camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            lightObject = new GameObject("NXSG Indirect Lighting Key"); var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2; light.renderMode = LightRenderMode.ForcePixel;
            light.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

            foreach (var ao in new[] { 0.0, 1.0 })
            foreach (var roughness in new[] { 0.0, 1.0 })
            {
                var endpoint = Surface(); var surface = endpoint.Nodes.Single(node => node.Id == "surface");
                surface.Properties["occlusion"] = ao; surface.Properties["roughness"] = roughness;
                AddBentNormal(endpoint, 0, 1, 0);
                RequireFinite(Capture(endpoint));
            }

            var bent = Surface(); AddBentNormal(bent, .25, .75, .6);
            var bentPixels = RequireFinite(Capture(bent));
            Require(bentPixels.Length == Size * Size, "Bent normal render did not return the full target");

            subject.transform.rotation = Quaternion.Euler(0, 55, 0);
            var worldDirection = Surface(); SetDirection(worldDirection, 1, 0, 0, -1);
            var objectDirection = Surface(); SetDirection(objectDirection, 1, 1, 0, -1);
            var worldImage = RequireFinite(Capture(worldDirection)); var objectImage = RequireFinite(Capture(objectDirection));
            Require(Changed(worldImage, objectImage) > 8, "Rotating the mesh did not distinguish object-space from world-space light direction");

            subject.transform.rotation = Quaternion.identity;
            var defaultLight = RequireFinite(Capture(Surface()));
            var overriddenLight = Surface(); SetDirection(overriddenLight, 1, 0, 0, -1);
            var overrideImage = RequireFinite(Capture(overriddenLight));
            Require(Changed(defaultLight, overrideImage) > 8, "Light-direction override did not change rendered direct lighting");

            var emissionOnly = Surface(); SetAlbedo(emissionOnly, 0, 0, 0, 1); SetEmission(emissionOnly, .2, .5, .8, 1);
            var emissionSurface = emissionOnly.Nodes.Single(node => node.Id == "surface");
            emissionSurface.Properties["metallic"] = 1; emissionSurface.Properties["occlusion"] = 0; emissionSurface.Properties["shadow"] = 0;
            var emissionDefault = RequireFinite(Capture(emissionOnly));
            SetDirection(emissionOnly, 1, 0, 0, -1);
            var emissionOverridden = RequireFinite(Capture(emissionOnly));
            Require(Changed(emissionDefault, emissionOverridden) == 0, "Light-direction override changed emission-only pixels");

            Debug.Log("NXSG INDIRECT LIGHTING SMOKE PASSED: connected tangent bent normal, AO/roughness endpoints, object/world direction, direct attenuation, independent emission");
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    static void InspectControls()
    {
        foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
        window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 1100, 800);
        typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
        typeof(GraphWindow).GetMethod("NewGraph", Private).Invoke(window, null);
        var graph = (ShaderGraph)typeof(GraphWindow).GetField("graph", Private).GetValue(window);
        graph.Nodes.Clear(); graph.Connections.Clear(); graph.Nodes.Add(NodeCatalog.Create("core.pbrSurface").WithIndirectId("surface"));
        graph.Nodes.Add(NodeCatalog.Create("core.output").WithIndirectId("output"));
        Link(graph, "surface", "surface", "output", "surface");
        typeof(GraphWindow).GetMethod("Rebuild", Private).Invoke(window, null);
        typeof(GraphWindow).GetMethod("SelectNode", Private).Invoke(window, new object[] { "surface", false });
        var inspector = (VisualElement)typeof(GraphWindow).GetField("inspector", Private).GetValue(window);
        Require(inspector.Q<FloatField>("node-property-bentStrength") != null, "Bent normal influence control missing");
        Require(inspector.Q<FloatField>("node-property-lightDirectionStrength") != null, "Light direction override control missing");
        Require(inspector.Query<PopupField<string>>().ToList().Any(field => field.label == "Direction space"), "Direction space control missing");
        window.DiscardChanges(); window.Close(); window = null;
    }

    static void SaveRenderSettings()
    {
        oldAmbientMode = RenderSettings.ambientMode; oldAmbientLight = RenderSettings.ambientLight; oldAmbientIntensity = RenderSettings.ambientIntensity;
        oldAmbientProbe = RenderSettings.ambientProbe; oldReflectionIntensity = RenderSettings.reflectionIntensity;
        renderSettingsSaved = true;
    }
    static Color[] Capture(ShaderGraph graph)
    {
        var emitted = ShaderEmitter.Emit(graph);
        if (!emitted.Succeeded) throw new InvalidOperationException("Indirect graph failed to emit: " + string.Join("; ", emitted.Diagnostics.Select(diagnostic => diagnostic.Message)));
        if (emitted.ShaderSource.IndexOf("UNITY_LIGHT_ATTENUATION(atten,input,input.ws);", StringComparison.Ordinal) >= 0 &&
            emitted.ShaderSource.IndexOf("NX_OverrideLightDirection(lightDir", StringComparison.Ordinal) >= 0)
        {
            var attenuation = emitted.ShaderSource.IndexOf("UNITY_LIGHT_ATTENUATION(atten,input,input.ws);", StringComparison.Ordinal);
            var direction = emitted.ShaderSource.IndexOf("NX_OverrideLightDirection(lightDir", StringComparison.Ordinal);
            if (direction < attenuation) throw new InvalidOperationException("Light direction override replaced or preceded Unity attenuation.");
        }
        Export(graph, emitted.ShaderSource);
        using (var preview = GraphPreview.Create(graph, null))
        {
            if (ShaderUtil.ShaderHasError(preview.Material.shader)) throw new InvalidOperationException("Indirect lighting shader did not compile in Unity.");
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels();
            UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels;
        }
    }
    static void Export(ShaderGraph graph, string shader)
    {
        var assets = "Assets/SmokeResults/IndirectLighting"; Directory.CreateDirectory(assets);
        var name = "indirect-" + (exportIndex++).ToString("D2");
        File.WriteAllText(Path.Combine(assets, name + ".nxsg"), GraphJson.Serialize(graph, true));
        File.WriteAllText(Path.Combine(assets, name + ".shader"), shader);
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var d3d = Path.Combine(root, "work/indirect-lighting-d3d-graphs"); Directory.CreateDirectory(d3d);
        File.WriteAllText(Path.Combine(d3d, name + ".nxsg"), GraphJson.Serialize(graph, true));
    }
    static ShaderGraph Surface()
    {
        var graph = new ShaderGraph { GraphId = "indirect-lighting-editor-smoke" };
        graph.Nodes.Add(NodeCatalog.Create("core.pbrSurface").WithIndirectId("surface"));
        graph.Nodes.Add(NodeCatalog.Create("core.output").WithIndirectId("output"));
        Link(graph, "surface", "surface", "output", "surface"); return graph;
    }
    static void AddBentNormal(ShaderGraph graph, double x, double y, double z)
    {
        var constant = new GraphNode { Id = "bent", Operation = "core.constant", Properties = new JObject { ["valueType"] = "vector3", ["value"] = new JArray(x, y, z) } };
        graph.Nodes.Add(constant); Link(graph, "bent", "value", "surface", "bentNormal");
        graph.Nodes.Single(node => node.Id == "surface").Properties["bentStrength"] = 1;
    }
    static void SetDirection(ShaderGraph graph, double strength, int space, double x, double z)
    {
        var surface = graph.Nodes.Single(node => node.Id == "surface");
        surface.Properties["lightDirection"] = new JArray(x, 0, z); surface.Properties["lightDirectionStrength"] = strength; surface.Properties["lightDirectionSpace"] = space;
    }
    static void SetAlbedo(ShaderGraph graph, double r, double g, double b, double a) => AddColor(graph, "albedo", r, g, b, a);
    static void SetEmission(ShaderGraph graph, double r, double g, double b, double a) => AddColor(graph, "emission", r, g, b, a);
    static void AddColor(ShaderGraph graph, string id, double r, double g, double b, double a)
    {
        var color = new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(r, g, b, a) } };
        graph.Nodes.Add(color); Link(graph, id, "value", "surface", id);
    }
    static void Link(ShaderGraph graph, string from, string output, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input,
            From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
    static Color[] RequireFinite(Color[] pixels)
    {
        if (pixels.Any(color => float.IsNaN(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b) || float.IsInfinity(color.r) || float.IsInfinity(color.g) || float.IsInfinity(color.b)))
            throw new InvalidOperationException("Indirect lighting produced non-finite pixels.");
        return pixels;
    }
    static int Changed(Color[] first, Color[] second) => first.Zip(second, (a, b) => Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b))).Count(distance => distance > .002f);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Finish(Exception exception)
    {
        RenderTexture.active = null;
        if (camera != null) camera.targetTexture = null;
        if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
        if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
        if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
        if (renderSettingsSaved)
        {
            RenderSettings.ambientMode = oldAmbientMode; RenderSettings.ambientLight = oldAmbientLight; RenderSettings.ambientIntensity = oldAmbientIntensity;
            RenderSettings.ambientProbe = oldAmbientProbe; RenderSettings.reflectionIntensity = oldReflectionIntensity;
        }
        if (window != null) { window.DiscardChanges(); window.Close(); }
        if (exception != null) { Debug.LogException(exception); EditorApplication.Exit(1); }
        else EditorApplication.Exit(0);
    }
}

internal static class IndirectLightingSmokeNodeExtensions
{
    internal static GraphNode WithIndirectId(this GraphNode node, string id) { node.Id = id; return node; }
}
