using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// Run in an isolated graphics-enabled Unity project with -executeMethod LightingDetailSmoke.Run.
public static class LightingDetailSmoke
{
    const int Size = 96;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static GraphWindow window;
    static Camera camera;
    static GameObject sphere;
    static GameObject lightObject;
    static RenderTexture target;
    static int ticks;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph Graph => (ShaderGraph)Field("graph");
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 1100, 800);
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            Invoke("NewGraph"); Graph.Nodes.Clear(); Graph.Connections.Clear();
            Graph.Nodes.Add(NodeCatalog.Create("core.anisotropicHighlight").WithId("anisotropic"));
            Graph.Nodes.Add(NodeCatalog.Create("core.subsurface").WithId("subsurface"));
            Graph.Nodes.Add(NodeCatalog.Create("core.layeredPbrSurface").WithId("layered"));
            Graph.Nodes.Add(NodeCatalog.Create("core.output").WithId("output"));
            Invoke("Rebuild");
            EditorApplication.update += Tick;
        }
        catch (Exception exception) { Finish(exception); }
    }

    static void Tick()
    {
        if (++ticks < 30) return; ticks = 0;
        try
        {
            if (ticksPhase == 0)
            {
                Inspect("anisotropic");
                var inspector = (VisualElement)Field("inspector");
                Require(inspector.Q<FloatField>("node-property-dualLobe") != null &&
                    inspector.Q<FloatField>("node-property-secondaryRoughness") != null &&
                    inspector.Q<FloatField>("node-property-secondaryShift") != null &&
                    inspector.Q<FloatField>("node-property-tangentStrength") != null,
                    "Anisotropic controls missing");
                var tint = inspector.Query<UnityEditor.UIElements.ColorField>().ToList().Single(field => field.label == "Second tint");
                tint.value = new Color(.2f, .3f, .4f, 1);
                var storedTint = Graph.Nodes.Single(node => node.Id == "anisotropic").Properties["secondaryTint"] as JArray;
                Require(storedTint != null && Math.Abs((double)storedTint[0] - .2) < .001, "Second tint control did not save");
                var value = NodeCatalog.Create("core.value"); value.Id = "lobe-weight"; value.Properties["value"] = .5;
                Graph.Nodes.Add(value); Edge(Graph, value.Id, "value", "anisotropic", "dualLobe"); Invoke("Rebuild"); Invoke("SelectNode", "anisotropic", false);
                Require(!((VisualElement)Field("inspector")).Q<FloatField>("node-property-dualLobe").enabledInHierarchy,
                    "Connected anisotropic lobe control remains editable");
                ticksPhase++;
            }
            else if (ticksPhase == 1)
            {
                Inspect("subsurface"); var inspector = (VisualElement)Field("inspector");
                foreach (var name in new[] { "thickness", "strength", "viewResponse", "attenuation" })
                    Require(inspector.Q<FloatField>("node-property-" + name) != null, "Subsurface control missing: " + name);
                Require(inspector.Query<UnityEditor.UIElements.ColorField>().ToList().Any(field => field.label == "Scatter tint"), "Subsurface tint control missing");
                Inspect("layered"); inspector = (VisualElement)Field("inspector");
                Require(inspector.Q<FloatField>("node-property-specularAa") != null, "Layered PBR specular AA control missing");
                RenderEndpoints(); ticksPhase++;
            }
            else
            {
                Debug.Log("NXSG LIGHTING DETAIL SMOKE PASSED: inspectors, connected inputs, neutral layered response and finite lighting endpoints");
                Finish(null);
            }
        }
        catch (Exception exception) { Finish(exception); }
    }

    static int ticksPhase;
    static int exportIndex;
    static void Inspect(string id) => Invoke("SelectNode", id, false);

    static void RenderEndpoints()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        camera = new GameObject("NXSG lighting detail camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 1.25f; camera.transform.position = new Vector3(0, 0, -3); camera.transform.LookAt(Vector3.zero);
        target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
        lightObject = new GameObject("NXSG lighting detail light"); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; lightObject.transform.rotation = Quaternion.Euler(25, -20, 0);

        var baseline = FeatureGraph("core.subsurface");
        var explicitNeutral = FeatureGraph("core.subsurface"); var neutralEffect = explicitNeutral.Nodes.Single(node => node.Id == "effect");
        neutralEffect.Properties["viewResponse"] = 0; neutralEffect.Properties["attenuation"] = 0;
        var baselinePixels = Render(baseline); var neutralPixels = Render(explicitNeutral);
        RequireFinite(baselinePixels); RequireFinite(neutralPixels);
        Require(Changed(baselinePixels, neutralPixels) == 0, "Neutral subsurface options changed pixels");

        foreach (var roughness in new[] { 0.0, 1.0 })
        foreach (var aa in new[] { 0.0, 1.0 })
        {
            var graph = SurfaceGraph(); var surface = graph.Nodes.Single(node => node.Id == "surface");
            surface.Properties["roughness"] = roughness; surface.Properties["specularAa"] = aa;
            RequireFinite(Render(graph));
        }
        foreach (var dual in new[] { 0.0, 1.0 })
        foreach (var shift in new[] { -1.0, 1.0 })
        {
            var graph = FeatureGraph("core.anisotropicHighlight"); var effect = graph.Nodes.Single(node => node.Id == "effect");
            effect.Properties["dualLobe"] = dual; effect.Properties["secondaryRoughness"] = dual; effect.Properties["shift"] = shift;
            effect.Properties["tangentStrength"] = dual; RequireFinite(Render(graph));
        }
        var anisoTintGraph = FeatureGraph("core.anisotropicHighlight");
        var aniso = anisoTintGraph.Nodes.Single(node => node.Id == "effect"); aniso.Properties["dualLobe"] = 1; aniso.Properties["secondaryRoughness"] = .8;
        aniso.Properties["secondaryTint"] = new JArray(1, 1, 1, 1); var whiteLobe = RequireFinite(Render(anisoTintGraph));
        aniso.Properties["secondaryTint"] = new JArray(1, .05, .02, 1); var redLobe = RequireFinite(Render(anisoTintGraph));
        Require(Changed(whiteLobe, redLobe) > 0, "Anisotropic tint edit did not change rendered pixels");

        var scatterTintGraph = FeatureGraph("core.subsurface");
        var scatterTint = scatterTintGraph.Nodes.Single(node => node.Id == "effect"); scatterTint.Properties["tint"] = new JArray(1, .05, .02, 1);
        var redScatter = RequireFinite(Render(scatterTintGraph)); scatterTint.Properties["tint"] = new JArray(.02, .05, 1, 1);
        var blueScatter = RequireFinite(Render(scatterTintGraph));
        Require(Changed(redScatter, blueScatter) > 0, "Subsurface tint edit did not change rendered pixels");
        foreach (var thickness in new[] { 0.0, 1.0 })
        foreach (var response in new[] { 0.0, 1.0 })
        foreach (var attenuation in new[] { 0.0, 1.0 })
        {
            var graph = FeatureGraph("core.subsurface"); var effect = graph.Nodes.Single(node => node.Id == "effect");
            effect.Properties["thickness"] = thickness; effect.Properties["viewResponse"] = response; effect.Properties["attenuation"] = attenuation;
            RequireFinite(Render(graph));
        }
    }

    static Color[] Render(ShaderGraph graph)
    {
        const string exportDirectory = "Assets/SmokeResults/LightingDetail";
        Directory.CreateDirectory(exportDirectory);
        var shader = ShaderEmitter.Emit(graph);
        if (!shader.Succeeded) throw new Exception("Lighting detail graph failed to emit: " + string.Join(";", shader.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var name = "lighting-detail-" + (exportIndex++).ToString("D2");
        File.WriteAllText(Path.Combine(exportDirectory, name + ".nxsg"), GraphJson.Serialize(graph, true));
        File.WriteAllText(Path.Combine(exportDirectory, name + ".shader"), shader.ShaderSource);
        using (var preview = GraphPreview.Create(graph, null))
        {
            if (ShaderUtil.ShaderHasError(preview.Material.shader)) throw new Exception("Lighting detail shader failed to compile.");
            sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply();
            var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels;
        }
    }

    static ShaderGraph FeatureGraph(string operation)
    {
        var graph = SurfaceGraph(); var effect = NodeCatalog.Create(operation); effect.Id = "effect"; graph.Nodes.Add(effect);
        Edge(graph, "effect", "color", "surface", "albedo"); return graph;
    }

    static ShaderGraph SurfaceGraph()
    {
        var graph = new ShaderGraph { GraphId = "lighting-detail-editor-smoke" };
        graph.Nodes.Add(NodeCatalog.Create("core.pbrSurface").WithId("surface")); graph.Nodes.Add(NodeCatalog.Create("core.output").WithId("output"));
        Edge(graph, "surface", "surface", "output", "surface"); return graph;
    }

    static void Edge(ShaderGraph graph, string from, string output, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input,
            From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }

    static Color[] RequireFinite(Color[] pixels)
    {
        if (pixels.Any(color => float.IsNaN(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b) || float.IsInfinity(color.r) || float.IsInfinity(color.g) || float.IsInfinity(color.b)))
            throw new Exception("Lighting detail produced non-finite pixels.");
        return pixels;
    }

    static int Changed(Color[] first, Color[] second) => first.Zip(second, (a, b) => Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b))).Count(distance => distance > .001f);

    static void Finish(Exception exception)
    {
        EditorApplication.update -= Tick;
        RenderTexture.active = null;
        if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
        if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
        if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
        if (window != null) { window.DiscardChanges(); window.Close(); }
        if (exception != null) { Debug.LogException(exception); EditorApplication.Exit(1); }
        else EditorApplication.Exit(0);
    }
}

internal static class LightingDetailSmokeNodeExtensions
{
    internal static GraphNode WithId(this GraphNode node, string id) { node.Id = id; return node; }
}
