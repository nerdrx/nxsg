using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod ParticleRenderSmoke.Run.
public static class ParticleRenderSmoke
{
    const int Size = 64;
    static GameObject background, subject, cameraObject;
    static Material backgroundMaterial;
    static Camera camera;
    static RenderTexture target;
    static GraphPreview backgroundPreview;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            background = GameObject.CreatePrimitive(PrimitiveType.Quad); background.transform.position = new Vector3(0, 0, .25f); background.transform.localScale = Vector3.one * 2;
            var backgroundGraph = new ShaderGraph { GraphId = "particle-background" };
            backgroundGraph.Nodes.Add(ColorNode("blue",new JArray(0,0,1,1)));
            backgroundGraph.Nodes.Add(new GraphNode {Id="surface",Operation="core.unlitSurface"});
            backgroundGraph.Nodes.Add(new GraphNode {Id="output",Operation="core.output"});
            Edge(backgroundGraph,"blue","value","surface","albedo"); Edge(backgroundGraph,"surface","surface","output","surface");
            backgroundPreview = GraphPreview.Create(backgroundGraph,null); backgroundMaterial = backgroundPreview.Material; background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;
            subject = GameObject.CreatePrimitive(PrimitiveType.Quad); subject.transform.position = Vector3.zero; subject.transform.localScale = Vector3.one * 2;
            var colors = new Color[subject.GetComponent<MeshFilter>().sharedMesh.vertexCount]; for (var i = 0; i < colors.Length; i++) colors[i] = new Color(1, 1, 1, .5f);
            var mesh = UnityEngine.Object.Instantiate(subject.GetComponent<MeshFilter>().sharedMesh); mesh.colors = colors; subject.GetComponent<MeshFilter>().sharedMesh = mesh;
            cameraObject = new GameObject("NXSG Particle Camera"); camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true; camera.orthographicSize = 1; camera.transform.position = new Vector3(0, 0, -3); camera.transform.LookAt(Vector3.zero); camera.depthTextureMode = DepthTextureMode.Depth;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;

            CheckSurfaceState();
            CheckBlendModes();
            CheckVertexColorAndOpacity();
            CheckSoftFade();
            CheckColorInput();
            CheckSample();
            CheckParticleSystemRenderer();
            Debug.Log("NXSG PARTICLE RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject); if (subject != null) UnityEngine.Object.DestroyImmediate(subject); if (background != null) UnityEngine.Object.DestroyImmediate(background); backgroundPreview?.Dispose(); }
    }

    static void CheckSurfaceState()
    {
        using (var preview = GraphPreview.Create(Graph(0, 0), null))
        {
            var material = preview.Material;
            if (material.renderQueue < 3000) throw new InvalidOperationException("particle surface is not transparent queue");
            if (material.FindPass("ShadowCaster") >= 0) throw new InvalidOperationException("particle surface emits shadow caster");
        }
    }

    static void CheckBlendModes()
    {
        var alpha = RenderGraph(Graph(0, 0));
        var additive = RenderGraph(Graph(1, 0));
        if (alpha.r < .12f || alpha.r > .38f || alpha.b < .62f) throw new InvalidOperationException("particle alpha blend wrong: " + alpha);
        if (Mathf.Abs(additive.r-alpha.r) > .03f || additive.b < .95f || additive.b < alpha.b+.15f) throw new InvalidOperationException("particle additive blend wrong: " + additive + " / " + alpha);
    }

    static void CheckVertexColorAndOpacity()
    {
        var halfVertex = RenderGraph(Graph(0, 0));
        var fullVertex = subject.GetComponent<MeshFilter>().sharedMesh;
        var colors = new Color[fullVertex.vertexCount]; for (var i = 0; i < colors.Length; i++) colors[i] = Color.white; fullVertex.colors = colors;
        var full = RenderGraph(Graph(0, 0));
        if (full.r < halfVertex.r + .12f || full.b > halfVertex.b - .12f) throw new InvalidOperationException("particle vertex alpha is ignored: " + halfVertex + " / " + full);
        for (var i = 0; i < colors.Length; i++) colors[i] = Color.white; fullVertex.colors = colors;
    }

    static void CheckSoftFade()
    {
        var hard = RenderGraph(Graph(0, 0));
        var soft = RenderGraph(Graph(0, .5));
        if (Mathf.Abs(soft.r-hard.r*.5f) > .06f) throw new InvalidOperationException("particle soft fade did not use camera depth: " + hard + " / " + soft);
        camera.orthographic = false;
        var perspectiveSoft = RenderGraph(Graph(0,.5));
        if (Mathf.Abs(perspectiveSoft.r-hard.r*.5f) > .06f) throw new InvalidOperationException("perspective depth fade wrong: " + perspectiveSoft);
        camera.orthographic = true;
        var disabled = RenderGraph(Graph(0, 0));
        if (Mathf.Abs(disabled.r - hard.r) > .03f) throw new InvalidOperationException("particle soft fade disabled state changed output");
    }

