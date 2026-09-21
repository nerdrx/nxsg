using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Graphics smoke check for camera-relative panosphere UV continuity.
public static class PanosphereSeamSmoke
{
    const int Size = 96;
    static GameObject quadObject, cameraObject;
    static Camera camera;
    static RenderTexture target;
    static Texture2D texture;

    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cameraObject = new GameObject("NXSG Panosphere seam camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 1f;
            target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            texture = PeriodicTexture();

            using (var preview = GraphPreview.Create(Graph(), null))
            {
                SetTextures(preview.Material, texture);
                quadObject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                foreach (var position in new[]
                {
                    new Vector3(0, 0, -3), new Vector3(3, 0, 0),
                    new Vector3(.025f, 0, 3), new Vector3(-3, 0, 0),
                    new Vector3(0, 2.9f, 1), new Vector3(0, -2.9f, 1)
                })
                {
                    var up = Mathf.Abs(Vector3.Dot(position.normalized, Vector3.up)) > .9f ? Vector3.forward : Vector3.up;
                    camera.transform.position = position;
                    camera.transform.LookAt(Vector3.zero, up);
                    quadObject.transform.rotation = Quaternion.LookRotation(-position.normalized, up);
                    CheckCenterStrip(position, Capture());
                }
            }
            // Noninteger tiling exposes spurious chart switching away from the actual seam.
            var fractional = Graph();
            fractional.Nodes.Find(n => n.Id == "scale").Properties["tiling"] = new JArray(2.37, 1.73);
            using (var preview = GraphPreview.Create(fractional, null))
            {
                SetTextures(preview.Material, texture);
                quadObject.GetComponent<Renderer>().sharedMaterial = preview.Material;
                foreach (var position in new[] {new Vector3(-3,.2f,.3f),new Vector3(-3,.4f,.7f),new Vector3(-3,-.3f,1)})
                {
                    camera.transform.position=position;camera.transform.LookAt(Vector3.zero);
                    quadObject.transform.rotation=Quaternion.LookRotation(-position.normalized,Vector3.up);
                    CheckCenterStrip(position,Capture());
                }
            }
            Debug.Log("NXSG PANOSPHERE SEAM SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            if (quadObject != null) UnityEngine.Object.DestroyImmediate(quadObject);
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    static ShaderGraph Graph()
    {
        var graph = new ShaderGraph { GraphId = "panosphere-seam-test" };
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0", Properties = new JObject { ["coordinateSource"] = "panosphere" } });
        graph.Nodes.Add(new GraphNode { Id = "scale", Operation = "core.uvTransform", Properties = new JObject { ["tiling"] = new JArray(2, 1), ["offset"] = new JArray(0, 0) } });
        graph.Resources.Add(new GraphResource { Id = "periodic", Kind = "texture2D", Uri = "builtin://white" });
        graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "periodic" } });
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = "core.unlitSurface" });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "uv", "uv", "scale", "uv");
        Edge(graph, "scale", "uv", "texture", "uv");
        Edge(graph, "texture", "color", "surface", "albedo");
        Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static Texture2D Capture()
    {
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        return image;
    }

    static void CheckCenterStrip(Vector3 cameraPosition, Texture2D image)
    {
        var row = Size / 2;
        for (var x = Size / 2 - 8; x <= Size / 2 + 8; x++)
        {
            var previous = image.GetPixel(x - 1, row);
            var current = image.GetPixel(x, row);
            Require(IsFinite(previous) && IsFinite(current), "panosphere texture produced non-finite color near " + cameraPosition);
            Require(Vector3.Distance(new Vector3(previous.r, previous.g, previous.b), new Vector3(current.r, current.g, current.b)) <= .1f,
                "panosphere texture seam near " + cameraPosition + " at x=" + x + ": " + previous + " / " + current);
        }
        UnityEngine.Object.DestroyImmediate(image);
    }

    static Texture2D PeriodicTexture()
    {
        const int textureSize = 64;
        var result = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, true, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        for (var y = 0; y < textureSize; y++) for (var x = 0; x < textureSize; x++)
        {
            var u = x / (float)textureSize;
            var v = y / (float)textureSize;
            result.SetPixel(x, y, new Color(.5f + .45f * Mathf.Cos(u * Mathf.PI * 2), .5f + .45f * Mathf.Cos(v * Mathf.PI * 2), .5f + .2f * Mathf.Sin((u + v) * Mathf.PI * 2), 1));
        }
        result.Apply(true, false);
        return result;
    }

    static bool IsFinite(Color color)
    {
        return !(float.IsNaN(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b) ||
                 float.IsInfinity(color.r) || float.IsInfinity(color.g) || float.IsInfinity(color.b));
    }

    static void SetTextures(Material material, Texture2D value)
    {
        for (var i = 0; i < ShaderUtil.GetPropertyCount(material.shader); i++)
            if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                material.SetTexture(ShaderUtil.GetPropertyName(material.shader, i), value);
    }

    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = from + "-" + to,
            From = new GraphPortRef { NodeId = from, PortId = fromPort },
            To = new GraphPortRef { NodeId = to, PortId = toPort }
        });
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
