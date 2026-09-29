using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Four animated lanes around an editable UV path.</summary>
    public static class PathingNodes
    {
        public const string Operation = "core.pathing";

        public static void Register(IDictionary<string, FeatureNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            nodes[Operation] = new FeatureNode("Pathing", "Textures",
                "Four parallel animated UV paths. Channels is an RGBA mask; Mask combines all lanes. Phase is animated progress along the path, and Direction is its UV flow direction. Connect Time, Audio and Mask to animate or gate the effect.",
                "uv:vector2,start:vector2,end:vector2,time:float,audio:float,mask:float,width:float,spacing:float,speed:float,tail:float,travel:float",
                "channels:color,value:float,phase:float,direction:vector2", false,
                new JObject { ["start"] = new JArray(.1, .5), ["end"] = new JArray(.9, .5),
                    ["width"] = .02, ["spacing"] = .1, ["speed"] = .3, ["tail"] = .28,
                    ["travel"] = 1.0, ["audio"] = 1.0, ["mask"] = 1.0 });
        }

        public static void Validate(GraphNode node, Action<string, string> error)
        {
            if (node == null || node.Operation != Operation || error == null) return;
            CheckVector(node, "start", error);
            CheckVector(node, "end", error);
            CheckNumber(node, "width", 0, .5, error);
            CheckNumber(node, "spacing", 0, 1, error);
            CheckNumber(node, "speed", -20, 20, error);
            CheckNumber(node, "tail", .001, 1, error);
            CheckNumber(node, "travel", 0, 1, error);
        }

        static void CheckVector(GraphNode node, string key, Action<string, string> error)
        {
            var token = node.Properties?[key];
            if (token == null) return;
            var values = token as JArray;
            if (values == null || values.Count != 2) { error(key, "Expected two finite UV coordinates."); return; }
            foreach (var value in values)
            {
                if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                { error(key, "Expected two finite UV coordinates."); return; }
                double number;
                try { number = (double)value; }
                catch { error(key, "Expected two finite UV coordinates."); return; }
                if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > 10000)
                { error(key, "UV coordinates must be finite and within 10000 units."); return; }
            }
        }

        static void CheckNumber(GraphNode node, string key, double min, double max, Action<string, string> error)
        {
            var token = node.Properties?[key];
            if (token == null) return;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            { error(key, "Expected a finite number."); return; }
            double number;
            try { number = (double)token; }
            catch { error(key, "Expected a finite number."); return; }
            if (double.IsNaN(number) || double.IsInfinity(number) || number < min || number > max)
                error(key, "Value must be between " + min + " and " + max + ".");
        }
    }
}
