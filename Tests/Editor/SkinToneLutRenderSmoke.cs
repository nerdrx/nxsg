using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;

// Run in a hidden graphics-enabled Unity 2022.3 editor.
public static class SkinToneLutRenderSmoke
{
    static Camera camera;
    static Renderer quad;
    static RenderTexture target;

    public static void Run()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
            camera = new GameObject("Skin LUT camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -2);
            camera.orthographic = true;
            camera.orthographicSize = .5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;

            var sampleRoot = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShaderEmitter).Assembly).resolvedPath, "Samples~");
            var graph = GraphJson.Parse(File.ReadAllText(Path.Combine(sampleRoot, "Skin Tone LUT.nxsg")));
            var neutral = Render(graph, null);
            Require(Close(neutral, new Color(.78f, .48f, .35f, 1), .06f), "Unassigned LUT changed base color: " + neutral);

            graph.Adapter = new JObject { ["textures"] = new JObject { ["skinLut"] = "render-test-asset" } };
            var lut = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            lut.SetPixel(0, 0, Color.red); lut.SetPixel(1, 0, Color.yellow);
            lut.SetPixel(0, 1, Color.blue); lut.SetPixel(1, 1, Color.green);
            lut.Apply();
            try
            {
                var node = graph.Nodes.Single(n => n.Operation == SkinToneLutNodes.Operation);
                node.Properties["pigment"] = 0;
                var warm = Render(graph, lut);
                node.Properties["pigment"] = 1;
                var cool = Render(graph, lut);
                Require(warm.r > .8f && warm.b < .12f, "Bottom LUT row was not sampled: " + warm);
                Require(cool.r < .12f && cool.b > .25f, "Top LUT row was not sampled: " + cool);
                node.Properties["mask"] = 0;
                Require(Close(Render(graph, lut), neutral, .06f), "Mask zero failed to restore base color");
                node.Properties["mask"] = 1;
                node.Properties["strength"] = 0;
                Require(Close(Render(graph, lut), neutral, .06f), "Strength zero failed to restore base color");
                Debug.Log("NXSG SKIN TONE LUT RENDER PASSED: unassigned neutral, pigment rows, mask and strength");
            }
            finally { UnityEngine.Object.DestroyImmediate(lut); }
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (quad != null) UnityEngine.Object.DestroyImmediate(quad.gameObject);
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }

    static Color Render(ShaderGraph graph, Texture2D lut)
    {
        var emitted = ShaderEmitter.Emit(graph);
        Require(emitted.Succeeded, string.Join(";", emitted.Diagnostics.Select(d => d.Message)));
        var shader = ShaderUtil.CreateShaderAsset(emitted.ShaderSource, true);
        var material = new Material(shader);
        if (lut != null) material.SetTexture(emitted.Properties.Single(p => p.ResourceId == "skinLut").Name, lut);
        quad.sharedMaterial = material;
        try
        {
            camera.Render(); camera.Render();
            Require(shader.isSupported && !ShaderUtil.ShaderHasError(shader),
                string.Join(";", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            try { image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); image.Apply(); return image.GetPixel(32, 32); }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
        }
        finally { UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(shader); }
    }

    static bool Close(Color a, Color b, float tolerance) =>
        Mathf.Abs(a.r - b.r) < tolerance && Mathf.Abs(a.g - b.g) < tolerance && Mathf.Abs(a.b - b.b) < tolerance;
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
