using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

public static class FeatureNodesRenderSmoke
{
    static Camera camera; static GameObject quad; static RenderTexture target;
    static readonly string[] Operations={"parallaxUV","parallaxOcclusion","furMask","flowMapUV","ditherMask","truchet","weave","scales","dots","scratches","cracks","woodRings","marble","clouds","sparkleMask","scanlines","glitchUV","pixelateUV","kaleidoscopeUV","swapUV","spherizeUV","pinchUV","barrelUV","chromaticTexture","normalBlend","normalStrength","normalFromHeight","reflectionDirection","objectScale","objectOrigin","objectRandom","distanceToPoint","sphereMask","boxVolumeMask","capsuleMask","stripes3D","snowMask","wetnessColor","anisotropicHighlight"};
    public static void Run()
    {
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            quad=GameObject.CreatePrimitive(PrimitiveType.Quad);
            camera=new GameObject("Visual toolkit camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=.6f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            target=new RenderTexture(96,96,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            var collage=new Texture2D(8*96,5*96,TextureFormat.RGBA32,false,true);
            for(var index=0;index<Operations.Length;index++)
            {
                var operation=Operations[index];var graph=MakeGraph(operation);
                using(var preview=GraphPreview.Create(graph,null))
                {
                    quad.GetComponent<Renderer>().sharedMaterial=preview.Material;camera.Render();RenderTexture.active=target;
                    var texture=new Texture2D(96,96,TextureFormat.RGBAFloat,false,true);texture.ReadPixels(new Rect(0,0,96,96),0,0);texture.Apply();var pixels=texture.GetPixels();UnityEngine.Object.DestroyImmediate(texture);RenderTexture.active=null;
                    if(pixels.Any(c=>float.IsNaN(c.r)||float.IsInfinity(c.r)||float.IsNaN(c.g)||float.IsInfinity(c.g)))throw new Exception(operation+" nonfinite pixels");
                    var center=pixels[48*96+48];
                    if(operation=="chromaticTexture")
                        if(center.r<.9f||center.g<.9f)throw new Exception(operation+" white default missing");
                    if(new[]{"furMask","ditherMask","truchet","weave","scales","dots","scratches","cracks","woodRings","marble","clouds","scanlines","sphereMask","boxVolumeMask","capsuleMask","stripes3D"}.Contains(operation))
                    {
                        var bright=pixels.Count(c=>c.r>.1f);var dark=pixels.Count(c=>c.r<.05f);
                        if(bright<10||dark<100)throw new Exception(operation+" did not render a mask: "+bright+" / "+dark);
                    }
                    if(operation=="distanceFade"&&Mathf.Abs(center.r-.8f)>.02f)throw new Exception("Distance fade value mismatch "+center.r);
                    collage.SetPixels(index%8*96,(4-index/8)*96,96,96,pixels);
                }
            }
            collage.Apply();System.IO.Directory.CreateDirectory("Library/NXSG");System.IO.File.WriteAllBytes("Library/NXSG/feature-nodes.png",collage.EncodeToPNG());UnityEngine.Object.DestroyImmediate(collage);
            Debug.Log("NXSG FEATURE NODES RENDER SMOKE PASSED: 39 nodes, finite outputs, patterns, volume masks");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        finally{RenderTexture.active=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(quad!=null)UnityEngine.Object.DestroyImmediate(quad);if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject);}
    }
    static ShaderGraph MakeGraph(string operation)
    {
        var graph=new ShaderGraph {GraphId="visual-node-render"};var node=NodeCatalog.Create("core."+operation);node.Id="test";graph.Nodes.Add(node);
        if(operation=="parallaxOcclusion"||operation=="chromaticTexture")
        {graph.Resources.Add(new GraphResource {Id="texture",Kind="texture2D",Uri="builtin://white"});node.Properties["resourceId"]="texture";}
        graph.Nodes.Add(new GraphNode {Id="surface",Operation="core.unlitSurface"});graph.Nodes.Add(new GraphNode {Id="output",Operation="core.output"});
        var port=NodeCatalog.Ports(node.Operation,true).First();var type=NodeCatalog.PortType(node,port);
        if(type=="vector2"||type=="vector3")
        {graph.Nodes.Add(new GraphNode {Id="preview",Operation="core.previewVector"});Wire(graph,"test",port,"preview",type=="vector2"?"uv":"normal");Wire(graph,"preview","color","surface","albedo");}
        else Wire(graph,"test",port,"surface","albedo");Wire(graph,"surface","surface","output","surface");return graph;
    }
    static void Wire(ShaderGraph graph,string from,string output,string to,string input){graph.Connections.Add(new GraphConnection {Id=from+output+to+input,From=new GraphPortRef {NodeId=from,PortId=output},To=new GraphPortRef {NodeId=to,PortId=input}});}
}
