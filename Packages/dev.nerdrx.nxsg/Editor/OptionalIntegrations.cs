using NXSG.Backend;
using UnityEditor;

namespace NXSG.Editor
{
    internal static class OptionalIntegrations
    {
        internal static string Fingerprint =>
            (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc") != null ? "lv1" : "lv0") + ":" +
            (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc") != null ? "ltcgi1" : "ltcgi0");

        internal static EmitterOptions Options(string shaderName = null)
        {
            var options = new EmitterOptions
            {
                LightVolumesAvailable = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc") != null,
                LtcgiAvailable = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc") != null
            };
            if (shaderName != null) options.ShaderName = shaderName;
            return options;
        }
    }
}
