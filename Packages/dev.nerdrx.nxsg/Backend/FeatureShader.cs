using System;
using NXSG.Core;

namespace NXSG.Backend
{
    internal static class FeatureShader
    {
        internal static string Body(GraphNode node, string uv, bool vertex,
            Func<string,string,string,string> p, Func<string,double,string> s,
            Func<string,double,string> prop, Func<string,bool,string> sample, string sampler)
        {
            string U() => p("uv",uv,"vector2");
            string T() => p("time","_Time.y","float");
            string Pos() => p("position","input.local","vector3");
            string Center() => "float3("+prop("x",0)+","+prop("y",0)+","+prop("z",0)+")";
            string Pattern(string name,params string[] args) => name+"("+U()+","+string.Join(",",args)+")";
            switch(node.Operation)
            {
                case "core.parallaxUV": return "("+U()+"-NX_ViewTangent(input).xy/max(abs(NX_ViewTangent(input).z),.1)*("+s("height",.5)+"-"+prop("reference",.5)+")*"+prop("strength",.05)+")";
                case "core.parallaxOcclusion": return "NX_Parallax("+sampler+","+sampler+"_ST,"+U()+",NX_ViewTangent(input),"+prop("strength",.05)+","+prop("steps",16)+")";
                case "core.furMask": return Pattern("NX_FurDots",prop("density",100),prop("thickness",.35),prop("height",0),prop("taper",1));
                case "core.flowMapUV": return "("+U()+"+(("+p("flow","float4(.5,.5,0,1)","color")+").rg*2-1)*"+prop("strength",.1)+"*"+T()+"*"+prop("speed",1)+")";
                case "core.ditherMask": return Pattern("NX_Dither",s("value",.5),prop("scale",64));
                case "core.truchet": return Pattern("NX_Truchet",prop("scale",8),prop("width",.08),prop("seed",0));
                case "core.weave": return Pattern("NX_Weave",prop("scale",30),prop("width",.75));
                case "core.scales": return Pattern("NX_Scales",prop("scale",12),prop("width",.06));
                case "core.dots": return "NX_ShapeEdge(length(frac("+U()+"*"+prop("scale",10)+")-.5)-max(0,"+prop("radius",.25)+"),.01)";
                case "core.scratches": return Pattern("NX_Scratches",prop("scale",30),prop("width",.025),prop("length",.7),prop("seed",0));
                case "core.cracks": return Pattern("NX_Cracks",prop("scale",8),prop("width",.04));
                case "core.woodRings": return "(.5+.5*sin((length("+U()+"-.5)*"+prop("scale",12)+"+NX_Noise("+U()+"*7)*"+prop("distortion",.3)+")*6.2831853))";
                case "core.marble": return "(.5+.5*sin(("+U()+").x*"+prop("scale",5)+"*6.2831853+NX_Fbm("+U()+"*"+prop("scale",5)+")*"+prop("distortion",3)+"*6.2831853))";
                case "core.clouds": return "saturate((NX_Fbm("+U()+"*"+prop("scale",4)+"+"+T()+"*"+prop("speed",.1)+")-.5)*"+prop("contrast",1)+"+.5)";
                case "core.sparkleMask": return Pattern("NX_Sparkles",prop("scale",30),T()+"*"+prop("speed",2),prop("density",.2),prop("size",.08));
                case "core.scanlines":
                    var scanCoordinate = "((" + U() + ").y*" + prop("scale",100) + "+" + T() + "*" + prop("speed",.2) + ")";
                    var scanWidth = "saturate(" + prop("width",.3) + ")";
                    if (vertex) return "step(frac(" + scanCoordinate + ")," + scanWidth + ")";
                    return "float width=max(" + scanWidth + ",.0001); float x=" + scanCoordinate + "; float footprint=max(fwidth(x),.0001); float lo=x-footprint*.5; float hi=x+footprint*.5; float loIntegral=floor(lo)*width+min(frac(lo),width); float hiIntegral=floor(hi)*width+min(frac(hi),width); return saturate((hiIntegral-loIntegral)/footprint);";
                case "core.glitchUV": return "("+U()+"+float2((NX_Hash(float2(floor(("+U()+").y*"+prop("rows",20)+"),floor("+T()+"*"+prop("speed",5)+")))*2-1)*"+prop("strength",.05)+",0))";
                case "core.pixelateUV": return "((floor("+U()+"*max(abs("+prop("cells",64)+"),1))+.5)/max(abs("+prop("cells",64)+"),1))";
                case "core.kaleidoscopeUV": return Pattern("NX_Kaleidoscope",prop("segments",6),prop("rotation",0));
                case "core.swapUV": return "("+U()+").yx";
                case "core.spherizeUV": return "float2 q="+U()+"-.5; return .5+q*(1+dot(q,q)*"+prop("strength",1)+"*4);";
                case "core.pinchUV": return "float2 q="+U()+"-.5; float f=1-saturate(length(q)/max(abs("+prop("radius",.5)+"),.00001)); return .5+q*(1-"+prop("strength",.5)+"*f*f);";
                case "core.barrelUV": return "float2 q="+U()+"*2-1; return (q*(1+"+prop("strength",.5)+"*dot(q,q))+1)*.5;";
                case "core.chromaticTexture": return "float2 u="+U()+"; float4 c="+sample("u",vertex)+"; c.r=("+sample("u+float2("+prop("strength",.005)+",0)",vertex)+").r; c.b=("+sample("u-float2("+prop("strength",.005)+",0)",vertex)+").b; return c;";
                case "core.normalBlend": return "float3 a="+p("a","float3(0,0,1)","vector3")+"; float3 b="+p("b","float3(0,0,1)","vector3")+"; return NX_SafeNormal(float3(a.xy+b.xy,a.z*b.z));";
                case "core.normalStrength": return "float3 n="+p("normal","float3(0,0,1)","vector3")+"; return NX_SafeNormal(float3(n.xy*"+s("strength",1)+",n.z));";
                case "core.normalFromHeight":
                    if(vertex) throw new InvalidOperationException("Normal from Height uses pixel derivatives; connect it to a surface Normal input, not vertex motion.");
                    return "NX_HeightNormal("+s("height",0)+",input.uv,"+prop("strength",1)+")";
                case "core.reflectionDirection": return "reflect(-NX_SafeNormal(_WorldSpaceCameraPos-input.ws),NX_SafeNormal("+p("normal","input.n","vector3")+"))";
                case "core.objectScale": return "float3(length(mul((float3x3)unity_ObjectToWorld,float3(1,0,0))),length(mul((float3x3)unity_ObjectToWorld,float3(0,1,0))),length(mul((float3x3)unity_ObjectToWorld,float3(0,0,1))))";
                case "core.objectOrigin": return "mul(unity_ObjectToWorld,float4(0,0,0,1)).xyz";
                case "core.objectRandom": return "NX_Hash(mul(unity_ObjectToWorld,float4(0,0,0,1)).xz+mul(unity_ObjectToWorld,float4(0,0,0,1)).y*17.3+"+prop("seed",0)+")";
                case "core.distanceToPoint": return "length("+Pos()+"-"+Center()+")";
                case "core.sphereMask": return "NX_ShapeEdge(length("+Pos()+"-"+Center()+")-"+prop("radius",.5)+","+prop("softness",.05)+")";
                case "core.boxVolumeMask": return "float3 q=abs("+Pos()+"-"+Center()+")-float3("+prop("width",1)+","+prop("height",1)+","+prop("depth",1)+")*.5; return NX_ShapeEdge(max(q.x,max(q.y,q.z)),"+prop("softness",.05)+");";
                case "core.capsuleMask": return "float3 q="+Pos()+"-"+Center()+"; q.y-=clamp(q.y,-abs("+prop("height",1)+")*.5,abs("+prop("height",1)+")*.5); return NX_ShapeEdge(length(q)-"+prop("radius",.2)+","+prop("softness",.05)+");";
                case "core.stripes3D": return "step(frac(("+Pos()+")["+prop("axis",1)+"]*"+prop("scale",10)+"),saturate("+prop("width",.5)+"))";
                case "core.snowMask": return "saturate((NX_SafeNormal("+p("normal","input.n","vector3")+").y-(1-"+prop("coverage",.5)+")+(NX_Noise(("+p("position","input.ws","vector3")+").xz*"+prop("scale",10)+")-.5)*"+prop("breakup",.3)+")*5+.5)";
                case "core.wetnessColor": return "float4 c="+p("color","float4(.5,.5,.5,1)","color")+"; c.rgb*=1-saturate("+s("mask",1)+")*saturate("+prop("strength",.5)+")*.7; return c;";
                case "core.anisotropicHighlight": return "NX_Aniso(input,"+p("normal","input.n","vector3")+","+p("tangent","input.tangent","vector3")+","+p("color","float4(1,1,1,1)","color")+","+s("roughness",.3)+")";
                case "core.iridescence": return "float4 base="+p("color","float4(1,1,1,1)","color")+"; float ndv=saturate(dot(NX_SafeNormal("+p("normal","input.n","vector3")+"),NX_SafeNormal(_WorldSpaceCameraPos-input.ws))); float film=saturate("+s("thickness",.5)+")*6.2831853+"+prop("phase",0)+"; float3 shift=float3(.5+.5*cos(film+ndv*5.2),.5+.5*cos(film+ndv*5.2+2.094),.5+.5*cos(film+ndv*5.2+4.188)); return float4(base.rgb*lerp(1,shift,"+prop("strength",1)+"),base.a);";
                case "core.refraction":
                    if (vertex) throw new InvalidOperationException("Refraction uses GrabPass screen pixels; connect it to a surface color, not vertex motion.");
                    return "float4 base="+p("color","float4(1,1,1,1)","color")+"; float3 n=NX_SafeNormal(mul((float3x3)UNITY_MATRIX_V,"+p("normal","input.n","vector3")+")); float3 v=NX_SafeNormal(mul((float3x3)UNITY_MATRIX_V,_WorldSpaceCameraPos-input.ws)); float eta=1/max("+s("ior",1.33)+",1.0); float3 incident=-v; float3 refracted=refract(incident,n,eta); float2 oldRay=incident.xy/max(abs(incident.z),.00001); float2 newRay=refracted.xy/max(abs(refracted.z),.00001); float2 su=input.screenPos.xy/max(input.screenPos.w,.00001); return base*tex2D(_NXSG_GrabTexture,su+(newRay-oldRay)*"+s("strength",.05)+");";
                case "core.interiorMapping": return "float2 uv="+p("uv",uv,"vector2")+"; float3 view=NX_SafeNormal("+p("view","NX_ViewTangent(input)","vector3")+"); float2 rooms=float2(max(abs("+prop("roomsX",4)+"),1),max(abs("+prop("roomsY",4)+"),1)); float2 cell=floor(uv*rooms); float2 local=frac(uv*rooms); float3 origin=float3(local*2-1,0); float3 ray=NX_SafeNormal(float3(-view.xy,-max(abs(view.z),.0001))); float depth=max(abs("+s("depth",1)+"),.0001); float3 bound=float3(1,1,depth); float3 safeRay=max(abs(ray),.00001)*lerp(-1,1,step(0,ray)); float3 t=(bound-origin)/safeRay; float3 tn=(-bound-origin)/safeRay; float3 hit=max(t,tn); float enter=min(hit.x,min(hit.y,hit.z)); float3 roomHit=origin+ray*max(enter,0); float2 wall=abs(roomHit.z+depth)<.01?roomHit.xy*.5+.5:(abs(roomHit.x)>abs(roomHit.y)?float2(roomHit.z/depth*.5+.5,roomHit.y*.5+.5):float2(roomHit.x*.5+.5,roomHit.z/depth*.5+.5)); float2 roomUV=(cell+frac(wall))/(rooms); return "+sample("roomUV",vertex)+";";
                case "core.textureBomb":
                    var bombSeed=prop("seed",0); var bombRotation=prop("rotation",1);
                    Func<string,string> bomb=cell=>sample("NX_BombUV(q,cells,"+bombSeed+","+bombRotation+","+cell+")",vertex);
                    return "float2 q="+U()+"; float cells=max(abs("+prop("cells",4)+"),1); float2 id=floor(q*cells); float2 f=frac(q*cells); float2 w=f*f*(3-2*f); float4 a="+bomb("id")+"; float4 b="+bomb("id+float2(1,0)")+"; float4 c="+bomb("id+float2(0,1)")+"; float4 d="+bomb("id+float2(1,1)")+"; return lerp("+sample("q*cells",vertex)+",lerp(lerp(a,b,w.x),lerp(c,d,w.x),w.y),saturate("+s("blend",1)+"));";
                case "core.subsurface": return "float4 base="+p("color","float4(1,1,1,1)","color")+"; float3 n=NX_SafeNormal("+p("normal","input.n","vector3")+"); float3 l=NX_SafeNormal(UnityWorldSpaceLightDir(input.ws)); float thickness=saturate("+s("thickness",.5)+"); float wrap=saturate((dot(n,l)+thickness)/max(1+thickness,.0001)); float back=saturate(dot(-n,l)); float3 tint="+p("tint","float4(1,.35,.2,1)","color")+".rgb; float3 light=_LightColor0.rgb; return float4(base.rgb*(1+"+prop("strength",.7)+"*(wrap*.65+back*.35)*tint*light),base.a);";
                default: throw new InvalidOperationException("Unsupported feature operation: "+node.Operation);
            }
        }
        internal const string Helpers=@"
float3 NX_SafeNormal(float3 n){return dot(n,n)>.0000001?normalize(n):float3(0,0,1);}
float3 NX_ViewTangent(NXInput input){float3 v=NX_SafeNormal(_WorldSpaceCameraPos-input.ws);return float3(dot(v,NX_SafeNormal(input.tangent)),dot(v,NX_SafeNormal(input.bitangent)),dot(v,NX_SafeNormal(input.n)));}
float2 NX_Parallax(sampler2D tex,float4 st,float2 uv,float3 view,float strength,int steps){steps=clamp(steps,4,64);float2 delta=-view.xy/max(abs(view.z),.1)*strength/steps;float2 previous=uv;float depth=0;float sampled=1-tex2Dlod(tex,float4(uv*st.xy+st.zw,0,0)).r;float previousDifference=sampled;for(int i=0;i<64;i++){if(i>=steps||depth>=sampled)break;previous=uv;previousDifference=sampled-depth;uv+=delta;depth+=1.0/steps;sampled=1-tex2Dlod(tex,float4(uv*st.xy+st.zw,0,0)).r;}float difference=depth-sampled;return lerp(uv,previous,saturate(difference/max(difference+previousDifference,.00001)));}
float NX_FurDots(float2 uv,float density,float thickness,float height,float taper){float2 p=uv*max(abs(density),.001);float2 id=floor(p);float2 center=float2(NX_Hash(id),NX_Hash(id+31.7))*.5+.25;float radius=max(0,thickness)*.5*pow(saturate(1-height),max(.001,taper));return NX_ShapeEdge(length(frac(p)-center)-radius,.01);}
float NX_Dither(float2 uv,float value,float scale){float2 p=floor(uv*max(abs(scale),1));float2 lo=fmod(abs(p),2);float2 hi=fmod(floor(abs(p)/2),2);float low=lo.x*2+lo.y*3-4*lo.x*lo.y;float high=hi.x*2+hi.y*3-4*hi.x*hi.y;return step((4*low+high+.5)/16,saturate(value));}
float NX_Truchet(float2 uv,float scale,float width,float seed){float2 p=uv*scale;float2 q=frac(p);if(NX_Hash(floor(p)+seed)>.5)q.x=1-q.x;float d=min(abs(length(q)-.5),abs(length(q-1)-.5));return NX_ShapeEdge(d-max(0,width)*.5,.008);}
float NX_Weave(float2 uv,float scale,float width){float2 p=uv*scale;float2 q=frac(p);float2 band=step(abs(q-.5),saturate(width)*.5);float alternate=fmod(floor(p.x)+floor(p.y),2);float over=lerp(band.x,band.y,alternate);float shade=lerp(sin(q.x*3.14159265),sin(q.y*3.14159265),alternate);return max(band.x*band.y*.2,over*(.3+.7*shade));}
float NX_Scales(float2 uv,float scale,float width){float2 p=uv*scale;float row=floor(p.y);p.x+=row*.5;float2 q=frac(p)-float2(.5,0);float d=abs(length(q*float2(1, .65))-.5);return NX_ShapeEdge(d-max(0,width)*.5,.008);}
float NX_Scratches(float2 uv,float scale,float width,float len,float seed){float2 p=uv*scale;float2 id=floor(p);float2 q=frac(p);float x=NX_Hash(id+seed)*.7+.15;float present=step(.6,NX_Hash(id+seed+13));return present*NX_ShapeEdge(abs(q.x-x)-max(0,width),.005)*NX_ShapeEdge(abs(q.y-.5)-saturate(len)*.5,.02);}
float NX_Cracks(float2 uv,float scale,float width){float2 p=uv*scale;float2 id=floor(p);float2 f=frac(p);float first=100,second=100;for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){float2 cell=float2(x,y);float2 cellPoint=float2(NX_Hash(id+cell),NX_Hash(id+cell+17.7));float d=length(cell+cellPoint-f);if(d<first){second=first;first=d;}else second=min(second,d);}return NX_ShapeEdge(second-first-max(0,width),.01);}
float NX_Fbm(float2 p){return (NX_Noise(p)*.5+NX_Noise(p*2.03+17)*.25+NX_Noise(p*4.07+31)*.125+NX_Noise(p*8.11+53)*.0625)/.9375;}
float NX_Sparkles(float2 uv,float scale,float time,float density,float size){float2 p=uv*scale;float2 id=floor(p);float2 q=frac(p)-.5;float phase=NX_Hash(id+31.3);float pulse=pow(saturate(sin(time+phase*6.2831853)),8);float shape=NX_ShapeEdge(length(q)-max(0,size),.01);return shape*pulse*step(NX_Hash(id),saturate(density));}
float2 NX_Kaleidoscope(float2 uv,float segments,float rotation){float2 q=uv-.5;float sector=6.2831853/max(1,segments);float a=atan2(q.y,q.x)+radians(rotation);a=abs((frac(a/sector+.5)-.5)*sector);return .5+length(q)*float2(cos(a),sin(a));}
float3 NX_HeightNormal(float height,float2 uv,float strength){float2 dx=ddx(uv),dy=ddy(uv);float det=dx.x*dy.y-dx.y*dy.x;float safe=(det<0?-1:1)*max(abs(det),.00000001);float hu=(ddx(height)*dy.y-ddy(height)*dx.y)/safe;float hv=(ddy(height)*dx.x-ddx(height)*dy.x)/safe;return NX_SafeNormal(float3(-hu*strength,-hv*strength,1));}
float4 NX_Aniso(NXInput input,float3 normal,float3 tangent,float4 color,float roughness){float3 n=NX_SafeNormal(normal);float3 t=NX_SafeNormal(tangent);float3 l=NX_SafeNormal(UnityWorldSpaceLightDir(input.ws));float3 h=NX_SafeNormal(l+NX_SafeNormal(_WorldSpaceCameraPos-input.ws));float spec=pow(saturate(1-pow(dot(t,h),2)),2/max(roughness*roughness,.001))*saturate(dot(n,l));return float4(color.rgb*spec*_LightColor0.rgb,color.a);}
float2 NX_BombUV(float2 uv,float cells,float seed,float rotate,float2 id){float2 q=uv*cells-id-.5;float h=NX_Hash(id+seed);float a=(h-.5)*rotate*6.2831853;float2 r=float2(cos(a)*q.x-sin(a)*q.y,sin(a)*q.x+cos(a)*q.y);float2 off=float2(NX_Hash(id+17.1+seed),NX_Hash(id+43.7+seed));return r+off+.5;}
";
    }
}
