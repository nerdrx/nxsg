using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;

public static class LtcgiChecks
{
    public static void Run(Action<bool,string> assert)
    {
        var graph = new ShaderGraph { GraphId="ltcgi-check" };
        foreach(var operation in new[]{"core.ltcgi","core.unlitSurface","core.output"}) { var n=NodeCatalog.Create(operation); n.Id=operation; graph.Nodes.Add(n); }
        Connect(graph,"core.ltcgi","color","core.unlitSurface","emission");
        Connect(graph,"core.unlitSurface","surface","core.output","surface");
        var missing=ShaderEmitter.Emit(graph);
        assert(!missing.Succeeded && missing.Diagnostics.Any(d=>d.Message.Contains("at.pimaker.ltcgi")),"missing optional LTCGI is actionable");
        var result=ShaderEmitter.Emit(graph,new EmitterOptions{LtcgiAvailable=true});
        assert(result.Succeeded,"LTCGI emits with explicit availability");
        assert(result.ShaderSource.Contains("LTCGI_AVATAR_MODE") && result.ShaderSource.Contains("LTCGI.cginc") && result.ShaderSource.Contains("LTCGI\"=\"ALWAYS"),"LTCGI include, avatar mode and tag");
        assert(result.ShaderSource.Contains("UNITY_PASS_FORWARDADD") && result.ShaderSource.Contains("UNITY_PASS_SHADOWCASTER"),"LTCGI excludes additive and shadow passes");
        graph.Connections.RemoveAt(0);
        var dead=ShaderEmitter.Emit(graph);
        assert(dead.Succeeded && !dead.ShaderSource.Contains("LTCGI.cginc"),"unused LTCGI requires no dependency");
        Connect(graph,"core.ltcgi","color","core.unlitSurface","displacement");
        assert(!ShaderEmitter.Emit(graph,new EmitterOptions{LtcgiAvailable=true}).Succeeded,"vertex lighting rejected");
    }
    static void Connect(ShaderGraph g,string from,string port,string to,string input) { g.Connections.Add(new GraphConnection{Id=from+input,From=new GraphPortRef{NodeId=from,PortId=port},To=new GraphPortRef{NodeId=to,PortId=input}}); }
}
