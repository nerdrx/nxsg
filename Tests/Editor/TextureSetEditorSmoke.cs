using System;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Run in hidden graphics-enabled Unity with -executeMethod TextureSetEditorSmoke.Run.
public static class TextureSetEditorSmoke
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        GraphWindow window = null;
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>();window.Show();window.CreateGUI();
            Set(window, "graph", GraphSamples.CreateDefault()); Set(window, "sourcePath", "Assets/Existing.nxsg"); Set(window, "diskSource", "existing");
            Set(window, "autoScene", true);
            var imported = TextureSetGraphBuilder.Build(new System.Collections.Generic.Dictionary<TextureSetSlot, string> { { TextureSetSlot.Albedo, "Assets/albedo.png" } });
            Invoke(window, "ApplyTextureSet", imported);
            Require((string)Get(window, "sourcePath") == null && (string)Get(window, "diskSource") == null, "texture import retained source path");
            Require(!(bool)Get(window, "autoScene"), "texture import left auto scene active");
            var newId=((ShaderGraph)Get(window,"graph")).GraphId;Undo.PerformUndo();Require(((ShaderGraph)Get(window,"graph")).GraphId==newId,"Import Undo restored previous document");
            var source = new Texture2D(1, 1, TextureFormat.RGBA32, false); source.SetPixel(0, 0, new Color(.2f, .4f, .6f, .8f)); source.Apply();
            var shader = Shader.Find("Hidden/NXSG/TextureChannelPreview"); Require(shader != null, "channel preview shader missing");
            var material = new Material(shader); material.SetFloat("_Channel", 1); var target = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32);
            var expected = new[] { .2f, .4f, .6f, .8f };
            for (var channel = 1; channel <= 4; channel++)
            {
                material.SetFloat("_Channel", channel); Graphics.Blit(source, target, material); var previous = RenderTexture.active; RenderTexture.active = target; var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false); pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixel.Apply(); RenderTexture.active = previous;
                Require(Mathf.Abs(pixel.GetPixel(0, 0).r - expected[channel - 1]) < .05f, "GPU channel preview mismatch for channel " + channel+": "+pixel.GetPixel(0,0)); UnityEngine.Object.DestroyImmediate(pixel);
            }
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(source);
            Debug.Log("NXSG TEXTURE SET EDITOR SMOKE PASSED");
            typeof(EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window,false);window.Close(); EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            if (window != null) { typeof(EditorWindow).GetProperty("hasUnsavedChanges").SetValue(window,false);window.Close(); }
            Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }

    static object Get(GraphWindow window, string field) { return typeof(GraphWindow).GetField(field, Flags).GetValue(window); }
    static void Set(GraphWindow window, string field, object value) { typeof(GraphWindow).GetField(field, Flags).SetValue(window, value); }
    static void Invoke(GraphWindow window, string method, object value) { typeof(GraphWindow).GetMethod(method, Flags).Invoke(window, new[] { value }); }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
