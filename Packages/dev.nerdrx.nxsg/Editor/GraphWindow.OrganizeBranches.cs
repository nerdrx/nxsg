using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        // Infer ownership from the inputs a branch actually feeds, not the node's type.
        // These frames are layout metadata only. Explicitly renamed frames become manual.
        List<JObject> OrganizeBranchGroups(bool selectedOnly)
        {
            var existing = GraphGroups.All(graph).ToList();
            if (selectedOnly) return existing;
            if (graph.Layout?.ExtensionData != null && graph.Layout.ExtensionData.TryGetValue("groups", out var rawGroups) && !(rawGroups is JArray))
                return existing;
            if (graph.Nodes.Count > 2048 || graph.Connections.Count > 8192) return existing;
            var manual = existing.Where(g => !IsInferredFrame(g)).ToList();
            var reserved = new HashSet<string>(manual.SelectMany(g => GraphGroups.Members(graph, g)));
            var byId = graph.Nodes.ToDictionary(n => n.Id);
            var incoming = graph.Connections.Where(e => byId.ContainsKey(e.From.NodeId) && byId.ContainsKey(e.To.NodeId))
                .ToLookup(e => e.To.NodeId, e => e.From.NodeId);
            var outputs = graph.Nodes.Where(n => n.Operation == "core.output").Select(n => n.Id).ToArray();
            if (outputs.Length == 0) return manual;
            var active = new HashSet<string>();
            var pending = new Stack<string>(outputs);
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (!active.Add(id)) continue;
                foreach (var parent in incoming[id]) pending.Push(parent);
            }
            var surfaces = new HashSet<string>(graph.Nodes.Where(n => active.Contains(n.Id) &&
                (n.Operation == "core.output" || Ports(n.Operation, true).Contains("surface"))).Select(n => n.Id));
            var seeds = graph.Connections.Where(e => surfaces.Contains(e.To.NodeId) &&
                active.Contains(e.From.NodeId) && !surfaces.Contains(e.From.NodeId))
                .OrderBy(e => byId[e.To.NodeId].Operation == "core.surfaceParticles" ? 1 : 0)
                .ThenBy(e => e.To.NodeId, StringComparer.Ordinal).ThenBy(e => e.To.PortId, StringComparer.Ordinal).ToArray();
            // Bounded inference for unusually large imported graphs; ordinary layout remains available.
            if (seeds.Length > 128) return existing;
            var labels = new Dictionary<string, string>();
            var ownership = new Dictionary<string, HashSet<string>>();
            var remainingVisits = 32768;
            foreach (var seed in seeds)
            {
                var surface = byId[seed.To.NodeId];
                var bucket = OrganizeInputBucket(surface, seed.To.PortId);
                var key = surface.Id + "/" + bucket;
                labels[key] = (surface.Operation == "core.surfaceParticles" ? "Particles" : Title(surface.Operation)) + " · " + bucket;
                var seen = new HashSet<string>();
                pending.Push(seed.From.NodeId);
                while (pending.Count > 0)
                {
                    if (--remainingVisits < 0) return existing;
                    var id = pending.Pop();
                    if (surfaces.Contains(id) || !seen.Add(id)) continue;
                    if (!reserved.Contains(id))
                    {
                        if (!ownership.TryGetValue(id, out var owners)) ownership[id] = owners = new HashSet<string>();
                        owners.Add(key);
                    }
                    foreach (var parent in incoming[id]) pending.Push(parent);
                }
            }
            var branches = ownership.Where(p => p.Value.Count == 1).GroupBy(p => p.Value.First())
                .ToDictionary(g => g.Key, g => g.Select(p => p.Key).OrderBy(id => id, StringComparer.Ordinal).ToArray());
            // Small single-branch graphs don't need extra containers.
            if (branches.Count < 2) return manual;
            var result = new List<JObject>(manual);
            var usedIds = new HashSet<string>(manual.Where(g => g["id"]?.Type == JTokenType.String).Select(g => (string)g["id"]));
            foreach (var branch in branches.OrderBy(p => labels[p.Key], StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal))
                AddFrame(branch.Key, labels[branch.Key], "branch", branch.Value);
            AddFrame("shared", "Shared controls", "shared", ownership.Where(p => p.Value.Count > 1).Select(p => p.Key));
            AddFrame("output", "Material output", "output", surfaces.Where(id => !reserved.Contains(id)));
            AddFrame("unused", "Unused branches", "unused", graph.Nodes.Where(n => !active.Contains(n.Id) && !reserved.Contains(n.Id)).Select(n => n.Id));
            return result.Count <= 1024 ? result : existing;

            void AddFrame(string key, string label, string role, IEnumerable<string> members)
            {
                var ids = members.OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (ids.Length == 0) return;
                var previous = existing.FirstOrDefault(g => IsInferredFrame(g) && (string)g["organizeKey"] == key);
                var frame = previous == null ? new JObject { ["id"] = "organize-" + key } : (JObject)previous.DeepClone();
                var baseId = frame["id"]?.Type == JTokenType.String ? (string)frame["id"] : "organize-" + key;
                var id = baseId;
                for (var suffix = 2; !usedIds.Add(id); suffix++) id = baseId + "-" + suffix;
                frame["id"] = id;
                frame["name"] = label; frame["members"] = new JArray(ids);
                frame["frame"] = true; frame["collapsed"] = false;
                frame["organizeKey"] = key; frame["organizeRole"] = role;
                frame["nxsgOrganizeVersion"] = 1;
                result.Add(frame);
            }
        }

        static string OrganizeInputBucket(GraphNode surface, string port)
        {
            if (surface.Operation == "core.surfaceParticles")
                switch (port)
                {
                    case "albedo": case "emission": case "opacity": return "Appearance";
                    case "density": case "emissionRate": case "lifetime": case "time": return "Emission timing";
                    case "size": case "edgeSharpness": return "Size & edges";
                    case "speed": case "gravity": case "spread": return "Motion";
                    case "mask": return "Emitter mask";
                }
            var label = PortLabel(port);
            return label.Length == 0 ? port : char.ToUpperInvariant(label[0]) + label.Substring(1);
        }

        Dictionary<string, Vector2> ArrangeOrganizeBlocks(Dictionary<string, Rect> blocks,
            Dictionary<string, string> roles, IEnumerable<(string from, string to)> edges)
        {
            if (!roles.Values.Contains("branch")) return GraphAutoLayout.Arrange(blocks, edges);
            const float gap = 72;
            var origin = OrganizeUnion(blocks.Values).position;
            var result = new Dictionary<string, Vector2>();
            var shared = blocks.Keys.Where(k => roles.TryGetValue(k, out var role) && role == "shared").OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var output = blocks.Keys.Where(k => roles.TryGetValue(k, out var role) && role == "output").OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var unused = blocks.Keys.Where(k => roles.TryGetValue(k, out var role) && role == "unused").OrderBy(k => k, StringComparer.Ordinal).ToArray();
            var main = blocks.Keys.Except(shared).Except(output).Except(unused)
                .OrderByDescending(k => blocks[k].width).ThenByDescending(k => blocks[k].height).ThenBy(k => k, StringComparer.Ordinal).ToArray();
            if (main.Length == 0) return GraphAutoLayout.Arrange(blocks, edges);
            var sharedWidth = shared.Length == 0 ? 0 : shared.Max(k => blocks[k].width) + gap;
            var rightColumn = output.Concat(unused).ToArray();
            var outputWidth = rightColumn.Length == 0 ? 0 : rightColumn.Max(k => blocks[k].width) + gap;
            var minimum = main.Max(k => blocks[k].width);
            var area = main.Sum(k => (blocks[k].width + gap) * (blocks[k].height + gap));
            var bestScore = float.PositiveInfinity;
            var bestSize = Vector2.zero;
            Dictionary<string, Vector2> best = null;
            // A few bounded shelf widths trade empty space against a very tall input list.
            foreach (var width in new[] { minimum, Mathf.Max(minimum, Mathf.Sqrt(area)), Mathf.Max(minimum, Mathf.Sqrt(area * 1.6f)), Mathf.Max(minimum, Mathf.Sqrt(area * 2.2f)) })
            {
                var packed = Pack(width, out var size);
                var totalWidth = size.x + sharedWidth + outputWidth;
                var totalHeight = Mathf.Max(size.y, Mathf.Max(ColumnHeight(shared), ColumnHeight(rightColumn)));
                var score = Mathf.Max(totalWidth / 1.6f, totalHeight);
                if (score >= bestScore) continue;
                bestScore = score; best = packed; bestSize = size;
            }
            foreach (var p in best) result[p.Key] = origin + new Vector2(sharedWidth, 0) + p.Value;
            PlaceColumn(shared, origin.x, origin.y);
            PlaceColumn(rightColumn, origin.x + sharedWidth + bestSize.x + gap, origin.y);
            return result;

            float ColumnHeight(string[] ids) => ids.Length == 0 ? 0 : ids.Sum(k => blocks[k].height) + gap * (ids.Length - 1);
            void PlaceColumn(string[] ids, float x, float y)
            {
                foreach (var id in ids) { result[id] = new Vector2(x, y); y += blocks[id].height + gap; }
            }
            Dictionary<string, Vector2> Pack(float width, out Vector2 size)
            {
                var packed = new Dictionary<string, Vector2>();
                var x = 0f; var y = 0f; var rowHeight = 0f; var right = 0f;
                foreach (var id in main)
                {
                    var box = blocks[id];
                    if (x > 0 && x + box.width > width + .01f) { y += rowHeight + gap; x = 0; rowHeight = 0; }
                    packed[id] = new Vector2(x, y);
                    right = Mathf.Max(right, x + box.width); rowHeight = Mathf.Max(rowHeight, box.height);
                    x += box.width + gap;
                }
                size = new Vector2(right, y + rowHeight);
                return packed;
            }
        }
    }
}
