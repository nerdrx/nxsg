using System;

namespace NXSG.Backend
{
    public static class PathingShader
    {
        public const string Hlsl = @"
float2 NXSG_PathDirection(float2 start,float2 end) {
    float2 d=end-start;
    return d*rsqrt(max(dot(d,d),1e-8));
}
float NXSG_PathPosition(float2 uv,float2 start,float2 end) {
    float2 d=end-start;
    return dot(uv-start,d)/max(dot(d,d),1e-8);
}
float4 NXSG_PathChannels(float2 uv,float2 start,float2 end,float time,float audio,float mask,float width,float spacing,float speed,float tail,float travel) {
    float2 d=end-start;
    float lengthSquared=dot(d,d);
    if(lengthSquared<1e-8)return 0;
    float2 axis=d*rsqrt(lengthSquared), normal=float2(-axis.y,axis.x);
    float progress=dot(uv-start,d)/lengthSquared;
    float lateral=dot(uv-start,normal);
    float aa=max(fwidth(lateral)*.5,1e-5);
    float lengthAa=max(fwidth(progress)*.5,1e-5);
    float ends=smoothstep(0,lengthAa,progress)*smoothstep(0,lengthAa,1-progress);
    float4 channels=0;
    [unroll] for(int i=0;i<4;i++) {
        float lane=(float(i)-1.5)*clamp(spacing,0,1);
        float stripe=1-smoothstep(clamp(width,0,.5),clamp(width,0,.5)+aa,abs(lateral-lane));
        float head=frac(time*clamp(speed,-20,20)+float(i)*.25);
        float age=frac(head-progress+1);
        float moving=1-smoothstep(0,max(clamp(tail,.001,1),lengthAa),age);
        channels[i]=stripe*ends*lerp(1,moving,saturate(travel));
    }
    return saturate(channels*max(audio,0)*saturate(mask));
}
float NXSG_PathMask(float2 uv,float2 start,float2 end,float time,float audio,float mask,float width,float spacing,float speed,float tail,float travel) {
    float4 channels=NXSG_PathChannels(uv,start,end,time,audio,mask,width,spacing,speed,tail,travel);
    return max(max(channels.r,channels.g),max(channels.b,channels.a));
}
";

        public static string Expression(string port, string uv, string start, string end,
            string time, string audio, string mask, string width, string spacing, string speed,
            string tail, string travel)
        {
            if (port == "direction") return "NXSG_PathDirection(" + start + "," + end + ")";
            if (port == "phase") return "frac(NXSG_PathPosition(" + uv + "," + start + "," + end + ")-" + time + "*" + speed + ")";
            if (port != "channels" && port != "value") throw new ArgumentException("Unknown Pathing output.", nameof(port));
            var function = port == "channels" ? "NXSG_PathChannels" : "NXSG_PathMask";
            return function + "(" + uv + "," + start + "," + end + "," + time + "," + audio + "," + mask + "," + width + "," + spacing + "," + speed + "," + tail + "," + travel + ")";
        }
    }
}
