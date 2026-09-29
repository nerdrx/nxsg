using System;

namespace NXSG.Backend
{
    public static class ConstellationShader
    {
        // Fixed 3x3 neighborhood keeps links continuous across UV-cell boundaries.
        public const string Hlsl = @"
float NXSG_ConstHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
float2 NXSG_ConstStar(float2 cell,float seed) {
    return cell + .5 + .32*(float2(NXSG_ConstHash(cell+seed+17.3),NXSG_ConstHash(cell+seed+91.7))*2-1);
}
float NXSG_ConstLine(float2 p,float2 a,float2 b,float width,float aa) {
    float2 ab=b-a;
    float t=saturate(dot(p-a,ab)/max(dot(ab,ab),1e-5));
    return 1-smoothstep(width,width+aa,length(p-a-ab*t));
}
float3 NXSG_Constellation(float2 uv,float time,float audio,float scale,float pointSize,float lineWidth,float linkChance,float twinkle,float seed) {
    float2 p=uv*clamp(scale,1,64), cell=floor(p);
    float aa=max(max(length(ddx(p)),length(ddy(p)))*.5,.001);
    float pointMask=0,lineMask=0;
    float radius=clamp(pointSize,0,.5), width=clamp(lineWidth,0,.25), chance=saturate(linkChance);
    [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++) {
        float2 id=cell+float2(x,y), a=NXSG_ConstStar(id,seed);
        float pulse=lerp(1,.6+.4*sin(time+NXSG_ConstHash(id+seed+43.1)*6.2831853),saturate(twinkle));
        pointMask=max(pointMask,(1-smoothstep(radius,radius+aa,length(p-a)))*pulse);
        float2 right=id+float2(1,0), up=id+float2(0,1);
        float horizontal=step(NXSG_ConstHash(id+seed+207.3),chance);
        float vertical=step(NXSG_ConstHash(id+seed+503.9),chance);
        lineMask=max(lineMask,NXSG_ConstLine(p,a,NXSG_ConstStar(right,seed),width,aa)*horizontal);
        lineMask=max(lineMask,NXSG_ConstLine(p,a,NXSG_ConstStar(up,seed),width,aa)*vertical);
    }
    float2 result=saturate(float2(pointMask,lineMask)*max(audio,0));
    return float3(result.x,result.y,max(result.x,result.y));
}
";

        public static string Expression(string port, string uv, string time, string audio, string scale,
            string pointSize, string lineWidth, string linkChance, string twinkle, string seed)
        {
            if (port != "points" && port != "lines" && port != "mask")
                throw new ArgumentException("Unknown Constellation output.", nameof(port));
            var call = "NXSG_Constellation(" + uv + "," + time + "," + audio + "," + scale + "," +
                pointSize + "," + lineWidth + "," + linkChance + "," + twinkle + "," + seed + ")";
            return call + (port == "points" ? ".x" : port == "lines" ? ".y" : ".z");
        }
    }
}
