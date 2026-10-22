using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
using Newtonsoft.Json.Linq;

public static class EffectsBackendChecks
{
    public static void Run(Action<bool,string> assert)
    {
        foreach(var op in NodeCatalog.All.Where(op => op != "core.parameter" && op != "core.output" && op != "core.shell" && op != "core.surfaceParticles" && op != "core.particleInfo" && op != "core.rayPosition"))
        {
            var graph=new ShaderGraph {GraphId="effects"};
            var node=NodeCatalog.Create(op); node.Id="effect";
            if(FeatureNodes.NeedsResource(op)||op=="core.texture2D"||op=="core.sticker"||op=="core.triplanarTexture"||op=="core.matcapTexture") {node.Properties["resourceId"]="texture";graph.Resources.Add(new GraphResource {Id="texture",Kind=op=="core.cubemap"?"cubemap":op=="core.textureArray"?"texture2DArray":"texture2D",Uri="builtin://white"});}
            graph.Nodes.Add(node);
            if (op == "core.vertexDeform" || op == "core.outline" || op == "core.softOutline" || op == "core.geometryDissolve" || op == "core.fur" || op == "core.tessellation") { var baseNode = NodeCatalog.Create("core.unlitSurface"); baseNode.Id = "base"; graph.Nodes.Add(baseNode); Connect(graph,"base","surface","effect","base"); }
            var surface=NodeCatalog.Create("core.unlitSurface"); surface.Id="surface";graph.Nodes.Add(surface);
            var output=NodeCatalog.Create("core.output");output.Id="output";graph.Nodes.Add(output);
            var port=NodeCatalog.Ports(op,true).First();var type=NodeCatalog.PortType(node,port);
            if(type=="surface") { if(op=="core.vertexDeform" || op=="core.outline" || op=="core.softOutline" || op=="core.geometryDissolve" || op=="core.fur" || op=="core.tessellation" || op=="core.volumeSurface") Connect(graph,node.Id,port,"output","surface"); else { var color=NodeCatalog.Create("core.constant");color.Id="albedo";graph.Nodes.Add(color);Connect(graph,color.Id,"value",node.Id,"albedo");Connect(graph,node.Id,port,"output","surface"); } }
            else
            {
                if(type=="vector2")
                {
                    var texture=NodeCatalog.Create("core.texture2D");texture.Id="sample";texture.Properties["resourceId"]="resource";
                    graph.Resources.Add(new GraphResource {Id="resource",Kind="texture2D",Uri="builtin://white"});graph.Nodes.Add(texture);
                    Connect(graph,node.Id,port,texture.Id,"uv");Connect(graph,texture.Id,"color",surface.Id,"albedo");
                }
                else if(type=="vector3") {surface.Operation="core.pbrSurface";Connect(graph,node.Id,port,surface.Id,"normal");}
                else Connect(graph,node.Id,port,surface.Id,"albedo");
                Connect(graph,surface.Id,"surface",output.Id,"surface");
            }
            var before=GraphJson.Serialize(graph);var result=ShaderEmitter.Emit(graph, new EmitterOptions { LtcgiAvailable = true, LightVolumesAvailable = true });
            assert(result.Succeeded,op+" emits: "+string.Join(";",result.Diagnostics.Select(d=>d.Message)));
            if (op == "core.scanlines")
            {
                assert(result.ShaderSource.Contains("fwidth(x)"), "scanlines fragment uses derivative filtering");
                assert(result.ShaderSource.Contains("hiIntegral-loIntegral"), "scanlines fragment uses periodic box integral");
            }
            assert(before==GraphJson.Serialize(graph),op+" emission preserves source");
            graph.Nodes.Reverse();graph.Connections.Reverse();assert(result.ShaderSource==ShaderEmitter.Emit(graph, new EmitterOptions { LtcgiAvailable = true, LightVolumesAvailable = true }).ShaderSource,op+" deterministic order");
        }
        var nullable = new ShaderGraph { GraphId="optional-null", Parameters=null, Resources=null };
        nullable.Nodes.Add(new GraphNode { Id="unlit",Operation="core.unlitSurface",Properties=null });nullable.Nodes.Add(NodeCatalog.Create("core.output"));nullable.Nodes.Last().Id="out";
        Connect(nullable,"unlit","surface","out","surface");
        assert(ShaderEmitter.Emit(nullable).Succeeded,"optional null collections/default properties handled without mutation or crash");
        var grouped=GraphSamples.CreateDefault();var hash=GraphJson.ComputeSemanticHash(grouped);
        var group=GraphGroups.Add(grouped,grouped.Nodes.Take(2).Select(n=>n.Id),"Reusable");
        assert(GraphJson.ComputeSemanticHash(grouped)==hash,"folding group leaves shader semantics unchanged");
        var copy=GraphJson.Parse(GraphJson.Serialize(grouped));assert(GraphGroups.All(copy).Count()==1,"group survives roundtrip");
        var snippet=GraphClipboard.Copy(grouped,GraphGroups.Members(grouped,group));
        var pasted=GraphClipboard.Paste(grouped,snippet,20,20);assert(pasted.NodeIds.All(id=>!grouped.Nodes.Any(n=>n.Id==id)),"Pattern inserts fresh IDs");
        assert(GraphGroups.All(pasted.Graph).Count()==2,"Pattern clipboard preserves groups with remapped members");
        GraphGroups.Remove(grouped,group);assert(GraphGroups.All(grouped).Count()==0&&GraphJson.ComputeSemanticHash(grouped)==hash,"ungroup preserves nodes");

        var rawGroupGraph = new ShaderGraph { GraphId="raw-groups", Layout=new GraphLayout { ExtensionData=new System.Collections.Generic.Dictionary<string,JToken>() } };
        rawGroupGraph.Nodes.Add(new GraphNode { Id="a", Operation="core.constant" });
        rawGroupGraph.Nodes.Add(new GraphNode { Id="b", Operation="core.constant" });
        var rawUnknown = new JArray {
            new JObject { ["id"]="affected", ["name"]="Affected", ["members"]=new JArray("a", new JObject { ["futureMember"]=true }), ["futureField"]=42 },
            JValue.CreateString("future group record"),
            new JObject { ["id"]="bad-name", ["name"]=7, ["members"]=new JArray("b") },
            new JObject { ["id"]="unaffected", ["name"]="Unaffected", ["members"]=new JArray("b") }
        };
        var expectedRawAfterMove = (JArray)rawUnknown.DeepClone();
        expectedRawAfterMove[0]["members"] = new JArray(rawUnknown[0]["members"][1].DeepClone());
        rawGroupGraph.Layout.ExtensionData["groups"] = rawUnknown.DeepClone();
        var insertedGroup = GraphGroups.Add(rawGroupGraph,new[] {"a"},"Inserted");
        var afterRawAdd = (JArray)rawGroupGraph.Layout.ExtensionData["groups"];
        assert(JToken.DeepEquals(afterRawAdd[1],rawUnknown[1]) && JToken.DeepEquals(afterRawAdd[2],rawUnknown[2]) &&
            (int?)afterRawAdd[0]["futureField"]==42 && JToken.DeepEquals(afterRawAdd[0]["members"][0],rawUnknown[0]["members"][1]),
            "add preserves unknown group records, fields, and member data");
        GraphGroups.Remove(rawGroupGraph,insertedGroup);
        assert(JToken.DeepEquals(expectedRawAfterMove,rawGroupGraph.Layout.ExtensionData["groups"]),"remove preserves unrecognized group data");

        var beyondCap = new ShaderGraph { GraphId="groups-beyond-cap", Layout=new GraphLayout { ExtensionData=new System.Collections.Generic.Dictionary<string,JToken>() } };
        beyondCap.Nodes.Add(new GraphNode { Id="a", Operation="core.constant" });
        beyondCap.Nodes.Add(new GraphNode { Id="b", Operation="core.constant" });
        var beyondGroups = new JArray {
            new JObject { ["id"]="a-group", ["name"]="A", ["members"]=new JArray("a") },
            new JObject { ["id"]="b-group", ["name"]="B", ["members"]=new JArray("b") }
        };
        for (var i=0;i<1022;i++) beyondGroups.Add(new JObject { ["id"]="visible"+i, ["name"]="Visible", ["members"]=new JArray("missing") });
        var beyondRecord = new JObject { ["id"]="beyond-view", ["name"]="Beyond", ["members"]=new JArray("a") };
        beyondGroups.Add(beyondRecord.DeepClone());
        beyondCap.Layout.ExtensionData["groups"] = beyondGroups;
        var beyondInserted = GraphGroups.Add(beyondCap,new[] {"a","b"},"Replacement");
        var afterBeyondAdd = (JArray)beyondCap.Layout.ExtensionData["groups"];
        assert(afterBeyondAdd.Count==1024 && afterBeyondAdd.Any(g=>JToken.DeepEquals(g,beyondRecord)),"add preserves raw group records beyond All view cap");
        GraphGroups.Remove(beyondCap,beyondInserted);
        var afterBeyondRemove = (JArray)beyondCap.Layout.ExtensionData["groups"];
        assert(afterBeyondRemove.Any(g=>JToken.DeepEquals(g,beyondRecord)),"remove preserves raw group records beyond All view cap");

        var nonArrayGroups = new ShaderGraph { GraphId="non-array-groups", Layout=new GraphLayout { ExtensionData=new System.Collections.Generic.Dictionary<string,JToken> { ["groups"]=new JObject { ["future"]=true } } } };
        GraphGroups.Remove(nonArrayGroups,new JObject());
        assert(JToken.DeepEquals(nonArrayGroups.Layout.ExtensionData["groups"],new JObject { ["future"]=true }),"remove leaves non-array group data intact");

        var fullGroups = new ShaderGraph { GraphId="group-limit", Layout=new GraphLayout { ExtensionData=new System.Collections.Generic.Dictionary<string,JToken>() } };
        fullGroups.Nodes.Add(new GraphNode { Id="a", Operation="core.constant" });
        fullGroups.Nodes.Add(new GraphNode { Id="b", Operation="core.constant" });
        for (var i=0;i<1023;i++) fullGroups.Nodes.Add(new GraphNode { Id="n"+i, Operation="core.constant" });
        fullGroups.Layout.ExtensionData["groups"] = new JArray(Enumerable.Range(0,1024).Select(i => new JObject {
            ["id"]="g"+i, ["name"]="group", ["members"]=new JArray(i==0 ? new[] {"a","b"} : new[] {"n"+(i-1)})
        }));
        var fullGroupsBefore = GraphJson.Serialize(fullGroups);
        var rejected = false;
        try { GraphGroups.Add(fullGroups,new[] {"a"},"Rejected"); }
        catch (InvalidOperationException) { rejected = true; }
        assert(rejected,"regroup that exceeds group limit is rejected");
        assert(GraphJson.Serialize(fullGroups)==fullGroupsBefore,"rejected regroup at group limit leaves graph unchanged");
        GraphGroups.Add(fullGroups,new[] {"a","b"},"Replacement");
        assert(GraphGroups.All(fullGroups).Count()==1024 && GraphGroups.Members(fullGroups,GraphGroups.All(fullGroups).Last()).Length==2,
            "regroup that replaces an existing group succeeds at group limit");
    }
    static void Connect(ShaderGraph graph,string from,string port,string to,string input)
    { graph.Connections.Add(new GraphConnection {Id=Guid.NewGuid().ToString("N"),From=new GraphPortRef {NodeId=from,PortId=port},To=new GraphPortRef {NodeId=to,PortId=input}}); }
}
