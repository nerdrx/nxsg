using System.Linq;
using NXSG.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        string insertionEdge;
        void UpdateInsertionTarget(string nodeId)
        {
            insertionEdge=null;
            if(selection.Count!=1||!nodes.ContainsKey(nodeId))return;
            var node=graph.Nodes.First(n=>n.Id==nodeId);
            var center=layer.WorldToLocal(nodes[nodeId].worldBound.center);
            var best=18f/Mathf.Max(.1f,zoom);
            foreach(var edge in graph.Connections)
            {
                if(edge.From.NodeId==nodeId||edge.To.NodeId==nodeId)continue;
                var from=sockets.FirstOrDefault(s=>s.output&&s.node==edge.From.NodeId&&s.port==edge.From.PortId);
                var to=sockets.FirstOrDefault(s=>!s.output&&s.node==edge.To.NodeId&&s.port==edge.To.PortId);
                if(from==null||to==null)continue;
                var source=graph.Nodes.First(n=>n.Id==from.node);var target=graph.Nodes.First(n=>n.Id==to.node);
                if(!Ports(node.Operation,false).Any(p=>!graph.Connections.Any(e=>e.To.NodeId==nodeId&&e.To.PortId==p)&&CanOffer(source,from.port,node,p))||!Ports(node.Operation,true).Any(p=>CanOffer(node,p,target,to.port)))continue;
                var a=layer.WorldToLocal(from.hit.worldBound.center);var b=layer.WorldToLocal(to.hit.worldBound.center);var bend=Mathf.Max(45,Mathf.Abs(b.x-a.x)*.45f);
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
