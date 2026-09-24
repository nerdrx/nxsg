using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class ParticleInputScopeChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(var port in NodeCatalog.Ports("core.surfaceParticles",false).Where(p=>p!="base"))
        {
            var graph=Graph();var wire=NodeCatalog.Create("core.wireframe");wire.Id="wire";graph.Nodes.Add(wire);
            Link(graph,"wire","value","particles",port);
            check(GraphValidator.Validate(graph).IsValid,"Wireframe particle "+port+" is a structurally valid scalar connection");
            var result=ShaderEmitter.Emit(graph);
            check(!result.Succeeded && result.Diagnostics.Any(d=>d.Message.Contains("Connect Wireframe to the Base surface")),
                "Wireframe rejected on generated-particle "+port+" input");
        }
        var valid=Graph();var baseWire=NodeCatalog.Create("core.wireframe");baseWire.Id="base-wire";valid.Nodes.Add(baseWire);
        Link(valid,"base-wire","value","base","opacity");
        check(ShaderEmitter.Emit(valid).Succeeded,"Wireframe remains supported on the particle emitter base surface");

        // Particle Info may feed appearance and an inactive auxiliary input of the base.
        // Its active appearance branch must not make that inactive base edge forbidden.
        var inactive=Graph();inactive.Nodes.Single(n=>n.Id=="base").Operation="core.layeredPbrSurface";
        var info=NodeCatalog.Create("core.particleInfo");info.Id="info";inactive.Nodes.Add(info);
        Link(inactive,"info","age","particles","opacity");Link(inactive,"info","age","base","sheenRoughness");
        check(ShaderEmitter.Emit(inactive).Succeeded,"Particle Info ignores inactive base layer dependencies");
        inactive.Nodes.Single(n=>n.Id=="base").Properties["sheen"]=1;
        check(!ShaderEmitter.Emit(inactive).Succeeded,"Particle Info remains forbidden in an active base layer");
    }
    static ShaderGraph Graph()
    {
        var graph=new ShaderGraph {GraphId="particle-input-scope"};
        foreach(var pair in new[]{new[]{"base","core.unlitSurface"},new[]{"particles","core.surfaceParticles"},new[]{"output","core.output"}})
        {var node=NodeCatalog.Create(pair[1]);node.Id=pair[0];graph.Nodes.Add(node);}
        Link(graph,"base","surface","particles","base");Link(graph,"particles","surface","output","surface");return graph;
    }
    static void Link(ShaderGraph graph,string from,string port,string to,string input)
    {graph.Connections.Add(new GraphConnection {Id=from+"-"+to+"-"+input,From=new GraphPortRef {NodeId=from,PortId=port},To=new GraphPortRef {NodeId=to,PortId=input}});}
}
