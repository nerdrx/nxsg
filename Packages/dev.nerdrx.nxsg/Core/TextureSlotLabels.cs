using System;
using System.Linq;

namespace NXSG.Core
{
    public static class TextureSlotLabels
    {
        public static string DisplayName(ShaderGraph graph, string resourceId)
        {
            var index = graph?.Resources?.FindIndex(r => r != null && r.Id == resourceId) ?? -1;
            if (index < 0) return "Texture (missing)";
            var name = Clean(graph.Resources[index].Name);
            return name.Length == 0 ? "Texture " + (index + 1) : name + " [" + (index + 1) + "]";
        }

        public static string Clean(string name)
        {
            // ShaderLab display strings must never become executable shader syntax.
            return new string((name ?? "").Where(c => !char.IsControl(c) && c != '"' && c != '\\').Take(64).ToArray()).Trim();
        }
    }
}