    static void CheckColorInput()
    {
        var mesh = subject.GetComponent<MeshFilter>().sharedMesh;
        var colors = new Color[mesh.vertexCount];
        for(var i=0;i<colors.Length;i++) colors[i]=new Color(.2f,.6f,.1f,1);
        mesh.colors=colors;
        var graph=Graph(0,0);
        graph.Nodes.Find(n=>n.Id=="surface").Operation="core.unlitSurface";
        graph.Connections.RemoveAll(e=>e.To.NodeId=="surface" && e.To.PortId=="albedo");
        Edge(graph,"particleColor","color","surface","albedo");
        var actual=RenderGraph(graph);
        if(Mathf.Abs(actual.r-.2f)>.03f || Mathf.Abs(actual.g-.6f)>.03f) throw new InvalidOperationException("Particle Color RGB read wrong: "+actual);
        mesh.colors=null;
        actual=RenderGraph(Graph(0,0));
        if(actual.r<.95f) throw new InvalidOperationException("Mesh without COLOR should preview white vertex color: "+actual);
    }

    static void CheckParticleSystemRenderer()
    {
        subject.SetActive(false);
        var particles = new GameObject("NXSG Particle System"); var system = particles.AddComponent<ParticleSystem>(); var renderer = particles.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Billboard;
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); var shape = system.shape; shape.enabled = false; system.useAutoRandomSeed = false; system.randomSeed = 1; var main = system.main; main.loop = false; main.startLifetime = 2; main.startSpeed = 0; main.startSize = 1; main.startColor = Color.white; var lifetime = system.colorOverLifetime; lifetime.enabled = true; var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(.8f, 1) }); lifetime.color = new ParticleSystem.MinMaxGradient(gradient); var emission = system.emission; emission.enabled = true; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, 1) });
        using (var preview = GraphPreview.Create(Graph(0, 0), null))
        {
            renderer.sharedMaterial = preview.Material; system.Play(); system.Simulate(.1f, true, true, true); var image = Capture(); var early = image.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(image); system.Simulate(1.5f, true, true, true); image = Capture(); var late = image.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(image); if (early.r < .03f || Mathf.Abs(early.r - late.r) < .03f) throw new InvalidOperationException("ParticleSystemRenderer lifetime color did not render: " + early + " / " + late);
        }
        UnityEngine.Object.DestroyImmediate(particles); subject.SetActive(true);
    }

    static void CheckSample()
    {
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/dev.nerdrx.nxsg");
        if (package == null) throw new InvalidOperationException("NXSG package not found");
        var path = System.IO.Path.Combine(package.resolvedPath, "Samples~/Particle Sparkles.nxsg");
        if (!System.IO.File.Exists(path)) throw new InvalidOperationException("Particle Sparkles sample missing");
        using (var preview = GraphPreview.Create(GraphJson.Parse(System.IO.File.ReadAllText(path)), null))
        {
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material; var image = Capture(); var pixel = image.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(image); if (float.IsNaN(pixel.r) || float.IsInfinity(pixel.r)) throw new InvalidOperationException("Particle Sparkles sample rendered non-finite pixel: " + pixel);
        }
    }

    static Color RenderGraph(ShaderGraph graph)
    {
        using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; var image = Capture(); var pixel = image.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(image); return pixel; }
    }

    static Texture2D Capture()
    {
        camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); RenderTexture.active = null; return image;
    }

    static ShaderGraph Graph(int blendMode, double softDistance)
    {
        var graph = new ShaderGraph { GraphId = "particle-render" }; graph.Nodes.Add(ColorNode("albedo", new JArray(1, 0, 0, 1))); graph.Nodes.Add(ColorNode("emission", new JArray(0, 0, 0, 1))); graph.Nodes.Add(Float("opacity", .5)); graph.Nodes.Add(NodeCatalog.Create("core.particleColor")); graph.Nodes[3].Id = "particleColor";
        var surface = NodeCatalog.Create("core.particleSurface"); surface.Id = "surface"; surface.Properties["blendMode"] = blendMode; surface.Properties["softDistance"] = softDistance; graph.Nodes.Add(surface); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "albedo", "value", "surface", "albedo"); Edge(graph, "emission", "value", "surface", "emission"); Edge(graph, "particleColor", "alpha", "surface", "opacity"); Edge(graph, "surface", "surface", "output", "surface"); return graph;
    }

    static GraphNode ColorNode(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
}
