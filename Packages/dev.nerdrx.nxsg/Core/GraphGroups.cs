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
            JToken rawValue = null;
            var hasGroups = graph.Layout?.ExtensionData != null && graph.Layout.ExtensionData.TryGetValue("groups", out rawValue);
            if (hasGroups && !(rawValue is JArray)) throw new InvalidOperationException("Group data is not an array.");

            var rawGroups = rawValue as JArray ?? new JArray();
            var rewritten = new JArray();
            var visibleCount = 0;
            foreach (var token in rawGroups)
            {
                var existingGroup = token as JObject;
                var recognized = existingGroup != null && (existingGroup["name"] == null || existingGroup["name"].Type == JTokenType.String) && visibleCount < 1024;
                if (recognized) visibleCount++;
                if (!recognized || !(existingGroup["members"] is JArray groupMembers))
                {
                    rewritten.Add(token.DeepClone());
                    continue;
                }

                var hasSelectedMember = groupMembers.Any(member => member.Type == JTokenType.String && members.Contains((string)member));
                if (!hasSelectedMember)
                {
                    rewritten.Add(token.DeepClone());
                    continue;
                }

                var remaining = new JArray(groupMembers.Where(member => member.Type != JTokenType.String || !members.Contains((string)member)).Select(member => member.DeepClone()));
                if (remaining.Count == 0) continue;
                var copy = (JObject)existingGroup.DeepClone();
                copy["members"] = remaining;
                rewritten.Add(copy);
            }
            if (rewritten.Count >= 1024) throw new InvalidOperationException("Too many groups.");
            var group = new JObject { ["id"] = Guid.NewGuid().ToString("N"), ["name"] = string.IsNullOrWhiteSpace(name) ? "Pattern" : name.Substring(0, Math.Min(name.Length, 80)),
                ["collapsed"] = true, ["members"] = new JArray(members.OrderBy(id => id, StringComparer.Ordinal)) };
            rewritten.Add(group);

            if (graph.Layout == null) graph.Layout = new GraphLayout();
            if (graph.Layout.ExtensionData == null) graph.Layout.ExtensionData = new Dictionary<string, JToken>();
            graph.Layout.ExtensionData["groups"] = rewritten;
            return group;
        }

        public static void Remove(ShaderGraph graph, JObject group)
        {
            if (graph?.Layout?.ExtensionData == null || group == null || !graph.Layout.ExtensionData.TryGetValue("groups", out var raw) || !(raw is JArray groups)) return;
            if (!All(graph).Any(candidate => ReferenceEquals(candidate, group))) return;
            graph.Layout.ExtensionData["groups"] = new JArray(groups.Where(token => !ReferenceEquals(token, group)).Select(token => token.DeepClone()));
        }
    }
}
