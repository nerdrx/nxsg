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

// Exercises real typed texture assets through preview and persistent build bindings.
public static class RenderingResourcesSmoke
{
    const string Root = "Assets/SmokeResults/RenderingResources";
    static Camera camera;
    static GameObject quad;
    static RenderTexture target;
    public static void Run()
    {
        try
        {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Graphics device required");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            var cyan = new Color(.1f, .7f, .9f, 1); var pink = new Color(.8f, .1f, .5f, 1);
            var cube = new Cubemap(8, TextureFormat.RGBAFloat, false);
            for (var face=0;face<6;face++) cube.SetPixels(Enumerable.Repeat(cyan,64).ToArray(),(CubemapFace)face);
            cube.Apply(); Asset(cube, "Cube.asset");
            var array = new Texture2DArray(8,8,2,TextureFormat.RGBAFloat,false,true);
            array.SetPixels(Enumerable.Repeat(cyan,64).ToArray(),0); array.SetPixels(Enumerable.Repeat(pink,64).ToArray(),1); array.Apply(); Asset(array,"Array.asset");
            camera = new GameObject("Resources camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0,0,-3); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            target=new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.transform.localScale=Vector3.one*2;
            var cubeGraph=Graph("core.cubemap","cubemap",cube);
            Check(cubeGraph,cyan,cube,"cubemap");
            var arrayGraph=Graph("core.textureArray","texture2DArray",array);
            foreach(var slice in new[]{-2f,0f,1f,9f})
            {
                arrayGraph.Nodes[0].Properties["slice"]=slice;
                Check(arrayGraph,slice<1?cyan:pink,array,"array-slice-"+slice);
            }
            arrayGraph.Connections[0].From.PortId="alpha";arrayGraph.Connections[0].To.PortId="displacement";
            using(var preview=GraphPreview.Create(arrayGraph,null)) Require(preview.Material.shader.isSupported,"Vertex array sampling unsupported");
            SaveGraph(arrayGraph,"array-displacement");

            var lv=NodeGraph("core.lightVolumes","color");lv.Nodes[0].Properties["albedo"]=new JArray(.1,.4,.8,1);
            using(var preview=GraphPreview.Create(lv,null))
            {
                var color=Capture(preview.Material);Require(Finite(color),"Light Volumes fallback returned a non-finite color");
            }
            SaveGraph(lv,"light-volumes");
            CheckToonRamp();
            var wrong=Graph("core.cubemap","cubemap",array);bool rejected=false;
            try { using(var preview=GraphPreview.Create(wrong,null)) {} } catch(InvalidOperationException e) { rejected=e.Message.Contains("requires a Cubemap"); }
            Require(rejected,"Wrong texture dimension was not rejected by preview binding");
            Debug.Log("NXSG RENDERING RESOURCES SMOKE PASSED: cube, array bounds, vertex array, build bindings, toon ramp, Light Volumes fallback");EditorApplication.Exit(0);
        }
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
        finally {RenderTexture.active=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject);if(quad!=null)UnityEngine.Object.DestroyImmediate(quad);}
    }
    static void Asset(UnityEngine.Object value,string name)
    {
        var path=Root+"/"+name; if(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)!=null)AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(value,path);AssetDatabase.SaveAssets();
    }
    static void CheckToonRamp()
    {
        RenderSettings.ambientMode=AmbientMode.Custom;RenderSettings.ambientProbe=new SphericalHarmonicsL2();
        var lightObject=new GameObject("Ramp test key");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.color=Color.white;
        try
        {
            var graph=new ShaderGraph{GraphId="toon-texture-ramp-test"};var toon=NodeCatalog.Create("core.toonSurface");toon.Id="toon";toon.Properties["lightingMode"]=2;toon.Properties["resourceId"]="ramp";toon.Properties["shadowStrength"]=1;
            var output=NodeCatalog.Create("core.output");output.Id="output";graph.Nodes.Add(toon);graph.Nodes.Add(output);Edge(graph,"toon","surface","output","surface");
            graph.Resources.Add(new GraphResource{Id="ramp",Name="Ramp",Kind="texture2D",Uri="project://ramp"});
            var colors=new[]{new Color(.8f,.03f,.05f,1),new Color(.02f,.7f,.1f,1)};
            for(var i=0;i<colors.Length;i++)
            {
                var texture=new Texture2D(8,2,TextureFormat.RGBAFloat,false,true);texture.SetPixels(Enumerable.Repeat(colors[i],16).ToArray());texture.Apply();Asset(texture,"Ramp"+i+".asset");
                graph.Adapter=new JObject{["textures"]=new JObject{["ramp"]=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture))}};
                using(var preview=GraphPreview.Create(graph,null))
                {
                    var actual=Capture(preview.Material);Debug.Log("Toon ramp "+i+": "+actual);
                    Require(Distance(actual,colors[i])<.1f,"Toon texture ramp did not control direct light: "+actual+" expected "+colors[i]);
                }
            }
            SaveGraph(graph,"toon-texture-ramp");
        }
        finally{UnityEngine.Object.DestroyImmediate(lightObject);}
    }
    static ShaderGraph Graph(string op,string kind,Texture asset)
    {
        var graph=NodeGraph(op,"color");graph.Nodes[0].Properties["resourceId"]="resource";
        graph.Resources.Add(new GraphResource{Id="resource",Name="Test texture",Kind=kind,Uri="project://textures/test"});
        graph.Adapter=new JObject{["textures"]=new JObject{["resource"]=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset))}};return graph;
    }
    static ShaderGraph NodeGraph(string operation,string port)
    {
        var graph=new ShaderGraph{GraphId="resources-"+operation};var node=NodeCatalog.Create(operation);node.Id="source";
        var surface=NodeCatalog.Create("core.unlitSurface");surface.Id="surface";var output=NodeCatalog.Create("core.output");output.Id="output";
        graph.Nodes.Add(node);graph.Nodes.Add(surface);graph.Nodes.Add(output);
        Edge(graph,"source",port,"surface","albedo");Edge(graph,"surface","surface","output","surface");return graph;
    }
    static void Check(ShaderGraph graph,Color expected,Texture texture,string name)
    {
        using(var preview=GraphPreview.Create(graph,null))
        {
            var result=Capture(preview.Material);File.WriteAllText(Root+"/"+name+"-source.txt",ShaderEmitter.Emit(graph).ShaderSource);Debug.Log(name+" resource depth="+(texture is Texture2DArray ? ((Texture2DArray)texture).depth : 0)+" property="+graph.Nodes[0].Properties.ToString());Require(Distance(result,expected)<.06f,name+" sample wrong: "+result+" expected "+expected);
        }
        var path=Root+"/"+name+".nxsg";File.WriteAllText(path,GraphJson.Serialize(graph));AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var built=GraphBuild.Build(graph,path);var property=ShaderEmitter.Emit(graph).Properties.Single(p=>p.ResourceId=="resource");
        Require(built!=null && built.GetTexture(property.Name)==texture,name+" persistent build lost typed texture binding");
        Require(Distance(Capture(built),expected)<.06f,name+" persistent material sample differs from preview");SaveGraph(graph,name);
    }
    static Color Capture(Material material)
    {
        quad.GetComponent<Renderer>().sharedMaterial=material;camera.Render();RenderTexture.active=target;
        var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();var color=image.GetPixel(32,32);UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=null;return color;
    }
    static void SaveGraph(ShaderGraph graph,string name)
    {
        var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ShaderEmitter).Assembly).resolvedPath;
        var root=Directory.GetParent(Directory.GetParent(package).FullName).FullName;var path=Path.Combine(root,"work/rendering-d3d-graphs");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,name+".nxsg"),GraphJson.Serialize(graph));
    }
    static void Edge(ShaderGraph graph,string from,string port,string to,string input)=>graph.Connections.Add(new GraphConnection{Id=from+"-"+to,From=new GraphPortRef{NodeId=from,PortId=port},To=new GraphPortRef{NodeId=to,PortId=input}});
    static float Distance(Color a,Color b)=>Vector3.Distance(new Vector3(a.r,a.g,a.b),new Vector3(b.r,b.g,b.b));
    static bool Finite(Color c)=>new[]{c.r,c.g,c.b,c.a}.All(x=>!float.IsNaN(x)&&!float.IsInfinity(x));
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
