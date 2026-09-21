using System;
using System.Linq;
using NXSG.Core;
using NXSG.Backend;
public static class TextureLabelChecks
{
    public static void Run(Action<bool,string> assert)
    {
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
}
