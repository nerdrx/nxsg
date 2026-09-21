using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Backend;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
public static class TextureLabelSmoke
{
    static GraphWindow window;static int ticks,phase;const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(string n)=>typeof(GraphWindow).GetField(n,F).GetValue(window);
    static void Set(string n,object v)=>typeof(GraphWindow).GetField(n,F).SetValue(window,v);
    static void Call(string n,params object[] args)=>typeof(GraphWindow).GetMethod(n,F).Invoke(window,args);
    public static void Run(){window=ScriptableObject.CreateInstance<GraphWindow>();window.Show();window.position=new Rect(0,0,1100,760);Set("livePreview",false);Set("autoScene",false);Call("NewGraph");EditorApplication.update+=Tick;}
    static void Tick(){if(++ticks%40!=0)return;try{
        if(phase==0){Call("SelectNode","texture",false);Call("RebuildInspector");var field=((VisualElement)Get("inspector")).Query<TextField>().ToList().Single(f=>f.label=="Slot name");field.value="Body albedo";phase++;return;}
        var g=(ShaderGraph)Get("graph");var node=g.Nodes.Single(n=>n.Id=="texture");var resource=g.Resources.Single(r=>r.Id==(string)node.Properties["resourceId"]);Require(resource.Name=="Body albedo","Rename field did not update resource");
        var nodes=(Dictionary<string,VisualElement>)Get("nodes");Require(nodes["texture"].Q<Label>().text=="Body albedo [1]","Node title differs from slot");
        ShaderUtil.allowAsyncCompilation=false;
        foreach(bool advanced in new[]{false,true}){
            if(advanced)g.Nodes.Single(n=>n.Id=="toon").Properties["opacity"]=1;
            resource.Name=null;var first=ShaderUtil.CreateShaderAsset(ShaderEmitter.Emit(g).ShaderSource,true);var m=new Material(first);var texture=new Texture2D(2,2);m.SetTexture("_MainTex",texture);
            resource.Name="Body albedo";var second=ShaderUtil.CreateShaderAsset(ShaderEmitter.Emit(g).ShaderSource,true);
            try{m.shader=second;m.SetPass(0);Require(!ShaderUtil.ShaderHasError(second),"Renamed shader failed");Require(m.GetTexture("_MainTex")==texture,"Texture assignment lost");bool found=false;for(int i=0;i<ShaderUtil.GetPropertyCount(second);i++)if(ShaderUtil.GetPropertyName(second,i)=="_MainTex")found=ShaderUtil.GetPropertyDescription(second,i)=="Body albedo [1]";Require(found,"Material label differs from node");}
            finally{UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);}
        }
        Debug.Log("NXSG TEXTURE LABELS PASSED: editor rename, matching labels, material texture retained in both backends");Finish(0);
    }catch(Exception e){Debug.LogException(e);Finish(1);}}
    static void Require(bool x,string m){if(!x)throw new Exception(m);}
    static void Finish(int result){EditorApplication.update-=Tick;window.DiscardChanges();window.Close();EditorApplication.Exit(result);}
}
