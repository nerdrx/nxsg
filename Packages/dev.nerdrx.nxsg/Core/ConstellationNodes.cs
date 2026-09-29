using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>One reusable animated point-and-link UV signal.</summary>
    public static class ConstellationNodes
    {
        public const string Operation = "core.constellation";

        public static void Register(IDictionary<string, FeatureNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            nodes[Operation] = new FeatureNode("Constellation", "Textures",
                "Animated points and links on a UV grid. Points, Lines and Mask are separate scalar outputs; colorize them with graph nodes. Time and Audio can be connected for animation. Fragment-only; each cell checks nearby stars.",
                "uv:vector2,time:float,audio:float,scale:float,pointSize:float,lineWidth:float,linkChance:float,twinkle:float",
                "points:float,lines:float,mask:float", false,
                new JObject { ["scale"] = 12.0, ["pointSize"] = .075, ["lineWidth"] = .018,
                    ["linkChance"] = .65, ["twinkle"] = .35, ["audio"] = 1.0, ["seed"] = 0.0 });
        }

        public static void Validate(GraphNode node, Action<string, string> error)
        {
            if (node == null || node.Operation != Operation || error == null) return;
            Check(node, "scale", 1, 64, error);
            Check(node, "pointSize", 0, .5, error);
            Check(node, "lineWidth", 0, .25, error);
            Check(node, "linkChance", 0, 1, error);
            Check(node, "twinkle", 0, 1, error);
            Check(node, "seed", -100000, 100000, error);
        }

        static void Check(GraphNode node, string key, double min, double max, Action<string, string> error)
        {
            var value = node.Properties?[key];
            if (value == null) return;
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
            { error(key, "Expected a finite number."); return; }
            double number;
            try { number = (double)value; }
            catch { error(key, "Expected a finite number."); return; }
            if (double.IsNaN(number) || double.IsInfinity(number) || number < min || number > max)
                error(key, "Value must be between " + min + " and " + max + ".");
        }
    }
}
