using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NXSG.Core;

namespace NXSG.Backend
{
    public sealed class EmitterOptions
    {
        public EmitterOptions()
        {
            ShaderName = BuiltinForwardShaderTemplate.ShaderName;
            IncludeShadowCaster = true;
            IncludeVrcFallback = true;
            VrcFallbackTag = "toonstandard";
        }

        public string ShaderName { get; set; }
        public bool IncludeShadowCaster { get; set; }
        public bool LtcgiAvailable { get; set; }
        public bool IncludeVrcFallback { get; set; }
        public string VrcFallbackTag { get; set; }
    }

    public sealed class MaterialProperty
    {
        public string Name { get; internal set; }
        public string DisplayName { get; internal set; }
        public GraphValueType Type { get; internal set; }
        public GraphBindingKind Binding { get; internal set; }
        public string ParameterId { get; internal set; }
        public string ResourceId { get; internal set; }
        public string ResourceUri { get; internal set; }
    }

    public sealed class SourceMapEntry
    {
        public string NodeId { get; internal set; }
        public string PortId { get; internal set; }
        public int StartLine { get; internal set; }
        public int EndLine { get; internal set; }
    }

    public sealed class EmissionResult
    {
        public EmissionResult(
            string shaderSource,
            IReadOnlyList<Diagnostic> diagnostics,
            IReadOnlyList<MaterialProperty> properties,
            IReadOnlyList<SourceMapEntry> sourceMap)
        {
            ShaderSource = shaderSource;
            Diagnostics = diagnostics;
            Properties = properties;
            SourceMap = sourceMap;
        }

        public string ShaderSource { get; private set; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; }
        public IReadOnlyList<MaterialProperty> Properties { get; private set; }
        public IReadOnlyList<SourceMapEntry> SourceMap { get; private set; }

        public bool Succeeded
        {
            get
            {
                for (var i = 0; i < Diagnostics.Count; i++)
                {
                    if (Diagnostics[i].Severity == DiagnosticSeverity.Error)
                    {
                        return false;
                    }
                }

                return ShaderSource != null;
            }
        }
    }

    /// <summary>
    /// Small, deterministic Built-In backend for the first graph contract.
    /// It emits only fixed HLSL templates and validated identifiers.
    /// </summary>
    public static class ShaderEmitter
    {
        private const int MaxTraversalWork = 8192;
        private const int MaxTraversalDepth = 64;
        private const int MaxGeneratedSourceBytes = 8 * 1024 * 1024;
        private const string TextureOperation = "core.texture2D";
        private const string UvOperation = "core.uv0";
        private const string ToonOperation = "core.toonSurface";
        private const string OutputOperation = "core.output";
        private const string ConstantOperation = "core.constant";
        private const string MultiplyOperation = "core.multiply";
        private const string ParameterOperation = "core.parameter";
        private const string ValueOperation = "core.value";
        private const string TimeOperation = "core.time";
        private const string UvTransformOperation = "core.uvTransform";
        private const string UvScrollOperation = "core.uvScroll";
        private const string UvRotateOperation = "core.uvRotate";
        private const string PolarUvOperation = "core.polarUV";
        private const string ObjectUvOperation = "core.objectUV";
        private const string WorldUvOperation = "core.worldUV";
        private const string NoiseOperation = "core.noise";
        private const string AddOperation = "core.add";
        private const string SubtractOperation = "core.subtract";
        private const string DivideOperation = "core.divide";
        private const string MinimumOperation = "core.minimum";
        private const string MaximumOperation = "core.maximum";
        private const string MixOperation = "core.mix";
        private const string EmissionOperation = "core.emission";
        private const string OneMinusOperation = "core.oneMinus";
        private const string ClampOperation = "core.clamp";
        private const string RampOperation = "core.ramp";

        public static EmissionResult Emit(ShaderGraph graph, EmitterOptions options = null)
        {
            var diagnostics = new List<Diagnostic>();
            var properties = new List<MaterialProperty>();
            var sourceMap = new List<SourceMapEntry>();
            options = options ?? new EmitterOptions();

            if (graph == null)
            {
                AddError(diagnostics, "backend.graph.null", "$", "Graph is required.");
                return Result(null, diagnostics, properties, sourceMap);
            }

            var validation = GraphValidator.Validate(graph);
            for (var i = 0; i < validation.Diagnostics.Count; i++)
            {
                diagnostics.Add(validation.Diagnostics[i]);
            }

            if (HasErrors(diagnostics))
            {
                return Result(null, diagnostics, properties, sourceMap);
            }

            // Keep the original compact toon emitter stable while routing the
            // extended surface/effect contract through its own backend. This
            // also lets older graphs retain byte-for-byte compatible output.
            if (AdvancedShaderEmitter.IsAdvanced(graph))
                return AdvancedShaderEmitter.Emit(graph, options ?? new EmitterOptions());

            if (graph.Format != "nxsg")
            {
                AddError(diagnostics, "backend.graph.format", "format", "Only the nxsg graph format is supported.");
            }

            if (graph.SchemaVersion != 1)
            {
                AddError(diagnostics, "backend.graph.schema", "schemaVersion", "Only schema version 1 is supported by this backend.");
            }

            var shaderName = ValidateShaderName(options.ShaderName, diagnostics);
            var fallback = ValidateFallback(options, diagnostics);
            var nodes = IndexNodes(graph, diagnostics);
            var inferredTypes = GraphTypes.Infer(graph);
            nodes = ApplyInferredTypes(nodes, inferredTypes);
            var incoming = IndexIncoming(graph, nodes, diagnostics);
            var output = FindSingleNode(nodes, OutputOperation, diagnostics);

            if (output == null)
            {
                return Result(null, diagnostics, properties, sourceMap);
            }

            var reachable = new HashSet<string>(StringComparer.Ordinal);
            if (!PreflightReachable(output.Id, nodes, incoming, reachable, diagnostics))
            {
                return Result(null, diagnostics, properties, sourceMap);
            }
            ValidateReachableOperations(reachable, nodes, diagnostics);

            var toon = FindConnectedNode(output, "surface", ToonOperation, nodes, incoming, diagnostics);
            if (toon == null)
            {
                return Result(null, diagnostics, properties, sourceMap);
            }

            var parameterMap = IndexParameters(graph.Parameters, diagnostics);
            var texture = ResolveTexture(reachable, nodes, incoming, parameterMap, graph.Resources, diagnostics);
            var tint = ResolveColor(toon, "albedo", texture != null, nodes, incoming, parameterMap, diagnostics);
            var emission = ResolveColor(toon, "emission", false, nodes, incoming, parameterMap, diagnostics);
            var threshold = ResolveScalar(toon, "threshold", "thresholdParameterId", 0.5f, parameterMap, properties, diagnostics);
            var softness = ResolveScalar(toon, "softness", "softnessParameterId", 0.05f, parameterMap, properties, diagnostics);
            var shadowStrength = ResolveScalar(toon, "shadowStrength", "shadowStrengthParameterId", 1.0f, parameterMap, properties, diagnostics);

            AddMaterialParameters(graph.Parameters, parameterMap, properties, diagnostics);
            if (texture != null)
            {
                properties.Add(new MaterialProperty
                {
                    Name = "_MainTex",
                    DisplayName = TextureSlotLabels.DisplayName(graph, texture.ResourceId),
                    Type = GraphValueType.Texture2D,
                    Binding = GraphBindingKind.Material,
                    ResourceId = texture.ResourceId,
                    ResourceUri = texture.ResourceUri
                });
            }
            if (HasUnsupportedNormal(toon, incoming))
            {
                AddError(diagnostics, "backend.toon.normal", "nodes[" + SafeDiagnosticId(toon.Id) + "]", "The first backend does not emit a normal-map branch yet.");
            }

            if (HasErrors(diagnostics))
            {
                return Result(null, diagnostics, properties, sourceMap);
            }

            var builder = new ShaderBuilder();
            builder.Line("Shader \"" + shaderName + "\"");
            builder.Line("{");
            builder.Indent++;
            builder.Line("Properties");
            builder.Line("{");
            builder.Indent++;
            if (texture != null)
            {
            builder.Line("_MainTex (\"" + TextureSlotLabels.DisplayName(graph, texture.ResourceId) + "\", 2D) = \"white\" {}");
            }
            builder.Line("_Color (\"Tint\", Color) = (1,1,1,1)");
            builder.Line(PreviewClock.Properties);
            if (threshold.PropertyName == null)
            {
                builder.Line("_NXSG_ToonThreshold (\"Toon Threshold\", Range(0,1)) = " + threshold.DefaultLiteral);
            }
            if (softness.PropertyName == null)
            {
                builder.Line("_NXSG_ToonSoftness (\"Toon Softness\", Range(0.001,1)) = " + softness.DefaultLiteral);
            }
            if (shadowStrength.PropertyName == null)
            {
                builder.Line("_NXSG_ShadowStrength (\"Shadow Strength\", Range(0,1)) = " + shadowStrength.DefaultLiteral);
            }
            EmitParameterDeclarations(builder, graph, graph.Parameters, parameterMap, properties);
            builder.Indent--;
            builder.Line("}");
            builder.Line("SubShader");
            builder.Line("{");
            builder.Indent++;
            builder.Line("Tags { \"RenderType\" = \"Opaque\" \"Queue\" = \"Geometry\"" + (fallback == null ? "" : " \"VRCFallback\" = \"" + fallback + "\"") + " }");
            var requiresLocalPosition = reachable.Any(id => nodes[id].Operation == ObjectUvOperation);
            var ramps = reachable.Where(id => nodes[id].Operation == RampOperation).Select(id => nodes[id]).OrderBy(node => node.Id, StringComparer.Ordinal).ToList();
            EmitForwardPass(builder, texture != null, texture == null ? "input.uv" : texture.UvExpression, requiresLocalPosition, tint, emission, threshold, softness, shadowStrength, properties, toon.Id, sourceMap, ramps);
            EmitForwardPass(builder, texture != null, texture == null ? "input.uv" : texture.UvExpression, requiresLocalPosition, tint, emission, threshold, softness, shadowStrength, properties, toon.Id, sourceMap, ramps, true);
            if (options.IncludeShadowCaster)
            {
                EmitShadowPass(builder, toon.Id, sourceMap);
            }
            builder.Indent--;
            builder.Line("}");
            builder.Line("CustomEditor \"NXSG.Editor.NXSGMaterialShaderGUI\"");
            builder.Line("Fallback Off");
            builder.Indent--;
            builder.Line("}");

            var source = builder.ToString();
            if (Encoding.UTF8.GetByteCount(source) > MaxGeneratedSourceBytes)
            {
                AddError(diagnostics, "backend.source.limit", "generatedSource", "Generated shader source exceeds the backend size limit.");
                return Result(null, diagnostics, properties, sourceMap);
            }

            return Result(source, diagnostics, properties, sourceMap);
        }

