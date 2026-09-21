using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod FurCardRenderSmoke.Run.
public static class FurCardRenderSmoke
{
    const int Size = 256;
    static GameObject subject; static Camera camera; static RenderTexture target;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var key = new GameObject("Fin key light").AddComponent<Light>(); key.type = LightType.Directional; key.transform.rotation = Quaternion.Euler(25, -25, 0);
            var ambient = new SphericalHarmonicsL2(); ambient.AddAmbientLight(Color.gray); RenderSettings.ambientProbe = ambient;
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            camera = new GameObject("NXSG Fur Fin Camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true; camera.orthographicSize = .85f; camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            CheckToggle();
            Debug.Log("NXSG FUR CARD RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject); if (subject != null) UnityEngine.Object.DestroyImmediate(subject); }
    }

    static void CheckToggle()
    {
        var on = Graph(1);
        var source = ShaderEmitter.Emit(on).ShaderSource;
        if (source == null || !source.Contains("Name \"FurCards\"") || source.Contains("Name \"Fur1\"") || source.Contains("Name \"FurFins\""))
            throw new InvalidOperationException("Cards-only mode emitted shell or duplicate fin passes.");
        Color[] visible;
        using (var preview = GraphPreview.Create(on, null))
        {
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
            for (var pass = 0; pass < preview.Material.passCount; pass++)
                if (!preview.Material.SetPass(pass)) throw new InvalidOperationException("Card pass failed: " + pass);
            visible = Capture(); EnsureFinite(visible); Save(visible, "fur-cards-only.png");
        }
        on.Nodes.Single(n => n.Id == "mask").Properties["value"] = 0;
        Color[] masked;
        using (var preview = GraphPreview.Create(on, null))
        { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; masked = Capture(); }
        if (Changed(visible, masked) < 100) throw new InvalidOperationException("Cards did not render or mask had no effect.");
        // The front centre is not a silhouette: cards must not inherit the fins' grazing-only gate.
        var centreChanges = 0;
        for (int y=Size/2-35;y<Size/2+35;y++) for(int x=Size/2-35;x<Size/2+35;x++)
            if (Vector3.Distance(new Vector3(visible[y*Size+x].r,visible[y*Size+x].g,visible[y*Size+x].b),new Vector3(masked[y*Size+x].r,masked[y*Size+x].g,masked[y*Size+x].b))>.03f) centreChanges++;
        if(centreChanges<20)throw new InvalidOperationException("Cards missing from front-facing surface.");
        var shells=Graph(0); var shellSource=ShaderEmitter.Emit(shells).ShaderSource;
        if(!shellSource.Contains("Name \"Fur1\"") || shellSource.Contains("Name \"FurCards\""))throw new InvalidOperationException("Default shells changed.");
    }

    static ShaderGraph Graph(int fins)
    {
        var graph = new ShaderGraph { GraphId = "fur-fin-render" };
        graph.Nodes.Add(ColorNode("baseColor", new JArray(.03, .03, .03, 1))); graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" });
        graph.Nodes.Add(ColorNode("rootColor", new JArray(.35, .12, .03, 1))); graph.Nodes.Add(ColorNode("tipColor", new JArray(1, .7, .15, 1)));
        graph.Nodes.Add(new GraphNode { Id = "mask", Operation = "core.value", Properties = new JObject { ["value"] = 1.0 } });
        var fur = NodeCatalog.Create("core.fur"); fur.Id = "fur"; fur.Properties["length"] = .18; fur.Properties["density"] = 100; fur.Properties["layers"] = 4; fur.Properties["fins"] = 1; fur.Properties["cardsOnly"] = fins; fur.Properties["windStrength"] = 0; fur.Properties["finOpacity"] = .7; graph.Nodes.Add(fur);
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "fur", "surface", "output", "surface"); Edge(graph, "baseColor", "value", "base", "albedo"); Edge(graph, "base", "surface", "fur", "base"); Edge(graph, "rootColor", "value", "fur", "rootColor"); Edge(graph, "tipColor", "value", "fur", "tipColor"); Edge(graph, "mask", "value", "fur", "mask");
        return graph;
    }

    static GraphNode ColorNode(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    static Color[] Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static void EnsureFinite(Color[] pixels) { if (pixels.Any(c => float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b))) throw new InvalidOperationException("Fur fin capture contained nonfinite pixels."); }
    static void Save(Color[] pixels, string name) { var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true); image.SetPixels(pixels.Select(c => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1)).ToArray()); image.Apply(); Directory.CreateDirectory("Library/NXSG"); File.WriteAllBytes(Path.Combine("Library/NXSG", name), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(delta => delta > .03f); }
}
