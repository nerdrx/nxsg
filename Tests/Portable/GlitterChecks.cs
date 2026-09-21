using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
using Newtonsoft.Json.Linq;
public static class GlitterChecks
{
    public static ShaderGraph Graph(string port="color",string input="emission")
    {
        var g=new ShaderGraph{GraphId="glitter-check"};
        var glitter=NodeCatalog.Create("core.glitter");glitter.Id="glitter";g.Nodes.Add(glitter);
        var surface=NodeCatalog.Create("core.unlitSurface");surface.Id="surface";g.Nodes.Add(surface);
        var black=NodeCatalog.Create("core.constant");black.Id="black";black.Properties["valueType"]="color";black.Properties["value"]=new JArray(0,0,0,1);g.Nodes.Add(black);
        var time=NodeCatalog.Create("core.value");time.Id="time";time.Properties["value"]=0;g.Nodes.Add(time);
        var output=NodeCatalog.Create("core.output");output.Id="output";g.Nodes.Add(output);
        Link(g,"time","value","glitter","time");if(input!="albedo")Link(g,"black","value","surface","albedo");Link(g,"glitter",port,"surface",input);Link(g,"surface","surface","output","surface");return g;
    }
    public static void Link(ShaderGraph g,string a,string p,string b,string q){g.Connections.Add(new GraphConnection{Id=a+b+q,From=new GraphPortRef{NodeId=a,PortId=p},To=new GraphPortRef{NodeId=b,PortId=q}});}
    public static void Run(Action<bool,string> assert)
    {
        foreach(var pair in new[]{new[]{"color","emission"},new[]{"color","albedo"},new[]{"value","opacity"},new[]{"value","emission"}})
        {
            var g=Graph(pair[0],pair[1]);var result=ShaderEmitter.Emit(g);assert(result.Succeeded,"Glitter to "+pair[1]+": "+string.Join(";",result.Diagnostics.Select(d=>d.Message)));
            assert(result.ShaderSource!=null&&result.ShaderSource.Contains("NX_Glitter(input,"),"Glitter uses procedural helper");
        }
        var vertex=ShaderEmitter.Emit(Graph("value","displacement"));assert(!vertex.Succeeded&&vertex.Diagnostics.Any(d=>d.Message.Contains("Glitter needs fragment")),"Glitter rejects vertex use with an explanation");
        foreach(var property in new[]{"density","size","viewStrength","twinkle","mask"}){var g=Graph();g.Nodes[0].Properties[property]=2;assert(!GraphValidator.Validate(g).IsValid,"Glitter validates "+property);}
    }
}
