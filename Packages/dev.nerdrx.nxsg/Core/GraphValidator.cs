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
            ValidateConnections(connections, nodeById, parameters, diagnostics, cancellationToken);
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
            else if (node.Operation == "core.texture2D")
            {
                var resourceId = (string)node.Properties["resourceId"];
                if (string.IsNullOrWhiteSpace(resourceId) || resources.All(item => item == null || item.Id != resourceId))
                {
                    Add(diagnostics, DiagnosticSeverity.Error, "resource.missing", path + ".properties.resourceId",
                        "Texture node must reference a declared resource.");
                }
            }
        }

        private static void ValidateCatalogProperties(GraphNode node, string path, List<Diagnostic> diagnostics)
        {
            string[] numeric = null, vectors = null;
            switch (node.Operation)
            {
                case "core.value": numeric = new[] { "value" }; break;
                case "core.time": numeric = new[] { "speed", "offset" }; break;
                case "core.noise": numeric = new[] { "scale", "speed" }; break;
                case "core.mix": numeric = new[] { "factor" }; break;
                case "core.emission": numeric = new[] { "strength" }; break;
                case "core.uvTransform": vectors = new[] { "tiling", "offset" }; break;
                case "core.uvScroll": vectors = new[] { "speed" }; break;
                default: return;
            }
            if (numeric != null) foreach (var name in numeric) CheckNumber(node.Properties[name], path + ".properties." + name, diagnostics);
            if (vectors != null) foreach (var name in vectors) CheckVector2(node.Properties[name], path + ".properties." + name, diagnostics);
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

        private static void ValidateConnections(List<GraphConnection> connections,
            Dictionary<string, GraphNode> nodes, List<GraphParameter> parameters,
            List<Diagnostic> diagnostics, CancellationToken cancellationToken)
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
                var fromType = GetPortType(fromNode, connection.From.PortId, false, parameters);
                var toType = GetPortType(toNode, connection.To.PortId, true, parameters);
                if (fromType.HasValue && toType.HasValue && !IsCompatible(fromType.Value, toType.Value))
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
                    !outgoing.ContainsKey(connection.From.NodeId) || !indegree.ContainsKey(connection.To.NodeId))
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
                        (port == "color" ? GraphValueType.Color : (GraphValueType?)null);
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
