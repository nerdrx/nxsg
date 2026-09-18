using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Actual GPU output checks; run only in the isolated graphics-enabled test project.
public static class UtilityNodesRenderSmoke
{
    static Camera camera;
    static GameObject quad;
    static RenderTexture target;
    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            camera = new GameObject("Utility node camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-2); camera.orthographic = true; camera.orthographicSize = .5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            target = new RenderTexture(32,32,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create(); camera.targetTexture = target;
            Check("absolute", "value", new JObject { ["a"]=-.25 }, .25f);
            Check("power", "value", new JObject { ["a"]=-.5, ["b"]=2 }, .25f);
            Check("sqrt", "value", new JObject { ["a"]=.25 }, .5f);
            Check("sqrt", "value", new JObject { ["a"]=-1 }, 0);
            Check("sine", "value", new JObject { ["a"]=Math.PI/6 }, .5f);
            Check("cosine", "value", new JObject { ["a"]=Math.PI/3 }, .5f);
            Check("fraction", "value", new JObject { ["a"]=-.25 }, .75f);
            Check("floor", "value", new JObject { ["a"]=.75 }, 0);
            Check("ceil", "value", new JObject { ["a"]=.25 }, 1);
            Check("round", "value", new JObject { ["a"]=.75 }, 1);
            Check("step", "value", new JObject { ["a"]=.5,["b"]=.7 }, 1);
            Check("smoothstep", "value", new JObject { ["value"]=.5,["low"]=0,["high"]=1 }, .5f);
            Check("remap", "value", new JObject { ["value"]=5,["inMin"]=0,["inMax"]=10,["outMin"]=.2,["outMax"]=.8 }, .5f);
            Check("pingPong", "value", new JObject { ["value"]=1.5,["length"]=1 }, .5f);
            Check("splitColor", "r", new JObject(), .2f, true);
            Check("combineColor", "color", new JObject { ["r"]=.2,["g"]=.4,["b"]=.6,["a"]=1 }, .2f);
            Check("luminance", "value", new JObject(), .37192f, true);
            Check("contrast", "color", new JObject { ["amount"]=2,["pivot"]=.5 }, -.1f, true);
            Check("saturation", "color", new JObject { ["amount"]=0 }, .37192f, true);
            Check("splitUV", "u", new JObject(), .3f, false, true);
            Check("combineUV", "uv", new JObject { ["u"]=.3,["v"]=.7 }, .3f);
            Debug.Log("NXSG UTILITY NODES RENDER SMOKE PASSED: 20 nodes and negative square-root guard");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active=null;
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(quad!=null)UnityEngine.Object.DestroyImmediate(quad);
            if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }
    static void Check(string operation,string port,JObject props,float expected,bool colorInput=false,bool uvInput=false)
    {
        var graph=new ShaderGraph { GraphId="utility-gpu-check" };
        var node=NodeCatalog.Create("core."+operation); node.Id="test";
        foreach(var p in props) node.Properties[p.Key]=p.Value.DeepClone(); graph.Nodes.Add(node);
        if(colorInput)
        {
            graph.Nodes.Add(new GraphNode { Id="color",Operation="core.constant",Properties=new JObject { ["valueType"]="color",["value"]=new JArray(.2,.4,.6,1) } });
            Wire(graph,"color","value","test","color");
        }
        if(uvInput)
        {
            graph.Nodes.Add(new GraphNode { Id="uv",Operation="core.combineUV",Properties=new JObject { ["u"]=.3,["v"]=.7 } });
            Wire(graph,"uv","uv","test","uv");
        }
        graph.Nodes.Add(new GraphNode { Id="surface",Operation="core.unlitSurface" });
        graph.Nodes.Add(new GraphNode { Id="output",Operation="core.output" });
        if(port=="uv")
        {
            graph.Nodes.Add(new GraphNode { Id="split",Operation="core.splitUV" });
            Wire(graph,"test",port,"split","uv"); Wire(graph,"split","u","surface","albedo");
        }
        else Wire(graph,"test",port,"surface","albedo");
        Wire(graph,"surface","surface","output","surface");
        using(var preview=GraphPreview.Create(graph,null))
        {
            quad.GetComponent<Renderer>().sharedMaterial=preview.Material;camera.Render();RenderTexture.active=target;
            var texture=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);texture.ReadPixels(new Rect(0,0,32,32),0,0);texture.Apply();
            var actual=texture.GetPixel(16,16).r;UnityEngine.Object.DestroyImmediate(texture);RenderTexture.active=null;
            if(float.IsNaN(actual)||float.IsInfinity(actual)||Mathf.Abs(actual-expected)>.025f)
                throw new InvalidOperationException(operation+" expected "+expected+" got "+actual);
        }
    }
    static void Wire(ShaderGraph graph,string from,string output,string to,string input)
    {
        graph.Connections.Add(new GraphConnection { Id=from+output+to+input,From=new GraphPortRef {NodeId=from,PortId=output},To=new GraphPortRef {NodeId=to,PortId=input} });
    }
}
