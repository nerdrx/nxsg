using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;

public static class UsabilityChecks
{
    public static void Run(Action<bool,string> check, string fixtures)
    {
        var directory = Path.Combine(fixtures,"../../Packages/dev.nerdrx.nxsg/Samples~");
        var graph = GraphJson.Parse(File.ReadAllText(Path.Combine(directory,"Particle Lifetime.nxsg")));
        var result = ShaderEmitter.Emit(graph);
        check(result.Succeeded,"particle lifetime example emits: " + string.Join(";",result.Diagnostics.Select(d=>d.Message)));
        var particle = graph.Nodes.Single(n=>n.Operation=="core.surfaceParticles");
        particle.Properties["sizeCurve"] = new JArray(new JArray(0,0),new JArray(.5,1),new JArray(1,0));
        particle.Properties["opacityCurve"] = new JArray(new JArray(0,1),new JArray(1,0));
        particle.Properties["colorCurve"] = new JArray(new JArray(0,1,0,0,1),new JArray(1,0,0,1,1));
        check(ShaderEmitter.Emit(GraphJson.Parse(GraphJson.Serialize(graph))).Succeeded,"lifetime curves survive roundtrip");
        particle.Properties["sizeCurve"] = new JArray(new JArray(.8,1),new JArray(.2,0));
        check(!GraphValidator.Validate(graph).IsValid,"out-of-order lifetime curve rejected");
        particle.Properties.Remove("sizeCurve");
        particle.Properties["colorCurve"] = new JArray(new JArray(0,1,0),new JArray(1,1,1));
        check(!GraphValidator.Validate(graph).IsValid,"malformed lifetime colors rejected");
        var invalidContext=GraphJson.Parse(File.ReadAllText(Path.Combine(directory,"Particle Lifetime.nxsg")));
        var root=invalidContext.Nodes.Single(n=>n.Operation=="core.surfaceParticles");
        var remap=invalidContext.Nodes.Single(n=>n.Id=="sizes");
        invalidContext.Connections.RemoveAll(c=>c.To.NodeId==root.Id&&c.To.PortId=="size");
        invalidContext.Connections.Add(new GraphConnection {Id="invalid-budget",From=new GraphPortRef{NodeId=remap.Id,PortId="value"},To=new GraphPortRef{NodeId=root.Id,PortId="emissionRate"}});
        check(!ShaderEmitter.Emit(invalidContext).Succeeded,"indirect particle info budget path rejected");
        invalidContext.Connections.RemoveAll(c=>c.Id=="invalid-budget");
        var baseSurface=invalidContext.Nodes.Single(n=>n.Id=="base");
        invalidContext.Connections.RemoveAll(c=>c.To.NodeId==baseSurface.Id&&c.To.PortId=="albedo");
        invalidContext.Connections.Add(new GraphConnection {Id="invalid-base",From=new GraphPortRef{NodeId="info",PortId="random"},To=new GraphPortRef{NodeId=baseSurface.Id,PortId="albedo"}});
        check(!ShaderEmitter.Emit(invalidContext).Succeeded,"particle info on base surface rejected");
        var audioGraph = GraphJson.Parse(File.ReadAllText(Path.Combine(directory,"Audio Hologram.nxsg")));
        var audio = audioGraph.Nodes.Single(n=>n.Operation=="core.audioLink");
        audio.Properties["rangeEnabled"] = 1; audio.Properties["min"] = .002; audio.Properties["max"] = .005;
        check(ShaderEmitter.Emit(audioGraph).Succeeded,"AudioLink range emits");
        audio.Properties["rangeEnabled"] = 2;
        check(!GraphValidator.Validate(audioGraph).IsValid,"AudioLink range mode validated");
        var textureGraph = TextureSetGraphBuilder.BuildMask("Assets/mask.png",true,.5f);
        var tex = textureGraph.Nodes.Single(n=>n.Operation=="core.texture2D");
        foreach(var edge in textureGraph.Connections.Where(c=>c.From.NodeId==tex.Id)) edge.From.PortId="alpha";
        var textureResult=ShaderEmitter.Emit(textureGraph);
        check(NodeCatalog.PortType(tex,"alpha")=="float" && textureResult.Succeeded,"texture alpha wires and emits: "+string.Join(";",textureResult.Diagnostics.Select(d=>d.Message)));
    }
}
