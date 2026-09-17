using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Bounded, file-free copy and paste for graph nodes.</summary>
    public static class GraphClipboard
    {
        private const int MaxNodes = 1024;
        private const int MaxConnections = 4096;
        private const int MaxBytes = 1024 * 1024;

        private static readonly HashSet<string> ParameterProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "parameterId", "thresholdParameterId", "softnessParameterId", "shadowStrengthParameterId"
        };

        public sealed class PasteResult
        {
            public PasteResult(ShaderGraph graph, IReadOnlyList<string> nodeIds)
            {
                Graph = graph;
                NodeIds = nodeIds;
            }

            public ShaderGraph Graph { get; private set; }
            public IReadOnlyList<string> NodeIds { get; private set; }
        }

        public static string Copy(ShaderGraph graph, IEnumerable<string> ids)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            if (graph.Nodes == null) throw Invalid("Graph has no node collection.");
            var selected = new HashSet<string>(ids.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
            if (selected.Count == 0 || selected.Count > MaxNodes) throw Invalid("Clipboard node count exceeds its limit or is empty.");

            var sourceNodes = graph.Nodes.Where(node => node != null && selected.Contains(node.Id)).ToList();
            if (sourceNodes.Count != selected.Count || sourceNodes.Any(node => !SafeNode(node))) throw Invalid("Clipboard contains an unsupported or invalid node.");
            var snippet = new ShaderGraph { GraphId = graph.GraphId, Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() } };
            var parameterIds = new HashSet<string>(StringComparer.Ordinal);
            var resourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in sourceNodes)
            {
                snippet.Nodes.Add(Clone(node));
                AddReferences(node, parameterIds, resourceIds);
                GraphNodeLayout layout;
                if (graph.Layout != null && graph.Layout.Nodes != null && graph.Layout.Nodes.TryGetValue(node.Id, out layout))
                {
                    if (layout == null || !Finite(layout.X) || !Finite(layout.Y)) throw Invalid("Node layout is not finite.");
                    snippet.Layout.Nodes[node.Id] = Clone(layout);
                }
                else snippet.Layout.Nodes[node.Id] = new GraphNodeLayout { X = graph.Nodes.IndexOf(node) * 215, Y = 80 };
            }

            if (graph.Connections != null)
            {
                foreach (var connection in graph.Connections)
                    if (connection != null && connection.From != null && connection.To != null &&
                        selected.Contains(connection.From.NodeId) && selected.Contains(connection.To.NodeId))
                        snippet.Connections.Add(Clone(connection));
            }
            if (snippet.Connections.Count > MaxConnections) throw Invalid("Clipboard connection count exceeds its limit.");
            if (graph.Parameters != null)
                snippet.Parameters = graph.Parameters.Where(p => p != null && parameterIds.Contains(p.Id)).Select(Clone).ToList();
            if (graph.Resources != null)
                snippet.Resources = graph.Resources.Where(r => r != null && resourceIds.Contains(r.Id)).Select(Clone).ToList();
            if (snippet.Parameters.Count > GraphLimits.MaxNodes || snippet.Resources.Count > GraphLimits.MaxResources ||
                parameterIds.Any(id => !snippet.Parameters.Any(p => p.Id == id)) || resourceIds.Any(id => !snippet.Resources.Any(r => r.Id == id)))
                throw Invalid("Clipboard contains unresolved references or exceeds its limits.");
            snippet.Adapter = CloneTextures(graph.Adapter, resourceIds);
            try
            {
                var text = GraphJson.Serialize(snippet);
                if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw Invalid("Clipboard exceeds the 1 MiB limit.");
                return text;
            }
            catch (Exception exception) { throw Invalid("Clipboard serialization failed.", exception); }
        }

        public static PasteResult Paste(ShaderGraph target, string text, double offsetX, double offsetY)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (Encoding.UTF8.GetByteCount(text) > MaxBytes || double.IsNaN(offsetX) || double.IsInfinity(offsetX) || double.IsNaN(offsetY) || double.IsInfinity(offsetY)) throw Invalid("Clipboard input exceeds its limit.");
            ShaderGraph snippet;
            try { snippet = GraphJson.Parse(text); }
            catch (Exception exception) { throw Invalid("Clipboard JSON is invalid.", exception); }
            if (snippet == null || snippet.Format != "nxsg" || snippet.SchemaVersion != 1 || snippet.Nodes == null || snippet.Nodes.Count == 0 || snippet.Nodes.Count > MaxNodes ||
                snippet.Connections == null || snippet.Connections.Count > MaxConnections || snippet.Nodes.Any(node => !SafeNode(node)) ||
                !UniqueIds(snippet.Nodes.Select(node => node.Id)) || !UniqueIds((snippet.Parameters ?? new List<GraphParameter>()).Where(p => p != null).Select(p => p.Id)) ||
                !UniqueIds((snippet.Resources ?? new List<GraphResource>()).Where(r => r != null).Select(r => r.Id))) throw Invalid("Clipboard graph is unsupported or exceeds its limits.");

            var result = Clone(target);
            EnsureCollections(result);
            if (result.Adapter == null && snippet.Adapter != null) result.Adapter = new JObject();
            if (result.Nodes.Count + snippet.Nodes.Count > GraphLimits.MaxNodes || result.Connections.Count + snippet.Connections.Count > GraphLimits.MaxConnections || result.Resources.Count + (snippet.Resources == null ? 0 : snippet.Resources.Count) > GraphLimits.MaxResources)
                throw Invalid("Pasted graph exceeds graph limits.");
            var used = new HashSet<string>(result.Nodes.Where(n => n != null).Select(n => n.Id), StringComparer.Ordinal);
            var nodeMap = NewMap(snippet.Nodes.Select(n => n.Id), used);
            var parameterMap = NewMap((snippet.Parameters ?? new List<GraphParameter>()).Where(p => p != null).Select(p => p.Id),
                new HashSet<string>(result.Parameters.Where(p => p != null).Select(p => p.Id), StringComparer.Ordinal));
            var resourceMap = NewMap((snippet.Resources ?? new List<GraphResource>()).Where(r => r != null).Select(r => r.Id),
                new HashSet<string>(result.Resources.Where(r => r != null).Select(r => r.Id), StringComparer.Ordinal));

            foreach (var parameter in snippet.Parameters ?? new List<GraphParameter>()) { if (parameter == null || !parameterMap.ContainsKey(parameter.Id)) throw Invalid("Clipboard parameter is invalid."); var copy = Clone(parameter); copy.Id = parameterMap[parameter.Id]; result.Parameters.Add(copy); }
            foreach (var resource in snippet.Resources ?? new List<GraphResource>()) { if (resource == null || !resourceMap.ContainsKey(resource.Id)) throw Invalid("Clipboard resource is invalid."); var copy = Clone(resource); copy.Id = resourceMap[resource.Id]; result.Resources.Add(copy); }
            var newIds = new List<string>();
            var layout = result.Layout ?? (result.Layout = new GraphLayout());
            if (layout.Nodes == null) layout.Nodes = new Dictionary<string, GraphNodeLayout>();
            foreach (var node in snippet.Nodes)
            {
                if (!ReferencesDeclared(node, parameterMap, resourceMap)) throw Invalid("Clipboard node reference is unresolved.");
                var copy = Clone(node); copy.Id = nodeMap[node.Id]; RemapProperties(copy.Properties, parameterMap, resourceMap);
                result.Nodes.Add(copy); newIds.Add(copy.Id);
                GraphNodeLayout original;
                if (snippet.Layout != null && snippet.Layout.Nodes != null && snippet.Layout.Nodes.TryGetValue(node.Id, out original))
                {
                    if (original == null || double.IsNaN(original.X) || double.IsInfinity(original.X) || double.IsNaN(original.Y) || double.IsInfinity(original.Y)) throw Invalid("Clipboard layout is not finite.");
                    if (!Finite(original.X + offsetX) || !Finite(original.Y + offsetY)) throw Invalid("Pasted layout exceeds numeric limits.");
                    layout.Nodes[copy.Id] = new GraphNodeLayout { X = original.X + offsetX, Y = original.Y + offsetY, ExtensionData = original.ExtensionData == null ? null : new Dictionary<string, JToken>(original.ExtensionData) };
                }
                else layout.Nodes[copy.Id] = new GraphNodeLayout { X = offsetX, Y = offsetY };
            }
            var edgeIds = new HashSet<string>(result.Connections.Where(e => e != null).Select(e => e.Id), StringComparer.Ordinal);
            foreach (var edge in snippet.Connections)
            {
                if (edge == null || edge.From == null || edge.To == null || !nodeMap.ContainsKey(edge.From.NodeId) || !nodeMap.ContainsKey(edge.To.NodeId)) throw Invalid("Clipboard connection is invalid.");
                var copy = Clone(edge); copy.Id = NewId(edgeIds); copy.From.NodeId = nodeMap[edge.From.NodeId]; copy.To.NodeId = nodeMap[edge.To.NodeId]; result.Connections.Add(copy);
            }
            RemapTextures(result.Adapter, snippet.Adapter, resourceMap);
            return new PasteResult(result, newIds);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool SafeNode(GraphNode node) { return node != null && !string.IsNullOrWhiteSpace(node.Id) && node.Version == 1 && NodeCatalog.IsKnown(node.Operation) && node.Properties != null; }
        private static void AddReferences(GraphNode node, HashSet<string> parameters, HashSet<string> resources)
        {
            if (node.Properties == null) return;
            foreach (var property in node.Properties.Properties())
            {
                if (ParameterProperties.Contains(property.Name) && property.Value.Type == JTokenType.String) parameters.Add((string)property.Value);
                else if (property.Name == "resourceId" && property.Value.Type == JTokenType.String) resources.Add((string)property.Value);
            }
        }
        private static void RemapProperties(JObject properties, IDictionary<string, string> parameters, IDictionary<string, string> resources)
        {
            if (properties == null) return;
            foreach (var property in properties.Properties())
            {
                if (ParameterProperties.Contains(property.Name) && property.Value.Type == JTokenType.String && parameters.ContainsKey((string)property.Value)) property.Value = parameters[(string)property.Value];
                else if (property.Name == "resourceId" && property.Value.Type == JTokenType.String && resources.ContainsKey((string)property.Value)) property.Value = resources[(string)property.Value];
            }
        }
        private static bool ReferencesDeclared(GraphNode node, IDictionary<string, string> parameters, IDictionary<string, string> resources)
        {
            if (node.Properties == null) return true;
            foreach (var property in node.Properties.Properties())
            {
                if (ParameterProperties.Contains(property.Name) && property.Value.Type == JTokenType.String && !parameters.ContainsKey((string)property.Value)) return false;
                if (property.Name == "resourceId" && property.Value.Type == JTokenType.String && !resources.ContainsKey((string)property.Value)) return false;
            }
            return true;
        }
        private static Dictionary<string, string> NewMap(IEnumerable<string> source, HashSet<string> used) { var map = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var id in source) { if (string.IsNullOrWhiteSpace(id) || map.ContainsKey(id)) throw Invalid("Clipboard IDs must be unique and nonempty."); map[id] = NewId(used); } return map; }
        private static bool UniqueIds(IEnumerable<string> ids) { var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var id in ids) if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) return false; return true; }
        private static string NewId(HashSet<string> used) { string id; do { id = Guid.NewGuid().ToString("N"); } while (!used.Add(id)); return id; }
        private static GraphParseException Invalid(string message, Exception inner = null) { return new GraphParseException(message, new[] { new Diagnostic(DiagnosticSeverity.Error, "clipboard.invalid", "$", message) }, inner); }
        private static void EnsureCollections(ShaderGraph graph) { if (graph.Nodes == null) graph.Nodes = new List<GraphNode>(); if (graph.Connections == null) graph.Connections = new List<GraphConnection>(); if (graph.Parameters == null) graph.Parameters = new List<GraphParameter>(); if (graph.Resources == null) graph.Resources = new List<GraphResource>(); }
        private static ShaderGraph Clone(ShaderGraph value) { return GraphJson.Parse(GraphJson.Serialize(value)); }
        private static GraphNode Clone(GraphNode value) { return JObject.FromObject(value, GraphJson.CreateSerializer()).ToObject<GraphNode>(GraphJson.CreateSerializer()); }
        private static GraphNodeLayout Clone(GraphNodeLayout value) { return JObject.FromObject(value, GraphJson.CreateSerializer()).ToObject<GraphNodeLayout>(GraphJson.CreateSerializer()); }
        private static GraphConnection Clone(GraphConnection value) { return JObject.FromObject(value, GraphJson.CreateSerializer()).ToObject<GraphConnection>(GraphJson.CreateSerializer()); }
        private static GraphParameter Clone(GraphParameter value) { return JObject.FromObject(value, GraphJson.CreateSerializer()).ToObject<GraphParameter>(GraphJson.CreateSerializer()); }
        private static GraphResource Clone(GraphResource value) { return JObject.FromObject(value, GraphJson.CreateSerializer()).ToObject<GraphResource>(GraphJson.CreateSerializer()); }
        private static JObject CloneTextures(JObject adapter, HashSet<string> resources) { var textures = adapter == null ? null : adapter["textures"] as JObject; if (textures == null) return null; var copy = (JObject)adapter.DeepClone(); copy["textures"] = new JObject(); foreach (var p in textures.Properties()) if (resources.Contains(p.Name)) ((JObject)copy["textures"])[p.Name] = p.Value.DeepClone(); return ((JObject)copy["textures"]).HasValues ? copy : null; }
        private static void RemapTextures(JObject adapter, JObject snippet, IDictionary<string, string> resources) { var source = snippet == null ? null : snippet["textures"] as JObject; if (source == null || adapter == null) return; var target = adapter["textures"] as JObject; if (target == null) { target = new JObject(); adapter["textures"] = target; } foreach (var p in source.Properties()) if (resources.ContainsKey(p.Name)) target[resources[p.Name]] = p.Value.DeepClone(); }
    }
}
