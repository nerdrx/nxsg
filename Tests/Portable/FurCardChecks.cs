using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
public static class FurCardChecks
{
    public static void Run(Action<bool,string> assert)
    {
        var g=GraphSamples.CreateDefault();
        var output=g.Nodes.Single(n=>n.Operation=="core.output");
        var edge=g.Connections.Single(e=>e.To.NodeId==output.Id);
        var fur=NodeCatalog.Create("core.fur");fur.Id="fur-cards-test";g.Nodes.Add(fur);
        var original=edge.From;edge.From=new GraphPortRef{NodeId=fur.Id,PortId="surface"};
        g.Connections.Add(new GraphConnection{Id="fur-base",From=original,To=new GraphPortRef{NodeId=fur.Id,PortId="base"}});
        var shells=ShaderEmitter.Emit(g);
        assert(shells.Succeeded && shells.ShaderSource.Contains("Name \"Fur1\""),"Default fur keeps shells");
        fur.Properties["cardsOnly"]=1;fur.Properties["fins"]=1;
        var cards=ShaderEmitter.Emit(g);
        assert(cards.Succeeded && cards.ShaderSource.Contains("Name \"FurCards\""),"Cards mode emits card pass");
        assert(!cards.ShaderSource.Contains("Name \"Fur1\"")&&!cards.ShaderSource.Contains("Name \"FurFins\""),"Cards mode has neither shells nor duplicate fins");
        var saved=GraphJson.Parse(GraphJson.Serialize(g));assert((int)saved.Nodes.Single(n=>n.Id==fur.Id).Properties["cardsOnly"]==1,"Cards mode survives save");
        fur.Properties["cardsOnly"]=2;assert(!ShaderEmitter.Emit(g).Succeeded,"Invalid fur mode rejected");
    }
}
