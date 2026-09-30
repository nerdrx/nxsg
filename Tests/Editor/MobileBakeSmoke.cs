using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Core;
using NXSG.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MobileBakeSmoke
{
    const string ShaderPath = "Assets/NXSGMobileSmoke.shader";
    const string GraphPath = "Assets/NXSGMobileSmoke.nxsg";

    public static void Run()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (Shader.Find("VRChat/Mobile/Toon Standard") == null) File.WriteAllText(ShaderPath, @"Shader ""VRChat/Mobile/Toon Standard"" {
Properties { _Color(""Color"",Color)=(1,1,1,1) _MainTex(""Main"",2D)=""white""{}
_Culling(""Cull"",Float)=2 _EmissionMap(""Emission"",2D)=""black""{}
_EmissionColor(""Emission color"",Color)=(0,0,0,1) _EmissionStrength(""Strength"",Float)=1 }
SubShader { Pass { CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include ""UnityCG.cginc""
struct v {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
struct f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
sampler2D _MainTex;
f vert(v i){f o;o.vertex=UnityObjectToClipPos(i.vertex);o.uv=i.uv;return o;}
fixed4 frag(f i):SV_Target{return tex2D(_MainTex,i.uv);}
ENDCG } } }");
            if (File.Exists(ShaderPath)) AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            var graph = GraphSamples.CreateDefault();
            File.WriteAllText(GraphPath, GraphJson.Serialize(graph));
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport);
            var desktop = GraphBuild.Build(graph, Path.GetFullPath(GraphPath));
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); source.Apply();
            desktop.SetTexture("_MainTex", source);
            var subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = subject.GetComponent<Renderer>();
            renderer.sharedMaterial = desktop;
            var before = GraphJson.Serialize(graph);
            var message = MobileBake.Bake(graph, Path.GetFullPath(GraphPath), desktop, 32);
            if (!message.Contains("Mobile bake saved")) throw new Exception("No bake result");
            if (GraphJson.Serialize(graph) != before) throw new Exception("Bake mutated graph");
            MobileMaterialSwap.SyncOpenScenesForTarget(true);
            var mobile = renderer.sharedMaterial;
            if (mobile == desktop || mobile.shader.name != "VRChat/Mobile/Toon Standard") throw new Exception("Android material not assigned");
            if (mobile.GetTexture("_MainTex") == null) throw new Exception("Baked albedo missing");
            var baked = new Texture2D(2, 2);
            baked.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(mobile.GetTexture("_MainTex"))));
            if (baked.GetPixel(1, 1).r < .8f || baked.GetPixel(1, 1).g > .2f) throw new Exception("Material texture override was not baked");
            UnityEngine.Object.DestroyImmediate(baked);
            var surface = graph.Nodes.Single(n => n.Operation == "core.toonSurface");
            var oldAlbedo = graph.Connections.Single(e => e.To.NodeId == surface.Id && e.To.PortId == "albedo");
            graph.Connections.Remove(oldAlbedo);
            var time = NodeCatalog.Create("core.time"); graph.Nodes.Add(time);
            var add = NodeCatalog.Create("core.add"); graph.Nodes.Add(add);
            graph.Connections.Add(Link(oldAlbedo.From.NodeId, oldAlbedo.From.PortId, add.Id, "a"));
            graph.Connections.Add(Link(time.Id, "value", add.Id, "b"));
            graph.Connections.Add(Link(add.Id, "value", surface.Id, "albedo"));
            var fallbackMessage = MobileBake.Bake(graph, Path.GetFullPath(GraphPath), desktop, 32);
            if (!fallbackMessage.Contains("UV texture as the base")) throw new Exception("Dynamic albedo fallback was not reported");
            MobileMaterialSwap.SyncOpenScenesForTarget(false);
            if (renderer.sharedMaterial != desktop) throw new Exception("Desktop material not restored");
            if (desktop.shader.name == "VRChat/Mobile/Toon Standard") throw new Exception("Desktop material changed");
            UnityEngine.Object.DestroyImmediate(source);
            Debug.Log("NXSG MOBILE BAKE SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static GraphConnection Link(string source, string output, string target, string input) => new GraphConnection
    {
        Id = Guid.NewGuid().ToString("N"),
        From = new GraphPortRef { NodeId = source, PortId = output },
        To = new GraphPortRef { NodeId = target, PortId = input }
    };
}
