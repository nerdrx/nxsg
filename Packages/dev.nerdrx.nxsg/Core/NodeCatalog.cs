using System;
using System.Collections.Generic;
using System.Linq;
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
            "core.musgrave", "core.voronoi", "core.checker", "core.wave", "core.gradient", "core.uvTile", "core.posterize",
            "core.add", "core.subtract", "core.divide", "core.minimum", "core.maximum", "core.mix", "core.emission", "core.oneMinus", "core.clamp",
            "core.absolute", "core.power", "core.sqrt", "core.sine", "core.cosine", "core.fraction", "core.floor", "core.ceil", "core.round",
            "core.step", "core.smoothstep", "core.remap", "core.pingPong",
            "core.splitColor", "core.combineColor", "core.luminance", "core.contrast", "core.saturation", "core.hueShift", "core.colorAdjust", "core.colorMask", "core.replaceColor", "core.splitUV", "core.combineUV",
            "core.position", "core.normalDirection", "core.viewDirection", "core.vertexColor", "core.cameraDistance", "core.screenUV",
            "core.circleMask", "core.boxMask", "core.polygonMask", "core.starMask", "core.radialRays", "core.spiral", "core.brick", "core.hexGrid",
            "core.triplanarTexture", "core.matcapTexture", "core.rimGlow", "core.heightMask", "core.slopeMask", "core.distanceFade",
            "core.wireframe",
            "core.constant", "core.parameter", "core.uv0", "core.texture2D", "core.multiply",
            "core.toonSurface", "core.unlitSurface", "core.pbrSurface", "core.fresnel", "core.colorRamp",
            "core.layer", "core.sticker", "core.dissolve", "core.flipbook", "core.uvDistort", "core.vertexMotion",
            "core.audioLink", "core.shell", "core.normalMap", "core.ltcgi", "core.darknessGlow", "core.previewVector", "core.particleSurface", "core.particleColor", "core.particleInfo", "core.surfaceParticles", "core.output",
            "core.volumeSurface", "core.rayPosition", "core.sdfSphere", "core.sdfBox", "core.sdfTorus", "core.sdfBlend"
        };

        private static readonly Dictionary<string, string[]> Inputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core.ramp"] = new[] { "value" },
            ["core.polarUV"] = new[] { "uv" }, ["core.uvRotate"] = new[] { "uv", "angle" },
            ["core.objectUV"] = new string[0], ["core.worldUV"] = new string[0],
            ["core.value"] = new string[0], ["core.time"] = new string[0],
            ["core.uvTransform"] = new[] { "uv" }, ["core.uvScroll"] = new[] { "uv", "time" },
            ["core.noise"] = new[] { "x", "uv", "position", "time" },
            ["core.musgrave"] = new[] { "uv", "position", "time" }, ["core.voronoi"] = new[] { "uv", "position", "time" },
            ["core.checker"] = new[] { "uv", "position", "time" }, ["core.wave"] = new[] { "uv", "position", "time" }, ["core.add"] = new[] { "a", "b" },
            ["core.subtract"] = new[] { "a", "b" }, ["core.divide"] = new[] { "a", "b" },
            ["core.minimum"] = new[] { "a", "b" }, ["core.maximum"] = new[] { "a", "b" },
            ["core.mix"] = new[] { "a", "b", "factor" }, ["core.emission"] = new[] { "color", "strength" },
            ["core.oneMinus"] = new[] { "color" }, ["core.clamp"] = new[] { "color" },
            ["core.absolute"] = new[] { "a" }, ["core.power"] = new[] { "a", "b" }, ["core.sqrt"] = new[] { "a" }, ["core.sine"] = new[] { "a" }, ["core.cosine"] = new[] { "a" }, ["core.fraction"] = new[] { "a" }, ["core.floor"] = new[] { "a" }, ["core.ceil"] = new[] { "a" }, ["core.round"] = new[] { "a" },
            ["core.step"] = new[] { "a", "b" }, ["core.smoothstep"] = new[] { "value", "low", "high" }, ["core.remap"] = new[] { "value", "inMin", "inMax", "outMin", "outMax" }, ["core.pingPong"] = new[] { "value", "length" },
            ["core.splitColor"] = new[] { "color" }, ["core.combineColor"] = new[] { "r", "g", "b", "a" }, ["core.luminance"] = new[] { "color" }, ["core.contrast"] = new[] { "color", "amount", "pivot" }, ["core.saturation"] = new[] { "color", "amount" }, ["core.hueShift"] = new[] { "color", "hue" }, ["core.colorAdjust"] = new[] { "color", "hue", "saturation", "lift", "gamma", "gain", "contrast", "exposure" }, ["core.colorMask"] = new[] { "color", "target", "tolerance", "softness" }, ["core.replaceColor"] = new[] { "color", "target", "replacement", "tolerance", "softness", "factor" }, ["core.splitUV"] = new[] { "uv" }, ["core.combineUV"] = new[] { "u", "v" },
            ["core.position"] = new string[0], ["core.normalDirection"] = new string[0], ["core.viewDirection"] = new string[0], ["core.vertexColor"] = new string[0], ["core.cameraDistance"] = new string[0], ["core.screenUV"] = new string[0],
            ["core.circleMask"] = new[] { "uv" }, ["core.boxMask"] = new[] { "uv" }, ["core.polygonMask"] = new[] { "uv" }, ["core.starMask"] = new[] { "uv" }, ["core.radialRays"] = new[] { "uv" }, ["core.spiral"] = new[] { "uv" }, ["core.brick"] = new[] { "uv" }, ["core.hexGrid"] = new[] { "uv" },
            ["core.triplanarTexture"] = new[] { "position", "normal" }, ["core.matcapTexture"] = new[] { "normal" }, ["core.rimGlow"] = new[] { "color", "power" }, ["core.heightMask"] = new[] { "position" }, ["core.slopeMask"] = new[] { "normal" }, ["core.distanceFade"] = new string[0],
            ["core.wireframe"] = new string[0],
            ["core.constant"] = new string[0], ["core.parameter"] = new string[0],
            ["core.uv0"] = new string[0], ["core.texture2D"] = new[] { "uv" },
            ["core.multiply"] = new[] { "a", "b" }, ["core.toonSurface"] = new[] { "albedo", "normal", "emission", "opacity", "displacement" },
            ["core.unlitSurface"] = new[] { "albedo", "emission", "opacity", "displacement" },
            ["core.pbrSurface"] = new[] { "albedo", "emission", "opacity", "displacement", "normal", "metallic", "roughness" },
            ["core.particleSurface"] = new[] { "albedo", "emission", "opacity" },
            ["core.particleColor"] = new string[0], ["core.particleInfo"] = new string[0],
            ["core.surfaceParticles"] = new[] { "base", "albedo", "emission", "opacity", "mask", "time", "density", "emissionRate", "size", "lifetime", "speed", "gravity", "spread", "edgeSharpness" },
            ["core.fresnel"] = new string[0], ["core.colorRamp"] = new[] { "value" },
            ["core.layer"] = new[] { "base", "overlay", "mask" }, ["core.sticker"] = new[] { "base", "uv", "mask" },
            ["core.dissolve"] = new[] { "value", "threshold" }, ["core.flipbook"] = new[] { "uv", "time" },
            ["core.uvDistort"] = new[] { "uv", "strength", "mask", "time", "flow" },
            ["core.gradient"] = new[] { "uv" }, ["core.uvTile"] = new[] { "uv" }, ["core.posterize"] = new[] { "value", "levels" }, ["core.vertexMotion"] = new[] { "time", "strength" },
            ["core.audioLink"] = new string[0], ["core.shell"] = new[] { "base", "layer", "offset" },
            ["core.normalMap"] = new[] { "color" }, ["core.ltcgi"] = new[] { "albedo", "normal", "roughness", "metallic", "strength" }, ["core.darknessGlow"] = new[] { "color", "strength", "threshold", "softness" },
            ["core.previewVector"] = new[] { "uv", "normal" },
            ["core.output"] = new[] { "surface" },
            ["core.volumeSurface"] = new[] { "density", "color", "emission", "distance" },
            ["core.rayPosition"] = new string[0], ["core.sdfSphere"] = new[] { "position", "radius" },
            ["core.sdfBox"] = new[] { "position", "size" }, ["core.sdfTorus"] = new[] { "position", "radius", "thickness" },
            ["core.sdfBlend"] = new[] { "a", "b", "smoothing" }
        };

        private static readonly Dictionary<string, string[]> Outputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["core.ramp"] = new[] { "value" },
            ["core.polarUV"] = new[] { "uv" }, ["core.uvRotate"] = new[] { "uv" },
            ["core.objectUV"] = new[] { "uv" }, ["core.worldUV"] = new[] { "uv" },
            ["core.value"] = new[] { "value" }, ["core.time"] = new[] { "value" },
            ["core.uvTransform"] = new[] { "uv" }, ["core.uvScroll"] = new[] { "uv" },
            ["core.noise"] = new[] { "color", "value" },
            ["core.musgrave"] = new[] { "color", "value" }, ["core.voronoi"] = new[] { "color", "value" },
            ["core.checker"] = new[] { "color", "value" }, ["core.wave"] = new[] { "color", "value" }, ["core.add"] = new[] { "value" },
            ["core.subtract"] = new[] { "value" }, ["core.divide"] = new[] { "value" },
            ["core.minimum"] = new[] { "value" }, ["core.maximum"] = new[] { "value" },
            ["core.mix"] = new[] { "value" }, ["core.emission"] = new[] { "color" },
            ["core.oneMinus"] = new[] { "color" }, ["core.clamp"] = new[] { "color" },
            ["core.absolute"] = new[] { "value" }, ["core.power"] = new[] { "value" }, ["core.sqrt"] = new[] { "value" }, ["core.sine"] = new[] { "value" }, ["core.cosine"] = new[] { "value" }, ["core.fraction"] = new[] { "value" }, ["core.floor"] = new[] { "value" }, ["core.ceil"] = new[] { "value" }, ["core.round"] = new[] { "value" }, ["core.step"] = new[] { "value" }, ["core.smoothstep"] = new[] { "value" }, ["core.remap"] = new[] { "value" }, ["core.pingPong"] = new[] { "value" },
            ["core.splitColor"] = new[] { "r", "g", "b", "a" }, ["core.combineColor"] = new[] { "color" }, ["core.luminance"] = new[] { "value" }, ["core.contrast"] = new[] { "color" }, ["core.saturation"] = new[] { "color" }, ["core.hueShift"] = new[] { "color" }, ["core.colorAdjust"] = new[] { "color" }, ["core.colorMask"] = new[] { "value" }, ["core.replaceColor"] = new[] { "color" }, ["core.splitUV"] = new[] { "u", "v" }, ["core.combineUV"] = new[] { "uv" },
            ["core.position"] = new[] { "position" }, ["core.normalDirection"] = new[] { "normal" }, ["core.viewDirection"] = new[] { "direction" }, ["core.vertexColor"] = new[] { "color", "alpha" }, ["core.cameraDistance"] = new[] { "value" }, ["core.screenUV"] = new[] { "uv" },
            ["core.circleMask"] = new[] { "value" }, ["core.boxMask"] = new[] { "value" }, ["core.polygonMask"] = new[] { "value" }, ["core.starMask"] = new[] { "value" }, ["core.radialRays"] = new[] { "value" }, ["core.spiral"] = new[] { "value" }, ["core.brick"] = new[] { "value" }, ["core.hexGrid"] = new[] { "value" },
            ["core.triplanarTexture"] = new[] { "color" }, ["core.matcapTexture"] = new[] { "color" }, ["core.rimGlow"] = new[] { "color" }, ["core.heightMask"] = new[] { "value" }, ["core.slopeMask"] = new[] { "value" }, ["core.distanceFade"] = new[] { "value" },
            ["core.wireframe"] = new[] { "value" },
            ["core.constant"] = new[] { "value" }, ["core.parameter"] = new[] { "value" },
            ["core.uv0"] = new[] { "uv" }, ["core.texture2D"] = new[] { "color", "alpha" },
            ["core.multiply"] = new[] { "value" }, ["core.toonSurface"] = new[] { "surface" },
            ["core.unlitSurface"] = new[] { "surface" }, ["core.pbrSurface"] = new[] { "surface" },
            ["core.surfaceParticles"] = new[] { "surface" },
            ["core.particleSurface"] = new[] { "surface" }, ["core.particleColor"] = new[] { "color", "alpha" }, ["core.particleInfo"] = new[] { "age", "random" },
            ["core.fresnel"] = new[] { "value" }, ["core.colorRamp"] = new[] { "color" },
            ["core.layer"] = new[] { "color" }, ["core.sticker"] = new[] { "color" },
            ["core.dissolve"] = new[] { "mask", "edge" }, ["core.flipbook"] = new[] { "uv" },
            ["core.uvDistort"] = new[] { "uv", "offset" },
            ["core.gradient"] = new[] { "value", "color" }, ["core.uvTile"] = new[] { "uv" }, ["core.posterize"] = new[] { "value" }, ["core.vertexMotion"] = new[] { "value" },
            ["core.audioLink"] = new[] { "value" }, ["core.shell"] = new[] { "surface" },
            ["core.normalMap"] = new[] { "normal" }, ["core.ltcgi"] = new[] { "color" }, ["core.darknessGlow"] = new[] { "color" },
            ["core.previewVector"] = new[] { "color" },
            ["core.output"] = new string[0],
            ["core.volumeSurface"] = new[] { "surface" }, ["core.rayPosition"] = new[] { "position" },
            ["core.sdfSphere"] = new[] { "distance" }, ["core.sdfBox"] = new[] { "distance" },
            ["core.sdfTorus"] = new[] { "distance" }, ["core.sdfBlend"] = new[] { "distance" }
        };

        public static IEnumerable<string> All { get { return Operations.Concat(FeatureNodes.All); } }
        public static bool IsKnown(string operation) { return operation != null && (Inputs.ContainsKey(operation) || FeatureNodes.IsKnown(operation)); }
        public static string Category(string operation)
        {
            if (FeatureNodes.TryGet(operation, out var feature)) return feature.Category;
            switch (operation)
            {
                case "core.ramp": return "Math";
                case "core.uv0": case "core.uvTransform": case "core.uvScroll": case "core.uvRotate":
                case "core.uvTile": case "core.polarUV": case "core.objectUV": case "core.worldUV": return "Coordinates";
                case "core.value": case "core.time": case "core.constant": case "core.parameter": return "Inputs";
                case "core.gradient": case "core.texture2D": case "core.noise": case "core.musgrave": case "core.voronoi": case "core.checker": case "core.wave": return "Textures";
                case "core.add": case "core.subtract": case "core.multiply": case "core.divide":
                case "core.minimum": case "core.maximum": case "core.mix": case "core.oneMinus":
                case "core.clamp": return "Math";
                case "core.absolute": case "core.power": case "core.sqrt": case "core.sine": case "core.cosine": case "core.fraction": case "core.floor": case "core.ceil": case "core.round": case "core.step": case "core.smoothstep": case "core.remap": case "core.pingPong": return "Math";
                case "core.emission": case "core.toonSurface": case "core.unlitSurface": case "core.pbrSurface":
                case "core.shell": case "core.normalMap": case "core.ltcgi": case "core.darknessGlow": case "core.output": return "Surface";
                case "core.posterize": case "core.fresnel": case "core.colorRamp": case "core.layer": case "core.sticker":
                case "core.dissolve": return "Color";
                case "core.splitColor": case "core.combineColor": case "core.luminance": case "core.contrast": case "core.saturation": case "core.hueShift": case "core.colorAdjust": case "core.colorMask": case "core.replaceColor": return "Color";
                case "core.splitUV": case "core.combineUV": return "Coordinates";
                case "core.position": case "core.normalDirection": case "core.viewDirection": case "core.vertexColor": case "core.cameraDistance": return "Inputs";
                case "core.screenUV": return "Coordinates";
                case "core.circleMask": case "core.boxMask": case "core.polygonMask": case "core.starMask": case "core.radialRays": case "core.spiral": case "core.brick": case "core.hexGrid": return "Math";
                case "core.triplanarTexture": case "core.matcapTexture": return "Textures";
                case "core.rimGlow": case "core.heightMask": case "core.slopeMask": case "core.distanceFade": return "Color";
                case "core.wireframe": return "Color";
                case "core.flipbook": case "core.uvDistort": case "core.vertexMotion": return "Animation";
                case "core.audioLink": case "core.particleColor": case "core.particleInfo": return "Inputs";
                case "core.surfaceParticles": case "core.particleSurface": return "Surface";
                case "core.volumeSurface": case "core.rayPosition": case "core.sdfSphere": case "core.sdfBox": case "core.sdfTorus": case "core.sdfBlend": return "Volumes";
                default: return "Other";
            }
        }
        public static string[] Ports(string operation, bool output)
        {
            if (FeatureNodes.IsKnown(operation)) return FeatureNodes.Ports(operation, output);
            string[] ports;
            return (output ? Outputs : Inputs).TryGetValue(operation ?? string.Empty, out ports) ? (string[])ports.Clone() : new string[0];
        }
        public static string Title(string operation)
        {
            if (FeatureNodes.TryGet(operation, out var feature)) return feature.Title;
            switch (operation)
            {
                case "core.position": return "Position";
                case "core.normalDirection": return "Normal Direction";
                case "core.viewDirection": return "View Direction";
                case "core.vertexColor": return "Vertex Color";
                case "core.cameraDistance": return "Camera Distance";
                case "core.screenUV": return "Screen UVs";
                case "core.circleMask": return "Circle Mask";
                case "core.boxMask": return "Box Mask";
                case "core.polygonMask": return "Polygon Mask";
                case "core.starMask": return "Star Mask";
                case "core.radialRays": return "Radial Rays";
                case "core.spiral": return "Spiral";
                case "core.brick": return "Brick Pattern";
                case "core.hexGrid": return "Hex Grid";
                case "core.triplanarTexture": return "Triplanar Texture";
                case "core.matcapTexture": return "Matcap Texture";
                case "core.rimGlow": return "Rim Glow";
                case "core.heightMask": return "Height Mask";
                case "core.slopeMask": return "Slope Mask";
                case "core.distanceFade": return "Distance Fade";
                case "core.wireframe": return "Wireframe";

                case "core.ramp": return "Ramp";
                case "core.polarUV": return "Polar UVs";
                case "core.uvRotate": return "Rotate UVs";
                case "core.objectUV": return "Object Planar UVs";
                case "core.worldUV": return "World Planar UVs";
                case "core.value": return "Value"; case "core.time": return "Time";
                case "core.uvTransform": return "UV Transform"; case "core.uvScroll": return "UV Scroll";
                case "core.musgrave": return "Musgrave"; case "core.voronoi": return "Voronoi"; case "core.checker": return "Checkerboard"; case "core.wave": return "Waves";
                case "core.gradient": return "Gradient"; case "core.uvTile": return "UV Tile / Mirror"; case "core.posterize": return "Posterize";
                case "core.noise": return "Noise"; case "core.add": return "Add"; case "core.subtract": return "Subtract";
                case "core.divide": return "Divide"; case "core.minimum": return "Minimum"; case "core.maximum": return "Maximum";
                case "core.mix": return "Mix";
                case "core.emission": return "Emission"; case "core.oneMinus": return "Invert";
                case "core.clamp": return "Clamp"; case "core.constant": return "Color";
                case "core.absolute": return "Absolute"; case "core.power": return "Power"; case "core.sqrt": return "Square Root"; case "core.sine": return "Sine"; case "core.cosine": return "Cosine"; case "core.fraction": return "Fraction"; case "core.floor": return "Round Down"; case "core.ceil": return "Round Up"; case "core.round": return "Round";
                case "core.step": return "Step"; case "core.smoothstep": return "Smoothstep"; case "core.remap": return "Remap"; case "core.pingPong": return "Ping Pong";
                case "core.splitColor": return "Split Color"; case "core.combineColor": return "Combine Color"; case "core.luminance": return "Luminance"; case "core.contrast": return "Contrast"; case "core.saturation": return "Saturation"; case "core.hueShift": return "Hue Shift"; case "core.colorAdjust": return "Color Adjust"; case "core.colorMask": return "Color Mask"; case "core.replaceColor": return "Replace Color"; case "core.splitUV": return "Split UV"; case "core.combineUV": return "Combine UV";
                case "core.parameter": return "Parameter"; case "core.uv0": return "UV Coordinates";
                case "core.texture2D": return "Texture"; case "core.multiply": return "Multiply";
                case "core.toonSurface": return "Toon Surface"; case "core.unlitSurface": return "Unlit Surface";
                case "core.surfaceParticles": return "Surface Particles";
                case "core.particleSurface": return "Particle Surface"; case "core.particleColor": return "Particle Color"; case "core.particleInfo": return "Particle Info";
                case "core.pbrSurface": return "PBR Surface"; case "core.fresnel": return "Fresnel";
                case "core.colorRamp": return "Color Ramp"; case "core.layer": return "Layer";
                case "core.sticker": return "Sticker"; case "core.dissolve": return "Dissolve";
                case "core.flipbook": return "Flipbook"; case "core.uvDistort": return "Distortion";
                case "core.vertexMotion": return "Vertex Motion"; case "core.audioLink": return "Audio Link";
                case "core.shell": return "Shell"; case "core.normalMap": return "Normal Map"; case "core.ltcgi": return "LTCGI Lighting"; case "core.darknessGlow": return "Darkness Glow";
                case "core.output": return "Output";
                case "core.volumeSurface": return "Volume Surface";
                case "core.rayPosition": return "Ray Position";
                case "core.sdfSphere": return "SDF Sphere";
                case "core.sdfBox": return "SDF Box";
                case "core.sdfTorus": return "SDF Torus";
                case "core.sdfBlend": return "SDF Blend";
                default: return operation;
            }
        }
        public static string Description(string operation)
        {
            if (FeatureNodes.TryGet(operation, out var feature)) return feature.Description;
            switch (operation)
            {
                case "core.position": return "Get a point on the mesh in object or world space. Feed Position on 3D textures and height masks.";
                case "core.normalDirection": return "Get the mesh surface direction in object or world space. This is a direction, not a normal-map texture.";
                case "core.viewDirection": return "Direction from the surface toward the camera in world space.";
                case "core.vertexColor": return "Read painted mesh RGBA and alpha. Meshes without vertex colors normally return white.";
                case "core.cameraDistance": return "Distance from each surface point to the camera in world units.";
                case "core.screenUV": return "Coordinates across the screen, from zero to one. This mapping follows the camera.";
                case "core.circleMask": return "A filled circle centered in UV space. Radius controls size; softness blurs the edge.";
                case "core.boxMask": return "A filled rectangle centered in UV space. Set width, height and edge softness.";
                case "core.polygonMask": return "A filled regular polygon. Choose sides, radius and rotation.";
                case "core.starMask": return "A pointed star mask. Inner and outer radii control the valleys and tips.";
                case "core.radialRays": return "Alternating rays around the UV center. Animate rotation for a spinning sunburst.";
                case "core.spiral": return "A curved spiral mask. Adjust turns, line width and rotation.";
                case "core.brick": return "Staggered brick rows. White is brick; black is mortar. Connect to Mix or a Color Ramp.";
                case "core.hexGrid": return "A honeycomb outline mask. Grid scale changes cell count; width thickens the lines.";
                case "core.triplanarTexture": return "Project a texture along three axes and blend by surface direction. Defaults to object space; connected Position and Normal must use matching space.";
                case "core.matcapTexture": return "Map a texture using the camera-facing surface normal, for stylized highlights. Connect a world-space normal to override the mesh normal.";
                case "core.rimGlow": return "Color the silhouette facing away from the camera. Connect to Emission for glow; Power controls rim tightness.";
                case "core.heightMask": return "Fade between low and high coordinates along one axis. Uses object position unless Position is connected.";
                case "core.slopeMask": return "Mask by the upward component of a world-space normal: zero is vertical, one faces up, minus one faces down.";
                case "core.distanceFade": return "White near the camera, black far away. Set distances in world units; connect to opacity or a mix factor.";
                case "core.wireframe": return "Mask actual triangle edges, including triangulation diagonals. Width and softness use screen pixels. Connect to color, emission or opacity; requires a PC geometry shader.";

                case "core.ramp": return "Reshape a number or noise mask with black/white points and a curve. Connect Noise value here to control its contrast.";
                case "core.polarUV": return "Wrap coordinates around a center: U is distance, V is angle. Useful for rings and radial patterns.";
                case "core.uvRotate": return "Rotate texture coordinates around a center, in degrees. Connect Time to angle to spin them.";
                case "core.objectUV": return "Project using the mesh's local X/Z position. Moves with the object; use UV Transform to adjust scale.";
                case "core.worldUV": return "Project using world X/Z position. Objects move through the pattern; use UV Transform to adjust scale.";
                case "core.value": return "An adjustable number. Use it to control strength, time, or blending.";
                case "core.time": return "Time in seconds, with speed and offset controls. Use it to animate effects.";
                case "core.uvTransform": return "Scale and shift texture coordinates to control tiling and placement.";
                case "core.uvScroll": return "Move texture coordinates over time, like flowing water or scrolling stripes.";
                case "core.noise": return "Smooth random patterns in 1D, 2D, 3D or evolving 4D. Set Animation speed to 0 to freeze.";
                case "core.musgrave": return "Layered fractal noise for clouds, terrain and smoky masks. Choose soft, ridged or turbulence.";
                case "core.voronoi": return "Cell-like patterns from distance to scattered points. Useful for cracks, scales and organic masks.";
                case "core.checker": return "Alternating black and white squares or 3D cubes. Useful for patterns and checking UVs.";
                case "core.wave": return "Smooth repeating bands or rings. Choose a direction, scale and animation speed.";
                case "core.add": return "Add two numbers or colors together to brighten or combine them.";
                case "core.subtract": return "Subtract B from A, using numbers or color channels.";
                case "core.divide": return "Divide A by B, using numbers or color channels. Very small divisors are limited to avoid division by zero.";
                case "core.minimum": return "Choose the lower value from A and B for each color channel.";
                case "core.maximum": return "Choose the higher value from A and B for each color channel.";
                case "core.mix": return "Blend two numbers or colors: factor 0 gives A, 1 gives B, and 0.5 mixes them equally.";
                case "core.emission": return "Add color that stays bright without lighting. Connect it to Toon Surface's emission input.";
                case "core.oneMinus": return "Invert numbers, colors or masks (1 minus input): black becomes white, and white becomes black.";
                case "core.clamp": return "Keep each color channel between 0 and 1. Values outside that range are clipped.";
                case "core.absolute": return "Get the positive magnitude of a number or color.";
                case "core.power": return "Raise a number or color to a power. Negative bases use their absolute value.";
                case "core.sqrt": return "Get a square root. Negative inputs are clamped to zero.";
                case "core.sine": return "Oscillate between -1 and 1. Input is radians (6.283 is one cycle); connect Time for motion.";
                case "core.cosine": return "Oscillate between -1 and 1, starting at 1. Input is radians (6.283 is one cycle).";
                case "core.fraction": return "Keep only the fractional part of a number or color.";
                case "core.floor": return "Round a number or color down to the nearest whole value.";
                case "core.ceil": return "Round a number or color up to the nearest whole value.";
                case "core.round": return "Round a number or color to the nearest whole value.";
                case "core.step": return "Output zero below threshold A and one at or above it using signal B.";
                case "core.smoothstep": return "Make a smooth 0 to 1 transition between low and high.";
                case "core.remap": return "Map a value from one range into another range.";
                case "core.pingPong": return "Repeat a value back and forth between zero and length.";
                case "core.splitColor": return "Read red, green, blue and alpha channels from a color.";
                case "core.combineColor": return "Build a color from red, green, blue and alpha channels.";
                case "core.luminance": return "Convert a color to its perceived brightness.";
                case "core.contrast": return "Adjust color contrast around a pivot.";
                case "core.saturation": return "Adjust color saturation.";
                case "core.colorAdjust": return "Adjust hue, saturation, lift, gamma, gain, contrast and exposure together. Unconnected neutral settings compile away; connected controls remain animatable. Alpha is preserved.";
                case "core.hueShift": return "Rotate color hue in HSV or OKLab. 0 leaves it unchanged; 1 is a full turn. Negative values wrap backward. Preserves alpha and HDR range. Connect Time or AudioLink to animate.";
                case "core.colorMask": return "Match RGB distance in the graph working space to a target color. Tolerance and softness use RGB distance; no color space conversion is applied.";
                case "core.replaceColor": return "Replace pixels near a target RGB color in the graph working space, preserving input alpha. Tolerance, softness and factor control the blend.";
                case "core.splitUV": return "Read U and V components from UV coordinates.";
                case "core.combineUV": return "Build UV coordinates from U and V values.";
                case "core.constant": return "Choose a solid color to use on its own or combine with other nodes.";
                case "core.parameter": return "Read a declared property that can control your material.";
                case "core.uv0": return "Choose mesh UV0–UV3, object/world mapping, polar or explicitly camera-relative mapping.";
                case "core.texture2D": return "Read an image using texture coordinates. Color supplies RGBA; Alpha reads only its transparency channel (0–1).";
                case "core.multiply": return "Multiply numbers or colors. Use colors to tint or darken. White keeps the other color unchanged.";
                case "core.toonSurface": return "Give your base color cartoon-style lighting, with optional emission, opacity, and displacement.";
                case "core.unlitSurface": return "Build a surface with color and emission without lighting.";
                case "core.pbrSurface": return "Build a physically based surface with color, normal, metallic, and roughness controls.";
                case "core.surfaceParticles": return "Emit shader-driven particles from the mesh wearing this material. Connect your surface to Base. PC geometry pass; particles follow the current mesh pose.";
                case "core.particleSurface": return "Transparent unlit particles with alpha or additive blending. Automatically applies particle color and lifetime alpha. Optional soft intersections need camera depth.";
                case "core.particleColor": return "Read particle or mesh vertex color and alpha. Particle Surface already applies these automatically; use this node for other effects.";
                case "core.fresnel": return "Compute an edge mask from a value and power.";
                case "core.colorRamp": return "Map a scalar value through a bounded color ramp.";
                case "core.layer": return "Blend an overlay color over a base color with a mask.";
                case "core.sticker": return "Project a texture onto UVs with optional masking and transform controls.";
                case "core.dissolve": return "Compare a value to a threshold and output dissolve and edge masks.";
                case "core.flipbook": return "Animate UVs through a rows by columns texture atlas.";
                case "core.gradient": return "A linear, radial or angular mask for Color Ramp, distortion or dissolve.";
                case "core.uvTile": return "Repeat, mirror or clamp UV tiles with scale and offset.";
                case "core.posterize": return "Turn a smooth number or mask into a chosen number of distinct steps.";
                case "core.uvDistort": return "Warp UVs with noise, waves, swirl, ripple, flow, pixelate or lens. Mask and animate the effect. Zero strength leaves UVs unchanged.";
                case "core.vertexMotion": return "Drive vertex motion from time and strength inputs.";
                case "core.audioLink": return "Read a smoothed audio band (0–1), scale with Gain, or map it between Minimum and Maximum. Fallback is used without AudioLink.";
                case "core.particleInfo": return "Read Surface Particles age (0 at birth, 1 at death) and a stable random value (0–1) per particle. Use inside particle appearance and motion branches.";
                case "core.shell": return "Wrap a surface or another Shell with a transparent layer. Up to 8 shell passes; nesting in Layer adds offsets.";
                case "core.normalMap": return "Decode a normal map color into a normal vector.";
                case "core.darknessGlow": return "Emission that fades under bright ambient/main light. Connect Color to Emission. Does not measure additional pixel lights or LTCGI.";
                case "core.ltcgi": return "Optional LTCGI lighting from an installed LTCGI package and active world controller. Normal input expects a tangent-space vector3. Connect Color to Surface Emission; use Add to combine with existing emission.";
                case "core.output": return "The final surface of your shader. Connect a Surface or Shell here.";
                case "core.volumeSurface": return "Ray-marched volume surface with bounded object-space distance-field sampling.";
                case "core.rayPosition": return "Object-space sample position for signed-distance volume nodes. Use only in a Volume Surface graph.";
                case "core.sdfSphere": return "Signed distance to a sphere at the current object-space ray position.";
                case "core.sdfBox": return "Signed distance to an axis-aligned box at the current object-space ray position.";
                case "core.sdfTorus": return "Signed distance to a torus at the current object-space ray position.";
                case "core.sdfBlend": return "Combine two signed distances with smooth union, subtraction, or intersection.";
                default: return string.Empty;
            }
        }
        public static string Aliases(string operation)
        {
            if (FeatureNodes.IsKnown(operation))
            {
                if (operation == "core.tessellation") return "tessellation tesselation subdivide subdivison subdivision displacement GPU geometry";
                return operation.Replace("core.", "").Replace("UV", " uv").Replace("Mask", " mask");
            }
            switch (operation)
            {
                case "core.position": return "Position";
                case "core.normalDirection": return "Normal Direction";
                case "core.viewDirection": return "View Direction";
                case "core.vertexColor": return "Vertex Color";
                case "core.cameraDistance": return "Camera Distance";
                case "core.screenUV": return "Screen UVs";
                case "core.circleMask": return "Circle Mask";
                case "core.boxMask": return "Box Mask";
                case "core.polygonMask": return "Polygon Mask";
                case "core.starMask": return "Star Mask";
                case "core.radialRays": return "Radial Rays";
                case "core.spiral": return "Spiral";
                case "core.brick": return "Brick Pattern";
                case "core.hexGrid": return "Hex Grid";
                case "core.triplanarTexture": return "Triplanar Texture";
                case "core.matcapTexture": return "Matcap Texture";
                case "core.rimGlow": return "Rim Glow";
                case "core.heightMask": return "Height Mask";
                case "core.slopeMask": return "Slope Mask";
                case "core.distanceFade": return "Distance Fade";
                case "core.wireframe": return "Wireframe";

                case "core.ramp": return "gradient contrast remap levels curve mask threshold";
                case "core.polarUV": return "polar radial circle rings angle radius texture coordinates";
                case "core.uvRotate": return "rotation spin pivot texture coordinates";
                case "core.objectUV": return "local object space planar projection mapping xz";
                case "core.worldUV": return "world space planar projection mapping xz";
                case "core.value": return "constant scalar"; case "core.time": return "clock animation";
                case "core.uvTransform": return "scale offset tiling"; case "core.uvScroll": return "pan animate";
                case "core.musgrave": return "fractal fbm clouds smoke turbulence ridged"; case "core.voronoi": return "cells cellular worley distance"; case "core.checker": return "checkerboard squares grid cubes"; case "core.wave": return "waves bands rings sine stripes";
                case "core.noise": return "procedural random 1d 2d 3d 4d"; case "core.add": return "plus sum";
                case "core.subtract": return "minus difference"; case "core.divide": return "division ratio safe divide";
                case "core.minimum": return "min lower"; case "core.maximum": return "max higher";
                case "core.mix": return "lerp blend"; case "core.emission": return "glow";
                case "core.oneMinus": return "invert one minus"; case "core.clamp": return "saturate";
                case "core.absolute": return "abs magnitude positive"; case "core.power": return "pow exponent raise"; case "core.sqrt": return "square root"; case "core.sine": return "sin wave"; case "core.cosine": return "cos wave"; case "core.fraction": return "frac decimal"; case "core.floor": return "round down"; case "core.ceil": return "round up"; case "core.round": return "nearest integer";
                case "core.step": return "threshold cutoff"; case "core.smoothstep": return "smooth transition"; case "core.remap": return "range map"; case "core.pingPong": return "repeat bounce loop";
                case "core.splitColor": return "rgba channels"; case "core.combineColor": return "rgba channels"; case "core.luminance": return "brightness grayscale"; case "core.contrast": return "color contrast"; case "core.saturation": return "color saturation"; case "core.hueShift": return "hue hsv oklab rainbow color rotation shift"; case "core.colorAdjust": return "hue saturation lift gamma gain contrast exposure brightness hsv oklab grading correction"; case "core.splitUV": return "uv components"; case "core.combineUV": return "uv components";
                case "core.colorMask": return "colour chroma key select isolate mask tolerance";
                case "core.replaceColor": return "recolor recolour colour swap replace target";
                case "core.constant": return "rgb rgba colour";
                case "core.multiply": return "tint darken blend";
                case "core.toonSurface": return "anime cel cartoon shading";
                case "core.unlitSurface": return "flat no lighting"; case "core.pbrSurface": return "physically based lit material";
                case "core.surfaceParticles": return "gpu particles mesh emitter sparkles surface embers aura geometry";
                case "core.particleSurface": return "sparkles embers smoke fluff transparent additive billboard shuriken";
                case "core.particleColor": return "vertex colour lifetime fade alpha shuriken";
                case "core.particleInfo": return "age lifetime random seed particle birth death";
                case "core.fresnel": return "edge rim grazing angle"; case "core.colorRamp": return "gradient palette lookup";
                case "core.layer": return "overlay composite blend"; case "core.sticker": return "decal projected texture";
                case "core.dissolve": return "cutout burn edge mask"; case "core.flipbook": return "texture atlas animation";
                case "core.gradient": return "linear radial angular mask ramp"; case "core.uvTile": return "repeat mirror clamp wrap tile"; case "core.posterize": return "steps quantize pixel banding";
                case "core.uvDistort": return "uv distortion warp wobble swirl ripple flow pixelate lens"; case "core.vertexMotion": return "vertex animation deformation";
                case "core.audioLink": return "audio reactive spectrum"; case "core.shell": return "outline rim extrude";
                case "core.normalMap": return "bump tangent normal";
                case "core.ltcgi": return "LTCGI area lighting emissive indirect illumination world controller";
                case "core.texture2D": return "image albedo diffuse";
                case "core.volumeSurface": return "volume ray march volumetric fog density SDF distance field";
                case "core.rayPosition": return "ray march sample object position volume";
                case "core.sdfSphere": return "signed distance sphere SDF volume";
                case "core.sdfBox": return "signed distance box SDF volume";
                case "core.sdfTorus": return "signed distance torus SDF volume";
                case "core.sdfBlend": return "SDF union subtract intersect smooth blend";
                default: return string.Empty;
            }
        }
        public static string PortType(GraphNode node, string port)
        {
            if (node == null || port == null) return null;
            if (FeatureNodes.IsKnown(node.Operation)) return FeatureNodes.PortType(node.Operation, port, FeatureNodes.PortType(node.Operation, port, false) == null);
            switch (node.Operation)
            {
                case "core.previewVector": return port == "uv" ? "vector2" : port == "normal" ? "vector3" : port == "color" ? "color" : null;
                case "core.ramp": return port == "value" ? "float" : null;
                case "core.polarUV": case "core.objectUV": case "core.worldUV": return port == "uv" ? "vector2" : null;
                case "core.uvRotate": return port == "uv" ? "vector2" : (port == "angle" ? "float" : null);
                case "core.value": case "core.time": return port == "value" ? "float" : null;
                case "core.uvTransform": case "core.uvScroll": return port == "uv" ? "vector2" : (port == "time" ? "float" : null);
                case "core.noise": case "core.musgrave": case "core.voronoi": case "core.checker": case "core.wave": return port == "uv" ? "vector2" : port == "position" ? "vector3" : (port == "time" || port == "x" || port == "value") ? "float" : port == "color" ? "color" : null;
                case "core.add": case "core.subtract": case "core.divide": case "core.minimum": case "core.maximum": return port == "a" || port == "b" || port == "value" ? "color" : null;
                case "core.mix": return port == "factor" ? "float" : (port == "a" || port == "b" || port == "value" ? "color" : null);
                case "core.emission": return port == "strength" ? "float" : (port == "color" ? "color" : null);
                case "core.oneMinus": case "core.clamp": return port == "color" ? "color" : null;
                case "core.absolute": case "core.power": case "core.sqrt": case "core.sine": case "core.cosine": case "core.fraction": case "core.floor": case "core.ceil": case "core.round": return port == "a" || port == "b" || port == "value" ? "color" : null;
                case "core.step": return port == "a" || port == "b" || port == "value" ? "float" : null;
                case "core.smoothstep": return port == "value" || port == "low" || port == "high" ? "float" : null;
                case "core.remap": return port == "value" || port == "inMin" || port == "inMax" || port == "outMin" || port == "outMax" ? "float" : null;
                case "core.pingPong": return port == "value" || port == "length" ? "float" : null;
                case "core.splitColor": return port == "color" ? "color" : port == "r" || port == "g" || port == "b" || port == "a" ? "float" : null;
                case "core.combineColor": return port == "r" || port == "g" || port == "b" || port == "a" ? "float" : port == "color" ? "color" : null;
                case "core.luminance": return port == "color" ? "color" : port == "value" ? "float" : null;
                case "core.contrast": return port == "color" ? "color" : port == "amount" || port == "pivot" ? "float" : null;
                case "core.saturation": return port == "color" ? "color" : port == "amount" ? "float" : null;
                case "core.hueShift": return port == "color" ? "color" : port == "hue" ? "float" : null;
                case "core.colorAdjust": return port == "color" ? "color" : new[] { "hue", "saturation", "lift", "gamma", "gain", "contrast", "exposure" }.Contains(port) ? "float" : null;
                case "core.colorMask": return port == "color" || port == "target" ? "color" : port == "tolerance" || port == "softness" || port == "value" ? "float" : null;
                case "core.replaceColor": return port == "color" || port == "target" || port == "replacement" ? "color" : port == "tolerance" || port == "softness" || port == "factor" ? "float" : null;
                case "core.splitUV": return port == "uv" ? "vector2" : port == "u" || port == "v" ? "float" : null;
                case "core.combineUV": return port == "u" || port == "v" ? "float" : port == "uv" ? "vector2" : null;
                case "core.position": return port == "position" ? "vector3" : null;
                case "core.normalDirection": return port == "normal" ? "vector3" : null;
                case "core.viewDirection": return port == "direction" ? "vector3" : null;
                case "core.vertexColor": return port == "color" ? "color" : port == "alpha" ? "float" : null;
                case "core.cameraDistance": return port == "value" ? "float" : null;
                case "core.screenUV": return port == "uv" ? "vector2" : null;
                case "core.circleMask": case "core.boxMask": case "core.polygonMask": case "core.starMask": case "core.radialRays": case "core.spiral": case "core.brick": case "core.hexGrid": return port == "uv" ? "vector2" : port == "value" ? "float" : null;
                case "core.triplanarTexture": return port == "position" || port == "normal" ? "vector3" : port == "color" ? "color" : null;
                case "core.matcapTexture": return port == "normal" ? "vector3" : port == "color" ? "color" : null;
                case "core.rimGlow": return port == "color" ? "color" : port == "power" || port == "value" ? "float" : null;
                case "core.heightMask": return port == "position" ? "vector3" : port == "value" ? "float" : null;
                case "core.slopeMask": return port == "normal" ? "vector3" : port == "value" ? "float" : null;
                case "core.distanceFade": return port == "value" ? "float" : null;
                case "core.wireframe": return port == "value" ? "float" : null;
                case "core.constant": return port == "value" ? TypeName(node.Properties == null ? null : node.Properties["valueType"]) : null;
                case "core.parameter": return port == "value" ? "color" : null;
                case "core.uv0": return port == "uv" ? "vector2" : null;
                case "core.texture2D": return port == "uv" ? "vector2" : (port == "color" ? "color" : port == "alpha" ? "float" : null);
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
                case "core.flipbook": return port == "uv" ? "vector2" : port == "time" ? "float" : null;
                case "core.uvDistort": return port == "uv" || port == "offset" ? "vector2" : port == "flow" ? "color" : port == "strength" || port == "mask" || port == "time" ? "float" : null;
                case "core.gradient": return port == "uv" ? "vector2" : port == "color" ? "color" : port == "value" ? "float" : null;
                case "core.uvTile": return port == "uv" ? "vector2" : null;
                case "core.posterize": return port == "value" || port == "levels" ? "float" : null;
                case "core.vertexMotion": return port == "time" || port == "strength" || port == "value" ? "float" : null;
                case "core.audioLink": return port == "value" ? "float" : null;
                case "core.particleInfo": return port == "age" || port == "random" ? "float" : null;
                case "core.surfaceParticles": return port == "base" || port == "surface" ? "surface" : port == "albedo" || port == "emission" ? "color" : port == "opacity" || port == "mask" || port == "time" || port == "density" || port == "emissionRate" || port == "size" || port == "lifetime" || port == "speed" || port == "gravity" || port == "spread" || port == "edgeSharpness" ? "float" : null;
                case "core.particleColor": return port == "color" ? "color" : port == "alpha" ? "float" : null;
                case "core.particleSurface": return port == "surface" ? "surface" : port == "opacity" ? "float" : port == "albedo" || port == "emission" ? "color" : null;
                case "core.unlitSurface": return port == "surface" ? "surface" : (port == "albedo" || port == "emission" ? "color" : (port == "opacity" || port == "displacement" ? "float" : null));
                case "core.shell": return port == "base" || port == "layer" || port == "surface" ? "surface" : (port == "offset" ? "float" : null);
                case "core.normalMap": return port == "color" ? "color" : (port == "normal" ? "vector3" : null);
                case "core.darknessGlow": return port == "color" ? "color" : port == "strength" || port == "threshold" || port == "softness" ? "float" : null;
                case "core.ltcgi": return port == "albedo" ? "color" : port == "normal" ? "vector3" : port == "roughness" || port == "metallic" || port == "strength" ? "float" : port == "color" ? "color" : null;
                case "core.output": return port == "surface" ? "surface" : null;
                case "core.volumeSurface":
                    if (port == "surface") return "surface";
                    if (port == "color" || port == "emission") return "color";
                    return port == "density" || port == "distance" ? "float" : null;
                case "core.rayPosition": return port == "position" ? "vector3" : null;
                case "core.sdfSphere": return port == "position" ? "vector3" : port == "radius" || port == "distance" ? "float" : null;
                case "core.sdfBox": return port == "position" || port == "size" ? "vector3" : port == "distance" ? "float" : null;
                case "core.sdfTorus": return port == "position" ? "vector3" : port == "radius" || port == "thickness" || port == "distance" ? "float" : null;
                case "core.sdfBlend": return port == "a" || port == "b" || port == "smoothing" || port == "distance" ? "float" : null;
                default: return null;
            }
        }
        public static GraphNode Create(string operation)
        {
            if (!IsKnown(operation)) return null;
            if (FeatureNodes.IsKnown(operation)) return FeatureNodes.Create(operation);
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
                case "core.noise": node.Properties["dimensions"] = 2; node.Properties["coordinateSource"] = "uv0"; node.Properties["scale"] = 5.0; node.Properties["speed"] = 1.0; break;
                case "core.musgrave": case "core.voronoi": case "core.checker": case "core.wave":
                    node.Properties["dimensions"] = 2; node.Properties["coordinateSource"] = "uv0"; node.Properties["scale"] = 5.0; node.Properties["speed"] = 0.0;
                    if (operation == "core.musgrave") { node.Properties["octaves"] = 4; node.Properties["lacunarity"] = 2.0; node.Properties["gain"] = .5; node.Properties["mode"] = 0; }
                    if (operation == "core.voronoi") node.Properties["randomness"] = 1.0;
                    if (operation == "core.wave") { node.Properties["mode"] = 0; node.Properties["axis"] = 0; }
                    break;
                case "core.mix": node.Properties["factor"] = .5; break;
                case "core.absolute": case "core.sqrt": case "core.sine": case "core.cosine": case "core.fraction": case "core.floor": case "core.ceil": case "core.round": node.Properties["a"] = .5; break;
                case "core.power": node.Properties["a"] = .5; node.Properties["b"] = 2.0; break;
                case "core.step": node.Properties["a"] = .5; node.Properties["b"] = 0.0; break;
                case "core.smoothstep": node.Properties["value"] = 0.0; node.Properties["low"] = 0.0; node.Properties["high"] = 1.0; break;
                case "core.remap": node.Properties["value"] = 0.0; node.Properties["inMin"] = 0.0; node.Properties["inMax"] = 1.0; node.Properties["outMin"] = 0.0; node.Properties["outMax"] = 1.0; break;
                case "core.pingPong": node.Properties["value"] = 0.0; node.Properties["length"] = 1.0; break;
                case "core.combineColor": node.Properties["r"] = 0.0; node.Properties["g"] = 0.0; node.Properties["b"] = 0.0; node.Properties["a"] = 1.0; break;
                case "core.contrast": node.Properties["amount"] = 1.0; node.Properties["pivot"] = .5; break;
                case "core.saturation": node.Properties["amount"] = 1.0; break;
                case "core.hueShift": node.Properties["hue"] = 0.0; node.Properties["hueSpace"] = 0; break;
                case "core.colorAdjust": node.Properties["hueSpace"] = 0; foreach (var key in new[] { "hue", "lift", "exposure" }) node.Properties[key] = 0.0; foreach (var key in new[] { "saturation", "gamma", "gain", "contrast" }) node.Properties[key] = 1.0; break;
                case "core.colorMask": node.Properties["target"] = new JArray(1, 0, 0, 1); node.Properties["tolerance"] = .1; node.Properties["softness"] = .1; break;
                case "core.replaceColor": node.Properties["target"] = new JArray(1, 0, 0, 1); node.Properties["replacement"] = new JArray(0, 0, 1, 1); node.Properties["tolerance"] = .1; node.Properties["softness"] = .1; node.Properties["factor"] = 1.0; break;
                case "core.combineUV": node.Properties["u"] = 0.0; node.Properties["v"] = 0.0; break;
                case "core.position": node.Properties["space"] = 0; break;
                case "core.normalDirection": node.Properties["space"] = 1; break;
                case "core.circleMask": node.Properties["radius"] = .4; node.Properties["softness"] = .02; break;
                case "core.boxMask": node.Properties["width"] = .7; node.Properties["height"] = .7; node.Properties["softness"] = .02; break;
                case "core.polygonMask": node.Properties["sides"] = 6; node.Properties["radius"] = .4; node.Properties["rotation"] = 0.0; node.Properties["softness"] = .02; break;
                case "core.starMask": node.Properties["points"] = 5; node.Properties["inner"] = .2; node.Properties["outer"] = .45; node.Properties["rotation"] = 0.0; node.Properties["softness"] = .02; break;
                case "core.radialRays": node.Properties["count"] = 12; node.Properties["rotation"] = 0.0; node.Properties["softness"] = .02; break;
                case "core.spiral": node.Properties["turns"] = 3.0; node.Properties["width"] = .2; node.Properties["rotation"] = 0.0; break;
                case "core.brick": node.Properties["tilingX"] = 5.0; node.Properties["tilingY"] = 8.0; node.Properties["mortar"] = .08; break;
                case "core.hexGrid": node.Properties["scale"] = 8.0; node.Properties["width"] = .05; break;
                case "core.triplanarTexture": node.Properties["resourceId"] = ""; node.Properties["scale"] = 1.0; node.Properties["sharpness"] = 4.0; break;
                case "core.matcapTexture": node.Properties["resourceId"] = ""; break;
                case "core.rimGlow": node.Properties["power"] = 3.0; break;
                case "core.heightMask": node.Properties["low"] = 0.0; node.Properties["high"] = 1.0; node.Properties["axis"] = 1; break;
                case "core.slopeMask": node.Properties["low"] = 0.0; node.Properties["high"] = 1.0; break;
                case "core.distanceFade": node.Properties["near"] = 0.0; node.Properties["far"] = 10.0; break;
                case "core.wireframe": node.Properties["width"] = 1.0; node.Properties["softness"] = 1.0; break;
                case "core.emission": node.Properties["strength"] = 1.0; break;
                case "core.constant": node.Properties["valueType"] = "color"; node.Properties["value"] = new JArray(1, 1, 1, 1); break;
                case "core.multiply": node.Properties["valueType"] = "color"; break;
                case "core.fresnel": node.Properties["power"] = 5.0; break;
                case "core.colorRamp": node.Properties["stops"] = new JArray(new JArray(0, 0, 0, 0, 1), new JArray(1, 1, 1, 1, 1)); break;
                case "core.sticker": node.Properties["resourceId"] = ""; node.Properties["position"] = Vector(0, 0); node.Properties["size"] = Vector(1, 1); node.Properties["rotation"] = 0.0; break;
                case "core.flipbook": node.Properties["rows"] = 1; node.Properties["columns"] = 1; node.Properties["speed"] = 1.0; break;
                case "core.audioLink": node.Properties["band"] = 0; node.Properties["gain"] = 1.0; node.Properties["smoothing"] = 0.5; node.Properties["fallback"] = 0.0; node.Properties["rangeEnabled"] = 0; node.Properties["min"] = 0.0; node.Properties["max"] = 1.0; break;
                case "core.normalMap": node.Properties["strength"] = 1.0; break;
                case "core.darknessGlow": node.Properties["strength"] = 1.0; node.Properties["threshold"] = .4; node.Properties["softness"] = .2; break;
                case "core.ltcgi": node.Properties["roughness"] = .5; node.Properties["metallic"] = 0.0; node.Properties["strength"] = 1.0; break;
                case "core.surfaceParticles": node.Properties["sourceUV"] = 0; node.Properties["density"] = .1; node.Properties["emissionRate"] = .5; node.Properties["size"] = .03; node.Properties["lifetime"] = 2.0; node.Properties["speed"] = .2; node.Properties["gravity"] = 0.0; node.Properties["spread"] = .05; node.Properties["edgeSharpness"] = 0.0; node.Properties["blendMode"] = 1; node.Properties["opacity"] = 1.0; node.Properties["mask"] = 1.0; break;
                case "core.particleSurface": node.Properties["opacity"] = 1.0; node.Properties["blendMode"] = 0; node.Properties["softDistance"] = 0.0; break;
                case "core.unlitSurface": node.Properties["opacity"] = 1.0; node.Properties["displacement"] = 0.0; break;
                case "core.pbrSurface": node.Properties["opacity"] = 1.0; node.Properties["displacement"] = 0.0; node.Properties["metallic"] = 0.0; node.Properties["roughness"] = 0.5; break;
                case "core.layer": node.Properties["mask"] = 1.0; break;
                case "core.dissolve": node.Properties["threshold"] = 0.5; node.Properties["edgeWidth"] = 0.05; break;
                case "core.shell": node.Properties["offset"] = 0.02; break;
                case "core.vertexMotion": node.Properties["strength"] = 0.02; node.Properties["speed"] = 1.0; node.Properties["frequency"] = 2.0; break;
                case "core.gradient": node.Properties["mode"] = 0; node.Properties["center"] = Vector(.5,.5); node.Properties["radius"] = .5; node.Properties["angle"] = 0.0; break;
                case "core.uvTile": node.Properties["mode"] = 0; node.Properties["tiling"] = Vector(1,1); node.Properties["offset"] = Vector(0,0); break;
                case "core.posterize": node.Properties["levels"] = 4.0; break;
                case "core.uvDistort": node.Properties["strength"] = 0.1; node.Properties["speed"] = 1.0; node.Properties["scale"] = 1.0; break;
                case "core.volumeSurface":
                    node.Properties["density"] = 1.0; node.Properties["color"] = new JArray(.4, .2, 1, 1); node.Properties["emission"] = new JArray(0, 0, 0, 1); node.Properties["distance"] = -1.0;
                    node.Properties["mode"] = 0; node.Properties["steps"] = 32; node.Properties["maxDistance"] = 4.0; node.Properties["bounds"] = Vector3(.5, .5, .5); node.Properties["depthClip"] = 0; break;
                case "core.sdfSphere": node.Properties["radius"] = .3; break;
                case "core.sdfBox": node.Properties["size"] = Vector3(.3, .3, .3); break;
                case "core.sdfTorus": node.Properties["radius"] = .3; node.Properties["thickness"] = .08; break;
                case "core.sdfBlend": node.Properties["mode"] = 0; node.Properties["smoothing"] = .1; break;
            }
            return node;
        }
        private static JArray Vector(double x, double y) { return new JArray(x, y); }
        private static JArray Vector3(double x, double y, double z) { return new JArray(x, y, z); }
        private static string TypeName(JToken token) { return token == null ? "color" : token.Value<string>(); }
    }
}
