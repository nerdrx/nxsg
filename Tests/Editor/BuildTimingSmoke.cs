using System;
using System.IO;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
public static class BuildTimingSmoke
{
    public static void Run()
    {
        try
        {
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GraphBuild).Assembly).resolvedPath;
            foreach(var name in new[]{"Animated Palette", "Volume Pearl Sculpture", "Fur Cards"})
            {
                var path="Assets/BuildTiming-"+name+".nxsg";
                File.WriteAllText(path,File.ReadAllText(Path.Combine(package,"Samples~",name+".nxsg")));
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var graph=GraphJson.Parse(File.ReadAllText(path));
                for(var i=0;i<2;i++)
                {
                    GraphBuild.Build(graph,Path.GetFullPath(path));
                    Debug.Log("BUILD TIMING "+name+" run "+i+": "+GraphBuild.LastBuildSummary);
                }
            }
            Debug.Log("BUILD TIMING PASSED");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
