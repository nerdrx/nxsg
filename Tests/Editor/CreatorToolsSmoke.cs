using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CreatorToolsSmoke
{
    public static void Run()
    {
        try
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new Exception("GPU required");
            ShaderUtil.allowAsyncCompilation=false;
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            Bake(); Clock(); Glow(); Bookmarks();
            Debug.Log("NXSG CREATOR TOOLS SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
    static GraphNode Add(ShaderGraph g,string op,string id){var n=NodeCatalog.Create("core."+op);n.Id=id;g.Nodes.Add(n);return n;}
    static void Link(ShaderGraph g,string a,string p,string b,string q){g.Connections.Add(new GraphConnection{Id=Guid.NewGuid().ToString("N"),From=new GraphPortRef{NodeId=a,PortId=p},To=new GraphPortRef{NodeId=b,PortId=q}});}
    static ShaderGraph ColorGraph()
    {
        var g=new ShaderGraph{GraphId="creator-tools"};var c=Add(g,"constant","color");c.Properties["valueType"]="color";c.Properties["value"]=new JArray(.2,.4,.7,1);
        Add(g,"unlitSurface","surface").Properties["cutoff"]=0;Add(g,"output","output");Link(g,"color","value","surface","albedo");Link(g,"surface","surface","output","surface");return g;
    }
    static void Bake()
    {
        var g=ColorGraph();string original=GraphJson.Serialize(g);var baked=GraphBaker.Render(g,"color","value",32);
        var c=baked.GetPixel(16,16);Require(Mathf.Abs(c.r-.2f)<.03f&&Mathf.Abs(c.g-.4f)<.03f&&Mathf.Abs(c.b-.7f)<.03f,"Bake color wrong: "+c);UnityEngine.Object.DestroyImmediate(baked);
        Require(original==GraphJson.Serialize(g),"Bake mutated original graph");
        g.Nodes.First(n=>n.Id=="color").Properties["value"]=new JArray(-.2,.4,2,-.5);baked=GraphBaker.Render(g,"color","value",32);c=baked.GetPixel(16,16);Require(c.r<.02f&&Mathf.Abs(c.g-.4f)<.03f&&c.b>.98f&&c.a<.02f,"Bake did not clamp channels independently: "+c);UnityEngine.Object.DestroyImmediate(baked);g=GraphJson.Parse(original);
        // UV gradient checks actual varying interpolation, not only a uniform clear color.
        var uv=Add(g,"uv0","uv");Add(g,"splitUV","split");Link(g,"uv","uv","split","uv");
        baked=GraphBaker.Render(g,"split","u",32);Require(baked.GetPixel(28,16).r-baked.GetPixel(3,16).r>.6f,"Bake UVs not varying");UnityEngine.Object.DestroyImmediate(baked);
        var time=Add(g,"time","time");bool rejected=false;try{GraphBaker.Prepare(g,"time","value");}catch(InvalidOperationException){rejected=true;}Require(rejected,"Bake accepted animated time");
        var path=GraphBaker.Bake(g,"color","value","Assets/CreatorBake.png",32);var importer=(TextureImporter)AssetImporter.GetAtPath(path);Require(!importer.sRGBTexture,"Bake import not linear");AssetDatabase.DeleteAsset(path);
        Debug.Log("CREATOR BAKE GPU PASSED");
    }
    static void Clock()
    {
        var g=ColorGraph();g.Connections.RemoveAll(e=>e.To.NodeId=="surface"&&e.To.PortId=="albedo");Add(g,"time","time");Link(g,"time","value","surface","albedo");
        using(var p=GraphPreview.Create(g,null))
        {
            NXSG.Editor.PreviewClock.Apply(p.Material,0);var zero=Blit(p.Material);
            NXSG.Editor.PreviewClock.Apply(p.Material,.7f);var later=Blit(p.Material);
            Require(zero.r<.03f&&later.r>.65f&&later.r<.75f,"Preview clock did not freeze shader at requested time: "+zero+" / "+later);
        }
        Debug.Log("CREATOR PREVIEW CLOCK GPU PASSED");
    }
    static Color Blit(Material m)
    {
        var old=RenderTexture.active;var rt=RenderTexture.GetTemporary(32,32,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);var t=new Texture2D(32,32,TextureFormat.RGBA32,false,true);
        try{Graphics.Blit(Texture2D.whiteTexture,rt,m,0);RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,32,32),0,0);t.Apply();return t.GetPixel(16,16);}finally{RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(t);}
    }
    static void Glow()
    {
        var g=ColorGraph();g.Connections.RemoveAll(e=>e.To.NodeId=="surface"&&e.To.PortId=="albedo");var black=Add(g,"constant","black");black.Properties["valueType"]="color";black.Properties["value"]=new JArray(0,0,0,1);Link(g,"black","value","surface","albedo");Add(g,"darknessGlow","glow");Link(g,"color","value","glow","color");Link(g,"glow","color","surface","emission");
        var lightObject=new GameObject("Creator light");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=0;
        var subject=GameObject.CreatePrimitive(PrimitiveType.Sphere);var cameraObject=new GameObject("Creator camera");var camera=cameraObject.AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-3);camera.transform.rotation=Quaternion.identity;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var rt=new RenderTexture(64,64,24);rt.Create();camera.targetTexture=rt;var priorAmbient=RenderSettings.ambientLight;var priorMode=RenderSettings.ambientMode;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;
        try {using(var p=GraphPreview.Create(g,null)){subject.GetComponent<Renderer>().sharedMaterial=p.Material;var dark=CameraPixel(camera,rt);light.intensity=4;var bright=CameraPixel(camera,rt);Require(dark.b-bright.b>.2f,"Glow did not fade under bright main light: "+dark+" / "+bright);Debug.Log("CREATOR GLOW GPU PASSED "+dark+" / "+bright);}}
        finally{RenderSettings.ambientLight=priorAmbient;RenderSettings.ambientMode=priorMode;UnityEngine.Object.DestroyImmediate(subject);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(lightObject);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        // Decoded green sign test: sampled normal is presented through color conversion.
        var green=Add(g,"normalMap","normal");Link(g,"color","value","normal","color");green.Properties["flipGreen"]=1;
        Add(g,"previewVector","vector");Link(g,"normal","normal","vector","normal");
        g.Connections.RemoveAll(e=>e.To.NodeId=="surface");Link(g,"vector","color","surface","albedo");
        Color flipped,normal;
        using(var p=GraphPreview.Create(g,null)){flipped=Blit(p.Material);}
        green.Properties["flipGreen"]=0;using(var p=GraphPreview.Create(g,null)){normal=Blit(p.Material);}
        Require(flipped.g-normal.g>.1f,"Normal flip did not change green: "+flipped+" / "+normal);
        Debug.Log("CREATOR GLOW AND NORMAL COMPILE PASSED");
    }
    static Color CameraPixel(Camera camera,RenderTexture rt)
    {
        var old=RenderTexture.active;var t=new Texture2D(64,64,TextureFormat.RGBA32,false);
        try{camera.Render();RenderTexture.active=rt;t.ReadPixels(new Rect(0,0,64,64),0,0);t.Apply();return t.GetPixel(32,32);}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(t);}
    }
    static void Bookmarks()
    {
        var w=ScriptableObject.CreateInstance<GraphWindow>();w.Show();w.CreateGUI();const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            var g=ColorGraph();typeof(GraphWindow).GetField("graph",flags).SetValue(w,g);var before=GraphJson.ComputeSemanticHash(g);
            typeof(GraphWindow).GetMethod("SaveBookmark",flags).Invoke(w,new object[]{"Eyes"});
            Require(g.Layout.ExtensionData["bookmarks"]["Eyes"]!=null,"Bookmark missing");Require(before==GraphJson.ComputeSemanticHash(g),"Bookmark changed shader semantic hash");
            Require(GraphJson.Parse(GraphJson.Serialize(g)).Layout.ExtensionData["bookmarks"]["Eyes"]!=null,"Bookmark did not roundtrip");
        }
        finally{typeof(EditorWindow).GetProperty("hasUnsavedChanges").SetValue(w,false);w.Close();}
        Debug.Log("CREATOR BOOKMARKS PASSED");
    }
}
