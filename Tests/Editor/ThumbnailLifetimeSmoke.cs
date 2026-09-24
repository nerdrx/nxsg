using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

public static class ThumbnailLifetimeSmoke
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Run()
    {
        GraphWindow window=null;
        try
        {
            window=ScriptableObject.CreateInstance<GraphWindow>();window.Show();
            typeof(GraphWindow).GetField("livePreview",Private).SetValue(window,false);
            typeof(GraphWindow).GetField("autoScene",Private).SetValue(window,false);
            for(int cycle=0;cycle<3;cycle++)
            {
                var graph=GraphSamples.CreateDefault();
                typeof(GraphWindow).GetField("graph",Private).SetValue(window,graph);
                typeof(GraphWindow).GetMethod("Rebuild",Private).Invoke(window,null);
                var states=(IDictionary)typeof(GraphWindow).GetField("inlineThumbnails",Private).GetValue(window);
                var resources=new List<UnityEngine.Object>();
                foreach(var state in states.Values.Cast<object>().Take(3))
                {
                    var preview=GraphPreview.Create(graph,null);
                    var editor=UnityEditor.Editor.CreateEditor(preview.Material);
                    state.GetType().GetField("Preview").SetValue(state,preview);
                    state.GetType().GetField("Editor").SetValue(state,editor);
                    state.GetType().GetField("Enabled").SetValue(state,true);
                    resources.Add(preview.Material);resources.Add(preview.Material.shader);resources.Add(editor);
                }
                if(resources.Count<3)throw new Exception("No thumbnail resources exercised");
                graph.Nodes.Clear();graph.Connections.Clear();
                typeof(GraphWindow).GetMethod("Rebuild",Private).Invoke(window,null);
                if(states.Count!=0||resources.Any(value=>value!=null))throw new Exception("Deleted thumbnails retained Unity objects while preview paused");
            }
            Debug.Log("NXSG THUMBNAIL LIFETIME PASSED: repeated paused deletion releases thumbnail editors, materials and shaders");
            window.DiscardChanges();window.Close();EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);if(window!=null){window.DiscardChanges();window.Close();}EditorApplication.Exit(1);}
    }
}
