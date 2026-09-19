using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
public static class InlineGoodiesSmoke
{
    static GraphWindow window; static int ticks,phase; static string before; static object state;
    public static void Run()
    {
        window=ScriptableObject.CreateInstance<GraphWindow>();window.Show();window.position=new Rect(0,0,1100,800);window.Focus();
        var graph=new ShaderGraph{GraphId="inline-goodies"};var ramp=NodeCatalog.Create("core.colorRamp");ramp.Id="ramp";graph.Nodes.Add(ramp);
        Set(window,"graph",graph); ((GraphSession)Get(window,"session")).json=GraphJson.Serialize(graph,true);Invoke(window,"Rebuild");EditorApplication.update+=Tick;
    }
    static void Tick()
    {
        if(++ticks%20!=0)return;
        try
        {
            var session=(GraphSession)Get(window,"session");
            if(phase==0)
            {
                before=session.json;
                var field=window.rootVisualElement.Query<GradientField>().First();var edited=new Gradient();edited.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.white,1)},field.value.alphaKeys);field.value=edited;
                Require(session.json!=before,"Gradient UI did not update graph");Undo.FlushUndoRecordObjects();Undo.PerformUndo();phase++;
            }
            else if(phase==1)
            {
                Require(session.json==before,"Gradient undo did not restore graph");
                window.rootVisualElement.Query<Toggle>().ToList().First(t=>t.label=="Thumbnail").value=true;
                phase++;
            }
            else if(phase==2)
            {
                Set(window,"inlinePreviewDue",0d);Invoke(window,"TickInlinePreviews");
                var map=(IDictionary)Get(window,"inlineThumbnails");state=map["ramp"];Require(Get(state,"Preview")!=null,"Thumbnail preview missing");
                Require(session.json==before&&GraphJson.Serialize((ShaderGraph)Get(window,"graph"),true)==before,"Thumbnail modified graph");
                Invoke(window,"Rebuild");phase++;
            }
            else
            {
                var host=(VisualElement)Get(state,"Host");Require(host.panel!=null,"Thumbnail lost after rebuild");
                Invoke(window,"DisposeInlinePreviews");Require(Get(state,"Preview")==null&&Get(state,"Editor")==null,"Thumbnail resources not disposed");
                Debug.Log("NXSG INLINE GOODIES SMOKE PASSED: real gradient edit/undo, thumbnail graph isolation, rebuild attachment and disposal");Finish(0);
            }
        }
        catch(Exception e){Debug.LogException(e);Finish(1);}
    }
    static void Finish(int code){EditorApplication.update-=Tick;window.DiscardChanges();window.Close();EditorApplication.Exit(code);}
    static void Require(bool ok,string text){if(!ok)throw new Exception(text);}
    static object Get(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    static void Invoke(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
}
