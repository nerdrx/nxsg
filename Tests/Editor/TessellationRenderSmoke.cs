using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod TessellationRenderSmoke.Run.
public static class TessellationRenderSmoke
{
    const int Size = 160;
    static GameObject quad; static Camera camera; static RenderTexture target;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.localScale = Vector3.one * 2.6f;
            camera = new GameObject("NXSG Tessellation Camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true; camera.orthographicSize = 1.7f; camera.transform.position = new Vector3(3.5f, .65f, -1.2f); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            CheckFactorsAndTexture(); CheckDistanceLod(); CheckSmoothing(); CheckSurfacePasses(); CheckWireframe();
            Debug.Log("NXSG TESSELLATION RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject); if (quad != null) UnityEngine.Object.DestroyImmediate(quad); }
    }

    static void CheckFactorsAndTexture()
    {
        Color[] low, high;
        using (var p = GraphPreview.Create(Graph("core.unlitSurface", 1, 1, 2, 15, .5, 0, 0, .5, false), null)) { quad.GetComponent<Renderer>().sharedMaterial = p.Material; low = Capture(); }
        using (var p = GraphPreview.Create(Graph("core.unlitSurface", 16, 16, 2, 15, .5, 0, 0, .5, false), null)) { quad.GetComponent<Renderer>().sharedMaterial = p.Material; high = Capture(); }
        RequireFinite(low); RequireFinite(high); if (CountVisible(low) < 20 || CountVisible(high) < 20) throw new InvalidOperationException("tessellation output is empty"); if (Changed(low, high) < 12) throw new InvalidOperationException("tessellation factor 1/16 did not change rendered silhouette");
        var texture = RadialTexture(); try
        {
            using (var p = GraphPreview.Create(Graph("core.unlitSurface", 16, 16, 2, 15, .5, 0, 0, .5, true), null)) { SetTextures(p.Material, texture); quad.GetComponent<Renderer>().sharedMaterial = p.Material; var textured = Capture(); RequireFinite(textured); if (Changed(high, textured) < 8) throw new InvalidOperationException("height texture did not change tessellated silhouette"); }
            using (var p = GraphPreview.Create(Graph("core.unlitSurface", 16, 16, 2, 15, 0, 0, 0, .5, true), null)) { SetTextures(p.Material, texture); quad.GetComponent<Renderer>().sharedMaterial = p.Material; if (Changed(low, Capture()) > 8) throw new InvalidOperationException("zero tessellation strength still displaced geometry"); }
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        SaveEvidence(low, high);
    }

    static void CheckDistanceLod()
    {
        camera.transform.position = new Vector3(3.5f, .65f, -1.2f); camera.transform.LookAt(Vector3.zero); Color[] near, far;
        using (var p = GraphPreview.Create(Graph("core.unlitSurface", 16, 1, 2, 6, .5, 0, 0, .5, false), null)) { quad.GetComponent<Renderer>().sharedMaterial = p.Material; near = Capture(); }
        camera.transform.position = new Vector3(14f, 2.6f, -4.8f); camera.transform.LookAt(Vector3.zero);
        using (var p = GraphPreview.Create(Graph("core.unlitSurface", 16, 1, 2, 6, .5, 0, 0, .5, false), null)) { quad.GetComponent<Renderer>().sharedMaterial = p.Material; far = Capture(); }
        using (var p = GraphPreview.Create(Graph("core.unlitSurface", 1, 1, 2, 6, .5, 0, 0, .5, false), null)) { quad.GetComponent<Renderer>().sharedMaterial = p.Material; if (Changed(far,Capture()) > 8) throw new InvalidOperationException("Far LOD did not reduce to minimum detail"); }
        if (Changed(near, far) < 8) throw new InvalidOperationException("distance LOD did not change rendered output");
        camera.transform.position = new Vector3(3.5f, .65f, -1.2f); camera.transform.LookAt(Vector3.zero);
    }

    static void CheckSmoothing()
    {
        var filter=quad.GetComponent<MeshFilter>(); var original=filter.sharedMesh;
        var mesh=UnityEngine.Object.Instantiate(original);
        mesh.normals=mesh.vertices.Select(v=>new Vector3(v.x,v.y,-1).normalized).ToArray();
        filter.sharedMesh=mesh;
        try
        {
            Color[] flat, smooth;
            using(var p=GraphPreview.Create(Graph("core.unlitSurface",16,16,2,15,0,0,0,.5,false),null)) { quad.GetComponent<Renderer>().sharedMaterial=p.Material; flat=Capture(); }
            using(var p=GraphPreview.Create(Graph("core.unlitSurface",16,16,2,15,0,0,1,.5,false),null)) { quad.GetComponent<Renderer>().sharedMaterial=p.Material; smooth=Capture(); }
            RequireFinite(smooth);
            if(Changed(flat,smooth)<8) throw new InvalidOperationException("Phong smoothing did not change curved-normal mesh silhouette");
        }
        finally { filter.sharedMesh=original; UnityEngine.Object.DestroyImmediate(mesh); }
    }

    static void CheckSurfacePasses()
    {
        foreach (var operation in new[] { "core.unlitSurface", "core.toonSurface", "core.pbrSurface", "core.shell" })
            using (var p = GraphPreview.Create(Graph(operation, 63, 1, 2, 15, .5, .5, 0, .5, false), null))
            {
                if (p.Material.shader == null || !p.Material.shader.isSupported || ShaderUtil.ShaderHasError(p.Material.shader)) throw new InvalidOperationException(operation + " shader error");
                for (var pass = 0; pass < p.Material.passCount; pass++) if (!p.Material.SetPass(pass)) throw new InvalidOperationException(operation + " pass failed: " + pass);
                p.Material.enableInstancing=true; p.Material.EnableKeyword("INSTANCING_ON");
                for (var pass = 0; pass < p.Material.passCount; pass++) if (!p.Material.SetPass(pass)) throw new InvalidOperationException(operation + " instanced pass failed: " + pass);
            }
    }

    static void CheckWireframe()
    {
        var graph=Graph("core.unlitSurface",8,8,2,15,.1,0,0,.5,false);
        var wire=NodeCatalog.Create("core.wireframe"); wire.Id="wire"; graph.Nodes.Add(wire);
        Edge(graph,"wire","value","base","opacity");
        using(var p=GraphPreview.Create(graph,null))
        {
            for(var pass=0;pass<p.Material.passCount;pass++) if(!p.Material.SetPass(pass)) throw new InvalidOperationException("Tessellation + wireframe pass failed");
            quad.GetComponent<Renderer>().sharedMaterial=p.Material;
            var pixels=Capture(); RequireFinite(pixels);
            if(CountVisible(pixels)<8)throw new InvalidOperationException("Tessellation wireframe invisible");
        }
        graph.Nodes.Single(n=>n.Id=="base").Operation="core.pbrSurface";
        using(var p=GraphPreview.Create(graph,null))
            if(p.Material.FindPass("ForwardAdd")<0) throw new InvalidOperationException("Lit tessellation wireframe missing additive pass");
    }

    static ShaderGraph Graph(string operation, int factor, int minFactor, double nearDistance, double farDistance, double strength, double reference, double smoothing, double height, bool textureHeight)
    {
        var g = new ShaderGraph { GraphId = "tessellation-render" }; g.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" });
        var mask = NodeCatalog.Create("core.circleMask"); mask.Id = "mask"; mask.Properties["radius"] = .39; mask.Properties["softness"] = .015; g.Nodes.Add(mask); Edge(g, "uv", "uv", "mask", "uv");
        GraphNode heightNode;
        if (textureHeight) { g.Resources.Add(new GraphResource { Id = "heightTex", Kind = "texture2D", Uri = "builtin://white" }); var tex = NodeCatalog.Create("core.texture2D"); tex.Id = "texture"; tex.Properties["resourceId"] = "heightTex"; var split = NodeCatalog.Create("core.splitColor"); split.Id = "split"; g.Nodes.Add(tex); g.Nodes.Add(split); Edge(g, "uv", "uv", "texture", "uv"); Edge(g, "texture", "color", "split", "color"); heightNode = split; }
        else heightNode = mask;
        GraphNode baseSurface; GraphNode baseOutput;
        g.Nodes.Add(ColorNode("albedo", new JArray(.08, .25, .8, 1)));
        if (operation == "core.shell")
        {
            var inner = NodeCatalog.Create("core.unlitSurface"); inner.Id = "inner"; g.Nodes.Add(inner); Edge(g, "albedo", "value", "inner", "albedo");
            baseSurface = NodeCatalog.Create("core.shell"); baseSurface.Id = "base"; g.Nodes.Add(baseSurface); Edge(g, "inner", "surface", "base", "base"); Edge(g, "inner", "surface", "base", "layer"); baseOutput = baseSurface;
        }
        else { baseSurface = NodeCatalog.Create(operation); baseSurface.Id = "base"; g.Nodes.Add(baseSurface); Edge(g, "albedo", "value", "base", "albedo"); baseOutput = baseSurface; }
        var tess = NodeCatalog.Create("core.tessellation"); tess.Id = "tess"; tess.Properties["factor"] = factor; tess.Properties["minFactor"] = minFactor; tess.Properties["nearDistance"] = nearDistance; tess.Properties["farDistance"] = farDistance; tess.Properties["strength"] = strength; tess.Properties["reference"] = reference; tess.Properties["smoothing"] = smoothing; tess.Properties["height"] = height; g.Nodes.Add(tess); Edge(g, baseOutput.Id, "surface", "tess", "base"); Edge(g, heightNode.Id, textureHeight ? "r" : "value", "tess", "height");
        g.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(g, "tess", "surface", "output", "surface"); return g;
    }

    static Texture2D RadialTexture() { var t = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear }; for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++) { var d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f; t.SetPixel(x, y, Color.white * Mathf.Clamp01(1 - d)); } t.Apply(); return t; }
    static void SetTextures(Material m, Texture2D t) { for (var i = 0; i < ShaderUtil.GetPropertyCount(m.shader); i++) if (ShaderUtil.GetPropertyType(m.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) m.SetTexture(ShaderUtil.GetPropertyName(m.shader, i), t); }
    static GraphNode ColorNode(string id, JArray value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = value } }; }
    static void Edge(ShaderGraph g, string from, string fp, string to, string tp) { g.Connections.Add(new GraphConnection { Id = from + fp + to + tp, From = new GraphPortRef { NodeId = from, PortId = fp }, To = new GraphPortRef { NodeId = to, PortId = tp } }); }
    static Color[] Capture() { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels; }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(d => d > .02f); }
    static int CountVisible(Color[] pixels) { return pixels.Count(c => c.r > .02f || c.g > .02f || c.b > .02f); }
    static void RequireFinite(Color[] p) { if (p.Any(c => float.IsNaN(c.r) || float.IsInfinity(c.r) || float.IsNaN(c.g) || float.IsInfinity(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.b))) throw new InvalidOperationException("tessellation produced non-finite pixels"); }
    static void SaveEvidence(Color[] left, Color[] right) { var image = new Texture2D(Size * 2, Size, TextureFormat.RGBA32, false, true); image.SetPixels(0, 0, Size, Size, left); image.SetPixels(Size, 0, Size, Size, right); image.Apply(); Directory.CreateDirectory("Library/NXSG"); File.WriteAllBytes("Library/NXSG/tessellation.png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); }
}
