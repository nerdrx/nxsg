using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
                var warm=new List<double>();
                for(var i=0;i<6;i++)
                {
                    GraphBuild.Build(graph,Path.GetFullPath(path));
                    Debug.Log("BUILD TIMING "+name+" run "+i+": "+GraphBuild.LastBuildSummary);
                    if(i>0)warm.Add(GraphBuild.LastBuildMilliseconds);
                }
                Debug.Log("BUILD MEDIAN "+name+" unchanged: "+warm.OrderBy(ms=>ms).ElementAt(warm.Count/2).ToString("F2")+" ms");
            }
            Debug.Log("BUILD TIMING PASSED");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
