using System.Linq;
using NXSG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        string insertionEdge;
        string insertionNodeId;
        readonly System.Collections.Generic.List<(GraphConnection edge, SocketView from, SocketView to)> insertionCandidates = new System.Collections.Generic.List<(GraphConnection, SocketView, SocketView)>();

        void PrepareInsertionCache(GraphNode node)
        {
            if (insertionNodeId == node.Id) return;
            insertionNodeId = node.Id;
            insertionCandidates.Clear();
            var graphNodes = graph.Nodes.ToDictionary(item => item.Id);
            var occupied = new System.Collections.Generic.HashSet<string>(graph.Connections.Where(edge => edge.To.NodeId == node.Id).Select(edge => edge.To.PortId));
            var inputs = Ports(node.Operation, false).Where(port => !occupied.Contains(port)).ToArray();
            var outputs = Ports(node.Operation, true);
            foreach (var edge in graph.Connections)
            {
                if (edge.From.NodeId == node.Id || edge.To.NodeId == node.Id) continue;
                if (!socketLookup.TryGetValue((edge.From.NodeId, edge.From.PortId, true), out var from) ||
                    !socketLookup.TryGetValue((edge.To.NodeId, edge.To.PortId, false), out var to)) continue;
                if (!graphNodes.TryGetValue(from.node, out var source) || !graphNodes.TryGetValue(to.node, out var target)) continue;
                if (inputs.Any(port => CanOffer(source, from.port, node, port)) && outputs.Any(port => CanOffer(node, port, target, to.port)))
                    insertionCandidates.Add((edge, from, to));
            }
        }

        void UpdateInsertionTarget(string nodeId)
        {
            insertionEdge=null;
            if(selection.Count!=1||!nodes.ContainsKey(nodeId))return;
            var node=graph.Nodes.First(n=>n.Id==nodeId);
            PrepareInsertionCache(node);
            var center=layer.WorldToLocal(nodes[nodeId].worldBound.center);
            var best=18f/Mathf.Max(.1f,zoom);
            foreach(var candidate in insertionCandidates)
            {
                var edge = candidate.edge; var from = candidate.from; var to = candidate.to;
                var a=layer.WorldToLocal(from.hit.worldBound.center);var b=layer.WorldToLocal(to.hit.worldBound.center);var bend=Mathf.Max(45,Mathf.Abs(b.x-a.x)*.45f);
                // A cubic stays inside the bounds of its endpoints and control points.
                if (center.y < Mathf.Min(a.y,b.y)-best || center.y > Mathf.Max(a.y,b.y)+best ||
                    center.x < Mathf.Min(Mathf.Min(a.x,b.x),b.x-bend)-best || center.x > Mathf.Max(Mathf.Max(a.x,b.x),a.x+bend)+best) continue;
                var previous=a;
                for(var i=1;i<=32;i++)
                {
                    float t=i/32f,u=1-t;var point=u*u*u*a+3*u*u*t*(a+Vector2.right*bend)+3*u*t*t*(b-Vector2.right*bend)+t*t*t*b;
                    var segment=point-previous;var closest=previous+segment*Mathf.Clamp01(Vector2.Dot(center-previous,segment)/Mathf.Max(.0001f,segment.sqrMagnitude));
                    var distance=Vector2.Distance(center,closest);if(distance<best){best=distance;insertionEdge=edge.Id;}
                    previous=point;
                }
            }
        }
        bool InsertOnHighlightedWire(string nodeId)
        {
            var edge=insertionEdge;insertionEdge=null;
            if(edge==null)return false;
            var inserted=GraphInsertion.TryInsert(graph,nodeId,edge);
            if(inserted==null){SetStatus("Cannot insert here: connections would conflict or form a loop.");return false;}
            Edit("Insert node on wire",()=>graph=inserted);
            SetStatus("Node inserted into wire. Undo restores the previous connections.");return true;
        }
    }
}
