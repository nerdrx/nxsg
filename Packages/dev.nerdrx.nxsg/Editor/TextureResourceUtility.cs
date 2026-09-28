using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NXSG.Backend;
using NXSG.Core;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    internal static class TextureResourceUtility
    {
        internal static void Assign(ShaderGraph graph, IEnumerable<NXSG.Backend.MaterialProperty> properties, Material material, string action)
        {
            foreach (var property in properties.Where(p => p.ResourceId != null))
            {
                var texture = Resolve(graph, property, action);
                material.SetTexture(property.Name, texture);
                if (property.Type == GraphValueType.Texture2DArray)
                    material.SetFloat(property.Name + "_Layers", ((Texture2DArray)texture).depth);
            }
        }

        internal static void SyncArrayLayerCounts(Material material)
        {
            if (material == null || material.shader == null) return;
            for (var i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                var layerProperty = material.shader.GetPropertyName(i);
                const string suffix = "_Layers";
                if (!layerProperty.StartsWith("_NXSG_Array_", StringComparison.Ordinal) || !layerProperty.EndsWith(suffix, StringComparison.Ordinal) || material.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Float) continue;
                var array = material.GetTexture(layerProperty.Substring(0, layerProperty.Length - suffix.Length)) as Texture2DArray;
                if (array == null || material.GetFloat(layerProperty) == array.depth) continue;
                Undo.RecordObject(material, "Update NXSG Texture Array Layers");
                material.SetFloat(layerProperty, array.depth);
                EditorUtility.SetDirty(material);
            }
        }

        static Texture Resolve(ShaderGraph graph, NXSG.Backend.MaterialProperty property, string action)
        {
            var resource = graph?.Resources?.FirstOrDefault(r => r != null && r.Id == property.ResourceId);
            if (resource == null)
                throw new InvalidOperationException(action + " texture resource '" + property.ResourceId + "' is missing from the graph.");

            var expectedKind = Kind(property.Type);
            if (expectedKind == null || resource.Kind != expectedKind)
                throw new InvalidOperationException("Texture resource '" + resource.Id + "' has kind '" + resource.Kind + "' but shader property '" + property.Name + "' requires '" + (expectedKind ?? property.Type.ToString()) + "'. Reassign a matching asset or fix the graph resource kind.");

            var bindings = graph.Adapter?["textures"] as JObject;
            var token = bindings?[resource.Id];
            var guid = token?.Type == JTokenType.String ? (string)token : null;
            if (string.IsNullOrEmpty(guid))
            {
                if (property.Type == GraphValueType.Texture2D && resource.Uri == "builtin://white")
                    return Texture2D.whiteTexture;
                throw new InvalidOperationException("Assign a Unity " + TypeName(property.Type) + " asset for texture resource '" + resource.Id + "' before " + action.ToLowerInvariant() + "ing.");
            }

            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            var texture = asset as Texture;
            if (texture == null)
                throw new InvalidOperationException("Texture asset for resource '" + resource.Id + "' is missing or is not a texture. Reassign a " + TypeName(property.Type) + " asset before " + action.ToLowerInvariant() + "ing.");
            if (!Matches(property.Type, texture))
                throw new InvalidOperationException("Texture resource '" + resource.Id + "' requires a " + TypeName(property.Type) + " asset, but the assigned asset is " + TypeName(texture) + ". Reassign a matching asset before " + action.ToLowerInvariant() + "ing.");
            return texture;
        }

        static string Kind(GraphValueType type)
        {
            switch (type)
            {
                case GraphValueType.Texture2D: return "texture2D";
                case GraphValueType.Cubemap: return "cubemap";
                case GraphValueType.Texture2DArray: return "texture2DArray";
                default: return null;
            }
        }

        static bool Matches(GraphValueType type, Texture texture)
        {
            switch (type)
            {
                case GraphValueType.Texture2D: return texture is Texture2D;
                case GraphValueType.Cubemap: return texture is Cubemap;
                case GraphValueType.Texture2DArray: return texture is Texture2DArray;
                default: return false;
            }
        }

        static string TypeName(GraphValueType type)
        {
            switch (type)
            {
                case GraphValueType.Texture2D: return "Texture2D";
                case GraphValueType.Cubemap: return "Cubemap";
                case GraphValueType.Texture2DArray: return "Texture2DArray";
                default: return "texture";
            }
        }

        static string TypeName(Texture texture)
        {
            if (texture is Texture2D) return "Texture2D";
            if (texture is Cubemap) return "Cubemap";
            if (texture is Texture2DArray) return "Texture2DArray";
            return texture.GetType().Name;
        }
    }
}
