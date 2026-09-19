using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    public static class GraphBaker
    {
        // Only UV-local operations can be represented by a flat texture.
        static readonly HashSet<string> Allowed = new HashSet<string>((
            "constant value parameter uv0 uvTransform uvRotate polarUV uvTile texture2D noise musgrave voronoi checker wave gradient " +
            "add subtract multiply divide minimum maximum mix oneMinus clamp absolute power sqrt sine cosine fraction floor ceil round step smoothstep remap pingPong " +
            "splitColor combineColor luminance contrast saturation splitUV combineUV ramp colorRamp layer emission posterize circleMask boxMask polygonMask starMask radialRays spiral brick hexGrid").Split(' ').Select(s=>"core."+s));

        public static ShaderGraph Prepare(ShaderGraph source,string nodeId,string port)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            var clone=GraphJson.Parse(GraphJson.Serialize(source));
            var node=clone.Nodes.Single(n=>n.Id==nodeId);
            var type=GraphTypes.PortType(clone,node,port,GraphTypes.Infer(clone));
            if(type!="float"&&type!="color")throw new InvalidOperationException("Choose a Float or Color output.");
            var live=new HashSet<string>(); var pending=new Stack<string>();pending.Push(nodeId);
            while(pending.Count>0)
            {
                var id=pending.Pop();if(!live.Add(id))continue;
                var current=clone.Nodes.Single(n=>n.Id==id);
                if(!Allowed.Contains(current.Operation))throw new InvalidOperationException(NodeCatalog.Title(current.Operation)+" depends on animation, geometry or scene data and cannot be baked as a static UV texture.");
                if(current.Operation=="core.parameter")
                {
                    var parameter=clone.Parameters.Single(p=>p.Id==(string)current.Properties["parameterId"]);
                    if(parameter.Binding!=GraphBindingKind.Constant)throw new InvalidOperationException("Animated/material parameters cannot be frozen silently. Use a constant for this branch.");
                }
                var coordinate=(string)current.Properties["coordinateSource"];
                if(coordinate!=null && coordinate!="uv0")throw new InvalidOperationException("Only UV0-local coordinates can be baked.");
                if((int?)current.Properties["dimensions"]>2)throw new InvalidOperationException("3D/4D noise requires geometry; choose 1D/2D for a UV bake.");
                if(NodeCatalog.Ports(current.Operation,false).Contains("time") && !clone.Connections.Any(e=>e.To.NodeId==id&&e.To.PortId=="time") && ((double?)current.Properties["speed"]??1)!=0)
                    throw new InvalidOperationException("Freeze "+NodeCatalog.Title(current.Operation)+" with a constant Time input or Speed 0 before baking.");
                foreach(var edge in clone.Connections.Where(e=>e.To.NodeId==id))pending.Push(edge.From.NodeId);
            }
            clone.Patterns.Clear();clone.Layout=null;
            clone.Nodes.RemoveAll(n=>!live.Contains(n.Id));clone.Connections.RemoveAll(e=>!live.Contains(e.From.NodeId)||!live.Contains(e.To.NodeId));
            var surface=NodeCatalog.Create("core.unlitSurface");surface.Properties["cutoff"]=0;clone.Nodes.Add(surface);
            var output=NodeCatalog.Create("core.output");clone.Nodes.Add(output);
            var clamp=NodeCatalog.Create("core.clamp");clone.Nodes.Add(clamp);
            Connect(clone,nodeId,port,clamp.Id,"color");Connect(clone,clamp.Id,"color",surface.Id,"albedo");Connect(clone,surface.Id,"surface",output.Id,"surface");
            return clone;
        }
        static void Connect(ShaderGraph g,string a,string p,string b,string q) {g.Connections.Add(new GraphConnection{Id=Guid.NewGuid().ToString("N"),From=new GraphPortRef{NodeId=a,PortId=p},To=new GraphPortRef{NodeId=b,PortId=q}});}

        public static Texture2D Render(ShaderGraph graph,string nodeId,string port,int resolution)
        {
            if(resolution<16||resolution>2048)throw new ArgumentOutOfRangeException(nameof(resolution));
            var prepared=Prepare(graph,nodeId,port);
            var old=RenderTexture.active;
            var target=RenderTexture.GetTemporary(resolution,resolution,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Texture2D texture=null;
            try
            {
                using(var preview=GraphPreview.Create(prepared,null))
                {
                    preview.Material.SetFloat("_NXSG_PreviewClock",1); preview.Material.SetFloat("_NXSG_PreviewTime",0);
                    Graphics.Blit(Texture2D.whiteTexture,target,preview.Material,0);
                    RenderTexture.active=target;
                    texture=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true);
                    texture.ReadPixels(new Rect(0,0,resolution,resolution),0,0);texture.Apply();return texture;
                }
            }
            catch {if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);throw;}
            finally {RenderTexture.active=old;RenderTexture.ReleaseTemporary(target);}
        }
        public static string Bake(ShaderGraph graph,string nodeId,string port,string path,int resolution)
        {
            if(string.IsNullOrEmpty(path)||!path.StartsWith("Assets/",StringComparison.Ordinal)||!path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||path.Contains(".."))throw new InvalidOperationException("Choose a PNG inside Assets.");
            path=AssetDatabase.GenerateUniqueAssetPath(path);
            var texture=Render(graph,nodeId,port,resolution);
            try {File.WriteAllBytes(path,texture.EncodeToPNG());} finally {UnityEngine.Object.DestroyImmediate(texture);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer!=null){importer.sRGBTexture=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.SaveAndReimport();}
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<Texture2D>(path);return path;
        }
    }
}
