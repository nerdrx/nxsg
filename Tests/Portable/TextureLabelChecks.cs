using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
public static class TextureLabelChecks
{
    public static void Run(Action<bool,string> assert)
    {
        CheckLinearOrdering(assert);
        var ordered=GraphSamples.CreateDefault();var id=ordered.Resources[0].Id;
        ordered.Resources.Add(new GraphResource{Id="aaa",Kind="texture2D",Uri="builtin://white"});
        var label=TextureSlotLabels.DisplayName(ordered,id);
        assert(label==TextureSlotLabels.DisplayName(GraphJson.Parse(GraphJson.Serialize(ordered)),id),"Slot numbering survives canonical resource reordering");
        foreach(bool advanced in new[]{false,true})
        {
            var g=GraphSamples.CreateDefault();if(advanced)g.Nodes.Single(n=>n.Id=="toon").Properties["opacity"]=1;
            var before=ShaderEmitter.Emit(g);var binding=before.Properties.Single(p=>p.Type==GraphValueType.Texture2D);
            var hash=GraphJson.ComputeSemanticHash(g);g.Resources[0].Name="Body albedo";
            var copy=GraphJson.Parse(GraphJson.Serialize(g));var after=ShaderEmitter.Emit(copy);
            assert(after.Succeeded&&after.Properties.Single(p=>p.Type==GraphValueType.Texture2D).Name==binding.Name,"Renaming preserves texture symbol");
            assert(after.ShaderSource.Contains("Body albedo [1]"),"Shader and node share renamed label");
            assert(hash!=GraphJson.ComputeSemanticHash(copy),"Rename invalidates generated display labels");
            var snippet=GraphClipboard.Copy(copy,new[]{"texture"});var pasted=GraphClipboard.Paste(new ShaderGraph{GraphId="paste-label"},snippet,0,0);
            assert(pasted.Graph.Resources.Single().Name=="Body albedo","Clipboard preserves texture name");
            copy.Resources[0].Name="bad\"\n\\name";assert(TextureSlotLabels.DisplayName(copy,copy.Resources[0].Id)=="badname [1]","Shader labels sanitized");
        }
    }

    static void CheckLinearOrdering(Action<bool,string> assert)
    {
        var graph = new ShaderGraph
        {
            Resources = new System.Collections.Generic.List<GraphResource>
            {
                new GraphResource { Id = "z", Name = "Last" },
                null,
                new GraphResource { Id = null, Name = "Null first" },
                new GraphResource { Id = "dup", Name = "Duplicate first" },
                new GraphResource { Id = "b", Name = "Middle" },
                new GraphResource { Id = null, Name = "Null second" },
                new GraphResource { Id = "dup", Name = "Duplicate second" },
                new GraphResource { Id = "", Name = "Empty" },
                new GraphResource { Id = "a", Name = "First" }
            }
        };
        foreach (var id in new[] { null, "", "a", "b", "dup", "z", "missing" })
            assert(TextureSlotLabels.DisplayName(graph, id) == SortedDisplayName(graph, id),
                "Linear texture label matches sorted reference for ID " + (id ?? "<null>"));
        assert(TextureSlotLabels.DisplayName(graph, "dup") == "Duplicate first [6]",
            "Duplicate IDs keep the first resource in stable sorted order");
        assert(TextureSlotLabels.DisplayName(graph, null) == "Null first [1]",
            "Null resource IDs sort first and keep stable order");
        assert(TextureSlotLabels.DisplayName(new ShaderGraph { Resources = null }, "a") == "Texture (missing)",
            "Missing resource list returns missing label");
    }

    static string SortedDisplayName(ShaderGraph graph, string resourceId)
    {
        var resources = graph?.Resources?.Where(r => r != null).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var index = resources?.FindIndex(r => r.Id == resourceId) ?? -1;
        if (index < 0) return "Texture (missing)";
        var name = TextureSlotLabels.Clean(resources[index].Name);
        return name.Length == 0 ? "Texture " + (index + 1) : name + " [" + (index + 1) + "]";
    }
}
