using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    // Native property drawers preserve Undo, multi-edit and embedded group headers,
    // including materials shared without the source graph.
    public sealed class NXSGMaterialShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            materialEditor.PropertiesDefaultGUI(properties);
            if (!EditorGUI.EndChangeCheck()) return;
            foreach (var material in materialEditor.targets.OfType<Material>())
                TextureResourceUtility.SyncArrayLayerCounts(material);
        }
    }
}
