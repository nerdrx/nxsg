using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
using Newtonsoft.Json.Linq;

public static class RenderingOptionsChecks
{
    public static void Run(Action<bool,string> check)
    {
        foreach(var property in new[]{"shadeMap","occlusion","shadow"})
        {
            var literal=Graph("core.toonSurface");literal.Nodes[0].Properties=new JObject();literal.Nodes[0].Properties[property]=.25;
            var result=ShaderEmitter.Emit(literal);
            check(result.Succeeded && result.ShaderSource.Contains("float3 toonResponse"),"Literal toon control uses the advanced lighting path: "+property);
        }
        var graph=Graph("core.toonSurface");
        var surface=graph.Nodes.First(n=>n.Id=="surface");
        var output=graph.Nodes.First(n=>n.Id=="output");
        foreach(var mode in new[]{0,1,2})
        {
            surface.Properties["lightingMode"]=mode;
            surface.Properties["resourceId"]="ramp";
            if(graph.Resources.Count==0) graph.Resources.Add(new GraphResource{Id="ramp",Kind="texture2D",Uri="builtin://white"});
            var result=ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(graph)));
            check(result.Succeeded,"Toon mode "+mode+" roundtrip emits");
            check(mode==2 ? result.Properties.Any(p=>p.ResourceId=="ramp") : !result.Properties.Any(p=>p.ResourceId=="ramp"),"Only texture ramp mode retains ramp resource");
        }
        surface.Properties["lightingMode"]=0;
        output.Properties["renderMode"]=3;
        output.Properties["cull"]=2;
        output.Properties["stencilEnabled"]=1;
        output.Properties["stencilRef"]=37;
        output.Properties["stencilPass"]=1;
        var alpha=ShaderEmitter.Emit(graph);
        check(alpha.Succeeded && alpha.ShaderSource.Contains("\"Queue\"=\"Transparent\"") && alpha.ShaderSource.Contains("Cull Off") && alpha.ShaderSource.Contains("Blend SrcAlpha OneMinusSrcAlpha") && alpha.ShaderSource.Contains("Ref 37") && alpha.ShaderSource.Contains("Pass Replace"),"Output alpha and stencil state compile");
        foreach(var key in new[]{"renderMode","cull","zWrite","zTest","queueOffset","stencilRef","stencilPass"})
        {
            var previous=output.Properties[key]?.DeepClone(); output.Properties[key]="bad";
            check(!GraphValidator.Validate(graph).IsValid,"Malformed "+key+" is rejected without throwing");
            output.Properties[key]=previous;
        }
        var opaque=Graph("core.toonSurface");opaque.Nodes.First(n=>n.Id=="output").Properties["renderMode"]=1;
        var viewMask=NodeCatalog.Create("core.fresnel");viewMask.Id="viewMask";opaque.Nodes.Add(viewMask);Edge(opaque,"viewMask","value","surface","opacity");
        check(ShaderEmitter.Emit(opaque).ShaderSource.Contains("Name \"ShadowCaster\"") && GraphPerformance.Analyze(opaque).StaticPassBudget==3,"Opaque ignores camera opacity and retains its shadow pass");
        var outlined=Graph("core.pbrSurface");
        var outline=NodeCatalog.Create("core.outline");outline.Id="outline";outlined.Nodes.Add(outline);
        outlined.Connections[0].To=new GraphPortRef{NodeId="outline",PortId="base"};Edge(outlined,"outline","surface","output","surface");
        var emitted=ShaderEmitter.Emit(outlined);
        check(emitted.Succeeded && emitted.ShaderSource.Contains("Name \"Outline\"") && emitted.ShaderSource.Contains("Cull Front"),"Outline adds its own hull pass");
        check(GraphPerformance.Analyze(outlined).StaticPassBudget==4,"Outline cost includes base, additive, shadow and hull");
        foreach(var op in new[]{"core.ssao","core.contactShadow"})
        {
            var depth=Graph("core.pbrSurface");var node=NodeCatalog.Create(op);node.Id="depth";depth.Nodes.Add(node);
            Edge(depth,"depth","visibility","surface",op=="core.ssao"?"occlusion":"shadow");
            check(ShaderEmitter.Emit(depth).Succeeded,"Depth visibility drives correct lighting input: "+op);
            depth.Connections.Last().To.PortId="displacement";
            check(!ShaderEmitter.Emit(depth).Succeeded,"Screen depth is rejected in vertex stage: "+op);
            depth.Connections.RemoveAt(depth.Connections.Count-1);
            check(!ShaderEmitter.Emit(depth).ShaderSource.Contains("NX_ScreenDepthAvailable"),"Disconnected screen effect removes depth helper");
        }
        var volumes=Graph("core.unlitSurface");var light=NodeCatalog.Create("core.lightVolumes");light.Id="light";volumes.Nodes.Add(light);Edge(volumes,"light","color","surface","albedo");
        check(!ShaderEmitter.Emit(volumes).Succeeded,"Missing Light Volumes package is actionable");
        check(ShaderEmitter.Emit(volumes,new EmitterOptions{LightVolumesAvailable=true}).Succeeded,"Installed Light Volumes adapter emits");
    }
    public static ShaderGraph Graph(string operation)
    {
        var g=new ShaderGraph{GraphId="rendering-check"};var surface=NodeCatalog.Create(operation);surface.Id="surface";g.Nodes.Add(surface);
        var output=NodeCatalog.Create("core.output");output.Id="output";g.Nodes.Add(output);Edge(g,"surface","surface","output","surface");return g;
    }
    public static void Edge(ShaderGraph g,string from,string port,string to,string input)
    {g.Connections.Add(new GraphConnection{Id=from+"-"+to+"-"+input,From=new GraphPortRef{NodeId=from,PortId=port},To=new GraphPortRef{NodeId=to,PortId=input}});}
}
