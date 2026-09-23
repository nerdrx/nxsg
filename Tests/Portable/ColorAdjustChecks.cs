using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class ColorAdjustChecks
{
    private static readonly string[] Controls = { "hue", "saturation", "lift", "gamma", "gain", "contrast", "exposure" };

    public static void Run(Action<bool, string> assert)
    {
        var adjust = NodeCatalog.Create("core.colorAdjust");
        assert(adjust != null && NodeCatalog.IsKnown("core.colorAdjust"), "Color Adjust is catalogued");
        assert(NodeCatalog.Ports("core.colorAdjust", false).SequenceEqual(new[] { "color", "hue", "saturation", "lift", "gamma", "gain", "contrast", "exposure" }), "Color Adjust inputs are catalogued");
        assert(NodeCatalog.Ports("core.colorAdjust", true).SequenceEqual(new[] { "color" }) && NodeCatalog.PortType(adjust, "color") == "color", "Color Adjust color output is catalogued");
        assert(Controls.All(key => adjust.Properties[key] != null), "Color Adjust neutral controls have defaults");

        var graph = Graph();
        var before = GraphJson.Serialize(graph);
        var roundTrip = GraphJson.Parse(before);
        assert(GraphJson.Serialize(roundTrip) == before && GraphValidator.Validate(roundTrip).IsValid, "Color Adjust round trips and validates");

        var nonFinite = Graph();
        nonFinite.Nodes.Single(n => n.Id == "adjust").Properties["hue"] = double.NaN;
        assert(!GraphValidator.Validate(nonFinite).IsValid, "Color Adjust rejects nonfinite controls");

        var neutral = ShaderEmitter.Emit(graph);
        var neutralBody = Body(neutral.ShaderSource);
        assert(neutral.Succeeded && neutralBody != null, "Neutral Color Adjust emits its function");
        assert(!ContainsColorMath(neutralBody), "Disconnected neutral controls emit no color operation math");

        foreach (var control in Controls)
        {
            var changed = Graph();
            changed.Nodes.Single(n => n.Id == "adjust").Properties[control] = control == "hue" ? .25 : control == "saturation" ? 1.4 : control == "lift" ? .08 : control == "gamma" ? 1.2 : control == "gain" ? 1.3 : control == "contrast" ? 1.25 : .5;
            var result = ShaderEmitter.Emit(changed);
            var body = Body(result.ShaderSource);
            var literal = ((double)changed.Nodes.Single(n => n.Id == "adjust").Properties[control]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            assert(result.Succeeded && body != null && body.Contains(literal), "Changed " + control + " emits its stage");
        }

        var modeGraph = Graph();
        var modeNode = modeGraph.Nodes.Single(n => n.Id == "adjust");
        modeNode.Properties["hueSpace"] = 1;
        assert(!ContainsColorMath(Body(ShaderEmitter.Emit(modeGraph).ShaderSource)), "Neutral OKLab emits no adjustment math");
        modeNode.Properties["hue"] = .25;
        assert(Body(ShaderEmitter.Emit(modeGraph).ShaderSource).Contains("NX_HueShiftOKLab(c,"), "OKLab is selected at generation time");
        assert(GraphJson.Serialize(GraphJson.Parse(GraphJson.Serialize(modeGraph))) == GraphJson.Serialize(modeGraph), "Hue space round trips");
        modeNode.Properties.Remove("hueSpace");
        assert(Body(ShaderEmitter.Emit(modeGraph).ShaderSource).Contains("NX_HueShift(c,"), "Legacy graphs keep HSV");
        modeNode.Properties["hueSpace"] = 2;
        assert(!GraphValidator.Validate(modeGraph).IsValid, "Unknown hue space is rejected");
        var connected = Graph();
        connected.Parameters.Add(new GraphParameter { Id = "hue", Name = "Hue", Type = GraphValueType.Float, Binding = GraphBindingKind.Material, DefaultValue = 0 });
        connected.Nodes.Add(new GraphNode { Id = "hue-source", Operation = "core.parameter", Properties = new JObject { ["parameterId"] = "hue" } });
        Link(connected, "hue-source", "value", "adjust", "hue", "material-hue");
        var material = ShaderEmitter.Emit(connected);
        assert(material.Succeeded && Body(material.ShaderSource).Contains("NX_HueShift(c,") && material.ShaderSource.Contains("_NXSG_P_hue"), "Connected material hue remains active at a neutral default");
    }

    private static ShaderGraph Graph()
    {
        var graph = new ShaderGraph { GraphId = "color-adjust-check" };
        graph.Nodes.Add(new GraphNode { Id = "color", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(.2, .4, .6, 1) } });
        var adjust = NodeCatalog.Create("core.colorAdjust"); adjust.Id = "adjust"; graph.Nodes.Add(adjust);
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Link(graph, "color", "value", "adjust", "color", "color-adjust");
        Link(graph, "adjust", "color", "surface", "albedo", "adjust-surface");
        Link(graph, "surface", "surface", "output", "surface", "surface-output");
        return graph;
    }

    private static string Body(string source)
    {
        if (source == null) return null;
        var start = source.IndexOf("float4 c=", StringComparison.Ordinal);
        var end = start < 0 ? -1 : source.IndexOf("return c;", start, StringComparison.Ordinal);
        return start < 0 || end < 0 ? null : source.Substring(start, end + "return c;".Length - start);
    }

    private static bool ContainsColorMath(string body)
    {
        return body != null && new[] { "c.rgb", "NX_HueShift(", "NX_HueShiftOKLab(" }.Any(body.Contains);
    }

    private static void Link(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}
