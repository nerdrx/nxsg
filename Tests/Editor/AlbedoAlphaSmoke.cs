using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in hidden graphics-enabled Unity with -executeMethod AlbedoAlphaSmoke.Run.
public static class AlbedoAlphaSmoke
{
    const int Size = 96;
    static Camera camera;
    static RenderTexture target;
    static GameObject subject;
    static GameObject lightObject;
    static AmbientMode oldAmbientMode;
    static Color oldAmbientLight;
    static float oldAmbientIntensity;

    public static void Run()
    {
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required.");

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            oldAmbientMode = RenderSettings.ambientMode;
            oldAmbientLight = RenderSettings.ambientLight;
            oldAmbientIntensity = RenderSettings.ambientIntensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.ambientIntensity = 1f;
            lightObject = new GameObject("NXSG Albedo Alpha Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(25f, -25f, 0f);
            var cameraObject = new GameObject("NXSG Albedo Alpha Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.targetTexture = target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();

            foreach (var operation in new[] { "core.toonSurface", "core.unlitSurface", "core.pbrSurface" })
                Check(operation);

            Debug.Log("NXSG ALBEDO ALPHA SMOKE PASSED");
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
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbientLight;
            RenderSettings.ambientIntensity = oldAmbientIntensity;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
            if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
        }
    }

    static void Check(string operation)
    {
        var texture=TransparentRed();
        try
        {
            foreach(var mode in new[]{-1,1,0,2})
            {
                var graph=Graph(operation,mode==2?0:1);
                if(mode>=0)graph.Nodes.Single(n=>n.Id=="surface").Properties["useAlbedoAlpha"]=mode==1?1:0;
                var emitted=ShaderEmitter.Emit(graph);
                Require(emitted.Succeeded,string.Join("; ",emitted.Diagnostics.Select(d=>d.Message)));
                var shadow=emitted.ShaderSource.IndexOf("Name \"ShadowCaster\"",StringComparison.Ordinal);
                Require(shadow>=0,"Missing shadow pass");
                if(mode==0||mode==2)Require(emitted.ShaderSource.Substring(shadow).Contains("clip(_Color.a*("),"Shadow still uses albedo alpha");
                if(operation!="core.unlitSurface")Require(emitted.ShaderSource.Contains("Name \"ForwardAdd\""),"Missing ForwardAdd");
                using(var preview=GraphPreview.Create(graph,null))
                {
                    var material=preview.Material;material.SetColor("_Color",new Color(1,1,1,.5f));SetTexture(material,texture);
                    subject.GetComponent<Renderer>().sharedMaterial=material;
                    var shown=Capture();
                    if(mode==0)Require(shown.r>.2f&&shown.a>.4f&&shown.a<.6f,operation+" ignore alpha / tint failed: "+shown);
                    else Require(shown.r<.01f,operation+" default/on/zero opacity should clip: "+shown);
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    static ShaderGraph Graph(string operation, double opacity)
    {
        var graph = new ShaderGraph { GraphId = "albedo-alpha-" + operation };
        graph.Resources.Add(new GraphResource { Id = "albedo", Kind = "texture2D", Uri = "builtin://white" });
        graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" });
        graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "albedo" } });
        graph.Nodes.Add(new GraphNode { Id = "surface", Operation = operation, Properties = new JObject { ["opacity"] = opacity } });
        graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
        Edge(graph, "uv", "uv", "texture", "uv");
        Edge(graph, "texture", "color", "surface", "albedo");
        Edge(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static Texture2D TransparentRed()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        texture.SetPixels(Enumerable.Repeat(new Color(1, 0, 0, 0), 4).ToArray());
        texture.Apply();
        return texture;
    }

    static void SetTexture(Material material, Texture2D texture)
    {
        for (var i = 0; i < ShaderUtil.GetPropertyCount(material.shader); i++)
            if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                material.SetTexture(ShaderUtil.GetPropertyName(material.shader, i), texture);
    }

    static Color Capture()
    {
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        image.Apply();
        var result = image.GetPixel(Size / 2, Size / 2);
        UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active = null;
        return result;
    }

    static void Edge(ShaderGraph graph, string from, string port, string to, string input)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = from + "-" + to,
            From = new GraphPortRef { NodeId = from, PortId = port },
            To = new GraphPortRef { NodeId = to, PortId = input }
        });
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
