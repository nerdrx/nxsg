using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed class BatchPresetWindow : EditorWindow
    {
        NXSGMaterialPreset preset;
        Material[] targets = new Material[0];

        public static void Open()
        {
            var window = GetWindow<BatchPresetWindow>(true, "NXSG Batch Preset");
            window.minSize = new Vector2(430, 270);
            window.Refresh();
            window.Show();
        }

        void OnSelectionChange() { Refresh(); Repaint(); }

        void Refresh()
        {
            targets = Selection.GetFiltered<Material>(SelectionMode.Assets).Distinct().ToArray();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Apply preset to selected materials", EditorStyles.boldLabel);
            preset = (NXSGMaterialPreset)EditorGUILayout.ObjectField("Preset", preset, typeof(NXSGMaterialPreset), false);
            EditorGUILayout.LabelField("Selected materials", targets.Length.ToString());
            if (targets.Length == 0) EditorGUILayout.HelpBox("Select material assets in the Project window.", MessageType.Info);
            else
            {
                foreach (var material in targets.Take(8))
                {
                    string reason;
                    var compatible = NXSGMaterialPresetUtility.IsCompatible(preset, material, out reason);
                    EditorGUILayout.LabelField(material.name + (compatible ? " · ready" : " · " + reason), EditorStyles.miniLabel);
                }
                if (targets.Length > 8) EditorGUILayout.LabelField("…and " + (targets.Length - 8) + " more", EditorStyles.miniLabel);
            }
            var allCompatible = preset != null && targets.Length > 0 && targets.All(material => NXSGMaterialPresetUtility.IsCompatible(preset, material, out _));
            using (new EditorGUI.DisabledScope(!allCompatible))
                if (GUILayout.Button("Apply to " + targets.Length + " materials"))
                {
                    try { NXSGMaterialPresetUtility.Apply(preset, targets); Close(); }
                    catch (System.Exception error) { EditorUtility.DisplayDialog("NXSG batch preset", error.Message, "OK"); }
                }
            EditorGUILayout.HelpBox("All targets must use the preset's NXSG shader. One Undo action restores their previous values.", MessageType.None);
        }
    }
}
