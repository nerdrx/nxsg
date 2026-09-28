using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

public static class GeometryDissolveSmoke
{
    const int Size = 256;
    static Texture2D dissolveMask;
    public static void Run()
    {
        Camera camera = null; GameObject subject = null; RenderTexture target = null;
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            camera = new GameObject("Geometry dissolve camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0,0,-3); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f,.035f,.05f,0); camera.allowHDR = true; camera.fieldOfView = 35;
            target = new RenderTexture(Size,Size,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            subject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var renderer = subject.GetComponent<Renderer>();
            dissolveMask = new Texture2D(4,1,TextureFormat.RGBA32,false,true) { filterMode=FilterMode.Point, wrapMode=TextureWrapMode.Clamp };
            dissolveMask.SetPixels(new[]{Color.clear,Color.white,Color.clear,Color.white}); dissolveMask.Apply(false,false);
            Directory.CreateDirectory("Assets/SmokeResults/GeometryDissolve");
            Color[] baseline = null, soft = null;
            foreach (var state in new[]{0,1,2,3,4})
            {
                var graph = Graph(state); var emission = NXSG.Backend.ShaderEmitter.Emit(graph);
                Require(emission.Succeeded,"Graph emission failed: "+string.Join(";",emission.Diagnostics.Select(d=>d.Message)));
                using(var preview=GraphPreview.Create(graph,null))
                {
                    renderer.sharedMaterial=preview.Material;
                    foreach(var textureName in preview.Material.GetTexturePropertyNames()) preview.Material.SetTexture(textureName,dissolveMask);
                    for(int pass=0;pass<preview.Material.passCount;pass++) Require(preview.Material.SetPass(pass),"Geometry Dissolve pass failed: "+pass);
                    Require(!ShaderUtil.ShaderHasError(preview.Material.shader),"Geometry Dissolve shader error");
                    camera.Render(); RenderTexture.active=target;
                    var image=new Texture2D(Size,Size,TextureFormat.RGBAFloat,false,true); image.ReadPixels(new Rect(0,0,Size,Size),0,0); image.Apply(); var pixels=image.GetPixels();
                    Require(pixels.All(p=>!float.IsNaN(p.r)&&!float.IsInfinity(p.r)&&!float.IsNaN(p.a)),"Nonfinite fin pixels");
                    if(state==0) baseline=pixels;
                    if(state==1) { soft=pixels; Require(Changed(baseline,pixels)>80,"Geometry dissolve did not draw outside mesh"); }
                    if(state==2) Require(Changed(baseline,pixels)>1000 && pixels.All(p=>p.b<.1f),"Full dissolve leaves visible mesh");
                    if(state==4)
                    {
                        var visible=pixels.Count(p=>p.b>.1f); var baseVisible=baseline.Count(p=>p.b>.1f);
                        Require(visible>8 && visible<baseVisible,"Full dissolve mask did not preserve only its zero-alpha UV region: "+visible+" / "+baseVisible);
                    }
                    if(state==3) Require(Changed(baseline,pixels)==0,"Zero mask did not restore surface");
                    var png=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true); png.SetPixels(pixels); png.Apply(); File.WriteAllBytes("Assets/SmokeResults/GeometryDissolve/geometry-dissolve-"+state+".png",png.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(png); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active=null;
                }
                File.WriteAllText("Assets/SmokeResults/GeometryDissolve/geometry-dissolve-"+state+".nxsg",GraphJson.Serialize(graph));
            }
            Debug.Log("NXSG GEOMETRY DISSOLVE SMOKE PASSED"); EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { RenderTexture.active=null; if(dissolveMask!=null)UnityEngine.Object.DestroyImmediate(dissolveMask); if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);} if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject); if(subject!=null)UnityEngine.Object.DestroyImmediate(subject); }
    }
    public static ShaderGraph Graph(int state)
    {
        var graph=new ShaderGraph{GraphId="geometry-dissolve-smoke-"+state};
        var surface=NodeCatalog.Create("core.unlitSurface"); surface.Id="surface";
        var aura=NodeCatalog.Create("core.geometryDissolve"); aura.Id="aura"; aura.Properties["amount"] = state==0?0:state==2||state==4?1:.6; aura.Properties["mask"]=state==3?0:1;
        var color=NodeCatalog.Create("core.constant");color.Id="color"; color.Properties["valueType"]="color";color.Properties["value"]=new JArray(.08,.1,.16,1);
        graph.Resources.Add(new GraphResource{Id="dissolve-mask",Kind="texture2D",Uri="builtin://white"});
        var uv=NodeCatalog.Create("core.uv0");uv.Id="mask-uv";
        var texture=NodeCatalog.Create("core.texture2D");texture.Id="mask-texture";texture.Properties["resourceId"]="dissolve-mask";
        var output=NodeCatalog.Create("core.output");output.Id="output";
        graph.Nodes.AddRange(new[]{surface,aura,color,uv,texture,output});
        Edge(graph,"color","value","surface","albedo");Edge(graph,"surface","surface","aura","base");Edge(graph,"mask-uv","uv","mask-texture","uv");if(state==4)Edge(graph,"mask-texture","alpha","aura","mask");Edge(graph,"aura","surface","output","surface");return graph;
    }
    static int Changed(Color[] a,Color[] b) { return Enumerable.Range(0,a.Length).Count(i=>Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)>.02f); }
    static void Edge(ShaderGraph g,string from,string output,string to,string input){g.Connections.Add(new GraphConnection{Id=from+to+input,From=new GraphPortRef{NodeId=from,PortId=output},To=new GraphPortRef{NodeId=to,PortId=input}});}
    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
