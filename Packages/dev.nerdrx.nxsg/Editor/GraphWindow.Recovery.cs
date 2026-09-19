using System;
using System.IO;
using System.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        string recoveryJson;
        double recoveryDue;
        void TickRecovery()
        {
            if(graph==null||session==null||string.IsNullOrEmpty(session.json)||session.json==recoveryJson||EditorApplication.timeSinceStartup<recoveryDue)return;
            recoveryDue=EditorApplication.timeSinceStartup+5;
            try { GraphRecovery.Write(graph,string.IsNullOrEmpty(sourcePath)?"Untitled":Path.GetFileNameWithoutExtension(sourcePath),false); recoveryJson=session.json; }
            catch(Exception e) { SetStatus("Recovery snapshot failed: "+e.Message); }
        }
        void CreateCheckpoint()
        {
            if(graph==null)return;
            try { GraphRecovery.Write(graph,string.IsNullOrEmpty(sourcePath)?"Untitled":Path.GetFileNameWithoutExtension(sourcePath),true); SetStatus("Checkpoint saved locally. Restore it from Recovery."); }
            catch(Exception e) { SetStatus("Checkpoint failed: "+e.Message); }
        }
        void ShowRecoveryMenu()
        {
            var menu=new GenericMenu();
            if(graph!=null)menu.AddItem(new GUIContent("Create checkpoint"),false,CreateCheckpoint);
            menu.AddSeparator("");
            var files=GraphRecovery.Files();
            foreach(var path in files)
            {
                var name=Path.GetFileNameWithoutExtension(path);var parts=name.Split('_');
                var title=parts.Length>=4?parts[2]+" · "+parts[3]+" · "+parts[0]:name;
                menu.AddItem(new GUIContent(title),false,()=>{if(CanDiscard())RestoreRecovery(path);});
            }
            if(files.Length==0)menu.AddDisabledItem(new GUIContent("No snapshots yet"));
            menu.ShowAsContext();
        }
        void RestoreRecovery(string path)
        {
            try
            {
                var recovered=GraphRecovery.Read(path); CheckEditableShape(recovered);
                CreateCheckpoint(); // Preserve the current graph before replacing the working copy.
                autoScene=false; scenePending=false;
                foreach(var toggle in rootVisualElement.Query<ToolbarToggle>().ToList())if(toggle.text=="Auto scene")toggle.SetValueWithoutNotify(false);
                // Recovery opens a copy. Neither the original graph nor material is overwritten.
                sourcePath=null; diskSource=null; contextMaterial=null; selected=null; selection.Clear();
                Undo.RegisterCompleteObjectUndo(session, "Restore graph snapshot");
                graph=recovered;
                session.json=GraphJson.Serialize(graph, true);
                hasUnsavedChanges=true;
                EditorUtility.SetDirty(session);
                Rebuild();
                SetStatus("Recovered copy. Auto scene paused; use Save as to choose its destination. Undo restores previous graph contents.");
            }
            catch(Exception e) { SetStatus("Recovery failed: "+e.Message); }
        }
    }
}