        private static Dictionary<string, GraphNode> IndexNodes(ShaderGraph graph, List<Diagnostic> diagnostics)
        {
            var result = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
            if (graph.Nodes == null)
            {
                AddError(diagnostics, "backend.nodes.null", "nodes", "Nodes are required.");
                return result;
            }

            if (graph.Nodes.Count > 4096)
            {
                AddError(diagnostics, "backend.nodes.limit", "nodes", "Node count exceeds the backend limit.");
            }

            for (var i = 0; i < graph.Nodes.Count; i++)
            {
                var node = graph.Nodes[i];
                if (node == null || string.IsNullOrEmpty(node.Id))
                {
                    AddError(diagnostics, "backend.node.id", "nodes[" + i.ToString(CultureInfo.InvariantCulture) + "]", "Every node needs a stable ID.");
                    continue;
                }

                if (!IsSafeId(node.Id))
                {
                    AddError(diagnostics, "backend.node.id.invalid", "nodes[" + i.ToString(CultureInfo.InvariantCulture) + "].id", "Node IDs may contain only letters, digits, underscore, and hyphen.");
                }

                if (result.ContainsKey(node.Id))
                {
                    AddError(diagnostics, "backend.node.id.duplicate", "nodes[" + i.ToString(CultureInfo.InvariantCulture) + "].id", "Node IDs must be unique.");
                    continue;
                }

                result.Add(node.Id, node);
            }

            return result;
        }

        private static Dictionary<string, GraphNode> ApplyInferredTypes(
            Dictionary<string, GraphNode> nodes, Dictionary<string, string> inferred)
        {
            var result = new Dictionary<string, GraphNode>(nodes, StringComparer.Ordinal);
            foreach (var pair in inferred)
            {
                GraphNode source;
                if (!nodes.TryGetValue(pair.Key, out source) || !GraphTypes.IsDynamic(source.Operation)) continue;
                var copy = new GraphNode { Id = source.Id, Operation = source.Operation, Version = source.Version, Properties = source.Properties == null ? new JObject() : (JObject)source.Properties.DeepClone() };
                copy.Properties["valueType"] = pair.Value;
                result[pair.Key] = copy;
            }
            return result;
        }

        private static Dictionary<string, GraphConnection> IndexIncoming(
            ShaderGraph graph,
            Dictionary<string, GraphNode> nodes,
            List<Diagnostic> diagnostics)
        {
            var result = new Dictionary<string, GraphConnection>(StringComparer.Ordinal);
            if (graph.Connections == null)
            {
                AddError(diagnostics, "backend.connections.null", "connections", "Connections are required.");
                return result;
            }

            for (var i = 0; i < graph.Connections.Count; i++)
            {
                var connection = graph.Connections[i];
                if (connection == null || connection.From == null || connection.To == null)
                {
                    AddError(diagnostics, "backend.connection.invalid", "connections[" + i.ToString(CultureInfo.InvariantCulture) + "]", "Connections need source and destination ports.");
                    continue;
                }

                if (!nodes.ContainsKey(connection.From.NodeId) || !nodes.ContainsKey(connection.To.NodeId))
                {
                    AddError(diagnostics, "backend.connection.endpoint", "connections[" + i.ToString(CultureInfo.InvariantCulture) + "]", "Connections must reference existing nodes.");
                    continue;
                }

                if (string.IsNullOrEmpty(connection.From.PortId) || string.IsNullOrEmpty(connection.To.PortId))
                {
                    AddError(diagnostics, "backend.connection.port", "connections[" + i.ToString(CultureInfo.InvariantCulture) + "]", "Connection ports are required.");
                    continue;
                }

                var key = PortKey(connection.To.NodeId, connection.To.PortId);
                if (result.ContainsKey(key))
                {
                    AddError(diagnostics, "backend.connection.multiple", "connections[" + i.ToString(CultureInfo.InvariantCulture) + "]", "An input port may have only one source in the first backend.");
                    continue;
                }

                result.Add(key, connection);
            }

            return result;
        }

        private static Dictionary<string, GraphParameter> IndexParameters(
            List<GraphParameter> parameters,
            List<Diagnostic> diagnostics)
        {
            var result = new Dictionary<string, GraphParameter>(StringComparer.Ordinal);
            if (parameters == null)
            {
                return result;
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter == null || string.IsNullOrEmpty(parameter.Id) || !IsSafeId(parameter.Id))
                {
                    AddError(diagnostics, "backend.parameter.id", "parameters[" + i.ToString(CultureInfo.InvariantCulture) + "]", "Parameter IDs must be safe stable identifiers.");
                    continue;
                }

                if (result.ContainsKey(parameter.Id))
                {
                    AddError(diagnostics, "backend.parameter.duplicate", "parameters[" + i.ToString(CultureInfo.InvariantCulture) + "].id", "Parameter IDs must be unique.");
                    continue;
                }

                result.Add(parameter.Id, parameter);
            }

            return result;
        }

        private static bool PreflightReachable(
            string nodeId,
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, GraphConnection> incoming,
            HashSet<string> reachable,
            List<Diagnostic> diagnostics)
        {
            var stack = new Stack<TraversalFrame>();
            var active = new HashSet<string>(StringComparer.Ordinal);
            var work = 0;
            stack.Push(new TraversalFrame(nodeId, 0, false));
            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                if (frame.Exit)
                {
                    active.Remove(frame.NodeId);
                    continue;
                }

                if (frame.Depth > MaxTraversalDepth)
                {
                    AddError(diagnostics, "backend.traversal.depth", "nodes[" + SafeDiagnosticId(frame.NodeId) + "]", "Reachable graph depth exceeds the backend limit.");
                    return false;
                }

                work++;
                if (work > MaxTraversalWork)
                {
                    AddError(diagnostics, "backend.traversal.work", "nodes", "Reachable graph evaluation work exceeds the backend limit of " + MaxTraversalWork.ToString(CultureInfo.InvariantCulture) + " visits; repeated inlined subgraphs can exceed this bound. Use shared lowering or simplify the graph.");
                    return false;
                }

                if (!nodes.ContainsKey(frame.NodeId))
                {
                    AddError(diagnostics, "backend.graph.node.missing", "nodes", "A reachable connection references a missing node.");
                    return false;
                }

                if (!active.Add(frame.NodeId))
                {
                    AddError(diagnostics, "backend.graph.cycle", "nodes[" + SafeDiagnosticId(frame.NodeId) + "]", "A data cycle reaches the output.");
                    return false;
                }

                reachable.Add(frame.NodeId);
                stack.Push(new TraversalFrame(frame.NodeId, frame.Depth, true));
                var ports = new List<string>();
                foreach (var port in InputPorts(nodes[frame.NodeId].Operation))
                {
                    ports.Add(port);
                }

                for (var i = ports.Count - 1; i >= 0; i--)
                {
                    GraphConnection connection;
                    if (incoming.TryGetValue(PortKey(frame.NodeId, ports[i]), out connection) && nodes.ContainsKey(connection.From.NodeId))
                    {
                        stack.Push(new TraversalFrame(connection.From.NodeId, frame.Depth + 1, false));
                    }
                }
            }

