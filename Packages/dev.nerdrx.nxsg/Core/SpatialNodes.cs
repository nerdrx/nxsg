using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    // Nodes whose meaning depends on mesh position, vertex basis, or repeated depth samples.
    internal static class SpatialNodes
    {
        internal static bool ValidSpace(JToken value) { return IntegerIn(value, 0, 1); }
        internal static bool ValidShape(JToken value) { return IntegerIn(value, 0, 2); }
        internal static bool ValidBlend(JToken value) { return IntegerIn(value, 0, 2); }
        internal static bool ValidSteps(JToken value) { return IntegerIn(value, 1, 32); }

        static bool IntegerIn(JToken value, int min, int max)
        {
            return value != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float) &&
                (double)value == System.Math.Truncate((double)value) && (double)value >= min && (double)value <= max;
        }

        internal static void Register(Dictionary<string, FeatureNode> nodes)
        {
            nodes.Add("core.vertexDeform", new FeatureNode(
                "Vertex Deform", "Surface",
                "Translate, rotate and scale mesh vertices around a pivot, then optionally snap or bend the shape toward a sphere or cylinder. Translation, Euler rotation (degrees), scale, pivot, mask, snap size and warp amount are connectable. Local space follows the object; world space uses world axes. Normals and tangents follow the transform. Deforms the final mesh surface before fur, particles, outlines, dissolve and tessellation stages, including additive and shadow geometry.",
                "base:surface,translation:vector3,rotation:vector3,scale:vector3,pivot:vector3,mask:float,snap:float,warp:float,nearDistance:float,nearStrength:float",
                "surface:surface", false,
                new JObject { ["translation"] = new JArray(0,0,0), ["rotation"] = new JArray(0,0,0), ["scale"] = new JArray(1,1,1), ["pivot"] = new JArray(0,0,0), ["mask"] = 1, ["snap"] = 0, ["warp"] = 0, ["nearDistance"] = 0, ["nearStrength"] = 1, ["space"] = 0, ["shape"] = 0 }));

            nodes.Add("core.infinityParallax", new FeatureNode(
                "Infinity Parallax", "Textures",
                "Blend a bounded stack of repeated samples to suggest an infinitely layered interior. View is tangent-space; depth and strength control the view offset, height optionally modulates layer positions, tint colors deeper layers, fade reduces deep contributions, and mask/texture alpha control output alpha. A visual interior effect only: it does not displace mesh geometry, trace hidden surfaces, or replace Parallax Occlusion / room mapping.",
                "uv:vector2,view:vector3,height:float,depth:float,strength:float,tint:color,fade:float,mask:float",
                "color:color,alpha:float", true,
                new JObject { ["resourceId"] = "", ["steps"] = 8, ["blend"] = 0, ["depth"] = .3, ["strength"] = .05, ["height"] = .5, ["tint"] = new JArray(.8,.9,1,1), ["fade"] = .7, ["mask"] = 1 }));
        }
    }
}
