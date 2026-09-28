using System;
using System.Text.RegularExpressions;
using System.Globalization;
using NXSG.Core;

namespace NXSG.Backend
{
    internal sealed partial class AdvancedShaderEmitter
    {
        string SurfaceAlphaClip(GraphNode surface)
        {
            var cutoff=Prop(surface,"cutoff",.001);
            if(renderState.AlphaEdgeSharpness<=0) return "clip(alpha-"+cutoff+"); ";
            return "alpha="+renderState.SharpenAlpha("alpha",cutoff)+"; clip(alpha-lerp("+cutoff+",.00001,"+renderState.AlphaEdgeSharpness.ToString("R",CultureInfo.InvariantCulture)+")); ";
        }

        // Apply the same fragment contract to every generated pass, without an
        // extra interpolator. Fail explicitly if a future pass has another shape.
        string InstrumentFacePasses(string passes)
        {
            if (!faceInputsEnabled) return passes;
            var expected = Regex.Matches(passes, @"#pragma fragment \w+").Count;
            var actual = 0;
            var flip = renderState.FlipBackfaceNormals
                ? " input.n*=NX_FrontFace>.5?1:-1; input.bitangent*=NX_FrontFace>.5?1:-1; " : "";
            var result = Regex.Replace(passes, @"(float4\s+frag\w*\(\s*(NXInput|NXFinInput|NXSoftInput|NXShadow)\s+\w+)\s*\)\s*:\s*SV_Target\s*\{", match => {
                actual++;
                return match.Groups[1].Value + ", float nxFace : VFACE):SV_Target { NX_FrontFace=nxFace>0?1:0; " +
                    (match.Groups[2].Value == "NXInput" ? flip : "");
            });
            if (actual != expected) throw new InvalidOperationException("Face controls cannot be applied to one of the generated fragment passes.");
            result = result.Replace("NXInput input=fin.data;", "NXInput input=fin.data;" + flip);
            result = result.Replace("input.sourceUV=i.sourceUV;", "input.sourceUV=i.sourceUV;" + flip);
            return result;
        }
    }
}