            return true;
        }

        private static void ValidateReachableOperations(HashSet<string> reachable, Dictionary<string, GraphNode> nodes, List<Diagnostic> diagnostics)
        {
            foreach (var id in reachable)
            {
                var operation = nodes[id].Operation;
                if (operation != UvOperation && operation != TextureOperation && operation != ToonOperation &&
                    operation != OutputOperation && operation != ConstantOperation && operation != MultiplyOperation &&
                    operation != ParameterOperation && operation != ValueOperation && operation != TimeOperation &&
                    operation != UvTransformOperation && operation != UvScrollOperation && operation != NoiseOperation &&
                    operation != AddOperation && operation != SubtractOperation && operation != DivideOperation &&
                    operation != MinimumOperation && operation != MaximumOperation && operation != MixOperation && operation != EmissionOperation &&
                    operation != OneMinusOperation && operation != ClampOperation && operation != UvRotateOperation &&
                    operation != PolarUvOperation && operation != ObjectUvOperation && operation != WorldUvOperation &&
                    operation != RampOperation)
                {
                    AddError(diagnostics, "backend.operation.unsupported", "nodes[" + SafeDiagnosticId(id) + "].operation", "The reachable operation is not supported by the first backend.");
                }
            }
        }

        private static GraphNode FindSingleNode(Dictionary<string, GraphNode> nodes, string operation, List<Diagnostic> diagnostics)
        {
            GraphNode found = null;
            foreach (var pair in nodes)
            {
                if (pair.Value.Operation != operation)
                {
                    continue;
                }

                if (found != null)
                {
                    AddError(diagnostics, "backend.output.multiple", "nodes", "The first backend requires one output node.");
                    return null;
                }

                found = pair.Value;
            }

            if (found == null)
            {
                AddError(diagnostics, "backend.output.missing", "nodes", "The graph needs one core.output node.");
            }

            return found;
        }

        private static GraphNode FindConnectedNode(
            GraphNode node,
            string inputPort,
            string expectedOperation,
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, GraphConnection> incoming,
            List<Diagnostic> diagnostics)
        {
            GraphConnection connection;
            if (!incoming.TryGetValue(PortKey(node.Id, inputPort), out connection))
            {
                AddError(diagnostics, "backend.connection.missing", "nodes[" + SafeDiagnosticId(node.Id) + "]." + inputPort, "The required input is not connected.");
                return null;
            }

            GraphNode source = nodes[connection.From.NodeId];
            if (source.Operation != expectedOperation)
            {
                AddError(diagnostics, "backend.connection.type", "nodes[" + SafeDiagnosticId(node.Id) + "]." + inputPort, "The connected operation is not supported for this input.");
                return null;
            }

            return source;
        }

        private static TextureInfo ResolveTexture(
            HashSet<string> reachable,
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters,
            List<GraphResource> resources,
            List<Diagnostic> diagnostics)
        {
            var textures = nodes.Values.Where(n => reachable.Contains(n.Id) && n.Operation == TextureOperation).ToList();
            if (textures.Count == 0) return null;
            if (textures.Count > 1)
            {
                AddError(diagnostics, "backend.texture.count", "nodes", "This backend currently supports one reachable texture node. Mix it with colors or noise; multiple texture samplers are not yet supported.");
                return null;
            }
            var node = textures[0];
            var resourceId = PropertyString(node, "resourceId");
            if (string.IsNullOrEmpty(resourceId) || !IsSafeId(resourceId))
            {
                AddError(diagnostics, "backend.texture.resource", "nodes[" + SafeDiagnosticId(node.Id) + "].properties.resourceId", "Texture resourceId is required and must be safe.");
                return null;
            }

            GraphResource resource = null;
            if (resources != null)
            {
                for (var i = 0; i < resources.Count; i++)
                {
                    if (resources[i] != null && resources[i].Id == resourceId)
                    {
                        resource = resources[i];
                        break;
                    }
                }
            }

            if (resource == null || resource.Kind != "texture2D")
            {
                AddError(diagnostics, "backend.texture.resource.missing", "resources[" + SafeDiagnosticId(resourceId) + "]", "Texture resource must resolve to a texture2D resource.");
                return null;
            }

            return new TextureInfo(node.Id, resourceId, resource.Uri, ResolveUv(node, nodes, incoming, parameters, new HashSet<string>(StringComparer.Ordinal), diagnostics));
        }

        private static ColorInfo ResolveColor(
            GraphNode toon,
            string inputPort,
            bool textureExists,
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters,
            List<Diagnostic> diagnostics)
        {
            GraphConnection connection;
            if (!incoming.TryGetValue(PortKey(toon.Id, inputPort), out connection))
            {
                if (inputPort == "albedo")
                    AddError(diagnostics, "backend.albedo.missing", "nodes[" + SafeDiagnosticId(toon.Id) + "].albedo", "Connect a color, texture, or procedural branch to Toon albedo.");
                return new ColorInfo("(0,0,0,1)", "fixed4(0,0,0,1)");
            }
            var source = nodes[connection.From.NodeId];
            if (IsScalarOutput(source, connection.From.PortId, nodes, incoming, parameters, new HashSet<string>(StringComparer.Ordinal)))
                return new ColorInfo("(1,1,1,1)", PromoteScalar(ResolveScalarExpression(source, nodes, incoming, parameters,
                    new HashSet<string>(StringComparer.Ordinal), diagnostics)), null, true);
            var result = ResolveColorExpression(source, nodes, incoming, parameters,
                new HashSet<string>(StringComparer.Ordinal), diagnostics);
            return result ?? new ColorInfo("(0,0,0,1)", "fixed4(0,0,0,1)");
        }

        private static ColorInfo ResolveParameterColor(
            GraphNode node,
            Dictionary<string, GraphParameter> parameters,
            List<Diagnostic> diagnostics)
        {
            var parameterId = PropertyString(node, "parameterId");
            GraphParameter parameter;
            if (string.IsNullOrEmpty(parameterId) || !parameters.TryGetValue(parameterId, out parameter))
            {
                AddError(diagnostics, "backend.parameter.missing", "nodes[" + SafeDiagnosticId(node.Id) + "].properties.parameterId", "Referenced parameter does not exist.");
                return null;
            }

            if (parameter.Type != GraphValueType.Color && parameter.Type != GraphValueType.Vector4)
            {
                AddError(diagnostics, "backend.parameter.type", "parameters[" + SafeDiagnosticId(parameter.Id) + "]", "A color input needs a color or vector4 parameter.");
                return null;
            }

            if (parameter.Binding == GraphBindingKind.Global || parameter.Binding == GraphBindingKind.AudioLink)
            {
                AddError(diagnostics, "backend.parameter.binding", "parameters[" + SafeDiagnosticId(parameter.Id) + "]", "Global and AudioLink color bindings are not emitted by the first backend.");
                return null;
            }

            LiteralValue parameterDefault;
            if (!TryReadLiteral(parameter.DefaultValue, out parameterDefault))
            {
                AddError(diagnostics, "backend.parameter.default", "parameters[" + SafeDiagnosticId(parameter.Id) + "].defaultValue", "Color parameters need a finite scalar or 3/4 component default.");
                return null;
            }

            if (parameter.Binding == GraphBindingKind.Constant)
            {
                return new ColorInfo(parameterDefault.ToLiteral(), parameterDefault.ToHlsl(), null, true);
            }

            return new ColorInfo(parameterDefault.ToLiteral(), ParameterPropertyName(parameter.Id), parameter.Id);
        }

        private static ColorInfo ResolveColorExpression(
            GraphNode node,
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters,
            HashSet<string> visiting,
            List<Diagnostic> diagnostics)
        {
            if (node.Operation != NoiseOperation && IsScalarOutput(node, "value", nodes, incoming, parameters, new HashSet<string>(StringComparer.Ordinal)))
                return new ColorInfo("(1,1,1,1)", PromoteScalar(ResolveScalarExpression(node, nodes, incoming, parameters, visiting, diagnostics)), null, true);
            if (!visiting.Add(node.Id))
            {
                AddError(diagnostics, "backend.expression.cycle", "nodes[" + SafeDiagnosticId(node.Id) + "]", "A color expression contains a cycle.");
                return null;
            }

            string expression = null;
            if (node.Operation == ConstantOperation)
            {
                LiteralValue literal;
                if (TryReadLiteral(node.Properties?["value"], out literal)) expression = literal.ToHlsl();
            }
            else if (node.Operation == ParameterOperation)
            {
                var parameter = ResolveParameterColor(node, parameters, diagnostics);
                expression = parameter?.ShaderExpression;
            }
            else if (node.Operation == MultiplyOperation || node.Operation == AddOperation || node.Operation == SubtractOperation ||
                node.Operation == DivideOperation || node.Operation == MinimumOperation || node.Operation == MaximumOperation || node.Operation == MixOperation)
            {
                var leftFallback = node.Operation == MultiplyOperation || node.Operation == DivideOperation ? "fixed4(1,1,1,1)" : "fixed4(0,0,0,1)";
                var rightFallback = node.Operation == AddOperation || node.Operation == SubtractOperation || node.Operation == MinimumOperation || node.Operation == MaximumOperation ? "fixed4(0,0,0,1)" : "fixed4(1,1,1,1)";
                var left = ColorInput(node, "a", leftFallback, nodes, incoming, parameters, visiting, diagnostics);
                var right = ColorInput(node, "b", rightFallback, nodes, incoming, parameters, visiting, diagnostics);
                if (node.Operation == MixOperation)
                {
                    var factor = ScalarInput(node, "factor", FloatLiteral(PropertyFloat(node, "factor", .5f)), nodes, incoming, parameters, visiting, diagnostics);
                    expression = "lerp(" + left + "," + right + ",saturate(" + factor + "))";
                }
                else if (node.Operation == AddOperation) expression = "(" + left + " + " + right + ")";
                else if (node.Operation == SubtractOperation) expression = "(" + left + " - " + right + ")";
                else if (node.Operation == DivideOperation) expression = "NXSG_SafeDivide(" + left + "," + right + ")";
                else if (node.Operation == MinimumOperation) expression = "min(" + left + "," + right + ")";
                else if (node.Operation == MaximumOperation) expression = "max(" + left + "," + right + ")";
                else expression = "(" + left + " * " + right + ")";
            }
            else if (node.Operation == OneMinusOperation || node.Operation == ClampOperation)
            {
                var color = ColorInput(node, "color", "fixed4(0,0,0,1)", nodes, incoming, parameters, visiting, diagnostics);
                expression = node.Operation == OneMinusOperation ? "(1 - " + color + ")" : "saturate(" + color + ")";
            }
            else if (node.Operation == EmissionOperation)
            {
                var color = ColorInput(node, "color", "fixed4(1,1,1,1)", nodes, incoming, parameters, visiting, diagnostics);
                var strength = ScalarInput(node, "strength", FloatLiteral(PropertyFloat(node, "strength", 1f)), nodes, incoming, parameters, visiting, diagnostics);
                expression = "(" + color + " * " + strength + ")";
            }
            else if (node.Operation == TextureOperation)
            {
                var uv = ResolveUv(node, nodes, incoming, parameters, visiting, diagnostics);
                expression = "tex2D(_MainTex, (" + uv + ") * _MainTex_ST.xy + _MainTex_ST.zw)";
            }
            else if (node.Operation == NoiseOperation)
                expression = "NXSG_NoiseColor(" + NoiseExpression(node, nodes, incoming, parameters, visiting, diagnostics) + ")";
            if (expression == null)
                AddError(diagnostics, "backend.color.expression", "nodes[" + SafeDiagnosticId(node.Id) + "]", "The node cannot produce a color expression.");
            visiting.Remove(node.Id);
            return expression == null ? null : new ColorInfo("(1,1,1,1)", expression, null, true);
        }

        private static string ColorInput(GraphNode node, string port, string fallback,
            Dictionary<string, GraphNode> nodes, Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters, HashSet<string> visiting, List<Diagnostic> diagnostics)
        {
            GraphConnection edge;
            if (!incoming.TryGetValue(PortKey(node.Id, port), out edge)) return fallback;
            if (IsScalarOutput(nodes[edge.From.NodeId], edge.From.PortId, nodes, incoming, parameters,
                new HashSet<string>(StringComparer.Ordinal)))
                return PromoteScalar(ResolveScalarExpression(nodes[edge.From.NodeId], nodes, incoming, parameters, visiting, diagnostics));
            return ResolveColorExpression(nodes[edge.From.NodeId], nodes, incoming, parameters, visiting, diagnostics)?.ShaderExpression ?? fallback;
        }

        private static string ScalarInput(GraphNode node, string port, string fallback,
            Dictionary<string, GraphNode> nodes, Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters, HashSet<string> visiting, List<Diagnostic> diagnostics)
        {
            GraphConnection edge;
            if (!incoming.TryGetValue(PortKey(node.Id, port), out edge)) return fallback;
            return ResolveScalarExpression(nodes[edge.From.NodeId], nodes, incoming, parameters, visiting, diagnostics);
        }

        private static string NoiseExpression(GraphNode node, Dictionary<string, GraphNode> nodes,
            Dictionary<string, GraphConnection> incoming, Dictionary<string, GraphParameter> parameters,
            HashSet<string> visiting, List<Diagnostic> diagnostics)
        {
            var uv = ResolveUv(node, nodes, incoming, parameters, visiting, diagnostics);
            var time = ScalarInput(node, "time", "NXSG_Time()", nodes, incoming, parameters, visiting, diagnostics);
            var scale = FloatLiteral(PropertyFloat(node, "scale", 5f));
            var speed = FloatLiteral(PropertyFloat(node, "speed", 1f));
            return "NXSG_ValueNoise((" + uv + ") * " + scale + " + float2(1,0.731) * (" + time + ") * " + speed + ")";
        }

        private static ScalarInfo ResolveScalar(
            GraphNode node,
            string property,
            string parameterProperty,
            float fallback,
            Dictionary<string, GraphParameter> parameters,
            List<MaterialProperty> materialProperties,
            List<Diagnostic> diagnostics)
        {
            var parameterId = PropertyString(node, parameterProperty);
            if (!string.IsNullOrEmpty(parameterId))
            {
                GraphParameter parameter;
                if (!parameters.TryGetValue(parameterId, out parameter))
                {
                    AddError(diagnostics, "backend.parameter.missing", "nodes[" + SafeDiagnosticId(node.Id) + "].properties." + parameterProperty, "Referenced parameter does not exist.");
                    return new ScalarInfo(fallback.ToString("R", CultureInfo.InvariantCulture), null);
                }

                if (parameter.Type != GraphValueType.Float || parameter.Binding == GraphBindingKind.Global || parameter.Binding == GraphBindingKind.AudioLink)
                {
                    AddError(diagnostics, "backend.parameter.binding", "parameters[" + SafeDiagnosticId(parameter.Id) + "]", "Toon scalar parameters must be float material or animatedMaterial bindings.");
                    return new ScalarInfo(fallback.ToString("R", CultureInfo.InvariantCulture), null);
                }

                var propertyName = ParameterPropertyName(parameter.Id);
                return new ScalarInfo(propertyName, propertyName, parameter.Id, DefaultLiteral(parameter));
            }

            var token = node.Properties == null ? null : node.Properties[property];
            float value;
            if (token != null && TryReadFloat(token, out value))
            {
                value = Clamp01(value);
            }
            else
            {
                value = fallback;
            }

            var shaderProperty = property == "threshold"
                ? "_NXSG_ToonThreshold"
                : (property == "softness" ? "_NXSG_ToonSoftness" : "_NXSG_ShadowStrength");
            return new ScalarInfo(shaderProperty, null, null, value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AddMaterialParameters(
            List<GraphParameter> parameters,
            Dictionary<string, GraphParameter> parameterMap,
            List<MaterialProperty> materialProperties,
            List<Diagnostic> diagnostics)
        {
            if (parameters == null)
            {
                return;
            }

            var emittedNames = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter == null || parameter.Binding == GraphBindingKind.Constant)
                {
                    continue;
                }

                if (parameter.Binding == GraphBindingKind.Global || parameter.Binding == GraphBindingKind.AudioLink)
                {
                    AddWarning(diagnostics, "backend.parameter.unemitted", "parameters[" + SafeDiagnosticId(parameter.Id) + "]", "Global and AudioLink bindings are not material properties in this first backend.");
                    continue;
                }

                if (parameter.Type != GraphValueType.Float && parameter.Type != GraphValueType.Color && parameter.Type != GraphValueType.Vector4)
                {
                    AddError(diagnostics, "backend.parameter.type", "parameters[" + SafeDiagnosticId(parameter.Id) + "]", "Only float, color, and vector4 material parameters are emitted by this backend.");
                    continue;
                }

                var propertyName = ParameterPropertyName(parameter.Id);
                if (!emittedNames.Add(propertyName))
                {
                    AddError(diagnostics, "backend.parameter.symbol-collision", "parameters[" + SafeDiagnosticId(parameter.Id) + "]", "Parameter IDs map to the same generated material symbol.");
                    continue;
                }

                materialProperties.Add(new MaterialProperty
                {
                    Name = propertyName,
                    DisplayName = SafeLabel(parameter.Name),
                    Type = parameter.Type,
                    Binding = parameter.Binding,
                    ParameterId = parameter.Id
                });
            }
        }

        private static void EmitParameterDeclarations(
            ShaderBuilder builder,
            ShaderGraph graph,
            List<GraphParameter> parameters,
            Dictionary<string, GraphParameter> parameterMap,
            List<MaterialProperty> materialProperties)
        {
            string lastHeader = null;
            foreach (var property in materialProperties.OrderBy(p=>p.ParameterId==null?"":MaterialGroups.HeaderFor(graph,p.ParameterId)??""))
            {
                if (property.ParameterId == null)
                {
                    continue;
                }

                GraphParameter parameter = parameterMap[property.ParameterId];
                var header = MaterialGroups.HeaderFor(graph, parameter.Id);
                if (header != null && header != lastHeader) { builder.Line(header); lastHeader = header; }
                var defaultValue = DefaultLiteral(parameter);
                if (parameter.Type == GraphValueType.Float)
                {
                    builder.Line(property.Name + " (\"" + EscapeShaderString(property.DisplayName) + "\", Range(0,1)) = " + defaultValue);
                }
                else if (parameter.Type == GraphValueType.Vector4)
                {
                    builder.Line(property.Name + " (\"" + EscapeShaderString(property.DisplayName) + "\", Vector) = " + defaultValue);
                }
                else
                {
                    builder.Line(property.Name + " (\"" + EscapeShaderString(property.DisplayName) + "\", Color) = " + defaultValue);
                }
            }
        }

        private static void EmitForwardPass(
            ShaderBuilder builder,
            bool hasTexture,
            string textureUv,
            bool requiresLocalPosition,
            ColorInfo tint,
            ColorInfo emission,
            ScalarInfo threshold,
            ScalarInfo softness,
            ScalarInfo shadowStrength,
            List<MaterialProperty> materialProperties,
            string toonNodeId,
            List<SourceMapEntry> sourceMap,
            List<GraphNode> ramps,
            bool additive = false)
        {
            builder.Line("Pass");
            builder.Line("{");
            builder.Indent++;
            builder.Line("Name \"" + (additive ? "ForwardAdd" : "ForwardBase") + "\"");
            builder.Line("Tags { \"LightMode\" = \"" + (additive ? "ForwardAdd" : "ForwardBase") + "\" }");
            builder.Line("Cull Back");
            builder.Line("ZTest LEqual");
            builder.Line(additive ? "ZWrite Off" : "ZWrite On");
            if (additive) builder.Line("Blend One One\nColorMask RGB");
            builder.Line("CGPROGRAM");
            builder.Line("#pragma target 3.0");
            builder.Line("#pragma vertex vert");
            builder.Line("#pragma fragment frag");
            builder.Line(additive ? "#pragma multi_compile_fwdadd_fullshadows" : "#pragma multi_compile_fwdbase");
            builder.Line("#pragma multi_compile_instancing");
            builder.Line("#include \"UnityCG.cginc\"");
            builder.Line("#include \"Lighting.cginc\"");
            builder.Line("#include \"AutoLight.cginc\"");
            builder.Line(PreviewClock.Hlsl);
            if (hasTexture)
            {
                builder.Line("sampler2D _MainTex;");
                builder.Line("float4 _MainTex_ST;");
            }
            builder.Line("fixed4 _Color;");
            builder.Line("half _NXSG_ToonThreshold;");
            builder.Line("half _NXSG_ToonSoftness;");
            builder.Line("half _NXSG_ShadowStrength;");
            foreach (var property in materialProperties)
            {
                if (property.Type == GraphValueType.Texture2D)
                {
                    continue;
                }

                builder.Line(property.Type == GraphValueType.Float
                    ? "half " + property.Name + ";"
                    : "fixed4 " + property.Name + ";");
            }
            builder.Line("struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };");
            builder.Line("struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2;" + (requiresLocalPosition ? " float3 localPosition : TEXCOORD3;" : "") + " LIGHTING_COORDS(" + (requiresLocalPosition ? "4,5" : "3,4") + ") UNITY_VERTEX_OUTPUT_STEREO };");
            var start = builder.LineNumber + 1;
            builder.Line("#line 1 \"nxsg://node/texture\"");
            builder.Line("v2f vert(appdata v) { v2f output; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output); float4 positionWS = mul(unity_ObjectToWorld, v.vertex); output.pos = UnityWorldToClipPos(positionWS.xyz); output.positionWS = positionWS.xyz; output.normalWS = UnityObjectToWorldNormal(v.normal); output.uv = v.uv;" + (requiresLocalPosition ? " output.localPosition = v.vertex.xyz;" : "") + " TRANSFER_VERTEX_TO_FRAGMENT(output); return output; }");
            var end = builder.LineNumber;
            sourceMap.Add(new SourceMapEntry { NodeId = toonNodeId, PortId = "vertex", StartLine = start, EndLine = end });
            start = builder.LineNumber + 1;
            builder.Line("#line 1 \"nxsg://node/toon\"");
            var colorExpression = "(" + HlslValue(tint) + ") * _Color";
            if (RequiresNoise(tint, emission))
            {
                builder.Line("float NXSG_NoiseHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }");
                builder.Line("float NXSG_ValueNoise(float2 p) { float2 cell=floor(p); float2 f=frac(p); f=f*f*(3-2*f); return lerp(lerp(NXSG_NoiseHash(cell),NXSG_NoiseHash(cell+float2(1,0)),f.x),lerp(NXSG_NoiseHash(cell+float2(0,1)),NXSG_NoiseHash(cell+float2(1,1)),f.x),f.y); }");
                builder.Line("fixed4 NXSG_NoiseColor(float value) { return fixed4(value,value,value,1); }");
            }
            if (RequiresCoordinateHelper(textureUv, tint, emission, "NXSG_UvRotate"))
                builder.Line("float2 NXSG_UvRotate(float2 source, float2 center, float angleDegrees) { float radians = angleDegrees * 0.017453292519943295; float s = sin(radians); float c = cos(radians); float2 delta = source - center; return center + float2(c * delta.x - s * delta.y, s * delta.x + c * delta.y); }");
            if (RequiresCoordinateHelper(textureUv, tint, emission, "NXSG_PolarUv"))
                builder.Line("float2 NXSG_PolarUv(float2 source, float2 center, float radialScale, float angleScale) { float2 delta = source - center; float angle = dot(delta, delta) > 1e-12 ? atan2(delta.y, delta.x) : 0.0; return float2(length(delta) * 2.0 * radialScale, (angle / 6.283185307179586 + 0.5) * angleScale); }");
            if (RequiresColorHelper(tint, emission, "NXSG_SafeDivide"))
                builder.Line("float4 NXSG_SafeDivide(float4 a, float4 b) { float4 denominator = max(abs(b), 1e-5); denominator = (b < 0 ? -denominator : denominator); return a / denominator; }");
            if (RequiresColorHelper(tint, emission, "NXSG_Splat"))
                builder.Line("float4 NXSG_Splat(float value) { return float4(value,value,value,value); }");
            if (RequiresCoordinateHelper(textureUv, tint, emission, "NXSG_SafeDivideScalar"))
                builder.Line("float NXSG_SafeDivideScalar(float a, float b) { float d=max(abs(b),1e-5); return a/(b<0 ? -d : d); }");
            for (var i = 0; i < ramps.Count; i++)
                builder.Line(RampHelper(ramps[i]));
            var lighting = additive
                ? "UNITY_LIGHT_ATTENUATION(atten,input,input.positionWS); half3 ambient=0; half3 direct=_LightColor0.rgb*lerp(0.35h,1.0h,toonBand)*atten;"
                : "half shadow=SHADOW_ATTENUATION(input); shadow=lerp(1.0h,shadow,saturate(" + HlslValue(shadowStrength) + ")); half3 ambient=ShadeSH9(half4(normalWS,1.0h)); half3 direct=_LightColor0.rgb*lerp(0.35h,1.0h,toonBand)*shadow;";
            builder.Line("fixed4 frag(v2f input) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); half3 normalWS=normalize(input.normalWS); half3 lightDirection=normalize(UnityWorldSpaceLightDir(input.positionWS)); half ndotl=saturate(dot(normalWS,lightDirection)); half softness=max(" + HlslValue(softness) + ",0.001h); half toonBand=smoothstep(" + HlslValue(threshold) + "-softness," + HlslValue(threshold) + "+softness,ndotl); " + lighting + " fixed4 textureColor=" + colorExpression + "; return fixed4(textureColor.rgb*(ambient+direct)" + (additive ? "" : " + (" + HlslValue(emission) + ").rgb") + "," + (additive ? "0" : "1") + "); }");
            end = builder.LineNumber;
            sourceMap.Add(new SourceMapEntry { NodeId = toonNodeId, PortId = "surface", StartLine = start, EndLine = end });
            builder.Line("ENDCG");
            builder.Indent--;
            builder.Line("}");
        }

        private static void EmitShadowPass(ShaderBuilder builder, string toonNodeId, List<SourceMapEntry> sourceMap)
        {
            builder.Line("Pass");
            builder.Line("{");
            builder.Indent++;
            builder.Line("Name \"ShadowCaster\"");
            builder.Line("Tags { \"LightMode\" = \"ShadowCaster\" }");
            builder.Line("Cull Back");
            builder.Line("ZTest LEqual");
            builder.Line("ZWrite On");
            builder.Line("CGPROGRAM");
            builder.Line("#pragma target 3.0");
            builder.Line("#pragma vertex vertShadow");
            builder.Line("#pragma fragment fragShadow");
            builder.Line("#pragma multi_compile_shadowcaster");
            builder.Line("#pragma multi_compile_instancing");
            builder.Line("#include \"UnityCG.cginc\"");
            builder.Line("struct shadowAppdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };");
            builder.Line("struct shadowV2f { V2F_SHADOW_CASTER; UNITY_VERTEX_OUTPUT_STEREO };");
            var start = builder.LineNumber + 1;
            builder.Line("#line 1 \"nxsg://pass/shadow-caster\"");
            builder.Line("shadowV2f vertShadow(shadowAppdata v) { shadowV2f output; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(shadowV2f, output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output); TRANSFER_SHADOW_CASTER_NORMALOFFSET(output); return output; }");
            builder.Line("float4 fragShadow(shadowV2f input) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input); SHADOW_CASTER_FRAGMENT(input); }");
            var end = builder.LineNumber;
            sourceMap.Add(new SourceMapEntry { NodeId = toonNodeId, PortId = "shadow", StartLine = start, EndLine = end });
            builder.Line("ENDCG");
            builder.Indent--;
            builder.Line("}");
        }

        private static bool HasUnsupportedNormal(GraphNode toon, Dictionary<string, GraphConnection> incoming)
        {
            return incoming.ContainsKey(PortKey(toon.Id, "normal"));
        }

        private static bool IsUvOperation(string operation)
        {
            return operation == UvOperation || operation == UvTransformOperation || operation == UvScrollOperation ||
                operation == UvRotateOperation || operation == PolarUvOperation || operation == ObjectUvOperation || operation == WorldUvOperation;
        }

        private static string ResolveUv(GraphNode node, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters, HashSet<string> visiting, List<Diagnostic> diagnostics)
        {
            GraphConnection input;
            if (incoming.TryGetValue(PortKey(node.Id, "uv"), out input) && nodes.ContainsKey(input.From.NodeId))
                return ResolveUvNode(nodes[input.From.NodeId], nodes, incoming, parameters, visiting, diagnostics);
            return "input.uv";
        }

        private static string ResolveUvNode(GraphNode node, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters, HashSet<string> visiting, List<Diagnostic> diagnostics)
        {
            if (!visiting.Add(node.Id)) return "input.uv";
            GraphConnection input;
            var source = "input.uv";
            if (incoming.TryGetValue(PortKey(node.Id, "uv"), out input) && nodes.ContainsKey(input.From.NodeId))
                source = ResolveUvNode(nodes[input.From.NodeId], nodes, incoming, parameters, visiting, diagnostics);
            string result = source;
            if (node.Operation == UvTransformOperation)
                result = "(" + source + " * " + Vector2Literal(node, "tiling", 1f, 1f) + " + " + Vector2Literal(node, "offset", 0f, 0f) + ")";
            else if (node.Operation == UvScrollOperation)
                result = "(" + source + " + (" + ScalarInput(node, "time", "NXSG_Time()", nodes, incoming, parameters, visiting, diagnostics) + ") * " + Vector2Literal(node, "speed", .1f, 0f) + ")";
            else if (node.Operation == UvRotateOperation)
            {
                var center = Vector2Literal(node, "center", .5f, .5f);
                var angle = ScalarInput(node, "angle", FloatLiteral(PropertyFloat(node, "angle", 0f)), nodes, incoming, parameters, visiting, diagnostics);
                result = "NXSG_UvRotate((" + source + ")," + center + ",(" + angle + "))";
            }
            else if (node.Operation == PolarUvOperation)
            {
                var center = Vector2Literal(node, "center", .5f, .5f);
                result = "NXSG_PolarUv((" + source + ")," + center + "," + FloatLiteral(PropertyFloat(node, "radialScale", 1f)) + "," + FloatLiteral(PropertyFloat(node, "angleScale", 1f)) + ")";
            }
            else if (node.Operation == ObjectUvOperation)
                result = "input.localPosition.xz";
            else if (node.Operation == WorldUvOperation)
                result = "input.positionWS.xz";
            visiting.Remove(node.Id);
            return result;
        }

        private static string ResolveScalarExpression(GraphNode node, Dictionary<string, GraphNode> nodes, Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters, HashSet<string> visiting, List<Diagnostic> diagnostics)
        {
            if (!visiting.Add(node.Id))
            {
                AddError(diagnostics, "backend.expression.cycle", "nodes[" + SafeDiagnosticId(node.Id) + "]", "Expression cycle detected.");
                return "0";
            }
            string expression = null;
            if (node.Operation == ValueOperation) expression = FloatLiteral(PropertyFloat(node, "value", 0f));
            else if (node.Operation == TimeOperation) expression = "(NXSG_Time() * " + FloatLiteral(PropertyFloat(node, "speed", 1f)) + " + " + FloatLiteral(PropertyFloat(node, "offset", 0f)) + ")";
            else if (node.Operation == ConstantOperation)
            {
                LiteralValue literal;
                if (TryReadLiteral(node.Properties?["value"], out literal)) expression = FloatLiteral(literal.X);
            }
            else if (node.Operation == ParameterOperation)
            {
                var id = PropertyString(node, "parameterId"); GraphParameter parameter;
                if (id != null && parameters.TryGetValue(id, out parameter) && parameter.Type == GraphValueType.Float)
                {
                    if (parameter.Binding == GraphBindingKind.Constant) expression = DefaultLiteral(parameter);
                    else if (parameter.Binding == GraphBindingKind.Material || parameter.Binding == GraphBindingKind.AnimatedMaterial) expression = ParameterPropertyName(id);
                }
            }
            else if (node.Operation == NoiseOperation) expression = NoiseExpression(node, nodes, incoming, parameters, visiting, diagnostics);
            else if (node.Operation == RampOperation)
            {
                var value = ScalarInput(node, "value", "0", nodes, incoming, parameters, visiting, diagnostics);
                expression = RampName(node) + "((" + value + "))";
            }
            else if (node.Operation == OneMinusOperation || node.Operation == ClampOperation)
                expression = node.Operation == OneMinusOperation ? "(1 - " + ScalarInput(node, "color", "0", nodes, incoming, parameters, visiting, diagnostics) + ")" : "saturate(" + ScalarInput(node, "color", "0", nodes, incoming, parameters, visiting, diagnostics) + ")";
            else if (node.Operation == AddOperation || node.Operation == SubtractOperation || node.Operation == MultiplyOperation ||
                node.Operation == DivideOperation || node.Operation == MinimumOperation || node.Operation == MaximumOperation || node.Operation == MixOperation)
            {
                var leftFallback = node.Operation == MultiplyOperation || node.Operation == DivideOperation ? "1" : "0";
                var rightFallback = node.Operation == AddOperation || node.Operation == SubtractOperation || node.Operation == MinimumOperation || node.Operation == MaximumOperation ? "0" : "1";
                var left = ScalarInput(node, "a", leftFallback, nodes, incoming, parameters, visiting, diagnostics);
                var right = ScalarInput(node, "b", rightFallback, nodes, incoming, parameters, visiting, diagnostics);
                if (node.Operation == MixOperation)
                    expression = "lerp(" + left + "," + right + ",saturate(" + ScalarInput(node, "factor", FloatLiteral(PropertyFloat(node, "factor", .5f)), nodes, incoming, parameters, visiting, diagnostics) + "))";
                else if (node.Operation == AddOperation) expression = "(" + left + " + " + right + ")";
                else if (node.Operation == SubtractOperation) expression = "(" + left + " - " + right + ")";
                else if (node.Operation == DivideOperation) expression = "NXSG_SafeDivideScalar(" + left + "," + right + ")";
                else if (node.Operation == MinimumOperation) expression = "min(" + left + "," + right + ")";
                else if (node.Operation == MaximumOperation) expression = "max(" + left + "," + right + ")";
                else expression = "(" + left + " * " + right + ")";
            }
            if (expression == null) AddError(diagnostics, "backend.scalar.expression", "nodes[" + SafeDiagnosticId(node.Id) + "]", "The node cannot produce a supported scalar expression.");
            visiting.Remove(node.Id);
            return expression ?? "0";
        }

        private static string PromoteScalar(string expression)
        {
            return "NXSG_Splat(" + expression + ")";
        }

        private static bool IsScalarOutput(GraphNode node, string port,
            Dictionary<string, GraphNode> nodes, Dictionary<string, GraphConnection> incoming,
            Dictionary<string, GraphParameter> parameters, HashSet<string> visiting)
        {
            if (node == null || !visiting.Add(node.Id)) return false;
            if (node.Operation == ValueOperation || node.Operation == TimeOperation ||
                (node.Operation == NoiseOperation && port == "value") || node.Operation == RampOperation)
                return true;
            if (GraphTypes.IsDynamic(node.Operation) && string.Equals(PropertyString(node, "valueType"), "float", StringComparison.OrdinalIgnoreCase))
                return true;
            if (node.Operation == ParameterOperation)
            {
                GraphParameter parameter;
                var id = PropertyString(node, "parameterId");
                return id != null && parameters.TryGetValue(id, out parameter) && parameter.Type == GraphValueType.Float;
            }
            if (node.Operation == ConstantOperation)
                return string.Equals(PropertyString(node, "valueType"), "float", StringComparison.OrdinalIgnoreCase) ||
                    (node.Properties != null && node.Properties["value"] != null &&
                     (node.Properties["value"].Type == JTokenType.Float || node.Properties["value"].Type == JTokenType.Integer));
            visiting.Remove(node.Id);
            return false;
        }

        private static string Vector2Literal(GraphNode node, string property, float x, float y)
        {
            var token = node.Properties == null ? null : node.Properties[property] as JArray;
            if (token != null && token.Count >= 2) { float a, b; if (TryReadFloat(token[0], out a) && TryReadFloat(token[1], out b)) return "float2(" + FloatLiteral(a) + "," + FloatLiteral(b) + ")"; }
            return "float2(" + FloatLiteral(x) + "," + FloatLiteral(y) + ")";
        }

        private static float PropertyFloat(GraphNode node, string property, float fallback)
        {
            float value; return node.Properties != null && TryReadFloat(node.Properties[property], out value) ? value : fallback;
        }

        private static string FloatLiteral(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string RampName(GraphNode node)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(node.Id ?? string.Empty));
                var text = new StringBuilder(16);
                for (var i = 0; i < 8; i++) text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return "NXSG_Ramp_" + text;
            }
        }

        private static string RampHelper(GraphNode node)
        {
            var points = new List<float[]>();
            var array = node.Properties == null ? null : node.Properties["points"] as JArray;
            if (array != null)
            {
                for (var i = 0; i < array.Count && points.Count < 16; i++)
                {
                    var point = array[i] as JArray;
                    float x, y;
                    if (point != null && point.Count >= 2 && TryReadFloat(point[0], out x) && TryReadFloat(point[1], out y))
                        points.Add(new[] { x, y });
                }
            }
            if (points.Count == 0) { points.Add(new[] { 0f, 0f }); points.Add(new[] { 1f, 1f }); }
            if (points.Count == 1) points.Add(new[] { 1f, 1f });
            var b = new StringBuilder();
            b.Append("float ").Append(RampName(node)).Append("(float v) { float black=")
                .Append(FloatLiteral(PropertyFloat(node, "blackPoint", 0f))).Append("; float white=")
                .Append(FloatLiteral(PropertyFloat(node, "whitePoint", 1f))).Append("; float smooth=")
                .Append(FloatLiteral(PropertyFloat(node, "smoothness", 0f))).Append("; float t; ");
            b.Append("if (abs(white-black)<1e-6) t=step(black,v); else t=saturate((v-black)/(white-black)); float y;");
            b.Append("if (t <= ").Append(FloatLiteral(points[0][0])).Append(") y=").Append(FloatLiteral(points[0][1])).Append(";");
            for (var i = 1; i < points.Count; i++)
            {
                var x0 = points[i - 1][0]; var y0 = points[i - 1][1];
                var x1 = points[i][0]; var y1 = points[i][1];
                b.Append(" else if (t <= ").Append(FloatLiteral(x1)).Append(") { float q=saturate((t-")
                    .Append(FloatLiteral(x0)).Append(")/").Append(FloatLiteral(Math.Max(x1 - x0, 1e-6f))).Append("); q=lerp(q,q*q*(3-2*q),saturate(smooth)); y=lerp(")
                    .Append(FloatLiteral(y0)).Append(",").Append(FloatLiteral(y1)).Append(",q); }");
            }
            b.Append(" else y=").Append(FloatLiteral(points[points.Count - 1][1])).Append("; return y; }");
            return b.ToString();
        }

        private static bool RequiresNoise(ColorInfo a, ColorInfo b) { return a != null && (a.ShaderExpression.Contains("NXSG_ValueNoise") || b.ShaderExpression.Contains("NXSG_ValueNoise")); }
        private static bool RequiresCoordinateHelper(string textureUv, ColorInfo a, ColorInfo b, string helper)
        {
            return (textureUv != null && textureUv.Contains(helper)) ||
                (a != null && a.ShaderExpression != null && a.ShaderExpression.Contains(helper)) ||
                (b != null && b.ShaderExpression != null && b.ShaderExpression.Contains(helper));
        }

        private static bool RequiresColorHelper(ColorInfo a, ColorInfo b, string helper)
        {
            return (a != null && a.ShaderExpression != null && a.ShaderExpression.Contains(helper)) ||
                (b != null && b.ShaderExpression != null && b.ShaderExpression.Contains(helper));
        }

        private static LiteralValue? TryResolveLiteral(
            GraphNode node,
            Dictionary<string, GraphNode> nodes,
            Dictionary<string, GraphConnection> incoming,
            HashSet<string> visiting,
            List<Diagnostic> diagnostics)
        {
            if (!visiting.Add(node.Id))
            {
                AddError(diagnostics, "backend.expression.cycle", "nodes[" + SafeDiagnosticId(node.Id) + "]", "A literal expression contains a cycle.");
                return null;
            }

            LiteralValue? value = null;
            if (node.Operation == ConstantOperation)
            {
                JToken token = node.Properties == null ? null : node.Properties["value"];
                LiteralValue parsed;
                if (TryReadLiteral(token, out parsed))
                {
                    value = parsed;
                }
            }
            else if (node.Operation == MultiplyOperation)
            {
                GraphConnection left;
                GraphConnection right;
                if (incoming.TryGetValue(PortKey(node.Id, "a"), out left) && incoming.TryGetValue(PortKey(node.Id, "b"), out right))
                {
                    LiteralValue? a = TryResolveLiteral(nodes[left.From.NodeId], nodes, incoming, visiting, diagnostics);
                    LiteralValue? b = TryResolveLiteral(nodes[right.From.NodeId], nodes, incoming, visiting, diagnostics);
                    if (a.HasValue && b.HasValue)
                    {
                        value = a.Value.Multiply(b.Value);
                    }
                }
            }

            visiting.Remove(node.Id);
            return value;
        }

        private static bool TryReadLiteral(JToken token, out LiteralValue value)
        {
            value = new LiteralValue(1, 1, 1, 1);
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                float number;
                if (TryReadFloat(token, out number))
                {
                    value = new LiteralValue(number, number, number, number);
                    return true;
                }
            }

            var array = token as JArray;
            if (array != null && (array.Count == 3 || array.Count == 4))
            {
                var values = new float[4] { 0, 0, 0, 1 };
                for (var i = 0; i < array.Count; i++)
                {
                    if (!TryReadFloat(array[i], out values[i]))
                    {
                        return false;
                    }
                }

                if (array.Count == 3)
                {
                    values[3] = 1;
                }

                value = new LiteralValue(values[0], values[1], values[2], values[3]);
                return true;
            }

            return false;
        }

        private static bool TryReadFloat(JToken token, out float value)
        {
            value = 0;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                return false;
            }

            double number;
            if (!double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || double.IsNaN(number) || double.IsInfinity(number))
            {
                return false;
            }

            value = (float)number;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ValidateShaderName(string shaderName, List<Diagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(shaderName) || shaderName.Length > 128 || shaderName.Contains("..") || shaderName.Contains("\\"))
            {
                AddError(diagnostics, "backend.shader-name.invalid", "options.shaderName", "Shader name must be a short safe path.");
                return BuiltinForwardShaderTemplate.ShaderName;
            }

            for (var i = 0; i < shaderName.Length; i++)
            {
                var c = shaderName[i];
                if (!(char.IsLetterOrDigit(c) || c == '/' || c == '_' || c == '-'))
                {
                    AddError(diagnostics, "backend.shader-name.invalid", "options.shaderName", "Shader name contains an unsafe character.");
                    return BuiltinForwardShaderTemplate.ShaderName;
                }
            }

            return shaderName;
        }

        private static string ValidateFallback(EmitterOptions options, List<Diagnostic> diagnostics)
        {
            if (!options.IncludeVrcFallback)
            {
                return null;
            }

            var fallback = options.VrcFallbackTag;
            if (fallback != "toonstandard" && fallback != "Toon" && fallback != "Unlit" && fallback != "Cutout" && fallback != "Transparent" && fallback != "Hidden")
            {
                AddError(diagnostics, "backend.fallback.invalid", "options.vrcFallbackTag", "Fallback tag is not in the supported allowlist.");
                return null;
            }

            return fallback;
        }

        private static string ParameterPropertyName(string parameterId)
        {
            return "_NXSG_P_" + SafeIdentifier(parameterId);
        }

        private static string SafeIdentifier(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                builder.Append(c == '-' ? '_' : c);
            }

            return builder.ToString();
        }

        private static bool IsSafeId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string SafeLabel(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "NXSG Parameter";
            }

            var builder = new StringBuilder(Math.Min(value.Length, 64));
            for (var i = 0; i < value.Length && builder.Length < 64; i++)
            {
                var c = value[i];
                builder.Append(c == '"' || c == '\\' || char.IsControl(c) ? '_' : c);
            }

            return builder.ToString();
        }

        private static string EscapeShaderString(string value)
        {
            return SafeLabel(value).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string DefaultLiteral(GraphParameter parameter)
        {
            LiteralValue value;
            if (TryReadLiteral(parameter.DefaultValue, out value))
            {
                return parameter.Type == GraphValueType.Float
                    ? value.X.ToString("R", CultureInfo.InvariantCulture)
                    : value.ToLiteral();
            }

            return parameter.Type == GraphValueType.Float ? "0" : "(1,1,1,1)";
        }

        private static string PropertyString(GraphNode node, string property)
        {
            return node.Properties == null ? null : (string)node.Properties[property];
        }

        private static string HlslValue(ScalarInfo value)
        {
            return value.ShaderExpression;
        }

        private static string HlslValue(ColorInfo value)
        {
            return value.ShaderExpression;
        }

        private static string PortKey(string nodeId, string portId)
        {
            return nodeId + "\u001f" + portId;
        }

        private static IEnumerable<string> InputPorts(string operation) { return NodeCatalog.Ports(operation, false); }

        private static string SafeDiagnosticId(string value)
        {
            return IsSafeId(value) ? value : "?";
        }

        private static float Clamp01(float value)
        {
            return value < 0 ? 0 : (value > 1 ? 1 : value);
        }

        private static bool HasErrors(List<Diagnostic> diagnostics)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddError(List<Diagnostic> diagnostics, string code, string path, string message)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, code, path, message));
        }

        private static void AddWarning(List<Diagnostic> diagnostics, string code, string path, string message)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, code, path, message));
        }

        private static EmissionResult Result(
            string source,
            List<Diagnostic> diagnostics,
            List<MaterialProperty> properties,
            List<SourceMapEntry> sourceMap)
        {
            return new EmissionResult(source, diagnostics.AsReadOnly(), properties.AsReadOnly(), sourceMap.AsReadOnly());
        }

        private sealed class TextureInfo
        {
            public TextureInfo(string nodeId, string resourceId, string resourceUri, string uvExpression)
            {
                NodeId = nodeId;
                ResourceId = resourceId;
                ResourceUri = resourceUri;
                UvExpression = uvExpression;
            }

            public string NodeId { get; private set; }
            public string ResourceId { get; private set; }
            public string ResourceUri { get; private set; }
            public string UvExpression { get; private set; }
        }

        private struct TraversalFrame
        {
            public TraversalFrame(string nodeId, int depth, bool exit)
            {
                NodeId = nodeId;
                Depth = depth;
                Exit = exit;
            }

            public string NodeId;
            public int Depth;
            public bool Exit;
        }

        private sealed class ColorInfo
        {
            public ColorInfo(string defaultLiteral, string shaderExpression, string parameterId = null, bool usesBaseColor = false)
            {
                DefaultLiteral = defaultLiteral;
                ShaderExpression = shaderExpression;
                ParameterId = parameterId;
                UsesBaseColor = usesBaseColor;
            }

            public string DefaultLiteral { get; private set; }
            public string ShaderExpression { get; private set; }
            public string ParameterId { get; private set; }
            public bool UsesBaseColor { get; private set; }
        }

        private sealed class ScalarInfo
        {
            public ScalarInfo(string shaderExpression, string propertyName, string parameterId = null, string defaultLiteral = null)
            {
                ShaderExpression = shaderExpression;
                PropertyName = propertyName;
                ParameterId = parameterId;
                DefaultLiteral = defaultLiteral ?? (parameterId == null ? shaderExpression : "0.5");
            }

            public string ShaderExpression { get; private set; }
            public string PropertyName { get; private set; }
            public string ParameterId { get; private set; }
            public string DefaultLiteral { get; private set; }
        }

        private struct LiteralValue
        {
            public LiteralValue(float x, float y, float z, float w)
            {
                X = x;
                Y = y;
                Z = z;
                W = w;
            }

            public float X;
            public float Y;
            public float Z;
            public float W;

            public LiteralValue Multiply(LiteralValue other)
            {
                return new LiteralValue(X * other.X, Y * other.Y, Z * other.Z, W * other.W);
            }

            // ShaderLab property defaults use tuples; executable HLSL needs a vector constructor.
            public string ToHlsl() { return "fixed4" + ToLiteral(); }

            public string ToLiteral()
            {
                return "(" + X.ToString("R", CultureInfo.InvariantCulture) + "," +
                    Y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    Z.ToString("R", CultureInfo.InvariantCulture) + "," +
                    W.ToString("R", CultureInfo.InvariantCulture) + ")";
            }
        }

        private sealed class ShaderBuilder
        {
            private readonly StringBuilder builder = new StringBuilder();
            public int Indent { get; set; }
            public int LineNumber { get; private set; }

            public void Line(string value)
            {
                for (var i = 0; i < Indent; i++)
                {
                    builder.Append("    ");
                }

                builder.AppendLine(value);
                LineNumber++;
            }

            public override string ToString()
            {
                return builder.ToString();
            }
        }
    }
}
