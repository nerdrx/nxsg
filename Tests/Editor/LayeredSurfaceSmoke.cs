// Copy Tests/Portable/LayeredSurfaceChecks.cs into the fixture alongside this test.
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LayeredSurfaceSmoke
{
    const int Size=160;
    static Camera camera;
    static RenderTexture target;
    static GameObject subject;
    static Light key,fill;
    static Cubemap environment;

    public static void Run()
    {
        var oldAmbient=RenderSettings.ambientProbe;
        var oldMode=RenderSettings.ambientMode;
        var oldReflectionMode=RenderSettings.defaultReflectionMode;
        var oldReflection=RenderSettings.customReflectionTexture;
        var oldIntensity=RenderSettings.reflectionIntensity;
        var oldAsync=ShaderUtil.allowAsyncCompilation;
        var oldPixels=QualitySettings.pixelLightCount;
        try
        {
            Require(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null,"Graphics device required");
            ShaderUtil.allowAsyncCompilation=false;
            QualitySettings.pixelLightCount=4;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            var ambient=new SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.12f,.12f,.12f));
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientProbe=ambient;
            environment=new Cubemap(16,TextureFormat.RGBAHalf,true);
            foreach(CubemapFace face in new[]{CubemapFace.PositiveX,CubemapFace.NegativeX,CubemapFace.PositiveY,CubemapFace.NegativeY,CubemapFace.PositiveZ,CubemapFace.NegativeZ})
                environment.SetPixels(Enumerable.Repeat(face==CubemapFace.PositiveY?new Color(.8f,.9f,1):new Color(.08f,.1f,.15f),256).ToArray(),face);
            environment.Apply(true,false);
            RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture=environment;RenderSettings.reflectionIntensity=1;
            subject=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            camera=new GameObject("Layered PBR camera").AddComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
            camera.orthographic=true;camera.orthographicSize=.65f;camera.allowHDR=true;
            camera.renderingPath=RenderingPath.Forward;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.targetTexture=target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
            key=new GameObject("Layered key").AddComponent<Light>();key.type=LightType.Directional;
            key.transform.rotation=Quaternion.Euler(25,-25,0);key.intensity=2;key.renderMode=LightRenderMode.ForcePixel;
            fill=new GameObject("Layered add light").AddComponent<Light>();fill.type=LightType.Point;
            fill.transform.position=new Vector3(-.6f,.2f,-1);fill.range=4;fill.intensity=2;fill.color=new Color(.2f,.4f,1);fill.renderMode=LightRenderMode.ForcePixel;

            var basic=Render(LayeredSurfaceChecks.Graph("core.pbrSurface"));
            var neutral=Render(LayeredSurfaceChecks.Graph());
            Require(Difference(basic,neutral)<.00001f,"Neutral layers changed PBR lighting, emission or alpha");
            var wired=LayeredSurfaceChecks.Graph();LayeredSurfaceChecks.AddValue(wired,"coat",0);LayeredSurfaceChecks.AddValue(wired,"sheen",0);
            Require(Difference(basic,Render(wired))<.00001f,"Connected zero layers changed rendered PBR");

            var coat=LayeredSurfaceChecks.Graph();LayeredSurfaceChecks.Surface(coat).Properties["coat"]=1;
            var glossy=Render(coat);Require(Difference(neutral,glossy)>.0001f,"Coat did not affect lit surface");Save(glossy,"coat");
            LayeredSurfaceChecks.Surface(coat).Properties["coatRoughness"]=.8;
            Require(Difference(glossy,Render(coat))>.0001f,"Coat roughness did not affect reflection");
            LayeredSurfaceChecks.Surface(coat).Properties["coatRoughness"]=.1;
            LayeredSurfaceChecks.AddConstant(coat,"coatNormal","vector3",new JArray(.6,0,.8));
            Require(Difference(glossy,Render(coat))>.0001f,"Independent coat normal did not affect reflection");

            var sheen=LayeredSurfaceChecks.Graph();LayeredSurfaceChecks.Surface(sheen).Properties["sheen"]=1;
            LayeredSurfaceChecks.Surface(sheen).Properties["sheenColor"]=new JArray(.3,.5,1,1);
            LayeredSurfaceChecks.Surface(sheen).Properties["sheenRoughness"]=.2;
            var velvet=Render(sheen);Require(Difference(neutral,velvet)>.00001f,"Sheen did not affect grazing surface");Save(velvet,"sheen");
            LayeredSurfaceChecks.Surface(sheen).Properties["sheenRoughness"]=.8;
            Require(Difference(velvet,Render(sheen))>.00001f,"Sheen roughness did not affect grazing response");
            LayeredSurfaceChecks.Surface(sheen).Properties["sheenColor"]=new JArray(0,0,0,1);
            Require(Difference(neutral,Render(sheen))<.00001f,"Black sheen color darkened base");

            var both=LayeredSurfaceChecks.Graph();var surface=LayeredSurfaceChecks.Surface(both);
            surface.Properties["coat"]=1;surface.Properties["sheen"]=1;surface.Properties["coatRoughness"]=0;surface.Properties["sheenRoughness"]=0;
            LayeredSurfaceChecks.AddConstant(both,"coatNormal","vector3",new JArray(0,0,0));
            var withFill=Render(both);fill.enabled=false;
            Require(Difference(withFill,Render(both))>.0001f,"Additional pixel light did not contribute");
            key.enabled=false;RenderSettings.ambientProbe=new SphericalHarmonicsL2();RenderSettings.reflectionIntensity=0;
            both.Nodes.Single(n=>n.Id=="input-albedo").Properties["value"]=new JArray(0,0,0,1);
            both.Nodes.Single(n=>n.Id=="input-emission").Properties["value"]=new JArray(.2,.1,.05,1);
            var emitted=Render(both);
            Require(emitted[Size/2*Size+Size/2].r>.1f,"Layers suppressed emission");
            both.Nodes.Single(n=>n.Id=="input-emission").Properties["value"]=new JArray(0,0,0,1);
            Render(both); // Every capture checks every RGBA channel, including black and grazing normals.
            Debug.Log("NXSG LAYERED SURFACE SMOKE PASSED: neutral PBR, connected zero, coat roughness/normal, sheen roughness, black/grazing finite, emission, ForwardAdd");
            EditorApplication.Exit(0);
        }
        catch(Exception exception){Debug.LogException(exception);EditorApplication.Exit(1);}
        finally
        {
            RenderTexture.active=null;ShaderUtil.allowAsyncCompilation=oldAsync;QualitySettings.pixelLightCount=oldPixels;
            RenderSettings.ambientProbe=oldAmbient;RenderSettings.ambientMode=oldMode;
            RenderSettings.defaultReflectionMode=oldReflectionMode;RenderSettings.customReflectionTexture=oldReflection;RenderSettings.reflectionIntensity=oldIntensity;
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if(subject!=null)UnityEngine.Object.DestroyImmediate(subject);
            if(key!=null)UnityEngine.Object.DestroyImmediate(key.gameObject);
            if(fill!=null)UnityEngine.Object.DestroyImmediate(fill.gameObject);
            if(environment!=null)UnityEngine.Object.DestroyImmediate(environment);
        }
    }
    static Color[] Render(ShaderGraph graph)
    {
        using(var preview=GraphPreview.Create(graph,null))
        {
            var material=preview.Material;subject.GetComponent<Renderer>().sharedMaterial=material;
            Require(material.FindPass("ForwardAdd")>=0,"Missing ForwardAdd pass");
            for(var pass=0;pass<material.passCount;pass++)Require(material.SetPass(pass),"Layered shader pass failed "+pass);
            camera.Render();camera.Render();
            Require(material.shader.isSupported&&!ShaderUtil.ShaderHasError(material.shader),string.Join(";",ShaderUtil.GetShaderMessages(material.shader).Select(m=>m.message)));
            var previous=RenderTexture.active;RenderTexture.active=target;
            var texture=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);
            try
            {
                texture.ReadPixels(new Rect(0,0,Size,Size),0,0);texture.Apply();var pixels=texture.GetPixels();
                Require(pixels.All(c=>Finite(c.r)&&Finite(c.g)&&Finite(c.b)&&Finite(c.a)),"Layered surface produced non-finite pixels");
                return pixels;
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    static float Difference(Color[] a,Color[] b)=>a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)+Mathf.Abs(x.a-y.a)).Average();
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Save(Color[] pixels,string name)
    {
        Directory.CreateDirectory("Assets/LayeredSurfaceEvidence");
        var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);
        try{texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes("Assets/LayeredSurfaceEvidence/"+name+".png",texture.EncodeToPNG());}
        finally{UnityEngine.Object.DestroyImmediate(texture);}
    }
}
