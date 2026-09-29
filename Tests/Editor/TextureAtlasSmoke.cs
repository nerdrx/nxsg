using System;
using System.IO;
using System.Reflection;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Copy into Assets/Editor and run with -executeMethod TextureAtlasSmoke.Run.
public static class TextureAtlasSmoke
{
    public static void Run()
    {
        var frames = new Texture2D[4];
        Texture2D atlas = null;
        try
        {
            var colors = new[] { Color.red, Color.green, Color.blue, new Color(1, 1, 0, 1) };
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
                frames[i].SetPixels(new[] { colors[i], colors[i], colors[i], colors[i] }); frames[i].Apply();
            }
            var build = typeof(TextureAtlasBuilder).GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic);
            if (build == null) throw new Exception("Atlas builder method missing");
            atlas = (Texture2D)build.Invoke(null, new object[] { frames, 2, 64 });
            if (atlas.width != 128 || atlas.height != 128) throw new Exception("Atlas dimensions incorrect");
            Check(atlas.GetPixel(32, 96), colors[0], "top left");
            Check(atlas.GetPixel(96, 96), colors[1], "top right");
            Check(atlas.GetPixel(32, 32), colors[2], "bottom left");
            Check(atlas.GetPixel(96, 32), colors[3], "bottom right");
            var path = Path.Combine(Application.dataPath, "NXSGAtlasSmoke.png");
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            AssetDatabase.ImportAsset("Assets/NXSGAtlasSmoke.png", ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NXSGAtlasSmoke.png") == null) throw new Exception("Atlas PNG did not import");
            Debug.Log("NXSG ATLAS SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            foreach (var frame in frames) if (frame != null) UnityEngine.Object.DestroyImmediate(frame);
            if (atlas != null) UnityEngine.Object.DestroyImmediate(atlas);
        }
    }

    static void Check(Color actual, Color expected, string tile)
    {
        if (Mathf.Abs(actual.r - expected.r) > .02f || Mathf.Abs(actual.g - expected.g) > .02f || Mathf.Abs(actual.b - expected.b) > .02f || actual.a < .98f)
            throw new Exception("Atlas " + tile + " tile has wrong color: " + actual);
    }
}
