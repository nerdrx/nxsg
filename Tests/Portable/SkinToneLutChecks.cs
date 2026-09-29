using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;

internal static class SkinToneLutChecks
{
    public static void Run(Action<bool, string> assert, string fixtures)
    {
        var samplePath = Path.GetFullPath(Path.Combine(fixtures, "../../Packages/dev.nerdrx.nxsg/Samples~/Skin Tone LUT.nxsg"));
        var graph = GraphJson.Parse(File.ReadAllText(samplePath));
        assert(GraphValidator.Validate(graph).IsValid, "unassigned Skin Tone LUT sample validates");
        var neutral = ShaderEmitter.Emit(graph);
        assert(neutral.Succeeded, "unassigned Skin Tone LUT emits");
        assert(Calls(neutral.ShaderSource) == 1, "unassigned LUT uses base color without a lookup call");

        graph.Adapter = new JObject { ["textures"] = new JObject { ["skinLut"] = "test-asset-guid" } };
        var assigned = ShaderEmitter.Emit(graph);
        assert(assigned.Succeeded, "assigned Skin Tone LUT emits");
        assert(Calls(assigned.ShaderSource) > 1, "assigned LUT samples the 2D table");
        assert(assigned.ShaderSource.Contains("texelSize.xy*.5") && assigned.ShaderSource.Contains("baseColor.a"),
            "LUT samples texel centers and preserves source alpha");

        var node = graph.Nodes.Single(n => n.Operation == SkinToneLutNodes.Operation);
        node.Properties["pigment"] = 1.1;
        assert(!GraphValidator.Validate(graph).IsValid, "Skin Tone LUT rejects pigment outside 0–1");
        node.Properties["pigment"] = .5;
        node.Properties["resourceId"] = 42;
        assert(!GraphValidator.Validate(graph).IsValid, "Skin Tone LUT rejects non-string resource ID");
    }

    static int Calls(string source) => source.Split(new[] { "NXSG_SkinToneLut(" }, StringSplitOptions.None).Length - 1;
}
