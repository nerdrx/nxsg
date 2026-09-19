using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

public static class CreatorMaterialSmoke
{
    public static void Run()
    {
        GraphPreview preview=null;Material source=null,target=null;NXSGMaterialPreset preset=null;
        string path=null;
        try
        {
            var graph=GraphSamples.CreateDefault();
            graph.Parameters.Add(new GraphParameter{Id="coat",Name="Coat Tint",Type=GraphValueType.Color,Binding=GraphBindingKind.Material,DefaultValue=new JArray(.2,.5,.8,1)});
            graph.Adapter=new JObject{["materialGroups"]=new JObject{["coat"]="Coat Settings"}};
            var node=NodeCatalog.Create("core.parameter");node.Properties["parameterId"]="coat";graph.Nodes.Add(node);
            var surface=graph.Nodes.First(n=>n.Operation=="core.toonSurface");graph.Connections.RemoveAll(e=>e.To.NodeId==surface.Id&&e.To.PortId=="albedo");
            graph.Connections.Add(new GraphConnection{Id="coat-surface",From=new GraphPortRef{NodeId=node.Id,PortId="value"},To=new GraphPortRef{NodeId=surface.Id,PortId="albedo"}});
            foreach(var operation in new[]{"core.toonSurface","core.unlitSurface"})
            {
                surface.Operation=operation;preview=GraphPreview.Create(graph,null);
                var shader=preview.Material.shader;var index=shader.FindPropertyIndex("_NXSG_P_coat");
                if(index<0 || !shader.GetPropertyAttributes(index).Any(a=>a.Contains("Coat Settings")))throw new Exception("Exported material lost group header: "+operation);
                preview.Dispose();preview=null;
            }
            preview=GraphPreview.Create(graph,null);source=preview.Material;
            source.SetColor("_NXSG_P_coat",Color.red);source.SetFloat("_NXSG_PreviewClock",1);
            target=new Material(source);target.SetColor("_NXSG_P_coat",Color.blue);target.SetFloat("_NXSG_PreviewClock",0);
            path=AssetDatabase.GenerateUniqueAssetPath("Assets/CreatorPresetSmoke.asset");
            preset=NXSGMaterialPresetUtility.Capture(source,path);
            if(preset.values.Any(v=>v.property.StartsWith("_NXSG_Preview")))throw new Exception("Preset captured preview-only clock");
            AssetDatabase.SaveAssets();Resources.UnloadAsset(preset);preset=AssetDatabase.LoadAssetAtPath<NXSGMaterialPreset>(path);
            if(preset==null||preset.values.Count==0)throw new Exception("Preset serialization lost values");
            Undo.IncrementCurrentGroup();NXSGMaterialPresetUtility.Apply(preset,new[]{source,target});Undo.FlushUndoRecordObjects();
            if(target.GetColor("_NXSG_P_coat")!=Color.red || target.GetFloat("_NXSG_PreviewClock")!=0)throw new Exception("Preset value apply or clock isolation failed");
            Undo.PerformUndo();if(target.GetColor("_NXSG_P_coat")!=Color.blue)throw new Exception("Preset Undo failed");
            Debug.Log("NXSG CREATOR MATERIAL SMOKE PASSED");
            preview.Dispose();preview=null;UnityEngine.Object.DestroyImmediate(target);target=null;AssetDatabase.DeleteAsset(path);path=null;
            EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        finally{if(preview!=null)preview.Dispose();if(target!=null)UnityEngine.Object.DestroyImmediate(target);if(path!=null)AssetDatabase.DeleteAsset(path);}
    }
}
