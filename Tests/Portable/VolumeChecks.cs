using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;

public static class VolumeChecks
{
    public static void Run(Action<bool,string> check)
    {
        var graph=new ShaderGraph { GraphId="volume-check" };
        var volume=NodeCatalog.Create("core.volumeSurface"); volume.Id="volume";
        var ray=NodeCatalog.Create("core.rayPosition");ray.Id="ray";
        var noise=NodeCatalog.Create("core.noise");noise.Id="noise";noise.Properties["dimensions"]=4;
        var output=NodeCatalog.Create("core.output");output.Id="out";
        graph.Nodes.AddRange(new[]{volume,ray,noise,output});
        Connect(graph,"ray","position","noise","position");Connect(graph,"noise","value","volume","density");Connect(graph,"volume","surface","out","surface");
        var result=ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(graph)));
        check(result.Succeeded,"4D volume density graph emits: "+string.Join(";",result.Diagnostics.Select(d=>d.Message)));
        check(result.Succeeded && result.ShaderSource.Contains("Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha") && result.ShaderSource.Contains("if(result.a>=.995)break"),"volume alpha and bounded early exit contract");
        volume.Properties["mode"]=1; check(!ShaderEmitter.Emit(graph).Succeeded,"solid mode requires a distance connection"); volume.Properties["mode"]=0;
        volume.Properties["bounds"]="invalid";check(!GraphValidator.Validate(graph).IsValid,"malformed bounds reported without throwing");
        volume.Properties["bounds"]=new JArray(0,.5,.5);check(!GraphValidator.Validate(graph).IsValid,"zero volume extent rejected");
        volume.Properties["bounds"]=new JArray(.5,.5,.5);volume.Properties["steps"]=10000;check(!GraphValidator.Validate(graph).IsValid,"excess march steps rejected");
        volume.Properties["steps"]=32;volume.Properties["steps"]=32.5;check(!GraphValidator.Validate(graph).IsValid,"fractional steps rejected");
        volume.Properties["steps"]=32;volume.Properties["color"]=new JArray(1,0);check(!GraphValidator.Validate(graph).IsValid,"malformed volume color rejected");
        volume.Properties["color"]=new JArray(1,0,0,1);volume.Operation="core.unlitSurface";
        graph.Connections.Single(c=>c.To.NodeId=="volume").To.PortId="opacity";
        check(!ShaderEmitter.Emit(graph).Succeeded,"Ray Position outside Volume Surface rejected");
    }
    static void Connect(ShaderGraph g,string from,string port,string to,string input)
    {g.Connections.Add(new GraphConnection {Id=Guid.NewGuid().ToString("N"),From=new GraphPortRef {NodeId=from,PortId=port},To=new GraphPortRef {NodeId=to,PortId=input}});}
}
