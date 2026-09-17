using System;
using System.IO;
using NXSG.Core;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Callbacks;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed class GraphAsset : ScriptableObject
    {
        [TextArea] public string source;
        public string parseError;
    }

    [CustomEditor(typeof(GraphAsset))]
    public sealed class GraphAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (GraphAsset)target;
            EditorGUILayout.LabelField("NX Shader Graph", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(asset.parseError))
                EditorGUILayout.HelpBox(asset.parseError, MessageType.Error);
            if (GUILayout.Button("Open graph")) GraphWindow.Open(AssetDatabase.GetAssetPath(asset));
            EditorGUILayout.HelpBox("Graph source stays editable. Build creates local shader assets; it does not upload an avatar.", MessageType.Info);
        }
    }

}
