using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// Run in graphics-enabled Unity 2022.3 with -executeMethod HairDetailSmoke.Run.
public static class HairDetailSmoke
{
    const int Size = 96;
    static Camera camera;
    static GameObject quad;
    static GameObject lightObject;
    static RenderTexture target;
    static Cubemap probe;
    static GraphWindow window;
    static int inspectorTicks;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(string name) => typeof(GraphWindow).GetField(name, Private).GetValue(window);
    static void Invoke(string name, params object[] args) => typeof(GraphWindow).GetMethod(name, Private).Invoke(window, args);
    static ShaderGraph EditingGraph => (ShaderGraph)Field("graph");

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            foreach (var old in Resources.FindObjectsOfTypeAll<GraphWindow>()) { old.DiscardChanges(); old.Close(); }
            window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.position = new Rect(0, 0, 1100, 800);
            typeof(GraphWindow).GetField("livePreview", Private).SetValue(window, false);
            Invoke("NewGraph"); EditingGraph.Nodes.Clear(); EditingGraph.Connections.Clear();
            EditingGraph.Nodes.Add(NodeCatalog.Create("core.anisotropicHighlight").WithId("hair"));
            Invoke("Rebuild"); Invoke("SelectNode", "hair", false);
            EditorApplication.update += CheckInspector;
        }
        catch (Exception exception) { Finish(exception); }
    }

    static void CheckInspector()
    {
        if (++inspectorTicks < 30) return;
        inspectorTicks = 0;
        try
        {
            var inspector = (VisualElement)Field("inspector");
            var width = inspector.Q<FloatField>("node-property-longitudinalWidth");
            Require(width != null && inspector.Q<FloatField>("node-property-azimuthalWidth") != null &&
                inspector.Q<FloatField>("node-property-shiftNoise") != null && inspector.Q<FloatField>("node-property-reflectionStrength") != null,
                "Hair detail controls missing from inspector");
            width.value = 2.5f;
            Require(Math.Abs((double)EditingGraph.Nodes.Single(node => node.Id == "hair").Properties["longitudinalWidth"] - 2.5) < .001,
                "Hair width edit did not save to graph");
            var source = NodeCatalog.Create("core.value").WithId("width-source"); source.Properties["value"] = .5; EditingGraph.Nodes.Add(source);
            Edge(EditingGraph, "width-source", "value", "hair", "longitudinalWidth"); Invoke("Rebuild"); Invoke("SelectNode", "hair", false);
            inspector = (VisualElement)Field("inspector");
            Require(!inspector.Q<FloatField>("node-property-longitudinalWidth").enabledInHierarchy,
                "Connected hair width field remains editable");
            EditorApplication.update -= CheckInspector;
            window.DiscardChanges(); window.Close(); window = null;
            BeginRender();
        }
        catch (Exception exception) { EditorApplication.update -= CheckInspector; Finish(exception); }
    }

    static void BeginRender()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            Directory.CreateDirectory("Assets/SmokeResults/HairCompletion");
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            camera = new GameObject("NXSG hair detail camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -2); camera.orthographic = true; camera.orthographicSize = .7f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            lightObject = new GameObject("NXSG hair detail light");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.7f; lightObject.transform.rotation = Quaternion.Euler(25, 35, 0);
            probe = new Cubemap(16, TextureFormat.RGBA32, false);
            foreach (CubemapFace face in Enum.GetValues(typeof(CubemapFace)))
                if (face != CubemapFace.Unknown) probe.SetPixels(Enumerable.Range(0, 256).Select(index => new Color(((int)face + 1) * .025f + (index % 16) / 40f, index / 16 / 30f, ((int)face + 1) * .035f, 1)).ToArray(), face);
            probe.Apply(); RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.customReflection = probe; RenderSettings.reflectionIntensity = 1;

            var neutral = MakeGraph();
            var neutralPixels = Capture(neutral, "00-neutral.nxsg");
            var legacyNeutral = MakeGraph(); Legacy(legacyNeutral);
            Require(Changed(neutralPixels, Capture(legacyNeutral, "08-legacy-neutral.nxsg")) == 0,
                "New neutral helper differs from legacy neutral render.");
            var legacyShifted = MakeGraph(); Legacy(legacyShifted); Node(legacyShifted).Properties["shift"] = .25; Node(legacyShifted).Properties["secondaryShift"] = -.15; Node(legacyShifted).Properties["dualLobe"] = .7;
            var legacyShiftPixels = Capture(legacyShifted, "09-legacy-shifted-dual.nxsg");
            var legacyReloaded = GraphJson.Parse(GraphJson.Serialize(legacyShifted));
            Require(Changed(legacyShiftPixels, Capture(legacyReloaded, "10-legacy-shifted-dual-reloaded.nxsg")) == 0,
                "Legacy shifted dual-lobe render changed after graph round trip.");
            foreach (var port in new[] { "normal", "tangent" })
            {
                var basis = NodeCatalog.Create("core.constant"); basis.Id = "basis-" + port;
                basis.Properties["valueType"] = "vector3";
                basis.Properties["value"] = port == "normal" ? new JArray(.2,.3,-1) : new JArray(1,.2,.35);
                legacyShifted.Nodes.Add(basis); Edge(legacyShifted, basis.Id, "value", "hair", port);
            }
            var legacyBasis = Capture(legacyShifted, "11-legacy-custom-basis.nxsg");
            var connectedNeutral = GraphJson.Parse(GraphJson.Serialize(legacyShifted));
            foreach (var port in new[] { "longitudinalWidth", "azimuthalWidth" })
            {
                var width = NodeCatalog.Create("core.value"); width.Id = "width-" + port; width.Properties["value"] = 1;
                connectedNeutral.Nodes.Add(width); Edge(connectedNeutral, width.Id, "value", "hair", port);
            }
            var connectedPixels = Capture(connectedNeutral, "12-connected-neutral-custom-basis.nxsg");
            Require(legacyBasis.Zip(connectedPixels, (a,b) => Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b))).Max() < .0001f,
                "Connected neutral widths changed shifted highlights with a custom nonorthogonal basis");
            var longGraph = MakeGraph(); Node(longGraph).Properties["longitudinalWidth"] = 4;
            var longPixels = Capture(longGraph, "01-longitudinal-wide.nxsg");
            Require(Changed(neutralPixels, longPixels) > 20, "Longitudinal width did not change rendered highlight.");
            var azimuthGraph = MakeGraph(); Node(azimuthGraph).Properties["azimuthalWidth"] = .3;
            var azimuthPixels = Capture(azimuthGraph, "02-azimuthal-wide.nxsg");
            Require(Changed(neutralPixels, azimuthPixels) > 20, "Azimuthal width did not change rendered highlight.");

            var offProbe = MakeGraph(); Node(offProbe).Properties["reflectionStrength"] = 0; Node(offProbe).Properties["reflectionStretch"] = 0;
            var offPixels = Capture(offProbe, "03-probe-off.nxsg");
            Node(offProbe).Properties["reflectionStretch"] = 3;
            var stillOffPixels = Capture(offProbe, "04-probe-zero-strength.nxsg");
            Require(Changed(offPixels, stillOffPixels) == 0, "Zero reflection strength changed output.");
            var isotropicProbe = MakeGraph(); Node(isotropicProbe).Properties["reflectionStrength"] = .8;
            var isotropicPixels = Capture(isotropicProbe, "05-probe-isotropic.nxsg");
            var stretchedProbe = MakeGraph(); Node(stretchedProbe).Properties["reflectionStrength"] = .8; Node(stretchedProbe).Properties["reflectionStretch"] = 2;
            var stretchedPixels = Capture(stretchedProbe, "06-probe-stretched.nxsg");
            Require(Changed(isotropicPixels, stretchedPixels) > 8, "Stretched reflection probe did not change rendered output.");
            var strongProbe = MakeGraph(); Node(strongProbe).Properties["reflectionStrength"] = 2;
            var strongPixels = Capture(strongProbe, "07-probe-strength-two.nxsg");
            Require(Changed(isotropicPixels, strongPixels) > 8, "Probe strength above one did not increase rendered response.");
            foreach (var width in new[] { 0.0, .3, 4.0, 1000.0 })
            {
                var endpoint = MakeGraph(); Node(endpoint).Properties["longitudinalWidth"] = width; Node(endpoint).Properties["azimuthalWidth"] = width;
                Node(endpoint).Properties["reflectionStrength"] = 1; Node(endpoint).Properties["reflectionStretch"] = -1;
                Capture(endpoint, "endpoint-" + width + ".nxsg");
            }
            Debug.Log("NXSG HAIR DETAIL SMOKE PASSED: independent widths, zero-strength neutrality and stretched probe response.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (quad != null) UnityEngine.Object.DestroyImmediate(quad);
            if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            RenderSettings.customReflection = null;
        }
    }

    static void Finish(Exception exception)
    {
        if (window != null) { window.DiscardChanges(); window.Close(); window = null; }
        if (exception != null) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static ShaderGraph MakeGraph()
    {
        var graph = new ShaderGraph { GraphId = "hair-detail-completion" };
        graph.Nodes.Add(NodeCatalog.Create("core.anisotropicHighlight").WithId("hair"));
        graph.Nodes.Add(NodeCatalog.Create("core.unlitSurface").WithId("surface"));
        graph.Nodes.Add(NodeCatalog.Create("core.output").WithId("output"));
        Edge(graph, "hair", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }
    static GraphNode Node(ShaderGraph graph) => graph.Nodes.Single(node => node.Id == "hair");
    static void Legacy(ShaderGraph graph)
    { foreach (var name in new[] { "shiftNoise", "secondaryShiftNoise", "longitudinalWidth", "azimuthalWidth", "reflectionStrength", "reflectionStretch", "reflectionRoughness" }) Node(graph).Properties.Remove(name); }
    static void Edge(ShaderGraph graph, string from, string output, string to, string input)
    { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
    static Color[] Capture(ShaderGraph graph, string filename)
    {
        File.WriteAllText("Assets/SmokeResults/HairCompletion/" + filename, GraphJson.Serialize(graph));
        using (var preview = GraphPreview.Create(graph, null))
        {
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader), "Hair preview shader has compilation errors: " + filename);
            quad.GetComponent<Renderer>().sharedMaterial = preview.Material;
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try
            {
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels();
                Require(pixels.All(c => !float.IsNaN(c.r) && !float.IsInfinity(c.r) && !float.IsNaN(c.g) && !float.IsInfinity(c.g) && !float.IsNaN(c.b) && !float.IsInfinity(c.b)), "Nonfinite hair output: " + filename);
                return pixels;
            }
            finally { RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(image); }
        }
    }
    static int Changed(Color[] left, Color[] right) => left.Zip(right, (a, b) => Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b))).Count(distance => distance > .02f);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
