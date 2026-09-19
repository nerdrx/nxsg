using System.Text;

namespace NXSG.Backend
{
    // Built-In PC tessellation decorator. Kept as text generation so core stays Unity-free.
    internal static class TessellationShader
    {
        public static string Forward(string factor, string minFactor, string nearDistance, string farDistance, string smoothing)
        {
            return Common(factor, minFactor, nearDistance, farDistance, smoothing, "vert", "NXInput");
        }

        public static string Shadow(string factor, string minFactor, string nearDistance, string farDistance, string smoothing)
        {
            return Common(factor, minFactor, nearDistance, farDistance, smoothing, "vertShadow", "NXShadow");
        }

        static string Common(string factor, string minFactor, string nearDistance, string farDistance, string smoothing, string vertex, string returnType)
        {
            var b = new StringBuilder();
            b.Append("NXApp vertTess(NXApp v) { return v; }\n");
            b.Append("struct NXTessFactors { float edge[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };\n");
            b.Append("float NX_TessDistanceFactor(float3 p) { float d=distance(_WorldSpaceCameraPos,p); return clamp(lerp(")
                .Append(factor).Append(",").Append(minFactor).Append(",saturate((d-").Append(nearDistance).Append(")/max(.0001,").Append(farDistance).Append("-").Append(nearDistance).Append("))),1,63); }\n");
            b.Append("NXTessFactors NX_TessFactors(InputPatch<NXApp,3> p,uint patchId:SV_PrimitiveID) { NXTessFactors o; UNITY_SETUP_INSTANCE_ID(p[0]); float f0=NX_TessDistanceFactor(mul(unity_ObjectToWorld,p[0].vertex).xyz); float f1=NX_TessDistanceFactor(mul(unity_ObjectToWorld,p[1].vertex).xyz); float f2=NX_TessDistanceFactor(mul(unity_ObjectToWorld,p[2].vertex).xyz); o.edge[0]=(f1+f2)*.5; o.edge[1]=(f2+f0)*.5; o.edge[2]=(f0+f1)*.5; o.inside=(o.edge[0]+o.edge[1]+o.edge[2])/3; return o; }\n");
            b.Append("[domain(\"tri\")] [partitioning(\"fractional_odd\")] [outputtopology(\"triangle_cw\")] [outputcontrolpoints(3)] [patchconstantfunc(\"NX_TessFactors\")]\n");
            b.Append("NXApp hullTess(InputPatch<NXApp,3> p,uint id:SV_OutputControlPointID) { return p[id]; }\n");
            b.Append("[domain(\"tri\")] ").Append(returnType).Append(" domainTess(NXTessFactors f,const OutputPatch<NXApp,3> p,float3 bary:SV_DomainLocation) { NXApp a=p[0]; a.vertex=p[0].vertex*bary.x+p[1].vertex*bary.y+p[2].vertex*bary.z; a.normal=normalize(p[0].normal*bary.x+p[1].normal*bary.y+p[2].normal*bary.z); float3 q0=a.vertex.xyz-p[0].normal*dot(a.vertex.xyz-p[0].vertex.xyz,p[0].normal); float3 q1=a.vertex.xyz-p[1].normal*dot(a.vertex.xyz-p[1].vertex.xyz,p[1].normal); float3 q2=a.vertex.xyz-p[2].normal*dot(a.vertex.xyz-p[2].vertex.xyz,p[2].normal); a.vertex.xyz=lerp(a.vertex.xyz,q0*bary.x+q1*bary.y+q2*bary.z,saturate(").Append(smoothing).Append(")); a.tangent=p[0].tangent*bary.x+p[1].tangent*bary.y+p[2].tangent*bary.z; a.uv=p[0].uv*bary.x+p[1].uv*bary.y+p[2].uv*bary.z; a.uv1=p[0].uv1*bary.x+p[1].uv1*bary.y+p[2].uv1*bary.z; a.uv2=p[0].uv2*bary.x+p[1].uv2*bary.y+p[2].uv2*bary.z; a.uv3=p[0].uv3*bary.x+p[1].uv3*bary.y+p[2].uv3*bary.z; a.color=p[0].color*bary.x+p[1].color*bary.y+p[2].color*bary.z; UNITY_TRANSFER_INSTANCE_ID(p[0],a); return ").Append(vertex).Append("(a); }\n");
            return b.ToString();
        }
    }
}
