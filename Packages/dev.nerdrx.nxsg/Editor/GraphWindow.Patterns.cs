using System;
using System.Collections.Generic;
using System.IO;
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
        void AddPatternToolbar(Toolbar toolbar)
        {
            var menu = new ToolbarMenu { text = "Patterns", tooltip = "Group selected nodes into one card, or save and reuse them in other graphs." };
            menu.menu.AppendAction("Group selection", _ => GroupSelection());
            menu.menu.AppendAction("Expand all", _ => Edit("Expand Patterns", () => { foreach (var group in GraphGroups.All(graph)) group["collapsed"] = false; }));
            menu.menu.AppendAction("Collapse all", _ => Edit("Collapse Patterns", () => { foreach (var group in GraphGroups.All(graph)) group["collapsed"] = true; }));
            menu.menu.AppendAction("Save selection as Pattern…", _ => SavePattern());
            menu.menu.AppendAction("Insert Pattern…", _ => InsertPattern());
            toolbar.Add(menu);
        }

        void GroupSelection()
        {
            if (graph == null || selection.Count == 0) { SetStatus("Select the nodes you want to group."); return; }
            Edit("Group nodes", () => GraphGroups.Add(graph, selection, "Pattern"));
        }

        void SavePattern()
        {
            if (graph == null || selection.Count == 0) { SetStatus("Select nodes to save as a Pattern."); return; }
            try
            {
                var snippet = GraphClipboard.Copy(graph, selection);
                var path = EditorUtility.SaveFilePanel("Save reusable Pattern", Application.dataPath, "Pattern", "nxsg");
                if (string.IsNullOrEmpty(path)) return;
                // Prevent accidentally replacing the graph currently being edited with a snippet.
                if (!string.IsNullOrEmpty(sourcePath) && Path.GetFullPath(path) == Path.GetFullPath(sourcePath))
                    throw new IOException("Choose a different file from the current graph.");
                File.WriteAllText(path, snippet); AssetDatabase.Refresh();
                SetStatus("Saved reusable Pattern. Insert it through Patterns → Insert Pattern.");
            }
            catch (Exception exception) { SetStatus("Pattern save failed: " + exception.Message); }
        }

        void InsertPattern()
        {
            if (graph == null) { SetStatus("Create or open a graph first."); return; }
            var path = EditorUtility.OpenFilePanel("Insert reusable Pattern", Application.dataPath, "nxsg");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("Pattern exceeds 1 MiB.");
                InsertPatternText(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception exception) { SetStatus("Pattern insert failed: " + exception.Message); }
        }

        void InsertPatternText(string text, string name)
        {
            var pasted = GraphClipboard.Paste(graph, text, 60, 60);
            GraphGroups.Add(pasted.Graph, pasted.NodeIds, name);
            Edit("Insert Pattern", () => { graph = pasted.Graph; selection = pasted.NodeIds.ToList(); selected = selection.LastOrDefault(); });
        }

        void DrawPatternGroups()
        {
            if (graph == null) return;
            foreach (var group in GraphGroups.All(graph))
            {
                var members = new HashSet<string>(GraphGroups.Members(graph, group));
                if (members.Count == 0) continue;
                var positions = members.Select(Position).ToArray();
                var point = new Vector2(positions.Min(p => p.x), positions.Min(p => p.y));
                var collapsed = group["collapsed"]?.Type == JTokenType.Boolean && (bool)group["collapsed"];
                var isFrame = group["frame"]?.Type == JTokenType.Boolean && (bool)group["frame"];
                var boundsMax = positions.Length == 0 ? point : positions.Aggregate(Vector2.Max);
                var frameWidth = Mathf.Max(300, boundsMax.x - point.x + 210);
                var frameHeight = Mathf.Max(100, boundsMax.y - point.y + 170);
                var card = new VisualElement { name = isFrame ? "frame-card" : "pattern-card", style = { position = UnityEngine.UIElements.Position.Absolute,
                    left = isFrame && !collapsed ? point.x - 16 : point.x, top = collapsed ? point.y : point.y - 38, width = collapsed ? 240 : isFrame ? frameWidth : 300,
                    height = isFrame && !collapsed ? frameHeight : StyleKeyword.Auto,
                    backgroundColor = isFrame ? new Color(.20f, .14f, .25f) : new Color(.12f, .18f, .21f), borderTopLeftRadius = 7, borderTopRightRadius = 7,
                    borderBottomLeftRadius = 7, borderBottomRightRadius = 7, paddingBottom = 6 } };
                var header = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                var title = new TextField { value = (string)group["name"] ?? "Pattern", isDelayed = true, maxLength = 80, style = { flexGrow = 1 } };
                title.RegisterValueChangedCallback(evt => Edit("Rename Pattern", () => group["name"] = evt.newValue));
                header.Add(title);
                if (isFrame)
                {
                    var note = new TextField { value = (string)group["note"] ?? "", isDelayed = true, maxLength = 1000, multiline = true, tooltip = "Frame note", style = { flexGrow = 1 } };
                    note.RegisterValueChangedCallback(evt => Edit("Edit frame note", () => group["note"] = evt.newValue));
                    header.Add(new Label("FRAME") { style = { marginLeft = 6, unityFontStyleAndWeight = FontStyle.Bold } });
                    card.Add(header);
                    var noteRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 6, marginRight = 6 } };
                    noteRow.Add(note); card.Add(noteRow);
                }
                else
                {
                    header.Add(new Button(() => Edit("Toggle Pattern", () => group["collapsed"] = !collapsed)) { text = collapsed ? "Expand" : "Fold" });
                    header.Add(new Button(() => Edit("Ungroup nodes", () => GraphGroups.Remove(graph, group))) { text = "×", tooltip = "Ungroup; keep all nodes and wires" });
                    card.Add(header);
                }
                if (isFrame) header.Add(new Button(() => Edit("Ungroup frame", () => GraphGroups.Remove(graph, group))) { text = "×", tooltip = "Remove frame; keep nodes and wires" });
                if (collapsed)
                {
                    foreach (var id in members) if (nodes.TryGetValue(id, out var box)) box.style.display = DisplayStyle.None;
                    sockets.RemoveAll(socket => members.Contains(socket.node));
                    foreach (var node in graph.Nodes.Where(n => members.Contains(n.Id)))
                    {
                        foreach (var port in Ports(node.Operation, false))
                            if (!graph.Connections.Any(e => e.To.NodeId == node.Id && e.To.PortId == port && members.Contains(e.From.NodeId)))
                                AddPatternSocket(card, node, port, false);
                        foreach (var port in Ports(node.Operation, true))
                            if (graph.Connections.Any(e => e.From.NodeId == node.Id && e.From.PortId == port && !members.Contains(e.To.NodeId)) ||
                                !graph.Connections.Any(e => e.From.NodeId == node.Id && e.From.PortId == port))
                                AddPatternSocket(card, node, port, true);
                    }
                    var grip = new Label("⋮⋮  " + members.Count + " nodes · drag to move") { style = { paddingLeft = 10, paddingTop = 6, paddingBottom = 4 } };
                    card.Add(grip);
                    Vector2 start = Vector2.zero;
                    Dictionary<string, Vector2> starts = null;
                    grip.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        selection = members.ToList(); selected = selection.LastOrDefault(); UpdateSelectionOutline(); RebuildInspector();
                        start = evt.position; starts = members.ToDictionary(id => id, Position);
                        grip.CapturePointer(evt.pointerId); evt.StopPropagation();
                    });
                    grip.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        if (!grip.HasPointerCapture(evt.pointerId) || starts == null) return;
                        var delta = ((Vector2)evt.position - start) / zoom;
                        foreach (var pair in starts) SetPosition(pair.Key, pair.Value + delta);
                        card.style.left = point.x + delta.x; card.style.top = point.y + delta.y;
                        layer.MarkDirtyRepaint(); evt.StopPropagation();
                    });
                    grip.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (!grip.HasPointerCapture(evt.pointerId)) return;
                        grip.ReleasePointer(evt.pointerId);
                        Undo.RegisterCompleteObjectUndo(session, "Move Pattern");
                        session.json = GraphJson.Serialize(graph, true); hasUnsavedChanges = true; EditorUtility.SetDirty(session);
                        starts = null; canvas.Focus(); evt.StopPropagation();
                    });
                }
                else if (isFrame)
                {
                    var grip = new Label("⋮⋮  " + members.Count + " nodes · drag to move") { style = { paddingLeft = 10, paddingTop = 6, paddingBottom = 4 } };
                    Vector2 start = Vector2.zero; Dictionary<string, Vector2> starts = null;
                    grip.RegisterCallback<PointerDownEvent>(evt => { if (evt.button != 0) return; selection = members.ToList(); selected = selection.LastOrDefault(); start = evt.position; starts = members.ToDictionary(id => id, Position); grip.CapturePointer(evt.pointerId); evt.StopPropagation(); });
                    grip.RegisterCallback<PointerMoveEvent>(evt => { if (!grip.HasPointerCapture(evt.pointerId) || starts == null) return; var delta = ((Vector2)evt.position - start) / zoom; foreach (var pair in starts) SetPosition(pair.Key, pair.Value + delta); layer.MarkDirtyRepaint(); evt.StopPropagation(); });
                    grip.RegisterCallback<PointerUpEvent>(evt => { if (!grip.HasPointerCapture(evt.pointerId)) return; grip.ReleasePointer(evt.pointerId); Undo.RegisterCompleteObjectUndo(session, "Move Frame"); session.json = GraphJson.Serialize(graph, true); hasUnsavedChanges = true; EditorUtility.SetDirty(session); starts = null; canvas.Focus(); evt.StopPropagation(); });
                    card.Add(grip);
                }
                card.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                if (isFrame && !collapsed)
                {
                    card.pickingMode = PickingMode.Ignore;
                    layer.Insert(0, card);
                    Action updateBounds = () =>
                    {
                        var min = new Vector2(float.MaxValue, float.MaxValue);
                        var max = new Vector2(float.MinValue, float.MinValue);
                        foreach (var id in members)
                        {
                            var p = Position(id); min = Vector2.Min(min, p);
                            var w = 180f; var h = 160f;
                            if (nodes.TryGetValue(id, out var box))
                            {
                                if (!float.IsNaN(box.layout.width) && box.layout.width > 0) w = box.layout.width;
                                if (!float.IsNaN(box.layout.height) && box.layout.height > 0) h = box.layout.height;
                            }
                            max = Vector2.Max(max, p + new Vector2(w,h));
                        }
                        card.style.left = min.x - 16; card.style.top = min.y - 96;
                        card.style.width = Mathf.Max(320,max.x-min.x+32); card.style.height=max.y-min.y+112;
                    };
                    card.schedule.Execute(updateBounds);
                    foreach(var id in members) if(nodes.TryGetValue(id,out var box)) box.RegisterCallback<GeometryChangedEvent>(_ => updateBounds());
                }
                else layer.Add(card);
            }
        }

        void AddPatternSocket(VisualElement card, GraphNode node, string port, bool output)
        {
            AddSocket(card, node, port, output);
            var row = card.Children().Last();
            var label = row.Q<Label>();
            if (label != null) label.text = Title(node.Operation) + " · " + port;
        }
    }
}
