using System;
using System.Linq;
using System.Reflection;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
public static class CoordinateMenuSmoke
{
    public static void Run()
    {
        GraphWindow window = null;
        try
        {
            window = ScriptableObject.CreateInstance<GraphWindow>(); window.Show();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Action<string, object[]> call = (name,args) => typeof(GraphWindow).GetMethod(name,flags).Invoke(window,args);
            typeof(GraphWindow).GetField("livePreview",flags).SetValue(window,false);
            typeof(GraphWindow).GetField("autoScene",flags).SetValue(window,false);
            call("NewGraph",new object[0]);
            Func<ShaderGraph> graph = () => (ShaderGraph)typeof(GraphWindow).GetField("graph",flags).GetValue(window);
            foreach(var source in new[]{"uv0","uv1","uv2","uv3","object","world","polar","panosphere","matcap"})
            {
                call("SwitchCoordinateSource",new object[]{"uv0",source});
                var g=graph(); var node=g.Nodes.Single(n=>n.Id=="uv0");
                if(node.Operation!="core.uv0" || ((string)node.Properties["coordinateSource"]??"uv0")!=source) throw new Exception("Wrong source "+source);
                if(!g.Connections.Any(e=>e.From.NodeId=="uv0" && e.To.NodeId=="texture"))throw new Exception("UV wire lost");
                var menu=(GenericMenu)typeof(GraphWindow).GetMethod("BuildOperationMenu",flags).Invoke(window,new object[]{node});
                if(menu.GetItemCount()<15)throw new Exception("Missing menu choices");
            }
            call("SwitchOperation",new object[]{"uv0","core.polarUV"});
            call("SwitchCoordinateSource",new object[]{"uv0","panosphere"});
            var pano=graph().Nodes.Single(n=>n.Id=="uv0");
            var title=(string)typeof(GraphWindow).GetMethod("NodeTitle",flags).Invoke(window,new object[]{pano});
            if(title!="Panosphere")throw new Exception("Missing source title");
            var saved=GraphJson.Serialize(graph());
            if(!saved.Contains("panosphere"))throw new Exception("Source not serialized");
            Debug.Log("NXSG COORDINATE MENU PASSED: all sources, transform switching, wires, titles, serialization");
            window.DiscardChanges();window.Close();EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);if(window!=null){window.DiscardChanges();window.Close();}EditorApplication.Exit(1);}
    }
}
