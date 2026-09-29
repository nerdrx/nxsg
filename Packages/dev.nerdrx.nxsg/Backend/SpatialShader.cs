using System;
using System.Globalization;
using System.Text;

namespace NXSG.Backend
{
    // Small text generators kept separate so every vertex pass can share the same deformation math.
    public static class SpatialShader
    {
        public static string TransformFunction(string functionName, int space, int shape)
        {
            if (space < 0 || space > 1) throw new ArgumentOutOfRangeException(nameof(space));
            if (shape < 0 || shape > 2) throw new ArgumentOutOfRangeException(nameof(shape));
            return TransformFunction(functionName, space == 0 ? "local" : "world", shape == 0 ? "none" : shape == 1 ? "sphere" : "cylinder");
        }

        public static string TransformFunction(string functionName, string space, string shape)
        {
            if (space != "local" && space != "world") throw new ArgumentException("Unsupported vertex deformation space.", nameof(space));
            if (shape != "none" && shape != "sphere" && shape != "cylinder") throw new ArgumentException("Unsupported vertex deformation shape.", nameof(shape));
            var world = space == "world";
            var positionIn = world ? "mul(unity_ObjectToWorld,float4(p,1)).xyz" : "p";
            var normalIn = world ? "UnityObjectToWorldNormal(n)" : "n";
            var tangentIn = world ? "UnityObjectToWorldDir(t.xyz)" : "t.xyz";
            var positionOut = world ? "mul(unity_WorldToObject,float4(q,1)).xyz" : "q";
            var normalOut = world ? "mul(nn,(float3x3)unity_ObjectToWorld)" : "nn";
            var tangentOut = world ? "UnityWorldToObjectDir(tt)" : "tt";
            var resultType = functionName + "Result";
            return @"
struct " + resultType + @" { float3 position; float3 normal; float4 tangent; };
float3 " + functionName + @"Rotate(float3 q,float3 rotation)
{
    float3 r=radians(rotation),c=cos(r),s=sin(r);
    return float3(q.x*(c.y*c.z)+q.y*(s.x*s.y*c.z-c.x*s.z)+q.z*(c.x*s.y*c.z+s.x*s.z),
                  q.x*(c.y*s.z)+q.y*(s.x*s.y*s.z+c.x*c.z)+q.z*(c.x*s.y*s.z-s.x*c.z),
                  -q.x*s.y+q.y*s.x*c.y+q.z*c.x*c.y);
}
float3 " + functionName + @"Linear(float3 v,float3 rotation,float3 scale) { return " + functionName + @"Rotate(v*scale,rotation); }
float3 " + functionName + @"Map(float3 p,float3 translation,float3 rotation,float3 scale,float3 pivot,float snap,float warp,float nearDistance,float nearStrength)
{
    float3 q=" + functionName + @"Linear(p-pivot,rotation,scale);
    q+=pivot+translation;
    float3 radial=q-pivot;
    if (" + (shape == "sphere" ? "true" : "false") + @") {
        float radius=max(abs(radial.x),max(abs(radial.y),abs(radial.z)));
        float len=length(radial); q=lerp(q,pivot+(len>1e-8?radial/len:float3(0,1,0))*radius,saturate(warp));
    }
    if (" + (shape == "cylinder" ? "true" : "false") + @") {
        float radius=max(abs(radial.x),abs(radial.z)); float len=length(radial.xz);
        float2 curved=len>1e-8?radial.xz/len*radius:float2(radius,0);
        q.xz=lerp(q.xz,pivot.xz+curved,saturate(warp));
    }
    if (snap>1e-6) q=round(q/snap)*snap;
    if (nearDistance>0 && nearStrength>0) {
        float3 worldPos=" + (world ? "q" : "mul(unity_ObjectToWorld,float4(q,1)).xyz") + @";
        float3 forward=-normalize(float3(UNITY_MATRIX_I_V._m02,UNITY_MATRIX_I_V._m12,UNITY_MATRIX_I_V._m22));
        float cameraDepth=dot(worldPos-_WorldSpaceCameraPos,forward);
        worldPos+=forward*max(0,nearDistance-cameraDepth)*saturate(nearStrength);
        q=" + (world ? "worldPos" : "mul(unity_WorldToObject,float4(worldPos,1)).xyz") + @";
    }
    return q;
}
" + resultType + " " + functionName + @"(float3 p,float3 n,float4 t,float3 translation,float3 rotation,float3 scale,float3 pivot,float mask,float snap,float warp,float nearDistance,float nearStrength)
{
    " + resultType + " o; float3 source=" + positionIn + @"; float3 rawN=" + normalIn + @"; float3 sourceN=dot(rawN,rawN)>1e-10?normalize(rawN):float3(0,1,0);
    float3 rawT=" + tangentIn + @"; float3 tangentPlane=rawT-sourceN*dot(sourceN,rawT); float3 axis=abs(sourceN.y)<.99?float3(0,1,0):float3(1,0,0);
    float3 sourceT=dot(tangentPlane,tangentPlane)>1e-10?normalize(tangentPlane):normalize(cross(axis,sourceN));
    float3 moved=" + functionName + @"Map(source,translation,rotation,scale,pivot,snap,warp,nearDistance,nearStrength);
    float3 jx=" + functionName + @"Linear(float3(1,0,0),rotation,scale),jy=" + functionName + @"Linear(float3(0,1,0),rotation,scale),jz=" + functionName + @"Linear(float3(0,0,1),rotation,scale);
    if (" + (shape == "none" ? "snap>1e-6" : "warp>1e-6 || snap>1e-6") + @" || nearDistance>0) {
        float e=0.0005;
        jx=(" + functionName + @"Map(source+float3(e,0,0),translation,rotation,scale,pivot,snap,warp,nearDistance,nearStrength)-moved)/e;
        jy=(" + functionName + @"Map(source+float3(0,e,0),translation,rotation,scale,pivot,snap,warp,nearDistance,nearStrength)-moved)/e;
        jz=(" + functionName + @"Map(source+float3(0,0,e),translation,rotation,scale,pivot,snap,warp,nearDistance,nearStrength)-moved)/e;
    }
    jx=lerp(float3(1,0,0),jx,saturate(mask)); jy=lerp(float3(0,1,0),jy,saturate(mask)); jz=lerp(float3(0,0,1),jz,saturate(mask));
    float3 transformedN=cross(jy,jz)*sourceN.x+cross(jz,jx)*sourceN.y+cross(jx,jy)*sourceN.z;
    float3 transformedT=jx*sourceT.x+jy*sourceT.y+jz*sourceT.z;
    float det=dot(jx,cross(jy,jz)); transformedN*=det<0?-1:1;
    float3 fullN=dot(transformedN,transformedN)>1e-10?normalize(transformedN):sourceN;
    float3 fullT=dot(transformedT,transformedT)>1e-10?normalize(transformedT):sourceT;
    float3 q=lerp(source,moved,saturate(mask)); float3 nn=fullN;
    float3 tt=fullT; tt-=nn*dot(nn,tt); tt=dot(tt,tt)>1e-10?normalize(tt):sourceT;
    float3 outN=" + normalOut + @"; float3 outT=" + tangentOut + @"; outN=dot(outN,outN)>1e-10?normalize(outN):float3(0,1,0);
    outT-=outN*dot(outN,outT); float3 outAxis=abs(outN.y)<.99?float3(0,1,0):float3(1,0,0); outT=dot(outT,outT)>1e-10?normalize(outT):normalize(cross(outAxis,outN));
    o.position=" + positionOut + @"; o.normal=outN; o.tangent=float4(outT,t.w*(det<0?-1:1)); return o;
}";
        }

