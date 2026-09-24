using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class LayeredSurfaceChecks
{
    public static void Run(Action<bool,string> check)
    {
        var node=NodeCatalog.Create("core.layeredPbrSurface");
        check(node!=null,"Layered PBR is registered");
        if(node==null)return;
        check((double)node.Properties["coat"]==0 && (double)node.Properties["sheen"]==0 &&
            (double)node.Properties["coatRoughness"]==.1 && (double)node.Properties["sheenRoughness"]==.5,
            "Layered PBR starts neutral with independent lobe roughness");
        foreach(var port in new[]{"coat","coatRoughness","sheen","sheenRoughness"})
            check(NodeCatalog.PortType(node,port)=="float","Layered PBR scalar socket "+port);
        check(NodeCatalog.PortType(node,"coatNormal")=="vector3" && NodeCatalog.PortType(node,"sheenColor")=="color",
            "Layered PBR independent normal and sheen color sockets");
        var neutral=Emit(Graph(),check);
        var basic=Emit(Graph("core.pbrSurface"),check);
        check(!neutral.Contains("NX_CoatLayer(") && !neutral.Contains("NX_SheenLayer("),
            "Disconnected zero lobes omit helpers and calls");
        check(Fragment(neutral,"frag")==Fragment(basic,"frag") && Fragment(neutral,"fragAdd")==Fragment(basic,"fragAdd"),
            "Neutral layered fragments preserve PBR lighting, emission and alpha exactly");

        var coat=Graph(); Surface(coat).Properties["coat"]=1;
        var coatSource=Emit(coat,check);
        check(coatSource.Contains("NX_CoatLayer(") && !coatSource.Contains("NX_SheenLayer("),"Only enabled coat helper emits");
        check(Fragment(coatSource,"frag").Contains("NX_CoatLayer(") && Fragment(coatSource,"fragAdd").Contains("NX_CoatLayer("),
            "Coat participates in main and additional pixel lights");
        var sheen=Graph(); Surface(sheen).Properties["sheen"]=1;
        var sheenSource=Emit(sheen,check);
        check(sheenSource.Contains("NX_SheenLayer(") && !sheenSource.Contains("NX_CoatLayer("),"Only enabled sheen helper emits");
        check(Fragment(sheenSource,"frag").Contains("NX_SheenLayer(") && Fragment(sheenSource,"fragAdd").Contains("NX_SheenLayer("),
            "Sheen participates in main and additional pixel lights");

        foreach(var port in new[]{"coat","sheen"})
        {
            var wired=Graph(); AddValue(wired,port,0);
            var source=Emit(wired,check);
            check(source.Contains(port=="coat"?"NX_CoatLayer(":"NX_SheenLayer("),"Connected neutral "+port+" stays live");
        }
        var animated=Graph();
        animated.Parameters.Add(new GraphParameter {Id="coat-amount",Name="Coat Amount",Type=GraphValueType.Float,Binding=GraphBindingKind.AnimatedMaterial,DefaultValue=0,Exposed=true});
        animated.Nodes.Add(new GraphNode {Id="coat-parameter",Operation="core.parameter",Properties=new JObject {["parameterId"]="coat-amount"}});
        Link(animated,"coat-parameter","value","surface","coat");
        var animatedResult=ShaderEmitter.Emit(animated);
        check(animatedResult.Succeeded && animatedResult.ShaderSource.Contains("NX_CoatLayer(") && animatedResult.Properties.Any(p=>p.Binding==GraphBindingKind.AnimatedMaterial),
            "Animated zero-default coat keeps material binding and layer shading");
        var all=Graph();
        foreach(var port in new[]{"coat","coatRoughness","sheen","sheenRoughness"})AddValue(all,port,.4);
        AddConstant(all,"coatNormal","vector3",new JArray(.5,0,1));
        AddConstant(all,"sheenColor","color",new JArray(.2,.4,1,1));
        var restored=GraphJson.Parse(GraphJson.Serialize(all));
        check(GraphValidator.Validate(restored).IsValid && ShaderEmitter.Emit(restored).Succeeded,
            "All layer sockets survive save/load and emit");
        Surface(restored).Properties["sheenColor"]=new JArray(1,0);
        check(!GraphValidator.Validate(restored).IsValid,"Malformed sheen color rejected even with connected input");
        Surface(restored).Properties["sheenColor"]=new JArray(1,1,1,1);
        Surface(restored).Properties["coatRoughness"]=double.NaN;
        check(!GraphValidator.Validate(restored).IsValid,"Non-finite coat roughness rejected");
        CheckInactiveDependencies(check);
    }

    static void CheckInactiveDependencies(Action<bool,string> check)
    {
        var lighting=Graph();var ltcgi=NodeCatalog.Create("core.ltcgi");ltcgi.Id="unused-lighting";lighting.Nodes.Add(ltcgi);
        Link(lighting,ltcgi.Id,"color","surface","sheenColor");
        var disabled=Emit(lighting,check);
        check(!disabled.Contains("Packages/at.pimaker.ltcgi") && !disabled.Contains("NX_SheenLayer("),
            "Disabled sheen ignores optional package dependencies in its color branch");
        Surface(lighting).Properties["sheen"]=1;
        check(!ShaderEmitter.Emit(lighting).Succeeded,"Enabled sheen retains its optional package dependency");
        Surface(lighting).Properties["sheen"]=0;AddValue(lighting,"sheen",0);
        check(!ShaderEmitter.Emit(lighting).Succeeded,"Connected zero sheen retains its color dependency");
        lighting.Connections.RemoveAll(e=>e.To.NodeId=="surface" && (e.To.PortId=="sheen" || e.To.PortId=="albedo"));
        Link(lighting,ltcgi.Id,"color","surface","albedo");
        check(!ShaderEmitter.Emit(lighting).Succeeded,"Disabled sheen does not prune a dependency shared with base albedo");

        var textured=Graph();var texture=NodeCatalog.Create("core.texture2D");texture.Id="coat-texture";
        texture.Properties["resourceId"]="coat-resource";textured.Nodes.Add(texture);
        textured.Resources.Add(new GraphResource {Id="coat-resource",Kind="texture2D",Uri="builtin://white"});
        Link(textured,texture.Id,"alpha","surface","coatRoughness");
        var withoutCoat=ShaderEmitter.Emit(textured);
        check(withoutCoat.Succeeded && !withoutCoat.Properties.Any(p=>p.Type==GraphValueType.Texture2D),
            "Disabled coat omits texture bindings used only for its roughness");
        AddValue(textured,"coat",0);
        var connectedCoat=ShaderEmitter.Emit(textured);
        check(connectedCoat.Succeeded && connectedCoat.Properties.Any(p=>p.Type==GraphValueType.Texture2D),
            "Connected zero coat retains its roughness texture binding");
        textured.Connections.RemoveAll(e=>e.To.NodeId=="surface" && e.To.PortId=="coat");
        texture.Properties["resourceId"]="missing";
        check(!GraphValidator.Validate(textured).IsValid,"Validation still rejects malformed authored dependencies in disabled layers");

        var wired=Graph();var wire=NodeCatalog.Create("core.wireframe");wire.Id="unused-wire";wired.Nodes.Add(wire);
        Link(wired,wire.Id,"value","surface","sheenRoughness");
        check(!Emit(wired,check).Contains("#pragma geometry geomWire"),"Disabled sheen roughness does not enable wireframe geometry");
        Surface(wired).Properties["sheen"]=1;
        check(Emit(wired,check).Contains("#pragma geometry geomWire"),"Enabled sheen roughness retains wireframe geometry");
    }

    public static ShaderGraph Graph(string operation="core.layeredPbrSurface")
    {
        var graph=new ShaderGraph {GraphId="layered-surface-check"};
        var surface=NodeCatalog.Create(operation); surface.Id="surface";
        surface.Properties["roughness"]=.65; surface.Properties["metallic"]=.1;
        graph.Nodes.Add(surface);
        var output=NodeCatalog.Create("core.output");output.Id="output";graph.Nodes.Add(output);
        Link(graph,"surface","surface","output","surface");
        AddConstant(graph,"albedo","color",new JArray(.16,.04,.015,.6));
        AddConstant(graph,"emission","color",new JArray(.02,.01,.005,1));
        return graph;
    }
    public static GraphNode Surface(ShaderGraph graph) => graph.Nodes.Single(n=>n.Id=="surface");
    public static void AddValue(ShaderGraph graph,string port,double value)
    {
        var node=NodeCatalog.Create("core.value"); node.Id="input-"+port;node.Properties["value"]=value;
        graph.Nodes.Add(node);Link(graph,node.Id,"value","surface",port);
    }
    public static void AddConstant(ShaderGraph graph,string port,string type,JArray value)
    {
        var node=new GraphNode {Id="input-"+port,Operation="core.constant",Properties=new JObject { ["valueType"]=type,["value"]=value }};
        graph.Nodes.Add(node);Link(graph,node.Id,"value","surface",port);
    }
    static void Link(ShaderGraph graph,string from,string port,string to,string input)
    {graph.Connections.Add(new GraphConnection {Id=from+"-"+to+"-"+input,From=new GraphPortRef {NodeId=from,PortId=port},To=new GraphPortRef {NodeId=to,PortId=input}});}
    static string Emit(ShaderGraph graph,Action<bool,string> check)
    {
        var result=ShaderEmitter.Emit(graph);
        check(result.Succeeded,"Layered fixture emits: "+string.Join(";",result.Diagnostics.Select(d=>d.Message)));
        return result.ShaderSource ?? "";
    }
    static string Fragment(string source,string function)
    {
        var start=source.IndexOf("float4 "+function+"(",StringComparison.Ordinal);
        if(start<0)return "missing "+function;
        var stop=source.IndexOf("ENDCG",start,StringComparison.Ordinal);
        return source.Substring(start,stop-start);
    }
}
