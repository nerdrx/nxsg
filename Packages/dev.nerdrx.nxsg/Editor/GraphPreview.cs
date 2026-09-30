using System;
using System.Collections.Generic;
using System.Linq;
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

        private GraphPreview(Shader shader, Material material, IReadOnlyList<NXSG.Backend.MaterialProperty> properties)
        {
            this.shader = shader;
            Material = material;
            Properties = properties;
        }

        public Material Material { get; private set; }
        public IReadOnlyList<NXSG.Backend.MaterialProperty> Properties { get; private set; }

        public static GraphPreview Create(ShaderGraph graph, Material context)
        {
            var emitted = ShaderEmitter.Emit(graph, OptionalIntegrations.Options("NXSG/Preview/" + Guid.NewGuid().ToString("N")));
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
                return new GraphPreview(createdShader, createdMaterial, emitted.Properties);
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
            Properties = null;
            shader = null;
        }

        private static void CheckCompilation(Material material, Shader shader)
        {
            var subshader = ShaderUtil.GetShaderData(shader).ActiveSubshader;
            for (var pass = 0; pass < material.passCount; pass++)
            {
                // Framebuffer capture has no programmable pass to bind.
                if (subshader?.GetPass(pass)?.IsGrabPass == true) continue;
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
            TextureResourceUtility.Assign(graph, emitted.Properties, material, "Preview");
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value != null) UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
