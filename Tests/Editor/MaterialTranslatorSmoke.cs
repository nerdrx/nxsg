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
    Properties { _MainTex (""Main"", 2D) = ""white"" {} _Color (""Color"", Color) = (1,1,1,1) }
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

    static void VerifyImport(string family, Shader shader)
    {
        Require(shader != null && shader.name.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0,
            family + " fixture shader is missing.");
        var materialPath = Root + "/" + family + ".mat";
        var source = new Material(shader) { name = family };
        source.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        source.SetTextureScale("_MainTex", new Vector2(1.7f, .8f));
        source.SetColor("_Color", new Color(.3f, .6f, .9f, .8f));
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
