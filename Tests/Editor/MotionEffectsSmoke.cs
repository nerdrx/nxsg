using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod MotionEffectsSmoke.Run.
public static class MotionEffectsSmoke
{
    const int Size = 96;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var cameraObject = new GameObject("NXSG Motion Camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.transform.position = new Vector3(0, 0, -3.5f); camera.transform.rotation = Quaternion.identity;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            try
            {
                CheckResponse(camera, target, sphere);
                CheckStretch(camera, target, sphere);
                CheckSwayCompile(sphere);
                Debug.Log("NXSG MOTION EFFECTS SMOKE PASSED"); EditorApplication.Exit(0);
            }
            finally { target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(sphere); }
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void CheckResponse(Camera camera, RenderTexture target, GameObject subject)
    {
        var graph = MotionChecks.ResponseGraph();
        graph.Nodes.Single(n=>n.Id=="response").Properties["startSpeed"]=0;
        using (var preview = GraphPreview.Create(graph, null))
        {
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
            preview.Material.SetFloat("_NXSG_MotionSpeed", 0); var zero = Pixel(camera, target);
            preview.Material.SetFloat("_NXSG_MotionSpeed", 2); var half = Pixel(camera, target);
            if(Mathf.Abs(half.r-.5f)>.03f) throw new Exception("Motion response midpoint incorrect: "+half);
            preview.Material.SetFloat("_NXSG_MotionSpeed", 4); var full = Pixel(camera, target);
            if (zero.r > .03f || full.r < .97f) throw new InvalidOperationException("motion response did not map 0..4 to 0..1: " + zero + " / " + full);
        }
    }

    static void CheckStretch(Camera camera, RenderTexture target, GameObject subject)
    {
        var graph = MotionChecks.StretchGraph();
        var checker = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) checker.SetPixel(x, y, ((x + y) & 1) == 0 ? Color.black : Color.white);
        checker.Apply();
        try
        {
            using (var preview = GraphPreview.Create(graph, null))
            {
                preview.Material.SetTexture("_MainTex", checker); subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                preview.Material.SetFloat("_NXSG_MotionSpeed", 0); var zero = Capture(camera, target);
                preview.Material.SetFloat("_NXSG_MotionSpeed", 4); var full = Capture(camera, target);
                var difference = zero.Zip(full, (a, b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)).Sum();
                if (difference < 5) throw new InvalidOperationException("motion stretch UV did not change sampled output: " + difference);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(checker); }
    }

    static void CheckSwayCompile(GameObject subject)
    {
        using (var preview = GraphPreview.Create(MotionChecks.SwayGraph(), null))
        {
            if (preview.Material.passCount < 2 || preview.Material.FindPass("ShadowCaster") < 0) throw new InvalidOperationException("motion sway lost vertex or shadow pass");
            for (var pass = 0; pass < preview.Material.passCount; pass++) if (!preview.Material.SetPass(pass)) throw new InvalidOperationException("motion sway pass failed: " + pass);
        }
    }

    static Color Pixel(Camera camera, RenderTexture target) { var pixels = Capture(camera, target); return pixels[Size / 2 * Size + Size / 2]; }
    static Color[] Capture(Camera camera, RenderTexture target)
    {
        camera.Render(); var old = RenderTexture.active; RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        try { image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); return image.GetPixels(); }
        finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(image); }
    }
}
