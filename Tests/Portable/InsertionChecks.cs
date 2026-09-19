using System;
using System.Linq;
using NXSG.Core;
public static class InsertionChecks
{
    public static void Run(Action<bool,string> assert)
    {
        var graph=GraphSamples.CreateDefault();
        var edge=graph.Connections.First(e=>graph.Nodes.Single(n=>n.Id==e.From.NodeId).Operation=="core.texture2D");
        var invert=NodeCatalog.Create("core.oneMinus");graph.Nodes.Add(invert);
        var before=GraphJson.Serialize(graph);
        var inserted=GraphInsertion.TryInsert(graph,invert.Id,edge.Id);
        assert(inserted!=null&&GraphValidator.Validate(inserted).IsValid,"wire insertion validates");
        assert(inserted.Connections.Count==graph.Connections.Count+1&&!inserted.Connections.Any(e=>e.Id==edge.Id),"wire split into two connections");
        assert(GraphJson.Serialize(graph)==before,"wire insertion preserves original until applied");
        var value=NodeCatalog.Create("core.value");graph.Nodes.Add(value);
        assert(GraphInsertion.TryInsert(graph,value.Id,edge.Id)==null,"no-input node cannot replace a wire");
        assert(GraphInsertion.TryInsert(inserted,invert.Id,inserted.Connections.First(e=>e.To.NodeId==invert.Id).Id)==null,"self insertion rejected");
    }
}
