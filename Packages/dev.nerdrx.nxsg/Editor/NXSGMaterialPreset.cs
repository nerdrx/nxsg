using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NXSG.Editor
{
    [Serializable]
    public sealed class NXSGMaterialPresetValue
    {
        public string property;
        public ShaderPropertyType type;
        public float[] numbers;
        public int integer;
        public string textureGuid;
        public Texture texture;
    }

    public sealed class NXSGMaterialPreset : ScriptableObject
    {
        public string shaderName;
        public List<NXSGMaterialPresetValue> values = new List<NXSGMaterialPresetValue>();
    }

    public static class NXSGMaterialPresetUtility
    {
        public static NXSGMaterialPreset Capture(Material material, string assetPath)
        {
            if (material == null || material.shader == null) throw new ArgumentNullException("material");
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Preset path must be inside the project's Assets folder.", "assetPath");
            var preset = ScriptableObject.CreateInstance<NXSGMaterialPreset>();
            preset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            preset.shaderName = material.shader.name;
            for (var i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                var name = material.shader.GetPropertyName(i);
                if (name.StartsWith("_NXSG_Preview", StringComparison.Ordinal) || name == "_NXSG_AudioLinkPreview") continue;
                var type = material.shader.GetPropertyType(i);
                var value = new NXSGMaterialPresetValue { property = name, type = type };
                switch (type)
                {
                    case ShaderPropertyType.Color:
                        var color = material.GetColor(name); value.numbers = new[] { color.r, color.g, color.b, color.a }; break;
                    case ShaderPropertyType.Vector:
                        var vector = material.GetVector(name); value.numbers = new[] { vector.x, vector.y, vector.z, vector.w }; break;
                    case ShaderPropertyType.Texture:
                        var texture = material.GetTexture(name);
                        value.texture = texture;
                        value.textureGuid = texture == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
                        var scale = material.GetTextureScale(name); var offset = material.GetTextureOffset(name);
                        value.numbers = new[] { scale.x, scale.y, offset.x, offset.y }; break;
                    case ShaderPropertyType.Int: value.integer=material.GetInteger(name);break;
                    default: value.numbers = new[] { material.GetFloat(name) }; break;
                }
                preset.values.Add(value);
            }
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(preset, assetPath);
            AssetDatabase.SaveAssets();
            return preset;
        }

        public static bool IsCompatible(NXSGMaterialPreset preset, Material material, out string reason)
        {
            if (preset == null || material == null || material.shader == null) { reason = "Preset or material is missing."; return false; }
            if (!string.Equals(preset.shaderName, material.shader.name, StringComparison.Ordinal))
            { reason = "Preset belongs to shader '" + preset.shaderName + "'."; return false; }
            foreach(var value in preset.values??new List<NXSGMaterialPresetValue>())
            {
                if(value==null||string.IsNullOrEmpty(value.property))continue;
                var index=material.shader.FindPropertyIndex(value.property);
                if(index>=0&&material.shader.GetPropertyType(index)!=value.type){reason="Property type changed: "+value.property+". Save a new preset for this graph version.";return false;}
            }
            reason = null; return true;
        }

        public static void Apply(NXSGMaterialPreset preset, Material[] materials)
        {
            if (preset == null || materials == null) return;
            var targets = materials.Where(item => item != null).Distinct().ToArray();
            if (targets.Length == 0) return;
            foreach (var material in targets)
            {
                string reason;
                if (!IsCompatible(preset, material, out reason)) throw new InvalidOperationException(reason);
            }
            Undo.RecordObjects(targets, "Apply NXSG material preset");
            foreach (var material in targets)
            {
                foreach (var value in preset.values ?? new List<NXSGMaterialPresetValue>())
                {
                    if (value==null || string.IsNullOrEmpty(value.property) || !material.HasProperty(value.property)) continue;
                    var numbers = value.numbers ?? new float[0];
                    switch (value.type)
                    {
                        case ShaderPropertyType.Color: if (numbers.Length >= 4) material.SetColor(value.property, new Color(numbers[0], numbers[1], numbers[2], numbers[3])); break;
                        case ShaderPropertyType.Vector: if (numbers.Length >= 4) material.SetVector(value.property, new Vector4(numbers[0], numbers[1], numbers[2], numbers[3])); break;
                        case ShaderPropertyType.Texture:
                            material.SetTexture(value.property, value.texture != null ? value.texture : (string.IsNullOrEmpty(value.textureGuid) ? null : AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(value.textureGuid))));
                            if (numbers.Length >= 4) { material.SetTextureScale(value.property, new Vector2(numbers[0], numbers[1])); material.SetTextureOffset(value.property, new Vector2(numbers[2], numbers[3])); }
                            break;
                        case ShaderPropertyType.Int: material.SetInteger(value.property,value.integer);break;
                        default: if (numbers.Length > 0) material.SetFloat(value.property, numbers[0]); break;
                    }
                }
                EditorUtility.SetDirty(material);
            }
        }
    }
}
