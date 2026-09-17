using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CoordinateRenderSmoke
{
    const string ShaderPath = "Assets/CoordinateRenderCheck.shader";
    const int Size = 129;

    public static void Run()
    {
        GameObject subject = null, cameraObject = null;
        Material material = null;
        RenderTexture target = null;
        Texture2D encoded = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics device required.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            ShaderUtil.allowAsyncCompilation = false;

            encoded = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < encoded.height; y++) for (var x = 0; x < encoded.width; x++)
                encoded.SetPixel(x, y, new Color(x / 63f, y / 63f, 0, 1));
            encoded.Apply(false, false);
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            cameraObject = new GameObject("NXSG Coordinate Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.orthographic = true; camera.orthographicSize = 1f; camera.targetTexture = target;
            camera.transform.position = new Vector3(0, 2.5f, 0);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            subject = CreateQuad();
            material = new Material(Compile(CreateGraph("core.polarUV"), "NXSG/CoordinatePolar"));
            material.SetTexture("_MainTex", encoded); subject.GetComponent<Renderer>().sharedMaterial = material;

            var polar = Render(camera, target);
            CheckPolar(polar);
            material.shader = Compile(CreateGraph("core.uvRotate"), "NXSG/CoordinateRotate");
            var rotated = Render(camera, target);
            CheckRotate(rotated);

            material.shader = Compile(CreateGraph("core.objectUV"), "NXSG/CoordinateObject");
            var localOrigin = Render(camera, target);
            subject.transform.position = new Vector3(.5f, 0, 0);
            camera.transform.position = new Vector3(.5f, 2.5f, 0);
            var localMoved = Render(camera, target);
            Require(ColorDistance(localOrigin.GetPixel(Size / 2, Size / 2), localMoved.GetPixel(Size / 2, Size / 2)) < .08f, "objectUV changed after object translation");
            material.shader = Compile(CreateGraph("core.worldUV"), "NXSG/CoordinateWorld");
            var worldMoved = Render(camera, target);
            Require(ColorDistance(localMoved.GetPixel(Size / 2, Size / 2), worldMoved.GetPixel(Size / 2, Size / 2)) > .05f, "worldUV did not respond to object translation");

            UnityEngine.Object.DestroyImmediate(polar); UnityEngine.Object.DestroyImmediate(rotated); UnityEngine.Object.DestroyImmediate(localOrigin); UnityEngine.Object.DestroyImmediate(localMoved); UnityEngine.Object.DestroyImmediate(worldMoved);
            Debug.Log("NXSG COORDINATE RENDER CHECK PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            if (encoded != null) UnityEngine.Object.DestroyImmediate(encoded);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            AssetDatabase.DeleteAsset(ShaderPath);
        }
    }

    static GameObject CreateQuad()
    {
        var mesh = new Mesh { name = "NXSG Coordinate Quad" };
        mesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up }; mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        var go = new GameObject("NXSG Coordinate Subject"); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>(); return go;
    }

    static Shader Compile(ShaderGraph graph, string name)
    {
        var result = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = name });
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        File.WriteAllText(Path.Combine(Application.dataPath, "CoordinateRenderCheck.shader"), result.ShaderSource);
        AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Coordinate shader failed import.");
        return shader;
    }

    static Texture2D Render(Camera camera, RenderTexture target)
    {
        camera.Render(); RenderTexture.active = target; var capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); capture.Apply(); return capture;
    }

    static ShaderGraph CreateGraph(string operation)
    {
        var graph = new ShaderGraph { GraphId = "coordinate-render" };
        graph.Nodes.Add(new GraphNode { Id = "source", Operation = operation, Properties = new JObject { ["radialScale"] = 1, ["angleScale"] = 1 } });
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" });
        graph.Nodes.Add(new GraphNode { Id = "tex", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "encoded" } });
        graph.Nodes.Add(new GraphNode { Id = "black", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0, 0, 0, 1) } });
        graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" }); graph.Nodes.Add(new GraphNode { Id = "out", Operation = "core.output" });
        graph.Resources.Add(new GraphResource { Id = "encoded", Kind = "texture2D", Uri = "builtin://white" });
        if (operation == "core.uvRotate")
        {
            graph.Nodes.Add(new GraphNode { Id = "angle", Operation = "core.value", Properties = new JObject { ["value"] = 90 } });
            Connect(graph, "uv", "uv", "source", "uv", "rotate-uv"); Connect(graph, "angle", "value", "source", "angle", "rotate-angle");
        }
        else if (operation == "core.polarUV") Connect(graph, "uv", "uv", "source", "uv", "polar-uv");
        Connect(graph, "source", "uv", "tex", "uv", "source-texture"); Connect(graph, "tex", "color", "toon", "emission", "texture-emission"); Connect(graph, "black", "value", "toon", "albedo", "black-albedo"); Connect(graph, "toon", "surface", "out", "surface", "toon-output");
        return graph;
    }

    static void CheckPolar(Texture2D image)
    {
        var center = image.GetPixel(Size / 2, Size / 2); var right = image.GetPixel(Size - 8, Size / 2); var top = image.GetPixel(Size / 2, Size - 8);
        Require(IsFinite(center) && IsFinite(right) && IsFinite(top), "polarUV produced non-finite pixels");
        Require(right.r > .65f && right.g > .35f && right.g < .65f, "polarUV right cardinal mismatch");
        Require(top.r > .65f && top.g > .60f, "polarUV top cardinal mismatch");
    }

    static void CheckRotate(Texture2D image)
    {
        var right = image.GetPixel(Size - 8, Size / 2); var top = image.GetPixel(Size / 2, Size - 8);
        Require(right.g > .65f && right.r > .35f && right.r < .65f, "uvRotate 90 degrees mismatch");
        Require(top.r < .35f && top.g > .35f, "uvRotate top mismatch");
    }

    static bool IsFinite(Color c) { return !(float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b)); }
    static float ColorDistance(Color a, Color b) { return Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b)); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id) { graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
}