        public static string TransformCall(string functionName, string position, string normal, string tangent,
            string translation, string rotation, string scale, string pivot, string mask, string snap, string warp, string nearDistance = "0", string nearStrength = "1")
        {
            return functionName + "(" + string.Join(",", new[] { position, normal, tangent, translation, rotation, scale, pivot, mask, snap, warp, nearDistance, nearStrength }) + ")";
        }

        public static string InfinityParallaxBody(string sampler, string uv, string view, string height,
            string depth, string strength, string tint, string fade, string mask, int steps, int blend = 0)
        {
            if (string.IsNullOrWhiteSpace(sampler)) throw new ArgumentException("Texture sampler is required.", nameof(sampler));
            if (steps < 1 || steps > 32) throw new ArgumentOutOfRangeException(nameof(steps), "Parallax layers must be between 1 and 32.");
            if (blend < 0 || blend > 2) throw new ArgumentOutOfRangeException(nameof(blend), "Blend mode must be composite, additive, or max.");
            var code = new StringBuilder();
            code.Append("float2 nxUv=").Append(uv).Append("; float3 nxRawView=").Append(view).Append("; float nxViewLengthSq=dot(nxRawView,nxRawView); float3 nxView=nxViewLengthSq>1e-10?nxRawView*rsqrt(nxViewLengthSq):float3(0,0,1); ");
            code.Append("float nxDepth=max(0,").Append(depth).Append("); float nxHeight=saturate(").Append(height).Append("); ");
            code.Append("float2 nxRay=nxView.xy/max(.15,abs(nxView.z))*max(0,").Append(strength).Append(")*nxDepth; ");
            code.Append("float4 nxAccum=0; float nxWeight=0; float nxMask=saturate(").Append(mask).Append("); ");
            code.Append("[unroll] for(int nxI=0;nxI<").Append(steps.ToString(CultureInfo.InvariantCulture)).Append(";nxI++){ ");
            code.Append("float nxT=((float)nxI+.5)/").Append(steps.ToString(CultureInfo.InvariantCulture)).Append("; float nxLayer=saturate(nxT+(nxHeight-.5)*.5); ");
            code.Append("float4 nxSample=tex2D(").Append(sampler).Append(",nxUv+nxRay*nxLayer); float nxW=pow(saturate(1-nxT),max(.001,").Append(fade).Append(")); ");
            code.Append("float3 nxTint=lerp(float3(1,1,1),(").Append(tint).Append(").rgb,nxT); ");
            if (blend == 0) code.Append("float nxA=saturate(nxSample.a*nxW); nxAccum.rgb+=(1-nxAccum.a)*nxSample.rgb*nxTint*nxA; nxAccum.a+=(1-nxAccum.a)*nxA; ");
            else if (blend == 1) code.Append("nxAccum.rgb+=nxSample.rgb*nxTint*nxW*nxSample.a; nxAccum.a=max(nxAccum.a,nxSample.a*nxW); ");
            else code.Append("nxAccum=max(nxAccum,float4(nxSample.rgb*nxTint*nxSample.a,nxSample.a)*nxW); ");
            code.Append("nxWeight+=nxW; } ");
            code.Append("float4 nxFront=tex2D(").Append(sampler).Append(",nxUv); float4 nxInside=nxWeight>1e-6?float4(nxAccum.rgb/max(1e-6,nxAccum.a),nxAccum.a):nxFront; ");
            code.Append("return float4(lerp(nxFront.rgb,nxInside.rgb,nxMask),lerp(nxFront.a,nxInside.a,nxMask));");
            return code.ToString();
        }
    }
}
