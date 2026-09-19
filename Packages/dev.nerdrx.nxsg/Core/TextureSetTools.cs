using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NXSG.Core
{
    public enum TextureSetSlot { Albedo, Normal, Roughness, Metallic, AmbientOcclusion, Height, Mask, Flow }

    public sealed class TextureSetSuggestion
    {
        public TextureSetSlot Slot;
        public string Path;
        public int Score;
    }

    /// <summary>Pure filename matching. It never opens or reimports an asset.</summary>
    public static class TextureSetMatcher
    {
        static readonly Dictionary<TextureSetSlot, string[][]> Terms = new Dictionary<TextureSetSlot, string[][]>
        {
            { TextureSetSlot.Albedo, new[] { new[] { "albedo" }, new[] { "base", "color" }, new[] { "basecolor" }, new[] { "diffuse" }, new[] { "color" } } },
            { TextureSetSlot.Normal, new[] { new[] { "normal" }, new[] { "norm" }, new[] { "nrm" } } },
            { TextureSetSlot.Roughness, new[] { new[] { "roughness" }, new[] { "rough" }, new[] { "rgh" } } },
            { TextureSetSlot.Metallic, new[] { new[] { "metallic" }, new[] { "metalness" }, new[] { "metal" } } },
            { TextureSetSlot.AmbientOcclusion, new[] { new[] { "ambient", "occlusion" }, new[] { "occlusion" }, new[] { "ao" } } },
            { TextureSetSlot.Height, new[] { new[] { "height" }, new[] { "displacement" }, new[] { "disp" } } },
            { TextureSetSlot.Mask, new[] { new[] { "mask" }, new[] { "opacity" }, new[] { "alpha" } } },
            { TextureSetSlot.Flow, new[] { new[] { "flow" }, new[] { "velocity" }, new[] { "direction" } } }
        };

        public static IReadOnlyDictionary<TextureSetSlot, TextureSetSuggestion> Suggest(IEnumerable<string> paths)
        {
            var result = new Dictionary<TextureSetSlot, TextureSetSuggestion>();
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                var tokens = Tokenize(Path.GetFileNameWithoutExtension(path));
                foreach (var pair in Terms)
                {
                    var score = Score(tokens, pair.Value);
                    if (score <= 0 || (result.ContainsKey(pair.Key) && result[pair.Key].Score >= score)) continue;
                    result[pair.Key] = new TextureSetSuggestion { Slot = pair.Key, Path = path, Score = score };
                }
            }
            return result;
        }

        static string[] Tokenize(string value)
        {
            return (value ?? string.Empty).ToLowerInvariant().Replace('-', '_').Replace('.', '_').Split('_');
        }

        static int Score(string[] tokens, string[][] alternatives)
        {
            var score = 0;
            foreach (var term in alternatives)
            {
                if (!term.All(t => tokens.Contains(t))) continue;
                score = Math.Max(score, term.Length * 10 + (term.Length == 1 ? 1 : 5));
            }
            return score;
        }
    }

    /// <summary>Builds portable graph fragments from reviewed texture assignments.</summary>
    public static class TextureSetGraphBuilder
    {
        public static ShaderGraph Build(IReadOnlyDictionary<TextureSetSlot, string> assignments, bool pbr = true)
        {
            var graph = new ShaderGraph { GraphId = Guid.NewGuid().ToString("N") };
            var uv = Add(graph, "core.uv0", "uv");
            var surface = Add(graph, pbr ? "core.pbrSurface" : "core.toonSurface", "surface");
            var output = Add(graph, "core.output", "output");
            Wire(graph, surface, "surface", output, "surface");
            var sampled = new List<GraphNode>();
            GraphNode flowTexture = null;
            foreach (var pair in (assignments ?? new Dictionary<TextureSetSlot, string>()).OrderBy(p=>p.Key))
            {
                if (string.IsNullOrEmpty(pair.Value)) continue;
                var texture = AddTexture(graph, pair.Value);
                if (pair.Key == TextureSetSlot.Flow) { flowTexture = texture; continue; }
                sampled.Add(texture);
                Wire(graph, uv, "uv", texture, "uv");
                switch (pair.Key)
                {
                    case TextureSetSlot.Albedo: Wire(graph, texture, "color", surface, "albedo"); break;
                    case TextureSetSlot.Normal:
                        { var normal = Add(graph, "core.normalMap", "normal"); Wire(graph, texture, "color", normal, "color"); Wire(graph, normal, "normal", surface, "normal"); } break;
                    case TextureSetSlot.Roughness: if (pbr) WireChannel(graph, texture, surface, "roughness"); break;
                    case TextureSetSlot.Metallic: if (pbr) WireChannel(graph, texture, surface, "metallic"); break;
                    case TextureSetSlot.AmbientOcclusion: WireColorMask(graph, texture, surface, "albedo", sampled); break;
                    case TextureSetSlot.Height:
                        var height = Add(graph,"core.multiply","heightStrength");
                        var amount = Add(graph,"core.value","heightAmount");amount.Properties["value"]=.02;
                        WireChannel(graph,texture,height,"a");Wire(graph,amount,"value",height,"b");Wire(graph,height,"value",surface,"displacement");break;
                    case TextureSetSlot.Mask: WireChannel(graph, texture, surface, "opacity"); break;
                }
            }
            if (flowTexture != null)
            {
                var distort = Add(graph, "core.uvDistort", "flow"); distort.Properties["strength"] = .1;distort.Properties["mode"]=4;distort.Properties["speed"]=0;
                Wire(graph, uv, "uv", flowTexture, "uv"); Wire(graph, uv, "uv", distort, "uv"); Wire(graph, flowTexture, "color", distort, "flow");
                foreach (var texture in sampled) graph.Connections.RemoveAll(c => c.To.NodeId == texture.Id && c.To.PortId == "uv");
                foreach (var texture in sampled) Wire(graph, distort, "uv", texture, "uv");
            }
            EnsureLayout(graph);
            return graph;
        }

        public static ShaderGraph BuildMask(string path, bool invert, float strength, string channel = "r")
        {
            var graph = Build(new Dictionary<TextureSetSlot, string> { { TextureSetSlot.Albedo, path } }, false);
            var texture = graph.Nodes.First(n => n.Operation == "core.texture2D");
            var surface = graph.Nodes.First(n => n.Operation == "core.toonSurface");
            graph.Connections.RemoveAll(c => c.To.NodeId == surface.Id && c.To.PortId == "albedo");
            var split = Add(graph, "core.splitColor", "split"); Wire(graph, texture, "color", split, "color");
            channel = channel == "g" || channel == "b" || channel == "a" ? channel : "r";
            GraphNode value = split;
            string valuePort = channel;
            if (invert)
            {
                var one = Add(graph, "core.constant", "one"); one.Properties["valueType"] = "float"; one.Properties["value"] = 1.0;
                var subtract = Add(graph, "core.subtract", "invert"); subtract.Properties["valueType"] = "float";
                Wire(graph, one, "value", subtract, "a"); Wire(graph, split, channel, subtract, "b"); value = subtract; valuePort = "value";
            }
            if (strength != 1)
            {
                var multiply = Add(graph, "core.multiply", "strength"); multiply.Properties["valueType"] = "float";
                var amount = Add(graph, "core.constant", "amount"); amount.Properties["valueType"] = "float"; amount.Properties["value"] = strength;
                Wire(graph, value, valuePort, multiply, "a"); Wire(graph, amount, "value", multiply, "b"); value = multiply; valuePort = "value";
            }
            Wire(graph, value, valuePort, surface, "opacity");
            EnsureLayout(graph); return graph;
        }

        public static ShaderGraph BuildFlow(string path, float strength)
        {
            var graph = Build(new Dictionary<TextureSetSlot, string> { { TextureSetSlot.Flow, path } }, false);
            var distort = graph.Nodes.First(n => n.Operation == "core.uvDistort"); distort.Properties["strength"] = strength;
            var checker=Add(graph,"core.checker","flowPreview");Wire(graph,distort,"uv",checker,"uv");Wire(graph,checker,"color",graph.Nodes.First(n=>n.Operation=="core.toonSurface"),"albedo");
            EnsureLayout(graph); return graph;
        }

        static GraphNode AddTexture(ShaderGraph graph, string path)
        {
            var node = Add(graph, "core.texture2D", "texture");
            var resource = new GraphResource { Id = "texture-" + node.Id, Kind = "texture2D", Uri = "project://" + path.Replace('\\', '/') };
            graph.Resources.Add(resource); node.Properties["resourceId"] = resource.Id; return node;
        }
        static GraphNode Add(ShaderGraph graph, string operation, string hint)
        {
            var node = NodeCatalog.Create(operation); node.Id = hint + "-" + graph.Nodes.Count; graph.Nodes.Add(node); return node;
        }
        static void WireChannel(ShaderGraph graph, GraphNode texture, GraphNode target, string port)
        {
            var split = Add(graph, "core.splitColor", "split"); Wire(graph, texture, "color", split, "color"); Wire(graph, split, "r", target, port);
        }
        static void WireColorMask(ShaderGraph graph, GraphNode texture, GraphNode target, string port, List<GraphNode> sampled)
        {
            var split = Add(graph, "core.splitColor", "ao"); var multiply = Add(graph, "core.multiply", "aoMultiply");
            multiply.Properties["valueType"] = "color"; Wire(graph, texture, "color", split, "color");
            var baseTexture = sampled.FirstOrDefault(n => n != texture && graph.Connections.Any(c => c.From.NodeId == n.Id && c.To.NodeId == target.Id && c.To.PortId == port));
            if (baseTexture == null) { Wire(graph,split,"r",target,port); return; }
            graph.Connections.RemoveAll(c => c.From.NodeId == baseTexture.Id && c.To.NodeId == target.Id && c.To.PortId == port);
            Wire(graph, baseTexture, "color", multiply, "a"); Wire(graph, split, "r", multiply, "b"); Wire(graph, multiply, "value", target, port);
        }
        static void EnsureLayout(ShaderGraph graph)
        {
            graph.Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() };
            var levels = new Dictionary<string,int>();
            Func<string,int> depth=null; depth=id=> { if(levels.TryGetValue(id,out var known))return known; var parents=graph.Connections.Where(e=>e.To.NodeId==id).Select(e=>e.From.NodeId).ToArray();return levels[id]=parents.Length==0?0:parents.Max(p=>depth(p))+1; };
            var rows = new Dictionary<int,int>();
            foreach(var node in graph.Nodes){var level=depth(node.Id);rows.TryGetValue(level,out var row);rows[level]=row+1;graph.Layout.Nodes[node.Id]=new GraphNodeLayout{X=level*240,Y=80+row*220};}
        }
        static void Wire(ShaderGraph graph, GraphNode from, string fromPort, GraphNode to, string toPort)
        {
            graph.Connections.Add(new GraphConnection { Id = Guid.NewGuid().ToString("N"), From = new GraphPortRef { NodeId = from.Id, PortId = fromPort }, To = new GraphPortRef { NodeId = to.Id, PortId = toPort } });
        }
    }
}
