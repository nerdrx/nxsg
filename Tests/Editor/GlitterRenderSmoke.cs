// Copy Tests/Portable/GlitterChecks.cs into the fixture alongside this test.
using System;
using System.IO;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
public static class GlitterRenderSmoke
{
    const int Size=256;static Camera camera;static Renderer quad;static RenderTexture target;
    public static void Run(){try{
        ShaderUtil.allowAsyncCompilation=false;
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
        quad=GameObject.CreatePrimitive(PrimitiveType.Quad).GetComponent<Renderer>();
        camera=new GameObject("Glitter camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-3);camera.orthographic=true;camera.orthographicSize=.5f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        target=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
        var g=GlitterChecks.Graph();var n=g.Nodes.Single(x=>x.Id=="glitter");n.Properties["scale"]=18;n.Properties["density"]=1;n.Properties["size"]=.6;n.Properties["viewStrength"]=0;n.Properties["twinkle"]=0;n.Properties["brightness"]=1;
        var plain=Render(g,Vector3.back*3);Require(Energy(plain)>.01f,"Glitter invisible");
        n.Properties["mask"]=0;Require(Energy(Render(g,Vector3.back*3))<.00001f,"Mask zero leaks glitter");n.Properties["mask"]=1;
        n.Properties["density"]=0;Require(Energy(Render(g,Vector3.back*3))<.00001f,"Density zero leaks glitter");n.Properties["density"]=1;
        n.Properties["size"]=0;Require(Energy(Render(g,Vector3.back*3))<.00001f,"Size zero leaks glitter");n.Properties["size"]=.6;
        n.Properties["brightness"]=3;var bright=Render(g,Vector3.back*3);Require(Mathf.Abs(Energy(bright)/Energy(plain)-3)<.02f,"HDR brightness does not scale color");n.Properties["brightness"]=1;
        n.Properties["viewStrength"]=1;n.Properties["sharpness"]=12;var a=Render(g,Vector3.back*3);var b=Render(g,new Vector3(3,0,-3));Require(Difference(a,b)>.001f,"View angle does not change glints");Save(a,"glitter-front");Save(b,"glitter-angle");
        n.Properties["viewStrength"]=0;n.Properties["twinkle"]=1;var t0=Render(g,Vector3.back*3);g.Nodes.Single(x=>x.Id=="time").Properties["value"]=2;var t1=Render(g,Vector3.back*3);Require(Difference(t0,t1)>.001f,"Twinkle does not animate");
        var mask=GlitterChecks.Graph("value","albedo");mask.Nodes[0].Properties["viewStrength"]=0;mask.Nodes[0].Properties["brightness"]=100;var mp=Render(mask,Vector3.back*3);Require(mp.Max(c=>c.r)<=1.0001f,"Mask exceeds one");
        // RGB luminance conversion on a wire, including alpha independence.
        var conversion=GlitterChecks.Graph("value","albedo");conversion.Connections.RemoveAll(e=>e.To.NodeId=="surface"&&e.To.PortId=="albedo");var c=conversion.Nodes.Single(x=>x.Id=="black");var ramp=NodeCatalog.Create("core.remap");ramp.Id="luma";conversion.Nodes.Add(ramp);GlitterChecks.Link(conversion,"black","value","luma","value");GlitterChecks.Link(conversion,"luma","value","surface","albedo");
        foreach(var test in new[]{new[]{1f,0f,0f,.2126f},new[]{0f,1f,0f,.7152f},new[]{0f,0f,1f,.0722f}}){c.Properties["value"]=new JArray(test[0],test[1],test[2],0);var pixels=Render(conversion,Vector3.back*3);Require(Mathf.Abs(pixels[Size/2*Size+Size/2].r-test[3])<.002f,"RGB luminance mismatch");}
        Debug.Log("NXSG GLITTER RENDER PASSED: masks, HDR, angle, twinkle, grayscale conversion");EditorApplication.Exit(0);
    }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
    static Color[] Render(ShaderGraph g,Vector3 eye){var e=ShaderEmitter.Emit(g);Require(e.Succeeded,string.Join(";",e.Diagnostics.Select(d=>d.Message)));
        // Pin view position independently of rasterization to isolate view-dependent glitter.
        var source=e.ShaderSource.Replace("_WorldSpaceCameraPos","_NXSG_TestEye").Replace("CGINCLUDE","CGINCLUDE\nfloat3 _NXSG_TestEye;");
        var shader=ShaderUtil.CreateShaderAsset(source,true);var mat=new Material(shader);mat.SetVector("_NXSG_TestEye",eye);quad.sharedMaterial=mat;
        try{camera.Render();camera.Render();Require(shader.isSupported&&!ShaderUtil.ShaderHasError(shader),string.Join(";",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true);try{image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply();var p=image.GetPixels();Require(p.All(c=>!float.IsNaN(c.r)&&!float.IsInfinity(c.r)),"Nonfinite pixels");return p;}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}}finally{UnityEngine.Object.DestroyImmediate(mat);UnityEngine.Object.DestroyImmediate(shader);}}
    static float Energy(Color[] p)=>p.Average(c=>c.r);
    static float Difference(Color[] a,Color[] b)=>a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)).Average();
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Save(Color[] p,string name){Directory.CreateDirectory("Assets/GlitterEvidence");var t=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);t.SetPixels(p);t.Apply();File.WriteAllBytes("Assets/GlitterEvidence/"+name+".png",t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);}
}
