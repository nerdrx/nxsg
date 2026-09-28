using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    /// <summary>Reusable groups keep ordinary nodes and edges; collapsing is layout only.</summary>
    public static class GraphGroups
    {
        public static IEnumerable<JObject> All(ShaderGraph graph)
        {
            JToken value;
            if (graph?.Layout?.ExtensionData == null || !graph.Layout.ExtensionData.TryGetValue("groups", out value) || !(value is JArray groups))
                return Enumerable.Empty<JObject>();
            return groups.OfType<JObject>().Where(g => g["name"] == null || g["name"].Type == JTokenType.String).Take(1024);
        }

        public static string[] Members(ShaderGraph graph, JObject group)
        {
            var ids = new HashSet<string>(graph.Nodes.Select(n => n.Id));
            return (group["members"] as JArray ?? new JArray()).Where(t => t.Type == JTokenType.String).Values<string>().Where(id => id != null && ids.Contains(id)).Distinct().ToArray();
        }

        public static JObject Add(ShaderGraph graph, IEnumerable<string> ids, string name)
        {
            var members = new HashSet<string>(ids);
            members.IntersectWith(graph.Nodes.Select(n => n.Id));
            if (members.Count == 0) throw new InvalidOperationException("Select nodes to group.");
            if (graph.Layout == null) graph.Layout = new GraphLayout();
            if (graph.Layout.ExtensionData == null) graph.Layout.ExtensionData = new Dictionary<string, JToken>();
            // Build the post-edit groups on copies so a rejected edit cannot mutate the graph.
            var groups = new JArray(All(graph).Select(g =>
            {
                var remaining = Members(graph, g).Where(id => !members.Contains(id)).ToArray();
                if (remaining.Length == 0) return null;
                var copy = (JObject)g.DeepClone();
                copy["members"] = new JArray(remaining);
                return copy;
            }).Where(g => g != null));
            if (groups.Count >= 1024) throw new InvalidOperationException("Too many groups.");
            var group = new JObject { ["id"] = Guid.NewGuid().ToString("N"), ["name"] = string.IsNullOrWhiteSpace(name) ? "Pattern" : name.Substring(0, Math.Min(name.Length, 80)),
                ["collapsed"] = true, ["members"] = new JArray(members.OrderBy(id => id, StringComparer.Ordinal)) };
            groups.Add(group); graph.Layout.ExtensionData["groups"] = groups;
            return group;
        }

        public static void Remove(ShaderGraph graph, JObject group)
        {
            if (graph?.Layout?.ExtensionData == null) return;
            graph.Layout.ExtensionData["groups"] = new JArray(All(graph).Where(g => g != group).Select(g => g.DeepClone()));
        }
    }
}
