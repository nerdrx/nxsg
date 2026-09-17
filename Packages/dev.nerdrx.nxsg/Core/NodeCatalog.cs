using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Portable registry for the built-in graph operations.</summary>
    public static class NodeCatalog
    {
        private static readonly string[] Operations =
        {
            "core.value", "core.time", "core.uvTransform", "core.uvScroll", "core.noise",
            "core.add", "core.mix", "core.emission", "core.oneMinus", "core.clamp",
            "core.constant", "core.parameter", "core.uv0", "core.texture2D", "core.multiply",
            "core.toonSurface", "core.output"
        };

        private static readonly Dictionary<string, string[]> Inputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core.value"] = new string[0], ["core.time"] = new string[0],
            ["core.uvTransform"] = new[] { "uv" }, ["core.uvScroll"] = new[] { "uv", "time" },
            ["core.noise"] = new[] { "uv", "time" }, ["core.add"] = new[] { "a", "b" },
            ["core.mix"] = new[] { "a", "b", "factor" }, ["core.emission"] = new[] { "color", "strength" },
            ["core.oneMinus"] = new[] { "color" }, ["core.clamp"] = new[] { "color" },
            ["core.constant"] = new string[0], ["core.parameter"] = new string[0],
            ["core.uv0"] = new string[0], ["core.texture2D"] = new[] { "uv" },
            ["core.multiply"] = new[] { "a", "b" }, ["core.toonSurface"] = new[] { "albedo", "normal", "emission" },
            ["core.output"] = new[] { "surface" }
        };

        private static readonly Dictionary<string, string[]> Outputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core.value"] = new[] { "value" }, ["core.time"] = new[] { "value" },
            ["core.uvTransform"] = new[] { "uv" }, ["core.uvScroll"] = new[] { "uv" },
            ["core.noise"] = new[] { "color", "value" }, ["core.add"] = new[] { "value" },
            ["core.mix"] = new[] { "value" }, ["core.emission"] = new[] { "color" },
            ["core.oneMinus"] = new[] { "color" }, ["core.clamp"] = new[] { "color" },
            ["core.constant"] = new[] { "value" }, ["core.parameter"] = new[] { "value" },
            ["core.uv0"] = new[] { "uv" }, ["core.texture2D"] = new[] { "color" },
            ["core.multiply"] = new[] { "value" }, ["core.toonSurface"] = new[] { "surface" },
            ["core.output"] = new string[0]
        };

        public static IEnumerable<string> All { get { return Operations; } }
        public static bool IsKnown(string operation) { return operation != null && Inputs.ContainsKey(operation); }
        public static string[] Ports(string operation, bool output)
        {
            string[] ports;
            return (output ? Outputs : Inputs).TryGetValue(operation ?? string.Empty, out ports) ? (string[])ports.Clone() : new string[0];
        }
        public static string Title(string operation)
        {
            switch (operation)
            {
                case "core.value": return "Value"; case "core.time": return "Time";
                case "core.uvTransform": return "UV Transform"; case "core.uvScroll": return "UV Scroll";
                case "core.noise": return "Noise"; case "core.add": return "Add"; case "core.mix": return "Mix";
                case "core.emission": return "Emission"; case "core.oneMinus": return "One Minus";
                case "core.clamp": return "Clamp"; case "core.constant": return "Color";
                case "core.parameter": return "Parameter"; case "core.uv0": return "UV Coordinates";
                case "core.texture2D": return "Texture"; case "core.multiply": return "Multiply";
                case "core.toonSurface": return "Toon Surface"; case "core.output": return "Output";
                default: return operation;
            }
        }
        public static string Aliases(string operation)
        {
            switch (operation)
            {
                case "core.value": return "constant scalar"; case "core.time": return "clock animation";
                case "core.uvTransform": return "scale offset tiling"; case "core.uvScroll": return "pan animate";
                case "core.noise": return "procedural random"; case "core.add": return "plus sum";
                case "core.mix": return "lerp blend"; case "core.emission": return "glow";
                case "core.oneMinus": return "invert"; case "core.clamp": return "saturate";
                case "core.constant": return "rgb rgba colour";
                case "core.multiply": return "tint darken blend";
                case "core.toonSurface": return "anime cel cartoon shading";
                case "core.texture2D": return "image albedo diffuse";
                default: return string.Empty;
            }
        }
        public static string PortType(GraphNode node, string port)
        {
            if (node == null || port == null) return null;
            switch (node.Operation)
            {
                case "core.value": case "core.time": return port == "value" ? "float" : null;
                case "core.uvTransform": case "core.uvScroll": return port == "uv" ? "vector2" : (port == "time" ? "float" : null);
                case "core.noise": return port == "uv" ? "vector2" : (port == "time" ? "float" : (port == "value" ? "float" : (port == "color" ? "color" : null)));
                case "core.add": case "core.mix": return port == "factor" ? "float" : (port == "a" || port == "b" || port == "value" ? "color" : null);
                case "core.emission": return port == "strength" ? "float" : (port == "color" ? "color" : null);
                case "core.oneMinus": case "core.clamp": return port == "color" ? "color" : null;
                case "core.constant": return port == "value" ? TypeName(node.Properties == null ? null : node.Properties["valueType"]) : null;
                case "core.parameter": return port == "value" ? "color" : null;
                case "core.uv0": return port == "uv" ? "vector2" : null;
                case "core.texture2D": return port == "uv" ? "vector2" : (port == "color" ? "color" : null);
                case "core.multiply": return port == "a" || port == "b" || port == "value" ? TypeName(node.Properties == null ? null : node.Properties["valueType"]) : null;
                case "core.toonSurface": return port == "surface" ? "surface" : (port == "normal" ? "vector3" : (port == "albedo" || port == "emission" ? "color" : null));
                case "core.output": return port == "surface" ? "surface" : null;
                default: return null;
            }
        }
        public static GraphNode Create(string operation)
        {
            if (!IsKnown(operation)) return null;
            var node = new GraphNode { Id = Guid.NewGuid().ToString("N"), Operation = operation };
            switch (operation)
            {
                case "core.value": node.Properties["value"] = 0.0; break;
                case "core.time": node.Properties["speed"] = 1.0; node.Properties["offset"] = 0.0; break;
                case "core.uvTransform": node.Properties["tiling"] = Vector(1, 1); node.Properties["offset"] = Vector(0, 0); break;
                case "core.uvScroll": node.Properties["speed"] = Vector(.1, 0); break;
                case "core.noise": node.Properties["scale"] = 5.0; node.Properties["speed"] = 1.0; break;
                case "core.mix": node.Properties["factor"] = .5; break;
                case "core.emission": node.Properties["strength"] = 1.0; break;
                case "core.constant": node.Properties["valueType"] = "color"; node.Properties["value"] = new JArray(1, 1, 1, 1); break;
                case "core.multiply": node.Properties["valueType"] = "color"; break;
            }
            return node;
        }
        private static JArray Vector(double x, double y) { return new JArray(x, y); }
        private static string TypeName(JToken token) { return token == null ? "color" : token.Value<string>(); }
    }
}
