using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    // Focused camera/detail nodes; sockets carry graph meaning and stay Unity-free.
    internal static class DetailNodes
    {
        internal static void Register(Dictionary<string, FeatureNode> nodes)
        {
            nodes.Add("core.sdfFaceShadow", new FeatureNode(
                "SDF Face Shadow", "Surface",
                "Selects between two mirrored signed-distance samples using the main light. Angle shifts the shadow threshold continuously. Object axes follow the renderer; custom head axes are supplied by the graph and do not track a bone.",
                "sdfLeft:float,sdfRight:float,headRight:vector3,headForward:vector3,threshold:float,softness:float,strength:float,angleStrength:float,offset:float",
                "mask:float", false,
                new JObject { ["basis"] = 0, ["sdfLeft"] = .5, ["sdfRight"] = .5, ["headRight"] = new JArray(1,0,0), ["headForward"] = new JArray(0,0,1), ["threshold"] = .5, ["softness"] = .04, ["strength"] = 1, ["angleStrength"] = .3, ["offset"] = 0 }));

            nodes.Add("core.depthRim", new FeatureNode(
                "Depth Rim", "Surface",
                "Camera-depth edge mask at transitions to farther scene depth and silhouettes. A plane-depth derivative reduces interior edges on tilted surfaces. Requires camera depth; missing depth and oblique mirror projections return zero. Screen-space only, not a geometric outline. Width zero disables the effect.",
                "width:float,softness:float,bias:float,strength:float",
                "mask:float", false,
                new JObject { ["width"] = 2, ["softness"] = .02, ["bias"] = .01, ["strength"] = 1 }));

            nodes.Add("core.gem", new FeatureNode(
                "Gem", "Color",
                "Stylized gem color using chromatic GrabPass refraction and a roughness-filtered first reflection probe. This is a screen and probe approximation: it does not trace scene geometry or resolve internal reflections.",
                "color:color,normal:vector3,ior:float,refraction:float,reflection:float,dispersion:float,roughness:float",
                "color:color", false,
                new JObject { ["color"] = new JArray(1,1,1,1), ["ior"] = 1.5, ["refraction"] = .06, ["reflection"] = .8, ["dispersion"] = .015, ["roughness"] = .15 }));
        }
    }
}
