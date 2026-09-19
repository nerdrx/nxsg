using System;
using System.Linq;

namespace NXSG.Core
{
    public static class GraphInsertion
    {
        // Work on a clone: a failed type/cycle check cannot damage existing wires.
        public static ShaderGraph TryInsert(ShaderGraph source,string nodeId,string edgeId)
        {
            var edge=source.Connections.FirstOrDefault(e=>e.Id==edgeId);
            var node=source.Nodes.FirstOrDefault(n=>n.Id==nodeId);
            if(edge==null||node==null||edge.From.NodeId==nodeId||edge.To.NodeId==nodeId)return null;
            foreach(var input in NodeCatalog.Ports(node.Operation,false))
            {
                if(source.Connections.Any(e=>e.To.NodeId==nodeId&&e.To.PortId==input))continue;
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
