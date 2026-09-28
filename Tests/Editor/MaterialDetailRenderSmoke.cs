using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in an isolated graphics-enabled Unity fixture.
public static class MaterialDetailRenderSmoke
{
    const int Size = 256;
    static Camera camera;
    static GameObject subject, background, keyLight;
    static Texture2D backgroundTexture;
    static RenderTexture target;
    static Material backgroundMaterial;
    static int captureIndex;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            camera = new GameObject("Material detail camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -4); camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.orthographic = true; camera.orthographicSize = 1.6f; camera.nearClipPlane = .05f; camera.farClipPlane = 20;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            background = GameObject.CreatePrimitive(PrimitiveType.Quad); background.name = "Depth and Gem background"; background.transform.position = new Vector3(0, 0, .8f); background.transform.localScale = new Vector3(5, 5, 1);
            backgroundTexture = MakeGradient();
            backgroundMaterial = new Material(Shader.Find("Standard")); backgroundMaterial.mainTexture = backgroundTexture;
            backgroundMaterial.color = Color.black; backgroundMaterial.SetTexture("_EmissionMap", backgroundTexture);
            backgroundMaterial.EnableKeyword("_EMISSION"); backgroundMaterial.SetColor("_EmissionColor", Color.black);
            background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
            subject = GameObject.CreatePrimitive(PrimitiveType.Quad); subject.name = "Detail subject"; subject.transform.localScale = new Vector3(2.5f, 2.5f, 1);
            keyLight = new GameObject("Face shadow key"); var light = keyLight.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1;

            CheckFaceAngle(light);
            CheckDepthRim();
            CheckGem();
            Debug.Log("NXSG MATERIAL DETAIL RENDER PASSED: angular face SDF, depth fallback/plane/silhouette, tinted refracted Gem");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (backgroundMaterial != null) UnityEngine.Object.DestroyImmediate(backgroundMaterial);
            if (backgroundTexture != null) UnityEngine.Object.DestroyImmediate(backgroundTexture);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
            if (background != null) UnityEngine.Object.DestroyImmediate(background);
            if (keyLight != null) UnityEngine.Object.DestroyImmediate(keyLight);
        }
    }

    static void CheckFaceAngle(Light light)
    {
        subject.transform.rotation = Quaternion.identity;
        var graph = MakeGraph("core.sdfFaceShadow");
        var face = graph.Nodes.Single(node => node.Operation == "core.sdfFaceShadow");
        face.Properties["headForward"] = new JArray(0,0,1); face.Properties["basis"] = 1; face.Properties["sdfLeft"] = .5; face.Properties["sdfRight"] = .5;
        face.Properties["threshold"] = .25; face.Properties["softness"] = .02; face.Properties["angleStrength"] = .5;
            light.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var front = MeanCenter(Capture(graph, false));
            light.transform.rotation = Quaternion.LookRotation(Vector3.left);
            var side = MeanCenter(Capture(graph, false));
            light.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var back = MeanCenter(Capture(graph, false));
            Require(front > side + .25f && side > back + .15f, "SDF threshold did not progress from front through side to back: " + front + ", " + side + ", " + back);
            Save(Capture(graph, false), "MaterialDetail-SDF-back.png"); SaveGraph(graph, "00-face-shadow.nxsg");
    }

    static void CheckDepthRim()
    {
        keyLight.SetActive(false);
        UnityEngine.Object.DestroyImmediate(backgroundMaterial);
        backgroundMaterial = new Material(Shader.Find("Standard")); backgroundMaterial.color = Color.black; backgroundMaterial.SetFloat("_SpecularHighlights",0); backgroundMaterial.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); backgroundMaterial.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
        subject.transform.rotation = Quaternion.Euler(0, 18, 0);
        var graph = MakeGraph("core.depthRim");
        var depthNode = graph.Nodes.Single(node => node.Operation == "core.depthRim");
        depthNode.Properties["width"] = 2; depthNode.Properties["softness"] = .02; depthNode.Properties["bias"] = .005;
        var missing = Capture(graph, false); Save(missing,"depth-missing.png");
        Require(missing.Max(pixel => pixel.r) < .02f, "Depth Rim changed image without camera depth: "+missing.Max(pixel=>pixel.r));
        var tilted = Capture(graph, true);
        Require(tilted[Size / 2 * Size + Size / 2].r < .05f, "Tilted plane produced an interior depth rim.");
        Require(CountBright(tilted) > 8, "Far background did not create a silhouette rim.");
        Save(tilted, "MaterialDetail-DepthRim-far-background.png");
        SaveGraph(graph, "01-depth-rim.nxsg");
        depthNode.Properties["width"] = 0;
        var disabled = Capture(graph, true);
        Require(disabled.Max(pixel => pixel.r) < .02f, "Depth Rim width zero was not neutral.");
    }

    static void CheckGem()
    {
        subject.GetComponent<Renderer>().enabled = false;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "Gem refraction subject"; sphere.transform.localScale = Vector3.one * 1.35f;
        try
        {
            UnityEngine.Object.DestroyImmediate(backgroundMaterial);
            backgroundMaterial = new Material(Shader.Find("Unlit/Texture")); backgroundMaterial.mainTexture = backgroundTexture;
            background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial; backgroundMaterial.SetColor("_EmissionColor", Color.white);
            var graph = MakeGraph("core.gem");
            var gem = graph.Nodes.Single(node => node.Operation == "core.gem");
            gem.Properties["color"] = new JArray(1, .7, .05, 1); gem.Properties["reflection"] = 0; gem.Properties["dispersion"] = .04;
            gem.Properties["refraction"] = 0;
            var tinted = Capture(graph, false, sphere);
            var center = tinted[Size / 2 * Size + Size / 2];
            Require(center.r > center.g * 1.25f && center.g > center.b * 1.2f, "Gem tint property did not color captured background: " + center);
            gem.Properties["refraction"] = .25;
            var refracted = Capture(graph, false, sphere);
            Require(Changed(tinted, refracted) > 12, "Gem refraction did not shift the background image.");
            Save(refracted, "MaterialDetail-Gem-gradient-refraction.png");
            SaveGraph(graph, "02-gem.nxsg");
        }
        finally { UnityEngine.Object.DestroyImmediate(sphere); subject.GetComponent<Renderer>().enabled = true; }
    }

    static Color[] Capture(ShaderGraph graph, bool depth, GameObject renderSubject = null)
    {
        Directory.CreateDirectory("Assets/SmokeResults/MaterialDetail");
        File.WriteAllText("Assets/SmokeResults/MaterialDetail/detail-"+(captureIndex++)+".nxsg",GraphJson.Serialize(graph));
        camera.depthTextureMode = depth ? DepthTextureMode.Depth : DepthTextureMode.None;
        var renderer = (renderSubject ?? subject).GetComponent<Renderer>();
        using (var preview = GraphPreview.Create(graph, null))
        {
            renderer.sharedMaterial = preview.Material;
            var forward=preview.Material.FindPass("ForwardBase"); Require(forward>=0 && preview.Material.SetPass(forward),"Material detail forward pass failed");
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader),"Material detail shader error");
            camera.Render(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            try { image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }
    }

    static ShaderGraph MakeGraph(string operation)
    {
        var graph = new ShaderGraph { GraphId = "material-detail-render-" + operation };
        var effect = Add(graph, operation, "effect");
        var black = Add(graph, "core.constant", "black"); black.Properties["valueType"] = "color"; black.Properties["value"] = new JArray(0, 0, 0, 1);
        var surface = Add(graph, "core.unlitSurface", "surface"); var output = Add(graph, "core.output", "output");
        Edge(graph, surface, "surface", output, "surface");
        if (operation == "core.gem") Edge(graph, effect.Id, "color", surface.Id, "albedo");
        else { Edge(graph, black.Id, "value", surface.Id, "albedo"); Edge(graph, effect.Id, "mask", surface.Id, "emission"); }
        return graph;
    }

    static GraphNode Add(ShaderGraph graph, string operation, string id)
    { var node = NodeCatalog.Create(operation); node.Id = id; graph.Nodes.Add(node); return node; }
    static void Edge(ShaderGraph graph, GraphNode from, string output, GraphNode to, string input) => Edge(graph,from.Id,output,to.Id,input);
    static void Edge(ShaderGraph graph, string from, string output, string to, string input)
    { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
    static float MeanCenter(Color[] pixels)
    { float value = 0; int count = 0; for (int y = Size / 2 - 12; y < Size / 2 + 12; y++) for (int x = Size / 2 - 12; x < Size / 2 + 12; x++) { value += pixels[y * Size + x].r; count++; } return value / count; }
    static int CountBright(Color[] pixels) { return pixels.Count(pixel => pixel.r > .7f); }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(distance => distance > .04f); }
    static Texture2D MakeGradient()
    {
        var texture = new Texture2D(128, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < texture.width; x++) { float t = x / (float)(texture.width - 1); texture.SetPixel(x, 0, new Color(1 - t * .9f, .2f, .1f + t * .9f, 1)); }
        texture.Apply(); return texture;
    }
    static void Save(Color[] pixels, string file)
    {
        var path = Path.Combine("Library/NXSG", file); Directory.CreateDirectory(Path.GetDirectoryName(path));
        var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true); image.SetPixels(pixels); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
    }
    static void SaveGraph(ShaderGraph graph, string file)
    {
        var directory = Path.Combine("Library/NXSG/MaterialDetailGraphs"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, file), GraphJson.Serialize(graph));
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
