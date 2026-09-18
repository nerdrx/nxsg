using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Isolated, graphics-enabled Unity. Checks actual shader output and coordinate stability.
public static class ProceduralRenderSmoke
{
    static GameObject quad, cameraObject;
    static Camera camera;
    static RenderTexture target;
    static Mesh mesh;
    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mesh = UnityEngine.Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
            quad.GetComponent<MeshFilter>().sharedMesh = mesh;
            mesh.uv = Enumerable.Repeat(new Vector2(.25f,.25f),mesh.vertexCount).ToArray();
            mesh.uv2 = Enumerable.Repeat(new Vector2(1.25f,.25f),mesh.vertexCount).ToArray();
            mesh.uv3 = Enumerable.Repeat(new Vector2(.65f,.35f),mesh.vertexCount).ToArray();
            mesh.uv4 = Enumerable.Repeat(new Vector2(.85f,.75f),mesh.vertexCount).ToArray();
            cameraObject = new GameObject("Procedural test camera"); camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.orthographic=true; camera.orthographicSize=.7f;
            camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);
            target=new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            foreach(var op in new[]{"core.noise","core.musgrave","core.voronoi","core.checker","core.wave"})
            foreach(var dim in op=="core.noise"?new[]{1,2,3,4}:new[]{2,3})
            {
                var graph=Graph(op);var node=graph.Nodes.Single(n=>n.Id=="pattern");node.Properties["dimensions"]=dim;node.Properties["speed"]=0;
                using(var preview=GraphPreview.Create(graph,null)){var pixels=Render(preview.Material);Require(pixels.All(c=>!float.IsNaN(c.r)&&!float.IsInfinity(c.r)&&c.r>=-.01f&&c.r<=1.01f),op+" "+dim+"D range");}
            }
            var evolving=Graph("core.noise");var noise=evolving.Nodes.Single(n=>n.Id=="pattern");noise.Properties["dimensions"]=4;noise.Properties["speed"]=1;
            var time=new GraphNode{Id="clock",Operation="core.value",Properties=new JObject{["value"]=.1}};evolving.Nodes.Add(time);Edge(evolving,"clock","value","pattern","time");
            Color[] first,near,far;
            using(var p=GraphPreview.Create(evolving,null))first=Render(p.Material);
            time.Properties["value"]=.101;using(var p=GraphPreview.Create(evolving,null))near=Render(p.Material);
            time.Properties["value"]=.8;using(var p=GraphPreview.Create(evolving,null))far=Render(p.Material);
            Require(Difference(first,near)<.02f,"4D evolution discontinuous");Require(Difference(first,far)>.005f,"4D time did not evolve volume");
            foreach (var op in new[] { "core.musgrave", "core.wave" })
            {
                var g=Graph(op);var n=g.Nodes.Single(v=>v.Id=="pattern");n.Properties["mode"]=0;
                Color[] before,after;using(var p=GraphPreview.Create(g,null))before=Render(p.Material);
                n.Properties["mode"]=1;using(var p=GraphPreview.Create(g,null))after=Render(p.Material);
                Require(Difference(before,after)>.001f,op+" pattern selector had no effect");
            }
            var cells=Graph("core.voronoi");var cell=cells.Nodes.Single(n=>n.Id=="pattern");cell.Properties["scale"]=2;cell.Properties["randomness"]=0;
            using(var p=GraphPreview.Create(cells,null))Require(Render(p.Material)[0].r<.02f,"2D Voronoi cell center must be zero");
            var checker=Graph("core.checker");var check=checker.Nodes.Single(n=>n.Id=="pattern");check.Properties["scale"]=1;
            using(var p=GraphPreview.Create(checker,null))Require(Render(p.Material)[0].r<.02f,"checker even cell");
            check.Properties["coordinateSource"]="uv1";using(var p=GraphPreview.Create(checker,null))Require(Render(p.Material)[0].r>.98f,"checker/UV1 odd cell");
            foreach(var source in new[]{"uv0","uv1","uv2","uv3","polar","object","world","panosphere","matcap"})
            {
                var g=Coordinates(source);using(var p=GraphPreview.Create(g,null))
                {
                    camera.transform.position=new Vector3(0,0,-3);camera.transform.LookAt(Vector3.zero);var a=Render(p.Material);
                    camera.transform.position=new Vector3(.7f,.3f,-3);camera.transform.LookAt(Vector3.zero);var b=Render(p.Material);
                    if(source.StartsWith("uv")||source=="polar")Require(Difference(a,b)<.005f,source+" unexpectedly follows camera");
                    if(source=="panosphere"||source=="matcap")Require(Difference(a,b)>.001f,source+" camera mapping did not respond");
                    if(source=="uv2")Require(Mathf.Abs(a[0].r-.65f)<.02f,"UV2 channel missing");
                    if(source=="uv3")Require(Mathf.Abs(a[0].r-.85f)<.02f,"UV3 channel missing");
                }
            }
            Debug.Log("NXSG PROCEDURAL RENDER SMOKE PASSED");EditorApplication.Exit(0);
        }
        catch(Exception exception){Debug.LogException(exception);EditorApplication.Exit(1);}
        finally{RenderTexture.active=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);if(quad!=null)UnityEngine.Object.DestroyImmediate(quad);if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);}
    }
    static ShaderGraph Graph(string operation)
    {
        var g=new ShaderGraph{GraphId="procedural-test"};var pattern=NodeCatalog.Create(operation);pattern.Id="pattern";g.Nodes.Add(pattern);
        g.Nodes.Add(new GraphNode{Id="surface",Operation="core.unlitSurface"});g.Nodes.Add(new GraphNode{Id="output",Operation="core.output"});
        Edge(g,"pattern","value","surface","albedo");Edge(g,"surface","surface","output","surface");return g;
    }
    static ShaderGraph Coordinates(string source)
    {
        var g=Graph("core.value");g.Nodes.RemoveAll(n=>n.Id=="pattern");g.Connections.RemoveAll(e=>e.From.NodeId=="pattern");
        g.Nodes.Add(new GraphNode{Id="uv",Operation="core.uv0",Properties=new JObject{["coordinateSource"]=source}});
        g.Nodes.Add(new GraphNode{Id="convert",Operation="core.previewVector"});Edge(g,"uv","uv","convert","uv");Edge(g,"convert","color","surface","albedo");return g;
    }
    static void Edge(ShaderGraph g,string from,string fp,string to,string tp){g.Connections.Add(new GraphConnection{Id=from+"-"+to+"-"+tp,From=new GraphPortRef{NodeId=from,PortId=fp},To=new GraphPortRef{NodeId=to,PortId=tp}});}
    static Color[] Render(Material material)
    {
        quad.GetComponent<Renderer>().sharedMaterial=material;camera.Render();RenderTexture.active=target;
        var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();var pixels=image.GetPixels(28,28,8,8);UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=null;return pixels;
    }
    static float Difference(Color[] a,Color[] b){return a.Zip(b,(x,y)=>Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)).Average();}
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
