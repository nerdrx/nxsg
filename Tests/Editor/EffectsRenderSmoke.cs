using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod EffectsRenderSmoke.Run.
public static class EffectsRenderSmoke
{
    const int Size = 96;
    static readonly List<string> failures = new List<string>();

    public static void Run()
    {
        GameObject sphere = null, cameraObject = null;
        RenderTexture target = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0;
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cameraObject = new GameObject("NXSG Effects Camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f); camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.targetTexture = target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();

            Case("unlit lighting invariant", () => CheckUnlit(camera, sphere, target));
            Case("color ramp stops", () => CheckColorRamp(camera, sphere, target));
            Case("layer mask", () => CheckLayer(camera, sphere, target));
            Case("dissolve masks", () => CheckDissolve(camera, sphere, target));
            Case("flipbook frame UV", () => CheckFlipbook(camera, sphere, target));
            Case("sticker decal mask", () => CheckSticker(camera, sphere, target));
            Case("fresnel center edge", () => CheckFresnel(camera, sphere, target));
            Case("normal map PBR compile", () => CheckNormalMap());
            Case("PBR metallic and roughness", () => CheckPbrResponse(camera,sphere,target));
            Case("AudioLink texture bands", () => CheckAudioTexture(camera,sphere,target));
            Case("shell passes and offset", () => CheckShell(camera, sphere, target));
            Case("audio link preview and fallback", () => CheckAudioLink(camera, sphere, target));
            Case("vertex motion displacement", () => CheckVertexMotion());
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Debug.Log("NXSG EFFECTS RENDER SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally { RenderTexture.active = null; if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); } if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject); if (sphere != null) UnityEngine.Object.DestroyImmediate(sphere); }
    }

    static void Case(string name, Action action) { try { action(); Debug.Log("NXSG effects case passed: " + name); } catch (Exception e) { failures.Add(name + ": " + e.Message); Debug.LogError("NXSG effects case failed: " + name + ": " + e.Message); } }

    static void CheckUnlit(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = Surface("core.unlitSurface"); Add(graph, ColorNode("red", Color.red)); Connect(graph, "red", "value", "surface", "albedo", "red-albedo");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var first = Render(camera, target, new Vector3(0, 0, -3.5f)); RenderSettings.ambientLight = Color.white; RenderSettings.ambientIntensity = 10; var second = Render(camera, target, new Vector3(0, 0, -3.5f)); Near(first, second, .08f, "unlit changed with lighting"); if (first.r < .5f || first.g > .15f) throw new InvalidOperationException("unlit color wrong: " + first); }
    }

    static void CheckColorRamp(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = Surface("core.unlitSurface"); Add(graph, Float("input", .5)); Add(graph, new GraphNode { Id = "ramp", Operation = "core.colorRamp", Properties = new JObject { ["stops"] = new JArray(new JArray(0, 1, 0, 0, 1), new JArray(1, 0, 0, 1, 1)) } }); Connect(graph, "input", "value", "ramp", "value", "input-ramp"); Connect(graph, "ramp", "color", "surface", "albedo", "ramp-albedo");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var color = Render(camera, target, Vector3.zero); if (color.r < .25f || color.b < .25f || color.g > .3f) throw new InvalidOperationException("ramp midpoint wrong: " + color); }
    }

