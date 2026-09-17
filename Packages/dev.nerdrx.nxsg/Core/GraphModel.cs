using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    public enum GraphValueType
    {
        Float,
        Vector2,
        Vector3,
        Vector4,
        Color,
        Bool,
        Texture2D,
        Surface
    }

    public enum GraphBindingKind
    {
        Constant,
        Material,
        AnimatedMaterial,
        Global,
        AudioLink
    }

    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ShaderGraph
    {
        public ShaderGraph()
        {
            Format = "nxsg";
            SchemaVersion = 1;
            Nodes = new List<GraphNode>();
            Connections = new List<GraphConnection>();
            Parameters = new List<GraphParameter>();
            Resources = new List<GraphResource>();
            Patterns = new List<GraphPattern>();
        }

        [JsonProperty("format", Required = Required.Always)]
        public string Format { get; set; }

        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; }

        [JsonProperty("graphId", Required = Required.Always)]
        public string GraphId { get; set; }

        [JsonProperty("nodes", Required = Required.Always)]
        public List<GraphNode> Nodes { get; set; }

        [JsonProperty("connections", Required = Required.Always)]
        public List<GraphConnection> Connections { get; set; }

        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public List<GraphParameter> Parameters { get; set; }

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public List<GraphResource> Resources { get; set; }

        [JsonProperty("patterns", NullValueHandling = NullValueHandling.Ignore)]
        public List<GraphPattern> Patterns { get; set; }

        [JsonProperty("layout", NullValueHandling = NullValueHandling.Ignore)]
        public GraphLayout Layout { get; set; }

        [JsonProperty("adapter", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Adapter { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public sealed class GraphNode
    {
        public GraphNode()
        {
            Version = 1;
            Properties = new JObject();
        }

        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("operation", Required = Required.Always)]
        public string Operation { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("properties", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Properties { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public sealed class GraphConnection
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("from", Required = Required.Always)]
        public GraphPortRef From { get; set; }

        [JsonProperty("to", Required = Required.Always)]
        public GraphPortRef To { get; set; }
    }

    public sealed class GraphPortRef
    {
        [JsonProperty("nodeId", Required = Required.Always)]
        public string NodeId { get; set; }

        [JsonProperty("portId", Required = Required.Always)]
        public string PortId { get; set; }
    }

    public sealed class GraphParameter
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        [JsonProperty("type", Required = Required.Always)]
        public GraphValueType Type { get; set; }

        [JsonProperty("binding", Required = Required.Always)]
        public GraphBindingKind Binding { get; set; }

        [JsonProperty("defaultValue", NullValueHandling = NullValueHandling.Include)]
        public JToken DefaultValue { get; set; }

        [JsonProperty("exposed", NullValueHandling = NullValueHandling.Ignore)]
        public bool Exposed { get; set; }
    }

    public sealed class GraphResource
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("kind", Required = Required.Always)]
        public string Kind { get; set; }

        [JsonProperty("uri", Required = Required.Always)]
        public string Uri { get; set; }

        [JsonProperty("contentHash", NullValueHandling = NullValueHandling.Ignore)]
        public string ContentHash { get; set; }
    }

    public sealed class GraphPattern
    {
        [JsonProperty("id", Required = Required.Always)]
        public string Id { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Calls { get; set; }

        [JsonProperty("nodes", NullValueHandling = NullValueHandling.Ignore)]
        public List<GraphNode> Nodes { get; set; }
    }

    public sealed class GraphLayout
    {
        [JsonProperty("nodes", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, GraphNodeLayout> Nodes { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public sealed class GraphNodeLayout
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; }
    }

    public sealed class Diagnostic
    {
        public Diagnostic(DiagnosticSeverity severity, string code, string path, string message)
        {
            Severity = severity;
            Code = code;
            Path = path ?? string.Empty;
            Message = message;
        }

        public DiagnosticSeverity Severity { get; private set; }
        public string Code { get; private set; }
        public string Path { get; private set; }
        public string Message { get; private set; }
        public long? Actual { get; set; }
        public long? Limit { get; set; }
    }

    public sealed class ValidationResult
    {
        public ValidationResult(IReadOnlyList<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; }

        public bool IsValid
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

                return true;
            }
        }
    }

    public sealed class GraphParseException : Exception
    {
        public GraphParseException(string message, IReadOnlyList<Diagnostic> diagnostics, Exception inner = null)
            : base(message, inner)
        {
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; }
    }
}
