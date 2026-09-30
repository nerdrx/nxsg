using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
            return Render(graph,nodeId,port,resolution,null);
        }

        public static Texture2D Render(ShaderGraph graph,string nodeId,string port,int resolution,Material context)
        {
            if(resolution<16||resolution>2048)throw new ArgumentOutOfRangeException(nameof(resolution));
            var prepared=Prepare(graph,nodeId,port);
            var old=RenderTexture.active;
            var target=RenderTexture.GetTemporary(resolution,resolution,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Texture2D texture=null;
            try
            {
                using(var preview=GraphPreview.Create(prepared,context))
                {
                    // GraphPreview assigns graph defaults after copying the material. Restore
                    // matching material overrides so this bake reflects the selected material.
                    ApplyContext(preview,context);
                    preview.Material.SetFloat("_NXSG_PreviewClock",1); preview.Material.SetFloat("_NXSG_PreviewTime",0);
                    // Blit binds its source as _MainTex; keep the graph's first texture intact.
                    var source=preview.Material.HasProperty("_MainTex")?preview.Material.GetTexture("_MainTex"):null;
                    Graphics.Blit(source,target,preview.Material,0);
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

        // A mobile bake is a sampled moment, not a static-node bake. Keep the
        // entire color branch so every texture, parameter and animation contributes.
        public static Texture2D RenderSnapshot(ShaderGraph graph,string nodeId,string port,int resolution,Material context,float seconds,bool audioEnabled,float audioValue)
        {
            if(resolution<16||resolution>2048)throw new ArgumentOutOfRangeException(nameof(resolution));
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
            var snapshot=GraphJson.Parse(GraphJson.Serialize(graph));
            // LTCGI is world lighting. Keep its input color in a mobile
            // snapshot, but omit the world-dependent lighting calculation.
            foreach(var lighting in snapshot.Nodes.Where(n=>n.Operation=="core.ltcgi"))
            {
                var albedo=snapshot.Connections.SingleOrDefault(e=>e.To.NodeId==lighting.Id&&e.To.PortId=="albedo");
                snapshot.Connections.RemoveAll(e=>e.To.NodeId==lighting.Id);
                if(albedo!=null)
                {
                    foreach(var edge in snapshot.Connections.Where(e=>e.From.NodeId==lighting.Id))
                    {
                        edge.From.NodeId=albedo.From.NodeId;
                        edge.From.PortId=albedo.From.PortId;
                    }
                    if(nodeId==lighting.Id){nodeId=albedo.From.NodeId;port=albedo.From.PortId;}
                }
                else
                {
                    lighting.Operation="core.constant";
                    lighting.Properties=new JObject { ["valueType"]="color", ["value"]=new JArray(0,0,0,1) };
                    foreach(var edge in snapshot.Connections.Where(e=>e.From.NodeId==lighting.Id))edge.From.PortId="value";
                    if(nodeId==lighting.Id)port="value";
                }
            }
            var root=snapshot.Nodes.Single(n=>n.Id==nodeId);
            var type=GraphTypes.PortType(snapshot,root,port,GraphTypes.Infer(snapshot));
            if(type!="float"&&type!="color"&&type!="vector3")throw new InvalidOperationException("Snapshot needs a Float, Color, or Normal output.");
            var live=new HashSet<string>();var pending=new Stack<string>();pending.Push(nodeId);
            while(pending.Count>0)
            {
                var id=pending.Pop();if(!live.Add(id))continue;
                foreach(var edge in snapshot.Connections.Where(e=>e.To.NodeId==id))pending.Push(edge.From.NodeId);
            }
            snapshot.Patterns.Clear();snapshot.Layout=null;
            snapshot.Nodes.RemoveAll(n=>!live.Contains(n.Id));
            snapshot.Connections.RemoveAll(e=>!live.Contains(e.From.NodeId)||!live.Contains(e.To.NodeId));
            var surface=NodeCatalog.Create("core.unlitSurface");surface.Properties["cutoff"]=0;snapshot.Nodes.Add(surface);
            var output=NodeCatalog.Create("core.output");snapshot.Nodes.Add(output);
            var clamp=NodeCatalog.Create("core.clamp");snapshot.Nodes.Add(clamp);
            if(type=="vector3")
            {
                var normalPreview=NodeCatalog.Create("core.previewVector");snapshot.Nodes.Add(normalPreview);
                Connect(snapshot,nodeId,port,normalPreview.Id,"normal");
                Connect(snapshot,normalPreview.Id,"color",clamp.Id,"color");
            }
            else Connect(snapshot,nodeId,port,clamp.Id,"color");
            Connect(snapshot,clamp.Id,"color",surface.Id,"albedo");Connect(snapshot,surface.Id,"surface",output.Id,"surface");

            var old=RenderTexture.active;
            var target=RenderTexture.GetTemporary(resolution,resolution,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Texture2D pixels=null;
            GameObject plane=null,cameraObject=null;
            Mesh mesh=null;
            try
            {
                using(var preview=GraphPreview.Create(snapshot,context))
                {
                    ApplyContext(preview,context);
                    preview.Material.SetFloat("_NXSG_PreviewClock",1);
                    preview.Material.SetFloat("_NXSG_PreviewTime",seconds);
                    preview.Material.SetFloat("_NXSG_AudioLinkPreview",audioEnabled?1:0);
                    preview.Material.SetFloat("_NXSG_AudioLinkValue",Mathf.Clamp01(audioValue));
                    mesh=new Mesh { name="NXSG mobile snapshot plane", hideFlags=HideFlags.HideAndDontSave };
                    mesh.vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)};
                    mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
                    mesh.normals=new[]{Vector3.back,Vector3.back,Vector3.back,Vector3.back};
                    mesh.tangents=new[]{new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1),new Vector4(1,0,0,1)};
                    mesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
                    mesh.triangles=new[]{0,2,1,1,2,3};
                    plane=new GameObject("NXSG mobile snapshot plane") { hideFlags=HideFlags.HideAndDontSave, layer=31 };
                    plane.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=plane.AddComponent<MeshRenderer>();renderer.sharedMaterial=preview.Material;
                    renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
                    cameraObject=new GameObject("NXSG mobile snapshot camera") { hideFlags=HideFlags.HideAndDontSave };
                    var camera=cameraObject.AddComponent<Camera>();
                    camera.transform.position=new Vector3(0,0,-2);camera.transform.LookAt(Vector3.zero);
                    camera.orthographic=true;camera.orthographicSize=.5f;camera.nearClipPlane=.1f;camera.farClipPlane=5;
                    camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                    camera.cullingMask=1<<31;camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=false;
                    camera.targetTexture=target;camera.Render();camera.targetTexture=null;
                    RenderTexture.active=target;
                    pixels=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true);
                    pixels.ReadPixels(new Rect(0,0,resolution,resolution),0,0);pixels.Apply();
                    return pixels;
                }
            }
            catch {if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);throw;}
            finally
            {
                RenderTexture.active=old;
                if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);
                if(plane!=null)UnityEngine.Object.DestroyImmediate(plane);
                if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        // Pruning a branch can promote a different texture to _MainTex. Match
        // texture resources by their stable material labels, not slot symbols.
        static void ApplyContext(GraphPreview preview, Material context)
        {
            if(context==null)return;
            var textures=preview.Properties.Where(p=>p.ResourceId!=null).ToArray();
            var defaults=textures.ToDictionary(p=>p.ResourceId,p=>new
            {
                texture=preview.Material.GetTexture(p.Name),
                scale=preview.Material.GetTextureScale(p.Name),
                offset=preview.Material.GetTextureOffset(p.Name)
            });
            preview.Material.CopyMatchingPropertiesFromMaterial(context);
            foreach(var property in textures)
            {
                string sourceName=null;
                for(var i=0;i<context.shader.GetPropertyCount();i++)
                    if(context.shader.GetPropertyType(i)==ShaderPropertyType.Texture &&
                       context.shader.GetPropertyDescription(i)==property.DisplayName)
                    { sourceName=context.shader.GetPropertyName(i); break; }
                if(sourceName!=null)
                {
                    preview.Material.SetTexture(property.Name,context.GetTexture(sourceName));
                    preview.Material.SetTextureScale(property.Name,context.GetTextureScale(sourceName));
                    preview.Material.SetTextureOffset(property.Name,context.GetTextureOffset(sourceName));
                }
                else
                {
                    var fallback=defaults[property.ResourceId];
                    preview.Material.SetTexture(property.Name,fallback.texture);
                    preview.Material.SetTextureScale(property.Name,fallback.scale);
                    preview.Material.SetTextureOffset(property.Name,fallback.offset);
                }
            }
        }
    }
}
