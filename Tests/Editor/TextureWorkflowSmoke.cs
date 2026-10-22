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
        Texture2D transientSource = null, transientNormal = null, transientEmission = null;
        Material input = null, pack = null;
        Material transientInput = null;
        RenderTexture target = null;
        UnityEngine.Object review = null;
        var previous = RenderTexture.active;
        try
        {
            var nested = typeof(GraphWindow).GetNestedType("TextureSetReviewWindow", BindingFlags.NonPublic);
            if (nested == null) throw new Exception("Material review window missing");
            review = ScriptableObject.CreateInstance(nested);
            transientSource = new Texture2D(2, 2);
            transientNormal = new Texture2D(2, 2);
            transientEmission = new Texture2D(2, 2);
            transientInput = new Material(Shader.Find("Standard"));
            transientInput.SetTexture("_MainTex", transientSource);
            transientInput.SetTexture("_BumpMap", transientNormal);
            transientInput.SetTexture("_EmissionMap", transientEmission);
            nested.GetMethod("AssignMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(review, new object[] { transientInput });
            var assigned = (IDictionary)nested.GetField("assigned", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(review);
            if (assigned.Count != 0) throw new Exception("Transient material textures should be skipped");

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "NXSGTextureWorkflowSmoke"));
            AssetDatabase.Refresh();
            source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            source.SetPixels32(new[] { new Color32(51, 102, 153, 204), new Color32(200, 30, 80, 255), new Color32(10, 220, 40, 125), new Color32(90, 70, 240, 0) });
            source.Apply();
            normal = new Texture2D(2, 2); emission = new Texture2D(2, 2);
            AssetDatabase.CreateAsset(source, "Assets/NXSGTextureWorkflowSmoke/Source.asset");
            AssetDatabase.CreateAsset(normal, "Assets/NXSGTextureWorkflowSmoke/Normal.asset");
            AssetDatabase.CreateAsset(emission, "Assets/NXSGTextureWorkflowSmoke/Emission.asset");
            AssetDatabase.SaveAssets();
            input = new Material(Shader.Find("Standard"));
            input.SetTexture("_MainTex", source); input.SetTexture("_BumpMap", normal); input.SetTexture("_EmissionMap", emission);
            nested.GetMethod("AssignMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(review, new object[] { input });
            if ((Texture2D)assigned[TextureSetSlot.Albedo] != source || (Texture2D)assigned[TextureSetSlot.Normal] != normal || (Texture2D)assigned[TextureSetSlot.Emission] != emission)
                throw new Exception("Material texture slots mapped incorrectly: albedo=" + Describe(assigned, TextureSetSlot.Albedo) + ", normal=" + Describe(assigned, TextureSetSlot.Normal) + ", emission=" + Describe(assigned, TextureSetSlot.Emission));

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
            AssetDatabase.DeleteAsset("Assets/NXSGTextureWorkflowSmoke");
            foreach (var item in new UnityEngine.Object[] { transientInput, transientSource, transientNormal, transientEmission, source, normal, emission, result, input, pack }) if (item != null) UnityEngine.Object.DestroyImmediate(item);
        }
    }

    static string Describe(IDictionary assigned, TextureSetSlot slot)
    {
        var texture = assigned.Contains(slot) ? assigned[slot] as Texture2D : null;
        return texture == null ? "<missing>" : texture.name + " (" + AssetDatabase.GetAssetPath(texture) + ", id=" + texture.GetInstanceID() + ")";
    }
}
