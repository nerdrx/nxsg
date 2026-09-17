using Newtonsoft.Json.Linq;

namespace NXSG.Core
{
    public static class GraphSamples
    {
        public static ShaderGraph CreateDefault()
        {
            var graph = new ShaderGraph
            {
                GraphId = "graph-main",
                Layout = new GraphLayout()
            };
            graph.Nodes.Add(new GraphNode
            {
                Id = "uv0",
                Operation = "core.uv0"
            });
            graph.Nodes.Add(new GraphNode
            {
                Id = "texture",
                Operation = "core.texture2D",
                Properties = new JObject { ["resourceId"] = "white" }
            });
            graph.Nodes.Add(new GraphNode
            {
                Id = "toon",
                Operation = "core.toonSurface"
            });
            graph.Nodes.Add(new GraphNode
            {
                Id = "output",
                Operation = "core.output"
            });
            graph.Connections.Add(new GraphConnection
            {
                Id = "edge-uv",
                From = new GraphPortRef { NodeId = "uv0", PortId = "uv" },
                To = new GraphPortRef { NodeId = "texture", PortId = "uv" }
            });
            graph.Connections.Add(new GraphConnection
            {
                Id = "edge-color",
                From = new GraphPortRef { NodeId = "texture", PortId = "color" },
                To = new GraphPortRef { NodeId = "toon", PortId = "albedo" }
            });
            graph.Connections.Add(new GraphConnection
            {
                Id = "edge-surface",
                From = new GraphPortRef { NodeId = "toon", PortId = "surface" },
                To = new GraphPortRef { NodeId = "output", PortId = "surface" }
            });
            graph.Resources.Add(new GraphResource
            {
                Id = "white",
                Kind = "texture2D",
                Uri = "builtin://white"
            });
            graph.Layout.Nodes = new System.Collections.Generic.Dictionary<string, GraphNodeLayout>
            {
                ["uv0"] = new GraphNodeLayout { X = 0, Y = 80 },
                ["texture"] = new GraphNodeLayout { X = 180, Y = 80 },
                ["toon"] = new GraphNodeLayout { X = 380, Y = 80 },
                ["output"] = new GraphNodeLayout { X = 620, Y = 80 }
            };
            return graph;
        }
    }
}
