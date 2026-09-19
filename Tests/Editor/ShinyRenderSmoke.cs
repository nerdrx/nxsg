using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod ShinyRenderSmoke.Run.
public static class ShinyRenderSmoke
{
    const int Size = 96;
    static Camera camera;
    static GameObject quad;
    static RenderTexture target;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            camera = new GameObject("NXSG shiny camera").AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1.3f; camera.transform.position = new Vector3(0, 0, -2); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            CheckIridescence(); CheckInterior(); CheckBomb(); CheckRefraction(); CheckSubsurface(); CheckRefractionBuild(); CheckSamples();
            Debug.Log("NXSG SHINY RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject); if (quad != null) UnityEngine.Object.DestroyImmediate(quad); }
    }

    static void CheckIridescence()
    {
        var graph = ColorGraph("core.iridescence"); Color[] front, side;
        using (var preview = GraphPreview.Create(graph, null)) { quad.GetComponent<Renderer>().sharedMaterial = preview.Material; camera.transform.position = new Vector3(0, 0, -2); front = Capture(); camera.transform.position = new Vector3(.8f, 0, -2); camera.transform.LookAt(Vector3.zero); side = Capture(); }
        RequireFinite(front); RequireFinite(side); if (Changed(front, side) < 20) throw new InvalidOperationException("Iridescence did not change with view angle.");
    }

    static void CheckInterior()
    {
        var graph = ColorGraph("core.interiorMapping"); var room = Atlas(8); try { Color[] a, b; using (var preview = GraphPreview.Create(graph, null)) { SetTextures(preview.Material, room); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; camera.transform.position = new Vector3(0, 0, -2); camera.transform.LookAt(Vector3.zero); a = Capture(); camera.transform.position = new Vector3(.7f, .2f, -2); camera.transform.LookAt(Vector3.zero); b = Capture(); } RequireFinite(a); RequireFinite(b); if (Changed(a, b) < 20) throw new InvalidOperationException("Interior mapping did not change with camera angle."); } finally { UnityEngine.Object.DestroyImmediate(room); }
    }

    static void CheckBomb()
    {
        var graph = ColorGraph("core.textureBomb"); var texture = Atlas(2); try { Color[] a, b; using (var preview = GraphPreview.Create(graph, null)) { SetTextures(preview.Material, texture); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; a = Capture(); } graph.Nodes.Single(n => n.Id == "effect").Properties["blend"] = 0; using (var preview = GraphPreview.Create(graph, null)) { SetTextures(preview.Material, texture); quad.GetComponent<Renderer>().sharedMaterial = preview.Material; b = Capture(); } RequireFinite(a); RequireFinite(b); if (Changed(a, b) < 20) throw new InvalidOperationException("Texture bomb did not differ from plain sampling."); } finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    static void CheckRefraction()
    {
        var background = GameObject.CreatePrimitive(PrimitiveType.Quad); var checker = Atlas(8); var backgroundMaterial = new Material(Shader.Find("Unlit/Texture")); backgroundMaterial.mainTexture = checker; background.transform.position = new Vector3(0, 0, 1); background.transform.localScale = Vector3.one * 4; background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
        try { var graph = ColorGraph("core.refraction"); using (var preview = GraphPreview.Create(graph, null)) { quad.GetComponent<Renderer>().sharedMaterial = preview.Material; var a = Capture(); graph.Nodes.Single(n => n.Id == "effect").Properties["strength"] = .35; using (var bent = GraphPreview.Create(graph, null)) { quad.GetComponent<Renderer>().sharedMaterial = bent.Material; var b = Capture(); RequireFinite(a); RequireFinite(b); if (Changed(a, b) < 5) throw new InvalidOperationException("Refraction strength did not affect checker GrabPass sample."); } } } finally { UnityEngine.Object.DestroyImmediate(backgroundMaterial); UnityEngine.Object.DestroyImmediate(checker); UnityEngine.Object.DestroyImmediate(background); }
    }

    static void CheckSubsurface()
    {
        var lightObject = new GameObject("NXSG subsurface light"); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; var graph = ColorGraph("core.subsurface"); try { Color[] front, back; using (var preview = GraphPreview.Create(graph, null)) { quad.GetComponent<Renderer>().sharedMaterial = preview.Material; lightObject.transform.rotation = Quaternion.Euler(0, 0, 0); front = Capture(); lightObject.transform.rotation = Quaternion.Euler(0, 180, 0); back = Capture(); } RequireFinite(front); RequireFinite(back); if (Changed(front, back) < 20) throw new InvalidOperationException("Subsurface backlight did not change output."); } finally { UnityEngine.Object.DestroyImmediate(lightObject); }
    }

    static void CheckRefractionBuild()
    {
        const string path = "Assets/NXSGRefractionBuildSmoke.nxsg";
        var graph = ColorGraph("core.refraction");
        File.WriteAllText(path, GraphJson.Serialize(graph, true));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var directory = "Assets/NXSGGenerated/" + AssetDatabase.AssetPathToGUID(path);
        try
        {
            var material = GraphBuild.Build(graph, path);
            if (material == null || ShaderUtil.ShaderHasError(material.shader)) throw new InvalidOperationException("Refraction build failed.");
            if (!ShaderUtil.GetShaderData(material.shader).ActiveSubshader.GetPass(0).IsGrabPass) throw new InvalidOperationException("Built refraction lost framebuffer capture.");
        }
        finally { AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(directory); }
    }

    static void CheckSamples()
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GraphWindow).Assembly);
        foreach (var name in new[] { "Fur Fins", "Shiny Surface", "Refraction Glass", "Interior Bomb" })
        {
            var graph = GraphJson.Parse(File.ReadAllText(Path.Combine(package.resolvedPath, "Samples~/" + name + ".nxsg")));
            using (var preview = GraphPreview.Create(graph, null))
                if (ShaderUtil.ShaderHasError(preview.Material.shader)) throw new InvalidOperationException(name + " sample shader failed.");
        }
    }

    static ShaderGraph ColorGraph(string operation)
    {
        var graph = new ShaderGraph { GraphId = "shiny-render-" + operation }; var effect = NodeCatalog.Create(operation); effect.Id = "effect"; graph.Nodes.Add(effect);
        if (operation == "core.interiorMapping" || operation == "core.textureBomb") { effect.Properties["resourceId"] = "texture"; graph.Resources.Add(new GraphResource { Id = "texture", Kind = "texture2D", Uri = "builtin://white" }); }
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(graph, "effect", "color", "surface", "albedo"); Edge(graph, "surface", "surface", "output", "surface"); return graph;
    }

    static Texture2D Atlas(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true); var colors = new Color[size * size]; for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) colors[y * size + x] = new Color((x + 1f) / size, (y + 1f) / size, ((x ^ y) & 3) / 3f, 1); texture.SetPixels(colors); texture.Apply(); texture.wrapMode = TextureWrapMode.Repeat; return texture;
    }

    static void SetTextures(Material material, Texture2D texture) { for (var i = 0; i < ShaderUtil.GetPropertyCount(material.shader); i++) if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) material.SetTexture(ShaderUtil.GetPropertyName(material.shader, i), texture); }
    static Color[] Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(value => value > .02f); }
    static void RequireFinite(Color[] pixels) { if (pixels.Any(c => float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b))) throw new InvalidOperationException("Shiny node produced non-finite pixels."); }
    static void Edge(ShaderGraph graph, string from, string port, string to, string input) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
}
