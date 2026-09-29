using System;
using System.IO;
using System.Linq;
using NXSG.Core;
using NXSG.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Run in the Unity fixture with -executeMethod MaterialTranslatorSmoke.Run.
public static class MaterialTranslatorSmoke
{
    const string Root = "Assets/NXSGMaterialTranslatorSmoke";
    const string TexturePath = Root + "/Source.png";
    const string ShaderPath = Root + "/Fixture.shader";

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Run()
    {
        try
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "NXSGMaterialTranslatorSmoke");
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            image.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            image.Apply();
            File.WriteAllBytes(TexturePath, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);

            File.WriteAllText(ShaderPath, @"Shader ""Poiyomi/SmokeFixture""
{
    Properties {
        _MainTex (""Main"", 2D) = ""white"" {} _Color (""Color"", Color) = (1,1,1,1)
        _BumpMap (""Normal"", 2D) = ""bump"" {} _BumpScale (""Normal scale"", Float) = 1
        _EnableEmission (""Emission 0"", Float) = 0 _EnableEmission1 (""Emission 1"", Float) = 0
        _EnableEmission2 (""Emission 2"", Float) = 0 _EmissionMap1 (""Emission map 1"", 2D) = ""white"" {}
        _EmissionMap2 (""Emission map 2"", 2D) = ""white"" {} _EmissionColor1 (""Emission color 1"", Color) = (1,1,1,1)
        _EmissionColor2 (""Emission color 2"", Color) = (1,0,1,1) _EmissionStrength1 (""Emission strength 1"", Float) = 1
        _EmissionStrength2 (""Emission strength 2"", Float) = 1 _EmissionMap1UV (""Emission UV"", Float) = 0
        _EmissionMap1Pan (""Emission pan"", Vector) = (0,0,0,0)
        _EmissionMask2 (""Emission mask 2"", 2D) = ""white"" {}
        _EnableAudioLink (""Audio Link"", Float) = 0 _EmissionAL1Enabled (""Emission audio"", Float) = 0
        _GlitterEnable (""Glitter"", Float) = 0 _GlitterALEnabled (""Glitter audio"", Float) = 0
        _GlitterMask (""Glitter mask"", 2D) = ""white"" {} _GlitterFrequency (""Glitter frequency"", Float) = 70
        _GlitterBrightness (""Glitter brightness"", Float) = 3 _GlitterSize (""Glitter size"", Float) = .25
        _GlitterSpeed (""Glitter speed"", Float) = 5 _GlitterContrast (""Glitter contrast"", Float) = 325
        _ClearCoatBRDF (""Clear coat enabled"", Float) = 0 _ClearCoatStrength (""Clear coat"", Float) = 0 _ClearCoatSmoothness (""Clear coat smoothness"", Float) = 1
        _AlphaForceOpaque (""Force opaque"", Float) = 0 _AlphaMask (""Alpha mask"", 2D) = ""white"" {}
        _AlphaMaskInvert (""Invert alpha"", Float) = 0 _AlphaMod (""Alpha adjustment"", Float) = 0
        _UseEmission (""lil emission"", Float) = 0 _UseEmission2nd (""lil emission 2"", Float) = 0
        _EmissionMap (""lil emission map"", 2D) = ""white"" {} _Emission2ndMap (""lil emission 2 map"", 2D) = ""white"" {}
        _EmissionColor (""lil emission color"", Color) = (1,1,1,1) _Emission2ndColor (""lil emission 2 color"", Color) = (1,1,1,1)
        _UseGlitter (""lil glitter"", Float) = 0 _UseMain2ndTex (""lil main 2"", Float) = 0
        _Main2ndTex (""lil main 2 map"", 2D) = ""white"" {} _Color2nd (""lil main 2 color"", Color) = (1,1,1,1)
        _UseShadow (""lil shadow"", Float) = 0 _UseOutline (""lil outline"", Float) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert(appdata_base v) : SV_POSITION { return UnityObjectToClipPos(v.vertex); }
            fixed4 frag() : SV_Target { return 1; }
            ENDCG
        }
    }
}");
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            var poiyomi = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Require(poiyomi != null, "Synthetic Poiyomi shader did not import.");
            VerifyImport("Poiyomi", poiyomi);
            VerifyRealMaterialSnapshot(poiyomi);

            var lilToonPath = Root + "/lilToonFixture.shader";
            File.WriteAllText(lilToonPath, File.ReadAllText(ShaderPath).Replace("Poiyomi/SmokeFixture", "lilToon/SmokeFixture"));
            AssetDatabase.ImportAsset(lilToonPath, ImportAssetOptions.ForceSynchronousImport);
            VerifyImport("lilToon", AssetDatabase.LoadAssetAtPath<Shader>(lilToonPath));

