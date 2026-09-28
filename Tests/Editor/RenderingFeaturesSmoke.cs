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

// Run in hidden graphics-enabled Unity with -executeMethod RenderingFeaturesSmoke.Run.
public static class RenderingFeaturesSmoke
{
    const int Size = 160;
    static Camera camera;
    static RenderTexture target;
    static GameObject plane, occluder, outlineSphere;
    static Material occluderMaterial;

    public static void Run()
    {
        try
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); plane.transform.localScale = Vector3.one * 2.7f;
            occluder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            occluder.transform.position = new Vector3(0, 0, -.04f); occluder.transform.localScale = Vector3.one * .5f;
            var renderer = occluder.GetComponent<Renderer>(); renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = false;
            occluderMaterial = new Material(Shader.Find("Standard"));occluderMaterial.color=Color.black;renderer.sharedMaterial=occluderMaterial;
            camera = new GameObject("NXSG rendering features camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f,.1f,.14f);
            camera.nearClipPlane = .05f; camera.farClipPlane = 10; camera.fieldOfView = 42;
            camera.transform.position = new Vector3(0, 0, -3); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;

            // Missing depth must be neutral before this camera has rendered a depth texture.
            camera.depthTextureMode = DepthTextureMode.None;
            var aoNoDepth = CaptureVisibility("core.ssao");
            Save(aoNoDepth, "screen-depth-ao-no-depth.png"); Debug.Log("AO no-depth mean="+MeanCenter(aoNoDepth));
            Require(MeanCenter(aoNoDepth) > .95f, "SSAO without camera depth was not neutral");
            var contactNoDepth = CaptureVisibility("core.contactShadow");
            Require(MeanCenter(contactNoDepth) > .95f, "contact shadow without camera depth was not neutral");
            Save(aoNoDepth, "screen-depth-ao-no-depth.png");

            camera.depthTextureMode = DepthTextureMode.Depth;
            renderer.enabled = false;
            plane.transform.rotation = Quaternion.identity;
            var aoFlat = CaptureVisibility("core.ssao");
            Require(MeanCenter(aoFlat) > .95f, "flat receiver self-occluded without a separate depth occluder");
            plane.transform.rotation = Quaternion.Euler(25, 0, 0);
            var aoTilted = CaptureVisibility("core.ssao");
            Require(MeanCenter(aoTilted) > .94f, "tilted planar receiver falsely self-occluded");
            Save(aoTilted, "screen-depth-ao-tilted.png");

            plane.transform.rotation = Quaternion.identity;
            renderer.enabled = true;
            var aoWithOccluder = CaptureVisibility("core.ssao");
            var aoChange = Changed(aoFlat, aoWithOccluder); Save(aoWithOccluder,"screen-depth-ao-contact.png"); Debug.Log("AO means flat="+MeanCenter(aoFlat)+" tilted="+MeanCenter(aoTilted)+" occluded="+MeanCenter(aoWithOccluder));
            Require(aoChange > 8 && MeanCenter(aoWithOccluder) < MeanCenter(aoFlat) - .01f,
                "near opaque depth occluder did not reduce the visible AO mask: changed=" + aoChange);
            Save(aoWithOccluder, "screen-depth-ao-contact.png");

            renderer.enabled = false;
            var contactOpen = CaptureVisibility("core.contactShadow");
            renderer.enabled = true;
            var contactBlocked = CaptureVisibility("core.contactShadow");
            var contactChange = Changed(contactOpen, contactBlocked);Save(contactBlocked,"screen-depth-contact-shadow.png");Debug.Log("Contact means open="+MeanCenter(contactOpen)+" blocked="+MeanCenter(contactBlocked));
            Require(contactChange > 8 && MeanCenter(contactBlocked) < MeanCenter(contactOpen) - .01f,
                "opaque depth occluder did not reduce contact-light visibility: changed=" + contactChange);
            Save(contactBlocked, "screen-depth-contact-shadow.png");

            CheckOutline();
            Debug.Log("NXSG RENDERING FEATURES SMOKE PASSED: AO, contact shadows, outline"); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (occluderMaterial != null) UnityEngine.Object.DestroyImmediate(occluderMaterial);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (plane != null) UnityEngine.Object.DestroyImmediate(plane);
            if (occluder != null) UnityEngine.Object.DestroyImmediate(occluder);
            if (outlineSphere != null) UnityEngine.Object.DestroyImmediate(outlineSphere);
        }
    }

    static Color[] CaptureVisibility(string operation)
    {
        var graph = VisibilityGraph(operation);
        using (var preview = GraphPreview.Create(graph, null))
        {
            Require(preview.Material.shader != null && !ShaderUtil.ShaderHasError(preview.Material.shader), operation + " shader failed import");
            for (var i = 0; i < preview.Material.passCount; i++) if (!preview.Material.SetPass(i)) throw new InvalidOperationException(operation + " pass failed to compile: " + i);
            plane.GetComponent<Renderer>().sharedMaterial = preview.Material;
            return Capture();
        }
    }

    static ShaderGraph VisibilityGraph(string operation)
    {
        var graph = new ShaderGraph { GraphId = "rendering-features-" + operation };
        var effect = NodeCatalog.Create(operation); effect.Id = "effect"; effect.Properties["samples"] = 16;
        if (operation == "core.ssao")
        {
            effect.Properties["radius"] = .45; effect.Properties["strength"] = 1; effect.Properties["thickness"] = .65; effect.Properties["bias"] = .01;
        }
        else
        {
            effect.Properties["distance"] = .8; effect.Properties["strength"] = 1; effect.Properties["thickness"] = .16; effect.Properties["bias"] = .005;
            var direction = new GraphNode { Id = "direction", Operation = "core.constant", Properties = new JObject { ["valueType"] = "vector3", ["value"] = new JArray(1.5, .4, -.5) } };
            graph.Nodes.Add(direction); Edge(graph, "direction", "value", "effect", "direction");
        }
        graph.Nodes.Add(effect);
        var combine = NodeCatalog.Create("core.combineColor"); combine.Id = "combine"; graph.Nodes.Add(combine);
        foreach (var channel in new[] { "r", "g", "b" }) Edge(graph, "effect", "visibility", "combine", channel);
        graph.Nodes.Add(new GraphNode { Id = "black", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0, 0, 0, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" });
        Edge(graph, "black", "value", "surface", "albedo"); Edge(graph, "combine", "color", "surface", "emission");
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "surface", "surface", "output", "surface");
        Require(GraphValidator.Validate(graph).IsValid, operation + " graph failed validation");
        return graph;
    }

    static void CheckOutline()
    {
        plane.GetComponent<Renderer>().enabled = false;
        outlineSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); outlineSphere.transform.localScale = Vector3.one * 1.35f;
        camera.depthTextureMode = DepthTextureMode.None;
        camera.transform.position = new Vector3(0, 0, -3); camera.transform.LookAt(Vector3.zero);
        var baseGraph = LitSphereGraph(false); Color[] basePixels;
        using (var preview = GraphPreview.Create(baseGraph, null)) { outlineSphere.GetComponent<Renderer>().sharedMaterial = preview.Material; basePixels = Capture(); }
        var outlineGraph = LitSphereGraph(true); Color[] outlinePixels;
        var emitted = ShaderEmitter.Emit(outlineGraph);
        Require(emitted.Succeeded && emitted.ShaderSource.Contains("Name \"Outline\""), "outline pass was not emitted");
        using (var preview = GraphPreview.Create(outlineGraph, null))
        {
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader), "outline shader failed import");
            for (var i = 0; i < preview.Material.passCount; i++) if (!preview.Material.SetPass(i)) throw new InvalidOperationException("outline pass failed to compile: " + i);
            outlineSphere.GetComponent<Renderer>().sharedMaterial = preview.Material; outlinePixels = Capture();
        }
        Require(Changed(basePixels, outlinePixels) > 8, "outline hull did not change the sphere silhouette");
        Save(outlinePixels, "outline-sphere.png");
    }

    static ShaderGraph LitSphereGraph(bool outlined)
    {
        var graph = new ShaderGraph { GraphId = outlined ? "outline-render" : "outline-base-render" };
        graph.Nodes.Add(new GraphNode { Id = "white", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(.8, .8, .8, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "base", Operation = "core.unlitSurface" }); Edge(graph, "white", "value", "base", "albedo");
        string root = "base";
        if (outlined)
        {
            var outline = NodeCatalog.Create("core.outline"); outline.Id = "outline"; outline.Properties["width"] = .06; outline.Properties["color"] = new JArray(0, 0, 0, 1);
            graph.Nodes.Add(outline); Edge(graph, "base", "surface", "outline", "base"); root = "outline";
        }
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" }); Edge(graph, root, "surface", "output", "surface");
        Require(GraphValidator.Validate(graph).IsValid, "outline graph failed validation"); return graph;
    }

    static Color[] Capture()
    {
        camera.Render(); RenderTexture.active = target;
        var texture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); texture.Apply();
        var pixels = texture.GetPixels(); UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active = null; return pixels;
    }
    static float MeanCenter(Color[] p)
    {
        var total = 0f; var count = 0;
        for (var y = Size / 4; y < Size * 3 / 4; y++) for (var x = Size / 4; x < Size * 3 / 4; x++) { if((x-Size/2)*(x-Size/2)+(y-Size/2)*(y-Size/2)<19*19)continue; var c = p[y * Size + x]; total += (c.r + c.g + c.b) / 3; count++; }
        return total / count;
    }
    static int Changed(Color[] a, Color[] b) { return a.Zip(b, (x, y) => Vector3.Distance(new Vector3(x.r, x.g, x.b), new Vector3(y.r, y.g, y.b))).Count(d => d > .03f); }
    static void Edge(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } });
    }
    static void Save(Color[] pixels, string name)
    {
        var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true); image.SetPixels(pixels); image.Apply();
        Directory.CreateDirectory("Library/NXSG"); File.WriteAllBytes(Path.Combine("Library/NXSG", name), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
