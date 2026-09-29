using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Copy into Assets/Editor and run with -executeMethod TextureWorkflowSmoke.Run.
public static class TextureWorkflowSmoke
{
    const string Output = "Assets/NXSGTextureWorkflowSmoke/Packed.png";

    public static void Run()
    {
        Texture2D source = null, normal = null, emission = null, result = null;
        Material input = null, pack = null;
        RenderTexture target = null;
        UnityEngine.Object review = null;
        var previous = RenderTexture.active;
        try
        {
            source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            source.SetPixels32(new[] { new Color32(51, 102, 153, 204), new Color32(200, 30, 80, 255), new Color32(10, 220, 40, 125), new Color32(90, 70, 240, 0) });
            source.Apply();
            normal = new Texture2D(2, 2); emission = new Texture2D(2, 2);
            input = new Material(Shader.Find("Standard"));
            input.SetTexture("_MainTex", source); input.SetTexture("_BumpMap", normal); input.SetTexture("_EmissionMap", emission);
            var nested = typeof(GraphWindow).GetNestedType("TextureSetReviewWindow", BindingFlags.NonPublic);
            if (nested == null) throw new Exception("Material review window missing");
            review = ScriptableObject.CreateInstance(nested);
            nested.GetMethod("AssignMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(review, new object[] { input });
            var assigned = (IDictionary)nested.GetField("assigned", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(review);
            if (assigned[TextureSetSlot.Albedo] != source || assigned[TextureSetSlot.Normal] != normal || assigned[TextureSetSlot.Emission] != emission)
                throw new Exception("Material texture slots mapped incorrectly");

            var shader = Shader.Find("Hidden/NXSG/PackChannels");
            if (shader == null || !shader.isSupported) throw new Exception("Channel packing shader unavailable");
            pack = new Material(shader);
            for (var channel = 0; channel < 4; channel++) { pack.SetTexture("_Source" + channel, source); pack.SetFloat("_Channel" + channel, channel); }
            target = RenderTexture.GetTemporary(2, 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(Texture2D.whiteTexture, target, pack);
            RenderTexture.active = target;
            result = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            result.ReadPixels(new Rect(0, 0, 2, 2), 0, 0); result.Apply();
            var expected = source.GetPixels32(); var actual = result.GetPixels32();
            for (var i = 0; i < 4; i++)
                if (Math.Abs(actual[i].r - expected[i].r) > 2 || Math.Abs(actual[i].g - expected[i].g) > 2 || Math.Abs(actual[i].b - expected[i].b) > 2 || Math.Abs(actual[i].a - expected[i].a) > 2)
                    throw new Exception("Packed pixel " + i + " differs from source");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Application.dataPath, "NXSGTextureWorkflowSmoke/Packed.png")));
            File.WriteAllBytes(Path.Combine(Application.dataPath, "NXSGTextureWorkflowSmoke/Packed.png"), result.EncodeToPNG());
            AssetDatabase.ImportAsset(Output, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Output);
            importer.sRGBTexture = false; importer.SaveAndReimport();
            if (((TextureImporter)AssetImporter.GetAtPath(Output)).sRGBTexture) throw new Exception("Packed PNG importer is not linear");
            Debug.Log("NXSG TEXTURE WORKFLOW SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (review != null) UnityEngine.Object.DestroyImmediate(review);
            foreach (var item in new UnityEngine.Object[] { source, normal, emission, result, input, pack }) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            AssetDatabase.DeleteAsset("Assets/NXSGTextureWorkflowSmoke");
        }
    }
}
