using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod VolumeRenderSmoke.Run.
public static class VolumeRenderSmoke
{
    const int Size = 128;
    static GameObject subject, cameraObject, occluder;
    static RenderTexture target;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            subject = GameObject.CreatePrimitive(PrimitiveType.Cube); subject.transform.localScale = Vector3.one * 1.4f;
            occluder = GameObject.CreatePrimitive(PrimitiveType.Cube); occluder.transform.position = new Vector3(0, 0, -.9f); occluder.transform.localScale = Vector3.one * .35f;
            occluder.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.black };
            occluder.SetActive(false);
            cameraObject = new GameObject("NXSG Volume Camera");
            var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.orthographic = true; camera.orthographicSize = 2.2f; camera.nearClipPlane = .01f; camera.farClipPlane = 20;
            camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;

            var filled = Render(camera, Graph(0, false));
            var absent = Render(camera, Graph(0, false, 0));
            Require(Lit(filled) > 20, "constant volume produced no visible pixels");
            Require(Lit(absent) < 2, "density 0 still rendered volume");
            var sphere = Render(camera, Graph(0, true));
            Require(Lit(sphere) < Lit(filled) * .8f, "sphere SDF did not change volume coverage");
            Require(Lit(Render(camera, Graph(0, true, 1, 0))) > 20, "SDF union removed volume");
            Require(Lit(Render(camera, Graph(0, true, 1, 1))) < 2, "SDF subtract did not remove identical volumes");
            CheckSdfImports();
            var solid = Graph(0,true); solid.Nodes.Single(n=>n.Id=="volume").Properties["mode"]=1;
            Require(Lit(Render(camera,solid))>10,"solid SDF mode did not render");
            var samples=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(NXSG.Backend.ShaderEmitter).Assembly).resolvedPath;
            var nebula=GraphJson.Parse(File.ReadAllText(Path.Combine(samples,"Samples~/Volume Nebula.nxsg")));
            var early=Render(camera,nebula,0); var late=Render(camera,nebula,4);
            Require(early.Zip(late,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.b-b.b)).Count(v=>v>.01f)>20,"4D volume noise did not animate");

            camera.orthographic = false; camera.transform.position = Vector3.zero; camera.transform.LookAt(Vector3.forward);
            Require(Lit(Render(camera, Graph(0, false))) > 10, "camera inside volume produced no visible pixels");
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -4); camera.transform.LookAt(Vector3.zero);
            occluder.SetActive(false); var unclipped = Lit(Render(camera, Graph(0, false)));
            occluder.SetActive(true); camera.depthTextureMode = DepthTextureMode.Depth;
            var clipped = Lit(Render(camera, Graph(0, false, 1, -1, true)));
            Require(clipped < unclipped, "depthClip did not honor opaque occluder");
            Debug.Log("NXSG VOLUME RENDER SMOKE PASSED: constant volume, density zero, SDF coverage, outside orthographic and inside perspective");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (occluder != null) { var material = occluder.GetComponent<Renderer>().sharedMaterial; UnityEngine.Object.DestroyImmediate(occluder); if (material != null) UnityEngine.Object.DestroyImmediate(material); }
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
        }
    }

    static Color[] Render(Camera camera, ShaderGraph graph, float time=1)
    {
        using (var preview = GraphPreview.Create(graph, null))
        {
            NXSG.Editor.PreviewClock.Apply(preview.Material,time);
            subject.GetComponent<Renderer>().sharedMaterial = preview.Material;
            camera.Render(); RenderTexture.active = target;
            var capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false); capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); capture.Apply();
            var pixels = capture.GetPixels(); UnityEngine.Object.DestroyImmediate(capture); return pixels;
        }
    }

    static float Lit(Color[] pixels) { return pixels.Count(c => c.r + c.g + c.b > .03f); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    static ShaderGraph Graph(double time, bool sdf, double density = 1, int blendMode = -1, bool depthClip = false, string sdfOperation = "core.sdfSphere")
    {
        var graph = new ShaderGraph { GraphId = "volume-render-smoke" };
        graph.Nodes.Add(Node("density", "core.constant", Float(.0 + density)));
        graph.Nodes.Add(Node("color", "core.constant", ColorValue(.4, .2, 1, 1)));
        graph.Nodes.Add(Node("emission", "core.constant", ColorValue(0, 0, 0, 1)));
        graph.Nodes.Add(Node("volume", "core.volumeSurface", new JObject { ["bounds"] = new JArray(.5, .5, .5), ["steps"] = 32, ["maxDistance"] = 4, ["density"] = 1, ["color"] = new JArray(.4, .2, 1, 1), ["emission"] = new JArray(0, 0, 0, 1), ["depthClip"] = depthClip ? 1 : 0 }));
        graph.Nodes.Add(Node("output", "core.output"));
        Connect(graph, "density", "value", "volume", "density"); Connect(graph, "color", "value", "volume", "color"); Connect(graph, "emission", "value", "volume", "emission");
        if (sdf)
        {
            graph.Nodes.Add(Node("ray", "core.rayPosition"));
            var sdfProperties = sdfOperation == "core.sdfBox" ? new JObject { ["size"] = new JArray(.3, .3, .3) } : sdfOperation == "core.sdfTorus" ? new JObject { ["radius"] = .3, ["thickness"] = .08 } : new JObject { ["radius"] = .3 };
            graph.Nodes.Add(Node("sphere", sdfOperation, sdfProperties));
            Connect(graph, "ray", "position", "sphere", "position");
            if (blendMode < 0) Connect(graph, "sphere", "distance", "volume", "distance");
            else { graph.Nodes.Add(Node("sphereB", "core.sdfSphere", new JObject { ["radius"] = .3 })); graph.Nodes.Add(Node("blend", "core.sdfBlend", new JObject { ["smoothing"] = .1, ["mode"] = blendMode })); Connect(graph, "ray", "position", "sphereB", "position"); Connect(graph, "sphere", "distance", "blend", "a"); Connect(graph, "sphereB", "distance", "blend", "b"); Connect(graph, "blend", "distance", "volume", "distance"); }
        }
        Connect(graph, "volume", "surface", "output", "surface");
        return graph;
    }

    static void CheckSdfImports()
    {
        foreach (var operation in new[] { "core.sdfBox", "core.sdfTorus" })
        {
            Require(NodeCatalog.IsKnown(operation), operation + " missing from node catalog");
            using (var preview = GraphPreview.Create(Graph(0, true, 1, -1, false, operation), null)) Require(preview.Material != null, operation + " import preview missing");
        }
    }

    static GraphNode Node(string id, string operation, JObject properties = null) { return new GraphNode { Id = id, Operation = operation, Properties = properties ?? new JObject() }; }
    static JObject Float(double value) { return new JObject { ["valueType"] = "float", ["value"] = value }; }
    static JObject ColorValue(double r, double g, double b, double a) { return new JObject { ["valueType"] = "color", ["value"] = new JArray(r, g, b, a) }; }
    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}
