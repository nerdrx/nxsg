using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        bool bentLightingEnabled, lightDirectionEnabled;

        bool UsesBentNormal(GraphNode surface) => Source(surface, "bentNormal") != null &&
            (Source(surface, "bentStrength") != null || (double?)surface.Properties["bentStrength"] != 0);

        string IndirectNormal(GraphNode surface)
        {
            if (!UsesBentNormal(surface)) return "n";
            bentLightingEnabled = true;
            return "NX_BentNormal(input,n," + Input(surface,"bentNormal","float3(0,0,1)","vector3") + "," + Scalar(surface,"bentStrength",1) + ")";
        }

        string IndirectSpecularVisibility(GraphNode surface, string normal, string roughness)
        {
            var ao = "saturate(" + Scalar(surface,"occlusion",1) + ")";
            if (!UsesBentNormal(surface)) return ao;
            bentLightingEnabled = true;
            return "lerp(" + ao + ",NX_BentVisibility(" + IndirectNormal(surface) + ",reflect(-view," + normal + ")," + ao + "," + roughness + "),saturate(" + Scalar(surface,"bentStrength",1) + "))";
        }

        string OverrideLightDirection(GraphNode surface)
        {
            if (Source(surface,"lightDirectionStrength") == null && !LightingValueDiffers(surface,"lightDirectionStrength",0)) return "";
            lightDirectionEnabled = true;
            var vector = Input(surface,"lightDirection",surface.Properties["lightDirection"] == null ? "float3(0,1,0)" : Literal(surface.Properties["lightDirection"],"vector3"),"vector3");
            if (IntProp(surface,"lightDirectionSpace",0,0,1) == 1) vector = "UnityObjectToWorldDir(" + vector + ")";
            return " lightDir=NX_OverrideLightDirection(lightDir," + vector + "," + Scalar(surface,"lightDirectionStrength",0) + "); ";
        }

        const string LightDirectionHelper = @"
float3 NX_OverrideLightDirection(float3 original,float3 target,float strength) {
    float lengthSq=dot(target,target); if(lengthSq<1e-10) return original;
    float3 mixed=lerp(original,target*rsqrt(lengthSq),saturate(strength));
    return dot(mixed,mixed)>1e-10?normalize(mixed):original;
}";

        // Approximate overlap of a visibility cone and a rough reflection cone.
        // AO defines the visible cone area; roughness defines the reflection lobe.
        // This is indirect-light shaping, not a replacement for scene shadow maps.
        const string BentLightingHelpers = @"
float3 NX_BentNormal(NXInput input,float3 normal,float3 tangentBent,float strength) {
    float3 bent=normalize(input.tangent)*tangentBent.x+normalize(input.bitangent)*tangentBent.y+normalize(input.n)*tangentBent.z;
    if(dot(bent,bent)<1e-10) return normal;
    bent=lerp(normal,normalize(bent),saturate(strength));
    return dot(bent,bent)>1e-10?normalize(bent):normal;
}
float NX_BentVisibility(float3 bent,float3 reflection,float ao,float roughness) {
    ao=saturate(ao); if(ao<=0) return 0; if(ao>=.99999) return 1;
    float visibleCos=sqrt(1-ao);
    float lobeCos=exp2(-3.32192809489*max(.0001,roughness*roughness));
    float visibleAngle=acos(visibleCos),lobeAngle=acos(lobeCos);
    float separation=acos(clamp(dot(bent,reflection),-1,1));
    float overlap=saturate((visibleAngle+lobeAngle-separation)/max(2*min(visibleAngle,lobeAngle),.0001));
    overlap=overlap*overlap*(3-2*overlap);
    return saturate(overlap*(1-max(visibleCos,lobeCos))/max(1-lobeCos,.00001));
}";
    }
}
