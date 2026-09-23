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

        public static string LastBuildSummary { get; private set; }
        public static double LastBuildMilliseconds { get; private set; }

        public static Material Build(ShaderGraph graph, string sourcePath)
        {
            var timer = new BuildTimer();
            try { return BuildCore(graph, sourcePath, timer); }
            finally
            {
                LastBuildMilliseconds = timer.Elapsed;
                LastBuildSummary = timer.Summary;
                Debug.Log("NXSG build: " + LastBuildSummary);
            }
        }

        sealed class BuildTimer
        {
            readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            readonly List<string> stages = new List<string>();
            double last;
            public double Elapsed => watch.Elapsed.TotalMilliseconds;
            public void Mark(string name) { var now = Elapsed; stages.Add(name + " " + (now-last).ToString("F0") + " ms"); last=now; }
            public string Summary => Elapsed.ToString("F0") + " ms total; " + string.Join(", ", stages);
        }

        static Material BuildCore(ShaderGraph graph, string sourcePath, BuildTimer timer)
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

            var journalPath = "Library/NXSG/" + guid + ".json";
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory("Library/NXSG");
            Recover(journalPath, shaderPath, materialPath);

            // Keep the asset paths/GUIDs stable; only the shader menu name becomes readable.
            var label = new string(Path.GetFileNameWithoutExtension(relative).Take(80)
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            if (string.IsNullOrEmpty(label)) label = "Graph";
            var result = ShaderEmitter.Emit(graph, OptionalIntegrations.Options("NXSG/" + label + "/" + guid));
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Select(d => d.Path + ": " + d.Message)));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Build validation needs a graphics-enabled Unity Editor. Parsing-only mode cannot validate the shader.");

            var sourceSnapshot = File.ReadAllText(absolute);
            if (GraphJson.ComputeSemanticHash(GraphJson.Parse(sourceSnapshot)) != GraphJson.ComputeSemanticHash(graph))
                throw new IOException("Save the current graph before building. The source and edit snapshot differ.");
            timer.Mark("generate/validate");
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
                var unchanged = File.Exists(shaderPath) && File.ReadAllText(shaderPath) == shaderSource;
                if (File.ReadAllText(absolute) != sourceSnapshot)
                    throw new IOException("Graph changed while building. Save and build again.");
                // Journal before mutating either stable asset. Import once; restore both on any failure.
                File.WriteAllText(journalPath, JsonUtility.ToJson(previous));
                if (!unchanged) File.WriteAllText(shaderPath, shaderSource);
                // Unity tracks include dependencies. An unchanged shader needs no forced reimport,
                // but still goes through dependency import and pass validation (including after reload).
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
                Checkpoint?.Invoke("shader-promoted");
                var shader = CheckShader(shaderPath);
                timer.Mark(unchanged ? "reuse/validate shader" : "import/compile shader");
                if (File.ReadAllText(absolute) != sourceSnapshot)
                    throw new IOException("Graph changed while building. Save and build again.");
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader) { name = Path.GetFileNameWithoutExtension(relative) };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                else material.shader = shader;
                foreach (var property in preserved) property.Apply(material);
                AssignTextures(graph, result, material);
                if (material.HasProperty("_NXSG_PreviewClock")) material.SetFloat("_NXSG_PreviewClock",0);
                if (material.HasProperty("_NXSG_AudioLinkPreview")) material.SetFloat("_NXSG_AudioLinkPreview",0);
                foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning)) Debug.LogWarning("NXSG: " + diagnostic.Message);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                Checkpoint?.Invoke("material-promoted");
                if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
                    throw new IOException("Generated material could not be reloaded.");
                timer.Mark("material save");
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
                var subshader = ShaderUtil.GetShaderData(shader).ActiveSubshader;
                for (var pass = 0; pass < material.passCount; pass++)
                {
                    // GrabPass executes during camera rendering, not Material.SetPass.
                    if (subshader?.GetPass(pass)?.IsGrabPass == true) continue;
                    ShaderUtil.CompilePass(material, pass, true);
                    if (!material.SetPass(pass)) throw new InvalidOperationException("Shader pass " + pass + " failed on " + SystemInfo.graphicsDeviceType);
                }
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
