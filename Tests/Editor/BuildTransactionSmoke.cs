using System;
using System.IO;
using System.Linq;
using NXSG.Core;
using NXSG.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
public static class BuildTransactionSmoke
{
    const string PathName="Assets/BuildTransaction.nxsg";
    static void Require(bool b,string m){if(!b)throw new Exception(m);}
    public static void Run()
    {
        try
        {
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GraphBuild).Assembly).resolvedPath;
            var graph=GraphJson.Parse(File.ReadAllText(Path.Combine(package,"Samples~/Animated Palette.nxsg")));
            Save(graph);
            var mat=GraphBuild.Build(graph,Path.GetFullPath(PathName));
            var id=AssetDatabase.AssetPathToGUID(PathName);
            Require(mat.shader.name=="NXSG/BuildTransaction/"+id,"Readable shader name missing");
            var preset=ScriptableObject.CreateInstance<NXSGMaterialPreset>();
            preset.shaderName="NXSG/Generated/"+id;
            Require(NXSGMaterialPresetUtility.IsCompatible(preset,mat,out var reason),"Legacy preset rejected: "+reason);
            preset.shaderName="NXSG/Other/00000000000000000000000000000000";
            Require(!NXSGMaterialPresetUtility.IsCompatible(preset,mat,out reason),"Unrelated preset accepted");
            UnityEngine.Object.DestroyImmediate(preset);
            var dir="Assets/NXSGGenerated/"+id;
            var shader=dir+"/Material.shader";var material=dir+"/Material.mat";
            var sg=AssetDatabase.AssetPathToGUID(shader);var mg=AssetDatabase.AssetPathToGUID(material);
            var oldShader=File.ReadAllText(shader);var oldMat=File.ReadAllText(material);
            var stamp=File.GetLastWriteTimeUtc(shader);
            GraphBuild.Build(graph,Path.GetFullPath(PathName));
            Require(File.GetLastWriteTimeUtc(shader)==stamp,"Unchanged shader was rewritten");
            var color=graph.Nodes.First(n=>n.Operation=="core.constant"&&(string)n.Properties["valueType"]=="color");
            color.Properties["value"]=new JArray(.23,.42,.61,1);Save(graph);
            foreach(var phase in new[]{"shader-promoted","material-promoted"})
            {
                GraphBuild.Checkpoint=p=>{if(p==phase)throw new InvalidOperationException("injected");};
                bool failed=false;try{GraphBuild.Build(graph,Path.GetFullPath(PathName));}catch(InvalidOperationException){failed=true;}finally{GraphBuild.Checkpoint=null;}
                Require(failed,"Injection did not fail");
                Require(File.ReadAllText(shader)==oldShader && File.ReadAllText(material)==oldMat,"Rollback changed assets at "+phase);
                Require(mat.shader==AssetDatabase.LoadAssetAtPath<Shader>(shader),"Retained material reference lost shader");
            }
            mat=GraphBuild.Build(graph,Path.GetFullPath(PathName));
            Require(File.ReadAllText(shader)!=oldShader,"Changed graph did not rebuild");
            Require(AssetDatabase.AssetPathToGUID(shader)==sg&&AssetDatabase.AssetPathToGUID(material)==mg,"GUIDs changed");
            var correct=File.ReadAllText(shader);File.WriteAllText(shader,"corrupted shader");
            GraphBuild.Build(graph,Path.GetFullPath(PathName));Require(File.ReadAllText(shader)==correct,"Corrupt output was reused");
            AssetDatabase.DeleteAsset(material);Require(GraphBuild.Build(graph,Path.GetFullPath(PathName))!=null,"Missing material not rebuilt");
            // A real compiler failure must restore the last valid shader and material.
            oldShader=File.ReadAllText(shader);oldMat=File.ReadAllText(material);
            GraphBuild.Checkpoint=p=>{if(p=="shader-promoted"){File.WriteAllText(shader,"Shader \"NXSG/Broken\" { nonsense }");AssetDatabase.ImportAsset(shader,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);}};
            bool rejected=false;try{GraphBuild.Build(graph,Path.GetFullPath(PathName));}catch(InvalidOperationException){rejected=true;}finally{GraphBuild.Checkpoint=null;}
            Require(rejected&&File.ReadAllText(shader)==oldShader&&File.ReadAllText(material)==oldMat,"Compiler failure rollback failed");
            Debug.Log("BUILD TRANSACTION PASSED: unchanged, changed, rollback, GUIDs, corrupt output, missing material, compiler failure");EditorApplication.Exit(0);
        }
        catch(Exception e){GraphBuild.Checkpoint=null;Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Save(ShaderGraph graph){File.WriteAllText(PathName,GraphJson.Serialize(graph,true));AssetDatabase.ImportAsset(PathName,ImportAssetOptions.ForceSynchronousImport);}
}
