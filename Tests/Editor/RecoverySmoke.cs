using System;
using System.IO;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
public static class RecoverySmoke
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance;
    static GraphWindow pendingWindow; static Material pendingMaterial; static int expectedConnections; static int undoTicks;
    public static void Run()
    {
        var type=typeof(GraphWindow).Assembly.GetType("NXSG.Editor.GraphRecovery");
        var folder=type.GetField("Folder",Flags);var old=(string)folder.GetValue(null);
        var temp=Path.GetFullPath("Library/NXSG/RecoveryTest-"+Guid.NewGuid().ToString("N"));
        try
        {
            folder.SetValue(null,temp);var graph=GraphSamples.CreateDefault();var before=GraphJson.Serialize(graph);
            var write=type.GetMethod("Write",Flags);var read=type.GetMethod("Read",Flags);var files=type.GetMethod("Files",Flags);
            var checkpoint=(string)write.Invoke(null,new object[]{graph,"Fixture",true});
            graph.Nodes[0].Properties["testTag"]="newer";
            write.Invoke(null,new object[]{graph,"Fixture",false});
            var recovered=(ShaderGraph)read.Invoke(null,new object[]{checkpoint});
            if(GraphJson.Serialize(recovered)!=before)throw new Exception("Checkpoint did not preserve previous content");
            if(((string[])files.Invoke(null,null)).Length!=2)throw new Exception("Autosnapshot replaced checkpoint");
            File.WriteAllText(checkpoint,"{ broken");
            bool rejected=false;try{read.Invoke(null,new object[]{checkpoint});}catch(TargetInvocationException){rejected=true;}
            if(!rejected)throw new Exception("Corrupt recovery accepted");
            if(!GraphJson.Serialize(graph).Contains("newer"))throw new Exception("Failed recovery mutated current graph");
            File.WriteAllText(checkpoint, before);
            var window=ScriptableObject.CreateInstance<GraphWindow>(); window.Show(); window.Focus();
            Set(window,"graph",null); ((GraphSession)Get(window,"session")).json=null;
            var material=new Material(Shader.Find("Unlit/Color"));
            Set(window,"contextMaterial",material);
            typeof(GraphWindow).GetMethod("RestoreRecovery",Flags).Invoke(window,new object[]{checkpoint});
            var restored=(ShaderGraph)Get(window,"graph");
            if(restored==null||GraphJson.Serialize(restored)!=before)throw new Exception("Blank window did not restore snapshot");
            if(Get(window,"contextMaterial")!=null||material.shader==null)throw new Exception("Recovery changed original material context");

            var insertion=GraphSamples.CreateDefault();var ramp=NodeCatalog.Create("core.oneMinus");ramp.Id="inserted";insertion.Nodes.Add(ramp);
            var session=(GraphSession)Get(window,"session");Set(window,"graph",insertion);session.json=GraphJson.Serialize(insertion,true);Invoke(window,"Rebuild");
            Undo.ClearUndo(session); Undo.IncrementCurrentGroup();
            Set(window,"insertionEdge","edge-color");
            if(!(bool)typeof(GraphWindow).GetMethod("InsertOnHighlightedWire",Flags).Invoke(window,new object[]{"inserted"}))throw new Exception("Wire insertion failed");
            expectedConnections=insertion.Connections.Count; pendingWindow=window; pendingMaterial=material; undoTicks=0;
            Undo.FlushUndoRecordObjects();Undo.PerformUndo();
            EditorApplication.update+=VerifyInsertionUndo;
            return;
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        finally{folder.SetValue(null,old);if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }

    static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    static void Invoke(object target,string name)=>target.GetType().GetMethod(name,Flags).Invoke(target,null);

    static void VerifyInsertionUndo()
    {
        if (++undoTicks < 2) return;
        EditorApplication.update-=VerifyInsertionUndo;
        try
        {
            var restored=(ShaderGraph)Get(pendingWindow,"graph");
            if(restored==null||restored.Connections.Count!=expectedConnections)throw new Exception("Wire insertion undo did not restore original connections");
            pendingWindow.DiscardChanges(); pendingWindow.Close(); UnityEngine.Object.DestroyImmediate(pendingMaterial);
            Debug.Log("NXSG RECOVERY SMOKE PASSED: snapshot roundtrip, blank-window restore, material isolation, insertion undo, corrupt snapshot rejection");
            EditorApplication.Exit(0);
        }
        catch(Exception e)
        {
            if(pendingWindow!=null){pendingWindow.DiscardChanges();pendingWindow.Close();}
            if(pendingMaterial!=null)UnityEngine.Object.DestroyImmediate(pendingMaterial);
            Debug.LogException(e); EditorApplication.Exit(1);
        }
    }
}
