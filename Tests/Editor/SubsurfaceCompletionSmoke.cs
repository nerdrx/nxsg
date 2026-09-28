using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SubsurfaceCompletionSmoke
{
    const int Size = 160;
    const string Output = "Assets/SmokeResults/SubsurfaceCompletion";
    static Camera camera;
    static RenderTexture target;
    static Renderer receiver;
    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new Exception("Graphics device required");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            Directory.CreateDirectory(Output);
            camera = new GameObject("Scatter camera").AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 1;
            camera.transform.position = new Vector3(0,0,-5); camera.transform.LookAt(Vector3.zero); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.transform.localScale = Vector3.one * 1.6f; receiver = sphere.GetComponent<Renderer>();
            var light = new GameObject("Scatter light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(0,65,0); light.renderMode = LightRenderMode.ForcePixel;
            RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.reflectionIntensity = 0;
            var baseline = Graph(); var legacy = Graph(); foreach (var p in new[] { "spread", "distortion", "shadowResponse" }) legacy.Nodes[0].Properties.Remove(p);
            var plain = Render(baseline, "neutral"); Require(Changed(plain, Render(legacy, "legacy")) == 0, "Neutral subsurface changed pixels");
            var spread = Graph(); spread.Nodes[0].Properties["spread"] = 1.5;
            Require(Changed(plain, Render(spread, "spread")) > 200, "Spread did not change scattering");
            var distorted = Graph(); distorted.Nodes[0].Properties["distortion"] = 1;
            Require(Changed(plain, Render(distorted, "distortion")) > 200, "Distortion did not change scattering");
            foreach (var value in new[] { -1.0, 0.0, 1.0 })
            {
                var endpoint = Graph(); endpoint.Nodes[0].Properties["spread"] = value; endpoint.Nodes[0].Properties["distortion"] = value;
                Render(endpoint, "endpoint-" + value);
            }
            sphere.SetActive(false);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.localScale = Vector3.one * 2; receiver = quad.GetComponent<Renderer>(); receiver.shadowCastingMode = ShadowCastingMode.Off;
            var caster = GameObject.CreatePrimitive(PrimitiveType.Cube); caster.transform.position = new Vector3(0,0,-.7f); caster.transform.localScale = Vector3.one * .3f;
            light.transform.rotation = Quaternion.Euler(0,35,0); light.shadows = LightShadows.Hard; light.shadowBias = .001f; light.shadowNormalBias = .001f;
            QualitySettings.shadows = ShadowQuality.All; QualitySettings.shadowDistance = 30; QualitySettings.shadowResolution = ShadowResolution.High;
            var shadow = Graph(); shadow.Nodes[0].Properties["shadowResponse"] = 1;
            var off = Render(Graph(), "shadow-off"); var on = Render(shadow, "shadow-on");
            var darker = Enumerable.Range(0,on.Length).Where(i => off[i].r-on[i].r > .1f).ToArray();
            Require(darker.Length > 30, "Scene shadow response did not suppress scattering under a caster");
            Require(darker.All(i => on[i].r >= .99f), "Scene shadow response incorrectly darkened the base color");
            light.shadows = LightShadows.None;
            Require(Changed(Render(Graph(), "no-shadow-off"), Render(shadow, "no-shadow-on")) == 0, "Missing scene shadows changed scattering");
            Debug.Log("NXSG SUBSURFACE COMPLETION SMOKE PASSED: neutral pixels, spread, distortion, real received shadows and finite endpoints");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static ShaderGraph Graph()
    {
        var g = new ShaderGraph { GraphId = "subsurface-completion" };
        var scatter = NodeCatalog.Create("core.subsurface"); scatter.Id = "scatter"; scatter.Properties["strength"] = 2; scatter.Properties["tint"] = new JArray(1,1,1,1); g.Nodes.Add(scatter);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; g.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; g.Nodes.Add(output);
        Edge(g, "scatter", "color", "surface", "albedo"); Edge(g, "surface", "surface", "output", "surface"); return g;
    }
    static Color[] Render(ShaderGraph g, string name)
    {
        File.WriteAllText(Output + "/" + name + ".nxsg", GraphJson.Serialize(g));
        var emitted = ShaderEmitter.Emit(g); Require(emitted.Succeeded, string.Join(";", emitted.Diagnostics.Select(d => d.Message)));
        using (var preview = GraphPreview.Create(g, null))
        {
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader), "Shader compile error: " + name);
            receiver.sharedMaterial = preview.Material; camera.Render(); camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0,0,Size,Size),0,0); image.Apply();
            var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null;
            Require(pixels.All(c => !float.IsNaN(c.r) && !float.IsInfinity(c.r) && !float.IsNaN(c.g) && !float.IsInfinity(c.g) && !float.IsNaN(c.b) && !float.IsInfinity(c.b)), "Nonfinite output: " + name);
            var png = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true); png.SetPixels(pixels.Select(c => c / 3).ToArray()); png.Apply(); File.WriteAllBytes(Output + "/" + name + ".png", png.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(png);
            return pixels;
        }
    }
    static int Changed(Color[] a, Color[] b) => Enumerable.Range(0,a.Length).Count(i => Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b) > .001f);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Edge(ShaderGraph g, string from, string output, string to, string input) => g.Connections.Add(new GraphConnection { Id = from + "-" + input, From = new GraphPortRef { NodeId = from, PortId = output }, To = new GraphPortRef { NodeId = to, PortId = input } });
}
