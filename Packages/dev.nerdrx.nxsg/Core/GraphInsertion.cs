using System;
using System.Linq;

namespace NXSG.Core
{
    public static class GraphInsertion
    {
        // Work on a clone: a failed type/cycle check cannot damage existing wires.
        public static ShaderGraph TryInsert(ShaderGraph source,string nodeId,string edgeId)
        {
            if(source?.Connections==null||source.Nodes==null||string.IsNullOrWhiteSpace(nodeId)||string.IsNullOrWhiteSpace(edgeId))return null;
            var connections=source.Connections;
            var graphNodes=source.Nodes;
            var edges=connections.Where(e=>e!=null&&e.Id==edgeId).Take(2).ToArray();
            var nodes=graphNodes.Where(n=>n!=null&&n.Id==nodeId).Take(2).ToArray();
            if(edges.Length!=1||nodes.Length!=1)return null;
            var edge=edges[0];
            var node=nodes[0];
            if(edge.From.NodeId==nodeId||edge.To.NodeId==nodeId)return null;
            foreach(var input in NodeCatalog.Ports(node.Operation,false))
            {
                if(connections.Any(e=>e.To.NodeId==nodeId&&e.To.PortId==input))continue;
                foreach(var output in NodeCatalog.Ports(node.Operation,true))
                {
                    var trial=GraphJson.Parse(GraphJson.Serialize(source));
                    trial.Connections.RemoveAll(e=>e.Id==edgeId);
                    trial.Connections.Add(new GraphConnection{Id=Guid.NewGuid().ToString("N"),From=new GraphPortRef{NodeId=edge.From.NodeId,PortId=edge.From.PortId},To=new GraphPortRef{NodeId=nodeId,PortId=input}});
                    trial.Connections.Add(new GraphConnection{Id=Guid.NewGuid().ToString("N"),From=new GraphPortRef{NodeId=nodeId,PortId=output},To=new GraphPortRef{NodeId=edge.To.NodeId,PortId=edge.To.PortId}});
                    if(!GraphValidator.Validate(trial).Diagnostics.Any(d=>d.Severity==DiagnosticSeverity.Error&&(d.Code.StartsWith("connection.",StringComparison.Ordinal)||d.Code=="graph.cycle")))return trial;
                }
            }
            return null;
        }
    }
}
