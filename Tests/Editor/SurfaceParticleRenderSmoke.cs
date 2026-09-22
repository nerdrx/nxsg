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
            CheckRender(); CheckSourceVertexAlpha(); CheckRate(); CheckParticleInputs(); CheckEdgeSharpness(); CheckMirrorAlpha(); CheckLifetimeCurves(); CheckAudioRange(); CheckSkinnedSource(); CheckSample(); Debug.Log("NXSG SURFACE PARTICLE RENDER SMOKE PASSED"); EditorApplication.Exit(0);
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
    static void CheckParticleInputs()
    {
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        var unconnected = Graph(1, 1, .2);
        unconnected.Nodes.Single(n => n.Id == "particles").Properties["emissionRate"] = 8;
        Color[] defaults;
        using (var preview = GraphPreview.Create(unconnected, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; defaults = Capture(); }

        // Value nodes must override stored properties on every numeric particle socket.
        var wiredDefaults = GraphWithParticleInputs(.2, 1, 8, .15, 2, .2, 0, .05);
        Color[] wired;
        using (var preview = GraphPreview.Create(wiredDefaults, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; wired = Capture(); }
        if (!Same(defaults, wired, .001f)) throw new InvalidOperationException("Connected particle defaults changed rendering");

        var sizeZero = GraphWithParticleInputs(.2, 1, .5, 0, 2, .2, 0, .05);
        using (var preview = GraphPreview.Create(sizeZero, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; if (CountRed(Capture()) > 2) throw new InvalidOperationException("Connected particle size 0 did not hide particles"); }
        var densityZero = GraphWithParticleInputs(.2, 0, .5, .15, 2, .2, 0, .05);
        using (var preview = GraphPreview.Create(densityZero, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; if (CountRed(Capture()) > 2) throw new InvalidOperationException("Connected particle density 0 did not hide particles"); }

        Color[] rateZero, rateHigh;
        var zero = GraphWithParticleInputs(.6, 1, 0, .15, 2, .2, 0, .05);
        using (var preview = GraphPreview.Create(zero, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; rateZero = Capture(); }
        var high = GraphWithParticleInputs(.6, 1, 8, .15, 2, .2, 0, .05);
        PublishParticleInputShader(high);
        using (var preview = GraphPreview.Create(high, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; rateHigh = Capture(); }
        if (CountRed(rateHigh) < CountRed(rateZero) + 4) throw new InvalidOperationException("Connected emission rate did not increase visible particles");
    }
    static void CheckEdgeSharpness()
    {
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        var legacy = Graph(1, 1, .2); var particle = legacy.Nodes.Single(n => n.Id == "particles");
        particle.Properties["emissionRate"] = 8; particle.Properties["size"] = .2; particle.Properties.Remove("edgeSharpness");
        Color[] fallback;
        using (var preview = GraphPreview.Create(legacy, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; fallback = Capture(); }

        var soft = Graph(1, 1, .2); particle = soft.Nodes.Single(n => n.Id == "particles");
        particle.Properties["emissionRate"] = 8; particle.Properties["size"] = .2; particle.Properties["edgeSharpness"] = 0;
        Color[] explicitZero;
        using (var preview = GraphPreview.Create(soft, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; explicitZero = Capture(); }
        if (!Same(fallback, explicitZero, .001f)) throw new InvalidOperationException("edgeSharpness 0 changed the legacy particle shape");

        var hard = Graph(1, 1, .2); particle = hard.Nodes.Single(n => n.Id == "particles");
        particle.Properties["emissionRate"] = 8; particle.Properties["size"] = .2; particle.Properties["edgeSharpness"] = 1;
        Color[] propertyOne;
        using (var preview = GraphPreview.Create(hard, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; propertyOne = Capture(); }
        if (CountRed(propertyOne) <= CountRed(explicitZero) + 4) throw new InvalidOperationException("edgeSharpness 1 did not increase particle coverage");

        var wired = Graph(1, 1, .2); particle = wired.Nodes.Single(n => n.Id == "particles");
        particle.Properties["emissionRate"] = 8; particle.Properties["size"] = .2; particle.Properties.Remove("edgeSharpness");
        wired.Nodes.Add(Float("edgeSharpness", 1)); Edge(wired, "edgeSharpness", "value", "particles", "edgeSharpness");
        using (var preview = GraphPreview.Create(wired, null)) { subject.GetComponent<Renderer>().sharedMaterial = preview.Material; if (!Same(propertyOne, Capture(), .001f)) throw new InvalidOperationException("Connected edgeSharpness 1 did not match property 1"); }
    }
    static void CheckMirrorAlpha()
    {
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        var oldBackground = camera.backgroundColor;
        try
        {
            foreach (var mode in new[] { 0, 1 })
            {
                var graph = Graph(1, 1, .6);
                var particle = graph.Nodes.Single(n => n.Id == "particles");
                particle.Properties["blendMode"] = mode;
                particle.Properties["size"] = .3;
                using (var preview = GraphPreview.Create(graph, null))
                {
                    subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                    camera.backgroundColor = Color.black;
                    var opaque = Capture();
                    if (CountRed(opaque) < 3 || opaque.Any(c => Mathf.Abs(c.a - 1) > .002f))
                        throw new InvalidOperationException("Particles changed opaque mirror alpha in blend mode " + mode);
                    camera.backgroundColor = Color.clear;
                    var transparent = Capture();
                    if (transparent.Any(c => c.a < -.002f || c.a > 1.002f) || !transparent.Any(c => c.r > .01f && c.a > .001f && c.a < .99f))
                        throw new InvalidOperationException("Particles lost partial coverage in transparent mirror in blend mode " + mode);
                }
            }
        }
        finally { camera.backgroundColor = oldBackground; }
    }
    static void CheckLifetimeCurves()
    {
        camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
        var graph = Graph(1,1,.6); var particle = graph.Nodes.Single(n=>n.Id=="particles");
        Color[] original;
        using(var preview=GraphPreview.Create(graph,null)) { subject.GetComponent<Renderer>().sharedMaterial=preview.Material; original=Capture(); }
        particle.Properties["sizeCurve"]=new JArray(new JArray(0,1),new JArray(1,1));
        particle.Properties["opacityCurve"]=new JArray(new JArray(0,1),new JArray(1,1));
        particle.Properties["colorCurve"]=new JArray(new JArray(0,1,1,1,1),new JArray(1,1,1,1,1));
        using(var preview=GraphPreview.Create(graph,null)) { subject.GetComponent<Renderer>().sharedMaterial=preview.Material; if(!Same(original,Capture(),.001f)) throw new Exception("Identity lifetime curves changed legacy rendering"); }
        foreach(var property in new[]{"sizeCurve","opacityCurve"})
        {
            particle.Properties[property]=new JArray(new JArray(0,0),new JArray(1,0));
            using(var preview=GraphPreview.Create(graph,null)) { subject.GetComponent<Renderer>().sharedMaterial=preview.Material; if(CountRed(Capture())>2) throw new Exception(property+" zero did not hide particles"); }
            particle.Properties[property]=new JArray(new JArray(0,1),new JArray(1,1));
        }
        particle.Properties["opacityCurve"]=new JArray(new JArray(0,0),new JArray(1,1));
        Color[] ramp;
        using(var preview=GraphPreview.Create(graph,null)) { subject.GetComponent<Renderer>().sharedMaterial=preview.Material; ramp=Capture(); }
        var sum=ramp.Sum(c=>(double)c.r); var full=original.Sum(c=>(double)c.r);
        if(sum<=.01 || sum>=full*.99)throw new Exception("Lifetime opacity must interpolate between its endpoints");
        particle.Properties.Remove("opacityCurve");
        graph.Nodes.Add(NodeCatalog.Create("core.particleInfo"));graph.Nodes.Last().Id="info";
        Edge(graph,"info","age","particles","opacity");
        using(var preview=GraphPreview.Create(graph,null)) { subject.GetComponent<Renderer>().sharedMaterial=preview.Material; if(!Same(ramp,Capture(),.002f))throw new Exception("Particle Info age disagrees with lifetime ramp"); }
    }
    static void CheckAudioRange()
    {
        var graph=new ShaderGraph {GraphId="audio-range-render"};
        var audio=NodeCatalog.Create("core.audioLink");audio.Id="audio";audio.Properties["rangeEnabled"]=1;audio.Properties["min"]=.2;audio.Properties["max"]=.8;
        var surface=NodeCatalog.Create("core.unlitSurface");surface.Id="surface";surface.Properties["useAlbedoAlpha"]=0;
        var output=NodeCatalog.Create("core.output");output.Id="output";
        graph.Nodes.Add(audio);graph.Nodes.Add(surface);graph.Nodes.Add(output);
        Edge(graph,"audio","value","surface","albedo");Edge(graph,"surface","surface","output","surface");
        using(var preview=GraphPreview.Create(graph,null))
        {
            subject.GetComponent<Renderer>().sharedMaterial=preview.Material;
            preview.Material.SetFloat("_NXSG_AudioLinkPreview",1);
            foreach(var value in new[]{0f,.5f,1f,2f})
            {
                preview.Material.SetFloat("_NXSG_AudioLinkValue",value);
                var pixel=Capture()[Size/2*Size+Size/2];
                var expected=Mathf.Lerp(.2f,.8f,Mathf.Clamp01(value));
                if(Mathf.Abs(pixel.r-expected)>.015f)throw new Exception("AudioLink range wrong: "+value+" -> "+pixel.r);
            }
        }
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
        var graph = new ShaderGraph { GraphId = "surface-particle-render" }; graph.Nodes.Add(ColorNode("black", new JArray(0, 0, 0, 1))); graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" }); graph.Nodes.Add(ColorNode("red", new JArray(1, 0, 0, 1))); graph.Nodes.Add(Float("time", time)); graph.Nodes.Add(Float("mask", mask)); graph.Nodes.Add(new GraphNode { Id = "particles", Operation = "core.surfaceParticles", Properties = new JObject { ["density"] = density, ["emissionRate"] = .5, ["size"] = .15, ["lifetime"] = 2, ["speed"] = .2, ["gravity"] = 0, ["spread"] = .05 } }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(graph, "black", "value", "base", "albedo"); Edge(graph, "base", "surface", "particles", "base"); Edge(graph, "red", "value", "particles", "albedo"); Edge(graph, "time", "value", "particles", "time"); Edge(graph, "mask", "value", "particles", "mask"); Edge(graph, "particles", "surface", "output", "surface"); return graph;
    }

    static ShaderGraph GraphWithParticleInputs(double time, double density, double emissionRate, double size, double lifetime, double speed, double gravity, double spread)
    {
        var graph = Graph(1, 1, time);
        foreach (var input in new[] { "density", "emissionRate", "size", "lifetime", "speed", "gravity", "spread" }) graph.Nodes.Add(Float("input_" + input, input == "density" ? density : input == "emissionRate" ? emissionRate : input == "size" ? size : input == "lifetime" ? lifetime : input == "speed" ? speed : input == "gravity" ? gravity : spread));
        foreach (var input in new[] { "density", "emissionRate", "size", "lifetime", "speed", "gravity", "spread" }) Edge(graph, "input_" + input, "value", "particles", input);
        return graph;
    }

    static void PublishParticleInputShader(ShaderGraph graph)
    {
        var emitted = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = "NXSG/Smoke/ParticleInputs" });
        if (!emitted.Succeeded) throw new InvalidOperationException("Particle input graph did not emit: " + string.Join("\n", emitted.Diagnostics.Select(d => d.Message)));
        var path = "Assets/SmokeResults/ParticleInputs.shader";
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "SmokeResults"));
        File.WriteAllText(path, emitted.ShaderSource);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
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
