using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod PixelLightSmoke.Run.
public static class PixelLightSmoke
{
    const int Size = 96;
    static readonly System.Collections.Generic.List<string> failures = new System.Collections.Generic.List<string>();
    static Camera camera;
    static RenderTexture target;
    static GameObject subject;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0;
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var cameraObject = new GameObject("NXSG Pixel Light Camera");
            camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f); camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.targetTexture = target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();

            Case("directional ForcePixel", CheckDirectional);
            Case("point attenuation", CheckPointAttenuation);
            Case("spot ForcePixel", CheckSpot);
            Case("spot cookie", CheckSpotCookie);
            Case("additive light color", CheckAdditiveColor);
            Case("emission independent of lighting", CheckEmission);
            Case("unlit lighting invariant", CheckUnlit);
            Case("advanced PBR and Toon", CheckAdvancedSurfaces);
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Debug.Log("NXSG PIXEL LIGHT SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject); if (subject != null) UnityEngine.Object.DestroyImmediate(subject); }
    }

    static void Case(string name, Action action) { try { action(); Debug.Log("NXSG pixel-light case passed: " + name); } catch (Exception e) { failures.Add(name + ": " + e.Message); Debug.LogError("NXSG pixel-light case failed: " + name + ": " + e.Message); } }

    static void CheckDirectional()
    {
        var light = AddLight(LightType.Directional, Color.white, 1, Quaternion.Euler(25, -25, 0));
        try { using (var preview = GraphPreview.Create(Surface("core.toonSurface", Color.white), null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; var color = Capture(); if (color.r < .05f) throw new InvalidOperationException("directional ForcePixel light produced no color: " + color); } }
        finally { UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckPointAttenuation()
    {
        var graph = Surface("core.toonSurface", Color.white); var light = AddLight(LightType.Point, Color.white, 8, Quaternion.identity); light.transform.position = new Vector3(0, 0, -1); light.range = 3;
        try { using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; var near = Capture(); light.transform.position = new Vector3(0, 0, -8); var far = Capture(); if (near.r <= far.r + .025f) throw new InvalidOperationException("point attenuation missing: " + near + " / " + far); } }
        finally { UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckSpot()
    {
        var graph = Surface("core.toonSurface", Color.white); var light = AddLight(LightType.Spot, Color.white, 8, Quaternion.LookRotation(Vector3.forward, Vector3.up)); light.transform.position = new Vector3(0, 0, -2); light.range = 5; light.spotAngle = 40;
        try { using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; var lit = Capture(); light.enabled = false; var dark = Capture(); if (lit.r <= dark.r + .025f) throw new InvalidOperationException("spot ForcePixel light produced no additive color: " + lit + " / " + dark); } }
        finally { UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckSpotCookie()
    {
        var graph = Surface("core.toonSurface", Color.white); var light = AddLight(LightType.Spot, Color.white, 8, Quaternion.LookRotation(Vector3.forward, Vector3.up)); light.transform.position = new Vector3(0, 0, -2); light.range = 5; light.spotAngle = 40;
        var white = new Texture2D(2, 2, TextureFormat.RGBA32, false); white.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); white.Apply();
        var black = new Texture2D(2, 2, TextureFormat.RGBA32, false); black.SetPixels(new[] { Color.clear, Color.clear, Color.clear, Color.clear }); black.Apply();
        try { using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; light.cookie = white; var lit = Capture(); light.cookie = black; var masked = Capture(); if (lit.r <= masked.r + .025f) throw new InvalidOperationException("spot cookie did not gate light: " + lit + " / " + masked); } }
        finally { UnityEngine.Object.DestroyImmediate(white); UnityEngine.Object.DestroyImmediate(black); UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckAdditiveColor()
    {
        var graph = Surface("core.toonSurface", Color.white); var red = AddLight(LightType.Point, Color.red, 5, Quaternion.identity); red.transform.position = new Vector3(-1, 0, -2); var blue = AddLight(LightType.Point, Color.blue, 5, Quaternion.identity); blue.transform.position = new Vector3(1, 0, -2);
        try { using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; var color = Capture(); if (color.r < .02f || color.b < .02f) throw new InvalidOperationException("additional lights did not add color: " + color); } }
        finally { UnityEngine.Object.DestroyImmediate(red); UnityEngine.Object.DestroyImmediate(blue); }
    }

    static void CheckEmission()
    {
        var graph = Surface("core.toonSurface", Color.black); Add(graph, ColorNode("emission", Color.red)); Connect(graph, "emission", "value", "surface", "emission", "emission"); var light = AddLight(LightType.Point, Color.white, 8, Quaternion.identity); light.transform.position = new Vector3(0, 0, -1);
        try { using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; var lit = Capture(); light.enabled = false; var dark = Capture(); if (lit.r < .1f || dark.r < .1f || Mathf.Abs(lit.r - dark.r) > .08f) throw new InvalidOperationException("emission was multiplied by lighting: " + lit + " / " + dark); } }
        finally { UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckUnlit()
    {
        var graph = Surface("core.unlitSurface", Color.red); var light = AddLight(LightType.Point, Color.white, 20, Quaternion.identity); light.transform.position = new Vector3(0, 0, -1);
        try { using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; light.enabled = false; var dark = Capture(); light.enabled = true; var lit = Capture(); if (Vector4.Distance(dark, lit) > .08f || dark.r < .3f) throw new InvalidOperationException("Unlit changed with lighting: " + dark + " / " + lit); } }
        finally { UnityEngine.Object.DestroyImmediate(light); }
    }

    static void CheckAdvancedSurfaces()
    {
        var directional = AddLight(LightType.Directional, Color.white, 1, Quaternion.Euler(25, -25, 0));
        var point = AddLight(LightType.Point, Color.red, 6, Quaternion.identity); point.transform.position = new Vector3(0, 0, -1); point.range = 3;
        var spot = AddLight(LightType.Spot, Color.blue, 6, Quaternion.LookRotation(Vector3.forward, Vector3.up)); spot.transform.position = new Vector3(0, 0, -2); spot.range = 5; spot.spotAngle = 45;
        foreach (var operation in new[] { "core.pbrSurface", "core.toonSurface" })
        {
            var graph = Surface(operation, new Color(.4f, .2f, .1f, 1)); if (operation == "core.toonSurface") graph.Nodes[0].Properties["opacity"] = .75;
            using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; if (preview.Material.FindPass("ForwardAdd") < 0) throw new InvalidOperationException(operation + " has no ForwardAdd pass"); Compile(preview.Material); CompileVariants(preview.Material); var withAdditional = Capture(); point.enabled = false; spot.enabled = false; var primaryOnly = Capture(); point.enabled = true; spot.enabled = true; if (Vector4.Distance(withAdditional, primaryOnly) < .025f) throw new InvalidOperationException(operation + " ignored additional lights: " + withAdditional + " / " + primaryOnly); }
        }
        UnityEngine.Object.DestroyImmediate(directional); UnityEngine.Object.DestroyImmediate(point); UnityEngine.Object.DestroyImmediate(spot);
    }

    static ShaderGraph Surface(string operation, Color color) { var graph = new ShaderGraph { GraphId = "pixel-light-" + operation }; var surface = NodeCatalog.Create(operation); surface.Id = "surface"; graph.Nodes.Add(surface); Add(graph, ColorNode("albedo", color)); Connect(graph, "albedo", "value", "surface", "albedo", "albedo"); var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output); Connect(graph, "surface", "surface", "output", "surface", "out"); return graph; }
    static void Add(ShaderGraph graph, GraphNode node) { graph.Nodes.Add(node); }
    static GraphNode ColorNode(string id, Color color) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(color.r, color.g, color.b, color.a) } }; }
    static void Connect(ShaderGraph graph, string from, string port, string to, string input, string id) { graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
    static Light AddLight(LightType type, Color color, float intensity, Quaternion rotation) { var go = new GameObject("NXSG pixel light"); var light = go.AddComponent<Light>(); light.type = type; light.color = color; light.intensity = intensity; light.renderMode = LightRenderMode.ForcePixel; go.transform.rotation = rotation; return light; }
    static void Compile(Material material) { for (var pass = 0; pass < material.passCount; pass++) { ShaderUtil.CompilePass(material, pass, true); if (!material.SetPass(pass)) throw new InvalidOperationException("shader pass failed: " + pass); } }
    static void CompileVariants(Material material)
    {
        var variants = new ShaderVariantCollection();
        foreach (var light in new[] { "POINT", "POINT_COOKIE", "SPOT", "DIRECTIONAL_COOKIE" })
            foreach (var shadow in new[] { "SHADOWS_DEPTH", "SHADOWS_CUBE" })
                variants.Add(new ShaderVariantCollection.ShaderVariant(material.shader, PassType.ForwardAdd, light, shadow));
        variants.WarmUp();
        UnityEngine.Object.DestroyImmediate(variants);
    }
    static Color Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var color = image.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(image); return color; }
}
