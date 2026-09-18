using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod FurRenderSmoke.Run.
public static class FurRenderSmoke
{
    const int Size = 160;
    static GameObject subject;
    static Camera camera;
    static RenderTexture target;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            camera = new GameObject("NXSG Fur Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true; camera.orthographicSize = 2.2f; camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            CheckVisibilityAndBounds(); CheckWindAndLod(); CheckLayersAndParticles();
            Debug.Log("NXSG FUR RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject); if (subject != null) UnityEngine.Object.DestroyImmediate(subject); }
    }

    static void CheckVisibilityAndBounds()
    {
        var baseGraph = Graph(false, 0, .04, 0);
        Color[] basePixels;
        using (var preview = GraphPreview.Create(baseGraph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; basePixels = Capture(); }
        var fur = Graph(false, 1, .04, 0);
        Color[] shortPixels;
        using (var preview = GraphPreview.Create(fur, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; shortPixels = Capture(); }
        if (CountColored(shortPixels) <= CountColored(basePixels) + 8) throw new InvalidOperationException("Fur mask 1 produced no visible colored silhouette.");
        fur.Nodes.Single(n => n.Id == "fur").Properties["length"] = .2;
        Color[] longPixels;
        using (var preview = GraphPreview.Create(fur, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; longPixels = Capture(); }
        if (CountColored(longPixels) <= CountColored(shortPixels) + 8) throw new InvalidOperationException("Long fur did not extend the visible silhouette.");
        var masked = Graph(false, 0, .2, 0);
        using (var preview = GraphPreview.Create(masked, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; if (CountColored(Capture()) > CountColored(basePixels) + 4) throw new InvalidOperationException("Fur mask 0 remained visible."); }
    }

    static void CheckWindAndLod()
    {
        var first = Graph(false, 1, .12, 0); first.Nodes.Single(n => n.Id == "fur").Properties["windStrength"] = 1;
        Color[] a, b;
        using (var preview = GraphPreview.Create(first, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; a = Capture(); }
        var later = Graph(false, 1, .12, 1); later.Nodes.Single(n => n.Id == "fur").Properties["windStrength"] = 1;
        using (var preview = GraphPreview.Create(later, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; b = Capture(); }
        if (Changed(a, b) < 8) throw new InvalidOperationException("Fur wind/time did not change rendered pixels.");
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        var near = Graph(false, 1, .12, 0); Color[] nearPixels;
        using (var preview = GraphPreview.Create(near, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; nearPixels = Capture(); }
        camera.transform.position = new Vector3(0, 0, -12); camera.transform.LookAt(Vector3.zero);
        var far = Graph(false, 1, .12, 0); Color[] farPixels;
        using (var preview = GraphPreview.Create(far, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; farPixels = Capture(); }
        if (Changed(nearPixels, farPixels) < 8) throw new InvalidOperationException("Fur distance LOD did not change rendered pixels.");
    }

    static void CheckLayersAndParticles()
    {
        foreach (var layers in new[] { 4, 16 })
        {
            var graph = Graph(false, 1, .08, 0); graph.Nodes.Single(n => n.Id == "fur").Properties["layers"] = layers;
            using (var preview = GraphPreview.Create(graph, null)) for (var pass = 0; pass < preview.Material.passCount; pass++) if (!preview.Material.SetPass(pass)) throw new InvalidOperationException("Fur layer pass failed: " + layers + "/" + pass);
        }
        var particles = Graph(true, 1, .08, 0);
        using (var preview = GraphPreview.Create(particles, null)) for (var pass = 0; pass < preview.Material.passCount; pass++) if (!preview.Material.SetPass(pass)) throw new InvalidOperationException("Fur to Surface Particles pass failed: " + pass);
    }

    static ShaderGraph Graph(bool particles, double mask, double length, double time)
    {
        var graph = new ShaderGraph { GraphId = "fur-render" };
        graph.Nodes.Add(ColorNode("baseColor", new JArray(0, 0, 0, 1))); graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" });
        graph.Nodes.Add(ColorNode("rootColor", new JArray(.35, .12, .03, 1))); graph.Nodes.Add(ColorNode("tipColor", new JArray(1, .7, .15, 1))); graph.Nodes.Add(Float("mask", mask)); graph.Nodes.Add(Float("time", time));
        var groom = NodeCatalog.Create("core.normalDirection"); groom.Id = "groom"; groom.Properties["space"] = 0; graph.Nodes.Add(groom);
        var fur = NodeCatalog.Create("core.fur"); fur.Id = "fur"; fur.Properties["length"] = length; fur.Properties["density"] = 100; fur.Properties["layers"] = 16; fur.Properties["windStrength"] = 1; fur.Properties["lodNear"] = 5; fur.Properties["lodFar"] = 15; graph.Nodes.Add(fur);
        GraphNode output;
        if (particles)
        {
            var particle = NodeCatalog.Create("core.surfaceParticles"); particle.Id = "particles"; particle.Properties["density"] = 1; particle.Properties["size"] = .03; graph.Nodes.Add(particle); output = particle;
            Edge(graph, "fur", "surface", "particles", "base"); Edge(graph, "tipColor", "value", "particles", "albedo"); Edge(graph, "time", "value", "particles", "time");
        }
        else { Edge(graph, "fur", "surface", "output", "surface"); output = null; }
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        if (particles) Edge(graph, "particles", "surface", "output", "surface");
        Edge(graph, "baseColor", "value", "base", "albedo"); Edge(graph, "base", "surface", "fur", "base"); Edge(graph, "rootColor", "value", "fur", "rootColor"); Edge(graph, "tipColor", "value", "fur", "tipColor"); Edge(graph, "mask", "value", "fur", "mask"); Edge(graph, "time", "value", "fur", "time"); Edge(graph, "groom", "normal", "fur", "groom");
        return graph;
    }

    static GraphNode ColorNode(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    static Color[] Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static int CountColored(Color[] pixels) { return pixels.Count(c => c.r > .04f || c.g > .04f || c.b > .04f); }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(delta => delta > .03f); }
}
