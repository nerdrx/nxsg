using System;
using System.IO;
using NXSG.Core;
using NXSG.Editor;
using UnityEngine;
using UnityEditor;
public static class EffectsBuildSmoke
{
    public static void Run()
    {
        try
        {
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
            Directory.CreateDirectory("Assets/EffectsExamples");
            foreach(var name in new[]{"Audio Hologram","Noise Color Ramp","Animated Sticker"})
            {
                var source=File.ReadAllText(Path.Combine(root,"Packages/dev.nerdrx.nxsg/Samples~/"+name+".nxsg"));
                var path="Assets/EffectsExamples/"+name+".nxsg"; File.WriteAllText(path,source); AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var graph=GraphJson.Parse(source);var material=GraphBuild.Build(graph,Path.GetFullPath(path));
                if(material==null||!material.shader.isSupported||ShaderUtil.ShaderHasError(material.shader))throw new Exception(name+" material build failed");
                var materialPath=AssetDatabase.GetAssetPath(material);var guid=AssetDatabase.AssetPathToGUID(materialPath);material.SetColor("_Color",new Color(.7f,.6f,.5f,1));EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
                var rebuilt=GraphBuild.Build(graph,Path.GetFullPath(path));
                if(AssetDatabase.AssetPathToGUID(materialPath)!=guid||Mathf.Abs(rebuilt.GetColor("_Color").r-.7f)>.01f)throw new Exception(name+" rebuild did not preserve identity/tint");
                Debug.Log("NXSG example built and rebuilt: "+name);
            }
            Debug.Log("NXSG EFFECTS BUILD SMOKE PASSED");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
