using NXSG.Backend;
using UnityEditor;

namespace NXSG.Editor
{
    internal static class OptionalIntegrations
    {
        internal static EmitterOptions Options(string shaderName = null)
        {
            var options = new EmitterOptions
            {
                LtcgiAvailable = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc") != null
            };
            if (shaderName != null) options.ShaderName = shaderName;
            return options;
        }
    }
}
