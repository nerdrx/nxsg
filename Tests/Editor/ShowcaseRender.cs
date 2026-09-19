using System;
using System.IO;
using System.Linq;
using NXSG.Core;
using Newtonsoft.Json.Linq;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod ShowcaseRender.Run.
public static class ShowcaseRender
{
    const int Size = 768;
    static Camera camera;
    static RenderTexture target;
    static GameObject sphere;
    static GameObject accent;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            SetupScene();
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GraphWindow).Assembly);
            var names = new[] { "Showcase Hologram", "Showcase Pearl", "Showcase Warm Fur" };
            foreach (var name in names) RenderGraph(Path.Combine(package.resolvedPath, "Samples~", name + ".nxsg"), name);
            Debug.Log("NXSG SHOWCASE RENDER PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere);
            if (accent != null) UnityEngine.Object.DestroyImmediate(accent);
        }
    }

    static void SetupScene()
    {
        camera = new GameObject("NXSG Showcase Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.006f, 0.008f, 0.025f, 1);
        camera.fieldOfView = 38;
        camera.transform.position = new Vector3(0, 0.1f, -4.2f);
        camera.transform.LookAt(new Vector3(0, 0.05f, 0));
        target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create(); camera.targetTexture = target;
        sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.transform.localScale = Vector3.one * 1.35f; sphere.transform.position = new Vector3(-0.35f, 0.05f, 0);
        accent = GameObject.CreatePrimitive(PrimitiveType.Capsule); accent.transform.localScale = new Vector3(.48f, 1.1f, .48f); accent.transform.position = new Vector3(.95f, -.15f, .15f);
        var key = new GameObject("NXSG Showcase Key").AddComponent<Light>(); key.type = LightType.Directional; key.color = new Color(.55f, .7f, 1f); key.intensity = 1.4f; key.transform.rotation = Quaternion.Euler(35, -30, 0);
        RenderSettings.ambientLight = new Color(.025f, .02f, .06f);
    }

    static void RenderGraph(string path, string name)
    {
        var graph = GraphJson.Parse(File.ReadAllText(path));
        var time = graph.Nodes.FirstOrDefault(node => node.Operation == "core.time");
        if (time != null)
        {
            time.Operation = "core.parameter";
            time.Properties = new JObject { ["parameterId"] = "showcase_clock" };
            graph.Parameters.Add(new GraphParameter { Id = "showcase_clock", Name = "Capture time", Type = GraphValueType.Float,
                Binding = GraphBindingKind.Material, Exposed = true, DefaultValue = new JValue(0) });
        }
        using (var preview = GraphPreview.Create(graph, null))
        {
            if (preview.Material == null || ShaderUtil.ShaderHasError(preview.Material.shader)) throw new InvalidOperationException(name + " shader failed.");
            for (var pass = 0; pass < preview.Material.passCount; pass++) if (!preview.Material.SetPass(pass)) throw new InvalidOperationException(name + " SetPass failed: " + pass);
            sphere.GetComponent<Renderer>().sharedMaterial = preview.Material;
            accent.GetComponent<Renderer>().sharedMaterial = preview.Material;
            Color[] first = null;
            for (var frame = 0; frame < (time == null ? 1 : 24); frame++)
            {
                if (time != null) preview.Material.SetFloat("_NXSG_P_showcase_clock", frame / 12f);
                var pixels = Capture();
                RequireFiniteAndVisible(pixels, name);
                if (frame == 0) first = pixels;
                if (frame == 23 && !pixels.Where((c,i) => Mathf.Abs(c.r-first[i].r) + Mathf.Abs(c.g-first[i].g) + Mathf.Abs(c.b-first[i].b) > .0001f).Any()) throw new Exception(name + " did not animate");
                Save(pixels, name + "-" + frame.ToString("D2") + ".png");
            }
        }
    }

    static Color[] Capture()
    {
        camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply();
        var pixels = image.GetPixels(); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active = null; return pixels;
    }

    static void RequireFiniteAndVisible(Color[] pixels, string name)
    {
        if (pixels.Any(c => float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b))) throw new InvalidOperationException(name + " produced non-finite pixels.");
        var background = new Vector3(.006f, .008f, .025f);
        if (pixels.Count(c => Vector3.Distance(new Vector3(c.r, c.g, c.b), background) > .01f) < 256) throw new InvalidOperationException(name + " produced no visible subject pixels.");
    }

    static void Save(Color[] pixels, string filename)
    {
        var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        image.SetPixels(pixels.Select(c => new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), 1)).ToArray()); image.Apply();
        var directory = Path.Combine("Library", "NXSG", "Showcase"); Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, filename), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
    }
}
