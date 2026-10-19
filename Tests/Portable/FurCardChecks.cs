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
        assert(shells.ShaderSource.Contains("max(1,activeLayers)") &&
            shells.ShaderSource.Contains("float lodFade=saturate(activeLayers-") &&
            shells.ShaderSource.Contains("c.a*particleMask*strand*lodFade") &&
            shells.ShaderSource.Contains("distance(_WorldSpaceCameraPos,input.originalWs)") &&
            shells.ShaderSource.Contains("float furLength=max(0,(") &&
            shells.ShaderSource.Contains("n*(furLength*h)") &&
            shells.ShaderSource.Contains("clip(furLength-.00001)") &&
            shells.ShaderSource.Contains("clip(furDensity-.001)") &&
            !shells.ShaderSource.Contains("floor(activeLayers)"),
            "Shell LOD fades continuously; zero length or density hides strands");
        var originalLayers=fur.Properties["layers"];
        var originalMinLayers=fur.Properties["minLayers"];
        fur.Properties["layers"]=4;fur.Properties["minLayers"]=32;
        var cappedLod=ShaderEmitter.Emit(g);
        assert(cappedLod.Succeeded && cappedLod.ShaderSource.Contains("float activeLayers=lerp(4,4,") &&
            !cappedLod.ShaderSource.Contains("Name \"Fur5\""),
            "Minimum shell layers are capped by the total shell count");
        fur.Properties["layers"]=originalLayers;fur.Properties["minLayers"]=originalMinLayers;
        var fullBudget=GraphPerformance.Analyze(g).StaticPassBudget;
        fur.Properties["length"]=0;
        var noLength=ShaderEmitter.Emit(g);
        assert(!noLength.ShaderSource.Contains("Name \"Fur1\"") &&
            !noLength.ShaderSource.Contains("NX_FurSelfShadow") &&
            GraphPerformance.Analyze(g).StaticPassBudget==fullBudget-(int)fur.Properties["layers"],
            "Unconnected zero length omits shell passes");
        fur.Properties["length"]=.04;fur.Properties["density"]=0;
        assert(!ShaderEmitter.Emit(g).ShaderSource.Contains("Name \"Fur1\""),
            "Unconnected zero density omits shell passes");
        fur.Properties["density"]=100;fur.Properties["mask"]=0;
        assert(!ShaderEmitter.Emit(g).ShaderSource.Contains("Name \"Fur1\""),
            "Unconnected zero mask omits shell passes");
        fur.Properties["mask"]=1;fur.Properties["density"]=0;
        var animated=NodeCatalog.Create("core.value");animated.Id="animated-density";animated.Properties["value"]=0;g.Nodes.Add(animated);
        var densityEdge=new GraphConnection{Id="animated-fur-density",From=new GraphPortRef{NodeId=animated.Id,PortId="value"},To=new GraphPortRef{NodeId=fur.Id,PortId="density"}};
        g.Connections.Add(densityEdge);
        assert(ShaderEmitter.Emit(g).ShaderSource.Contains("Name \"Fur1\""),
            "Connected density keeps shells even when its current value is zero");
        g.Connections.Remove(densityEdge);g.Nodes.Remove(animated);fur.Properties["density"]=100;
        fur.Properties["cardsOnly"]=1;fur.Properties["fins"]=1;
        var cards=ShaderEmitter.Emit(g);
        assert(cards.Succeeded && cards.ShaderSource.Contains("Name \"FurCards\""),"Cards mode emits card pass");
        assert(cards.ShaderSource.Contains("if(coverage>0 && gate>0)") &&
            cards.ShaderSource.Contains("normalize(v.normal)*finLength") &&
            cards.ShaderSource.Contains("float4 c=lerp(root,tip,h)"),
            "Cards reject zero density and use unit normals and a linear color ramp");
        fur.Properties["density"]=0;
        assert(!ShaderEmitter.Emit(g).ShaderSource.Contains("Name \"FurCards\""),
            "Unconnected zero density omits the card pass");
        fur.Properties["density"]=100;
        fur.Properties["finOpacity"]=0;
        assert(!ShaderEmitter.Emit(g).ShaderSource.Contains("Name \"FurCards\""),
            "Zero card opacity omits the card pass");
        fur.Properties["finOpacity"]=.7;
        assert(!cards.ShaderSource.Contains("Name \"Fur1\"")&&!cards.ShaderSource.Contains("Name \"FurFins\""),"Cards mode has neither shells nor duplicate fins");
        var saved=GraphJson.Parse(GraphJson.Serialize(g));assert((int)saved.Nodes.Single(n=>n.Id==fur.Id).Properties["cardsOnly"]==1,"Cards mode survives save");
        fur.Properties["cardsOnly"]=0;fur.Properties["finOpacity"]=0;
        var noFins=ShaderEmitter.Emit(g);
        assert(noFins.ShaderSource.Contains("Name \"Fur1\"") && !noFins.ShaderSource.Contains("Name \"FurFins\""),
            "Zero fin opacity omits fins while preserving shells");
        fur.Properties["cardsOnly"]=2;assert(!ShaderEmitter.Emit(g).Succeeded,"Invalid fur mode rejected");
    }
}
