using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        void AddCreatorMenu(Toolbar toolbar)
        {
            var tools = new ToolbarMenu { text="Create", tooltip="Preview, import, bake and organize your material" };
            tools.menu.AppendAction("Material playground / compare", _ => OpenMaterialPlayground());
            tools.menu.AppendAction("Why does the scene look different?", _ => ShowPreviewDiagnostics());
            tools.menu.AppendAction("Bookmark current view…", _ => BookmarkDialog());
            tools.menu.AppendAction("Jump to bookmark…", _ => BookmarkMenu());
            tools.menu.AppendAction("Bake selected output to texture…", _ => BakeSelected());
            AddTextureToolsMenu(tools);
            AddMaterialToolsMenu(tools);
            toolbar.Add(tools);
        }

        void OpenMaterialPlayground()
        {
            if(graph==null)return;
            try{MaterialPlayground.ShowGraph(graph,contextMaterial);}
            catch(Exception e){SetStatus("Playground needs attention: "+e.Message);}
        }

        void BookmarkDialog()
        {
            if(graph==null)return;
            var box=new VisualElement { style={position=UnityEngine.UIElements.Position.Absolute,left=20,top=45,width=270,paddingLeft=10,paddingRight=10,paddingTop=10,paddingBottom=10,backgroundColor=new Color(.16f,.16f,.16f)} };
            var name=new TextField("Bookmark") { value="New view" }; box.Add(name);
            box.Add(new Button(()=> { if(string.IsNullOrWhiteSpace(name.value))return; SaveBookmark(name.value.Trim().Substring(0,Math.Min(80,name.value.Trim().Length))); box.RemoveFromHierarchy(); }){text="Save view"});
            box.Add(new Button(()=>box.RemoveFromHierarchy()){text="Cancel"}); rootVisualElement.Add(box); name.Focus();
        }
        void SaveBookmark(string name)
        {
            Edit("Save graph bookmark",()=> {
                if(graph.Layout==null)graph.Layout=new GraphLayout();
                if(graph.Layout.ExtensionData==null)graph.Layout.ExtensionData=new System.Collections.Generic.Dictionary<string,JToken>();
                if(!graph.Layout.ExtensionData.ContainsKey("bookmarks") || !(graph.Layout.ExtensionData["bookmarks"] is JObject))graph.Layout.ExtensionData["bookmarks"]=new JObject();
                graph.Layout.ExtensionData["bookmarks"][name]=new JObject { ["panX"]=pan.x,["panY"]=pan.y,["zoom"]=zoom };
            });
        }
        void BookmarkMenu()
        {
            var menu=new GenericMenu(); var bookmarks=graph?.Layout?.ExtensionData != null && graph.Layout.ExtensionData.TryGetValue("bookmarks",out var bookmarkData) ? bookmarkData as JObject : null;
            if(bookmarks==null||!bookmarks.Properties().Any())menu.AddDisabledItem(new GUIContent("No bookmarks yet"));
            else foreach(var item in bookmarks.Properties())
            {
                var key=item.Name; var data=item.Value;
                menu.AddItem(new GUIContent("Jump/"+key.Replace("/","∕")),false,()=> {
                    if(!(data is JObject) || new[]{"panX","panY","zoom"}.Any(k=>data[k]!=null&&data[k].Type!=JTokenType.Float&&data[k].Type!=JTokenType.Integer))return;
                    float x=(float?)data["panX"]??30,y=(float?)data["panY"]??70,z=(float?)data["zoom"]??1;
                    if(float.IsNaN(x)||float.IsInfinity(x)||float.IsNaN(y)||float.IsInfinity(y)||float.IsNaN(z)||float.IsInfinity(z))return;
                    pan=new Vector2(x,y);zoom=Mathf.Clamp(z,.25f,2);TransformCanvas();
                });
                menu.AddItem(new GUIContent("Delete/"+key.Replace("/","∕")),false,()=>Edit("Delete bookmark",()=>((JObject)graph.Layout.ExtensionData["bookmarks"]).Remove(key)));
            }
            menu.ShowAsContext();
        }
        void ShowPreviewDiagnostics()
        {
            ShowSidebarTab(2); problemsPanel.Clear();
            Action<string> note=text=>problemsPanel.Add(new HelpBox(text,HelpBoxMessageType.Info));
            note("Graph: "+(sourcePath??"Unsaved")+"\nMaterial: "+(contextMaterial!=null?contextMaterial.name:"neutral preview; no material selected"));
            note(hasUnsavedChanges?"Unsaved edits are in the preview. Save/build to apply them.":"Graph edits are saved.");
            note(sceneStatus!=null?sceneStatus.text:"No scene build status yet.");
            if(!string.IsNullOrEmpty(previewNodeId))
            {
                note("Preview is showing one node output, not the full material.");
                problemsPanel.Add(new Button(()=> {previewNodeId=previewNodePort=null;previewHash=null;QueueLivePreview();}){text="Return to full material preview"});
            }
            if(previewMessage!=null)note(previewMessage);
            note("Studio preview lights and mesh differ from your scene. Compare the same mesh, lights, reflection probes and color space. Material overrides are preserved when rebuilding.");
            if(contextMaterial!=null)
            {
                note("Material shader: "+(contextMaterial.shader!=null?contextMaterial.shader.name:"missing"));
                if(contextMaterial.shader!=null)
                {
                    var shaderPath=AssetDatabase.GetAssetPath(contextMaterial.shader);
                    if(File.Exists(shaderPath))
                    {
                        var header=File.ReadLines(shaderPath).FirstOrDefault();
                        if(header!=null && header.StartsWith("// NXSG graph hash: ",StringComparison.Ordinal))
                            note(header.Substring(20)==GraphJson.ComputeSemanticHash(graph)?"Generated shader matches the current graph content.":"Generated shader differs from this graph. Build to update the selected material.");
                    }
                    note("Color space: "+QualitySettings.activeColorSpace+" · Graphics API: "+SystemInfo.graphicsDeviceType);
                    var defaults=new Material(contextMaterial.shader);
                    try
                    {
                        var changes=new System.Collections.Generic.List<string>();var shader=contextMaterial.shader;
                        for(var i=0;i<shader.GetPropertyCount();i++)
                        {
                            if((shader.GetPropertyFlags(i)&UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector)!=0)continue;
                            var name=shader.GetPropertyName(i);bool different;
                            switch(shader.GetPropertyType(i))
                            {
                                case UnityEngine.Rendering.ShaderPropertyType.Color: different=contextMaterial.GetColor(name)!=defaults.GetColor(name);break;
                                case UnityEngine.Rendering.ShaderPropertyType.Vector: different=contextMaterial.GetVector(name)!=defaults.GetVector(name);break;
                                case UnityEngine.Rendering.ShaderPropertyType.Texture: different=contextMaterial.GetTexture(name)!=defaults.GetTexture(name)||contextMaterial.GetTextureScale(name)!=defaults.GetTextureScale(name)||contextMaterial.GetTextureOffset(name)!=defaults.GetTextureOffset(name);break;
                                case UnityEngine.Rendering.ShaderPropertyType.Int: different=contextMaterial.GetInteger(name)!=defaults.GetInteger(name);break;
                                default:different=!Mathf.Approximately(contextMaterial.GetFloat(name),defaults.GetFloat(name));break;
                            }
                            if(different)changes.Add(shader.GetPropertyDescription(i)+" ("+name+")");
                        }
                        note(changes.Count==0?"Material values match shader defaults.":"Values differing from shader defaults (including assigned textures):\n"+string.Join("\n",changes.Take(24))+(changes.Count>24?"\n…":""));
                    }
                    finally{DestroyImmediate(defaults);}
                }
                problemsPanel.Add(new Button(()=> {Selection.activeObject=contextMaterial;EditorGUIUtility.PingObject(contextMaterial);}){text="Inspect material overrides"});
                problemsPanel.Add(new Button(()=> { previewHash=null;QueueLivePreview();SetStatus("Preview refresh queued.");}){text="Refresh preview from material"});
            }
            problemsPanel.Add(new Button(Build){text="Save, build and refresh scene"});
            diagnosticsPending=false;
        }
        void BakeSelected()
        {
            var node=graph?.Nodes.FirstOrDefault(n=>n.Id==selected);
            if(node==null){SetStatus("Select a numeric or color node output to bake.");return;}
            var menu=new GenericMenu();
            foreach(var port in Ports(node.Operation,true))
            {
                var type=GraphTypes.PortType(graph,node,port,GraphTypes.Infer(graph));
                if(type!="float"&&type!="color")continue;
                foreach(var size in new[]{256,512,1024,2048})
                {var output=port;var resolution=size;menu.AddItem(new GUIContent(output+"/"+size+" × "+size),false,()=>{
                    var path=EditorUtility.SaveFilePanelInProject("Bake static UV branch","NXSG Bake","png","Creates a linear 8-bit PNG. HDR/negative values are clamped. Original graph stays editable.");
                    if(string.IsNullOrEmpty(path))return;
                    try {path=GraphBaker.Bake(graph,node.Id,output,path,resolution);SetStatus("Baked static UV branch: "+path+". Original graph preserved.");}
                    catch(Exception e){SetStatus("Bake blocked: "+e.Message);}
                });}
            }
            if(menu.GetItemCount()==0)menu.AddDisabledItem(new GUIContent("Choose a Float or Color output")); menu.ShowAsContext();
        }
    }
}