    static void CheckLayer(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = Surface("core.unlitSurface"); Add(graph, ColorNode("base", Color.red)); Add(graph, ColorNode("overlay", Color.blue)); Add(graph, Float("mask", .5)); Add(graph, new GraphNode { Id = "layer", Operation = "core.layer" }); Connect(graph, "base", "value", "layer", "base", "base"); Connect(graph, "overlay", "value", "layer", "overlay", "overlay"); Connect(graph, "mask", "value", "layer", "mask", "mask"); Connect(graph, "layer", "color", "surface", "albedo", "layer-albedo");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var color = Render(camera, target, Vector3.zero); if (color.r < .25f || color.b < .25f || color.g > .3f) throw new InvalidOperationException("layer mask ignored: " + color); }
    }

    static void CheckDissolve(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = Surface("core.unlitSurface"); Add(graph, Float("value", .5)); Add(graph, Float("threshold", .5)); Add(graph, new GraphNode { Id = "dissolve", Operation = "core.dissolve" }); Connect(graph, "value", "value", "dissolve", "value", "value"); Connect(graph, "threshold", "value", "dissolve", "threshold", "threshold"); Connect(graph, "dissolve", "mask", "surface", "albedo", "mask-albedo");
        using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var mask = Render(camera, target, Vector3.zero); if (Mathf.Abs(mask.r - 1) > .05f) throw new InvalidOperationException("dissolve mask unexpected: " + mask); }
        graph.Nodes.Single(n => n.Id == "value").Properties["value"] = .25; using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var below = Render(camera, target, Vector3.zero); if (below.r > .1f) throw new InvalidOperationException("dissolve mask did not cut below threshold: " + below); }
        graph.Nodes.Single(n => n.Id == "value").Properties["value"] = .5; graph.Connections.RemoveAll(c => c.Id == "mask-albedo"); Connect(graph, "dissolve", "edge", "surface", "albedo", "edge-albedo"); using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var edge = Render(camera, target, Vector3.zero); if (edge.r < .1f) throw new InvalidOperationException("dissolve edge missing: " + edge); }
    }

    static void CheckFlipbook(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = Surface("core.unlitSurface"); Add(graph, new GraphNode { Id = "uv", Operation = "core.uv0" }); Add(graph, Float("time", 0)); Add(graph, NodeCatalog.Create("core.flipbook")); graph.Nodes.Single(n => n.Operation == "core.flipbook").Id = "flip"; graph.Nodes.Single(n => n.Id == "flip").Properties["rows"] = 2; graph.Nodes.Single(n => n.Id == "flip").Properties["columns"] = 2; Add(graph, new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "atlas" } }); graph.Resources.Add(new GraphResource { Id = "atlas", Kind = "texture2D", Uri = "builtin://white" }); Connect(graph, "uv", "uv", "flip", "uv", "uv"); Connect(graph, "time", "value", "flip", "time", "time"); Connect(graph, "flip", "uv", "texture", "uv", "flip-texture"); Connect(graph, "texture", "color", "surface", "albedo", "flip-albedo");
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false); atlas.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); atlas.Apply(); var first = Color.black; try { foreach (var time in new[] { 0.0, 1.0 }) { graph.Nodes.Single(n => n.Id == "time").Properties["value"] = time; using (var preview = GraphPreview.Create(graph, null)) { SetTextures(preview.Material, atlas); sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var frame = Render(camera, target, Vector3.zero); if (time == 0) first = frame; else if (Vector3.Distance(new Vector3(first.r, first.g, first.b), new Vector3(frame.r, frame.g, frame.b)) < .1f) throw new InvalidOperationException("flipbook frame did not change: " + first + " / " + frame); } } } finally { UnityEngine.Object.DestroyImmediate(atlas); }
    }

    static void CheckSticker(Camera camera, GameObject sphere, RenderTexture target)
    {
        sphere.SetActive(false);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.localScale = Vector3.one * 3;
        var texture = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        texture.SetPixels(new[] { new Color(1,0,0,.5f), new Color(1,0,0,.5f), new Color(1,0,0,.5f), new Color(1,0,0,.5f) }); texture.Apply();
        try
        {
            var graph = Surface("core.unlitSurface"); var sticker = NodeCatalog.Create("core.sticker"); sticker.Id="sticker";
            sticker.Properties["resourceId"]="decal"; sticker.Properties["size"]=new JArray(.4,.4);
            graph.Resources.Add(new GraphResource {Id="decal",Kind="texture2D",Uri="builtin://white"}); graph.Nodes.Add(sticker);
            graph.Nodes.Add(ColorNode("base",Color.blue)); Connect(graph,"base","value","sticker","base","base"); Connect(graph,"sticker","color","surface","albedo","result");
            using(var preview=GraphPreview.Create(graph,null))
            {
                preview.Material.SetTexture("_MainTex",texture); quad.GetComponent<Renderer>().sharedMaterial=preview.Material;
                var image=Capture(camera,target); var center=image.GetPixel(48,48); var outside=image.GetPixel(20,48); UnityEngine.Object.DestroyImmediate(image);
                Near(center,new Color(.5f,0,.5f,1),.3f,"sticker center blend");
                if(outside.b<.8f || outside.r>.1f) throw new InvalidOperationException("sticker outside UV bounds: "+outside);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(quad); sphere.SetActive(true); }
    }

    static void CheckFresnel(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph = Surface("core.unlitSurface"); Add(graph, NodeCatalog.Create("core.fresnel")); graph.Nodes.Single(n => n.Operation == "core.fresnel").Id = "fresnel"; Connect(graph, "fresnel", "value", "surface", "albedo", "fresnel-albedo"); using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; var image = Capture(camera, target); var center = image.GetPixel(Size / 2, Size / 2).r; var maximum = 0f; for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++) maximum = Mathf.Max(maximum, image.GetPixel(x, y).r); if (maximum < center + .05f) throw new InvalidOperationException("fresnel edge does not exceed center: " + center + " / " + maximum); UnityEngine.Object.DestroyImmediate(image); }
    }

    static void CheckPbrResponse(Camera camera,GameObject sphere,RenderTexture target)
    {
        var lightObject=new GameObject("PBR test light"); var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional;light.intensity=2;
        lightObject.transform.rotation=Quaternion.Euler(10,15,0);
        RenderSettings.ambientLight=Color.black;RenderSettings.ambientIntensity=0;
        try
        {
            var graph=Surface("core.pbrSurface");graph.Nodes.Add(ColorNode("color",new Color(.35f,.1f,.04f,1)));Connect(graph,"color","value","surface","albedo","color");
            var surface=graph.Nodes.Single(n=>n.Id=="surface");surface.Properties["metallic"]=0;surface.Properties["roughness"]=.15;
            Color[] a,b,c;
            using(var preview=GraphPreview.Create(graph,null)){sphere.GetComponent<Renderer>().sharedMaterial=preview.Material;var image=Capture(camera,target);a=image.GetPixels();UnityEngine.Object.DestroyImmediate(image);}
            surface.Properties["metallic"]=1;
            using(var preview=GraphPreview.Create(graph,null)){sphere.GetComponent<Renderer>().sharedMaterial=preview.Material;var image=Capture(camera,target);b=image.GetPixels();UnityEngine.Object.DestroyImmediate(image);}
            surface.Properties["roughness"]=.9;
            using(var preview=GraphPreview.Create(graph,null)){sphere.GetComponent<Renderer>().sharedMaterial=preview.Material;var image=Capture(camera,target);c=image.GetPixels();UnityEngine.Object.DestroyImmediate(image);}
            var metal=a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)).Sum();
            var rough=b.Zip(c,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)).Sum();
            if(float.IsNaN(metal)||float.IsNaN(rough)||metal<.1f||rough<.1f)throw new InvalidOperationException("PBR controls did not change lighting: "+metal+"/"+rough);
        }
        finally{UnityEngine.Object.DestroyImmediate(lightObject);}
    }

    static void CheckAudioTexture(Camera camera,GameObject sphere,RenderTexture target)
    {
        var previous=Shader.GetGlobalTexture("_AudioTexture");var previousSize=Shader.GetGlobalVector("_AudioTexture_TexelSize");var audio=new Texture2D(128,64,TextureFormat.RGBAFloat,false,true);
        var pixels=new Color[128*64];for(var y=0;y<4;y++){pixels[y*128]=new Color(.1f*(y+1),0,0,1);for(var x=0;x<16;x++)pixels[(28+y)*128+x]=new Color(.01f*x+.1f*y,0,0,1);}audio.SetPixels(pixels);audio.Apply();
        try
        {
            Shader.SetGlobalTexture("_AudioTexture",audio);
            var graph=Surface("core.unlitSurface");var node=NodeCatalog.Create("core.audioLink");node.Id="audio";node.Properties["band"]=2;node.Properties["smoothing"]=0;graph.Nodes.Add(node);Connect(graph,"audio","value","surface","albedo","audio");
            using(var preview=GraphPreview.Create(graph,null)){ Shader.SetGlobalTexture("_AudioTexture",audio); sphere.GetComponent<Renderer>().sharedMaterial=preview.Material;var color=Render(camera,target,Vector3.zero);if(Mathf.Abs(color.r-.3f)>.03f)throw new InvalidOperationException("Audio raw band wrong: "+color);}
            node.Properties["smoothing"]=1;
            using(var preview=GraphPreview.Create(graph,null)){ Shader.SetGlobalTexture("_AudioTexture",audio); sphere.GetComponent<Renderer>().sharedMaterial=preview.Material;var color=Render(camera,target,Vector3.zero);if(Mathf.Abs(color.r-.2f)>.03f)throw new InvalidOperationException("Audio filtered band wrong: "+color);}
        }
        finally{Shader.SetGlobalTexture("_AudioTexture",previous);Shader.SetGlobalVector("_AudioTexture_TexelSize",previousSize);UnityEngine.Object.DestroyImmediate(audio);}
    }

    static void CheckNormalMap() { var graph = Surface("core.pbrSurface"); Add(graph, ColorNode("normal", new Color(.5f, .5f, 1, 1))); Add(graph, NodeCatalog.Create("core.normalMap")); graph.Nodes.Single(n => n.Operation == "core.normalMap").Id = "normalMap"; Connect(graph, "normal", "value", "normalMap", "color", "normal-color"); Connect(graph, "normalMap", "normal", "surface", "normal", "normal-surface"); using (var preview = GraphPreview.Create(graph, null)) { if (preview.Material.passCount == 0) throw new InvalidOperationException("PBR normal map has no pass"); } }

    static void CheckShell(Camera camera, GameObject sphere, RenderTexture target)
    {
        var graph=Surface("core.shell");graph.Nodes.Add(new GraphNode {Id="baseSurface",Operation="core.unlitSurface"});
        graph.Nodes.Add(new GraphNode {Id="layerSurface",Operation="core.unlitSurface",Properties=new JObject {["opacity"]=.5}});
        graph.Nodes.Add(ColorNode("black",Color.black));graph.Nodes.Add(ColorNode("red",Color.red));graph.Nodes.Add(Float("offset",0));
        Connect(graph,"black","value","baseSurface","albedo","black");Connect(graph,"red","value","layerSurface","albedo","red");
        Connect(graph,"baseSurface","surface","surface","base","base");Connect(graph,"layerSurface","surface","surface","layer","layer");Connect(graph,"offset","value","surface","offset","offset");
        int small,large;
        using(var preview=GraphPreview.Create(graph,null)){sphere.GetComponent<Renderer>().sharedMaterial=preview.Material;var image=Capture(camera,target);small=image.GetPixels().Count(c=>c.r>.1f);UnityEngine.Object.DestroyImmediate(image);}
        graph.Nodes.Single(n=>n.Id=="offset").Properties["value"]=.2;
        using(var preview=GraphPreview.Create(graph,null))
        {
            if(preview.Material.FindPass("Shell")<0||preview.Material.FindPass("ShadowCaster")<0)throw new InvalidOperationException("Shell/base shadow passes missing");
            sphere.GetComponent<Renderer>().sharedMaterial=preview.Material;var image=Capture(camera,target);large=image.GetPixels().Count(c=>c.r>.1f);var center=image.GetPixel(Size/2,Size/2);UnityEngine.Object.DestroyImmediate(image);
            if(Mathf.Abs(center.r-.5f)>.08f || center.b>.05f)throw new InvalidOperationException("Independent shell transparency: "+center);
        }
        if(large<=small*1.2f)throw new InvalidOperationException("Shell offset did not expand silhouette: "+small+"/"+large);
    }

    static void CheckAudioLink(Camera camera, GameObject sphere, RenderTexture target) { var graph = Surface("core.unlitSurface"); Add(graph, NodeCatalog.Create("core.audioLink")); graph.Nodes.Single(n => n.Operation == "core.audioLink").Id = "audio"; graph.Nodes.Single(n => n.Id == "audio").Properties["fallback"] = .2; Connect(graph, "audio", "value", "surface", "albedo", "audio-albedo"); using (var preview = GraphPreview.Create(graph, null)) { sphere.GetComponent<Renderer>().sharedMaterial = preview.Material; preview.Material.SetFloat("_NXSG_AudioLinkPreview", 1); preview.Material.SetFloat("_NXSG_AudioLinkValue", .7f); var live = Render(camera, target, Vector3.zero); preview.Material.SetFloat("_NXSG_AudioLinkPreview", 0); var fallback = Render(camera, target, Vector3.zero); if (live.r < fallback.r + .2f || fallback.r < .1f) throw new InvalidOperationException("audio preview/fallback mismatch: " + live + " / " + fallback); } }

    static void CheckVertexMotion() { var graph = Surface("core.unlitSurface"); Add(graph, Float("time", 1)); Add(graph, Float("strength", .1)); Add(graph, NodeCatalog.Create("core.vertexMotion")); graph.Nodes.Single(n => n.Operation == "core.vertexMotion").Id = "motion"; Connect(graph, "time", "value", "motion", "time", "time"); Connect(graph, "strength", "value", "motion", "strength", "strength"); Connect(graph, "motion", "value", "surface", "displacement", "displacement"); using (var preview = GraphPreview.Create(graph, null)) { if (preview.Material.passCount == 0) throw new InvalidOperationException("vertex motion shader has no pass"); } }

    static ShaderGraph Surface(string operation, string id = "surface") { var graph = new ShaderGraph { GraphId = "effects-smoke" }; graph.Nodes.Add(new GraphNode { Id = id, Operation = operation }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Connect(graph, id, "surface", "output", "surface", "output"); return graph; }
    static void Add(ShaderGraph graph, GraphNode node) { graph.Nodes.Add(node); }
    static GraphNode Float(string id, double value) { return new GraphNode { Id = id, Operation = "core.value", Properties = new JObject { ["value"] = value } }; }
    static GraphNode ColorNode(string id, Color value) { return new GraphNode { Id = id, Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(value.r, value.g, value.b, value.a) } }; }
    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id) { graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
    static Color Render(Camera camera, RenderTexture target, Vector3 unused) { camera.Render(); RenderTexture.active = target; var image = Capture(camera, target); var color = image.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(image); return color; }
    static Texture2D Capture(Camera camera, RenderTexture target) { camera.Render(); RenderTexture.active = target; var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); image.Apply(); return image; }
    static Color RenderAt(Camera camera, RenderTexture target, Vector3 position) { var old = camera.transform.position; camera.transform.position = position; var color = Render(camera, target, position); camera.transform.position = old; return color; }
    static void SetTextures(Material material, Texture2D texture) { material.SetTexture("_MainTex", texture); for (var i = 0; i < ShaderUtil.GetPropertyCount(material.shader); i++) if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) material.SetTexture(ShaderUtil.GetPropertyName(material.shader, i), texture); }
    static void Near(Color a, Color b, float tolerance, string message) { if (Vector4.Distance(a, b) > tolerance) throw new InvalidOperationException(message + ": " + a + " / " + b); }
}
