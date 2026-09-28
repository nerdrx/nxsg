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

// Run in hidden graphics-enabled Unity with -executeMethod DepthBulgeSmoke.Run.
public static class DepthBulgeSmoke
{
    const int Size = 512;
    static Camera camera;
    static GameObject grid, sphere, keyLight;
    static RenderTexture target;
    static Material sphereMaterial;
    static Shader depthOnlyShader;
    static Material depthOnlyMaterial;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            grid = new GameObject("Depth Bulge dense grid", typeof(MeshFilter), typeof(MeshRenderer));
            grid.GetComponent<MeshFilter>().sharedMesh = MakeGrid(3.2f, 80);
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = new Vector3(0, 0, -.12f);
            sphere.transform.localScale = Vector3.one * .62f;
            sphereMaterial = new Material(Shader.Find("Standard")) { color = new Color(.12f, .55f, .75f, 1) };
            sphere.GetComponent<Renderer>().sharedMaterial = sphereMaterial;
            keyLight = new GameObject("Depth Bulge key light");
            keyLight.transform.rotation = Quaternion.Euler(35, -25, 0);
            keyLight.AddComponent<Light>().type = LightType.Directional;
            camera = new GameObject("Depth Bulge perspective camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.015f, .02f, .035f);
            camera.orthographic = false; camera.fieldOfView = 42; camera.nearClipPlane = .05f; camera.farClipPlane = 10;
            camera.transform.position = new Vector3(0, 1.65f, -4.4f); camera.transform.LookAt(new Vector3(0, 0, -.03f));
            camera.depthTextureMode = DepthTextureMode.None;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;

            // Exercise the no-depth path on a fresh camera before any depth texture is rendered.
            var noDepthBaseline = Render(0, .1f, 1, .002f, false);
            var withoutDepth = Render(-.03f, .1f, 1, .002f, false);
            Require(Changed(noDepthBaseline, withoutDepth) <= 2, "missing camera depth did not fall back to zero");

            camera.depthTextureMode = DepthTextureMode.Depth;
            var baseline = Render(0, .1f, 1, .002f, true);
            var contact = Render(-.03f, .1f, 1, .002f, true);
            RequireFinite(contact);
            Require(Changed(baseline, contact) > 12, "nearby depth did not change the grid render");
            Save(baseline, "depth-bulge-baseline.png");
            Save(contact, "depth-bulge-contact-opaque.png");

            var sphereRenderer = sphere.GetComponent<Renderer>();
            sphereRenderer.enabled = false;
            var selfDepthBase = Render(0, .1f, 1, .002f, true);
            var selfDepth = Render(-.03f, .1f, 1, .002f, true);
            Require(Changed(selfDepthBase, selfDepth) <= 2, "self depth passed the nearzero-gap bias");
            sphereRenderer.enabled = true;

            camera.orthographic = true;
            camera.orthographicSize = 1.8f;
            var orthoBase = Render(0, .1f, 1, .002f, true);
            var ortho = Render(-.03f, .1f, 1, .002f, true);
            RequireFinite(ortho);
            Require(Changed(orthoBase, ortho) > 8, "orthographic camera depth did not deform the grid");
            camera.orthographic = false;
            camera.ResetProjectionMatrix();

            var obliqueProjection = camera.projectionMatrix;
            obliqueProjection.m20 += .2f;
            camera.projectionMatrix = obliqueProjection;
            var obliqueBase = Render(0, .1f, 1, .002f, true);
            var oblique = Render(-.03f, .1f, 1, .002f, true);
            Require(Changed(obliqueBase, oblique) <= 2, "oblique clip plane should disable depth bulge");
            camera.ResetProjectionMatrix();

            depthOnlyShader = ShaderUtil.CreateShaderAsset(DepthOnlyOccluderShader, true);
            Require(depthOnlyShader != null && depthOnlyShader.isSupported && !ShaderUtil.ShaderHasError(depthOnlyShader), "depth-only occluder shader failed to compile");
            depthOnlyMaterial = new Material(depthOnlyShader);
            sphereRenderer.sharedMaterial = depthOnlyMaterial;
            var contactDemo = Render(-.03f, .4f, 1, .002f, true, 1, -.12, true);
            Save(contactDemo, "depth-bulge-contact.png");
            var redPixels = CountRed(contactDemo);
            Require(redPixels > 4 && redPixels < Size * Size / 3, "touch output was empty or escaped the local contact region: " + redPixels);
            sphereRenderer.sharedMaterial = sphereMaterial;

            var zeroMaskBase = Render(0, .1f, 1, .002f, true, 0);
            Require(Changed(zeroMaskBase, Render(-.03f, .1f, 1, .002f, true, 0)) <= 2, "mask 0 changed geometry");
            var farBase = Render(0, .1f, 1, .002f, true, 1, -.8);
            var far = Render(-.03f, .1f, 1, .002f, true, 1, -.8);
            Require(Changed(farBase, far) <= 2, "depth beyond distance still affected the grid");
            var positive = Render(.03f, .1f, 1, .002f, true);
            Require(Changed(contact, positive) > 8, "positive and negative height produced the same result");
            Debug.Log("NXSG DEPTH BULGE SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (sphereMaterial != null) UnityEngine.Object.DestroyImmediate(sphereMaterial);
            if (depthOnlyMaterial != null) UnityEngine.Object.DestroyImmediate(depthOnlyMaterial);
            if (depthOnlyShader != null) UnityEngine.Object.DestroyImmediate(depthOnlyShader);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (grid != null) { var mesh = grid.GetComponent<MeshFilter>().sharedMesh; UnityEngine.Object.DestroyImmediate(grid); if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); }
            if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
            if (keyLight != null) UnityEngine.Object.DestroyImmediate(keyLight);
        }
    }

    static Color[] Render(double height, double distance, double falloff, double bias, bool useDepth, double mask = 1, double sphereZ = -.12, bool diagnosticTouch = false)
    {
        sphere.transform.position = new Vector3(0, 0, (float)sphereZ);
        camera.depthTextureMode = useDepth ? DepthTextureMode.Depth : DepthTextureMode.None;
        var graph = MakeGraph(height, distance, falloff, bias, mask, diagnosticTouch);
        var emitted = ShaderEmitter.Emit(graph);
        Require(emitted.Succeeded, string.Join("; ", emitted.Diagnostics.Select(d => d.Message)));
        using (var preview = GraphPreview.Create(graph, null))
        {
            grid.GetComponent<Renderer>().sharedMaterial = preview.Material;
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply();
            var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null;
            return pixels;
        }
    }

    static ShaderGraph MakeGraph(double height, double distance, double falloff, double bias, double mask, bool diagnosticTouch)
    {
        var g = new ShaderGraph { GraphId = "depth-bulge-smoke" };
        var bulge = new GraphNode { Id = "bulge", Operation = "core.depthBulge", Properties = new JObject { ["height"] = height, ["distance"] = distance, ["falloff"] = falloff, ["bias"] = bias, ["mask"] = 1 } };
        g.Nodes.Add(bulge);
        if (mask != 1)
        {
            g.Nodes.Add(new GraphNode { Id = "mask", Operation = "core.constant", Properties = new JObject { ["valueType"] = "float", ["value"] = mask } });
            Edge(g, "mask", "value", "bulge", "mask");
        }
        var surface = new GraphNode { Id = "surface", Operation = diagnosticTouch ? "core.unlitSurface" : "core.pbrSurface", Properties = new JObject { ["opacity"] = 1, ["displacement"] = 0, ["metallic"] = 0, ["roughness"] = .65 } };
        g.Nodes.Add(surface);
        Edge(g, "bulge", "displacement", "surface", "displacement");
        if (diagnosticTouch)
        {
            g.Nodes.Add(new GraphNode { Id = "black", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0, 0, 0, 1) } });
            g.Nodes.Add(new GraphNode { Id = "red", Operation = "core.combineColor" });
            Edge(g, "black", "value", "surface", "albedo");
            Edge(g, "bulge", "touch", "red", "r"); Edge(g, "red", "color", "surface", "emission");
        }
        g.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(g, "surface", "surface", "output", "surface");
        return g;
    }

    static Mesh MakeGrid(float size, int divisions)
    {
        var vertices = new Vector3[(divisions + 1) * (divisions + 1)];
        var indices = new int[divisions * divisions * 6];
        for (var y = 0; y <= divisions; y++) for (var x = 0; x <= divisions; x++) vertices[y * (divisions + 1) + x] = new Vector3((x / (float)divisions - .5f) * size, (y / (float)divisions - .5f) * size, 0);
        var t = 0;
        for (var y = 0; y < divisions; y++) for (var x = 0; x < divisions; x++)
        {
            var a = y * (divisions + 1) + x; var b = a + divisions + 1;
            indices[t++] = a; indices[t++] = b; indices[t++] = a + 1; indices[t++] = a + 1; indices[t++] = b; indices[t++] = b + 1;
        }
        var mesh = new Mesh { name = "Depth Bulge grid" }; mesh.vertices = vertices; mesh.triangles = indices; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    static void Edge(ShaderGraph g, string from, string output, string to, string input)
    {
        g.Connections.Add(new GraphConnection { Id = from + output + to + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(d => d > .02f); }
    static int CountRed(Color[] p) { return p.Count(c => c.r > .05f && c.r > c.g * 1.5f); }
    static void RequireFinite(Color[] p) { Require(!p.Any(c => float.IsNaN(c.r) || float.IsInfinity(c.r) || float.IsNaN(c.g) || float.IsInfinity(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.b)), "depth bulge produced non-finite pixels"); }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Save(Color[] pixels, string name)
    {
        var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true); image.SetPixels(pixels); image.Apply();
        Directory.CreateDirectory("Library/NXSG"); File.WriteAllBytes(Path.Combine("Library/NXSG", name), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
    }

    const string DepthOnlyOccluderShader = @"
Shader ""Hidden/NXSGDepthOnlyOccluder"" {
 SubShader {
  Tags { ""Queue""=""Geometry"" ""RenderType""=""Opaque"" }
  Pass { ZWrite Off ColorMask 0 ZTest LEqual }
  Pass {
   Name ""ShadowCaster""
   Tags { ""LightMode""=""ShadowCaster"" }
   ZWrite On ColorMask 0
   CGPROGRAM
   #pragma target 3.0
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_shadowcaster
   #include ""UnityCG.cginc""
   struct v2f { V2F_SHADOW_CASTER; };
   v2f vert(appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o); return o; }
   float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i); }
   ENDCG
  }
 }
}
";
}
