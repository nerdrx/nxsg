using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod RenderingShowcase.Run.
// Renders real NXSG graphs. Output goes to <repo>/work/rendering-showcase/.
public static class RenderingShowcase
{
    const int Width = 1920, Height = 1080;
    const string RampAssetPath = "Assets/NXSGShowcaseRamp.asset";
    static Camera camera;
    static RenderTexture target;
    static Texture2D rampTexture;
    static readonly List<GraphPreview> previews = new List<GraphPreview>();
    static readonly List<GameObject> objects = new List<GameObject>();
    static readonly List<Material> sceneMaterials = new List<Material>();

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("A graphics device is required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            SetupScene();
            CreateRampTexture();
            var toon = BuildToon("teal-violet-toon", new Color(.2f, .9f, .82f), new Color(.12f, .035f, .24f), false, false);
            var ramp = BuildToon("palette-ramp-toon", Color.white, new Color(.055f, .02f, .13f), true, true);
            var outlined = BuildToon("ink-outline-toon", new Color(.43f, .83f, 1f), new Color(.1f, .025f, .2f), false, true);
            var depthBase = BuildToon("depth-baseline-toon", new Color(.42f, .7f, .9f), new Color(.08f, .035f, .2f), false, false);
            var depthLit = BuildToon("depth-ao-contact-toon", new Color(.42f, .7f, .9f), new Color(.08f, .035f, .2f), false, false, true);
            AddPanels();
            AddToonStage(toon, ramp);
            AddOutlineStage(outlined);
            AddDepthStage(depthBase, depthLit);
            AddLabels();

            camera.Render();
            var pixels = ReadPixels();
            if (pixels.Any(c => !Finite(c.r) || !Finite(c.g) || !Finite(c.b))) throw new InvalidOperationException("Showcase render contains non-finite pixels.");
            var root = FindRepositoryRoot();
            var output = Path.Combine(root, "work", "rendering-showcase"); Directory.CreateDirectory(output);
            Save(pixels, Path.Combine(output, "nxsg-rendering-showcase.png"));
            Debug.Log("NXSG RENDERING SHOWCASE SAVED: " + Path.Combine(output, "nxsg-rendering-showcase.png"));
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            foreach (var preview in previews) preview.Dispose();
            previews.Clear();
            foreach (var material in sceneMaterials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            sceneMaterials.Clear();
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(RampAssetPath))) AssetDatabase.DeleteAsset(RampAssetPath);
            if (rampTexture != null) UnityEngine.Object.DestroyImmediate(rampTexture);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }

    static void SetupScene()
    {
        camera = NewObject("NXSG showcase camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.009f, .012f, .034f);
        camera.orthographic = true; camera.orthographicSize = 3.7f; camera.transform.position = new Vector3(0, 0, -12); camera.transform.LookAt(new Vector3(0, 0, 0));
        camera.nearClipPlane = .05f; camera.farClipPlane = 40; camera.allowHDR = true; camera.depthTextureMode = DepthTextureMode.Depth;
        target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
        RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientIntensity = .9f;
        var sh = new SphericalHarmonicsL2(); sh.AddDirectionalLight(Vector3.up, new Color(.25f, .18f, .36f), 1); sh.AddDirectionalLight(Vector3.left, new Color(.1f, .19f, .24f), .5f); RenderSettings.ambientProbe = sh;
        var key = NewObject("NXSG cyan key").AddComponent<Light>(); key.type = LightType.Directional; key.color = new Color(.52f, .82f, 1); key.intensity = .75f; key.transform.rotation = Quaternion.Euler(28, -24, 0); key.shadows = LightShadows.None;
        var fill = NewObject("NXSG lavender fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.color = new Color(.75f, .24f, .84f); fill.intensity = .12f; fill.transform.rotation = Quaternion.Euler(12, 150, 0);
    }

    static void AddPanels()
    {
        var panel = BuiltinMaterial("Unlit/Color", new Color(.023f, .03f, .075f));
        foreach (var x in new[] { -4.05f, 0f, 4.05f })
        {
            var card = Primitive(PrimitiveType.Quad, "Showcase panel"); card.transform.position = new Vector3(x, -.2f, 2.2f); card.transform.localScale = new Vector3(3.85f, 5.65f, 1); card.GetComponent<Renderer>().sharedMaterial = panel;
        }
        var floorMat = BuiltinMaterial("Unlit/Color", new Color(.012f, .016f, .042f));

    }

    static void AddToonStage(Material toon, Material ramp)
    {
        var hero = Primitive(PrimitiveType.Sphere, "Cel shaded hero"); hero.transform.position = new Vector3(-4.05f, -.1f, 0); hero.transform.localScale = Vector3.one * 1.65f; hero.GetComponent<Renderer>().sharedMaterial = toon;
        var capsule = Primitive(PrimitiveType.Capsule, "Color ramp form"); capsule.transform.position = new Vector3(-5.12f, -.65f, -.18f); capsule.transform.rotation = Quaternion.Euler(0, 0, -12); capsule.transform.localScale = new Vector3(.62f, 1.25f, .62f); capsule.GetComponent<Renderer>().sharedMaterial = ramp;
        var cube = Primitive(PrimitiveType.Cube, "Color ramp cube"); cube.transform.position = new Vector3(-2.95f, -.68f, .12f); cube.transform.rotation = Quaternion.Euler(20, 28, -8); cube.transform.localScale = Vector3.one * .9f; cube.GetComponent<Renderer>().sharedMaterial = ramp;
        for (var i = 0; i < 3; i++)
        {
            var orb = Primitive(PrimitiveType.Sphere, "Floating accent"); orb.transform.position = new Vector3(-5.35f + i * .32f, 1.13f + Mathf.Sin(i * 2) * .14f, -.3f); orb.transform.localScale = Vector3.one * (.12f + .035f * (i % 2)); orb.GetComponent<Renderer>().sharedMaterial = ramp;
        }
    }

    static void AddOutlineStage(Material outlined)
    {
        var sphere = Primitive(PrimitiveType.Sphere, "Outlined toon sphere"); sphere.transform.position = new Vector3(-.42f, -.25f, 0); sphere.transform.localScale = Vector3.one * 1.52f; sphere.GetComponent<Renderer>().sharedMaterial = outlined;
        var capsule = Primitive(PrimitiveType.Capsule, "Outlined toon capsule"); capsule.transform.position = new Vector3(.88f, -.55f, -.28f); capsule.transform.rotation = Quaternion.Euler(0, 0, 14); capsule.transform.localScale = new Vector3(.55f, 1.32f, .55f); capsule.GetComponent<Renderer>().sharedMaterial = outlined;
        var bead = Primitive(PrimitiveType.Sphere, "Ink accent"); bead.transform.position = new Vector3(-.92f, 1.02f, -.25f); bead.transform.localScale = Vector3.one * .2f; bead.GetComponent<Renderer>().sharedMaterial = outlined;
    }

    static void AddDepthStage(Material baseline, Material depth)
    {
        // Matching depth-writing occluders make the camera-depth effect readable beside baseline.
        for (var i = 0; i < 2; i++)
        {
            float x = i == 0 ? 3.2f : 4.92f;
            var receiver = Primitive(PrimitiveType.Quad, i == 0 ? "Baseline receiver" : "Depth receiver");
            receiver.transform.position = new Vector3(x, -.15f, .52f); receiver.transform.localScale = new Vector3(1.55f, 2.25f, 1);
            receiver.GetComponent<Renderer>().sharedMaterial = i == 0 ? baseline : depth;
            var occluder = Primitive(PrimitiveType.Sphere, "Depth occluder"); occluder.transform.position = new Vector3(x, -.15f, .42f); occluder.transform.localScale = Vector3.one * .86f;
            var small = Primitive(PrimitiveType.Sphere, "Contact bead"); small.transform.position = new Vector3(x + .52f, -.78f, -.28f); small.transform.localScale = Vector3.one * .34f; small.GetComponent<Renderer>().sharedMaterial = depth;
        }
    }

    static void AddLabels()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text("NXSG  /  LIGHTING + OUTLINES", new Vector3(0, 3.13f, -1.5f), 48, .008f, new Color(.89f, .94f, 1), TextAnchor.MiddleCenter, font);
        Text("TOON BANDS + RAMPS", new Vector3(-4.05f, 2.23f, -1.5f), 24, .0075f, new Color(.52f, .9f, .94f), TextAnchor.MiddleCenter, font);
        Text("MESH OUTLINES", new Vector3(0, 2.23f, -1.5f), 24, .0075f, new Color(.72f, .58f, 1), TextAnchor.MiddleCenter, font);
        Text("CONTACT SHADOWS", new Vector3(4.05f, 2.23f, -1.5f), 22, .007f, new Color(.59f, .9f, .8f), TextAnchor.MiddleCenter, font);
        Text("BANDS  •  PALETTE", new Vector3(-4.05f, -2.55f, -1.5f), 21, .007f, new Color(.7f, .78f, 1), TextAnchor.MiddleCenter, font);
        Text("HULL EXPANSION  •  INK", new Vector3(0, -2.55f, -1.5f), 20, .0065f, new Color(.7f, .78f, 1), TextAnchor.MiddleCenter, font);
        Text("BASELINE       /       AO + SHADOW", new Vector3(4.05f, -2.55f, -1.5f), 17, .0065f, new Color(.7f, .78f, 1), TextAnchor.MiddleCenter, font);
        Text("Built from editable NXSG graphs   •   Unity Built-In", new Vector3(0, -3.28f, -1.5f), 18, .0065f, new Color(.48f, .59f, .76f), TextAnchor.MiddleCenter, font);
    }

    static Material BuildToon(string id, Color albedoColor, Color shadeColor, bool paletteRamp, bool outline, bool screenDepth = false)
    {
        var graph = new ShaderGraph { GraphId = "showcase-" + id };
        var toon = NodeCatalog.Create("core.toonSurface"); toon.Id = "toon";
        toon.Properties["threshold"] = .48; toon.Properties["softness"] = .045; toon.Properties["shadowStrength"] = .92;
        if (id == "teal-violet-toon") { toon.Properties["lightingMode"] = 1; toon.Properties["bands"] = 3; }
        if (paletteRamp)
        {
            const string resourceId = "toon-ramp";
            toon.Properties["occlusion"] = 0; toon.Properties["lightingMode"] = 2; toon.Properties["resourceId"] = resourceId; toon.Properties["rampRow"] = .5;
            graph.Resources.Add(new GraphResource { Id = resourceId, Name = "Violet cyan coral ramp", Kind = "texture2D", Uri = "project://" + RampAssetPath });
            graph.Adapter = new JObject { ["textures"] = new JObject { [resourceId] = AssetDatabase.AssetPathToGUID(RampAssetPath) } };
        }
        graph.Nodes.Add(toon);
        AddColor(graph, "albedo", albedoColor, "toon", "albedo");
        AddColor(graph, "shade", shadeColor, "toon", "shadeColor");
        string surfaceRoot = "toon";
        if (screenDepth)
        {
            var ao = NodeCatalog.Create("core.ssao"); ao.Id = "ao"; ao.Properties["radius"] = .5; ao.Properties["strength"] = .9; ao.Properties["thickness"] = .7; ao.Properties["bias"] = .008; ao.Properties["samples"] = 32; graph.Nodes.Add(ao); Edge(graph, "ao", "visibility", "toon", "occlusion");
            var direction = new GraphNode { Id = "lightDirection", Operation = "core.constant", Properties = new JObject { ["valueType"] = "vector3", ["value"] = new JArray(.3, .65, -.7) } }; graph.Nodes.Add(direction);
            var contact = NodeCatalog.Create("core.contactShadow"); contact.Id = "contact"; contact.Properties["distance"] = .55; contact.Properties["strength"] = .95; contact.Properties["thickness"] = .14; contact.Properties["bias"] = .007; contact.Properties["samples"] = 32; graph.Nodes.Add(contact);
            Edge(graph, "lightDirection", "value", "contact", "direction"); Edge(graph, "contact", "visibility", "toon", "shadow");
        }
        if (outline)
        {
            var hull = NodeCatalog.Create("core.outline"); hull.Id = "outline"; hull.Properties["width"] = .025; hull.Properties["color"] = new JArray(.018, .009, .055, 1); graph.Nodes.Add(hull); Edge(graph, "toon", "surface", "outline", "base"); surfaceRoot = "outline";
        }
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output); Edge(graph, surfaceRoot, "surface", "output", "surface");
        var validation = GraphValidator.Validate(graph); if (!validation.IsValid) throw new InvalidOperationException(id + " graph invalid: " + string.Join("; ", validation.Diagnostics.Select(d => d.Message)));
        var exported = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = "NXSG/Showcase/" + id });
        if (!exported.Succeeded) throw new InvalidOperationException(id + " source export failed: " + string.Join("; ", exported.Diagnostics.Select(d => d.Message)));
        ExportGraphAndShader(graph, id, exported.ShaderSource);
        var preview = GraphPreview.Create(graph, null); previews.Add(preview);
        if (preview.Material == null || ShaderUtil.ShaderHasError(preview.Material.shader)) throw new InvalidOperationException(id + " emitted shader failed import.");
        for (var pass = 0; pass < preview.Material.passCount; pass++) { ShaderUtil.CompilePass(preview.Material, pass, true); if (!preview.Material.SetPass(pass)) throw new InvalidOperationException(id + " shader pass failed: " + pass); }
        if (paletteRamp)
        {
            var property = exported.Properties.Single(p => p.ResourceId == "toon-ramp");
            var boundTexture = preview.Material.GetTexture(property.Name) as Texture2D;
            var midpoint = rampTexture.GetPixel(64, 0);
            Debug.Log("NXSG RAMP CHECK: property=" + property.Name + " bound=" + (boundTexture == rampTexture) + " texel64=" + midpoint);
            if (boundTexture != rampTexture || Vector3.Distance(new Vector3(midpoint.r, midpoint.g, midpoint.b), Vector3.one) < .2f || !exported.ShaderSource.Contains("tex2D(" + property.Name + ","))
                throw new InvalidOperationException("Toon texture ramp was not bound or sampled through its emitted material property " + property.Name + ".");
        }
        return preview.Material;
    }

    static void ExportGraphAndShader(ShaderGraph graph, string id, string shaderSource)
    {
        var root = FindRepositoryRoot();
        var d3d = Path.Combine(root, "work", "rendering-d3d-graphs"); Directory.CreateDirectory(d3d);
        var source = Path.Combine(root, "work", "rendering-showcase", "showcase-source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(d3d, "showcase-" + id + ".nxsg"), GraphJson.Serialize(graph));
        File.WriteAllText(Path.Combine(source, "showcase-" + id + ".shader"), shaderSource);
    }

    static void AddColor(ShaderGraph graph, string id, Color color, string to, string input)
    {
        var node = new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(color.r, color.g, color.b, color.a) } };
        graph.Nodes.Add(node); Edge(graph, id, "value", to, input);
    }
    static void CreateRampTexture()
    {
        rampTexture = new Texture2D(128, 2, TextureFormat.RGBA32, false, true) { name = "NXSG showcase toon ramp", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var stops = new[] { new Color(.035f, .78f, .72f), new Color(.27f, .12f, .72f), new Color(.98f, .53f, .42f) };
        for (var y = 0; y < 2; y++) for (var x = 0; x < 128; x++) { float t = x / 127f * 2; int segment = Mathf.Min(1, Mathf.FloorToInt(t)); rampTexture.SetPixel(x, y, Color.Lerp(stops[segment], stops[segment + 1], t - segment)); }
        rampTexture.Apply(); AssetDatabase.CreateAsset(rampTexture, RampAssetPath); AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(RampAssetPath, ImportAssetOptions.ForceSynchronousImport);
        rampTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(RampAssetPath);
    }
    static void Edge(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
    static GameObject Primitive(PrimitiveType type, string name) { var go = GameObject.CreatePrimitive(type); go.name = name; objects.Add(go); return go; }
    static GameObject NewObject(string name) { var go = new GameObject(name); objects.Add(go); return go; }
    static Material BuiltinMaterial(string shaderName, Color color) { var shader = Shader.Find(shaderName); if (shader == null) throw new InvalidOperationException("Built-in shader missing: " + shaderName); var material = new Material(shader) { color = color }; sceneMaterials.Add(material); return material; }

    static void Text(string value, Vector3 position, int size, float scale, Color color, TextAnchor anchor, Font font)
    {
        var go = NewObject("Showcase label: " + value); go.transform.position = position; go.transform.rotation = camera.transform.rotation;
        var mesh = go.AddComponent<TextMesh>(); mesh.text = value; mesh.font = font; mesh.fontSize = size; mesh.characterSize = scale * 12; mesh.anchor = anchor; mesh.alignment = TextAlignment.Center; mesh.color = color;
        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = font.material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }
    static Color[] ReadPixels()
    {
        RenderTexture.active = target; var image = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels;
    }
    static void Save(Color[] pixels, string path)
    {
        var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true);
        image.SetPixels(pixels.Select(c => new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), 1)).ToArray()); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
    }
    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Application.dataPath);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Tests", "Portable"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate NXSG package root from " + Application.dataPath);
    }
    static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
}
