using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class GeometryDetailChecks
{
    public static void Run(Action<bool,string> check)
    {
        var g=RenderingOptionsChecks.Graph("core.pbrSurface");
        var node=NodeCatalog.Create("core.geometryDissolve");node.Id="dissolve";g.Nodes.Add(node);
        g.Connections[0].To=new GraphPortRef{NodeId=node.Id,PortId="base"};RenderingOptionsChecks.Edge(g,node.Id,"surface","output","surface");
        g.Resources.Add(new GraphResource{Id="dissolve-mask",Kind="texture2D",Uri="builtin://white"});
        var uv=NodeCatalog.Create("core.uv0");uv.Id="mask-uv";g.Nodes.Add(uv);
        var texture=NodeCatalog.Create("core.texture2D");texture.Id="mask-texture";texture.Properties["resourceId"]="dissolve-mask";g.Nodes.Add(texture);
        RenderingOptionsChecks.Edge(g,uv.Id,"uv",texture.Id,"uv");
        RenderingOptionsChecks.Edge(g,texture.Id,"alpha",node.Id,"mask");
        var neutral=ShaderEmitter.Emit(g);
        check(neutral.Succeeded&&!neutral.ShaderSource.Contains("NX_BreakRotate"),"Unchanged Geometry Dissolve compiles out transforms");
        node.Properties["amount"]=.5;
        var result=ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(g)));
        check(result.Succeeded&&result.ShaderSource.Contains("geomBreakupShadow")&&result.ShaderSource.Contains("geomWireAdd")&&result.ShaderSource.Contains("NX_BreakRotate"),"Geometry Dissolve affects surface, additional lights and shadows");
        var representative="input.sourceUV=(tri[0].sourceUV+tri[1].sourceUV+tri[2].sourceUV)/3.0;";
        check(Count(result.ShaderSource,representative)>=3&&result.ShaderSource.Contains("input.uv=(tri[0].uv+tri[1].uv+tri[2].uv)/3.0;")&&result.ShaderSource.Contains("input.color=(tri[0].color+tri[1].color+tri[2].color)/3.0;"),"Textured dissolve mask uses consistent triangle representative in base, additional-light and shadow stages");
        check(GraphPerformance.Analyze(g).StaticPassBudget==3,"Geometry Dissolve adds no pass");
        node.Properties["mask"]="bad";check(!GraphValidator.Validate(g).IsValid,"Dissolve rejects malformed mask");
        var toon=RenderingOptionsChecks.Graph("core.toonSurface");var surface=toon.Nodes[0];surface.Properties["lightingMode"]=0;
        var old=ShaderEmitter.Emit(toon);check(old.Succeeded&&!old.ShaderSource.Contains("nxSceneShadow"),"Default Toon retains default attenuation");
        surface.Properties["receiveShadow"]=0;surface.Properties["borderStrength"]=.5;surface.Properties["borderColor"]=new JArray(1,.4,.2,1);
        var detail=ShaderEmitter.Emit(toon);
        check(detail.Succeeded&&detail.ShaderSource.Contains("float nxSceneShadow=UNITY_SHADOW_ATTENUATION")&&detail.ShaderSource.Contains("float4(1,0.4,0.2,1)"),"Toon shadow receive and border tint compile");
        surface.Properties["borderColor"]=new JArray(0,1);check(!GraphValidator.Validate(toon).IsValid,"Toon rejects malformed border tint");
    }
    static int Count(string text,string value){var count=0;var index=0;while((index=text.IndexOf(value,index,StringComparison.Ordinal))>=0){count++;index+=value.Length;}return count;}
}
