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
            var perLayerSceneShadow = mode == 3 && HasLayerShadowOverride(surface);
            if (mode == 3)
            {
                if (perLayerSceneShadow)
                    b.AppendLine("float nxToonGlobalScene=lerp(1,saturate(nxSceneShadow),saturate("+Scalar(surface,"receiveShadow",1)+"));");
                AppendLayeredToonResponse(b, surface, passIndex, perLayerSceneShadow);
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
                    AppendToonBorder(b, surface, mode == 1 ? "frac(lightCoordinate*bands)" : "lightCoordinate", mode == 1 ? ".5" : threshold);
                }
            }
            AppendToonRim(b, surface, perLayerSceneShadow ? "nxToonGlobalScene" : null);
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

        void AppendLayeredToonResponse(StringBuilder b, GraphNode surface, int passIndex, bool perLayerSceneShadow)
        {
            var layerCount = IntProp(surface, "shadowLayers", 3, 1, 3);
            b.AppendLine(perLayerSceneShadow ? "float3 toonResponse=float3(1,1,1)*nxToonGlobalScene;" : "float3 toonResponse=float3(1,1,1);");
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
                if (perLayerSceneShadow)
                {
                    var layerReceive = Scalar(surface, "layerReceiveShadow" + (layer == 1 ? string.Empty : suffix), 1);
                    var layerTarget = "toonLayer" + layer + "ShadeTarget";
                    b.AppendLine("float3 " + layerTarget + "=(" + shadeColor + ").rgb*lerp(1,saturate(nxSceneShadow),saturate(" + Scalar(surface, "receiveShadow", 1) + ")*saturate(" + layerReceive + ")); toonResponse=lerp(toonResponse," + layerTarget + ",(1-toonLayer" + layer + "Lit)*saturate(" + strength + ")); ");
                }
                else b.AppendLine("toonResponse=lerp(toonResponse,(" + shadeColor + ").rgb,(1-toonLayer" + layer + "Lit)*saturate(" + strength + ")); ");
                AppendToonBorder(b, surface, "toonLayer" + layer + "Coordinate", threshold, perLayerSceneShadow ? "nxToonGlobalScene" : null);
            }
        }

        void AppendToonBorder(StringBuilder b, GraphNode surface, string coordinate, string threshold, string sceneFactor = null)
        {
            if (Source(surface,"borderStrength") == null && (double?)surface.Properties["borderStrength"] != null && (double)surface.Properties["borderStrength"] == 0) return;
            if (Source(surface,"borderStrength") == null && surface.Properties["borderStrength"] == null) return;
            var strength = Scalar(surface,"borderStrength",0);
            var width = Scalar(surface,"borderWidth",.05);
            var color = Input(surface,"borderColor",ColorProp(surface,"borderColor",1,.3,.15,1),"color");
            var target = "(" + color + ").rgb" + (sceneFactor == null ? string.Empty : "*" + sceneFactor);
            b.AppendLine("toonResponse=lerp(toonResponse,"+target+",(1-smoothstep(0,max(.0001,"+width+"),abs("+coordinate+"-("+threshold+"))))*saturate("+strength+"));");
        }

        void AppendToonRim(StringBuilder b, GraphNode surface, string sceneFactor)
        {
            var amount = Source(surface, "rimStrength");
            var authoredStrength = (double?)surface.Properties["rimStrength"];
            if (amount == null && (!authoredStrength.HasValue || authoredStrength.Value == 0)) return;
            var strength = Scalar(surface, "rimStrength", 0);
            var width = Scalar(surface, "rimWidth", .2);
            var softness = Scalar(surface, "rimSoftness", .05);
            var alignment = Scalar(surface, "rimLightAlignment", 1);
            var color = Input(surface, "rimColor", ColorProp(surface, "rimColor", 1, 1, 1, 1), "color");
            var rimTarget = "(" + color + ").rgb" + (sceneFactor == null ? string.Empty : "*" + sceneFactor);
            b.AppendLine("float nxRimViewEdge=1-saturate(dot(n,view)); float nxRimMask=smoothstep(1-saturate("+width+")-max(.001,"+softness+"),1-saturate("+width+")+max(.001,"+softness+"),nxRimViewEdge); float nxRimLight=lerp(1,saturate(dot(n,lightDir)),saturate("+alignment+")); toonResponse=lerp(toonResponse,"+rimTarget+",nxRimMask*nxRimLight*saturate("+strength+"));");
        }

        bool HasLayerShadowOverride(GraphNode surface)
        {
            foreach (var port in new[] { "layerReceiveShadow", "layerReceiveShadow2", "layerReceiveShadow3" })
            {
                if (Source(surface, port) != null) return true;
                var value = (double?)surface.Properties[port];
                if (value.HasValue && value.Value != 1) return true;
            }
            return false;
        }

        string LightAttenuation(GraphNode surface)
        {
            const string standard = "UNITY_LIGHT_ATTENUATION(atten,input,input.ws);";
            if (surface.Operation == "core.toonSurface" && IntProp(surface, "lightingMode", 0, 0, 3) == 3 && HasLayerShadowOverride(surface))
                return "float nxSceneShadow=saturate(UNITY_SHADOW_ATTENUATION(input,input.ws));\n#undef UNITY_SHADOW_ATTENUATION\n#define UNITY_SHADOW_ATTENUATION(a,b) 1\n" + standard + "\n#undef UNITY_SHADOW_ATTENUATION\n";
            if (surface.Operation != "core.toonSurface" || Source(surface,"receiveShadow") == null && (surface.Properties["receiveShadow"] == null || (double)surface.Properties["receiveShadow"] == 1)) return standard;
            // Preserve Unity's distance/cookie attenuation. Replace only its scene-shadow factor in this pass.
            return "float nxSceneShadow=UNITY_SHADOW_ATTENUATION(input,input.ws);\n#undef UNITY_SHADOW_ATTENUATION\n#define UNITY_SHADOW_ATTENUATION(a,b) lerp(1,nxSceneShadow,saturate("+Scalar(surface,"receiveShadow",1)+"))\n"+standard;
        }

    }
}
