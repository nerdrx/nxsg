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

        var duplicateEdgeId = GraphSamples.CreateDefault();
        var colorEdge = duplicateEdgeId.Connections.Single(e => e.Id == "edge-color");
        duplicateEdgeId.Connections.Single(e => e.Id == "edge-surface").Id = colorEdge.Id;
        var insertNode = NodeCatalog.Create("core.oneMinus"); duplicateEdgeId.Nodes.Add(insertNode);
        var duplicateEdgeBefore = GraphJson.Serialize(duplicateEdgeId);
        assert(GraphInsertion.TryInsert(duplicateEdgeId,insertNode.Id,colorEdge.Id)==null && GraphJson.Serialize(duplicateEdgeId)==duplicateEdgeBefore,
            "wire insertion rejects ambiguous edge ID without changing source");

        var duplicateNodeId = GraphSamples.CreateDefault();
        var duplicateNode = NodeCatalog.Create("core.oneMinus"); duplicateNode.Id = "toon"; duplicateNodeId.Nodes.Add(duplicateNode);
        var duplicateNodeBefore = GraphJson.Serialize(duplicateNodeId);
        assert(GraphInsertion.TryInsert(duplicateNodeId,"toon","edge-color")==null && GraphJson.Serialize(duplicateNodeId)==duplicateNodeBefore,
            "wire insertion rejects ambiguous node ID without changing source");

        var emptyCollections = new ShaderGraph { GraphId="empty-collections", Nodes=null, Connections=null };
        assert(GraphValidator.Validate(emptyCollections).IsValid,"null graph collections are treated as empty by validation");
        assert(GraphInsertion.TryInsert(emptyCollections,"node","edge")==null && GraphInsertion.TryInsert(null,"node","edge")==null,
            "wire insertion safely rejects graphs without collections or null source");
        emptyCollections.Layout = new GraphLayout { ExtensionData = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>
            { ["groups"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["name"]="empty", ["members"] = new Newtonsoft.Json.Linq.JArray("missing") }) } };
        assert(GraphGroups.Members(emptyCollections,GraphGroups.All(emptyCollections).First()).Length==0,
            "group membership handles validator-accepted null node collection");
    }
}
