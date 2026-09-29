using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        readonly List<GraphNode> vertexDeformations = new List<GraphNode>();
        bool vertexDeformEnabled;

        bool ActiveDeformation(GraphNode node)
        {
            if (Source(node,"mask") == null && (double?)node.Properties["mask"] == 0) return false;
            foreach (var port in new[]{"translation","rotation","scale","pivot","mask","snap","warp","nearDistance","nearStrength"})
                if(Source(node,port) != null) return true;
            foreach(var port in new[]{"translation","rotation","scale"})
                if(node.Properties[port] is JArray vector && vector.Any(v => (double)v != (port == "scale" ? 1 : 0))) return true;
            return (double?)node.Properties["snap"] > 0 || ((int?)node.Properties["shape"] ?? 0) != 0 && (double?)node.Properties["warp"] > 0 || (double?)node.Properties["nearDistance"] > 0;
        }

        GraphNode UnwrapVertexDeform(GraphNode root)
        {
            while (root.Operation == "core.vertexDeform")
            {
                vertexDeformations.Add(root);
                root = Source(root,"base") ?? throw new InvalidOperationException("Connect a mesh surface to Vertex Deform Base.");
            }
            return root;
        }

        string VertexDeformationCode()
        {
            var active=vertexDeformations.Where(ActiveDeformation).Reverse().ToArray();
            vertexDeformEnabled=active.Length>0;
            if (!vertexDeformEnabled) return "";
            var helpers = new StringBuilder();
            var body = new StringBuilder("void NX_ApplyVertexDeform(inout NXApp v) { NXInput input=NX_Make(v);\n");
            foreach (var node in active)
            {
                var name = "NX_Deform_" + Hash(node.Id);
                helpers.AppendLine(SpatialShader.TransformFunction(name,IntProp(node,"space",0,0,1),IntProp(node,"shape",0,0,2)));
                string V(string port, string fallback) => Input(node,port,node.Properties[port] == null ? fallback : Literal(node.Properties[port],"vector3"),"vector3",true);
                var call = SpatialShader.TransformCall(name,"v.vertex.xyz","v.normal","v.tangent",
                    V("translation","float3(0,0,0)"),V("rotation","float3(0,0,0)"),V("scale","float3(1,1,1)"),V("pivot","float3(0,0,0)"),
                    Scalar(node,"mask",1,true),Scalar(node,"snap",0,true),Scalar(node,"warp",0,true),Scalar(node,"nearDistance",0,true),Scalar(node,"nearStrength",1,true));
                body.AppendLine("{ "+name+"Result d="+call+"; v.vertex.xyz=d.position; v.normal=d.normal; v.tangent=d.tangent; }");
            }
            return helpers.Append(body).AppendLine("}").ToString();
        }

        string InstrumentVertexPasses(string passes)
        {
            if (!vertexDeformEnabled) return passes;
            // Every concrete vertex entry establishes the instance before touching
            // matrices. Hull passthrough entries defer to the domain's concrete entry.
            if (!passes.Contains("UNITY_SETUP_INSTANCE_ID(v);")) throw new InvalidOperationException("Vertex Deform found no supported mesh vertex entry.");
            return passes.Replace("UNITY_SETUP_INSTANCE_ID(v);","UNITY_SETUP_INSTANCE_ID(v); NX_ApplyVertexDeform(v);");
        }
    }
}
