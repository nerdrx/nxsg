using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class FurShadowRenderSmoke
{
    const int Size=192;
    static Camera camera;static RenderTexture target;static GameObject sphere,blocker;static Light light;
    public static void Run()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowDistance=30;QualitySettings.shadowCascades=0;QualitySettings.shadowResolution=ShadowResolution.VeryHigh;QualitySettings.pixelLightCount=4;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.ambientIntensity=0;RenderSettings.ambientProbe=new SphericalHarmonicsL2();
            sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            light=new GameObject("Fur shadow light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.shadows=LightShadows.Hard;light.shadowBias=.01f;light.shadowNormalBias=0;RenderSettings.sun=light;
            blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=new Vector3(0,0,-1);blocker.transform.localScale=new Vector3(3,3,.1f);blocker.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;
            camera=new GameObject("Fur shadow camera").AddComponent<Camera>();camera.renderingPath=RenderingPath.Forward;camera.orthographic=true;camera.orthographicSize=.75f;camera.transform.position=new Vector3(0,0,-4);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            SceneShadows(false);SceneShadows(true);
            // Exercise the complete multipass material, soft shadows and cascades together.
            QualitySettings.shadowCascades=2;light.shadows=LightShadows.Soft;
            var combinedOff=Render(Graph(0,0,1),false,false);var combinedOn=Render(Graph(1,0,1),false,false);
            Require(Energy(combinedOff)>.0005f && Energy(combinedOn)<Energy(combinedOff)*.65f,"Combined fur soft/cascaded shadow reception failed");
            blocker.SetActive(false);light.shadows=LightShadows.None;light.transform.rotation=Quaternion.Euler(0,60,0);
            SelfShadows(false);SelfShadows(true);
            Debug.Log("NXSG FUR SHADOW RENDER SMOKE PASSED");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void SceneShadows(bool finsOnly)
    {
        var off=Render(Graph(0,0,1),finsOnly);var on=Render(Graph(1,0,1),finsOnly);
        float a=Energy(off),b=Energy(on);Debug.Log("FUR SCENE SHADOW finsOnly="+finsOnly+" off="+a+" on="+b);
        Require(a>.0005f && b<a*.65f,"Scene shadow did not attenuate fur: "+a+" / "+b);
        Save(off,finsOnly?"fins-scene-off":"fur-scene-off");Save(on,finsOnly?"fins-scene-on":"fur-scene-on");
    }
    static void SelfShadows(bool finsOnly)
    {
        var off=Render(Graph(0,0,1),finsOnly);var high=Render(Graph(0,3,2),finsOnly);
        var zero=Render(Graph(0,3,0),finsOnly);
        float a=Energy(off),b=Energy(high);Debug.Log("FUR SELF SHADOW finsOnly="+finsOnly+" off="+a+" high="+b);
        Require(a>.0005f && b<a*.98f,"Self shadows did not darken fur along light rays");
        Require(off.Zip(zero,(x,y)=>Mathf.Abs(x.g-y.g)).Max()<.0001f,"Zero strength changed the unshadowed material");
        // Intermediate quality variants must compile and render finite colors too.
        Render(Graph(0,1,1),finsOnly);Render(Graph(0,2,1),finsOnly);
        Save(off,finsOnly?"fins-self-off":"fur-self-off");Save(high,finsOnly?"fins-self-on":"fur-self-on");
    }
    static ShaderGraph Graph(int scene,int quality,float strength)
    {
        var g=new ShaderGraph{GraphId="fur-shadow-smoke"};
        GraphNode Add(string id,string op){var n=NodeCatalog.Create("core."+op);n.Id=id;g.Nodes.Add(n);return n;}
        void Link(string a,string p,string b,string q){g.Connections.Add(new GraphConnection{Id=a+b+q,From=new GraphPortRef{NodeId=a,PortId=p},To=new GraphPortRef{NodeId=b,PortId=q}});}
        var black=Add("black","constant");black.Properties["valueType"]="color";black.Properties["value"]=new JArray(0,0,0,1);
        var green=Add("green","constant");green.Properties["valueType"]="color";green.Properties["value"]=new JArray(.2,.8,.4,1);
        Add("base","unlitSurface");Add("output","output");var fur=Add("fur","fur");
        fur.Properties["layers"]=8;fur.Properties["minLayers"]=8;fur.Properties["length"]=.16;fur.Properties["density"]=100;fur.Properties["thickness"]=.8;fur.Properties["taper"]=.5;fur.Properties["gravity"]=0;fur.Properties["windStrength"]=0;fur.Properties["rimStrength"]=0;fur.Properties["fins"]=1;fur.Properties["finOpacity"]=1;
        fur.Properties["receiveShadows"]=scene;fur.Properties["selfShadowQuality"]=quality;fur.Properties["selfShadowStrength"]=strength;fur.Properties["selfShadowBias"]=.03;
        Link("black","value","base","albedo");Link("base","surface","fur","base");Link("green","value","fur","rootColor");Link("green","value","fur","tipColor");Link("fur","surface","output","surface");return g;
    }
    static Color[] Render(ShaderGraph graph,bool finsOnly,bool isolate=true)
    {
        var result=ShaderEmitter.Emit(graph);Require(result.Succeeded,string.Join(";",result.Diagnostics.Select(d=>d.Message)));
        // Isolate actual emitted overlay passes to distinguish fin failures from shell lighting.
        var source=result.ShaderSource;
        if(isolate)source=Regex.Replace(source,finsOnly?@"Pass \{\nName ""Fur[0-9]+""[\s\S]*?ENDCG\n\}":@"Pass \{\nName ""FurFins""[\s\S]*?ENDCG\n\}","");
        var shader=ShaderUtil.CreateShaderAsset(source,true);var material=new Material(shader);
        try
        {
            Require(shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"Fur shadow shader unsupported");
            sphere.GetComponent<Renderer>().sharedMaterial=material;
            camera.Render();camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
            try{image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();var pixels=image.GetPixels();foreach(var c in pixels)Require(!float.IsNaN(c.r)&&!float.IsInfinity(c.r)&&!float.IsNaN(c.g)&&!float.IsInfinity(c.g),"Nonfinite fur pixel");Require(!ShaderUtil.ShaderHasError(shader),string.Join(";",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));return pixels;}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        finally{UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(shader);}
    }
    static float Energy(Color[] pixels){return pixels.Average(c=>c.g);}
    static void Save(Color[] pixels,string name){Directory.CreateDirectory("Assets/FurShadowEvidence");var t=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);t.SetPixels(pixels);t.Apply();File.WriteAllBytes("Assets/FurShadowEvidence/"+name+".png",t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
}
