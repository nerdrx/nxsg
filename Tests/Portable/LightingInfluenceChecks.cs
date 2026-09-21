using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
public static class LightingInfluenceChecks
{
    public static void Run(Action<bool,string> assert)
    {
        foreach(var operation in new[]{"core.toonSurface","core.pbrSurface"})
        {
            var graph=GraphSamples.CreateDefault();var surface=graph.Nodes.Single(n=>n.Operation=="core.toonSurface");surface.Operation=operation;surface.Properties["opacity"]=1;
            var before=ShaderEmitter.Emit(graph);assert(before.Succeeded,"Lighting baseline emits");
            surface.Properties["lightingMin"]=0;surface.Properties["lightingMax"]=0;surface.Properties["lightingSaturation"]=1;
            var after=ShaderEmitter.Emit(graph);assert(after.Succeeded&&before.ShaderSource==after.ShaderSource,"Default lighting settings preserve shader output");
            surface.Properties["lightingMin"]=.3;surface.Properties["lightingMax"]=2;surface.Properties["lightingSaturation"]=0;
            var restored=GraphJson.Parse(GraphJson.Serialize(graph));assert(ShaderEmitter.Emit(restored).Succeeded,"Lighting controls survive save/load");
            surface.Properties["lightingMin"]=-1;assert(!ShaderEmitter.Emit(graph).Succeeded,"Negative lighting bound rejected");
        }
    }
}
