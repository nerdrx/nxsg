using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Resolves the scalar/color polymorphism used by built-in math nodes.</summary>
    public static class GraphTypes
    {
        private static readonly HashSet<string> DynamicOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            "core.add", "core.subtract", "core.multiply", "core.divide", "core.minimum",
            "core.maximum", "core.mix", "core.oneMinus", "core.clamp", "core.absolute", "core.power", "core.sqrt",
            "core.sine", "core.cosine", "core.fraction", "core.floor", "core.ceil", "core.round"
        };

        public static bool IsDynamic(string operation)
        {
            return operation != null && DynamicOperations.Contains(operation);
        }

        // Returns node id -> inferred dynamic output type. Non-dynamic nodes are omitted.
        public static Dictionary<string, string> Infer(ShaderGraph graph)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (graph == null || graph.Nodes == null) return result;

            var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
            {
                if (node != null && !string.IsNullOrWhiteSpace(node.Id) && !nodes.ContainsKey(node.Id))
                    nodes.Add(node.Id, node);
            }
            var incoming = new Dictionary<string, List<GraphConnection>>(StringComparer.Ordinal);
            var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
            var demanded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes.Values)
            {
                if (IsDynamic(node.Operation)) { result[node.Id] = null; indegree[node.Id] = 0; }
            }

            var connections = graph.Connections ?? new List<GraphConnection>();
            foreach (var connection in connections)
            {
                if (connection == null || connection.From == null || connection.To == null) continue;
                if (!incoming.TryGetValue(connection.To.NodeId ?? string.Empty, out var list))
                    incoming[connection.To.NodeId ?? string.Empty] = list = new List<GraphConnection>();
                list.Add(connection);
                var source = nodes.ContainsKey(connection.From.NodeId ?? string.Empty) ? nodes[connection.From.NodeId] : null;
                var target = nodes.ContainsKey(connection.To.NodeId ?? string.Empty) ? nodes[connection.To.NodeId] : null;
                if (source != null && IsDynamic(source.Operation) && target != null)
                {
                    if (IsDynamic(target.Operation) && IsOperand(target.Operation, connection.To.PortId))
                    {
                        if (!dependents.TryGetValue(connection.From.NodeId, out var targets))
                            dependents[connection.From.NodeId] = targets = new List<string>();
                        targets.Add(connection.To.NodeId);
                        indegree[target.Id]++;
                    }
                    if (PortType(graph, target, connection.To.PortId, result) == "float")
                        demanded.Add(connection.From.NodeId);
                }
            }

            // Push scalar demand backwards through dynamic operands, then propagate values forward.
            var demandQueue = new Queue<string>(demanded);
            while (demandQueue.Count > 0)
            {
                var id = demandQueue.Dequeue();
                if (!incoming.TryGetValue(id, out var list)) continue;
                foreach (var connection in list)
                {
                    if (!IsOperand(nodes[id].Operation, connection.To.PortId) || !nodes.ContainsKey(connection.From.NodeId ?? string.Empty)) continue;
                    if (IsDynamic(nodes[connection.From.NodeId].Operation) && demanded.Add(connection.From.NodeId)) demandQueue.Enqueue(connection.From.NodeId);
                }
            }
            foreach (var id in demanded) result[id] = "float";

            var queue = new Queue<string>();
            foreach (var pair in indegree) if (pair.Value == 0) queue.Enqueue(pair.Key);
            var processed = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                processed.Add(id);
                var node = nodes[id];
                incoming.TryGetValue(id, out var list);
                var color = false; var numeric = false;
                if (list != null) foreach (var connection in list)
                {
                    if (!IsOperand(node.Operation, connection.To.PortId)) continue;
                    var source = nodes.ContainsKey(connection.From.NodeId ?? string.Empty) ? nodes[connection.From.NodeId] : null;
                    var type = SourceType(source, connection.From.PortId, result, graph);
                    color |= type == "color"; numeric |= type == "float";
                }
                result[id] = color ? "color" : (numeric || demanded.Contains(id) ? "float" : LegacyType(node));
                if (dependents.TryGetValue(id, out var targets))
                    foreach (var target in targets) if (indegree.ContainsKey(target) && --indegree[target] == 0) queue.Enqueue(target);
            }
            // Cycles have no topological answer. Resolve from direct operands and legacy fallback; validator rejects cycle.
            foreach (var pair in indegree) if (!processed.Contains(pair.Key))
            {
                var color = false;
                if (incoming.TryGetValue(pair.Key, out var list)) foreach (var connection in list)
                    if (IsOperand(nodes[pair.Key].Operation, connection.To.PortId) &&
                        SourceType(nodes.ContainsKey(connection.From.NodeId ?? string.Empty) ? nodes[connection.From.NodeId] : null,
                            connection.From.PortId, result, graph) == "color") color = true;
                result[pair.Key] = color ? "color" : (demanded.Contains(pair.Key) ? "float" : LegacyType(nodes[pair.Key]));
            }
            return result;
        }

        public static string PortType(ShaderGraph graph, GraphNode node, string port,
            Dictionary<string, string> inferred = null)
        {
            if (node == null || string.IsNullOrWhiteSpace(port)) return null;
            if (inferred == null) inferred = Infer(graph);
            if (IsDynamic(node.Operation) && IsDynamicPort(node.Operation, port))
                return inferred.TryGetValue(node.Id ?? string.Empty, out var type) ? type : LegacyType(node);
            if (node.Operation == "core.parameter" && port == "value")
            {
                var parameterId = node.Properties == null ? null : (string)node.Properties["parameterId"];
                foreach (var parameter in graph == null ? new List<GraphParameter>() : graph.Parameters ?? new List<GraphParameter>())
                    if (parameter != null && parameter.Id == parameterId) return parameter.Type.ToString().ToLowerInvariant();
            }
            return NodeCatalog.PortType(node, port);
        }

        public static bool Compatible(string from, string to)
        {
            return from != null && to != null && (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(from, "float", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(to, "color", StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(from, "color", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(to, "float", StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsDynamicPort(string operation, string port)
        {
            if (operation == "core.mix") return port == "a" || port == "b" || port == "value";
            if (operation == "core.oneMinus" || operation == "core.clamp") return port == "color";
            return port == "a" || port == "b" || port == "value";
        }

        private static bool IsOperand(string operation, string port)
        {
            if (operation == "core.oneMinus" || operation == "core.clamp") return port == "color";
            if (operation == "core.absolute" || operation == "core.sqrt" || operation == "core.sine" || operation == "core.cosine" || operation == "core.fraction" || operation == "core.floor" || operation == "core.ceil" || operation == "core.round") return port == "a";
            return port == "a" || port == "b";
        }

        private static string SourceType(GraphNode source, string port, Dictionary<string, string> inferred, ShaderGraph graph)
        {
            if (source == null) return null;
            if (IsDynamic(source.Operation) && inferred.TryGetValue(source.Id ?? string.Empty, out var type)) return type;
            return PortType(graph, source, port, inferred);
        }

        private static string LegacyType(GraphNode node)
        {
            if (node != null && (node.Operation == "core.absolute" || node.Operation == "core.power" || node.Operation == "core.sqrt" || node.Operation == "core.sine" || node.Operation == "core.cosine" || node.Operation == "core.fraction" || node.Operation == "core.floor" || node.Operation == "core.ceil" || node.Operation == "core.round")) return "float";
            if (node != null && node.Operation == "core.multiply")
            {
                var value = node.Properties == null ? null : ReadType(node.Properties["valueType"]);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "color";
        }

        private static string ReadType(JToken token)
        {
            var text = token == null || token.Type != JTokenType.String ? null : token.Value<string>();
            return string.IsNullOrWhiteSpace(text) ? null : text.ToLowerInvariant();
        }
    }
}
