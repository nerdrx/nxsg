using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        void AddOrganizeMenu(ToolbarMenu menu)
        {
            menu.menu.AppendAction("Auto-organize graph", _ => AutoOrganize(false),
                _ => graph != null && graph.Nodes.Count > 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.menu.AppendAction("Auto-organize selection", _ => AutoOrganize(true),
                _ => graph != null && selection.Count > 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.menu.AppendSeparator();
        }

        void AutoOrganize(bool selectedOnly)
        {
            if (graph == null) return;
            var ids = new HashSet<string>(selectedOnly ? selection : graph.Nodes.Select(n => n.Id));
            ids.IntersectWith(graph.Nodes.Select(n => n.Id));
            if (ids.Count < 2) { SetStatus("Select at least two nodes to organize."); return; }

            // Calculate the complete layout before recording or changing the graph.
            var bounds = graph.Nodes.ToDictionary(n => n.Id, OrganizeNodeBounds);
            var targets = ids.ToDictionary(id => id, id => bounds[id].position);
            var blocks = new Dictionary<string, Rect>();
            var owners = new Dictionary<string, string>();
            var connections = graph.Connections.Select(e => (from: e.From.NodeId, to: e.To.NodeId)).ToArray();
            foreach (var group in GraphGroups.All(graph))
            {
                var members = GraphGroups.Members(graph, group);
                if (members.Length == 0 || members.Any(id => !ids.Contains(id) || owners.ContainsKey(id))) continue;
                var key = members.OrderBy(id => id, StringComparer.Ordinal).First();
                var collapsed = group["collapsed"]?.Type == JTokenType.Boolean && (bool)group["collapsed"];
                if (!collapsed && members.Length > 1)
                {
                    var inner = members.ToDictionary(id => id, id => bounds[id]);
                    foreach (var pair in GraphAutoLayout.Arrange(inner, connections)) targets[pair.Key] = pair.Value;
                }
                var memberBounds = members.Select(id => new Rect(targets[id], bounds[id].size));
                blocks[key] = OrganizeGroupBounds(group, memberBounds);
                foreach (var id in members) owners[id] = key;
            }
            foreach (var id in ids)
                if (!owners.ContainsKey(id)) { owners[id] = id; blocks[id] = bounds[id]; }

            var blockEdges = connections.Where(e => owners.ContainsKey(e.from) && owners.ContainsKey(e.to))
                .Select(e => (from: owners[e.from], to: owners[e.to]));
            var arranged = GraphAutoLayout.Arrange(blocks, blockEdges);
            foreach (var id in ids) targets[id] += arranged[owners[id]] - blocks[owners[id]].position;

            // Selection layout never moves its neighbors. Put the organized block below
            // intersecting neighbors when it needs more room than the original selection.
            if (selectedOnly)
            {
                var obstacles = bounds.Where(p => !ids.Contains(p.Key) &&
                    (!nodes.TryGetValue(p.Key, out var box) || box.resolvedStyle.display != DisplayStyle.None))
                    .Select(p => p.Value).ToList();
                foreach (var group in GraphGroups.All(graph))
                {
                    var members = GraphGroups.Members(graph, group);
                    if (members.Length > 0 && members.All(id => !ids.Contains(id)))
                        obstacles.Add(OrganizeGroupBounds(group, members.Select(id => bounds[id])));
                }
                var envelope = OrganizeUnion(blocks.Select(p => new Rect(arranged[p.Key], p.Value.size)));
                var offset = Vector2.zero;
                foreach (var obstacle in obstacles.OrderBy(r => r.yMin))
                {
                    var padded = Rect.MinMaxRect(obstacle.xMin - 24, obstacle.yMin - 24, obstacle.xMax + 24, obstacle.yMax + 24);
                    var moved = new Rect(envelope.position + offset, envelope.size);
                    if (moved.Overlaps(padded)) offset.y = padded.yMax - envelope.yMin;
                }
                foreach (var id in ids) targets[id] += offset;
            }

            if (targets.All(p => (p.Value - Position(p.Key)).sqrMagnitude < .01f))
            { SetStatus("Already organized."); return; }
            Undo.IncrementCurrentGroup();
            Edit(selectedOnly ? "Auto-organize selection" : "Auto-organize graph", () =>
            {
                foreach (var pair in targets) SetPosition(pair.Key, pair.Value);
            });
            canvas.schedule.Execute(() => FrameOrganizedNodes(ids));
            SetStatus("Organized " + ids.Count + " nodes. Undo restores the previous layout.");
        }

        Rect OrganizeNodeBounds(GraphNode node)
        {
            // Hidden Pattern nodes have no rendered height; reserve room for every socket.
            var size = new Vector2(175, 100 + 28 * (Ports(node.Operation, false).Count() + Ports(node.Operation, true).Count()));
            if (nodes.TryGetValue(node.Id, out var box) && box.resolvedStyle.display != DisplayStyle.None)
            {
                if (ValidOrganizeSize(box.layout.width)) size.x = box.layout.width;
                if (ValidOrganizeSize(box.layout.height)) size.y = box.layout.height;
            }
            return new Rect(Position(node.Id), size);
        }

        Rect OrganizeGroupBounds(JObject group, IEnumerable<Rect> members)
        {
            var bounds = OrganizeUnion(members);
            if (group["collapsed"]?.Type == JTokenType.Boolean && (bool)group["collapsed"])
            {
                var card = layer.Children().FirstOrDefault(e => ReferenceEquals(e.userData, group));
                var height = card != null && ValidOrganizeSize(card.layout.height) ? card.layout.height : 240;
                return new Rect(bounds.position, new Vector2(240, height));
            }
            if (group["frame"]?.Type == JTokenType.Boolean && (bool)group["frame"])
                return new Rect(bounds.xMin - 16, bounds.yMin - 96, Mathf.Max(320, bounds.width + 32), bounds.height + 112);
            return new Rect(bounds.xMin - 16, bounds.yMin - 48, Mathf.Max(332, bounds.width + 32), bounds.height + 64);
        }

        static bool ValidOrganizeSize(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;

        static Rect OrganizeUnion(IEnumerable<Rect> values)
        {
            var first = true; var bounds = new Rect();
            foreach (var value in values)
            {
                bounds = first ? value : Rect.MinMaxRect(Mathf.Min(bounds.xMin, value.xMin), Mathf.Min(bounds.yMin, value.yMin),
                    Mathf.Max(bounds.xMax, value.xMax), Mathf.Max(bounds.yMax, value.yMax));
                first = false;
            }
            return bounds;
        }

        void FrameOrganizedNodes(HashSet<string> ids)
        {
            if (graph == null || canvas == null) return;
            var boxes = graph.Nodes.Where(n => ids.Contains(n.Id) && nodes.TryGetValue(n.Id, out var box) &&
                box.resolvedStyle.display != DisplayStyle.None).Select(OrganizeNodeBounds).ToList();
            foreach (var group in GraphGroups.All(graph))
            {
                var members = GraphGroups.Members(graph, group);
                if (members.Length > 0 && members.All(ids.Contains))
                    boxes.Add(OrganizeGroupBounds(group, graph.Nodes.Where(n => members.Contains(n.Id)).Select(OrganizeNodeBounds)));
            }
            if (boxes.Count == 0) return;
            var bounds = OrganizeUnion(boxes);
            var viewport = canvas.contentRect.size;
            if (viewport.x < 1 || viewport.y < 1) return;
            zoom = Mathf.Clamp(Mathf.Min(viewport.x / (bounds.width + 100), viewport.y / (bounds.height + 100)), .1f, 1.6f);
            pan = viewport * .5f - bounds.center * zoom;
            TransformCanvas();
        }
    }
}
