using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed class TextureChannelPacker : EditorWindow
    {
        static readonly string[] ChannelNames = { "Red", "Green", "Blue", "Alpha" };
        readonly Texture2D[] sources = new Texture2D[4];
        readonly int[] channels = { 0, 1, 2, 3 };
        int resolution = 1024;

        public static void Open()
        {
            var window = GetWindow<TextureChannelPacker>(true, "NXSG Channel Packing");
            window.minSize = new Vector2(390, 270);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Pack texture channels", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Choose a source and channel for each output. Empty sources become white. Input textures stay unchanged; output is a linear PNG.", MessageType.Info);
            for (var i = 0; i < 4; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(ChannelNames[i], GUILayout.Width(48));
                    sources[i] = (Texture2D)EditorGUILayout.ObjectField(sources[i], typeof(Texture2D), false);
                    channels[i] = EditorGUILayout.Popup(channels[i], ChannelNames, GUILayout.Width(76));
                }
            }
            resolution = EditorGUILayout.IntPopup("Resolution", resolution, new[] { "256", "512", "1024", "2048", "4096" }, new[] { 256, 512, 1024, 2048, 4096 });
            if (GUILayout.Button("Save packed PNG")) Save();
        }

        void Save()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save packed texture", "NXSG Packed Mask", "png", "Choose a path in Assets.");
            if (string.IsNullOrEmpty(path)) return;
            var shader = Shader.Find("Hidden/NXSG/PackChannels");
            if (shader == null) { EditorUtility.DisplayDialog("NXSG channel packing", "Packing shader is missing. Reimport the NXSG package.", "OK"); return; }
            var material = new Material(shader);
            var target = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var result = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    material.SetTexture("_Source" + i, sources[i] != null ? sources[i] : Texture2D.whiteTexture);
                    material.SetFloat("_Channel" + i, channels[i]);
                }
                Graphics.Blit(Texture2D.whiteTexture, target, material);
                RenderTexture.active = target;
                result.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                result.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, path.Substring("Assets/".Length)), result.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                }
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
            catch (Exception error) { EditorUtility.DisplayDialog("NXSG channel packing", error.Message, "OK"); }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                DestroyImmediate(result);
                DestroyImmediate(material);
            }
        }
    }
}
