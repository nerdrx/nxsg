using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed class TextureAtlasBuilder : EditorWindow
    {
        Texture2D[] frames = Array.Empty<Texture2D>();
        int columns = 4;
        int cellSize = 256;
        Vector2 scroll;

        public static void Open()
        {
            var window = GetWindow<TextureAtlasBuilder>(true, "NXSG Atlas Builder");
            window.frames = Selection.GetFiltered<Texture2D>(SelectionMode.Assets)
                .OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToArray();
            window.minSize = new Vector2(420, 320);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Build an atlas from selected textures", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select image assets in the Project window before opening. Frames fill the grid in path order, left to right. Empty cells are transparent. Sources are not changed.", MessageType.Info);
            columns = EditorGUILayout.IntSlider("Columns", columns, 1, 16);
            cellSize = EditorGUILayout.IntPopup("Cell size", cellSize, new[] { "64", "128", "256", "512", "1024" }, new[] { 64, 128, 256, 512, 1024 });
            var rows = Mathf.CeilToInt(frames.Length / (float)columns);
            EditorGUILayout.LabelField("Output", columns + " × " + rows + " cells (" + columns * cellSize + " × " + rows * cellSize + " px)");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var frame in frames) EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(frame), EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(frames.Length == 0 || columns * cellSize > 8192 || rows * cellSize > 8192))
                if (GUILayout.Button("Save atlas PNG")) Save();
            if (frames.Length == 0) EditorGUILayout.HelpBox("No image assets selected.", MessageType.Warning);
            else if (columns * cellSize > 8192 || rows * cellSize > 8192) EditorGUILayout.HelpBox("Atlas exceeds 8192 pixels on one axis. Reduce cell size or use fewer images.", MessageType.Warning);
        }

        void Save()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save texture atlas", "NXSG Atlas", "png", "Choose a path in Assets.");
            if (string.IsNullOrEmpty(path)) return;
            Texture2D output = null;
            try
            {
                output = Build(frames, columns, cellSize);
                File.WriteAllBytes(Path.Combine(Application.dataPath, path.Substring("Assets/".Length)), output.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                EditorGUIUtility.PingObject(Selection.activeObject);
            }
            catch (Exception error) { EditorUtility.DisplayDialog("NXSG atlas builder", error.Message, "OK"); }
            finally { if (output != null) DestroyImmediate(output); }
        }

        internal static Texture2D Build(Texture2D[] images, int columns, int cellSize)
        {
            if (images == null || images.Length == 0 || images.Any(image => image == null) || columns < 1 || cellSize < 1)
                throw new ArgumentException("Choose valid image assets, columns, and cell size.");
            var rows = Mathf.CeilToInt(images.Length / (float)columns);
            var width = columns * cellSize;
            var height = rows * cellSize;
            if (width > 8192 || height > 8192) throw new ArgumentException("Atlas exceeds 8192 pixels on one axis.");
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(0, width, height, 0);
                    for (var i = 0; i < images.Length; i++)
                        Graphics.DrawTexture(new Rect((i % columns) * cellSize, (i / columns) * cellSize, cellSize, cellSize), images[i]);
                }
                finally { GL.PopMatrix(); }
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                output.Apply();
                return output;
            }
            catch { DestroyImmediate(output); throw; }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
