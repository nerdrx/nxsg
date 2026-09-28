using System;
using System.IO;
using System.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ToonCompletionSmoke
{
    const int Size = 192;
    const string OutputDirectory = "Assets/SmokeResults/ToonCompletion";

    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            Directory.CreateDirectory(OutputDirectory);
            var camera = new GameObject("Toon completion camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -5);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true;
            camera.orthographicSize = 2;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = 30;
            QualitySettings.shadowResolution = ShadowResolution.High;

            var receiver = GameObject.CreatePrimitive(PrimitiveType.Quad);
            receiver.transform.localScale = Vector3.one * 4;
            var receiverRenderer = receiver.GetComponent<Renderer>();
            receiverRenderer.shadowCastingMode = ShadowCastingMode.Off;
            receiverRenderer.receiveShadows = true;
            var caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            caster.transform.position = new Vector3(0, 0, -.7f);
            caster.transform.localScale = Vector3.one * .4f;
            caster.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            var light = new GameObject("Toon completion key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(0, 35, 0);
            light.shadows = LightShadows.Hard;
            light.shadowBias = .001f;
            light.shadowNormalBias = .001f;
            light.renderMode = LightRenderMode.ForcePixel;

            var legacy = Graph(false);
            var explicitDefaults = Graph(true);
            foreach (var property in new[] { "layerReceiveShadow", "layerReceiveShadow2", "layerReceiveShadow3", "rimColor", "rimStrength", "rimWidth", "rimSoftness", "rimLightAlignment" })
                legacy.Nodes.Single(n => n.Id == "surface").Properties.Remove(property);
            Color[] legacyPixels, defaultPixels;
            using (var preview = GraphPreview.Create(legacy, null)) { receiverRenderer.sharedMaterial = preview.Material; legacyPixels = Capture(camera, target); }
            using (var preview = GraphPreview.Create(explicitDefaults, null)) { receiverRenderer.sharedMaterial = preview.Material; defaultPixels = Capture(camera, target); }
            Require(MaxDifference(legacyPixels, defaultPixels) <= .00001f, "New defaults changed legacy toon pixels");
            Save(legacyPixels, "legacy-neutral.png");

            Color[] layerOff, layerOn;
            using (var preview = GraphPreview.Create(Graph(true, 0), null)) { receiverRenderer.sharedMaterial = preview.Material; layerOff = Capture(camera, target); }
            using (var preview = GraphPreview.Create(Graph(true, 1), null)) { receiverRenderer.sharedMaterial = preview.Material; layerOn = Capture(camera, target); }
            var layerDelta = Enumerable.Range(0, layerOff.Length).Count(i => layerOff[i].r - layerOn[i].r > .08f);
            Require(layerDelta > 30, "Layer receive-shadow control did not change cast-shadow pixels");
            Save(layerOff, "layer2-receive-off.png"); Save(layerOn, "layer2-receive-on.png");

            var dynamicZero = DynamicLayerGraph(0);
            var dynamicOne = DynamicLayerGraph(1);
            var authoredZero = Layer1Graph(0);
            var authoredOne = Layer1Graph(1);
            Color[] dynamicZeroPixels, dynamicOnePixels, authoredZeroPixels, authoredOnePixels;
            using (var preview = GraphPreview.Create(dynamicZero, null)) { receiverRenderer.sharedMaterial = preview.Material; dynamicZeroPixels = Capture(camera, target); }
            using (var preview = GraphPreview.Create(dynamicOne, null)) { receiverRenderer.sharedMaterial = preview.Material; dynamicOnePixels = Capture(camera, target); }
            using (var preview = GraphPreview.Create(authoredZero, null)) { receiverRenderer.sharedMaterial = preview.Material; authoredZeroPixels = Capture(camera, target); }
            using (var preview = GraphPreview.Create(authoredOne, null)) { receiverRenderer.sharedMaterial = preview.Material; authoredOnePixels = Capture(camera, target); }
            Require(MaxDifference(dynamicZeroPixels, authoredZeroPixels) <= .00001f && MaxDifference(dynamicOnePixels, authoredOnePixels) <= .00001f, "Dynamic layer 1 values 0 and 1 differ from authored values");
            Require(Enumerable.Range(0, dynamicZeroPixels.Length).Count(i => dynamicZeroPixels[i].r - dynamicOnePixels[i].r > .08f) > 30, "Dynamic layer 1 values did not change cast-shadow pixels");

            caster.SetActive(false);
            light.shadows = LightShadows.None;
            light.intensity = 2;
            light.type = LightType.Point;
            light.range = 8;
            light.transform.position = new Vector3(0, 0, -1);
            Color[] near, far;
            using (var preview = GraphPreview.Create(Graph(true, 0), null))
            {
                receiverRenderer.sharedMaterial = preview.Material;
                near = Capture(camera, target);
                light.transform.position = new Vector3(0, 0, -6);
                far = Capture(camera, target);
            }
            var center = Size * (Size / 2) + Size / 2;
            Require(near[center].r > far[center].r + .04f, "Per-layer shadow mode removed point-light distance falloff");
            Save(near, "point-near.png"); Save(far, "point-far.png");

            light.type = LightType.Directional;
            light.intensity = 1;
            light.cookieSize = 4;
            light.cookie = MakeCookie();
            light.transform.rotation = Quaternion.Euler(0, 0, 0);
            Color[] cookiePixels, noCookiePixels;
            using (var preview = GraphPreview.Create(Graph(true, 0), null)) { receiverRenderer.sharedMaterial = preview.Material; cookiePixels = Capture(camera, target); }
            light.cookie = null;
            using (var preview = GraphPreview.Create(Graph(true, 0), null)) { receiverRenderer.sharedMaterial = preview.Material; noCookiePixels = Capture(camera, target); }
            var cookieDifferences = Enumerable.Range(0, cookiePixels.Length).Where(i => i % Size > 20 && i % Size < Size - 20 && i / Size > 20 && i / Size < Size - 20).Count(i => Mathf.Abs(cookiePixels[i].r - noCookiePixels[i].r) > .08f);
            Require(cookieDifferences > 30, "Per-layer shadow mode removed directional-light cookie response");
            Save(cookiePixels, "directional-cookie.png"); Save(noCookiePixels, "directional-no-cookie.png");

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = Vector3.zero;
            sphere.transform.localScale = Vector3.one * 1.5f;
            sphere.GetComponent<Renderer>().receiveShadows = false;
            sphere.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            receiver.SetActive(false);
            light.cookie = null;
            light.transform.rotation = Quaternion.Euler(0, 45, 0);
            var rimGraph = Graph(true, 0);
            var rimSurface = rimGraph.Nodes.Single(n => n.Id == "surface");
            rimSurface.Properties["shadowStrength"] = 0.0;
            rimSurface.Properties["shadowStrength2"] = 0.0;
            rimSurface.Properties["shadowStrength3"] = 0.0;
            rimSurface.Properties["rimColor"] = new Newtonsoft.Json.Linq.JArray(1, .05, .05, 1);
            rimSurface.Properties["rimStrength"] = 1.0;
            rimSurface.Properties["rimWidth"] = .4;
            rimSurface.Properties["rimSoftness"] = .05;
            rimSurface.Properties["rimLightAlignment"] = 1.0;
            Color[] rimA, rimB;
            using (var preview = GraphPreview.Create(rimGraph, null))
            {
                sphere.GetComponent<Renderer>().sharedMaterial = preview.Material;
                light.transform.rotation = Quaternion.Euler(0, -45, 0); rimA = Capture(camera, target);
                light.transform.rotation = Quaternion.Euler(0, 45, 0); rimB = Capture(camera, target);
            }
            var rimChanged = Enumerable.Range(0, rimA.Length).Count(i => Mathf.Max(Mathf.Abs(rimA[i].r-rimB[i].r),Mathf.Abs(rimA[i].g-rimB[i].g),Mathf.Abs(rimA[i].b-rimB[i].b)) > .05f);
            Require(rimChanged > 20, "Integrated rim tint did not respond to main-light direction");
            Save(rimA, "rim-light-left.png"); Save(rimB, "rim-light-right.png");

            var cookieGraph = Graph(true, 0);
            File.WriteAllText(OutputDirectory + "/legacy-neutral.nxsg", GraphJson.Serialize(legacy));
            File.WriteAllText(OutputDirectory + "/layer2-receive-off.nxsg", GraphJson.Serialize(Graph(true, 0)));
            File.WriteAllText(OutputDirectory + "/layer2-receive-on.nxsg", GraphJson.Serialize(Graph(true, 1)));
            File.WriteAllText(OutputDirectory + "/dynamic-layer1-zero.nxsg", GraphJson.Serialize(dynamicZero));
            File.WriteAllText(OutputDirectory + "/dynamic-layer1-one.nxsg", GraphJson.Serialize(dynamicOne));
            File.WriteAllText(OutputDirectory + "/authored-layer1-zero.nxsg", GraphJson.Serialize(authoredZero));
            File.WriteAllText(OutputDirectory + "/authored-layer1-one.nxsg", GraphJson.Serialize(authoredOne));
            File.WriteAllText(OutputDirectory + "/point-cookie-preservation.nxsg", GraphJson.Serialize(cookieGraph));
            File.WriteAllText(OutputDirectory + "/rim-light-aware.nxsg", GraphJson.Serialize(rimGraph));
            File.WriteAllText(OutputDirectory + "/rim-zero.nxsg", GraphJson.Serialize(Graph(true, 0)));
            Debug.Log("NXSG TOON COMPLETION SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static ShaderGraph Graph(bool includeDefaults, int layerReceiveShadow2 = 1)
    {
        var graph = new ShaderGraph { GraphId = "toon-completion" };
        var surface = NodeCatalog.Create("core.toonSurface");
        surface.Id = "surface";
        surface.Properties["lightingMode"] = 3;
        surface.Properties["shadowLayers"] = 3;
        surface.Properties["shadowStrength"] = 0.0;
        surface.Properties["shadowStrength2"] = 1.0;
        surface.Properties["shadowStrength3"] = 0.0;
        surface.Properties["threshold2"] = .98;
        surface.Properties["softness2"] = .01;
        surface.Properties["shadeColor2"] = new Newtonsoft.Json.Linq.JArray(.4, .1, .8, 1);
        surface.Properties["layerReceiveShadow2"] = layerReceiveShadow2;
        if (!includeDefaults)
            foreach (var property in new[] { "layerReceiveShadow", "layerReceiveShadow2", "layerReceiveShadow3", "rimColor", "rimStrength", "rimWidth", "rimSoftness", "rimLightAlignment" }) surface.Properties.Remove(property);
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        graph.Nodes.Add(surface); graph.Nodes.Add(output);
        graph.Connections.Add(new GraphConnection { Id = "out", From = new GraphPortRef { NodeId = "surface", PortId = "surface" }, To = new GraphPortRef { NodeId = "output", PortId = "surface" } });
        return graph;
    }

    static ShaderGraph DynamicLayerGraph(double value)
    {
        var graph = Graph(true, 1);
        var surface = graph.Nodes.Single(n => n.Id == "surface");
        surface.Properties["shadowStrength"] = 1.0;
        surface.Properties["shadowStrength2"] = 0.0;
        surface.Properties["threshold"] = .98;
        surface.Properties["softness"] = .01;
        surface.Properties["shadeColor"] = new Newtonsoft.Json.Linq.JArray(.8, .1, .1, 1);
        var control = NodeCatalog.Create("core.value"); control.Id = "dynamicLayer1"; control.Properties["value"] = value; graph.Nodes.Add(control);
        graph.Connections.Add(new GraphConnection { Id = "dynamic-layer1", From = new GraphPortRef { NodeId = control.Id, PortId = "value" }, To = new GraphPortRef { NodeId = "surface", PortId = "layerReceiveShadow" } });
        return graph;
    }

    static ShaderGraph Layer1Graph(int value)
    {
        var graph = Graph(true, 1);
        var surface = graph.Nodes.Single(n => n.Id == "surface");
        surface.Properties["layerReceiveShadow"] = value;
        surface.Properties["shadowStrength"] = 1.0;
        surface.Properties["shadowStrength2"] = 0.0;
        surface.Properties["threshold"] = .98;
        surface.Properties["softness"] = .01;
        surface.Properties["shadeColor"] = new Newtonsoft.Json.Linq.JArray(.8, .1, .1, 1);
        return graph;
    }

    static Texture2D MakeCookie()
    {
        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var pixels = new Color[16 * 16];
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++) pixels[y * 16 + x] = (x / 4 % 2 == 0) ? Color.white : new Color(0, 0, 0, 0);
        texture.SetPixels(pixels); texture.Apply();
        return texture;
    }

    static Color[] Capture(Camera camera, RenderTexture target)
    {
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); texture.Apply();
        var pixels = texture.GetPixels();
        UnityEngine.Object.DestroyImmediate(texture);
        RenderTexture.active = null;
        Require(pixels.All(c => !float.IsNaN(c.r) && !float.IsInfinity(c.r)), "Render produced non-finite pixels");
        return pixels;
    }

    static float MaxDifference(Color[] a, Color[] b) { return Enumerable.Range(0, a.Length).Max(i => Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b))); }
    static void Save(Color[] pixels, string name)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(OutputDirectory + "/" + name, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
