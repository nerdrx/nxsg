using UnityEditor;

namespace NXSG.Editor
{
    // Native property drawers preserve Undo, multi-edit and embedded group headers,
    // including materials shared without the source graph.
    public sealed class NXSGMaterialShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            materialEditor.PropertiesDefaultGUI(properties);
        }
    }
}
