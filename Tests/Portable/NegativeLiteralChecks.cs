using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Backend;

public static class NegativeLiteralChecks
{
    public static IEnumerable<ShaderGraph> Fixtures()
    {
        foreach(var c in new[]{new[]{"remap","inMin"},new[]{"remap","outMin"},new[]{"smoothstep","low"},new[]{"heightMask","low"},new[]{"slopeMask","low"},new[]{"distanceFade","near"},new[]{"sphereMask","radius"}})
        {
            var g=Base(c[0]+"-"+c[1]);var n=NodeCatalog.Create("core."+c[0]);n.Id="signal";n.Properties[c[1]]=-1;g.Nodes.Add(n);Link(g,"signal","value","surface","albedo");yield return g;
        }
        var parallax=Base("negative-parallax");var uv=NodeCatalog.Create("core.parallaxUV");uv.Id="uv";uv.Properties["reference"]=-1;parallax.Nodes.Add(uv);var checker=NodeCatalog.Create("core.checker");checker.Id="checker";parallax.Nodes.Add(checker);Link(parallax,"uv","uv","checker","uv");Link(parallax,"checker","color","surface","albedo");yield return parallax;
        var tess=Base("negative-tessellation");var t=NodeCatalog.Create("core.tessellation");t.Id="tess";t.Properties["reference"]=-1;tess.Nodes.Add(t);tess.Connections.Clear();Link(tess,"surface","surface","tess","base");Link(tess,"tess","surface","output","surface");yield return tess;
        var fur=Base("negative-fur");var f=NodeCatalog.Create("core.fur");f.Id="fur";f.Properties["gravity"]=-1;f.Properties["windSpeed"]=-1;f.Properties["lodNear"]=-1;f.Properties["fins"]=1;fur.Nodes.Add(f);fur.Connections.Clear();Link(fur,"surface","surface","fur","base");Link(fur,"fur","surface","output","surface");yield return fur;
        var parameter=Base("negative-property");parameter.Parameters.Add(new GraphParameter{Id="negative",Name="Negative",Type=GraphValueType.Float,Binding=GraphBindingKind.Material,DefaultValue=new JValue(-.5),Exposed=true});yield return parameter;
    }
    static ShaderGraph Base(string id)
    {
        var g=new ShaderGraph{GraphId=id};var s=NodeCatalog.Create("core.unlitSurface");s.Id="surface";g.Nodes.Add(s);var o=NodeCatalog.Create("core.output");o.Id="output";g.Nodes.Add(o);Link(g,"surface","surface","output","surface");return g;
    }
    static void Link(ShaderGraph g,string a,string p,string b,string q){g.Connections.Add(new GraphConnection{Id=a+b+q,From=new GraphPortRef{NodeId=a,PortId=p},To=new GraphPortRef{NodeId=b,PortId=q}});}
    public static void Run(Action<bool,string> assert)
    {
        foreach(var g in Fixtures())
        {
            var e=ShaderEmitter.Emit(g);assert(e.Succeeded,g.GraphId+" emits: "+string.Join(";",e.Diagnostics.Select(d=>d.Message)));
            if(!e.Succeeded)continue;
            assert(!Regex.IsMatch(e.ShaderSource,@"--(?=[0-9.])"),g.GraphId+" has no merged negative-number token");
            if(g.GraphId=="negative-property")assert(e.ShaderSource.Contains("Float) = -0.5"),"ShaderLab float defaults stay bare");
        }
    }
}
