using System;
using Newtonsoft.Json.Linq;
using NXSG.Core;

namespace NXSG.Backend
{
    public static class MaterialGroups
    {
        public static string HeaderFor(ShaderGraph graph, string parameterId)
        {
            var groups = graph?.Adapter?["materialGroups"] as JObject;
            var name = groups == null ? null : (string)groups[parameterId];
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            var safe = string.Empty;
            for (var i = 0; i < name.Length && safe.Length < 80; i++)
            {
                var character = name[i];
                if (!char.IsLetterOrDigit(character) && character != ' ' && character != '_' && character != '-') continue;
                safe += character;
            }
            return safe.Length == 0 ? null : "[Header(" + safe + ")]";
        }
    }
}
