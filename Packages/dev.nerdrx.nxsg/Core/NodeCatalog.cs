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
            "core.toonSurface", "core.unlitSurface", "core.pbrSurface", "core.fresnel", "core.colorRamp",
            "core.layer", "core.sticker", "core.dissolve", "core.flipbook", "core.uvDistort", "core.vertexMotion",
            "core.audioLink", "core.shell", "core.normalMap", "core.previewVector", "core.output"
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
            ["core.multiply"] = new[] { "a", "b" }, ["core.toonSurface"] = new[] { "albedo", "normal", "emission", "opacity", "displacement" },
            ["core.unlitSurface"] = new[] { "albedo", "emission", "opacity", "displacement" },
            ["core.pbrSurface"] = new[] { "albedo", "emission", "opacity", "displacement", "normal", "metallic", "roughness" },
            ["core.fresnel"] = new string[0], ["core.colorRamp"] = new[] { "value" },
            ["core.layer"] = new[] { "base", "overlay", "mask" }, ["core.sticker"] = new[] { "base", "uv", "mask" },
            ["core.dissolve"] = new[] { "value", "threshold" }, ["core.flipbook"] = new[] { "uv", "time" },
            ["core.uvDistort"] = new[] { "uv", "strength" }, ["core.vertexMotion"] = new[] { "time", "strength" },
            ["core.audioLink"] = new string[0], ["core.shell"] = new[] { "base", "layer", "offset" },
            ["core.normalMap"] = new[] { "color" },
            ["core.previewVector"] = new[] { "uv", "normal" },
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
            ["core.unlitSurface"] = new[] { "surface" }, ["core.pbrSurface"] = new[] { "surface" },
            ["core.fresnel"] = new[] { "value" }, ["core.colorRamp"] = new[] { "color" },
            ["core.layer"] = new[] { "color" }, ["core.sticker"] = new[] { "color" },
            ["core.dissolve"] = new[] { "mask", "edge" }, ["core.flipbook"] = new[] { "uv" },
            ["core.uvDistort"] = new[] { "uv" }, ["core.vertexMotion"] = new[] { "value" },
            ["core.audioLink"] = new[] { "value" }, ["core.shell"] = new[] { "surface" },
            ["core.normalMap"] = new[] { "normal" },
            ["core.previewVector"] = new[] { "color" },
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
                case "core.emission": case "core.toonSurface": case "core.unlitSurface": case "core.pbrSurface":
                case "core.shell": case "core.normalMap": case "core.output": return "Surface";
                case "core.fresnel": case "core.colorRamp": case "core.layer": case "core.sticker":
                case "core.dissolve": return "Color";
                case "core.flipbook": case "core.uvDistort": case "core.vertexMotion": return "Animation";
                case "core.audioLink": return "Inputs";
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
                case "core.toonSurface": return "Toon Surface"; case "core.unlitSurface": return "Unlit Surface";
                case "core.pbrSurface": return "PBR Surface"; case "core.fresnel": return "Fresnel";
                case "core.colorRamp": return "Color Ramp"; case "core.layer": return "Layer";
                case "core.sticker": return "Sticker"; case "core.dissolve": return "Dissolve";
                case "core.flipbook": return "Flipbook"; case "core.uvDistort": return "UV Distort";
                case "core.vertexMotion": return "Vertex Motion"; case "core.audioLink": return "Audio Link";
                case "core.shell": return "Shell"; case "core.normalMap": return "Normal Map";
                case "core.output": return "Output";
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
                case "core.toonSurface": return "Give your base color cartoon-style lighting, with optional emission, opacity, and displacement.";
                case "core.unlitSurface": return "Build a surface with color and emission without lighting.";
                case "core.pbrSurface": return "Build a physically based surface with color, normal, metallic, and roughness controls.";
                case "core.fresnel": return "Compute an edge mask from a value and power.";
                case "core.colorRamp": return "Map a scalar value through a bounded color ramp.";
                case "core.layer": return "Blend an overlay color over a base color with a mask.";
                case "core.sticker": return "Project a texture onto UVs with optional masking and transform controls.";
                case "core.dissolve": return "Compare a value to a threshold and output dissolve and edge masks.";
                case "core.flipbook": return "Animate UVs through a rows by columns texture atlas.";
                case "core.uvDistort": return "Offset UVs using a strength-controlled distortion.";
                case "core.vertexMotion": return "Drive vertex motion from time and strength inputs.";
                case "core.audioLink": return "Read a smoothed audio band value with a fallback.";
                case "core.shell": return "Wrap a base surface with an offset shell layer.";
                case "core.normalMap": return "Decode a normal map color into a normal vector.";
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
                case "core.unlitSurface": return "flat no lighting"; case "core.pbrSurface": return "physically based lit material";
                case "core.fresnel": return "edge rim grazing angle"; case "core.colorRamp": return "gradient palette lookup";
                case "core.layer": return "overlay composite blend"; case "core.sticker": return "decal projected texture";
                case "core.dissolve": return "cutout burn edge mask"; case "core.flipbook": return "texture atlas animation";
                case "core.uvDistort": return "warp wobble coordinates"; case "core.vertexMotion": return "vertex animation deformation";
                case "core.audioLink": return "audio reactive spectrum"; case "core.shell": return "outline rim extrude";
                case "core.normalMap": return "bump tangent normal";
                case "core.texture2D": return "image albedo diffuse";
                default: return string.Empty;
            }
        }
        public static string PortType(GraphNode node, string port)
        {
            if (node == null || port == null) return null;
            switch (node.Operation)
            {
                case "core.previewVector": return port == "uv" ? "vector2" : port == "normal" ? "vector3" : port == "color" ? "color" : null;
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
                case "core.toonSurface": case "core.pbrSurface":
                    if (port == "surface") return "surface";
                    if (port == "normal") return "vector3";
                    if (port == "albedo" || port == "emission") return "color";
                    if (port == "opacity" || port == "displacement" || port == "metallic" || port == "roughness") return "float";
                    return null;
                case "core.fresnel": return port == "value" ? "float" : (port == "power" ? "float" : null);
                case "core.colorRamp": return port == "value" ? "float" : (port == "color" ? "color" : null);
                case "core.layer": return port == "base" || port == "overlay" || port == "color" ? "color" : (port == "mask" ? "float" : null);
                case "core.sticker": return port == "base" ? "color" : (port == "uv" ? "vector2" : (port == "mask" ? "float" : (port == "color" ? "color" : null)));
                case "core.dissolve": return port == "value" || port == "threshold" || port == "mask" || port == "edge" ? "float" : null;
                case "core.flipbook": case "core.uvDistort": return port == "uv" ? "vector2" : (port == "time" || port == "strength" ? "float" : null);
                case "core.vertexMotion": return port == "time" || port == "strength" || port == "value" ? "float" : null;
                case "core.audioLink": return port == "value" ? "float" : null;
                case "core.unlitSurface": return port == "surface" ? "surface" : (port == "albedo" || port == "emission" ? "color" : (port == "opacity" || port == "displacement" ? "float" : null));
                case "core.shell": return port == "base" || port == "layer" || port == "surface" ? "surface" : (port == "offset" ? "float" : null);
                case "core.normalMap": return port == "color" ? "color" : (port == "normal" ? "vector3" : null);
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
                case "core.fresnel": node.Properties["power"] = 5.0; break;
                case "core.colorRamp": node.Properties["stops"] = new JArray(new JArray(0, 0, 0, 0, 1), new JArray(1, 1, 1, 1, 1)); break;
                case "core.sticker": node.Properties["resourceId"] = ""; node.Properties["position"] = Vector(0, 0); node.Properties["size"] = Vector(1, 1); node.Properties["rotation"] = 0.0; break;
                case "core.flipbook": node.Properties["rows"] = 1; node.Properties["columns"] = 1; node.Properties["speed"] = 1.0; break;
                case "core.audioLink": node.Properties["band"] = 0; node.Properties["gain"] = 1.0; node.Properties["smoothing"] = 0.5; node.Properties["fallback"] = 0.0; break;
                case "core.normalMap": node.Properties["strength"] = 1.0; break;
                case "core.unlitSurface": node.Properties["opacity"] = 1.0; node.Properties["displacement"] = 0.0; break;
                case "core.pbrSurface": node.Properties["opacity"] = 1.0; node.Properties["displacement"] = 0.0; node.Properties["metallic"] = 0.0; node.Properties["roughness"] = 0.5; break;
                case "core.layer": node.Properties["mask"] = 1.0; break;
                case "core.dissolve": node.Properties["threshold"] = 0.5; node.Properties["edgeWidth"] = 0.05; break;
                case "core.shell": node.Properties["offset"] = 0.02; break;
                case "core.vertexMotion": node.Properties["strength"] = 0.02; node.Properties["speed"] = 1.0; node.Properties["frequency"] = 2.0; break;
                case "core.uvDistort": node.Properties["strength"] = 0.1; node.Properties["speed"] = 1.0; node.Properties["scale"] = 1.0; break;
            }
            return node;
        }
        private static JArray Vector(double x, double y) { return new JArray(x, y); }
        private static string TypeName(JToken token) { return token == null ? "color" : token.Value<string>(); }
    }
}
