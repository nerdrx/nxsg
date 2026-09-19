using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
using Newtonsoft.Json.Linq;

public static class EffectsBackendChecks
{
    public static void Run(Action<bool,string> assert)
    {
        foreach(var op in NodeCatalog.All.Where(op => op != "core.parameter" && op != "core.output" && op != "core.shell" && op != "core.surfaceParticles"))
        {
            var graph=new ShaderGraph {GraphId="effects"};
            var node=NodeCatalog.Create(op); node.Id="effect";
            if(op=="core.texture2D"||op=="core.sticker"||op=="core.triplanarTexture"||op=="core.matcapTexture"||op=="core.parallaxOcclusion"||op=="core.chromaticTexture"||op=="core.interiorMapping"||op=="core.textureBomb") {node.Properties["resourceId"]="texture";graph.Resources.Add(new GraphResource {Id="texture",Kind="texture2D",Uri="builtin://white"});}
            graph.Nodes.Add(node);
            if (op == "core.fur" || op == "core.tessellation") { var baseNode = NodeCatalog.Create("core.unlitSurface"); baseNode.Id = "base"; graph.Nodes.Add(baseNode); Connect(graph,"base","surface","effect","base"); }
            var surface=NodeCatalog.Create("core.unlitSurface"); surface.Id="surface";graph.Nodes.Add(surface);
            var output=NodeCatalog.Create("core.output");output.Id="output";graph.Nodes.Add(output);
            var port=NodeCatalog.Ports(op,true).First();var type=NodeCatalog.PortType(node,port);
            if(type=="surface") { if(op=="core.fur" || op=="core.tessellation") Connect(graph,node.Id,port,"output","surface"); else { var color=NodeCatalog.Create("core.constant");color.Id="albedo";graph.Nodes.Add(color);Connect(graph,color.Id,"value",node.Id,"albedo");Connect(graph,node.Id,port,"output","surface"); } }
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
            var before=GraphJson.Serialize(graph);var result=ShaderEmitter.Emit(graph, new EmitterOptions { LtcgiAvailable = true });
            assert(result.Succeeded,op+" emits: "+string.Join(";",result.Diagnostics.Select(d=>d.Message)));
            if (op == "core.scanlines")
            {
                assert(result.ShaderSource.Contains("fwidth(x)"), "scanlines fragment uses derivative filtering");
                assert(result.ShaderSource.Contains("hiIntegral-loIntegral"), "scanlines fragment uses periodic box integral");
            }
            assert(before==GraphJson.Serialize(graph),op+" emission preserves source");
            graph.Nodes.Reverse();graph.Connections.Reverse();assert(result.ShaderSource==ShaderEmitter.Emit(graph, new EmitterOptions { LtcgiAvailable = true }).ShaderSource,op+" deterministic order");
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
    }
    static void Connect(ShaderGraph graph,string from,string port,string to,string input)
    { graph.Connections.Add(new GraphConnection {Id=Guid.NewGuid().ToString("N"),From=new GraphPortRef {NodeId=from,PortId=port},To=new GraphPortRef {NodeId=to,PortId=input}}); }
}
