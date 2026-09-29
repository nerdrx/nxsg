using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Dedicated 2D tone-map lookup with neutral unassigned behavior.</summary>
    public static class SkinToneLutNodes
    {
        public const string Operation = "core.skinToneLut";

        public static void Register(IDictionary<string, FeatureNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            nodes[Operation] = new FeatureNode("Skin Tone LUT", "Color",
                "Map base-color luminance (LUT X) and pigment (LUT Y) to a skin tone. Mask and Strength blend the result with the base color. Unassigned LUT is neutral; assign a 2D texture in the graph and rebuild.",
                "base:color,pigment:float,mask:float,strength:float", "color:color", true,
                new JObject { ["resourceId"] = "", ["pigment"] = .5, ["mask"] = 1.0, ["strength"] = 1.0 });
        }

        public static void Validate(GraphNode node, Action<string, string> error)
        {
            if (node == null || node.Operation != Operation || error == null) return;
            var resource = node.Properties?["resourceId"];
            if (resource != null && resource.Type != JTokenType.String)
                error("resourceId", "Expected a texture resource ID.");
            foreach (var key in new[] { "pigment", "mask", "strength" })
            {
                var value = node.Properties?[key];
                if (value == null) continue;
                if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                { error(key, "Expected a finite number from 0 to 1."); continue; }
                double number;
                try { number = (double)value; }
                catch { error(key, "Expected a finite number from 0 to 1."); continue; }
                if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number > 1)
                    error(key, "Value must be from 0 to 1.");
            }
        }
    }
}
