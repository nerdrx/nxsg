using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    public static class GraphValidator
    {
        public static ValidationResult Validate(ShaderGraph graph, CancellationToken cancellationToken = default(CancellationToken))
        {
            var diagnostics = new List<Diagnostic>();
            if (graph == null)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "graph.null", "$", "Graph is null.");
                return new ValidationResult(diagnostics);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(graph.Format, "nxsg", StringComparison.Ordinal))
            {
                Add(diagnostics, DiagnosticSeverity.Error, "graph.format", "$.format", "Format must be 'nxsg'.");
            }
            if (graph.SchemaVersion != 1)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "graph.schemaVersion", "$.schemaVersion", "Only schemaVersion 1 is supported.");
            }
            if (string.IsNullOrWhiteSpace(graph.GraphId))
            {
                Add(diagnostics, DiagnosticSeverity.Error, "graph.graphId", "$.graphId", "graphId is required.");
            }

            var nodes = graph.Nodes ?? new List<GraphNode>();
            var connections = graph.Connections ?? new List<GraphConnection>();
            var parameters = graph.Parameters ?? new List<GraphParameter>();
            var resources = graph.Resources ?? new List<GraphResource>();
            var patterns = graph.Patterns ?? new List<GraphPattern>();
            CheckCount(diagnostics, "nodes", "$.nodes", nodes.Count, GraphLimits.MaxNodes);
            CheckCount(diagnostics, "connections", "$.connections", connections.Count, GraphLimits.MaxConnections);
            CheckCount(diagnostics, "resources", "$.resources", resources.Count, GraphLimits.MaxResources);

            var nodeById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
            for (var i = 0; i < nodes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = nodes[i];
                var path = "$.nodes[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (node == null)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "node.null", path, "Node is null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "node.id", path + ".id", "Node id is required.");
                }
                else if (!nodeById.TryAdd(node.Id, node))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "node.duplicateId", path + ".id", "Node id is duplicated.");
                }
                if (string.IsNullOrWhiteSpace(node.Operation))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "node.operation", path + ".operation", "Node operation is required.");
                }
                else if (!NodeCatalog.IsKnown(node.Operation))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "operation.unknown", path + ".operation",
                        "Unknown operation is inert and blocks affected compilation.");
                }
                else if (node.Version != 1)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "operation.version", path + ".version",
                        "This operation version is unsupported; preserve the node and migrate it explicitly before compiling.");
                }
                CheckFinite(node.Properties, path + ".properties", diagnostics);
                ValidateNodeProperties(node, path, parameters, resources, diagnostics);
            }

            ValidateParameters(parameters, diagnostics, cancellationToken);
            ValidateResources(resources, diagnostics, cancellationToken);
            ValidatePatterns(patterns, diagnostics, cancellationToken);
            ValidateConnections(graph, connections, nodeById, GraphTypes.Infer(graph), diagnostics, cancellationToken);
            DetectCycle(connections, nodeById, diagnostics);
            return new ValidationResult(diagnostics);
        }

        private static void ValidateNodeProperties(GraphNode node, string path, List<GraphParameter> parameters,
            List<GraphResource> resources, List<Diagnostic> diagnostics)
        {
            if (node.Properties == null)
            {
                return;
            }

            ValidateCatalogProperties(node, path, diagnostics);
            if (FeatureNodes.TryGet(node.Operation, out var feature))
            {
                foreach (var name in FeatureNodes.Numeric(node.Operation)) CheckNumber(node.Properties[name], path + ".properties." + name, diagnostics);
                if (node.Operation == "core.fur")
                {
                    CheckIntegerRange(node.Properties["cardsOnly"], path + ".properties.cardsOnly", 0, 1, diagnostics);
                    CheckIntegerRange(node.Properties["fins"], path + ".properties.fins", 0, 1, diagnostics);
                    CheckRange(node.Properties["finOpacity"], path + ".properties.finOpacity", 0, 1, diagnostics);
                    CheckIntegerRange(node.Properties["layers"], path + ".properties.layers", 4, 32, diagnostics);
                    CheckIntegerRange(node.Properties["minLayers"], path + ".properties.minLayers", 1, 32, diagnostics);
                    CheckIntegerRange(node.Properties["receiveShadows"], path + ".properties.receiveShadows", 0, 1, diagnostics);
                    CheckIntegerRange(node.Properties["selfShadowQuality"], path + ".properties.selfShadowQuality", 0, 3, diagnostics);
                    CheckRange(node.Properties["selfShadowStrength"], path + ".properties.selfShadowStrength", 0, 4, diagnostics);
                    CheckRange(node.Properties["selfShadowBias"], path + ".properties.selfShadowBias", 0, .25, diagnostics);
                }
                if (node.Operation == "core.parallaxOcclusion") CheckIntegerRange(node.Properties["steps"], path + ".properties.steps", 4, 64, diagnostics);
                if (node.Operation == "core.interiorMapping") { CheckIntegerRange(node.Properties["roomsX"], path + ".properties.roomsX", 1, 32, diagnostics); CheckIntegerRange(node.Properties["roomsY"], path + ".properties.roomsY", 1, 32, diagnostics); }
                if (node.Operation == "core.textureBomb") CheckIntegerRange(node.Properties["cells"], path + ".properties.cells", 1, 32, diagnostics);
                if (node.Operation == "core.kaleidoscopeUV") CheckIntegerRange(node.Properties["segments"], path + ".properties.segments", 1, 64, diagnostics);
                if (node.Operation == "core.stripes3D") CheckIntegerRange(node.Properties["axis"], path + ".properties.axis", 0, 2, diagnostics);
                if (node.Operation == "core.tessellation")
                {
                    CheckIntegerRange(node.Properties["factor"], path + ".properties.factor", 1, 63, diagnostics);
                    CheckIntegerRange(node.Properties["minFactor"], path + ".properties.minFactor", 1, 63, diagnostics);
                    CheckRange(node.Properties["nearDistance"], path + ".properties.nearDistance", 0, double.MaxValue, diagnostics);
                    CheckRange(node.Properties["farDistance"], path + ".properties.farDistance", 0, double.MaxValue, diagnostics);
                    CheckRange(node.Properties["smoothing"], path + ".properties.smoothing", 0, 1, diagnostics);
                    var minFactor = node.Properties["minFactor"] ?? new JValue(1);
                    var factor = node.Properties["factor"] ?? new JValue(8);
                    if (IsNumber(minFactor) && IsNumber(factor) && (double)minFactor > (double)factor)
                        Add(diagnostics, DiagnosticSeverity.Error, "value.order", path + ".properties.minFactor", "Minimum tessellation factor cannot exceed factor.");
                    var nearDistance = node.Properties["nearDistance"] ?? new JValue(2);
                    var farDistance = node.Properties["farDistance"] ?? new JValue(15);
                    if (IsNumber(nearDistance) && IsNumber(farDistance) && (double)farDistance <= (double)nearDistance)
                        Add(diagnostics, DiagnosticSeverity.Error, "value.order", path + ".properties.farDistance", "Far distance must be greater than near distance.");
                }
            }

            if (node.Operation == "core.constant" && node.Properties["valueType"] != null)
            {
                ParseType(node.Properties["valueType"], path + ".properties.valueType", diagnostics);
            }
            else if (node.Operation == "core.parameter")
            {
                var parameterId = (string)node.Properties["parameterId"];
                if (string.IsNullOrWhiteSpace(parameterId) || parameters.All(item => item == null || item.Id != parameterId))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "parameter.missing", path + ".properties.parameterId",
                        "Parameter node must reference a declared parameter.");
                }
            }
            else if (node.Operation == "core.texture2D" || node.Operation == "core.triplanarTexture" || node.Operation == "core.matcapTexture" || node.Operation == "core.parallaxOcclusion" || node.Operation == "core.chromaticTexture" || node.Operation == "core.interiorMapping" || node.Operation == "core.textureBomb")
            {
                var resourceId = (string)node.Properties["resourceId"];
                if (string.IsNullOrWhiteSpace(resourceId) || resources.All(item => item == null || item.Id != resourceId))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "resource.missing", path + ".properties.resourceId",
                        "Texture node must reference a declared resource.");
                }
            }
            else if (node.Operation == "core.sticker")
            {
                var resourceId = (string)node.Properties["resourceId"];
                if (string.IsNullOrWhiteSpace(resourceId) || resources.All(item => item == null || item.Id != resourceId))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "resource.missing", path + ".properties.resourceId",
                        "Sticker node must reference a declared resource.");
                }
            }
        }

        private static void ValidateCatalogProperties(GraphNode node, string path, List<Diagnostic> diagnostics)
        {
            string[] numeric = null, vectors = null;
            switch (node.Operation)
            {
                case "core.motionResponse": numeric = new[]{"startSpeed","fullSpeed","curve"}; break;
                case "core.motionSway": numeric = new[]{"strength","frequency","spatialScale","fullSpeed"}; break;
                case "core.motionStretchUV": numeric = new[]{"strength","maxStretch","axis"}; break;
                case "core.value": numeric = new[] { "value" }; break;
                case "core.time": numeric = new[] { "speed", "offset" }; break;
                case "core.noise": case "core.checker": numeric = new[] { "scale", "speed" }; break;
                case "core.musgrave": numeric = new[] { "scale", "speed", "lacunarity", "gain" }; break;
                case "core.voronoi": numeric = new[] { "scale", "speed", "randomness" }; break;
                case "core.wave": numeric = new[] { "scale", "speed" }; break;
                case "core.uv0": case "core.texture2D": break;
                case "core.mix": numeric = new[] { "factor" }; break;
                case "core.absolute": case "core.sqrt": case "core.sine": case "core.cosine": case "core.fraction": case "core.floor": case "core.ceil": case "core.round": numeric = new[] { "a" }; break;
                case "core.power": numeric = new[] { "a", "b" }; break;
                case "core.step": numeric = new[] { "a", "b" }; break;
                case "core.smoothstep": numeric = new[] { "value", "low", "high" }; break;
                case "core.remap": numeric = new[] { "value", "inMin", "inMax", "outMin", "outMax" }; break;
                case "core.pingPong": numeric = new[] { "value", "length" }; break;
                case "core.splitColor": case "core.luminance": break;
                case "core.combineColor": numeric = new[] { "r", "g", "b", "a" }; break;
                case "core.contrast": numeric = new[] { "amount", "pivot" }; break;
                case "core.saturation": numeric = new[] { "amount" }; break;
                case "core.splitUV": break;
                case "core.combineUV": numeric = new[] { "u", "v" }; break;
                case "core.circleMask": numeric = new[] { "radius", "softness" }; break;
                case "core.boxMask": numeric = new[] { "width", "height", "softness" }; break;
                case "core.polygonMask": numeric = new[] { "radius", "rotation", "softness" }; break;
                case "core.starMask": numeric = new[] { "inner", "outer", "rotation", "softness" }; break;
                case "core.radialRays": numeric = new[] { "rotation", "softness" }; break;
                case "core.spiral": numeric = new[] { "turns", "width", "rotation" }; break;
                case "core.brick": numeric = new[] { "tilingX", "tilingY", "mortar" }; break;
                case "core.hexGrid": numeric = new[] { "scale", "width" }; break;
                case "core.triplanarTexture": numeric = new[] { "scale", "sharpness" }; break;
                case "core.rimGlow": numeric = new[] { "power" }; break;
                case "core.heightMask": numeric = new[] { "low", "high" }; break;
                case "core.slopeMask": numeric = new[] { "low", "high" }; break;
                case "core.distanceFade": numeric = new[] { "near", "far" }; break;
                case "core.wireframe": numeric = new[] { "width", "softness" }; break;
                case "core.ramp": numeric = new[] { "blackPoint", "whitePoint", "smoothness" }; break;
                case "core.emission": numeric = new[] { "strength" }; break;
                case "core.uvTransform": vectors = new[] { "tiling", "offset" }; break;
                case "core.polarUV": vectors = new[] { "center" }; numeric = new[] { "radialScale", "angleScale" }; break;
                case "core.uvRotate": vectors = new[] { "center" }; numeric = new[] { "angle" }; break;
                case "core.uvScroll": vectors = new[] { "speed" }; break;
                case "core.toonSurface": numeric = new[] { "opacity", "displacement", "cutoff", "threshold", "softness", "shadowStrength" }; break;
                case "core.unlitSurface": numeric = new[] { "opacity", "displacement", "cutoff" }; break;
                case "core.pbrSurface": numeric = new[] { "opacity", "displacement", "metallic", "roughness", "cutoff" }; break;
                case "core.surfaceParticles": numeric = new[] { "density", "size", "lifetime", "speed", "gravity", "spread", "opacity", "mask", "emissionRate", "edgeSharpness" }; break;
                case "core.particleSurface": numeric = new[] { "opacity", "softDistance" }; break;
                case "core.fresnel": numeric = new[] { "power" }; break;
                case "core.colorRamp": break;
                case "core.layer": numeric = new[] { "mask" }; break;
                case "core.sticker": vectors = new[] { "position", "size" }; numeric = new[] { "rotation" }; break;
                case "core.flipbook": numeric = new[] { "rows", "columns", "speed" }; break;
                case "core.audioLink": numeric = new[] { "band", "gain", "smoothing", "fallback", "min", "max" }; break;
                case "core.normalMap": numeric = new[] { "strength", "flipGreen" }; break;
                case "core.darknessGlow": numeric = new[] { "strength", "threshold", "softness" }; break;
                case "core.ltcgi": numeric = new[] { "roughness", "metallic", "strength" }; break;
                case "core.dissolve": numeric = new[] { "threshold", "edgeWidth" }; break;
                case "core.shell": numeric = new[] { "offset" }; break;
                case "core.vertexMotion": numeric = new[] { "strength", "speed", "frequency" }; break;
                case "core.glitter": numeric = new[] { "scale", "density", "size", "sharpness", "viewStrength", "speed", "twinkle", "brightness", "seed", "mask" }; break;
                case "core.uvDistort": numeric = new[] { "strength", "speed", "scale", "mask", "radius", "falloff" }; vectors = new[] { "center", "direction", "axes" }; break;
                case "core.gradient": numeric = new[] { "angle", "radius" }; vectors = new[] { "center" }; break;
                case "core.uvTile": vectors = new[] { "tiling", "offset" }; break;
                case "core.posterize": numeric = new[] { "levels" }; break;
                default: return;
            }
            if (node.Operation == "core.toonSurface" || node.Operation == "core.unlitSurface" || node.Operation == "core.pbrSurface")
                CheckIntegerRange(node.Properties["useAlbedoAlpha"], path + ".properties.useAlbedoAlpha", 0, 1, diagnostics);
            if (node.Operation == "core.toonSurface" || node.Operation == "core.pbrSurface")
                foreach (var setting in new[] { "lightingMin", "lightingMax", "lightingSaturation" })
                    CheckRange(node.Properties[setting], path + ".properties." + setting, 0, 65504, diagnostics);
            if (node.Properties["coordinateSource"] != null)
                CheckChoice(node.Properties["coordinateSource"], path + ".properties.coordinateSource", new[] { "uv0", "uv1", "uv2", "uv3", "object", "world", "polar", "panosphere", "matcap" }, diagnostics);
            if (new[] { "core.noise", "core.musgrave", "core.voronoi", "core.checker", "core.wave" }.Contains(node.Operation))
            {
                CheckIntegerRange(node.Properties["dimensions"], path + ".properties.dimensions", node.Operation == "core.noise" ? 1 : 2, node.Operation == "core.noise" ? 4 : 3, diagnostics);
                CheckChoice(node.Properties["coordinateSpace"], path + ".properties.coordinateSpace", new[] { "object", "world" }, diagnostics);
            }
            if (node.Operation == "core.musgrave")
            {
                CheckIntegerRange(node.Properties["octaves"], path + ".properties.octaves", 1, 8, diagnostics);
                CheckIntegerRange(node.Properties["mode"], path + ".properties.mode", 0, 2, diagnostics);
            }
            if (node.Operation == "core.position" || node.Operation == "core.normalDirection") CheckIntegerRange(node.Properties["space"], path + ".properties.space", 0, 1, diagnostics);
            if (node.Operation == "core.polygonMask") CheckIntegerRange(node.Properties["sides"], path + ".properties.sides", 3, 32, diagnostics);
            if (node.Operation == "core.starMask") CheckIntegerRange(node.Properties["points"], path + ".properties.points", 3, 32, diagnostics);
            if (node.Operation == "core.radialRays") CheckIntegerRange(node.Properties["count"], path + ".properties.count", 1, 128, diagnostics);
            if (node.Operation == "core.heightMask") CheckIntegerRange(node.Properties["axis"], path + ".properties.axis", 0, 2, diagnostics);
            if (node.Operation == "core.particleSurface")
            {
                CheckIntegerRange(node.Properties["blendMode"], path + ".properties.blendMode", 0, 1, diagnostics);
                CheckRange(node.Properties["softDistance"], path + ".properties.softDistance", 0, float.MaxValue, diagnostics);
            }
            if (node.Operation == "core.surfaceParticles")
            {
                CheckIntegerRange(node.Properties["sourceUV"], path + ".properties.sourceUV", 0, 1, diagnostics);
                CheckIntegerRange(node.Properties["blendMode"], path + ".properties.blendMode", 0, 1, diagnostics);
            }
            if (node.Operation == "core.surfaceParticles") CheckRange(node.Properties["lifetime"], path + ".properties.lifetime", .001, float.MaxValue, diagnostics);
            if (node.Operation == "core.wave")
            {
                CheckIntegerRange(node.Properties["mode"], path + ".properties.mode", 0, 1, diagnostics);
                CheckIntegerRange(node.Properties["axis"], path + ".properties.axis", 0, 2, diagnostics);
            }
            if (node.Operation == "core.uvDistort")
            {
                CheckIntegerRange(node.Properties["mode"], path + ".properties.mode", 0, 6, diagnostics);
                CheckIntegerRange(node.Properties["detail"], path + ".properties.detail", 1, 6, diagnostics);
                CheckRange(node.Properties["radius"], path + ".properties.radius", .000001, float.MaxValue, diagnostics);
            }
            if (node.Operation == "core.gradient" || node.Operation == "core.uvTile") CheckIntegerRange(node.Properties["mode"], path + ".properties.mode", 0, 2, diagnostics);
            if (node.Operation == "core.gradient") CheckRange(node.Properties["radius"], path + ".properties.radius", .000001, float.MaxValue, diagnostics);
            if (node.Operation == "core.posterize") CheckRange(node.Properties["levels"], path + ".properties.levels", 2, float.MaxValue, diagnostics);
            if (node.Operation == "core.ltcgi")
            {
                CheckRange(node.Properties["roughness"], path + ".properties.roughness", 0, 1, diagnostics);
                CheckRange(node.Properties["metallic"], path + ".properties.metallic", 0, 1, diagnostics);
                CheckRange(node.Properties["strength"], path + ".properties.strength", 0, double.MaxValue, diagnostics);
            }
            if(node.Operation=="core.motionResponse")
            {
                CheckRange(node.Properties["startSpeed"],path+".properties.startSpeed",0,float.MaxValue,diagnostics);
                CheckRange(node.Properties["curve"],path+".properties.curve",.001,float.MaxValue,diagnostics);
                var start=node.Properties["startSpeed"]??new JValue(.1);var full=node.Properties["fullSpeed"]??new JValue(4);
                if(IsNumber(start)&&IsNumber(full)&&(double)full<=(double)start)Add(diagnostics,DiagnosticSeverity.Error,"value.range",path+".properties.fullSpeed","Full speed must exceed start speed.");
            }
            if(node.Operation=="core.motionSway")CheckRange(node.Properties["fullSpeed"],path+".properties.fullSpeed",.0001,float.MaxValue,diagnostics);
            if(node.Operation=="core.motionStretchUV")
            {
                CheckRange(node.Properties["maxStretch"],path+".properties.maxStretch",1,float.MaxValue,diagnostics);
                CheckRange(node.Properties["strength"],path+".properties.strength",0,float.MaxValue,diagnostics);
                CheckIntegerRange(node.Properties["axis"],path+".properties.axis",0,1,diagnostics);
            }
            if(node.Operation=="core.normalMap")CheckIntegerRange(node.Properties["flipGreen"],path+".properties.flipGreen",0,1,diagnostics);
            if (numeric != null) foreach (var name in numeric) CheckNumber(node.Properties[name], path + ".properties." + name, diagnostics);
            if (vectors != null) foreach (var name in vectors) CheckVector2(node.Properties[name], path + ".properties." + name, diagnostics);
            if (node.Operation == "core.surfaceParticles")
            {
                CheckRampPoints(node.Properties["sizeCurve"], path + ".properties.sizeCurve", diagnostics);
                CheckRampPoints(node.Properties["opacityCurve"], path + ".properties.opacityCurve", diagnostics);
                CheckColorRampStops(node.Properties["colorCurve"], path + ".properties.colorCurve", diagnostics);
            }
            if (node.Operation == "core.ramp") CheckRampPoints(node.Properties["points"], path + ".properties.points", diagnostics);
            if (node.Operation == "core.colorRamp") CheckColorRampStops(node.Properties["stops"], path + ".properties.stops", diagnostics);
            if (node.Operation == "core.flipbook") CheckFlipbookLimits(node, path, diagnostics);
            if (node.Operation == "core.audioLink")
            {
                CheckIntegerRange(node.Properties["rangeEnabled"],path+".properties.rangeEnabled",0,1,diagnostics);
                CheckIntegerRange(node.Properties["band"],path+".properties.band",0,3,diagnostics);
                var smoothing = node.Properties["smoothing"];
                if (IsNumber(smoothing) && ((double)smoothing < 0 || (double)smoothing > 1)) Add(diagnostics,DiagnosticSeverity.Error,"value.range",path+".properties.smoothing","Smoothing must be between 0 and 1.");
            }
            if (node.Operation == "core.sticker" && node.Properties["size"] is JArray size && size.Count == 2 && size.All(IsNumber) && size.Any(v => (double)v <= 0))
                Add(diagnostics,DiagnosticSeverity.Error,"value.range",path+".properties.size","Sticker size must be positive.");
            if (node.Operation == "core.glitter")
            {
                CheckRange(node.Properties["scale"], path + ".properties.scale", double.Epsilon, 100000, diagnostics);
                CheckRange(node.Properties["density"], path + ".properties.density", 0, 1, diagnostics);
                CheckRange(node.Properties["size"], path + ".properties.size", 0, 1, diagnostics);
                CheckRange(node.Properties["sharpness"], path + ".properties.sharpness", 1, 512, diagnostics);
                CheckRange(node.Properties["viewStrength"], path + ".properties.viewStrength", 0, 1, diagnostics);
                CheckRange(node.Properties["twinkle"], path + ".properties.twinkle", 0, 1, diagnostics);
                CheckRange(node.Properties["brightness"], path + ".properties.brightness", 0, 65504, diagnostics);
                CheckRange(node.Properties["mask"], path + ".properties.mask", 0, 1, diagnostics);
            }
        }

        private static void CheckRampPoints(JToken token, string path, List<Diagnostic> diagnostics)
        {
            if (token == null) return;
            var points = token as JArray;
            if (points == null || points.Count < 2 || points.Count > 16)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "property.type", path, "Ramp points must contain 2 to 16 [x, y] pairs.");
                return;
            }
            double previousX = -1.0;
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i] as JArray;
                var pointPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (point == null || point.Count != 2)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "property.type", pointPath, "Ramp point must be a two-number pair.");
                    continue;
                }
                CheckNumber(point[0], pointPath + "[0]", diagnostics);
                CheckNumber(point[1], pointPath + "[1]", diagnostics);
                if (point[0].Type != JTokenType.Integer && point[0].Type != JTokenType.Float) continue;
                if (point[1].Type != JTokenType.Integer && point[1].Type != JTokenType.Float) continue;
                var x = point[0].Value<double>();
                var y = point[1].Value<double>();
                if (x < 0 || x > 1 || y < 0 || y > 1)
                    Add(diagnostics, DiagnosticSeverity.Error, "value.range", pointPath, "Ramp point values must be between 0 and 1.");
                if (i > 0 && x <= previousX)
                    Add(diagnostics, DiagnosticSeverity.Error, "value.order", pointPath + "[0]", "Ramp point x values must be strictly increasing.");
                previousX = x;
            }
        }

        private static void CheckColorRampStops(JToken token, string path, List<Diagnostic> diagnostics)
        {
            if (token == null) return;
            var stops = token as JArray;
            if (stops == null || stops.Count < 2 || stops.Count > 8)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "property.type", path, "Color ramp stops must contain 2 to 8 [position, r, g, b, a] entries.");
                return;
            }
            double previous = -1;
            for (var i = 0; i < stops.Count; i++)
            {
                var stop = stops[i] as JArray;
                var stopPath = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (stop == null || stop.Count != 5)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "property.type", stopPath, "Color ramp stop must contain five numbers.");
                    continue;
                }
                for (var component = 0; component < stop.Count; component++) CheckNumber(stop[component], stopPath + "[" + component.ToString(CultureInfo.InvariantCulture) + "]", diagnostics);
                if (!IsNumber(stop[0]) || !IsNumber(stop[1]) || !IsNumber(stop[2]) || !IsNumber(stop[3]) || !IsNumber(stop[4])) continue;
                for (var component = 0; component < stop.Count; component++)
                {
                    var value = stop[component].Value<double>();
                    if (value < 0 || value > 1) Add(diagnostics, DiagnosticSeverity.Error, "value.range", stopPath, "Color ramp stop components must be between 0 and 1.");
                }
                var position = stop[0].Value<double>();
                if (i > 0 && position <= previous) Add(diagnostics, DiagnosticSeverity.Error, "value.order", stopPath + "[0]", "Color ramp stop positions must be strictly increasing.");
                previous = position;
            }
        }

        private static void CheckFlipbookLimits(GraphNode node, string path, List<Diagnostic> diagnostics)
        {
            CheckIntegerRange(node.Properties["rows"], path + ".properties.rows", 1, 64, diagnostics);
            CheckIntegerRange(node.Properties["columns"], path + ".properties.columns", 1, 64, diagnostics);
        }

        private static void CheckChoice(JToken token, string path, string[] choices, List<Diagnostic> diagnostics)
        {
            if (token != null && (token.Type != JTokenType.String || !choices.Contains((string)token)))
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "node.property.choice", path, "Choose one of: " + string.Join(", ", choices)));
        }
        private static void CheckRange(JToken token, string path, double min, double max, List<Diagnostic> diagnostics)
        {
            if (token == null) return;
            // Unity controls store single-precision values. Accept the exact decimal
            // endpoint and its nearest shader-float representation (e.g. 0.0001f).
            var lower = Math.Min(min, (double)(float)min);
            var upper = Math.Max(max, (double)(float)max);
            if ((token.Type != JTokenType.Float && token.Type != JTokenType.Integer) || double.IsNaN((double)token) || double.IsInfinity((double)token) || (double)token < lower || (double)token > upper)
                diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "node.property.range", path, "Value must be between " + min + " and " + max + "."));
        }

        private static void CheckIntegerRange(JToken token, string path, int minimum, int maximum, List<Diagnostic> diagnostics)
        {
            if (token == null) return;
            if (!IsNumber(token) || (double)token != Math.Floor((double)token))
            {
                Add(diagnostics, DiagnosticSeverity.Error, "property.type", path, "Property must be an integer.");
                return;
            }
            var value = token.Value<double>();
            if (value < minimum || value > maximum) Add(diagnostics, DiagnosticSeverity.Error, "value.range", path, "Integer property is outside its supported range.");
        }

        private static bool IsNumber(JToken token)
        {
            return token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);
        }

        private static void CheckNumber(JToken token, string path, List<Diagnostic> diagnostics)
        {
            if (token == null) return;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            { Add(diagnostics, DiagnosticSeverity.Error, "property.type", path, "Property must be a finite number."); return; }
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue) Add(diagnostics, DiagnosticSeverity.Error, "value.nonfinite", path, "Numeric values must fit a finite shader float.");
        }

        private static void CheckVector2(JToken token, string path, List<Diagnostic> diagnostics)
        {
            if (token == null) return;
            var array = token as JArray;
            if (array == null || array.Count != 2)
            { Add(diagnostics, DiagnosticSeverity.Error, "property.type", path, "Property must be a two-number vector."); return; }
            CheckNumber(array[0], path + "[0]", diagnostics); CheckNumber(array[1], path + "[1]", diagnostics);
        }

        private static void ValidateParameters(List<GraphParameter> parameters, List<Diagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < parameters.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parameter = parameters[i];
                var path = "$.parameters[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (parameter == null)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "parameter.null", path, "Parameter is null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(parameter.Id) || !ids.Add(parameter.Id))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "parameter.duplicateId", path + ".id", "Parameter id is empty or duplicated.");
                }
                if (string.IsNullOrWhiteSpace(parameter.Name) || !names.Add(parameter.Name))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "parameter.name", path + ".name", "Parameter name is empty or duplicated.");
                }
                CheckFinite(parameter.DefaultValue, path + ".defaultValue", diagnostics);
            }
        }

        private static void ValidateResources(List<GraphResource> resources, List<Diagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < resources.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resource = resources[i];
                var path = "$.resources[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (resource == null)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "resource.null", path, "Resource is null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(resource.Id) || !ids.Add(resource.Id))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "resource.duplicateId", path + ".id", "Resource id is empty or duplicated.");
                }
                if (string.IsNullOrWhiteSpace(resource.Uri) || !IsSafeResourceUri(resource.Uri))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "resource.path", path + ".uri",
                        "Resource URI must be project-relative, builtin://, or another explicitly safe project URI.");
                }
            }
        }

        private static void ValidatePatterns(List<GraphPattern> patterns, List<Diagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            var byId = new Dictionary<string, GraphPattern>(StringComparer.Ordinal);
            for (var i = 0; i < patterns.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pattern = patterns[i];
                var path = "$.patterns[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (pattern == null || string.IsNullOrWhiteSpace(pattern.Id) || byId.ContainsKey(pattern.Id))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "pattern.duplicateId", path + ".id", "Pattern id is empty or duplicated.");
                    continue;
                }
                byId.Add(pattern.Id, pattern);
                if (pattern.Nodes != null && pattern.Nodes.Count > GraphLimits.MaxNodes)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "pattern.nodes", path + ".nodes", "Pattern node count exceeds authored node limit.");
                }
            }

            foreach (var pattern in byId.Values)
            {
                VisitPattern(pattern.Id, byId, new HashSet<string>(StringComparer.Ordinal), 1, diagnostics);
            }
        }

        private static void VisitPattern(string id, Dictionary<string, GraphPattern> patterns,
            HashSet<string> active, int depth, List<Diagnostic> diagnostics)
        {
            if (depth > GraphLimits.MaxPatternDepth)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "pattern.depth", "$.patterns", "Pattern expansion depth exceeds the safety limit.");
                return;
            }
            if (!active.Add(id))
            {
                Add(diagnostics, DiagnosticSeverity.Error, "pattern.cycle", "$.patterns", "Pattern recursion is not allowed.");
                return;
            }
            var pattern = patterns[id];
            if (pattern.Calls != null)
            {
                foreach (var call in pattern.Calls)
                {
                    if (!patterns.ContainsKey(call))
                    {
                        Add(diagnostics, DiagnosticSeverity.Error, "pattern.missing", "$.patterns", "Pattern call references a missing pattern.");
                    }
                    else
                    {
                        VisitPattern(call, patterns, active, depth + 1, diagnostics);
                    }
                }
            }
            active.Remove(id);
        }

        private static void ValidateConnections(ShaderGraph graph, List<GraphConnection> connections,
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, string> inferred, List<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var inputPorts = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < connections.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var connection = connections[i];
                var path = "$.connections[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (connection == null)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.null", path, "Connection is null.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(connection.Id) || !ids.Add(connection.Id))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.duplicateId", path + ".id", "Connection id is empty or duplicated.");
                }
                if (connection.From == null || connection.To == null)
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.endpoint", path, "Connection must have from and to endpoints.");
                    continue;
                }
                if (!nodes.TryGetValue(connection.From.NodeId ?? string.Empty, out var fromNode) ||
                    !nodes.TryGetValue(connection.To.NodeId ?? string.Empty, out var toNode))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.node", path, "Connection endpoint references a missing node.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(connection.From.PortId) || string.IsNullOrWhiteSpace(connection.To.PortId))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.port", path, "Connection endpoints require explicit non-empty port IDs.");
                    continue;
                }
                var inputKey = connection.To.NodeId + "\u001f" + connection.To.PortId;
                if (!inputPorts.Add(inputKey))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.inputMultiple", path + ".to", "An input port has more than one connection.");
                }
                if (!IsPortDefined(fromNode, connection.From.PortId, false) ||
                    !IsPortDefined(toNode, connection.To.PortId, true))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.port.unknown", path,
                        "Connection references a port that is not declared by its operation.");
                    continue;
                }
                var fromType = GraphTypes.PortType(graph, fromNode, connection.From.PortId, inferred);
                var toType = GraphTypes.PortType(graph, toNode, connection.To.PortId, inferred);
                if (fromType != null && toType != null && !GraphTypes.Compatible(fromType, toType))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "connection.type", path,
                        "Connection socket types are incompatible.");
                }
            }
        }

        private static bool IsPortDefined(GraphNode node, string port, bool input)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Operation)) return false;
            if (NodeCatalog.IsKnown(node.Operation))
            {
                var ports = NodeCatalog.Ports(node.Operation, !input);
                return ports.Contains(port, StringComparer.Ordinal);
            }
            return false;
        }

        private static void DetectCycle(List<GraphConnection> connections, Dictionary<string, GraphNode> nodes,
            List<Diagnostic> diagnostics)
        {
            var indegree = nodes.Keys.ToDictionary(key => key, key => 0, StringComparer.Ordinal);
            var outgoing = nodes.Keys.ToDictionary(key => key, key => new List<string>(), StringComparer.Ordinal);
            foreach (var connection in connections)
            {
                if (connection == null || connection.From == null || connection.To == null ||
                    !outgoing.ContainsKey(connection.From.NodeId ?? string.Empty) || !indegree.ContainsKey(connection.To.NodeId ?? string.Empty))
                {
                    continue;
                }
                outgoing[connection.From.NodeId].Add(connection.To.NodeId);
                indegree[connection.To.NodeId]++;
            }
            var queue = new Queue<string>(indegree.Where(item => item.Value == 0).Select(item => item.Key));
            var visited = 0;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                visited++;
                foreach (var target in outgoing[node])
                {
                    indegree[target]--;
                    if (indegree[target] == 0) queue.Enqueue(target);
                }
            }
            if (visited != nodes.Count)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "graph.cycle", "$.connections", "Data connections must form an acyclic graph.");
            }
        }

        private static GraphValueType? GetPortType(GraphNode node, string port, bool input, List<GraphParameter> parameters)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Operation)) return null;
            if (node.Operation != "core.parameter" && node.Operation != "core.constant" && node.Operation != "core.multiply")
            {
                var name = NodeCatalog.PortType(node, port);
                GraphValueType catalogType;
                if (name != null && Enum.TryParse(name, true, out catalogType)) return catalogType;
            }
            switch (node.Operation)
            {
                case "core.constant":
                    return input ? (GraphValueType?)null : ReadType(node.Properties == null ? null : node.Properties["valueType"]);
                case "core.parameter":
                    if (input) return null;
                    var parameterId = node.Properties == null ? null : (string)node.Properties["parameterId"];
                    var parameter = parameters.FirstOrDefault(item => item != null && item.Id == parameterId);
                    return parameter == null ? (GraphValueType?)null : parameter.Type;
                case "core.uv0":
                    return input ? (GraphValueType?)null : GraphValueType.Vector2;
                case "core.texture2D":
                    return input ? (port == "uv" ? GraphValueType.Vector2 : (GraphValueType?)null) :
                        (port == "color" ? GraphValueType.Color : port == "alpha" ? GraphValueType.Float : (GraphValueType?)null);
                case "core.multiply":
                    return ReadType(node.Properties == null ? null : node.Properties["valueType"]);
                case "core.toonSurface":
                    if (input) return port == "albedo" ? GraphValueType.Color : (port == "normal" ? GraphValueType.Vector3 : (GraphValueType?)null);
                    return port == "surface" ? GraphValueType.Surface : (GraphValueType?)null;
                case "core.output":
                    return input && port == "surface" ? GraphValueType.Surface : (GraphValueType?)null;
                default:
                    return null;
            }
        }

        private static GraphValueType? ParseType(JToken token, string path, List<Diagnostic> diagnostics)
        {
            var result = ReadType(token);
            if (!result.HasValue)
            {
                Add(diagnostics, DiagnosticSeverity.Error, "type.invalid", path, "Unknown graph value type.");
            }
            return result;
        }

        private static GraphValueType? ReadType(JToken token)
        {
            var text = token == null ? null : token.Value<string>();
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (Enum.TryParse(text, true, out GraphValueType type)) return type;
            return null;
        }

        private static bool IsCompatible(GraphValueType from, GraphValueType to)
        {
            if (from == to) return true;
            if (from == GraphValueType.Float && (to == GraphValueType.Vector2 || to == GraphValueType.Vector3 ||
                to == GraphValueType.Vector4 || to == GraphValueType.Color)) return true;
            return false;
        }

        private static bool IsSafeResourceUri(string value)
        {
            if (value.StartsWith("builtin://", StringComparison.Ordinal))
            {
                var builtinPath = value.Substring("builtin://".Length).Replace('\\', '/');
                return !string.IsNullOrWhiteSpace(builtinPath) && !builtinPath.StartsWith("/", StringComparison.Ordinal) &&
                    !builtinPath.Contains(":") && !builtinPath.Split('/').Any(part => part == "..");
            }
            if (value.StartsWith("project://", StringComparison.Ordinal))
            {
                var projectPath = value.Substring("project://".Length).Replace('\\', '/');
                return !string.IsNullOrWhiteSpace(projectPath) && !projectPath.StartsWith("/", StringComparison.Ordinal) &&
                    !projectPath.Contains(":") && !projectPath.Split('/').Any(part => part == "..");
            }
            if (value.StartsWith("/", StringComparison.Ordinal) || value.Contains("://")) return false;
            var normalized = value.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Contains(":") || normalized.Split('/').Any(part => part == "..")) return false;
            return !string.IsNullOrWhiteSpace(normalized);
        }

        private static void CheckFinite(JToken token, string path, List<Diagnostic> diagnostics)
        {
            if (token == null) return;
            if (token.Type == JTokenType.Float)
            {
                var value = token.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "value.nonfinite", path, "Numeric values must be finite.");
                }
            }
            foreach (var child in token.Children())
            {
                CheckFinite(child, path + "." + child.Path, diagnostics);
            }
        }

        private static void CheckCount(List<Diagnostic> diagnostics, string codeName, string path, int actual, int limit)
        {
            if (actual <= limit) return;
            var diagnostic = new Diagnostic(DiagnosticSeverity.Error, "limit." + codeName, path,
                "Count exceeds the safety limit.")
            {
                Actual = actual,
                Limit = limit
            };
            diagnostics.Add(diagnostic);
        }

        private static void Add(List<Diagnostic> diagnostics, DiagnosticSeverity severity, string code, string path, string message)
        {
            diagnostics.Add(new Diagnostic(severity, code, path, message));
        }
    }
}
