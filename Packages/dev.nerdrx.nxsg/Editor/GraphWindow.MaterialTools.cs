using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        // Parent toolbar owns placement; this method only supplies actions.
        void AddMaterialToolsMenu(ToolbarMenu menu)
        {
            
            menu.menu.AppendAction("Material/Save preset…", _ => SaveMaterialPreset());
            menu.menu.AppendAction("Material/Load preset…", _ => LoadMaterialPreset());
        }

        void SaveMaterialPreset()
        {
            if (contextMaterial == null) { EditorUtility.DisplayDialog("NXSG material", "Open a graph with a material first.", "OK"); return; }
            var path = EditorUtility.SaveFilePanelInProject("Save material preset", contextMaterial.name, "asset", "Choose preset location.");
            if (!string.IsNullOrEmpty(path)) NXSGMaterialPresetUtility.Capture(contextMaterial, path);
        }

        void LoadMaterialPreset()
        {
            if (contextMaterial == null) { EditorUtility.DisplayDialog("NXSG material", "Open a graph with a material first.", "OK"); return; }
            var path = EditorUtility.OpenFilePanel("Load material preset", Application.dataPath, "asset");
            if (string.IsNullOrEmpty(path)) return;
            var relative = FileUtil.GetProjectRelativePath(path);
            if (string.IsNullOrEmpty(relative) || !relative.StartsWith("Assets/", System.StringComparison.Ordinal))
            { EditorUtility.DisplayDialog("NXSG material", "Choose a preset inside this project.", "OK"); return; }
            var preset = AssetDatabase.LoadAssetAtPath<NXSGMaterialPreset>(relative);
            if (preset == null) { EditorUtility.DisplayDialog("NXSG material", "Selected asset is not an NXSG material preset.", "OK"); return; }
            try { NXSGMaterialPresetUtility.Apply(preset, new[] { contextMaterial }); previewHash=null;QueueLivePreview();SetStatus("Applied material preset: "+preset.name); }
            catch (System.Exception exception) { EditorUtility.DisplayDialog("NXSG material", exception.Message, "OK"); }
        }
    }
}
