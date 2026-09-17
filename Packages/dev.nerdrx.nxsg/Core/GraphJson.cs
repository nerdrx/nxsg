using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace NXSG.Core
{
    internal static class GraphLimits
    {
        internal const int MaxInputBytes = 4 * 1024 * 1024;
        internal const int MaxJsonDepth = 64;
        internal const int MaxNodes = 4096;
        internal const int MaxConnections = 16384;
        internal const int MaxResources = 1024;
        internal const int MaxPatternDepth = 32;
        internal const int MaxExpandedNodes = 32768;
        internal const int MaxGeneratedSourceBytes = 8 * 1024 * 1024;
        internal const int MaxVariantsPerPass = 256;
    }

    public static class GraphJson
    {
        public static ShaderGraph Parse(string json, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var byteCount = Encoding.UTF8.GetByteCount(json);
            if (byteCount > GraphLimits.MaxInputBytes)
            {
                throw LimitException("input.bytes", "Input JSON exceeds the byte limit.", byteCount, GraphLimits.MaxInputBytes);
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (var stringReader = new StringReader(json))
                using (var reader = new JsonTextReader(stringReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    reader.MaxDepth = GraphLimits.MaxJsonDepth;
                    var root = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read())
                    {
                        throw new InvalidDataException("Trailing JSON content is not allowed.");
                    }
                    cancellationToken.ThrowIfCancellationRequested();

                    var serializer = CreateSerializer();
                    var graph = root.ToObject<ShaderGraph>(serializer);
                    if (graph == null)
                    {
                        throw new InvalidDataException("The JSON root did not contain a graph object.");
                    }

                    return graph;
                }
            }
            catch (GraphParseException)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidDataException)
            {
                var diagnostic = new Diagnostic(
                    DiagnosticSeverity.Error,
                    "json.parse",
                    "$",
                    exception.Message);
                throw new GraphParseException("NXSG JSON could not be parsed.",
                    new[] { diagnostic }, exception);
            }
        }

        public static string Serialize(ShaderGraph graph, bool indented = false)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            var document = PrepareDocument(graph);
            return indented ? document.ToString(Formatting.Indented) : Canonicalize(document);
        }

        public static string ComputeSemanticHash(ShaderGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            var semantic = PrepareDocument(graph);
            semantic.Remove("layout");

            var canonical = Canonicalize(semantic);
            var bytes = Encoding.UTF8.GetBytes(canonical);
            using (var sha = SHA256.Create())
            {
                return ToLowerHex(sha.ComputeHash(bytes));
            }
        }

        internal static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.Create(CreateSettings());
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include
            };
            settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter
            {
                NamingStrategy = new CamelCaseNamingStrategy(),
                AllowIntegerValues = false
            });
            return settings;
        }

        private static JObject PrepareDocument(ShaderGraph graph)
        {
            var root = JObject.FromObject(graph, CreateSerializer());
            SortKnownIdentityArrays(root);
            return SortObjectProperties(root);
        }

        private static void SortKnownIdentityArrays(JObject root)
        {
            if (root == null)
            {
                return;
            }
            foreach (var property in root.Properties().ToList())
            {
                if (property.Value is JArray array && IsKnownIdentityArray(property.Name))
                {
                    property.Value = SortIdentityArray(array);
                    if (property.Name == "patterns")
                    {
                        foreach (var pattern in property.Value.OfType<JObject>())
                        {
                            if (pattern["nodes"] is JArray patternNodes)
                            {
                                pattern["nodes"] = SortIdentityArray(patternNodes);
                            }
                        }
                    }
                }
            }
        }

        private static JArray SortIdentityArray(JArray array)
        {
            var objectItems = array.OfType<JObject>().ToList();
            if (objectItems.Count != array.Count || !objectItems.All(item => item["id"] != null))
            {
                return array;
            }
            return new JArray(objectItems.OrderBy(item => (string)item["id"], StringComparer.Ordinal));
        }

        private static bool IsKnownIdentityArray(string propertyName)
        {
            return propertyName == "nodes" || propertyName == "connections" || propertyName == "parameters" ||
                propertyName == "resources" || propertyName == "patterns";
        }

        private static JObject SortObjectProperties(JObject source)
        {
            var result = new JObject();
            foreach (var property in source.Properties().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (property.Value is JObject childObject)
                {
                    result.Add(property.Name, SortObjectProperties(childObject));
                }
                else if (property.Value is JArray childArray)
                {
                    var sortedArray = new JArray();
                    foreach (var child in childArray)
                    {
                        sortedArray.Add(child is JObject childItem ? SortObjectProperties(childItem) : child);
                    }
                    result.Add(property.Name, sortedArray);
                }
                else
                {
                    result.Add(property.Name, property.Value);
                }
            }
            return result;
        }

        private static string Canonicalize(JToken token)
        {
            var builder = new StringBuilder();
            WriteCanonical(token, builder);
            return builder.ToString();
        }

        private static void WriteCanonical(JToken token, StringBuilder builder)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                builder.Append("null");
                return;
            }

            if (token is JObject objectToken)
            {
                builder.Append('{');
                var properties = objectToken.Properties().OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
                for (var i = 0; i < properties.Count; i++)
                {
                    if (i != 0) builder.Append(',');
                    builder.Append(JsonConvert.ToString(properties[i].Name));
                    builder.Append(':');
                    WriteCanonical(properties[i].Value, builder);
                }
                builder.Append('}');
                return;
            }

            if (token is JArray arrayToken)
            {
                builder.Append('[');
                for (var i = 0; i < arrayToken.Count; i++)
                {
                    if (i != 0) builder.Append(',');
                    WriteCanonical(arrayToken[i], builder);
                }
                builder.Append(']');
                return;
            }

            var value = ((JValue)token).Value;
            switch (token.Type)
            {
                case JTokenType.Boolean:
                    builder.Append((bool)value ? "true" : "false");
                    break;
                case JTokenType.Integer:
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case JTokenType.Float:
                    builder.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JTokenType.String:
                    builder.Append(JsonConvert.ToString((string)value));
                    break;
                default:
                    builder.Append(JsonConvert.SerializeObject(value, Formatting.None, CreateSettings()));
                    break;
            }
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static GraphParseException LimitException(string code, string message, int actual, int limit)
        {
            var diagnostic = new Diagnostic(DiagnosticSeverity.Error, code, "$", message)
            {
                Actual = actual,
                Limit = limit
            };
            return new GraphParseException(message, new[] { diagnostic });
        }
    }
}
