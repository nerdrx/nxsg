using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class MalformedReferenceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var badValues = new JToken[] { new JObject { ["bad"] = 1 }, new JArray(1), new JValue(true), new JValue(1), JValue.CreateNull(), new JValue("999"), new JValue("0") };
        foreach (var value in badValues)
        {
            CheckBadProperty(check, "core.parameter", "parameterId", value, "parameter.missing");
            CheckBadProperty(check, "core.texture2D", "resourceId", value, "resource.missing");
            CheckBadProperty(check, "core.sticker", "resourceId", value, "resource.missing");
            CheckBadProperty(check, "core.constant", "valueType", value, "type.invalid");
            CheckBadProperty(check, "core.multiply", "valueType", value, "type.invalid");
        }
        CheckBadProperty(check, "core.parameter", "parameterId", null, "parameter.missing");
        CheckBadProperty(check, "core.texture2D", "resourceId", null, "resource.missing");
        CheckBadProperty(check, "core.sticker", "resourceId", null, "resource.missing");
        CheckLegacyMissingTypes(check);

        var oldConstant = SurfaceGraph();
        var constant = NodeCatalog.Create("core.constant"); constant.Id = "constant"; constant.Properties.Remove("valueType");
        oldConstant.Nodes.Add(constant); Link(oldConstant, "constant", "value", "surface", "albedo");
        check(GraphValidator.Validate(oldConstant).IsValid && ShaderEmitter.Emit(oldConstant).Succeeded,
            "legacy constant without valueType still validates and emits");

        var oldGraph = GraphSamples.CreateDefault();
        check(GraphValidator.Validate(oldGraph).IsValid && ShaderEmitter.Emit(oldGraph).Succeeded,
            "legacy default graph still validates and emits");

        var parameterGraph = SurfaceGraph();
        parameterGraph.Parameters.Add(new GraphParameter { Id = "rough", Name = "Roughness", Type = GraphValueType.Float,
            Binding = GraphBindingKind.Material, DefaultValue = new JValue(.5) });
        var parameter = NodeCatalog.Create("core.parameter"); parameter.Id = "parameter"; parameter.Properties["parameterId"] = "rough";
        parameterGraph.Nodes.Add(parameter); Link(parameterGraph, "parameter", "value", "surface", "roughness");
        check(GraphValidator.Validate(parameterGraph).IsValid && ShaderEmitter.Emit(parameterGraph).Succeeded,
            "valid parameter references still validate and emit");

        var textureGraph = SurfaceGraph();
        textureGraph.Resources.Add(new GraphResource { Id = "albedo", Kind = "texture2D", Uri = "builtin://white" });
        var texture = NodeCatalog.Create("core.texture2D"); texture.Id = "texture"; texture.Properties["resourceId"] = "albedo";
        textureGraph.Nodes.Add(texture); Link(textureGraph, "texture", "color", "surface", "albedo");
        check(GraphValidator.Validate(textureGraph).IsValid && ShaderEmitter.Emit(textureGraph).Succeeded,
            "valid texture references still validate and emit");
    }

    private static void CheckLegacyMissingTypes(Action<bool, string> check)
    {
        foreach (var operation in new[] { "core.constant", "core.multiply" })
        {
            var graph = SurfaceGraph();
            var node = NodeCatalog.Create(operation); node.Id = "legacy"; node.Properties.Remove("valueType"); graph.Nodes.Add(node);
            var validation = GraphValidator.Validate(graph);
            check(validation.IsValid && ShaderEmitter.Emit(graph).Succeeded, operation + " missing valueType stays valid for old graphs");
            check(GraphTypes.PortType(graph, node, "value") == "color", operation + " missing valueType keeps legacy color inference");
        }
    }

    private static void CheckBadProperty(Action<bool, string> check, string operation, string property, JToken value, string diagnostic)
    {
        var graph = SurfaceGraph();
        var node = NodeCatalog.Create(operation); node.Id = "reference";
        if (value == null) node.Properties.Remove(property);
        else node.Properties[property] = value.DeepClone();
        graph.Nodes.Add(node);
        if (operation == "core.parameter")
        {
            graph.Parameters.Add(new GraphParameter { Id = "p", Name = "P", Type = GraphValueType.Float,
                Binding = GraphBindingKind.Material, DefaultValue = new JValue(.5) });
            Link(graph, "reference", "value", "surface", "roughness");
        }
        else if (operation == "core.texture2D") Link(graph, "reference", "color", "surface", "albedo");
        var before = GraphJson.Serialize(graph);
        var validation = GraphValidator.Validate(graph);
        check(!validation.IsValid && validation.Diagnostics.Any(item => item.Code == diagnostic),
            operation + " malformed " + property + " reports " + diagnostic);
        check(!ShaderEmitter.Emit(graph).Succeeded, operation + " malformed " + property + " blocks emission");
        check(before == GraphJson.Serialize(graph), operation + " malformed " + property + " is preserved");
        if (operation == "core.parameter")
            check(GraphTypes.PortType(graph, node, "value") == null, "malformed parameter reference has no inferred type");
        if (operation == "core.constant" || operation == "core.multiply")
        {
            check(GraphTypes.PortType(graph, node, operation == "core.constant" ? "value" : "value") == null,
                operation + " malformed valueType has no inferred type");
            check(NodeCatalog.PortType(node, "value") == null, operation + " malformed valueType has no catalog port type");
        }
    }

    private static ShaderGraph SurfaceGraph()
    {
        var graph = new ShaderGraph { GraphId = "malformed-reference" };
        var surface = NodeCatalog.Create("core.pbrSurface"); surface.Id = "surface"; graph.Nodes.Add(surface);
        var output = NodeCatalog.Create("core.output"); output.Id = "output"; graph.Nodes.Add(output);
        Link(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    private static void Link(ShaderGraph graph, string from, string fromPort, string to, string toPort)
    {
        graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + toPort,
            From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } });
    }
}
