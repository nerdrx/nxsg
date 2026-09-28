using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

public static class SoftOutlineSmoke
{
    const int Size = 256;
    public static void Run()
    {
        Camera camera = null; GameObject subject = null; RenderTexture target = null;
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            camera = new GameObject("Soft outline camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0,0,-3); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f,.035f,.05f,0); camera.allowHDR = true; camera.fieldOfView = 35;
            target = new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var renderer = subject.GetComponent<Renderer>();
            Directory.CreateDirectory("Assets/SmokeResults/SoftOutline");
            Color[] baseline = null, soft = null;
            foreach (var state in new[]{0,1,2,3,4,5})
            {
                var graph = Graph(state); var emission = NXSG.Backend.ShaderEmitter.Emit(graph);
                Require(emission.Succeeded,"Graph emission failed: "+string.Join(";",emission.Diagnostics.Select(d=>d.Message)));
                using(var preview=GraphPreview.Create(graph,null))
                {
                    renderer.sharedMaterial=preview.Material;
                    if(state>=4) preview.Material.SetColor("_Color",new Color(1,1,1,0));
                    for(int pass=0;pass<preview.Material.passCount;pass++) Require(preview.Material.SetPass(pass),"Soft Outline pass failed: "+pass);
                    Require(!ShaderUtil.ShaderHasError(preview.Material.shader),"Soft Outline shader error");
                    camera.Render(); RenderTexture.active=target;
                    var image=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true); image.ReadPixels(new Rect(0,0,Size,Size),0,0); image.Apply(); var pixels=image.GetPixels();
                    Require(pixels.All(p=>!float.IsNaN(p.r)&&!float.IsInfinity(p.r)&&!float.IsNaN(p.a)),"Nonfinite fin pixels");
                    if(state==0) baseline=pixels;
                    if(state==1) { soft=pixels; Require(Changed(baseline,pixels)>80,"Soft outline did not draw outside mesh"); }
                    if(state==2) Require(Changed(baseline,pixels)==0,"Zero opacity still draws fins");
                    if(state==3) Require(Changed(soft,pixels)>50,"Falloff did not change feathering");
                    if(state>=4) Require(pixels.All(p=>p.a<.001f),"Transparent material tint leaves visible surface or fins");
                    var png=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true); png.SetPixels(pixels); png.Apply(); File.WriteAllBytes("Assets/SmokeResults/SoftOutline/soft-outline-"+state+".png",png.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(png); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active=null;
                }
                File.WriteAllText("Assets/SmokeResults/SoftOutline/soft-outline-"+state+".nxsg",GraphJson.Serialize(graph));
            }
            Debug.Log("NXSG SOFT OUTLINE SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { RenderTexture.active=null; if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);} if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject); if(subject!=null)UnityEngine.Object.DestroyImmediate(subject); }
    }
    public static ShaderGraph Graph(int state)
    {
        var graph=new ShaderGraph{GraphId="soft-outline-smoke-"+state};
        var surface=NodeCatalog.Create("core.unlitSurface"); surface.Id="surface";
        surface.Properties["useAlbedoAlpha"]=state==5?0:1;
        var aura=NodeCatalog.Create("core.softOutline"); aura.Id="aura"; aura.Properties["width"] = state==0?0:.15; aura.Properties["opacity"]=state==2?0:1; aura.Properties["falloff"]=state==3?4:1;
        var color=NodeCatalog.Create("core.constant");color.Id="color"; color.Properties["valueType"]="color";color.Properties["value"]=new JArray(.08,.1,.16,1);
        var output=NodeCatalog.Create("core.output");output.Id="output";
        graph.Nodes.AddRange(new[]{surface,aura,color,output});
        Edge(graph,"color","value","surface","albedo");Edge(graph,"surface","surface","aura","base");Edge(graph,"aura","surface","output","surface");return graph;
    }
    static int Changed(Color[] a,Color[] b) { return Enumerable.Range(0,a.Length).Count(i=>Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)>.02f); }
    static void Edge(ShaderGraph g,string from,string output,string to,string input){g.Connections.Add(new GraphConnection{Id=from+to+input,From=new GraphPortRef{NodeId=from,PortId=output},To=new GraphPortRef{NodeId=to,PortId=input}});}
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
