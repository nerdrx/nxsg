using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;
using NXSG.Editor;
using UnityEngine;
using UnityEditor;

public static class FaceRenderSmoke
{
    static Camera camera;
    static GameObject mesh;
    static RenderTexture target;
    static int count;
    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            mesh=GameObject.CreatePrimitive(PrimitiveType.Quad);
            camera=new GameObject("Face render check").AddComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-3); camera.transform.LookAt(Vector3.zero);
            camera.orthographic=true; camera.orthographicSize=1; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
            target=new RenderTexture(96,96,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear); target.antiAliasing=4; target.Create(); camera.allowMSAA=true; camera.targetTexture=target;
            var graph=Graph();
            var front=Draw(graph); mesh.transform.rotation=Quaternion.Euler(0,180,0); var back=Draw(graph);
            Require(front.b>.8 && front.r<.1 && back.r>.8 && back.b<.1,"Front Face does not distinguish rasterized faces");
            mesh.transform.rotation=Quaternion.identity;
            UnityEngine.Object.DestroyImmediate(mesh); mesh=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var output=graph.Nodes.Single(n=>n.Id=="output"); var surface=graph.Nodes.Single(n=>n.Id=="surface");
            surface.Properties["opacity"]=.5; output.Properties["renderMode"]=3; output.Properties["twoPassTransparency"]=1;
            var alpha=Draw(graph);
            Require(alpha.b>.35 && alpha.b<.65 && alpha.r>.15 && alpha.r<.4,"Back/front passes do not composite in the requested order: "+alpha);
            output.Properties["backPassBlend"]=3; output.Properties["frontPassBlend"]=3;
            var premultiplied=Draw(graph); Require(Distance(alpha,premultiplied)<.03,"Premultiplied mode changes equivalent alpha result");

            graph=Graph(); output=graph.Nodes.Single(n=>n.Id=="output"); surface=graph.Nodes.Single(n=>n.Id=="surface");
            output.Properties["renderMode"]=2; output.Properties["cull"]=0; output.Properties["alphaToCoverage"]=1; surface.Properties["opacity"]=.5;
            var coverage=Draw(graph); Require(coverage.b>.1 && coverage.b<.9,"MSAA alpha coverage did not cover a partial pixel: "+coverage);
            output.Properties["alphaEdgeSharpness"]=1; surface.Properties["cutoff"]=.5;
            surface.Properties["opacity"]=.9; var inside=Draw(graph); surface.Properties["opacity"]=.1; var outside=Draw(graph);
            Require(inside.b>.9 && outside.maxColorComponent<.02,"Cutout coverage threshold failed");
            foreach(var operation in new[]{"core.softOutline","core.fur","core.surfaceParticles"}) {
                var layered=Graph(); var layer=NodeCatalog.Create(operation); layer.Id="layer"; layered.Nodes.Add(layer);
                layered.Connections.RemoveAll(e=>e.To.NodeId=="output"); Edge(layered,"surface","surface","layer","base"); Edge(layered,"layer","surface","output","surface");
                layered.Nodes.Single(n=>n.Id=="output").Properties["flipBackfaceNormals"]=1;
                Draw(layered);
            }
            Debug.Log("NXSG FACE RENDER SMOKE PASSED: front/back pixels, ordered transparency, premultiplied equivalence, 4x MSAA coverage and cutout endpoints");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally {
            RenderTexture.active=null;
            if(camera!=null) { camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(camera.gameObject); }
            if(mesh!=null) UnityEngine.Object.DestroyImmediate(mesh);
            if(target!=null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }
    static Color Draw(ShaderGraph graph)
    {
        var result=ShaderEmitter.Emit(graph); Require(result.Succeeded,string.Join(";",result.Diagnostics.Select(d=>d.Message)));
        Directory.CreateDirectory("Assets/SmokeResults/Faces"); File.WriteAllText("Assets/SmokeResults/Faces/faces-"+(count++)+".nxsg",GraphJson.Serialize(graph,true));
        using(var preview=GraphPreview.Create(graph,null)) {
            Require(!ShaderUtil.ShaderHasError(preview.Material.shader),"Face shader compile error: "+string.Join(";",ShaderUtil.GetShaderMessages(preview.Material.shader).Select(m=>m.message)));
            mesh.GetComponent<Renderer>().sharedMaterial=preview.Material; camera.Render(); RenderTexture.active=target;
            var image=new Texture2D(96,96,TextureFormat.RGBA32,false,true); image.ReadPixels(new Rect(0,0,96,96),0,0); image.Apply();
            var pixel=image.GetPixel(48,48); UnityEngine.Object.DestroyImmediate(image); RenderTexture.active=null; return pixel;
        }
    }
    static ShaderGraph Graph()
    {
        var graph=new ShaderGraph { GraphId="face-render" };
        foreach(var pair in new[]{new[]{"face","core.frontFace"},new[]{"red","core.constant"},new[]{"blue","core.constant"},new[]{"mix","core.mix"},new[]{"surface","core.unlitSurface"},new[]{"output","core.output"}}) {
            var node=NodeCatalog.Create(pair[1]); node.Id=pair[0]; graph.Nodes.Add(node);
        }
        foreach(var name in new[]{"red","blue"}) { var node=graph.Nodes.Single(n=>n.Id==name); node.Properties["valueType"]="color"; node.Properties["value"]=name=="red"?new JArray(1,0,0,1):new JArray(0,0,1,1); }
        graph.Nodes.Single(n=>n.Id=="output").Properties["cull"]=2;
        Edge(graph,"face","isFront","mix","factor"); Edge(graph,"red","value","mix","a"); Edge(graph,"blue","value","mix","b"); Edge(graph,"mix","value","surface","albedo"); Edge(graph,"surface","surface","output","surface"); return graph;
    }
    static void Edge(ShaderGraph g,string from,string port,string to,string input) => g.Connections.Add(new GraphConnection{Id=from+"-"+to+input,From=new GraphPortRef{NodeId=from,PortId=port},To=new GraphPortRef{NodeId=to,PortId=input}});
    static double Distance(Color a,Color b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b);
    static void Require(bool value,string message) { if(!value) throw new Exception(message); }
}
