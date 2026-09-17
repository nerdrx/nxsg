using System;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    [InitializeOnLoad]
    public static class GraphMaterialHeader
    {
        static GraphMaterialHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += Draw;
        }

        static void Draw(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1 || !(editor.target is Material material) ||
                material.shader == null || !material.shader.name.StartsWith("NXSG/", StringComparison.Ordinal)) return;

            var source = SourcePath(material);
            var tooltip = string.IsNullOrEmpty(source)
                ? "This shader has no source link. Open the editor, then choose your .nxsg file with Open."
                : "Open this material's original NXSG graph.";
            if (GUILayout.Button(new GUIContent("Open Shader Graph", tooltip), GUILayout.Height(26)))
            {
                if (string.IsNullOrEmpty(source)) GraphWindow.ShowEditor();
                else GraphWindow.Open(source);
            }
            if (string.IsNullOrEmpty(source))
                EditorGUILayout.HelpBox("No linked graph. Choose Open in the graph editor to select your .nxsg file.", MessageType.Info);
        }

        public static string SourcePath(Material material)
        {
            if (material == null || material.shader == null) return null;
            var path = AssetDatabase.GetAssetPath(material.shader);
            const string prefix = "Assets/NXSGGenerated/";
            const string suffix = "/Material.shader";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal)) return null;
            var guid = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
            if (guid.Length != 32) return null;
            var source = AssetDatabase.GUIDToAssetPath(guid);
            return source.EndsWith(".nxsg", StringComparison.OrdinalIgnoreCase) &&
                AssetDatabase.LoadAssetAtPath<GraphAsset>(source) != null ? source : null;
        }
    }
}
