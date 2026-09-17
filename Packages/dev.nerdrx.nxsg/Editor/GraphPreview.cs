using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    /// <summary>Temporary, in-memory shader and material for graph previews.</summary>
    public sealed class GraphPreview : IDisposable
    {
        private Shader shader;

        private GraphPreview(Shader shader, Material material)
        {
            this.shader = shader;
            Material = material;
        }

        public Material Material { get; private set; }

        public static GraphPreview Create(ShaderGraph graph, Material context)
        {
            var emitted = ShaderEmitter.Emit(graph, new EmitterOptions
            {
                ShaderName = "NXSG/Preview/" + Guid.NewGuid().ToString("N")
            });
            if (!emitted.Succeeded)
                throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics.Select(d => d.Path + ": " + d.Message)));

            Shader createdShader = null;
            Material createdMaterial = null;
            var previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                createdShader = ShaderUtil.CreateShaderAsset(emitted.ShaderSource, true);
                if (createdShader == null || !createdShader.isSupported)
                    throw new InvalidOperationException("Preview shader is unsupported by the active graphics renderer.");
                createdShader.hideFlags = HideFlags.HideAndDontSave;

                createdMaterial = new Material(createdShader);
                createdMaterial.hideFlags = HideFlags.HideAndDontSave;
                if (context != null)
                    createdMaterial.CopyMatchingPropertiesFromMaterial(context);

                CheckCompilation(createdMaterial, createdShader);
                AssignTextures(graph, emitted, createdMaterial);
                return new GraphPreview(createdShader, createdMaterial);
            }
            catch
            {
                Destroy(createdMaterial);
                Destroy(createdShader);
                throw;
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync;
            }
        }

        public void Dispose()
        {
            Destroy(Material);
            Destroy(shader);
            Material = null;
            shader = null;
        }

        private static void CheckCompilation(Material material, Shader shader)
        {
            for (var pass = 0; pass < material.passCount; pass++)
            {
                ShaderUtil.CompilePass(material, pass, true);
                if (!material.SetPass(pass))
                    throw new InvalidOperationException("Preview shader pass " + pass + " failed compilation.");
            }

            if (!ShaderUtil.ShaderHasError(shader)) return;
            var messages = ShaderUtil.GetShaderMessages(shader);
            throw new InvalidOperationException(string.Join("\n", messages.Select(m => m.message)));
        }

        private static void AssignTextures(ShaderGraph graph, EmissionResult emitted, Material material)
        {
            var bindings = graph.Adapter?["textures"] as JObject;
            foreach (var property in emitted.Properties)
            {
                if (property.ResourceId == null) continue;
                var resource = (graph.Resources ?? new System.Collections.Generic.List<GraphResource>())
                    .FirstOrDefault(r => r != null && r.Id == property.ResourceId);
                var guid = (string)bindings?[property.ResourceId];
                Texture2D texture;
                if (string.IsNullOrEmpty(guid) && resource != null && resource.Uri == "builtin://white")
                    texture = Texture2D.whiteTexture;
                else
                {
                    texture = string.IsNullOrEmpty(guid)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                    if (texture == null)
                        throw new InvalidOperationException("Preview texture is missing for resource '" + property.ResourceId + "'. Assign a Unity texture in the graph.");
                }
                material.SetTexture(property.Name, texture);
            }
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value != null) UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
