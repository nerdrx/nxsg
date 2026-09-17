using System;
using System.IO;
using NXSG.Core;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Callbacks;
using UnityEngine;

namespace NXSG.Editor
{
    [ScriptedImporter(1, "nxsg")]
    public sealed class GraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = ScriptableObject.CreateInstance<GraphAsset>();
            // Keep source recoverable even when parsing fails; import never builds output.
            var size = new FileInfo(context.assetPath).Length;
            if (size > 4 * 1024 * 1024)
                asset.parseError = "Graph exceeds the 4 MiB safety limit. Source file was preserved.";
            else
            {
                asset.source = File.ReadAllText(context.assetPath);
                try { GraphJson.Parse(asset.source); }
                catch (Exception exception) { asset.parseError = exception.Message; }
            }
            context.AddObjectToAsset("graph", asset);
            context.SetMainObject(asset);
        }

        [OnOpenAsset]
        static bool Open(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId) as GraphAsset;
            if (asset == null) return false;
            GraphWindow.Open(AssetDatabase.GetAssetPath(asset));
            return true;
        }
    }

}
