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
            "core.ramp", "core.polarUV", "core.uvRotate", "core.objectUV", "core.worldUV",
            "core.value", "core.time", "core.uvTransform", "core.uvScroll", "core.noise",
            "core.add", "core.subtract", "core.divide", "core.minimum", "core.maximum", "core.mix", "core.emission", "core.oneMinus", "core.clamp",
            "core.constant", "core.parameter", "core.uv0", "core.texture2D", "core.multiply",
            "core.toonSurface", "core.output"
        };

        private static readonly Dictionary<string, string[]> Inputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core.ramp"] = new[] { "value" },
            ["core.polarUV"] = new[] { "uv" }, ["core.uvRotate"] = new[] { "uv", "angle" },
            ["core.objectUV"] = new string[0], ["core.worldUV"] = new string[0],
            ["core.value"] = new string[0], ["core.time"] = new string[0],
            ["core.uvTransform"] = new[] { "uv" }, ["core.uvScroll"] = new[] { "uv", "time" },
            ["core.noise"] = new[] { "uv", "time" }, ["core.add"] = new[] { "a", "b" },
            ["core.subtract"] = new[] { "a", "b" }, ["core.divide"] = new[] { "a", "b" },
            ["core.minimum"] = new[] { "a", "b" }, ["core.maximum"] = new[] { "a", "b" },
            ["core.mix"] = new[] { "a", "b", "factor" }, ["core.emission"] = new[] { "color", "strength" },
            ["core.oneMinus"] = new[] { "color" }, ["core.clamp"] = new[] { "color" },
            ["core.constant"] = new string[0], ["core.parameter"] = new string[0],
            ["core.uv0"] = new string[0], ["core.texture2D"] = new[] { "uv" },
            ["core.multiply"] = new[] { "a", "b" }, ["core.toonSurface"] = new[] { "albedo", "normal", "emission" },
            ["core.output"] = new[] { "surface" }
        };

        private static readonly Dictionary<string, string[]> Outputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core.ramp"] = new[] { "value" },
            ["core.polarUV"] = new[] { "uv" }, ["core.uvRotate"] = new[] { "uv" },
            ["core.objectUV"] = new[] { "uv" }, ["core.worldUV"] = new[] { "uv" },
            ["core.value"] = new[] { "value" }, ["core.time"] = new[] { "value" },
            ["core.uvTransform"] = new[] { "uv" }, ["core.uvScroll"] = new[] { "uv" },
            ["core.noise"] = new[] { "color", "value" }, ["core.add"] = new[] { "value" },
            ["core.subtract"] = new[] { "value" }, ["core.divide"] = new[] { "value" },
            ["core.minimum"] = new[] { "value" }, ["core.maximum"] = new[] { "value" },
            ["core.mix"] = new[] { "value" }, ["core.emission"] = new[] { "color" },
            ["core.oneMinus"] = new[] { "color" }, ["core.clamp"] = new[] { "color" },
            ["core.constant"] = new[] { "value" }, ["core.parameter"] = new[] { "value" },
            ["core.uv0"] = new[] { "uv" }, ["core.texture2D"] = new[] { "color" },
            ["core.multiply"] = new[] { "value" }, ["core.toonSurface"] = new[] { "surface" },
            ["core.output"] = new string[0]
        };

        public static IEnumerable<string> All { get { return Operations; } }
        public static bool IsKnown(string operation) { return operation != null && Inputs.ContainsKey(operation); }
        public static string Category(string operation)
        {
            switch (operation)
            {
                case "core.ramp": return "Math";
                case "core.uv0": case "core.uvTransform": case "core.uvScroll": case "core.uvRotate":
                case "core.polarUV": case "core.objectUV": case "core.worldUV": return "Coordinates";
                case "core.value": case "core.time": case "core.constant": case "core.parameter": return "Inputs";
                case "core.texture2D": case "core.noise": return "Textures";
                case "core.add": case "core.subtract": case "core.multiply": case "core.divide":
                case "core.minimum": case "core.maximum": case "core.mix": case "core.oneMinus":
                case "core.clamp": return "Math";
                case "core.emission": case "core.toonSurface": case "core.output": return "Surface";
                default: return "Other";
            }
        }
        public static string[] Ports(string operation, bool output)
        {
            string[] ports;
            return (output ? Outputs : Inputs).TryGetValue(operation ?? string.Empty, out ports) ? (string[])ports.Clone() : new string[0];
        }
        public static string Title(string operation)
        {
            switch (operation)
            {
                case "core.ramp": return "Ramp";
                case "core.polarUV": return "Polar UVs";
                case "core.uvRotate": return "Rotate UVs";
                case "core.objectUV": return "Object Planar UVs";
                case "core.worldUV": return "World Planar UVs";
                case "core.value": return "Value"; case "core.time": return "Time";
                case "core.uvTransform": return "UV Transform"; case "core.uvScroll": return "UV Scroll";
                case "core.noise": return "Noise"; case "core.add": return "Add"; case "core.subtract": return "Subtract";
                case "core.divide": return "Divide"; case "core.minimum": return "Minimum"; case "core.maximum": return "Maximum";
                case "core.mix": return "Mix";
                case "core.emission": return "Emission"; case "core.oneMinus": return "Invert";
                case "core.clamp": return "Clamp"; case "core.constant": return "Color";
                case "core.parameter": return "Parameter"; case "core.uv0": return "UV Coordinates";
                case "core.texture2D": return "Texture"; case "core.multiply": return "Multiply";
                case "core.toonSurface": return "Toon Surface"; case "core.output": return "Output";
                default: return operation;
            }
        }
        public static string Description(string operation)
        {
            switch (operation)
            {
                case "core.ramp": return "Reshape a number or noise mask with black/white points and a curve. Connect Noise value here to control its contrast.";
                case "core.polarUV": return "Wrap coordinates around a center: U is distance, V is angle. Useful for rings and radial patterns.";
                case "core.uvRotate": return "Rotate texture coordinates around a center, in degrees. Connect Time to angle to spin them.";
                case "core.objectUV": return "Project using the mesh's local X/Z position. Moves with the object; use UV Transform to adjust scale.";
                case "core.worldUV": return "Project using world X/Z position. Objects move through the pattern; use UV Transform to adjust scale.";
                case "core.value": return "An adjustable number. Use it to control strength, time, or blending.";
                case "core.time": return "Time in seconds, with speed and offset controls. Use it to animate effects.";
                case "core.uvTransform": return "Scale and shift texture coordinates to control tiling and placement.";
                case "core.uvScroll": return "Move texture coordinates over time, like flowing water or scrolling stripes.";
                case "core.noise": return "Create a smooth random grayscale pattern that can move over time.";
                case "core.add": return "Add two numbers or colors together to brighten or combine them.";
                case "core.subtract": return "Subtract B from A, using numbers or color channels.";
                case "core.divide": return "Divide A by B, using numbers or color channels. Very small divisors are limited to avoid division by zero.";
                case "core.minimum": return "Choose the lower value from A and B for each color channel.";
                case "core.maximum": return "Choose the higher value from A and B for each color channel.";
                case "core.mix": return "Blend two numbers or colors: factor 0 gives A, 1 gives B, and 0.5 mixes them equally.";
                case "core.emission": return "Add color that stays bright without lighting. Connect it to Toon Surface's emission input.";
                case "core.oneMinus": return "Invert numbers, colors or masks (1 minus input): black becomes white, and white becomes black.";
                case "core.clamp": return "Keep each color channel between 0 and 1. Values outside that range are clipped.";
                case "core.constant": return "Choose a solid color to use on its own or combine with other nodes.";
                case "core.parameter": return "Read a declared property that can control your material.";
                case "core.uv0": return "The mesh's first texture coordinates: where each part of an image lands on the mesh.";
                case "core.texture2D": return "Read an image using texture coordinates and output its color.";
                case "core.multiply": return "Multiply numbers or colors. Use colors to tint or darken. White keeps the other color unchanged.";
                case "core.toonSurface": return "Give your base color cartoon-style lighting, with an optional emission input.";
                case "core.output": return "The final surface of your shader. Connect a Toon Surface here.";
                default: return string.Empty;
            }
        }
        public static string Aliases(string operation)
        {
            switch (operation)
            {
                case "core.ramp": return "gradient contrast remap levels curve mask threshold";
                case "core.polarUV": return "polar radial circle rings angle radius texture coordinates";
                case "core.uvRotate": return "rotation spin pivot texture coordinates";
                case "core.objectUV": return "local object space planar projection mapping xz";
                case "core.worldUV": return "world space planar projection mapping xz";
                case "core.value": return "constant scalar"; case "core.time": return "clock animation";
                case "core.uvTransform": return "scale offset tiling"; case "core.uvScroll": return "pan animate";
                case "core.noise": return "procedural random"; case "core.add": return "plus sum";
                case "core.subtract": return "minus difference"; case "core.divide": return "division ratio safe divide";
                case "core.minimum": return "min lower"; case "core.maximum": return "max higher";
                case "core.mix": return "lerp blend"; case "core.emission": return "glow";
                case "core.oneMinus": return "invert one minus"; case "core.clamp": return "saturate";
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
                case "core.ramp": return port == "value" ? "float" : null;
                case "core.polarUV": case "core.objectUV": case "core.worldUV": return port == "uv" ? "vector2" : null;
                case "core.uvRotate": return port == "uv" ? "vector2" : (port == "angle" ? "float" : null);
                case "core.value": case "core.time": return port == "value" ? "float" : null;
                case "core.uvTransform": case "core.uvScroll": return port == "uv" ? "vector2" : (port == "time" ? "float" : null);
                case "core.noise": return port == "uv" ? "vector2" : (port == "time" ? "float" : (port == "value" ? "float" : (port == "color" ? "color" : null)));
                case "core.add": case "core.subtract": case "core.divide": case "core.minimum": case "core.maximum": return port == "a" || port == "b" || port == "value" ? "color" : null;
                case "core.mix": return port == "factor" ? "float" : (port == "a" || port == "b" || port == "value" ? "color" : null);
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
                case "core.ramp": node.Properties["blackPoint"] = 0.0; node.Properties["whitePoint"] = 1.0; node.Properties["smoothness"] = 0.0; node.Properties["points"] = new JArray(Vector(0, 0), Vector(1, 1)); break;
                case "core.polarUV": node.Properties["center"] = Vector(.5, .5); node.Properties["radialScale"] = 1.0; node.Properties["angleScale"] = 1.0; break;
                case "core.uvRotate": node.Properties["center"] = Vector(.5, .5); node.Properties["angle"] = 0.0; break;
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
