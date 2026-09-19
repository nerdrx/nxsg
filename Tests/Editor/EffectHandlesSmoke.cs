using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;

// Run in Unity editor with -executeMethod EffectHandlesSmoke.Run.
public static class EffectHandlesSmoke
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static EffectHandlesWindow window; static ShaderGraph graph; static GraphSession session; static int phase;
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static object Invoke(string name, params object[] args) { return typeof(EffectHandlesWindow).GetMethod(name, Private).Invoke(window, args); }
    public static void Run()
    {
        graph = BuildGraph(); session = ScriptableObject.CreateInstance<GraphSession>(); session.json = GraphJson.Serialize(graph, true);
        window = EffectHandlesWindow.Open("sticker", () => graph, (name, action) => { Undo.RegisterCompleteObjectUndo(session, name); action(); session.json = GraphJson.Serialize(graph, true); EditorUtility.SetDirty(session); });
        EditorApplication.update += Tick;
    }
    static double next;
    static Vector2 initial;
    static void Mouse(EventType type,Vector2 point,int button=0) { window.SendEvent(new Event { type=type,mousePosition=point,button=button }); }
    static Vector2 Position() { var p=graph.Nodes.Single(n=>n.Id=="sticker").Properties["position"];return new Vector2((float)p[0],(float)p[1]); }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+1;
        try
        {
            var rect=(Rect)typeof(EffectHandlesWindow).GetField("previewRect",Private).GetValue(window);
            if(rect.width<50)return;
            if(phase==0)
            {
                initial=Position();Mouse(EventType.MouseDown,rect.center);Mouse(EventType.MouseUp,rect.center);
                Require((Position()-initial).sqrMagnitude>.001,"Real mesh click did not place sticker");
                typeof(EffectHandlesWindow).GetField("meshMode",Private).SetValue(window,false);window.Repaint();phase++;return;
            }
            if(phase==1)
            {
                Invoke("Apply","Reset placement",Vector2.zero,new Vector2(.4f,.3f),0f);
                initial=Position();var center=EffectHandlesMath.UvToGui(rect,new Vector2(.5f,.5f));var start=center+new Vector2(20,0);
                Mouse(EventType.MouseDown,start);Mouse(EventType.MouseDrag,start+new Vector2(10,0));Mouse(EventType.MouseDrag,start+new Vector2(20,0));Mouse(EventType.MouseUp,start+new Vector2(20,0));
                Require(Mathf.Abs(Position().x-20/rect.width)<.002,"UV drag accumulated wrong delta: "+Position());
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();graph=GraphJson.Parse(session.json);Invoke("RefreshAfterUndo");
                Require(Position().sqrMagnitude<.0001,"Drag Undo did not restore original offset");
                Require(Invoke("CurrentNode")!=null,"Undo lost node reference");
                phase++;window.Repaint();return;
            }
            if(phase==2)
            {
                var center=rect.center;var corner=center+new Vector2(rect.width*.2f,-rect.height*.15f);
                Mouse(EventType.MouseDown,corner);Mouse(EventType.MouseDrag,corner+new Vector2(10,-10));Mouse(EventType.MouseUp,corner+new Vector2(10,-10));
                var size=graph.Nodes.Single(n=>n.Id=="sticker").Properties["size"];Require((float)size[0]>.4f && (float)size[1]>.3f,"Corner resize failed");
                Mouse(EventType.MouseDown,center+new Vector2(0,28));Mouse(EventType.MouseDrag,center+new Vector2(28,0));Mouse(EventType.MouseUp,center+new Vector2(28,0));
                Require(Mathf.Abs((float)graph.Nodes.Single(n=>n.Id=="sticker").Properties["rotation"]-90)<.1,"Rotation handle failed");
                phase++;window.Repaint();return;
            }
            var mesh=new Mesh {vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(-1,1,0)},triangles=new[]{0,1,2},uv=new[]{Vector2.zero,Vector2.right,Vector2.up}};
            var hit=EffectHandlesMath.RaycastUv(mesh,new Ray(new Vector3(-.25f,-.25f,-1),Vector3.forward),Matrix4x4.identity);
            Require(hit.HasValue && (hit.Value-new Vector2(.375f,.375f)).sqrMagnitude<.0001,"Barycentric UV pick incorrect");UnityEngine.Object.DestroyImmediate(mesh);
            var capture=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("gamescopectl","screenshot /tmp/nxsg-effect-handles.png"){UseShellExecute=false});capture.WaitForExit(5000);
            EditorApplication.update-=Tick;window.Close();UnityEngine.Object.DestroyImmediate(session);Debug.Log("NXSG EFFECT HANDLES SMOKE PASSED");EditorApplication.Exit(0);
        }
        catch(Exception e) { EditorApplication.update-=Tick;if(window!=null)window.Close();if(session!=null)UnityEngine.Object.DestroyImmediate(session);Debug.LogException(e);EditorApplication.Exit(1); }
    }
    static ShaderGraph BuildGraph()
    {
        var graph = new ShaderGraph { GraphId = "effect-handles-smoke", Layout = new GraphLayout { Nodes = new Dictionary<string, GraphNodeLayout>() } }; graph.Resources.Add(new GraphResource { Id = "white", Kind = "texture2D", Uri = "builtin://white" });
        graph.Nodes.Add(new GraphNode { Id = "uv0", Operation = "core.uv0", Properties = new JObject() }); graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.constant", Properties = new JObject { ["valueType"] = "color", ["value"] = new JArray(.015,.04,.1,1) } }); graph.Nodes.Add(new GraphNode { Id = "sticker", Operation = "core.sticker", Properties = new JObject { ["resourceId"] = "white", ["position"] = new JArray(.2, .3), ["size"] = new JArray(.4, .5), ["rotation"] = 0.0, ["mask"] = 1.0 } }); graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.unlitSurface", Properties = new JObject() }); graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output", Properties = new JObject() });
        Edge(graph, "texture", "value", "sticker", "base", "texture-sticker"); Edge(graph, "uv0", "uv", "sticker", "uv", "uv-sticker"); Edge(graph, "sticker", "color", "toon", "albedo", "sticker-toon"); Edge(graph, "toon", "surface", "output", "surface", "toon-output"); return graph;
    }
    static void Edge(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id) { graph.Connections.Add(new GraphConnection { Id = id, From = new GraphPortRef { NodeId = from, PortId = fromPort }, To = new GraphPortRef { NodeId = to, PortId = toPort } }); }
}
