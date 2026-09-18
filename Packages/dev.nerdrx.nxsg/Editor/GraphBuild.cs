using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NXSG.Editor
{
    public static class GraphBuild
    {
        // Development failure injection; no production behavior depends on it.
        public static Action<string> Checkpoint;

        public static Material Build(ShaderGraph graph, string sourcePath)
        {
            var absolute = Path.GetFullPath(sourcePath);
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(assets, StringComparison.Ordinal))
                throw new InvalidOperationException("Save the graph inside this project's Assets folder before building.");
            var relative = "Assets/" + absolute.Substring(assets.Length).Replace('\\', '/');
            var guid = AssetDatabase.AssetPathToGUID(relative);
            if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Graph source is not imported yet. Save it, then retry.");
            var directory = "Assets/NXSGGenerated/" + guid;
            var shaderPath = directory + "/Material.shader";
            var materialPath = directory + "/Material.mat";
            var staging = directory + "/Staged.shader";
            var journalPath = "Library/NXSG/" + guid + ".json";
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory("Library/NXSG");
            Recover(journalPath, shaderPath, materialPath);

            var result = ShaderEmitter.Emit(graph, new EmitterOptions { ShaderName = "NXSG/Generated/" + guid });
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Select(d => d.Path + ": " + d.Message)));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Build validation needs a graphics-enabled Unity Editor. Parsing-only mode cannot validate the shader.");

            var sourceSnapshot = File.ReadAllText(absolute);
            if (GraphJson.ComputeSemanticHash(GraphJson.Parse(sourceSnapshot)) != GraphJson.ComputeSemanticHash(graph))
                throw new IOException("Save the current graph before building. The source and edit snapshot differ.");
            var previous = new Journal
            {
                shader = File.Exists(shaderPath) ? Convert.ToBase64String(File.ReadAllBytes(shaderPath)) : null,
                material = File.Exists(materialPath) ? Convert.ToBase64String(File.ReadAllBytes(materialPath)) : null
            };
            var previousAsync = ShaderUtil.allowAsyncCompilation;
            var preserved = CaptureProperties(AssetDatabase.LoadAssetAtPath<Material>(materialPath));
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var shaderSource = "// NXSG graph hash: " + GraphJson.ComputeSemanticHash(graph) + "\n" + result.ShaderSource;
                File.WriteAllText(staging, shaderSource);
                AssetDatabase.ImportAsset(staging, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                CheckShader(staging);
                Checkpoint?.Invoke("staged");
                if (File.ReadAllText(absolute) != sourceSnapshot)
                    throw new IOException("Graph changed while building. Save and build again.");
                // Persist original bytes before either output changes. Retain the journal until both imports pass.
                File.WriteAllText(journalPath, JsonUtility.ToJson(previous));
                File.WriteAllText(shaderPath, shaderSource);
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Checkpoint?.Invoke("shader-promoted");
                var shader = CheckShader(shaderPath);
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader) { name = Path.GetFileNameWithoutExtension(relative) };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                else material.shader = shader;
                foreach (var property in preserved) property.Apply(material);
                AssignTextures(graph, result, material);
                if (material.HasProperty("_NXSG_AudioLinkPreview")) material.SetFloat("_NXSG_AudioLinkPreview",0);
                foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning)) Debug.LogWarning("NXSG: " + diagnostic.Message);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                Checkpoint?.Invoke("material-promoted");
                AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
                    throw new IOException("Generated material could not be reloaded.");
                File.Delete(journalPath);
                return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            }
            catch
            {
                Recover(journalPath, shaderPath, materialPath);
                throw;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync;
                AssetDatabase.DeleteAsset(staging);
            }
        }

        static void AssignTextures(ShaderGraph graph, EmissionResult result, Material material)
        {
            var bindings = graph.Adapter?["textures"] as JObject;
            foreach (var property in result.Properties)
            {
                if (property.ResourceId == null) continue;
                var guid = (string)bindings?[property.ResourceId];
                if (guid != null)
                {
                    var texture = string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!string.IsNullOrEmpty(guid) && texture == null)
                        throw new InvalidOperationException("Texture is missing for resource '" + property.ResourceId + "'. Reassign it in the graph.");
                    material.SetTexture(property.Name, texture);
                }
                else if (property.ResourceUri == "builtin://white") material.SetTexture(property.Name,Texture2D.whiteTexture);
                else if (property.ResourceUri != "builtin://white")
                    throw new InvalidOperationException("Assign a Unity texture for resource '" + property.ResourceId + "' before building.");
            }
        }

        static List<SavedProperty> CaptureProperties(Material material)
        {
            var properties = new List<SavedProperty>();
            if (material == null || material.shader == null) return properties;
            for (var i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                var name = material.shader.GetPropertyName(i);
                var type = material.shader.GetPropertyType(i);
                var saved = new SavedProperty { name = name, type = type };
                switch (type)
                {
                    case ShaderPropertyType.Color: saved.vector = material.GetColor(name); break;
                    case ShaderPropertyType.Vector: saved.vector = material.GetVector(name); break;
                    case ShaderPropertyType.Texture:
                        saved.texture = material.GetTexture(name); saved.scale = material.GetTextureScale(name); saved.offset = material.GetTextureOffset(name); break;
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range: saved.number = material.GetFloat(name); break;
                    default: continue;
                }
                properties.Add(saved);
            }
            return properties;
        }

        sealed class SavedProperty
        {
            public string name;
            public ShaderPropertyType type;
            public Vector4 vector;
            public float number;
            public Texture texture;
            public Vector2 scale, offset;
            public void Apply(Material material)
            {
                var index = material.shader.FindPropertyIndex(name);
                if (index < 0 || material.shader.GetPropertyType(index) != type) return;
                switch (type)
                {
                    case ShaderPropertyType.Color: material.SetColor(name, vector); break;
                    case ShaderPropertyType.Vector: material.SetVector(name, vector); break;
                    case ShaderPropertyType.Texture: material.SetTexture(name, texture); material.SetTextureScale(name, scale); material.SetTextureOffset(name, offset); break;
                    default: material.SetFloat(name, number); break;
                }
            }
        }

        static Shader CheckShader(string path)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Shader is unsupported by the active graphics renderer: " + path);
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                for (var pass = 0; pass < material.passCount; pass++)
                    if (!material.SetPass(pass)) throw new InvalidOperationException("Shader pass " + pass + " failed on " + SystemInfo.graphicsDeviceType);
                if (ShaderUtil.ShaderHasError(shader))
                    throw new InvalidOperationException(string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            return shader;
        }

        static void Recover(string path, string shaderPath, string materialPath)
        {
            if (!File.Exists(path)) return;
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(path));
            if (journal == null) throw new IOException("Build recovery journal is unreadable: " + path);
            Restore(shaderPath, journal.shader);
            Restore(materialPath, journal.material);
            File.Delete(path);
        }

        [InitializeOnLoadMethod]
        static void RecoverInterruptedBuilds()
        {
            EditorApplication.delayCall += () =>
            {
                if (!Directory.Exists("Library/NXSG")) return;
                foreach (var path in Directory.GetFiles("Library/NXSG", "*.json"))
                {
                    var guid = Path.GetFileNameWithoutExtension(path);
                    if (guid.Length != 32 || guid.Any(c => !Uri.IsHexDigit(c))) continue;
                    try { Recover(path, "Assets/NXSGGenerated/" + guid + "/Material.shader", "Assets/NXSGGenerated/" + guid + "/Material.mat"); }
                    catch (Exception exception) { Debug.LogError("NXSG build recovery needs attention: " + exception.Message); }
                }
            };
        }

        static void Restore(string path, string original)
        {
            if (original == null) { AssetDatabase.DeleteAsset(path); return; }
            File.WriteAllBytes(path, Convert.FromBase64String(original));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        [Serializable]
        sealed class Journal { public string shader; public string material; }
    }
}
