using System.Text;
using System.Globalization;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        const string LightVolumeGraphHelper = @"
float4 NX_LightVolumeGraph(NXInput input,float4 albedo,float3 normal,float roughness,float metallic,float strength,int outputMode) {
#if defined(UNITY_PASS_FORWARDADD) || defined(UNITY_PASS_SHADOWCASTER)
return float4(0,0,0,1);
#else
float3 diffuse,specular;
NX_LightVolumes(input.ws,normal,normalize(_WorldSpaceCameraPos-input.ws),albedo.rgb,roughness,metallic,diffuse,specular);
return float4((outputMode==1?diffuse:outputMode==2?specular:diffuse+specular)*max(0,strength),1);
#endif
}";
        string ToonLighting(GraphNode surface, int passIndex, bool additional)
        {
            var b = new StringBuilder();
            var mode = IntProp(surface,"lightingMode",0,0,3);
            var strength = ToonSetting(surface,"shadowStrength",passIndex);
            var threshold = ToonSetting(surface,"threshold",passIndex);
            var softness = ToonSetting(surface,"softness",passIndex);
            if (mode == 3)
            {
                AppendLayeredToonResponse(b, surface, passIndex);
            }
            else
            {
                b.AppendLine("float lightCoordinate=saturate(dot(n,lightDir)*.5+.5+"+Scalar(surface,"shadeMap",.5)+"-.5);");
                if (mode == 2)
                    b.AppendLine("float3 toonResponse=lerp(float3(1,1,1),tex2D("+textureNames[(string)surface.Properties["resourceId"]]+",float2(lightCoordinate,saturate("+Prop(surface,"rampRow",.5)+"))).rgb,saturate("+strength+"));");
                else
                {
                    if (mode == 1)
                        b.AppendLine("float bands="+IntProp(surface,"bands",3,2,8)+"-1; float scaled=lightCoordinate*bands; float lit=saturate((floor(scaled)+smoothstep(.5-max(.001,"+softness+"),.5+max(.001,"+softness+"),frac(scaled)))/bands);");
                    else b.AppendLine("float lit=smoothstep("+threshold+"-max(.001,"+softness+"),"+threshold+"+max(.001,"+softness+"),lightCoordinate);");
                    b.AppendLine("float3 toonResponse=lerp(lerp(float3(1,1,1),("+Input(surface,"shadeColor",ColorProp(surface,"shadeColor",0,0,0,1),"color")+").rgb,saturate("+strength+")),float3(1,1,1),lit);");
                }
            }
            b.AppendLine("float3 direct=_LightColor0.rgb*atten*toonResponse;");
            if (HasLightingControls(surface)) b.AppendLine("direct=NX_LightingContribution(direct,"+Prop(surface,"lightingSaturation",1)+","+Prop(surface,"lightingMax",0)+");");
            if (additional) b.AppendLine("return float4(c.rgb*direct,alpha);");
            else
            {
                b.AppendLine("float3 ambient=max(0,ShadeSH9(float4(n,1)))*saturate("+Scalar(surface,"occlusion",1)+");");
                if (HasLightingControls(surface))
                    b.AppendLine("ambient=NX_LightingContribution(ambient,"+Prop(surface,"lightingSaturation",1)+","+Prop(surface,"lightingMax",0)+"); return float4(c.rgb*NX_LightingBase(ambient+direct,"+Prop(surface,"lightingMin",0)+","+Prop(surface,"lightingMax",0)+")+emission,alpha);");
                else b.AppendLine("return float4(c.rgb*(ambient+direct)+emission,alpha);");
            }
            return b.ToString();
        }

        void AppendLayeredToonResponse(StringBuilder b, GraphNode surface, int passIndex)
        {
            var layerCount = IntProp(surface, "shadowLayers", 3, 1, 3);
            b.AppendLine("float3 toonResponse=float3(1,1,1);");
            for (var layer = 1; layer <= layerCount; layer++)
            {
                var suffix = layer == 1 ? string.Empty : layer.ToString(CultureInfo.InvariantCulture);
                var strengthPort = "shadowStrength" + suffix;
                var authoredStrength = surface.Properties[strengthPort];
                if (layer > 1 && Source(surface, strengthPort) == null && authoredStrength != null && (double)authoredStrength == 0)
                    continue;

                var layerNormalStrength = Scalar(surface, "normalStrength" + suffix, 1);
                var normal = "toonLayerNormal" + layer;
                var normalCandidate = "toonLayerNormalCandidate" + layer;
                b.AppendLine("float3 toonGeomRaw" + layer + "=input.n; float toonGeomLengthSq" + layer + "=dot(toonGeomRaw" + layer + ",toonGeomRaw" + layer + "); float3 toonGeomNormal" + layer + "=toonGeomLengthSq" + layer + ">1e-8?toonGeomRaw" + layer + "*rsqrt(toonGeomLengthSq" + layer + "):float3(0,0,1); float3 toonMappedRaw" + layer + "=n; float toonMappedLengthSq" + layer + "=dot(toonMappedRaw" + layer + ",toonMappedRaw" + layer + "); float3 toonMappedNormal" + layer + "=toonMappedLengthSq" + layer + ">1e-8?toonMappedRaw" + layer + "*rsqrt(toonMappedLengthSq" + layer + "):toonGeomNormal" + layer + "; float3 " + normalCandidate + "=lerp(toonGeomNormal" + layer + ",toonMappedNormal" + layer + ",saturate(" + layerNormalStrength + ")); float " + normal + "LengthSq=dot(" + normalCandidate + "," + normalCandidate + "); float3 " + normal + "=" + normal + "LengthSq>1e-8?" + normalCandidate + "*rsqrt(" + normal + "LengthSq):toonGeomNormal" + layer + ";");

                var shadeMap = Scalar(surface, "shadeMap" + suffix, .5);
                var threshold = layer == 1 ? ToonSetting(surface, "threshold", passIndex) : Scalar(surface, "threshold" + suffix, layer == 2 ? .35 : .2);
                var softness = layer == 1 ? ToonSetting(surface, "softness", passIndex) : Scalar(surface, "softness" + suffix, .05);
                b.AppendLine("float toonLayer" + layer + "Coordinate=saturate(dot(" + normal + ",lightDir)*.5+.5+" + shadeMap + "-.5); float toonLayer" + layer + "Lit=smoothstep(" + threshold + "-max(.001," + softness + ")," + threshold + "+max(.001," + softness + "),toonLayer" + layer + "Coordinate);");
                var red = layer == 1 ? 0 : layer == 2 ? .35 : .08;
                var green = layer == 1 ? 0 : layer == 2 ? .25 : .04;
                var blue = layer == 1 ? 0 : layer == 2 ? .5 : .15;
                var shadeColor = Input(surface, "shadeColor" + suffix, ColorProp(surface, "shadeColor" + suffix, red, green, blue, 1), "color");
                var strength = layer == 1 ? ToonSetting(surface, "shadowStrength", passIndex) : Scalar(surface, strengthPort, 1);
                b.AppendLine("toonResponse=lerp(toonResponse,(" + shadeColor + ").rgb,(1-toonLayer" + layer + "Lit)*saturate(" + strength + ")); ");
            }
        }

    }
}
