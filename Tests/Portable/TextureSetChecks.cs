using System;
using System.Linq;
using NXSG.Core;

public static class TextureSetChecks
{
    public static void Run(Action<bool, string> assert)
    {
        var found = TextureSetMatcher.Suggest(new[] { "Fox_BaseColor.png", "Fox_Normal.png", "Fox_Roughness.png", "Fox_metal.png" });
        assert(found[TextureSetSlot.Albedo].Path == "Fox_BaseColor.png", "texture matcher base color");
        assert(found[TextureSetSlot.Normal].Path == "Fox_Normal.png", "texture matcher normal");
        assert(found[TextureSetSlot.Roughness].Path == "Fox_Roughness.png", "texture matcher roughness");
        var graph = TextureSetGraphBuilder.Build(new System.Collections.Generic.Dictionary<TextureSetSlot, string>
        {
            { TextureSetSlot.Albedo, "Assets/Fox_BaseColor.png" },
            { TextureSetSlot.Normal, "Assets/Fox_Normal.png" },
            { TextureSetSlot.Roughness, "Assets/Fox_Roughness.png" },
            { TextureSetSlot.Metallic, "Assets/Fox_metal.png" }
        });
        assert(GraphValidator.Validate(graph).IsValid, "texture set graph validates");
        assert(graph.Connections.Any(c => c.From.PortId == "color" && c.To.PortId == "color") &&
            graph.Connections.Any(c => c.From.PortId == "r" && c.To.PortId == "roughness"), "texture set graph uses color and red channel contracts");
        assert(GraphValidator.Validate(TextureSetGraphBuilder.BuildMask("Assets/mask.png", true, .5f)).IsValid, "mask helper validates");
        var all = TextureSetGraphBuilder.Build(new System.Collections.Generic.Dictionary<TextureSetSlot, string>
        {
            { TextureSetSlot.Albedo, "Assets/albedo.png" }, { TextureSetSlot.Normal, "Assets/normal.png" },
            { TextureSetSlot.Roughness, "Assets/rough.png" }, { TextureSetSlot.Metallic, "Assets/metal.png" },
            { TextureSetSlot.AmbientOcclusion, "Assets/ao.png" }, { TextureSetSlot.Height, "Assets/height.png" },
            { TextureSetSlot.Mask, "Assets/mask.png" }, { TextureSetSlot.Flow, "Assets/flow.png" }
        }, false);
        assert(GraphValidator.Validate(all).IsValid && NXSG.Backend.ShaderEmitter.Emit(all).Succeeded, "toon texture set graph validates and emits");
        var flow = TextureSetGraphBuilder.BuildFlow("Assets/flow.png", .2f);
        assert(GraphValidator.Validate(flow).IsValid && NXSG.Backend.ShaderEmitter.Emit(flow).Succeeded && flow.Connections.Any(c => c.To.PortId == "flow"), "flow helper validates, emits and wires flow color");
    }
}
