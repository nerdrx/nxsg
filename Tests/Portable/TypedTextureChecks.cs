using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

public static class TypedTextureChecks
{
    public static void Run(System.Action<bool, string> assert)
    {
        Check("core.cubemap", "cubemap", GraphValueType.Cubemap, assert);
        Check("core.textureArray", "texture2DArray", GraphValueType.Texture2DArray, assert);

        var wrongCubeKind = Graph("core.cubemap", "texture2DArray");
        assert(!GraphValidator.Validate(wrongCubeKind).IsValid, "Cubemap node rejects Texture2DArray resource kind");
        assert(!ShaderEmitter.Emit(wrongCubeKind).Succeeded, "Cubemap emitter rejects Texture2DArray resource kind");
        var wrongArrayKind = Graph("core.textureArray", "cubemap");
        assert(!GraphValidator.Validate(wrongArrayKind).IsValid, "Texture Array node rejects Cubemap resource kind");
        assert(!ShaderEmitter.Emit(wrongArrayKind).Succeeded, "Texture Array emitter rejects Cubemap resource kind");
    }

    static void Check(string operation, string kind, GraphValueType type, System.Action<bool, string> assert)
    {
        var graph = Graph(operation, kind);
        var serialized = GraphJson.Serialize(graph);
        var restored = GraphJson.Parse(serialized);
        assert(GraphValidator.Validate(restored).IsValid, operation + " resource round-trips and validates");
        assert((string)restored.Adapter["textures"]["source"] == "unity-asset-guid", operation + " asset binding round-trips");

        var result = ShaderEmitter.Emit(restored);
        assert(result.Succeeded, operation + " emits from typed resource");
        var binding = result.Properties.Single(p => p.ResourceId == "source");
        assert(binding.Type == type && binding.ResourceUri == "project://textures/source.asset", operation + " material metadata preserves dimension");
        if (type == GraphValueType.Cubemap)
            assert(result.ShaderSource.Contains("\", Cube) =") && result.ShaderSource.Contains("samplerCUBE " + binding.Name) && result.ShaderSource.Contains("texCUBElod(" + binding.Name), "Cubemap emits cube property, sampler and sample");
        else
        {
            assert(result.ShaderSource.Contains("\", 2DArray) =") && result.ShaderSource.Contains("UNITY_DECLARE_TEX2DARRAY(" + binding.Name + ")") && result.ShaderSource.Contains("UNITY_SAMPLE_TEX2DARRAY_LOD(" + binding.Name), "Texture Array emits typed property and explicit LOD sample");
            assert(result.ShaderSource.Contains(binding.Name + "_Layers (\"\", Float) = 1") && result.ShaderSource.Contains(binding.Name + "_Layers-1))") && !result.ShaderSource.Contains(".GetDimensions("), "Texture Array clamps from the editor-assigned asset layer count");
        }
        assert(result.ShaderSource.Contains("#pragma target 4.5"), operation + " uses the advanced stereo-capable shader target");

        if (type == GraphValueType.Texture2DArray)
        {
            var vertexGraph = Graph(operation, kind, true);
            var vertex = ShaderEmitter.Emit(vertexGraph);
            assert(vertex.Succeeded && vertex.ShaderSource.Contains("UNITY_SAMPLE_TEX2DARRAY_LOD("), "Texture Array explicit LOD sample is valid for vertex displacement");
        }
    }

    static ShaderGraph Graph(string operation, string kind, bool vertex = false)
    {
        var graph = new ShaderGraph { GraphId = "typed-texture-" + operation };
        graph.Resources.Add(new GraphResource { Id = "source", Name = "Test Resource", Kind = kind, Uri = "project://textures/source.asset" });
        graph.Adapter = new JObject { ["textures"] = new JObject { ["source"] = "unity-asset-guid" } };

        var sample = NodeCatalog.Create(operation); sample.Id = "sample"; sample.Properties["resourceId"] = "source";
        var surface = NodeCatalog.Create("core.unlitSurface"); surface.Id = "surface";
        var output = NodeCatalog.Create("core.output"); output.Id = "output";
        graph.Nodes.Add(sample); graph.Nodes.Add(surface); graph.Nodes.Add(output);

        graph.Connections.Add(new GraphConnection
        {
            Id = "sample-to-surface",
            From = new GraphPortRef { NodeId = "sample", PortId = vertex ? "alpha" : "color" },
            To = new GraphPortRef { NodeId = "surface", PortId = vertex ? "displacement" : "albedo" }
        });
        graph.Connections.Add(new GraphConnection
        {
            Id = "surface-to-output",
            From = new GraphPortRef { NodeId = "surface", PortId = "surface" },
            To = new GraphPortRef { NodeId = "output", PortId = "surface" }
        });
        return graph;
    }
}
