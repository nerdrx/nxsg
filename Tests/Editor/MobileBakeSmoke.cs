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
            graph.Nodes.Single(n => n.Operation == "core.toonSurface").Operation = "core.pbrSurface";
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
            if (!message.Contains("Mobile snapshot")) throw new Exception("No bake result");
            if (GraphJson.Serialize(graph) != before) throw new Exception("Bake mutated graph");
            MobileMaterialSwap.SyncOpenScenesForTarget(true);
            var mobile = renderer.sharedMaterial;
            if (mobile == desktop || mobile.shader.name != "VRChat/Mobile/Toon Standard") throw new Exception("Android material not assigned");
            if (mobile.GetTexture("_MainTex") == null) throw new Exception("Baked albedo missing");
            var baked = new Texture2D(2, 2);
            baked.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(mobile.GetTexture("_MainTex"))));
            if (baked.GetPixel(1, 1).r < .8f || baked.GetPixel(1, 1).g > .2f) throw new Exception("Material texture override was not baked");
            UnityEngine.Object.DestroyImmediate(baked);
            var surface = graph.Nodes.Single(n => n.Operation == "core.pbrSurface");
            var opacity = NodeCatalog.Create("core.value"); opacity.Properties["value"] = .25; graph.Nodes.Add(opacity);
            var displacement = NodeCatalog.Create("core.value"); displacement.Properties["value"] = .1; graph.Nodes.Add(displacement);
            graph.Connections.Add(Link(opacity.Id, "value", surface.Id, "opacity"));
            graph.Connections.Add(Link(displacement.Id, "value", surface.Id, "displacement"));
            surface.Properties["opacity"] = .25;
            surface.Properties["useAlbedoAlpha"] = 1;
            surface.Properties["cutoff"] = .5;
            surface.Properties["displacement"] = .1;
            source.SetPixels(Enumerable.Repeat(new Color(1, 0, 0, .25f), 4).ToArray()); source.Apply();
            var opaqueBefore = GraphJson.Serialize(graph);
            var opaqueResult = MobileBake.Bake(graph, Path.GetFullPath(GraphPath), desktop, 32);
            if (!opaqueResult.Contains("opacity and cutout flattened to opaque") || !opaqueResult.Contains("displacement geometry omitted"))
                throw new Exception("Mobile bake did not report desktop-only surface controls");
            if (GraphJson.Serialize(graph) != opaqueBefore) throw new Exception("Mobile bake changed desktop surface controls");
            baked = new Texture2D(2, 2); baked.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(mobile.GetTexture("_MainTex"))));
            var opaquePixel = baked.GetPixel(1, 1);
            if (opaquePixel.a < .99f || opaquePixel.r < .8f)
                throw new Exception("Mobile albedo lost color or retained desktop transparency: " + opaquePixel);
            UnityEngine.Object.DestroyImmediate(baked);
            source.SetPixels(Enumerable.Repeat(Color.red, 4).ToArray()); source.Apply();
            var oldAlbedo = graph.Connections.Single(e => e.To.NodeId == surface.Id && e.To.PortId == "albedo");
            graph.Connections.Remove(oldAlbedo);
            var time = NodeCatalog.Create("core.time"); graph.Nodes.Add(time);
            var blue = NodeCatalog.Create("core.constant"); blue.Properties["valueType"]="color";
            blue.Properties["value"]=new JArray(0,0,1,1);graph.Nodes.Add(blue);
            var mix = NodeCatalog.Create("core.mix"); graph.Nodes.Add(mix);
            graph.Connections.Add(Link(oldAlbedo.From.NodeId, oldAlbedo.From.PortId, mix.Id, "a"));
            graph.Connections.Add(Link(blue.Id, "value", mix.Id, "b"));
            graph.Connections.Add(Link(time.Id, "value", mix.Id, "factor"));
            graph.Connections.Add(Link(mix.Id, "value", surface.Id, "albedo"));
            var snapshotMessage = MobileBake.Bake(graph, Path.GetFullPath(GraphPath), desktop, 32, .5f);
            if (!snapshotMessage.Contains("0.50 s")) throw new Exception("Snapshot time was not reported");
            baked=new Texture2D(2,2);
            baked.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(mobile.GetTexture("_MainTex"))));
            var mixed=baked.GetPixel(1,1);
            if (mixed.r<.2f || mixed.b<.2f) throw new Exception("Snapshot omitted a texture or animated mix: "+mixed);
            UnityEngine.Object.DestroyImmediate(baked);
            var roughness=NodeCatalog.Create("core.value");roughness.Properties["value"]=.25;graph.Nodes.Add(roughness);
            graph.Connections.Add(Link(roughness.Id,"value",surface.Id,"roughness"));
            var normalColor=NodeCatalog.Create("core.constant");normalColor.Properties["valueType"]="color";
            normalColor.Properties["value"]=new JArray(.5,.5,1,1);graph.Nodes.Add(normalColor);
            var normalNode=NodeCatalog.Create("core.normalMap");graph.Nodes.Add(normalNode);
            graph.Connections.Add(Link(normalColor.Id,"value",normalNode.Id,"color"));
            graph.Connections.Add(Link(normalNode.Id,"normal",surface.Id,"normal"));
            var particles=NodeCatalog.Create("core.surfaceParticles");graph.Nodes.Add(particles);
            var output=graph.Nodes.Single(n=>n.Operation=="core.output");
            graph.Connections.RemoveAll(e=>e.To.NodeId==output.Id&&e.To.PortId=="surface");
            graph.Connections.Add(Link(surface.Id,"surface",particles.Id,"base"));
            graph.Connections.Add(Link(particles.Id,"surface",output.Id,"surface"));
            var wrapped=MobileBake.Bake(graph,Path.GetFullPath(GraphPath),desktop,32,.5f);
            if (!wrapped.Contains("Surface Particles geometry omitted")) throw new Exception("Surface particle wrapper did not bake base surface");
            var gloss=mobile.GetTexture("_GlossMap");
            if (gloss==null) throw new Exception("Roughness did not become mobile gloss map");
            if (mobile.GetTexture("_BumpMap")==null) throw new Exception("Normal did not become mobile normal map");
            if (((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mobile.GetTexture("_BumpMap")))).textureType!=TextureImporterType.NormalMap)
                throw new Exception("Baked normal was not imported as a normal map");
            baked=new Texture2D(2,2);baked.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(gloss)));
            if(Mathf.Abs(baked.GetPixel(1,1).r-.75f)>.1f)throw new Exception("Mobile gloss map wrong: "+baked.GetPixel(1,1));
            UnityEngine.Object.DestroyImmediate(baked);
            var ltcgi=NodeCatalog.Create("core.ltcgi");graph.Nodes.Add(ltcgi);
            graph.Connections.Add(Link(blue.Id,"value",ltcgi.Id,"albedo"));
            var passed=GraphBaker.RenderSnapshot(graph,ltcgi.Id,"color",32,desktop,.5f,false,0);
            if(passed.GetPixel(16,16).b<.8f || passed.GetPixel(16,16).r>.2f)
                throw new Exception("LTCGI input color was not passed through");
            UnityEngine.Object.DestroyImmediate(passed);
            graph.Connections.RemoveAll(e=>e.To.NodeId==surface.Id&&e.To.PortId=="albedo");
            MobileBake.Bake(graph,Path.GetFullPath(GraphPath),desktop,32,.5f);
            baked=new Texture2D(2,2);
            baked.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(mobile.GetTexture("_MainTex"))));
            var emptyAlbedo=baked.GetPixel(0,0);
            if(emptyAlbedo.r>.01f || emptyAlbedo.g>.01f || emptyAlbedo.b>.01f || emptyAlbedo.a<.99f)
                throw new Exception("Disconnected albedo should bake opaque black: "+emptyAlbedo);
            UnityEngine.Object.DestroyImmediate(baked);
            MobileMaterialSwap.SyncOpenScenesForTarget(false);
            if (renderer.sharedMaterial != desktop) throw new Exception("Desktop material not restored");
            if (desktop.shader.name == "VRChat/Mobile/Toon Standard") throw new Exception("Desktop material changed");
            UnityEngine.Object.DestroyImmediate(source);
            VerifyTextureSlotRemap();
            VerifyRollback();
            Debug.Log("NXSG MOBILE BAKE SMOKE PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static void VerifyTextureSlotRemap()
    {
        const string path="Assets/NXSGTextureSlotSmoke.nxsg";
        var graph=GraphSamples.CreateDefault();
        var mask=NodeCatalog.Create("core.texture2D");
        mask.Id="a-mask"; mask.Properties["resourceId"]="mask"; graph.Nodes.Add(mask);
        graph.Resources.Add(new GraphResource { Id="mask", Kind="texture2D", Uri="builtin://white" });
        graph.Connections.Add(Link(mask.Id,"color","toon","emission"));
        File.WriteAllText(path,GraphJson.Serialize(graph));
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var material=GraphBuild.Build(graph,Path.GetFullPath(path));
        var albedoLabel=TextureSlotLabels.DisplayName(graph,"white");
        string albedoProperty=null;
        for(var i=0;i<material.shader.GetPropertyCount();i++)
            if(material.shader.GetPropertyDescription(i)==albedoLabel)
                albedoProperty=material.shader.GetPropertyName(i);
        if(albedoProperty==null || albedoProperty=="_MainTex")
            throw new Exception("Texture-slot regression fixture did not promote a different resource to _MainTex");
        var maskTexture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
        var colorTexture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
        try
        {
            maskTexture.SetPixels(Enumerable.Repeat(Color.green,4).ToArray());maskTexture.Apply();
            colorTexture.SetPixels(Enumerable.Repeat(Color.red,4).ToArray());colorTexture.Apply();
            material.SetTexture("_MainTex",maskTexture);
            material.SetTexture(albedoProperty,colorTexture);
            var baked=GraphBaker.RenderSnapshot(graph,"texture","color",32,material,0,false,0);
            try
            {
                var pixel=baked.GetPixel(16,16);
                if(pixel.r<.8f || pixel.g>.2f)
                    throw new Exception("Pruned _MainTex took another resource's texture: "+pixel);
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(maskTexture);
            UnityEngine.Object.DestroyImmediate(colorTexture);
        }
    }

    static void VerifyRollback()
    {
        const string path = "Assets/NXSGMobileRollbackSmoke.nxsg";
        const string registryPath = "Assets/NXSGGenerated/MobileBakes.asset";
        var graph = GraphSamples.CreateDefault();
        var surface = graph.Nodes.Single(n => n.Operation == "core.toonSurface");
        surface.Operation = "core.pbrSurface";
        graph.Connections.RemoveAll(e => e.To.NodeId == surface.Id && e.To.PortId == "albedo");
        var color = NodeCatalog.Create("core.constant"); color.Properties["value"] = new JArray(1, 0, 0, 1); graph.Nodes.Add(color);
        graph.Connections.Add(Link(color.Id, "value", surface.Id, "albedo"));
        graph.Connections.Add(Link(color.Id, "value", surface.Id, "emission"));
        foreach (var port in new[] { "metallic", "roughness", "occlusion" })
        {
            var value = NodeCatalog.Create("core.value"); value.Properties["value"] = .4; graph.Nodes.Add(value);
            graph.Connections.Add(Link(value.Id, "value", surface.Id, port));
        }
        var normalColor = NodeCatalog.Create("core.constant"); normalColor.Properties["value"] = new JArray(.5, .5, 1, 1); graph.Nodes.Add(normalColor);
        var normal = NodeCatalog.Create("core.normalMap"); graph.Nodes.Add(normal);
        graph.Connections.Add(Link(normalColor.Id, "value", normal.Id, "color"));
        graph.Connections.Add(Link(normal.Id, "normal", surface.Id, "normal"));
        File.WriteAllText(path, GraphJson.Serialize(graph));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var desktop = GraphBuild.Build(graph, Path.GetFullPath(path));
        MobileBake.Bake(graph, Path.GetFullPath(path), desktop, 32);
        var registry = AssetDatabase.LoadAssetAtPath<MobileBakeRegistry>(registryPath);
        var mobile = registry.entries.Single(e => e.desktop == desktop).mobile;
        var directory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(mobile));
        var savedFiles = Directory.GetFiles(directory).Concat(new[] { registryPath, registryPath + ".meta" })
            .ToDictionary(file => file, File.ReadAllBytes);
        var registryJson = EditorJsonUtility.ToJson(registry);
        color.Properties["value"] = new JArray(0, 1, 0, 1);
        foreach (var unsavedEdit in new[] { false, true })
        {
            if (unsavedEdit) { mobile.SetFloat("_MetallicStrength", .37f); EditorUtility.SetDirty(mobile); }
            var materialJson = EditorJsonUtility.ToJson(mobile);
            foreach (var phase in new[] { "texture-imported:Albedo", "texture-imported:Emission", "texture-imported:Metallic", "texture-imported:Gloss", "texture-imported:Occlusion", "texture-imported:Normal", "material-updated", "registry-updated", "assets-saved" })
            {
                MobileBake.Checkpoint = checkpoint => { if (checkpoint == phase) throw new InvalidOperationException("injected mobile bake failure"); };
                var failed = false;
                try { MobileBake.Bake(graph, Path.GetFullPath(path), desktop, 32); }
                catch (InvalidOperationException exception) { failed = exception.Message == "injected mobile bake failure"; }
                finally { MobileBake.Checkpoint = null; }
                if (!failed) throw new Exception("Mobile bake injection did not fail at " + phase);
                foreach (var file in savedFiles)
                    if (!File.Exists(file.Key) || !File.ReadAllBytes(file.Key).SequenceEqual(file.Value))
                        throw new Exception("Mobile rollback changed " + file.Key + " at " + phase);
                if (EditorJsonUtility.ToJson(mobile) != materialJson || EditorJsonUtility.ToJson(registry) != registryJson)
                {
                    Directory.CreateDirectory("Library/NXSG/MobileRollback");
                    File.WriteAllText("Library/NXSG/MobileRollback/material-before.json", materialJson);
                    File.WriteAllText("Library/NXSG/MobileRollback/material-after.json", EditorJsonUtility.ToJson(mobile));
                    File.WriteAllText("Library/NXSG/MobileRollback/registry-before.json", registryJson);
                    File.WriteAllText("Library/NXSG/MobileRollback/registry-after.json", EditorJsonUtility.ToJson(registry));
                    throw new Exception("Mobile rollback changed live material/registry state at " + phase);
                }
                if (EditorUtility.IsDirty(mobile) != unsavedEdit || EditorUtility.IsDirty(registry))
                    throw new Exception("Mobile rollback changed material/registry dirty state at " + phase + " (unsaved=" + unsavedEdit + ")");
            }
        }
        MobileBake.Bake(graph, Path.GetFullPath(path), desktop, 32);
        var albedoPath = directory + "/Albedo.png";
        if (File.ReadAllBytes(albedoPath).SequenceEqual(savedFiles[albedoPath]))
            throw new Exception("Successful mobile rebake did not promote changed albedo after rollback");
        if (AssetDatabase.LoadAssetAtPath<Material>(directory + "/Mobile.mat") != mobile)
            throw new Exception("Mobile rebake replaced the stable material identity");

        const string otherPath = "Assets/NXSGMobileRollbackOther.mat";
        var other = new Material(desktop); AssetDatabase.CreateAsset(other, otherPath);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(other, out string otherGuid, out long otherId);
        var otherDirectory = Path.Combine(Path.GetDirectoryName(directory), otherGuid + "-" + otherId);
        registryJson = EditorJsonUtility.ToJson(registry);
        MobileBake.Checkpoint = phase => { if (phase == "assets-saved") throw new InvalidOperationException("injected first bake failure"); };
        var firstFailed = false;
        try { MobileBake.Bake(graph, Path.GetFullPath(path), other, 32); }
        catch (InvalidOperationException exception) { firstFailed = exception.Message == "injected first bake failure"; }
        finally { MobileBake.Checkpoint = null; }
        if (!firstFailed || Directory.GetFiles(otherDirectory).Length != 0 || EditorJsonUtility.ToJson(registry) != registryJson)
            throw new Exception("Failed first mobile bake retained generated assets or changed the registry");
        var blockedPath = Path.Combine(otherDirectory, "Metallic.png");
        Directory.CreateDirectory(blockedPath);
        var blocked = false;
        try { MobileBake.Bake(graph, Path.GetFullPath(path), other, 32); }
        catch (IOException) { blocked = true; }
        if (!blocked || File.Exists(Path.Combine(otherDirectory, "Albedo.png")) || !Directory.Exists(blockedPath) ||
            EditorJsonUtility.ToJson(registry) != registryJson)
            throw new Exception("A blocked later texture path damaged earlier bake outputs or the registry");
        Directory.Delete(blockedPath);
    }

    static GraphConnection Link(string source, string output, string target, string input) => new GraphConnection
    {
        Id = Guid.NewGuid().ToString("N"),
        From = new GraphPortRef { NodeId = source, PortId = output },
        To = new GraphPortRef { NodeId = target, PortId = input }
    };
}