            Debug.Log("NXSG MATERIAL TRANSLATOR SMOKE PASSED: Poiyomi and lilToon, source GUID, backup, graph, texture binding");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static void VerifyRealMaterialSnapshot(Shader fixtureShader)
    {
        var originalPath = Environment.GetEnvironmentVariable("NXSG_TRANSLATOR_REAL_MATERIAL");
        if (string.IsNullOrEmpty(originalPath)) return;
        Require(File.Exists(originalPath), "Real source material snapshot is missing.");
        var copyPath = Root + "/RealPoiyomiSnapshot.mat";
        var sourceYaml = File.ReadAllText(originalPath);
        var shaderGuid = AssetDatabase.AssetPathToGUID(ShaderPath);
        sourceYaml = System.Text.RegularExpressions.Regex.Replace(sourceYaml,
            @"(?<=m_Shader: \{fileID: 4800000, guid: )[0-9a-f]{32}", shaderGuid);
        File.WriteAllText(copyPath, sourceYaml);
        AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);
        var material = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
        Require(material != null && material.shader == fixtureShader, "Real material snapshot did not load under the fixture shader.");
        Require(material.GetFloat("_EnableEmission1") > .5f && material.GetFloat("_EnableEmission2") > .5f &&
            material.GetFloat("_GlitterEnable") > .5f && material.GetFloat("_ClearCoatStrength") > 0,
            "Real material snapshot lost its enabled settings.");
        material.SetTexture("_EmissionMap1", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        material.SetTexture("_GlitterMask", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        var result = MaterialTranslator.Import(material);
        var graph = GraphJson.Parse(AssetDatabase.LoadAssetAtPath<GraphAsset>(result.GraphPath).source);
        Require(graph.Nodes.Any(n => n.Operation == "core.layeredPbrSurface") &&
            graph.Nodes.Count(n => n.Operation == "core.audioLink") >= 2 &&
            graph.Nodes.Any(n => n.Operation == "core.glitter") &&
            graph.Nodes.Any(n => n.Operation == "core.uv0" && (string)n.Properties["coordinateSource"] == "panosphere"),
            "Real Poiyomi snapshot did not map its active feature branches.");
        Debug.Log("NXSG REAL POIYOMI MATERIAL SNAPSHOT PASSED");
    }

    static void VerifyImport(string family, Shader shader)
    {
        Require(shader != null && shader.name.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0,
            family + " fixture shader is missing.");
        var materialPath = Root + "/" + family + ".mat";
        var source = new Material(shader) { name = family };
        source.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        source.SetTextureScale("_MainTex", new Vector2(1.7f, .8f));
        source.SetColor("_Color", new Color(.3f, .6f, .9f, .8f));
        var fixtureTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (family == "Poiyomi")
        {
            source.SetFloat("_EnableEmission1", 1); source.SetFloat("_EnableEmission2", 1);
            source.SetTexture("_EmissionMap1", fixtureTexture);
            source.SetFloat("_EmissionMap1UV", 4);
            source.SetVector("_EmissionMap1Pan", new Vector4(0, 1, 0, 0));
            source.SetFloat("_EnableAudioLink", 1); source.SetFloat("_EmissionAL1Enabled", 1);
            source.SetFloat("_GlitterEnable", 1); source.SetFloat("_GlitterALEnabled", 1);
            source.SetTexture("_GlitterMask", fixtureTexture);
            source.SetFloat("_ClearCoatBRDF", 1); source.SetFloat("_ClearCoatStrength", .077f); source.SetFloat("_ClearCoatSmoothness", 1);
            source.SetFloat("_AlphaForceOpaque", 1);
        }
        else
        {
            source.SetFloat("_UseEmission", 1); source.SetFloat("_UseEmission2nd", 1);
            source.SetTexture("_EmissionMap", fixtureTexture); source.SetTexture("_Emission2ndMap", fixtureTexture);
            source.SetFloat("_UseGlitter", 1); source.SetFloat("_UseMain2ndTex", 1);
            source.SetTexture("_Main2ndTex", fixtureTexture);
            source.SetFloat("_UseOutline", 1);
        }
        AssetDatabase.CreateAsset(source, materialPath);
        AssetDatabase.SaveAssets();
        var originalGuid = AssetDatabase.AssetPathToGUID(materialPath);
        var textureGuid = AssetDatabase.AssetPathToGUID(TexturePath);

        var result = MaterialTranslator.Import(AssetDatabase.LoadAssetAtPath<Material>(materialPath));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Require(AssetDatabase.AssetPathToGUID(materialPath) == originalGuid, family + " import changed the source material GUID.");
        Require(result.SourceMaterial != null && AssetDatabase.GetAssetPath(result.SourceMaterial) == materialPath,
            family + " import did not retain the original material asset.");
        Require(!string.IsNullOrEmpty(result.BackupPath) && File.Exists(result.BackupPath), family + " material backup is missing.");
        var backup = AssetDatabase.LoadAssetAtPath<Material>(result.BackupPath);
        Require(backup != null && backup.shader == shader && backup.GetTexture("_MainTex") != null,
            family + " material backup did not retain the original shader and texture.");
        Require(!string.IsNullOrEmpty(result.GraphPath) && File.Exists(result.GraphPath), family + " imported graph is missing.");
        Require(!string.IsNullOrEmpty(result.ReportPath) && File.Exists(result.ReportPath), family + " import report is missing.");

        var graphAsset = AssetDatabase.LoadAssetAtPath<GraphAsset>(result.GraphPath);
        Require(graphAsset != null && string.IsNullOrEmpty(graphAsset.parseError), family + " imported graph failed to parse.");
        var graph = GraphJson.Parse(graphAsset.source);
        var surface = graph.Nodes.Single(n => n.Operation == (family == "Poiyomi" ? "core.layeredPbrSurface" : "core.toonSurface"));
        var output = graph.Nodes.Single(n => n.Operation == "core.output");
        Require(graph.Nodes.Count(n => n.Operation == "core.texture2D") >= 3, family + " dropped active texture layers.");
        Require(graph.Resources.Any(r => r.Name != null && r.Name.StartsWith("Emission", StringComparison.Ordinal)), family + " imported texture slots are unnamed.");
        Require(graph.Connections.Any(c => c.To.NodeId == surface.Id && c.To.PortId == "emission"), family + " emission is disconnected.");
        Require(graph.Nodes.Any(n => n.Operation == "core.add"), family + " emission layers were not combined.");
        Require(graph.Nodes.Any(n => n.Operation == "core.glitter"), family + " glitter is missing.");
        if (family == "Poiyomi")
        {
            Require(graph.Nodes.Any(n => n.Operation == "core.audioLink"), "Poiyomi AudioLink was dropped.");
            Require(graph.Nodes.Any(n => n.Operation == "core.uv0" && (string)n.Properties["coordinateSource"] == "panosphere"), "Poiyomi emission Panosphere UV was dropped.");
            Require(graph.Nodes.Any(n => n.Operation == "core.uvScroll" && Math.Abs((double)n.Properties["speed"][1] - 1) < .001), "Poiyomi emission pan was dropped.");
            Require(Math.Abs((double)surface.Properties["coat"] - .077) < .001, "Poiyomi clear coat was dropped.");
            Require((int)output.Properties["renderMode"] == 1, "Poiyomi force opaque was lost.");
        }
        else
        {
            Require(graph.Nodes.Any(n => n.Operation == "core.layer"), "lilToon color layer was dropped.");
            Require(graph.Nodes.Any(n => n.Operation == "core.outline"), "lilToon outline was dropped.");
        }
        Require(graph.Nodes.Any(node => node.Operation == "core.constant" && (string)node.Properties["valueType"] == "color" &&
            Math.Abs((double)node.Properties["value"][0] - .3) < .001), family + " graph lost its source tint.");
        Require(graph.Nodes.Any(node => node.Operation == "core.uvTransform" &&
            Math.Abs((double)node.Properties["tiling"][0] - 1.7) < .001), family + " graph lost its texture tiling.");
        var textureResource = graph.Resources.FirstOrDefault(resource => resource.Kind == "texture2D");
        Require(textureResource != null && textureResource.Uri.StartsWith("project://", StringComparison.Ordinal),
            family + " graph has no project texture resource.");
        var texturePath = textureResource.Uri.Substring("project://".Length);
        Require(AssetDatabase.LoadAssetAtPath<Texture>(texturePath) != null, family + " graph texture cannot be loaded.");
        Require(AssetDatabase.AssetPathToGUID(texturePath) == textureGuid, family + " graph did not preserve the source texture GUID.");
        var textureBindings = graph.Adapter == null ? null : graph.Adapter["textures"] as JObject;
        Require(textureBindings != null && (string)textureBindings[textureResource.Id] == textureGuid,
            family + " graph adapter did not bind the source texture GUID.");
        Require(result.SourceMaterial.shader != shader, family + " source material was not converted in place.");
        Require(result.SourceMaterial.GetTexture("_MainTex") == AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath),
            family + " source material lost its albedo texture.");
        Require(result.SourceMaterial.GetColor("_Color") == Color.white,
            family + " source material kept its old tint and would apply it twice.");
    }
}
