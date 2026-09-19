using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in standalone Unity 2022.3 with -executeMethod LtcgiRenderSmoke.Run.
public static class LtcgiRenderSmoke
{
    const int Size = 96;

    public static void Run()
    {
        GameObject receiver = null, cameraObject = null, controller = null, screen = null;
        Texture2D video = null;
        RenderTexture target = null;
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics device required.");
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc") == null)
                throw new InvalidOperationException("Pinned LTCGI package include not found.");

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;

            receiver = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cameraObject = new GameObject("NXSG LTCGI Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -3.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            camera.targetTexture = target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();

            using (var preview = GraphPreview.Create(Graph(), null))
            {
                receiver.GetComponent<Renderer>().sharedMaterial = preview.Material;
                Shader.SetGlobalFloat("_Udon_LTCGI_GlobalEnable", 0);
                var noController = Render(camera, target);
                RequireFinite(noController, "no controller");
                RequireDark(noController, "no controller");

                var controllerType = FindType("pi.LTCGI.LTCGI_Controller");
                var screenType = FindType("pi.LTCGI.LTCGI_Screen");
                if (controllerType == null || screenType == null)
                    throw new InvalidOperationException("LTCGI controller/screen type missing.");

                video = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                video.SetPixels(Enumerable.Repeat(Color.white, 4).ToArray());
                video.Apply();
                controller = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Packages/at.pimaker.ltcgi/LTCGI Controller.prefab"));
                Set(controller.GetComponent(controllerType), "VideoTexture", video);
                screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
                screen.GetComponent<Renderer>().enabled = false;
                screen.transform.position = new Vector3(0, 0, -1.0f);
                screen.transform.rotation = Quaternion.identity;
                var screenComponent = screen.AddComponent(screenType);
                Set(screenComponent, "Color", Color.white);
                Set(screenComponent, "DoubleSided", true);
                Set(screenComponent, "Diffuse", true);
                Set(screenComponent, "Specular", false);
                Set(screenComponent, "LightmapChannel", 0);
                controller.GetComponent(controllerType).GetType().GetMethods()
                    .Single(m => m.Name == "UpdateMaterials" && m.GetParameters().Length == 0)
                    .Invoke(controller.GetComponent(controllerType), null);

                var lit = Render(camera, target);
                RequireFinite(lit, "controller + screen");
                if (lit.r <= noController.r + .02f && lit.g <= noController.g + .02f && lit.b <= noController.b + .02f)
                    throw new InvalidOperationException("LTCGI controller and screen produced no illumination: " + lit);

                var lamp = new GameObject("Additional pixel light");
                try
                {
                    var light = lamp.AddComponent<Light>(); light.type=LightType.Point; light.renderMode=LightRenderMode.ForcePixel;
                    light.intensity=5; light.range=5; lamp.transform.position=new Vector3(0,0,-2);
                    var extra = Render(camera,target);
                    if (Vector4.Distance(lit,extra)>.02f) throw new InvalidOperationException("LTCGI emission repeated by ForwardAdd: "+lit+" / "+extra);
                }
                finally { UnityEngine.Object.DestroyImmediate(lamp); }
                Debug.Log("NXSG LTCGI lit pixel: "+lit);
                Shader.SetGlobalFloat("_Udon_LTCGI_GlobalEnable", 0);
                var off = Render(camera, target);
                RequireFinite(off, "controller off");
                RequireDark(off, "controller off");

                Shader.SetGlobalFloat("_Udon_LTCGI_GlobalEnable", 1);
                var strengthZero = Graph();
                strengthZero.Nodes.Single(n => n.Id == "ltcgi").Properties["strength"] = 0.0;
                using (var zero = GraphPreview.Create(strengthZero, null))
                {
                    receiver.GetComponent<Renderer>().sharedMaterial = zero.Material;
                    var zeroColor = Render(camera, target);
                    RequireFinite(zeroColor, "strength 0");
                    RequireDark(zeroColor, "strength 0");
                }
            }
            Debug.Log("NXSG LTCGI RENDER SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = null;
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (video != null) UnityEngine.Object.DestroyImmediate(video);
            if (screen != null) UnityEngine.Object.DestroyImmediate(screen);
            if (controller != null) UnityEngine.Object.DestroyImmediate(controller);
            if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (receiver != null) UnityEngine.Object.DestroyImmediate(receiver);
        }
    }

    static ShaderGraph Graph()
    {
        var graph = new ShaderGraph { GraphId = "ltcgi-render-smoke" };
        graph.Nodes.Add(new GraphNode { Id = "albedo", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(0, 0, 0, 1) } });
        graph.Nodes.Add(NodeCatalog.Create("core.ltcgi")); graph.Nodes[1].Id = "ltcgi";
        graph.Nodes.Add(NodeCatalog.Create("core.toonSurface")); graph.Nodes[2].Id = "surface";
        graph.Nodes.Add(NodeCatalog.Create("core.output")); graph.Nodes[3].Id = "output";
        Connect(graph, "albedo", "value", "surface", "albedo", "albedo");
        Connect(graph, "ltcgi", "color", "surface", "emission", "lighting");
        Connect(graph, "surface", "surface", "output", "surface", "output");
        return graph;
    }

    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }

    static Type FindType(string fullName) { return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null); }
    static void Set(object target, string name, object value) { target.GetType().GetField(name).SetValue(target, value); }
    static Color Render(Camera camera, RenderTexture target)
    {
        camera.Render(); RenderTexture.active = target;
        var capture = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        capture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); capture.Apply();
        var color = capture.GetPixel(Size / 2, Size / 2); UnityEngine.Object.DestroyImmediate(capture); return color;
    }
    static void RequireFinite(Color c, string name) { if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b)) throw new InvalidOperationException(name + " produced non-finite color: " + c); }
    static void RequireDark(Color c, string name) { if (c.r > .02f || c.g > .02f || c.b > .02f) throw new InvalidOperationException(name + " was not dark: " + c); }
}
