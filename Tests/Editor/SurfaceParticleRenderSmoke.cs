using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod SurfaceParticleRenderSmoke.Run.
public static class SurfaceParticleRenderSmoke
{
    const int Size = 256;
    static GameObject subject; static Camera camera; static RenderTexture target;
    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            subject = GameObject.CreatePrimitive(PrimitiveType.Cube); subject.transform.localScale = Vector3.one * 1.4f;
            camera = new GameObject("NXSG Surface Particle Camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true; camera.orthographicSize = 2.5f; camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            CheckRender(); CheckSourceVertexAlpha(); CheckRate(); CheckSkinnedSource(); CheckSample(); Debug.Log("NXSG SURFACE PARTICLE RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject); if (subject != null) UnityEngine.Object.DestroyImmediate(subject); }
    }
    static void CheckRender()
    {
        var graph = Graph(0, 1, .2); Color[] baseOnly, first, second, later;
        using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; baseOnly = Capture(); }
        graph = Graph(1, 1, .2); using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; first = Capture(); second = Capture(); }
        graph = Graph(1, 1, 1); using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; later = Capture(); }
        if (CountRed(first) <= CountRed(baseOnly) + 2) throw new InvalidOperationException("surfaceParticles produced no visible red particles");
        if (!Same(first, second, .001f)) throw new InvalidOperationException("surfaceParticles same time is not deterministic");
        if (Changed(first, later) < 8) throw new InvalidOperationException("surfaceParticles time did not change pixels");
        graph = Graph(1, 0, .2); using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; if (CountRed(Capture()) > CountRed(baseOnly) + 2) throw new InvalidOperationException("surfaceParticles mask 0 still rendered"); }
        graph = Graph(1, 1, .2); camera.transform.position = new Vector3(2.5f, 1.2f, -3.2f); camera.transform.LookAt(Vector3.zero); using (var preview = GraphPreview.Create(graph, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; if (CountRed(Capture()) < 8) throw new InvalidOperationException("surfaceParticles disappeared after camera rotation"); }
    }
    static void CheckSourceVertexAlpha()
    {
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        var filter = subject.GetComponent<MeshFilter>();
        var original = filter.sharedMesh;
        var mesh = UnityEngine.Object.Instantiate(original);
        filter.sharedMesh = mesh;
        try
        {
            mesh.colors = Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray();
            Color[] white;
            using (var preview = GraphPreview.Create(Graph(1, 1, .2), null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; white = Capture(); }

            // Hair and other authored submeshes can carry alpha-zero vertex colors. That
            // source data must not silently disable particles when mask/opacity are 1.
            mesh.colors = Enumerable.Repeat(new Color(0, 0, 0, 0), mesh.vertexCount).ToArray();
            Color[] alphaZero;
            using (var preview = GraphPreview.Create(Graph(1, 1, .2), null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; alphaZero = Capture(); }
            if (CountRed(white) < 3 || !Same(alphaZero, white, .001f))
                throw new InvalidOperationException("surfaceParticles inherited source vertex RGB or alpha");

            // Explicit graph wiring remains authoritative: Vertex Color RGB can still
            // drive the emitter mask when the creator asks for it.
            mesh.colors = Enumerable.Repeat(new Color(0, 0, 0, 1), mesh.vertexCount).ToArray();
            using (var preview = GraphPreview.Create(GraphWithVertexMask(), null))
            {
                subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                if (CountRed(Capture()) > 2) throw new InvalidOperationException("Vertex Color mask did not hide surface particles");
            }
        }
        finally
        {
            filter.sharedMesh = original;
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
    static void CheckRate()
    {
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        // Sum several times so random triangle phases do not bias rate comparisons.
        double none = 0, slow = 0, fast = 0;
        foreach (var rate in new[] { 0.0, .25, 2.0, 1000.0 })
        {
            double total = 0;
            foreach (var time in new[] { .2, .8, 1.4, 2.0 })
            {
                var graph = Graph(1, 1, time);
                graph.Nodes.Single(n => n.Id == "particles").Properties["emissionRate"] = rate;
                using (var preview = GraphPreview.Create(graph, null))
                {
                    subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                    total += Capture().Sum(c => (double)c.r);
                }
            }
            if (rate == 0) none = total;
            else if (rate == .25) slow = total;
            else if (rate == 2) fast = total;
            else if (total < fast * 3) throw new InvalidOperationException("High emission rate did not amplify particles");
        }
        var beans = Graph(1, 1, .6);
        beans.Nodes.Single(n => n.Id == "particles").Properties["emissionRate"] = 10000;
        beans.Nodes.Single(n => n.Id == "particles").Properties["lifetime"] = 1;
        beans.Nodes.Single(n => n.Id == "particles").Properties["size"] = .01;
        using(var preview=GraphPreview.Create(beans,null))
        {
            subject.GetComponent<Renderer>().sharedMaterial=preview.Material;
            var pixels=Capture();
            if(pixels.Any(c=>float.IsNaN(c.r)||float.IsInfinity(c.r))||CountRed(pixels)<100)
                throw new InvalidOperationException("10k/triangle/sec did not render finite visible particles");
        }
        if (none > .01 || slow <= .01 || fast < slow * 1.5)
            throw new InvalidOperationException("Emission rate did not control visible population: " + none + " / " + slow + " / " + fast);
    }
    static double RedCenter(Color[] pixels)
    {
        double sum=0,weight=0;
        for(var i=0;i<pixels.Length;i++) { var w=Math.Max(0,pixels[i].r-pixels[i].g); sum+=(i%Size)*w;weight+=w; }
        if(weight<.1)throw new InvalidOperationException("No source particles for position test");
        return sum/weight;
    }
    static void CheckSkinnedSource()
    {
        camera.transform.position=new Vector3(0,0,-4);camera.transform.LookAt(Vector3.zero);
        var mesh=UnityEngine.Object.Instantiate(subject.GetComponent<MeshFilter>().sharedMesh);
        var weights=new BoneWeight[mesh.vertexCount];for(var i=0;i<weights.Length;i++)weights[i]=new BoneWeight {boneIndex0=0,weight0=1};
        var bone=new GameObject("Emitter bone");bone.transform.SetParent(subject.transform,false);
        mesh.boneWeights=weights;mesh.bindposes=new[]{bone.transform.worldToLocalMatrix*subject.transform.localToWorldMatrix};
        subject.GetComponent<MeshRenderer>().enabled=false;
        var skin=subject.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=new[]{bone.transform};skin.rootBone=bone.transform;skin.updateWhenOffscreen=true;skin.localBounds=new Bounds(Vector3.zero,Vector3.one*8);
        using(var preview=GraphPreview.Create(Graph(1,1,.2),null))
        {
            skin.sharedMaterial=preview.Material;var before=RedCenter(Capture());
            bone.transform.localPosition=new Vector3(.6f,0,0);var after=RedCenter(Capture());
            if(after-before<Size*.08)throw new InvalidOperationException("Particles did not follow skinned source bone: "+before+" / "+after);
        }
        UnityEngine.Object.DestroyImmediate(skin);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(bone);
    }
    static void CheckSample()
    {
        UnityEngine.Object.DestroyImmediate(subject);subject=GameObject.CreatePrimitive(PrimitiveType.Sphere);subject.transform.localScale=Vector3.one*2;
        camera.orthographicSize=1.6f;
        var light=new GameObject("Sample light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(35,-30,0);
        var package=UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/dev.nerdrx.nxsg");
        var graph=GraphJson.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(package.resolvedPath,"Samples~/Surface Sparkles.nxsg")));
        graph.Nodes.Add(Float("sampleTime",.6));Edge(graph,"sampleTime","value","particles","time");
        using(var preview=GraphPreview.Create(graph,null))
        {
            subject.GetComponent<Renderer>().sharedMaterial=preview.Material;var pixels=Capture();
            if(CountRed(pixels)<20)throw new InvalidOperationException("Surface Sparkles sample did not render");
            var image=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);image.SetPixels(pixels);image.Apply();
            System.IO.Directory.CreateDirectory("Library/NXSG");System.IO.File.WriteAllBytes("Library/NXSG/surface-particles.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        UnityEngine.Object.DestroyImmediate(light.gameObject);
    }
    static ShaderGraph Graph(double density, double mask, double time)
    {
        var graph = new ShaderGraph { GraphId = "surface-particle-render" }; graph.Nodes.Add(ColorNode("black", new JArray(0, 0, 0, 1))); graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" }); graph.Nodes.Add(ColorNode("red", new JArray(1, 0, 0, 1))); graph.Nodes.Add(Float("time", time)); graph.Nodes.Add(Float("mask", mask)); graph.Nodes.Add(new GraphNode { Id = "particles", Operation = "core.surfaceParticles", Properties = new JObject { ["density"] = density, ["size"] = .15 } }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(graph, "black", "value", "base", "albedo"); Edge(graph, "base", "surface", "particles", "base"); Edge(graph, "red", "value", "particles", "albedo"); Edge(graph, "time", "value", "particles", "time"); Edge(graph, "mask", "value", "particles", "mask"); Edge(graph, "particles", "surface", "output", "surface"); return graph;
    }
    static ShaderGraph GraphWithVertexMask()
    {
        var graph = Graph(1, 1, .2);
        graph.Nodes.Add(new GraphNode { Id = "vertexColor", Operation = "core.vertexColor" });
        graph.Connections.RemoveAll(c => c.To.NodeId == "particles" && c.To.PortId == "mask");
        Edge(graph, "vertexColor", "color", "particles", "mask");
        return graph;
    }
    static GraphNode ColorNode(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort) { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    static Color[] Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static int CountRed(Color[] pixels) { return pixels.Count(pixel => pixel.r > .04f && pixel.r > pixel.g * 1.5f && pixel.r > pixel.b * 1.5f); }
    static bool Same(Color[] a, Color[] b, float tolerance) { return a.Length == b.Length && a.Zip(b, (x, y) => Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b)).All(delta => delta <= tolerance); }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(delta => delta > .03f); }
}
