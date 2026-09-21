using System;
using System.Linq;

namespace NXSG.Core
{
    public static class TextureSlotLabels
    {
        public static string DisplayName(ShaderGraph graph, string resourceId)
        {
            var resources = graph?.Resources?.Where(r => r != null).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            var index = resources?.FindIndex(r => r.Id == resourceId) ?? -1;
            if (index < 0) return "Texture (missing)";
            var name = Clean(resources[index].Name);
            return name.Length == 0 ? "Texture " + (index + 1) : name + " [" + (index + 1) + "]";
        }

        public static string Clean(string name)
        {
            // ShaderLab display strings must never become executable shader syntax.
            return new string((name ?? "").Where(c => !char.IsControl(c) && c != '"' && c != '\\').Take(64).ToArray()).Trim();
        }
    }
}
