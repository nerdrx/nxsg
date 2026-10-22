using System;
using System.Linq;

namespace NXSG.Core
{
    public static class TextureSlotLabels
    {
        public static string DisplayName(ShaderGraph graph, string resourceId)
        {
            var resources = graph?.Resources;
            if (resources == null) return "Texture (missing)";
            GraphResource match = null;
            var index = 0;
            foreach (var resource in resources)
            {
                if (resource == null) continue;
                if (StringComparer.Ordinal.Equals(resource.Id, resourceId))
                {
                    if (match == null) match = resource;
                }
                else if (StringComparer.Ordinal.Compare(resource.Id, resourceId) < 0)
                {
                    index++;
                }
            }
            if (match == null) return "Texture (missing)";
            var name = Clean(match.Name);
            return name.Length == 0 ? "Texture " + (index + 1) : name + " [" + (index + 1) + "]";
        }

        public static string Clean(string name)
        {
            // ShaderLab display strings must never become executable shader syntax.
            return new string((name ?? "").Where(c => !char.IsControl(c) && c != '"' && c != '\\').Take(64).ToArray()).Trim();
        }
    }
}
