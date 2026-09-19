using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;

// Run in Unity with -executeMethod ShadowContractSmoke.Run.
public static class ShadowContractSmoke
{
    public static void Run()
    {
        try
        {
            var graph = new ShaderGraph { GraphId = "shadow-contract" };
            graph.Resources.Add(new GraphResource { Id = "height", Kind = "texture2D", Uri = "builtin://white" });
            graph.Nodes.Add(new GraphNode { Id = "uv", Operation = "core.uv0" });
            graph.Nodes.Add(new GraphNode { Id = "parallax", Operation = "core.parallaxOcclusion", Properties = new JObject { ["resourceId"] = "height" } });
            graph.Nodes.Add(new GraphNode { Id = "texture", Operation = "core.texture2D", Properties = new JObject { ["resourceId"] = "height" } });
            graph.Nodes.Add(new GraphNode { Id = "toon", Operation = "core.toonSurface" });
            graph.Nodes.Add(new GraphNode { Id = "output", Operation = "core.output" });
            Connect(graph, "uv", "uv", "parallax", "uv", "uv-parallax");
            Connect(graph, "parallax", "uv", "texture", "uv", "parallax-texture");
            Connect(graph, "texture", "color", "toon", "albedo", "texture-toon");
            Connect(graph, "toon", "surface", "output", "surface", "toon-output");

            var result = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = "NXSG/ShadowContract" });
            Require(result.Succeeded, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Require(result.ShaderSource.Contains("float3 tangent:TEXCOORD11"), "Shadow varying lost tangent");
            Require(result.ShaderSource.Contains("float3 bitangent:TEXCOORD12"), "Shadow varying lost bitangent");
            Require(result.ShaderSource.Contains("o.tangent=input.tangent"), "Shadow vertex did not preserve tangent");
            Require(result.ShaderSource.Contains("o.bitangent=input.bitangent"), "Shadow vertex did not preserve bitangent");
            Require(result.ShaderSource.Contains("input.tangent=i.tangent"), "Shadow fragment did not restore tangent");
            Require(result.ShaderSource.Contains("input.bitangent=i.bitangent"), "Shadow fragment did not restore bitangent");
            Require(result.Diagnostics.Any(d => d.Code == "lighting.forwardAdd"), "Missing additional-light limitation diagnostic");
            const string shaderPath = "Assets/ShadowContractSmoke.shader";
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "ShadowContractSmoke.shader"), result.ShaderSource);
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Generated shadow shader failed import");
            var material = new Material(shader);
            try
            {
                for (var pass = 0; pass < material.passCount; pass++)
                {
                    ShaderUtil.CompilePass(material, pass, true);
                    Require(material.SetPass(pass), "Generated shader pass failed compilation: " + pass);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(material); AssetDatabase.DeleteAsset(shaderPath); }
            Debug.Log("NXSG SHADOW CONTRACT CHECK PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void Connect(ShaderGraph graph, string from, string fromPort, string to, string toPort, string id)
    {
        graph.Connections.Add(new GraphConnection
        {
            Id = id,
            From = new GraphPortRef { NodeId = from, PortId = fromPort },
            To = new GraphPortRef { NodeId = to, PortId = toPort }
        });
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
