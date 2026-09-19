using System;
using System.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class MotionChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var response = ResponseGraph();
        var responseResult = ShaderEmitter.Emit(response);
        assert(GraphValidator.Validate(response).IsValid && responseResult.Succeeded,
            "avatar motion response graph validates and emits");
        assert(responseResult.ShaderSource.Contains("_NXSG_MotionSpeed"),
            "avatar motion response keeps the runtime speed uniform");
        assert(responseResult.ShaderSource.Contains("max(0,_NXSG_MotionSpeed)"),
            "avatar motion speed is not folded into a literal");
        assert(responseResult.Properties.Any(p => p.Name == "_NXSG_MotionSpeed"),
            "avatar motion graph declares the speed material property");

        var stretch = StretchGraph();
        var stretchResult = ShaderEmitter.Emit(stretch);
        assert(GraphValidator.Validate(stretch).IsValid && stretchResult.Succeeded,
            "avatar motion stretch graph validates and emits");
        assert(stretchResult.ShaderSource.Contains("_NXSG_MotionSpeed"),
            "motion stretch keeps the runtime speed uniform");

        var sway = SwayGraph();
        var swayResult = ShaderEmitter.Emit(sway);
        assert(GraphValidator.Validate(sway).IsValid && swayResult.Succeeded,
            "avatar motion sway graph validates and emits");
        assert(swayResult.ShaderSource.Contains("_NXSG_MotionSpeed") && swayResult.ShaderSource.Contains("ShadowCaster"),
            "motion sway emits a runtime input and shadow pass");

        var invalidResponse = ResponseGraph();
        invalidResponse.Nodes.Single(n => n.Id == "response").Properties["fullSpeed"] = .1;
        assert(!GraphValidator.Validate(invalidResponse).IsValid,
            "motion response rejects full speed at or below start speed");

        var invalidStretch = StretchGraph();
        invalidStretch.Nodes.Single(n => n.Id == "stretch").Properties["axis"] = 2;
        assert(!GraphValidator.Validate(invalidStretch).IsValid,
            "motion stretch rejects an invalid axis");
    }

    internal static ShaderGraph ResponseGraph()
    {
        var graph = Base("motion-response");
        graph.Nodes.Add(Node("motion", "core.avatarMotion"));
        graph.Nodes.Add(Node("response", "core.motionResponse"));
        Link(graph, "motion", "speed", "response", "speed");
        Link(graph, "response", "value", "surface", "albedo");
        return graph;
    }

    internal static ShaderGraph StretchGraph()
    {
        var graph = Base("motion-stretch");
        graph.Resources.Add(new GraphResource { Id = "texture", Kind = "texture2D", Uri = "builtin://white" });
        graph.Nodes.Add(Node("uv", "core.uv0"));
        graph.Nodes.Add(Node("motion", "core.avatarMotion"));
        graph.Nodes.Add(Node("stretch", "core.motionStretchUV"));
        graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new Newtonsoft.Json.Linq.JObject { ["resourceId"] = "texture" } });
        Link(graph, "uv", "uv", "stretch", "uv");
        Link(graph, "motion", "speed", "stretch", "speed");
        Link(graph, "stretch", "uv", "texture", "uv");
        Link(graph, "texture", "color", "surface", "albedo");
        return graph;
    }

    internal static ShaderGraph SwayGraph()
    {
        var graph = Base("motion-sway");
        graph.Nodes.Add(Node("motion", "core.avatarMotion"));
        graph.Nodes.Add(Node("sway", "core.motionSway"));
        Link(graph, "motion", "speed", "sway", "speed");
        Link(graph, "sway", "value", "surface", "displacement");
        return graph;
    }

    static ShaderGraph Base(string id)
    {
        var graph = new ShaderGraph { GraphId = id };
        graph.Nodes.Add(Node("surface", "core.unlitSurface"));
        graph.Nodes.Add(Node("output", "core.output"));
        Link(graph, "surface", "surface", "output", "surface");
        return graph;
    }

    static GraphNode Node(string id, string operation)
    { var node = NodeCatalog.Create(operation); node.Id = id; return node; }

    static void Link(ShaderGraph graph, string from, string port, string to, string input)
    { graph.Connections.Add(new GraphConnection { Id = from + "-" + to + "-" + input, From = new GraphPortRef { NodeId = from, PortId = port }, To = new GraphPortRef { NodeId = to, PortId = input } }); }
}
